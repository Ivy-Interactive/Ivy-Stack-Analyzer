using System.Text.RegularExpressions;

namespace Ivy.StackAnalyzer.Manifests;

/// <summary>
/// Parses Gradle build scripts (<c>build.gradle</c> / <c>build.gradle.kts</c>)
/// for dependency declarations. Best-effort line scan — Gradle scripts are code,
/// so this targets the common <c>configuration "group:artifact:version"</c> form.
/// </summary>
public sealed partial class GradleParser : IManifestParser
{
    public bool CanParse(string fileName)
        => string.Equals(fileName, "build.gradle", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileName, "build.gradle.kts", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
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
            deps.Add(new Dependency(name, version, scope));
        }
        return new ParsedManifest { Path = relativePath, Ecosystem = "maven", Dependencies = deps };
    }

    // matches: implementation "g:a:v"  /  testImplementation('g:a:v')  /  api 'g:a'
    [GeneratedRegex(@"(?<cfg>\w+)\s*[\(\s]\s*['""](?<coord>[\w.\-]+:[\w.\-]+(?::[\w.\-]+)?)['""]")]
    private static partial Regex DepRegex();
}
