using UnityEngine;
using System.Collections.Generic;
using AxisController;
using MapController;

namespace UnityVolumeRendering
{
    public static class VolumeSTCubeOriginalSceneAdapter
    {
        public const string ControllerName = "VolumeController";
        public const int OriginalLayerCount = 8;
        private const float OriginalLayerBottom = -5.25253f;
        private const float OriginalLayerSpacing = 3.96f;
        private static readonly Dictionary<int, TransferFunction> GeographicLayerTransferFunctions =
            new Dictionary<int, TransferFunction>();
        private static readonly Dictionary<int, string> GeographicLayerVariables =
            new Dictionary<int, string>();

        public static VolumeControllerObject EnsureController(IList<VolumeRenderedObject> initialVolumes = null)
        {
            GameObject controllerObject = GameObject.Find(ControllerName);
            if (controllerObject == null)
                controllerObject = new GameObject(ControllerName);

            VolumeControllerObject controller = controllerObject.GetComponent<VolumeControllerObject>();
            if (controller == null)
            {
                AttachVolumesBeforeControllerAwake(controllerObject.transform, initialVolumes);
                controller = controllerObject.AddComponent<VolumeControllerObject>();
            }
            else if (initialVolumes != null)
            {
                for (int i = 0; i < initialVolumes.Count; i++)
                    AttachVolume(initialVolumes[i], controller);
            }

            VolumeSTCubeTimeController.GetOrAdd(controller);

            return controller;
        }

        private static void AttachVolumesBeforeControllerAwake(Transform controllerTransform, IList<VolumeRenderedObject> volumes)
        {
            if (controllerTransform == null || volumes == null)
                return;

            for (int i = 0; i < volumes.Count; i++)
            {
                VolumeRenderedObject volume = volumes[i];
                if (volume != null)
                    volume.transform.SetParent(controllerTransform, false);
            }
        }

        public static void AttachVolume(VolumeRenderedObject volumeObject, VolumeControllerObject controller)
        {
            if (volumeObject == null || controller == null)
                return;

            volumeObject.transform.SetParent(controller.transform, false);
        }

        public static void ClearExistingVolumes(
            VolumeControllerObject controller,
            VolumeRenderedObject preserve = null)
        {
            VolumeRenderedObject[] existingVolumes = Object.FindObjectsOfType<VolumeRenderedObject>();
            for (int i = 0; i < existingVolumes.Length; i++)
            {
                if (existingVolumes[i] == null || existingVolumes[i] == preserve)
                    continue;

                if (Application.isPlaying)
                    Object.Destroy(existingVolumes[i].gameObject);
                else
                    Object.DestroyImmediate(existingVolumes[i].gameObject);
            }

            if (controller == null)
                return;

            for (int i = controller.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = controller.transform.GetChild(i);
                if (child == null)
                    continue;

                if (preserve != null && child.GetComponent<VolumeRenderedObject>() == preserve)
                    continue;

                if (child.GetComponent<VolumeRenderedObject>() != null ||
                    child.name.StartsWith("VolumeSTCubeView_") ||
                    child.name.StartsWith("VolumeSTCubePointPreview_"))
                {
                    if (Application.isPlaying)
                        Object.Destroy(child.gameObject);
                    else
                        Object.DestroyImmediate(child.gameObject);
                }
            }

            controller.meshRenderers = new MeshRenderer[0];
            controller.volumeContainerObjects = new VolumeRenderedObject[0];
        }

        public static void RefreshController(
            VolumeControllerObject controller,
            VolumeSTCubeRenderMode renderMode,
            VolumeSTCubeTimeAxis timeAxis = VolumeSTCubeTimeAxis.Z,
            VolumeSTCubeDataLayout dataLayout = VolumeSTCubeDataLayout.Auto)
        {
            if (controller == null)
                return;

            // Keep the data contract and the scene layout separate: t is stored
            // in texture Z, then rotated onto world Y so every time layer stays
            // geographically aligned above the original horizontal map.
            VolumeSTCubeTimeAxis worldTimeAxis = VolumeSTCubeTimeAxis.Y;

            List<MeshRenderer> meshRenderers = new List<MeshRenderer>();
            List<VolumeRenderedObject> volumeObjects = new List<VolumeRenderedObject>();

            for (int i = 0; i < controller.transform.childCount; i++)
            {
                Transform child = controller.transform.GetChild(i);
                VolumeRenderedObject volumeObject = child.GetComponent<VolumeRenderedObject>();
                MeshRenderer meshRenderer = child.childCount > 0 ? child.GetChild(0).GetComponent<MeshRenderer>() : null;

                if (volumeObject == null || meshRenderer == null)
                {
                    Debug.LogWarning($"VolumeSTCube original scene preset skipped non-volume child '{child.name}' under {ControllerName}.");
                    continue;
                }

                volumeObjects.Add(volumeObject);
                meshRenderers.Add(meshRenderer);

                Quaternion dataRotation = worldTimeAxis == VolumeSTCubeTimeAxis.Z
                    ? Quaternion.identity
                    : Quaternion.Euler(90.0f, 0.0f, 0.0f);
                meshRenderer.transform.localRotation = dataRotation;
                if (volumeObject.dataset != null)
                    volumeObject.dataset.rotation = dataRotation;

                Vector2 horizontalScale = controller.originalSceneHorizontalScale;
                if (Mathf.Approximately(horizontalScale.x, 0.0f))
                    horizontalScale.x = 1.0f;
                if (Mathf.Approximately(horizontalScale.y, 0.0f))
                    horizontalScale.y = 1.0f;

                volumeObject.transform.localScale = worldTimeAxis == VolumeSTCubeTimeAxis.Z
                    ? new Vector3(10.0f * horizontalScale.x, 10.0f * horizontalScale.y, 4.0f)
                    : new Vector3(10.0f * horizontalScale.x, 4.0f, 10.0f * horizontalScale.y);
            }

            controller.meshRenderers = meshRenderers.ToArray();
            controller.volumeContainerObjects = volumeObjects.ToArray();
            dataLayout = ResolveControllerDataLayout(controller, dataLayout);
            VolumeSTCubeTimeController timeController = VolumeSTCubeTimeController.GetOrAdd(controller);
            timeController.TimeAxis = timeAxis;
            timeController.WorldTimeAxis = worldTimeAxis;
            timeController.DataLayout = dataLayout;

            if (volumeObjects.Count == 0)
                return;

            float layerSpacing = GetLayerSpacing(volumeObjects.Count) * controller.originalSceneLayerSpacingScale;
            for (int i = 0; i < volumeObjects.Count; i++)
            {
                float timePosition = OriginalLayerBottom + controller.originalSceneLayerYOffset;
                if (dataLayout == VolumeSTCubeDataLayout.XYTime)
                    timePosition += layerSpacing * i;
                volumeObjects[i].transform.localPosition = worldTimeAxis == VolumeSTCubeTimeAxis.Z
                    ? new Vector3(controller.originalSceneHorizontalOffset.x, controller.originalSceneHorizontalOffset.y, timePosition)
                    : new Vector3(controller.originalSceneHorizontalOffset.x, timePosition, controller.originalSceneHorizontalOffset.y);
            }

            controller.transferFunction = volumeObjects[0].transferFunction;
            controller.transferFunction2D = volumeObjects[0].transferFunction2D;
            ApplyOriginalSceneRenderPreset(controller, renderMode);
            RefreshSceneGuides(controller, volumeObjects.Count, dataLayout);
            AlignGeographicMapBelowStack(controller);
            timeController.NotifyRendererReady();
        }

        public static bool HasOriginalSceneGuides()
        {
            return GameObject.Find("Map") != null && Object.FindObjectOfType<AxisContainer>() != null;
        }

        public static bool AlignGeographicMapBelowStack(VolumeControllerObject controller)
        {
            if (controller == null || controller.meshRenderers == null || controller.meshRenderers.Length == 0)
                return false;

            float bottom = GetBottomTime(controller, VolumeSTCubeTimeAxis.Y);
            float top = GetTopTime(controller, VolumeSTCubeTimeAxis.Y);
            bool changed = false;

            Map map = Object.FindObjectOfType<Map>();
            if (map != null)
            {
                Vector3 position = map.transform.position;
                Vector3 targetPosition = new Vector3(position.x, bottom - 0.05f, position.z);
                Quaternion targetRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);
                if ((map.transform.position - targetPosition).sqrMagnitude > 0.000001f ||
                    Quaternion.Angle(map.transform.rotation, targetRotation) > 0.01f)
                {
                    map.transform.position = targetPosition;
                    map.transform.rotation = targetRotation;
                    changed = true;
                }
            }

            UpperPlane upperPlane = Object.FindObjectOfType<UpperPlane>();
            if (upperPlane != null)
            {
                Vector3 position = upperPlane.transform.position;
                Vector3 targetPosition = new Vector3(position.x, top + 0.05f, position.z);
                if ((upperPlane.transform.position - targetPosition).sqrMagnitude > 0.000001f ||
                    Quaternion.Angle(upperPlane.transform.rotation, Quaternion.identity) > 0.01f)
                {
                    upperPlane.transform.position = targetPosition;
                    upperPlane.transform.rotation = Quaternion.identity;
                    changed = true;
                }
            }

            return changed;
        }

        public static bool NeedsGeographicStackLayout(VolumeControllerObject controller)
        {
            if (controller == null || controller.meshRenderers == null || controller.meshRenderers.Length == 0)
                return false;

            Quaternion expectedRotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            for (int i = 0; i < controller.meshRenderers.Length; i++)
            {
                MeshRenderer renderer = controller.meshRenderers[i];
                if (renderer != null && Quaternion.Angle(renderer.transform.localRotation, expectedRotation) > 0.1f)
                    return true;
            }

            VolumeSTCubeDataLayout detectedLayout = ResolveControllerDataLayout(controller, VolumeSTCubeDataLayout.Auto);
            VolumeSTCubeTimeController timeController = controller.GetComponent<VolumeSTCubeTimeController>();
            if (timeController == null || timeController.DataLayout != detectedLayout)
                return true;

            if (detectedLayout == VolumeSTCubeDataLayout.XYZTimeSeries)
            {
                Vector3 expected = new Vector3(
                    controller.originalSceneHorizontalOffset.x,
                    OriginalLayerBottom + controller.originalSceneLayerYOffset,
                    controller.originalSceneHorizontalOffset.y);
                for (int i = 0; i < controller.volumeContainerObjects.Length; i++)
                {
                    VolumeRenderedObject volume = controller.volumeContainerObjects[i];
                    if (volume != null && (volume.transform.localPosition - expected).sqrMagnitude > 0.000001f)
                        return true;
                }
            }

            return GetWorldTimeAxis(controller) != VolumeSTCubeTimeAxis.Y;
        }

        public static bool SetTopDownCamera()
        {
            Camera camera = Camera.main;
            bool changed = false;
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                changed = true;
            }

            Bounds bounds = GetSceneVolumeBounds();
            Vector3 center = bounds.center;
            VolumeControllerObject controller = Object.FindObjectOfType<VolumeControllerObject>();
            bool timeOnZ = controller != null && GetWorldTimeAxis(controller) == VolumeSTCubeTimeAxis.Z;
            float size = timeOnZ
                ? Mathf.Max(bounds.size.x, bounds.size.y, 10.0f)
                : Mathf.Max(bounds.size.x, bounds.size.z, 10.0f);
            float depth = timeOnZ
                ? Mathf.Max(bounds.size.z + size * 1.25f, 18.0f)
                : Mathf.Max(bounds.size.y + size * 1.25f, 18.0f);

            Vector3 targetPosition = timeOnZ
                ? new Vector3(center.x, center.y, bounds.max.z + depth)
                : new Vector3(center.x, bounds.max.y + depth, center.z);
            Quaternion targetRotation = timeOnZ
                ? Quaternion.Euler(0.0f, 180.0f, 0.0f)
                : Quaternion.Euler(90.0f, 0.0f, 0.0f);
            float targetOrthographicSize = size * 0.62f;

            if ((camera.transform.position - targetPosition).sqrMagnitude > 0.000001f ||
                Quaternion.Angle(camera.transform.rotation, targetRotation) > 0.01f ||
                !camera.orthographic ||
                !Mathf.Approximately(camera.orthographicSize, targetOrthographicSize))
            {
                camera.transform.position = targetPosition;
                camera.transform.rotation = targetRotation;
                camera.orthographic = true;
                camera.orthographicSize = targetOrthographicSize;
                changed = true;
            }

            camera.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = Mathf.Max(depth * 3.0f, 1000.0f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.white;

            if (!Application.isPlaying && camera.GetComponent<VolumeSTCubeCameraGuard>() == null)
            {
                camera.gameObject.AddComponent<VolumeSTCubeCameraGuard>();
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// Frames the geographic stack from an elevated three-quarter angle.
        /// The camera remains orthographic so the legacy STC pan/zoom controls
        /// keep their original behaviour and the map is not perspective-distorted.
        /// </summary>
        public static bool SetPresentationCamera()
        {
            Camera camera = Camera.main;
            bool changed = false;
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                changed = true;
            }

            Bounds bounds = GetSceneVolumeBounds();
            Vector3 target = bounds.center;
            float horizontalOffset = Mathf.Clamp(bounds.extents.magnitude * 0.55f, 3.5f, 4.5f);
            float verticalOffset = Mathf.Max(bounds.size.y * 0.7f, 6.0f);
            Vector3 targetPosition = new Vector3(
                Mathf.Clamp(target.x + horizontalOffset, -4.75f, 4.75f),
                Mathf.Min(bounds.max.y + verticalOffset, 28.0f),
                Mathf.Clamp(target.z - horizontalOffset, -4.75f, 4.75f));
            Quaternion targetRotation = Quaternion.LookRotation(target - targetPosition, Vector3.up);
            float targetOrthographicSize = Mathf.Max(bounds.extents.magnitude * 1.08f, 5.0f);

            if ((camera.transform.position - targetPosition).sqrMagnitude > 0.000001f ||
                Quaternion.Angle(camera.transform.rotation, targetRotation) > 0.01f ||
                !camera.orthographic ||
                !Mathf.Approximately(camera.orthographicSize, targetOrthographicSize))
            {
                camera.transform.SetPositionAndRotation(targetPosition, targetRotation);
                camera.orthographic = true;
                camera.orthographicSize = targetOrthographicSize;
                changed = true;
            }

            camera.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = Mathf.Max((targetPosition - target).magnitude * 6.0f, 1000.0f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.92f, 0.94f, 0.97f, 1.0f);

            if (!Application.isPlaying && camera.GetComponent<VolumeSTCubeCameraGuard>() == null)
            {
                camera.gameObject.AddComponent<VolumeSTCubeCameraGuard>();
                changed = true;
            }

            return changed;
        }

        public static void SetTimelineDemoCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
                return;

            Bounds bounds = GetSceneVolumeBounds();
            VolumeControllerObject controller = Object.FindObjectOfType<VolumeControllerObject>();
            bool timeOnZ = controller != null && GetWorldTimeAxis(controller) == VolumeSTCubeTimeAxis.Z;
            float horizontalSize = timeOnZ
                ? Mathf.Max(bounds.size.x, bounds.size.y, 10.0f)
                : Mathf.Max(bounds.size.x, bounds.size.z, 10.0f);
            float timeDepth = timeOnZ ? Mathf.Max(bounds.size.z, 4.0f) : Mathf.Max(bounds.size.y, 4.0f);
            Vector3 center = bounds.center;
            Vector3 offset = timeOnZ
                ? new Vector3(0.0f, 0.0f, timeDepth + horizontalSize * 2.0f)
                : new Vector3(0.0f, timeDepth + horizontalSize * 2.0f, 0.0f);

            camera.transform.position = center + offset;
            camera.transform.LookAt(center);
            camera.orthographic = true;
            camera.orthographicSize = horizontalSize * 0.72f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = Mathf.Max(offset.magnitude * 4.0f, 1000.0f);
            camera.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.92f, 0.94f, 0.97f, 1.0f);
        }

        private static Bounds GetSceneVolumeBounds()
        {
            VolumeControllerObject controller = Object.FindObjectOfType<VolumeControllerObject>();
            Renderer[] renderers = controller != null ? controller.GetComponentsInChildren<Renderer>() : Object.FindObjectsOfType<Renderer>();

            bool hasBounds = false;
            Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 10.0f);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null || !renderers[i].enabled)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderers[i].bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            // The volume covers the China GeoJSON bounding box, while the original
            // map intentionally includes surrounding Asia. Include that base plane
            // when framing the camera so its geographic context is not cropped.
            Map map = Object.FindObjectOfType<Map>();
            Renderer mapRenderer = map != null ? map.GetComponent<Renderer>() : null;
            if (mapRenderer != null && mapRenderer.enabled)
            {
                if (!hasBounds)
                {
                    bounds = mapRenderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(mapRenderer.bounds);
                }
            }

            return bounds;
        }

        private static float GetLayerSpacing(int importedLayerCount)
        {
            if (importedLayerCount <= 1)
                return 0.0f;

            float originalStackSpan = OriginalLayerSpacing * (OriginalLayerCount - 1);
            return originalStackSpan / (importedLayerCount - 1);
        }

        private static void ApplyOriginalSceneRenderPreset(VolumeControllerObject controller, VolumeSTCubeRenderMode renderMode)
        {
            switch (renderMode)
            {
                case VolumeSTCubeRenderMode.Volume:
                case VolumeSTCubeRenderMode.Hybrid:
                case VolumeSTCubeRenderMode.PointPreview:
                    controller.SetRenderMode(RenderMode.DirectVolumeRendering);
                    break;
                case VolumeSTCubeRenderMode.Surface:
                    controller.SetRenderMode(RenderMode.IsosurfaceRendering);
                    break;
            }

            controller.SetHighlightPosition(new Vector2(0.5f, 0.5f));
            controller.SetHighlightRadius(1.0f);
            bool rawGeographicStack = IsRawGeographicStack(controller);
            ApplyOpacityPreset(controller, rawGeographicStack ? 0.9f : 0.5f);
            controller.SetVisibilityWindow(
                rawGeographicStack && renderMode == VolumeSTCubeRenderMode.Volume
                    ? 0.01f
                    : renderMode == VolumeSTCubeRenderMode.Volume ? 0.08f : 0.35f,
                1.0f);
            controller.SetLightIntensity(renderMode == VolumeSTCubeRenderMode.Volume ? 0.15f : 0.5f);
            controller.SetIsosurfaceValue(0.5f);
            if (Application.platform == RuntimePlatform.Android &&
                renderMode == VolumeSTCubeRenderMode.Volume)
            {
                // A freshly attached VolumeRenderedObject may carry the prefab's
                // serialized lighting flag even when the controller is already
                // unlit. Set every renderer explicitly; otherwise the controller's
                // unchanged false value does not propagate and each frame allocates
                // an RGBA gradient Texture3D on Quest.
                controller.SetLightingEnabled(false);
                if (controller.volumeContainerObjects != null)
                {
                    for (int i = 0; i < controller.volumeContainerObjects.Length; i++)
                    {
                        VolumeRenderedObject volume =
                            controller.volumeContainerObjects[i];
                        if (volume != null)
                            volume.SetLightingEnabled(false);
                    }
                }
            }
        }

        /// <summary>
        /// Applies the opacity convention shared by scene controls and the public API.
        /// The original controller's SetOpacity method also replaces the colour map
        /// with a low-opacity palette. Raw geographic time stacks keep the original
        /// STC colours but use a clearer alpha curve so each t-as-z slice remains
        /// readable above the map.
        /// </summary>
        public static void ApplyOpacityPreset(VolumeControllerObject controller, float opacity)
        {
            if (controller == null)
                return;

            opacity = Mathf.Clamp01(opacity);
            if (!IsRawGeographicStack(controller))
            {
                controller.SetOpacity(opacity);
                return;
            }

            int controllerId = controller.GetInstanceID();
            if (!GeographicLayerTransferFunctions.TryGetValue(controllerId, out TransferFunction transferFunction) ||
                transferFunction == null)
            {
                transferFunction = ScriptableObject.CreateInstance<TransferFunction>();
                transferFunction.name = "VolumeSTCube Geographic STC Layers";
                GeographicLayerTransferFunctions[controllerId] = transferFunction;
            }

            bool isSalt =
                GeographicLayerVariables.TryGetValue(controllerId, out string variableName) &&
                variableName.IndexOf(
                    "salt", System.StringComparison.OrdinalIgnoreCase) >= 0;
            ConfigureGeographicLayerTransferFunction(transferFunction, opacity);
            controller.SetIsosurfaceValue(isSalt ? 1.01f : 0.5f);
            controller.SetTransferFunctionMode(TFRenderMode.TF1D);
            controller.SetTransferFunction(transferFunction);
            ApplyRaySamplingPreset(controller, isSalt);

            if (controller.volumeContainerObjects == null)
                return;

            for (int i = 0; i < controller.volumeContainerObjects.Length; i++)
            {
                VolumeRenderedObject volume = controller.volumeContainerObjects[i];
                if (volume != null)
                    volume.transferFunction = transferFunction;
            }
        }

        /// <summary>
        /// Re-applies the shared geographic transfer function after an asynchronous
        /// variable/time-frame change. Variable-specific display normalization is
        /// performed when the RAW texture is created.
        /// </summary>
        public static void ApplyVariableOpacityPreset(
            VolumeControllerObject controller,
            string variableName,
            float opacity)
        {
            if (controller == null)
                return;
            GeographicLayerVariables[controller.GetInstanceID()] =
                variableName ?? string.Empty;
            ApplyOpacityPreset(controller, opacity);
            controller.SetLightIntensity(0.15f);
        }

        private static void ApplyRaySamplingPreset(
            VolumeControllerObject controller,
            bool isSalt)
        {
            if (controller.meshRenderers == null)
                return;
            for (int index = 0; index < controller.meshRenderers.Length; index++)
            {
                MeshRenderer renderer = controller.meshRenderers[index];
                if (renderer == null || renderer.sharedMaterial == null)
                    continue;
                renderer.sharedMaterial.SetFloat(
                    "_JitterFactor", isSalt ? 0.3f : 5.0f);
            }
        }

        private static void ConfigureGeographicLayerTransferFunction(TransferFunction transferFunction, float opacity)
        {
            transferFunction.colourControlPoints.Clear();
            transferFunction.alphaControlPoints.Clear();

            // Keep the exact colour progression used by the original STC opacity
            // control. Only the alpha curve below is adapted for geographic layers.
            transferFunction.colourControlPoints.Add(new TFColourControlPoint(0.0f, new Color(0.368f, 0.309f, 0.635f, 1.0f)));
            transferFunction.colourControlPoints.Add(new TFColourControlPoint(0.125f, new Color(0.248f, 0.591f, 0.717f, 1.0f)));
            transferFunction.colourControlPoints.Add(new TFColourControlPoint(0.25f, new Color(0.538f, 0.815f, 0.645f, 1.0f)));
            transferFunction.colourControlPoints.Add(new TFColourControlPoint(0.375f, new Color(0.848f, 0.939f, 0.607f, 1.0f)));
            transferFunction.colourControlPoints.Add(new TFColourControlPoint(0.5f, new Color(1.0f, 0.998f, 0.745f, 1.0f)));
            transferFunction.colourControlPoints.Add(new TFColourControlPoint(0.625f, new Color(0.995f, 0.825f, 0.5f, 1.0f)));
            transferFunction.colourControlPoints.Add(new TFColourControlPoint(0.75f, new Color(0.973f, 0.547f, 0.318f, 1.0f)));
            transferFunction.colourControlPoints.Add(new TFColourControlPoint(0.875f, new Color(0.862f, 0.283f, 0.3f, 1.0f)));
            transferFunction.colourControlPoints.Add(new TFColourControlPoint(1.0f, new Color(0.62f, 0.004f, 0.259f, 1.0f)));

            // Python writes the area outside China as byte value 1 (~0.004).
            // Keep that transparent, then make valid samples visibly layer-like.
            transferFunction.alphaControlPoints.Add(new TFAlphaControlPoint(0.0f, 0.0f));
            transferFunction.alphaControlPoints.Add(new TFAlphaControlPoint(0.012f, 0.0f));
            transferFunction.alphaControlPoints.Add(new TFAlphaControlPoint(0.020f, 0.38f * opacity));
            transferFunction.alphaControlPoints.Add(new TFAlphaControlPoint(0.20f, 0.56f * opacity));
            transferFunction.alphaControlPoints.Add(new TFAlphaControlPoint(0.60f, 0.72f * opacity));
            transferFunction.alphaControlPoints.Add(new TFAlphaControlPoint(1.0f, 0.86f * opacity));
            transferFunction.GenerateTexture();
        }

        public static bool IsRawGeographicStack(VolumeControllerObject controller)
        {
            if (controller == null || controller.volumeContainerObjects == null)
                return false;

            for (int i = 0; i < controller.volumeContainerObjects.Length; i++)
            {
                VolumeRenderedObject volume = controller.volumeContainerObjects[i];
                if (volume != null && volume.dataset != null &&
                    !string.IsNullOrEmpty(volume.dataset.filePath) &&
                    volume.dataset.filePath.EndsWith(".raw", System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void RefreshSceneGuides(
            VolumeControllerObject controller,
            int volumeCount,
            VolumeSTCubeDataLayout dataLayout)
        {
            VolumeSTCubeTimeAxis timeAxis = GetWorldTimeAxis(controller);
            AxisContainer axisContainer = Object.FindObjectOfType<AxisContainer>();
            if (axisContainer != null)
            {
                // The legacy axis contains date labels, so it only represents the
                // vertical time stack used by XY+T. XYZ+T keeps Z as spatial depth
                // and exposes time exclusively through the Timeline.
                if (dataLayout == VolumeSTCubeDataLayout.XYZTimeSeries)
                {
                    if (axisContainer.isActive)
                    {
                        axisContainer.isActive = false;
                        if (axisContainer.axisBtnText != null)
                            axisContainer.axisBtnText.text = "Show Axis";
                        for (int i = 0; i < axisContainer.axisList.Count; i++)
                        {
                            if (axisContainer.axisList[i] != null)
                                axisContainer.axisList[i].gameObject.SetActive(false);
                        }
                    }
                    return;
                }

                float timeLength = GetTimeLength(controller, timeAxis);
                float timeStart = GetBottomTime(controller, timeAxis);
                axisContainer.volumeHeight = timeLength;
                axisContainer.setAxisLength(Mathf.Clamp(volumeCount * 3, 8, 32));
                float step = timeLength / Mathf.Max(1, Mathf.Clamp(volumeCount * 3, 8, 32));
                if (timeAxis == VolumeSTCubeTimeAxis.Z)
                {
                    axisContainer.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
                    axisContainer.setPosition(new Vector3(0.0f, 0.0f, timeStart + step / 2.0f));
                }
                else
                {
                    axisContainer.transform.rotation = Quaternion.identity;
                    axisContainer.setPosition(new Vector3(0.0f, timeStart + step / 2.0f, 0.0f));
                }
            }
        }

        private static VolumeSTCubeDataLayout ResolveControllerDataLayout(
            VolumeControllerObject controller,
            VolumeSTCubeDataLayout requestedLayout)
        {
            if (requestedLayout != VolumeSTCubeDataLayout.Auto)
                return requestedLayout;

            if (controller == null)
                return VolumeSTCubeDataLayout.XYTime;

            VolumeSTCubeRawTimeSeries series = controller.GetComponent<VolumeSTCubeRawTimeSeries>();
            if (series != null && series.Count > 0)
                return VolumeSTCubeDataLayout.XYZTimeSeries;

            List<string> rawFiles = new List<string>();
            VolumeRenderedObject[] volumes = controller.GetComponentsInChildren<VolumeRenderedObject>(true);
            for (int i = 0; i < volumes.Length; i++)
            {
                if (volumes[i] != null && volumes[i].dataset != null &&
                    !string.IsNullOrEmpty(volumes[i].dataset.filePath))
                    rawFiles.Add(volumes[i].dataset.filePath);
            }

            return VolumeSTCubeDataLayoutDetector.DetectRawFiles(rawFiles);
        }

        public static VolumeSTCubeTimeAxis GetTimeAxis(VolumeControllerObject controller)
        {
            if (controller == null)
                return VolumeSTCubeTimeAxis.Z;

            VolumeSTCubeTimeController timeController = controller.GetComponent<VolumeSTCubeTimeController>();
            return timeController != null ? timeController.TimeAxis : VolumeSTCubeTimeAxis.Z;
        }

        public static VolumeSTCubeTimeAxis GetWorldTimeAxis(VolumeControllerObject controller)
        {
            if (controller == null)
                return VolumeSTCubeTimeAxis.Z;

            VolumeSTCubeTimeController timeController = controller.GetComponent<VolumeSTCubeTimeController>();
            return timeController != null ? timeController.WorldTimeAxis : VolumeSTCubeTimeAxis.Y;
        }

        private static float GetTimeLength(VolumeControllerObject controller, VolumeSTCubeTimeAxis axis)
        {
            return Mathf.Max(0.0f, GetTopTime(controller, axis) - GetBottomTime(controller, axis));
        }

        private static float GetBottomTime(VolumeControllerObject controller, VolumeSTCubeTimeAxis axis)
        {
            if (controller == null || controller.meshRenderers == null)
                return 0.0f;

            float bottom = float.PositiveInfinity;
            for (int i = 0; i < controller.meshRenderers.Length; i++)
            {
                MeshRenderer renderer = controller.meshRenderers[i];
                if (renderer == null)
                    continue;
                bottom = Mathf.Min(bottom, axis == VolumeSTCubeTimeAxis.Z ? renderer.bounds.min.z : renderer.bounds.min.y);
            }
            return float.IsPositiveInfinity(bottom) ? 0.0f : bottom;
        }

        private static float GetTopTime(VolumeControllerObject controller, VolumeSTCubeTimeAxis axis)
        {
            if (controller == null || controller.meshRenderers == null)
                return 0.0f;

            float top = float.NegativeInfinity;
            for (int i = 0; i < controller.meshRenderers.Length; i++)
            {
                MeshRenderer renderer = controller.meshRenderers[i];
                if (renderer == null)
                    continue;
                top = Mathf.Max(top, axis == VolumeSTCubeTimeAxis.Z ? renderer.bounds.max.z : renderer.bounds.max.y);
            }
            return float.IsNegativeInfinity(top) ? 0.0f : top;
        }
    }
}
