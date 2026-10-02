#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityVolumeRendering.Tests;

namespace UnityVolumeRendering.Tests
{
    /// <summary>
    /// One entry point for every guard, so "did I break anything" is a single
    /// command instead of a manual menu hunt:
    ///
    ///   Unity -batchmode -projectPath RenderingModule \
    ///     -executeMethod UnityVolumeRendering.Tests.VolumeSTCubeGuardRunner.RunHeadless
    ///
    /// (tools/run_unity_tests.sh wraps that and checks the editor is closed.)
    /// The EditMode guards run immediately; the interaction baseline needs Play
    /// Mode, so it is driven by the editor update loop and the process exits once
    /// it finishes. Results land in .runtime/test-results/.
    /// </summary>
    public static class VolumeSTCubeGuardRunner
    {
        private const string FailureKey = "SlabLab.Guard.EditModeFailures";

        private static readonly List<string> failures = new List<string>();

        [MenuItem("VolumeSTCube/Desktop/Validate All", priority = 41)]
        private static void RunFromMenu()
        {
            failures.Clear();
            RunEditModeGuards();
            // The interaction baseline enters Play Mode, which reloads the
            // domain: this list and any closure over it would be gone before
            // the baseline even boots, so the edit-mode half of the run is
            // parked in SessionState for the other side of the reload.
            SessionState.SetString(FailureKey, string.Join(" | ",
                failures.ToArray()));
            VolumeSTCubeInteractionBaseline.Start(true);
        }

        /// <summary>Headless entry point: -executeMethod target.</summary>
        public static void RunHeadless()
        {
            // Drives Play Mode through EditorApplication.update, then exits from
            // NotifyBaselineFinished once the verdict exists.
            RunFromMenu();
        }

        /// <summary>
        /// Called by the interaction baseline with its verdict, possibly after
        /// the Play Mode domain reload. Rebuilds the summary from the parked
        /// edit-mode result plus this verdict.
        /// </summary>
        public static void NotifyBaselineFinished(bool passed, bool skipped,
            string skipDetail)
        {
            failures.Clear();
            string parked = SessionState.GetString(FailureKey, "");
            if (!string.IsNullOrEmpty(parked))
                failures.AddRange(parked.Split(new[] { " | " },
                    StringSplitOptions.None));
            SessionState.EraseString(FailureKey);
            if (!passed)
                failures.Add("interaction baseline");
            Report(skipped, skipDetail);
            if (Application.isBatchMode)
                EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static void RunEditModeGuards()
        {
            Run("refactor guards", VolumeSTCubeRefactorTests.RunChecks);
            Run("history regressions", VolumeSTCubeHistoryTests.RunChecks);
            Run("shortcut guards", VolumeSTCubeShortcutTests.RunChecks);
            Run("export guards", VolumeSTCubeExportTests.RunChecks);
            Run("range-entry guards", VolumeSTCubeBoundaryEntryTests.RunChecks);
            Run("hint guards", VolumeSTCubeHintTests.RunChecks);
            Run("service-error guards", VolumeSTCubeServiceErrorTests.RunChecks);
        }

        private static void Run(string label, Action check)
        {
            try
            {
                check();
                Debug.Log("Guard OK: " + label);
            }
            catch (Exception exception)
            {
                string message = label + ": " + exception.Message;
                failures.Add(message);
                Debug.LogError("Guard FAILED: " + message);
            }
        }

        private static void Report(bool baselineSkipped = false,
            string skipDetail = null)
        {
            string directory = Path.GetFullPath(Path.Combine(
                Application.dataPath, "../../.runtime/test-results"));
            Directory.CreateDirectory(directory);
            string summary;
            if (failures.Count > 0)
                summary = "FAIL: " + string.Join(" | ", failures);
            else if (baselineSkipped)
                // A skipped baseline proves nothing; never let it read as a pass.
                summary = "SKIPPED: interaction baseline (" + skipDetail + ")";
            else
                summary = "PASS: all guards hold";
            File.WriteAllText(Path.Combine(directory, "guards-summary.txt"),
                summary + "\n" + DateTime.UtcNow.ToString("O") + "\n");
            File.WriteAllText(Path.Combine(directory, "guards.json"),
                "{\n"
                + "  \"generatedAt\": \"" + DateTime.UtcNow.ToString("O")
                + "\",\n"
                + "  \"result\": \"" + (failures.Count > 0 ? "fail"
                    : baselineSkipped ? "skipped" : "pass") + "\",\n"
                + "  \"baselineSkippedBecause\": "
                + (baselineSkipped
                    ? "\"" + (skipDetail ?? "").Replace("\"", "'") + "\""
                    : "null") + ",\n"
                + "  \"failures\": [" + string.Join(", ",
                    failures.ConvertAll(item =>
                        "\"" + item.Replace("\"", "'") + "\"").ToArray())
                + "]\n"
                + "}\n");
            if (failures.Count == 0)
                Debug.Log(summary);
            else
                Debug.LogError(summary);
        }
    }
}
#endif
