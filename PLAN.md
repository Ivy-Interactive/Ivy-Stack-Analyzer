# Ivy.StackAnalyzer — Implementation Plan

## 1. Goal & philosophy

Produce a **deterministic, signal-rich report** of a repository on disk that gives an
LLM everything it needs to identify the *significant layers* of an application
(frontend / backend / mobile / infra), their **languages**, and their **major
frameworks** — **without the tool itself making the final semantic call**.

The tool reports **facts and structure only**. It never labels a component
"frontend" or decides which layers matter. That judgment is left entirely to the
LLM that consumes the report.

### Influences (both MIT licensed — attributed in README)
- **[github-linguist](https://github.com/github-linguist/linguist)** — language
  detection (extension / filename / shebang rules from a data file) and
  vendored / generated / documentation classification so "primary language" math
  isn't polluted. We **seed `languages.yml` directly from linguist**.
- **[specfy/stack-analyser](https://github.com/specfy/stack-analyser)** —
  technology detection from manifests / dependencies / config files, organized by
  category. We **seed our technology detectors from its ~700 `register({...})`
  rules** (MIT, verified).

### The one non-negotiable design driver
Adding a new language or framework = **adding a data entry, not writing code.**

---

## 2. Solution layout

```
Ivy-Stack-Analyzer/
├─ Ivy.StackAnalyzer.slnx          # XML solution format (dotnet 10)
├─ global.json                      # pin SDK 10.0.x, rollForward latestFeature
├─ Directory.Build.props            # net10.0, Nullable, ImplicitUsings, LangVersion latest
├─ Directory.Packages.props         # Central Package Management (all versions here)
├─ .editorconfig
├─ README.md
└─ src/
   ├─ Ivy.StackAnalyzer/                       # core class library
   │  └─ Ivy.StackAnalyzer.csproj
   ├─ Ivy.StackAnalyzer.Console/               # Spectre.Console.Cli app
   │  └─ Ivy.StackAnalyzer.Console.csproj
   └─ Ivy.StackAnalyzer.Test/                  # xUnit v3
      └─ Ivy.StackAnalyzer.Test.csproj
```

> **Note:** the original brief listed the core csproj as
> `Ivy.StackAnalyzer.Console.csproj` under `src\Ivy.StackAnalyzer\` — treated as a
> typo; the core library's project file is `Ivy.StackAnalyzer.csproj`.
>
> **Spelling:** "Analyzer" is spelled with a *z* consistently — repo, projects,
> namespaces, the CLI (`ivy-stack-analyzer`), and the entry class `Analyzer` in
> `Analyzer.cs`. (The upstream `specfy/stack-analyser` keeps its own British *s*.)

**Project references:** Console → Core; Test → Core (+ Console for one end-to-end CLI test).

### Packages (Central Package Management)
| Package | Used by | Purpose |
|---|---|---|
| `Spectre.Console.Cli` | Console | CLI framework |
| `YamlDotNet` | Core, Console | YAML output **and** loading the language / detector data files |
| `Tomlyn` | Core | parse `Cargo.toml`, `pyproject.toml` |
| `System.Text.Json` (BCL) | Core | parse `package.json`, `composer.json` |
| `System.Xml.Linq` (BCL) | Core | parse `*.csproj`, `pom.xml` |
| `xunit.v3`, `Microsoft.NET.Test.Sdk`, `Microsoft.Testing.Platform` | Test | testing |
| `Verify.XunitV3` | Test | snapshot-test analyzer output against fixture repos |

`slnx` is created with `dotnet new sln --format slnx`, then
`dotnet sln Ivy.StackAnalyzer.slnx add src/**/*.csproj`.

---

## 3. Core public API

```csharp
namespace Ivy.StackAnalyzer;

public sealed class Analyzer
{
    public Analyzer(AnalyzerOptions? options = null);

    public StackDetection Analyze(string repoPath);
    public Task<StackDetection> AnalyzeAsync(string repoPath, CancellationToken ct = default);
}

public sealed record AnalyzerOptions
{
    public bool IncludeVendored { get; init; } = false;
    public int MaxReadmeLines { get; init; } = 120;
    public int MaxDependenciesPerManifest { get; init; } = 100;
    public IReadOnlyList<string> AdditionalRuleDirectories { get; init; } = [];  // user extensibility
    public bool RespectGitignore { get; init; } = true;
}
```

---

## 4. The `StackDetection` record (the report)

> No `LayerHint` anywhere. The report is pure facts and structure.

```csharp
public sealed record StackDetection
{
    public required string RepoPath { get; init; }
    public required RepoSummary Summary { get; init; }
    public required IReadOnlyList<LanguageStat> Languages { get; init; }       // repo-wide, vendored excluded
    public required IReadOnlyList<Component> Components { get; init; }         // per project root
    public required IReadOnlyList<DetectedTechnology> Technologies { get; init; } // flattened, deduped
    public required IReadOnlyList<InfraSignal> Infrastructure { get; init; }
    public ReadmeExcerpt? Readme { get; init; }
    public required AnalyzerMetadata Metadata { get; init; }
}

public sealed record Component                       // a manifest-bearing subtree
{
    public required string RelativePath { get; init; }            // "apps/web", "src/Api", "."
    public required IReadOnlyList<LanguageStat> Languages { get; init; }   // subtree-local footprint
    public required IReadOnlyList<ManifestFile> Manifests { get; init; }
    public required IReadOnlyList<DetectedTechnology> Technologies { get; init; }
    public int FileCount { get; init; }
    public long SizeBytes { get; init; }
    public bool IsWorkspaceRoot { get; init; }                    // factual: aggregator, not a leaf component
    public bool IsAuxiliary { get; init; }                        // factual: under test/example/fixture/sample
}

public sealed record LanguageStat(string Name, LanguageType Type, int Files, long Bytes, double Percent);
public sealed record ManifestFile(string Path, string Ecosystem, IReadOnlyList<Dependency> Dependencies);
public sealed record Dependency(string Name, string? Version, DependencyScope Scope);
public sealed record DetectedTechnology(string Name, TechCategory Category, string Evidence,
                                        Confidence Confidence, string? ComponentPath);
public sealed record InfraSignal(string Kind, TechCategory Category, IReadOnlyList<string> Files, string? Evidence);
public sealed record ReadmeExcerpt(string Path, string Excerpt);
public sealed record AnalyzerMetadata(string AnalyzerVersion, long DurationMs, int RulesLoaded,
                                      int LanguageDefsLoaded, IReadOnlyList<string> IgnoredDirectories);

public enum LanguageType { Programming, Markup, Data, Prose }
public enum TechCategory { Language, Framework, Library, Runtime, Database, Orm, Styling, Build, Ci,
                           Cloud, Messaging, Testing, PackageManager, Ai, Hosting, Analytics }
public enum DependencyScope { Runtime, Dev, Peer, Optional }
public enum Confidence { Low, Medium, High }
```

---

## 5. The analysis pipeline (inside `Analyzer.Analyze`)

```
1. Walk         → FileSystemScanner: enumerate files, apply ignore rules
2. Classify     → LanguageClassifier: per-file language via languages.yml; tag vendored/generated
3. Componentize → ComponentDetector: find manifests → component roots; assign each file to nearest root
4. Parse        → IManifestParser registry: extract dependencies per manifest
5. Detect       → RuleEngine + ITechnologyDetector: match rules → DetectedTechnology (+ evidence)
6. Infra        → InfraScanner: Docker / compose / k8s / terraform / CI / detected DBs
7. Assemble     → StackDetection
```

---

## 6. Component detection (step 3 — the structural backbone)

With `LayerHint` removed, the **`Component` map is the primary signal the LLM uses**
to infer frontend / backend. Algorithm:

1. **Collect manifest files during the walk** — `package.json`, `*.csproj` /
   `*.fsproj`, `go.mod`, `Cargo.toml`, `pyproject.toml` / `requirements.txt`,
   `pom.xml`, `build.gradle(.kts)`, `composer.json`, `Gemfile`, `pubspec.yaml`,
   `mix.exs`. Each manifest's **containing directory** is a candidate component
   root. Vendored dirs are excluded *first*, so `node_modules/x/package.json` is
   never a component.

2. **Resolve monorepo aggregators** so we surface real components, not just the root:
   - npm / pnpm / yarn **workspaces** (`package.json` `workspaces`,
     `pnpm-workspace.yaml`) → root flagged `IsWorkspaceRoot`; members are components.
   - **.NET** `.sln` / `.slnx` → each referenced `.csproj` dir is a component.
   - Cargo `[workspace] members`, Go `go.work`, Gradle `settings.gradle`,
     Nx / Turbo (`turbo.json`).

3. **Dedup per directory** — a dir with `package.json` *and* a `Dockerfile` is
   **one** component carrying multiple manifests.

4. **Attribute every file to its nearest-ancestor component root**
   (longest path-prefix match). This prevents a parent component from swallowing
   files that belong to a nested child, keeping `apps/web` (TypeScript) and
   `src/Api` (C#) cleanly separated. Files above any root fall to the
   **repo-root component**, which always exists as a fallback.

5. **Compute per component**: language footprint (counts + bytes), manifests +
   parsed dependencies, and technologies (rule engine scoped to that component's
   deps / files).

**Edge handling (factual flags, never judgments):**
- A directory with config but **no dependency manifest** (just a `Dockerfile`) is
  not a component — it becomes an `InfraSignal`.
- Components under `test/`, `examples/`, `fixtures/`, `samples/` get
  `IsAuxiliary = true` so the LLM *can* discount them — but we never decide they're
  insignificant.

---

## 7. Extensibility — adding languages & frameworks

Data-first, with a code escape hatch.

### 7a. Languages — `data/languages.yml` (seeded from linguist, embedded resource)
```yaml
TypeScript: { type: programming, extensions: [".ts", ".tsx"], color: "#3178c6" }
C#:         { type: programming, extensions: [".cs"] }
Go:         { type: programming, extensions: [".go"], filenames: ["go.mod"] }
Shell:      { type: programming, extensions: [".sh"], interpreters: ["bash", "sh"] }
```
Seeded directly from linguist's `languages.yml` (600+ languages). Users add entries
via `AdditionalRuleDirectories` with no recompile.

### 7b. Technologies — `data/detectors/*.yml` (seeded from specfy, embedded resource)
specfy rules look like:
```ts
register({
  tech: 'nextjs', name: 'Next.js', type: 'framework',
  files: ['next.config.js', 'next.config.mjs'],
  dependencies: [{ type: 'npm', name: 'next' }],
});
```
Transformed (one-time importer, **not** shipped at runtime) into our schema:
```yaml
- id: nextjs
  name: Next.js
  category: framework
  match:
    ecosystem: npm
    dependencyAny: [next, "@netlify/plugin-nextjs"]
    filesAny: ["next.config.js", "next.config.mjs", "next.config.ts"]
  supersedes: [react]          # Next.js outranks bare React as dominant framework
  confidence: high

- id: aspnetcore
  name: ASP.NET Core
  category: framework
  match:
    sdkAny: ["Microsoft.NET.Sdk.Web"]            # csproj Sdk attribute (our superset)
    dependencyPrefixAny: ["Microsoft.AspNetCore."]
  confidence: high
```

**specfy → ours field mapping:**

| specfy | ours |
|---|---|
| `type` | `category` |
| `dependencies: [{type, name}]` | `match.dependencyAny`, grouped by `ecosystem` |
| `files` | `match.filesAny` |
| `extensions` | merged into `languages.yml` instead |
| `name` | `name` |

`MatchSpec` is a **superset** of specfy's format, adding `sdkAny`,
`dependencyPrefixAny`, `pathGlobs`, and `supersedes` (dominant-framework ranking)
for cases specfy can't express (e.g. `Microsoft.NET.Sdk.Web`).

### 7c. Code escape hatch — `ITechnologyDetector`
```csharp
public interface ITechnologyDetector
{
    IEnumerable<DetectedTechnology> Detect(ComponentContext ctx);   // ctx: files, manifests, deps, langs
}
```
Discovered via a simple registry (no heavy DI container).

### 7d. Manifest parsers — `IManifestParser`
```csharp
public interface IManifestParser
{
    bool CanParse(string fileName);                  // "package.json", "*.csproj", ...
    ManifestFile Parse(string path, string content);
}
```
Built-in: npm (`package.json` + workspaces), NuGet (`*.csproj` / `*.fsproj`
PackageReference + `Directory.Packages.props`), PyPI (`pyproject.toml` /
`requirements.txt` / `Pipfile`), `go.mod`, `Cargo.toml`, Maven (`pom.xml`),
Gradle (`build.gradle(.kts)`), Composer, Gemfile, `pubspec.yaml`, `mix.exs`.
Add an ecosystem = add one `IManifestParser`.

### 7e. Ignore / vendoring — `data/vendor.yml` (seeded from linguist)
Glob list of vendored / generated dirs (`node_modules`, `dist`, `bin`, `obj`,
`.venv`, `vendor`, `target`, `.git`, …), editable as data; `.gitignore` honored
when `RespectGitignore` is on.

---

## 8. Console app (Spectre.Console.Cli)

```
ivy-stack analyze <path> [options]
  --output, -o   yaml | json         (default: yaml)
  --out <file>                       write to file instead of stdout
  --rules <dir>                      extra detector / language definitions
  --include-vendored
  --no-gitignore
```
`CommandApp<AnalyzeCommand>` with a typed `Settings` class. Default serializes
`StackDetection` to **YAML via YamlDotNet** (camelCase, enums as strings, nulls
omitted). `--output json` reuses `System.Text.Json` for tooling.

### Example output
```yaml
repoPath: D:\Repos\sample
summary:
  totalFiles: 1240
  analyzedFiles: 980
  primaryLanguages: [TypeScript, C#]
  componentCount: 2
languages:
  - { name: TypeScript, type: programming, files: 412, bytes: 1840221, percent: 58.3 }
  - { name: C#,         type: programming, files: 233, bytes:  980112, percent: 31.1 }
components:
  - path: apps/web
    languages: [{ name: TypeScript, percent: 92.1, files: 412 }]
    manifests:
      - path: apps/web/package.json
        ecosystem: npm
        dependencies:
          - { name: next,  version: "14.2.0", scope: runtime }
          - { name: react, version: "18.3.0", scope: runtime }
    technologies:
      - { name: Next.js, category: framework, confidence: high, evidence: "npm dep 'next'; next.config.mjs" }
      - { name: Tailwind CSS, category: styling, confidence: high, evidence: "tailwind.config.ts" }
  - path: src/Api
    languages: [{ name: C#, percent: 96.0, files: 200 }]
    manifests:
      - path: src/Api/Api.csproj
        ecosystem: nuget
        dependencies:
          - { name: Microsoft.AspNetCore.OpenApi, version: "10.0.0", scope: runtime }
    technologies:
      - { name: ASP.NET Core, category: framework, confidence: high, evidence: "Sdk=Microsoft.NET.Sdk.Web" }
infrastructure:
  - { kind: Docker, category: build, files: [apps/web/Dockerfile, src/Api/Dockerfile] }
  - { kind: Postgres, category: database, evidence: "docker-compose.yml service 'postgres:16'" }
metadata: { analyzerVersion: 0.1.0, durationMs: 142, rulesLoaded: 318, languageDefsLoaded: 600 }
```

---

## 9. Testing strategy (xUnit v3)

- **Unit:** each `IManifestParser` and the `RuleEngine` against small inline strings.
- **LanguageClassifier:** extension / shebang / filename resolution tables.
- **Fixture mini-repos:** committed under `Test/Fixtures/` (e.g.
  `next-dotnet-monorepo`, `django-spa`, `go-service`, `numpy-style-lib`). Run
  `Analyzer` → **`Verify` snapshot** the YAML. Adding a framework = add a fixture +
  accept the snapshot.
- **End-to-end:** invoke the Console `CommandApp` in-process, assert YAML round-trips.

---

## 10. Milestones

| # | Milestone | Deliverable |
|---|-----------|-------------|
| M0 | Scaffold | slnx, 3 projects, CPM, global.json, Directory.Build.props, green build |
| M1 | Walk + languages | `FileSystemScanner`, `LanguageClassifier`, linguist-seeded `vendor.yml` + `languages.yml`, repo-wide stats |
| M2 | Components + manifests | `ComponentDetector`, parser registry, per-component footprints |
| M3 | Detection engine | `RuleEngine`, specfy-seeded `data/detectors/*.yml`, `ITechnologyDetector`, supersedes ranking |
| M4 | Infra + readme | `InfraScanner`, README excerpt, full `StackDetection` |
| M5 | Console | Spectre `analyze` command, YAML / JSON serialization |
| M6 | Tests | parsers, classifier, fixtures + Verify snapshots, e2e |
| M7 | Seeders | **one-shot** importers (run once, discarded): linguist → `languages.yml` / `vendor.yml`; specfy → `detectors/*.yml`. Generated data files are committed; the scripts are not kept in the repo. |

---

## 11. Licensing / attribution

- **github-linguist** — MIT. `languages.yml` and `vendor.yml` derived from it.
- **specfy/stack-analyser** — MIT (verified). `detectors/*.yml` derived from its
  `src/rules/**` `register({...})` definitions.

Both attributions go in `README.md` and a `THIRD-PARTY-NOTICES.md`.

---

## 12. Resolved decisions

1. **`IsAuxiliary` flag** — **kept.** A purely path-derived factual flag set `true`
   when a component lives under `test/`, `tests/`, `examples/`, `fixtures/`,
   `samples/`, `demo/`, `e2e/`, or `docs/`. These dirs frequently contain their own
   manifests (an example app, an e2e project) and so are detected as real
   components, but are usually not part of the app's significant stack. The tool
   surfaces them and tags them; the LLM decides whether to discount them.
2. **Seeder packaging** — **one-shot.** The linguist and specfy importers are run
   once to generate `languages.yml` / `vendor.yml` / `detectors/*.yml`; those data
   files are committed, and the importer scripts are **not** kept in the repo.
