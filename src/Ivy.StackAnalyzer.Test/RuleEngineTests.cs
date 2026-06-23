using Ivy.StackAnalyzer.Components;
using Ivy.StackAnalyzer.Models;
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
        IEnumerable<string>? extensions = null,
        IEnumerable<string>? envVars = null,
        IEnumerable<string>? scripts = null,
        IEnumerable<KeyValuePair<string, string>>? properties = null)
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
            Properties = new Dictionary<string, string>(properties ?? [], StringComparer.OrdinalIgnoreCase),
            IsWorkspaceRoot = false,
            IsAuxiliary = false,
            FileNames = names,
            FilePaths = names.Select(n => "apps/web/" + n).ToHashSet(StringComparer.Ordinal),
            Extensions = (extensions ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase),
            EnvVarNames = (envVars ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase),
            Scripts = (scripts ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase),
        };
    }

    private static EcosystemDependency Npm(string name) => new("npm", new Dependency(name, null, DependencyScope.Runtime));
    private static EcosystemDependency NuGet(string name) => new("nuget", new Dependency(name, null, DependencyScope.Runtime));
    private static EcosystemDependency Pypi(string name, DependencyScope scope = DependencyScope.Runtime)
        => new("pypi", new Dependency(name, null, scope));

    private static readonly DataStore Data = DataStore.Load();

    [Fact]
    public void Detects_framework_by_dependency()
    {
        var engine = new RuleEngine(Data);
        var result = engine.Detect(Ctx(deps: [Npm("react")]));
        Assert.Contains(result, t => t.Name == "React" && t.Category == TechCategory.Framework);
    }

    [Theory]
    [InlineData("bun test")]
    [InlineData("bun test --coverage")]
    public void Detects_bun_test_from_package_json_script(string command)
    {
        var engine = new RuleEngine(Data);
        var result = engine.Detect(Ctx(scripts: [command]));
        Assert.Contains(result, t => t.Name == "bun test" && t.Category == TechCategory.Testing);
    }

    [Fact]
    public void Does_not_detect_bun_test_from_unrelated_script()
    {
        var engine = new RuleEngine(Data);
        var result = engine.Detect(Ctx(scripts: ["bun run build", "vitest"]));
        Assert.DoesNotContain(result, t => t.Name == "bun test");
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

    [Fact]
    public void Dotenv_only_match_is_low_confidence()
    {
        // An env-var name (e.g. from .env.example scaffolding) is a weak signal: the
        // detection still surfaces, but downgraded so the hash/digest can drop it.
        var engine = new RuleEngine(Data);
        var hit = engine.Detect(Ctx(envVars: ["ANTHROPIC_API_KEY"])).FirstOrDefault(t => t.Name == "Anthropic");
        Assert.NotNull(hit);
        Assert.Equal(Confidence.Low, hit!.Confidence);
    }

    [Fact]
    public void Strong_match_keeps_declared_confidence()
    {
        var engine = new RuleEngine(Data);
        var react = engine.Detect(Ctx(deps: [Npm("react")])).Single(t => t.Name == "React");
        Assert.Equal(Confidence.High, react.Confidence);
    }

    [Fact]
    public void Transitive_only_slot_tech_is_dropped_to_low()
    {
        // A framework/db/orm whose only support is a transitive dep (e.g. Django
        // pulled into a CLI's pip-compile lockfile) must not enter a hash slot.
        var engine = new RuleEngine(Data);
        var hit = engine.Detect(Ctx(deps: [Pypi("django", DependencyScope.Transitive)]))
            .FirstOrDefault(t => t.Name == "Django");
        Assert.NotNull(hit);
        Assert.Equal(Confidence.Low, hit!.Confidence);
    }

    [Fact]
    public void Direct_slot_tech_keeps_high_confidence()
    {
        var engine = new RuleEngine(Data);
        var hit = engine.Detect(Ctx(deps: [Pypi("django", DependencyScope.Runtime)]))
            .Single(t => t.Name == "Django");
        Assert.Equal(Confidence.High, hit.Confidence);
    }

    [Fact]
    public void Detects_windows_forms_by_msbuild_property()
    {
        var engine = new RuleEngine(Data);
        var result = engine.Detect(Ctx(properties: [new("UseWindowsForms", "true")]));
        Assert.Contains(result, t => t.Name == "Windows Forms" && t.Category == TechCategory.Framework);
    }

    [Theory]
    [InlineData("SUPABASE_URL", "Supabase")]
    [InlineData("SLACK_WEBHOOK_URL", "Slack")]
    [InlineData("POSTHOG_API_KEY", "PostHog")]
    public void Detects_env_wired_saas_from_env_var_names(string envVar, string expectedName)
    {
        // These hosted services are commonly wired via env vars / compose with no SDK
        // dependency; the dotenv-prefixed detectors should still surface them, but as a
        // weak (Low) signal so hash/digest consumers can drop the noise.
        var engine = new RuleEngine(Data);
        var hit = engine.Detect(Ctx(envVars: [envVar])).FirstOrDefault(t => t.Name == expectedName);
        Assert.NotNull(hit);
        Assert.Equal(Confidence.Low, hit!.Confidence);
    }
}
