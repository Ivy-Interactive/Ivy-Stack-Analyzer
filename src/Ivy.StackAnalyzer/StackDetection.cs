namespace Ivy.StackAnalyzer;

/// <summary>
/// The full report. Pure facts and structure — there is deliberately no
/// "layer" concept here. The consuming LLM infers frontend / backend / etc.
/// from the <see cref="Components"/> map, languages, and technologies.
/// </summary>
public sealed record StackDetection
{
    public required string RepoPath { get; init; }
    public required RepoSummary Summary { get; init; }

    /// <summary>Repo-wide language footprint, vendored files excluded.</summary>
    public required IReadOnlyList<LanguageStat> Languages { get; init; }

    /// <summary>One entry per manifest-bearing subtree (component root).</summary>
    public required IReadOnlyList<Component> Components { get; init; }

    /// <summary>All detected technologies, flattened across components and deduped.</summary>
    public required IReadOnlyList<DetectedTechnology> Technologies { get; init; }

    public required IReadOnlyList<InfraSignal> Infrastructure { get; init; }

    public ReadmeExcerpt? Readme { get; init; }

    public required AnalyzerMetadata Metadata { get; init; }
}

public sealed record RepoSummary
{
    public required int TotalFiles { get; init; }
    public required int AnalyzedFiles { get; init; }
    public required IReadOnlyList<string> PrimaryLanguages { get; init; }
    public required int ComponentCount { get; init; }
}

/// <summary>A manifest-bearing subtree of the repository.</summary>
public sealed record Component
{
    /// <summary>Path relative to the repo root, using forward slashes. "." for the root component.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Language footprint local to this subtree.</summary>
    public required IReadOnlyList<LanguageStat> Languages { get; init; }

    public required IReadOnlyList<ManifestFile> Manifests { get; init; }

    public required IReadOnlyList<DetectedTechnology> Technologies { get; init; }

    public int FileCount { get; init; }
    public long SizeBytes { get; init; }

    /// <summary>Factual: this directory aggregates other components (workspace / solution root).</summary>
    public bool IsWorkspaceRoot { get; init; }

    /// <summary>Factual: lives under test / example / fixture / sample / demo / e2e / docs.</summary>
    public bool IsAuxiliary { get; init; }
}

public sealed record LanguageStat(string Name, LanguageType Type, int Files, long Bytes, double Percent);

public sealed record ManifestFile(string Path, string Ecosystem, IReadOnlyList<Dependency> Dependencies);

public sealed record Dependency(string Name, string? Version, DependencyScope Scope);

public sealed record DetectedTechnology(
    string Name,
    TechCategory Category,
    string Evidence,
    Confidence Confidence,
    string? ComponentPath);

public sealed record InfraSignal(string Kind, TechCategory Category, IReadOnlyList<string> Files, string? Evidence);

public sealed record ReadmeExcerpt(string Path, string Excerpt);

public sealed record AnalyzerMetadata(
    string AnalyzerVersion,
    long DurationMs,
    int RulesLoaded,
    int LanguageDefsLoaded,
    IReadOnlyList<string> IgnoredDirectories);

public enum LanguageType { Programming, Markup, Data, Prose }

/// <summary>
/// Technology category. A superset of specfy's <c>type</c> taxonomy so that
/// seeded rules keep their original signal instead of being bucketed away.
/// </summary>
public enum TechCategory
{
    Language,
    Framework,
    Library,
    Runtime,
    Database,
    Orm,
    Styling,
    Build,
    Ci,
    Cloud,
    Messaging,
    Testing,
    PackageManager,
    Ai,
    Hosting,
    Analytics,
    Auth,
    Cms,
    Monitoring,
    Security,
    Storage,
    Payment,
    Queue,
    Saas,
    Tool,
    Iac,
    Documentation,
}

public enum DependencyScope { Runtime, Dev, Peer, Optional, Transitive }

public enum Confidence { Low, Medium, High }
