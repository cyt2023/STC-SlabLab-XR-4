#!/usr/bin/env python3
"""Repository-level contracts that can be checked without opening Unity.

Complements the in-editor guards: this covers the numbers, keys, routes and
scripts that must not drift when the interface is improved. Run it with
``.venv/bin/python tools/check_contracts.py``; results land in
``.runtime/test-results/contracts.txt``.
"""

from __future__ import annotations

import json
import builtins
import os
import re
import stat
import subprocess
import sys
import symtable
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
API = REPO / "RenderingModule" / "Assets" / "VolumeSTCubeAPI"
EDITOR = REPO / "RenderingModule" / "Assets" / "Editor"
RESULTS = REPO / ".runtime" / "test-results"

LAYOUT_CONSTANTS = {
    "FieldPairCentreShiftRight": "0.12f",
    "FieldPairLiftUp": "0.22f",
    "FieldPairCompactScale": "0.78f",
    "AxisDockViewportX": "0.52f",
    "AxisDockViewportY": "0.50f",
    "AxisDockScale": "1.38f",
    "AxisBodyScale": "1.30f",
    "FieldPresentationScale": "1.15f",
}

LAYOUT_SIZES = {
    "ButtonSizeStandard": "210, 42",
    "ButtonSizeMedium": "190, 42",
    "BoundaryActionButtonSize": "300, 54",
    "RoleButtonSize": "132, 36",
    "StepperButtonSize": "130, 26",
    "RollupGroupButtonSize": "48, 30",
    "RowChipSize": "150, 28",
    "PanelCardSizeLarge": "900, 440",
    "PanelCanvasSize": "1120.0f, 840.0f",
    "AxisMarkerDotSize": "14, 14",
}

SETTINGS_KEYS = {
    "SpatialPromptKey": "VolumeSTCube.Quest.SpatialPrompt",
    "QuestPromptKey": "VolumeSTCube.Quest.Prompt",
    "BarPromptMigrationKey": "VolumeSTCube.Quest.BarPromptMigrationV2",
    "MatPlotUrlKey": "VolumeSTCube.Quest.MatPlotUrl",
    "WaveUrlKey": "VolumeSTCube.Quest.S4DUrl",
    "DatasetRootKey": "VolumeSTCube.Quest.DatasetRoot",
    "WaveStartUtcKey": "VolumeSTCube.Wave.StartUtc",
    "WaveEndUtcKey": "VolumeSTCube.Wave.EndUtc",
    "RuntimeApplicationModeKey": "VolumeSTCube.SlabLabRuntimeApplicationMode",
}

ROUTES = {
    "/health",
    "/wave/status", "/wave/import", "/wave/live-dataset",
    "/wave/frame-batch", "/wave/timeline-batch",
    "/datasets", "/datasets/resolve",
    "/datasets/{dataset_id}/manifest", "/datasets/{dataset_id}/validate",
    "/analysis/resolve-intent", "/analysis/prepare-matplot-job",
    "/analysis/preview-atlas", "/analysis/materialize",
    "/jobs/{job_id}", "/jobs/{job_id}/panel",
    "/jobs/{job_id}/cells/{cell_id}/panel", "/jobs/{job_id}/chart-result",
    "/jobs/{job_id}/digest", "/digest-jobs/{digest_job_id}",
    "/snapshots/{snapshot_id}",
    "/snapshots/{snapshot_id}/cells/{cell_id}/aggregate-volume",
    "/speech/transcribe",
}

WORKBENCH_PARTS = 23

# Wording the operator sees at the key moments. These are the strings the flow
# verification relied on; an interaction change must not reword them.
STATUS_WORDING = [
    "Connecting to the Wave server...",
    "Live Wave data is ready. Continue when ready.",
    "Preparing the selected variable for Time and Depth setup.",
    "Wave server data is ready. Review the detected variables, then continue.",
    "The Wave server did not answer within ",
    "Reconnecting to the Wave server (",
    "Still waiting for the Wave server...",
    "SlabLab application mode: ",
    # The two "grounded" lines are how a drag tells the operator what it snapped
    # to — time snaps to whole day cuts by construction, depth to the layer
    # lattice on release. They are the feedback half of "snap feedback", and the
    # only part of it that a value-changing snap would alter.
    "Time interval grounded: day ",
    "Depth band grounded: ",
    "DATA SOURCE · LIVE",
    "DATA SOURCE · LOCAL CACHE (NO NETWORK)",
    "Keys: Page Up / Page Down: previous / next step",
    "X: reset view",
    "Y: slab frame",
    "P: snapshot",
]

failures: list[str] = []


def check(label: str, condition: bool, detail: str = "") -> None:
    if condition:
        print("  ok   %s" % label)
        return
    print("  FAIL %s %s" % (label, detail))
    failures.append(label + (" " + detail if detail else ""))


def layout_source() -> str:
    return (API / "SlabLabLayout.cs").read_text(encoding="utf-8")


def shortcut_keys(table_source: str) -> set[str]:
    """The KeyCode names listed in SlabLabShortcuts.Keys, lower-cased."""
    block = re.search(r"public static readonly KeyCode\[\] Keys =\s*\{(.*?)\};",
                      table_source, re.S)
    if block is None:
        return set()
    return {name.lower().replace("_", "")
            for name in re.findall(r"KeyCode\.(\w+)", block.group(1))}


# Unity names the movement axes in InputManager with plain key names.
AXIS_KEY_NAMES = {
    "left": "leftarrow", "right": "rightarrow",
    "up": "uparrow", "down": "downarrow",
    "a": "a", "d": "d", "w": "w", "s": "s",
}


def movement_keys(locomotion_source: str) -> set[str]:
    """
    Every key the camera locomotion reads through Input.GetAxisRaw, resolved
    against the project's InputManager bindings.
    """
    axes = set(re.findall(r'GetAxisRaw\("(\w+)"\)', locomotion_source))
    if not axes:
        return set()
    asset = (REPO / "RenderingModule" / "ProjectSettings" /
             "InputManager.asset").read_text(encoding="utf-8")
    bound: set[str] = set()
    for name in axes:
        block = re.search(
            r"m_Name: %s\n(.*?)(?=\n  - serializedVersion|\Z)" % name,
            asset, re.S)
        if block is None:
            continue
        for button in re.findall(
                r"(?:negativeButton|positiveButton|altNegativeButton|"
                r"altPositiveButton): (\S+)", block.group(1)):
            key = AXIS_KEY_NAMES.get(button.lower())
            if key:
                bound.add(key)
    return bound


def main() -> int:
    print("Layout constants")
    layout = layout_source()
    for name, value in LAYOUT_CONSTANTS.items():
        match = re.search(
            r"const float %s = ([^;]+);" % name, layout)
        check(name, match is not None and match.group(1).strip() == value,
              "expected %s" % value)
    for name, value in LAYOUT_SIZES.items():
        match = re.search(
            r"static readonly Vector2 %s = new Vector2\(([^)]*)\);" % name,
            layout)
        check(name, match is not None and match.group(1).strip() == value,
              "expected %s" % value)

    print("Persistence keys")
    settings = (API / "SlabLabSettings.cs").read_text(encoding="utf-8")
    for name, value in SETTINGS_KEYS.items():
        match = re.search(
            r"const string %s\s*=\s*\n?\s*\"([^\"]+)\";" % name, settings)
        check(name, match is not None and match.group(1) == value,
              "expected %s" % value)
    check("no PlayerPrefs in the workbench",
          not any("PlayerPrefs." in path.read_text(encoding="utf-8")
                  for path in API.glob(
                      "VolumeSTCubeQuestSpatialWorkbench*.cs")))

    print("Workbench split")
    parts = sorted(API.glob("VolumeSTCubeQuestSpatialWorkbench*.cs"))
    check("part files", len(parts) == WORKBENCH_PARTS,
          "found %d, expected %d" % (len(parts), WORKBENCH_PARTS))
    check("editor guards present",
          (EDITOR / "VolumeSTCubeRefactorTests.cs").is_file() and
          (EDITOR / "VolumeSTCubeInteractionBaseline.cs").is_file() and
          (EDITOR / "VolumeSTCubeGuardRunner.cs").is_file() and
          (EDITOR / "VolumeSTCubeShortcutTests.cs").is_file() and
          (EDITOR / "VolumeSTCubeExportTests.cs").is_file() and
          (EDITOR / "VolumeSTCubeBoundaryEntryTests.cs").is_file())

    print("Snapshot export")
    snapshot = (API / "SlabLabSnapshot.cs").read_text(encoding="utf-8")
    export_hud = (API / "VolumeSTCubeFlatScreenHUD.cs").read_text(
        encoding="utf-8")
    check("snapshot helper exists",
          "public static string NextPath(" in snapshot and
          "public static bool IsPng(" in snapshot)
    check("snapshot files live under one folder",
          "Application.persistentDataPath" in snapshot and
          "const string FolderName" in snapshot)
    check("HUD wires the Snapshot button",
          'CreateButton("Snapshot", actionBar, SaveSnapshot,' in export_hud)
    # ScreenCapture writes at the end of the frame; a button handler that does
    # not wait would report success before the file exists.
    check("snapshot waits for the file",
          "ScreenCapture.CaptureScreenshot(path, 1)" in export_hud and
          "new WaitForEndOfFrame()" in export_hud and
          "SlabLabSnapshot.IsPng(path)" in export_hud)

    print("Typed Time range")
    entry = (API / "SlabLabBoundaryEntry.cs").read_text(encoding="utf-8")
    entry_table = (API / "SlabLabShortcuts.cs").read_text(encoding="utf-8")
    boundary = (API / "VolumeSTCubeQuestSpatialWorkbench.Boundary.cs"
                ).read_text(encoding="utf-8")
    check("range parser exists",
          "public static bool TryParseTimeCuts(" in entry)
    check("range entry is a shortcut",
          "case KeyCode.T: return SlabLabShortcut.RangeEntry;" in entry_table)
    check("range entry is advertised",
          "T: type time range" in entry_table)
    check("workbench owns the typed range",
          "public bool DesktopRangeEntry()" in boundary and
          "public void DesktopApplyRangeEntry()" in boundary and
          "public void DesktopCancelRangeEntry()" in boundary)
    # The typed path must preview/confirm through the same calls the drag uses,
    # otherwise the two paths could disagree about what a range means.
    apply_block = boundary.split("public void DesktopApplyRangeEntry()")[1]
    apply_block = apply_block.split("public void DesktopCancelRangeEntry()")[0]
    check("typed range previews like a drag",
          "UpdateTimeBoundaryHandles();" in apply_block and
          "PreviewBoundaryTime(timeBoundaryEnd);" in apply_block and
          "BuildBoundaryPanel();" in apply_block)
    check("typed range clamps through the shared helper",
          "SlabLabBoundaryEntry.TryParseTimeCuts(" in apply_block)

    print("Opening screen self-heal")
    # The packaged app starts its bundled backend in the same launch that opens
    # the opening screen, so the first registration can lose that race. The
    # workspace already keeps asking by itself (WatchWaveImport); this is the
    # mechanism, and it is the only one — an extra retry layer was tried and
    # removed because it duplicated this.
    device_source = (API / "VolumeSTCubeQuestSpatialWorkbench.cs"
                     ).read_text(encoding="utf-8")
    core_source = (API / "VolumeSTCubeQuestSpatialWorkbench.Dataset.cs"
                   ).read_text(encoding="utf-8")
    check("the self-heal entry point exists",
          "private void WatchWaveImport()" in core_source)
    check("the update loop drives it",
          "WatchWaveImport();" in device_source)
    check("it retries a bounded number of quick attempts",
          "MaxWaveAutoRetries" in core_source and
          "Reconnecting to the Wave server (retry " in core_source)
    check("a silent server still ends on the operator's retry",
          "Still waiting for the Wave server..." in core_source)

    print("Stall-timeout guard")
    # The 45 s window used to be checked by hand (point the workspace at a silent
    # port, run it, watch the log). It is a guard now, so the pieces it needs must
    # stay: a listener that lives outside the reloading domain, an opt-in phase,
    # and a restore of whatever the client was pointed at.
    silent = (EDITOR / "SlabLabSilentServer.cs").read_text(encoding="utf-8")
    baseline_source = (EDITOR / "VolumeSTCubeInteractionBaseline.cs"
                       ).read_text(encoding="utf-8")
    check("the silent server is its own process",
          "ProcessStartInfo" in silent and "python3" in silent and
          "const int Port = 8099" in silent)
    check("the stall phase is opt-in",
          "bool includeStall = false" in baseline_source and
          "if (!stallRequested)" in baseline_source)
    check("the stall phase survives the Play Mode reload",
          "StallRequestKey" in baseline_source)
    check("a stalled server must be reported in the operator's words",
          '"The Wave server did not answer within 45s"' in baseline_source)
    check("an early give-up is not credited to the window",
          "the 45s window" in baseline_source)
    check("the client is pointed back afterwards",
          "client.serviceBaseUrl = stallUrlBefore;" in baseline_source)
    # A verdict file that only appears on success made the gate wait its full
    # timeout and then report "wrote no verdict" instead of the real reason.
    # Both directions have to write one.
    check("the stall check reports failure as well as success",
          baseline_source.count("WriteStallReport(stallFailure);") >= 2 and
          "WriteStallReport();" in baseline_source)

    print("VR flow guard")
    # The Quest/VR branch used to be a static audit. This runs it in the editor's
    # VR preview and checks the device-independent half: the mode split, the boot,
    # a step transition, and that nothing logged an error.
    vr = (EDITOR / "VolumeSTCubeVrFlowTests.cs").read_text(encoding="utf-8")
    check("the VR check really switches mode",
          "VolumeSTCubeApplicationMode.VirtualReality" in vr)
    check("it asserts the world rig instead of the flat shell",
          "VolumeSTCubeFlatScreenHUD" in vr and
          "world-space panels are missing" in vr)
    check("a logged error fails the run",
          "LogType.Exception" in vr and "logged errors" in vr)
    check("the operator's mode is restored",
          "SetStartupPreference" in vr)
    check("its clock cannot come out negative",
          "DateTime.UtcNow.Ticks" in vr)

    print("Hover hints")
    hints = (API / "SlabLabHints.cs").read_text(encoding="utf-8")
    hints_hud = (API / "VolumeSTCubeFlatScreenHUD.cs").read_text(
        encoding="utf-8")
    check("the hint table exists",
          "public static string For(string caption)" in hints)
    check("the HUD shows hints in the notice line",
          "UpdateHoverHint" in hints_hud and
          "noticeText.text = hint;" in hints_hud)
    check("a live notice is not clobbered by a hint",
          "!hoverHintShown" in hints_hud)

    print("Rendering claim")
    # "Present in the hierarchy" is not "drawn". The baseline proves the pixels
    # with a colour nothing else on screen uses — and paints a certainly-visible
    # control label first, so a zero can never be blamed on the instrument.
    check("the data-source line is measured in pixels",
          "StartColorProbe(control: true)" in baseline_source and
          "StartColorProbe(control: false)" in baseline_source)
    check("a positive control runs before the claim",
          "the colour probe is not working" in baseline_source)
    check("the probe colour is unique and counted",
          "Color.magenta" in baseline_source and
          "if (magenta < 150)" in baseline_source)

    print("Timing budget")
    timing_baseline = (EDITOR / "VolumeSTCubeInteractionBaseline.cs"
                       ).read_text(encoding="utf-8")
    workbench_core = (API / "VolumeSTCubeQuestSpatialWorkbench.cs"
                      ).read_text(encoding="utf-8")
    check("the app reports its own startup time",
          "public float DesktopStartupSeconds" in workbench_core and
          "startupReadySeconds = Time.realtimeSinceStartup - startupStartedAt;"
          in (API / "VolumeSTCubeQuestSpatialWorkbench.Dataset.cs"
              ).read_text(encoding="utf-8"))
    check("the guard puts a budget on it",
          "BootBudgetSeconds" in timing_baseline and
          "StepBudgetSeconds" in timing_baseline and
          "CheckTimings();" in timing_baseline)
    # The quiet window has to belong to one phase; carrying it across phases made
    # the next step "settle" in 0.02 s.
    check("the settle window is per phase",
          "if (settledPhase != phase)" in timing_baseline)

    print("Keyboard shortcuts")
    # One table, or the Help card and the handler drift apart. The step keys in
    # particular must be polled through SlabLabShortcuts instead of being
    # hard-coded in the HUD, so a re-mapping cannot leave the card lying.
    table = (API / "SlabLabShortcuts.cs").read_text(encoding="utf-8")
    hud = (API / "VolumeSTCubeFlatScreenHUD.cs").read_text(encoding="utf-8")
    interaction = (API / "VolumeSTCubeQuestSpatialWorkbench.Interaction.cs"
                   ).read_text(encoding="utf-8")
    check("shortcut table exists", "public static SlabLabShortcut Map(" in table)
    check("HUD polls the table", "SlabLabShortcuts.Keys" in hud and
          "SlabLabShortcuts.Map(" in hud)
    check("workbench maps through the table",
          "SlabLabShortcuts.Map(" in interaction)
    check("HUD does not hard-code a step key",
          "KeyCode.PageUp" not in hud and "KeyCode.PageDown" not in hud)
    check("typing gate in the workbench handler",
          "input.textInputActive" in interaction)
    # X and Y used to be handled inside the locomotion component and were
    # documented nowhere. They now go through the table too, so the Help card
    # cannot advertise a key that nothing handles.
    locomotion = (API / "VolumeSTCubeQuestRuntime.cs").read_text(
        encoding="utf-8")
    check("runtime polls the table",
          "ShortcutPressed(SlabLabShortcut.ResetView)" in locomotion and
          "ShortcutPressed(SlabLabShortcut.ToggleSlabFrame)" in locomotion)
    check("no hard-coded reset key outside the table",
          "KeyCode.X" not in locomotion and "KeyCode.Y" not in locomotion)
    check("reset view is reachable as a public entry point",
          "public void ResetDesktopView()" in locomotion and
          "public void DesktopRecentreField()" in
          (API / "VolumeSTCubeQuestSpatialWorkbench.DesktopLayout.cs")
          .read_text(encoding="utf-8"))
    check("Reset View button shares the reset entry point",
          "private void ResetView()" in hud and
          "locomotion.ResetDesktopView()" in hud)
    check("Reset View button no longer re-fits the volume only",
          "workbench.ResetVolumeLayout, 170.0f" not in hud)

    # The first version of the shortcuts used the arrow keys, which
    # InputManager also binds to the Horizontal / Vertical movement axes the
    # locomotion reads: every step also nudged the camera rig. A shortcut key
    # must never be a movement key.
    collisions = sorted(set(shortcut_keys(table)) & movement_keys(locomotion))
    check("no shortcut key doubles as a movement key",
          not collisions, "clashes with the movement axes: %s" % collisions)

    print("Guard infrastructure")
    # The interaction baseline enters Play Mode, and this project keeps the
    # default "reload domain" behaviour: every static field and the
    # EditorApplication.update subscription are wiped before the workbench has
    # booted. The first version of the baseline died that way and wrote no
    # artifact at all (silent no-op). These checks keep the re-attach and the
    # idle gate in place.
    baseline = (EDITOR / "VolumeSTCubeInteractionBaseline.cs").read_text(
        encoding="utf-8")
    runner = (EDITOR / "VolumeSTCubeGuardRunner.cs").read_text(
        encoding="utf-8")
    for label, text, snippet in (
            ("baseline reattaches after reload", baseline,
             "[InitializeOnLoadMethod]"),
            ("baseline parks its run", baseline, "SessionState.SetBool("),
            ("baseline waits for an idle app", baseline,
             '"DesktopOperationPending"'),
            ("runner parks edit-mode failures", runner,
             "SessionState.SetString("),
            ("runner reports skips honestly", runner, "SKIPPED: "),
    ):
        check(label, snippet in text, "missing %s" % snippet)

    print("Status wording")
    source = "\n".join(
        path.read_text(encoding="utf-8")
        for path in list(API.glob("*.cs")) + list(EDITOR.glob("*.cs")))
    for wording in STATUS_WORDING:
        check(repr(wording)[1:-1][:44], wording in source)

    print("Service routes")
    sys.path.insert(0, str(REPO))
    from Services.S4DAnalysisService.app import app  # noqa: E402

    declared = {route.path for route in app.routes
                if getattr(route, "path", "").startswith("/")
                and route.path not in ("/openapi.json", "/docs",
                                       "/docs/oauth2-redirect", "/redoc")}
    check("route inventory", declared == ROUTES,
          "extra=%s missing=%s" % (sorted(declared - ROUTES),
                                   sorted(ROUTES - declared)))

    # A router can import cleanly while a later request references a name left
    # behind in app.py. Audit global dependencies in every function, including
    # fallback/exception branches that an ordinary smoke test may not execute.
    missing_globals = []
    service_root = REPO / "Services" / "S4DAnalysisService"
    for path in sorted(service_root.rglob("*.py")):
        if "tests" in path.relative_to(service_root).parts:
            continue
        symbols = symtable.symtable(path.read_text(encoding="utf-8"),
                                    str(path), "exec")
        defined = {symbol.get_name() for symbol in symbols.get_symbols()
                   if symbol.is_assigned() or symbol.is_imported()
                   or symbol.is_namespace()}
        defined.update(dir(builtins))
        defined.update({"__file__", "__name__"})

        def undefined_globals(scope):
            unresolved = {symbol.get_name() for symbol in scope.get_symbols()
                          if symbol.is_global() and symbol.is_referenced()
                          and symbol.get_name() not in defined}
            for child in scope.get_children():
                unresolved.update(undefined_globals(child))
            return unresolved

        missing_globals.extend(str(path.relative_to(REPO)) + ": " + name
                               for name in sorted(undefined_globals(symbols)))
    check("split service modules resolve their global dependencies",
          not missing_globals, "; ".join(missing_globals))

    print("Scripts and docs")
    # The packaged app ships a .env holding the Wave API key by request. It must
    # stay out of git and stay unreadable to other accounts on the machine.
    print("Secret hygiene")
    env_file = REPO / ".env"
    tracked = subprocess.run(
        ["git", "ls-files", "--error-unmatch", ".env"],
        cwd=str(REPO), capture_output=True, text=True)
    check(".env is not tracked by git", tracked.returncode != 0)
    if env_file.is_file():
        mode = stat.S_IMODE(env_file.stat().st_mode)
        check(".env is not readable by other accounts", mode & 0o077 == 0,
              "mode is %o, expected 600" % mode)
    else:
        check(".env permissions", True, "no .env in this checkout")
    packager = (REPO / "Packaging/macOS/package-macos.sh").read_text(
        encoding="utf-8")
    check("packaged .env is owner-only",
          'chmod 600 "$package_dir/.env"' in packager)
    # The four double-clickable commands are how a teacher uses the package; a
    # packaging edit that drops one has to fail here rather than on their Mac.
    # ("Stop Backend.command" was exercised by hand: it stops both services by
    # PID, frees both ports, and the supervisor reports the shutdown.)
    for command in ("Start STC SlabLab.command", "Stop Backend.command",
                    "Setup Backend.command", "Prepare Offline Data.command"):
        check("the packager installs " + command, command in packager)

    # The packaged README is the teacher's only documentation. It has to cover
    # the capabilities the app now has, or nobody will find them.
    delivered_readme = (REPO / "Packaging/macOS" /
                        "README-START-HERE.txt").read_text(encoding="utf-8")
    for label, snippet in (
            ("shortcuts are documented", "KEYBOARD AND HINTS"),
            ("the step keys are named", "Page Up / Page Down"),
            ("the snapshot key is named", "P "),
            ("the data-source states are named",
             "DATA SOURCE \u00b7 LOCAL CACHE (NO NETWORK)"),
            ("the cold-start self-heal is explained", "keeps trying by itself"),
            ("an unbundled window points at the fix",
             "neither live nor bundled"),
    ):
        check("packaged README: " + label, snippet in delivered_readme)

    for script in ("Start-Backend.sh", "Stop-Backend.sh",
                   "tools/run_unity_tests.sh",
                   "tools/release-check.sh",
                   "Packaging/macOS/package-macos.sh"):
        path = REPO / script
        result = subprocess.run(["bash", "-n", str(path)],
                                capture_output=True, text=True)
        check(script, result.returncode == 0, result.stderr.strip()[:120])
    release_check = (REPO / "tools" / "release-check.sh")
    check("release-check.sh is executable",
          release_check.is_file() and os.access(release_check, os.X_OK))
    for doc in ("docs/INTERACTION-GUARDRAILS.md",
                "docs/QUEST-HEADSET-CHECKLIST.md",
                "docs/OPEN-DECISIONS.md",
                "Packaging/macOS/README-START-HERE.txt"):
        check(doc, (REPO / doc).is_file())
    # Documentation that drifts is worse than none: the guardrail file once had
    # three sections numbered 7, which is exactly the kind of thing a reader
    # stops trusting a document over.
    guardrails = (REPO / "docs" / "INTERACTION-GUARDRAILS.md").read_text(
        encoding="utf-8")
    headings = re.findall(r"^## (\d+)\. ", guardrails, re.M)
    check("the guardrail sections are numbered 1..N in order",
          headings == [str(index) for index in range(1, len(headings) + 1)],
          "found %s" % headings)

    # The architecture note names the partial files and their count. A reader who
    # opens that folder sees fifteen look-alike names, so a stale count is worse
    # than none — cross-check it against the tree.
    readme_text = (REPO / "README.md").read_text(encoding="utf-8")
    check("the README maps the workbench partials",
          "Inside `Assets/VolumeSTCubeAPI/`" in readme_text and
          ("%d files" % WORKBENCH_PARTS) in readme_text,
          "expected the architecture note to say %d files" % WORKBENCH_PARTS)

    RESULTS.mkdir(parents=True, exist_ok=True)
    summary = ("PASS: contracts hold" if not failures
               else "FAIL: " + " | ".join(failures))
    (RESULTS / "contracts.txt").write_text(summary + "\n", encoding="utf-8")
    (RESULTS / "contracts.json").write_text(
        json.dumps({"failures": failures}, indent=2) + "\n",
        encoding="utf-8")
    print()
    print(summary)
    return 0 if not failures else 1


if __name__ == "__main__":
    raise SystemExit(main())
