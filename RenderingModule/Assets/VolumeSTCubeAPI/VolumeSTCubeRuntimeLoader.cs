using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace UnityVolumeRendering
{
    internal static class VolumeSTCubeRuntimeLoader
    {
        internal static VolumeSTCubeView LoadRawDataset(VolumeSTCubeData data, VolumeSTCubeConfig config)
        {
            config.dataLayout = VolumeSTCubeDataLayoutDetector.Resolve(data, config.dataLayout);
            if (config.dataLayout == VolumeSTCubeDataLayout.XYZTimeSeries)
                return LoadXYZTimeSeries(data, config);

            List<GameObject> createdObjects = new List<GameObject>();
            List<VolumeRenderedObject> createdVolumes = new List<VolumeRenderedObject>();
            bool groupUnderController = config.autoGroupUnderVolumeController;
            VolumeControllerObject sceneController = groupUnderController ? VolumeSTCubeOriginalSceneAdapter.EnsureController() : null;
            RemoveXYZTimeSeriesMetadata(sceneController);
            GameObject ownedRoot = !groupUnderController && data.rawFilePaths.Count > 1 ? new GameObject($"VolumeSTCubeView_{config.viewId}") : null;
            GameObject root = sceneController != null ? sceneController.gameObject : ownedRoot;

            for (int i = 0; i < data.rawFilePaths.Count; i++)
            {
                string rawPath = data.rawFilePaths[i];
                if (string.IsNullOrEmpty(rawPath) || !File.Exists(rawPath))
                {
                    Debug.LogError($"VolumeSTCubeRuntimeLoader.LoadRawDataset failed: raw file does not exist: {rawPath}");
                    DestroyRawLoadArtifacts(ownedRoot, createdObjects);
                    return null;
                }

                string iniPath = ResolveIniPath(data, rawPath, i);
                DatasetIniData iniData = DatasetIniReader.ParseIniFile(iniPath);
                if (iniData == null)
                {
                    Debug.LogError($"VolumeSTCubeRuntimeLoader.LoadRawDataset failed: missing or invalid ini file for {rawPath}. Expected {iniPath}");
                    DestroyRawLoadArtifacts(ownedRoot, createdObjects);
                    return null;
                }

                VolumeRenderedObject volumeObject = VolumeSTCubeRawVolumeFactory.Import(rawPath, iniPath, data.datasetName);
                if (!ValidateCreatedVolumeObject(volumeObject, rawPath))
                {
                    DestroyRawLoadArtifacts(ownedRoot, createdObjects);
                    return null;
                }

                if (sceneController != null)
                    VolumeSTCubeOriginalSceneAdapter.AttachVolume(volumeObject, sceneController);
                else if (!groupUnderController && root != null)
                    volumeObject.transform.SetParent(root.transform, false);
                else if (!groupUnderController)
                    root = volumeObject.gameObject;

                if (sceneController == null && config.renderMode != VolumeSTCubeRenderMode.Volume)
                    ApplyRenderMode(volumeObject, config.renderMode);

                createdObjects.Add(volumeObject.gameObject);
                createdVolumes.Add(volumeObject);
            }

            if (groupUnderController && sceneController == null)
                sceneController = VolumeSTCubeOriginalSceneAdapter.EnsureController(createdVolumes);

            if (groupUnderController && sceneController == null)
            {
                Debug.LogError("VolumeSTCubeRuntimeLoader.LoadRawDataset failed: the scene controller could not be created after loading the volume objects.");
                DestroyRawLoadArtifacts(ownedRoot, createdObjects);
                return null;
            }

            if (sceneController != null)
            {
                root = sceneController.gameObject;
                VolumeSTCubeOriginalSceneAdapter.RefreshController(
                    sceneController,
                    config.renderMode,
                    config.timeAxis,
                    config.dataLayout);
            }

            VolumeSTCubeView view = new VolumeSTCubeView
            {
                viewId = config.viewId,
                datasetName = !string.IsNullOrEmpty(config.datasetName) ? config.datasetName : data.datasetName,
                rootObject = root,
                volumeObjects = createdObjects,
                config = config,
                data = data,
                isVisible = true,
                ownsRootObject = !groupUnderController
            };

            if (config.position != Vector3.zero || config.rotation != Vector3.zero || config.scale != Vector3.one)
                view.ApplyTransform(config.position, config.rotation, config.scale);
            if (!Mathf.Approximately(config.opacity, 1.0f))
                view.ApplyOpacity(config.opacity);
            if (config.timeMin != 0.0f || config.timeMax != 1.0f)
                view.ApplyTimeFilter(config.timeMin, config.timeMax);

            return view;
        }

        private static VolumeSTCubeView LoadXYZTimeSeries(VolumeSTCubeData data, VolumeSTCubeConfig config)
        {
            if (data.rawFilePaths == null || data.rawFilePaths.Count == 0)
                return null;

            // XYZ+T can be several gigabytes after Unity expands uint8 values and
            // creates gradient textures. Keep only the selected time file loaded.
            VolumeControllerObject controller = VolumeSTCubeOriginalSceneAdapter.EnsureController();
            VolumeRenderedObject firstVolume = null;
            if (controller == null)
            {
                string firstIni = ResolveIniPath(data, data.rawFilePaths[0], 0);
                firstVolume = VolumeSTCubeRawVolumeFactory.Import(data.rawFilePaths[0], firstIni, data.datasetName);
                if (!ValidateCreatedVolumeObject(firstVolume, data.rawFilePaths[0]))
                    return null;
                controller = VolumeSTCubeOriginalSceneAdapter.EnsureController(
                    new List<VolumeRenderedObject> { firstVolume });
            }

            if (controller == null)
            {
                Debug.LogError("VolumeSTCubeRuntimeLoader.LoadXYZTimeSeries failed: no VolumeControllerObject could be created.");
                DestroyGameObject(firstVolume != null ? firstVolume.gameObject : null);
                return null;
            }

            // Quest uses the unlit DVR presentation. Disable lighting before any
            // runtime frame is created so VolumeRenderedObject never allocates the
            // much larger RGBA gradient volume for this workflow.
            controller.SetLightingEnabled(false);

            VolumeSTCubeRawTimeSeries series = controller.GetComponent<VolumeSTCubeRawTimeSeries>();
            if (series == null)
                series = controller.gameObject.AddComponent<VolumeSTCubeRawTimeSeries>();
            series.Configure(data.rawFilePaths, data.iniFilePaths, config.renderMode, config.opacity);

            if (firstVolume != null)
            {
                VolumeSTCubeOriginalSceneAdapter.RefreshController(
                    controller,
                    config.renderMode,
                    config.timeAxis,
                    VolumeSTCubeDataLayout.XYZTimeSeries);
                VolumeSTCubeOriginalSceneAdapter.ApplyOpacityPreset(controller, config.opacity);
                series.AdoptLoadedVolume(0, firstVolume.gameObject);
            }
            else if (!series.LoadIndex(
                Mathf.Clamp(config.initialTimeIndex, 0,
                    Mathf.Max(0, data.rawFilePaths.Count - 1)), false))
            {
                Debug.LogError("VolumeSTCubeRuntimeLoader.LoadXYZTimeSeries failed to load the first time step.");
                return null;
            }

            VolumeSTCubeView view = new VolumeSTCubeView
            {
                viewId = config.viewId,
                datasetName = !string.IsNullOrEmpty(config.datasetName) ? config.datasetName : data.datasetName,
                rootObject = controller.gameObject,
                volumeObjects = series.CurrentVolumeObject != null
                    ? new List<GameObject> { series.CurrentVolumeObject }
                    : new List<GameObject>(),
                config = config,
                data = data,
                isVisible = true,
                ownsRootObject = false
            };

            if (config.position != Vector3.zero || config.rotation != Vector3.zero || config.scale != Vector3.one)
                view.ApplyTransform(config.position, config.rotation, config.scale);
            if (config.timeMin != 0.0f || config.timeMax != 1.0f)
                view.ApplyTimeFilter(config.timeMin, config.timeMax);

            Debug.Log($"VolumeSTCube detected XYZ+Time: {data.rawFilePaths.Count} time files; only the selected XYZ volume is loaded.");
            return view;
        }

        internal static VolumeSTCubeView LoadPointPreview(VolumeSTCubeData data, VolumeSTCubeConfig config)
        {
            if (data.HasCsvFile() && !data.HasPointData() && !LoadPointDataFromCsv(data))
                return null;

            GameObject root = new GameObject($"VolumeSTCubePointPreview_{config.viewId}");
            int count = Mathf.Min(data.Count(), 2000);
            List<GameObject> points = new List<GameObject>();

            for (int i = 0; i < count; i++)
            {
                GameObject point = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                point.name = $"Point_{i}";
                point.transform.SetParent(root.transform, false);
                point.transform.localPosition = new Vector3(data.x[i], data.t[i], data.y[i]);
                point.transform.localScale = Vector3.one * 0.03f;
                points.Add(point);
            }

            if (data.Count() > count)
                Debug.LogWarning($"VolumeSTCubeRuntimeLoader.LoadPointPreview displayed {count} of {data.Count()} points. Full interpolation still belongs in preprocessing.");

            VolumeSTCubeView view = new VolumeSTCubeView
            {
                viewId = config.viewId,
                datasetName = config.datasetName,
                rootObject = root,
                volumeObjects = points,
                config = config,
                data = data,
                isVisible = true
            };
            view.ApplyTransform(config.position, config.rotation, config.scale);
            return view;
        }

        internal static VolumeSTCubeView LoadPointDataset(VolumeSTCubeData data, VolumeSTCubeConfig config)
        {
            if (data.HasCsvFile() && !data.HasPointData() && !LoadPointDataFromCsv(data))
                return null;

            bool groupUnderController = config.autoGroupUnderVolumeController;
            VolumeControllerObject sceneController = groupUnderController
                ? VolumeSTCubeOriginalSceneAdapter.EnsureController()
                : null;
            GameObject ownedRoot = !groupUnderController
                ? new GameObject($"VolumeSTCubeView_{config.viewId}")
                : null;
            GameObject root = sceneController != null ? sceneController.gameObject : ownedRoot;

            VolumeDataset dataset = CreateDatasetFromPointData(data, config);
            if (dataset == null)
            {
                DestroyGameObject(ownedRoot);
                return null;
            }

            VolumeRenderedObject volumeObject = VolumeObjectFactory.CreateObject(dataset);
            if (!ValidateCreatedVolumeObject(volumeObject, "point data"))
            {
                DestroyGameObject(ownedRoot);
                return null;
            }

            if (groupUnderController && sceneController == null)
                sceneController = VolumeSTCubeOriginalSceneAdapter.EnsureController(new List<VolumeRenderedObject> { volumeObject });

            if (groupUnderController && sceneController == null)
            {
                Debug.LogError("VolumeSTCubeRuntimeLoader.LoadPointDataset failed: the scene controller could not be created after generating the volume object.");
                DestroyGameObject(volumeObject.gameObject);
                return null;
            }

            if (sceneController != null)
            {
                VolumeSTCubeOriginalSceneAdapter.AttachVolume(volumeObject, sceneController);
                VolumeSTCubeOriginalSceneAdapter.RefreshController(
                    sceneController,
                    config.renderMode,
                    config.timeAxis,
                    config.dataLayout);
                root = sceneController.gameObject;
            }
            else
            {
                volumeObject.transform.SetParent(root.transform, false);
                ApplyRenderMode(volumeObject, config.renderMode);
            }

            VolumeSTCubeView view = new VolumeSTCubeView
            {
                viewId = config.viewId,
                datasetName = !string.IsNullOrEmpty(config.datasetName) ? config.datasetName : data.datasetName,
                rootObject = root,
                volumeObjects = new List<GameObject> { volumeObject.gameObject },
                config = config,
                data = data,
                isVisible = true,
                ownsRootObject = !groupUnderController
            };

            if (config.position != Vector3.zero || config.rotation != Vector3.zero || config.scale != Vector3.one)
                view.ApplyTransform(config.position, config.rotation, config.scale);
            if (!Mathf.Approximately(config.opacity, 1.0f))
                view.ApplyOpacity(config.opacity);
            if (config.timeMin != 0.0f || config.timeMax != 1.0f)
                view.ApplyTimeFilter(config.timeMin, config.timeMax);

            return view;
        }

        private static string ResolveIniPath(VolumeSTCubeData data, string rawPath, int index)
        {
            if (data.iniFilePaths != null && index < data.iniFilePaths.Count && !string.IsNullOrEmpty(data.iniFilePaths[index]))
                return data.iniFilePaths[index];

            if (Path.GetExtension(rawPath).ToLowerInvariant() == ".ini")
                return rawPath;

            return rawPath + ".ini";
        }

        private static void DestroyGameObject(GameObject obj)
        {
            if (obj == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj);
        }

        private static void RemoveXYZTimeSeriesMetadata(VolumeControllerObject controller)
        {
            if (controller == null)
                return;

            VolumeSTCubeRawTimeSeries series = controller.GetComponent<VolumeSTCubeRawTimeSeries>();
            if (series == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(series);
            else
                Object.DestroyImmediate(series);
        }

        private static void DestroyRawLoadArtifacts(GameObject ownedRoot, List<GameObject> createdObjects)
        {
            for (int i = 0; i < createdObjects.Count; i++)
                DestroyGameObject(createdObjects[i]);

            if (ownedRoot != null)
                DestroyGameObject(ownedRoot);
        }

        private static VolumeDataset CreateDatasetFromPointData(VolumeSTCubeData data, VolumeSTCubeConfig config)
        {
            int dimX = Mathf.Clamp(config.pointGridDimX, 2, 256);
            int dimY = Mathf.Clamp(config.pointGridDimY, 2, 256);
            int dimT = Mathf.Clamp(config.pointGridDimT, 2, 256);
            int radius = Mathf.Clamp(config.pointSplatRadius, 0, 8);
            int voxelCount = dimX * dimY * dimT;

            float minX = Min(data.x);
            float maxX = Max(data.x);
            float minY = Min(data.y);
            float maxY = Max(data.y);
            float minT = Min(data.t);
            float maxT = Max(data.t);
            float minV = data.variable != null && data.variable.Count > 0 ? Min(data.variable) : 0.0f;
            float maxV = data.variable != null && data.variable.Count > 0 ? Max(data.variable) : 1.0f;

            float[] sum = new float[voxelCount];
            float[] weight = new float[voxelCount];

            for (int i = 0; i < data.x.Count; i++)
            {
                int cx = ToGrid(data.x[i], minX, maxX, dimX);
                int cy = ToGrid(data.y[i], minY, maxY, dimY);
                int ct = ToGrid(data.t[i], minT, maxT, dimT);
                float value = data.variable != null && data.variable.Count > 0 ? data.variable[i] : 1.0f;
                float normalizedValue = Mathf.Lerp(1.0f, 223.0f, Normalize(value, minV, maxV));

                for (int dz = -radius; dz <= radius; dz++)
                {
                    int z = ct + dz;
                    if (z < 0 || z >= dimT)
                        continue;

                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        int y = cy + dy;
                        if (y < 0 || y >= dimY)
                            continue;

                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int x = cx + dx;
                            if (x < 0 || x >= dimX)
                                continue;

                            float distance = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                            if (distance > radius + 0.001f)
                                continue;

                            float w = radius == 0 ? 1.0f : 1.0f - (distance / (radius + 1.0f));
                            int index = x + y * dimX + z * dimX * dimY;
                            sum[index] += normalizedValue * w;
                            weight[index] += w;
                        }
                    }
                }
            }

            float[] volumeData = new float[voxelCount];
            for (int i = 0; i < voxelCount; i++)
                volumeData[i] = weight[i] > 0.0f ? sum[i] / weight[i] : 0.0f;

            VolumeDataset dataset = ScriptableObject.CreateInstance<VolumeDataset>();
            dataset.datasetName = !string.IsNullOrEmpty(data.datasetName) ? data.datasetName : "point_data_volume";
            dataset.filePath = "generated-from-point-data";
            dataset.dimX = dimX;
            dataset.dimY = dimY;
            dataset.dimZ = dimT;
            dataset.data = volumeData;
            dataset.scale = new Vector3(1.0f, 1.0f, Mathf.Max(0.1f, (float)dimT / Mathf.Max(dimX, dimY)));
            // t occupies texture Z, while the rendered time direction is world Y.
            dataset.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            dataset.FixDimensions();

            Debug.Log($"VolumeSTCubeRuntimeLoader: generated VolumeDataset from {data.x.Count} points at {dimX}x{dimY}x{dimT}.");
            return dataset;
        }

        private static bool LoadPointDataFromCsv(VolumeSTCubeData data)
        {
            if (string.IsNullOrEmpty(data.csvFilePath) || !File.Exists(data.csvFilePath))
            {
                Debug.LogError($"VolumeSTCubeRuntimeLoader.LoadPointDataFromCsv failed: CSV file does not exist: {data.csvFilePath}");
                return false;
            }

            string[] lines = File.ReadAllLines(data.csvFilePath);
            if (lines.Length < 2)
            {
                Debug.LogError($"VolumeSTCubeRuntimeLoader.LoadPointDataFromCsv failed: CSV must contain a header and at least one data row: {data.csvFilePath}");
                return false;
            }

            string[] headers = SplitCsvLine(lines[0]);
            int xIndex = FindHeader(headers, data.csvXColumn, "x");
            int yIndex = FindHeader(headers, data.csvYColumn, "y");
            int tIndex = FindHeader(headers, data.csvTColumn, "t", "time");
            int variableIndex = FindHeader(headers, data.csvVariableColumn, "variable", "value", "val");

            if (xIndex < 0 || yIndex < 0 || tIndex < 0 || variableIndex < 0)
            {
                Debug.LogError("VolumeSTCubeRuntimeLoader.LoadPointDataFromCsv failed: CSV header must include the configured x, y, t, and variable columns.");
                return false;
            }

            data.x.Clear();
            data.y.Clear();
            data.t.Clear();
            data.variable.Clear();

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;

                string[] parts = SplitCsvLine(lines[i]);
                if (!TryGetFloat(parts, xIndex, out float x) ||
                    !TryGetFloat(parts, yIndex, out float y) ||
                    !TryGetFloat(parts, tIndex, out float t) ||
                    !TryGetFloat(parts, variableIndex, out float variable))
                {
                    Debug.LogError($"VolumeSTCubeRuntimeLoader.LoadPointDataFromCsv failed: invalid numeric value at CSV line {i + 1}.");
                    return false;
                }

                data.x.Add(x);
                data.y.Add(y);
                data.t.Add(t);
                data.variable.Add(variable);
            }

            Debug.Log($"VolumeSTCubeRuntimeLoader: loaded {data.x.Count} point rows from CSV: {data.csvFilePath}");
            return data.x.Count > 0;
        }

        private static string[] SplitCsvLine(string line)
        {
            return line.Split(',');
        }

        private static int FindHeader(string[] headers, params string[] names)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                string header = headers[i].Trim().ToLowerInvariant();
                for (int j = 0; j < names.Length; j++)
                {
                    if (header == names[j])
                        return i;
                }
            }

            return -1;
        }

        private static bool TryGetFloat(string[] parts, int index, out float value)
        {
            value = 0.0f;
            if (index < 0 || index >= parts.Length)
                return false;

            return float.TryParse(parts[index].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static int ToGrid(float value, float min, float max, int dimension)
        {
            return Mathf.Clamp(Mathf.RoundToInt(Normalize(value, min, max) * (dimension - 1)), 0, dimension - 1);
        }

        private static float Normalize(float value, float min, float max)
        {
            if (Mathf.Approximately(min, max))
                return 0.5f;
            return Mathf.Clamp01((value - min) / (max - min));
        }

        private static float Min(List<float> values)
        {
            float min = float.MaxValue;
            for (int i = 0; i < values.Count; i++)
                min = Mathf.Min(min, values[i]);
            return min;
        }

        private static float Max(List<float> values)
        {
            float max = float.MinValue;
            for (int i = 0; i < values.Count; i++)
                max = Mathf.Max(max, values[i]);
            return max;
        }

        private static bool ValidateCreatedVolumeObject(VolumeRenderedObject volumeObject, string rawPath)
        {
            if (volumeObject == null)
            {
                Debug.LogError($"VolumeSTCubeRuntimeLoader.LoadRawDataset failed: VolumeObjectFactory returned null for {rawPath}.");
                return false;
            }

            if (volumeObject.volumeContainerObject == null)
            {
                Debug.LogError($"VolumeSTCubeRuntimeLoader.LoadRawDataset failed: VolumeRenderedObject has no VolumeContainer for {rawPath}.");
                DestroyGameObject(volumeObject.gameObject);
                return false;
            }

            if (volumeObject.volumeContainerObject.GetComponent<MeshRenderer>() == null)
            {
                Debug.LogError($"VolumeSTCubeRuntimeLoader.LoadRawDataset failed: VolumeContainer has no MeshRenderer for {rawPath}.");
                DestroyGameObject(volumeObject.gameObject);
                return false;
            }

            return true;
        }

        private static void ApplyRenderMode(VolumeRenderedObject volumeObject, VolumeSTCubeRenderMode renderMode)
        {
            switch (renderMode)
            {
                case VolumeSTCubeRenderMode.Volume:
                    volumeObject.SetRenderMode(RenderMode.DirectVolumeRendering);
                    break;
                case VolumeSTCubeRenderMode.Surface:
                    volumeObject.SetRenderMode(RenderMode.IsosurfaceRendering);
                    break;
                case VolumeSTCubeRenderMode.Hybrid:
                    Debug.LogWarning("VolumeSTCubeRenderMode.Hybrid is not directly supported by the original renderer yet. Falling back to DirectVolumeRendering.");
                    volumeObject.SetRenderMode(RenderMode.DirectVolumeRendering);
                    break;
                case VolumeSTCubeRenderMode.PointPreview:
                    Debug.LogWarning("PointPreview render mode is only used for point data. Raw data is rendered as a volume.");
                    volumeObject.SetRenderMode(RenderMode.DirectVolumeRendering);
                    break;
            }
        }
    }
}
