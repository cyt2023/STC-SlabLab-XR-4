using UnityEngine;

namespace UnityVolumeRendering
{
    public class VolumeSTCubeJsonRunner : MonoBehaviour
    {
        [TextArea(8, 30)]
        public string jsonSpec;

        public bool renderOnStart = false;
        public string rawFilePath;
        public string iniFilePath;
        public string viewId = "json_runner_view";
        public string datasetName = "json_runner_dataset";

        private VolumeSTCubeView currentView;

        private void Start()
        {
            if (renderOnStart)
                RenderJson();
        }

        [ContextMenu("Render JSON Spec")]
        public void RenderJson()
        {
            if (string.IsNullOrWhiteSpace(jsonSpec))
            {
                Debug.LogError("VolumeSTCubeJsonRunner.RenderJson failed: jsonSpec is empty.");
                return;
            }

            currentView = VolumeSTCubeAPI.CreateViewFromJson(jsonSpec);
            if (currentView == null)
            {
                Debug.LogError("VolumeSTCubeJsonRunner.RenderJson failed: API returned null.");
                return;
            }

            Debug.Log($"VolumeSTCubeJsonRunner.RenderJson succeeded: viewId '{currentView.viewId}', volume object count {currentView.volumeObjects.Count}.");
        }

        [ContextMenu("Build JSON From Paths And Render")]
        public void BuildJsonFromPathsAndRender()
        {
            jsonSpec = BuildRawFilesJson(rawFilePath, iniFilePath, viewId, datasetName);
            RenderJson();
        }

        [ContextMenu("Destroy Current View")]
        public void DestroyCurrentView()
        {
            if (currentView == null)
            {
                Debug.LogWarning("VolumeSTCubeJsonRunner.DestroyCurrentView skipped: no current view.");
                return;
            }

            VolumeSTCubeAPI.DestroyView(currentView.viewId);
            currentView = null;
        }

        public static string BuildRawFilesJson(string rawPath, string iniPath, string viewId, string datasetName)
        {
            return "{\n"
                + "  \"viewType\": \"VolumeSTCube\",\n"
                + $"  \"viewId\": \"{Escape(viewId)}\",\n"
                + $"  \"datasetName\": \"{Escape(datasetName)}\",\n"
                + "  \"dataMode\": \"rawFiles\",\n"
                + "  \"rawFiles\": [\n"
                + $"    \"{Escape(rawPath)}\"\n"
                + "  ],\n"
                + "  \"iniFiles\": [\n"
                + $"    \"{Escape(iniPath)}\"\n"
                + "  ],\n"
                + "  \"render\": {\n"
                + "    \"mode\": \"Volume\",\n"
                + "    \"showBoundingBox\": true,\n"
                + "    \"showTimeAxis\": true,\n"
                + "    \"timeAxis\": \"Z\",\n"
                + "    \"dataLayout\": \"Auto\",\n"
                + "    \"showTimeline\": true,\n"
                + "    \"enableInteraction\": true,\n"
                + "    \"opacity\": 1.0\n"
                + "  },\n"
                + "  \"transform\": {\n"
                + "    \"position\": [0, 0, 0],\n"
                + "    \"rotation\": [0, 0, 0],\n"
                + "    \"scale\": [1, 1, 1]\n"
                + "  },\n"
                + "  \"filters\": {\n"
                + "    \"timeMin\": 0.0,\n"
                + "    \"timeMax\": 1.0,\n"
                + "    \"variableMin\": 0.0,\n"
                + "    \"variableMax\": 1.0\n"
                + "  }\n"
                + "}";
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
