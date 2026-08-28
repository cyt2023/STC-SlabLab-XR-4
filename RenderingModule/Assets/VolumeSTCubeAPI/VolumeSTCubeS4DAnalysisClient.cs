using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace UnityVolumeRendering
{
    [Serializable]
    public sealed class S4DIndexBucketRequest
    {
        public string id;
        public string label;
        public int[] indices;
        // Optional variable override used when Variable is a Facet Grid axis.
        // Legacy Time x Depth requests leave it empty.
        public string variableId;
    }

    [Serializable]
    public sealed class S4DDimensionRoleRequest
    {
        public string dimension;
        public string role;
    }

    [Serializable]
    public sealed class S4DFacetGridRequest
    {
        public string datasetId;
        public string variableId;
        public S4DIndexBucketRequest[] timeBuckets;
        public S4DIndexBucketRequest[] depthBuckets;
        public S4DDimensionRoleRequest[] dimensionRoles;
        public string rawIntent;
        public string analysisQuestion;
        public string analyticTask = "characterize_distribution";
        public string[] requestedCellIds;
        public bool hasSharedScaleOverride;
        public float sharedScaleMinimum;
        public float sharedScaleMaximum;
        public string chartType = "horizontal_heatmap";
        public string colorMap = "viridis";
        public string missingPolicy = "exclude";
        public string scalePolicy = "shared_across_grid";
    }

    [Serializable]
    public sealed class S4DSharedScale
    {
        public float minimum;
        public float maximum;
        public string unit;
    }

    public sealed class S4DFacetGridResult
    {
        public bool Succeeded;
        public string JobId;
        public string MatPlotAgentJobId;
        public string SnapshotId;
        public string Error;
        public Texture2D Panel;
        public string ChartResultJson;
        public S4DSharedScale SharedScale;
        public S4DCellStatistic[] CellStatistics;
    }

    [Serializable]
    public sealed class S4DCellStatistic
    {
        public string cellId;
        public float minimum;
        public float mean;
        public float maximum;
        public float validFraction;
        public int validCount;
        public bool hasData;
    }

    [Serializable]
    internal sealed class S4DChartResultEnvelope
    {
        public string[] cellOrder;
        public S4DCellStatistic[] cellStatistics;
    }

    [Serializable]
    public sealed class S4DDigestResult
    {
        public string headline;
        public string summary;
        public string[] findings;
        public string highestCell;
        public string lowestCell;
        public string widestCell;
        public string analyticTask;
        public string rawIntent;
        public string generatedBy;
    }

    [Serializable]
    public sealed class S4DDatasetResolution
    {
        public string datasetId;
        public string datasetVersion;
        public string variableId;
        public string displayName;
        public string unit;
        public string valueSemantics;
    }

    public sealed class S4DGroundVolumeResult
    {
        public bool Succeeded;
        public string Error;
        public float[] Values;
        public int DimX;
        public int DimY;
        public int DimZ;
        public int[] DepthIndices;
        public float Minimum;
        public float Mean;
        public float Maximum;
        public float ValidFraction;
        public float SnapshotCellMean;
        public float ReconstructedCellMean;
    }

    [Serializable]
    public sealed class S4DIntentResolutionRequest
    {
        public string text;
        public string variableId;
        public string variableDisplayName;
        public string unit;
    }

    [Serializable]
    public sealed class S4DIntentResolution
    {
        public string rawText;
        public string analyticTask;
        public string displayLabel;
        public string focus;
        public float confidence;
        public bool usedFallback;
        public string normalizedInstruction;
    }

    /// <summary>
    /// Runtime client for the PC-side S4D analysis service. A request always
    /// materializes every Facet Grid cell through mandatory MatPlotAgent jobs.
    /// </summary>
    public sealed class VolumeSTCubeS4DAnalysisClient
    {
        [Serializable]
        private sealed class SpeechTranscriptionResponse
        {
            public string text;
            public string model;
        }

        [Serializable]
        private sealed class MaterializeResponse
        {
            public string jobId;
            public string matplotAgentJobId;
            public string snapshotId;
            public string status;
            public string statusUrl;
            public S4DSharedScale sharedScale;
        }

        [Serializable]
        private sealed class CellJobStatus
        {
            public string cellId;
            public string status;
            public string stage;
            public float progress;
            public string error;
            public string panelUrl;
        }

        [Serializable]
        private sealed class JobStatusResponse
        {
            public string jobId;
            public string matplotAgentJobId;
            public string snapshotId;
            public string status;
            public string stage;
            public float progress;
            public string error;
            public CellJobStatus[] cells;
        }

        [Serializable]
        private sealed class DigestJobResponse
        {
            public string digestJobId;
            public string status;
            public string stage;
            public float progress;
            public string error;
            public S4DDigestResult digest;
        }

        private readonly string baseUrl;
        private readonly int timeoutSeconds;
        private readonly float pollIntervalSeconds;
        private UnityWebRequest activeRequest;
        private int cancellationVersion;

        public VolumeSTCubeS4DAnalysisClient(
            string baseUrl,
            int timeoutSeconds = 240,
            float pollIntervalSeconds = 1.0f)
        {
            this.baseUrl = string.IsNullOrWhiteSpace(baseUrl)
                ? "http://127.0.0.1:8020"
                : baseUrl.TrimEnd('/');
            this.timeoutSeconds = Mathf.Max(10, timeoutSeconds);
            this.pollIntervalSeconds = Mathf.Max(0.25f, pollIntervalSeconds);
        }

        public void Cancel()
        {
            cancellationVersion++;
            if (activeRequest != null && !activeRequest.isDone)
                activeRequest.Abort();
        }

        public IEnumerator ResolveDataset(
            string variable,
            int dimX,
            int dimY,
            int dimZ,
            int timeCount,
            Action<S4DDatasetResolution, string> onComplete)
        {
            string path = "/datasets/resolve?variable=" +
                UnityWebRequest.EscapeURL(variable ?? string.Empty) +
                "&x=" + dimX + "&y=" + dimY + "&z=" + dimZ +
                "&timeCount=" + timeCount;
            using (UnityWebRequest webRequest = UnityWebRequest.Get(Api(path)))
            {
                activeRequest = webRequest;
                webRequest.timeout = timeoutSeconds;
                yield return webRequest.SendWebRequest();
                activeRequest = null;
                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    onComplete?.Invoke(null,
                        "Dataset manifest resolution failed: " + RequestError(webRequest));
                    yield break;
                }
                S4DDatasetResolution resolution =
                    Parse<S4DDatasetResolution>(webRequest.downloadHandler.text);
                if (resolution == null ||
                    string.IsNullOrWhiteSpace(resolution.datasetId) ||
                    string.IsNullOrWhiteSpace(resolution.variableId))
                {
                    onComplete?.Invoke(null,
                        "Dataset service returned an invalid manifest match.");
                    yield break;
                }
                onComplete?.Invoke(resolution, null);
            }
        }

        public IEnumerator PreviewAtlas(
            S4DFacetGridRequest request,
            Action<Texture2D, string> onComplete)
        {
            if (!Validate(request, out string validationError))
            {
                onComplete?.Invoke(null, validationError);
                yield break;
            }

            int runVersion = ++cancellationVersion;
            byte[] json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(request));
            using (UnityWebRequest webRequest = new UnityWebRequest(
                Api("/analysis/preview-atlas"), UnityWebRequest.kHttpVerbPOST))
            {
                activeRequest = webRequest;
                webRequest.uploadHandler = new UploadHandlerRaw(json);
                webRequest.downloadHandler = new DownloadHandlerTexture(true);
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.timeout = timeoutSeconds;
                yield return webRequest.SendWebRequest();
                activeRequest = null;
                if (runVersion != cancellationVersion)
                {
                    onComplete?.Invoke(null, "S4D preview was cancelled.");
                    yield break;
                }
                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    onComplete?.Invoke(
                        null,
                        "S4D aggregate preview failed: " + RequestError(webRequest));
                    yield break;
                }
                onComplete?.Invoke(DownloadHandlerTexture.GetContent(webRequest), null);
            }
        }

        public IEnumerator ResolveIntent(
            S4DIntentResolutionRequest request,
            Action<S4DIntentResolution, string> onComplete)
        {
            if (request == null)
            {
                onComplete?.Invoke(null, "Intent request is missing.");
                yield break;
            }
            byte[] json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(request));
            using (UnityWebRequest webRequest = new UnityWebRequest(
                Api("/analysis/resolve-intent"), UnityWebRequest.kHttpVerbPOST))
            {
                activeRequest = webRequest;
                webRequest.uploadHandler = new UploadHandlerRaw(json);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.timeout = timeoutSeconds;
                yield return webRequest.SendWebRequest();
                activeRequest = null;
                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    onComplete?.Invoke(null,
                        "Intent resolution failed: " + RequestError(webRequest));
                    yield break;
                }
                S4DIntentResolution resolution =
                    Parse<S4DIntentResolution>(webRequest.downloadHandler.text);
                if (resolution == null ||
                    string.IsNullOrWhiteSpace(resolution.analyticTask))
                {
                    onComplete?.Invoke(null,
                        "Intent service returned an invalid structured result.");
                    yield break;
                }
                onComplete?.Invoke(resolution, null);
            }
        }

        public IEnumerator Materialize(
            S4DFacetGridRequest request,
            Action<string, float> onProgress,
            Action<S4DFacetGridResult> onComplete,
            Action<string, Texture2D> onCellReady = null)
        {
            if (!Validate(request, out string validationError))
            {
                CompleteError(validationError, onComplete);
                yield break;
            }

            int runVersion = ++cancellationVersion;
            onProgress?.Invoke("Submitting complete Facet Grid...", 0.01f);
            MaterializeResponse created = null;
            byte[] json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(request));
            using (UnityWebRequest webRequest = new UnityWebRequest(
                Api("/analysis/materialize"), UnityWebRequest.kHttpVerbPOST))
            {
                activeRequest = webRequest;
                webRequest.uploadHandler = new UploadHandlerRaw(json);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.timeout = timeoutSeconds;
                yield return webRequest.SendWebRequest();
                activeRequest = null;
                if (runVersion != cancellationVersion)
                {
                    CompleteError("S4D analysis was cancelled.", onComplete);
                    yield break;
                }
                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    CompleteError(
                        "S4D analysis service rejected the Grid: " + RequestError(webRequest),
                        onComplete);
                    yield break;
                }
                created = Parse<MaterializeResponse>(webRequest.downloadHandler.text);
            }

            if (created == null || string.IsNullOrWhiteSpace(created.jobId))
            {
                CompleteError("S4D analysis service returned an invalid job.", onComplete);
                yield break;
            }

            JobStatusResponse finalStatus = null;
            System.Collections.Generic.HashSet<string> downloadedCells =
                new System.Collections.Generic.HashSet<string>();
            while (true)
            {
                using (UnityWebRequest webRequest =
                    UnityWebRequest.Get(Api("/jobs/" + created.jobId)))
                {
                    activeRequest = webRequest;
                    webRequest.timeout = timeoutSeconds;
                    yield return webRequest.SendWebRequest();
                    activeRequest = null;
                    if (runVersion != cancellationVersion)
                    {
                        CompleteError("S4D analysis was cancelled.", onComplete);
                        yield break;
                    }
                    if (webRequest.result != UnityWebRequest.Result.Success)
                    {
                        CompleteError(
                            "S4D job status failed: " + RequestError(webRequest),
                            onComplete);
                        yield break;
                    }
                    JobStatusResponse status = Parse<JobStatusResponse>(
                        webRequest.downloadHandler.text);
                    if (status == null)
                    {
                        CompleteError("S4D job returned invalid status JSON.", onComplete);
                        yield break;
                    }
                    string stage = string.IsNullOrWhiteSpace(status.stage)
                        ? status.status
                        : status.stage;
                    onProgress?.Invoke("MatPlotAgent: " + stage, Mathf.Clamp01(status.progress));
                    if (status.cells != null)
                    {
                        for (int index = 0; index < status.cells.Length; index++)
                        {
                            CellJobStatus cell = status.cells[index];
                            if (cell == null ||
                                !string.Equals(cell.status, "completed",
                                    StringComparison.OrdinalIgnoreCase) ||
                                string.IsNullOrWhiteSpace(cell.cellId) ||
                                downloadedCells.Contains(cell.cellId))
                                continue;
                            using (UnityWebRequest cellRequest =
                                UnityWebRequestTexture.GetTexture(
                                    Api("/jobs/" + created.jobId + "/cells/" +
                                        UnityWebRequest.EscapeURL(cell.cellId) + "/panel")))
                            {
                                activeRequest = cellRequest;
                                cellRequest.timeout = timeoutSeconds;
                                yield return cellRequest.SendWebRequest();
                                activeRequest = null;
                                if (runVersion != cancellationVersion)
                                {
                                    CompleteError("S4D analysis was cancelled.", onComplete);
                                    yield break;
                                }
                                if (cellRequest.result ==
                                    UnityWebRequest.Result.Success)
                                {
                                    downloadedCells.Add(cell.cellId);
                                    onCellReady?.Invoke(cell.cellId,
                                        DownloadHandlerTexture.GetContent(cellRequest));
                                }
                            }
                        }
                    }
                    if (string.Equals(status.status, "failed", StringComparison.OrdinalIgnoreCase))
                    {
                        CompleteError(
                            string.IsNullOrWhiteSpace(status.error)
                                ? "MatPlotAgent Grid job failed validation."
                                : status.error,
                            onComplete);
                        yield break;
                    }
                    if (string.Equals(
                        status.status, "completed", StringComparison.OrdinalIgnoreCase))
                    {
                        finalStatus = status;
                        break;
                    }
                }
                yield return new WaitForSecondsRealtime(pollIntervalSeconds);
            }

            Texture2D panel = null;
            onProgress?.Invoke("Downloading validated Facet Grid...", 0.98f);
            using (UnityWebRequest webRequest = UnityWebRequestTexture.GetTexture(
                Api("/jobs/" + created.jobId + "/panel")))
            {
                activeRequest = webRequest;
                webRequest.timeout = timeoutSeconds;
                yield return webRequest.SendWebRequest();
                activeRequest = null;
                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    CompleteError(
                        "Could not download the Facet Grid: " + RequestError(webRequest),
                        onComplete);
                    yield break;
                }
                panel = DownloadHandlerTexture.GetContent(webRequest);
            }

            string chartResultJson = string.Empty;
            using (UnityWebRequest webRequest = UnityWebRequest.Get(
                Api("/jobs/" + created.jobId + "/chart-result")))
            {
                activeRequest = webRequest;
                webRequest.timeout = timeoutSeconds;
                yield return webRequest.SendWebRequest();
                activeRequest = null;
                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    if (panel != null)
                        UnityEngine.Object.Destroy(panel);
                    CompleteError(
                        "Facet Grid metadata validation failed: " + RequestError(webRequest),
                        onComplete);
                    yield break;
                }
                chartResultJson = webRequest.downloadHandler.text;
            }

            S4DChartResultEnvelope chartEnvelope =
                JsonUtility.FromJson<S4DChartResultEnvelope>(chartResultJson);
            onComplete?.Invoke(new S4DFacetGridResult
            {
                Succeeded = true,
                JobId = created.jobId,
                MatPlotAgentJobId = finalStatus != null
                    ? finalStatus.matplotAgentJobId
                    : created.matplotAgentJobId,
                SnapshotId = finalStatus != null &&
                    !string.IsNullOrWhiteSpace(finalStatus.snapshotId)
                    ? finalStatus.snapshotId
                    : created.snapshotId,
                Panel = panel,
                ChartResultJson = chartResultJson,
                CellStatistics = chartEnvelope != null
                    ? chartEnvelope.cellStatistics
                    : null,
                SharedScale = created.sharedScale
            });
        }

        public IEnumerator GroundAggregateVolume(
            string snapshotId,
            string cellId,
            Action<S4DGroundVolumeResult> onComplete)
        {
            if (string.IsNullOrWhiteSpace(snapshotId) ||
                string.IsNullOrWhiteSpace(cellId))
            {
                CompleteGroundError(
                    "A completed snapshot and selected cell are required for Ground.",
                    onComplete);
                yield break;
            }
            string path = "/snapshots/" + UnityWebRequest.EscapeURL(snapshotId) +
                "/cells/" + UnityWebRequest.EscapeURL(cellId) + "/aggregate-volume";
            using (UnityWebRequest webRequest = UnityWebRequest.Get(Api(path)))
            {
                activeRequest = webRequest;
                webRequest.timeout = timeoutSeconds;
                yield return webRequest.SendWebRequest();
                activeRequest = null;
                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    CompleteGroundError(
                        "Ground aggregate failed: " + RequestError(webRequest),
                        onComplete);
                    yield break;
                }
                if (!TryHeaderInt(webRequest, "X-S4D-Dim-X", out int dimX) ||
                    !TryHeaderInt(webRequest, "X-S4D-Dim-Y", out int dimY) ||
                    !TryHeaderInt(webRequest, "X-S4D-Dim-Z", out int dimZ))
                {
                    CompleteGroundError(
                        "Ground aggregate response is missing volume dimensions.",
                        onComplete);
                    yield break;
                }
                byte[] bytes = webRequest.downloadHandler.data;
                int valueCount = checked(dimX * dimY * dimZ);
                if (bytes == null || bytes.Length != valueCount * sizeof(float))
                {
                    CompleteGroundError(
                        "Ground aggregate byte size does not match its dimensions.",
                        onComplete);
                    yield break;
                }
                float[] values = new float[valueCount];
                Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
                if (!BitConverter.IsLittleEndian)
                {
                    for (int index = 0; index < bytes.Length; index += sizeof(float))
                        Array.Reverse(bytes, index, sizeof(float));
                    Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
                }
                string depthHeader =
                    webRequest.GetResponseHeader("X-S4D-Depth-Indices") ?? string.Empty;
                string[] depthParts = depthHeader.Split(
                    new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                int[] depthIndices = new int[depthParts.Length];
                for (int index = 0; index < depthParts.Length; index++)
                    int.TryParse(depthParts[index], out depthIndices[index]);
                float.TryParse(
                    webRequest.GetResponseHeader("X-S4D-Min"),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float minimum);
                float.TryParse(
                    webRequest.GetResponseHeader("X-S4D-Mean"),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float mean);
                float.TryParse(
                    webRequest.GetResponseHeader("X-S4D-Max"),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float maximum);
                float.TryParse(webRequest.GetResponseHeader("X-S4D-Valid-Fraction"),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float validFraction);
                float.TryParse(webRequest.GetResponseHeader("X-S4D-Cell-Mean"),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float snapshotCellMean);
                float.TryParse(webRequest.GetResponseHeader("X-S4D-Reconstructed-Mean"),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float reconstructedCellMean);
                onComplete?.Invoke(new S4DGroundVolumeResult
                {
                    Succeeded = true,
                    Values = values,
                    DimX = dimX,
                    DimY = dimY,
                    DimZ = dimZ,
                    DepthIndices = depthIndices,
                    Minimum = minimum,
                    Mean = mean,
                    Maximum = maximum,
                    ValidFraction = validFraction,
                    SnapshotCellMean = snapshotCellMean,
                    ReconstructedCellMean = reconstructedCellMean
                });
            }
        }

        public IEnumerator TranscribeAudio(
            byte[] wavBytes,
            Action<string, string> onComplete)
        {
            if (wavBytes == null || wavBytes.Length < 64)
            {
                onComplete?.Invoke(null, "The microphone recording is empty.");
                yield break;
            }

            WWWForm form = new WWWForm();
            form.AddBinaryData("file", wavBytes, "quest-voice.wav", "audio/wav");
            using (UnityWebRequest webRequest = UnityWebRequest.Post(
                Api("/speech/transcribe"), form))
            {
                activeRequest = webRequest;
                webRequest.timeout = Mathf.Max(timeoutSeconds, 90);
                yield return webRequest.SendWebRequest();
                activeRequest = null;
                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    onComplete?.Invoke(null,
                        "Voice transcription failed: " + RequestError(webRequest));
                    yield break;
                }
                SpeechTranscriptionResponse response =
                    Parse<SpeechTranscriptionResponse>(webRequest.downloadHandler.text);
                if (response == null || string.IsNullOrWhiteSpace(response.text))
                {
                    onComplete?.Invoke(null,
                        "No speech was recognized. Try again or use TYPE.");
                    yield break;
                }
                onComplete?.Invoke(response.text.Trim(), null);
            }
        }

        public IEnumerator GenerateDigest(
            string s4dJobId,
            Action<S4DDigestResult, string> onComplete)
        {
            if (string.IsNullOrWhiteSpace(s4dJobId))
            {
                onComplete?.Invoke(null,
                    "A completed S4D Grid job is required for Digest.");
                yield break;
            }
            DigestJobResponse created;
            using (UnityWebRequest webRequest = new UnityWebRequest(
                Api("/jobs/" + UnityWebRequest.EscapeURL(s4dJobId) +
                    "/digest"),
                UnityWebRequest.kHttpVerbPOST))
            {
                activeRequest = webRequest;
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.timeout = timeoutSeconds;
                yield return webRequest.SendWebRequest();
                activeRequest = null;
                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    onComplete?.Invoke(null,
                        "Digest submission failed: " + RequestError(webRequest));
                    yield break;
                }
                created = Parse<DigestJobResponse>(
                    webRequest.downloadHandler.text);
            }
            if (created == null ||
                string.IsNullOrWhiteSpace(created.digestJobId))
            {
                onComplete?.Invoke(null,
                    "Digest service returned an invalid job.");
                yield break;
            }

            while (true)
            {
                DigestJobResponse status;
                using (UnityWebRequest webRequest = UnityWebRequest.Get(
                    Api("/digest-jobs/" +
                        UnityWebRequest.EscapeURL(created.digestJobId))))
                {
                    activeRequest = webRequest;
                    webRequest.timeout = timeoutSeconds;
                    yield return webRequest.SendWebRequest();
                    activeRequest = null;
                    if (webRequest.result != UnityWebRequest.Result.Success)
                    {
                        onComplete?.Invoke(null,
                            "Digest polling failed: " +
                                RequestError(webRequest));
                        yield break;
                    }
                    status = Parse<DigestJobResponse>(
                        webRequest.downloadHandler.text);
                }
                if (status == null)
                {
                    onComplete?.Invoke(null,
                        "Digest service returned an invalid status.");
                    yield break;
                }
                if (string.Equals(status.status, "completed",
                    StringComparison.OrdinalIgnoreCase))
                {
                    onComplete?.Invoke(status.digest, null);
                    yield break;
                }
                if (string.Equals(status.status, "failed",
                    StringComparison.OrdinalIgnoreCase))
                {
                    onComplete?.Invoke(null,
                        string.IsNullOrWhiteSpace(status.error)
                            ? "Digest generation failed."
                            : status.error);
                    yield break;
                }
                yield return new WaitForSecondsRealtime(
                    pollIntervalSeconds);
            }
        }

        private string Api(string path)
        {
            return baseUrl + (path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path);
        }

        private static bool Validate(S4DFacetGridRequest request, out string error)
        {
            if (request == null)
            {
                error = "Facet Grid request is required.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(request.datasetId) ||
                string.IsNullOrWhiteSpace(request.variableId))
            {
                error = "datasetId and variableId are required.";
                return false;
            }
            if (request.timeBuckets == null || request.timeBuckets.Length == 0 ||
                request.depthBuckets == null || request.depthBuckets.Length == 0)
            {
                error = "Time and Depth buckets are required.";
                return false;
            }
            error = string.Empty;
            return true;
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
            string body = request.downloadHandler != null
                ? request.downloadHandler.text
                : string.Empty;
            return string.IsNullOrWhiteSpace(body)
                ? request.error
                : request.error + " | " + body;
        }

        private static void CompleteError(
            string error,
            Action<S4DFacetGridResult> onComplete)
        {
            onComplete?.Invoke(new S4DFacetGridResult
            {
                Succeeded = false,
                Error = error
            });
        }

        private static bool TryHeaderInt(
            UnityWebRequest request, string name, out int value)
        {
            return int.TryParse(request.GetResponseHeader(name), out value) && value > 0;
        }

        private static void CompleteGroundError(
            string error,
            Action<S4DGroundVolumeResult> onComplete)
        {
            onComplete?.Invoke(new S4DGroundVolumeResult
            {
                Succeeded = false,
                Error = error
            });
        }
    }
}
