using System;
using UnityEngine;

namespace UnityVolumeRendering
{
    [DisallowMultipleComponent]
    public sealed class VolumeSTCubeTimeController : MonoBehaviour
    {
        private VolumeControllerObject volumeController;
        private Vector2 window = new Vector2(0.0f, 1.0f);

        public VolumeSTCubeTimeAxis TimeAxis { get; set; } = VolumeSTCubeTimeAxis.Z;
        public VolumeSTCubeTimeAxis WorldTimeAxis { get; set; } = VolumeSTCubeTimeAxis.Y;
        public VolumeSTCubeDataLayout DataLayout { get; set; } = VolumeSTCubeDataLayout.XYTime;

        public event Action<Vector2> WindowChanged;

        public Vector2 Window => window;

        public bool IsRendererReady
        {
            get
            {
                return volumeController != null
                    && volumeController.meshRenderers != null
                    && volumeController.meshRenderers.Length > 0
                    && volumeController.meshRenderers[0] != null
                    && volumeController.meshRenderers[volumeController.meshRenderers.Length - 1] != null;
            }
        }

        public static VolumeSTCubeTimeController GetOrAdd(VolumeControllerObject controller)
        {
            if (controller == null)
                return null;

            VolumeSTCubeTimeController timeController = controller.GetComponent<VolumeSTCubeTimeController>();
            if (timeController == null)
                timeController = controller.gameObject.AddComponent<VolumeSTCubeTimeController>();
            timeController.volumeController = controller;
            timeController.SynchronizeFromRenderer();
            return timeController;
        }

        private void Awake()
        {
            volumeController = GetComponent<VolumeControllerObject>();
            SynchronizeFromRenderer();
        }

        private void LateUpdate()
        {
            if (volumeController == null)
                volumeController = GetComponent<VolumeControllerObject>();
            if (volumeController == null)
                return;

            Vector2 rendererWindow;
            if (DataLayout == VolumeSTCubeDataLayout.XYZTimeSeries)
            {
                VolumeSTCubeRawTimeSeries series = volumeController.GetComponent<VolumeSTCubeRawTimeSeries>();
                rendererWindow = series != null ? series.GetNormalizedWindow() : window;
            }
            else
            {
                rendererWindow = Normalize(volumeController.GetClipedHeightWindow());
            }
            if (rendererWindow != window)
            {
                window = rendererWindow;
                WindowChanged?.Invoke(window);
            }
        }

        public void SetWindow(float minimum, float maximum)
        {
            SetWindow(new Vector2(minimum, maximum));
        }

        public void SetWindow(Vector2 requestedWindow)
        {
            Vector2 normalized = Normalize(requestedWindow);
            bool changed = normalized != window;
            window = normalized;

            if (volumeController != null)
            {
                if (DataLayout == VolumeSTCubeDataLayout.XYZTimeSeries)
                {
                    VolumeSTCubeRawTimeSeries series = volumeController.GetComponent<VolumeSTCubeRawTimeSeries>();
                    if (series != null)
                    {
                        series.SetNormalizedTime((window.x + window.y) * 0.5f);
                        window = series.GetNormalizedWindow();
                    }
                }
                else
                {
                    volumeController.SetClipedHeight(window);
                }
            }

            if (changed)
                WindowChanged?.Invoke(window);
        }

        public void SetCenter(float center)
        {
            float width = Mathf.Clamp(window.y - window.x, 0.0001f, 1.0f);
            center = Mathf.Clamp01(center);
            float minimum = Mathf.Clamp(center - width * 0.5f, 0.0f, 1.0f - width);
            SetWindow(minimum, minimum + width);
        }

        public void SetCenter(float center, float width)
        {
            width = Mathf.Clamp(width, 0.0001f, 1.0f);
            center = Mathf.Clamp01(center);
            float minimum = Mathf.Clamp(center - width * 0.5f, 0.0f, 1.0f - width);
            SetWindow(minimum, minimum + width);
        }

        public void NotifyRendererReady()
        {
            SynchronizeFromRenderer();
            WindowChanged?.Invoke(window);
        }

        private void SynchronizeFromRenderer()
        {
            if (volumeController != null)
            {
                VolumeSTCubeRawTimeSeries series = volumeController.GetComponent<VolumeSTCubeRawTimeSeries>();
                window = DataLayout == VolumeSTCubeDataLayout.XYZTimeSeries && series != null
                    ? series.GetNormalizedWindow()
                    : Normalize(volumeController.GetClipedHeightWindow());
            }
        }

        private static Vector2 Normalize(Vector2 value)
        {
            float minimum = Mathf.Clamp01(Mathf.Min(value.x, value.y));
            float maximum = Mathf.Clamp01(Mathf.Max(value.x, value.y));
            return new Vector2(minimum, maximum);
        }
    }
}
