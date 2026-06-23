using Ivy.StackAnalyzer.Scanning;
using Xunit;

namespace Ivy.StackAnalyzer.Test;

public class LanguageAggregatorTests
{
    [Fact]
    public void Aggregate_excludes_vendored_and_unclassified_and_computes_percent()
    {
        var files = new[]
        {
            Harness.Classified("a.cs", "C#", LanguageType.Programming, length: 700),
            Harness.Classified("b.ts", "TypeScript", LanguageType.Programming, length: 300),
            Harness.Classified("data.json", "JSON", LanguageType.Data, length: 0),
            Harness.Classified("vendor.cs", "C#", LanguageType.Programming, length: 9999, vendored: true),
            Harness.Classified("unknown.xyz", null),
        };

        var stats = LanguageAggregator.Aggregate(files);

        var cs = Assert.Single(stats, s => s.Name == "C#");
        Assert.Equal(1, cs.Files);              // vendored C# excluded
        Assert.Equal(700, cs.Bytes);
        Assert.Equal(70.0, cs.Percent);
        Assert.Equal(30.0, Assert.Single(stats, s => s.Name == "TypeScript").Percent);
        Assert.Equal(3, stats.Count);   // C#, TypeScript, JSON (vendored + unclassified excluded)
        // ordered by bytes desc
        Assert.Equal("C#", stats[0].Name);
    }

    [Fact]
    public void PrimaryLanguages_returns_top_programming_only()
    {
        var files = new[]
        {
            Harness.Classified("a.cs", "C#", LanguageType.Programming, length: 500),
            Harness.Classified("b.ts", "TypeScript", LanguageType.Programming, length: 400),
            Harness.Classified("c.json", "JSON", LanguageType.Data, length: 100000), // big but Data
        };

        var primary = LanguageAggregator.PrimaryLanguages(LanguageAggregator.Aggregate(files));

        Assert.Equal(["C#", "TypeScript"], primary);   // JSON (Data) excluded
    }

    [Fact]
    public void Aggregate_empty_is_empty()
        => Assert.Empty(LanguageAggregator.Aggregate([]));
}
