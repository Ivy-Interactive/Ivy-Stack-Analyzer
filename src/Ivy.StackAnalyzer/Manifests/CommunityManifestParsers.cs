using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;

namespace Ivy.StackAnalyzer.Manifests;

// Lightweight, defensive manifest parsers for ecosystems whose frameworks would
// otherwise be invisible. Each maps a community manifest to (ecosystem, deps); the
// detector rules in data/detectors/*.yml match those dep names. Best-effort by
// design — a malformed file yields an empty dep list, never an exception.

/// <summary>Julia <c>Project.toml</c> / <c>JuliaProject.toml</c>: the <c>[deps]</c>
/// table maps package name → UUID. Names are the dependency identifiers.</summary>
public sealed class JuliaProjectParser : IManifestParser
{
    public bool CanParse(string fileName)
        => string.Equals(fileName, "Project.toml", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileName, "JuliaProject.toml", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
    {
        var deps = new List<Dependency>();
        try
        {
            if (Toml.ToModel(content) is TomlTable m
                && m.TryGetValue("deps", out var d) && d is TomlTable t)
                foreach (var key in t.Keys)
                    deps.Add(new Dependency(key, null, DependencyScope.Runtime));
        }
        catch { /* not a Julia project / malformed */ }
        return new ParsedManifest { Path = relativePath, Ecosystem = "julia", Dependencies = deps };
    }
}

/// <summary>Crystal <c>shard.yml</c>: <c>dependencies:</c> and
/// <c>development_dependencies:</c> are mappings keyed by shard name.</summary>
public sealed class CrystalShardParser : IManifestParser
{
    public bool CanParse(string fileName)
        => string.Equals(fileName, "shard.yml", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileName, "shard.yaml", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
    {
        var deps = new List<Dependency>();
        bool inDeps = false; int sectionIndent = -1;
        foreach (var raw in content.Replace("\t", "  ").Split('\n'))
        {
            var line = raw.TrimEnd();
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            int indent = line.Length - trimmed.Length;

            if (indent == 0)
            {
                inDeps = trimmed.StartsWith("dependencies:", StringComparison.Ordinal)
                      || trimmed.StartsWith("development_dependencies:", StringComparison.Ordinal);
                sectionIndent = -1;
                continue;
            }
            if (!inDeps) continue;
            // The first indented key under the section sets the shard-name indent.
            if (sectionIndent < 0) sectionIndent = indent;
            if (indent != sectionIndent) continue;       // skip nested github:/version: lines
            var name = trimmed.TrimEnd(':').Trim();
            var colon = name.IndexOf(':');
            if (colon >= 0) name = name[..colon].Trim();
            if (name.Length > 0) deps.Add(new Dependency(name, null, DependencyScope.Runtime));
        }
        return new ParsedManifest { Path = relativePath, Ecosystem = "shards", Dependencies = deps };
    }
}

/// <summary>Nim <c>*.nimble</c>: <c>requires "pkg >= 1.0", "other"</c> lines.</summary>
public sealed partial class NimbleParser : IManifestParser
{
    public bool CanParse(string fileName)
        => fileName.EndsWith(".nimble", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
    {
        var deps = new List<Dependency>();
        foreach (Match req in RequiresRegex().Matches(content))
            foreach (Match s in StringRegex().Matches(req.Groups["body"].Value))
            {
                var spec = s.Groups["v"].Value.Trim();
                var name = NameRegex().Match(spec).Groups["n"].Value;
                if (name.Length == 0 || name.Equals("nim", StringComparison.OrdinalIgnoreCase)) continue;
                deps.Add(new Dependency(name, null, DependencyScope.Runtime));
            }
        return new ParsedManifest { Path = relativePath, Ecosystem = "nimble", Dependencies = deps };
    }

    [GeneratedRegex(@"requires\s+(?<body>(""[^""]*""\s*,?\s*)+)")]
    private static partial Regex RequiresRegex();
    [GeneratedRegex(@"""(?<v>[^""]+)""")]
    private static partial Regex StringRegex();
    [GeneratedRegex(@"^(?<n>[A-Za-z0-9_]+)")]
    private static partial Regex NameRegex();
}

/// <summary>Perl <c>cpanfile</c>: <c>requires 'Module::Name', '0.1';</c>.</summary>
public sealed partial class CpanfileParser : IManifestParser
{
    public bool CanParse(string fileName)
        => string.Equals(fileName, "cpanfile", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
    {
        var deps = new List<Dependency>();
        foreach (Match m in DepRegex().Matches(content))
        {
            var name = m.Groups["name"].Value;
            if (name.Equals("perl", StringComparison.OrdinalIgnoreCase)) continue;
            var scope = m.Groups["kw"].Value is "test_requires" or "recommends"
                ? DependencyScope.Dev : DependencyScope.Runtime;
            deps.Add(new Dependency(name, null, scope));
        }
        return new ParsedManifest { Path = relativePath, Ecosystem = "cpan", Dependencies = deps };
    }

    // requires 'Catalyst::Runtime'; / test_requires "Test::More";
    [GeneratedRegex(@"(?<kw>requires|test_requires|recommends|suggests)\s+['""](?<name>[\w:]+)['""]")]
    private static partial Regex DepRegex();
}

/// <summary>Erlang <c>rebar.config</c>: atoms / tuples inside the <c>{deps, [...]}</c>
/// term — <c>{cowboy, "2.9"}</c>, <c>cowboy</c>, <c>{cowboy, {git, ...}}</c>.</summary>
public sealed partial class RebarConfigParser : IManifestParser
{
    public bool CanParse(string fileName)
        => string.Equals(fileName, "rebar.config", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
    {
        var deps = new List<Dependency>();
        var m = DepsBlockRegex().Match(content);
        if (m.Success)
            // Take only the leading atom of each top-level list element so a source
            // tuple like `{cowboy, {git, "...", {branch,"main"}}}` yields just `cowboy`
            // (not the nested `git`/`branch`/`https` atoms).
            foreach (var element in SplitTopLevel(m.Groups["body"].Value))
            {
                var atom = LeadingAtomRegex().Match(element);
                if (atom.Success) deps.Add(new Dependency(atom.Groups["n"].Value, null, DependencyScope.Runtime));
            }
        return new ParsedManifest { Path = relativePath, Ecosystem = "rebar", Dependencies = deps };
    }

    // Split a bracketed Erlang term body on commas at brace/bracket depth 0.
    private static IEnumerable<string> SplitTopLevel(string body)
    {
        int depth = 0, start = 0;
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (c is '{' or '[') depth++;
            else if (c is '}' or ']') depth--;
            else if (c == ',' && depth == 0) { yield return body[start..i]; start = i + 1; }
        }
        if (start < body.Length) yield return body[start..];
    }

    [GeneratedRegex(@"\{\s*deps\s*,\s*\[(?<body>.*?)\]\s*\}", RegexOptions.Singleline)]
    private static partial Regex DepsBlockRegex();
    // the leading atom of a list element: `cowboy` or `{cowboy, ...`
    [GeneratedRegex(@"^\s*\{?\s*(?<n>[a-z][a-zA-Z0-9_]*)")]
    private static partial Regex LeadingAtomRegex();
}

/// <summary>OCaml <c>*.opam</c> (<c>depends: [ "dream" {…} "lwt" ]</c>) and
/// <c>dune-project</c> (<c>(depends dream lwt)</c>).</summary>
public sealed partial class OpamParser : IManifestParser
{
    public bool CanParse(string fileName)
        => fileName.EndsWith(".opam", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileName, "opam", StringComparison.OrdinalIgnoreCase)        // opam 1.2 bare file
        || string.Equals(fileName, "dune-project", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
    {
        var deps = new List<Dependency>();
        var isDune = relativePath.EndsWith("dune-project", StringComparison.OrdinalIgnoreCase);
        var block = isDune ? DuneDependsRegex().Match(content) : OpamDependsRegex().Match(content);
        if (block.Success)
        {
            var body = block.Groups["body"].Value;
            // Opam constraints live in `{…}` after a package — `"dream" {>= "1.0"}`.
            // Strip them so the version literals inside aren't read as package names.
            if (!isDune) body = ConstraintRegex().Replace(body, "");
            var rx = isDune ? DuneNameRegex() : OpamNameRegex();
            foreach (Match d in rx.Matches(body))
            {
                var name = d.Groups["n"].Value;
                if (name.Length == 0 || name.Equals("ocaml", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("dune", StringComparison.OrdinalIgnoreCase)) continue;
                deps.Add(new Dependency(name, null, DependencyScope.Runtime));
            }
        }
        return new ParsedManifest { Path = relativePath, Ecosystem = "opam", Dependencies = deps };
    }

    [GeneratedRegex(@"depends:\s*\[(?<body>.*?)\]", RegexOptions.Singleline)]
    private static partial Regex OpamDependsRegex();
    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex ConstraintRegex();
    [GeneratedRegex(@"""(?<n>[\w.\-]+)""")]
    private static partial Regex OpamNameRegex();
    [GeneratedRegex(@"\(depends\s+(?<body>[^)]*)\)", RegexOptions.Singleline)]
    private static partial Regex DuneDependsRegex();
    [GeneratedRegex(@"(?<n>[A-Za-z][\w.\-]*)")]
    private static partial Regex DuneNameRegex();
}

/// <summary>Swift Package Manager <c>Package.swift</c>: each
/// <c>.package(url: "https://github.com/vapor/vapor.git", …)</c> contributes the
/// repository's last path segment (e.g. <c>vapor</c>) as the dependency name.</summary>
public sealed partial class SwiftPackageParser : IManifestParser
{
    public bool CanParse(string fileName)
        => string.Equals(fileName, "Package.swift", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
    {
        var deps = new List<Dependency>();
        foreach (Match m in PackageUrlRegex().Matches(content))
        {
            var url = m.Groups["url"].Value.TrimEnd('/');
            var seg = url[(url.LastIndexOf('/') + 1)..];
            if (seg.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) seg = seg[..^4];
            if (seg.Length > 0) deps.Add(new Dependency(seg, null, DependencyScope.Runtime));
        }
        return new ParsedManifest { Path = relativePath, Ecosystem = "swiftpm", Dependencies = deps };
    }

    // Tolerate legacy `.Package(url:` (capital P, old SPM) alongside modern `.package(`.
    [GeneratedRegex(@"\.[Pp]ackage\(\s*(?:name:\s*""[^""]*""\s*,\s*)?url:\s*""(?<url>[^""]+)""")]
    private static partial Regex PackageUrlRegex();
}

/// <summary>Zig <c>build.zig.zon</c>: dependency names are the keys of the
/// <c>.dependencies = .{ .raylib = .{ … } }</c> struct.</summary>
public sealed partial class ZigZonParser : IManifestParser
{
    public bool CanParse(string fileName)
        => string.Equals(fileName, "build.zig.zon", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
    {
        var deps = new List<Dependency>();
        var body = ExtractDependenciesBlock(content);
        // Only dep entries use `.name = .{ … }`; url/hash/path fields are strings, so
        // a `= .{` match inside the block is always a dependency name (any nesting depth).
        if (body is not null)
            foreach (Match m in DepNameRegex().Matches(body))
                deps.Add(new Dependency(m.Groups["n"].Value, null, DependencyScope.Runtime));
        return new ParsedManifest { Path = relativePath, Ecosystem = "zig", Dependencies = deps };
    }

    // Brace-match the `.dependencies = .{ … }` struct so multi-dependency files are
    // captured whole (a non-greedy regex would stop at the first nested `}`).
    private static string? ExtractDependenciesBlock(string content)
    {
        var m = DepsOpenRegex().Match(content);
        if (!m.Success) return null;
        int start = m.Index + m.Length;        // just past the opening `.{`
        int depth = 1;
        for (int i = start; i < content.Length; i++)
        {
            if (content[i] == '{') depth++;
            else if (content[i] == '}' && --depth == 0) return content[start..i];
        }
        return content[start..];
    }

    [GeneratedRegex(@"\.dependencies\s*=\s*\.\{")]
    private static partial Regex DepsOpenRegex();
    [GeneratedRegex(@"\.(?<n>[A-Za-z_][\w]*)\s*=\s*\.\{")]
    private static partial Regex DepNameRegex();
}

/// <summary>Haskell <c>*.cabal</c> (<c>build-depends:</c>) and Stack
/// <c>package.yaml</c> (<c>dependencies:</c>).</summary>
public sealed partial class CabalParser : IManifestParser
{
    public bool CanParse(string fileName)
        => fileName.EndsWith(".cabal", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileName, "package.yaml", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (relativePath.EndsWith("package.yaml", StringComparison.OrdinalIgnoreCase))
            ParseYamlDependencies(content, names);
        else
            ParseCabalBuildDepends(content, names);
        var deps = names.Select(n => new Dependency(n, null, DependencyScope.Runtime)).ToList();
        return new ParsedManifest { Path = relativePath, Ecosystem = "hackage", Dependencies = deps };
    }

    // *.cabal build-depends. Collect the keyword line plus its continuation lines
    // (indented strictly more than the keyword), stopping at the next sibling field
    // or stanza header — so following fields like `hs-source-dirs:` / `ghc-options:`
    // are not absorbed as dependencies. A file may have several stanzas, each with
    // its own build-depends.
    private static void ParseCabalBuildDepends(string content, HashSet<string> names)
    {
        bool inDeps = false;
        int keyIndent = -1;
        foreach (var raw in content.Split('\n'))
        {
            var line = CommentRegex().Replace(raw, "").TrimEnd(); // drop `-- …` comments
            if (inDeps)
            {
                int indent = line.Length - line.TrimStart(' ', '\t').Length;
                if (line.Trim().Length == 0 || indent <= keyIndent)
                    inDeps = false; // blank line / sibling field / new stanza ends the block
                else { AddCabalEntries(line, names); continue; }
            }
            if (!inDeps)
            {
                var m = BuildDependsLineRegex().Match(line);
                if (m.Success)
                {
                    inDeps = true;
                    keyIndent = m.Groups["ws"].Value.Length;
                    AddCabalEntries(m.Groups["rest"].Value, names);
                }
            }
        }
    }

    // Each comma-separated entry contributes its leading package-name token.
    private static void AddCabalEntries(string chunk, HashSet<string> names)
    {
        foreach (var entry in chunk.Split(','))
        {
            var m = CabalNameRegex().Match(entry);
            if (m.Success) Add(names, m.Groups["n"].Value);
        }
    }

    // Stack package.yaml: a `dependencies:` key followed by `- pkg >= 1` list items at
    // any indent (including column 0). Collect items until the next top-level key.
    private static void ParseYamlDependencies(string content, HashSet<string> names)
    {
        bool inDeps = false;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.TrimEnd();
            var trimmed = line.TrimStart();
            if (!inDeps)
            {
                if (trimmed.StartsWith("dependencies:", StringComparison.Ordinal)) inDeps = true;
                continue;
            }
            if (trimmed.StartsWith("- "))
                Add(names, CabalNameRegex().Match(trimmed[2..]).Groups["n"].Value);
            else if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
                inDeps = false;          // a new top-level key ends the block
        }
    }

    private static void Add(HashSet<string> set, string name)
    {
        name = name.Trim();
        if (name.Length > 0 && !name.Equals("base", StringComparison.OrdinalIgnoreCase)) set.Add(name);
    }

    [GeneratedRegex(@"--[^\n]*")]
    private static partial Regex CommentRegex();
    [GeneratedRegex(@"^(?<ws>[ \t]*)build-depends[ \t]*:(?<rest>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex BuildDependsLineRegex();
    // A package name at the start of an entry (after optional leading whitespace);
    // anchored so a version constraint like `>= 4` can't contribute a token.
    [GeneratedRegex(@"^\s*(?<n>[A-Za-z][\w\-]*)")]
    private static partial Regex CabalNameRegex();
}
