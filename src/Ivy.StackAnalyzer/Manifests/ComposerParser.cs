using System.Text.Json;

namespace Ivy.StackAnalyzer.Manifests;

/// <summary>Parses PHP <c>composer.json</c>.</summary>
public sealed class ComposerParser : IManifestParser
{
    public bool CanParse(string fileName)
        => string.Equals(fileName, "composer.json", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
    {
        var deps = new List<Dependency>();
        try
        {
            using var doc = JsonDocument.Parse(content, new JsonDocumentOptions { AllowTrailingCommas = true });
            ReadDeps(doc.RootElement, "require", DependencyScope.Runtime, deps);
            ReadDeps(doc.RootElement, "require-dev", DependencyScope.Dev, deps);
        }
        catch (JsonException) { }
        return new ParsedManifest { Path = relativePath, Ecosystem = "composer", Dependencies = deps };
    }

    private static void ReadDeps(JsonElement root, string prop, DependencyScope scope, List<Dependency> into)
    {
        if (!root.TryGetProperty(prop, out var obj) || obj.ValueKind != JsonValueKind.Object) return;
        foreach (var p in obj.EnumerateObject())
        {
            // skip platform requirements like "php" or "ext-*"
            if (p.Name is "php" || p.Name.StartsWith("ext-", StringComparison.Ordinal)) continue;
            into.Add(new Dependency(p.Name, p.Value.GetString(), scope));
        }
    }
}
