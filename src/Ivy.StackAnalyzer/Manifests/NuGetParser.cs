using System.Xml.Linq;

namespace Ivy.StackAnalyzer.Manifests;

/// <summary>
/// Parses MSBuild project files (<c>*.csproj</c> / <c>*.fsproj</c>) for
/// <c>PackageReference</c>s and the <c>Sdk</c> attribute. Also handles
/// <c>Directory.Packages.props</c> (central package versions).
/// </summary>
public sealed class NuGetParser : IManifestParser
{
    public bool CanParse(string fileName)
        => fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileName, "Directory.Packages.props", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
    {
        var deps = new List<Dependency>();
        string? sdk = null;
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var doc = XDocument.Parse(content);
            var root = doc.Root;
            if (root is not null)
            {
                sdk = (string?)root.Attribute("Sdk");

                // Scalar properties from every <PropertyGroup> (last value wins).
                foreach (var pg in root.Descendants().Where(e => e.Name.LocalName == "PropertyGroup"))
                    foreach (var prop in pg.Elements())
                        if (!prop.HasElements)
                            properties[prop.Name.LocalName] = prop.Value.Trim();

                foreach (var pr in root.Descendants().Where(e =>
                    e.Name.LocalName is "PackageReference" or "PackageVersion" or "GlobalPackageReference"))
                {
                    var name = (string?)pr.Attribute("Include") ?? (string?)pr.Attribute("Update");
                    if (string.IsNullOrEmpty(name)) continue;
                    var version = (string?)pr.Attribute("Version")
                        ?? (string?)pr.Element(pr.Name.Namespace + "Version");
                    var isDev = string.Equals((string?)pr.Attribute("PrivateAssets"), "all", StringComparison.OrdinalIgnoreCase);
                    deps.Add(new Dependency(name!, version, isDev ? DependencyScope.Dev : DependencyScope.Runtime));
                }
            }
        }
        catch (System.Xml.XmlException) { /* malformed -> empty */ }

        return new ParsedManifest
        {
            Path = relativePath,
            Ecosystem = "nuget",
            Dependencies = deps,
            Sdk = sdk,
            Properties = properties,
        };
    }
}
