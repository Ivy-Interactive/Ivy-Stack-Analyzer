using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;

namespace Ivy.StackAnalyzer.Manifests;

/// <summary>
/// Parses Python manifests: <c>pyproject.toml</c> (PEP 621, Poetry, PEP 735
/// dependency-groups), <c>Pipfile</c>, <c>setup.py</c> (<c>install_requires</c>),
/// conda <c>environment.yml</c>, and any <c>*requirements*.txt</c>. pip-compile
/// lockfiles are recognised and their transitive closure is marked
/// <see cref="DependencyScope.Transitive"/> so it cannot fabricate a hash slot.
/// </summary>
public sealed partial class PyPiParser : IManifestParser
{
    public bool CanParse(string fileName)
        => string.Equals(fileName, "pyproject.toml", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileName, "Pipfile", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileName, "setup.py", StringComparison.OrdinalIgnoreCase)
        || IsCondaEnv(fileName)
        || IsRequirementsTxt(fileName);

    // dev-requirements.txt, requirements-dev.txt, test-requirements.txt, requirements.txt, …
    private static bool IsRequirementsTxt(string f)
        => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
        && f.Contains("requirements", StringComparison.OrdinalIgnoreCase);

    private static bool IsCondaEnv(string f)
        => string.Equals(f, "environment.yml", StringComparison.OrdinalIgnoreCase)
        || string.Equals(f, "environment.yaml", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
    {
        var fileName = relativePath[(relativePath.LastIndexOf('/') + 1)..];
        var deps = fileName.ToLowerInvariant() switch
        {
            "pyproject.toml" => ParsePyproject(content),
            "pipfile" => ParsePipfile(content),
            "setup.py" => ParseSetupPy(content),
            "environment.yml" or "environment.yaml" => ParseCondaEnv(content),
            _ => ParseRequirements(content),
        };
        return new ParsedManifest { Path = relativePath, Ecosystem = "pypi", Dependencies = deps };
    }

    private static List<Dependency> ParsePyproject(string content)
    {
        var deps = new List<Dependency>();
        TomlTable model;
        try { model = Toml.ToModel(content); }
        catch { return deps; }

        // PEP 621: [project] dependencies = ["foo>=1"], [project.optional-dependencies]
        if (model.TryGetValue("project", out var projObj) && projObj is TomlTable project)
        {
            if (project.TryGetValue("dependencies", out var d) && d is TomlArray arr)
                foreach (var item in arr) AddFromSpecifier(item?.ToString(), DependencyScope.Runtime, deps);
            if (project.TryGetValue("optional-dependencies", out var opt) && opt is TomlTable optTable)
                foreach (var group in optTable.Values)
                    if (group is TomlArray ga)
                        foreach (var item in ga) AddFromSpecifier(item?.ToString(), DependencyScope.Optional, deps);
        }

        // Poetry: [tool.poetry.dependencies] / [tool.poetry.group.*.dependencies]
        if (model.TryGetValue("tool", out var toolObj) && toolObj is TomlTable tool
            && tool.TryGetValue("poetry", out var poetryObj) && poetryObj is TomlTable poetry)
        {
            if (poetry.TryGetValue("dependencies", out var pd) && pd is TomlTable pdt)
                AddFromTable(pdt, DependencyScope.Runtime, deps, skipPython: true);
            if (poetry.TryGetValue("group", out var grp) && grp is TomlTable groups)
                foreach (var g in groups.Values)
                    if (g is TomlTable gt && gt.TryGetValue("dependencies", out var gd) && gd is TomlTable gdt)
                        AddFromTable(gdt, DependencyScope.Dev, deps, skipPython: true);
        }

        // PEP 735: [dependency-groups] dev = ["pytest", {include-group = "test"}]
        // (the modern uv / pip convention). String values are PEP 508 specs;
        // {include-group = ...} tables are references to other groups -> skip.
        if (model.TryGetValue("dependency-groups", out var dg) && dg is TomlTable dgt)
            foreach (var group in dgt.Values)
                if (group is TomlArray dga)
                    foreach (var item in dga)
                        if (item is string s) AddFromSpecifier(s, DependencyScope.Dev, deps);
        return deps;
    }

    private static List<Dependency> ParsePipfile(string content)
    {
        var deps = new List<Dependency>();
        TomlTable model;
        try { model = Toml.ToModel(content); }
        catch { return deps; }
        if (model.TryGetValue("packages", out var p) && p is TomlTable pt)
            AddFromTable(pt, DependencyScope.Runtime, deps, skipPython: false);
        if (model.TryGetValue("dev-packages", out var dp) && dp is TomlTable dpt)
            AddFromTable(dpt, DependencyScope.Dev, deps, skipPython: false);
        return deps;
    }

    // setup.py: extract install_requires=[...] and extras_require={...: [...]}.
    private static List<Dependency> ParseSetupPy(string content)
    {
        var deps = new List<Dependency>();
        foreach (Match block in RequiresBlockRegex().Matches(content))
        {
            var scope = block.Groups["kind"].Value.StartsWith("install", StringComparison.OrdinalIgnoreCase)
                ? DependencyScope.Runtime : DependencyScope.Optional;
            foreach (Match lit in StringLiteralRegex().Matches(block.Groups["body"].Value))
                AddFromSpecifier(lit.Groups["v"].Value, scope, deps);
        }
        return deps;
    }

    // conda environment.yml: top-level `dependencies:` list, plus a nested `- pip:` list.
    private static List<Dependency> ParseCondaEnv(string content)
    {
        var deps = new List<Dependency>();
        bool inDeps = false;
        int depsIndent = -1;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Replace("\t", "    ").TrimEnd();
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            int indent = line.Length - line.TrimStart(' ').Length;

            if (!inDeps)
            {
                if (trimmed is "dependencies:" || trimmed.StartsWith("dependencies:", StringComparison.Ordinal))
                { inDeps = true; depsIndent = indent; }
                continue;
            }

            // A non-list key at or above the `dependencies:` indent ends the block.
            if (indent <= depsIndent && !trimmed.StartsWith('-')) { inDeps = false; continue; }
            if (!trimmed.StartsWith('-')) continue;

            var entry = trimmed[1..].Trim();
            if (entry.Length == 0 || entry.StartsWith("pip:", StringComparison.OrdinalIgnoreCase)) continue;

            // Strip a conda channel prefix (`conda-forge::numpy`) and skip the interpreter pin.
            var sep = entry.IndexOf("::", StringComparison.Ordinal);
            if (sep >= 0) entry = entry[(sep + 2)..];
            entry = entry.Trim().Trim('"', '\'');
            if (entry.Length == 0) continue;
            if (entry.Equals("python", StringComparison.OrdinalIgnoreCase)
                || entry.StartsWith("python=", StringComparison.OrdinalIgnoreCase)
                || entry.StartsWith("python ", StringComparison.OrdinalIgnoreCase)) continue;

            AddFromSpecifier(entry, DependencyScope.Runtime, deps);
        }
        return deps;
    }

    private static List<Dependency> ParseRequirements(string content)
    {
        // pip-compile output lists the full transitive closure as if it were direct.
        // When we recognise that format, only deps requested via `-r`/`-c`/`-e`/`.in`
        // stay Runtime; everything else is marked Transitive so it cannot fabricate a
        // framework/db/orm hash slot.
        bool compiled = LooksCompiled(content);
        var deps = new List<Dependency>();
        Dependency? pending = null;          // the dep whose `# via` block we're reading
        bool pendingDirect = false;

        void Flush()
        {
            if (pending is null) return;
            var scope = !compiled || pendingDirect ? DependencyScope.Runtime : DependencyScope.Transitive;
            deps.Add(pending with { Scope = scope });
            pending = null;
        }

        foreach (var raw in content.Split('\n'))
        {
            var line = raw.TrimEnd();
            var trimmed = line.Trim();
            if (trimmed.Length == 0) { continue; }

            // A `# via` continuation line belongs to the previous dep.
            if (trimmed.StartsWith('#'))
            {
                if (pending is not null && ViaIsDirect(trimmed)) pendingDirect = true;
                continue;
            }
            if (trimmed.StartsWith('-')) continue; // options like -r, --hash on their own line

            Flush();

            // Strip an inline comment introduced by whitespace + '#'.
            var inlineDirect = false;
            var hash = CommentRegex().Match(line);
            if (hash.Success)
            {
                var comment = line[hash.Index..];
                inlineDirect = ViaIsDirect(comment);
                line = line[..hash.Index].Trim();
            }
            var parsed = ParseSpecifier(line);
            if (parsed is null) continue;
            pending = parsed;
            pendingDirect = inlineDirect;
        }
        Flush();
        return deps;
    }

    // Heuristics for a pip-compile / pip-tools generated lockfile.
    private static bool LooksCompiled(string content)
        => content.Contains("# via", StringComparison.Ordinal)
        || content.Contains("pip-compile", StringComparison.OrdinalIgnoreCase)
        || content.Contains("uv pip compile", StringComparison.OrdinalIgnoreCase);

    // A `# via` target that references a requirements input (`-r foo.in`, `-c …`,
    // `-e .`, or a `*.in` file) means the package was directly requested.
    private static bool ViaIsDirect(string comment)
        => comment.Contains("-r ", StringComparison.Ordinal)
        || comment.Contains("-c ", StringComparison.Ordinal)
        || comment.Contains("-e ", StringComparison.Ordinal)
        || comment.Contains(".in", StringComparison.OrdinalIgnoreCase);

    private static void AddFromTable(TomlTable table, DependencyScope scope, List<Dependency> into, bool skipPython)
    {
        foreach (var (key, value) in table)
        {
            if (skipPython && string.Equals(key, "python", StringComparison.OrdinalIgnoreCase)) continue;
            string? version = value switch
            {
                string s => s,
                TomlTable t when t.TryGetValue("version", out var v) => v?.ToString(),
                _ => null,
            };
            into.Add(new Dependency(key, NormalizeVersion(version), scope));
        }
    }

    private static void AddFromSpecifier(string? spec, DependencyScope scope, List<Dependency> into)
    {
        var dep = ParseSpecifier(spec);
        if (dep is not null) into.Add(dep with { Scope = scope });
    }

    private static Dependency? ParseSpecifier(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        // Drop an environment marker (PEP 508): "foo>=1; python_version<'3.9'" -> "foo>=1".
        var semi = spec.IndexOf(';');
        if (semi >= 0) spec = spec[..semi];
        var m = NameRegex().Match(spec.Trim());
        if (!m.Success) return null;
        var name = m.Groups["name"].Value;
        var version = m.Groups["ver"].Success ? m.Groups["ver"].Value.Trim() : null;
        return new Dependency(name, version, DependencyScope.Runtime);
    }

    private static string? NormalizeVersion(string? v)
        => string.IsNullOrWhiteSpace(v) || v == "*" ? null : v;

    [GeneratedRegex(@"^(?<name>[A-Za-z0-9][A-Za-z0-9._-]*)(?:\[[^\]]*\])?\s*(?<ver>[<>=!~^].*)?$")]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"\s#")]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"(?<kind>install_requires|extras_require|tests_require)\s*=\s*[\[{](?<body>[^\]}]*)[\]}]", RegexOptions.Singleline)]
    private static partial Regex RequiresBlockRegex();

    [GeneratedRegex(@"['""](?<v>[^'""]+)['""]")]
    private static partial Regex StringLiteralRegex();
}
