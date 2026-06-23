using System.Text.Json;
using Ivy.StackAnalyzer;
using Ivy.StackAnalyzer.Serialization;
using Xunit;

namespace Ivy.StackAnalyzer.Test;

public class SerializerTests
{
    private static StackDetection Sample()
    {
        // Two value-equal LanguageStat instances (repo-wide + component) exercise the
        // no-alias requirement; a null ComponentPath / Readme exercise null omission.
        return new StackDetection
        {
            RepoPath = "/tmp/x",
            Summary = new RepoSummary
            {
                TotalFiles = 2,
                AnalyzedFiles = 2,
                PrimaryLanguages = ["C#"],
                ComponentCount = 1,
            },
            Languages = [new LanguageStat("C#", LanguageType.Programming, 1, 100, 100.0)],
            Components =
            [
                new Component
                {
                    RelativePath = ".",
                    Languages = [new LanguageStat("C#", LanguageType.Programming, 1, 100, 100.0)],
                    Manifests = [],
                    Technologies = [new DetectedTechnology("ASP.NET Core", TechCategory.Framework, "Sdk=Web", Confidence.High, ".")],
                },
            ],
            Technologies = [new DetectedTechnology("ASP.NET Core", TechCategory.Framework, "Sdk=Web", Confidence.High, null)],
            Infrastructure = [],
            Readme = null,
            Metadata = new AnalyzerMetadata("0.1.0", 5, 10, 20, []),
        };
    }

    [Fact]
    public void Yaml_uses_camelcase_enums_omits_nulls_and_no_aliases()
    {
        var yaml = StackSerializer.ToYaml(Sample());

        Assert.Contains("category: framework", yaml);   // enum -> camelCase
        Assert.Contains("type: programming", yaml);
        Assert.Contains("confidence: high", yaml);
        Assert.DoesNotContain("readme:", yaml);          // null omitted
        Assert.DoesNotContain("&o", yaml);               // no YAML anchors
        Assert.DoesNotContain("*o", yaml);               // no YAML aliases

        // The value-equal LanguageStat is emitted in both places (not aliased).
        var occurrences = yaml.Split("name: C#").Length - 1;
        Assert.Equal(2, occurrences);
    }

    [Fact]
    public void Json_uses_camelcase_enums_and_omits_nulls()
    {
        var json = StackSerializer.ToJson(Sample());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("/tmp/x", root.GetProperty("repoPath").GetString());
        var tech = root.GetProperty("technologies")[0];
        Assert.Equal("ASP.NET Core", tech.GetProperty("name").GetString());
        Assert.Equal("framework", tech.GetProperty("category").GetString());   // enum -> camelCase string
        Assert.False(tech.TryGetProperty("componentPath", out _));             // null omitted
        Assert.False(root.TryGetProperty("readme", out _));                    // null omitted
    }

    [Fact]
    public void Serialize_dispatches_on_format()
    {
        var d = Sample();
        Assert.StartsWith("{", StackSerializer.Serialize(d, OutputFormat.Json).TrimStart());
        Assert.Contains("repoPath:", StackSerializer.Serialize(d, OutputFormat.Yaml));
    }
}
