namespace Ivy.StackAnalyzer.Scanning;

/// <summary>A single file discovered during the walk.</summary>
public sealed record ScannedFile
{
    /// <summary>Path relative to the repo root, using forward slashes.</summary>
    public required string RelativePath { get; init; }
    public required string FullPath { get; init; }
    public required long Length { get; init; }
    public bool IsVendored { get; init; }

    public string FileName => RelativePath[(RelativePath.LastIndexOf('/') + 1)..];
    public string Extension => Path.GetExtension(RelativePath);
}

/// <summary>The result of walking a repository.</summary>
public sealed record ScanResult
{
    public required IReadOnlyList<ScannedFile> Files { get; init; }
    public required int TotalFiles { get; init; }
    public required IReadOnlyList<string> IgnoredDirectories { get; init; }
}
