using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.XR.Management;

namespace UnityVolumeRendering.EditorTools
{
    /// <summary>Repeatable player configuration for monitor and tablet builds.</summary>
    public static class VolumeSTCubeFlatBuild
    {
        private const string ScenePath = "Assets/Scenes/mainScene.unity";
        [MenuItem("VolumeSTCube/Desktop/Configure Current Platform")]
        public static void ConfigureCurrentPlatform()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            Configure(target, BuildPipeline.GetBuildTargetGroup(target));
            Debug.Log("SlabLab flat-screen configuration complete for " + target + ".");
        }

        [MenuItem("VolumeSTCube/Desktop/Build macOS")]
        public static void BuildMacOS()
        {
            Build(BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone,
                "SlabLab-Flat.app");
        }

        [MenuItem("VolumeSTCube/Desktop/Build Windows 64-bit")]
        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone,
                "SlabLab-Flat.exe");
        }

        [MenuItem("VolumeSTCube/Desktop/Build Android Tablet APK")]
        public static void BuildAndroidTablet()
        {
            Build(BuildTarget.Android, BuildTargetGroup.Android,
                "SlabLab-Flat-Tablet.apk");
        }

        [MenuItem("VolumeSTCube/Desktop/Export iPad Xcode Project")]
        public static void BuildIPad()
        {
            Build(BuildTarget.iOS, BuildTargetGroup.iOS, "SlabLab-Flat-iPad");
        }

        private static void Build(BuildTarget target, BuildTargetGroup group,
            string outputName)
        {
            if (EditorUserBuildSettings.activeBuildTarget != target &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
                throw new InvalidOperationException("Could not switch build target to " + target + ".");

            Configure(target, group);
            string buildDirectory = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "Builds"));
            Directory.CreateDirectory(buildDirectory);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(buildDirectory, outputName),
                target = target,
                targetGroup = group,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Flat-screen build failed: " +
                    report.summary.result + ", errors=" + report.summary.totalErrors);
            Debug.Log("Flat-screen build ready: " + report.summary.outputPath);
        }

        private static void Configure(BuildTarget target, BuildTargetGroup group)
        {
            PlayerSettings.companyName = "STC SlabLab";
            PlayerSettings.productName = "STC SlabLab Flat";
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetApiCompatibilityLevel(group,
                ApiCompatibilityLevel.NET_Standard_2_0);

            VolumeSTCubeBuildModeDefines.Configure(group, true);

            DisableXRStartup(group);

            if (group == BuildTargetGroup.Android)
            {
                PlayerSettings.SetApplicationIdentifier(group,
                    "com.stcslablab.flat");
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                PlayerSettings.SetScriptingBackend(group, ScriptingImplementation.IL2CPP);
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
                PlayerSettings.allowedAutorotateToPortrait = false;
                PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
                PlayerSettings.allowedAutorotateToLandscapeLeft = true;
                PlayerSettings.allowedAutorotateToLandscapeRight = true;
            }
            else if (group == BuildTargetGroup.iOS)
            {
                PlayerSettings.SetApplicationIdentifier(group,
                    "com.stcslablab.flat");
                PlayerSettings.SetScriptingBackend(group, ScriptingImplementation.IL2CPP);
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
                PlayerSettings.allowedAutorotateToPortrait = false;
                PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
                PlayerSettings.allowedAutorotateToLandscapeLeft = true;
                PlayerSettings.allowedAutorotateToLandscapeRight = true;
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
            AssetDatabase.SaveAssets();
        }

        private static void DisableXRStartup(BuildTargetGroup group)
        {
            XRGeneralSettingsPerBuildTarget perTarget;
            if (!EditorBuildSettings.TryGetConfigObject(
                    XRGeneralSettings.k_SettingsKey, out perTarget) ||
                perTarget == null ||
                !perTarget.HasManagerSettingsForBuildTarget(group))
                return;

            XRGeneralSettings settings = perTarget.SettingsForBuildTarget(group);
            if (settings == null)
                return;
            settings.InitManagerOnStart = false;
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(perTarget);
        }
    }
}
