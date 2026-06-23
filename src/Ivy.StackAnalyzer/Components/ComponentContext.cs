using Ivy.StackAnalyzer.Manifests;
using Ivy.StackAnalyzer.Scanning;

namespace Ivy.StackAnalyzer.Components;

/// <summary>A dependency together with the ecosystem of the manifest that declared it.</summary>
public sealed record EcosystemDependency(string Ecosystem, Dependency Dependency);

/// <summary>
/// Everything known about one component subtree. This is the unit the rule
/// engine and any <c>ITechnologyDetector</c> operate on.
/// </summary>
public sealed class ComponentContext
{
    public required string RelativePath { get; init; }
    public required IReadOnlyList<ClassifiedFile> Files { get; init; }
    public required IReadOnlyList<ParsedManifest> Manifests { get; init; }
    public required IReadOnlyList<LanguageStat> Languages { get; init; }
    public required bool IsWorkspaceRoot { get; init; }
    public required bool IsAuxiliary { get; init; }

    public long SizeBytes { get; init; }
    public int FileCount { get; init; }

    /// <summary>All dependencies (with ecosystem) across this component's manifests.</summary>
    public required IReadOnlyList<EcosystemDependency> Dependencies { get; init; }

    /// <summary>MSBuild SDK attributes seen in this component's project files.</summary>
    public IReadOnlyList<string> Sdks { get; init; } = [];

    /// <summary>MSBuild build properties merged across this component's project files.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bare file names present anywhere in this component.</summary>
    public required IReadOnlySet<string> FileNames { get; init; }

    /// <summary>Repo-relative paths (forward slashes) of every file in this component.</summary>
    public required IReadOnlySet<string> FilePaths { get; init; }

    /// <summary>Distinct file extensions present (lowercase, with leading dot).</summary>
    public required IReadOnlySet<string> Extensions { get; init; }

    /// <summary>Environment-variable names discovered in <c>.env*</c> files.</summary>
    public IReadOnlySet<string> EnvVarNames { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Run-script command strings across this component's manifests (e.g. <c>package.json</c>
    /// <c>scripts</c> values). Used to detect tools invoked only via a script, such as <c>bun test</c>.</summary>
    public IReadOnlySet<string> Scripts { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
