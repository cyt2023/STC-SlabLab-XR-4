using System.Collections.Generic;
using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Handle returned by VolumeSTCubeAPI. Use it for per-view controls; use
    /// VolumeSTCubeAPI.DestroyView when the view is no longer needed.
    /// </summary>
    public class VolumeSTCubeView
    {
        public string viewId;
        public string datasetName;
        public GameObject rootObject;
        public List<GameObject> volumeObjects = new List<GameObject>();
        public VolumeSTCubeConfig config;
        public VolumeSTCubeData data;
        public bool isVisible = true;
        public bool ownsRootObject = true;
        public GameObject timelineObject;

        public void SetVisible(bool visible)
        {
            isVisible = visible;
            if (rootObject != null)
                rootObject.SetActive(visible);
            if (timelineObject != null)
                timelineObject.SetActive(visible);
        }

        public void ApplyTransform(Vector3 position, Vector3 rotation, Vector3 scale)
        {
            if (rootObject == null)
            {
                Debug.LogWarning("VolumeSTCubeView.ApplyTransform skipped: rootObject is missing.");
                return;
            }

            rootObject.transform.position = position;
            rootObject.transform.rotation = Quaternion.Euler(rotation);
            rootObject.transform.localScale = scale;
        }

        public void ApplyTimeFilter(float tMin, float tMax)
        {
            if (config != null)
            {
                config.timeMin = tMin;
                config.timeMax = tMax;
            }

            VolumeControllerObject controller = rootObject != null ? rootObject.GetComponent<VolumeControllerObject>() : null;
            if (controller == null && rootObject != null)
                controller = rootObject.GetComponentInParent<VolumeControllerObject>();

            VolumeSTCubeRawTimeSeries series = controller != null
                ? controller.GetComponent<VolumeSTCubeRawTimeSeries>()
                : null;
            if (series != null && series.Count > 0)
            {
                float center = Mathf.Clamp01((tMin + tMax) * 0.5f);
                if (series.SetNormalizedTime(center))
                {
                    Vector2 selectedWindow = series.GetNormalizedWindow();
                    if (config != null)
                    {
                        config.timeMin = selectedWindow.x;
                        config.timeMax = selectedWindow.y;
                    }
                    volumeObjects.Clear();
                    if (series.CurrentVolumeObject != null)
                        volumeObjects.Add(series.CurrentVolumeObject);
                }
                return;
            }

            if (controller != null && ControllerOwnsVolumes(controller))
            {
                VolumeSTCubeTimeController.GetOrAdd(controller).SetWindow(tMin, tMax);
                return;
            }

            if (ApplyTimeFilterToVolumes(tMin, tMax))
                return;

            Debug.LogWarning("VolumeSTCubeView.ApplyTimeFilter is not connected: this view has no managed volume controller.");
        }

        private bool ControllerOwnsVolumes(VolumeControllerObject controller)
        {
            if (controller == null || controller.volumeContainerObjects == null)
                return false;

            for (int i = 0; i < volumeObjects.Count; i++)
            {
                GameObject volumeObject = volumeObjects[i];
                VolumeRenderedObject renderedObject = volumeObject != null
                    ? volumeObject.GetComponent<VolumeRenderedObject>()
                    : null;
                if (renderedObject == null)
                    continue;

                for (int j = 0; j < controller.volumeContainerObjects.Length; j++)
                {
                    if (controller.volumeContainerObjects[j] == renderedObject)
                        return true;
                }
            }

            return false;
        }

        public VolumeControllerObject GetManagedController()
        {
            VolumeControllerObject controller = rootObject != null
                ? rootObject.GetComponent<VolumeControllerObject>()
                : null;
            if (controller == null && rootObject != null)
                controller = rootObject.GetComponentInParent<VolumeControllerObject>();

            return ControllerOwnsVolumes(controller) ? controller : null;
        }

        public VolumeSTCubeTimeController GetTimeController()
        {
            return VolumeSTCubeTimeController.GetOrAdd(GetManagedController());
        }

        public VolumeSTCubeDataLayout GetDataLayout()
        {
            VolumeControllerObject controller = rootObject != null
                ? rootObject.GetComponent<VolumeControllerObject>()
                : null;
            VolumeSTCubeRawTimeSeries series = controller != null
                ? controller.GetComponent<VolumeSTCubeRawTimeSeries>()
                : null;
            if (series != null && series.Count > 0)
                return VolumeSTCubeDataLayout.XYZTimeSeries;
            return config != null && config.dataLayout != VolumeSTCubeDataLayout.Auto
                ? config.dataLayout
                : VolumeSTCubeDataLayout.XYTime;
        }

        public int GetTimeSampleCount()
        {
            VolumeControllerObject controller = rootObject != null
                ? rootObject.GetComponent<VolumeControllerObject>()
                : null;
            VolumeSTCubeRawTimeSeries series = controller != null
                ? controller.GetComponent<VolumeSTCubeRawTimeSeries>()
                : null;
            return series != null ? series.Count : 0;
        }

        public string GetTimeSampleLabel(int index)
        {
            VolumeControllerObject controller = rootObject != null
                ? rootObject.GetComponent<VolumeControllerObject>()
                : null;
            VolumeSTCubeRawTimeSeries series = controller != null
                ? controller.GetComponent<VolumeSTCubeRawTimeSeries>()
                : null;
            return series != null ? series.GetTimeLabel(index) : string.Empty;
        }

        public bool IsTimeTransitionPending()
        {
            VolumeControllerObject controller = rootObject != null
                ? rootObject.GetComponent<VolumeControllerObject>()
                : null;
            VolumeSTCubeRawTimeSeries series = controller != null
                ? controller.GetComponent<VolumeSTCubeRawTimeSeries>()
                : null;
            return series != null && series.IsTransitionPending;
        }

        private bool ApplyTimeFilterToVolumes(float tMin, float tMax)
        {
            if (volumeObjects == null || volumeObjects.Count == 0)
                return false;

            float min = Mathf.Clamp01(Mathf.Min(tMin, tMax));
            float max = Mathf.Clamp01(Mathf.Max(tMin, tMax));
            int count = volumeObjects.Count;
            bool foundVolume = false;

            for (int i = 0; i < count; i++)
            {
                GameObject volumeObject = volumeObjects[i];
                if (volumeObject == null)
                    continue;

                VolumeRenderedObject renderedObject = volumeObject.GetComponent<VolumeRenderedObject>();
                MeshRenderer renderer = renderedObject != null ? renderedObject.meshRenderer : null;
                if (renderer == null || renderer.sharedMaterial == null)
                    continue;

                foundVolume = true;
                float localStart = Mathf.Clamp01(min * count - i);
                float localEnd = Mathf.Clamp01(max * count - i);
                bool overlaps = localEnd > 0.0f && localStart < 1.0f && localEnd > localStart;
                volumeObject.SetActive(overlaps);
                if (!overlaps)
                    continue;

                renderer.sharedMaterial.SetFloat("_StartPlane", localStart);
                renderer.sharedMaterial.SetFloat("_EndPlane", localEnd);
            }

            return foundVolume;
        }

        public void ApplyOpacity(float opacity)
        {
            if (config != null)
                config.opacity = opacity;

            VolumeControllerObject controller = rootObject != null ? rootObject.GetComponent<VolumeControllerObject>() : null;
            if (controller == null && rootObject != null)
                controller = rootObject.GetComponentInParent<VolumeControllerObject>();

            if (controller != null)
            {
                VolumeSTCubeRawTimeSeries series = controller.GetComponent<VolumeSTCubeRawTimeSeries>();
                if (series != null)
                    series.SetOpacity(opacity);
                else
                    VolumeSTCubeOriginalSceneAdapter.ApplyOpacityPreset(controller, Mathf.Clamp01(opacity));
                return;
            }

            Debug.LogWarning("VolumeSTCubeView.ApplyOpacity is not connected for ungrouped VolumeRenderedObject instances. Use a VolumeControllerObject or connect a transfer-function preset.");
        }

        public void Destroy()
        {
            if (!ownsRootObject)
            {
                VolumeControllerObject controller = rootObject != null ? rootObject.GetComponent<VolumeControllerObject>() : null;
                VolumeSTCubeRawTimeSeries series = controller != null
                    ? controller.GetComponent<VolumeSTCubeRawTimeSeries>()
                    : null;
                if (series != null && series.CurrentVolumeObject != null &&
                    !volumeObjects.Contains(series.CurrentVolumeObject))
                    volumeObjects.Add(series.CurrentVolumeObject);
                for (int i = 0; i < volumeObjects.Count; i++)
                {
                    if (volumeObjects[i] == null)
                        continue;

                    VolumeRenderedObject rendered =
                        volumeObjects[i].GetComponent<VolumeRenderedObject>();
                    VolumeDataset dataset = rendered != null ? rendered.dataset : null;
                    if (dataset != null)
                    {
                        dataset.ReleaseRuntimeTextures();
                        if (Application.isPlaying)
                            Object.Destroy(dataset);
                        else
                            Object.DestroyImmediate(dataset);
                    }

                    if (Application.isPlaying)
                    {
                        volumeObjects[i].transform.SetParent(null, true);
                        Object.Destroy(volumeObjects[i]);
                    }
                    else
                        Object.DestroyImmediate(volumeObjects[i]);
                }

                if (series != null)
                {
                    if (Application.isPlaying)
                        Object.Destroy(series);
                    else
                        Object.DestroyImmediate(series);
                }

                if (controller != null && config != null)
                    VolumeSTCubeOriginalSceneAdapter.RefreshController(
                        controller,
                        config.renderMode,
                        config.timeAxis,
                        VolumeSTCubeDataLayout.XYTime);

                DestroyTimeline();

                volumeObjects.Clear();
                rootObject = null;
                return;
            }

            if (rootObject == null)
            {
                DestroyTimeline();
                return;
            }

            if (Application.isPlaying)
                Object.Destroy(rootObject);
            else
                Object.DestroyImmediate(rootObject);
            rootObject = null;
            volumeObjects.Clear();
            DestroyTimeline();
        }

        public void CreateTimeline()
        {
            if (!Application.isPlaying || config == null || !config.showTimeline || timelineObject != null)
                return;

            timelineObject = VolumeSTCubeTimeline.Create(this);
        }

        private void DestroyTimeline()
        {
            if (timelineObject == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(timelineObject);
            else
                Object.DestroyImmediate(timelineObject);
            timelineObject = null;
        }
    }
}
