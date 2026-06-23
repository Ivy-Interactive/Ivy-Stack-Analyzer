using Ivy.StackAnalyzer.Scanning;
using Xunit;

namespace Ivy.StackAnalyzer.Test;

public class FileSystemScannerTests
{
    private static TempRepo Sample()
    {
        var repo = new TempRepo();
        repo.Write("src/app.cs", "class A {}\n")
            .Write("node_modules/left-pad/index.js", "module.exports = 1;\n")
            .Write("bin/Debug/app.dll", "binary\n")
            .Write("obj/proj.json", "{}\n")
            .Write(".gitignore", "secret.txt\n")
            .Write("secret.txt", "shh\n");
        return repo;
    }

    [Fact]
    public void GitAttributes_linguist_overrides_flag_files()
    {
        using var repo = new TempRepo();
        repo.Write(".gitattributes",
                "tests/data/** linguist-vendored\nschemas/*.gen.cs linguist-generated\nrefmanual/** linguist-documentation\n")
            .Write("src/App.cs", "class A {}\n")
            .Write("tests/data/fixture.tex", "\\documentclass{article}\n")
            .Write("schemas/Model.gen.cs", "class M {}\n")
            .Write("refmanual/guide.cs", "class D {}\n");

        var scan = new FileSystemScanner(Harness.Data, new AnalyzerOptions()).Scan(repo.Root);
        var byPath = scan.Files.ToDictionary(f => f.RelativePath);

        Assert.False(byPath["src/App.cs"].IsVendored);
        Assert.False(byPath["src/App.cs"].IsDocumentation);
        Assert.True(byPath["tests/data/fixture.tex"].IsVendored);   // linguist-vendored
        Assert.True(byPath["schemas/Model.gen.cs"].IsVendored);     // linguist-generated
        Assert.True(byPath["refmanual/guide.cs"].IsDocumentation);  // linguist-documentation
    }

    [Fact]
    public void Prunes_vendored_dirs_and_gitignored_files_by_default()
    {
        using var repo = Sample();
        var scan = new FileSystemScanner(Harness.Data, new AnalyzerOptions()).Scan(repo.Root);
        var paths = scan.Files.Select(f => f.RelativePath).ToHashSet();

        Assert.Contains("src/app.cs", paths);
        Assert.DoesNotContain("node_modules/left-pad/index.js", paths);
        Assert.DoesNotContain("bin/Debug/app.dll", paths);
        Assert.DoesNotContain("obj/proj.json", paths);
        Assert.DoesNotContain("secret.txt", paths);   // .gitignore honored
        Assert.Contains(scan.IgnoredDirectories, d => d == "node_modules");
    }

    [Fact]
    public void Include_vendored_keeps_vendored_dirs()
    {
        using var repo = Sample();
        var opts = new AnalyzerOptions { IncludeVendored = true };
        var scan = new FileSystemScanner(Harness.Data, opts).Scan(repo.Root);
        var paths = scan.Files.Select(f => f.RelativePath).ToHashSet();

        Assert.Contains("node_modules/left-pad/index.js", paths);
    }

    [Fact]
    public void No_gitignore_option_keeps_ignored_files()
    {
        using var repo = Sample();
        var opts = new AnalyzerOptions { RespectGitignore = false };
        var scan = new FileSystemScanner(Harness.Data, opts).Scan(repo.Root);
        var paths = scan.Files.Select(f => f.RelativePath).ToHashSet();

        Assert.Contains("secret.txt", paths);
    }

    [Fact]
    public void Scan_is_deterministic_ordinal_order()
    {
        using var repo = new TempRepo();
        repo.Write("z.cs", "").Write("a.cs", "").Write("m/x.cs", "");
        var scan = new FileSystemScanner(Harness.Data, new AnalyzerOptions()).Scan(repo.Root);
        var paths = scan.Files.Select(f => f.RelativePath).ToList();
        var sorted = paths.OrderBy(p => p, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, paths);
    }
}
