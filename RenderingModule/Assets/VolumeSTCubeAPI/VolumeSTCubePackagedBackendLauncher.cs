using System;
using System.IO;
using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Starts the bundled Python services when the distributed macOS player is
    /// opened directly. The command launcher remains available as a fallback.
    /// </summary>
    internal static class VolumeSTCubePackagedBackendLauncher
    {
        private static System.Diagnostics.Process supervisor;
        private static bool attempted;
        private static float lastRecoveryAttempt = -999.0f;

        /// <summary>
        /// Why the bundled backend is not running, in the operator's words, when
        /// the launcher could not start it. The log has always said this; the
        /// screen did not, so a teacher who forgot `Setup Backend.command` saw
        /// only "cannot connect". The workbench folds this into the message it
        /// shows at the opening step.
        /// </summary>
        public static string SetupHint { get; private set; }

        /// <summary>
        /// Called when a request finds the backend unreachable. The startup
        /// launch is one-shot, so a backend that dies mid-session would
        /// otherwise stay down until the app restarts; this retries it at most
        /// once every 30 seconds.
        /// </summary>
        public static void EnsureRunning()
        {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            if (Time.realtimeSinceStartup - lastRecoveryAttempt < 30.0f)
                return;
            lastRecoveryAttempt = Time.realtimeSinceStartup;
            attempted = false;
            StartBundledBackend();
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void StartBundledBackend()
        {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            if (attempted)
                return;
            attempted = true;

            string packageRoot = FindPackageRoot();
            if (string.IsNullOrEmpty(packageRoot))
            {
                SetupHint = "Run the app through Start STC SlabLab.command so it " +
                    "can find its backend, or run Setup Backend.command once.";
                Debug.LogWarning(
                    "SlabLab backend launcher was not found beside the application.");
                return;
            }

            string readyMarker = Path.Combine(
                packageRoot, ".venv", ".stc-slablab-ready");
            if (!File.Exists(readyMarker))
            {
                SetupHint = "The backend is not installed. Run Setup Backend.command " +
                    "once, then start the app again.";
                Debug.LogError(
                    "SlabLab backend is not installed. Run Setup Backend.command once.");
                return;
            }

            try
            {
                string script = Path.Combine(packageRoot, "Start-Backend.sh");
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = QuoteArgument(script) + " --supervise",
                    WorkingDirectory = packageRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                supervisor = new System.Diagnostics.Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };
                supervisor.OutputDataReceived += (_, eventArgs) =>
                {
                    if (!string.IsNullOrEmpty(eventArgs.Data))
                        Debug.Log("SlabLab backend: " + eventArgs.Data);
                };
                supervisor.ErrorDataReceived += (_, eventArgs) =>
                {
                    if (!string.IsNullOrEmpty(eventArgs.Data))
                        Debug.LogError("SlabLab backend: " + eventArgs.Data);
                };
                if (!supervisor.Start())
                {
                    Debug.LogError("SlabLab could not start its bundled backend.");
                    return;
                }
                supervisor.BeginOutputReadLine();
                supervisor.BeginErrorReadLine();
                Debug.Log("SlabLab is starting its bundled backend services.");
            }
            catch (Exception exception)
            {
                Debug.LogError("SlabLab backend startup failed: " + exception.Message);
            }
#endif
        }

        private static string FindPackageRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(Application.dataPath);
            for (int depth = 0; directory != null && depth < 8; depth++)
            {
                string script = Path.Combine(directory.FullName, "Start-Backend.sh");
                if (File.Exists(script))
                    return directory.FullName;
                directory = directory.Parent;
            }
            return string.Empty;
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
