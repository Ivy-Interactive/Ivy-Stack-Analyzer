using Ivy.StackAnalyzer;
using Xunit;

namespace Ivy.StackAnalyzer.Test;

public class AnalyzerTests
{
    [Fact]
    public void Analyze_missing_path_throws()
        => Assert.Throws<DirectoryNotFoundException>(
            () => new Analyzer().Analyze(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid())));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Analyze_blank_path_throws(string path)
        => Assert.Throws<ArgumentException>(() => new Analyzer().Analyze(path));

    [Fact]
    public void Analyze_populates_metadata()
    {
        using var repo = new TempRepo();
        repo.Write("go.mod", "module x\n\nrequire github.com/gin-gonic/gin v1.10.0\n");

        var result = new Analyzer().Analyze(repo.Root);

        Assert.True(result.Metadata.RulesLoaded > 0);
        Assert.True(result.Metadata.LanguageDefsLoaded > 0);
        Assert.False(string.IsNullOrEmpty(result.Metadata.AnalyzerVersion));
        Assert.Contains(result.Technologies, t => t.Name == "Gin");
    }
}
