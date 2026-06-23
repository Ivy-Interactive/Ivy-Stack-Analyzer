---
name: review-stack-analyse
description: Clone a repo, independently analyse its stack, run the Ivy.StackAnalyzer CLI plus three reference tools (specfy/stack-analyser, microsoft/component-detection, github-linguist) via one helper script, compare them, and record disagreements as suggestion files. Use when the user types /review-stack-analyse <repo-url> or asks to review/validate the analyser's output against a real repository.
argument-hint: <repo-url>
---

# /review-stack-analyse

You are validating **Ivy.StackAnalyzer** (the CLI in this repository) against a
real-world repository. You compare your own independent agentic analysis to our
CLI and three reference tools, then turn defensible disagreements into actionable
suggestion files.

The repository URL is: **$ARGUMENTS**

If `$ARGUMENTS` is empty, ask the user for a repo URL and stop.

**Token discipline:** do NOT read the raw tool outputs (the big specfy/ivy JSON,
linguist text, CD manifest) into context. The helper script in step 3 distills
them into one compact comparison — read only that. When you later need to quote an
exact CLI value for a suggestion, `grep` the specific field out of the saved
artifact rather than reading the whole file.

## 1. Clone the repo under `D:\Temp\`

- Folder name = the URL's last path segment minus any `.git` (e.g. `…/bar.git` → `bar`).
- Target `D:\Temp\<name>`; if it exists, delete it first for a clean clone.
- `git clone --depth 1 <repo-url> "D:/Temp/<name>"`
- On failure (bad URL, auth, network): report and stop.

## 2. Analyse the repo yourself FIRST (before running any tool)

Do this independently so your judgement isn't anchored. Inspect the clone with
Glob/Grep/Read — be economical (read manifests + a directory listing; don't read
every source file):

- **Manifests/lockfiles** — `package.json`, `*.csproj`/`*.fsproj`, `go.mod`,
  `Cargo.toml`, `pyproject.toml`/`requirements.txt`, `pom.xml`,
  `build.gradle(.kts)`, `composer.json`, `Gemfile`, `pubspec.yaml`, `mix.exs`.
- **Structure** — the real components; monorepo / workspace?
- **Frameworks** — infer from deps and config files (`next.config.*`,
  `tailwind.config.*`, `Sdk="…Web"`, `fastapi`, …).
- **Languages**, **infrastructure** (Docker/compose/k8s/Terraform/CI/DBs),
  **README** intent.

Write a short baseline: primary languages, components + paths, major frameworks
per component, infrastructure. This is your source of truth.

## 3. Run our CLI + all reference tools in one shot

Run the helper script (it builds/runs the Ivy CLI and the three reference tools,
handles every Windows/Git-Bash gotcha, caches downloads in `D:\Temp\_tools\`, and
prints a single normalized comparison):

```
bash ".claude/skills/review-stack-analyse/scripts/analyze.sh" "D:/Temp/<name>" "<name>"
```

Read **only** the compact comparison it prints. It reports, per source, whether
each tool ran or was `UNAVAILABLE`, plus:

- **LANGUAGES** — Ivy vs linguist (linguist needs Docker Desktop running; if it's
  down the script says so and you proceed without it).
- **TECHNOLOGIES** — a union table flagged `I`=ivy / `S`=specfy (names are
  normalized, so "TanStack Router" vs "tanstackrouter" align — those are NOT
  disagreements).
- **POTENTIAL GAPS** — techs specfy found but ivy didn't (your prime candidates).
- **COMPONENTS** (ivy) + **COMPONENT-DETECTION** dependency cross-check.

Raw artifacts are saved as `D:/Temp/_tools/ivy-<name>.json`,
`specfy-<name>.json`, `linguist-<name>.txt`, `cd-<name>/ScanManifest_*.json` for
targeted `grep`s only.

If the Ivy CLI itself failed (`[ivy] FAILED`), inspect `ivy-<name>.err`, report,
and stop. Individual tools failing is fine — note them and continue.

## 4. Compare and record disagreements

Judge each dimension against the right authority:

- **Languages** → linguist (e.g. a lockfile inflating a data language is a
  vendoring gap).
- **Technologies/frameworks** → specfy + your own analysis. Work through
  **POTENTIAL GAPS**, but judge each: some entries (`docker`, `github`, `python`,
  `nodejs`) are things we surface elsewhere (infrastructure, languages) rather
  than as `technologies` — those are **not** disagreements. A gap like `shadcn`
  (a real, unsurfaced tech) **is**.
- **Components/ecosystems** → component-detection + your own analysis. (CD is weak
  on `uv.lock`/`bun.lock`; if it found little there, lean on your analysis.)

A **disagreement** is a concrete, defensible case where our CLI is wrong or
incomplete vs your analysis — ideally corroborated by a reference tool, but a
clear miss your analysis proves (e.g. the repo's headline framework) counts even
if the reference tools also miss it. Ignore cosmetic differences (ordering,
naming style, confidence wording).

For **each** distinct disagreement create `src\.suggestions\<kebab-name>.md`
(relative to THIS repo; create the dir if missing — it is gitignored). Each file:

```markdown
# <Short title>

- **Repo reviewed:** <repo-url>
- **Component / path:** <where, or "repo-wide">

## What our CLI reported
<the relevant slice of the Ivy output, grep'd from ivy-<name>.json>

## What it should be
<your finding + evidence — file names, dependency names, paths>

## Corroborating tools
<e.g. "specfy: shadcn; linguist: TypeScript 70% vs our TOML 48%; CD: n/a".
Note any unavailable tool.>

## Suggested fix
<concrete change: a rule for `data/detectors/<category>.yml` (include the exact
YAML); an entry in `data/languages.yml`; a `data/vendor.yml` pattern; or a code
change. Prefer ready-to-paste snippets.>
```

If there are no disagreements, create no files — say so.

## 5. Report back

- Clone location; which reference tools ran vs were unavailable.
- The compact comparison (or a tight summary of it).
- One-line verdict: does our CLI hold up?
- Bullet list of suggestion files created (path + one-line summary), or "none needed".

Don't commit anything. Leave the clone and `D:\Temp\_tools\` outputs and the
suggestion files for the user to review.
