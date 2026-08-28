using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace UnityVolumeRendering
{
    public sealed class VolumeSTCubeMatPlotResult
    {
        public bool Succeeded;
        public string JobId;
        public string Error;
        public Texture2D Image;
    }

    /// <summary>Small runtime client for MatPlotAgent-fixed's local FastAPI service.</summary>
    public sealed class VolumeSTCubeMatPlotClient
    {
        [Serializable]
        private sealed class CreateJobResponse
        {
            public string job_id;
        }

        [Serializable]
        private sealed class JobStatusResponse
        {
            public string job_id;
            public string status;
            public string stage;
            public float progress;
            public string error;
            public string image_url;
        }

        private readonly string baseUrl;
        private readonly int timeoutSeconds;
        private readonly float pollIntervalSeconds;

        public VolumeSTCubeMatPlotClient(string baseUrl, int timeoutSeconds = 120, float pollIntervalSeconds = 1.25f)
        {
            this.baseUrl = string.IsNullOrWhiteSpace(baseUrl)
                ? "http://127.0.0.1:8010"
                : baseUrl.TrimEnd('/');
            this.timeoutSeconds = Mathf.Max(5, timeoutSeconds);
            this.pollIntervalSeconds = Mathf.Max(0.25f, pollIntervalSeconds);
        }

        public IEnumerator Run(
            string prompt,
            string csvPath,
            Action<string, float> onProgress,
            Action<VolumeSTCubeMatPlotResult> onComplete)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                CompleteError("Enter a natural-language chart request.", onComplete);
                yield break;
            }
            if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
            {
                CompleteError("The extracted XY CSV does not exist: " + csvPath, onComplete);
                yield break;
            }

            onProgress?.Invoke("Checking MatPlotAgent...", 0.01f);
            using (UnityWebRequest health = UnityWebRequest.Get(Api("/health")))
            {
                health.timeout = 5;
                yield return health.SendWebRequest();
                if (health.result != UnityWebRequest.Result.Success)
                {
                    CompleteError(
                        "MatPlotAgent is unavailable at " + baseUrl + ". Start it on port 8010. " +
                        RequestError(health),
                        onComplete);
                    yield break;
                }
            }

            byte[] csvBytes;
            try
            {
                csvBytes = File.ReadAllBytes(csvPath);
            }
            catch (Exception exception)
            {
                CompleteError("Could not read the extracted XY CSV: " + exception.Message, onComplete);
                yield break;
            }

            onProgress?.Invoke("Uploading selected XY slice...", 0.04f);
            string jobId;
            WWWForm form = new WWWForm();
            form.AddField("prompt", prompt);
            form.AddBinaryData("data", csvBytes, Path.GetFileName(csvPath), "text/csv");
            using (UnityWebRequest request = UnityWebRequest.Post(Api("/jobs"), form))
            {
                request.timeout = timeoutSeconds;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    CompleteError("MatPlotAgent rejected the XY slice: " + RequestError(request), onComplete);
                    yield break;
                }

                CreateJobResponse created = Parse<CreateJobResponse>(request.downloadHandler.text);
                jobId = created != null ? created.job_id : string.Empty;
                if (string.IsNullOrWhiteSpace(jobId))
                {
                    CompleteError("MatPlotAgent returned an invalid job response.", onComplete);
                    yield break;
                }
            }

            JobStatusResponse finalStatus = null;
            while (true)
            {
                using (UnityWebRequest request = UnityWebRequest.Get(Api("/jobs/" + jobId)))
                {
                    request.timeout = timeoutSeconds;
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        CompleteError("MatPlotAgent status request failed: " + RequestError(request), onComplete);
                        yield break;
                    }

                    JobStatusResponse status = Parse<JobStatusResponse>(request.downloadHandler.text);
                    if (status == null)
                    {
                        CompleteError("MatPlotAgent returned invalid status JSON.", onComplete);
                        yield break;
                    }

                    string stage = string.IsNullOrWhiteSpace(status.stage) ? status.status : status.stage;
                    onProgress?.Invoke("MatPlotAgent: " + stage, Mathf.Clamp01(status.progress));
                    if (string.Equals(status.status, "failed", StringComparison.OrdinalIgnoreCase))
                    {
                        CompleteError(
                            string.IsNullOrWhiteSpace(status.error) ? "MatPlotAgent job failed." : status.error,
                            onComplete);
                        yield break;
                    }
                    if (string.Equals(status.status, "completed", StringComparison.OrdinalIgnoreCase))
                    {
                        finalStatus = status;
                        break;
                    }
                }
                yield return new WaitForSecondsRealtime(pollIntervalSeconds);
            }

            string imagePath = finalStatus != null && !string.IsNullOrWhiteSpace(finalStatus.image_url)
                ? finalStatus.image_url
                : "/jobs/" + jobId + "/image";
            onProgress?.Invoke("Downloading generated chart...", 0.98f);
            using (UnityWebRequest imageRequest = UnityWebRequestTexture.GetTexture(Api(imagePath)))
            {
                imageRequest.timeout = timeoutSeconds;
                yield return imageRequest.SendWebRequest();
                if (imageRequest.result != UnityWebRequest.Result.Success)
                {
                    CompleteError("Could not download the generated chart: " + RequestError(imageRequest), onComplete);
                    yield break;
                }

                onComplete?.Invoke(new VolumeSTCubeMatPlotResult
                {
                    Succeeded = true,
                    JobId = jobId,
                    Image = DownloadHandlerTexture.GetContent(imageRequest)
                });
            }
        }

        private string Api(string path)
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out Uri absolute) &&
                (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
                return absolute.ToString();
            return baseUrl + (path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path);
        }

        private static T Parse<T>(string json) where T : class
        {
            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch
            {
                return null;
            }
        }

        private static string RequestError(UnityWebRequest request)
        {
            string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            return string.IsNullOrWhiteSpace(body) ? request.error : request.error + " | " + body;
        }

        private static void CompleteError(string error, Action<VolumeSTCubeMatPlotResult> onComplete)
        {
            onComplete?.Invoke(new VolumeSTCubeMatPlotResult
            {
                Succeeded = false,
                Error = error
            });
        }
    }
}
