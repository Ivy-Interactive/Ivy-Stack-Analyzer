using System.Xml.Linq;

namespace Ivy.StackAnalyzer.Manifests;

/// <summary>Parses Maven <c>pom.xml</c> dependencies and modules.</summary>
public sealed class MavenParser : IManifestParser
{
    public bool CanParse(string fileName)
        => string.Equals(fileName, "pom.xml", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
    {
        var deps = new List<Dependency>();
        var modules = new List<string>();
        try
        {
            var doc = XDocument.Parse(content);
            XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            foreach (var dep in doc.Descendants(ns + "dependency"))
            {
                var group = (string?)dep.Element(ns + "groupId");
                var artifact = (string?)dep.Element(ns + "artifactId");
                if (string.IsNullOrEmpty(artifact)) continue;
                var version = (string?)dep.Element(ns + "version");
                var scopeText = (string?)dep.Element(ns + "scope");
                var scope = scopeText switch
                {
                    "test" => DependencyScope.Dev,
                    "provided" => DependencyScope.Peer,
                    "runtime" => DependencyScope.Runtime,
                    _ => DependencyScope.Runtime,
                };
                var name = string.IsNullOrEmpty(group) ? artifact! : $"{group}:{artifact}";
                deps.Add(new Dependency(name, version, scope));
            }

            var modulesEl = doc.Descendants(ns + "modules").FirstOrDefault();
            if (modulesEl is not null)
                foreach (var mod in modulesEl.Elements(ns + "module"))
                    if (!string.IsNullOrWhiteSpace(mod.Value)) modules.Add(mod.Value.Trim());
        }
        catch (System.Xml.XmlException) { }

        return new ParsedManifest
        {
            Path = relativePath,
            Ecosystem = "maven",
            Dependencies = deps,
            Workspaces = modules,
        };
    }
}
