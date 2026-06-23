#!/usr/bin/env bash
# Runs the Ivy CLI + the three reference tools against a clone, then prints ONE
# compact normalized comparison (via compare.py). Handles every gotcha the tools
# have on Windows/Git-Bash so the agent doesn't burn turns/tokens debugging them.
#
# Usage:  analyze.sh <clone-path> <name>
#   <clone-path>  absolute path to the cloned repo (e.g. D:/Temp/foo)
#   <name>        short slug used for output filenames (e.g. foo)
#
# Raw artifacts are left in D:/Temp/_tools/ for targeted lookups when writing
# suggestions. Each tool is best-effort: a failure is reported and skipped.
set -uo pipefail

CLONE="${1:?usage: analyze.sh <clone-path> <name>}"
NAME="${2:?usage: analyze.sh <clone-path> <name>}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../../.." && pwd)"   # scripts -> skill -> review-stack-analyze -> skills -> .claude -> repo
TOOLS="D:/Temp/_tools"
mkdir -p "$TOOLS" "$TOOLS/cd-$NAME"

# Pinned reference-tool versions / images (reproducible + cache-friendly).
SPECFY_VERSION="1.27.6"
LINGUIST_IMAGE="crazymax/linguist:9.5.0"

echo ">> Ivy.StackAnalyzer (Release build, run exe)..." >&2
# Build once in Release (incremental: a no-op when unchanged), then run the
# produced exe directly — avoids `dotnet run`'s per-invocation build/restore check.
IVY_DIR="$REPO_ROOT/src/Ivy.StackAnalyzer.Console/bin/Release/net10.0"
( cd "$REPO_ROOT" && dotnet build -c Release src/Ivy.StackAnalyzer.Console/Ivy.StackAnalyzer.Console.csproj ) \
    >/dev/null 2>"$TOOLS/ivy-build.err" || echo "[ivy] build failed (see ivy-build.err)" >&2
if [ -f "$IVY_DIR/ivy-stack-analyzer.exe" ]; then IVY=( "$IVY_DIR/ivy-stack-analyzer.exe" )
elif [ -f "$IVY_DIR/ivy-stack-analyzer.dll" ]; then IVY=( dotnet "$IVY_DIR/ivy-stack-analyzer.dll" )
else IVY=( dotnet run -c Release --project "$REPO_ROOT/src/Ivy.StackAnalyzer.Console" -- ); fi
"${IVY[@]}" "$CLONE" -o json \
    > "$TOOLS/ivy-$NAME.json" 2>"$TOOLS/ivy-$NAME.err" || echo "[ivy] FAILED (see ivy-$NAME.err)" >&2

echo ">> specfy/stack-analyser@$SPECFY_VERSION..." >&2
# specfy resolves -o relative to the analyzed root and chokes on absolute Windows
# paths, so write into the clone with a relative name, then move it out.
( cd "$CLONE" && npx -y "@specfy/stack-analyser@$SPECFY_VERSION" "$CLONE" -o "_specfy-$NAME.json" --flat ) \
    >/dev/null 2>"$TOOLS/specfy-$NAME.err" \
    && mv -f "$CLONE/_specfy-$NAME.json" "$TOOLS/specfy-$NAME.json" \
    || echo "[specfy] unavailable (Node missing or run failed)" >&2

echo ">> microsoft/component-detection..." >&2
CD_EXE="$TOOLS/component-detection.exe"
[ -f "$CD_EXE" ] || curl -fsSL -o "$CD_EXE" \
    https://github.com/microsoft/component-detection/releases/latest/download/component-detection-win-x64.exe \
    || echo "[component-detection] download failed" >&2
[ -f "$CD_EXE" ] && ( "$CD_EXE" scan --SourceDirectory "$CLONE" --Output "$TOOLS/cd-$NAME" >/dev/null 2>&1 \
    || echo "[component-detection] scan failed" >&2 )

echo ">> github-linguist ($LINGUIST_IMAGE via Docker)..." >&2
# Pre-pull once (cached thereafter); MSYS_NO_PATHCONV stops Git-Bash mangling the
# ":/repo" mount target.
docker image inspect "$LINGUIST_IMAGE" >/dev/null 2>&1 || docker pull "$LINGUIST_IMAGE" >/dev/null 2>&1 \
    || echo "[linguist] image pull failed (is Docker Desktop running?)" >&2
MSYS_NO_PATHCONV=1 docker run --rm -v "$CLONE:/repo" "$LINGUIST_IMAGE" \
    > "$TOOLS/linguist-$NAME.txt" 2>"$TOOLS/linguist-$NAME.err" \
    || echo "[linguist] unavailable (is Docker Desktop running?)" >&2

echo "" >&2
python "$SCRIPT_DIR/compare.py" \
    --ivy "$TOOLS/ivy-$NAME.json" \
    --specfy "$TOOLS/specfy-$NAME.json" \
    --cd "$TOOLS/cd-$NAME" \
    --linguist "$TOOLS/linguist-$NAME.txt"
