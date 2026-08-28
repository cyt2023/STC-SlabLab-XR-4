using AxisController;
using MapController;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    public static class VolumeSTCubeOneClickTest
    {
        private const string TestViewId = "one_click_point_test";
        private const string RunnerObjectName = "VolumeSTCube_OneClickTestRunner";
        private const string CameraObjectName = "VolumeSTCube_SmokeTestCamera";

        [MenuItem("Volume Rendering/Test/One-click API Smoke Test")]
        public static void RunPointDataSmokeTest()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "VolumeSTCube API Smoke Test",
                    "Enter Play mode first, then run this menu item again.",
                    "OK");
                return;
            }

            ClearPreviousSmokeTestObjects();

            VolumeSTCubeJsonRunner runner = GetOrCreateRunner();
            runner.viewId = TestViewId;
            runner.datasetName = "one_click_inline_point_data";
            runner.renderOnStart = false;
            runner.jsonSpec = BuildPointDataJson();
            runner.RenderJson();

            VolumeSTCubeView createdView = VolumeSTCubeAPI.GetView(TestViewId);
            ConfigureTimelineDemoScene(createdView);
            EnsureTestCamera();

            if (createdView != null && createdView.rootObject != null)
            {
                Selection.activeGameObject = createdView.rootObject;
                SceneView.lastActiveSceneView?.FrameSelected();
                Debug.Log("VolumeSTCube timeline demo ready: t is stored in texture Z and displayed as layers above the map while the timeline plays.");
            }
            else
            {
                Selection.activeGameObject = runner.gameObject;
                Debug.LogWarning("VolumeSTCube timeline demo finished, but its generated view was not registered. Check the Console for errors.");
            }

            // Entering the original scene can pause the Editor when Console "Error Pause"
            // reacts to legacy scene errors. Resume after the isolated smoke test is ready.
            EditorApplication.delayCall += () =>
            {
                if (Application.isPlaying)
                    EditorApplication.isPaused = false;
            };
        }

        [MenuItem("Volume Rendering/Test/Clear One-click Smoke Test")]
        public static void ClearOneClickSmokeTest()
        {
            ClearPreviousSmokeTestObjects();
            if (!Application.isPlaying)
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("VolumeSTCube one-click smoke test objects cleared.");
        }

        private static VolumeSTCubeJsonRunner GetOrCreateRunner()
        {
            GameObject runnerObject = GameObject.Find(RunnerObjectName);
            if (runnerObject == null)
                runnerObject = new GameObject(RunnerObjectName);

            VolumeSTCubeJsonRunner runner = runnerObject.GetComponent<VolumeSTCubeJsonRunner>();
            if (runner == null)
                runner = runnerObject.AddComponent<VolumeSTCubeJsonRunner>();

            runnerObject.transform.position = new Vector3(-1.4f, 0.0f, 0.0f);
            return runner;
        }

        private static void EnsureTestCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                GameObject cameraObject = GameObject.Find(CameraObjectName);
                if (cameraObject == null)
                    cameraObject = new GameObject(CameraObjectName);

                camera = cameraObject.GetComponent<Camera>();
                if (camera == null)
                    camera = cameraObject.AddComponent<Camera>();
                camera.tag = "MainCamera";
            }

            CameraController legacyCameraController = camera.GetComponent<CameraController>();
            if (legacyCameraController != null)
                legacyCameraController.enabled = false;

            camera.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
            VolumeSTCubeOriginalSceneAdapter.SetTimelineDemoCamera();
        }

        private static void ConfigureTimelineDemoScene(VolumeSTCubeView view)
        {
            if (view == null)
                return;

            VolumeControllerObject controller = view.GetManagedController();
            if (controller == null || controller.meshRenderers == null || controller.meshRenderers.Length == 0)
                return;

            float bottomY = float.PositiveInfinity;
            float topY = float.NegativeInfinity;
            for (int i = 0; i < controller.meshRenderers.Length; i++)
            {
                MeshRenderer renderer = controller.meshRenderers[i];
                if (renderer == null)
                    continue;
                bottomY = Mathf.Min(bottomY, renderer.bounds.min.y);
                topY = Mathf.Max(topY, renderer.bounds.max.y);
            }

            if (float.IsInfinity(bottomY) || float.IsInfinity(topY))
                return;

            Map map = Object.FindObjectOfType<Map>();
            if (map != null)
            {
                map.dragable = false;
                map.transform.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);
                Vector3 position = map.transform.localPosition;
                map.transform.localPosition = new Vector3(position.x, bottomY - 0.05f, position.z);
            }

            UpperPlane upperPlane = Object.FindObjectOfType<UpperPlane>();
            if (upperPlane != null)
            {
                upperPlane.dragable = false;
                upperPlane.transform.localRotation = Quaternion.identity;
                Vector3 position = upperPlane.transform.localPosition;
                upperPlane.transform.localPosition = new Vector3(position.x, topY + 0.05f, position.z);
                MeshRenderer upperRenderer = upperPlane.GetComponent<MeshRenderer>();
                if (upperRenderer != null)
                    upperRenderer.enabled = false;
                Collider upperCollider = upperPlane.GetComponent<Collider>();
                if (upperCollider != null)
                    upperCollider.enabled = false;
            }

            AxisContainer axis = Object.FindObjectOfType<AxisContainer>();
            if (axis != null && axis.isActive)
                axis.toggleActive();

            EventAnchor.AnchorList anchorList = Object.FindObjectOfType<EventAnchor.AnchorList>();
            if (anchorList != null)
            {
                anchorList.enabled = false;
                Renderer[] anchorRenderers = anchorList.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < anchorRenderers.Length; i++)
                    anchorRenderers[i].enabled = false;
                Collider[] anchorColliders = anchorList.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < anchorColliders.Length; i++)
                    anchorColliders[i].enabled = false;
            }

            TClipper clipper = Object.FindObjectOfType<TClipper>();
            if (clipper != null)
            {
                if (clipper.mapText != null)
                    clipper.mapText.gameObject.SetActive(false);
                if (clipper.upperClipedText != null)
                    clipper.upperClipedText.gameObject.SetActive(false);
                if (clipper.timeRangeText != null)
                    clipper.timeRangeText.gameObject.SetActive(false);
            }

            ControlPanel panel = Object.FindObjectOfType<ControlPanel>();
            VolumeSTCubeTimeController timeController = view.GetTimeController();
            if (panel != null && panel.timeRangeSlider != null && timeController != null)
            {
                float windowWidth = Mathf.Clamp(view.config.timelineWindow, 0.0001f, 1.0f);
                panel.timeRangeSlider.onValueChanged = new Slider.SliderEvent();
                panel.timeRangeSlider.onValueChanged.AddListener(value => timeController.SetCenter(value, windowWidth));
            }
        }

        private static void ClearPreviousSmokeTestObjects()
        {
            VolumeSTCubeAPI.DestroyView(TestViewId);
            DestroyIfExists($"VolumeSTCubeView_{TestViewId}");
            DestroyIfExists($"VolumeSTCubePointPreview_{TestViewId}");
            DestroyIfExists(RunnerObjectName);
            DestroyIfExists(CameraObjectName);
        }

        private static void DestroyIfExists(string objectName)
        {
            GameObject existing = GameObject.Find(objectName);
            if (existing != null)
                Object.DestroyImmediate(existing);
        }

        private static string BuildPointDataJson()
        {
            return "{\n"
                + "  \"viewType\": \"VolumeSTCube\",\n"
                + $"  \"viewId\": \"{TestViewId}\",\n"
                + "  \"datasetName\": \"one_click_inline_point_data\",\n"
                + "  \"dataMode\": \"pointData\",\n"
                + "  \"points\": [\n"
                + "    {\"x\": 0.18, \"y\": 0.22, \"t\": 0.05, \"variable\": 80},\n"
                + "    {\"x\": 0.28, \"y\": 0.34, \"t\": 0.20, \"variable\": 100},\n"
                + "    {\"x\": 0.40, \"y\": 0.45, \"t\": 0.35, \"variable\": 120},\n"
                + "    {\"x\": 0.52, \"y\": 0.54, \"t\": 0.50, \"variable\": 140},\n"
                + "    {\"x\": 0.63, \"y\": 0.62, \"t\": 0.65, \"variable\": 160},\n"
                + "    {\"x\": 0.74, \"y\": 0.70, \"t\": 0.80, \"variable\": 180},\n"
                + "    {\"x\": 0.84, \"y\": 0.78, \"t\": 0.95, \"variable\": 200}\n"
                + "  ],\n"
                + "  \"render\": {\n"
                + "    \"mode\": \"Volume\",\n"
                + "    \"opacity\": 0.85,\n"
                + "    \"showBoundingBox\": true,\n"
                + "    \"showTimeAxis\": true,\n"
                + "    \"timeAxis\": \"Z\",\n"
                + "    \"showTimeline\": true,\n"
                + "    \"timelineAutoPlay\": true,\n"
                + "    \"timelinePlaybackSeconds\": 8.0,\n"
                + "    \"timelineWindow\": 0.10,\n"
                + "    \"autoGroupUnderVolumeController\": true,\n"
                + "    \"enableInteraction\": true\n"
                + "  },\n"
                + "  \"pointGrid\": {\n"
                + "    \"dimX\": 40,\n"
                + "    \"dimY\": 40,\n"
                + "    \"dimT\": 40,\n"
                + "    \"splatRadius\": 4\n"
                + "  },\n"
                + "  \"transform\": {\n"
                + "    \"position\": [0, 0, 0],\n"
                + "    \"rotation\": [0, 0, 0],\n"
                + "    \"scale\": [1, 1, 1]\n"
                + "  }\n"
                + "}";
        }
    }
}
