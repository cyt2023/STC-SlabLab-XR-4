namespace UnityVolumeRendering
{
    /// <summary>The two supported SlabLab front ends.</summary>
    public enum VolumeSTCubeApplicationMode
    {
        Desktop,
        VirtualReality
    }

    /// <summary>
    /// Single source of truth for choosing the input, camera and UI adapter.
    /// Dataset loading, rendering and analysis are shared by both modes.
    /// </summary>
    public static class VolumeSTCubeMode
    {
        public const string EditorPreferenceKey =
            "VolumeSTCube.SlabLabApplicationMode";
        public const string LegacyDesktopPreferenceKey =
            "VolumeSTCube.SlabLabDesktopPreview";
        public const string RuntimePreferenceKey =
            "VolumeSTCube.SlabLabRuntimeApplicationMode";

        private static bool modeLocked;
        private static VolumeSTCubeApplicationMode lockedMode;

        public static VolumeSTCubeApplicationMode Current
        {
            get
            {
                if (modeLocked)
                    return lockedMode;
                lockedMode = ResolveStartupMode();
                modeLocked = true;
                return lockedMode;
            }
        }

        private static VolumeSTCubeApplicationMode ResolveStartupMode()
        {
            string runtimeSelection = UnityEngine.PlayerPrefs.GetString(
                RuntimePreferenceKey, string.Empty);
            if (runtimeSelection == VolumeSTCubeApplicationMode.VirtualReality.ToString())
                return VolumeSTCubeApplicationMode.VirtualReality;
            if (runtimeSelection == VolumeSTCubeApplicationMode.Desktop.ToString())
                return VolumeSTCubeApplicationMode.Desktop;
#if UNITY_EDITOR
            string configured = UnityEditor.EditorPrefs.GetString(
                EditorPreferenceKey, string.Empty);
            if (configured == VolumeSTCubeApplicationMode.VirtualReality.ToString())
                return VolumeSTCubeApplicationMode.VirtualReality;
            if (configured == VolumeSTCubeApplicationMode.Desktop.ToString())
                return VolumeSTCubeApplicationMode.Desktop;
            return UnityEditor.EditorPrefs.GetBool(
                LegacyDesktopPreferenceKey, true)
                ? VolumeSTCubeApplicationMode.Desktop
                : VolumeSTCubeApplicationMode.VirtualReality;
#elif SLABLAB_VR
            return VolumeSTCubeApplicationMode.VirtualReality;
#elif SLABLAB_DESKTOP || SLABLAB_FLAT
            return VolumeSTCubeApplicationMode.Desktop;
#elif UNITY_STANDALONE || UNITY_IOS
            return VolumeSTCubeApplicationMode.Desktop;
#elif UNITY_ANDROID
            // Android packages must normally declare one of the explicit
            // symbols. VR is the safer fallback for historical Quest builds.
            return VolumeSTCubeApplicationMode.VirtualReality;
#else
            return VolumeSTCubeApplicationMode.Desktop;
#endif
        }

        public static void SetStartupPreference(VolumeSTCubeApplicationMode mode)
        {
            lockedMode = mode;
            modeLocked = true;
            UnityEngine.PlayerPrefs.SetString(RuntimePreferenceKey,
                mode.ToString());
            UnityEngine.PlayerPrefs.Save();
#if UNITY_EDITOR
            UnityEditor.EditorPrefs.SetString(EditorPreferenceKey,
                mode.ToString());
            UnityEditor.EditorPrefs.SetBool(LegacyDesktopPreferenceKey,
                mode == VolumeSTCubeApplicationMode.Desktop);
#endif
        }

        public static void SelectAndReload(VolumeSTCubeApplicationMode mode)
        {
            SetStartupPreference(mode);
            UnityEngine.SceneManagement.Scene activeScene =
                UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (activeScene.buildIndex >= 0)
                UnityEngine.SceneManagement.SceneManager.LoadScene(
                    activeScene.buildIndex);
            else if (!string.IsNullOrEmpty(activeScene.name))
                UnityEngine.SceneManagement.SceneManager.LoadScene(
                    activeScene.name);
        }

        public static bool IsDesktop =>
            Current == VolumeSTCubeApplicationMode.Desktop;

        public static bool IsVirtualReality =>
            Current == VolumeSTCubeApplicationMode.VirtualReality;
    }
}
