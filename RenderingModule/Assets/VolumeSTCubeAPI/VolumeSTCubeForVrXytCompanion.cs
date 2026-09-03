using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Side-by-side XYT companion for the For_VR event data. X/Z are the
    /// supplied geographic node positions, Y is event time, and colour is the
    /// physical value. Prediction and Ground Truth use the same scale.
    /// </summary>
    public sealed class VolumeSTCubeForVrXytCompanion : MonoBehaviour
    {
        [Serializable]
        private sealed class Manifest
        {
            public Grid grid;
            public Bounds boundsEPSG4326;
            public GeographicSurface geographicSurface;
            public DatasetInfo[] datasets;
        }

        [Serializable]
        private sealed class Grid
        {
            public int x;
            public int y;
            public int z;
        }

        [Serializable]
        private sealed class Bounds
        {
            public float lonMin;
            public float lonMax;
            public float latMin;
            public float latMax;
        }

        [Serializable]
        private sealed class GeographicSurface
        {
            public int nodeCount;
            public int faceCount;
            public string coordinateFile;
            public string faceFile;
        }

        [Serializable]
        private sealed class DatasetInfo
        {
            public string name;
            public string channel;
            public string unit;
            public float physicalMinimum;
            public float physicalMaximum;
            public string geographicValuesFile;
            public int geographicFrameStrideBytes;
            public string[] timeHKT;
        }

        private sealed class EventRange
        {
            public int number;
            public int first;
            public int last;
        }

        private const int MaximumVisibleTimeSlices = 16;
        private const int MaximumDrillDownNodes = 36;
        private const float CubeWidth = 0.70f;
        private const float CubeDepth = 0.50f;
        private const float CubeHeight = 0.78f;
        private const float CubeSeparation = 0.80f;
        private const float FieldHalfWidth = 1.02f;
        private const float FieldHalfHeight = 0.84f;
        private const float FieldHalfDepth = 0.62f;
        private const float ControlCanvasScale = 0.00072f;
        private const int UiLayer = 5;

        public const float IndependentFieldHalfWidth = FieldHalfWidth;

        private VolumeSTCubeSliceDataset source;
        private Action<int> timeSelected;
        private Manifest manifest;
        private DatasetInfo predictionInfo;
        private DatasetInfo groundTruthInfo;
        private string predictionPath;
        private string groundTruthPath;
        private string predictionRawDirectory;
        private string groundTruthRawDirectory;
        private Vector2[] projectedNodes;
        private int[] triangles;
        private readonly List<EventRange> events = new List<EventRange>();
        private readonly List<UnityEngine.Object> ownedObjects =
            new List<UnityEngine.Object>();
        private readonly List<UnityEngine.Object> rollupOwnedObjects =
            new List<UnityEngine.Object>();
        private GameObject fieldRoot;
        private VolumeSTCubeForVrFieldSwapLayout swapLayout;
        private GameObject visualRoot;
        private GameObject rollupRoot;
        private GameObject sliceRoot;
        private GameObject selectionRoot;
        private GameObject spotlightRoot;
        private GameObject provenanceRoot;
        private readonly GameObject[] combinedTimeSelectorRoots =
            new GameObject[2];
        private GameObject combinedTimeInteractionRoot;
        private GameObject combinedEventBoundariesRoot;
        private GameObject selectedSlicePreviewRoot;
        private GameObject controlsRoot;
        private VolumeSTCubeQuestRayInteractor rayInteractor;
        private VolumeSTCubeQuestSpatialWorkbench spatialWorkbench;
        private TextMeshProUGUI eventText;
        private TextMeshProUGUI selectionText;
        private TextMeshProUGUI modeButtonText;
        private TextMeshProUGUI allEventsButtonText;
        private readonly TextMesh[] combinedTimeLabels = new TextMesh[2];
        private TextMesh selectedSlicePreviewLabel;
        private Mesh selectedPredictionPreviewMesh;
        private Mesh selectedGroundTruthPreviewMesh;
        private int selectedSlicePreviewFrame = -1;
        private int currentFrame;
        private int currentEventIndex = -1;
        private int[] sampledFrames = new int[0];
        private float[][] sampledPrediction = new float[0][];
        private float[][] sampledGroundTruth = new float[0][];
        private float sharedMinimum;
        private float sharedMaximum = 1.0f;
        private float spotlightX;
        private float spotlightZ;
        private float spotlightRadius = 0.105f;
        private bool drillDown;
        private bool showAllEvents;
        private bool combinedTimeDragging;
        private readonly int[] combinedTimeCuts = new int[2];
        private int activeCombinedTimeCut;
        private bool initialized;
        private bool spotlightDragging;
        private int provenanceFirstFrame = -1;
        private int provenanceLastFrame = -1;
        private string provenanceVariable = string.Empty;
        private float draggedCubeCenterX;
        private Vector2 spotlightDragOffset;
        private int lastControlFrame = -1;
        private float lastControlTime = -10.0f;

        public void Initialize(VolumeSTCubeSliceDataset dataset,
            Action<int> onTimeSelected)
        {
            source = dataset;
            timeSelected = onTimeSelected;
            if (!LoadContract())
            {
                enabled = false;
                return;
            }
            DiscoverEvents();
            BuildVisualRoot();
            BuildControls();
            ShowFrame(0);
            initialized = true;
        }

        public void ShowFrame(int frameIndex)
        {
            if (source == null || source.TimeCount == 0)
                return;
            currentFrame = Mathf.Clamp(frameIndex, 0, source.TimeCount - 1);
            int nextEvent = FindEventIndex(currentFrame);
            bool eventChanged = nextEvent != currentEventIndex;
            if (eventChanged)
            {
                currentEventIndex = nextEvent;
                if (!showAllEvents)
                    RebuildEventCubes();
            }
            UpdateCombinedTimeSelector();
            if (eventChanged)
                UpdateSelectionOverlay();
            else
                UpdateSelectionText(SelectedNodes());
            UpdateLabels();
        }

        public void SetVisible(bool visible)
        {
            if (fieldRoot != null)
                fieldRoot.SetActive(visible);
        }

        public void OpenAllEventsTimeSelection()
        {
            if (showAllEvents)
            {
                ApplyModeVisibility();
                UpdateSelectedSlicePreview(true);
                UpdateLabels();
                return;
            }
            showAllEvents = true;
            drillDown = false;
            combinedTimeDragging = false;
            currentFrame = combinedTimeCuts[activeCombinedTimeCut];
            currentEventIndex = FindEventIndex(currentFrame);
            if (allEventsButtonText != null)
                allEventsButtonText.text = "ONE EVENT";
            if (modeButtonText != null)
                modeButtonText.text = "DRILL DOWN";
            RebuildEventCubes();
            UpdateCombinedTimeSelector();
            UpdateSelectedSlicePreview(true);
            UpdateLabels();
            UpdateSelectionOverlay();
        }

        private void OnEnable()
        {
            if (initialized && fieldRoot != null)
                fieldRoot.SetActive(true);
        }

        private void OnDisable()
        {
            if (fieldRoot != null)
                fieldRoot.SetActive(false);
        }

        private bool LoadContract()
        {
            try
            {
                string unityRawRoot = Directory.GetParent(source.DirectoryPath).FullName;
                string manifestPath = Path.Combine(unityRawRoot,
                    "conversion_manifest.json");
                manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
                if (manifest == null || manifest.boundsEPSG4326 == null ||
                    manifest.geographicSurface == null || manifest.datasets == null)
                    throw new InvalidDataException("The conversion manifest is incomplete.");

                string channel = source.Name.EndsWith("Water_Level",
                    StringComparison.OrdinalIgnoreCase) ? "Water_Level" : "HS";
                for (int index = 0; index < manifest.datasets.Length; index++)
                {
                    DatasetInfo candidate = manifest.datasets[index];
                    if (!string.Equals(candidate.channel, channel,
                        StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (candidate.name.StartsWith("Prediction_",
                        StringComparison.OrdinalIgnoreCase))
                        predictionInfo = candidate;
                    else if (candidate.name.StartsWith("GroundTruth_",
                        StringComparison.OrdinalIgnoreCase))
                        groundTruthInfo = candidate;
                }
                if (predictionInfo == null || groundTruthInfo == null)
                    throw new InvalidDataException(
                        "Prediction and Ground Truth metadata are required.");

                sharedMinimum = Mathf.Min(predictionInfo.physicalMinimum,
                    groundTruthInfo.physicalMinimum);
                sharedMaximum = Mathf.Max(predictionInfo.physicalMaximum,
                    groundTruthInfo.physicalMaximum);
                string geoRoot = Path.Combine(unityRawRoot, "GeoSurface");
                predictionRawDirectory = Path.Combine(unityRawRoot,
                    predictionInfo.name);
                groundTruthRawDirectory = Path.Combine(unityRawRoot,
                    groundTruthInfo.name);
                predictionPath = ResolvePath(geoRoot,
                    predictionInfo.geographicValuesFile);
                groundTruthPath = ResolvePath(geoRoot,
                    groundTruthInfo.geographicValuesFile);
                projectedNodes = ReadProjectedNodes(ResolvePath(geoRoot,
                    manifest.geographicSurface.coordinateFile));
                triangles = ReadUInt32Triples(ResolvePath(geoRoot,
                    manifest.geographicSurface.faceFile));
                return projectedNodes.Length > 0 && triangles.Length > 0;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("For_VR XYT companion unavailable: " +
                    exception.Message);
                return false;
            }
        }

        private static string ResolvePath(string geoRoot, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return string.Empty;
            // Conversion manifests may be generated on either Windows or macOS.
            // Path.GetFileName only recognizes the current platform's separator,
            // so normalize manifest paths before extracting the leaf name.
            string normalized = relativePath.Replace('\\', '/');
            string fileName = Path.GetFileName(normalized);
            return Path.Combine(geoRoot, fileName);
        }

        private Vector2[] ReadProjectedNodes(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (!BitConverter.IsLittleEndian)
                for (int offset = 0; offset + 3 < bytes.Length; offset += 4)
                    Array.Reverse(bytes, offset, 4);
            float[] coordinates = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, coordinates, 0, bytes.Length);
            Vector2[] result = new Vector2[coordinates.Length / 2];
            double south = MercatorY(manifest.boundsEPSG4326.latMin);
            double north = MercatorY(manifest.boundsEPSG4326.latMax);
            double span = Math.Max(1.0e-12, north - south);
            for (int index = 0; index < result.Length; index++)
            {
                float nx = Mathf.InverseLerp(manifest.boundsEPSG4326.lonMin,
                    manifest.boundsEPSG4326.lonMax, coordinates[index * 2]);
                float nz = (float)((MercatorY(coordinates[index * 2 + 1]) -
                    south) / span);
                result[index] = new Vector2(
                    (nx - 0.5f) * CubeWidth,
                    (nz - 0.5f) * CubeDepth);
            }
            return result;
        }

        private static int[] ReadUInt32Triples(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            int[] result = new int[bytes.Length / 4];
            for (int index = 0; index + 2 < result.Length; index += 3)
            {
                // Longitude/latitude faces must swap B/C when placed on
                // Unity's X/Z ground plane so the visible face points up.
                result[index] = unchecked((int)ReadUInt32LittleEndian(bytes,
                    index * 4));
                result[index + 1] = unchecked((int)ReadUInt32LittleEndian(bytes,
                    (index + 2) * 4));
                result[index + 2] = unchecked((int)ReadUInt32LittleEndian(bytes,
                    (index + 1) * 4));
            }
            return result;
        }

        private static uint ReadUInt32LittleEndian(byte[] bytes, int offset)
        {
            return (uint)(bytes[offset] | bytes[offset + 1] << 8 |
                bytes[offset + 2] << 16 | bytes[offset + 3] << 24);
        }

        private void DiscoverEvents()
        {
            events.Clear();
            Regex pattern = new Regex(@"event_(\d+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            for (int frame = 0; frame < source.TimeCount; frame++)
            {
                Match match = pattern.Match(Path.GetFileName(source.RawPaths[frame]));
                int number = match.Success
                    ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                    : 1;
                if (events.Count == 0 || events[events.Count - 1].number != number)
                    events.Add(new EventRange { number = number, first = frame, last = frame });
                else
                    events[events.Count - 1].last = frame;
            }
        }

        private int FindEventIndex(int frame)
        {
            for (int index = 0; index < events.Count; index++)
                if (frame >= events[index].first && frame <= events[index].last)
                    return index;
            return Mathf.Clamp(currentEventIndex, 0, Mathf.Max(0, events.Count - 1));
        }

        private void BuildVisualRoot()
        {
            swapLayout = GetComponent<VolumeSTCubeForVrFieldSwapLayout>();
            if (swapLayout == null)
                swapLayout = gameObject.AddComponent<VolumeSTCubeForVrFieldSwapLayout>();
            swapLayout.Acquire();

            fieldRoot = new GameObject("Independent For_VR XYT Field");
            fieldRoot.transform.SetParent(transform.parent, true);
            // The XYT Field takes the former main presentation position. The
            // animated Field is temporarily shifted left by swapLayout.
            SyncIndependentFieldPose();
            BuildIndependentFieldFrame();

            visualRoot = new GameObject("For_VR XYT Comparison");
            visualRoot.transform.SetParent(fieldRoot.transform, false);
            float frontSign = Camera.main != null && fieldRoot.transform
                .InverseTransformPoint(Camera.main.transform.position).z >= 0.0f
                    ? 1.0f : -1.0f;
            visualRoot.transform.localPosition = new Vector3(
                0.0f, -0.56f, frontSign * 0.14f);
            provenanceRoot = new GameObject("MatPlot source footprint in STC");
            provenanceRoot.transform.SetParent(visualRoot.transform, false);
            provenanceRoot.SetActive(false);
            BuildCubeFrame(-CubeSeparation * 0.5f,
                new Color(0.38f, 0.90f, 1.0f, 0.28f));
            BuildCubeFrame(CubeSeparation * 0.5f,
                new Color(1.0f, 0.78f, 0.38f, 0.28f));
            BuildBaseMap(-CubeSeparation * 0.5f, "Prediction");
            BuildBaseMap(CubeSeparation * 0.5f, "Ground Truth");
            rollupRoot = new GameObject("Smooth roll-up XYT volumes");
            rollupRoot.transform.SetParent(visualRoot.transform, false);
            sliceRoot = new GameObject("Sampled event time slices");
            sliceRoot.transform.SetParent(visualRoot.transform, false);
            selectionRoot = new GameObject("Spotlight drill hierarchy");
            selectionRoot.transform.SetParent(visualRoot.transform, false);
            BuildSpotlightInteraction();
            BuildCombinedTimeSelection();
            BuildSelectedSlicePreview();
        }

        public bool ShowMatPlotSourceRange(int firstFrame, int lastFrame,
            string variableName, out Vector3 worldAnchor)
        {
            worldAnchor = fieldRoot != null
                ? fieldRoot.transform.position : transform.position;
            if (source == null || source.TimeCount <= 0 || visualRoot == null ||
                provenanceRoot == null)
                return false;

            firstFrame = Mathf.Clamp(firstFrame, 0, source.TimeCount - 1);
            lastFrame = Mathf.Clamp(lastFrame, firstFrame,
                source.TimeCount - 1);
            string normalizedVariable = variableName ?? string.Empty;
            int midpoint = Mathf.RoundToInt((firstFrame + lastFrame) * 0.5f);
            int sourceEvent = FindEventIndex(midpoint);
            if (!showAllEvents && sourceEvent != currentEventIndex &&
                sourceEvent >= 0 && sourceEvent < events.Count)
            {
                currentFrame = midpoint;
                currentEventIndex = sourceEvent;
                RebuildEventCubes();
                UpdateLabels();
                UpdateSelectionOverlay();
            }

            int displayFirst = 0;
            int displayLast = source.TimeCount - 1;
            if (!showAllEvents && currentEventIndex >= 0 &&
                currentEventIndex < events.Count)
            {
                displayFirst = events[currentEventIndex].first;
                displayLast = events[currentEventIndex].last;
            }
            float denominator = Mathf.Max(1, displayLast - displayFirst);
            float y0 = Mathf.Clamp01((firstFrame - displayFirst) / denominator) *
                CubeHeight;
            float y1 = Mathf.Clamp01((lastFrame - displayFirst) / denominator) *
                CubeHeight;
            if (Mathf.Abs(y1 - y0) < 0.018f)
            {
                float center = (y0 + y1) * 0.5f;
                y0 = Mathf.Max(0.0f, center - 0.009f);
                y1 = Mathf.Min(CubeHeight, center + 0.009f);
            }
            bool prediction = normalizedVariable.IndexOf("pred",
                StringComparison.OrdinalIgnoreCase) >= 0;
            float centerX = prediction
                ? -CubeSeparation * 0.5f : CubeSeparation * 0.5f;
            // MatPlot provenance uses one selection colour regardless of the
            // dataset role: selected chart, dashed link and STC source band
            // must read as one interaction state.
            Color accent = new Color(0.76f, 0.28f, 1.0f, 0.98f);

            if (provenanceFirstFrame != firstFrame ||
                provenanceLastFrame != lastFrame ||
                provenanceVariable != normalizedVariable)
            {
                ClearChildren(provenanceRoot.transform);
                BuildCombinedBoundaryOutline(centerX, y0, accent,
                    provenanceRoot.transform, 0.008f);
                BuildCombinedBoundaryOutline(centerX, y1, accent,
                    provenanceRoot.transform, 0.008f);
                float edgeX = centerX + CubeWidth * 0.5f;
                float frontZ = -CubeDepth * 0.5f - 0.018f;
                CreateLine("MatPlot source range spine", provenanceRoot.transform,
                    new[] { new Vector3(edgeX, y0, frontZ),
                        new Vector3(edgeX, y1, frontZ) }, accent, 0.008f);
                GameObject labelObject = new GameObject(
                    "MatPlot source time label");
                labelObject.transform.SetParent(provenanceRoot.transform, false);
                labelObject.transform.localPosition = new Vector3(
                    edgeX + 0.025f, (y0 + y1) * 0.5f, frontZ);
                TextMesh label = labelObject.AddComponent<TextMesh>();
                label.font = Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
                label.fontSize = 128;
                label.characterSize = 0.0032f;
                label.anchor = TextAnchor.MiddleLeft;
                label.alignment = TextAlignment.Left;
                label.color = accent;
                label.text = "MATPLOT SOURCE\n" + TimeLabel(firstFrame) +
                    " - " + TimeLabel(lastFrame);
                provenanceFirstFrame = firstFrame;
                provenanceLastFrame = lastFrame;
                provenanceVariable = normalizedVariable;
            }
            provenanceRoot.SetActive(true);
            worldAnchor = visualRoot.transform.TransformPoint(new Vector3(
                centerX + CubeWidth * 0.5f,
                (y0 + y1) * 0.5f,
                -CubeDepth * 0.5f - 0.018f));
            return true;
        }

        public void HideMatPlotSourceRange()
        {
            if (provenanceRoot != null)
                provenanceRoot.SetActive(false);
        }

        private void SyncIndependentFieldPose()
        {
            if (fieldRoot == null)
                return;
            fieldRoot.transform.position = transform.position +
                transform.right * VolumeSTCubeForVrFieldSwapLayout.ActiveSeparation;
            fieldRoot.transform.rotation = transform.rotation;
            fieldRoot.transform.localScale = transform.localScale;
        }

        private void LateUpdate()
        {
            SyncIndependentFieldPose();
        }

        private void Update()
        {
            UpdateSpotlightDrag();
            UpdateCombinedTimeDrag();
        }

        private void BuildBaseMap(float centerX, string role)
        {
            GameObject map = GameObject.CreatePrimitive(PrimitiveType.Quad);
            map.name = role + " Hong Kong basemap";
            map.transform.SetParent(visualRoot.transform, false);
            map.transform.localPosition = new Vector3(centerX, -0.008f, 0.0f);
            map.transform.localRotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            map.transform.localScale = new Vector3(CubeWidth, CubeDepth, 1.0f);
            Collider collider = map.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
            Material material = new Material(Shader.Find("Unlit/Texture"));
            material.mainTexture = Resources.Load<Texture2D>("HongKongOSM");
            material.color = Color.white;
            material.renderQueue = 2990;
            ownedObjects.Add(material);
            map.GetComponent<Renderer>().material = material;
        }

        private void BuildIndependentFieldFrame()
        {
            Vector3[] corners =
            {
                new Vector3(-FieldHalfWidth,-FieldHalfHeight,-FieldHalfDepth),
                new Vector3(FieldHalfWidth,-FieldHalfHeight,-FieldHalfDepth),
                new Vector3(FieldHalfWidth,-FieldHalfHeight,FieldHalfDepth),
                new Vector3(-FieldHalfWidth,-FieldHalfHeight,FieldHalfDepth),
                new Vector3(-FieldHalfWidth,FieldHalfHeight,-FieldHalfDepth),
                new Vector3(FieldHalfWidth,FieldHalfHeight,-FieldHalfDepth),
                new Vector3(FieldHalfWidth,FieldHalfHeight,FieldHalfDepth),
                new Vector3(-FieldHalfWidth,FieldHalfHeight,FieldHalfDepth)
            };
            int[,] edges =
            {
                {0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},
                {0,4},{1,5},{2,6},{3,7}
            };
            Color ghost = new Color(0.0f, 0.86f, 0.94f, 0.20f);
            Color bracket = new Color(0.0f, 0.92f, 1.0f, 0.90f);
            for (int edge = 0; edge < edges.GetLength(0); edge++)
            {
                Vector3 a = corners[edges[edge, 0]];
                Vector3 b = corners[edges[edge, 1]];
                CreateLine("Independent XYT Field soft edge", fieldRoot.transform,
                    new [] { a, b }, ghost, 0.0025f);
                float fraction = Mathf.Clamp(0.18f /
                    Mathf.Max(0.001f, Vector3.Distance(a, b)), 0.10f, 0.24f);
                CreateLine("Independent XYT Field corner A", fieldRoot.transform,
                    new [] { a, Vector3.Lerp(a, b, fraction) }, bracket, 0.0062f);
                CreateLine("Independent XYT Field corner B", fieldRoot.transform,
                    new [] { b, Vector3.Lerp(b, a, fraction) }, bracket, 0.0062f);
            }
            Vector3 axisBottom = new Vector3(-FieldHalfWidth + 0.07f,
                -0.53f, FieldHalfDepth - 0.055f);
            CreateLine("Independent XYT Field time axis", fieldRoot.transform,
                new [] { axisBottom, new Vector3(axisBottom.x, 0.29f, axisBottom.z) },
                new Color(0.20f, 0.48f, 1.0f, 1.0f), 0.010f);
        }

        private void BuildCubeFrame(float centerX, Color color)
        {
            Vector3[] corners =
            {
                new Vector3(centerX - CubeWidth * 0.5f, 0, -CubeDepth * 0.5f),
                new Vector3(centerX + CubeWidth * 0.5f, 0, -CubeDepth * 0.5f),
                new Vector3(centerX + CubeWidth * 0.5f, 0, CubeDepth * 0.5f),
                new Vector3(centerX - CubeWidth * 0.5f, 0, CubeDepth * 0.5f),
                new Vector3(centerX - CubeWidth * 0.5f, CubeHeight, -CubeDepth * 0.5f),
                new Vector3(centerX + CubeWidth * 0.5f, CubeHeight, -CubeDepth * 0.5f),
                new Vector3(centerX + CubeWidth * 0.5f, CubeHeight, CubeDepth * 0.5f),
                new Vector3(centerX - CubeWidth * 0.5f, CubeHeight, CubeDepth * 0.5f)
            };
            int[,] edges =
            {
                {0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},
                {0,4},{1,5},{2,6},{3,7}
            };
            for (int index = 0; index < edges.GetLength(0); index++)
                CreateLine("XYT cube edge", visualRoot.transform,
                    new [] { corners[edges[index, 0]], corners[edges[index, 1]] },
                    color, 0.0035f);
        }

        private void RebuildEventCubes()
        {
            ClearChildren(sliceRoot.transform);
            ReleaseRollupResources();
            ClearChildren(rollupRoot.transform);
            if (currentEventIndex < 0 || currentEventIndex >= events.Count)
                return;
            EventRange range = showAllEvents
                ? new EventRange
                {
                    number = 0,
                    first = 0,
                    last = source.TimeCount - 1
                }
                : events[currentEventIndex];
            int count = range.last - range.first + 1;
            int visibleCount = Mathf.Min(MaximumVisibleTimeSlices, count);
            sampledFrames = new int[visibleCount];
            sampledPrediction = new float[visibleCount][];
            sampledGroundTruth = new float[visibleCount][];
            for (int slice = 0; slice < visibleCount; slice++)
            {
                float u = visibleCount <= 1 ? 0.0f : slice / (float)(visibleCount - 1);
                int frame = range.first + Mathf.RoundToInt(u * (count - 1));
                sampledFrames[slice] = frame;
                sampledPrediction[slice] = ReadFrame(predictionPath,
                    predictionInfo, frame);
                sampledGroundTruth[slice] = ReadFrame(groundTruthPath,
                    groundTruthInfo, frame);
                BuildTimeSlice(-CubeSeparation * 0.5f, u * CubeHeight,
                    sampledPrediction[slice], "Prediction");
                BuildTimeSlice(CubeSeparation * 0.5f, u * CubeHeight,
                    sampledGroundTruth[slice], "Ground Truth");
                BuildSliceTimeLabel(-CubeSeparation * 0.5f, u * CubeHeight,
                    frame, false);
                BuildSliceTimeLabel(CubeSeparation * 0.5f, u * CubeHeight,
                    frame, true);
            }
            BuildSmoothRollupVolumes(range);
            ApplyModeVisibility();
            UpdateCombinedTimeSelector();
        }

        private void BuildSmoothRollupVolumes(EventRange range)
        {
            if (sampledFrames.Length == 0)
                return;
            BuildRayMarchedVolume(-CubeSeparation * 0.5f,
                predictionRawDirectory, predictionInfo, range, "Prediction");
            BuildRayMarchedVolume(CubeSeparation * 0.5f,
                groundTruthRawDirectory, groundTruthInfo, range, "Ground Truth");
        }

        /// <summary>
        /// Builds one continuous X/Y/time scalar field and lets the existing
        /// direct-volume shader integrate it along the camera ray. Unlike the
        /// former billboard cloud, no rendered primitive corresponds to a time
        /// layer, so the roll-up reads as one coherent volume.
        /// </summary>
        private void BuildRayMarchedVolume(float centerX, string rawDirectory,
            DatasetInfo datasetInfo, EventRange range, string role)
        {
            int dimX = manifest.grid != null && manifest.grid.x > 0
                ? manifest.grid.x : 96;
            int dimY = manifest.grid != null && manifest.grid.y > 0
                ? manifest.grid.y : 64;
            int timeCount = range.last - range.first + 1;
            int sourcePlaneSize = dimX * dimY;
            byte[] voxels = new byte[sourcePlaneSize * timeCount];

            for (int time = 0; time < timeCount; time++)
            {
                int frame = range.first + time;
                string fileName = Path.GetFileName(source.RawPaths[frame]);
                string rawPath = Path.Combine(rawDirectory, fileName);
                byte[] bytes = File.ReadAllBytes(rawPath);
                if (bytes.Length < sourcePlaneSize)
                    throw new InvalidDataException("XYT raster frame is too small: " +
                        rawPath);

                // Texture layout is x + y*dimX + time*(dimX*dimY).
                // The source z planes repeat the same horizontal surface, so
                // only the first plane is evidence-bearing.
                int targetOffset = time * sourcePlaneSize;
                for (int index = 0; index < sourcePlaneSize; index++)
                {
                    byte encoded = bytes[index];
                    // The conversion contract reserves zero for outside the
                    // geographic mesh. Keep it truly empty in the volume.
                    if (encoded == 0)
                    {
                        voxels[targetOffset + index] = 0;
                        continue;
                    }
                    // Prediction and GT are encoded with their own source
                    // extrema. Decode first, then re-encode on the channel's
                    // shared physical scale so equal values receive equal
                    // colours in both comparison cubes.
                    float localNormalized = (encoded - 1.0f) / 254.0f;
                    float physical = Mathf.Lerp(datasetInfo.physicalMinimum,
                        datasetInfo.physicalMaximum, localNormalized);
                    float sharedNormalized = Mathf.InverseLerp(sharedMinimum,
                        sharedMaximum, physical);
                    voxels[targetOffset + index] = (byte)Mathf.Clamp(
                        1 + Mathf.RoundToInt(sharedNormalized * 254.0f), 1, 255);
                }
            }

            // Upload the encoded scalar field directly as R8. This avoids the
            // asynchronous float-to-half conversion previously used by
            // VolumeDataset, which could leave a newly selected variable with
            // no renderer while another event texture was being released.
            Texture3D dataTexture = new Texture3D(dimX, dimY, timeCount,
                TextureFormat.R8, false)
            {
                name = role + " continuous XYT R8 texture",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            dataTexture.SetPixelData(voxels, 0);
            dataTexture.Apply(false, true);

            GameObject template = Resources.Load<GameObject>("VolumeContainer");
            GameObject volume = template != null
                ? Instantiate(template)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            volume.name = role + " ray-marched continuous XYT volume";
            volume.transform.SetParent(rollupRoot.transform, false);
            volume.transform.localPosition = new Vector3(centerX,
                CubeHeight * 0.5f, 0.0f);
            // Texture Z is time. Rotate it into Unity Y while texture X/Y stay
            // aligned with the Hong Kong map's X/Z plane.
            volume.transform.localRotation = Quaternion.Euler(-90.0f, 0.0f, 0.0f);
            volume.transform.localScale = new Vector3(
                CubeWidth, CubeDepth, CubeHeight);
            Collider volumeCollider = volume.GetComponent<Collider>();
            if (volumeCollider != null)
                Destroy(volumeCollider);

            MeshRenderer renderer = volume.GetComponent<MeshRenderer>();
            Shader shader = Shader.Find("VolumeRendering/DirectVolumeRenderingShader");
            Material material = new Material(shader)
            {
                name = role + " reliable continuous XYT material",
                renderQueue = 3020
            };
            Texture2D noise = NoiseTextureGenerator.GenerateNoiseTexture(64, 64);
            TransferFunction transferFunction = CreateRollupTransferFunction();
            material.SetTexture("_DataTex", dataTexture);
            material.SetTexture("_NoiseTex", noise);
            material.SetTexture("_TFTex", transferFunction.GetTexture());
            material.SetVector("_TextureSize", new Vector3(dimX, dimY, timeCount));
            material.SetFloat("_MinVal", 0.001f);
            material.SetFloat("_MaxVal", 1.0f);
            material.SetFloat("_IsosurfaceVal", 1.01f);
            material.SetFloat("_CircleX", 0.5f);
            material.SetFloat("_CircleY", 0.5f);
            material.SetFloat("_CircleDensity", 1.0f);
            material.SetFloat("_CircleRadius", 1.0f);
            material.SetFloat("_StartPlane", 0.0f);
            material.SetFloat("_EndPlane", 1.0f);
            material.SetFloat("_ClipedHeight", 1.0f);
            material.SetFloat("_JitterFactor", 5.0f);
            material.EnableKeyword("MODE_DVR");
            material.DisableKeyword("MODE_MIP");
            material.DisableKeyword("MODE_SURF");
            material.DisableKeyword("TF2D_ON");
            material.DisableKeyword("LIGHTING_ON");
            material.DisableKeyword("DEPTHWRITE_ON");
            material.EnableKeyword("DEPTHWRITE_OFF");
            material.EnableKeyword("RAY_TERMINATE_ON");
            renderer.sharedMaterial = material;

            rollupOwnedObjects.Add(dataTexture);
            rollupOwnedObjects.Add(noise);
            rollupOwnedObjects.Add(material);
            rollupOwnedObjects.Add(transferFunction.GetTexture());
            rollupOwnedObjects.Add(transferFunction);
        }

        private TransferFunction CreateRollupTransferFunction()
        {
            TransferFunction transferFunction =
                ScriptableObject.CreateInstance<TransferFunction>();
            transferFunction.name = "For_VR continuous XYT transfer function";
            bool signed = string.Equals(predictionInfo.channel, "Water_Level",
                StringComparison.OrdinalIgnoreCase);
            if (signed)
            {
                float zero = Mathf.InverseLerp(sharedMinimum, sharedMaximum, 0.0f);
                transferFunction.colourControlPoints.Add(new TFColourControlPoint(
                    0.0f, new Color(0.02f, 0.20f, 0.78f)));
                transferFunction.colourControlPoints.Add(new TFColourControlPoint(
                    zero * 0.55f, new Color(0.02f, 0.66f, 1.0f)));
                transferFunction.colourControlPoints.Add(new TFColourControlPoint(
                    zero, new Color(0.12f, 0.92f, 0.68f)));
                transferFunction.colourControlPoints.Add(new TFColourControlPoint(
                    Mathf.Lerp(zero, 1.0f, 0.55f),
                    new Color(1.0f, 0.74f, 0.12f)));
                transferFunction.colourControlPoints.Add(new TFColourControlPoint(
                    1.0f, new Color(1.0f, 0.16f, 0.04f)));
            }
            else
            {
                transferFunction.colourControlPoints.Add(new TFColourControlPoint(
                    0.0f, new Color(0.03f, 0.20f, 0.48f)));
                transferFunction.colourControlPoints.Add(new TFColourControlPoint(
                    0.18f, new Color(0.06f, 0.62f, 1.0f)));
                transferFunction.colourControlPoints.Add(new TFColourControlPoint(
                    0.48f, new Color(0.08f, 0.96f, 0.66f)));
                transferFunction.colourControlPoints.Add(new TFColourControlPoint(
                    0.74f, new Color(1.0f, 0.82f, 0.22f)));
                transferFunction.colourControlPoints.Add(new TFColourControlPoint(
                    1.0f, new Color(1.0f, 0.24f, 0.04f)));
            }
            transferFunction.alphaControlPoints.Add(new TFAlphaControlPoint(
                0.0f, 0.0f));
            transferFunction.alphaControlPoints.Add(new TFAlphaControlPoint(
                0.003f, 0.0f));
            transferFunction.alphaControlPoints.Add(new TFAlphaControlPoint(
                0.004f, 0.020f));
            transferFunction.alphaControlPoints.Add(new TFAlphaControlPoint(
                0.12f, 0.040f));
            transferFunction.alphaControlPoints.Add(new TFAlphaControlPoint(
                0.50f, 0.068f));
            transferFunction.alphaControlPoints.Add(new TFAlphaControlPoint(
                1.0f, 0.125f));
            transferFunction.GenerateTexture();
            return transferFunction;
        }

        private void BuildTimeSlice(float centerX, float y, float[] values,
            string role)
        {
            Vector3[] vertices = new Vector3[projectedNodes.Length];
            Color32[] colors = new Color32[projectedNodes.Length];
            for (int index = 0; index < vertices.Length; index++)
            {
                vertices[index] = new Vector3(centerX + projectedNodes[index].x,
                    y, projectedNodes[index].y);
                float value = values[index];
                Color color = float.IsNaN(value) || float.IsInfinity(value)
                    ? Color.clear : EvaluateDetailColor(value);
                if (color.a > 0.0f)
                    color.a = 0.92f;
                colors[index] = color;
            }
            Mesh mesh = new Mesh { name = role + " XYT time slice" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.colors32 = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            ownedObjects.Add(mesh);
            GameObject layer = new GameObject(role + " sampled time layer");
            layer.transform.SetParent(sliceRoot.transform, false);
            layer.AddComponent<MeshFilter>().sharedMesh = mesh;
            Material material = CreateVertexColorMaterial();
            layer.AddComponent<MeshRenderer>().material = material;
        }

        private void BuildSliceTimeLabel(float centerX, float y, int frame,
            bool rightSide)
        {
            Color color = rightSide
                ? new Color(1.0f, 0.78f, 0.30f, 1.0f)
                : new Color(0.42f, 0.96f, 1.0f, 1.0f);
            float edgeX = centerX + (rightSide ? 1.0f : -1.0f) *
                CubeWidth * 0.5f;
            float labelX = edgeX + (rightSide ? 0.028f : -0.028f);
            float frontZ = -CubeDepth * 0.5f - 0.014f;
            CreateLine("Time label tick", sliceRoot.transform,
                new [] { new Vector3(edgeX, y, frontZ),
                    new Vector3(labelX, y, frontZ) }, color, 0.0035f);

            GameObject labelObject = new GameObject("Time " + TimeLabel(frame));
            labelObject.transform.SetParent(sliceRoot.transform, false);
            labelObject.transform.localPosition = new Vector3(labelX, y, frontZ);
            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 128;
            label.characterSize = 0.0034f;
            label.anchor = rightSide ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
            label.alignment = rightSide ? TextAlignment.Left : TextAlignment.Right;
            label.color = color;
            label.text = TimeLabel(frame);
        }

        private void UpdateSelectionOverlay()
        {
            if (selectionRoot == null || sampledFrames.Length == 0)
                return;
            ApplyModeVisibility();
            ClearChildren(selectionRoot.transform);
            List<int> selected = SelectedNodes();
            UpdateSelectionText(selected);
        }

        private void ApplyModeVisibility()
        {
            if (rollupRoot != null)
                rollupRoot.SetActive(!drillDown);
            if (sliceRoot != null)
                sliceRoot.SetActive(drillDown);
            if (spotlightRoot != null)
                spotlightRoot.SetActive(drillDown && !showAllEvents);
            if (selectionRoot != null)
                selectionRoot.SetActive(drillDown && !showAllEvents);
            if (combinedEventBoundariesRoot != null)
                combinedEventBoundariesRoot.SetActive(showAllEvents);
            for (int index = 0; index < combinedTimeSelectorRoots.Length; index++)
                if (combinedTimeSelectorRoots[index] != null)
                    combinedTimeSelectorRoots[index].SetActive(showAllEvents);
            if (combinedTimeInteractionRoot != null)
                combinedTimeInteractionRoot.SetActive(showAllEvents);
            if (selectedSlicePreviewRoot != null)
                selectedSlicePreviewRoot.SetActive(showAllEvents);
        }

        private List<int> SelectedNodes()
        {
            List<int> result = new List<int>();
            float radiusSquared = spotlightRadius * spotlightRadius;
            for (int index = 0; index < projectedNodes.Length; index++)
            {
                float dx = projectedNodes[index].x - spotlightX;
                float dz = projectedNodes[index].y - spotlightZ;
                if (dx * dx + dz * dz <= radiusSquared)
                    result.Add(index);
            }
            return result;
        }

        private void BuildSpotlightInteraction()
        {
            spotlightRoot = new GameObject("Draggable dashed Spotlight");
            spotlightRoot.transform.SetParent(visualRoot.transform, false);
            spotlightRoot.transform.localPosition = new Vector3(
                spotlightX, 0.0f, spotlightZ);
            BuildDashedSpotlightCylinder(-CubeSeparation * 0.5f,
                new Color(0.46f, 0.94f, 1.0f, 0.48f));
            BuildDashedSpotlightCylinder(CubeSeparation * 0.5f,
                new Color(1.0f, 0.82f, 0.48f, 0.48f));
            rayInteractor = FindObjectOfType<VolumeSTCubeQuestRayInteractor>();
        }

        private void BuildDashedSpotlightCylinder(float centerX, Color color)
        {
            const int ringDashes = 24;
            for (int level = 0; level < 2; level++)
            for (int dash = 0; dash < ringDashes; dash++)
            {
                float a0 = dash / (float)ringDashes * Mathf.PI * 2.0f;
                float a1 = (dash + 0.58f) / ringDashes * Mathf.PI * 2.0f;
                float y = level == 0 ? 0.0f : CubeHeight;
                CreateLine("Spotlight dashed ring", spotlightRoot.transform,
                    new []
                    {
                        new Vector3(centerX + Mathf.Cos(a0) * spotlightRadius,
                            y, Mathf.Sin(a0) * spotlightRadius),
                        new Vector3(centerX + Mathf.Cos(a1) * spotlightRadius,
                            y, Mathf.Sin(a1) * spotlightRadius)
                    }, color, 0.007f);
            }
            const int verticalDashes = 9;
            for (int rail = 0; rail < 4; rail++)
            for (int dash = 0; dash < verticalDashes; dash++)
            {
                float angle = rail * Mathf.PI * 0.5f;
                float y0 = dash / (float)verticalDashes * CubeHeight;
                float y1 = (dash + 0.56f) / verticalDashes * CubeHeight;
                float x = centerX + Mathf.Cos(angle) * spotlightRadius;
                float z = Mathf.Sin(angle) * spotlightRadius;
                CreateLine("Spotlight dashed time rail", spotlightRoot.transform,
                    new [] { new Vector3(x, y0, z), new Vector3(x, y1, z) },
                    color, 0.006f);
            }

            GameObject handle = new GameObject("Spotlight drag volume");
            handle.layer = UiLayer;
            handle.transform.SetParent(spotlightRoot.transform, false);
            handle.transform.localPosition = new Vector3(centerX,
                CubeHeight * 0.5f, 0.0f);
            CapsuleCollider collider = handle.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.radius = spotlightRadius * 1.16f;
            collider.height = CubeHeight + 0.06f;
            collider.isTrigger = true;
            VolumeSTCubeQuestClickTarget target =
                handle.AddComponent<VolumeSTCubeQuestClickTarget>();
            target.Clicked = () => BeginSpotlightDrag(centerX);
        }

        private void BeginSpotlightDrag(float centerX)
        {
            if (rayInteractor == null)
                rayInteractor = FindObjectOfType<VolumeSTCubeQuestRayInteractor>();
            if (rayInteractor == null ||
                !TrySpotlightPlanePoint(rayInteractor.PointerRay, out Vector3 local))
                return;
            draggedCubeCenterX = centerX;
            spotlightDragOffset = new Vector2(
                spotlightX - (local.x - centerX), spotlightZ - local.z);
            spotlightDragging = true;
        }

        private void UpdateSpotlightDrag()
        {
            if (!spotlightDragging || rayInteractor == null)
                return;
            if (rayInteractor.TriggerHeld &&
                TrySpotlightPlanePoint(rayInteractor.PointerRay, out Vector3 local))
            {
                spotlightX = Mathf.Clamp(local.x - draggedCubeCenterX +
                    spotlightDragOffset.x,
                    -CubeWidth * 0.5f + spotlightRadius,
                    CubeWidth * 0.5f - spotlightRadius);
                spotlightZ = Mathf.Clamp(local.z + spotlightDragOffset.y,
                    -CubeDepth * 0.5f + spotlightRadius,
                    CubeDepth * 0.5f - spotlightRadius);
                spotlightRoot.transform.localPosition = new Vector3(
                    spotlightX, 0.0f, spotlightZ);
            }
            if (rayInteractor.TriggerReleased)
            {
                spotlightDragging = false;
                UpdateSelectionOverlay();
            }
        }

        private bool TrySpotlightPlanePoint(Ray ray, out Vector3 local)
        {
            local = Vector3.zero;
            Plane plane = new Plane(visualRoot.transform.up,
                visualRoot.transform.TransformPoint(Vector3.zero));
            if (!plane.Raycast(ray, out float distance) || distance < 0.0f ||
                distance > 12.0f)
                return false;
            local = visualRoot.transform.InverseTransformPoint(ray.GetPoint(distance));
            return true;
        }

        private void BuildCombinedTimeSelection()
        {
            combinedEventBoundariesRoot = new GameObject(
                "All six event boundaries");
            combinedEventBoundariesRoot.transform.SetParent(
                visualRoot.transform, false);
            Color boundaryColor = new Color(0.68f, 0.92f, 1.0f, 0.34f);
            for (int index = 0; index < events.Count; index++)
            {
                float y = events[index].first /
                    (float)Mathf.Max(1, source.TimeCount - 1) * CubeHeight;
                BuildCombinedBoundaryOutline(-CubeSeparation * 0.5f, y,
                    boundaryColor);
                BuildCombinedBoundaryOutline(CubeSeparation * 0.5f, y,
                    boundaryColor);
                GameObject labelObject = new GameObject(
                    "Combined event " + events[index].number + " label");
                labelObject.transform.SetParent(
                    combinedEventBoundariesRoot.transform, false);
                labelObject.transform.localPosition = new Vector3(
                    CubeSeparation * 0.5f + CubeWidth * 0.5f + 0.026f,
                    y, -CubeDepth * 0.5f - 0.012f);
                TextMesh label = labelObject.AddComponent<TextMesh>();
                label.font = Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
                label.fontSize = 112;
                label.characterSize = 0.0031f;
                label.anchor = TextAnchor.MiddleLeft;
                label.alignment = TextAlignment.Left;
                label.color = new Color(0.72f, 0.94f, 1.0f, 0.92f);
                label.text = "E" + events[index].number + "  " +
                    TimeLabel(events[index].first);
            }
            BuildCombinedBoundaryOutline(-CubeSeparation * 0.5f, CubeHeight,
                boundaryColor);
            BuildCombinedBoundaryOutline(CubeSeparation * 0.5f, CubeHeight,
                boundaryColor);

            int lastFrame = Mathf.Max(1, source.TimeCount - 1);
            combinedTimeCuts[0] = Mathf.Clamp(source.TimeCount / 3 - 1,
                0, lastFrame - 1);
            combinedTimeCuts[1] = Mathf.Clamp(source.TimeCount * 2 / 3 - 1,
                combinedTimeCuts[0] + 1, lastFrame);
            Color[] cutColors =
            {
                new Color(0.18f, 0.92f, 1.0f, 0.98f),
                new Color(1.0f, 0.76f, 0.12f, 0.98f)
            };
            for (int cut = 0; cut < 2; cut++)
            {
                combinedTimeSelectorRoots[cut] = new GameObject(
                    "Combined time Cut " + (cut == 0 ? "A" : "B"));
                combinedTimeSelectorRoots[cut].transform.SetParent(
                    visualRoot.transform, false);
                BuildCombinedBoundaryOutline(-CubeSeparation * 0.5f, 0.0f,
                    cutColors[cut], combinedTimeSelectorRoots[cut].transform,
                    0.009f);
                BuildCombinedBoundaryOutline(CubeSeparation * 0.5f, 0.0f,
                    cutColors[cut], combinedTimeSelectorRoots[cut].transform,
                    0.009f);
                GameObject timeLabelObject = new GameObject(
                    "Combined time Cut " + (cut == 0 ? "A" : "B") +
                    " label");
                timeLabelObject.transform.SetParent(
                    combinedTimeSelectorRoots[cut].transform, false);
                timeLabelObject.transform.localPosition = new Vector3(
                    -CubeSeparation * 0.5f - CubeWidth * 0.5f - 0.030f,
                    0.0f, -CubeDepth * 0.5f - 0.015f);
                combinedTimeLabels[cut] = timeLabelObject.AddComponent<TextMesh>();
                combinedTimeLabels[cut].font = Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
                combinedTimeLabels[cut].fontSize = 118;
                combinedTimeLabels[cut].characterSize = 0.0032f;
                combinedTimeLabels[cut].anchor = TextAnchor.MiddleRight;
                combinedTimeLabels[cut].alignment = TextAlignment.Right;
                combinedTimeLabels[cut].color = cutColors[cut];
            }
            activeCombinedTimeCut = 0;
            currentFrame = combinedTimeCuts[activeCombinedTimeCut];

            combinedTimeInteractionRoot = new GameObject(
                "All-events time slice interaction volume");
            combinedTimeInteractionRoot.layer = UiLayer;
            combinedTimeInteractionRoot.transform.SetParent(
                visualRoot.transform, false);
            combinedTimeInteractionRoot.transform.localPosition = new Vector3(
                0.0f, CubeHeight * 0.5f, 0.0f);
            BoxCollider collider =
                combinedTimeInteractionRoot.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(CubeSeparation + CubeWidth,
                CubeHeight, CubeDepth + 0.08f);
            combinedTimeInteractionRoot.AddComponent<
                VolumeSTCubeQuestClickTarget>().Clicked = BeginCombinedTimeDrag;
            UpdateCombinedTimeSelector();
        }

        private void BuildSelectedSlicePreview()
        {
            selectedSlicePreviewRoot = new GameObject(
                "Selected time slice detail beside STC");
            selectedSlicePreviewRoot.transform.SetParent(visualRoot.transform, false);
            selectedSlicePreviewRoot.transform.localPosition = new Vector3(
                CubeSeparation * 0.5f + CubeWidth * 0.5f + 0.28f,
                CubeHeight * 0.48f, -CubeDepth * 0.5f - 0.035f);

            selectedPredictionPreviewMesh = BuildSelectedSliceCard(
                "PREDICTION", 0.17f, new Color(0.38f, 0.90f, 1.0f, 0.95f));
            selectedGroundTruthPreviewMesh = BuildSelectedSliceCard(
                "GROUND TRUTH", -0.17f, new Color(1.0f, 0.78f, 0.38f, 0.95f));

            GameObject labelObject = new GameObject("Selected slice detail label");
            labelObject.transform.SetParent(selectedSlicePreviewRoot.transform, false);
            labelObject.transform.localPosition = new Vector3(0.0f, 0.36f, -0.012f);
            selectedSlicePreviewLabel = labelObject.AddComponent<TextMesh>();
            selectedSlicePreviewLabel.font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            selectedSlicePreviewLabel.fontSize = 104;
            selectedSlicePreviewLabel.characterSize = 0.0027f;
            selectedSlicePreviewLabel.anchor = TextAnchor.MiddleCenter;
            selectedSlicePreviewLabel.alignment = TextAlignment.Center;
            selectedSlicePreviewLabel.color = new Color(0.86f, 0.98f, 1.0f, 1.0f);
            selectedSlicePreviewLabel.text = "SELECTED SLICE DETAIL";
            selectedSlicePreviewRoot.SetActive(false);
        }

        private Mesh BuildSelectedSliceCard(string role, float centerY,
            Color accent)
        {
            GameObject map = GameObject.CreatePrimitive(PrimitiveType.Quad);
            map.name = role + " selected-time map";
            map.transform.SetParent(selectedSlicePreviewRoot.transform, false);
            map.transform.localPosition = new Vector3(0.0f, centerY, 0.0f);
            map.transform.localScale = new Vector3(0.48f, 0.27f, 1.0f);
            Collider collider = map.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
            Material mapMaterial = new Material(Shader.Find("Unlit/Texture"));
            mapMaterial.mainTexture = Resources.Load<Texture2D>("HongKongOSM");
            mapMaterial.color = new Color(1.0f, 1.0f, 1.0f, 0.94f);
            mapMaterial.renderQueue = 3060;
            map.GetComponent<Renderer>().material = mapMaterial;
            ownedObjects.Add(mapMaterial);

            Vector3[] vertices = new Vector3[projectedNodes.Length];
            for (int index = 0; index < vertices.Length; index++)
                vertices[index] = new Vector3(
                    projectedNodes[index].x / CubeWidth * 0.48f,
                    projectedNodes[index].y / CubeDepth * 0.27f, -0.008f);
            Mesh mesh = new Mesh { name = role + " selected-time exact slice" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            ownedObjects.Add(mesh);
            GameObject data = new GameObject(role + " selected-time values",
                typeof(MeshFilter), typeof(MeshRenderer));
            data.transform.SetParent(selectedSlicePreviewRoot.transform, false);
            data.transform.localPosition = new Vector3(0.0f, centerY, 0.0f);
            data.GetComponent<MeshFilter>().sharedMesh = mesh;
            Material dataMaterial = CreateVertexColorMaterial();
            dataMaterial.renderQueue = 3070;
            data.GetComponent<MeshRenderer>().material = dataMaterial;

            GameObject roleLabelObject = new GameObject(role + " preview label");
            roleLabelObject.transform.SetParent(selectedSlicePreviewRoot.transform, false);
            roleLabelObject.transform.localPosition = new Vector3(
                -0.24f, centerY + 0.15f, -0.012f);
            TextMesh roleLabel = roleLabelObject.AddComponent<TextMesh>();
            roleLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            roleLabel.fontSize = 92;
            roleLabel.characterSize = 0.0025f;
            roleLabel.anchor = TextAnchor.MiddleLeft;
            roleLabel.alignment = TextAlignment.Left;
            roleLabel.color = accent;
            roleLabel.text = role;
            return mesh;
        }

        private void UpdateSelectedSlicePreview(bool force = false)
        {
            if (!showAllEvents || selectedPredictionPreviewMesh == null ||
                selectedGroundTruthPreviewMesh == null ||
                (!force && selectedSlicePreviewFrame == currentFrame))
                return;
            selectedSlicePreviewFrame = currentFrame;
            ApplyPreviewColors(selectedPredictionPreviewMesh,
                ReadFrame(predictionPath, predictionInfo, currentFrame));
            ApplyPreviewColors(selectedGroundTruthPreviewMesh,
                ReadFrame(groundTruthPath, groundTruthInfo, currentFrame));
            if (selectedSlicePreviewLabel != null)
                selectedSlicePreviewLabel.text = "CUT " +
                    (activeCombinedTimeCut == 0 ? "A" : "B") +
                    " DETAIL  E" +
                    (FindEventIndex(currentFrame) + 1).ToString("00") + "  " +
                    TimeLabel(currentFrame);
        }

        private void ApplyPreviewColors(Mesh mesh, float[] values)
        {
            Color32[] colors = new Color32[projectedNodes.Length];
            for (int index = 0; index < colors.Length; index++)
            {
                float value = values[index];
                Color color = float.IsNaN(value) || float.IsInfinity(value)
                    ? Color.clear : EvaluateDetailColor(value);
                if (color.a > 0.0f)
                    color.a = 0.94f;
                colors[index] = color;
            }
            mesh.colors32 = colors;
        }

        private void BuildCombinedBoundaryOutline(float centerX, float y,
            Color color, Transform parent = null, float width = 0.004f)
        {
            Transform target = parent != null
                ? parent : combinedEventBoundariesRoot.transform;
            CreateLine("Combined STC time outline", target,
                new[]
                {
                    new Vector3(centerX - CubeWidth * 0.5f, y,
                        -CubeDepth * 0.5f),
                    new Vector3(centerX + CubeWidth * 0.5f, y,
                        -CubeDepth * 0.5f),
                    new Vector3(centerX + CubeWidth * 0.5f, y,
                        CubeDepth * 0.5f),
                    new Vector3(centerX - CubeWidth * 0.5f, y,
                        CubeDepth * 0.5f),
                    new Vector3(centerX - CubeWidth * 0.5f, y,
                        -CubeDepth * 0.5f)
                }, color, width);
        }

        private void BeginCombinedTimeDrag()
        {
            if (!showAllEvents)
                return;
            if (rayInteractor == null)
                rayInteractor = FindObjectOfType<VolumeSTCubeQuestRayInteractor>();
            combinedTimeDragging = rayInteractor != null;
            if (combinedTimeDragging)
            {
                if (TryCombinedTimeFrameFromRay(rayInteractor.PointerRay,
                    out int frame))
                {
                    activeCombinedTimeCut = Mathf.Abs(frame - combinedTimeCuts[0]) <=
                        Mathf.Abs(frame - combinedTimeCuts[1]) ? 0 : 1;
                    SelectCombinedTimeFrame(frame);
                }
            }
        }

        private void UpdateCombinedTimeDrag()
        {
            if (!combinedTimeDragging || rayInteractor == null)
                return;
            if (rayInteractor.TriggerHeld)
                SelectCombinedTimeFromRay(rayInteractor.PointerRay);
            if (rayInteractor.TriggerReleased)
                combinedTimeDragging = false;
        }

        private void SelectCombinedTimeFromRay(Ray ray)
        {
            if (TryCombinedTimeFrameFromRay(ray, out int frame))
                SelectCombinedTimeFrame(frame);
        }

        private bool TryCombinedTimeFrameFromRay(Ray ray, out int frame)
        {
            frame = 0;
            Plane plane = new Plane(visualRoot.transform.forward,
                visualRoot.transform.TransformPoint(Vector3.zero));
            if (!plane.Raycast(ray, out float distance) || distance < 0.0f ||
                distance > 12.0f)
                return false;
            Vector3 local = visualRoot.transform.InverseTransformPoint(
                ray.GetPoint(distance));
            frame = Mathf.RoundToInt(Mathf.Clamp01(local.y / CubeHeight) *
                Mathf.Max(0, source.TimeCount - 1));
            return true;
        }

        private void SelectCombinedTimeFrame(int frame)
        {
            int lastFrame = Mathf.Max(1, source.TimeCount - 1);
            frame = activeCombinedTimeCut == 0
                ? Mathf.Clamp(frame, 0, combinedTimeCuts[1] - 1)
                : Mathf.Clamp(frame, combinedTimeCuts[0] + 1, lastFrame);
            if (frame == combinedTimeCuts[activeCombinedTimeCut])
                return;
            combinedTimeCuts[activeCombinedTimeCut] = frame;
            currentFrame = frame;
            currentEventIndex = FindEventIndex(frame);
            UpdateCombinedTimeSelector();
            UpdateLabels();
            UpdateSelectedSlicePreview();
            timeSelected?.Invoke(frame);
            if (spatialWorkbench == null)
                spatialWorkbench = FindObjectOfType<
                    VolumeSTCubeQuestSpatialWorkbench>();
            spatialWorkbench?.PreviewForVrCombinedTimeRange(
                combinedTimeCuts[0], combinedTimeCuts[1],
                activeCombinedTimeCut);
        }

        private void UpdateCombinedTimeSelector()
        {
            if (combinedTimeSelectorRoots[0] == null || source == null)
                return;
            for (int cut = 0; cut < 2; cut++)
            {
                float normalized = combinedTimeCuts[cut] /
                    (float)Mathf.Max(1, source.TimeCount - 1);
                combinedTimeSelectorRoots[cut].transform.localPosition =
                    new Vector3(0.0f, normalized * CubeHeight, 0.0f);
                int eventIndex = FindEventIndex(combinedTimeCuts[cut]);
                int eventNumber = eventIndex >= 0 && eventIndex < events.Count
                    ? events[eventIndex].number : 0;
                if (combinedTimeLabels[cut] != null)
                    combinedTimeLabels[cut].text = "CUT " +
                        (cut == 0 ? "A" : "B") + "  E" + eventNumber + "  " +
                        TimeLabel(combinedTimeCuts[cut]);
            }
            UpdateSelectedSlicePreview();
        }

        private void BuildNodeTrajectories(float centerX, List<int> selected,
            float[][] frames)
        {
            if (selected.Count == 0)
                return;
            int stride = Mathf.Max(1,
                Mathf.CeilToInt(selected.Count / (float)MaximumDrillDownNodes));
            int shown = 0;
            for (int cursor = 0; cursor < selected.Count &&
                shown < MaximumDrillDownNodes; cursor += stride, shown++)
            {
                int node = selected[cursor];
                Vector3[] points = new Vector3[frames.Length];
                Gradient gradient = new Gradient();
                Color first = EvaluateColor(frames[0][node]);
                Color last = EvaluateColor(frames[frames.Length - 1][node]);
                gradient.SetKeys(
                    new [] { new GradientColorKey(first, 0),
                        new GradientColorKey(last, 1) },
                    new [] { new GradientAlphaKey(0.34f, 0),
                        new GradientAlphaKey(0.34f, 1) });
                for (int time = 0; time < frames.Length; time++)
                    points[time] = new Vector3(centerX + projectedNodes[node].x,
                        TimeY(time), projectedNodes[node].y);
                LineRenderer line = CreateLine("Drill-down node trajectory",
                    selectionRoot.transform, points, Color.white, 0.0035f);
                line.colorGradient = gradient;
            }
        }

        private void BuildAggregateTrajectory(float centerX, List<int> selected,
            float[][] frames)
        {
            if (selected.Count == 0)
                return;
            Vector3[] points = new Vector3[frames.Length];
            Color first = EvaluateColor(Mean(frames[0], selected));
            Color last = EvaluateColor(Mean(frames[frames.Length - 1], selected));
            for (int time = 0; time < frames.Length; time++)
                points[time] = new Vector3(centerX + spotlightX,
                    TimeY(time), spotlightZ);
            LineRenderer line = CreateLine("Roll-up regional trajectory",
                selectionRoot.transform, points, Color.white, 0.006f);
            Gradient gradient = new Gradient();
            gradient.SetKeys(new [] { new GradientColorKey(first, 0),
                new GradientColorKey(last, 1) },
                new [] { new GradientAlphaKey(0.26f, 0),
                    new GradientAlphaKey(0.26f, 1) });
            line.colorGradient = gradient;
        }

        private float TimeY(int sampledIndex)
        {
            return sampledFrames.Length <= 1 ? 0 :
                sampledIndex / (float)(sampledFrames.Length - 1) * CubeHeight;
        }

        private void UpdateSelectionText(List<int> selected)
        {
            if (selectionText == null)
                return;
            if (!drillDown)
            {
            selectionText.text = showAllEvents
                    ? "672-HOUR XYT OVERVIEW   DRAG CUT A / CUT B   CONFIRM IN THE ORANGE TIME PANEL"
                    : "ROLL UP OVERVIEW   SMOOTH XYT VOLUME OVER HONG KONG";
                return;
            }
            float[] prediction = ReadFrame(predictionPath, predictionInfo,
                currentFrame);
            float[] truth = ReadFrame(groundTruthPath, groundTruthInfo,
                currentFrame);
            double squaredError = 0;
            double bias = 0;
            for (int index = 0; index < selected.Count; index++)
            {
                float error = prediction[selected[index]] - truth[selected[index]];
                bias += error;
                squaredError += error * error;
            }
            float predictionMean = Mean(prediction, selected);
            float truthMean = Mean(truth, selected);
            double divisor = Math.Max(1, selected.Count);
            string unit = predictionInfo.unit ?? "";
            selectionText.text = (drillDown ? "DRILL DOWN" : "ROLL UP") +
                "  " + selected.Count + " NODES   P " +
                predictionMean.ToString("0.00", CultureInfo.InvariantCulture) +
                "  GT " + truthMean.ToString("0.00", CultureInfo.InvariantCulture) +
                "  BIAS " + (bias / divisor).ToString("+0.00;-0.00;0.00",
                    CultureInfo.InvariantCulture) + "  RMSE " +
                Math.Sqrt(squaredError / divisor).ToString("0.00",
                    CultureInfo.InvariantCulture) + " " + unit +
                "   |   GRAB DASHED CYLINDER TO MOVE";
        }

        private static float Mean(float[] values, List<int> selected)
        {
            if (values == null || selected == null || selected.Count == 0)
                return 0;
            double sum = 0;
            int count = 0;
            for (int index = 0; index < selected.Count; index++)
            {
                float value = values[selected[index]];
                if (float.IsNaN(value) || float.IsInfinity(value))
                    continue;
                sum += value;
                count++;
            }
            return count > 0 ? (float)(sum / count) : 0;
        }

        private void BuildControls()
        {
            bool desktop = VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled;
            controlsRoot = new GameObject("For_VR XYT Controls", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            controlsRoot.transform.SetParent(fieldRoot.transform, false);
            Canvas canvas = controlsRoot.GetComponent<Canvas>();
            canvas.renderMode = UnityEngine.RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvas.sortingOrder = 360;
            RectTransform rect = controlsRoot.GetComponent<RectTransform>();
            rect.sizeDelta = desktop
                ? new Vector2(1560, 310)
                : new Vector2(1880, 250);
            float frontSign = Camera.main != null &&
                fieldRoot.transform.InverseTransformPoint(
                    Camera.main.transform.position).z >= 0
                    ? 1.0f : -1.0f;
            rect.localPosition = new Vector3(0.0f,
                desktop ? 0.66f : 0.62f,
                frontSign * (FieldHalfDepth + 0.035f));
            rect.localRotation = Quaternion.LookRotation(
                new Vector3(0, 0, -frontSign), Vector3.up);
            rect.localScale = Vector3.one *
                (desktop ? 0.00086f : ControlCanvasScale);
            controlsRoot.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 32;

            RectTransform panel = CreateRect("Panel", controlsRoot.transform);
            panel.sizeDelta = rect.sizeDelta;
            Image background = panel.gameObject.AddComponent<Image>();
            background.color = new Color(0.012f, 0.05f, 0.072f, 0.94f);
            Outline outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.0f, 0.8f, 0.9f, 0.8f);
            eventText = CreateText(panel, "INDEPENDENT XYT FIELD",
                desktop ? 52 : 42,
                desktop ? new Vector2(20, 240) : new Vector2(20, 184),
                new Vector2(rect.sizeDelta.x - 40, desktop ? 58 : 50),
                TextAnchor.MiddleCenter);
            selectionText = CreateText(panel, "SPOTLIGHT",
                desktop ? 39 : 31,
                desktop ? new Vector2(20, 180) : new Vector2(20, 132),
                new Vector2(rect.sizeDelta.x - 40, desktop ? 48 : 42),
                TextAnchor.MiddleCenter);

            float buttonWidth = desktop ? 250.0f : 184.0f;
            float buttonGap = desktop ? 18.0f : 18.0f;
            float x = (rect.sizeDelta.x -
                (buttonWidth * 4.0f + buttonGap * 3.0f)) * 0.5f;
            CreateButton(panel, "EVENT <", x, () => StepEvent(-1));
            x += buttonWidth + buttonGap;
            CreateButton(panel, "EVENT >", x, () => StepEvent(1));
            x += buttonWidth + buttonGap;
            Button mode = CreateButton(panel, "DRILL DOWN", x, ToggleMode);
            modeButtonText = mode.GetComponentInChildren<TextMeshProUGUI>();
            x += buttonWidth + buttonGap;
            Button allEvents = CreateButton(panel, "ALL 6 EVENTS", x,
                ToggleAllEvents);
            allEventsButtonText =
                allEvents.GetComponentInChildren<TextMeshProUGUI>();
            if (allEventsButtonText != null)
                allEventsButtonText.fontSize = desktop ? 34 : 25;
        }

        private void StepEvent(int direction)
        {
            if (events.Count == 0)
                return;
            if (showAllEvents)
            {
                showAllEvents = false;
                if (allEventsButtonText != null)
                    allEventsButtonText.text = "ALL 6 EVENTS";
            }
            int index = (currentEventIndex + direction + events.Count) % events.Count;
            timeSelected?.Invoke(events[index].first);
        }

        private void ToggleAllEvents()
        {
            showAllEvents = !showAllEvents;
            combinedTimeDragging = false;
            if (allEventsButtonText != null)
                allEventsButtonText.text = showAllEvents
                    ? "ONE EVENT" : "ALL 6 EVENTS";
            if (showAllEvents && drillDown)
            {
                drillDown = false;
                if (modeButtonText != null)
                    modeButtonText.text = "DRILL DOWN";
            }
            RebuildEventCubes();
            UpdateLabels();
            UpdateSelectionOverlay();
        }

        private void ToggleMode()
        {
            drillDown = !drillDown;
            if (modeButtonText != null)
                modeButtonText.text = drillDown ? "ROLL UP" : "DRILL DOWN";
            UpdateSelectionOverlay();
        }

        private void UpdateLabels()
        {
            if (eventText == null || currentEventIndex < 0 ||
                currentEventIndex >= events.Count)
                return;
            EventRange range = events[currentEventIndex];
            eventText.text = showAllEvents
                ? "ALL 6 EVENTS   |   SET TWO CUTS FOR THREE TIME RANGES"
                : "EVENT " + range.number.ToString("00") +
                  " OF " + events.Count.ToString("00");
        }

        private string TimeLabel(int frame)
        {
            string[] labels = predictionInfo.timeHKT;
            if (labels == null || frame < 0 || frame >= labels.Length)
                return "t=" + frame;
            DateTimeOffset parsed;
            return DateTimeOffset.TryParse(labels[frame], out parsed)
                ? parsed.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)
                : labels[frame];
        }

        private float[] ReadFrame(string path, DatasetInfo info, int frame)
        {
            int count = projectedNodes.Length;
            int byteCount = checked(count * 4);
            byte[] bytes = new byte[byteCount];
            using (FileStream stream = new FileStream(path, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite))
            {
                stream.Position = (long)frame * info.geographicFrameStrideBytes;
                int offset = 0;
                while (offset < byteCount)
                {
                    int read = stream.Read(bytes, offset, byteCount - offset);
                    if (read <= 0)
                        break;
                    offset += read;
                }
            }
            if (!BitConverter.IsLittleEndian)
                for (int offset = 0; offset < byteCount; offset += 4)
                    Array.Reverse(bytes, offset, 4);
            float[] values = new float[count];
            Buffer.BlockCopy(bytes, 0, values, 0, byteCount);
            return values;
        }

        private Color EvaluateColor(float value)
        {
            float normalized = Mathf.InverseLerp(sharedMinimum, sharedMaximum,
                value);
            bool signed = string.Equals(predictionInfo.channel, "Water_Level",
                StringComparison.OrdinalIgnoreCase);
            if (signed)
            {
                float zero = Mathf.InverseLerp(sharedMinimum, sharedMaximum,
                    0.0f);
                float negativeMid = zero * 0.55f;
                float positiveMid = Mathf.Lerp(zero, 1.0f, 0.55f);
                if (normalized <= negativeMid)
                    return PaletteLerp(normalized, 0.0f,
                        new Color(0.02f, 0.20f, 0.78f), negativeMid,
                        new Color(0.02f, 0.66f, 1.0f));
                if (normalized <= zero)
                    return PaletteLerp(normalized, negativeMid,
                        new Color(0.02f, 0.66f, 1.0f), zero,
                        new Color(0.12f, 0.92f, 0.68f));
                if (normalized <= positiveMid)
                    return PaletteLerp(normalized, zero,
                        new Color(0.12f, 0.92f, 0.68f), positiveMid,
                        new Color(1.0f, 0.74f, 0.12f));
                return PaletteLerp(normalized, positiveMid,
                    new Color(1.0f, 0.74f, 0.12f), 1.0f,
                    new Color(1.0f, 0.16f, 0.04f));
            }
            if (normalized <= 0.18f)
                return PaletteLerp(normalized, 0.0f,
                    new Color(0.03f, 0.20f, 0.48f), 0.18f,
                    new Color(0.06f, 0.62f, 1.0f));
            if (normalized <= 0.48f)
                return PaletteLerp(normalized, 0.18f,
                    new Color(0.06f, 0.62f, 1.0f), 0.48f,
                    new Color(0.08f, 0.96f, 0.66f));
            if (normalized <= 0.74f)
                return PaletteLerp(normalized, 0.48f,
                    new Color(0.08f, 0.96f, 0.66f), 0.74f,
                    new Color(1.0f, 0.82f, 0.22f));
            return PaletteLerp(normalized, 0.74f,
                new Color(1.0f, 0.82f, 0.22f), 1.0f,
                new Color(1.0f, 0.24f, 0.04f));
        }

        private Color EvaluateDetailColor(float value)
        {
            // Drill-down changes spatial granularity only. It intentionally
            // uses the exact same value-to-colour function as the roll-up
            // transfer function above.
            return EvaluateColor(value);
        }

        private static Color PaletteLerp(float value, float leftPosition,
            Color leftColor, float rightPosition, Color rightColor)
        {
            float t = Mathf.InverseLerp(leftPosition, rightPosition, value);
            return Color.Lerp(leftColor, rightColor,
                Mathf.SmoothStep(0.0f, 1.0f, t));
        }

        private Material CreateVertexColorMaterial()
        {
            Material material = new Material(Shader.Find("Sprites/Default"));
            material.color = Color.white;
            material.renderQueue = 3050;
            ownedObjects.Add(material);
            return material;
        }

        private Material CreateTransparentMaterial(Color color)
        {
            Material material = new Material(Shader.Find("Sprites/Default"));
            material.color = color;
            material.renderQueue = 3150;
            ownedObjects.Add(material);
            return material;
        }

        private LineRenderer CreateLine(string name, Transform parent,
            Vector3[] points, Color color, float width)
        {
            GameObject lineObject = new GameObject(name);
            lineObject.transform.SetParent(parent, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.startWidth = width;
            line.endWidth = width;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.startColor = color;
            line.endColor = color;
            line.material = CreateTransparentMaterial(Color.white);
            return line;
        }

        private RectTransform CreateRect(string name, Transform parent)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        private TextMeshProUGUI CreateText(Transform parent, string value,
            float size, Vector2 position, Vector2 dimensions,
            TextAnchor alignment)
        {
            RectTransform rect = CreateRect("Text", parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 0);
            rect.pivot = new Vector2(0, 0);
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null)
                text.font = TMP_Settings.defaultFontAsset;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.alignment = alignment == TextAnchor.MiddleCenter
                ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
            text.color = new Color(0.84f, 0.97f, 1.0f);
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }

        private Button CreateButton(Transform parent, string label, float x,
            UnityEngine.Events.UnityAction action)
        {
            bool desktop = VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled;
            float width = desktop ? 250.0f : 184.0f;
            float height = desktop ? 108.0f : 88.0f;
            RectTransform rect = CreateRect(label + " Button", parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 0);
            rect.pivot = new Vector2(0, 0);
            rect.anchoredPosition = new Vector2(x, desktop ? 30.0f : 24.0f);
            rect.sizeDelta = new Vector2(width, height);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.06f, 0.28f, 0.35f, 1.0f);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            // The collider-backed target is the single input path on Quest and
            // desktop preview. Registering Button.onClick as a second path made
            // one release advance two events (01/03/05 only).
            rect.gameObject.layer = UiLayer;
            BoxCollider collider = rect.gameObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(width, height, 20);
            collider.center = new Vector3(width * 0.5f, height * 0.5f, 0);
            VolumeSTCubeQuestClickTarget clickTarget =
                rect.gameObject.AddComponent<VolumeSTCubeQuestClickTarget>();
            clickTarget.AllowDesktopMouseDown = true;
            clickTarget.Clicked = () => InvokeControlOnce(action);
            CreateText(rect, label, desktop ? 38 : 30, Vector2.zero,
                new Vector2(width, height),
                TextAnchor.MiddleCenter);
            return button;
        }

        private void InvokeControlOnce(UnityEngine.Events.UnityAction action)
        {
            if (lastControlFrame == Time.frameCount ||
                Time.unscaledTime - lastControlTime < 0.18f)
                return;
            lastControlFrame = Time.frameCount;
            lastControlTime = Time.unscaledTime;
            action?.Invoke();
        }

        private void ClearChildren(Transform parent)
        {
            for (int index = parent.childCount - 1; index >= 0; index--)
                Destroy(parent.GetChild(index).gameObject);
        }

        private void ReleaseRollupResources()
        {
            for (int index = 0; index < rollupOwnedObjects.Count; index++)
                if (rollupOwnedObjects[index] != null)
                    Destroy(rollupOwnedObjects[index]);
            rollupOwnedObjects.Clear();
        }

        private static double MercatorY(double latitudeDegrees)
        {
            double latitude = Math.Max(-85.05112878,
                Math.Min(85.05112878, latitudeDegrees)) * Math.PI / 180.0;
            return Math.Log(Math.Tan(Math.PI * 0.25 + latitude * 0.5));
        }

        private void OnDestroy()
        {
            ReleaseRollupResources();
            if (fieldRoot != null)
                Destroy(fieldRoot);
            if (swapLayout != null)
                swapLayout.Release();
            for (int index = 0; index < ownedObjects.Count; index++)
                if (ownedObjects[index] != null)
                    Destroy(ownedObjects[index]);
            ownedObjects.Clear();
        }
    }
}
