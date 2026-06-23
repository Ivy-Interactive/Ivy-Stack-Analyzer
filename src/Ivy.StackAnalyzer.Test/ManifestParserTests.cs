using Ivy.StackAnalyzer.Manifests;
using Xunit;

namespace Ivy.StackAnalyzer.Test;

public class ManifestParserTests
{
    [Fact]
    public void Npm_parses_deps_scopes_and_workspaces()
    {
        const string json = """
        {
          "name": "root",
          "workspaces": ["apps/*", "packages/*"],
          "dependencies": { "next": "14.2.0", "react": "18.3.0" },
          "devDependencies": { "typescript": "5.4.0" },
          "peerDependencies": { "react-dom": "18.3.0" }
        }
        """;
        var m = new NpmParser().Parse("package.json", json);

        Assert.Equal("npm", m.Ecosystem);
        Assert.Equal(["apps/*", "packages/*"], m.Workspaces);
        Assert.Contains(m.Dependencies, d => d.Name == "next" && d.Version == "14.2.0" && d.Scope == DependencyScope.Runtime);
        Assert.Contains(m.Dependencies, d => d.Name == "typescript" && d.Scope == DependencyScope.Dev);
        Assert.Contains(m.Dependencies, d => d.Name == "react-dom" && d.Scope == DependencyScope.Peer);
    }

    [Fact]
    public void Npm_workspaces_object_form()
    {
        const string json = """{ "workspaces": { "packages": ["libs/*"] } }""";
        var m = new NpmParser().Parse("package.json", json);
        Assert.Equal(["libs/*"], m.Workspaces);
    }

    [Fact]
    public void NuGet_reads_packagereference_and_sdk()
    {
        const string xml = """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <ItemGroup>
            <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.0" />
            <PackageReference Include="Serilog" Version="3.1.0" />
            <PackageReference Include="StyleCop.Analyzers" Version="1.2.0" PrivateAssets="all" />
          </ItemGroup>
        </Project>
        """;
        var m = new NuGetParser().Parse("Api/Api.csproj", xml);

        Assert.Equal("nuget", m.Ecosystem);
        Assert.Equal("Microsoft.NET.Sdk.Web", m.Sdk);
        Assert.Contains(m.Dependencies, d => d.Name == "Microsoft.AspNetCore.OpenApi" && d.Version == "10.0.0");
        Assert.Contains(m.Dependencies, d => d.Name == "StyleCop.Analyzers" && d.Scope == DependencyScope.Dev);
    }

    [Fact]
    public void PyPi_pyproject_pep621_and_poetry()
    {
        const string pep621 = """
        [project]
        name = "svc"
        dependencies = ["django>=5.0", "requests[security]>=2.0"]
        [project.optional-dependencies]
        dev = ["pytest>=8.0"]
        """;
        var m = new PyPiParser().Parse("pyproject.toml", pep621);
        Assert.Equal("pypi", m.Ecosystem);
        Assert.Contains(m.Dependencies, d => d.Name == "django" && d.Version == ">=5.0");
        Assert.Contains(m.Dependencies, d => d.Name == "requests");
        Assert.Contains(m.Dependencies, d => d.Name == "pytest" && d.Scope == DependencyScope.Optional);
    }

    [Fact]
    public void PyPi_requirements_txt()
    {
        const string req = "flask==3.0.0\n# comment\nnumpy>=1.26  # inline\n-r other.txt\n";
        var m = new PyPiParser().Parse("requirements.txt", req);
        Assert.Contains(m.Dependencies, d => d.Name == "flask" && d.Version == "==3.0.0");
        Assert.Contains(m.Dependencies, d => d.Name == "numpy");
        Assert.DoesNotContain(m.Dependencies, d => d.Name.StartsWith('-'));
    }

    [Fact]
    public void PyPi_pep735_dependency_groups()
    {
        const string toml = """
        [project]
        name = "svc"
        dependencies = ["django>=5"]
        [dependency-groups]
        dev = ["pytest>=8", {include-group = "lint"}]
        lint = ["ruff"]
        """;
        var m = new PyPiParser().Parse("pyproject.toml", toml);
        Assert.Contains(m.Dependencies, d => d.Name == "pytest" && d.Scope == DependencyScope.Dev);
        Assert.Contains(m.Dependencies, d => d.Name == "ruff");
        Assert.DoesNotContain(m.Dependencies, d => d.Name == "include-group");
    }

    [Theory]
    [InlineData("dev-requirements.txt")]
    [InlineData("requirements-dev.txt")]
    [InlineData("test-requirements.txt")]
    public void PyPi_parses_alternate_requirements_names(string fileName)
    {
        var parser = new PyPiParser();
        Assert.True(parser.CanParse(fileName));
        var m = parser.Parse(fileName, "pytest==8.0\nblack\n");
        Assert.Contains(m.Dependencies, d => d.Name == "pytest");
    }

    [Fact]
    public void PyPi_setup_py_install_requires_and_extras()
    {
        const string setup = """
        from setuptools import setup
        setup(name="svc",
              install_requires=["flask>=3", "requests"],
              extras_require={"dev": ["pytest"]})
        """;
        var m = new PyPiParser().Parse("setup.py", setup);
        Assert.Contains(m.Dependencies, d => d.Name == "flask");
        Assert.Contains(m.Dependencies, d => d.Name == "requests");
        Assert.Contains(m.Dependencies, d => d.Name == "pytest" && d.Scope == DependencyScope.Optional);
    }

    [Fact]
    public void PyPi_conda_environment_yaml()
    {
        const string env = """
        name: ml
        channels: [conda-forge]
        dependencies:
          - python=3.10
          - pytorch
          - conda-forge::numpy
          - pip:
            - transformers==4.0
        """;
        var m = new PyPiParser().Parse("environment.yml", env);
        Assert.Contains(m.Dependencies, d => d.Name == "pytorch");
        Assert.Contains(m.Dependencies, d => d.Name == "numpy");
        Assert.Contains(m.Dependencies, d => d.Name == "transformers");
        Assert.DoesNotContain(m.Dependencies, d => d.Name == "python");
    }

    [Fact]
    public void PyPi_pip_compile_marks_transitive_deps()
    {
        const string compiled = """
        # This file is autogenerated by pip-compile
        fastapi==0.110.0
            # via -r requirements.in
        starlette==0.36.0
            # via fastapi
        """;
        var m = new PyPiParser().Parse("requirements.txt", compiled);
        Assert.Contains(m.Dependencies, d => d.Name == "fastapi" && d.Scope == DependencyScope.Runtime);
        Assert.Contains(m.Dependencies, d => d.Name == "starlette" && d.Scope == DependencyScope.Transitive);
    }

    [Fact]
    public void Paket_dependencies_and_references()
    {
        const string deps = """
        source https://api.nuget.org/v3/index.json
        nuget FSharp.Core ~> 6.0
        nuget Elmish

        group Test
          nuget NUnit3TestAdapter
        """;
        var m = new PaketParser().Parse("paket.dependencies", deps);
        Assert.Equal("nuget", m.Ecosystem);
        Assert.Contains(m.Dependencies, d => d.Name == "FSharp.Core");
        Assert.Contains(m.Dependencies, d => d.Name == "Elmish" && d.Scope == DependencyScope.Runtime);
        Assert.Contains(m.Dependencies, d => d.Name == "NUnit3TestAdapter" && d.Scope == DependencyScope.Dev);

        const string refs = "FSharp.Core\nElmish\ngroup Test\n  NUnit3TestAdapter\n";
        var r = new PaketParser().Parse("Proj/paket.references", refs);
        Assert.Contains(r.Dependencies, d => d.Name == "Elmish");
        Assert.Contains(r.Dependencies, d => d.Name == "NUnit3TestAdapter" && d.Scope == DependencyScope.Dev);
    }

    [Fact]
    public void NuGet_reads_msbuild_properties()
    {
        const string xml = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <UseWindowsForms>true</UseWindowsForms>
          </PropertyGroup>
        </Project>
        """;
        var m = new NuGetParser().Parse("App/App.csproj", xml);
        Assert.Equal("true", m.Properties["UseWindowsForms"]);
        Assert.Equal("WinExe", m.Properties["OutputType"]);
    }

    [Fact]
    public void GoMod_require_block_and_indirect()
    {
        const string gomod = """
        module example.com/svc

        go 1.22

        require (
            github.com/google/uuid v1.6.0
            golang.org/x/sys v0.20.0 // indirect
        )

        require github.com/gin-gonic/gin v1.10.0
        """;
        var m = new GoModParser().Parse("go.mod", gomod);
        Assert.Equal("go", m.Ecosystem);
        Assert.Contains(m.Dependencies, d => d.Name == "github.com/google/uuid" && d.Version == "v1.6.0");
        Assert.Contains(m.Dependencies, d => d.Name == "golang.org/x/sys" && d.Scope == DependencyScope.Optional);
        Assert.Contains(m.Dependencies, d => d.Name == "github.com/gin-gonic/gin");
    }

    [Fact]
    public void Cargo_deps_and_workspace_members()
    {
        const string toml = """
        [package]
        name = "svc"

        [dependencies]
        serde = "1.0"
        tokio = { version = "1.38", features = ["full"] }

        [workspace]
        members = ["crates/a", "crates/b"]
        """;
        var m = new CargoParser().Parse("Cargo.toml", toml);
        Assert.Equal("cargo", m.Ecosystem);
        Assert.Equal(["crates/a", "crates/b"], m.Workspaces);
        Assert.Contains(m.Dependencies, d => d.Name == "serde" && d.Version == "1.0");
        Assert.Contains(m.Dependencies, d => d.Name == "tokio" && d.Version == "1.38");
    }

    [Fact]
    public void Maven_dependencies_and_scopes()
    {
        const string pom = """
        <project xmlns="http://maven.apache.org/POM/4.0.0">
          <dependencies>
            <dependency>
              <groupId>org.springframework.boot</groupId>
              <artifactId>spring-boot-starter-web</artifactId>
              <version>3.2.0</version>
            </dependency>
            <dependency>
              <groupId>junit</groupId>
              <artifactId>junit</artifactId>
              <scope>test</scope>
            </dependency>
          </dependencies>
        </project>
        """;
        var m = new MavenParser().Parse("pom.xml", pom);
        Assert.Equal("maven", m.Ecosystem);
        Assert.Contains(m.Dependencies, d => d.Name == "org.springframework.boot:spring-boot-starter-web");
        Assert.Contains(m.Dependencies, d => d.Name == "junit:junit" && d.Scope == DependencyScope.Dev);
    }

    [Fact]
    public void Composer_skips_platform_requirements()
    {
        const string json = """
        { "require": { "php": ">=8.2", "laravel/framework": "^11.0", "ext-pdo": "*" },
          "require-dev": { "phpunit/phpunit": "^11.0" } }
        """;
        var m = new ComposerParser().Parse("composer.json", json);
        Assert.Contains(m.Dependencies, d => d.Name == "laravel/framework");
        Assert.DoesNotContain(m.Dependencies, d => d.Name is "php" or "ext-pdo");
        Assert.Contains(m.Dependencies, d => d.Name == "phpunit/phpunit" && d.Scope == DependencyScope.Dev);
    }

    [Fact]
    public void Gemfile_gems()
    {
        const string gemfile = """
        source "https://rubygems.org"
        gem "rails", "~> 7.1"
        gem "pg"
        """;
        var m = new GemfileParser().Parse("Gemfile", gemfile);
        Assert.Equal("rubygems", m.Ecosystem);
        Assert.Contains(m.Dependencies, d => d.Name == "rails" && d.Version == "~> 7.1");
        Assert.Contains(m.Dependencies, d => d.Name == "pg");
    }

    [Fact]
    public void Pubspec_dependencies()
    {
        const string pub = """
        name: app
        dependencies:
          flutter:
            sdk: flutter
          http: ^1.2.0
        dev_dependencies:
          flutter_test:
            sdk: flutter
        """;
        var m = new PubspecParser().Parse("pubspec.yaml", pub);
        Assert.Equal("pub", m.Ecosystem);
        Assert.Contains(m.Dependencies, d => d.Name == "http" && d.Version == "^1.2.0");
        Assert.DoesNotContain(m.Dependencies, d => d.Name == "flutter");
    }

    [Fact]
    public void Mix_deps()
    {
        const string mix = """
        defmodule App.MixProject do
          defp deps do
            [
              {:phoenix, "~> 1.7"},
              {:ecto, "~> 3.11"}
            ]
          end
        end
        """;
        var m = new MixParser().Parse("mix.exs", mix);
        Assert.Equal("hex", m.Ecosystem);
        Assert.Contains(m.Dependencies, d => d.Name == "phoenix" && d.Version == "~> 1.7");
        Assert.Contains(m.Dependencies, d => d.Name == "ecto");
    }

    [Fact]
    public void Registry_resolves_by_filename()
    {
        var reg = new ManifestParserRegistry();
        Assert.IsType<NpmParser>(reg.Resolve("package.json"));
        Assert.IsType<NuGetParser>(reg.Resolve("Api.csproj"));
        Assert.IsType<GoModParser>(reg.Resolve("go.mod"));
        Assert.Null(reg.Resolve("random.txt"));
    }
}
