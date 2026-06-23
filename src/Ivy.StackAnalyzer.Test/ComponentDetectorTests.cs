using Ivy.StackAnalyzer.Components;
using Ivy.StackAnalyzer.Manifests;
using Xunit;

namespace Ivy.StackAnalyzer.Test;

public class ComponentDetectorTests
{
    private static IReadOnlyList<ComponentContext> Detect(TempRepo repo)
    {
        var classified = Harness.Classify(repo.Root);
        return new ComponentDetector(new ManifestParserRegistry(), new AnalyzerOptions()).Detect(classified);
    }

    [Fact]
    public void Workspace_root_members_and_auxiliary_flags()
    {
        using var repo = new TempRepo();
        repo.Write("package.json", """{ "name": "root", "workspaces": ["apps/*"] }""")
            .Write("apps/web/package.json", """{ "name": "web", "dependencies": { "next": "14.0.0" } }""")
            .Write("apps/web/src/page.tsx", "export default () => null;\n")
            .Write("src/Api/Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\"></Project>")
            .Write("tests/e2e/package.json", """{ "name": "e2e", "devDependencies": { "@playwright/test": "1.0.0" } }""");

        var comps = Detect(repo);
        var byPath = comps.ToDictionary(c => c.RelativePath);

        Assert.Contains(".", byPath.Keys);
        Assert.Contains("apps/web", byPath.Keys);
        Assert.Contains("src/Api", byPath.Keys);
        Assert.Contains("tests/e2e", byPath.Keys);

        Assert.True(byPath["."].IsWorkspaceRoot);            // declares workspaces
        Assert.False(byPath["apps/web"].IsWorkspaceRoot);
        Assert.True(byPath["tests/e2e"].IsAuxiliary);        // under tests/
        Assert.False(byPath["apps/web"].IsAuxiliary);
    }

    [Fact]
    public void Follows_msbuild_import_for_shared_packagereferences()
    {
        using var repo = new TempRepo();
        repo.Write("tests/Common.props", """
            <Project>
              <ItemGroup>
                <PackageReference Include="NUnit3TestAdapter" Version="4.0.0" />
              </ItemGroup>
            </Project>
            """)
            .Write("tests/MyTests/MyTests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="..\Common.props" />
              <ItemGroup>
                <PackageReference Include="NUnit" Version="3.14.0" />
              </ItemGroup>
            </Project>
            """);

        var comp = Detect(repo).Single(c => c.RelativePath == "tests/MyTests");
        // The directly-declared dep and the one imported from ../Common.props both surface.
        Assert.Contains(comp.Dependencies, d => d.Dependency.Name == "NUnit");
        Assert.Contains(comp.Dependencies, d => d.Dependency.Name == "NUnit3TestAdapter");
    }

    [Fact]
    public void Nearest_root_attribution_keeps_nested_files_with_child()
    {
        using var repo = new TempRepo();
        repo.Write("package.json", """{ "name": "root" }""")
            .Write("apps/web/package.json", """{ "name": "web" }""")
            .Write("apps/web/src/deep/page.tsx", "export default () => null;\n")
            .Write("top.ts", "export const x = 1;\n");

        var byPath = Detect(repo).ToDictionary(c => c.RelativePath);

        // nested tsx belongs to apps/web, not the root component
        Assert.Contains(byPath["apps/web"].Files, f => f.File.RelativePath == "apps/web/src/deep/page.tsx");
        Assert.DoesNotContain(byPath["."].Files, f => f.File.RelativePath == "apps/web/src/deep/page.tsx");
        // a file above any child stays at root
        Assert.Contains(byPath["."].Files, f => f.File.RelativePath == "top.ts");
    }

    [Fact]
    public void One_directory_with_multiple_manifests_is_one_component()
    {
        using var repo = new TempRepo();
        repo.Write("svc/package.json", """{ "name": "svc", "dependencies": { "express": "4.0.0" } }""")
            .Write("svc/requirements.txt", "flask==3.0.0\n");

        var byPath = Detect(repo).ToDictionary(c => c.RelativePath);

        var svc = byPath["svc"];
        Assert.Equal(2, svc.Manifests.Count);
        Assert.Contains(svc.Manifests, m => m.Ecosystem == "npm");
        Assert.Contains(svc.Manifests, m => m.Ecosystem == "pypi");
    }

    [Fact]
    public void Root_component_always_exists()
    {
        using var repo = new TempRepo();
        repo.Write("readme.md", "# hi\n");   // no manifests at all
        Assert.Contains(Detect(repo), c => c.RelativePath == ".");
    }

    [Fact]
    public void Compose_environment_keys_become_env_var_names()
    {
        // Services like Supabase/Slack are often wired via docker-compose env keys
        // with no SDK dependency. Those keys must surface in EnvVarNames so the
        // existing dotenv-prefixed detectors can fire.
        using var repo = new TempRepo();
        repo.Write("package.json", """{ "name": "root" }""")
            .Write("docker-compose.yaml", """
                services:
                  api:
                    image: node:20
                    environment:
                      - SUPABASE_URL=http://localhost:54321
                      - SLACK_WEBHOOK_URL
                    ports:
                      - "3000:3000"
                  worker:
                    image: node:20
                    environment:
                      POSTHOG_API_KEY: phc_xxx
                      POSTHOG_HOST: https://eu.posthog.com
                """);

        var root = Detect(repo).Single(c => c.RelativePath == ".");

        Assert.Contains("SUPABASE_URL", root.EnvVarNames);
        Assert.Contains("SLACK_WEBHOOK_URL", root.EnvVarNames);
        Assert.Contains("POSTHOG_API_KEY", root.EnvVarNames);
        Assert.Contains("POSTHOG_HOST", root.EnvVarNames);
        // keys outside an `environment:` block (e.g. ports) must not leak in
        Assert.DoesNotContain("3000", root.EnvVarNames);
        Assert.DoesNotContain("ports", root.EnvVarNames);
    }

    [Fact]
    public void Test_fixture_and_suite_components_are_auxiliary()
    {
        using var repo = new TempRepo();
        repo.Write("package.json", """{ "name": "root", "workspaces": ["apps/*"] }""")
            .Write("apps/api/package.json", """{ "name": "api", "dependencies": { "vitest": "1.0.0" } }""")
            .Write("apps/test-site/package.json", """{ "name": "firecrawl-test-site", "dependencies": { "astro": "4.0.0" } }""")
            .Write("apps/test-suite/package.json", """{ "name": "test-suite", "devDependencies": { "jest": "29.0.0" } }""");

        var byPath = Detect(repo).ToDictionary(c => c.RelativePath);

        // hyphenated test-fixture / test-suite path segments are flagged auxiliary
        Assert.True(byPath["apps/test-site"].IsAuxiliary);
        Assert.True(byPath["apps/test-suite"].IsAuxiliary);
        // a real product component is not auxiliary
        Assert.False(byPath["apps/api"].IsAuxiliary);
    }
}
