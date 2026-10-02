#!/usr/bin/env bash
# One command that answers "is this build still the build we verified?".
#
#   ./tools/release-check.sh              # contracts + pytest + Unity guards
#   ./tools/release-check.sh --quick      # contracts + pytest only
#   ./tools/release-check.sh --package    # also verify the macOS zip
#   ./tools/release-check.sh --stall      # also spend 45 s proving the stall window
#   ./tools/release-check.sh --vr         # also run the Quest/VR branch
#
# The Unity half runs differently depending on the machine: a closed editor is
# driven in batch mode (tools/run_unity_tests.sh), an open one is driven through
# its own menu bar, because Unity locks the project while the editor holds it.
# Every result is a file under .runtime/, so a failing step can be re-read
# afterwards instead of re-run.
set -uo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
results="$repo_root/.runtime/test-results"
report="$results/release-check.txt"
mkdir -p "$results"

quick=0
with_package=0
with_stall=0
with_vr=0
for argument in "$@"; do
  case "$argument" in
    --quick) quick=1 ;;
    --package) with_package=1 ;;
    --stall) with_stall=1 ;;
    --vr) with_vr=1 ;;
    *) echo "Unknown option: $argument" >&2; exit 2 ;;
  esac
done

python="$repo_root/.venv/bin/python"
[[ -x "$python" ]] || python="$(command -v python3)"

failures=()
lines=()

note() {
  printf '%s\n' "$1"
  lines+=("$1")
}

fail() {
  failures+=("$1")
  note "FAIL  $1"
}

pass() {
  note "ok    $1"
}

# ---------------------------------------------------------------- contracts
if "$python" "$repo_root/tools/check_contracts.py" >/dev/null 2>&1; then
  pass "repository contracts"
else
  fail "repository contracts (see .runtime/test-results/contracts.txt)"
fi

# ------------------------------------------------------------------- pytest
if (cd "$repo_root" && "$python" -m pytest -q Services/S4DAnalysisService/tests \
      >/dev/null 2>&1); then
  pass "backend test suite"
else
  fail "backend test suite (run it without -q for the traceback)"
fi

if [[ "$quick" == "1" ]]; then
  note ""
  note "$( [[ ${#failures[@]} -eq 0 ]] && echo "PASS: quick check" || echo "FAIL: ${failures[*]}" )"
  printf '%s\n' "${lines[@]}" > "$report"
  exit $( [[ ${#failures[@]} -eq 0 ]] && echo 0 || echo 1 )
fi

# -------------------------------------------------------------- Unity guards
guard_summary="$results/guards-summary.txt"
before="$(stat -f %m "$guard_summary" 2>/dev/null || echo 0)"

if pgrep -f "Unity.app/Contents/MacOS/Unity -projectpath" >/dev/null 2>&1; then
  # The editor is open: drive the same menu item a person would click.
  osascript -e 'tell application "System Events" to tell process "Unity" to click menu item "Validate All" of menu 1 of menu item "Desktop" of menu 1 of menu bar item "VolumeSTCube" of menu bar 1' >/dev/null 2>&1
  if [[ $? -ne 0 ]]; then
    fail "could not reach the Unity menu (accessibility permission?)"
  else
    waited=0
    while [[ "$waited" -lt 240 ]]; do
      sleep 10
      waited=$((waited + 10))
      now="$(stat -f %m "$guard_summary" 2>/dev/null || echo 0)"
      [[ "$now" != "$before" && "$now" != "0" ]] && break
    done
    now="$(stat -f %m "$guard_summary" 2>/dev/null || echo 0)"
    if [[ "$now" == "$before" || "$now" == "0" ]]; then
      fail "the editor never wrote a guard summary (is it in Play Mode?)"
    elif grep -q "^PASS" "$guard_summary"; then
      pass "Unity guards (menu run)"
    else
      fail "Unity guards: $(head -1 "$guard_summary")"
    fi
  fi
else
  if "$repo_root/tools/run_unity_tests.sh" >/dev/null 2>&1; then
    pass "Unity guards (batch mode)"
  else
    fail "Unity guards (batch mode): $(head -1 "$guard_summary" 2>/dev/null)"
  fi
fi

# ------------------------------------------------------------------ package
if [[ "$with_stall" == "1" ]]; then
  # The 45 s window needs 45 s of silence, so it lives behind its own menu item
  # and its own flag instead of slowing every run down.
  stall_report="$repo_root/.runtime/stall-timeout-validation.txt"
  stall_before="$(stat -f %m "$stall_report" 2>/dev/null || echo 0)"
  if ! pgrep -f "Unity.app/Contents/MacOS/Unity -projectpath" >/dev/null 2>&1; then
    fail "the stall check needs the interactive editor to stay open"
  else
    osascript -e 'tell application "System Events" to tell process "Unity" to click menu item "Validate Stall Timeout" of menu 1 of menu item "Desktop" of menu 1 of menu bar item "VolumeSTCube" of menu bar 1' >/dev/null 2>&1
    waited=0
    while [[ "$waited" -lt 300 ]]; do
      sleep 10
      waited=$((waited + 10))
      now="$(stat -f %m "$stall_report" 2>/dev/null || echo 0)"
      [[ "$now" != "$stall_before" && "$now" != "0" ]] && break
    done
    now="$(stat -f %m "$stall_report" 2>/dev/null || echo 0)"
    if [[ "$now" == "$stall_before" || "$now" == "0" ]]; then
      fail "the stall check wrote no verdict"
    elif grep -q "^PASS" "$stall_report"; then
      pass "stall timeout: $(head -1 "$stall_report")"
    else
      fail "stall timeout: $(head -1 "$stall_report")"
    fi
  fi
fi

if [[ "$with_vr" == "1" ]]; then
  # The Quest/VR branch runs in the editor's VR preview, so it needs the
  # interactive editor for the same reason the stall check does.
  vr_report="$repo_root/.runtime/vr-flow-validation.txt"
  vr_before="$(stat -f %m "$vr_report" 2>/dev/null || echo 0)"
  if ! pgrep -f "Unity.app/Contents/MacOS/Unity -projectpath" >/dev/null 2>&1; then
    fail "the VR check needs the interactive editor to stay open"
  else
    osascript -e 'tell application "System Events" to tell process "Unity" to click menu item "Validate VR Flow" of menu 1 of menu item "Desktop" of menu 1 of menu bar item "VolumeSTCube" of menu bar 1' >/dev/null 2>&1
    waited=0
    while [[ "$waited" -lt 180 ]]; do
      sleep 10
      waited=$((waited + 10))
      now="$(stat -f %m "$vr_report" 2>/dev/null || echo 0)"
      [[ "$now" != "$vr_before" && "$now" != "0" ]] && break
    done
    now="$(stat -f %m "$vr_report" 2>/dev/null || echo 0)"
    if [[ "$now" == "$vr_before" || "$now" == "0" ]]; then
      fail "the VR check wrote no verdict"
    elif grep -q "^PASS" "$vr_report"; then
      pass "VR flow: $(head -1 "$vr_report")"
    else
      fail "VR flow: $(head -1 "$vr_report")"
    fi
  fi
fi

if [[ "$with_package" == "1" ]]; then
  builds="$repo_root/RenderingModule/Builds"
  archive="$builds/STC-SlabLab-macOS.zip"
  app="$builds/STC-SlabLab-macOS/STC SlabLab.app"
  if [[ ! -f "$archive" ]]; then
    fail "no package at $archive (run package-macos.sh with --with-env)"
  elif ! unzip -t "$archive" >/dev/null 2>&1; then
    fail "the package archive is corrupt"
  elif ! codesign -v "$app" >/dev/null 2>&1; then
    fail "the packaged app does not verify its signature"
  else
    pass "package archive and signature"
    # The delivery is only the delivery if it carries the current interface.
    dll="$app/Contents/Resources/Data/Managed/Assembly-CSharp.dll"
    if "$python" - "$dll" <<'PY'
import pathlib, sys
data = pathlib.Path(sys.argv[1]).read_bytes()
wanted = [
    "DATA SOURCE \u00b7 LOCAL CACHE (NO NETWORK)",
    "Keys: Page Up / Page Down: previous / next step",
    "X: reset view",
    "P: snapshot",
    "T: type time range",
    "Save this screen as a PNG in Captures",
]
missing = [text for text in wanted if not data.count(text.encode("utf-16-le"))]
print("\n".join(missing))
sys.exit(1 if missing else 0)
PY
    then
      pass "packaged build carries the current interface"
    else
      fail "the package predates the current interface (rebuild it)"
    fi
    env_file="$builds/STC-SlabLab-macOS/.env"
    # The interface-string check above only proves the package was built *at
    # some point* after the UI changed. This one proves the shipped binary is
    # newer than every runtime script, which is what "the delivery is the code"
    # actually means — the manual step of remembering to rebuild.
    dll="$app/Contents/Resources/Data/Managed/Assembly-CSharp.dll"
    stale="$(find "$repo_root/RenderingModule/Assets/VolumeSTCubeAPI" \
                   "$repo_root/RenderingModule/Assets/Scripts" \
                   -name '*.cs' -newer "$dll" 2>/dev/null | head -3)"
    if [[ -n "$stale" ]]; then
      fail "the shipped binary predates the code: $(echo "$stale" | head -1)"
    else
      pass "packaged build is newer than every runtime script"
    fi
    if [[ -f "$env_file" ]]; then
      mode="$(stat -f %Sp "$env_file")"
      [[ "$mode" == "-rw-------" ]] && pass "packaged .env is owner-only" \
        || fail "packaged .env is $mode, expected -rw-------"
    else
      pass "no .env in this package (live mode will need one)"
    fi

    # The Unity side is covered above; this is the same claim for everything
    # else the package ships. A stale backend or a stale start script would
    # otherwise be delivered silently, and the Python is where the analysis
    # actually happens.
    package_dir="$builds/STC-SlabLab-macOS"
    mismatched=""
    compare_delivery() {
      local source_path="$1" delivered_path="$2" label="$3"
      if [[ -d "$source_path" ]]; then
        diff -r -q --exclude=__pycache__ "$source_path" "$delivered_path" \
          >/dev/null 2>&1 || mismatched="$mismatched $label"
      else
        diff -q "$source_path" "$delivered_path" >/dev/null 2>&1 \
          || mismatched="$mismatched $label"
      fi
    }
    compare_delivery "$repo_root/Services/S4DAnalysisService" \
      "$package_dir/Services/S4DAnalysisService" "Services/S4DAnalysisService"
    compare_delivery "$repo_root/Services/MatPlotAgent/api_server.py" \
      "$package_dir/Services/MatPlotAgent/api_server.py" "MatPlotAgent/api_server.py"
    compare_delivery "$repo_root/Services/MatPlotAgent/local_run.py" \
      "$package_dir/Services/MatPlotAgent/local_run.py" "MatPlotAgent/local_run.py"
    compare_delivery "$repo_root/Start-Backend.sh" \
      "$package_dir/Start-Backend.sh" "Start-Backend.sh"
    compare_delivery "$repo_root/Stop-Backend.sh" \
      "$package_dir/Stop Backend.command" "Stop Backend.command"
    compare_delivery "$repo_root/tools/prepare_wave_cache.py" \
      "$package_dir/tools/prepare_wave_cache.py" "tools/prepare_wave_cache.py"
    compare_delivery "$repo_root/Packaging/macOS/README-START-HERE.txt" \
      "$package_dir/README-START-HERE.txt" "README-START-HERE.txt"
    compare_delivery "$repo_root/Packaging/macOS/requirements-desktop.txt" \
      "$package_dir/requirements-desktop.txt" "requirements-desktop.txt"
    compare_delivery "$repo_root/Packaging/macOS/Setup Backend.command" \
      "$package_dir/Setup Backend.command" "Setup Backend.command"
    compare_delivery "$repo_root/Packaging/macOS/Start STC SlabLab.command" \
      "$package_dir/Start STC SlabLab.command" "Start STC SlabLab.command"
    compare_delivery "$repo_root/Packaging/macOS/Prepare Offline Data.command" \
      "$package_dir/Prepare Offline Data.command" "Prepare Offline Data.command"
    if [[ -n "$mismatched" ]]; then
      fail "the package does not match the repo:$mismatched"
    else
      pass "packaged backend and tooling match the repo byte for byte"
    fi
  fi
fi

note ""
if [[ ${#failures[@]} -eq 0 ]]; then
  note "PASS: release check"
else
  note "FAIL: ${failures[*]}"
fi
printf '%s\n' "${lines[@]}" > "$report"
printf '%s\n' "${lines[@]}"
[[ ${#failures[@]} -eq 0 ]] && exit 0 || exit 1
