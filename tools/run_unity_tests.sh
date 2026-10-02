#!/usr/bin/env bash
# One command for every guard: EditMode checks plus the interaction baseline.
#
#   ./tools/run_unity_tests.sh
#
# It launches the project's Unity in batch mode, so the interactive editor has to
# be closed first (Unity locks the project's Library folder). Results land in
# .runtime/test-results/.
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
project="$repo_root/RenderingModule"
unity_app="${UNITY_APP:-/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app}"
unity_bin="$unity_app/Contents/MacOS/Unity"
results="$repo_root/.runtime/test-results"

if [[ ! -x "$unity_bin" ]]; then
  echo "Unity not found at $unity_bin (override with UNITY_APP=...)." >&2
  exit 2
fi

# Unity refuses to open a project whose Library is in use by another editor.
if [[ -f "$project/Library/EditorInstance.json" ]] && \
   pgrep -f "$unity_app/Contents/MacOS/Unity" >/dev/null 2>&1; then
  echo "An interactive Unity editor is running for this project." >&2
  echo "Close it (or run the 'VolumeSTCube > Desktop > Validate All' menu item" >&2
  echo "in that editor) and run this script again." >&2
  exit 2
fi

mkdir -p "$results"
rm -f "$results/guards-summary.txt" "$results/interaction-baseline.txt"

echo "Checking repository contracts ..."
"$repo_root/.venv/bin/python" "$repo_root/tools/check_contracts.py" \
  > "$results/contracts.log" 2>&1 || {
    echo "Repository contracts failed; see $results/contracts.log" >&2
    tail -5 "$results/contracts.log" >&2
    exit 1
  }

echo "Running guards (this walks the workflow once; allow ~2 minutes) ..."
"$unity_bin" \
  -batchmode \
  -projectPath "$project" \
  -executeMethod UnityVolumeRendering.Tests.VolumeSTCubeGuardRunner.RunHeadless \
  -logFile "$results/unity-guards.log" \
  -silent-crashes \
  ; status=$?

echo
for file in guards-summary.txt interaction-baseline.txt; do
  if [[ -f "$results/$file" ]]; then
    printf '%-26s %s\n' "$file:" "$(head -1 "$results/$file")"
  fi
done
echo "Log: $results/unity-guards.log"
exit "$status"
