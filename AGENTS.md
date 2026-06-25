# AGENTS.md

Guidance for AI coding agents working in this repository.

## What this is

**Ivy.StackAnalyzer** is a deterministic, signal-rich repository analyzer. Point it
at a folder and it produces a structured report — languages, components, frameworks,
infrastructure — designed to give an LLM everything it needs to reason about an
application's significant layers (frontend / backend / mobile / infra).

It reports **facts and structure only**. It never labels a component "frontend" or
decides which layers matter — that judgment is left to the consumer. There is
deliberately no `LayerHint` anywhere.

## Golden rule

> **Adding a language or framework is a data entry, not a code change.**

Detection knowledge lives in `src/Ivy.StackAnalyzer/data/` (embedded YAML). Prefer a
data edit over code. Only touch C# when a genuinely new *capability* is needed (a new
manifest ecosystem, a new match facet, bespoke logic).

## Layout

| Path | What |
|---|---|
| `src/Ivy.StackAnalyzer/` | Core library (the analyzer) |
| `src/Ivy.StackAnalyzer.Console/` | CLI, packaged as the `ivy-stack-analyzer` .NET tool |
| `src/Ivy.StackAnalyzer.Test/` | xUnit tests |
| `src/Ivy.StackAnalyzer/data/detectors/*.yml` | Technology detector rules (by category) |
| `src/Ivy.StackAnalyzer/data/languages.yml` | Language defs (from github-linguist) |
| `src/Ivy.StackAnalyzer/data/heuristics.yml` | Ambiguous-extension content heuristics (linguist port) |
| `src/Ivy.StackAnalyzer/data/{vendor,documentation,infra}.yml` | Vendoring, doc paths, infra signals |

The pipeline (`Pipeline.cs`): **Walk → Classify → Componentize → Parse → Detect →
Infra → Assemble**.

## Build / test

```bash
dotnet build Ivy.StackAnalyzer.slnx -c Release
dotnet test  Ivy.StackAnalyzer.slnx -c Release
```

Requires the **.NET 10 SDK**. Run the CLI on a repo while iterating:

```bash
dotnet run --project src/Ivy.StackAnalyzer.Console -- <path> [-o yaml|json] [--rules <dir>]
```

`--rules <dir>` overlays extra data (a `detectors/` subdir, plus optional
`languages.yml` / `vendor.yml` / `heuristics.yml`) **without rebuilding** — the fast
way to prototype and validate a new rule against a real repo.

## Detector rule format

A rule in `data/detectors/<category>.yml`:

```yaml
- id: <globally-unique-id>          # must be unique across ALL detector files
  name: <Display Name>
  category: framework               # see categories below
  match:                            # rule fires if ANY facet matches (logical OR)
    deps:        [{ ecosystem: pypi, name: tornado }]   # exact manifest dep
    depsRegex:   [{ ecosystem: npm, pattern: '^@angular/' }]
    depPrefix:   [{ ecosystem: maven, prefix: 'org.springframework.boot:' }]
    files:       [foundry.toml]                          # exact filename anywhere
    filesRegex:  ['(^|/)[^/]+\.uproject$']               # regex over relative path
    extensions:  ['.slx']                                # extension present in component
    sdk:         [Microsoft.NET.Sdk.Web]                 # MSBuild SDK attr (.NET)
    properties:  [{ name: UseWPF, value: 'true' }]       # MSBuild property
    scriptsRegex:['\bbun test\b']                        # package.json scripts values
    contentRegex:['#include\s*<gtk/gtk\.h>']             # regex over sampled source text
  supersedes: [react]               # optional: hide a base framework this subsumes
  confidence: high                  # high | medium | low
```

**Parsed dependency ecosystems** (yield `deps`): `npm`, `nuget` (csproj + paket),
`pypi`, `go`, `cargo`, `maven` (Maven + Gradle, incl. `libs.versions.toml`),
`composer`, `rubygems`, `pub`, `hex`, plus the community parsers `julia`, `shards`,
`nimble`, `cpan`, `rebar`, `opam`, `swiftpm`, `zig`, `hackage`. `docker` / `terraform`
/ `github` come from infra. Any new ecosystem string must be added to the allowlist in
`EcosystemConsistencyTests` or that test fails.

**`contentRegex`** scans a bounded, lazily-loaded sample of a component's source text.
It is the escape hatch for technologies whose only on-disk signal is in code (a C
`#include`, a Swift `import`). A content match is *direct, high-confidence* evidence,
so the pattern MUST be unambiguous and anchored — a loose pattern is a false positive.
These regexes run with `IgnoreCase`; anchor line matches with `(?m)^`.

## Conventions & guardrails

- **Categories.** Only `framework`, `database`, `orm` enter the downstream "stack
  hash", so categorize precisely. Use `library` generously for HTTP clients, state
  managers, DI, icon sets, arg parsers; `testing`/`build`/`tool`/`packagemanager` for
  those. Mis-elevating a library into `framework` is a real defect.
- **Avoid false positives.** A wrong detection is worse than a miss. Prefer a parsed
  manifest dep over a file marker over a content regex. Never add a bare, generic
  extension (`.egg`, `.fig`) as the sole facet of a hash-slot rule.
- **YAML regex must be single-quoted** (`'\.ui$'`). A double-quoted `"\.ui$"` is
  invalid YAML and silently drops the *entire* file's rules. After any data edit,
  rebuild + run the CLI and confirm rules still fire (and that `rulesLoaded` in
  metadata didn't drop — the `Detector_rule_count_has_not_collapsed` test guards this).
- **Unique ids.** Duplicate detector ids fail `Detector_rule_ids_are_unique`.
- **Snapshot tests** (`AnalyzerSnapshotTests`) scrub volatile metadata
  (`rulesLoaded`, `durationMs`, …) so they don't churn on every data add. If a fixture
  snapshot legitimately changes, update the `.verified.yaml`.

## Extending capabilities (code)

| To add… | Do this |
|---|---|
| A language | Add an entry to `languages.yml` (or a `--rules` `languages.yml`) |
| A framework / library | Add a rule to `detectors/*.yml` |
| An ambiguous-extension disambiguation | Add a block to `heuristics.yml` |
| A manifest ecosystem | Implement `IManifestParser`, register it in `ManifestParserRegistry`, allowlist the ecosystem in `EcosystemConsistencyTests` |
| Bespoke logic | Implement `ITechnologyDetector` |

Match the surrounding code's style and comment density. Keep changes deterministic —
no `Date.now()`/`Math.random()` style nondeterminism in the analysis path.

## Data provenance

`languages.yml`, `vendor.yml`, and `heuristics.yml` derive from
[github-linguist](https://github.com/github-linguist/linguist) (MIT); detector rules
are seeded from [specfy/stack-analyser](https://github.com/specfy/stack-analyser)
(MIT). See `THIRD-PARTY-NOTICES.md` when adding or modifying derived data.
