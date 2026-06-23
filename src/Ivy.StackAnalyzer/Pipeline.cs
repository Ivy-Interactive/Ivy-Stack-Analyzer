using System.Diagnostics;
using System.Reflection;
using Ivy.StackAnalyzer.Components;
using Ivy.StackAnalyzer.Models;
using Ivy.StackAnalyzer.Detection;
using Ivy.StackAnalyzer.Infra;
using Ivy.StackAnalyzer.Manifests;
using Ivy.StackAnalyzer.Scanning;

namespace Ivy.StackAnalyzer;

/// <summary>
/// Orchestrates the seven-stage analysis pipeline (PLAN.md §5):
/// walk → classify → componentize → parse → detect → infra → assemble.
/// </summary>
internal static class Pipeline
{
    public static Task<StackDetection> RunAsync(
        string repoPath, AnalyzerOptions options, CancellationToken ct)
    {
        return Task.Run(() => Run(repoPath, options, ct), ct);
    }

    private static StackDetection Run(string repoPath, AnalyzerOptions options, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(repoPath))
            throw new ArgumentException("Repository path must be provided.", nameof(repoPath));
        var fullPath = Path.GetFullPath(repoPath);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Repository path not found: {fullPath}");

        var sw = Stopwatch.StartNew();

        // 0. Data
        var data = DataStore.Load(options.AdditionalRuleDirectories);
        var parsers = new ManifestParserRegistry();
        var classifier = new LanguageClassifier(data);
        var ruleEngine = new RuleEngine(data);
        // Built-in code detectors run alongside the data rules and any user-supplied
        // ones (code escape hatch, PLAN.md §7c).
        IReadOnlyList<ITechnologyDetector> detectors =
            [new PrismaDetector(), .. options.AdditionalDetectors];

        // 1. Walk
        var scan = new FileSystemScanner(data, options).Scan(fullPath, ct);

        // 2. Classify
        var classified = scan.Files.Select(classifier.Classify).ToList();

        // 3-4. Componentize + parse
        var contexts = new ComponentDetector(parsers, options).Detect(classified);

        // 5. Detect (per component)
        var components = new List<Component>(contexts.Count);
        foreach (var ctx in contexts)
        {
            ct.ThrowIfCancellationRequested();
            var techs = new List<DetectedTechnology>(ruleEngine.Detect(ctx));
            foreach (var d in detectors) techs.AddRange(d.Detect(ctx));
            techs = Dedupe(techs).ToList();

            components.Add(new Component
            {
                RelativePath = ctx.RelativePath,
                Languages = ctx.Languages,
                Manifests = ctx.Manifests.Select(m => CapForReport(m, options.MaxDependenciesPerManifest)).ToList(),
                Technologies = techs,
                FileCount = ctx.FileCount,
                SizeBytes = ctx.SizeBytes,
                IsWorkspaceRoot = ctx.IsWorkspaceRoot,
                IsAuxiliary = ctx.IsAuxiliary,
            });
        }
        components = components.OrderBy(c => c.RelativePath, StringComparer.Ordinal).ToList();

        // 6. Infra + readme
        var infra = new InfraScanner(data.Infra).Scan(classified);
        var readme = ReadmeReader.Read(classified, options.MaxReadmeLines);

        // 7. Assemble
        var repoLanguages = LanguageAggregator.Aggregate(classified);
        var allTechs = FlattenTechnologies(components);
        var analyzedFiles = classified.Count(f => f.Language is not null && !f.File.IsVendored);

        sw.Stop();

        var summary = new RepoSummary
        {
            TotalFiles = scan.TotalFiles,
            AnalyzedFiles = analyzedFiles,
            PrimaryLanguages = LanguageAggregator.PrimaryLanguages(repoLanguages),
            ComponentCount = components.Count(c => !c.IsWorkspaceRoot),
        };

        var metadata = new AnalyzerMetadata(
            AnalyzerVersion: Version(),
            DurationMs: sw.ElapsedMilliseconds,
            RulesLoaded: data.RulesLoaded,
            LanguageDefsLoaded: data.LanguageDefsLoaded,
            IgnoredDirectories: scan.IgnoredDirectories);

        return new StackDetection
        {
            RepoPath = fullPath,
            Summary = summary,
            Languages = repoLanguages,
            Components = components,
            Technologies = allTechs,
            Infrastructure = infra,
            Readme = readme,
            Metadata = metadata,
        };
    }

    // The dependency cap bounds the size of the *reported* manifest only; detection
    // already ran against the full set (see ComponentDetector).
    private static ManifestFile CapForReport(ParsedManifest m, int cap)
        => m.Dependencies.Count <= cap
            ? m.ToManifestFile()
            : new ManifestFile(m.Path, m.Ecosystem, m.Dependencies.Take(cap).ToList());

    private static IEnumerable<DetectedTechnology> Dedupe(IEnumerable<DetectedTechnology> techs)
        => techs
            .GroupBy(t => (t.Name, t.Category))
            .Select(g => g.OrderByDescending(t => t.Confidence).First());

    /// <summary>Flatten component technologies into a repo-wide deduped list.</summary>
    private static IReadOnlyList<DetectedTechnology> FlattenTechnologies(IReadOnlyList<Component> components)
    {
        var groups = components
            .SelectMany(c => c.Technologies)
            .GroupBy(t => (t.Name, t.Category));

        var result = new List<DetectedTechnology>();
        foreach (var g in groups)
        {
            var best = g.OrderByDescending(t => t.Confidence).First();
            var paths = g.Select(t => t.ComponentPath).Where(p => p is not null).Distinct().ToList();
            // Repo-wide entry: keep a single component path only if unambiguous.
            var componentPath = paths.Count == 1 ? paths[0] : null;
            result.Add(best with { ComponentPath = componentPath });
        }

        return result
            .OrderBy(t => t.Category)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static string Version()
    {
        var info = typeof(Pipeline).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }
        return typeof(Pipeline).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    }
}
