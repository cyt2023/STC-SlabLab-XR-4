# Interaction guardrails

How to improve the interface of STC SlabLab without breaking the behaviour that
was verified on 2026-09-23/24. The rule is simple: **make "it still works"
reproducible, and keep changes inside an allow-list.**

1. [Where a change is allowed to go](#1-where-a-change-is-allowed-to-go)
2. [What "still works" means — the frozen contracts](#2-what-still-works-means--the-frozen-contracts)
3. [Running the guards](#3-running-the-guards)
4. [The five steps for every interaction change](#4-the-five-steps-for-every-interaction-change)
5. [Interaction-specific risks worth a check](#5-interaction-specific-risks-worth-a-check)
6. [Current guard inventory](#6-current-guard-inventory)
7. [The Quest/VR branch has a run](#7-the-questvr-branch-has-a-run-not-just-an-audit)
8. [The stall window is a check now](#8-the-stall-window-is-a-check-now-not-a-procedure)
9. [Secret hygiene](#9-secret-hygiene)

## 1. Where a change is allowed to go

| Allowed (presentation and input only) | Not allowed without re-verifying the flow |
|---|---|
| `Assets/VolumeSTCubeAPI/…SpatialWorkbench.Chrome.cs` (drawing helpers, styles) | `…SpatialWorkbench.Dataset.cs` (Wave registration, manifests, variable loading) |
| `…SpatialWorkbench.Interaction.cs` (pointer, hover, drag, keyboard) | `…SpatialWorkbench.Analysis.cs` (S4D / MatPlot calls, jobs) |
| `…FlatScreenHUD.cs` (bottom bar, help and history overlays), `SlabLabShortcuts.cs` (the key table) | `VolumeSTCubeWaveClient.cs`, `VolumeSTCubeS4DAnalysisClient.cs`, `Services/**` |
| `SlabLabSnapshot.cs` (export helper) — additive, writes only under its own folder | `VolumeSTCubeWaveClient.cs`, `VolumeSTCubeS4DAnalysisClient.cs`, `Services/**` |
| `SlabLabBoundaryEntry.cs` (range parser) and the typed Time-range entry in `…Boundary.cs` — **additive input path**: the drag, the clamping and the confirm step are untouched, and the baseline drives the typed path end to end | the existing drag maths in `…Boundary.cs` and the confirm branch (`ApplyBoundaryChange`): re-verifying means `Validate All` |
| `…SpatialWorkbench.DesktopLayout.cs`, `VolumeSTCubeQuestRuntime.cs` — **view and recovery affordances only** (re-centre, reset view, framing helpers) | their existing framing maths: the recorded bounds below are what pins it, so any edit there must be followed by a full `Validate All` |
| New optional affordances: shortcuts, tooltips, help table, progress, export | `VolumeSTCubeWaveClient.cs`, `VolumeSTCubeS4DAnalysisClient.cs`, `Services/**` |
| `SlabLabLayout` / `SlabLabSettings` **values** (they are pinned by tests) | `…SpatialWorkbench.Phase.cs`, `…Jobs.cs`, `…State.cs` (state contracts) |

Additions are preferred over rewrites: a new shortcut, a new hint or a new
button must not change an existing click path, status text or step order.

## 2. What "still works" means — the frozen contracts

1. **Step order and status text** — opening screen → Configure Field → Define
   the Slab, with the same `SetStatus` wording for the key steps.
2. **Layout numbers** — `SlabLabLayout` constants and the measured viewport
   bounds of the Field pair (`x[0.229..0.858]`, centre `0.543`) and the tri-axis
   body (`x[0.806..1.178]`).
3. **The opening page states its data source** — `DATA SOURCE · LIVE` or
   `DATA SOURCE · LOCAL CACHE (NO NETWORK)`, taken from the service's `source`
   field, the line must not overlap any button, and it must actually be *drawn*
   (its TMP overlay's font has to fit its rect — see below).
4. **Recovery** — the `Reset View` button and the `X` key share one entry point
   that restores the authored viewpoint and re-centres the Field pair; the
   baseline proves it by disturbing the camera first and then pressing the
   button.
5. **Demo export** — the `Snapshot` button writes a real PNG under
   `Application.persistentDataPath/Captures`; the baseline presses the button
   and checks the file's signature and size before deleting it.
6. **Typed Time range** — `T` types the two cut days, clamped exactly like a
   dragged cut and previewed through the same calls, so `3,14` must read back as
   `before 1-3 / during 4-14 / after 15-24`. Invalid text keeps the entry open and
   never moves a cut.
7. **Cold start** — the packaged app starts its bundled backend in the same
   launch that opens the opening screen, so the first registration can lose that
   race. `WatchWaveImport` (driven by the desktop update loop) keeps asking: three
   quick retries, then a slow one, and it says so in the status line. The shipped
   app was cold-started with no backend to prove it: failure → "Reconnecting to
   the Wave server (retry 1 of 3)..." → "Detected 2 variables", no clicks. A
   *silent* server still ends on the 45 s stall message and the operator's RETRY.
8. **State** — `Phase` derivation, `PendingJob` bits, and "no pending job after
   a step settles" (`pendingJobs == None`).
9. **Timing** — the opening screen reports its own `Start()` → dataset time
   (`DesktopStartupSeconds`, budget 15 s) and Steps 2/3 must settle within 6 s of
   the click (each number includes the probe's own 1.5 s quiet window). They are
   written to the artifact, so a slow trend shows up in the diff.
10. **Persistence** — `SlabLabSettings` keys and defaults; the workbench never
   calls `PlayerPrefs` directly.
11. **Service contract** — the S4D endpoint set and JSON shapes, plus the
   offline bundle behaviour (`waveMode: live | cache | offline`).

The timings came from a review of the "the left field takes a while to appear"
fix: numbers are what makes that fix checkable. Writing them down immediately
paid off — Step 3 reported `0.02 s`, which is impossible, and that is how the
probe's quiet window was found leaking from one phase into the next.

### The shipped app had to be run (2026-09-24, later)

Every guard above runs inside the Unity editor, and the packaged app had only
ever been checked for zip integrity, code signature and a backend smoke test.
Launching the real bundle changed that: a genuine cold start does log

```
Wave import failed: Cannot connect to destination host
```

because the app starts its bundled backend in the same launch that opens the
opening screen and the first registration loses that race.

The first reading of that log stopped there and concluded the operator had to
press RETRY. That was wrong, and the mistake is worth recording: the log had been
read with `tail`, so it ended before the recovery that was already happening. The
full sequence on the *unmodified* mechanism is

```
Wave import failed: Cannot connect to destination host
Reconnecting to the Wave server (retry 1 of 3)...
Detected 2 variables in .../wave-imports/wave_live_.../UnityRaw.
Wave server data is ready. Review the detected variables, then continue.
```

`WatchWaveImport` — which existed all along — kept asking and healed the cold
start by itself. A second retry layer was added on top of it during that
misreading and has since been removed, so there is one mechanism again; the guard
in `VolumeSTCubeHistoryTests` now protects *that* one (entry point, update-loop
call, bounded quick retries, and the "still waiting" state for a silent server).

Three lessons kept in the process: run the artifact, not only the editor; read a
log to its end before concluding (a `tail` cut the evidence off); and look for the
mechanism that already exists before adding a second one — the redundancy cost
more than the bug it was meant to fix, because it also changed what the operator
sees during a cold start. And when only the last build line is read, a stale log
can hide a failure — check that the last error is *older* than the last successful
build.

### "Present in the hierarchy" is not "drawn" — and how to actually measure it

The Step 1 data-source line is guarded by looking the label up in the panel. That
kind of assertion is blind to rendering: a label can be created, active, carry the
right string and still put nothing on the screen. A screenshot of the *packaged*
app seemed to show exactly that — the band between `CONTINUE` and `MODE` looked
empty — and a probe in the shipped build found a genuinely squeezed layout
(`fontSize=36.0` in a `22.0`-tall rect), which is worth fixing whether or not the
glyphs actually clip. The rect was raised to the height that geometry needs, and
the baseline asserts the deterministic half of the claim: the overlay is active
and its font fits its rect (`fontSize <= rect height`; on the old numbers that is
`36 > 22.5`, so the check is not vacuous).

Three attempts were made at the *pixels*, and only the third is any good:

1. **Mapping the label's rect into the capture** — wrong: the panel canvas is
   nested, the rectangle came out in a screen corner, and the result read like a
   product failure. Removed.
2. **Showing and hiding the label, then differencing** — wrong: the signal was
   ~11.9k pixels, but two consecutive frames of the same scene (both with the
   label hidden, used as the noise floor) differed by **1.56 M** pixels. An
   animated opening screen plus editor capture timing swamps it. Removed.
3. **A colour nothing else can have, with a positive control** — right. Paint the
   label magenta, capture one frame, and count magenta pixels; paint a
   certainly-visible sibling the same way first, so a zero can never be blamed on
   a broken instrument. The measured result:

   ```
   controlProbePixels:  32448   (the "VARIABLES FOUND" label)
   dataSourceProbePixels: 10745 (the DATA SOURCE line)
   ```

   The line **is** drawn. The earlier suspicion was wrong, the retraction of it
   was right, and the claim is now asserted on every run instead of argued about.

The lesson kept: a hierarchy assertion is not a rendering assertion, and a pixel
assertion is only worth having if its own noise is understood — or, better,
designed out.

### A measurement you can trust has to flinch

Numbers only prove something if the instrument would notice a change. The
baseline therefore **disturbs the view on purpose**: it yaws the camera 30°,
requires the tri-axis bounds to move (they do — `x[0.806..1.178]` becomes
`x[0.680..0.867]`), then runs the Reset-view path and requires the recorded
number back to within 0.02. A guard that cannot fail is not a guard, so this
check fails loudly if a future change makes the camera independent of the
measurement.

Each of those has an executable guard (below), so a violation fails a test
instead of being noticed in a demo.

## 3. Running the guards

The 2026-10-02 refactor completed the workbench ownership split (23 files).
Every one of its 1,071 member units was checked against a saved source baseline;
245 methods and the Android speech adapter moved without changing their bodies, and field initialization order
stayed in place. Lifecycle messages now live in the main file. Matrix, Draft,
History, Findings, Field, Speech, Workflow and Buckets have dedicated files.
The same viewport constants and interaction assertions still apply. See
[REFACTOR-VERIFICATION.zh-CN.md](REFACTOR-VERIFICATION.zh-CN.md).

To exercise all six desktop stages without remote model calls, start the
isolated server in a terminal:

```bash
.venv/bin/python tools/run_workflow_test_server.py
```

Then choose `VolumeSTCube > Desktop > Validate Full Workflow` in Unity. The run
uses real Wave cache registration, RAW reading, analysis routes, deterministic
MatPlotAgent rendering, snapshots, Ground reconstruction and findings routing.
Only remote generation and interpretation are fixtures. It drives the runtime
controller actions, checks history restoration, fails on runtime errors, and
captures every page before advancing. Each run gets a fresh screenshot folder;
the latest result is `.runtime/full-workflow/workflow.txt`. Stop the test server
afterwards with Ctrl+C. The normal services at 8010/8020 and stored URLs are not
redirected by this check.

**One command for everything** (this is the one to remember):

```bash
./tools/release-check.sh            # contracts + pytest + the Unity guards
./tools/release-check.sh --quick    # contracts + pytest only (~2 s)
./tools/release-check.sh --package  # also verify the macOS delivery
./tools/release-check.sh --stall    # also spend 45 s proving the stall window
./tools/release-check.sh --vr       # also run the Quest/VR branch
```

It drives the Unity half the way the machine allows: with the editor closed it
runs `tools/run_unity_tests.sh` in batch mode, and with the editor open it clicks
the very same `VolumeSTCube > Desktop > Validate All` menu item a person would,
then waits for the artifact. `--package` also checks the zip, the code
signature, and the packaged `.env` permissions, and then answers three questions
about whether the delivery really *is* the code:

* does the shipped `Assembly-CSharp.dll` contain the current interface strings?
* is the shipped binary newer than every runtime script? (a stale player used to
  be caught only by whoever remembered to rebuild)
* does the packaged backend and tooling match the repo byte for byte?
  (`Services/`, the MatPlot agent, `Start-Backend.sh`, all four double-clickable
  commands, `tools/prepare_wave_cache.py`, the README and the requirements file —
  a stale Python backend would otherwise ship silently)

A delivery that fails any of them is rejected rather than signed off. Every line
is also written to `.runtime/test-results/release-check.txt`.

In the editor (works while you are iterating):

```
VolumeSTCube > Desktop > Validate All        # edit-mode guards + interaction baseline
VolumeSTCube > Desktop > Validate Refactor   # layout / settings / phase / job / state contracts
VolumeSTCube > Desktop > Validate Interaction# drives Step 1→3 and compares measurements
VolumeSTCube > Desktop > Validate History    # workflow regressions
```

From a terminal (the interactive editor must be closed — Unity locks `Library/`):

```bash
./tools/run_unity_tests.sh          # shell contracts + in-editor guards
```

The repository-level contracts (numbers, keys, routes, scripts, docs) need no
Unity at all and run in a second:

```bash
.venv/bin/python tools/check_contracts.py
```

Artifacts, always overwritten, easy to diff between runs:

```
.runtime/test-results/guards-summary.txt         PASS/FAIL of the whole run
.runtime/test-results/guards.json                the same verdict, machine readable
.runtime/test-results/contracts.txt              repo-level contracts (no Unity needed)
.runtime/test-results/interaction-baseline.json  measured bounds, phase, pendingJobs
.runtime/test-results/interaction-baseline.txt   one-line verdict
.runtime/refactor-validation.txt                 edit-mode contracts
.runtime/history-validation.txt                  workflow regressions
.runtime/shortcut-validation.txt                 key table and typing gate
.runtime/export-validation.txt                   snapshot naming and PNG check
.runtime/hint-validation.txt                     hover hints cover the controls
.runtime/range-entry-validation.txt              typed range parsing and clamping
.runtime/stall-timeout-validation.txt            the 45 s window, measured
.runtime/vr-flow-validation.txt                  the Quest/VR branch, booted
```

`SKIPPED:` is not a pass. The baseline needs a live dataset in the open scene;
when the backend is down it reports `SKIPPED: interaction baseline (no live
dataset registered …)` and the summary deliberately refuses to say
`PASS: all guards hold`, so a green line always means the flow was really
measured.

### What the first real run found (2026-09-24)

The guards were written before they were ever executed, and executing them
found four defects — three in the guard rig, one real drift in the tests:

1. The baseline entered Play Mode and **never ran**: this project keeps the
   default *reload domain* on Enter Play Mode, so the `EditorApplication.update`
   subscription and every static field were gone before the workbench booted.
   It wrote no artifact and no log line, which is indistinguishable from "there
   was nothing to report". Fixed by parking the run in `SessionState` and
   re-attaching from `[InitializeOnLoadMethod]`.
2. `Validate All` folded the baseline verdict in through a closure over the
   runner's in-memory failure list — also wiped by the same reload, so the
   summary would have been lost. Fixed the same way.
3. The baseline drove `DesktopNextStep()` while Step 1/Step 2 were still
   loading. The desktop buttons reject input during a pending job, so the run
   recorded half-arranged bounds (`maxX 0.879` instead of `0.858`) and then
   timed out. Fixed by waiting for an app with no pending job that has held
   still for 1.5 s before every drive/measure step.
4. `PreviousHidesAxisAndIndependentFieldByStage` failed: it walked back from
   Step 3 with no dataset in the workbench, and the hardening work added a guard
   that refuses to open Step 2 without a dataset (`NavigateDesktopStep` sends the
   operator back to Step 1). The old assertion was written before that guard
   existed. The test now imports a dataset first — and a second test
   (`PreviousWithoutADatasetStaysOnTheImportStep`) locks the new behaviour, so
   the fix tightened the suite instead of loosening it.

Re-running after those fixes reproduced the recorded numbers exactly:
field pair `x[0.229..0.858]` centre `0.543`, tri-axis `x[0.806..1.178]`,
`pendingJobs: None`, `PASS: all guards hold`.

Backend side (independent):

```bash
.venv/bin/python -m pytest Services/S4DAnalysisService/tests -q   # 55 passed (2026-10-02)
```

## 4. The five steps for every interaction change

1. **Record the baseline** — run `Validate All`; `interaction-baseline.json`
   holds the numbers you are about to protect.
2. **Change only the allow-list** — prefer adding; never move an existing step,
   button or status string.
3. **Compile clean** — the last successful build must come after the last error
   in `~/Library/Logs/Unity/Editor.log`.
4. **Re-run the guards** — `Validate All` (or `run_unity_tests.sh`) and the
   pytest suite.
5. **Diff the artifacts** — compare `interaction-baseline.json` against the
   previous run and the step log; any unexpected delta means revert. The split
   was a pure move, so rolling back is cheap (`.runtime/pre-split-backup/`).

## 5. Interaction-specific risks worth a check

| Risk | How the guards catch it |
|---|---|
| A new shortcut steals keys from a text field | Shortcut handling goes through the shared table and is gated on `input.textInputActive` / `desktopInputEditingPrompt`; `TypingOwnsTheKeyboard` asserts a shortcut is refused and the stage does not move while typing |
| A shortcut steals a key that already does something | `tools/check_contracts.py` resolves every `Input.GetAxisRaw` the camera reads against `ProjectSettings/InputManager.asset` and fails if a key in `SlabLabShortcuts.Keys` is also a movement key. This caught the first version, which used the arrow keys: every step also nudged the rig, because `Horizontal`/`Vertical` bind left/right/up/down |
| A shortcut is advertised but wired to nothing (or the reverse) | `TheTableCoversEverythingTheHelpCardAdvertises` pins the table against the Help-card wording, and the repo contracts fail if the HUD hard-codes a step key instead of reading `SlabLabShortcuts` |
| Shortcut and button navigation drift apart | Both call `DesktopPreviousStep` / `DesktopNextStep`; the edit-mode guard drives the keys and the Play-mode run drives the HUD entry point end to end (`shortcutRoundTrip`) |
| The operator loses the view and has no way back | `viewRecovery` in the baseline: disturb → the bounds must move → Reset view → the bounds must return to the recorded value |
| A control's hint drifts from the key it names | `AHintCannotNameAKeyTheTableMoved` reads both tables: the hint that says "X" is checked against `SlabLabShortcuts.Map(KeyCode.X)`, and so on for Page Up/Down, P, H/F1 and T |
| A recovery key exists but nobody can find it | The Help card is built from `SlabLabShortcuts.HelpLine`, and the guard asserts the card names every advertised key |
| A step gets slow again (the "field takes a while" regression) | `timings` in the baseline: the opening screen, Step 2 and Step 3 each carry a budget, and the numbers are recorded for the diff |
| The opening step gives up when the backend is a few seconds late | The shipped app was cold-started with no backend; `Player.log` shows failure → "Reconnecting to the Wave server (retry 1 of 3)..." → "Detected 2 variables" with no clicks. `TheOpeningStepKeepsAskingForDataByItself` pins the mechanism (`WatchWaveImport`, its update-loop call, the quick-retry budget), and a silent server still ends on the 45 s stall message |
| An export silently writes nothing (or a truncated file) | `snapshot` in the baseline: press the real button, wait for a new file, require the PNG signature and > 1 KB, then delete it. `WaitForEndOfFrame` is pinned because `ScreenCapture` writes after the frame |
| A snapshot overwrites someone else's file | `NextPath` scans for a free name; `SnapshotNamesAreUniqueAndLandInTheCapturesFolder` asserts two shots in the same millisecond differ |
| A line is created but cannot be seen | Two checks in the baseline: `DataSourceLabelIsDrawn` (the overlay is active and its font fits its rect) and the colour probe (the label is painted a colour nothing else uses and must put ≥ 150 of it on screen — with a certainly-visible control label painted the same way, so a broken probe cannot masquerade as a missing line) |
| The typed range invents its own rules | `TypedCutDaysReachTheSameReadoutAsADrag` and `TypedCutsAreClampedLikeADraggedCut` run the same clamping the drag uses and assert the dialog's own readout; a repo contract asserts the apply path calls `UpdateTimeBoundaryHandles` / `PreviewBoundaryTime` / `BuildBoundaryPanel` like `NudgeActiveBoundary` does |
| A typo moves a boundary anyway | `DesktopApplyRangeEntry` keeps the entry open on unparsable text; the edit-mode guard asserts the cuts did not move |
| A drag leaves state behind | `interaction-baseline.json` reports `pendingJobs`; extend `StateStructsMatchTheRetiredFlags` for gesture flags |
| The operator cannot tell where a drag landed | Already handled, and now frozen: the drag reports itself while it moves (`Boundary preview: …`, plus the depth slice preview) and again when it lands (`Time interval grounded: day X to day Y.`, `Depth band grounded: <band>.`). Time snaps to whole day cuts by construction, depth rounds to its layer lattice on release. Verified by reading both release paths; **not** by a guard, because pointer drags cannot be synthesised here — the two messages are in `STATUS_WORDING` so they can at least not disappear |
| Hover work slows the frame | Compare the import timing lines in `Editor.log` between runs |
| Colour becomes the only signal | Keep the existing colour **plus** label pairing for tokens |
| A control drifts to the edge | The measured bounds above (that is how the tri-axis and Field-pair regressions were caught) |

## 6. Current guard inventory

| Guard | Runs in | Covers |
|---|---|---|
| `VolumeSTCubeRefactorTests` (6) | Edit mode | layout constants, PlayerPrefs keys/defaults, "no direct PlayerPrefs", `Phase` derivation, `PendingJob` bits, state structs |
| `VolumeSTCubeHistoryTests` (8) | Edit mode | committed analysis ownership, bucket copies, live time range, stage transitions, "no dataset means no step 2", the opening step keeps asking by itself |
| `VolumeSTCubeShortcutTests` (3) | Edit mode | key table vs Help card, keys drive the same navigation as the buttons, typing owns the keyboard |
| `VolumeSTCubeExportTests` (3) | Edit mode | snapshot naming, PNG signature check, HUD wiring |
| `VolumeSTCubeHintTests` (3) | Edit mode | every control that needs explaining has a hint, a hint cannot name a key the shortcut table moved, and the HUD shows it in the notice line |
| `VolumeSTCubeBoundaryEntryTests` (3) | Edit mode | typed cut days reach the same readout, the same clamps, and only a Time range opens the entry |
| `SlabLabSilentServer` + the baseline's stall phase | Play mode, opt-in | a server that accepts and never answers is reported after 45 s, and an early give-up is not credited |
| `VolumeSTCubeVrFlowTests` | Play mode, VR preview | the Quest/VR branch: mode split (world rig, no flat shell), boot to "data ready", the shared step transition, and zero logged errors |
| `VolumeSTCubeInteractionBaseline` | Play mode | Step 1→3 flow, Field-pair and tri-axis bounds, phase, pending jobs, the Step 1 data-source line, the HUD shortcut round trip, the disturbance/reset recovery probe, the Snapshot button, the typed Time range on the real boundary bar |
| `tools/check_contracts.py` (guard rig) | No Unity | numbers, keys, wording, routes, and that the guard rig still survives the Play Mode reload |
| `tools/release-check.sh` | Shell | runs all of the above in one command and, with `--package`, rejects a delivery that is older than the interface |
| `Services/S4DAnalysisService/tests` (48) | pytest | endpoints, offline cache, jobs, resolution, speech helpers, key redaction |

## 7. The Quest/VR branch has a run, not just an audit

The VR side of this project was previously "statically audited" — the coroutine
hosts, index guards and swallowed exceptions had been read, not run. That gap is
now partly closed: `VolumeSTCube > Desktop > Validate VR Flow` switches the
project to VR mode, enters Play Mode, and checks what a headset is not needed for

```
PASS: VR branch booted and stepped to Step 2 in 3.6s with no errors;
      stage Field, phase FieldLoading, 2 variable(s); world panels ok, phase ImportReady
```

* the mode split is real — VR mode builds the world-space `panelCanvas` and
  `boundaryCanvas` and does **not** build the flat-screen shell;
* the opening step reaches "data ready" and the shared stage transition still
  moves to Step 2, so the rig survives a step change;
* anything logged as an error, exception or assert during the run fails the
  check — captured with `Application.logMessageReceived`, which is how a VR-only
  exception would show up even when the screen looks fine;
* the operator's mode is restored afterwards, and the clock is stored as ticks:
  formatting a `DateTime` and parsing it back mixed local and UTC time and
  produced a verdict line claiming the run took **-7197 s**. A wrong number in an
  artifact is worse than no number, so it is a contract now.

What it explicitly does **not** cover: headset input — hand rays, grip/pinch,
locomotion, the Quest system keyboard. Those need the device, and the check says
so rather than implying more than it proves.
[docs/QUEST-HEADSET-CHECKLIST.md](QUEST-HEADSET-CHECKLIST.md) is the device-side
list, including one blocker it found: the headset cannot be told where the S4D
backend is (only the MatPlot URL has an on-device field).

## 8. The stall window is a check now, not a procedure

The 45 s stall timeout used to be verified by hand: write a PlayerPrefs override
to point the workspace at a port that accepts and never answers, start a silent
listener, open the app, wait a minute, read the log, then remember to put the
override back. It is now one opt-in phase of the baseline:

```bash
./tools/release-check.sh --stall        # adds ~50 s
# or: VolumeSTCube > Desktop > Validate Stall Timeout
```

It reports `PASS: a silent Wave server is reported after 45.0s (window 45s)`
into `.runtime/stall-timeout-validation.txt`, and the check refuses to credit a
message that arrives early (anything under 40 s means the client gave up for a
different reason and is reported as a failure).
The verdict file is written in **both** directions: it used to appear only on
success, so `--stall` would wait out its whole 300 s budget on a failure and then
report "wrote no verdict" instead of the actual reason (found by deliberately
occupying the silent port, which now yields
`FAIL: the silent server would not start (port 8099 busy?)` in about a minute).

Three things that had to be learned the hard way, all of them now written into
the code:

1. **The silent listener cannot live in the guard.** Entering Play Mode reloads
   the domain, so a managed `TcpListener` is collected and the port closes; the
   client then reports "cannot connect", which is a different failure. The
   listener is a separate `python3` process (`SlabLabSilentServer`).
2. **The run flag cannot be a plain static either.** `stallRequested` came back
   `false` after the reload and the phase silently never ran. It lives in
   `SessionState` now, like the rest of the run's intent.
3. **Pointing the app somewhere else must not touch what the operator owns.**
   The old procedure wrote `VolumeSTCube.Quest.S4DUrl`; when a run was
   interrupted that override stayed behind and the app kept talking to a dead
   port — it took a `defaults read` to find. The guard changes the live
   component's `serviceBaseUrl` instead, and
   `VolumeSTCube > Desktop > Reset Wave URL Override` exists so a human can
   always get back to the built-in gateway.

## 9. Secret hygiene

The packaged app ships a `.env` holding the Wave API key, which was a deliberate
choice; the job of the guards is to keep that key from travelling any further.

| Requirement | How it is checked |
|---|---|
| `.env` never enters git | `tools/check_contracts.py` runs `git ls-files --error-unmatch .env` and fails if it is tracked |
| Other accounts cannot read it | the repo copy must be mode `600`, and `package-macos.sh` must `chmod 600` the copy it bakes into the zip |
| No endpoint echoes it | the key is sent as the `X-API-Key` header (never a query parameter, which would land in access logs); `/health`, `/wave/status` and `/datasets` are swept with the real key |
| An upstream error cannot hand it back | `_redact()` strips the key from quoted gateway bodies; `test_an_echoing_gateway_cannot_leak_the_key` drives a gateway that echoes the key back and asserts both the status payload and the raised message show `<redacted>` instead |
| It is not in the process table | the launcher passes it through the environment, not argv (`ps` sweep) |
