using System.Collections.Generic;
using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Public facade for creating and controlling VolumeSTCube views.
    /// External scripts should depend on this class instead of renderer internals.
    /// </summary>
    public static class VolumeSTCubeAPI
    {
        /// <summary>Creates a managed view from RAW paths, CSV input, or point arrays.</summary>
        /// <returns>The created view, or null when validation or loading fails.</returns>
        public static VolumeSTCubeView CreateView(VolumeSTCubeData data, VolumeSTCubeConfig config)
        {
            if (data == null || !data.Validate())
            {
                Debug.LogError("VolumeSTCubeAPI.CreateView failed: invalid data.");
                return null;
            }

            data = data.CopyForLoad();

            if (config == null)
                config = VolumeSTCubeConfig.Default(string.Empty);
            else
                config = config.CopyForLoad();

            if (string.IsNullOrEmpty(config.viewId))
                config.viewId = string.IsNullOrEmpty(data.datasetName) ? System.Guid.NewGuid().ToString("N") : data.datasetName;

            if (string.IsNullOrEmpty(data.datasetName))
                data.datasetName = config.datasetName;

            if (VolumeSTCubeViewRegistry.Contains(config.viewId))
                DestroyView(config.viewId);

            VolumeSTCubeView view;
            if (data.preprocessCsvToRaw)
            {
                if (!VolumeSTCubeCsvRawProcessor.TryProcess(data, config, out VolumeSTCubeData rawData))
                {
                    Debug.LogError("VolumeSTCubeAPI.CreateView failed: CSV preprocessing did not produce a raw dataset.");
                    return null;
                }

                view = VolumeSTCubeRuntimeLoader.LoadRawDataset(rawData, config);
                data = rawData;
            }
            else if (data.HasRawFiles())
            {
                view = VolumeSTCubeRuntimeLoader.LoadRawDataset(data, config);
            }
            else if (config.renderMode == VolumeSTCubeRenderMode.PointPreview)
            {
                view = VolumeSTCubeRuntimeLoader.LoadPointPreview(data, config);
            }
            else
            {
                view = VolumeSTCubeRuntimeLoader.LoadPointDataset(data, config);
            }

            if (view == null)
            {
                Debug.LogError($"VolumeSTCubeAPI.CreateView failed: loader returned null for viewId '{config.viewId}'.");
                return null;
            }

            VolumeSTCubeViewRegistry.AddOrReplace(view);
            view.CreateTimeline();
            return view;
        }

        /// <summary>Creates a view from the JSON schema documented in docs/API_USAGE.md.</summary>
        public static VolumeSTCubeView CreateViewFromJson(string json)
        {
            VolumeSTCubeJsonSpec spec = VolumeSTCubeJsonModels.FromJson(json);
            if (spec == null)
            {
                Debug.LogError("VolumeSTCubeAPI.CreateViewFromJson failed: JSON could not be parsed.");
                return null;
            }

            return CreateView(spec.ToData(), spec.ToConfig());
        }

        public static VolumeSTCubeView CreateViewFromCsv(string csvFilePath, VolumeSTCubeConfig config = null)
        {
            return CreateViewFromCsv(csvFilePath, "x", "y", "t", "variable", config);
        }

        public static VolumeSTCubeView CreateViewFromCsv(
            string csvFilePath,
            string xColumn,
            string yColumn,
            string tColumn,
            string variableColumn,
            VolumeSTCubeConfig config = null)
        {
            VolumeSTCubeData data = new VolumeSTCubeData
            {
                csvFilePath = csvFilePath,
                csvXColumn = xColumn,
                csvYColumn = yColumn,
                csvTColumn = tColumn,
                csvVariableColumn = variableColumn
            };

            if (config == null)
                config = VolumeSTCubeConfig.Default(System.Guid.NewGuid().ToString("N"));

            return CreateView(data, config);
        }

        public static VolumeSTCubeView CreateViewFromCsvRaw(string csvFilePath, VolumeSTCubeConfig config = null)
        {
            return CreateViewFromCsvRaw(csvFilePath, "x", "y", string.Empty, "z", config);
        }

        public static VolumeSTCubeView CreateViewFromGeoCsv(string csvFilePath, VolumeSTCubeConfig config = null)
        {
            return CreateViewFromCsvRaw(csvFilePath, "lng", "lat", string.Empty, "val", config);
        }

        public static VolumeSTCubeView CreateViewFromCsvRaw(
            string csvFilePath,
            string xColumn,
            string yColumn,
            string tColumn,
            string valueColumn,
            VolumeSTCubeConfig config = null)
        {
            VolumeSTCubeData data = new VolumeSTCubeData
            {
                csvFilePath = csvFilePath,
                csvXColumn = xColumn,
                csvYColumn = yColumn,
                csvTColumn = tColumn,
                csvVariableColumn = valueColumn,
                preprocessCsvToRaw = true
            };

            if (config == null)
                config = VolumeSTCubeConfig.Default(System.Guid.NewGuid().ToString("N"));

            return CreateView(data, config);
        }

        /// <summary>
        /// Loads every top-level .raw file in a directory using its matching .raw.ini file.
        /// The layout is auto-detected as XY+T or XYZ+T unless config.dataLayout overrides it.
        /// </summary>
        public static VolumeSTCubeView CreateViewFromRawDirectory(string rawDirectory, VolumeSTCubeConfig config = null)
        {
            if (TryCreateViewFromRawDirectory(rawDirectory, config, out VolumeSTCubeView view, out string error))
                return view;

            Debug.LogError($"VolumeSTCubeAPI.CreateViewFromRawDirectory failed: {error}");
            return null;
        }

        /// <summary>
        /// Non-throwing RAW-directory loader for callers that need a user-facing error message.
        /// </summary>
        /// <param name="rawDirectory">Folder containing top-level .raw and .raw.ini pairs.</param>
        /// <param name="config">Optional rendering configuration.</param>
        /// <param name="view">Created view when the method returns true.</param>
        /// <param name="error">Failure description when the method returns false.</param>
        public static bool TryCreateViewFromRawDirectory(
            string rawDirectory,
            VolumeSTCubeConfig config,
            out VolumeSTCubeView view,
            out string error)
        {
            view = null;
            if (!VolumeSTCubeRawDirectorySource.TryCreate(rawDirectory, out VolumeSTCubeData data, out error))
                return false;

            config = config != null
                ? config.CopyForLoad()
                : VolumeSTCubeConfig.Default(data.datasetName);
            if (string.IsNullOrEmpty(config.datasetName))
                config.datasetName = data.datasetName;
            config.autoGroupUnderVolumeController = true;
            config.dataLayout = VolumeSTCubeDataLayoutDetector.Resolve(data, config.dataLayout);

            VolumeSTCubeOriginalSceneAdapter.ClearExistingVolumes(VolumeSTCubeOriginalSceneAdapter.EnsureController());
            view = CreateView(data, config);
            if (view != null)
                return true;

            error = $"Unity failed to build the '{data.datasetName}' view. Check the Console for renderer details.";
            return false;
        }

        /// <summary>
        /// Creates a view from dataset metadata that was already discovered by
        /// VolumeSTCubeRawSliceReader. This avoids enumerating the same Quest
        /// storage directory again when the user switches variables.
        /// </summary>
        public static bool TryCreateViewFromRawDataset(
            VolumeSTCubeSliceDataset dataset,
            VolumeSTCubeConfig config,
            out VolumeSTCubeView view,
            out string error)
        {
            view = null;
            error = string.Empty;
            if (dataset == null || dataset.RawPaths == null ||
                dataset.RawPaths.Length == 0)
            {
                error = "The discovered RAW dataset contains no time files.";
                return false;
            }

            VolumeSTCubeData data = new VolumeSTCubeData
            {
                datasetName = dataset.Name
            };
            data.rawFilePaths.AddRange(dataset.RawPaths);
            if (dataset.IniPaths != null)
                data.iniFilePaths.AddRange(dataset.IniPaths);

            config = config != null
                ? config.CopyForLoad()
                : VolumeSTCubeConfig.Default(data.datasetName);
            if (string.IsNullOrEmpty(config.datasetName))
                config.datasetName = data.datasetName;
            config.autoGroupUnderVolumeController = true;
            config.dataLayout = VolumeSTCubeDataLayoutDetector.Resolve(
                data, config.dataLayout);

            VolumeSTCubeOriginalSceneAdapter.ClearExistingVolumes(
                VolumeSTCubeOriginalSceneAdapter.EnsureController());
            view = CreateView(data, config);
            if (view != null)
                return true;

            error = "The discovered RAW dataset could not be loaded.";
            return false;
        }

        public static VolumeSTCubeView CreateViewFromPoints(
            IList<float> x,
            IList<float> y,
            IList<float> t,
            IList<float> variable,
            VolumeSTCubeConfig config = null)
        {
            VolumeSTCubeData data = new VolumeSTCubeData();
            CopyValues(x, data.x);
            CopyValues(y, data.y);
            CopyValues(t, data.t);
            CopyValues(variable, data.variable);

            if (config == null)
                config = VolumeSTCubeConfig.Default(System.Guid.NewGuid().ToString("N"));

            return CreateView(data, config);
        }

        public static bool UpdateView(string viewId, VolumeSTCubeData newData)
        {
            VolumeSTCubeView existing = GetView(viewId);
            if (existing == null)
            {
                Debug.LogError($"VolumeSTCubeAPI.UpdateView failed: viewId '{viewId}' was not found.");
                return false;
            }

            VolumeSTCubeConfig config = existing.config;
            DestroyView(viewId);
            return CreateView(newData, config) != null;
        }

        public static bool ApplyTimeFilter(string viewId, float tMin, float tMax)
        {
            VolumeSTCubeView view = GetView(viewId);
            if (view == null)
                return false;

            view.ApplyTimeFilter(tMin, tMax);
            return true;
        }

        public static bool SetVisible(string viewId, bool visible)
        {
            VolumeSTCubeView view = GetView(viewId);
            if (view == null)
                return false;

            view.SetVisible(visible);
            return true;
        }

        public static bool DestroyView(string viewId)
        {
            VolumeSTCubeView view = GetView(viewId);
            if (view == null)
                return false;

            view.Destroy();
            VolumeSTCubeViewRegistry.Remove(viewId);
            return true;
        }

        public static void ClearAll()
        {
            foreach (VolumeSTCubeView view in VolumeSTCubeViewRegistry.Snapshot())
                view.Destroy();
            VolumeSTCubeViewRegistry.Clear();
        }

        public static VolumeSTCubeView GetView(string viewId)
        {
            return VolumeSTCubeViewRegistry.Get(viewId);
        }

        private static void CopyValues(IList<float> source, List<float> target)
        {
            if (source == null)
                return;

            for (int i = 0; i < source.Count; i++)
                target.Add(source[i]);
        }

    }
}
