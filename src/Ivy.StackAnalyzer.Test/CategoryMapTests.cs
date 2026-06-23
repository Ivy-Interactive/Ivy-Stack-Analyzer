using Ivy.StackAnalyzer.Detection;
using Xunit;

namespace Ivy.StackAnalyzer.Test;

public class CategoryMapTests
{
    [Theory]
    [InlineData("framework", TechCategory.Framework)]
    [InlineData("db", TechCategory.Database)]
    [InlineData("database", TechCategory.Database)]
    [InlineData("ui", TechCategory.Styling)]
    [InlineData("tool", TechCategory.Build)]               // specfy-oriented bucketing
    [InlineData("package_manager", TechCategory.PackageManager)]
    [InlineData("packagemanager", TechCategory.PackageManager)]
    [InlineData("queue", TechCategory.Queue)]
    [InlineData("iac", TechCategory.Iac)]
    [InlineData("FRAMEWORK", TechCategory.Framework)]      // case-insensitive
    [InlineData("totally-unknown", TechCategory.Library)]  // default
    public void Parse_maps_category(string input, TechCategory expected)
        => Assert.Equal(expected, CategoryMap.Parse(input));

    [Theory]
    [InlineData("low", Confidence.Low)]
    [InlineData("medium", Confidence.Medium)]
    [InlineData("high", Confidence.High)]
    [InlineData("anything-else", Confidence.High)]
    public void ParseConfidence_maps(string input, Confidence expected)
        => Assert.Equal(expected, CategoryMap.ParseConfidence(input));
}
