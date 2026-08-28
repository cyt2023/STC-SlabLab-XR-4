using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UnityVolumeRendering
{
    [CustomEditor(typeof(VolumeControllerObject))]
    public class VolumeRenderedControllerCustomInspector : Editor, IProgressView
    {
        private bool tfSettings = true;
        private bool hlSettings = true;
        private bool clipedSettings = true;
        private bool lightSettings = true;

        private float currentProgress = 1.0f;
        private string currentProgressDescrition = "";
        private bool progressDirty = false;

        public void StartProgress(string title, string description)
        {
        }

        public void FinishProgress(ProgressStatus status = ProgressStatus.Succeeded)
        {
            currentProgress = 1.0f;
        }

        public void UpdateProgress(float totalProgress, float currentStageProgress, string description)
        {
            currentProgressDescrition = description;
            currentProgress = totalProgress;
            progressDirty = true;
        }
        public override bool RequiresConstantRepaint()
        {
            return progressDirty;
        }

        public override void OnInspectorGUI()
        {
            VolumeControllerObject volControlObj = (VolumeControllerObject)target;

            if (currentProgress < 1.0f)
            {
                Rect rect = EditorGUILayout.BeginVertical();
                EditorGUI.ProgressBar(rect, currentProgress, currentProgressDescrition);
                GUILayout.Space(18);
                EditorGUILayout.EndVertical();
            }
            progressDirty = false;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("VolumeSTCube Scene Layout", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            float layerSpacingScale = EditorGUILayout.FloatField("Layer spacing scale", volControlObj.originalSceneLayerSpacingScale);
            float layerYOffset = EditorGUILayout.FloatField("Layer Y offset", volControlObj.originalSceneLayerYOffset);
            Vector2 horizontalOffset = EditorGUILayout.Vector2Field("Map X/Z offset", volControlObj.originalSceneHorizontalOffset);
            Vector2 horizontalScale = EditorGUILayout.Vector2Field("Map X/Z scale", volControlObj.originalSceneHorizontalScale);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(volControlObj, "Change VolumeSTCube scene layout");
                volControlObj.originalSceneLayerSpacingScale = Mathf.Max(0.01f, layerSpacingScale);
                volControlObj.originalSceneLayerYOffset = layerYOffset;
                volControlObj.originalSceneHorizontalOffset = horizontalOffset;
                volControlObj.originalSceneHorizontalScale = new Vector2(
                    Mathf.Approximately(horizontalScale.x, 0.0f) ? 1.0f : horizontalScale.x,
                    Mathf.Approximately(horizontalScale.y, 0.0f) ? 1.0f : horizontalScale.y);
                VolumeSTCubeOriginalSceneAdapter.RefreshController(volControlObj, ToVolumeSTCubeRenderMode(volControlObj.GetRenderMode()));
                EditorUtility.SetDirty(volControlObj);
            }
            if (GUILayout.Button("Apply layout to imported volumes"))
                VolumeSTCubeOriginalSceneAdapter.RefreshController(volControlObj, ToVolumeSTCubeRenderMode(volControlObj.GetRenderMode()));

            // Render mode
            RenderMode oldRenderMode = volControlObj.GetRenderMode();
            RenderMode newRenderMode = (RenderMode)EditorGUILayout.EnumPopup("Render mode", oldRenderMode);
            if (newRenderMode != oldRenderMode)
            {
                Task task = volControlObj.SetRenderModeAsync(newRenderMode, new ProgressHandler(this));
            }

            // Visibility window
            Vector2 visibilityWindow = volControlObj.GetVisibilityWindow();
            visibilityWindow.x = EditorGUILayout.FloatField("Visibility window", visibilityWindow.x);
            volControlObj.SetVisibilityWindow(visibilityWindow);

            if (newRenderMode == RenderMode.IsosurfaceRendering || newRenderMode == RenderMode.DirectVolumeRendering)
            {
                //Debug.Log("newRenderMode");
                float oldThreshold = volControlObj.GetGradientVisibilityThreshold();
                float oldThresholdSqrt = Mathf.Sqrt(oldThreshold); // Convert to square root scaling (=> more precision close to 0)
                float newThreshold = EditorGUILayout.Slider(
                    new GUIContent("Gradient visibility threshold", "Minimum gradient maginitude value that will be visible"),
                    oldThresholdSqrt, 0.0f, 1.0f
                );
                newThreshold = newThreshold * newThreshold; // Convert back to linear scaling
                if (newThreshold != oldThreshold)
                    volControlObj.SetGradientVisibilityThreshold(newThreshold);
            }

            // Transfer function settings
            EditorGUILayout.Space();
            tfSettings = EditorGUILayout.Foldout(tfSettings, "Transfer function");
            if (tfSettings)
            {
                // Transfer function type
                TFRenderMode tfMode = (TFRenderMode)EditorGUILayout.EnumPopup("Transfer function type", volControlObj.GetTransferFunctionMode());
                if (tfMode != volControlObj.GetTransferFunctionMode())
                {
                    Task task = volControlObj.SetTransferFunctionModeAsync(tfMode, new ProgressHandler(this));
                }

                // Show TF button
                if (GUILayout.Button("Edit transfer function"))
                {
                    if (tfMode == TFRenderMode.TF1D)
                        TransferFunctionEditorWindow.ShowWindow(volControlObj);
                    else
                        TransferFunction2DEditorWindow.ShowWindow();
                }
            }

            // Lighting settings
            GUILayout.Space(10);
            lightSettings = EditorGUILayout.Foldout(lightSettings, "Lighting");
            if (lightSettings)
            {
                if (volControlObj.GetRenderMode() == RenderMode.DirectVolumeRendering)
                {
                    Task task = volControlObj.SetLightingEnabledAsync(GUILayout.Toggle(volControlObj.GetLightingEnabled(), "Enable lighting"), new ProgressHandler(this));
                }
                else
                    volControlObj.SetLightingEnabled(false);

                if (volControlObj.GetLightingEnabled() || volControlObj.GetRenderMode() == RenderMode.IsosurfaceRendering)
                {
                    LightSource oldLightSource = volControlObj.GetLightSource();
                    LightSource newLightSource = (LightSource)EditorGUILayout.EnumPopup("Light source", oldLightSource);
                    if (newLightSource != oldLightSource)
                        volControlObj.SetLightSource(newLightSource);

                    // Gradient lighting threshold: Threshold for how low gradients can contribute to lighting.
                    Vector2 gradLightThreshold = volControlObj.GetGradientLightingThreshold();
                    // Convert to square root scaling (=> more precision close to 0)
                    gradLightThreshold = new Vector2(Mathf.Sqrt(gradLightThreshold.x), Mathf.Sqrt(gradLightThreshold.y));
                    EditorGUILayout.MinMaxSlider(
                        new GUIContent("Gradient lighting threshold",
                            "Minimum and maximum threshold for gradient contribution to lighting.\n"
                            + "Voxels with gradient less than min will be unlit, and with gradient >= max will fully shaded."),
                        ref gradLightThreshold.x, ref gradLightThreshold.y, 0.0f, 1.0f
                    );
                    // Convert back to linear scale, before setting updated value.
                    volControlObj.SetGradientLightingThreshold(new Vector2(gradLightThreshold.x * gradLightThreshold.x, gradLightThreshold.y * gradLightThreshold.y));
                }
            }

            //  Highlight settings
            EditorGUILayout.Space();
            hlSettings = EditorGUILayout.Foldout(hlSettings, "Highlight");
            float hlX = volControlObj.GetHighlightPosition().x;
            float hlY = volControlObj.GetHighlightPosition().y;
            float hlR = volControlObj.GetHighlightRadius();
            if (hlSettings)
            {
                hlX = EditorGUILayout.Slider("High light X", hlX, 0, 1.0f);
                hlY = EditorGUILayout.Slider("High light Y", hlY, 0, 1.0f);
                hlR = EditorGUILayout.Slider("High light R", hlR, 0, 1.0f);
                volControlObj.SetHighlightPosition(new Vector2(hlX, hlY));
                volControlObj.SetHighlightRadius(hlR);
            }

            //  Clipped settings
            EditorGUILayout.Space();
            clipedSettings = EditorGUILayout.Foldout(clipedSettings, "Clipped");
            if (clipedSettings)
            {
                Vector2 clipedHeightWindow = volControlObj.GetClipedHeightWindow();
                //clipedHeight = EditorGUILayout.Slider("Cliped height", clipedHeight, 0, 1.0f);
                EditorGUILayout.LabelField("Min Val:", clipedHeightWindow.x.ToString());
                EditorGUILayout.LabelField("Max Val:", clipedHeightWindow.y.ToString());
                EditorGUILayout.MinMaxSlider("Visible value range", ref clipedHeightWindow.x, ref clipedHeightWindow.y, 0.0f, 1.0f);
                volControlObj.SetClipedHeight(clipedHeightWindow);
            }
        }

        private static VolumeSTCubeRenderMode ToVolumeSTCubeRenderMode(RenderMode renderMode)
        {
            return renderMode == RenderMode.IsosurfaceRendering ? VolumeSTCubeRenderMode.Surface : VolumeSTCubeRenderMode.Volume;
        }
    }
}
