using Tomlyn;
using Tomlyn.Model;

namespace Ivy.StackAnalyzer.Manifests;

/// <summary>Parses <c>Cargo.toml</c> dependencies and <c>[workspace] members</c>.</summary>
public sealed class CargoParser : IManifestParser
{
    public bool CanParse(string fileName)
        => string.Equals(fileName, "Cargo.toml", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
    {
        var deps = new List<Dependency>();
        var workspaces = new List<string>();
        TomlTable model;
        try { model = Toml.ToModel(content); }
        catch { return new ParsedManifest { Path = relativePath, Ecosystem = "cargo", Dependencies = deps }; }

        Read(model, "dependencies", DependencyScope.Runtime, deps);
        Read(model, "dev-dependencies", DependencyScope.Dev, deps);
        Read(model, "build-dependencies", DependencyScope.Dev, deps);

        if (model.TryGetValue("workspace", out var wsObj) && wsObj is TomlTable ws
            && ws.TryGetValue("members", out var m) && m is TomlArray members)
            foreach (var item in members)
                if (item is string s) workspaces.Add(s);

        return new ParsedManifest
        {
            Path = relativePath,
            Ecosystem = "cargo",
            Dependencies = deps,
            Workspaces = workspaces,
        };
    }

    private static void Read(TomlTable model, string key, DependencyScope scope, List<Dependency> into)
    {
        if (!model.TryGetValue(key, out var obj) || obj is not TomlTable table) return;
        foreach (var (name, value) in table)
        {
            string? version = value switch
            {
                string s => s,
                TomlTable t when t.TryGetValue("version", out var v) => v?.ToString(),
                _ => null,
            };
            into.Add(new Dependency(name, version, scope));
        }
    }
}
