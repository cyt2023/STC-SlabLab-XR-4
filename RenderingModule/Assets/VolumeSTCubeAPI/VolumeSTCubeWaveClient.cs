using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Registers a live Wave subset through the local S4D gateway. The gateway
    /// owns the API key; field values remain remote and load on demand.
    /// </summary>
    public sealed class VolumeSTCubeWaveClient : MonoBehaviour
    {
        [Serializable]
        private sealed class ImportResult
        {
            public string datasetId;
            public string datasetVersion;
            public string datasetRoot;
            public string[] variables;
            public string start;
            public string end;
            public int hours;
            public int requests;
            // Which half of the hybrid source answered (see wave_importer.py):
            // "live" for the gateway, "cache" for the bundled snapshot.
            public string source;
            public bool hydrated;
        }

        public string serviceBaseUrl = "http://127.0.0.1:8020";
        public int timeoutSeconds = 900;
        // Registration only exchanges metadata, so a long silence means the
        // request is stuck rather than busy.
        public float stallTimeoutSeconds = 45.0f;

        public bool IsImporting { get; private set; }
        public float Progress { get; private set; }
        public string DatasetId { get; private set; }
        public string DatasetVersion { get; private set; }
        public string[] VariableIds { get; private set; }
        /// <summary>live | cache — which source answered the last import.</summary>
        public string Source { get; private set; }
        public event Action<string, float> ImportProgress;
        public event Action<string, string> ImportCompleted;

        private float lastActivityAt;

        public void Import(string startUtc, string endUtc, params string[] fields)
        {
            // A script recompile (or any domain reload) during Play Mode kills the
            // coroutine that owned the previous request without running its
            // completion path, so IsImporting stayed true for the rest of the
            // session: the workspace kept reporting "waiting for the Wave server"
            // while the backend was healthy, and no retry could ever start. Treat
            // a request that has been silent for a whole stall window as dead.
            if (IsImporting && Time.realtimeSinceStartup - lastActivityAt <=
                Mathf.Max(10.0f, stallTimeoutSeconds))
                return;
            IsImporting = true;
            Progress = 0.0f;
            DatasetId = string.Empty;
            DatasetVersion = string.Empty;
            VariableIds = null;
            Source = string.Empty;
            lastActivityAt = Time.realtimeSinceStartup;
            StartCoroutine(ImportRoutine(startUtc, endUtc, fields));
        }

        private void OnDisable()
        {
            // The coroutine host stops with this component; do not leave callers
            // waiting on a request that can no longer complete.
            IsImporting = false;
        }

        private IEnumerator ImportRoutine(
            string startUtc, string endUtc, string[] fields)
        {
            ReportProgress("Connecting to the Wave server...", 0.1f);
            string body = "{\"start\":\"" + Escape(startUtc) +
                "\",\"end\":\"" + Escape(endUtc) + "\",\"fields\":[";
            if (fields == null || fields.Length == 0)
                fields = new[] { "hs", "elev" };
            for (int index = 0; index < fields.Length; index++)
            {
                if (index > 0)
                    body += ",";
                body += "\"" + Escape(fields[index]) + "\"";
            }
            body += "]}";

            string url = serviceBaseUrl.TrimEnd('/') + "/wave/live-dataset";
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            using (UnityWebRequest request = new UnityWebRequest(
                url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(bytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.Max(30, timeoutSeconds);
                request.SetRequestHeader("Content-Type", "application/json");
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                float started = Time.realtimeSinceStartup;
                while (!operation.isDone)
                {
                    // A registration that never answers used to leave the client
                    // stuck in IsImporting forever: no error, no retry, and the
                    // workspace stayed empty. Give up loudly instead so the
                    // caller can surface it and try again.
                    if (Time.realtimeSinceStartup - started >
                        Mathf.Max(10.0f, stallTimeoutSeconds))
                    {
                        request.Abort();
                        IsImporting = false;
                        ImportCompleted?.Invoke(string.Empty,
                            "The Wave server did not answer within " +
                            Mathf.RoundToInt(stallTimeoutSeconds) +
                            "s. Check that the backend is running, then retry.");
                        yield break;
                    }
                    ReportProgress("Checking Wave metadata and preparing the live timeline...",
                        Mathf.Lerp(0.15f, 0.85f, request.downloadProgress));
                    yield return null;
                }

                IsImporting = false;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    string response = request.downloadHandler != null
                        ? request.downloadHandler.text : string.Empty;
                    // The gateway explains itself in {"detail": "..."}; show that
                    // sentence rather than the raw body, which buries it in quotes
                    // and escapes and overruns the status line.
                    string error = "Wave import failed: " +
                        SlabLabServiceError.Detail(response, request.error);
                    ImportCompleted?.Invoke(string.Empty, error);
                    yield break;
                }

                ImportResult result = JsonUtility.FromJson<ImportResult>(
                    request.downloadHandler.text);
                if (result == null || string.IsNullOrWhiteSpace(result.datasetRoot))
                {
                    ImportCompleted?.Invoke(string.Empty,
                        "Wave import completed, but the dataset path was missing.");
                    yield break;
                }
                DatasetId = result.datasetId;
                DatasetVersion = result.datasetVersion;
                VariableIds = result.variables;
                // Older backends do not send the field; only our own gateway can
                // answer from the bundled snapshot, so an absent value means the
                // live path did the work.
                Source = string.IsNullOrEmpty(result.source)
                    ? "live" : result.source;
                ReportProgress("Live Wave dataset ready.", 1.0f);
                ImportCompleted?.Invoke(result.datasetRoot, string.Empty);
            }
        }

        private void ReportProgress(string message, float progress)
        {
            lastActivityAt = Time.realtimeSinceStartup;
            Progress = Mathf.Clamp01(progress);
            ImportProgress?.Invoke(message, Progress);
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
