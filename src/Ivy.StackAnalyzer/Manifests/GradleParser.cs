using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;

namespace Ivy.StackAnalyzer.Manifests;

/// <summary>
/// Parses Gradle dependency declarations from <c>build.gradle</c> /
/// <c>build.gradle.kts</c> (a best-effort line scan — Gradle scripts are code) and
/// from Gradle <c>*.versions.toml</c> version catalogs (the modern
/// <c>gradle/libs.versions.toml</c> convention, parsed as TOML). Coordinates are
/// reported under the <c>maven</c> ecosystem.
/// </summary>
public sealed partial class GradleParser : IManifestParser
{
    public bool CanParse(string fileName)
        => string.Equals(fileName, "build.gradle", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileName, "build.gradle.kts", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".versions.toml", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
    {
        var fileName = relativePath[(relativePath.LastIndexOf('/') + 1)..];
        var deps = fileName.EndsWith(".versions.toml", StringComparison.OrdinalIgnoreCase)
            ? ParseVersionCatalog(content)
            : ParseBuildScript(content);
        return new ParsedManifest { Path = relativePath, Ecosystem = "maven", Dependencies = deps };
    }

    private static List<Dependency> ParseBuildScript(string content)
    {
        var deps = new List<Dependency>();
        foreach (Match m in DepRegex().Matches(content))
        {
            var config = m.Groups["cfg"].Value;
            var coord = m.Groups["coord"].Value;
            var parts = coord.Split(':');
            if (parts.Length < 2) continue;
            var name = $"{parts[0]}:{parts[1]}";
            var version = parts.Length >= 3 ? parts[2] : null;
            var scope = config.Contains("test", StringComparison.OrdinalIgnoreCase)
                ? DependencyScope.Dev : DependencyScope.Runtime;
            deps.Add(new Dependency(name, NormalizeVersion(version), scope));
        }
        return deps;
    }

    // Gradle version catalog (libs.versions.toml): a [libraries] table whose entries
    // are either a "group:artifact:version" string, a { module = "g:a", … } table, or
    // a { group = "g", name = "a", … } table. We only need the g:a coordinate.
    private static List<Dependency> ParseVersionCatalog(string content)
    {
        var deps = new List<Dependency>();
        TomlTable model;
        try { model = Toml.ToModel(content); }
        catch { return deps; }
        if (!model.TryGetValue("libraries", out var libsObj) || libsObj is not TomlTable libs)
            return deps;

        foreach (var entry in libs.Values)
        {
            string? coord = entry switch
            {
                string s => s,
                TomlTable t when t.TryGetValue("module", out var mod) => mod?.ToString(),
                TomlTable t when t.TryGetValue("group", out var g) && t.TryGetValue("name", out var n)
                    => $"{g}:{n}",
                _ => null,
            };
            if (string.IsNullOrWhiteSpace(coord)) continue;
            var parts = coord.Split(':');
            if (parts.Length < 2) continue;
            deps.Add(new Dependency($"{parts[0]}:{parts[1]}",
                NormalizeVersion(parts.Length >= 3 ? parts[2] : null), DependencyScope.Runtime));
        }
        return deps;
    }

    // An interpolated version ("$ver", "${libs.versions.x}") is not a real version.
    private static string? NormalizeVersion(string? v)
        => string.IsNullOrWhiteSpace(v) || v.Contains('$') ? null : v;

    // matches: implementation "g:a:v"  /  testImplementation('g:a:v')  /  api 'g:a'.
    // The version segment tolerates interpolation ("g:a:$ver") by accepting any
    // non-quote characters after the artifact, so the whole declaration still matches.
    [GeneratedRegex(@"(?<cfg>\w+)\s*[\(\s]\s*['""](?<coord>[\w.\-]+:[\w.\-]+(?::[^'""]+)?)['""]")]
    private static partial Regex DepRegex();
}
