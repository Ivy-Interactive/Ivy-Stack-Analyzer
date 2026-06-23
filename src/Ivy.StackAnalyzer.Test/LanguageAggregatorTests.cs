using Ivy.StackAnalyzer.Scanning;
using Xunit;

namespace Ivy.StackAnalyzer.Test;

public class LanguageAggregatorTests
{
    [Fact]
    public void Aggregate_excludes_vendored_unclassified_prose_and_data_and_computes_percent()
    {
        var files = new[]
        {
            Harness.Classified("a.cs", "C#", LanguageType.Programming, length: 700),
            Harness.Classified("b.ts", "TypeScript", LanguageType.Programming, length: 300),
            Harness.Classified("data.json", "JSON", LanguageType.Data, length: 5000),      // data: excluded
            Harness.Classified("readme.md", "Markdown", LanguageType.Prose, length: 5000), // prose: excluded
            Harness.Classified("vendor.cs", "C#", LanguageType.Programming, length: 9999, vendored: true),
            Harness.Classified("unknown.xyz", null),
        };

        var stats = LanguageAggregator.Aggregate(files);

        var cs = Assert.Single(stats, s => s.Name == "C#");
        Assert.Equal(1, cs.Files);              // vendored C# excluded
        Assert.Equal(700, cs.Bytes);
        Assert.Equal(70.0, cs.Percent);         // share of programming+markup bytes only (700/1000)
        Assert.Equal(30.0, Assert.Single(stats, s => s.Name == "TypeScript").Percent);
        Assert.Equal(2, stats.Count);   // C#, TypeScript only (vendored, unclassified, prose, data excluded)
        Assert.DoesNotContain(stats, s => s.Name == "JSON");
        Assert.DoesNotContain(stats, s => s.Name == "Markdown");
        // ordered by bytes desc
        Assert.Equal("C#", stats[0].Name);
    }

    [Fact]
    public void Aggregate_excludes_documentation_files()
    {
        var files = new[]
        {
            Harness.Classified("src/a.cs", "C#", LanguageType.Programming, length: 700),
            new ClassifiedFile
            {
                File = new ScannedFile
                {
                    RelativePath = "examples/demo.vue",
                    FullPath = @"C:\nonexistent\examples\demo.vue",
                    Length = 9999,
                    IsDocumentation = true,
                },
                Language = "Vue",
                Type = LanguageType.Markup,
            },
        };

        var stats = LanguageAggregator.Aggregate(files);

        Assert.Equal("C#", Assert.Single(stats).Name);   // documentation Vue excluded
    }

    [Fact]
    public void DominantProgrammingLanguage_ignores_markup_data_and_prose()
    {
        var stats = new[]
        {
            new LanguageStat("Jupyter Notebook", LanguageType.Markup, 1, 9000, 90.0),
            new LanguageStat("TypeScript", LanguageType.Programming, 1, 1000, 10.0),
        };

        Assert.Equal("TypeScript", LanguageAggregator.DominantProgrammingLanguage(stats));
        Assert.Null(LanguageAggregator.DominantProgrammingLanguage(
            [new LanguageStat("HTML", LanguageType.Markup, 1, 100, 100.0)]));
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
