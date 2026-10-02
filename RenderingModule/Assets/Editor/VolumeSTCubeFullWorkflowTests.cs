#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityVolumeRendering.Tests
{
    /// <summary>
    /// Walks all six desktop steps against an isolated offline S4D server.
    /// The server uses real RAW/cache/contracts/snapshot routes and deterministic
    /// MatPlot charts; only external model calls are fixtures. Controller actions
    /// use the same runtime methods as the UI and never assign workflow flags.
    /// Start tools/run_workflow_test_server.py before running this menu item.
    /// </summary>
    public static class VolumeSTCubeFullWorkflowTests
    {
        private const string PendingKey = "SlabLab.FullWorkflow.Pending";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string TestUrl = "http://127.0.0.1:8031";
        private static readonly List<string> observations = new List<string>();
        private static readonly List<string> errors = new List<string>();
        private static VolumeSTCubeQuestSpatialWorkbench workbench;
        private static int step;
        private static double deadline;
        private static double nextAction;
        private static string snapshotId;
        private static int historyCount;
        private static string reportDirectory;
        private static string pendingCapture;

        [MenuItem("VolumeSTCube/Desktop/Validate Full Workflow", priority = 43)]
        public static void Start()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before starting the full workflow check.");
            SessionState.SetBool(PendingKey, true);
            VolumeSTCubeMode.SetStartupPreference(VolumeSTCubeApplicationMode.Desktop);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!SessionState.GetBool(PendingKey, false))
                return;
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.EraseBool(PendingKey);
                return;
            }
            observations.Clear();
            errors.Clear();
            workbench = null;
            pendingCapture = null;
            step = 0;
            nextAction = 0;
            deadline = EditorApplication.timeSinceStartup + 120;
            reportDirectory = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../.runtime/full-workflow/ui/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));
            Directory.CreateDirectory(reportDirectory);
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying)
                return;
            if (EditorApplication.timeSinceStartup > deadline)
            {
                Finish(false, "Timed out at action " + step + ": " + Describe());
                return;
            }
            if (EditorApplication.timeSinceStartup < nextAction)
                return;
            try
            {
                if (workbench == null)
                    workbench = UnityEngine.Object.FindObjectOfType<VolumeSTCubeQuestSpatialWorkbench>();
                if (workbench == null || !Idle())
                    return;
                switch (step)
                {
                    case 0:
                        if (DatasetCount() == 0)
                            return;
                        // Redirect this Play Mode instance only. PlayerPrefs and
                        // the production services at 8010/8020 are left alone.
                        Set("s4dUrl", TestUrl);
                        var wave = Get("waveClient") as VolumeSTCubeWaveClient;
                        Require(wave != null, "The Wave client is missing");
                        wave.serviceBaseUrl = TestUrl;
                        Call("ImportWaveServerDataset");
                        Advance(1);
                        break;
                    case 1:
                        if (DatasetCount() == 0 || (Get("waveClient") as VolumeSTCubeWaveClient).IsImporting)
                            return;
                        Require(Stage() == "DatasetImport", "Import stage changed");
                        Require(workbench.CurrentPhase.ToString() == "ImportReady", "Import never became ready");
                        if (!Observe("01-import")) return;
                        workbench.DesktopNextStep();
                        Advance(2);
                        break;
                    case 2:
                        if (Stage() != "Field" || Get("selectedDataset") == null)
                            return;
                        if (!Observe("02-field")) return;
                        // Real combined-STC preview/confirmation path, including
                        // its authored buckets. No flag is forced ready here.
                        workbench.PreviewForVrCombinedTimeRange(3, 14, 0);
                        workbench.ConfirmForVrCombinedTimeRange(3, 14);
                        Advance(3);
                        break;
                    case 3:
                        Require(Stage() == "Slab", "Time confirmation did not enter Slab");
                        var list = Get("datasets") as IList;
                        int selected = list.IndexOf(Get("selectedDataset"));
                        Call("BindVariableToAxisRig", 0, selected);
                        Call("BindAxisToken", 0, 0, 0);
                        Call("BindAxisToken", 0, 1, 1);
                        Call("BindVariableSelectorToAxis", 2);
                        Advance(4);
                        break;
                    case 4:
                        if (pendingCapture == null) workbench.DesktopOpenIntent();
                        Require(Get("intentCanvas") is Canvas intent && intent.gameObject.activeSelf,
                            "The axis bindings did not open Intent");
                        if (!Observe("03-slab-intent")) return;
                        Call("ApplyIntentToFrame");
                        Advance(5);
                        break;
                    case 5:
                        Require((bool)Get("intentConfigured"), "Intent did not resolve");
                        workbench.DesktopBuildFullMatrix();
                        Require(Stage() == "Matrix", "Full Matrix did not enter Step 4");
                        Advance(6);
                        break;
                    case 6:
                        if (Get("s4dGridImage") == null || Get("currentDigest") == null)
                            return;
                        Require(string.IsNullOrEmpty((string)Get("s4dGridFailure")), "Matrix generation failed");
                        historyCount = workbench.DesktopAnalysisCount;
                        Require(historyCount > 0, "The completed result was not committed to history");
                        snapshotId = (string)Get("s4dSnapshotId");
                        Require(!string.IsNullOrEmpty(snapshotId), "The Grid has no snapshot");
                        if (!Observe("04-matrix")) return;
                        Call("SelectFacetPreviewCell", 0, 0);
                        workbench.DesktopNextStep();
                        Require(Stage() == "Analyze", "The selected Matrix cell did not open Ground");
                        Advance(7);
                        break;
                    case 7:
                        if (Get("groundAggregateVolume") == null)
                            return;
                        Require((string)Get("groundAggregateSnapshotId") == snapshotId,
                            "Ground no longer refers to the committed snapshot");
                        if (!Observe("05-ground")) return;
                        workbench.DesktopNextStep();
                        Require(Stage() == "Result", "Step 5 did not open findings");
                        Advance(8);
                        break;
                    case 8:
                        Require(Get("currentDigest") is S4DDigestResult digest &&
                            !string.IsNullOrWhiteSpace(digest.headline), "Findings are missing");
                        if (pendingCapture == null) Call("OpenAiFindingsPanel");
                        Require(Get("aiFindingsCanvas") is Canvas findings &&
                            findings.gameObject.activeInHierarchy, "The findings panel is not open");
                        if (!Observe("06-findings")) return;
                        workbench.DesktopPreviousStep();
                        Require(Stage() == "Analyze", "Back from findings did not return to Analyze");
                        workbench.DesktopPreviousStep();
                        Require(Stage() == "Matrix", "Back from Analyze did not return to Matrix");
                        workbench.DesktopOpenAnalysis(0);
                        Require(workbench.DesktopAnalysisCount == historyCount, "Reopening history lost a result");
                        Require((string)Get("s4dSnapshotId") == snapshotId, "History restored a different snapshot");
                        observations.Add("history: findings -> Analyze -> Matrix -> same committed snapshot");
                        Advance(9);
                        break;
                    case 9:
                        for (int i = 1; i <= 6; i++)
                        {
                            string[] captures = Directory.GetFiles(reportDirectory, "0" + i + "-*.png");
                            Require(captures.Length > 0 && new FileInfo(captures[0]).Length > 1024,
                                "Step " + i + " screenshot was not written");
                        }
                        Require(errors.Count == 0, "Runtime errors: " + string.Join(" | ", errors));
                        Finish(true, "all six steps, MatPlot charts, Ground, findings and history passed");
                        break;
                }
            }
            catch (Exception exception)
            {
                Finish(false, exception.GetBaseException().ToString() + ": " + Describe());
            }
        }

        private static void Advance(int next)
        {
            step = next;
            nextAction = EditorApplication.timeSinceStartup + 1.5;
            deadline = EditorApplication.timeSinceStartup + 120;
        }

        private static bool Observe(string label)
        {
            string path = Path.Combine(reportDirectory, label + ".png");
            if (pendingCapture == null)
            {
                observations.Add(label + ": " + Describe());
                pendingCapture = label;
                ScreenCapture.CaptureScreenshot(path);
                // CaptureScreenshot renders at the end of the frame. Advancing
                // now would capture the next page under the previous label.
                nextAction = EditorApplication.timeSinceStartup + 0.5;
                return false;
            }
            Require(pendingCapture == label, "Screenshot phase changed unexpectedly");
            if (!File.Exists(path)) return false;
            Require(new FileInfo(path).Length > 1024, "Screenshot is empty: " + label);
            pendingCapture = null;
            return true;
        }

        private static bool Idle()
        {
            if (Get("pendingJobs").ToString() != "None")
                return false;
            return !(bool)Call("DesktopOperationPending");
        }

        private static int DatasetCount() { return (Get("datasets") as ICollection)?.Count ?? 0; }
        private static string Stage() { return Get("stage")?.ToString() ?? "none"; }
        private static string Describe()
        {
            return workbench == null ? "no workspace" : Stage() + ", " +
                workbench.CurrentPhase + ", pending " + Get("pendingJobs");
        }
        private static object Get(string field)
        {
            FieldInfo info = workbench.GetType().GetField(field, Private);
            if (info == null) throw new MissingFieldException(workbench.GetType().Name, field);
            return info.GetValue(workbench);
        }
        private static void Set(string field, object value) { workbench.GetType().GetField(field, Private).SetValue(workbench, value); }
        private static object Call(string method, params object[] arguments)
        {
            return workbench.GetType().GetMethod(method, Private).Invoke(workbench, arguments);
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(message);
        }
        private static void Finish(bool passed, string detail)
        {
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= Tick;
            SessionState.EraseBool(PendingKey);
            string result = (passed ? "PASS: " : "FAIL: ") + detail;
            string report = result + "\n" + DateTime.UtcNow.ToString("O") + "\n" +
                string.Join("\n", observations) + "\nScreenshots: " + reportDirectory + "\n";
            File.WriteAllText(Path.Combine(reportDirectory, "workflow.txt"), report);
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../.runtime/full-workflow/workflow.txt")), report);
            EditorApplication.isPlaying = false;
            if (passed) Debug.Log(result); else Debug.LogError(result);
        }
    }
}
#endif
