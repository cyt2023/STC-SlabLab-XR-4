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

        public static VolumeSTCubeApplicationMode Current
        {
            get
            {
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
        }

        public static bool IsDesktop =>
            Current == VolumeSTCubeApplicationMode.Desktop;

        public static bool IsVirtualReality =>
            Current == VolumeSTCubeApplicationMode.VirtualReality;
    }
}
