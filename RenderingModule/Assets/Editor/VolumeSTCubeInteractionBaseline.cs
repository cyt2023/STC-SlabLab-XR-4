#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityVolumeRendering;

namespace UnityVolumeRendering.Tests
{
    /// <summary>
    /// The interaction baseline, taken from the numbers measured by hand while
    /// the workflow was verified. It drives the same path an operator takes
    /// (opening screen -> Configure Field -> Define the Slab), measures the
    /// visible bounds of the Field pair and the tri-axis body, and fails when any
    /// of them moves past the recorded tolerance or when a pending job is left
    /// behind. Every run writes its measurements to
    /// .runtime/test-results/interaction-baseline.json so a diff is explicit.
    ///
    /// The run is driven by EditorApplication.update (Play Mode advances on the
    /// main loop, so a blocking wait would freeze it); the same state machine
    /// serves the menu item and the command line runner. Because entering Play
    /// Mode reloads the domain (wiping every static field and the update
    /// subscription), the intent of a run is parked in SessionState and the
    /// machine is rebuilt on the other side of the reload.
    /// </summary>
    public static class VolumeSTCubeInteractionBaseline
    {
        private const BindingFlags Private =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private const string PendingKey = "SlabLab.InteractionBaseline.Pending";
        private const string GuardRunKey =
            "SlabLab.InteractionBaseline.GuardRun";
        private const string StallRequestKey =
            "SlabLab.InteractionBaseline.StallRequest";

        // Recorded while the workflow was verified 2026-09-23/24.
        private const float FieldPairMinX = 0.229f;
        private const float FieldPairMaxX = 0.858f;
        private const float FieldPairCentre = 0.543f;
        private const float AxisMinX = 0.806f;
        private const float AxisMaxX = 1.178f;
        private const float Tolerance = 0.02f;
        private const float BootTimeoutSeconds = 120.0f;
        private const float StepTimeoutSeconds = 40.0f;
        private const float SettleSeconds = 1.5f;

        private sealed class Measurement
        {
            public string label;
            public bool visible;
            public float minX;
            public float maxX;
            public float minY;
            public float maxY;
            public string phase;
            public string pendingJobs;
        }

        private static readonly List<Measurement> Measurements =
            new List<Measurement>();
        private static readonly List<string> Failures = new List<string>();

        private static VolumeSTCubeQuestSpatialWorkbench workbench;
        private static int phase;
        private static float phaseUntil;
        private static float heldSince;
        private static int settledPhase = -1;
        private static float deadline;
        private static string skipReason;
        private static bool running;
        private static bool reportToGuard;
        private static bool stallRequested;
        private static string stallUrlBefore;
        private static float stallStartedAt;
        private static float stallSeconds;
        private static string stallFailure;
        private static string dataSourceLabel;
        private static string dataSourceOverlap;
        private static bool dataSourceDrawn;
        private static string shortcutRoundTrip;
        private static string viewRecovery;
        private static string viewRecoveryPath;
        private static bool locomotionWasEnabled = true;
        private static string[] snapshotBefore = new string[0];
        private static string snapshotResult;
        private static string typedRange;
        // Timings. The Field pair used to take ~8 s to appear after Step 2; a
        // budget keeps that fix from regressing silently. Each step number
        // includes the 1.5 s quiet window the probe itself insists on, so the
        // budget is quoted with that inside it.
        private static float bootStartedAt;
        private static float stepStartedAt;
        private static float bootSeconds;
        private static float step2Seconds;
        private static float step3Seconds;
        private const float BootBudgetSeconds = 15.0f;
        private const float StepBudgetSeconds = 6.0f;

        [MenuItem("VolumeSTCube/Desktop/Validate Interaction", priority = 40)]
        private static void RunFromMenu()
        {
            Start(false);
        }

        /// <summary>
        /// The stall check needs 45 s of silence, so it is opt-in: the ordinary
        /// Validate All run stays quick, and this menu item adds the phase that
        /// points the client at a server which accepts and never answers.
        /// </summary>
        [MenuItem("VolumeSTCube/Desktop/Validate Stall Timeout", priority = 42)]
        private static void RunStallFromMenu()
        {
            Start(false, true);
        }

        /// <summary>
        /// Rebuilds the state machine after the domain reload that Enter Play
        /// Mode performs. Without this the run started, reloaded and then did
        /// nothing at all — the first attempt at this baseline left no artifact
        /// and no log line, which read like "nothing to report" instead of
        /// "the check never ran".
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ReattachAfterDomainReload()
        {
            if (running || !SessionState.GetBool(PendingKey, false))
                return;
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // A stale flag from an interrupted run must not fire later.
                SessionState.EraseBool(PendingKey);
                return;
            }
            reportToGuard = SessionState.GetBool(GuardRunKey, false);
            stallRequested = SessionState.GetBool(StallRequestKey, false);
            Measurements.Clear();
            Failures.Clear();
            skipReason = null;
            workbench = null;
            phase = 0;
            phaseUntil = 0.0f;
            heldSince = 0.0f;
            dataSourceLabel = null;
            dataSourceOverlap = null;
            dataSourceDrawn = false;
            colorProbeDone = false;
            colorProbePath = null;
            dataSourcePixels = 0;
            controlProbePixels = 0;
            colorProbeStep = 0;
            shortcutRoundTrip = null;
            viewRecovery = null;
            viewRecoveryPath = null;
            snapshotResult = null;
            typedRange = null;
            bootSeconds = 0.0f;
            step2Seconds = 0.0f;
            step3Seconds = 0.0f;
            running = true;
            deadline = Time.realtimeSinceStartup + BootTimeoutSeconds;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        /// <summary>
        /// Starts the run. <paramref name="forGuardRunner"/> folds the verdict
        /// into the "Validate All" summary; otherwise it is only logged.
        /// </summary>
        public static void Start(bool forGuardRunner, bool includeStall = false)
        {
            if (running)
            {
                Debug.LogWarning("Interaction baseline is already running.");
                return;
            }
            Measurements.Clear();
            Failures.Clear();
            skipReason = null;
            workbench = null;
            phase = 0;
            phaseUntil = 0.0f;
            heldSince = 0.0f;
            dataSourceLabel = null;
            dataSourceOverlap = null;
            dataSourceDrawn = false;
            colorProbeDone = false;
            colorProbePath = null;
            dataSourcePixels = 0;
            controlProbePixels = 0;
            colorProbeStep = 0;
            shortcutRoundTrip = null;
            viewRecovery = null;
            viewRecoveryPath = null;
            snapshotResult = null;
            typedRange = null;
            bootSeconds = 0.0f;
            step2Seconds = 0.0f;
            step3Seconds = 0.0f;
            running = true;
            reportToGuard = forGuardRunner;
            stallRequested = includeStall;
            // Enter Play Mode reloads the domain, so the intent has to survive
            // it: a plain static came back false and the stall phase silently
            // never ran (the same trap the run itself already documents).
            SessionState.SetBool(StallRequestKey, includeStall);
            SessionState.SetBool(PendingKey, true);
            SessionState.SetBool(GuardRunKey, forGuardRunner);
            if (!EditorApplication.isPlaying)
            {
                VolumeSTCubeMode.SetStartupPreference(
                    VolumeSTCubeApplicationMode.Desktop);
                EditorApplication.isPlaying = true;
                deadline = Time.realtimeSinceStartup + 30.0f;
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
                return;
            }
            deadline = Time.realtimeSinceStartup + BootTimeoutSeconds;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (phase == 0)
            {
                if (!EditorApplication.isPlaying)
                {
                    if (Time.realtimeSinceStartup > deadline)
                    {
                        Finish(false, "Play Mode never started");
                        return;
                    }
                    return;
                }
                phase = 1;
                bootStartedAt = Time.realtimeSinceStartup;
                deadline = Time.realtimeSinceStartup + BootTimeoutSeconds;
                return;
            }

            if (phase == 1)
            {
                workbench = UnityEngine.Object.FindObjectOfType<
                    VolumeSTCubeQuestSpatialWorkbench>();
                if (workbench != null && DatasetCount(workbench) > 0)
                {
                    // The app's own number: Start() until the opening screen had
                    // a dataset. (bootStartedAt is the probe becoming active,
                    // which is always later and would understate this.)
                    bootSeconds = workbench.DesktopStartupSeconds >= 0.0f
                        ? workbench.DesktopStartupSeconds
                        : Time.realtimeSinceStartup - bootStartedAt;
                    phase = 2;
                    heldSince = 0.0f;
                    deadline = Time.realtimeSinceStartup + BootTimeoutSeconds;
                    return;
                }
                if (Time.realtimeSinceStartup > deadline)
                {
                    skipReason = "no live dataset registered (backend not " +
                        "running? see .runtime/backend/logs)";
                    StopAndFinish(true);
                }
                return;
            }

            switch (phase)
            {
                case 2:                        // drive to Step 2
                    // Step 1 keeps importing after the dataset is registered
                    // (manifest, then variable load). Pressing Next during that
                    // window is rejected with "Please wait for the current
                    // operation to finish", so wait for an idle, settled app.
                    if (!Settled(() => StageIs("DatasetImport")))
                    {
                        if (Time.realtimeSinceStartup > deadline)
                        {
                            Failures.Add("the opening step never became idle");
                            StopAndFinish(false);
                        }
                        return;
                    }
                    // Step 1 must state which source answered (live gateway or
                    // the bundled snapshot). Recorded here so an interaction
                    // change cannot silently drop the line.
                    dataSourceLabel = FindDataSourceLabel();
                    if (string.IsNullOrEmpty(dataSourceLabel))
                        Failures.Add("Step 1 does not state the data source");
                    dataSourceDrawn = DataSourceLabelIsDrawn(out string drawNote);
                    if (!dataSourceDrawn)
                        Failures.Add("the DATA SOURCE line is in the panel but " +
                            "not drawn: " + drawNote);
                    dataSourceOverlap = PanelOverlapWithDataSource();
                    if (dataSourceOverlap != null)
                        Failures.Add("the DATA SOURCE line overlaps \"" +
                            dataSourceOverlap + "\"");
                    // Does it actually reach the screen? Region mapping was
                    // wrong once and frame differencing was drowned by the
                    // animated screen; asking a question no other pixel can
                    // answer is immune to both.
                    // States: 0 start, 1 control capture pending, 2 counted and
                    // the real capture pending, 3 done. Each state is entered
                    // once — the first version re-entered its start state every
                    // frame and quietly wrote a capture per frame until the
                    // editor was stuck in Play Mode with 2,500 PNGs in the temp
                    // folder.
                    if (colorProbeStep < 3)
                    {
                        if (colorProbeStep == 0)
                        {
                            colorProbeStep = StartColorProbe(control: true)
                                ? 1 : 3;
                            return;
                        }
                        if (!File.Exists(colorProbePath))
                        {
                            if (Time.realtimeSinceStartup > colorProbeDeadline)
                            {
                                Failures.Add("could not capture the opening " +
                                    "screen for its colour probe");
                                colorProbeStep = 3;
                            }
                            return;
                        }
                        // The control first: a label that is certainly visible
                        // has to come back in the probe colour, otherwise the
                        // probe itself is broken and a zero for the real line
                        // would be a lie.
                        CheckColorProbe(control: colorProbeStep == 1);
                        colorProbeStep = colorProbeStep == 1
                            ? (StartColorProbe(control: false) ? 2 : 3)
                            : 3;
                        return;
                    }
                    stepStartedAt = Time.realtimeSinceStartup;
                    heldSince = 0.0f;
                    workbench.DesktopNextStep();
                    phase = 3;
                    deadline = Time.realtimeSinceStartup + StepTimeoutSeconds;
                    return;
                case 3:                        // wait for Step 2, then measure
                    // The field is still being arranged right after the stage
                    // switches: measuring there records half-loaded bounds
                    // (that is how the first successful run read maxX 0.879
                    // instead of the recorded 0.858).
                    if (Settled(() => StageIs("Field")))
                    {
                        step2Seconds = Time.realtimeSinceStartup - stepStartedAt;
                        heldSince = 0.0f;
                        Measure("field-pair");
                        Set(workbench, "authorBoundaryConfirmed", true);
                        stepStartedAt = Time.realtimeSinceStartup;
                        workbench.DesktopNextStep();
                        phase = 4;
                        deadline = Time.realtimeSinceStartup + StepTimeoutSeconds;
                        return;
                    }
                    if (Time.realtimeSinceStartup > deadline)
                    {
                        Failures.Add(
                            "stage never settled on Field after CONTINUE");
                        StopAndFinish(false);
                    }
                    return;
                case 4:                        // wait for Step 3, then measure
                    if (Settled(() => StageIs("Slab")))
                    {
                        step3Seconds = Time.realtimeSinceStartup - stepStartedAt;
                        heldSince = 0.0f;
                        Measure("tri-axis");
                        phase = 5;
                        phaseUntil = Time.realtimeSinceStartup + SettleSeconds;
                        return;
                    }
                    if (Time.realtimeSinceStartup > deadline)
                    {
                        Failures.Add(
                            "stage never settled on Slab after SET TIME RANGE");
                        StopAndFinish(false);
                    }
                    return;
                case 5:                        // let the slab layout settle, re-measure
                    if (Time.realtimeSinceStartup < phaseUntil)
                        return;
                    Measurements.RemoveAll(item => item.label == "tri-axis");
                    Measure("tri-axis");
                    CheckFieldPair();
                    CheckTriAxis();
                    CheckTimings();
                    phase = 6;
                    return;
                case 6:                        // arrow keys drive the real HUD path
                    CheckShortcutRoundTrip();
                    phase = 7;
                    phaseUntil = Time.realtimeSinceStartup + SettleSeconds;
                    return;
                case 7:                        // remember what recovery must give back
                    if (Time.realtimeSinceStartup < phaseUntil)
                        return;
                    // Freeze the camera rig for the probe. Otherwise a wheel or a
                    // right-drag by whoever is at the machine lands in the middle
                    // of the measurement and the "before" numbers stop describing
                    // the state the reset has to restore.
                    FreezeLocomotion(true);
                    Measure("slab-field-before");
                    Measure("slab-axis-before");
                    DisturbCamera();
                    phase = 8;
                    phaseUntil = Time.realtimeSinceStartup + SettleSeconds;
                    return;
                case 8:                        // the disturbance must be visible
                    if (Time.realtimeSinceStartup < phaseUntil)
                        return;
                    Measure("slab-field-disturbed");
                    Measure("slab-axis-disturbed");
                    if (!DisturbanceIsVisible())
                        Failures.Add("a 30 degree camera yaw did not move the " +
                            "recorded bounds, so this guard would never notice " +
                            "a lost view");
                    ResetView();
                    phase = 9;
                    phaseUntil = Time.realtimeSinceStartup + SettleSeconds;
                    return;
                case 9:                        // recovery must restore the framing
                    if (Time.realtimeSinceStartup < phaseUntil)
                        return;
                    Measure("slab-field-recovered");
                    Measure("slab-axis-recovered");
                    CheckViewRecovery();
                    FreezeLocomotion(false);
                    phase = 10;
                    phaseUntil = Time.realtimeSinceStartup + SettleSeconds;
                    return;
                case 10:                       // the Snapshot button writes a PNG
                    if (Time.realtimeSinceStartup < phaseUntil)
                        return;
                    snapshotBefore = SnapshotFiles();
                    Button snapshotButton = FindButton("Snapshot");
                    if (snapshotButton == null)
                    {
                        Failures.Add("the action bar has no Snapshot button");
                        snapshotResult = "no button";
                        StopAndFinish(false);
                        return;
                    }
                    snapshotButton.onClick.Invoke();
                    phase = 11;
                    deadline = Time.realtimeSinceStartup + 10.0f;
                    return;
                case 11:                       // wait for the file to land
                    string written = NewSnapshotFile();
                    if (written != null)
                    {
                        CheckSnapshotFile(written);
                        phase = 12;
                        phaseUntil = Time.realtimeSinceStartup + SettleSeconds;
                        return;
                    }
                    if (Time.realtimeSinceStartup > deadline)
                    {
                        Failures.Add("the Snapshot button did not write a PNG");
                        snapshotResult = "nothing written";
                        StopAndFinish(false);
                    }
                    return;
                case 12:                       // type an exact Time range for real
                    if (Time.realtimeSinceStartup < phaseUntil)
                        return;
                    if (!OpenBoundaryEditor())
                    {
                        StopAndFinish(Failures.Count == 0);
                        return;
                    }
                    phase = 13;
                    phaseUntil = Time.realtimeSinceStartup + SettleSeconds;
                    return;
                case 13:                       // wait for the bar, then type a range
                    if (Time.realtimeSinceStartup < phaseUntil)
                        return;
                    CheckTypedTimeRange();
                    if (!stallRequested)
                    {
                        StopAndFinish(Failures.Count == 0);
                        return;
                    }
                    phase = 14;
                    phaseUntil = Time.realtimeSinceStartup + SettleSeconds;
                    return;
                case 14:                       // point the client at a silent server
                    if (Time.realtimeSinceStartup < phaseUntil)
                        return;
                    if (!BeginStallCheck())
                    {
                        // No point waiting 90 s for a message that cannot come:
                        // write the verdict now, so the gate reports the real
                        // reason instead of "wrote no verdict".
                        Failures.Add(stallFailure);
                        WriteStallReport(stallFailure);
                        StopAndFinish(false);
                        return;
                    }
                    phase = 15;
                    deadline = Time.realtimeSinceStartup + 90.0f;
                    return;
                case 15:                       // a stalled server must be reported
                    if (CheckStallReported())
                    {
                        StopAndFinish(Failures.Count == 0);
                        return;
                    }
                    if (Time.realtimeSinceStartup > deadline)
                    {
                        stallFailure = "a silent Wave server was never " +
                            "reported; last status: " + (StatusLine() ?? "<none>");
                        Failures.Add(stallFailure);
                        WriteStallReport(stallFailure);
                        StopAndFinish(false);
                    }
                    return;
                default:
                    StopAndFinish(Failures.Count == 0);
                    return;
            }
        }

        /// <summary>
        /// The edit-mode guards prove the key table and the workbench entry
        /// point. This drives the same call the flat-screen HUD makes when it
        /// sees an arrow key, so the wiring between the two is covered as well:
        /// back a step, forward again, and the workflow must land where the
        /// buttons would put it. Only the OS key event itself is not synthesised.
        /// </summary>
        private static void CheckShortcutRoundTrip()
        {
            var hud = UnityEngine.Object.FindObjectOfType<
                VolumeSTCubeFlatScreenHUD>();
            if (hud == null)
            {
                shortcutRoundTrip = "no flat-screen HUD in this scene";
                return;
            }
            if (!StageIs("Slab"))
            {
                shortcutRoundTrip = "not on Slab; skipped";
                return;
            }
            try
            {
                MethodInfo apply = hud.GetType().GetMethod("ApplyShortcut",
                    Private);
                if (apply == null)
                {
                    Failures.Add("the HUD no longer exposes ApplyShortcut");
                    return;
                }
                apply.Invoke(hud, new object[]
                {
                    SlabLabShortcut.PreviousStep, KeyCode.PageUp
                });
                if (!StageIs("Field"))
                {
                    Failures.Add("the previous-step shortcut did not " +
                        "return to Step 2");
                    shortcutRoundTrip = "failed";
                    return;
                }
                apply.Invoke(hud, new object[]
                {
                    SlabLabShortcut.NextStep, KeyCode.PageDown
                });
                if (!StageIs("Slab"))
                {
                    Failures.Add("the next-step shortcut did not " +
                        "return to Step 3");
                    shortcutRoundTrip = "failed";
                    return;
                }
                shortcutRoundTrip = "Field -> Slab";
            }
            catch (Exception exception)
            {
                Failures.Add("shortcut round trip threw: " + exception.Message);
                shortcutRoundTrip = "threw";
            }
        }

        private static void StopAndFinish(bool passed)
        {
            EditorApplication.update -= Tick;
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.update += StopTick;
                pendingResult = passed;
                deadline = Time.realtimeSinceStartup + 30.0f;
                return;
            }
            Finish(passed, null);
        }

        private static bool pendingResult;

        private static void StopTick()
        {
            if (EditorApplication.isPlaying)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    EditorApplication.update -= StopTick;
                    Finish(pendingResult, null);
                }
                return;
            }
            EditorApplication.update -= StopTick;
            Finish(pendingResult, null);
        }

        private static void Finish(bool passed, string note)
        {
            if (note != null && skipReason == null)
                skipReason = note;
            running = false;
            SessionState.EraseBool(PendingKey);
            SessionState.EraseBool(StallRequestKey);
            WriteReport(passed);
            workbench = null;
            if (reportToGuard)
                VolumeSTCubeGuardRunner.NotifyBaselineFinished(passed,
                    skipReason != null, skipReason);
            // The silent server is a separate process: stopping it before Play
            // Mode ends is what keeps it from being orphaned by the reload.
            SlabLabSilentServer.Stop();
        }

        // --------------------------------------------------------- measuring
        private static void Measure(string label)
        {
            var measurement = new Measurement { label = label };
            Camera camera = Get(workbench, "xrCamera") as Camera;
            if (label.Contains("field"))
            {
                GameObject surface = Get(workbench, "spatialRoot") as GameObject;
                Bounds(surface, camera, measurement);
                var player = Get(workbench, "forVrSurfacePlayer") as
                    VolumeSTCubeForVrSurfacePlayer;
                if (player != null && player.XytCompanion != null &&
                    player.XytCompanion.DesktopFieldTransform != null)
                {
                    var second = new Measurement();
                    if (Bounds(player.XytCompanion.DesktopFieldTransform.gameObject,
                            camera, second))
                        measurement.maxX = Mathf.Max(measurement.maxX, second.maxX);
                }
            }
            else
            {
                Bounds(Get(workbench, "spatialAxisComposerRoot") as GameObject,
                    camera, measurement);
            }
            measurement.phase = workbench.CurrentPhase.ToString();
            measurement.pendingJobs = (Get(workbench, "pendingJobs") ?? "None")
                .ToString();
            Measurements.RemoveAll(item => item.label == label);
            Measurements.Add(measurement);
        }

        private static bool Bounds(GameObject root, Camera camera,
            Measurement into)
        {
            if (root == null || camera == null)
                return false;
            into.minX = 1.0f;
            into.maxX = 0.0f;
            into.minY = 1.0f;
            into.maxY = 0.0f;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool projected = false;
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null || !renderer.enabled)
                    continue;
                if (renderer is LineRenderer)
                    continue;
                Material material = renderer.sharedMaterial;
                if (material != null)
                {
                    Color colour = material.HasProperty("_Color")
                        ? material.GetColor("_Color") : material.color;
                    if (colour.a < 0.35f)
                        continue;
                }
                Bounds bounds = renderer.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = bounds.center + new Vector3(
                        (corner & 1) == 0 ? -bounds.extents.x : bounds.extents.x,
                        (corner & 2) == 0 ? -bounds.extents.y : bounds.extents.y,
                        (corner & 4) == 0 ? -bounds.extents.z : bounds.extents.z);
                    Vector3 viewport = camera.WorldToViewportPoint(point);
                    if (viewport.z <= 0.0f)
                        continue;
                    into.minX = Mathf.Min(into.minX, viewport.x);
                    into.maxX = Mathf.Max(into.maxX, viewport.x);
                    into.minY = Mathf.Min(into.minY, viewport.y);
                    into.maxY = Mathf.Max(into.maxY, viewport.y);
                    projected = true;
                }
            }
            into.visible = projected;
            return projected;
        }

        private static void CheckFieldPair()
        {
            Check("field-pair", FieldPairMinX, FieldPairMaxX, true);
        }

        private static void CheckTriAxis()
        {
            Check("tri-axis", AxisMinX, AxisMaxX, false);
        }

        /// <summary>
        /// The opening screen, Step 2 and Step 3 all have to arrive inside a
        /// budget. These numbers are also written to the artifact, so a slow
        /// trend shows up in the diff before it becomes a complaint.
        /// </summary>
        private static void CheckTimings()
        {
            if (bootSeconds > BootBudgetSeconds)
                Failures.Add(string.Format(
                    "the opening screen took {0:0.0}s to have data " +
                    "(budget {1:0.0}s)", bootSeconds, BootBudgetSeconds));
            if (step2Seconds > StepBudgetSeconds)
                Failures.Add(string.Format(
                    "Step 2 took {0:0.0}s to settle after CONTINUE " +
                    "(budget {1:0.0}s)", step2Seconds, StepBudgetSeconds));
            if (step3Seconds > StepBudgetSeconds)
                Failures.Add(string.Format(
                    "Step 3 took {0:0.0}s to settle after SET TIME RANGE " +
                    "(budget {1:0.0}s)", step3Seconds, StepBudgetSeconds));
        }

        private static void Check(string label, float minX, float maxX,
            bool pair)
        {
            Measurement found = Measurements.Find(item => item.label == label);
            if (found == null || !found.visible)
            {
                Failures.Add(label + ": nothing visible in the viewport");
                return;
            }
            if (Mathf.Abs(found.minX - minX) > Tolerance ||
                Mathf.Abs(found.maxX - maxX) > Tolerance)
                Failures.Add(string.Format(
                    "{0}: x[{1:0.000}..{2:0.000}] moved (baseline " +
                    "x[{3:0.000}..{4:0.000}])", label, found.minX, found.maxX,
                    minX, maxX));
            if (pair)
            {
                float centre = (found.minX + found.maxX) * 0.5f;
                if (Mathf.Abs(centre - FieldPairCentre) > Tolerance)
                    Failures.Add(string.Format(
                        "{0}: centre {1:0.000} moved (baseline {2:0.000})",
                        label, centre, FieldPairCentre));
                if (found.phase != "FieldReady" && found.phase != "FieldLoading")
                    Failures.Add(label + ": phase " + found.phase +
                        " is not a Field phase");
            }
            else if (found.phase != "SlabAxisBinding")
            {
                Failures.Add(label + ": phase " + found.phase +
                    " is not SlabAxisBinding");
            }
            if (found.pendingJobs != "None")
                Failures.Add(label + ": pendingJobs left at " + found.pendingJobs);
        }

        // ----------------------------------------------------------- helpers
        private static bool StageIs(string stage)
        {
            object current = Get(workbench, "stage");
            return current != null && current.ToString() == stage;
        }

        /// <summary>Yaw the camera the way a stray right-drag does.</summary>
        private static void DisturbCamera()
        {
            Camera camera = Get(workbench, "xrCamera") as Camera;
            if (camera != null)
                camera.transform.Rotate(0.0f, 30.0f, 0.0f, Space.World);
        }

        private static void FreezeLocomotion(bool frozen)
        {
            var locomotion = UnityEngine.Object.FindObjectOfType<
                VolumeSTCubeQuestLocomotion>();
            if (locomotion == null)
                return;
            if (frozen)
            {
                locomotionWasEnabled = locomotion.enabled;
                locomotion.enabled = false;
                return;
            }
            locomotion.enabled = locomotionWasEnabled;
        }

        /// <summary>
        /// Did the reset put the camera back on the pose the runtime captured
        /// when the app started? This is the part of recovery that does not
        /// depend on where the camera happened to be before the probe.
        /// </summary>
        private static bool CameraIsAtAuthoredPose()
        {
            var locomotion = UnityEngine.Object.FindObjectOfType<
                VolumeSTCubeQuestLocomotion>();
            if (locomotion == null)
                return true;
            if (Get(locomotion, "initialPosition") is Vector3 position &&
                Vector3.Distance(locomotion.transform.position, position) > 0.01f)
                return false;
            if (Get(locomotion, "initialRotation") is Quaternion rotation &&
                Quaternion.Angle(locomotion.transform.rotation, rotation) > 0.1f)
                return false;
            Transform head = Get(locomotion, "head") as Transform;
            if (head != null &&
                Get(locomotion, "initialHeadEuler") is Vector3 euler &&
                Quaternion.Angle(Quaternion.Euler(head.localEulerAngles),
                    Quaternion.Euler(euler)) > 0.1f)
                return false;
            return true;
        }

        /// <summary>
        /// Recovery is driven through the on-screen "Reset View" button, which
        /// is what an operator reaches for. It has to land on the same entry
        /// point as the X key, so this covers the button wiring and the reset
        /// logic at once.
        /// </summary>
        private static void ResetView()
        {
            Button button = FindButton("Reset View");
            if (button != null)
            {
                viewRecoveryPath = "button";
                button.onClick.Invoke();
                return;
            }
            var locomotion = UnityEngine.Object.FindObjectOfType<
                VolumeSTCubeQuestLocomotion>();
            if (locomotion != null)
            {
                viewRecoveryPath = "key path (no button found)";
                locomotion.ResetDesktopView();
                return;
            }
            viewRecovery = "no way to reset the view in this scene";
        }

        private static Button FindButton(string caption)
        {
            Button[] buttons = UnityEngine.Object.FindObjectsOfType<Button>();
            for (int index = 0; index < buttons.Length; index++)
            {
                Text label = buttons[index].GetComponentInChildren<Text>(true);
                if (label != null && label.text == caption)
                    return buttons[index];
            }
            return null;
        }

        /// <summary>
        /// Step back to Step 2 and open its Time Range editor through the same
        /// action the SET TIME RANGE button calls, so the typed entry is
        /// exercised on the real boundary bar rather than on a bare object.
        /// </summary>
        private static bool OpenBoundaryEditor()
        {
            if (StageIs("Slab"))
                workbench.DesktopPreviousStep();
            if (!StageIs("Field"))
            {
                typedRange = "not on Step 2; skipped";
                return false;
            }
            Set(workbench, "authorBoundaryConfirmed", false);
            workbench.DesktopOpenFieldSetup();
            if (!StageIs("Field") || !workbench.DesktopBoundaryBarActive)
            {
                typedRange = "the Time Range editor did not open";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Drive the typed Time-range entry through the HUD shortcut — the same
        /// call the T key makes — and require the numbers the operator reads to
        /// change. The keystrokes themselves cannot be synthesised, so the buffer
        /// the key handler fills is set directly.
        /// </summary>
        private static void CheckTypedTimeRange()
        {
            var hud = UnityEngine.Object.FindObjectOfType<
                VolumeSTCubeFlatScreenHUD>();
            if (hud == null)
            {
                typedRange = "no flat-screen HUD in this scene";
                return;
            }
            MethodInfo apply = hud.GetType().GetMethod("ApplyShortcut", Private);
            if (apply == null)
            {
                Failures.Add("the HUD no longer exposes ApplyShortcut");
                return;
            }
            apply.Invoke(hud, new object[]
            {
                SlabLabShortcut.RangeEntry, KeyCode.T
            });
            string prompt = BoundaryReadout();
            if (string.IsNullOrEmpty(prompt) ||
                !prompt.Contains(SlabLabBoundaryEntry.TimePrompt))
            {
                Failures.Add("the typed range entry did not show its prompt: " +
                    (prompt ?? "no readout"));
                typedRange = "no prompt";
                return;
            }
            Set(workbench, "boundaryRangeEntryText", "3,14");
            workbench.DesktopApplyRangeEntry();
            string applied = BoundaryReadout();
            if (string.IsNullOrEmpty(applied) ||
                !applied.Contains("BEFORE 1-3") ||
                !applied.Contains("DURING 4-14"))
            {
                Failures.Add("the typed range did not reach the readout: " +
                    (applied ?? "no readout"));
                typedRange = "readout did not follow";
                return;
            }
            typedRange = applied.Replace("\n", " ");
            // Leave the workspace where the rest of the run expects it.
            workbench.DesktopCancelBoundary();
            Set(workbench, "authorBoundaryConfirmed", true);
        }

        /// <summary>
        /// The panel's labels are drawn by a TMP overlay that the legacy Text
        /// hands off to, and that overlay auto-sizes inside the label's rect. A
        /// label whose rect is barely taller than its font draws nothing at all —
        /// which is exactly what happened to the DATA SOURCE line in the packaged
        /// build while every hierarchy check still said "present".
        /// </summary>
        private static bool DataSourceLabelIsDrawn(out string note)
        {
            note = "no DATA SOURCE label to inspect";
            var content = Get(workbench, "panelContent") as RectTransform;
            if (content == null)
                return false;
                Text[] labels = content.GetComponentsInChildren<Text>(true);
            for (int index = 0; index < labels.Length; index++)
            {
                Text label = labels[index];
                if (label == null || label.text == null ||
                    !label.text.StartsWith("DATA SOURCE"))
                    continue;
                if (!label.gameObject.activeInHierarchy)
                    continue;             // a stale copy from an earlier rebuild
                TMPro.TextMeshProUGUI crisp =
                    label.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                if (crisp == null || !crisp.enabled ||
                    !crisp.gameObject.activeInHierarchy)
                {
                    note = "no active TMP overlay";
                    return false;
                }
                float height = crisp.rectTransform.rect.height;
                note = string.Format(
                    "font {0:0.0} in a {1:0.0} tall rect (text '{2}')",
                    crisp.fontSize, height, crisp.text);
                return crisp.fontSize <= height + 0.5f;
            }
            return false;
        }

        private static Text FindDataSourceLabelObject()
        {
            return FindLabel("DATA SOURCE");
        }

        /// <summary>
        /// The first *active* label in the panel whose text starts with the given
        /// prefix. Rebuilt panels leave stale copies behind, and the one on screen
        /// is the only one worth measuring.
        /// </summary>
        private static Text FindLabel(string prefix)
        {
            var content = Get(workbench, "panelContent") as RectTransform;
            if (content == null)
                return null;
            Text[] labels = content.GetComponentsInChildren<Text>(true);
            Text inactive = null;
            for (int index = 0; index < labels.Length; index++)
            {
                if (labels[index] != null && labels[index].text != null &&
                    labels[index].text.StartsWith(prefix))
                {
                    if (labels[index].gameObject.activeInHierarchy)
                        return labels[index];
                    if (inactive == null)
                        inactive = labels[index];
                }
            }
            return inactive;
        }

        // ------------------------------------------------------- stall timeout
        /// <summary>
        /// Point the client at a server that accepts and never answers, and ask
        /// it to import. The URL is changed on the live component (it is read per
        /// request), so nothing the operator owns has to be saved or restored —
        /// this used to be a PlayerPrefs edit plus a manual run.
        /// </summary>
        private static bool BeginStallCheck()
        {
            var client = Get(workbench, "waveClient") as VolumeSTCubeWaveClient;
            if (client == null)
            {
                stallFailure = "no Wave client to stall";
                return false;
            }
            if (!SlabLabSilentServer.Start())
            {
                stallFailure = "the silent server would not start (port " +
                    SlabLabSilentServer.Port + " busy?)";
                return false;
            }
            stallUrlBefore = client.serviceBaseUrl;
            client.serviceBaseUrl = "http://127.0.0.1:" + SlabLabSilentServer.Port;
            stallStartedAt = Time.realtimeSinceStartup;
            MethodInfo import = workbench.GetType().GetMethod(
                "ImportWaveServerDataset", Private);
            if (import == null)
            {
                stallFailure = "the import entry point is gone";
                return false;
            }
            import.Invoke(workbench, null);
            return true;
        }

        /// <summary>
        /// True once the 45 s window has been reported. A message that arrives
        /// much earlier would mean the client gave up for another reason, which
        /// must not be credited to the stall window.
        /// </summary>
        private static bool CheckStallReported()
        {
            string status = StatusLine();
            if (status == null || !status.StartsWith(
                    "The Wave server did not answer within 45s"))
                return false;
            stallSeconds = Time.realtimeSinceStartup - stallStartedAt;
            if (stallSeconds < 40.0f)
            {
                Failures.Add(string.Format(
                    "the stall message arrived after {0:0.0}s, which is not " +
                    "the 45s window", stallSeconds));
                return true;
            }
            var client = Get(workbench, "waveClient") as VolumeSTCubeWaveClient;
            if (client != null)
                client.serviceBaseUrl = stallUrlBefore;
            SlabLabSilentServer.Stop();
            WriteStallReport();
            return true;
        }

        private static void WriteStallReport(string failure = null)
        {
            string directory = Path.GetFullPath(Path.Combine(
                Application.dataPath, "../../.runtime"));
            Directory.CreateDirectory(directory);
            string verdict = failure == null
                ? string.Format(
                    "PASS: a silent Wave server is reported after {0:0.0}s " +
                    "(window 45s)", stallSeconds)
                : "FAIL: " + failure;
            File.WriteAllText(Path.Combine(directory,
                "stall-timeout-validation.txt"),
                verdict + "\n" + DateTime.UtcNow.ToString("O") + "\n");
        }

        // -------------------------------------------------------- colour probe
        private static readonly Color ProbeColour = Color.magenta;
        private static bool colorProbeDone;
        private static string colorProbePath;
        private static float colorProbeDeadline;
        private static Color labelColourBefore;
        private static int dataSourcePixels;
        private static int controlProbePixels;
        private static int colorProbeStep;

        /// <summary>
        /// Paint the label a colour nothing else on screen uses, capture a frame,
        /// and count that colour. Region mapping put the box in a corner once and
        /// a show/hide difference was drowned by a screen that moves 1.5 M pixels
        /// between frames; a colour only this label can have is immune to both.
        /// </summary>
        private static bool StartColorProbe(bool control)
        {
            Text label = control
                ? FindLabel("VARIABLES FOUND")
                : FindDataSourceLabelObject();
            if (label == null)
            {
                Failures.Add(control
                    ? "no control label to paint"
                    : "no DATA SOURCE label to paint");
                return false;
            }
            labelColourBefore = label.color;
            label.color = ProbeColour;
            TMPro.TextMeshProUGUI crisp =
                label.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (crisp != null)
                crisp.color = ProbeColour;
            colorProbePath = Path.Combine(Path.GetTempPath(),
                "slablab-colour-probe-" + DateTime.UtcNow.Ticks + ".png");
            ScreenCapture.CaptureScreenshot(colorProbePath, 1);
            colorProbeDeadline = Time.realtimeSinceStartup + 20.0f;
            return true;
        }

        private static void CheckColorProbe(bool control)
        {
            Text label = control
                ? FindLabel("VARIABLES FOUND")
                : FindDataSourceLabelObject();
            if (label != null)
            {
                label.color = labelColourBefore;
                TMPro.TextMeshProUGUI crisp =
                    label.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                if (crisp != null)
                    crisp.color = labelColourBefore;
            }
            var texture = new Texture2D(2, 2);
            try
            {
                if (!ImageConversion.LoadImage(texture,
                        File.ReadAllBytes(colorProbePath)))
                {
                    Failures.Add("the colour-probe capture could not be read");
                    return;
                }
                Color32[] pixels = texture.GetPixels32();
                int magenta = 0;
                for (int index = 0; index < pixels.Length; index++)
                {
                    Color32 colour = pixels[index];
                    // Magenta carries a lot of red and blue and almost no green;
                    // the thresholds are loose enough for anti-aliased glyph
                    // edges and tight enough that nothing else on this screen
                    // can satisfy them.
                    if (colour.r >= 150 && colour.b >= 150 &&
                        colour.g <= 110 && colour.r + colour.b >
                        2 * colour.g + 140)
                        magenta++;
                }
                dataSourcePixels = magenta;
                if (control)
                {
                    controlProbePixels = magenta;
                    if (magenta < 150)
                        Failures.Add(string.Format(
                            "the colour probe is not working: a label that is " +
                            "certainly visible came back with {0} of the probe " +
                            "colour", magenta));
                }
                else if (magenta < 150)
                    Failures.Add(string.Format(
                        "the DATA SOURCE line does not reach the screen " +
                        "({0} of its probe colour in a {1}x{2} capture)",
                        magenta, texture.width, texture.height));
            }
            catch (Exception exception)
            {
                Failures.Add("the colour probe threw: " + exception.Message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
                try
                {
                    File.Delete(colorProbePath);
                }
                catch (IOException)
                {
                }
            }
        }

        private static string StatusLine()
        {
            var text = Get(workbench, "statusText") as Text;
            return text != null ? text.text : null;
        }

        private static string BoundaryReadout()
        {
            var label = Get(workbench, "boundaryCurrentRangeText") as Text;
            return label != null ? label.text : null;
        }

        private static string[] SnapshotFiles()
        {
            string folder = SlabLabSnapshot.Folder();
            return Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.png") : new string[0];
        }

        private static string NewSnapshotFile()
        {
            string[] files = SnapshotFiles();
            for (int index = 0; index < files.Length; index++)
            {
                if (Array.IndexOf(snapshotBefore, files[index]) < 0)
                    return files[index];
            }
            return null;
        }

        /// <summary>
        /// The button is only useful if it produces a real image, so the file is
        /// checked for the PNG signature and a plausible size before the guard
        /// deletes it again (one file per run would otherwise pile up).
        /// </summary>
        private static void CheckSnapshotFile(string path)
        {
            long length = 0;
            try
            {
                length = new FileInfo(path).Length;
            }
            catch (IOException)
            {
                length = 0;
            }
            if (length <= 1000 || !SlabLabSnapshot.IsPng(path))
            {
                Failures.Add("the Snapshot button wrote an unusable file: " +
                    path);
                snapshotResult = "invalid file";
            }
            else
            {
                snapshotResult = Path.GetFileName(path) + " (" + length +
                    " bytes)";
            }
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Leaving one file behind is not worth failing the run over.
            }
        }

        private static bool DisturbanceIsVisible()
        {
            return !SameBounds("slab-axis-before", "slab-axis-disturbed") ||
                !SameBounds("slab-field-before", "slab-field-disturbed");
        }

        private static bool SameBounds(string first, string second)
        {
            Measurement a = Measurements.Find(item => item.label == first);
            Measurement b = Measurements.Find(item => item.label == second);
            if (a == null || b == null || !a.visible || !b.visible)
                return false;
            return Mathf.Abs(a.minX - b.minX) <= Tolerance &&
                Mathf.Abs(a.maxX - b.maxX) <= Tolerance;
        }

        /// <summary>
        /// Recovery is judged against the state the probe itself recorded just
        /// before the disturbance, plus the authored camera pose — not against
        /// the fixed constants. That keeps it honest (the view really did come
        /// back) and stable (it does not depend on where the camera happened to
        /// be when the probe started). The fixed constants are still asserted at
        /// the standard Step 2 / Step 3 measurements earlier in the run.
        /// </summary>
        private static void CheckViewRecovery()
        {
            if (!CameraIsAtAuthoredPose())
            {
                Failures.Add("Reset view did not restore the authored camera pose");
                viewRecovery = "camera pose not restored";
                return;
            }
            Measurement axis = Measurements.Find(
                item => item.label == "slab-axis-recovered");
            if (axis == null || !axis.visible)
            {
                Failures.Add("nothing visible after Reset view");
                viewRecovery = "nothing visible";
                return;
            }
            if (!SameBounds("slab-axis-before", "slab-axis-recovered"))
            {
                Failures.Add("Reset view did not restore the tri-axis");
                viewRecovery = "tri-axis moved";
                return;
            }
            if (!SameBounds("slab-field-before", "slab-field-recovered"))
            {
                Failures.Add("Reset view did not restore the Field pair");
                viewRecovery = "field pair moved";
                return;
            }
            viewRecovery = string.Format(
                "camera pose + axis x[{0:0.000}..{1:0.000}] + field pair restored",
                axis.minX, axis.maxX);
        }

        /// <summary>The opening page's DATA SOURCE line, or null when absent.</summary>
        private static string FindDataSourceLabel()
        {
            Text label = FindDataSourceLabelObject();
            return label != null ? label.text : null;
        }

        /// <summary>
        /// The DATA SOURCE line was added into a band of the opening panel that
        /// was already empty, so nothing moved. This keeps that true: it returns
        /// the caption of any button whose rectangle the line intersects, and
        /// null when the layout is clean.
        /// </summary>
        private static string PanelOverlapWithDataSource()
        {
            var content = Get(workbench, "panelContent") as RectTransform;
            if (content == null)
                return null;
            Text label = null;
            Text[] labels = content.GetComponentsInChildren<Text>(true);
            for (int index = 0; index < labels.Length; index++)
            {
                if (labels[index] != null && labels[index].text != null &&
                    labels[index].text.StartsWith("DATA SOURCE"))
                {
                    label = labels[index];
                    break;
                }
            }
            if (label == null)
                return null;
            Rect box = WorldRect(label.rectTransform);
            if (box.width <= 0.0f || box.height <= 0.0f)
                return null;
            Button[] buttons = content.GetComponentsInChildren<Button>(true);
            for (int index = 0; index < buttons.Length; index++)
            {
                Rect other = WorldRect(buttons[index].transform as RectTransform);
                if (other.width <= 0.0f || other.height <= 0.0f ||
                    !box.Overlaps(other))
                    continue;
                Text caption = buttons[index].GetComponentInChildren<Text>(true);
                return caption != null && caption.text != null
                    ? caption.text : "a button";
            }
            return null;
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var result = new Rect();
            if (rect == null)
                return result;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            result.min = new Vector2(corners[0].x, corners[0].y);
            result.max = new Vector2(corners[2].x, corners[2].y);
            return result;
        }

        /// <summary>
        /// True when the workflow is on <paramref name="condition"/> and the app
        /// has had no pending job for SettleSeconds. The desktop buttons refuse
        /// input while a job runs, and bounds measured mid-load are meaningless,
        /// so every drive/measure step waits for this first.
        /// </summary>
        private static bool Settled(Func<bool> condition)
        {
            // The quiet window belongs to one phase: carrying it across a phase
            // change let the next step "settle" instantly (the timing numbers
            // showed Step 3 finishing in 0.02 s, which is how this was caught).
            if (settledPhase != phase)
            {
                settledPhase = phase;
                heldSince = 0.0f;
            }
            if (!condition() || !Idle())
            {
                heldSince = 0.0f;
                return false;
            }
            if (heldSince <= 0.0f)
            {
                heldSince = Time.realtimeSinceStartup;
                return false;
            }
            return Time.realtimeSinceStartup - heldSince >= SettleSeconds;
        }

        private static bool Idle()
        {
            if (workbench == null)
                return false;
            try
            {
                MethodInfo pending = workbench.GetType().GetMethod(
                    "DesktopOperationPending", Private);
                return pending != null &&
                    !(bool)pending.Invoke(workbench, null);
            }
            catch (Exception exception)
            {
                string message = "DesktopOperationPending threw: " +
                    exception.Message;
                if (!Failures.Contains(message))
                    Failures.Add(message);
                return false;
            }
        }

        private static int DatasetCount(
            VolumeSTCubeQuestSpatialWorkbench target)
        {
            object value = Get(target, "datasets");
            return value is System.Collections.ICollection collection
                ? collection.Count : 0;
        }

        private static object Get(object target, string field)
        {
            if (target == null)
                return null;
            FieldInfo info = target.GetType().GetField(field, Private);
            return info != null ? info.GetValue(target) : null;
        }

        private static void Set(object target, string field, object value)
        {
            FieldInfo info = target?.GetType().GetField(field, Private);
            info?.SetValue(target, value);
        }

        private static void WriteReport(bool passed)
        {
            string directory = Path.GetFullPath(Path.Combine(
                Application.dataPath, "../../.runtime/test-results"));
            Directory.CreateDirectory(directory);
            var lines = new List<string>
            {
                "{",
                "  \"generatedAt\": \"" + DateTime.UtcNow.ToString("O") + "\",",
                "  \"result\": \"" + (skipReason != null ? "skipped"
                    : passed ? "pass" : "fail") + "\",",
            };
            if (skipReason != null)
                lines.Add("  \"skippedBecause\": \"" + skipReason + "\",");
            lines.Add("  \"dataSourceLabel\": " +
                (string.IsNullOrEmpty(dataSourceLabel)
                    ? "null"
                    : "\"" + dataSourceLabel.Replace("\"", "'") + "\"") + ",");
            lines.Add("  \"dataSourceOverlaps\": " +
                (string.IsNullOrEmpty(dataSourceOverlap)
                    ? "null"
                    : "\"" + dataSourceOverlap.Replace("\"", "'") + "\"")
                + ",");
            lines.Add("  \"dataSourceProbePixels\": " + dataSourcePixels + ",");
            lines.Add("  \"controlProbePixels\": " + controlProbePixels + ",");
            lines.Add("  \"shortcutRoundTrip\": " +
                (string.IsNullOrEmpty(shortcutRoundTrip)
                    ? "null"
                    : "\"" + shortcutRoundTrip.Replace("\"", "'") + "\"")
                + ",");
            lines.Add("  \"viewRecovery\": " +
                (string.IsNullOrEmpty(viewRecovery)
                    ? "null"
                    : "\"" + viewRecovery.Replace("\"", "'") + "\"")
                + ",");
            lines.Add("  \"viewRecoveryPath\": " +
                (string.IsNullOrEmpty(viewRecoveryPath)
                    ? "null"
                    : "\"" + viewRecoveryPath.Replace("\"", "'") + "\"")
                + ",");
            lines.Add("  \"snapshot\": " +
                (string.IsNullOrEmpty(snapshotResult)
                    ? "null"
                    : "\"" + snapshotResult.Replace("\"", "'") + "\"")
                + ",");
            lines.Add("  \"typedRange\": " +
                (string.IsNullOrEmpty(typedRange)
                    ? "null"
                    : "\"" + typedRange.Replace("\"", "'") + "\"")
                + ",");
            lines.Add(string.Format(
                "  \"timings\": {{ \"openingSeconds\": {0:0.00}, " +
                "\"step2Seconds\": {1:0.00}, \"step3Seconds\": {2:0.00}, " +
                "\"budgetSeconds\": [{3:0.0}, {4:0.0}] }},",
                bootSeconds, step2Seconds, step3Seconds,
                BootBudgetSeconds, StepBudgetSeconds));
            lines.Add("  \"measurements\": [");
            for (int index = 0; index < Measurements.Count; index++)
            {
                Measurement item = Measurements[index];
                lines.Add(string.Format(
                    "    {{\"label\": \"{0}\", \"visible\": {1}, " +
                    "\"minX\": {2:0.000}, \"maxX\": {3:0.000}, " +
                    "\"centreX\": {4:0.000}, \"minY\": {5:0.000}, " +
                    "\"maxY\": {6:0.000}, \"phase\": \"{7}\", " +
                    "\"pendingJobs\": \"{8}\"}}{9}",
                    item.label, item.visible ? "true" : "false", item.minX,
                    item.maxX, (item.minX + item.maxX) * 0.5f, item.minY,
                    item.maxY, item.phase, item.pendingJobs,
                    index == Measurements.Count - 1 ? "" : ","));
            }
            lines.Add("  ],");
            lines.Add("  \"failures\": [");
            for (int index = 0; index < Failures.Count; index++)
                lines.Add("    \"" + Failures[index].Replace("\"", "'") + "\"" +
                    (index == Failures.Count - 1 ? "" : ","));
            lines.Add("  ]");
            lines.Add("}");
            File.WriteAllText(Path.Combine(directory,
                "interaction-baseline.json"), string.Join("\n", lines) + "\n");
            string summary = skipReason != null
                ? "SKIPPED: " + skipReason
                : passed ? "PASS: interaction baseline holds"
                    : "FAIL: " + string.Join(" | ", Failures);
            File.WriteAllText(Path.Combine(directory,
                "interaction-baseline.txt"),
                summary + "\n" + DateTime.UtcNow.ToString("O") + "\n");
            if (passed)
                Debug.Log("Interaction baseline " + summary);
            else
                Debug.LogError("Interaction baseline " + summary);
        }
    }
}
#endif
