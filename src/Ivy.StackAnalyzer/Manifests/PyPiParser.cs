using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;

namespace Ivy.StackAnalyzer.Manifests;

/// <summary>Parses Python manifests: <c>pyproject.toml</c>, <c>requirements*.txt</c>, <c>Pipfile</c>.</summary>
public sealed partial class PyPiParser : IManifestParser
{
    public bool CanParse(string fileName)
        => string.Equals(fileName, "pyproject.toml", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileName, "Pipfile", StringComparison.OrdinalIgnoreCase)
        || (fileName.StartsWith("requirements", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));

    public ParsedManifest Parse(string relativePath, string content)
    {
        var fileName = relativePath[(relativePath.LastIndexOf('/') + 1)..];
        var deps = fileName.ToLowerInvariant() switch
        {
            "pyproject.toml" => ParsePyproject(content),
            "pipfile" => ParsePipfile(content),
            _ => ParseRequirements(content),
        };
        return new ParsedManifest { Path = relativePath, Ecosystem = "pypi", Dependencies = deps };
    }

    private static List<Dependency> ParsePyproject(string content)
    {
        var deps = new List<Dependency>();
        TomlTable model;
        try { model = Toml.ToModel(content); }
        catch { return deps; }

        // PEP 621: [project] dependencies = ["foo>=1"], [project.optional-dependencies]
        if (model.TryGetValue("project", out var projObj) && projObj is TomlTable project)
        {
            if (project.TryGetValue("dependencies", out var d) && d is TomlArray arr)
                foreach (var item in arr) AddFromSpecifier(item?.ToString(), DependencyScope.Runtime, deps);
            if (project.TryGetValue("optional-dependencies", out var opt) && opt is TomlTable optTable)
                foreach (var group in optTable.Values)
                    if (group is TomlArray ga)
                        foreach (var item in ga) AddFromSpecifier(item?.ToString(), DependencyScope.Optional, deps);
        }

        // Poetry: [tool.poetry.dependencies] / [tool.poetry.group.*.dependencies]
        if (model.TryGetValue("tool", out var toolObj) && toolObj is TomlTable tool
            && tool.TryGetValue("poetry", out var poetryObj) && poetryObj is TomlTable poetry)
        {
            if (poetry.TryGetValue("dependencies", out var pd) && pd is TomlTable pdt)
                AddFromTable(pdt, DependencyScope.Runtime, deps, skipPython: true);
            if (poetry.TryGetValue("group", out var grp) && grp is TomlTable groups)
                foreach (var g in groups.Values)
                    if (g is TomlTable gt && gt.TryGetValue("dependencies", out var gd) && gd is TomlTable gdt)
                        AddFromTable(gdt, DependencyScope.Dev, deps, skipPython: true);
        }
        return deps;
    }

    private static List<Dependency> ParsePipfile(string content)
    {
        var deps = new List<Dependency>();
        TomlTable model;
        try { model = Toml.ToModel(content); }
        catch { return deps; }
        if (model.TryGetValue("packages", out var p) && p is TomlTable pt)
            AddFromTable(pt, DependencyScope.Runtime, deps, skipPython: false);
        if (model.TryGetValue("dev-packages", out var dp) && dp is TomlTable dpt)
            AddFromTable(dpt, DependencyScope.Dev, deps, skipPython: false);
        return deps;
    }

    private static List<Dependency> ParseRequirements(string content)
    {
        var deps = new List<Dependency>();
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('-')) continue;
            var hash = line.IndexOf(" #", StringComparison.Ordinal);
            if (hash >= 0) line = line[..hash].Trim();
            AddFromSpecifier(line, DependencyScope.Runtime, deps);
        }
        return deps;
    }

    private static void AddFromTable(TomlTable table, DependencyScope scope, List<Dependency> into, bool skipPython)
    {
        foreach (var (key, value) in table)
        {
            if (skipPython && string.Equals(key, "python", StringComparison.OrdinalIgnoreCase)) continue;
            string? version = value switch
            {
                string s => s,
                TomlTable t when t.TryGetValue("version", out var v) => v?.ToString(),
                _ => null,
            };
            into.Add(new Dependency(key, NormalizeVersion(version), scope));
        }
    }

    private static void AddFromSpecifier(string? spec, DependencyScope scope, List<Dependency> into)
    {
        if (string.IsNullOrWhiteSpace(spec)) return;
        var m = NameRegex().Match(spec.Trim());
        if (!m.Success) return;
        var name = m.Groups["name"].Value;
        var version = m.Groups["ver"].Success ? m.Groups["ver"].Value.Trim() : null;
        into.Add(new Dependency(name, version, scope));
    }

    private static string? NormalizeVersion(string? v)
        => string.IsNullOrWhiteSpace(v) || v == "*" ? null : v;

    [GeneratedRegex(@"^(?<name>[A-Za-z0-9][A-Za-z0-9._-]*)(?:\[[^\]]*\])?\s*(?<ver>[<>=!~^].*)?$")]
    private static partial Regex NameRegex();
}
