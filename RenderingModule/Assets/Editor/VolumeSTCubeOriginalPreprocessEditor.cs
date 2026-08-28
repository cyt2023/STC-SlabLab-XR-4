using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityVolumeRendering
{
    public static class VolumeSTCubeOriginalPreprocessEditor
    {
        private const string DefaultDataModuleRelativePath = "../../DataTransformationModule";
        private const int PythonStepTimeoutMilliseconds = 30 * 60 * 1000;

        [MenuItem("Volume Rendering/Load dataset/Preprocess CSV and load as XY+T", false, 20)]
        private static void RunOriginalPreprocessingAndImport()
        {
            RunPreprocessingAndImport(PreprocessProfile.Full);
        }

        private static void RunQuickOriginalPreprocessingAndImport()
        {
            RunPreprocessingAndImport(PreprocessProfile.Quick);
        }

        private static void RunPreprocessingAndImport(PreprocessProfile profile)
        {
            if (!ValidateOriginalSceneForImport())
                return;

            string defaultPath = Path.GetFullPath(Path.Combine(Application.dataPath, DefaultDataModuleRelativePath));
            string dataModulePath = EditorUtility.OpenFolderPanel(
                "Select DataTransformationModule folder",
                Directory.Exists(defaultPath) ? defaultPath : Application.dataPath,
                string.Empty);

            if (string.IsNullOrEmpty(dataModulePath))
                return;

            if (!ValidateOriginalPreprocessFolder(dataModulePath))
                return;

            try
            {
                Dictionary<string, string> environment = CreatePreprocessEnvironment(profile);
                if (HasMergedExampleData(dataModulePath))
                {
                    UnityEngine.Debug.Log("VolumeSTCube preprocessing: data_merged already exists, skipping 0_exampleDataMerge.py.");
                }
                else
                {
                    EditorUtility.DisplayProgressBar("VolumeSTCube preprocessing", "Running original data merge if available...", 0.05f);
                    RunOptionalScript(dataModulePath, Path.Combine("exampleData", "0_exampleDataMerge.py"), null);
                }

                string profileLabel = profile == PreprocessProfile.Quick ? "quick test" : "full";
                EditorUtility.DisplayProgressBar("VolumeSTCube preprocessing", $"Running original kriging interpolation ({profileLabel})...", 0.35f);
                RunRequiredScript(dataModulePath, "1_KrigingInterpolation.py", environment);

                CleanGeneratedRawData(dataModulePath);

                EditorUtility.DisplayProgressBar("VolumeSTCube preprocessing", $"Running original 3D smoothing and raw export ({profileLabel})...", 0.7f);
                RunRequiredScript(dataModulePath, "2_Smooth.py", environment);

                string rawOutputDir = Path.Combine(dataModulePath, "UnityRawData");
                if (!Directory.Exists(rawOutputDir))
                {
                    UnityEngine.Debug.LogError($"Original preprocessing finished but did not create UnityRawData: {rawOutputDir}");
                    return;
                }

                EditorUtility.DisplayProgressBar("VolumeSTCube preprocessing", "Importing generated raw stack into original scene preset...", 0.95f);
                VolumeSTCubeConfig config = VolumeSTCubeConfig.Default(Path.GetFileName(dataModulePath));
                config.datasetName = "UnityRawData";
                config.autoGroupUnderVolumeController = true;
                config.renderMode = VolumeSTCubeRenderMode.Volume;
                config.opacity = 0.5f;
                config.timeAxis = VolumeSTCubeTimeAxis.Z;
                config.showTimeline = true;

                VolumeSTCubeView view = VolumeSTCubeAPI.CreateViewFromRawDirectory(rawOutputDir, config);
                if (view == null)
                {
                    UnityEngine.Debug.LogError("Generated raw stack import failed. Check Console output from the Python preprocessing steps.");
                    return;
                }

                Selection.activeGameObject = view.rootObject;
                SceneView.lastActiveSceneView?.FrameSelected();
                UnityEngine.Debug.Log($"Original VolumeSTCube {profileLabel} preprocessing and raw stack import succeeded: {rawOutputDir}");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(ex.Message);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private enum PreprocessProfile
        {
            Full,
            Quick
        }

        private static Dictionary<string, string> CreatePreprocessEnvironment(PreprocessProfile profile)
        {
            if (profile == PreprocessProfile.Full)
                return null;

            return new Dictionary<string, string>
            {
                { "VOLUMESTC_LAYER_COUNT", "8" },
                { "VOLUMESTC_TIME_WIDTH", "16" },
                { "VOLUMESTC_GRID_WIDTH", "64" },
                { "VOLUMESTC_GRID_HEIGHT", "64" },
                { "VOLUMESTC_EXPAND_RATIO", "2" },
                { "VOLUMESTC_SPATIAL_RADIUS", "1" },
                { "VOLUMESTC_TEMPORAL_RADIUS", "3" }
            };
        }

        private static void CleanGeneratedRawData(string dataModulePath)
        {
            string rawOutputDir = Path.Combine(dataModulePath, "UnityRawData");
            if (!Directory.Exists(rawOutputDir))
                return;

            foreach (string path in Directory.GetFiles(rawOutputDir, "*.raw"))
                File.Delete(path);
            foreach (string path in Directory.GetFiles(rawOutputDir, "*.raw.ini"))
                File.Delete(path);
        }

        private static bool ValidateOriginalPreprocessFolder(string dataModulePath)
        {
            string krigingPath = Path.Combine(dataModulePath, "1_KrigingInterpolation.py");
            string smoothPath = Path.Combine(dataModulePath, "2_Smooth.py");
            if (!File.Exists(krigingPath) || !File.Exists(smoothPath))
            {
                UnityEngine.Debug.LogError($"Selected folder is not the original DataTransformationModule. Missing 1_KrigingInterpolation.py or 2_Smooth.py: {dataModulePath}");
                return false;
            }

            string chinaGeoJson = Path.Combine(dataModulePath, "exampleData", "chinaGeoJson.json");
            string chinaChange = Path.Combine(dataModulePath, "exampleData", "chinaChange.json");
            if (!File.Exists(chinaGeoJson) || !File.Exists(chinaChange))
            {
                UnityEngine.Debug.LogError($"Original preprocessing requires chinaGeoJson.json and chinaChange.json under exampleData: {dataModulePath}");
                return false;
            }

            return true;
        }

        private static bool HasMergedExampleData(string dataModulePath)
        {
            string mergedFolder = Path.Combine(dataModulePath, "exampleData", "data_merged");
            if (!Directory.Exists(mergedFolder))
                return false;

            return File.Exists(Path.Combine(mergedFolder, "LOC_AQI_0.csv"))
                && File.Exists(Path.Combine(mergedFolder, "LOC_AQI_8471.csv"));
        }

        private static bool ValidateOriginalSceneForImport()
        {
            if (VolumeSTCubeOriginalSceneAdapter.HasOriginalSceneGuides())
                return true;

            string sceneName = SceneManager.GetActiveScene().name;
            EditorUtility.DisplayDialog(
                "Open mainScene first",
                $"The original VolumeSTCube map and axis objects are not in the active scene '{sceneName}'.\n\nOpen Assets/Scenes/mainScene.unity, then run this preprocessing import again.",
                "OK");
            return false;
        }

        private static void RunOptionalScript(string workingDirectory, string relativeScriptPath, Dictionary<string, string> environment)
        {
            string scriptPath = Path.Combine(workingDirectory, relativeScriptPath);
            if (File.Exists(scriptPath))
                RunPythonScript(workingDirectory, scriptPath, environment);
        }

        private static void RunRequiredScript(string workingDirectory, string relativeScriptPath, Dictionary<string, string> environment)
        {
            string scriptPath = Path.Combine(workingDirectory, relativeScriptPath);
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException("Required original preprocessing script is missing.", scriptPath);

            RunPythonScript(workingDirectory, scriptPath, environment);
        }

        private static void RunPythonScript(string workingDirectory, string scriptPath, Dictionary<string, string> environment)
        {
            string arguments = $"\"{scriptPath}\"";
            if (TryRunProcess("python", arguments, workingDirectory, scriptPath, environment, out string failure))
                return;

            if (TryRunProcess("py", $"-3 {arguments}", workingDirectory, scriptPath, environment, out failure))
                return;

            throw new InvalidOperationException(failure);
        }

        private static bool TryRunProcess(string executable, string arguments, string workingDirectory, string scriptPath, Dictionary<string, string> environment, out string failure)
        {
            failure = string.Empty;
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (environment != null)
            {
                foreach (KeyValuePair<string, string> pair in environment)
                    startInfo.EnvironmentVariables[pair.Key] = pair.Value;
            }

            try
            {
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        failure = $"Could not start Python for script: {scriptPath}";
                        return false;
                    }

                    StringBuilder output = new StringBuilder();
                    StringBuilder error = new StringBuilder();
                    process.OutputDataReceived += (_, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Data))
                            output.AppendLine(args.Data);
                    };
                    process.ErrorDataReceived += (_, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Data))
                            error.AppendLine(args.Data);
                    };

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    Stopwatch timer = Stopwatch.StartNew();
                    while (!process.WaitForExit(1000))
                    {
                        TimeSpan elapsed = timer.Elapsed;
                        EditorUtility.DisplayProgressBar(
                            "VolumeSTCube preprocessing",
                            $"Running {Path.GetFileName(scriptPath)} with {executable}... elapsed {elapsed:mm\\:ss}",
                            0.5f);

                        if (elapsed.TotalMilliseconds <= PythonStepTimeoutMilliseconds)
                            continue;

                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                        }

                        failure = $"Python preprocessing timed out after {PythonStepTimeoutMilliseconds / 60000} minutes with {executable}: {Path.GetFileName(scriptPath)}";
                        return false;
                    }

                    process.WaitForExit();

                    string outputText = output.ToString();
                    string errorText = error.ToString();

                    if (!string.IsNullOrWhiteSpace(outputText))
                        UnityEngine.Debug.Log(outputText);

                    if (process.ExitCode != 0)
                    {
                        failure = $"Python preprocessing failed with {executable}: {Path.GetFileName(scriptPath)}\n{errorText}";
                        return false;
                    }

                    if (!string.IsNullOrWhiteSpace(errorText))
                        UnityEngine.Debug.LogWarning(errorText);

                    return true;
                }
            }
            catch (Exception ex)
            {
                failure = $"Could not run {executable} for {Path.GetFileName(scriptPath)}: {ex.Message}";
                return false;
            }
        }
    }
}
