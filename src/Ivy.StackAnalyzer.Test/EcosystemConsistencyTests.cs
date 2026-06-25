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
        "julia", "shards", "nimble", "cpan", "rebar", "opam", "swiftpm", "zig", "hackage",
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

    [Fact]
    public void Detector_rule_ids_are_unique()
    {
        // A duplicate id signals an accidental copy or a bad merge. Output dedupes by
        // (name, category), but ids should still be globally unique.
        var dupes = Harness.Data.Rules
            .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(dupes.Count == 0, "Duplicate detector rule ids: " + string.Join(", ", dupes));
    }

    [Fact]
    public void Detector_rule_count_has_not_collapsed()
    {
        // A malformed detector file deserializes to null -> 0 rules, silently dropping a
        // whole category with no other failing test (rulesLoaded is scrubbed from
        // snapshots). This floor catches that. Raise it as the corpus grows.
        Assert.True(Harness.Data.Rules.Count >= 1000,
            $"Only {Harness.Data.Rules.Count} detector rules loaded — a detector file may have failed to parse.");
    }
}
