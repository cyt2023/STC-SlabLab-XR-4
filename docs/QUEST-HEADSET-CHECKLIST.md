# Quest headset checklist

Everything about the Quest/VR branch that does not need a headset already runs on
the desktop: `VolumeSTCube > Desktop > Validate VR Flow` boots the VR mode in the
editor's preview, checks the mode split (world rig, no flat shell), the opening
step, a stage transition and that nothing logs an error. See
[INTERACTION-GUARDRAILS.md](INTERACTION-GUARDRAILS.md).

This file is for the part that needs the device.

## 0. Known blocker — the headset cannot be told where the backend is

The workspace starts the Wave (S4D) connection from
`s4dUrl = "http://127.0.0.1:8020"` (the field in `VolumeSTCubeQuestSpatialWorkbench.cs`)
and the only override is the `SlabLabSettings` preference:

```bash
rg -n 'GetWaveUrl|ClearWaveUrl|S4DUrl' RenderingModule/Assets --glob '!*.meta'
# SlabLabSettings reads or clears the preference; no runtime setter exists.
```

Nothing in the *runtime* writes it — unlike the MatPlot URL, which has an
on-device field (`VolumeSTCubeQuestWorkbench.cs` → `SlabLabSettings.SetMatPlotUrl`).
On a headset `127.0.0.1` is the headset itself, so the opening step cannot reach a
backend running on the PC, and there is no way to point it somewhere else from the
device.

Three ways forward; the first two are one small change each:

1. add an on-device field for the S4D URL beside the existing MatPlot one;
2. bake the PC's Tailscale address in as the build default (or as the fallback in
   `SlabLabSettings.GetWaveUrl`);
3. leave the Quest path as editor-preview-only for now.

## 1. What you need

* Quest in developer mode, `adb` working (`adb devices` lists it).
* The APK: Unity → `VolumeSTCube > Desktop > Build Android Tablet APK`
  → `RenderingModule/Builds/SlabLab-Flat-Tablet.apk`.
* The dataset folder for the headset: `For_VR/UnityRaw` (the same one the desktop
  app uses).
* A backend reachable from the headset — `./Start-Backend.sh --supervise` on the
  PC, with the headset and the PC on the same Tailscale network.

## 2. Install and launch

```bash
adb install -r RenderingModule/Builds/SlabLab-Flat-Tablet.apk
adb shell am start -n com.volumestcube.quest/com.unity3d.player.UnityPlayerActivity
```

## 3. Push the dataset

```bash
adb shell mkdir -p /sdcard/Android/data/com.volumestcube.quest/files/OneDrive_1_4-30-2026
adb push For_VR/UnityRaw/. \
  /sdcard/Android/data/com.volumestcube.quest/files/OneDrive_1_4-30-2026/
```

The workspace also accepts `…/files/Datasets`, and if it finds nothing it prints
the path it settled on in the opening screen's status line — read that instead of
guessing.

## 4. What to check, and what "pass" looks like

| # | Do this | Pass looks like | Already covered without a headset? |
|---|---|---|---|
| 1 | Open the app | The VR panels appear in the headset; the opening step lists `Wave_HS` and `Wave_Water_Level` | partially — boot, mode split and "data ready" are in `Validate VR Flow` |
| 2 | Aim a controller ray at a world-space button | The button highlights and the press registers | no — rays/grips are device-only |
| 3 | Hold grip / pinch and move | The rig moves (desktop analogue: `G` or middle-mouse drag) | no |
| 4 | Step to Configure Field and open Set Time Range | The time handles appear in the world and can be dragged; the readout follows | the state machine is covered, the dragging is not |
| 5 | Open the intent step and press VOICE | Android asks for microphone permission once; after granting, the transcript appears for review | no |
| 6 | Press TYPE instead | The Quest system keyboard opens; DONE returns the text to review | no |
| 7 | Confirm the intent and build the Full Matrix | The S4D job runs and the grid fills — **needs step 0 solved**, otherwise this is where it fails | the desktop path is covered; the headset needs a reachable URL |

## 5. When something fails

```bash
adb logcat -s Unity:V | grep -i "SlabLab"
adb shell run-as com.volumestcube.quest ls files/    # only on debuggable builds
```

The status line inside the headset repeats what the log says, so a screenshot of
the panel is usually enough to report a failure — and the same message exists in
the desktop guards, which makes it comparable.
