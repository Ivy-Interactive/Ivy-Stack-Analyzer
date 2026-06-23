namespace Ivy.StackAnalyzer.Manifests;

/// <summary>
/// The richer parse result. <see cref="ManifestFile"/> is the public projection;
/// the extra fields (<see cref="Workspaces"/>, <see cref="Sdk"/>) feed component
/// and technology detection.
/// </summary>
public sealed record ParsedManifest
{
    public required string Path { get; init; }
    public required string Ecosystem { get; init; }
    public required IReadOnlyList<Dependency> Dependencies { get; init; }

    /// <summary>Workspace member globs declared here (npm/pnpm/cargo/etc.), if any.</summary>
    public IReadOnlyList<string> Workspaces { get; init; } = [];

    /// <summary>Run-script command strings declared here (e.g. <c>package.json</c> <c>scripts</c>
    /// values like <c>"bun test"</c>). Lets the rule engine detect tools invoked only via a script.</summary>
    public IReadOnlyList<string> Scripts { get; init; } = [];

    /// <summary>MSBuild <c>Sdk</c> attribute for <c>*.csproj</c> / <c>*.fsproj</c>.</summary>
    public string? Sdk { get; init; }

    /// <summary>MSBuild <c>PropertyGroup</c> scalar properties (e.g.
    /// <c>UseWindowsForms=true</c>). Some frameworks have no package marker and are
    /// only knowable from a build property.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public ManifestFile ToManifestFile() => new(Path, Ecosystem, Dependencies);
}

/// <summary>Parses a single dependency manifest. Add an ecosystem = add one of these.</summary>
public interface IManifestParser
{
    /// <summary>Whether this parser handles a file with the given name.</summary>
    bool CanParse(string fileName);

    /// <summary>Parse the manifest. <paramref name="relativePath"/> is repo-relative (forward slashes).</summary>
    ParsedManifest Parse(string relativePath, string content);
}
