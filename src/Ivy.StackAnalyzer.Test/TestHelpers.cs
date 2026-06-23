using Ivy.StackAnalyzer.Models;
using Ivy.StackAnalyzer.Scanning;

namespace Ivy.StackAnalyzer.Test;

/// <summary>A throwaway on-disk repository for filesystem-touching tests.</summary>
internal sealed class TempRepo : IDisposable
{
    public string Root { get; }

    public TempRepo()
    {
        Root = Path.Combine(Path.GetTempPath(), "ivy-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>Write a file at a repo-relative path (forward slashes), creating dirs.</summary>
    public TempRepo Write(string relPath, string content = "")
    {
        var full = Path.Combine(Root, relPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return this;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>Shared loaded data + a helper to scan+classify a temp repo.</summary>
internal static class Harness
{
    public static readonly DataStore Data = DataStore.Load();

    public static List<ClassifiedFile> Classify(string root, AnalyzerOptions? options = null)
    {
        options ??= new AnalyzerOptions();
        var scan = new FileSystemScanner(Data, options).Scan(root);
        var classifier = new LanguageClassifier(Data);
        return scan.Files.Select(classifier.Classify).ToList();
    }

    public static ScannedFile File(string relPath, long length = 100, bool vendored = false, string? fullPath = null) => new()
    {
        RelativePath = relPath,
        FullPath = fullPath ?? (@"C:\nonexistent\" + relPath),
        Length = length,
        IsVendored = vendored,
    };

    public static ClassifiedFile Classified(string relPath, string? language, LanguageType type = LanguageType.Programming,
        long length = 100, bool vendored = false) => new()
    {
        File = File(relPath, length, vendored),
        Language = language,
        Type = language is null ? null : type,
    };
}
