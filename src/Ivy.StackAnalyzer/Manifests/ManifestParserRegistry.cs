namespace Ivy.StackAnalyzer.Manifests;

/// <summary>
/// Holds the built-in <see cref="IManifestParser"/>s and resolves the right one
/// for a file name. Add an ecosystem = register one parser here.
/// </summary>
public sealed class ManifestParserRegistry
{
    private readonly IReadOnlyList<IManifestParser> _parsers;

    public ManifestParserRegistry(IEnumerable<IManifestParser>? parsers = null)
        => _parsers = parsers?.ToList() ?? Default();

    public static IReadOnlyList<IManifestParser> Default() =>
    [
        new NpmParser(),
        new NuGetParser(),
        new PaketParser(),
        new PyPiParser(),
        new GoModParser(),
        new CargoParser(),
        new MavenParser(),
        new GradleParser(),
        new ComposerParser(),
        new GemfileParser(),
        new PubspecParser(),
        new MixParser(),
    ];

    /// <summary>The parser for a file, or <c>null</c> if none handles it.</summary>
    public IManifestParser? Resolve(string fileName)
    {
        foreach (var p in _parsers)
            if (p.CanParse(fileName)) return p;
        return null;
    }

    public bool IsManifest(string fileName) => Resolve(fileName) is not null;
}
