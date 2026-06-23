using System.Text.RegularExpressions;

namespace Ivy.StackAnalyzer.Manifests;

/// <summary>Parses Ruby <c>Gemfile</c> and <c>*.gemspec</c> gem declarations.</summary>
public sealed partial class GemfileParser : IManifestParser
{
    public bool CanParse(string fileName)
        => string.Equals(fileName, "Gemfile", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".gemspec", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
    {
        var deps = new List<Dependency>();
        foreach (Match m in GemRegex().Matches(content))
        {
            var name = m.Groups["name"].Value;
            var version = m.Groups["ver"].Success ? m.Groups["ver"].Value : null;
            deps.Add(new Dependency(name, version, DependencyScope.Runtime));
        }
        return new ParsedManifest { Path = relativePath, Ecosystem = "rubygems", Dependencies = deps };
    }

    // gem 'rails', '~> 7.0'  /  add_dependency "nokogiri"
    [GeneratedRegex(@"(?:gem|add_dependency|add_runtime_dependency|add_development_dependency)\s*[\(\s]\s*['""](?<name>[\w\-.]+)['""](?:\s*,\s*['""](?<ver>[^'""]+)['""])?")]
    private static partial Regex GemRegex();
}
