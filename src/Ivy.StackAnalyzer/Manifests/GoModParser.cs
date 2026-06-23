using System.Text.RegularExpressions;

namespace Ivy.StackAnalyzer.Manifests;

/// <summary>Parses <c>go.mod</c> require directives.</summary>
public sealed partial class GoModParser : IManifestParser
{
    public bool CanParse(string fileName)
        => string.Equals(fileName, "go.mod", StringComparison.OrdinalIgnoreCase);

    public ParsedManifest Parse(string relativePath, string content)
    {
        var deps = new List<Dependency>();
        bool inBlock = false;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//")) continue;

            if (inBlock)
            {
                if (line == ")") { inBlock = false; continue; }
                AddRequire(line, deps);
                continue;
            }
            if (line.StartsWith("require (")) { inBlock = true; continue; }
            if (line.StartsWith("require "))
                AddRequire(line["require ".Length..].Trim(), deps);
        }
        return new ParsedManifest { Path = relativePath, Ecosystem = "go", Dependencies = deps };
    }

    private static void AddRequire(string line, List<Dependency> deps)
    {
        var m = RequireRegex().Match(line);
        if (!m.Success) return;
        var indirect = line.Contains("// indirect");
        deps.Add(new Dependency(m.Groups["mod"].Value, m.Groups["ver"].Value,
            indirect ? DependencyScope.Optional : DependencyScope.Runtime));
    }

    [GeneratedRegex(@"^(?<mod>[^\s]+)\s+(?<ver>v[^\s]+)")]
    private static partial Regex RequireRegex();
}
