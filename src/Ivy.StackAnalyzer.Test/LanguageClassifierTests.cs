using Ivy.StackAnalyzer.Models;
using Ivy.StackAnalyzer.Scanning;
using Xunit;

namespace Ivy.StackAnalyzer.Test;

public class LanguageClassifierTests
{
    private static readonly LanguageClassifier Classifier = new(DataStore.Load());

    private static ScannedFile File(string relPath, string? fullPath = null) => new()
    {
        RelativePath = relPath,
        FullPath = fullPath ?? @"C:\nonexistent\" + relPath,
        Length = 100,
    };

    [Theory]
    [InlineData("src/App.cs", "C#")]
    [InlineData("src/main.go", "Go")]
    [InlineData("src/index.ts", "TypeScript")]   // not Qt "XML"
    [InlineData("src/App.tsx", "TSX")]
    [InlineData("README.md", "Markdown")]        // not "GCC Machine Description"
    [InlineData("Dockerfile", "Dockerfile")]     // by filename
    [InlineData("go.mod", "Go Module")]          // by filename
    [InlineData("styles/site.css", "CSS")]
    public void Resolves_expected_language(string path, string expected)
    {
        var c = Classifier.Classify(File(path));
        Assert.Equal(expected, c.Language);
    }

    [Fact]
    public void Unknown_extension_is_unclassified()
    {
        var c = Classifier.Classify(File("data/blob.xyzzy"));
        Assert.Null(c.Language);
    }

    [Fact]
    public void Shebang_resolves_interpreter_when_extension_unknown()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ivy-shebang-" + Guid.NewGuid().ToString("N"));
        System.IO.File.WriteAllText(tmp, "#!/usr/bin/env python3\nprint('hi')\n");
        try
        {
            var c = Classifier.Classify(File("scripts/run", tmp));
            Assert.Equal("Python", c.Language);
        }
        finally { System.IO.File.Delete(tmp); }
    }
}
