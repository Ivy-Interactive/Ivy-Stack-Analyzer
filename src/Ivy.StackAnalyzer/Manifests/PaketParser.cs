namespace Ivy.StackAnalyzer.Manifests;

/// <summary>
/// Parses Paket manifests (<c>paket.dependencies</c> / <c>paket.references</c>),
/// the dependency manager many F#/.NET repos use instead of in-project
/// <c>PackageReference</c>s. Emits NuGet-ecosystem dependencies so the existing
/// .NET detectors fire. Deps under a <c>group</c> whose name contains "test" or
/// "build" are marked <see cref="DependencyScope.Dev"/>.
/// </summary>
public sealed class PaketParser : IManifestParser
{
    public bool CanParse(string fileName)
        => string.Equals(fileName, "paket.dependencies", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileName, "paket.references", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
    {
        var fileName = relativePath[(relativePath.LastIndexOf('/') + 1)..];
        bool isDependencies = fileName.Equals("paket.dependencies", StringComparison.OrdinalIgnoreCase);
        var deps = new List<Dependency>();
        var scope = DependencyScope.Runtime;

        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//") || line.StartsWith('#')) continue;

            // `group Test` switches the active scope for subsequent lines.
            if (line.StartsWith("group ", StringComparison.OrdinalIgnoreCase))
            {
                var groupName = line[6..].Trim();
                scope = IsDevGroup(groupName) ? DependencyScope.Dev : DependencyScope.Runtime;
                continue;
            }

            string? name = isDependencies ? PackageFromDependencyLine(line) : PackageFromReferenceLine(line);
            if (!string.IsNullOrEmpty(name)) deps.Add(new Dependency(name!, null, scope));
        }

        return new ParsedManifest { Path = relativePath, Ecosystem = "nuget", Dependencies = deps };
    }

    private static bool IsDevGroup(string name)
        => name.Contains("test", StringComparison.OrdinalIgnoreCase)
        || name.Contains("build", StringComparison.OrdinalIgnoreCase);

    // paket.dependencies: `nuget FSharp.Core ~> 6.0`. Only nuget sources are NuGet deps.
    private static string? PackageFromDependencyLine(string line)
    {
        if (!line.StartsWith("nuget ", StringComparison.OrdinalIgnoreCase)) return null;
        var rest = line[6..].Trim();
        var sp = rest.IndexOfAny([' ', '\t']);
        return sp < 0 ? rest : rest[..sp];
    }

    // paket.references: a bare package name per line (settings may follow). Skip
    // file references and framework directives.
    private static string? PackageFromReferenceLine(string line)
    {
        if (line.StartsWith("File:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("framework:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("redirects:", StringComparison.OrdinalIgnoreCase)) return null;
        var sp = line.IndexOfAny([' ', '\t']);
        return sp < 0 ? line : line[..sp];
    }
}
