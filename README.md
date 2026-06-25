# Ivy.StackAnalyzer

A **deterministic, signal-rich** repository analyzer. Point it at a folder and it
produces a structured report — languages, components, frameworks, infrastructure —
designed to give an LLM everything it needs to reason about an application's
*significant layers* (frontend / backend / mobile / infra).

> **The tool reports facts and structure only.** It never labels a component
> "frontend" or decides which layers matter. That judgment is left to the LLM (or
> human) that consumes the report. There is deliberately no `LayerHint` anywhere.

## Why

Most stack detectors bake in opinions. This one is built around a single rule:

> **Adding a new language or framework = adding a data entry, not writing code.**

Language detection is seeded from [github-linguist][linguist]; technology detection
is seeded from [specfy/stack-analyser][specfy]. Both are data files shipped as
embedded resources, and both are extensible at runtime via `--rules`.

## Install

As a **.NET tool** (published to nuget.org):

```bash
dotnet tool install -g ivy-stack-analyzer
ivy-stack-analyzer ./my-repo
```

As a **library** (NuGet package `Ivy.StackAnalyzer`):

```bash
dotnet add package Ivy.StackAnalyzer
```

### Build from source

Requires the **.NET 10 SDK**.

```bash
dotnet build Ivy.StackAnalyzer.slnx
dotnet test Ivy.StackAnalyzer.slnx
```

## Usage

```bash
ivy-stack-analyzer <path> [options]
  -o, --output  yaml | json     # default: yaml
  --out <file>                  # write to a file instead of stdout
  --rules <dir>                 # extra detector / language definitions (repeatable)
  --include-vendored            # include vendored / generated directories
  --no-gitignore                # ignore .gitignore while walking
```

```bash
dotnet run --project src/Ivy.StackAnalyzer.Console -- ./my-repo
```

### Example output (abridged)

```yaml
summary:
  totalFiles: 8
  analyzedFiles: 8
  primaryLanguages: [TypeScript, C#]
  componentCount: 3
components:
  - relativePath: apps/web
    technologies:
      - { name: Next.js, category: framework, confidence: high, evidence: "npm dep 'next'; next.config.mjs" }
      - { name: Tailwind, category: styling, confidence: high, evidence: "npm dep 'tailwindcss'" }
  - relativePath: src/Api
    technologies:
      - { name: ASP.NET Core, category: framework, confidence: high, evidence: "Sdk=Microsoft.NET.Sdk.Web" }
      - { name: Entity Framework Core, category: orm, confidence: high }
infrastructure:
  - { kind: Postgres, category: database, evidence: "docker-compose image 'Postgres'" }
```

Note: `react` is reported as superseded by `Next.js` (dominant-framework ranking),
so it does not appear separately.

## Library API

```csharp
using Ivy.StackAnalyzer;

var detection = new Analyzer().Analyze("path/to/repo");
foreach (var component in detection.Components)
    Console.WriteLine($"{component.RelativePath}: {string.Join(", ", component.Technologies.Select(t => t.Name))}");
```

See `AnalyzerOptions` for configuration (`IncludeVendored`, `RespectGitignore`,
`MaxReadmeLines`, `MaxDependenciesPerManifest`, `AdditionalRuleDirectories`).

## How it works

A seven-stage pipeline (`Pipeline.cs`):

1. **Walk** — `FileSystemScanner` enumerates files, pruning vendored/generated dirs
   (`data/vendor.yml`) and honoring `.gitignore`.
2. **Classify** — `LanguageClassifier` resolves each file's language via
   `data/languages.yml` (filename → extension → shebang). Extensions shared by
   several languages (`.m`, `.pl`, `.v`, `.h`, …) are disambiguated by content using
   a github-linguist-style heuristics layer (`data/heuristics.yml`, `Heuristics.cs`).
3. **Componentize** — `ComponentDetector` turns manifest-bearing directories into
   components, attributes every file to its nearest-ancestor root, and flags
   monorepo aggregators (`IsWorkspaceRoot`) and auxiliary subtrees (`IsAuxiliary`).
4. **Parse** — `IManifestParser` registry extracts dependencies (npm, NuGet, PyPI,
   Go, Cargo, Maven/Gradle — incl. `libs.versions.toml` catalogs, Composer, RubyGems,
   Pub, Hex, plus Julia, Crystal, Nim, CPAN, Rebar, opam/dune, Swift PM, Zig, Cabal).
5. **Detect** — `RuleEngine` matches `data/detectors/*.yml` against each component,
   applying `supersedes` ranking; `ITechnologyDetector` is the code escape hatch.
6. **Infra** — `InfraScanner` surfaces Docker, Compose, Kubernetes, Helm,
   Terraform, CI, and databases declared in compose files.
7. **Assemble** — into the `StackDetection` report.

## Extending

| To add… | Do this |
|---|---|
| A language | Add an entry to a `languages.yml` in a `--rules` directory |
| A framework / library | Add a rule to `detectors/*.yml` in a `--rules` directory |
| An ecosystem | Implement `IManifestParser` and register it |
| Bespoke logic | Implement `ITechnologyDetector` |

The `MatchSpec` rule schema is a **superset** of specfy's format, adding `sdk`
(MSBuild SDK attribute), `depPrefix`, `pathGlobs`, `supersedes`, and `contentRegex`
(a regex matched against a bounded sample of a component's source text — the escape
hatch for technologies whose only on-disk signal lives in source code, e.g. a C
`#include` or a Swift `import`).

## Attribution & licensing

This project derives data from two MIT-licensed projects:

- **[github-linguist][linguist]** — `data/languages.yml` and `data/vendor.yml`.
- **[specfy/stack-analyser][specfy]** — `data/detectors/*.yml`.

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Ivy.StackAnalyzer itself is
licensed under the terms in [LICENSE](LICENSE).

[linguist]: https://github.com/github-linguist/linguist
[specfy]: https://github.com/specfy/stack-analyser
