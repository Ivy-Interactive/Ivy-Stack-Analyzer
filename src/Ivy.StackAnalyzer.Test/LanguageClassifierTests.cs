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
    [InlineData("shaders/water.fx", "HLSL")]     // not InfluxData FLUX
    [InlineData("kernels/conv.cl", "OpenCL")]    // not Common Lisp / Cool
    public void Resolves_expected_language(string path, string expected)
    {
        var c = Classifier.Classify(File(path));
        Assert.Equal(expected, c.Language);
    }

    [Fact]
    public void Binary_content_is_not_classified_as_source()
    {
        // A file whose extension maps to a language but whose bytes are binary
        // (e.g. a Python pickle) must not be counted as code.
        var tmp = Path.Combine(Path.GetTempPath(), "ivy-bin-" + Guid.NewGuid().ToString("N") + ".py");
        System.IO.File.WriteAllBytes(tmp, [0x80, 0x03, 0x00, 0x01, 0x02]);
        try
        {
            var c = Classifier.Classify(File("model.py", tmp));
            Assert.Null(c.Language);
        }
        finally { System.IO.File.Delete(tmp); }
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
