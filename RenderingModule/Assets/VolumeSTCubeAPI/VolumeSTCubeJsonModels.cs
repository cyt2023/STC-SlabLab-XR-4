using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityVolumeRendering
{
    [Serializable]
    public class VolumeSTCubeJsonSpec
    {
        public string viewType;
        public string viewId;
        public string datasetName;
        public string dataMode;
        public string csvFile;
        public VolumeSTCubeJsonCsvColumns csvColumns = new VolumeSTCubeJsonCsvColumns();
        public List<string> rawFiles = new List<string>();
        public List<string> iniFiles = new List<string>();
        public List<VolumeSTCubeJsonPoint> points = new List<VolumeSTCubeJsonPoint>();
        public VolumeSTCubeJsonRender render = new VolumeSTCubeJsonRender();
        public VolumeSTCubeJsonTransform transform = new VolumeSTCubeJsonTransform();
        public VolumeSTCubeJsonFilters filters = new VolumeSTCubeJsonFilters();
        public VolumeSTCubeJsonPointGrid pointGrid = new VolumeSTCubeJsonPointGrid();

        public VolumeSTCubeData ToData()
        {
            VolumeSTCubeData data = new VolumeSTCubeData
            {
                datasetName = datasetName,
                csvFilePath = csvFile,
                csvXColumn = csvColumns != null ? csvColumns.x : "x",
                csvYColumn = csvColumns != null ? csvColumns.y : "y",
                csvTColumn = csvColumns != null ? csvColumns.t : "t",
                csvVariableColumn = csvColumns != null ? csvColumns.variable : "variable",
                preprocessCsvToRaw = IsCsvRawMode(dataMode),
                rawFilePaths = rawFiles ?? new List<string>(),
                iniFilePaths = iniFiles ?? new List<string>()
            };

            if (points != null)
            {
                foreach (VolumeSTCubeJsonPoint point in points)
                {
                    data.x.Add(point.x);
                    data.y.Add(point.y);
                    data.t.Add(point.t);
                    data.variable.Add(point.variable);
                }
            }

            return data;
        }

        private static bool IsCsvRawMode(string mode)
        {
            return string.Equals(mode, "csvRaw", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "geoCsv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "csvToRaw", StringComparison.OrdinalIgnoreCase);
        }

        public VolumeSTCubeConfig ToConfig()
        {
            VolumeSTCubeConfig config = VolumeSTCubeConfig.Default(viewId);
            config.datasetName = datasetName;

            if (render != null)
            {
                VolumeSTCubeRenderMode mode;
                if (Enum.TryParse(render.mode, true, out mode))
                    config.renderMode = mode;
                config.showBoundingBox = render.showBoundingBox;
                config.showTimeAxis = render.showTimeAxis;
                VolumeSTCubeTimeAxis timeAxis;
                if (Enum.TryParse(render.timeAxis, true, out timeAxis))
                    config.timeAxis = timeAxis;
                VolumeSTCubeDataLayout dataLayout;
                if (Enum.TryParse(render.dataLayout, true, out dataLayout))
                    config.dataLayout = dataLayout;
                config.showTimeline = render.showTimeline;
                config.timelineAutoPlay = render.timelineAutoPlay;
                config.timelinePlaybackSeconds = render.timelinePlaybackSeconds;
                config.timelineWindow = render.timelineWindow;
                config.autoGroupUnderVolumeController = render.autoGroupUnderVolumeController;
                config.enableInteraction = render.enableInteraction;
                config.opacity = render.opacity;
            }

            if (transform != null)
            {
                config.position = transform.Position();
                config.rotation = transform.Rotation();
                config.scale = transform.Scale();
            }

            if (filters != null)
            {
                config.timeMin = filters.timeMin;
                config.timeMax = filters.timeMax;
                config.variableMin = filters.variableMin;
                config.variableMax = filters.variableMax;
            }

            if (pointGrid != null)
            {
                config.pointGridDimX = pointGrid.dimX;
                config.pointGridDimY = pointGrid.dimY;
                config.pointGridDimT = pointGrid.dimT;
                config.pointSplatRadius = pointGrid.splatRadius;
            }

            return config;
        }
    }

    [Serializable]
    public class VolumeSTCubeJsonPoint
    {
        public float x;
        public float y;
        public float t;
        public float variable;
    }

    [Serializable]
    public class VolumeSTCubeJsonRender
    {
        public string mode = "Volume";
        public bool showBoundingBox = true;
        public bool showTimeAxis = true;
        public string timeAxis = "Z";
        public string dataLayout = "Auto";
        public bool showTimeline = true;
        public bool timelineAutoPlay = false;
        public float timelinePlaybackSeconds = 10.0f;
        public float timelineWindow = 0.05f;
        public bool autoGroupUnderVolumeController = true;
        public bool enableInteraction = true;
        public float opacity = 1.0f;
    }

    [Serializable]
    public class VolumeSTCubeJsonTransform
    {
        public float[] position = new float[] { 0.0f, 0.0f, 0.0f };
        public float[] rotation = new float[] { 0.0f, 0.0f, 0.0f };
        public float[] scale = new float[] { 1.0f, 1.0f, 1.0f };

        public Vector3 Position()
        {
            return ToVector3(position, Vector3.zero);
        }

        public Vector3 Rotation()
        {
            return ToVector3(rotation, Vector3.zero);
        }

        public Vector3 Scale()
        {
            return ToVector3(scale, Vector3.one);
        }

        private static Vector3 ToVector3(float[] values, Vector3 fallback)
        {
            if (values == null || values.Length < 3)
                return fallback;
            return new Vector3(values[0], values[1], values[2]);
        }
    }

    [Serializable]
    public class VolumeSTCubeJsonFilters
    {
        public float timeMin = 0.0f;
        public float timeMax = 1.0f;
        public float variableMin = 0.0f;
        public float variableMax = 1.0f;
    }

    [Serializable]
    public class VolumeSTCubeJsonPointGrid
    {
        public int dimX = 64;
        public int dimY = 64;
        public int dimT = 32;
        public int splatRadius = 2;
    }

    [Serializable]
    public class VolumeSTCubeJsonCsvColumns
    {
        public string x = "x";
        public string y = "y";
        public string t = "t";
        public string variable = "variable";
    }

    public static class VolumeSTCubeJsonModels
    {
        public static VolumeSTCubeJsonSpec FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                return JsonUtility.FromJson<VolumeSTCubeJsonSpec>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"VolumeSTCubeJsonModels.FromJson failed: {ex.Message}");
                return null;
            }
        }
    }
}
