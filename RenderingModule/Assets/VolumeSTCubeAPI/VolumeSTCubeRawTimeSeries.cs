using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityVolumeRendering
{
    [DisallowMultipleComponent]
    public sealed class VolumeSTCubeRawTimeSeries : MonoBehaviour
    {
        [SerializeField] private List<string> rawFilePaths = new List<string>();
        [SerializeField] private List<string> iniFilePaths = new List<string>();
        [SerializeField] private int currentIndex = -1;
        [SerializeField] private VolumeSTCubeRenderMode renderMode = VolumeSTCubeRenderMode.Volume;
        [SerializeField, Range(0.0f, 1.0f)] private float opacity = 0.9f;

        private readonly Dictionary<int, VolumeDataset> readyDatasets = new Dictionary<int, VolumeDataset>();
        private int requestedIndex = -1;
        private int loadingIndex = -1;
        private int lastDirection = 1;
        private int configurationVersion;

        public int Count => rawFilePaths != null ? rawFilePaths.Count : 0;
        public int CurrentIndex => currentIndex;
        public int RequestedIndex => requestedIndex >= 0 ? requestedIndex : currentIndex;
        public bool IsTransitionPending => requestedIndex >= 0 && requestedIndex != currentIndex;
        public GameObject CurrentVolumeObject { get; private set; }

        public void Configure(
            IList<string> rawFiles,
            IList<string> iniFiles,
            VolumeSTCubeRenderMode targetRenderMode,
            float targetOpacity)
        {
            configurationVersion++;
            ReleaseReadyDatasets();
            rawFilePaths = rawFiles != null ? new List<string>(rawFiles) : new List<string>();
            iniFilePaths = new List<string>();
            for (int i = 0; i < rawFilePaths.Count; i++)
            {
                string iniPath = iniFiles != null && i < iniFiles.Count && !string.IsNullOrEmpty(iniFiles[i])
                    ? iniFiles[i]
                    : rawFilePaths[i] + ".ini";
                iniFilePaths.Add(iniPath);
            }

            renderMode = targetRenderMode;
            opacity = Mathf.Clamp01(targetOpacity);
            currentIndex = -1;
            requestedIndex = -1;
            loadingIndex = -1;
            lastDirection = 1;
            CurrentVolumeObject = null;
        }

        public bool LoadIndex(int requestedIndex, bool force = false)
        {
            if (Count == 0)
                return false;

            int index = Mathf.Clamp(requestedIndex, 0, Count - 1);
            if (!force && index == currentIndex && ResolveCurrentVolume() != null)
            {
                this.requestedIndex = index;
                return true;
            }

            if (currentIndex >= 0 && index != currentIndex)
                lastDirection = index > currentIndex ? 1 : -1;
            this.requestedIndex = index;

            VolumeDataset preparedDataset;
            if (!force && readyDatasets.TryGetValue(index, out preparedDataset))
            {
                readyDatasets.Remove(index);
                return ActivatePreparedDataset(index, preparedDataset);
            }

            if (!force)
            {
                StartBackgroundLoad(index);
                return true;
            }

            string rawPath = rawFilePaths[index];
            string iniPath = index < iniFilePaths.Count ? iniFilePaths[index] : rawPath + ".ini";
            if (!File.Exists(rawPath) || !File.Exists(iniPath))
            {
                Debug.LogError($"VolumeSTCube XYZ+T time step is missing its RAW or INI file: {rawPath}");
                return false;
            }

            VolumeControllerObject controller = GetComponent<VolumeControllerObject>();
            VolumeRenderedObject volume = VolumeSTCubeTimeFrameLoader.LoadAndActivate(
                controller,
                rawPath,
                iniPath,
                renderMode,
                opacity);
            if (volume == null)
                return false;

            currentIndex = index;
            this.requestedIndex = index;
            CurrentVolumeObject = volume.gameObject;
            VolumeSTCubeTimeController timeController = GetComponent<VolumeSTCubeTimeController>();
            if (timeController != null)
                timeController.NotifyRendererReady();
            Debug.Log($"VolumeSTCube XYZ+T loaded time file {index + 1}/{Count}: {Path.GetFileName(rawPath)}");
            StartNeighbourPreload();
            return true;
        }

        public void AdoptLoadedVolume(int index, GameObject volumeObject)
        {
            currentIndex = Count > 0 ? Mathf.Clamp(index, 0, Count - 1) : -1;
            requestedIndex = currentIndex;
            CurrentVolumeObject = volumeObject;
            StartNeighbourPreload();
        }

        public bool SetNormalizedTime(float normalizedTime)
        {
            if (Count == 0)
                return false;

            int index = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(normalizedTime) * Count), 0, Count - 1);
            return LoadIndex(index);
        }

        public Vector2 GetNormalizedWindow()
        {
            if (Count <= 0 || currentIndex < 0)
                return new Vector2(0.0f, 1.0f);

            int visibleIndex = RequestedIndex;
            return new Vector2(visibleIndex / (float)Count, (visibleIndex + 1) / (float)Count);
        }

        public string GetTimeLabel(int index)
        {
            if (Count == 0)
                return "t";

            index = Mathf.Clamp(index, 0, Count - 1);
            return Path.GetFileNameWithoutExtension(rawFilePaths[index]);
        }

        public void SetOpacity(float value)
        {
            opacity = Mathf.Clamp01(value);
            VolumeSTCubeOriginalSceneAdapter.ApplyOpacityPreset(GetComponent<VolumeControllerObject>(), opacity);
        }

        public void SetRenderMode(VolumeSTCubeRenderMode value)
        {
            renderMode = value;
        }

        private GameObject ResolveCurrentVolume()
        {
            if (CurrentVolumeObject != null)
                return CurrentVolumeObject;

            VolumeRenderedObject volume = GetComponentInChildren<VolumeRenderedObject>(true);
            CurrentVolumeObject = volume != null ? volume.gameObject : null;
            return CurrentVolumeObject;
        }

        private async void StartBackgroundLoad(int index)
        {
            if (index < 0 || index >= Count || index == currentIndex || readyDatasets.ContainsKey(index))
                return;
            if (loadingIndex >= 0)
                return;

            loadingIndex = index;
            int loadVersion = configurationVersion;
            string rawPath = rawFilePaths[index];
            string iniPath = index < iniFilePaths.Count ? iniFilePaths[index] : rawPath + ".ini";
            VolumeDataset dataset = null;
            try
            {
                dataset = await VolumeSTCubeTimeFrameLoader.PreloadAsync(rawPath, iniPath);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            if (this == null || loadVersion != configurationVersion)
            {
                ReleaseDataset(dataset);
                return;
            }

            loadingIndex = -1;
            if (dataset == null)
            {
                if (requestedIndex == index)
                    requestedIndex = currentIndex;
                StartRequestedOrNeighbourLoad();
                return;
            }

            if (requestedIndex == index && currentIndex != index)
            {
                ActivatePreparedDataset(index, dataset);
                return;
            }

            if (index == GetNeighbourIndex())
            {
                ReleaseReadyDatasets();
                readyDatasets[index] = dataset;
            }
            else
            {
                ReleaseDataset(dataset);
            }

            StartRequestedOrNeighbourLoad();
        }

        private bool ActivatePreparedDataset(int index, VolumeDataset dataset)
        {
            ReleaseReadyDatasets();
            VolumeControllerObject controller = GetComponent<VolumeControllerObject>();
            VolumeRenderedObject volume = VolumeSTCubeTimeFrameLoader.Activate(
                controller,
                dataset,
                renderMode,
                opacity);
            if (volume == null)
            {
                ReleaseDataset(dataset);
                requestedIndex = currentIndex;
                return false;
            }

            currentIndex = index;
            requestedIndex = index;
            CurrentVolumeObject = volume.gameObject;
            VolumeSTCubeTimeController timeController = GetComponent<VolumeSTCubeTimeController>();
            if (timeController != null)
                timeController.NotifyRendererReady();
            Debug.Log($"VolumeSTCube XYZ+T activated cached time file {index + 1}/{Count}.");
            StartNeighbourPreload();
            return true;
        }

        private void StartRequestedOrNeighbourLoad()
        {
            if (requestedIndex >= 0 && requestedIndex != currentIndex)
                StartBackgroundLoad(requestedIndex);
            else
                StartNeighbourPreload();
        }

        private void StartNeighbourPreload()
        {
            // Standalone Quest has much less graphics headroom than desktop. A
            // neighbour preload creates another complete 3D texture while the current
            // one is still resident, which is enough to trigger the headset's low
            // memory guard for the supplied datasets.
            if (Application.platform == RuntimePlatform.Android)
                return;
            int neighbourIndex = GetNeighbourIndex();
            if (neighbourIndex >= 0)
                StartBackgroundLoad(neighbourIndex);
        }

        private int GetNeighbourIndex()
        {
            if (currentIndex < 0 || Count <= 1)
                return -1;
            int neighbourIndex = currentIndex + lastDirection;
            return neighbourIndex >= 0 && neighbourIndex < Count ? neighbourIndex : -1;
        }

        private void ReleaseReadyDatasets()
        {
            foreach (KeyValuePair<int, VolumeDataset> entry in readyDatasets)
                ReleaseDataset(entry.Value);
            readyDatasets.Clear();
        }

        private static void ReleaseDataset(VolumeDataset dataset)
        {
            if (dataset == null)
                return;
            dataset.ReleaseRuntimeTextures();
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(dataset);
            else
                UnityEngine.Object.DestroyImmediate(dataset);
        }

        private void OnDestroy()
        {
            configurationVersion++;
            ReleaseReadyDatasets();
        }
    }
}
