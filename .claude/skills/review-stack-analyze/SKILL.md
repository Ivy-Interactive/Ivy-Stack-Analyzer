---
name: review-stack-analyze
description: Clone a repo, independently analyze its stack, run the Ivy.StackAnalyzer CLI plus three reference tools (specfy/stack-analyser, microsoft/component-detection, github-linguist) via one helper script, compare them, and record disagreements as suggestion files — including whether the report yields a clean stack hash. Use when the user types /review-stack-analyze <repo-url> or asks to review/validate the analyzer's output against a real repository.
argument-hint: <repo-url>
---

# /review-stack-analyze

You are validating **Ivy.StackAnalyzer** (the CLI in this repository) against a
real-world repository. The analyzer's primary consumer is **/generate-stack-hash**,
so you evaluate it on two axes: (1) is the report factually correct vs your own
analysis and the reference tools, and (2) does it yield a clean, correct **stack
hash** (significant tech only — no noise, no misses). Defensible problems on
either axis become actionable suggestion files.

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

## 2. Analyze the repo yourself FIRST (before running any tool)

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
per component, infrastructure, **databases actually used, and the test framework**.
Then mentally form the stack hash you'd expect (see step 5). This is your truth.

## 3. Run our CLI + all reference tools in one shot

Run the helper script (it builds/runs the Ivy CLI and the three reference tools,
handles every Windows/Git-Bash gotcha, caches downloads in `D:\Temp\_tools\`, and
prints a single normalized comparison):

```
bash ".claude/skills/review-stack-analyze/scripts/analyze.sh" "D:/Temp/<name>" "<name>"
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

## 4. Compare and record disagreements (factual axis)

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

## 5. Stack-hash perspective (does the report yield a good hash?)

The hash captures only the **significant** stack: per significant component its
primary **language** + **framework(s)**, the real **database(s)**, the **test**
framework, and key infra. Derive the hash twice — once from the Ivy report, once
from your own analysis — and compare. (Format: `fe(ts):react+next+tailwind|be(py):fastapi+sqlmodel|db:postgres|test:pytest`,
no spaces; meta-framework implies base, e.g. `react+next`.)

Two hash-specific failure modes — record each as a disagreement (tag it
`## Hash impact`):

- **Hash-breaking miss** — a significant layer framework, the real database, or
  the test framework is absent/wrong in the report, so a hash slot would be empty
  or incorrect. (E.g. FastAPI/SQLModel not detected; the actual Postgres missed.)
  These are the highest priority.
- **Hash-polluting noise** — something surfaced at **high confidence** that is
  *not* a significant choice yet would land in a hash slot: a cache-driver or
  env-only service reported as `database`; a router/util/icon set mis-categorized
  as `framework`/`styling`; a docs-site generator counted as an app framework.
  These wrongly enter the hash.

Do **not** flag: items already at `confidence: low` (the hash drops those, e.g.
env-var placeholders), incidental libraries the hash already ignores, or naming
style. Focus only on what would change the hash.

For **each** distinct disagreement (factual or hash) create
`src\.suggestions\<kebab-name>.md` (relative to THIS repo; create the dir if
missing — it is gitignored). Each file:

```markdown
# <Short title>

- **Repo reviewed:** <repo-url>
- **Component / path:** <where, or "repo-wide">

## What our CLI reported
<the relevant slice of the Ivy output, grep'd from ivy-<name>.json>

## What it should be
<your finding + evidence — file names, dependency names, paths>

## Hash impact
<expected hash vs hash from the report, and which slot is wrong; or "none" for a
purely factual issue that doesn't change the hash>

## Corroborating tools
<e.g. "specfy: shadcn; linguist: TypeScript 70% vs our TOML 48%; CD: n/a".
Note any unavailable tool.>

## Suggested fix
<concrete change: a rule for `data/detectors/<category>.yml` (include the exact
YAML); an entry in `data/languages.yml`; a `data/vendor.yml` pattern; tighter
detection in a parser/scanner; or a confidence/category change. Ready-to-paste.>
```

If there are no disagreements, create no files — say so.

## 6. Report back

- Clone location; which reference tools ran vs were unavailable.
- The compact comparison (or a tight summary of it).
- **The stack hash** derived from the report, and whether it matches the hash from
  your own analysis.
- One-line verdict: does our CLI hold up, and does it yield a correct hash?
- Bullet list of suggestion files created (path + one-line summary), or "none needed".

Don't commit anything. Leave the clone and `D:\Temp\_tools\` outputs and the
suggestion files for the user to review.
