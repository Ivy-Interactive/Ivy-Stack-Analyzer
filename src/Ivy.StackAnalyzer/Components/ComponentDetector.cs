using Ivy.StackAnalyzer.Manifests;
using Ivy.StackAnalyzer.Scanning;

namespace Ivy.StackAnalyzer.Components;

/// <summary>
/// Builds the component map (PLAN.md §6): manifest-bearing directories become
/// component roots, every file is attributed to its nearest-ancestor root, and
/// monorepo aggregators are flagged. Pure structure — no semantic labelling.
/// </summary>
public sealed class ComponentDetector
{
    private readonly ManifestParserRegistry _parsers;
    private readonly AnalyzerOptions _options;

    private static readonly string[] AuxSegments =
        ["test", "tests", "example", "examples", "fixture", "fixtures",
         "sample", "samples", "demo", "demos", "e2e", "docs"];

    private static readonly HashSet<string> WorkspaceDeclarators = new(StringComparer.OrdinalIgnoreCase)
    {
        "pnpm-workspace.yaml", "go.work", "settings.gradle", "settings.gradle.kts",
        "turbo.json", "nx.json", "lerna.json",
    };

    public ComponentDetector(ManifestParserRegistry parsers, AnalyzerOptions options)
    {
        _parsers = parsers;
        _options = options;
    }

    public IReadOnlyList<ComponentContext> Detect(IReadOnlyList<ClassifiedFile> files)
    {
        var analyzable = files.Where(f => _options.IncludeVendored || !f.File.IsVendored).ToList();

        // 1. Component root directories.
        var rootDirs = new HashSet<string> { "" }; // repo root always exists
        var workspaceRootDirs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var f in analyzable)
        {
            var name = f.File.FileName;
            var dir = DirOf(f.File.RelativePath);
            if (_parsers.IsManifest(name)) rootDirs.Add(dir);
            if (IsWorkspaceDeclarator(name)) { rootDirs.Add(dir); workspaceRootDirs.Add(dir); }
        }

        var sortedRoots = rootDirs.OrderByDescending(d => d.Length).ToList();

        // 2. Attribute every file to nearest-ancestor root.
        var filesByRoot = sortedRoots.ToDictionary(r => r, _ => new List<ClassifiedFile>(), StringComparer.Ordinal);
        foreach (var f in analyzable)
        {
            var root = NearestRoot(f.File.RelativePath, sortedRoots);
            filesByRoot[root].Add(f);
        }

        // 3. Build a component per root.
        var components = new List<ComponentContext>();
        foreach (var root in rootDirs.OrderBy(r => r, StringComparer.Ordinal))
        {
            var compFiles = filesByRoot[root];

            // Manifests located directly in this root directory.
            var manifests = new List<ParsedManifest>();
            foreach (var f in compFiles)
            {
                if (DirOf(f.File.RelativePath) != root) continue;
                var parser = _parsers.Resolve(f.File.FileName);
                if (parser is null) continue;
                var parsed = ParseSafely(parser, f.File);
                if (parsed is not null) manifests.Add(parsed);
            }

            // A repo-root component with no files and no manifests is still emitted
            // (it is the guaranteed fallback), but skip empty non-root roots.
            if (root != "" && compFiles.Count == 0 && manifests.Count == 0) continue;

            var deps = new List<EcosystemDependency>();
            foreach (var m in manifests)
            {
                var kept = m.Dependencies.Take(_options.MaxDependenciesPerManifest);
                foreach (var d in kept) deps.Add(new EcosystemDependency(m.Ecosystem, d));
            }

            var sdks = manifests.Where(m => !string.IsNullOrEmpty(m.Sdk)).Select(m => m.Sdk!).Distinct().ToList();

            bool isWorkspaceRoot = workspaceRootDirs.Contains(root)
                || manifests.Any(m => m.Workspaces.Count > 0);

            var languages = LanguageAggregator.Aggregate(compFiles);
            var fileNames = compFiles.Select(f => f.File.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var filePaths = compFiles.Select(f => f.File.RelativePath).ToHashSet(StringComparer.Ordinal);
            var extensions = compFiles.Select(f => f.File.Extension.ToLowerInvariant())
                .Where(e => e.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

            components.Add(new ComponentContext
            {
                RelativePath = root == "" ? "." : root,
                Files = compFiles,
                Manifests = manifests,
                Languages = languages,
                Dependencies = deps,
                Sdks = sdks,
                IsWorkspaceRoot = isWorkspaceRoot,
                IsAuxiliary = IsAuxiliary(root),
                SizeBytes = compFiles.Sum(f => f.File.Length),
                FileCount = compFiles.Count,
                FileNames = fileNames,
                FilePaths = filePaths,
                Extensions = extensions,
                EnvVarNames = ReadEnvVarNames(compFiles),
            });
        }

        return components;
    }

    // A pathologically large "manifest" must not be slurped into memory.
    private const long MaxManifestBytes = 16 * 1024 * 1024;

    private static ParsedManifest? ParseSafely(IManifestParser parser, ScannedFile file)
    {
        if (file.Length > MaxManifestBytes) return null;
        try
        {
            var content = File.ReadAllText(file.FullPath);
            return parser.Parse(file.RelativePath, content);
        }
        // Defense-in-depth: no single malformed/locked manifest can abort the run.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException
            or InvalidOperationException or System.Xml.XmlException) { return null; }
    }

    private static IReadOnlySet<string> ReadEnvVarNames(List<ClassifiedFile> files)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            if (f.File.FileName.StartsWith(".env", StringComparison.OrdinalIgnoreCase))
                ReadDotenvKeys(f.File.FullPath, names);
            else if (IsComposeFile(f.File.FileName))
                ReadComposeEnvKeys(f.File.FullPath, names);
        }
        return names;
    }

    private static void ReadDotenvKeys(string fullPath, HashSet<string> names)
    {
        try
        {
            foreach (var raw in File.ReadLines(fullPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line[..eq].Trim();
                if (key.StartsWith("export ")) key = key["export ".Length..].Trim();
                if (key.Length > 0) names.Add(key);
            }
        }
        catch (IOException) { }
    }

    // Many hosted services (e.g. Supabase, Slack webhooks) are wired purely through
    // docker-compose `environment:` keys with no SDK dependency. Harvesting those key
    // names feeds the same `dotenv` match source as .env files, so the existing
    // dotenv-prefixed detectors fire for compose-wired services too.
    private static bool IsComposeFile(string fileName)
        => (fileName.StartsWith("docker-compose", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("compose", StringComparison.OrdinalIgnoreCase))
           && (fileName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
               || fileName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));

    private static void ReadComposeEnvKeys(string fullPath, HashSet<string> names)
    {
        string[] lines;
        try { lines = File.ReadAllLines(fullPath); }
        catch (IOException) { return; }

        bool inEnv = false;
        int envIndent = -1; // indentation of the `environment:` key
        foreach (var raw in lines)
        {
            if (raw.TrimStart().StartsWith('#')) continue;
            var trimmed = raw.Trim();
            if (trimmed.Length == 0) continue;
            var indent = CountIndent(raw);

            if (!inEnv)
            {
                if (IsEnvironmentHeader(trimmed)) { inEnv = true; envIndent = indent; }
                continue;
            }

            // A line indented no further than `environment:` ends the block.
            if (indent <= envIndent)
            {
                inEnv = IsEnvironmentHeader(trimmed);
                if (inEnv) envIndent = indent;
                continue;
            }

            // List form:  - KEY=value  | - KEY
            // Map form:    KEY: value
            var entry = trimmed;
            if (entry == "-") continue;
            if (entry.StartsWith("- ")) entry = entry[2..].Trim();

            var sep = entry.IndexOfAny(['=', ':']);
            var key = (sep < 0 ? entry : entry[..sep]).Trim().Trim('"', '\'');
            if (key.Length > 0) names.Add(key);
        }
    }

    private static bool IsEnvironmentHeader(string trimmed)
        => trimmed == "environment:"
        || (trimmed.StartsWith("environment:", StringComparison.Ordinal)
            && trimmed[12..].TrimStart() is "" or "{}" or "[]");

    private static int CountIndent(string line)
    {
        int i = 0;
        while (i < line.Length && line[i] == ' ') i++;
        return i;
    }

    private static string NearestRoot(string relativePath, List<string> sortedRootsDesc)
    {
        foreach (var r in sortedRootsDesc)
        {
            if (r.Length == 0) return ""; // root is last (shortest); matches everything
            if (relativePath == r || relativePath.StartsWith(r + "/", StringComparison.Ordinal))
                return r;
        }
        return "";
    }

    private bool IsWorkspaceDeclarator(string fileName)
        => WorkspaceDeclarators.Contains(fileName)
        || fileName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

    private static bool IsAuxiliary(string dir)
    {
        if (dir.Length == 0) return false;
        foreach (var seg in dir.Split('/'))
            if (Array.Exists(AuxSegments, a => string.Equals(a, seg, StringComparison.OrdinalIgnoreCase)))
                return true;
        return false;
    }

    private static string DirOf(string relativePath)
    {
        var i = relativePath.LastIndexOf('/');
        return i < 0 ? "" : relativePath[..i];
    }
}
