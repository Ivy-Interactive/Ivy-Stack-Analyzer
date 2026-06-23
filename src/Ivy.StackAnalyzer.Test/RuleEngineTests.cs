using Ivy.StackAnalyzer.Components;
using Ivy.StackAnalyzer.Data;
using Ivy.StackAnalyzer.Detection;
using Ivy.StackAnalyzer.Manifests;
using Xunit;

namespace Ivy.StackAnalyzer.Test;

public class RuleEngineTests
{
    private static ComponentContext Ctx(
        IEnumerable<EcosystemDependency>? deps = null,
        IEnumerable<string>? fileNames = null,
        IEnumerable<string>? sdks = null,
        IEnumerable<string>? extensions = null)
    {
        var names = (fileNames ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new ComponentContext
        {
            RelativePath = "apps/web",
            Files = [],
            Manifests = [],
            Languages = [],
            Dependencies = (deps ?? []).ToList(),
            Sdks = (sdks ?? []).ToList(),
            IsWorkspaceRoot = false,
            IsAuxiliary = false,
            FileNames = names,
            FilePaths = names.Select(n => "apps/web/" + n).ToHashSet(StringComparer.Ordinal),
            Extensions = (extensions ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase),
        };
    }

    private static EcosystemDependency Npm(string name) => new("npm", new Dependency(name, null, DependencyScope.Runtime));
    private static EcosystemDependency NuGet(string name) => new("nuget", new Dependency(name, null, DependencyScope.Runtime));

    private static readonly DataStore Data = DataStore.Load();

    [Fact]
    public void Detects_framework_by_dependency()
    {
        var engine = new RuleEngine(Data);
        var result = engine.Detect(Ctx(deps: [Npm("react")]));
        Assert.Contains(result, t => t.Name == "React" && t.Category == TechCategory.Framework);
    }

    [Fact]
    public void Supersedes_hides_subsumed_framework()
    {
        var engine = new RuleEngine(Data);
        var result = engine.Detect(Ctx(deps: [Npm("next"), Npm("react")]));
        Assert.Contains(result, t => t.Name == "Next.js");
        Assert.DoesNotContain(result, t => t.Name == "React");
    }

    [Fact]
    public void Detects_aspnetcore_by_sdk_attribute()
    {
        var engine = new RuleEngine(Data);
        var result = engine.Detect(Ctx(sdks: ["Microsoft.NET.Sdk.Web"]));
        Assert.Contains(result, t => t.Name == "ASP.NET Core" && t.Category == TechCategory.Framework);
    }

    [Fact]
    public void Detects_efcore_by_dependency_prefix()
    {
        var engine = new RuleEngine(Data);
        var result = engine.Detect(Ctx(deps: [NuGet("Microsoft.EntityFrameworkCore.SqlServer")]));
        Assert.Contains(result, t => t.Name == "Entity Framework Core" && t.Category == TechCategory.Orm);
    }

    [Fact]
    public void Detects_by_config_file_name()
    {
        var engine = new RuleEngine(Data);
        var result = engine.Detect(Ctx(deps: [Npm("tailwindcss")], fileNames: ["tailwind.config.ts"]));
        Assert.Contains(result, t => t.Category == TechCategory.Styling);
    }

    [Fact]
    public void No_match_yields_nothing()
    {
        var engine = new RuleEngine(Data);
        var result = engine.Detect(Ctx(deps: [Npm("this-package-does-not-exist-xyz")]));
        Assert.Empty(result);
    }
}
