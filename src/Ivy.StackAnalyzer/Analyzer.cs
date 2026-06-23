namespace Ivy.StackAnalyzer;

/// <summary>
/// Entry point for analyzing a repository on disk into a deterministic
/// <see cref="StackDetection"/> report. The analyzer reports facts and
/// structure only — it never labels a component or decides which layers matter.
/// </summary>
public sealed class Analyzer
{
    private readonly AnalyzerOptions _options;

    public Analyzer(AnalyzerOptions? options = null)
        => _options = options ?? new AnalyzerOptions();

    public StackDetection Analyze(string repoPath)
        => AnalyzeAsync(repoPath).GetAwaiter().GetResult();

    public Task<StackDetection> AnalyzeAsync(string repoPath, CancellationToken ct = default)
        => Pipeline.RunAsync(repoPath, _options, ct);
}
