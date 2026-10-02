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
    /// Runs the Quest/VR branch of the workspace — the one a headset would use —
    /// inside the editor's VR preview and checks everything that can be checked
    /// without a headset: the mode really builds the world-space rig instead of
    /// the flat shell, the opening step reaches "data ready", the shared stage
    /// transition still moves to Step 2, and nothing logs an error on the way.
    ///
    /// It does not claim to verify headset input (hand rays, locomotion, the
    /// system keyboard): that needs the device. Everything the device-independent
    /// VR path does is covered here, which is more than the static audit it
    /// replaces. Verdict lands in .runtime/vr-flow-validation.txt.
    /// </summary>
    public static class VolumeSTCubeVrFlowTests
    {
        private const string PendingKey = "SlabLab.VrFlow.Pending";
        private const string ModeKey = "SlabLab.VrFlow.ModeBefore";
        private const string StartedKey = "SlabLab.VrFlow.StartedUtc";
        private const double BootBudgetSeconds = 90.0;
        private const string StageKey = "SlabLab.VrFlow.LastStage";
        private const double WatchSeconds = 120.0;
        private const BindingFlags Private =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly List<string> errors = new List<string>();
        private static VolumeSTCubeQuestSpatialWorkbench workbench;
        private static int step;
        private static string observed = string.Empty;

        [MenuItem("VolumeSTCube/Desktop/Validate VR Flow", priority = 44)]
        public static void RunFromMenu()
        {
            if (SessionState.GetBool(PendingKey, false))
            {
                Debug.LogWarning("The VR flow check is already running.");
                return;
            }
            errors.Clear();
            workbench = null;
            step = 0;
            observed = string.Empty;
            SessionState.SetString(ModeKey,
                VolumeSTCubeMode.Current.ToString());
            // Ticks, not a formatted timestamp: DateTime.Parse reads "O" back as
            // local time, and subtracting that from UtcNow produced a verdict
            // line claiming -7197 s. Ticks have no locality to get wrong.
            SessionState.SetString(StartedKey, DateTime.UtcNow.Ticks.ToString());
            SessionState.SetString(StageKey, string.Empty);
            SessionState.SetBool(PendingKey, true);
            Application.logMessageReceived -= OnLogMessage;
            Application.logMessageReceived += OnLogMessage;
            VolumeSTCubeMode.SetStartupPreference(
                VolumeSTCubeApplicationMode.VirtualReality);
            EditorApplication.isPlaying = true;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if (!SessionState.GetBool(PendingKey, false))
                return;
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Finish(false, "the run never reached Play Mode");
                return;
            }
            workbench = null;
            Application.logMessageReceived -= OnLogMessage;
            Application.logMessageReceived += OnLogMessage;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying)
                return;
            if (workbench == null)
            {
                workbench = UnityEngine.Object.FindObjectOfType<
                    VolumeSTCubeQuestSpatialWorkbench>();
                if (workbench == null)
                {
                    if (WaitedTooLong())
                        Finish(false, "the workspace never appeared");
                    return;
                }
                if (step == 0)
                    step = 1;
            }
            if (WaitedTooLong())
            {
                Finish(false, "the VR boot never finished; last stage " +
                    SessionState.GetString(StageKey, "<none>"));
                return;
            }
            if (step == 1)
            {
                if (DatasetCount() == 0)
                    return;
                step = 2;
                return;
            }
            if (step == 2)
            {
                if (CheckVrRig())
                    step = 3;
                return;
            }
            if (step == 3)
            {
                // The transition itself is shared with the desktop flow, so
                // driving it here checks the VR rig survives a step change.
                workbench.DesktopNextStep();
                step = 4;
                return;
            }
            if (step == 4 && StageIs("Field"))
            {
                Finish(true, null);
            }
        }

        private static bool CheckVrRig()
        {
            if (UnityEngine.Object.FindObjectOfType<VolumeSTCubeFlatScreenHUD>()
                != null)
            {
                Finish(false, "VR mode built the flat-screen shell");
                return false;
            }
            var panel = Get(workbench, "panelCanvas") as Canvas;
            var boundary = Get(workbench, "boundaryCanvas") as Canvas;
            if (panel == null || boundary == null)
            {
                Finish(false, "the world-space panels are missing");
                return false;
            }
            observed = string.Format("world panels ok, phase {0}",
                workbench.CurrentPhase);
            SessionState.SetString(StageKey, observed);
            return true;
        }

        private static string Describe()
        {
            return string.Format(
                "stage {0}, phase {1}, {2} variable(s); {3}",
                StageName(), workbench.CurrentPhase, DatasetCount(), observed);
        }

        private static bool StageIs(string stage)
        {
            object current = Get(workbench, "stage");
            return current != null && current.ToString() == stage;
        }

        private static string StageName()
        {
            object current = Get(workbench, "stage");
            return current != null ? current.ToString() : "<none>";
        }

        private static int DatasetCount()
        {
            object value = Get(workbench, "datasets");
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

        private static bool WaitedTooLong()
        {
            return (DateTime.UtcNow - StartedAt()).TotalSeconds > WatchSeconds;
        }

        private static DateTime StartedAt()
        {
            string stamp = SessionState.GetString(StartedKey, string.Empty);
            return long.TryParse(stamp, out long ticks)
                ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.UtcNow;
        }

        private static void OnLogMessage(string message, string stackTrace,
            LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception &&
                type != LogType.Assert)
                return;
            if (errors.Count < 8)
                errors.Add(message + "  " + FirstLine(stackTrace));
        }

        private static string FirstLine(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            int breakAt = value.IndexOf('\n');
            return breakAt < 0 ? value : value.Substring(0, breakAt);
        }

        private static void Finish(bool passed, string note)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLogMessage;
            double seconds = (DateTime.UtcNow - StartedAt()).TotalSeconds;
            string directory = Path.GetFullPath(Path.Combine(
                Application.dataPath, "../../.runtime"));
            Directory.CreateDirectory(directory);
            string verdict;
            if (!passed)
                verdict = "FAIL: " + (note ?? "no verdict");
            else if (errors.Count > 0)
                verdict = "FAIL: the VR path logged errors: " +
                    string.Join(" | ", errors.ToArray());
            else if (seconds > BootBudgetSeconds)
                verdict = string.Format(
                    "FAIL: the VR branch needed {0:0.0}s to reach Step 2 " +
                    "(budget {1:0.0}s)", seconds, BootBudgetSeconds);
            else
                verdict = string.Format(
                    "PASS: VR branch booted and stepped to Step 2 in {0:0.0}s " +
                    "with no errors; {1}", seconds, Describe());
            File.WriteAllText(Path.Combine(directory,
                "vr-flow-validation.txt"),
                verdict + "\n" + DateTime.UtcNow.ToString("O") + "\n");
            if (verdict.StartsWith("PASS"))
                Debug.Log(verdict);
            else
                Debug.LogError(verdict);
            // Put the project back in the mode the operator normally runs.
            string before = SessionState.GetString(ModeKey, "Desktop");
            VolumeSTCubeMode.SetStartupPreference(
                string.Equals(before, "VirtualReality",
                    StringComparison.Ordinal)
                    ? VolumeSTCubeApplicationMode.VirtualReality
                    : VolumeSTCubeApplicationMode.Desktop);
            SessionState.SetBool(PendingKey, false);
            if (EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;
        }
    }
}
#endif
