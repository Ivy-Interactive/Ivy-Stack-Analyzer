using Ivy.StackAnalyzer.Detection;

namespace Ivy.StackAnalyzer;

/// <summary>
/// Configuration for an <see cref="Analyzer"/> run. All options have sensible
/// defaults; the analyzer is fully usable with <c>new AnalyzerOptions()</c>.
/// </summary>
public sealed record AnalyzerOptions
{
    /// <summary>Code escape-hatch detectors run alongside the data-driven rule engine (PLAN.md §7c).</summary>
    public IReadOnlyList<ITechnologyDetector> AdditionalDetectors { get; init; } = [];

    /// <summary>Include files under vendored / generated directories in the report.</summary>
    public bool IncludeVendored { get; init; } = false;

    /// <summary>Maximum number of README lines captured in the excerpt.</summary>
    public int MaxReadmeLines { get; init; } = 120;

    /// <summary>Maximum number of dependencies kept per parsed manifest in the
    /// <em>reported</em> output. Detection always runs against the full set, so this
    /// only bounds report size — it never hides a technology from the rule engine.</summary>
    public int MaxDependenciesPerManifest { get; init; } = 100;

    /// <summary>Extra directories scanned for user-supplied language / detector data files.</summary>
    public IReadOnlyList<string> AdditionalRuleDirectories { get; init; } = [];

    /// <summary>Honor <c>.gitignore</c> rules when walking the repository.</summary>
    public bool RespectGitignore { get; init; } = true;
}
