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
            // Composer packages are always "vendor/name"; everything else is a platform
            // requirement (php, hhvm, ext-*, lib-*, composer-*, php-64bit, ...).
            if (!p.Name.Contains('/')) continue;
            var version = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
            into.Add(new Dependency(p.Name, version, scope));
        }
    }
}
