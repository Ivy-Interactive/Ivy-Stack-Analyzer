using Xunit;

namespace Ivy.StackAnalyzer.Test;

/// <summary>
/// Guards the join key between detector rules and manifest parsers: every
/// <c>ecosystem:</c> in detectors/*.yml must be one a parser emits (or a known
/// specfy-sourced synthetic ecosystem we don't parse yet). A typo like "golang"
/// would otherwise silently zero a whole ecosystem's detections.
/// </summary>
public class EcosystemConsistencyTests
{
    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        // emitted by IManifestParser implementations
        "npm", "nuget", "pypi", "go", "cargo", "maven", "composer", "rubygems", "pub", "hex",
        // synthetic dependency sources carried over from specfy (no parser yet; match is a no-op but the token is valid)
        "docker", "githubAction", "deno", "terraform",
    };

    [Fact]
    public void All_detector_ecosystems_are_known()
    {
        var unknown = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Harness.Data.Rules)
        {
            foreach (var d in r.Match.Deps) if (!Known.Contains(d.Ecosystem)) unknown.Add(d.Ecosystem);
            foreach (var d in r.Match.DepsRegex) if (!Known.Contains(d.Ecosystem)) unknown.Add(d.Ecosystem);
            foreach (var d in r.Match.DepPrefix) if (!Known.Contains(d.Ecosystem)) unknown.Add(d.Ecosystem);
        }

        Assert.True(unknown.Count == 0,
            "Unknown ecosystem tokens in detectors/*.yml (no parser emits these): " + string.Join(", ", unknown));
    }
}
