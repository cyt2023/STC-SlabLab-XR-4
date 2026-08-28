using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VolumeSTCubeQuest.EditorTools
{
    /// <summary>Runs the Quest Slab Lab workbench in the normal Unity Game view.</summary>
    [InitializeOnLoad]
    public static class VolumeSTCubeSlabLabPreview
    {
        private const string PreferenceKey = "VolumeSTCube.SlabLabDesktopPreview";
        private const string ToggleMenu = "VolumeSTCube/Slab Lab/Enable Desktop Preview";

        static VolumeSTCubeSlabLabPreview()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode ||
                !EditorPrefs.GetBool(PreferenceKey, true))
                return;
            EditorApplication.delayCall += ConfigureGameViewNativeZoom;
        }

        [MenuItem("VolumeSTCube/Slab Lab/Start Desktop Preview", priority = 1)]
        private static void StartPreview()
        {
            EditorPrefs.SetBool(PreferenceKey, true);
            Menu.SetChecked(ToggleMenu, true);
            ConfigureGameViewNativeZoom();
            if (!EditorApplication.isPlaying)
                EditorApplication.isPlaying = true;
            else
                Debug.Log("Slab Lab desktop preview will be installed the next time Play Mode starts.");
        }

        [MenuItem(ToggleMenu, priority = 20)]
        private static void TogglePreview()
        {
            bool enabled = !EditorPrefs.GetBool(PreferenceKey, true);
            EditorPrefs.SetBool(PreferenceKey, enabled);
            Menu.SetChecked(ToggleMenu, enabled);
            Debug.Log("Slab Lab desktop preview " + (enabled ? "enabled." : "disabled."));
        }

        [MenuItem(ToggleMenu, true)]
        private static bool ValidateTogglePreview()
        {
            Menu.SetChecked(ToggleMenu, EditorPrefs.GetBool(PreferenceKey, true));
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        [MenuItem("VolumeSTCube/Slab Lab/Stop Desktop Preview", priority = 2)]
        private static void StopPreview()
        {
            if (EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;
        }

        [MenuItem("VolumeSTCube/Slab Lab/Stop Desktop Preview", true)]
        private static bool ValidateStopPreview()
        {
            return EditorApplication.isPlaying;
        }

        private static void ConfigureGameViewNativeZoom()
        {
            // Unity's Free Aspect Game view may retain a 1.5x zoom and render a
            // reduced backing buffer before enlarging it. Reset the internal
            // zoom to one physical display pixel per rendered pixel whenever
            // the desktop workbench starts. Reflection is editor-only and is
            // guarded so a Unity patch-level rename cannot break Play Mode.
            try
            {
                Type gameViewType = typeof(EditorWindow).Assembly.GetType(
                    "UnityEditor.GameView");
                if (gameViewType == null)
                    return;
                FieldInfo zoomField = gameViewType.GetField("m_ZoomArea",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (zoomField == null)
                    return;
                UnityEngine.Object[] gameViews =
                    Resources.FindObjectsOfTypeAll(gameViewType);
                for (int index = 0; index < gameViews.Length; index++)
                {
                    EditorWindow window = gameViews[index] as EditorWindow;
                    object zoomArea = zoomField.GetValue(gameViews[index]);
                    if (window == null || zoomArea == null)
                        continue;
                    PropertyInfo scaleProperty = zoomArea.GetType().GetProperty(
                        "scale", BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic);
                    if (scaleProperty == null || !scaleProperty.CanWrite)
                        continue;
                    scaleProperty.SetValue(zoomArea, Vector2.one, null);
                    window.Repaint();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Unable to reset Game view to native 1x: " +
                    exception.Message);
            }
        }
    }
}
