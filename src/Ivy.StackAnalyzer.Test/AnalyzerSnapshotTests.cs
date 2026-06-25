using Ivy.StackAnalyzer;
using Ivy.StackAnalyzer.Serialization;
using VerifyXunit;
using Xunit;

namespace Ivy.StackAnalyzer.Test;

/// <summary>
/// End-to-end fixture tests: run the <see cref="Analyzer"/> over a committed
/// mini-repo and snapshot the YAML. Adding a framework = add a fixture + accept
/// the snapshot.
/// </summary>
public class AnalyzerSnapshotTests
{
    private static string FixturePath(string name)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static SettingsTask VerifyFixture(string name)
    {
        var detection = new Analyzer().Analyze(FixturePath(name));
        var yaml = StackSerializer.ToYaml(detection);
        return Verifier.Verify(yaml, "yaml")
            .UseDirectory("Snapshots")
            .UseFileName(name)
            // durationMs is timing-dependent; repoPath is machine-dependent;
            // rulesLoaded / languageDefsLoaded are data-file counts that change
            // whenever a detector or language entry is added (not detection behavior).
            .ScrubLinesContaining("durationMs", "repoPath", "rulesLoaded", "languageDefsLoaded");
    }

    [Fact]
    public Task NextDotnetMonorepo() => VerifyFixture("next-dotnet-monorepo");

    [Fact]
    public Task GoService() => VerifyFixture("go-service");

    [Fact]
    public Task DjangoSpa() => VerifyFixture("django-spa");

    [Fact]
    public Task NumpyStyleLib() => VerifyFixture("numpy-style-lib");
}
