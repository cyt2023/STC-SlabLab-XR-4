using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>
    /// End-to-end XYZ+T exploration surface:
    /// RAW dataset -> Z plane -> time step -> XY CSV -> MatPlotAgent chart.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VolumeSTCubeMatPlotWorkbench : MonoBehaviour
    {
        private enum WorkflowStage
        {
            SelectZ,
            SelectTime,
            Prompt,
            Result
        }

        [Header("Data")]
        public string dataRootOverride;
        public bool showOnStart = true;

        [Header("MatPlotAgent")]
        public string matPlotBaseUrl = "http://127.0.0.1:8010";
        public int requestTimeoutSeconds = 120;

        private readonly List<VolumeSTCubeSliceDataset> datasets = new List<VolumeSTCubeSliceDataset>();
        private VolumeSTCubeSliceDataset selectedDataset;
        private VolumeSTCubeView currentView;
        private int selectedTimeIndex;
        private int selectedZIndex;
        private Texture2D[] timePreviews = new Texture2D[0];
        private Texture2D[] zPreviews = new Texture2D[0];
        private Texture2D matPlotImage;
        private Vector2 timeScroll;
        private Vector2 zScroll;
        private Rect windowRect;
        private bool windowVisible;
        private bool matPlotRunning;
        private float matPlotProgress;
        private string statusMessage = "Choose a RAW XYZ+T dataset.";
        private string chartPrompt = "Create a heatmap of value over the XY grid with a clear color bar.";
        private string resolvedDataRoot;
        private string editableDataRoot;
        private string editableMatPlotUrl;
        private string lastExportedCsv;
        private WorkflowStage workflowStage = WorkflowStage.SelectZ;

        private GUIStyle titleStyle;
        private GUIStyle sectionStyle;
        private GUIStyle smallStyle;
        private GUIStyle wrappedStyle;

        private void Awake()
        {
            windowVisible = showOnStart;
            resolvedDataRoot = ResolveDefaultDataRoot();
            editableDataRoot = resolvedDataRoot;
            editableMatPlotUrl = matPlotBaseUrl;
        }

        private void Start()
        {
            RefreshDatasets();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
                windowVisible = !windowVisible;
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (!windowVisible)
            {
                if (GUI.Button(new Rect(12, 12, 190, 34), "Open STC → MatPlot (F8)"))
                    windowVisible = true;
                return;
            }

            float width = Mathf.Max(1100.0f, Screen.width - 24.0f);
            float height = Mathf.Max(650.0f, Screen.height - 24.0f);
            width = Mathf.Min(width, Screen.width - 8.0f);
            height = Mathf.Min(height, Screen.height - 8.0f);
            if (windowRect.width <= 0.0f || windowRect.height <= 0.0f)
                windowRect = new Rect(4.0f, 4.0f, width, height);
            else
            {
                windowRect.width = width;
                windowRect.height = height;
            }

            windowRect = GUI.Window(GetInstanceID(), windowRect, DrawWindow, "VolumeSTCube XY Slice Workbench");
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            DrawToolbar();
            DrawDatasetChooser();

            if (selectedDataset == null)
            {
                GUILayout.Space(16.0f);
                GUILayout.Label(
                    "Select chlorophyll, NO3, salt, or another compatible RAW folder. " +
                    "Each .raw file must have a matching .raw.ini file.",
                    wrappedStyle);
                GUILayout.FlexibleSpace();
                DrawStatus();
                GUILayout.EndVertical();
                GUI.DragWindow(new Rect(0, 0, windowRect.width, 24));
                return;
            }

            GUILayout.Space(4.0f);
            GUILayout.Label(
                selectedDataset.Name + "   |   XYZ " + selectedDataset.DimX + " × " +
                selectedDataset.DimY + " × " + selectedDataset.DimZ + "   |   " +
                selectedDataset.TimeCount + " time steps   |   " +
                selectedDataset.GetTimeLabel(selectedTimeIndex) + "   |   " +
                (selectedZIndex >= 0 ? "z=" + selectedZIndex : "Z not selected"),
                sectionStyle);

            DrawWorkflowProgress();
            float contentHeight = Mathf.Max(420.0f, windowRect.height - 226.0f);
            switch (workflowStage)
            {
                case WorkflowStage.SelectZ:
                    DrawZPanel(contentHeight);
                    break;
                case WorkflowStage.SelectTime:
                    DrawTimePanel(contentHeight);
                    break;
                case WorkflowStage.Prompt:
                    DrawAnalysisPanel(contentHeight);
                    break;
                case WorkflowStage.Result:
                    DrawResultPanel(contentHeight);
                    break;
            }
            DrawStatus();
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, windowRect.width, 24));
        }

        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("STC → XY → MatPlot", titleStyle, GUILayout.Width(245.0f));
            GUILayout.Label("Data root", GUILayout.Width(64.0f));
            editableDataRoot = GUILayout.TextField(editableDataRoot ?? string.Empty, GUILayout.MinWidth(260.0f));
            if (GUILayout.Button("Refresh", GUILayout.Width(72.0f)))
            {
                resolvedDataRoot = editableDataRoot.Trim();
                RefreshDatasets();
            }
            if (GUILayout.Button("Hide (F8)", GUILayout.Width(84.0f)))
                windowVisible = false;
            GUILayout.EndHorizontal();
        }

        private void DrawDatasetChooser()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Dataset", sectionStyle, GUILayout.Width(78.0f));
            if (datasets.Count == 0)
            {
                GUILayout.Label("No compatible RAW folders found under " + resolvedDataRoot, wrappedStyle);
            }
            else
            {
                for (int i = 0; i < datasets.Count; i++)
                {
                    Color previous = GUI.backgroundColor;
                    if (datasets[i] == selectedDataset)
                        GUI.backgroundColor = new Color(0.2f, 0.78f, 1.0f, 1.0f);
                    if (GUILayout.Button(datasets[i].Name, GUILayout.Width(120.0f), GUILayout.Height(28.0f)))
                        LoadDataset(i);
                    GUI.backgroundColor = previous;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawWorkflowProgress()
        {
            string[] labels = { "1  Choose Z", "2  Choose time", "3  Describe chart", "4  View result" };
            int active = (int)workflowStage;
            GUILayout.BeginHorizontal(GUI.skin.box, GUILayout.Height(34.0f));
            for (int i = 0; i < labels.Length; i++)
            {
                Color previous = GUI.backgroundColor;
                if (i == active)
                    GUI.backgroundColor = new Color(0.2f, 0.78f, 1.0f, 1.0f);
                GUI.enabled = i <= active && !matPlotRunning;
                if (GUILayout.Button(labels[i], GUILayout.ExpandWidth(true), GUILayout.Height(26.0f)))
                    workflowStage = (WorkflowStage)i;
                GUI.enabled = true;
                GUI.backgroundColor = previous;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawZPanel(float height)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.Height(height));
            GUILayout.Label("Step 1 of 4 — Choose a Z layer", sectionStyle);
            GUILayout.Label(
                zPreviews.Length + " clearly separated XY layers from " + selectedDataset.GetTimeLabel(selectedTimeIndex) +
                ". Click one layer to continue.",
                smallStyle);
            zScroll = GUILayout.BeginScrollView(zScroll, GUILayout.ExpandHeight(true));
            int columns = Mathf.Max(3, Mathf.FloorToInt((windowRect.width - 52.0f) / 98.0f));
            for (int row = 0; row * columns < zPreviews.Length; row++)
            {
                GUILayout.BeginHorizontal();
                for (int column = 0; column < columns; column++)
                {
                    int zIndex = row * columns + column;
                    if (zIndex >= zPreviews.Length)
                    {
                        GUILayout.Space(90.0f);
                        continue;
                    }
                    DrawZCard(zIndex);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawZCard(int zIndex)
        {
            GUILayout.BeginVertical(GUILayout.Width(90.0f));
            Color previous = GUI.backgroundColor;
            if (zIndex == selectedZIndex)
                GUI.backgroundColor = new Color(1.0f, 0.72f, 0.18f, 1.0f);
            GUIContent content = zPreviews[zIndex] != null
                ? new GUIContent(zPreviews[zIndex], "Select z=" + zIndex)
                : new GUIContent("z=" + zIndex);
            if (GUILayout.Button(content, GUILayout.Width(86.0f), GUILayout.Height(62.0f)))
                SelectZ(zIndex);
            GUI.backgroundColor = previous;
            GUILayout.Label("z=" + zIndex, smallStyle, GUILayout.Width(86.0f));
            GUILayout.EndVertical();
        }

        private void DrawTimePanel(float height)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.Height(height));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Step 2 of 4 — Choose a time at z=" + selectedZIndex, sectionStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("← Choose another Z", GUILayout.Width(170.0f), GUILayout.Height(26.0f)))
                workflowStage = WorkflowStage.SelectZ;
            GUILayout.EndHorizontal();
            GUILayout.Label(
                timePreviews.Length + " time-ordered XY previews. Click one time card to load its full 3D volume and continue.",
                smallStyle);
            timeScroll = GUILayout.BeginScrollView(timeScroll, GUILayout.ExpandHeight(true));
            int columns = Mathf.Max(3, Mathf.FloorToInt((windowRect.width - 52.0f) / 150.0f));
            for (int row = 0; row * columns < timePreviews.Length; row++)
            {
                GUILayout.BeginHorizontal();
                for (int column = 0; column < columns; column++)
                {
                    int timeIndex = row * columns + column;
                    if (timeIndex >= timePreviews.Length)
                    {
                        GUILayout.Space(142.0f);
                        continue;
                    }
                    DrawTimeCard(timeIndex);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawTimeCard(int timeIndex)
        {
            GUILayout.BeginVertical(GUILayout.Width(142.0f));
            Color previous = GUI.backgroundColor;
            if (timeIndex == selectedTimeIndex)
                GUI.backgroundColor = new Color(0.2f, 0.78f, 1.0f, 1.0f);
            GUIContent content = timePreviews[timeIndex] != null
                ? new GUIContent(timePreviews[timeIndex], "Load " + selectedDataset.GetTimeLabel(timeIndex))
                : new GUIContent(selectedDataset.GetTimeLabel(timeIndex));
            if (GUILayout.Button(content, GUILayout.Width(138.0f), GUILayout.Height(102.0f)))
                SelectTime(timeIndex);
            GUI.backgroundColor = previous;
            GUILayout.Label(selectedDataset.GetTimeLabel(timeIndex), smallStyle, GUILayout.Width(138.0f));
            GUILayout.EndVertical();
        }

        private void DrawAnalysisPanel(float height)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.Height(height));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Step 3 of 4 — Describe the 2D chart", sectionStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("← Choose another time", GUILayout.Width(180.0f), GUILayout.Height(26.0f)))
                workflowStage = WorkflowStage.SelectTime;
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "Selected: " + selectedDataset.Name + " / " + selectedDataset.GetTimeLabel(selectedTimeIndex) +
                " / z=" + selectedZIndex + ". MatPlotAgent receives only x, y, value from this slice.",
                wrappedStyle);
            GUILayout.Space(18.0f);
            GUILayout.Label("MatPlotAgent URL", smallStyle);
            editableMatPlotUrl = GUILayout.TextField(editableMatPlotUrl ?? string.Empty);
            GUILayout.Space(14.0f);
            GUILayout.Label("Chart request", smallStyle);
            chartPrompt = GUILayout.TextArea(chartPrompt ?? string.Empty, GUILayout.Height(180.0f));
            GUILayout.Space(14.0f);

            GUI.enabled = !matPlotRunning;
            if (GUILayout.Button(
                    matPlotRunning ? "Generating..." : "Extract XY and generate chart",
                    GUILayout.Height(46.0f)))
                StartMatPlotJob();
            GUI.enabled = true;

            if (matPlotRunning)
            {
                Rect progressRect = GUILayoutUtility.GetRect(100.0f, 18.0f, GUILayout.ExpandWidth(true));
                GUI.Box(progressRect, string.Empty);
                Rect fill = progressRect;
                fill.width *= Mathf.Clamp01(matPlotProgress);
                Color previous = GUI.color;
                GUI.color = new Color(0.15f, 0.72f, 1.0f, 1.0f);
                GUI.DrawTexture(fill, Texture2D.whiteTexture);
                GUI.color = previous;
            }

            if (!string.IsNullOrWhiteSpace(lastExportedCsv))
                GUILayout.Label("XY CSV: " + lastExportedCsv, smallStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
        }

        private void DrawResultPanel(float height)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.Height(height));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Step 4 of 4 — Generated 2D chart", sectionStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("← Edit request", GUILayout.Width(130.0f), GUILayout.Height(26.0f)))
                workflowStage = WorkflowStage.Prompt;
            if (GUILayout.Button("Choose time", GUILayout.Width(120.0f), GUILayout.Height(26.0f)))
                workflowStage = WorkflowStage.SelectTime;
            if (GUILayout.Button("Start from Z", GUILayout.Width(120.0f), GUILayout.Height(26.0f)))
                workflowStage = WorkflowStage.SelectZ;
            GUILayout.EndHorizontal();

            GUILayout.Label(
                selectedDataset.Name + " / " + selectedDataset.GetTimeLabel(selectedTimeIndex) +
                " / z=" + selectedZIndex,
                smallStyle);

            Rect imageRect = GUILayoutUtility.GetRect(
                100.0f,
                100.0f,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            if (matPlotImage != null)
                GUI.DrawTexture(imageRect, matPlotImage, ScaleMode.ScaleToFit, true);
            else
                GUI.Label(imageRect, "No generated chart is available. Return to the previous step and generate one.", wrappedStyle);
            GUILayout.EndVertical();
        }

        private void DrawStatus()
        {
            GUILayout.BeginHorizontal(GUI.skin.box, GUILayout.Height(28.0f));
            GUILayout.Label("Status: " + statusMessage, wrappedStyle);
            GUILayout.EndHorizontal();
        }

        private void RefreshDatasets()
        {
            if (currentView != null)
            {
                VolumeSTCubeAPI.DestroyView(currentView.viewId);
                currentView = null;
            }
            datasets.Clear();
            selectedDataset = null;
            workflowStage = WorkflowStage.SelectZ;
            ClearGeneratedChart();
            ReleasePreviewTextures();
            resolvedDataRoot = string.IsNullOrWhiteSpace(resolvedDataRoot)
                ? ResolveDefaultDataRoot()
                : Path.GetFullPath(resolvedDataRoot);
            editableDataRoot = resolvedDataRoot;
            try
            {
                datasets.AddRange(VolumeSTCubeRawSliceReader.DiscoverDatasets(resolvedDataRoot));
                statusMessage = datasets.Count > 0
                    ? "Found " + datasets.Count + " compatible XYZ+T dataset(s)."
                    : "No compatible RAW datasets found.";
            }
            catch (Exception exception)
            {
                statusMessage = "Dataset discovery failed: " + exception.Message;
            }
        }

        private void LoadDataset(int index)
        {
            if (index < 0 || index >= datasets.Count || matPlotRunning)
                return;

            VolumeSTCubeSliceDataset next = datasets[index];
            statusMessage = "Loading " + next.Name + " into the STC 3D renderer...";
            if (currentView != null)
            {
                VolumeSTCubeAPI.DestroyView(currentView.viewId);
                currentView = null;
            }

            VolumeSTCubeConfig config = VolumeSTCubeConfig.Default("matplot_slice_workbench");
            config.datasetName = next.Name;
            config.dataLayout = VolumeSTCubeDataLayout.XYZTimeSeries;
            config.showTimeline = false;
            config.timelineAutoPlay = false;
            config.opacity = 0.9f;
            if (!VolumeSTCubeAPI.TryCreateViewFromRawDirectory(
                    next.DirectoryPath,
                    config,
                    out currentView,
                    out string error))
            {
                statusMessage = error;
                return;
            }

            selectedDataset = next;
            selectedTimeIndex = 0;
            selectedZIndex = -1;
            workflowStage = WorkflowStage.SelectZ;
            ClearGeneratedChart();
            timeScroll = Vector2.zero;
            zScroll = Vector2.zero;
            RebuildZPreviews();
            DestroyTextures(timePreviews);
            timePreviews = new Texture2D[0];
            statusMessage = "Loaded " + next.Name + ". First choose one clearly separated Z layer.";
        }

        private void SelectTime(int timeIndex)
        {
            if (selectedDataset == null || timeIndex < 0 || timeIndex >= selectedDataset.TimeCount)
                return;
            selectedTimeIndex = timeIndex;
            if (currentView != null)
            {
                float minimum = timeIndex / (float)selectedDataset.TimeCount;
                float maximum = (timeIndex + 1) / (float)selectedDataset.TimeCount;
                currentView.ApplyTimeFilter(minimum, maximum);
            }
            RebuildZPreviews();
            workflowStage = WorkflowStage.Prompt;
            statusMessage = "Selected " + selectedDataset.GetTimeLabel(timeIndex) + ". Describe the 2D chart you want.";
        }

        private void SelectZ(int zIndex)
        {
            if (selectedDataset == null || zIndex < 0 || zIndex >= selectedDataset.DimZ)
                return;
            selectedZIndex = zIndex;
            RebuildTimePreviews();
            workflowStage = WorkflowStage.SelectTime;
            statusMessage = "Selected z=" + zIndex + ". Now choose one of the " + selectedDataset.TimeCount + " time steps.";
        }

        private void RebuildZPreviews()
        {
            DestroyTextures(zPreviews);
            if (selectedDataset == null)
            {
                zPreviews = new Texture2D[0];
                return;
            }

            zPreviews = new Texture2D[selectedDataset.DimZ];
            try
            {
                string rawPath = selectedDataset.RawPaths[selectedTimeIndex];
                string iniPath = selectedDataset.IniPaths[selectedTimeIndex];
                for (int z = 0; z < selectedDataset.DimZ; z++)
                {
                    VolumeSTCubeRawSlice slice = VolumeSTCubeRawSliceReader.ReadSlice(rawPath, iniPath, z);
                    zPreviews[z] = VolumeSTCubeRawSliceReader.CreatePreviewTexture(slice, 86, 62);
                }
            }
            catch (Exception exception)
            {
                statusMessage = "Could not build Z previews: " + exception.Message;
            }
        }

        private void RebuildTimePreviews()
        {
            DestroyTextures(timePreviews);
            if (selectedDataset == null)
            {
                timePreviews = new Texture2D[0];
                return;
            }

            timePreviews = new Texture2D[selectedDataset.TimeCount];
            try
            {
                for (int time = 0; time < selectedDataset.TimeCount; time++)
                {
                    VolumeSTCubeRawSlice slice = VolumeSTCubeRawSliceReader.ReadSlice(
                        selectedDataset.RawPaths[time],
                        selectedDataset.IniPaths[time],
                        selectedZIndex);
                    timePreviews[time] = VolumeSTCubeRawSliceReader.CreatePreviewTexture(slice, 138, 102);
                }
            }
            catch (Exception exception)
            {
                statusMessage = "Could not build time previews: " + exception.Message;
            }
        }

        private void StartMatPlotJob()
        {
            if (selectedDataset == null || matPlotRunning)
                return;

            try
            {
                string outputDirectory = Path.Combine(Application.temporaryCachePath, "VolumeSTCubeMatPlot");
                lastExportedCsv = VolumeSTCubeRawSliceReader.ExportCsv(
                    selectedDataset,
                    selectedTimeIndex,
                    selectedZIndex,
                    outputDirectory);
            }
            catch (Exception exception)
            {
                statusMessage = "XY extraction failed: " + exception.Message;
                return;
            }

            matPlotBaseUrl = string.IsNullOrWhiteSpace(editableMatPlotUrl)
                ? "http://127.0.0.1:8010"
                : editableMatPlotUrl.Trim();
            matPlotRunning = true;
            matPlotProgress = 0.0f;
            statusMessage = "Extracted " + selectedDataset.DimX + " x " + selectedDataset.DimY + " XY values.";
            string contextualPrompt = BuildContextualPrompt();
            VolumeSTCubeMatPlotClient client = new VolumeSTCubeMatPlotClient(
                matPlotBaseUrl,
                requestTimeoutSeconds);
            StartCoroutine(client.Run(contextualPrompt, lastExportedCsv, OnMatPlotProgress, OnMatPlotComplete));
        }

        private string BuildContextualPrompt()
        {
            return (chartPrompt ?? string.Empty).Trim() +
                   "\n\nThe uploaded CSV is one two-dimensional XY slice extracted by VolumeSTCube." +
                   " Columns are exactly x, y, value." +
                   " Dataset variable: " + selectedDataset.Name + "." +
                   " Time step: " + selectedTimeIndex + " (" + selectedDataset.GetTimeLabel(selectedTimeIndex) + ")." +
                   " Fixed Z layer: " + selectedZIndex + " of " + selectedDataset.DimZ + "." +
                   " XY dimensions: " + selectedDataset.DimX + " by " + selectedDataset.DimY + "." +
                   " The value column contains the stored RAW uint8 visualization value; do not invent physical units.";
        }

        private void OnMatPlotProgress(string message, float progress)
        {
            statusMessage = message;
            matPlotProgress = progress;
        }

        private void OnMatPlotComplete(VolumeSTCubeMatPlotResult result)
        {
            matPlotRunning = false;
            matPlotProgress = result != null && result.Succeeded ? 1.0f : 0.0f;
            if (result == null || !result.Succeeded)
            {
                statusMessage = result != null ? result.Error : "MatPlotAgent returned no result.";
                return;
            }

            if (matPlotImage != null)
                Destroy(matPlotImage);
            matPlotImage = result.Image;
            workflowStage = WorkflowStage.Result;
            statusMessage = "MatPlotAgent completed job " + result.JobId + ".";
        }

        private void ClearGeneratedChart()
        {
            if (matPlotImage != null)
                Destroy(matPlotImage);
            matPlotImage = null;
            lastExportedCsv = null;
            matPlotProgress = 0.0f;
        }

        private string ResolveDefaultDataRoot()
        {
            if (!string.IsNullOrWhiteSpace(dataRootOverride))
                return Path.GetFullPath(dataRootOverride);
            string environmentPath = Environment.GetEnvironmentVariable("VOLUMESTC_DATA_ROOT");
            if (!string.IsNullOrWhiteSpace(environmentPath))
                return Path.GetFullPath(environmentPath);

            string projectCandidate = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "..",
                "OneDrive_1_4-30-2026"));
            if (Directory.Exists(projectCandidate))
                return projectCandidate;

            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private void ReleasePreviewTextures()
        {
            DestroyTextures(timePreviews);
            DestroyTextures(zPreviews);
            timePreviews = new Texture2D[0];
            zPreviews = new Texture2D[0];
        }

        private static void DestroyTextures(Texture2D[] textures)
        {
            if (textures == null)
                return;
            for (int i = 0; i < textures.Length; i++)
            {
                if (textures[i] != null)
                    Destroy(textures[i]);
            }
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
                return;
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 19,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            sectionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            smallStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            wrappedStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true
            };
        }

        private void OnDestroy()
        {
            ReleasePreviewTextures();
            if (matPlotImage != null)
                Destroy(matPlotImage);
        }
    }

    public static class VolumeSTCubeMatPlotWorkbenchBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Quest uses the world-space controller-driven workbench instead of desktop IMGUI.
            return;
#endif
#if UNITY_EDITOR
            // Editor Play Mode mirrors the Quest spatial workbench. Do not create
            // the legacy full-screen IMGUI surface on top of the 3D console.
            if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
                return;
#endif
            if (UnityEngine.Object.FindObjectOfType<VolumeSTCubeMatPlotWorkbench>() != null)
                return;
            GameObject workbench = new GameObject("VolumeSTCubeMatPlotWorkbench");
            workbench.AddComponent<VolumeSTCubeMatPlotWorkbench>();
        }
    }
}
