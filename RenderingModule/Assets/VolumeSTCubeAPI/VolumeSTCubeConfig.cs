using System;
using UnityEngine;

namespace UnityVolumeRendering
{
    public enum VolumeSTCubeRenderMode
    {
        Volume,
        Surface,
        Hybrid,
        PointPreview
    }

    public enum VolumeSTCubeTimeAxis
    {
        Z,
        Y
    }

    /// <summary>How the dimensions in a RAW collection are interpreted.</summary>
    public enum VolumeSTCubeDataLayout
    {
        /// <summary>Inspect filenames and matching INI metadata at load time.</summary>
        Auto,
        /// <summary>X and Y are spatial; texture Z contains consecutive time samples.</summary>
        XYTime,
        /// <summary>Each RAW file is one XYZ volume; file order is the time dimension.</summary>
        XYZTimeSeries
    }

    /// <summary>Rendering, layout, interaction, and timeline options for one view.</summary>
    [Serializable]
    public class VolumeSTCubeConfig
    {
        public string viewId;
        public string datasetName;
        public VolumeSTCubeRenderMode renderMode = VolumeSTCubeRenderMode.Volume;
        public bool showBoundingBox = true;
        public bool showTimeAxis = true;
        public VolumeSTCubeTimeAxis timeAxis = VolumeSTCubeTimeAxis.Z;
        /// <summary>
        /// Keep Auto for normal use. Set an explicit value only when filenames do not
        /// contain enough information to distinguish XY+T from XYZ+T.
        /// </summary>
        public VolumeSTCubeDataLayout dataLayout = VolumeSTCubeDataLayout.Auto;
        public bool showTimeline = true;
        public bool timelineAutoPlay = false;
        public float timelinePlaybackSeconds = 10.0f;
        public float timelineWindow = 0.05f;
        public bool enableInteraction = true;
        public bool autoGroupUnderVolumeController = true;
        public Vector3 position = Vector3.zero;
        public Vector3 scale = Vector3.one;
        public Vector3 rotation = Vector3.zero;
        public float opacity = 1.0f;
        public string colorMapName;
        public string transferFunctionName;
        public float timeMin = 0.0f;
        public float timeMax = 1.0f;
        public int initialTimeIndex = 0;
        public float variableMin = 0.0f;
        public float variableMax = 1.0f;
        public int pointGridDimX = 64;
        public int pointGridDimY = 64;
        public int pointGridDimT = 32;
        public int pointSplatRadius = 2;

        public static VolumeSTCubeConfig Default(string viewId)
        {
            return new VolumeSTCubeConfig
            {
                viewId = viewId,
                renderMode = VolumeSTCubeRenderMode.Volume,
                showBoundingBox = true,
                showTimeAxis = true,
                timeAxis = VolumeSTCubeTimeAxis.Z,
                dataLayout = VolumeSTCubeDataLayout.Auto,
                showTimeline = true,
                timelineAutoPlay = false,
                timelinePlaybackSeconds = 10.0f,
                timelineWindow = 0.05f,
                enableInteraction = true,
                autoGroupUnderVolumeController = true,
                position = Vector3.zero,
                scale = Vector3.one,
                rotation = Vector3.zero,
                opacity = 1.0f,
                timeMin = 0.0f,
                timeMax = 1.0f,
                initialTimeIndex = 0,
                pointGridDimX = 64,
                pointGridDimY = 64,
                pointGridDimT = 32,
                pointSplatRadius = 2
            };
        }

        internal VolumeSTCubeConfig CopyForLoad()
        {
            return (VolumeSTCubeConfig)MemberwiseClone();
        }
    }
}
