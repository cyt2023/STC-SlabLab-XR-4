using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityVolumeRendering;

namespace VolumeSTCubeQuest.EditorTools
{
    /// <summary>Runs the Quest Slab Lab workbench in the normal Unity Game view.</summary>
    [InitializeOnLoad]
    public static class VolumeSTCubeSlabLabPreview
    {
        private const string DesktopMenu = "VolumeSTCube/Mode/Desktop";
        private const string VrMenu = "VolumeSTCube/Mode/VR";

        static VolumeSTCubeSlabLabPreview()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode ||
                !VolumeSTCubeMode.IsDesktop)
                return;
            EditorApplication.delayCall += ConfigureGameViewNativeZoom;
        }

        [MenuItem("VolumeSTCube/Mode/Start Desktop", priority = 1)]
        private static void StartPreview()
        {
            SetMode(VolumeSTCubeApplicationMode.Desktop);
            ConfigureGameViewNativeZoom();
            if (!EditorApplication.isPlaying)
                EditorApplication.isPlaying = true;
            else
                Debug.Log("Desktop mode will be installed the next time Play Mode starts.");
        }

        [MenuItem("VolumeSTCube/Mode/Start VR", priority = 2)]
        private static void StartVr()
        {
            SetMode(VolumeSTCubeApplicationMode.VirtualReality);
            if (!EditorApplication.isPlaying)
                EditorApplication.isPlaying = true;
            else
                Debug.Log("VR mode will be installed the next time Play Mode starts.");
        }

        [MenuItem(DesktopMenu, priority = 20)]
        private static void SelectDesktop()
        {
            SetMode(VolumeSTCubeApplicationMode.Desktop);
        }

        [MenuItem(DesktopMenu, true)]
        private static bool ValidateDesktop()
        {
            RefreshModeChecks();
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        [MenuItem(VrMenu, priority = 21)]
        private static void SelectVr()
        {
            SetMode(VolumeSTCubeApplicationMode.VirtualReality);
        }

        [MenuItem(VrMenu, true)]
        private static bool ValidateVr()
        {
            RefreshModeChecks();
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        [MenuItem("VolumeSTCube/Mode/Stop", priority = 3)]
        private static void StopPreview()
        {
            if (EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;
        }

        [MenuItem("VolumeSTCube/Mode/Stop", true)]
        private static bool ValidateStopPreview()
        {
            return EditorApplication.isPlaying;
        }

        private static void SetMode(VolumeSTCubeApplicationMode mode)
        {
            VolumeSTCubeMode.SetStartupPreference(mode);
            RefreshModeChecks();
            Debug.Log("SlabLab application mode: " + mode + ".");
        }

        private static void RefreshModeChecks()
        {
            Menu.SetChecked(DesktopMenu, VolumeSTCubeMode.IsDesktop);
            Menu.SetChecked(VrMenu, VolumeSTCubeMode.IsVirtualReality);
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
