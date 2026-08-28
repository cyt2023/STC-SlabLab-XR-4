using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR.Features;

namespace VolumeSTCubeQuest.EditorTools
{
    public sealed class VolumeSTCubeQuestShaderStripper : IPreprocessShaders
    {
        public int callbackOrder => 0;

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (shader.name != "VolumeRendering/DirectVolumeRenderingShader" &&
                shader.name != "VolumeRendering/HeatMapRendering" &&
                shader.name != "VolumeRendering/MixedVolumeRenderingShader")
                return;

            for (int index = data.Count - 1; index >= 0; index--)
            {
                ShaderKeyword[] keywords = data[index].shaderKeywordSet.GetShaderKeywords();
                if (!Has(keywords, "MODE_DVR") || Has(keywords, "MODE_MIP") || Has(keywords, "MODE_SURF") ||
                    Has(keywords, "CROSS_SECTION_ON") || Has(keywords, "LIGHTING_ON") ||
                    Has(keywords, "USE_MAIN_LIGHT") || Has(keywords, "CUBIC_INTERPOLATION_ON") ||
                    Has(keywords, "HIGHLIGHT_OPACITY") || Has(keywords, "HIGHLIGHT_CLIPED") ||
                    Has(keywords, "HIGHLIGHT_INTENSITY"))
                    data.RemoveAt(index);
            }
        }

        private static bool Has(ShaderKeyword[] keywords, string name)
        {
            for (int index = 0; index < keywords.Length; index++)
            {
                if (keywords[index].name == name)
                    return true;
            }
            return false;
        }
    }

    public static class VolumeSTCubeQuestBuild
    {
        private const string PackageId = "com.volumestcube.quest";
        private const string LoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";
        private const string QuestFeatureId = "com.unity.openxr.feature.oculusquest";
        private const string TouchFeatureId = "com.unity.openxr.feature.input.oculustouch";

        [MenuItem("VolumeSTCube/Quest/Configure Project")]
        public static void ConfigureQuest()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                    throw new InvalidOperationException("Could not switch the project to Android.");
            }

            PlayerSettings.companyName = "VolumeSTCube";
            PlayerSettings.productName = "VolumeSTCube Quest";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, PackageId);
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Android, ApiCompatibilityLevel.NET_Standard_2_0);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.MTRendering = true;
            PlayerSettings.graphicsJobs = false;
            // The volume shader has many keyword combinations. Mesh-channel stripping
            // forces Unity to compile every combination during each Android build.
            PlayerSettings.stripUnusedMeshComponents = false;

            XRGeneralSettingsPerBuildTarget perTarget = GetOrCreateXRSettings();
            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            XRGeneralSettings general = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            general.InitManagerOnStart = true;
            XRManagerSettings manager = perTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            if (!XRPackageMetadataStore.AssignLoader(manager, LoaderType, BuildTargetGroup.Android) &&
                !XRPackageMetadataStore.IsLoaderAssigned(LoaderType, BuildTargetGroup.Android))
                throw new InvalidOperationException("Could not assign the OpenXR loader for Android.");

            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            EnableFeature(QuestFeatureId);
            EnableFeature(TouchFeatureId);

            EditorUtility.SetDirty(perTarget);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(manager);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("VolumeSTCube Quest configuration complete.");
        }

        [MenuItem("VolumeSTCube/Quest/Build APK")]
        public static void BuildQuestApk()
        {
            ConfigureQuest();
            string outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds"));
            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(outputDirectory, "VolumeSTCubeQuest.apk");
            string[] scenes = { "Assets/Scenes/mainScene.unity" };
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException(
                    "Quest APK build failed: " + report.summary.result + ", errors=" + report.summary.totalErrors);
            Debug.Log("VolumeSTCube Quest APK built: " + outputPath + " (" + report.summary.totalSize + " bytes)");
        }

        private static XRGeneralSettingsPerBuildTarget GetOrCreateXRSettings()
        {
            XRGeneralSettingsPerBuildTarget settings;
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out settings);
            if (settings != null)
                return settings;

            const string folder = "Assets/XR";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets", "XR");
            settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            settings.name = "XR General Settings Per Build Target";
            const string assetPath = folder + "/XRGeneralSettingsPerBuildTarget.asset";
            AssetDatabase.CreateAsset(settings, assetPath);
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
            AssetDatabase.SaveAssets();
            return settings;
        }

        private static void EnableFeature(string featureId)
        {
            OpenXRFeature feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, featureId);
            if (feature == null)
                throw new InvalidOperationException("OpenXR feature was not found: " + featureId);
            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }
    }
}
