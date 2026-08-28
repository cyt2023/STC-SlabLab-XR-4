using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace UnityVolumeRendering
{
    public class VolumeSTCubeServerClient : MonoBehaviour
    {
        public string serverBaseUrl = "http://localhost:8000";
        public string exampleEndpoint = "/api/volumestcube/example";
        public string specEndpoint = "/api/volumestcube/spec";

        /// <summary>Raised after a server response has created a Unity view.</summary>
        public event Action<VolumeSTCubeView> ViewLoaded;
        /// <summary>Raised for transport, server, JSON, or rendering failures.</summary>
        public event Action<string> RequestFailed;

        public void LoadExampleFromServer()
        {
            LoadSpecFromUrl(serverBaseUrl + exampleEndpoint);
        }

        public void LoadSpecFromUrl(string url)
        {
            StartCoroutine(GetSpec(url));
        }

        public void SendJsonAndRender(string jsonBody)
        {
            StartCoroutine(PostSpec(serverBaseUrl + specEndpoint, jsonBody));
        }

        private IEnumerator GetSpec(string url)
        {
            Debug.Log($"VolumeSTCubeServerClient: GET request started: {url}");
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();
                HandleResponse(request);
            }
        }

        private IEnumerator PostSpec(string url, string jsonBody)
        {
            Debug.Log($"VolumeSTCubeServerClient: POST request started: {url}");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
            using (UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                yield return request.SendWebRequest();
                HandleResponse(request);
            }
        }

        private void HandleResponse(UnityWebRequest request)
        {
            if (request.result != UnityWebRequest.Result.Success)
            {
                string error = $"VolumeSTCubeServerClient request failed: {request.error}";
                Debug.LogError(error);
                RequestFailed?.Invoke(error);
                return;
            }

            string json = request.downloadHandler.text;
            Debug.Log("VolumeSTCubeServerClient: response received.");
            Debug.Log("VolumeSTCubeServerClient: render started.");
            VolumeSTCubeView view = VolumeSTCubeAPI.CreateViewFromJson(json);
            if (view != null)
            {
                Debug.Log($"VolumeSTCubeServerClient: render succeeded for viewId '{view.viewId}'.");
                ViewLoaded?.Invoke(view);
            }
            else
            {
                const string error = "VolumeSTCubeServerClient: response was received, but Unity view creation failed.";
                Debug.LogError(error);
                RequestFailed?.Invoke(error);
            }
        }
    }
}
