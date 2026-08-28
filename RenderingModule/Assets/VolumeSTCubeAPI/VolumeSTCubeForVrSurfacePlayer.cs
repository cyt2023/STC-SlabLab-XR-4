using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Geographic surface renderer for the For_VR Hong Kong event data.
    /// The source is [time,node,channel] with no physical depth, so this view
    /// renders one animated height surface over a matching basemap instead of
    /// presenting the values as a synthetic volume.
    /// </summary>
    public sealed class VolumeSTCubeForVrSurfacePlayer : MonoBehaviour
    {
        [Serializable]
        private sealed class ConversionManifest
        {
            public GeographicBounds boundsEPSG4326;
            public GeographicSurfaceMetadata geographicSurface;
            public DatasetMetadata[] datasets;
        }

        [Serializable]
        private sealed class GeographicBounds
        {
            public float lonMin;
            public float lonMax;
            public float latMin;
            public float latMax;
        }

        [Serializable]
        private sealed class GeographicSurfaceMetadata
        {
            public string coordinateReference;
            public string projection;
            public int nodeCount;
            public int faceCount;
            public string coordinateFile;
            public string faceFile;
        }

        [Serializable]
        private sealed class DatasetMetadata
        {
            public string name;
            public string channel;
            public string unit;
            public float physicalMinimum;
            public float physicalMaximum;
            public string geographicValuesFile;
            public string geographicValuesEncoding;
            public int geographicFrameStrideBytes;
            public string[] timeHKT;
        }

        private sealed class TimelineEventRange
        {
            public int number;
            public int first;
            public int last;
        }

        private const float PlaybackIntervalSeconds = 0.18f;
        private const float TimelineCanvasScale = 0.0013f;
        private const float TimelinePanelHeight = 210.0f;
        private static readonly float[] PlaybackSpeeds = { 1.0f, 2.0f, 5.0f, 10.0f };
        private static readonly Vector3 GroundLayerPosition =
            new Vector3(0.0f, -0.80f, 0.0f);
        private VolumeSTCubeSliceDataset dataset;
        private DatasetMetadata metadata;
        private Action<int> timeChanged;
        private Transform hiddenVolumeRoot;
        private GameObject surfaceRoot;
        private GameObject timelineCanvas;
        private Mesh surfaceMesh;
        private Vector3[] vertices;
        private Color32[] colors;
        private int[] frameValues;
        private float[] physicalFrameValues;
        private Slider timelineSlider;
        private RectTransform timelineSliderRect;
        private VolumeSTCubeQuestRayInteractor rayInteractor;
        private VolumeSTCubeForVrXytCompanion xytCompanion;
        private TextMeshProUGUI playText;
        private TextMeshProUGUI speedText;
        private TextMeshProUGUI timeText;
        private TextMeshProUGUI datasetText;
        private TextMeshProUGUI geographicText;
        private bool suppressSlider;
        private bool playing;
        private int playbackSpeedIndex;
        private float nextFrameTime;
        private int currentFrame;
        private int nextRendererSweep;
        private int lastControlActionFrame = -1;
        private float lastControlActionTime = -10.0f;
        private bool questScrubbing;
        private ConversionManifest conversionManifest;
        private Vector2[] geographicCoordinates;
        private int[] geographicTriangles;
        private string geographicValuesPath;
        private bool usingExactGeographicMesh;
        private float mapWidth = 1.48f;
        private float mapDepth = 1.02f;
        private float sharedPhysicalMinimum;
        private float sharedPhysicalMaximum = 1.0f;

        public bool IsPlaying => playing;

        public void OpenCombinedXytTimeSelection()
        {
            SetPlaying(false);
            xytCompanion?.OpenAllEventsTimeSelection();
        }

        public bool TryUpdateGeographicSnapshot(Mesh targetMesh, int frameIndex,
            out string heading, out string statistics, out string scaleLabel)
        {
            heading = string.Empty;
            statistics = string.Empty;
            scaleLabel = string.Empty;
            if (!usingExactGeographicMesh || targetMesh == null || metadata == null ||
                geographicCoordinates == null || geographicTriangles == null)
                return false;

            frameIndex = Mathf.Clamp(frameIndex, 0, dataset.TimeCount - 1);
            float[] values = ReadGeographicPhysicalFrame(frameIndex,
                geographicCoordinates.Length);
            Vector3[] snapshotVertices = new Vector3[geographicCoordinates.Length];
            Color32[] snapshotColors = new Color32[geographicCoordinates.Length];
            double south = MercatorY(conversionManifest.boundsEPSG4326.latMin);
            double north = MercatorY(conversionManifest.boundsEPSG4326.latMax);
            float range = Mathf.Max(0.0001f,
                sharedPhysicalMaximum - sharedPhysicalMinimum);
            float absoluteMaximum = Mathf.Max(0.0001f,
                Mathf.Max(Mathf.Abs(sharedPhysicalMinimum),
                    Mathf.Abs(sharedPhysicalMaximum)));
            bool signed = string.Equals(metadata.channel, "Water_Level",
                StringComparison.OrdinalIgnoreCase);
            double sum = 0.0;
            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            int validCount = 0;
            for (int index = 0; index < values.Length; index++)
            {
                Vector2 coordinate = geographicCoordinates[index];
                float x = Mathf.InverseLerp(conversionManifest.boundsEPSG4326.lonMin,
                    conversionManifest.boundsEPSG4326.lonMax, coordinate.x) - 0.5f;
                float y = (float)((MercatorY(coordinate.y) - south) /
                    Math.Max(1.0e-12, north - south)) - 0.5f;
                snapshotVertices[index] = new Vector3(x, y, -0.006f);
                float value = values[index];
                bool valid = !float.IsNaN(value) && !float.IsInfinity(value);
                if (!valid)
                {
                    snapshotColors[index] = new Color32(0, 0, 0, 0);
                    continue;
                }
                float normalized = Mathf.Clamp01(
                    (value - sharedPhysicalMinimum) / range);
                Color32 color = signed
                    ? EvaluateSignedSurfaceColor(value, absoluteMaximum)
                    : EvaluateSnapshotSurfaceColor(normalized);
                color.a = 235;
                snapshotColors[index] = color;
                minimum = Mathf.Min(minimum, value);
                maximum = Mathf.Max(maximum, value);
                sum += value;
                validCount++;
            }

            targetMesh.Clear();
            if (snapshotVertices.Length > 65535)
                targetMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            targetMesh.vertices = snapshotVertices;
            targetMesh.colors32 = snapshotColors;
            targetMesh.triangles = geographicTriangles;
            targetMesh.RecalculateBounds();

            string timestamp = metadata.timeHKT != null &&
                frameIndex < metadata.timeHKT.Length
                    ? metadata.timeHKT[frameIndex]
                    : dataset.GetTimeLabel(frameIndex);
            if (DateTimeOffset.TryParse(timestamp, out DateTimeOffset parsedTime))
                timestamp = parsedTime.ToString("yyyy-MM-dd HH:mm 'HKT'");
            string role = dataset.Name.StartsWith("GroundTruth_",
                StringComparison.OrdinalIgnoreCase) ? "GROUND TRUTH" : "PREDICTION";
            string variable = string.Equals(metadata.channel, "Water_Level",
                StringComparison.OrdinalIgnoreCase) ? "WATER LEVEL" : "HS";
            string unit = string.IsNullOrWhiteSpace(metadata.unit) ? "" : metadata.unit;
            heading = role + " " + variable + " (" + unit + ")" +
                "\nHOUR " + (frameIndex + 1) + " / " + dataset.TimeCount +
                "\n" + timestamp;
            statistics = validCount > 0
                ? string.Format("MIN   {0:0.00}\nMEAN  {1:0.00}\nMAX   {2:0.00} {3}",
                    minimum, sum / validCount, maximum, unit)
                : "NO VALID SAMPLES";
            scaleLabel = signed
                ? string.Format("{0:0.00} {2}\n0\n{1:0.00} {2}",
                    sharedPhysicalMaximum, sharedPhysicalMinimum, unit)
                : string.Format("{0:0.00} {2}\n\n{1:0.00} {2}",
                    sharedPhysicalMaximum, sharedPhysicalMinimum, unit);
            return true;
        }

        public void UpdateSnapshotLegend(Texture2D legend)
        {
            if (legend == null)
                return;
            bool signed = metadata != null && string.Equals(metadata.channel,
                "Water_Level", StringComparison.OrdinalIgnoreCase);
            float absoluteMaximum = Mathf.Max(0.0001f,
                Mathf.Max(Mathf.Abs(sharedPhysicalMinimum),
                    Mathf.Abs(sharedPhysicalMaximum)));
            float range = Mathf.Max(0.0001f,
                sharedPhysicalMaximum - sharedPhysicalMinimum);
            for (int y = 0; y < legend.height; y++)
            {
                float t = y / (float)Mathf.Max(1, legend.height - 1);
                float value = sharedPhysicalMinimum + t * range;
                Color color = signed
                    ? EvaluateSignedSurfaceColor(value, absoluteMaximum)
                    : EvaluateSnapshotSurfaceColor(t);
                color.a = 1.0f;
                for (int x = 0; x < legend.width; x++)
                    legend.SetPixel(x, y, color);
            }
            legend.Apply(false, false);
        }

        public void EnsurePlaybackContinues()
        {
            if (playing || dataset == null || dataset.TimeCount <= 1)
                return;
            // Resume from the current frame. This deliberately does not call
            // the START-button path, which would reset playback to day one.
            SetPlaying(true);
        }

        public static bool Supports(VolumeSTCubeSliceDataset candidate)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.DirectoryPath))
                return false;
            string normalized = candidate.DirectoryPath.Replace('\\', '/');
            return normalized.IndexOf("/For_VR/UnityRaw/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void Initialize(VolumeSTCubeSliceDataset source, int initialFrame,
            Transform volumeRoot, Action<int> onTimeChanged)
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            QualitySettings.antiAliasing = Mathf.Max(QualitySettings.antiAliasing, 4);
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
#endif
            dataset = source;
            timeChanged = onTimeChanged;
            hiddenVolumeRoot = volumeRoot;
            LoadMetadata();
            BuildSurface();
            BuildTimeline();
            xytCompanion = gameObject.AddComponent<VolumeSTCubeForVrXytCompanion>();
            xytCompanion.Initialize(dataset, frame => ShowFrame(frame, true));
            // Every dataset opens at the far-left end of the timeline.
            ShowFrame(0, false);
            HideSyntheticVolume();
            Debug.Log("For_VR geographic surface ready: " + dataset.Name +
                ", " + dataset.TimeCount + " hourly frames over Hong Kong.");
        }

        public void SetVisible(bool visible)
        {
            if (surfaceRoot != null)
                surfaceRoot.SetActive(visible);
            if (timelineCanvas != null)
                timelineCanvas.SetActive(visible);
            if (xytCompanion != null)
                xytCompanion.SetVisible(visible);
        }

        public void FreezeAtFrame(int frameIndex)
        {
            SetPlaying(false);
            ShowFrame(frameIndex, false);
        }

        public void ShowFrame(int frameIndex, bool notify)
        {
            if (dataset == null || dataset.RawPaths == null || dataset.RawPaths.Length == 0)
                return;
            frameIndex = Mathf.Clamp(frameIndex, 0, dataset.RawPaths.Length - 1);
            int sampleCount = usingExactGeographicMesh
                ? geographicCoordinates.Length
                : dataset.DimX * dataset.DimY;
            byte[] bytes = usingExactGeographicMesh
                ? null
                : ReadFrameBytes(frameIndex, sampleCount);
            float[] exactValues = usingExactGeographicMesh
                ? ReadGeographicPhysicalFrame(frameIndex, sampleCount)
                : null;
            if ((!usingExactGeographicMesh && bytes.Length < sampleCount) ||
                (usingExactGeographicMesh && exactValues.Length < sampleCount) ||
                surfaceMesh == null)
                return;

            currentFrame = frameIndex;
            if (xytCompanion != null)
                xytCompanion.ShowFrame(frameIndex);
            if (frameValues == null || frameValues.Length != sampleCount)
                frameValues = new int[sampleCount];
            if (physicalFrameValues == null || physicalFrameValues.Length != sampleCount)
                physicalFrameValues = new float[sampleCount];
            float minimum = metadata != null ? metadata.physicalMinimum : 0.0f;
            float maximum = metadata != null ? metadata.physicalMaximum : 1.0f;
            float physicalRange = Mathf.Max(0.0001f, maximum - minimum);
            float sharedRange = Mathf.Max(0.0001f,
                sharedPhysicalMaximum - sharedPhysicalMinimum);
            bool signedWaterLevel = metadata != null &&
                string.Equals(metadata.channel, "Water_Level",
                    StringComparison.OrdinalIgnoreCase);
            float sharedAbsoluteMaximum = Mathf.Max(0.0001f,
                Mathf.Max(Mathf.Abs(sharedPhysicalMinimum),
                    Mathf.Abs(sharedPhysicalMaximum)));
            double mercatorSouth = MercatorY(conversionManifest.boundsEPSG4326.latMin);
            double mercatorNorth = MercatorY(conversionManifest.boundsEPSG4326.latMax);
            for (int index = 0; index < sampleCount; index++)
            {
                float exactValue = usingExactGeographicMesh
                    ? exactValues[index]
                    : 0.0f;
                int encoded = usingExactGeographicMesh
                    ? (float.IsNaN(exactValue) || float.IsInfinity(exactValue) ? 0 : 1)
                    : bytes[index];
                frameValues[index] = encoded;
                float nx;
                float ny;
                if (usingExactGeographicMesh)
                {
                    Vector2 coordinate = geographicCoordinates[index];
                    nx = Mathf.InverseLerp(conversionManifest.boundsEPSG4326.lonMin,
                        conversionManifest.boundsEPSG4326.lonMax, coordinate.x);
                    ny = (float)((MercatorY(coordinate.y) - mercatorSouth) /
                        Math.Max(1.0e-12, mercatorNorth - mercatorSouth));
                }
                else
                {
                    int x = index % dataset.DimX;
                    int y = index / dataset.DimX;
                    nx = x / (float)Mathf.Max(1, dataset.DimX - 1);
                    float latitude = Mathf.Lerp(conversionManifest.boundsEPSG4326.latMin,
                        conversionManifest.boundsEPSG4326.latMax,
                        y / (float)Mathf.Max(1, dataset.DimY - 1));
                    ny = (float)((MercatorY(latitude) - mercatorSouth) /
                        Math.Max(1.0e-12, mercatorNorth - mercatorSouth));
                }
                float normalized = encoded <= 0 ? 0.0f
                    : usingExactGeographicMesh
                        ? Mathf.Clamp01((exactValue - minimum) / physicalRange)
                        : (encoded - 1.0f) / 254.0f;
                float physicalValue = usingExactGeographicMesh
                    ? exactValue
                    : minimum + normalized * physicalRange;
                physicalFrameValues[index] = physicalValue;
                float sharedNormalized = Mathf.Clamp01(
                    (physicalValue - sharedPhysicalMinimum) / sharedRange);
                float elevation = encoded <= 0 ? 0.012f
                    : signedWaterLevel
                        ? 0.185f + (physicalValue / sharedAbsoluteMaximum) * 0.15f
                        : 0.035f + sharedNormalized * 0.30f;
                vertices[index] = new Vector3(
                    Mathf.Lerp(-mapWidth * 0.5f, mapWidth * 0.5f, nx), elevation,
                    Mathf.Lerp(-mapDepth * 0.5f, mapDepth * 0.5f, ny));
                colors[index] = encoded <= 0
                    ? new Color32(0, 0, 0, 0)
                    : signedWaterLevel
                        ? EvaluateSignedSurfaceColor(physicalValue,
                            sharedAbsoluteMaximum)
                        : EvaluateSurfaceColor(sharedNormalized);
            }
            surfaceMesh.vertices = vertices;
            surfaceMesh.colors32 = colors;
            surfaceMesh.triangles = usingExactGeographicMesh
                ? BuildValidGeographicTriangles(frameValues, geographicTriangles)
                : BuildValidTriangles(frameValues, dataset.DimX, dataset.DimY);
            surfaceMesh.RecalculateNormals();
            surfaceMesh.RecalculateBounds();
            UpdateTimelineText(minimum, physicalRange);
            if (timelineSlider != null)
            {
                suppressSlider = true;
                timelineSlider.SetValueWithoutNotify(currentFrame);
                suppressSlider = false;
            }
            if (notify)
                timeChanged?.Invoke(currentFrame);
        }

        private void Update()
        {
            UpdateTimelineScrubbing();
            if (!playing || dataset == null || dataset.TimeCount <= 1)
                return;
            float interval = CurrentPlaybackInterval();
            if (Time.unscaledTime >= nextFrameTime)
            {
                int next = (currentFrame + 1) % dataset.TimeCount;
                ShowFrame(next, true);
                nextFrameTime = Time.unscaledTime + interval;
            }
            UpdateAnimatedTimelineHandle(interval);
        }

        private float CurrentPlaybackInterval()
        {
            return PlaybackIntervalSeconds / PlaybackSpeeds[playbackSpeedIndex];
        }

        private void UpdateAnimatedTimelineHandle(float interval)
        {
            if (timelineSlider == null || dataset == null ||
                currentFrame >= dataset.TimeCount - 1)
                return;
            float remaining = Mathf.Max(0.0f,
                nextFrameTime - Time.unscaledTime);
            float progress = 1.0f - Mathf.Clamp01(
                remaining / Mathf.Max(0.001f, interval));
            suppressSlider = true;
            timelineSlider.SetValueWithoutNotify(currentFrame + progress);
            suppressSlider = false;
        }

        private int ContiguousEventStart(int frameIndex)
        {
            int start = Mathf.Clamp(frameIndex, 0,
                Mathf.Max(0, dataset.TimeCount - 1));
            while (start > 0 && FramesAreHourlyContinuous(start - 1, start))
                start--;
            return start;
        }

        private bool FramesAreHourlyContinuous(int current, int next)
        {
            if (metadata == null || metadata.timeHKT == null ||
                current < 0 || next < 0 || current >= metadata.timeHKT.Length ||
                next >= metadata.timeHKT.Length)
                return next == current + 1;
            if (!DateTimeOffset.TryParse(metadata.timeHKT[current], out DateTimeOffset from) ||
                !DateTimeOffset.TryParse(metadata.timeHKT[next], out DateTimeOffset to))
                return next == current + 1;
            double hours = (to - from).TotalHours;
            return hours > 0.5 && hours < 1.5;
        }

        private void LateUpdate()
        {
            if (Time.frameCount < nextRendererSweep)
                return;
            nextRendererSweep = Time.frameCount + 15;
            HideSyntheticVolume();
        }

        private void OnDestroy()
        {
            if (xytCompanion != null)
                Destroy(xytCompanion);
            if (surfaceMesh != null)
                Destroy(surfaceMesh);
            if (surfaceRoot != null)
                Destroy(surfaceRoot);
            if (timelineCanvas != null)
                Destroy(timelineCanvas);
        }

        private void BuildSurface()
        {
            if (surfaceRoot != null)
                Destroy(surfaceRoot);
            surfaceRoot = new GameObject("Hong Kong Geographic Surface");
            surfaceRoot.transform.SetParent(transform, false);
            // The Field floor is y=-0.84. Keep the map four centimetres above
            // it to prevent z-fighting, with the data surface rising from that
            // geographic ground plane.
            surfaceRoot.transform.localPosition = GroundLayerPosition;

            GameObject map = GameObject.CreatePrimitive(PrimitiveType.Quad);
            map.name = "Hong Kong OpenStreetMap Basemap";
            map.transform.SetParent(surfaceRoot.transform, false);
            map.transform.localPosition = Vector3.zero;
            map.transform.localRotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            map.transform.localScale = new Vector3(mapWidth, mapDepth, 1.0f);
            Collider collider = map.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
            Material mapMaterial = new Material(Shader.Find("Unlit/Texture"));
            mapMaterial.mainTexture = Resources.Load<Texture2D>("HongKongOSM");
            map.GetComponent<Renderer>().material = mapMaterial;

            GameObject surface = new GameObject("Animated Hong Kong Surface");
            surface.transform.SetParent(surfaceRoot.transform, false);
            surfaceMesh = new Mesh { name = "For_VR Hong Kong Surface Mesh" };
            surfaceMesh.MarkDynamic();
            int count = Mathf.Max(1, usingExactGeographicMesh
                ? geographicCoordinates.Length
                : dataset.DimX * dataset.DimY);
            if (count > 65535)
                surfaceMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            vertices = new Vector3[count];
            colors = new Color32[count];
            surface.AddComponent<MeshFilter>().sharedMesh = surfaceMesh;
            Material surfaceMaterial = new Material(Shader.Find("Sprites/Default"));
            surfaceMaterial.color = Color.white;
            surfaceMaterial.renderQueue = 3100;
            surface.AddComponent<MeshRenderer>().material = surfaceMaterial;
        }

        private void BuildTimeline()
        {
            GameObject existing = GameObject.Find("For_VR Surface Timeline");
            if (existing != null)
                Destroy(existing);
            timelineCanvas = new GameObject("For_VR Surface Timeline",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            timelineCanvas.transform.SetParent(transform, false);
            Canvas canvas = timelineCanvas.GetComponent<Canvas>();
            canvas.renderMode = UnityEngine.RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvas.sortingOrder = 350;
            RectTransform canvasRect = timelineCanvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(1200.0f, TimelinePanelHeight);
            float frontSign = -1.0f;
            if (Camera.main != null)
            {
                Vector3 localCamera = transform.InverseTransformPoint(
                    Camera.main.transform.position);
                frontSign = localCamera.z >= 0.0f ? 1.0f : -1.0f;
            }
            // Its top edge touches the Field ground edge while the panel stays
            // immediately in front of the Field.
            float panelHalfHeight = TimelinePanelHeight * TimelineCanvasScale * 0.5f;
            canvasRect.localPosition = new Vector3(0.0f,
                GroundLayerPosition.y - panelHalfHeight,
                frontSign * 0.72f);
            // A world-space Canvas renders its readable face opposite its
            // Transform.forward direction. Pointing forward at the camera made
            // the viewer see the back of every glyph (horizontally mirrored).
            canvasRect.localRotation = Quaternion.LookRotation(
                new Vector3(0.0f, 0.0f, -frontSign), Vector3.up);
            canvasRect.localScale = Vector3.one * TimelineCanvasScale;
            CanvasScaler scaler = timelineCanvas.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 32.0f;

            RectTransform panel = CreateRect("Panel", timelineCanvas.transform);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(1200.0f, TimelinePanelHeight);
            Image background = panel.gameObject.AddComponent<Image>();
            background.color = new Color(0.012f, 0.050f, 0.072f, 0.94f);
            Outline outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.0f, 0.85f, 0.92f, 0.85f);
            outline.effectDistance = new Vector2(2, -2);

            datasetText = CreateText(panel, dataset.Name.Replace('_', ' '), 48,
                new Vector2(24, 150), new Vector2(430, 46), TextAnchor.MiddleLeft);
            timeText = CreateText(panel, "LOADING TIME...", 44,
                new Vector2(454, 150), new Vector2(722, 46), TextAnchor.MiddleRight);
            geographicText = CreateText(panel, GeographicSummary(), 30,
                new Vector2(24, 92), new Vector2(1152, 56), TextAnchor.MiddleLeft);
            geographicText.color = new Color(0.55f, 0.93f, 0.98f);
            // Once the Canvas always faces the viewer, local X is consistently
            // left-to-right on screen. Keep the rail on the left and the two
            // controls on the right for both sides of the Field.
            const float startButtonX = 844.0f;
            const float speedButtonX = 1010.0f;
            const float sliderX = 24.0f;
            Button play = CreateButton(panel, "START", new Vector2(startButtonX, 30), new Vector2(150, 66), () =>
            {
                if (playing)
                {
                    SetPlaying(false);
                    return;
                }
                ShowFrame(0, true);
                SetPlaying(true);
            });
            playText = play.GetComponentInChildren<TextMeshProUGUI>();
            Button speed = CreateButton(panel, "SPEED 1x", new Vector2(speedButtonX, 30), new Vector2(166, 66), () =>
            {
                playbackSpeedIndex = (playbackSpeedIndex + 1) % PlaybackSpeeds.Length;
                UpdateSpeedText();
                if (playing)
                    nextFrameTime = Time.unscaledTime + CurrentPlaybackInterval();
            });
            speedText = speed.GetComponentInChildren<TextMeshProUGUI>();
            UpdateSpeedText();

            GameObject sliderObject = new GameObject("Timeline", typeof(RectTransform), typeof(Slider));
            sliderObject.transform.SetParent(panel, false);
            RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
            sliderRect.anchorMin = sliderRect.anchorMax = new Vector2(0.0f, 0.0f);
            sliderRect.pivot = new Vector2(0.0f, 0.0f);
            sliderRect.anchoredPosition = new Vector2(sliderX, 39);
            sliderRect.sizeDelta = new Vector2(800, 48);
            Image sliderBackground = sliderObject.AddComponent<Image>();
            sliderBackground.color = new Color(0.08f, 0.18f, 0.22f, 1.0f);
            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(sliderObject.transform, false);
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0, 0.18f);
            fillRect.anchorMax = new Vector2(1, 0.82f);
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
            fill.GetComponent<Image>().color = new Color(0.0f, 0.78f, 0.86f, 1.0f);
            GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(sliderObject.transform, false);
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(18, 36);
            handle.GetComponent<Image>().color = new Color(1.0f, 0.67f, 0.12f, 1.0f);
            timelineSlider = sliderObject.GetComponent<Slider>();
            timelineSliderRect = sliderRect;
            timelineSlider.fillRect = fillRect;
            timelineSlider.handleRect = handleRect;
            timelineSlider.targetGraphic = handle.GetComponent<Image>();
            timelineSlider.minValue = 0;
            timelineSlider.maxValue = Mathf.Max(0, dataset.TimeCount - 1);
            timelineSlider.wholeNumbers = false;
            timelineSlider.direction = Slider.Direction.LeftToRight;
            timelineSlider.onValueChanged.AddListener(value =>
            {
                if (suppressSlider)
                    return;
                SetPlaying(false);
                ShowFrame(Mathf.RoundToInt(value), true);
            });
            sliderObject.layer = 5;
            BoxCollider sliderCollider = sliderObject.AddComponent<BoxCollider>();
            sliderCollider.isTrigger = true;
            sliderCollider.size = new Vector3(800.0f, 66.0f, 20.0f);
            sliderCollider.center = new Vector3(400.0f, 24.0f, 0.0f);
            sliderObject.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked = () =>
                ScrubFromRay(CurrentPointerRay());
            BuildEventTimelineScale(panel, sliderX, 39.0f, 800.0f, 48.0f);
            rayInteractor = FindObjectOfType<VolumeSTCubeQuestRayInteractor>();
        }

        private void BuildEventTimelineScale(Transform panel, float sliderX,
            float sliderY, float sliderWidth, float sliderHeight)
        {
            if (dataset == null || dataset.RawPaths == null ||
                dataset.RawPaths.Length == 0)
                return;

            List<TimelineEventRange> ranges = new List<TimelineEventRange>();
            Regex pattern = new Regex(@"event_(\d+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            for (int frame = 0; frame < dataset.RawPaths.Length; frame++)
            {
                Match match = pattern.Match(Path.GetFileName(
                    dataset.RawPaths[frame]));
                int number = match.Success
                    ? int.Parse(match.Groups[1].Value) : 1;
                if (ranges.Count == 0 ||
                    ranges[ranges.Count - 1].number != number)
                    ranges.Add(new TimelineEventRange
                    {
                        number = number,
                        first = frame,
                        last = frame
                    });
                else
                    ranges[ranges.Count - 1].last = frame;
            }

            float total = Mathf.Max(1.0f, dataset.RawPaths.Length);
            for (int index = 0; index < ranges.Count; index++)
            {
                TimelineEventRange range = ranges[index];
                float start = range.first / total;
                float end = (range.last + 1.0f) / total;
                float x = sliderX + start * sliderWidth;
                float width = Mathf.Max(2.0f, (end - start) * sliderWidth);

                RectTransform band = CreateRect("Event " + range.number +
                    " timeline band", panel);
                band.anchorMin = band.anchorMax = Vector2.zero;
                band.pivot = Vector2.zero;
                band.anchoredPosition = new Vector2(x, sliderY);
                band.sizeDelta = new Vector2(width, sliderHeight);
                Image bandImage = band.gameObject.AddComponent<Image>();
                bandImage.color = index % 2 == 0
                    ? new Color(0.10f, 0.78f, 0.95f, 0.13f)
                    : new Color(0.36f, 0.48f, 1.0f, 0.11f);
                bandImage.raycastTarget = false;

                RectTransform tick = CreateRect("Event " + range.number +
                    " boundary", panel);
                tick.anchorMin = tick.anchorMax = Vector2.zero;
                tick.pivot = new Vector2(0.5f, 0.0f);
                tick.anchoredPosition = new Vector2(x, sliderY - 2.0f);
                tick.sizeDelta = new Vector2(3.0f, sliderHeight + 8.0f);
                Image tickImage = tick.gameObject.AddComponent<Image>();
                tickImage.color = new Color(0.72f, 0.96f, 1.0f, 0.92f);
                tickImage.raycastTarget = false;

                string dateRange = EventDateRange(range.first, range.last);
                TextMeshProUGUI label = CreateText(panel,
                    "E" + range.number + "  " + dateRange, 19,
                    new Vector2(x + 2.0f, 5.0f),
                    new Vector2(Mathf.Max(18.0f, width - 4.0f), 28.0f),
                    TextAnchor.MiddleCenter);
                label.fontSizeMin = 11.0f;
                label.fontSizeMax = 19.0f;
                label.color = new Color(0.74f, 0.94f, 1.0f, 1.0f);
            }

            RectTransform finalTick = CreateRect("Final event boundary", panel);
            finalTick.anchorMin = finalTick.anchorMax = Vector2.zero;
            finalTick.pivot = new Vector2(0.5f, 0.0f);
            finalTick.anchoredPosition = new Vector2(sliderX + sliderWidth,
                sliderY - 2.0f);
            finalTick.sizeDelta = new Vector2(3.0f, sliderHeight + 8.0f);
            Image finalTickImage = finalTick.gameObject.AddComponent<Image>();
            finalTickImage.color = new Color(0.72f, 0.96f, 1.0f, 0.92f);
            finalTickImage.raycastTarget = false;
        }

        private string EventDateRange(int first, int last)
        {
            if (metadata == null || metadata.timeHKT == null ||
                first < 0 || last >= metadata.timeHKT.Length)
                return (first + 1) + "-" + (last + 1);
            if (!DateTimeOffset.TryParse(metadata.timeHKT[first],
                    out DateTimeOffset from) ||
                !DateTimeOffset.TryParse(metadata.timeHKT[last],
                    out DateTimeOffset to))
                return (first + 1) + "-" + (last + 1);
            return from.ToString("MM/dd") + "-" + to.ToString("MM/dd");
        }

        private void UpdateTimelineScrubbing()
        {
            if (timelineSliderRect == null || dataset == null)
                return;
#if UNITY_EDITOR
            if (Input.GetMouseButton(0))
            {
                Camera camera = Camera.main;
                if (camera != null)
                    ScrubFromRay(camera.ScreenPointToRay(Input.mousePosition));
            }
#endif
            if (rayInteractor == null)
                rayInteractor = FindObjectOfType<VolumeSTCubeQuestRayInteractor>();
            if (rayInteractor == null)
                return;
            if (rayInteractor.TriggerPressed)
                questScrubbing = PointerInsideSlider(rayInteractor.PointerRay);
            if (questScrubbing && rayInteractor.TriggerHeld)
                ScrubFromRay(rayInteractor.PointerRay);
            if (rayInteractor.TriggerReleased)
                questScrubbing = false;
        }

        private Ray CurrentPointerRay()
        {
            if (rayInteractor != null)
                return rayInteractor.PointerRay;
            Camera camera = Camera.main;
            return camera != null
                ? camera.ScreenPointToRay(Input.mousePosition)
                : new Ray(transform.position, transform.forward);
        }

        private bool PointerInsideSlider(Ray ray)
        {
            if (timelineSliderRect == null)
                return false;
            Plane plane = new Plane(timelineSliderRect.forward,
                timelineSliderRect.position);
            if (!plane.Raycast(ray, out float distance))
                return false;
            Vector3 local = timelineSliderRect.InverseTransformPoint(
                ray.GetPoint(distance));
            Rect rect = timelineSliderRect.rect;
            return rect.Contains(new Vector2(local.x, local.y));
        }

        private void ScrubFromRay(Ray ray)
        {
            if (!PointerInsideSlider(ray))
                return;
            Plane plane = new Plane(timelineSliderRect.forward,
                timelineSliderRect.position);
            if (!plane.Raycast(ray, out float distance))
                return;
            Vector3 local = timelineSliderRect.InverseTransformPoint(
                ray.GetPoint(distance));
            Rect rect = timelineSliderRect.rect;
            float normalized = Mathf.InverseLerp(rect.xMin, rect.xMax, local.x);
            if (timelineSlider != null &&
                timelineSlider.direction == Slider.Direction.RightToLeft)
                normalized = 1.0f - normalized;
            SetPlaying(false);
            ShowFrame(Mathf.RoundToInt(normalized *
                Mathf.Max(0, dataset.TimeCount - 1)), true);
        }

        private void SetPlaying(bool value)
        {
            playing = value;
            nextFrameTime = value
                ? Time.unscaledTime + CurrentPlaybackInterval()
                : Time.unscaledTime;
            if (playText != null)
                playText.text = playing ? "PAUSE" : "START";
        }

        private void UpdateSpeedText()
        {
            if (speedText == null)
                return;
            speedText.text = "SPEED " +
                PlaybackSpeeds[playbackSpeedIndex].ToString("0") + "x";
        }

        private void UpdateTimelineText(float minimum, float physicalRange)
        {
            if (timeText == null || dataset == null)
                return;
            string timestamp = metadata != null && metadata.timeHKT != null &&
                currentFrame < metadata.timeHKT.Length
                ? metadata.timeHKT[currentFrame]
                : dataset.GetTimeLabel(currentFrame);
            // Keep the live readout concise. The former centre-value suffix
            // forced the whole line down to an unreadable size.
            timeText.text = "HOUR " + (currentFrame + 1) + " / " +
                dataset.TimeCount + "    " + timestamp;
            if (geographicText != null)
            {
                int validCount = 0;
                double sum = 0.0;
                float frameMinimum = float.PositiveInfinity;
                float frameMaximum = float.NegativeInfinity;
                for (int index = 0; index < frameValues.Length; index++)
                {
                    if (frameValues[index] <= 0)
                        continue;
                    float value = physicalFrameValues[index];
                    frameMinimum = Mathf.Min(frameMinimum, value);
                    frameMaximum = Mathf.Max(frameMaximum, value);
                    sum += value;
                    validCount++;
                }
                string unit = metadata != null && !string.IsNullOrWhiteSpace(metadata.unit)
                    ? metadata.unit : string.Empty;
                string statistics = validCount > 0
                    ? string.Format("FRAME  MIN {0:0.00} | MEAN {1:0.00} | MAX {2:0.00} {3}    SCALE {4:0.00}..{5:0.00} {3}",
                        frameMinimum, sum / validCount, frameMaximum, unit,
                        sharedPhysicalMinimum, sharedPhysicalMaximum)
                    : "FRAME  no valid samples";
                geographicText.text = statistics + " (PRED+GT)\n" + GeographicSummary();
            }
        }

        private string GeographicSummary()
        {
            GeographicBounds bounds = conversionManifest?.boundsEPSG4326;
            if (bounds == null)
                return "HONG KONG | EPSG:4326";
            return string.Format(
                (usingExactGeographicMesh ? "7364 NODES | 13445 FACES" : "96x64 RASTER FALLBACK") +
                " | WGS84 > MERCATOR | E {0:0.000}-{1:0.000} | N {2:0.000}-{3:0.000}",
                bounds.lonMin, bounds.lonMax, bounds.latMin, bounds.latMax);
        }

        private void LoadExactGeographicMesh(string unityRawRoot)
        {
            usingExactGeographicMesh = false;
            GeographicSurfaceMetadata surface = conversionManifest?.geographicSurface;
            if (surface == null || metadata == null || surface.nodeCount <= 0 ||
                surface.faceCount <= 0 || string.IsNullOrWhiteSpace(surface.coordinateFile) ||
                string.IsNullOrWhiteSpace(surface.faceFile) ||
                string.IsNullOrWhiteSpace(metadata.geographicValuesFile))
                return;

            string coordinatePath = Path.Combine(unityRawRoot,
                surface.coordinateFile.Replace('/', Path.DirectorySeparatorChar));
            string facePath = Path.Combine(unityRawRoot,
                surface.faceFile.Replace('/', Path.DirectorySeparatorChar));
            geographicValuesPath = Path.Combine(unityRawRoot,
                metadata.geographicValuesFile.Replace('/', Path.DirectorySeparatorChar));
            long expectedValues = (long)metadata.geographicFrameStrideBytes *
                dataset.TimeCount;
            if (!File.Exists(coordinatePath) || !File.Exists(facePath) ||
                !File.Exists(geographicValuesPath) ||
                metadata.geographicFrameStrideBytes < surface.nodeCount * 4 ||
                new FileInfo(coordinatePath).Length != surface.nodeCount * 8L ||
                new FileInfo(facePath).Length != surface.faceCount * 12L ||
                new FileInfo(geographicValuesPath).Length < expectedValues)
                return;

            geographicCoordinates = new Vector2[surface.nodeCount];
            using (BinaryReader reader = new BinaryReader(File.OpenRead(coordinatePath)))
                for (int index = 0; index < geographicCoordinates.Length; index++)
                    geographicCoordinates[index] = new Vector2(
                        reader.ReadSingle(), reader.ReadSingle());

            geographicTriangles = new int[surface.faceCount * 3];
            using (BinaryReader reader = new BinaryReader(File.OpenRead(facePath)))
                for (int index = 0; index < geographicTriangles.Length; index++)
                    geographicTriangles[index] = checked((int)reader.ReadUInt32());
            for (int index = 0; index < geographicTriangles.Length; index++)
                if (geographicTriangles[index] < 0 ||
                    geographicTriangles[index] >= geographicCoordinates.Length)
                    throw new InvalidDataException("Geographic face index is outside the node array.");
            usingExactGeographicMesh = true;
        }

        private void UpdateProjectedMapDimensions()
        {
            GeographicBounds bounds = conversionManifest?.boundsEPSG4326;
            if (bounds == null)
                return;
            double longitudeSpan = Math.Abs(bounds.lonMax - bounds.lonMin) * Math.PI / 180.0;
            double mercatorSpan = Math.Abs(MercatorY(bounds.latMax) - MercatorY(bounds.latMin));
            mapDepth = mapWidth / Mathf.Clamp((float)(longitudeSpan /
                Math.Max(1.0e-12, mercatorSpan)), 0.8f, 2.2f);
        }

        private byte[] ReadFrameBytes(int frameIndex, int sampleCount)
        {
            string path = dataset.RawPaths[frameIndex];
            byte[] bytes = new byte[sampleCount];
            using (FileStream stream = new FileStream(path, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite))
            {
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0)
                        break;
                    offset += read;
                }
            }
            return bytes;
        }

        private float[] ReadGeographicPhysicalFrame(int frameIndex, int sampleCount)
        {
            int byteCount = checked(sampleCount * 4);
            byte[] bytes = new byte[byteCount];
            using (FileStream stream = new FileStream(geographicValuesPath,
                FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.Position = (long)frameIndex *
                    metadata.geographicFrameStrideBytes;
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0)
                        break;
                    offset += read;
                }
            }
            if (!BitConverter.IsLittleEndian)
                for (int index = 0; index < bytes.Length; index += 4)
                    Array.Reverse(bytes, index, 4);
            float[] values = new float[sampleCount];
            Buffer.BlockCopy(bytes, 0, values, 0, byteCount);
            return values;
        }

        private static double MercatorY(double latitudeDegrees)
        {
            double latitude = Math.Max(-85.05112878,
                Math.Min(85.05112878, latitudeDegrees)) * Math.PI / 180.0;
            return Math.Log(Math.Tan(Math.PI * 0.25 + latitude * 0.5));
        }

        private void LoadMetadata()
        {
            conversionManifest = new ConversionManifest
            {
                boundsEPSG4326 = new GeographicBounds
                {
                    lonMin = 113.650220f,
                    lonMax = 114.659994f,
                    latMin = 22.0300455f,
                    latMax = 22.6998139f
                }
            };
            try
            {
                string root = Directory.GetParent(dataset.DirectoryPath)?.FullName;
                string path = Path.Combine(root ?? string.Empty, "conversion_manifest.json");
                ConversionManifest manifest = JsonUtility.FromJson<ConversionManifest>(
                    File.ReadAllText(path));
                if (manifest != null)
                    conversionManifest = manifest;
                if (conversionManifest.boundsEPSG4326 == null)
                    conversionManifest.boundsEPSG4326 = new GeographicBounds
                    {
                        lonMin = 113.650220f,
                        lonMax = 114.659994f,
                        latMin = 22.0300455f,
                        latMax = 22.6998139f
                    };
                if (manifest?.datasets == null)
                    return;
                for (int index = 0; index < manifest.datasets.Length; index++)
                    if (string.Equals(manifest.datasets[index].name, dataset.Name,
                        StringComparison.OrdinalIgnoreCase))
                        metadata = manifest.datasets[index];
                if (metadata != null)
                {
                    sharedPhysicalMinimum = metadata.physicalMinimum;
                    sharedPhysicalMaximum = metadata.physicalMaximum;
                    for (int index = 0; index < manifest.datasets.Length; index++)
                    {
                        DatasetMetadata candidate = manifest.datasets[index];
                        if (!string.Equals(candidate.channel, metadata.channel,
                            StringComparison.OrdinalIgnoreCase))
                            continue;
                        sharedPhysicalMinimum = Mathf.Min(sharedPhysicalMinimum,
                            candidate.physicalMinimum);
                        sharedPhysicalMaximum = Mathf.Max(sharedPhysicalMaximum,
                            candidate.physicalMaximum);
                    }
                }
                LoadExactGeographicMesh(root);
                UpdateProjectedMapDimensions();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("For_VR surface metadata unavailable: " + exception.Message);
            }
        }

        private void HideSyntheticVolume()
        {
            if (hiddenVolumeRoot == null)
                return;
            Renderer[] renderers = hiddenVolumeRoot.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
                if (renderers[index] != null)
                    renderers[index].enabled = false;
        }

        private static int[] BuildValidTriangles(int[] values, int width, int height)
        {
            int[] buffer = new int[Mathf.Max(0, (width - 1) * (height - 1) * 6)];
            int count = 0;
            for (int y = 0; y < height - 1; y++)
            for (int x = 0; x < width - 1; x++)
            {
                int a = x + y * width;
                int b = a + 1;
                int c = a + width;
                int d = c + 1;
                if (values[a] == 0 || values[b] == 0 || values[c] == 0 || values[d] == 0)
                    continue;
                buffer[count++] = a; buffer[count++] = d; buffer[count++] = c;
                buffer[count++] = a; buffer[count++] = b; buffer[count++] = d;
            }
            if (count == buffer.Length)
                return buffer;
            int[] result = new int[count];
            Array.Copy(buffer, result, count);
            return result;
        }

        private static int[] BuildValidGeographicTriangles(int[] values, int[] faces)
        {
            if (faces == null || faces.Length == 0)
                return Array.Empty<int>();
            int[] buffer = new int[faces.Length];
            int count = 0;
            for (int index = 0; index + 2 < faces.Length; index += 3)
            {
                int a = faces[index];
                int b = faces[index + 1];
                int c = faces[index + 2];
                if (values[a] <= 0 || values[b] <= 0 || values[c] <= 0)
                    continue;
                // The source topology is defined in longitude/latitude. Unity's
                // ground plane uses X/Z, so swap B/C to retain an upward face.
                buffer[count++] = a;
                buffer[count++] = c;
                buffer[count++] = b;
            }
            if (count == buffer.Length)
                return buffer;
            int[] result = new int[count];
            Array.Copy(buffer, result, count);
            return result;
        }

        private static Color32 EvaluateSurfaceColor(float t)
        {
            t = Mathf.Clamp01(t);
            Color color;
            if (t < 0.25f)
                color = Color.Lerp(new Color(0.05f, 0.16f, 0.52f), new Color(0.0f, 0.78f, 0.88f), t * 4.0f);
            else if (t < 0.5f)
                color = Color.Lerp(new Color(0.0f, 0.78f, 0.88f), new Color(0.42f, 0.88f, 0.28f), (t - 0.25f) * 4.0f);
            else if (t < 0.75f)
                color = Color.Lerp(new Color(0.42f, 0.88f, 0.28f), new Color(1.0f, 0.78f, 0.10f), (t - 0.5f) * 4.0f);
            else
                color = Color.Lerp(new Color(1.0f, 0.78f, 0.10f), new Color(0.86f, 0.08f, 0.12f), (t - 0.75f) * 4.0f);
            color.a = 0.82f;
            return color;
        }

        private static Color32 EvaluateSignedSurfaceColor(float value,
            float absoluteMaximum)
        {
            float signed = Mathf.Clamp(value /
                Mathf.Max(0.0001f, absoluteMaximum), -1.0f, 1.0f);
            // Water level values often cluster close to zero. A white neutral
            // disappeared against the map and made both water datasets look
            // empty. Keep zero saturated and visible while retaining a clear
            // negative/positive diverging scale.
            Color neutral = new Color(0.04f, 0.78f, 0.68f, 0.92f);
            Color color = signed < 0.0f
                ? Color.Lerp(neutral, new Color(0.03f, 0.16f, 0.70f, 0.92f), -signed)
                : Color.Lerp(neutral, new Color(0.96f, 0.22f, 0.05f, 0.92f), signed);
            return color;
        }

        private static Color32 EvaluateSnapshotSurfaceColor(float t)
        {
            t = Mathf.Clamp01(t);
            Color color;
            if (t < 0.34f)
                color = Color.Lerp(new Color(0.20f, 0.02f, 0.48f),
                    new Color(0.72f, 0.05f, 0.52f), t / 0.34f);
            else if (t < 0.68f)
                color = Color.Lerp(new Color(0.72f, 0.05f, 0.52f),
                    new Color(1.0f, 0.38f, 0.04f), (t - 0.34f) / 0.34f);
            else
                color = Color.Lerp(new Color(1.0f, 0.38f, 0.04f),
                    new Color(1.0f, 0.94f, 0.16f), (t - 0.68f) / 0.32f);
            color.a = 0.92f;
            return color;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            return obj.GetComponent<RectTransform>();
        }

        private static TextMeshProUGUI CreateText(Transform parent, string value, int size,
            Vector2 position, Vector2 dimensions, TextAnchor alignment)
        {
            RectTransform rect = CreateRect("Text", parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 0);
            rect.pivot = new Vector2(0, 0);
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null)
                text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.alignment = ToTmpAlignment(alignment);
            text.color = new Color(0.88f, 0.97f, 1.0f);
            text.text = value;
            text.enableAutoSizing = true;
            text.fontSizeMin = 28.0f;
            text.fontSizeMax = size;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static TextAlignmentOptions ToTmpAlignment(TextAnchor alignment)
        {
            switch (alignment)
            {
                case TextAnchor.MiddleLeft:
                    return TextAlignmentOptions.Left;
                case TextAnchor.MiddleRight:
                    return TextAlignmentOptions.Right;
                case TextAnchor.UpperLeft:
                    return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter:
                    return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight:
                    return TextAlignmentOptions.TopRight;
                case TextAnchor.LowerLeft:
                    return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter:
                    return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight:
                    return TextAlignmentOptions.BottomRight;
                default:
                    return TextAlignmentOptions.Center;
            }
        }

        private Button CreateButton(Transform parent, string label,
            Vector2 position, Vector2 dimensions, UnityEngine.Events.UnityAction action)
        {
            RectTransform rect = CreateRect(label + " Button", parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 0);
            rect.pivot = new Vector2(0, 0);
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.08f, 0.30f, 0.36f, 1.0f);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => InvokeControlOnce(action));
            rect.gameObject.layer = 5;
            BoxCollider collider = rect.gameObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(dimensions.x, dimensions.y, 16.0f);
            collider.center = new Vector3(
                dimensions.x * 0.5f, dimensions.y * 0.5f, 0.0f);
            rect.gameObject.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked = () =>
                InvokeControlOnce(action);
            TextMeshProUGUI text = CreateText(rect, label, 44, Vector2.zero,
                dimensions, TextAnchor.MiddleCenter);
            text.fontSizeMin = 28.0f;
            text.fontSizeMax = 44.0f;
            text.raycastTarget = false;
            return button;
        }

        private void InvokeControlOnce(UnityEngine.Events.UnityAction action)
        {
            // A desktop click can arrive once through OnMouseDown and again
            // through Button.onClick on release. Treat both as one action.
            if (lastControlActionFrame == Time.frameCount ||
                Time.unscaledTime - lastControlActionTime < 0.16f)
                return;
            lastControlActionFrame = Time.frameCount;
            lastControlActionTime = Time.unscaledTime;
            action?.Invoke();
        }
    }
}
