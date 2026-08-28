using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Replaces the visible XYZ volume while preserving scene render controls.
    /// It does not own timeline state or cache policy.
    /// </summary>
    internal static class VolumeSTCubeTimeFrameLoader
    {
        public static VolumeRenderedObject LoadAndActivate(
            VolumeControllerObject controller,
            string rawPath,
            string iniPath,
            VolumeSTCubeRenderMode renderMode,
            float opacity)
        {
            if (controller == null)
                return null;

            VolumeRenderedObject replacement = VolumeSTCubeRawVolumeFactory.Import(
                rawPath,
                iniPath,
                Path.GetFileNameWithoutExtension(rawPath));
            return Activate(controller, replacement, renderMode, opacity);
        }

        public static VolumeRenderedObject Activate(
            VolumeControllerObject controller,
            VolumeDataset preparedDataset,
            VolumeSTCubeRenderMode renderMode,
            float opacity)
        {
            if (controller == null || preparedDataset == null)
                return null;

            VolumeRenderedObject replacement = VolumeSTCubeRawVolumeFactory.CreateObject(
                preparedDataset,
                preparedDataset.filePath);
            return Activate(controller, replacement, renderMode, opacity);
        }

        public static Task<VolumeDataset> PreloadAsync(string rawPath, string iniPath)
        {
            return VolumeSTCubeRawVolumeFactory.PreloadDatasetAsync(
                rawPath,
                iniPath,
                Path.GetFileNameWithoutExtension(rawPath));
        }

        private static VolumeRenderedObject Activate(
            VolumeControllerObject controller,
            VolumeRenderedObject replacement,
            VolumeSTCubeRenderMode renderMode,
            float opacity)
        {
            if (controller == null || replacement == null)
                return null;

            if (Application.platform == RuntimePlatform.Android &&
                renderMode == VolumeSTCubeRenderMode.Volume)
                replacement.SetLightingEnabled(false);
            RenderState state = RenderState.Capture(controller);
            List<VolumeDataset> replacedDatasets = CollectReplacedDatasets(controller, replacement.dataset);

            VolumeSTCubeOriginalSceneAdapter.ClearExistingVolumes(controller, replacement);
            VolumeSTCubeOriginalSceneAdapter.AttachVolume(replacement, controller);
            VolumeSTCubeOriginalSceneAdapter.RefreshController(
                controller,
                renderMode,
                VolumeSTCubeTimeAxis.Z,
                VolumeSTCubeDataLayout.XYZTimeSeries);
            VolumeSTCubeOriginalSceneAdapter.ApplyOpacityPreset(controller, opacity);
            state.Restore(controller);
            if (Application.platform == RuntimePlatform.Android &&
                renderMode == VolumeSTCubeRenderMode.Volume)
                replacement.SetLightingEnabled(false);
            ReleaseDatasets(replacedDatasets);
            return replacement;
        }

        private static List<VolumeDataset> CollectReplacedDatasets(
            VolumeControllerObject controller,
            VolumeDataset replacementDataset)
        {
            List<VolumeDataset> datasets = new List<VolumeDataset>();
            if (controller.volumeContainerObjects == null)
                return datasets;

            for (int i = 0; i < controller.volumeContainerObjects.Length; i++)
            {
                VolumeRenderedObject volume = controller.volumeContainerObjects[i];
                if (volume != null && volume.dataset != null && volume.dataset != replacementDataset)
                    datasets.Add(volume.dataset);
            }
            return datasets;
        }

        private static void ReleaseDatasets(List<VolumeDataset> datasets)
        {
            for (int i = 0; i < datasets.Count; i++)
            {
                datasets[i].ReleaseRuntimeTextures();
                if (Application.isPlaying)
                    Object.Destroy(datasets[i]);
                else
                    Object.DestroyImmediate(datasets[i]);
            }
        }

        private struct RenderState
        {
            private bool hasState;
            private Vector2 visibilityWindow;
            private float lightIntensity;
            private float isosurfaceValue;
            private Vector2 highlightPosition;
            private float highlightRadius;
            private bool lightingEnabled;

            public static RenderState Capture(VolumeControllerObject controller)
            {
                bool available = controller.volumeContainerObjects != null &&
                    controller.volumeContainerObjects.Length > 0;
                return new RenderState
                {
                    hasState = available,
                    visibilityWindow = controller.GetVisibilityWindow(),
                    lightIntensity = controller.GetLightIntensity(),
                    isosurfaceValue = controller.GetIsosurfaceValue(),
                    highlightPosition = controller.GetHighlightPosition(),
                    highlightRadius = controller.GetHighlightRadius(),
                    lightingEnabled = controller.GetLightingEnabled()
                };
            }

            public void Restore(VolumeControllerObject controller)
            {
                if (!hasState)
                    return;
                controller.SetVisibilityWindow(visibilityWindow);
                controller.SetLightIntensity(lightIntensity);
                controller.SetIsosurfaceValue(isosurfaceValue);
                controller.SetHighlightPosition(highlightPosition);
                controller.SetHighlightRadius(highlightRadius);
                controller.SetLightingEnabled(lightingEnabled);
            }
        }
    }
}
