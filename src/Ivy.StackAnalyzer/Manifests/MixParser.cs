using System.Text.RegularExpressions;

namespace Ivy.StackAnalyzer.Manifests;

/// <summary>Parses Elixir <c>mix.exs</c> deps from the <c>defp deps</c> list.</summary>
public sealed partial class MixParser : IManifestParser
{
    public bool CanParse(string fileName)
        => string.Equals(fileName, "mix.exs", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
    {
        var deps = new List<Dependency>();
        foreach (Match m in DepRegex().Matches(content))
        {
            var name = m.Groups["name"].Value;
            var version = m.Groups["ver"].Success ? m.Groups["ver"].Value : null;
            deps.Add(new Dependency(name, version, DependencyScope.Runtime));
        }
        return new ParsedManifest { Path = relativePath, Ecosystem = "hex", Dependencies = deps };
    }

    // {:phoenix, "~> 1.7"}  /  {:plug, github: "..."}
    [GeneratedRegex(@"\{\s*:(?<name>\w+)\s*,\s*(?:""(?<ver>[^""]+)"")?")]
    private static partial Regex DepRegex();
}
