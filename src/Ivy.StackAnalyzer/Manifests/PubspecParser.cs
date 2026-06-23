using YamlDotNet.Serialization;

namespace Ivy.StackAnalyzer.Manifests;

/// <summary>Parses Dart / Flutter <c>pubspec.yaml</c>.</summary>
public sealed class PubspecParser : IManifestParser
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder().Build();

    public bool CanParse(string fileName)
        => string.Equals(fileName, "pubspec.yaml", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileName, "pubspec.yml", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
    {
        var deps = new List<Dependency>();
        try
        {
            var model = Yaml.Deserialize<Dictionary<string, object>>(content);
            if (model is not null)
            {
                Read(model, "dependencies", DependencyScope.Runtime, deps);
                Read(model, "dev_dependencies", DependencyScope.Dev, deps);
            }
        }
        catch (YamlDotNet.Core.YamlException) { }
        return new ParsedManifest { Path = relativePath, Ecosystem = "pub", Dependencies = deps };
    }

    private static void Read(Dictionary<string, object> model, string key, DependencyScope scope, List<Dependency> into)
    {
        if (!model.TryGetValue(key, out var obj) || obj is not Dictionary<object, object> table) return;
        foreach (var (k, v) in table)
        {
            var name = k?.ToString();
            if (string.IsNullOrEmpty(name) || name == "flutter") continue;
            into.Add(new Dependency(name, v as string, scope));
        }
    }
}
