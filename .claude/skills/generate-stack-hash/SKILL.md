---
name: generate-stack-hash
description: Generate a one-line, similarity-preserving Stack Descriptor Hash (SDH) for a repository. Clones the repo, runs the Ivy.StackAnalyzer CLI to get a factual stack report, then maps it to the canonical hash. Use when the user types /generate-stack-hash <repo-url> or asks for a stack hash / stack signature of a repo.
argument-hint: <repo-url>
---

# /generate-stack-hash

Produce a single-line **Stack Descriptor Hash (SDH)** for a repository: a
canonical, human-readable string capturing the significant layers, the
frameworks that build each layer, and the test technology. Same repo → identical
hash; similar stacks → similar strings.

The repository URL is: **$ARGUMENTS**

If `$ARGUMENTS` is empty, ask the user for a repo URL and stop.

## 1. Get the repo under `D:\Temp\`

- Folder name = the URL's last path segment minus any `.git`.
- If `D:\Temp\<name>` already exists, reuse it. Otherwise shallow-clone:
  `git clone --depth 1 <repo-url> "D:/Temp/<name>"`
- On clone failure (bad URL/auth/network): report and stop.

## 2. Run the analyzer (this is the hash input)

From this repository's root (`D:\Repos\_Ivy\Ivy-Stack-Analyser`):

```
dotnet run --project src/Ivy.StackAnalyzer.Console -- "D:/Temp/<name>" -o yaml
```

Read the report. The parts that matter for the hash are:
- `components[]` — each with `relativePath`, `languages`, `technologies`
  (`name` + `category`), and the `isAuxiliary` / `isWorkspaceRoot` flags.
- top-level `technologies[]` and `languages[]`, and `infrastructure[]`.

Ignore components flagged `isAuxiliary: true` and pure workspace-root
aggregators. Use each component's dominant language + its `framework`/`orm`/
`database`/`styling`/`testing`-category technologies.

**Ignore any technology with `confidence: low`** — these are weak signals (e.g.
env-var placeholders from `.env.example`) and must never enter the hash.

> Note: the analyzer applies `supersedes`, so a base framework may be hidden by
> its meta-framework (e.g. it reports **Next.js** but not React). When deriving
> the hash you must still emit the base per Rule 4 below (`react+next`).

## 3. Derive the hash (apply the SDH spec exactly)

You convert the report into ONE line. It is deterministic and
similarity-preserving. **The output contains no spaces anywhere.**

### Grammar
```
hash    = segment ( "|" segment )*
segment = role [ "(" lang ")" ] ":" token ( "+" token )*
role    = "fe" | "mobile" | "desktop" | "be" | "fs" | "lib" | "db" | "infra" | "test"
lang    = canonical language slug (omit for db/infra/test)
token   = canonical technology slug
```

### Rule 1 — Fixed layer order (omit absent layers)
```
fe, mobile, desktop, be, fs, lib, db, infra, test
```
Use `fs` only when a single framework genuinely spans client+server (Rails,
Laravel, Django MVC, Phoenix, Blazor Server, full-stack Next.js); otherwise split
into `fe`+`be`. Use `lib` for a library/SDK/CLI repo with no app layer.

### Rule 2 — Significance: defining tech only (drop incidental libs)
Cap each layer to these slots (fewer is fine); the slot order is also the token
order:

| role | slots → tokens |
|---|---|
| fe | ui-framework, meta-framework/builder, styling-system |
| mobile | framework |
| desktop | framework |
| be | web-framework, orm |
| fs | framework, orm |
| lib | (none — language only) |
| db | engines (most-central first) |
| infra | container/orchestration/iac |
| test | test frameworks |

Drop icon sets, validation, state, routers, component kits, loggers, monitoring,
analytics, CI providers, hosting brands.

**`db` corroboration:** include a `db` token only when it is backed by a real
driver dependency (e.g. `psycopg`, `pg`, `mongoose`, `redis` client), a compose /
infrastructure service, or a high-confidence database detection. Never add a `db`
token from an env-var placeholder or a default cache-driver config alone.

### Rule 3 — Token order (the similarity engine)
1. Order by the slot order above (primary/base technology first).
2. Within the same slot, alphabetical.
3. `db` and `test`: alphabetical (no primary).

### Rule 4 — Base before meta
A meta-framework implies and is preceded by its base, both included:
`react+next`, `react+remix`, `react+gatsby`, `vue+nuxt`, `svelte+sveltekit`,
`solid+solidstart`, `reactnative+expo`. (Emit the base even when the analyzer
omitted it via `supersedes`.)

### Rule 5 — Normalize to canonical slugs
Lowercase, drop version, remove all non-alphanumeric chars, then apply synonyms
(generalize by analogy):
- Frameworks: Next.js→next, Nuxt→nuxt, SvelteKit→sveltekit, ASP.NET Core→aspnetcore,
  Entity Framework Core→efcore, React Native→reactnative, Spring Boot→spring,
  Ruby on Rails→rails, Express→express, NestJS→nestjs, Tailwind CSS→tailwind.
- Languages: TypeScript→ts, JavaScript→js, Python→py, Ruby→rb, Go→go, Rust→rs,
  Java→java, Kotlin→kt, C#→cs, PHP→php, Swift→swift, Dart→dart, Elixir→ex.
- DB: PostgreSQL→postgres, MySQL→mysql, MongoDB→mongodb, SQL Server→mssql,
  Redis→redis, SQLite→sqlite.

### Rule 6 — Test layer
Collect significant test frameworks across all layers into ONE trailing `test:`
segment, alphabetical, deduped. Omit if none.

### Rule 7 — Multiplicity & determinism
One segment per role. Multiple frontends/backends → merge significant tokens
(deduped, ordered by Rules 3–4). Skip example/fixture/sample/docs/tooling
components. Never emit versions, counts, paths, or spaces. Same input → identical
output.

### Reference examples
```
fe(ts):react+next+tailwind|be(py):fastapi+sqlmodel|db:postgres|test:playwright+pytest
fe(ts):react+vite+tailwind|be(py):fastapi|db:postgres+redis|test:vitest
fs(py):django|db:postgres|test:pytest
fe(cs):blazor|be(cs):aspnetcore+efcore|db:mssql|test:xunit
be(go):gin+gorm|db:postgres
mobile(dart):flutter|db:firebase
lib(py)|test:pytest
```

## 4. Report back
Output, in this order:
1. The hash on its own line in a fenced code block.
2. A short bullet mapping (one line per segment) showing which component(s) and
   evidence produced it, so the user can sanity-check.

Self-check before answering: segments in Rule-1 order; absent layers omitted;
only defining tokens, ordered by Rules 3–4 (base before meta); all slugs
normalized; one alphabetical `test:` segment or none; **no spaces anywhere**.
