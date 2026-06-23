using System.Text.Json;

namespace Ivy.StackAnalyzer.Manifests;

/// <summary>Parses <c>package.json</c>, including npm/yarn workspaces.</summary>
public sealed class NpmParser : IManifestParser
{
    public bool CanParse(string fileName)
        => string.Equals(fileName, "package.json", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
    {
        var deps = new List<Dependency>();
        var workspaces = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(content, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            var root = doc.RootElement;
            ReadDeps(root, "dependencies", DependencyScope.Runtime, deps);
            ReadDeps(root, "devDependencies", DependencyScope.Dev, deps);
            ReadDeps(root, "peerDependencies", DependencyScope.Peer, deps);
            ReadDeps(root, "optionalDependencies", DependencyScope.Optional, deps);

            if (root.TryGetProperty("workspaces", out var ws))
            {
                if (ws.ValueKind == JsonValueKind.Array)
                    foreach (var w in ws.EnumerateArray())
                        if (w.ValueKind == JsonValueKind.String) workspaces.Add(w.GetString()!);
                if (ws.ValueKind == JsonValueKind.Object && ws.TryGetProperty("packages", out var pkgs)
                    && pkgs.ValueKind == JsonValueKind.Array)
                    foreach (var w in pkgs.EnumerateArray())
                        if (w.ValueKind == JsonValueKind.String) workspaces.Add(w.GetString()!);
            }
        }
        catch (JsonException) { /* malformed manifest -> empty */ }

        return new ParsedManifest
        {
            Path = relativePath,
            Ecosystem = "npm",
            Dependencies = deps,
            Workspaces = workspaces,
        };
    }

    private static void ReadDeps(JsonElement root, string prop, DependencyScope scope, List<Dependency> into)
    {
        if (!root.TryGetProperty(prop, out var obj) || obj.ValueKind != JsonValueKind.Object) return;
        foreach (var p in obj.EnumerateObject())
            into.Add(new Dependency(p.Name, p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null, scope));
    }
}
