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

    [Fact]
    public void Gradle_version_catalog_and_interpolated_versions()
    {
        // libs.versions.toml [libraries] entries (module / group+name forms).
        var catalog = """
        [libraries]
        retrofit = { module = "com.squareup.retrofit2:retrofit", version.ref = "retrofit" }
        core = { group = "androidx.core", name = "core-ktx", version = "1.12.0" }
        """;
        var cat = new GradleParser().Parse("gradle/libs.versions.toml", catalog);
        Assert.Equal("maven", cat.Ecosystem);
        Assert.Contains(cat.Dependencies, d => d.Name == "com.squareup.retrofit2:retrofit");
        Assert.Contains(cat.Dependencies, d => d.Name == "androidx.core:core-ktx");

        // an interpolated version ("g:a:$ver") must not drop the whole declaration.
        var script = """implementation "org.koin:koin-android:$versions.koinVersion" """;
        var s = new GradleParser().Parse("build.gradle", script);
        Assert.Contains(s.Dependencies, d => d.Name == "org.koin:koin-android");
    }

    [Fact]
    public void Julia_crystal_nim_cpan_parse_deps()
    {
        var jl = new JuliaProjectParser().Parse("Project.toml",
            "name = \"X\"\n[deps]\nFlux = \"abc\"\nDataFrames = \"def\"\n");
        Assert.Equal("julia", jl.Ecosystem);
        Assert.Contains(jl.Dependencies, d => d.Name == "Flux");

        var cr = new CrystalShardParser().Parse("shard.yml",
            "name: app\ndependencies:\n  kemal:\n    github: kemalcr/kemal\n");
        Assert.Equal("shards", cr.Ecosystem);
        Assert.Contains(cr.Dependencies, d => d.Name == "kemal");

        var nim = new NimbleParser().Parse("app.nimble", "requires \"nim >= 1.6\"\nrequires \"jester\"\n");
        Assert.Contains(nim.Dependencies, d => d.Name == "jester");
        Assert.DoesNotContain(nim.Dependencies, d => d.Name == "nim");

        var cpan = new CpanfileParser().Parse("cpanfile", "requires 'Catalyst::Runtime', '5.9';\n");
        Assert.Contains(cpan.Dependencies, d => d.Name == "Catalyst::Runtime");
    }

    [Fact]
    public void Zig_zon_captures_multiple_dependencies()
    {
        // Brace-matched so a multi-dependency block is captured whole.
        var zon = """
        .{
          .name = "x",
          .dependencies = .{
            .raylib = .{ .url = "a", .hash = "b" },
            .mach = .{ .url = "c" },
          },
          .paths = .{""},
        }
        """;
        var z = new ZigZonParser().Parse("build.zig.zon", zon);
        Assert.Equal("zig", z.Ecosystem);
        Assert.Contains(z.Dependencies, d => d.Name == "raylib");
        Assert.Contains(z.Dependencies, d => d.Name == "mach");
    }

    [Fact]
    public void Swift_rebar_opam_cabal_parse_deps()
    {
        var sw = new SwiftPackageParser().Parse("Package.swift",
            ".package(url: \"https://github.com/vapor/vapor.git\", from: \"4.0.0\")");
        Assert.Equal("swiftpm", sw.Ecosystem);
        Assert.Contains(sw.Dependencies, d => d.Name == "vapor");

        var rebar = new RebarConfigParser().Parse("rebar.config", "{deps, [\n  {cowboy, \"2.9\"},\n  ranch\n]}.\n");
        Assert.Equal("rebar", rebar.Ecosystem);
        Assert.Contains(rebar.Dependencies, d => d.Name == "cowboy");

        var opam = new OpamParser().Parse("x.opam", "depends: [ \"ocaml\" \"dream\" {>= \"1.0\"} \"lwt\" ]\n");
        Assert.Contains(opam.Dependencies, d => d.Name == "dream");
        Assert.DoesNotContain(opam.Dependencies, d => d.Name == "ocaml");

        // package.yaml dependencies may sit at column 0 (valid YAML block sequence).
        var hs = new CabalParser().Parse("package.yaml", "name: app\ndependencies:\n- base\n- warp\n");
        Assert.Equal("hackage", hs.Ecosystem);
        Assert.Contains(hs.Dependencies, d => d.Name == "warp");
        Assert.DoesNotContain(hs.Dependencies, d => d.Name == "base");
    }

    [Fact]
    public void Cabal_build_depends_stops_at_following_stanza_fields()
    {
        // The multi-line build-depends must not absorb following fields
        // (hs-source-dirs / ghc-options / default-language) as dependencies.
        var cabal = "name: app\nlibrary\n" +
                    "  build-depends:    base >= 4.7 && < 5\n" +
                    "                  , yesod\n" +
                    "                  , persistent-postgresql\n" +
                    "  hs-source-dirs:   src\n" +
                    "  ghc-options:      -Wall\n" +
                    "  default-language: Haskell2010\n";
        var m = new CabalParser().Parse("app.cabal", cabal);
        var names = m.Dependencies.Select(d => d.Name).ToHashSet();
        Assert.Contains("yesod", names);
        Assert.Contains("persistent-postgresql", names);
        Assert.DoesNotContain("base", names); // excluded by Add()
        Assert.DoesNotContain("hs-source-dirs", names);
        Assert.DoesNotContain("ghc-options", names);
        Assert.DoesNotContain("default-language", names);
    }

    [Fact]
    public void Opam_excludes_version_constraints()
    {
        var opam = new OpamParser().Parse("x.opam", "depends: [ \"ocaml\" {>= \"4.08\"} \"dream\" {>= \"1.0.0\"} \"lwt\" ]\n");
        var names = opam.Dependencies.Select(d => d.Name).ToHashSet();
        Assert.Contains("dream", names);
        Assert.Contains("lwt", names);
        Assert.DoesNotContain("4.08", names);  // version literal inside {…}
        Assert.DoesNotContain("1.0.0", names);
    }

    [Fact]
    public void Rebar_takes_only_leading_atom_of_each_element()
    {
        // A git source tuple must yield only the package atom, not git/branch/https.
        var rebar = new RebarConfigParser().Parse("rebar.config",
            "{deps, [\n  {cowboy, \"2.9.0\"},\n  {jsx, {git, \"https://github.com/x/jsx.git\", {branch, \"main\"}}},\n  ranch\n]}.\n");
        var names = rebar.Dependencies.Select(d => d.Name).ToHashSet();
        Assert.Equal(new HashSet<string> { "cowboy", "jsx", "ranch" }, names);
    }

    [Fact]
    public void Gradle_build_script_scopes_test_dependencies_as_dev()
    {
        var gradle = "dependencies {\n" +
                     "  implementation \"org.springframework.boot:spring-boot-starter\"\n" +
                     "  testImplementation \"org.junit.jupiter:junit-jupiter\"\n" +
                     "}\n";
        var m = new GradleParser().Parse("build.gradle", gradle);
        Assert.Contains(m.Dependencies, d => d.Name == "org.springframework.boot:spring-boot-starter" && d.Scope == DependencyScope.Runtime);
        Assert.Contains(m.Dependencies, d => d.Name == "org.junit.jupiter:junit-jupiter" && d.Scope == DependencyScope.Dev);
    }

    [Fact]
    public void Registry_resolves_community_parsers()
    {
        var reg = new ManifestParserRegistry();
        Assert.IsType<JuliaProjectParser>(reg.Resolve("Project.toml"));
        Assert.IsType<CrystalShardParser>(reg.Resolve("shard.yml"));
        Assert.IsType<NimbleParser>(reg.Resolve("app.nimble"));
        Assert.IsType<CpanfileParser>(reg.Resolve("cpanfile"));
        Assert.IsType<RebarConfigParser>(reg.Resolve("rebar.config"));
        Assert.IsType<OpamParser>(reg.Resolve("dune-project"));
        Assert.IsType<SwiftPackageParser>(reg.Resolve("Package.swift"));
        Assert.IsType<ZigZonParser>(reg.Resolve("build.zig.zon"));
        Assert.IsType<CabalParser>(reg.Resolve("app.cabal"));
        Assert.IsType<CabalParser>(reg.Resolve("package.yaml"));
        Assert.IsType<GradleParser>(reg.Resolve("gradle/libs.versions.toml".Split('/')[^1]));
    }
}
