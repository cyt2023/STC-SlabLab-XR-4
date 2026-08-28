using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Quest-native world-space version of the STC -> XY -> MatPlot workflow.
    /// It is intentionally built at runtime so the existing desktop scene does not
    /// need to be rewritten and remains available for normal editor use.
    /// </summary>
    public sealed class VolumeSTCubeQuestWorkbench : MonoBehaviour
    {
        private enum Stage
        {
            SelectZ,
            SelectTime,
            Prompt,
            Result
        }

        private readonly List<VolumeSTCubeSliceDataset> datasets = new List<VolumeSTCubeSliceDataset>();
        private Camera xrCamera;
        private VolumeSTCubeQuestRayInteractor rayInteractor;
        private Canvas canvas;
        private RectTransform contentRoot;
        private Text statusText;
        private Text promptDisplay;
        private Text urlDisplay;
        private Font font;
        private VolumeSTCubeSliceDataset selectedDataset;
        private VolumeSTCubeView currentView;
        private Texture2D[] zPreviews = new Texture2D[0];
        private Texture2D[] timePreviews = new Texture2D[0];
        private Texture2D chartImage;
        private int selectedZ = -1;
        private int selectedTime;
        private int zPage;
        private Stage stage = Stage.SelectZ;
        private bool initialized;
        private bool jobRunning;
        private float progress;
        private string prompt = "Create a heatmap of value over the XY grid with a clear color bar.";
        private string matPlotUrl = "http://127.0.0.1:8010";
        private string dataRoot;
        private string exportedCsv;
        private TouchScreenKeyboard keyboard;
        private bool editingUrl;

        private static readonly Color PanelColor = new Color(0.035f, 0.055f, 0.09f, 0.97f);
        private static readonly Color CardColor = new Color(0.09f, 0.13f, 0.19f, 0.98f);
        private static readonly Color AccentColor = new Color(0.08f, 0.68f, 0.95f, 1.0f);
        private static readonly Color SelectedColor = new Color(0.95f, 0.55f, 0.12f, 1.0f);

        public void Initialize(Camera camera, VolumeSTCubeQuestRayInteractor interactor)
        {
            xrCamera = camera;
            rayInteractor = interactor;
            initialized = true;
        }

        private void Start()
        {
            if (!initialized)
                return;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            prompt = PlayerPrefs.GetString("VolumeSTCube.Quest.Prompt", prompt);
            matPlotUrl = PlayerPrefs.GetString("VolumeSTCube.Quest.MatPlotUrl", matPlotUrl);
            CreateCanvas();
            RefreshDatasets();
        }

        private void Update()
        {
            if (keyboard == null)
                return;

            if (editingUrl)
            {
                matPlotUrl = keyboard.text;
                if (urlDisplay != null)
                    urlDisplay.text = matPlotUrl;
            }
            else
            {
                prompt = keyboard.text;
                if (promptDisplay != null)
                    promptDisplay.text = prompt;
            }

            if (keyboard.status == TouchScreenKeyboard.Status.Visible)
                return;

            if (editingUrl)
                PlayerPrefs.SetString("VolumeSTCube.Quest.MatPlotUrl", matPlotUrl);
            else
                PlayerPrefs.SetString("VolumeSTCube.Quest.Prompt", prompt);
            PlayerPrefs.Save();
            keyboard = null;
            BuildCurrentStage();
        }

        public void TogglePanel()
        {
            if (canvas != null)
                canvas.gameObject.SetActive(!canvas.gameObject.activeSelf);
        }

        private void CreateCanvas()
        {
            GameObject canvasObject = new GameObject("VolumeSTCube Quest Workbench", typeof(RectTransform));
            canvasObject.transform.SetParent(xrCamera.transform, false);
            canvasObject.transform.localPosition = new Vector3(0.0f, 0.0f, 2.05f);
            canvasObject.transform.localRotation = Quaternion.identity;
            canvasObject.transform.localScale = Vector3.one * 0.00155f;

            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = UnityEngine.RenderMode.WorldSpace;
            canvas.worldCamera = xrCamera;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 24.0f;
            canvasObject.AddComponent<GraphicRaycaster>();

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(1280.0f, 720.0f);
            Image background = canvasObject.AddComponent<Image>();
            background.color = PanelColor;

            CreateText(canvasRect, "VolumeSTCube Quest  |  Z -> Time -> MatPlot", 30, FontStyle.Bold,
                new Vector2(0.0f, 318.0f), new Vector2(1210.0f, 48.0f), TextAnchor.MiddleLeft);
            CreateText(canvasRect,
                "Right trigger: select   |   B: show/hide panel   |   Left stick: move   |   Right stick: rotate/scale volume   |   X: reset",
                17, FontStyle.Normal, new Vector2(0.0f, 282.0f), new Vector2(1210.0f, 30.0f), TextAnchor.MiddleCenter);

            CreateStepBar(canvasRect);

            GameObject contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.transform.SetParent(canvasRect, false);
            contentRoot = contentObject.GetComponent<RectTransform>();
            contentRoot.anchorMin = contentRoot.anchorMax = new Vector2(0.5f, 0.5f);
            contentRoot.sizeDelta = new Vector2(1235.0f, 505.0f);
            contentRoot.anchoredPosition = new Vector2(0.0f, -25.0f);

            statusText = CreateText(canvasRect, "Starting...", 18, FontStyle.Normal,
                new Vector2(0.0f, -331.0f), new Vector2(1210.0f, 36.0f), TextAnchor.MiddleLeft);
        }

        private void CreateStepBar(RectTransform parent)
        {
            string[] labels = { "1  Z layer", "2  Time", "3  Prompt", "4  Result" };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                Button button = CreateButton(parent, labels[i],
                    new Vector2(-465.0f + i * 310.0f, 245.0f), new Vector2(285.0f, 42.0f),
                    i == 0 ? AccentColor : CardColor, () => NavigateTo((Stage)index));
                button.gameObject.name = "Step " + (i + 1);
            }
        }

        private void NavigateTo(Stage requested)
        {
            if (jobRunning)
                return;
            if (requested == Stage.SelectTime && selectedZ < 0)
                return;
            if ((requested == Stage.Prompt || requested == Stage.Result) && (selectedZ < 0 || selectedDataset == null))
                return;
            if (requested == Stage.Result && chartImage == null)
                return;
            stage = requested;
            BuildCurrentStage();
        }

        private void RefreshDatasets()
        {
            dataRoot = ResolveDataRoot();
            datasets.Clear();
            try
            {
                datasets.AddRange(VolumeSTCubeRawSliceReader.DiscoverDatasets(dataRoot));
                SetStatus(datasets.Count > 0
                    ? "Found " + datasets.Count + " dataset(s). Select a dataset, then choose Z."
                    : "No RAW datasets found at " + dataRoot + ". Push OneDrive_1_4-30-2026 with ADB.");
            }
            catch (Exception exception)
            {
                SetStatus("Dataset discovery failed: " + exception.Message);
            }
            BuildCurrentStage();
        }

        private string ResolveDataRoot()
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                // ADB cannot write to Quest's scoped external Android/data directory.
                // Development installs therefore accept data copied with `run-as` into
                // the app's private files directory, while regular installs keep using
                // Unity's persistent external path.
                string privateRoot = Path.Combine("/data/user/0", Application.identifier, "files", "OneDrive_1_4-30-2026");
                if (Directory.Exists(privateRoot))
                    return privateRoot;
                return Path.Combine(Application.persistentDataPath, "OneDrive_1_4-30-2026");
            }
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "OneDrive_1_4-30-2026"));
        }

        private void BuildCurrentStage()
        {
            if (contentRoot == null)
                return;
            ClearChildren(contentRoot);
            RefreshStepColors();
            switch (stage)
            {
                case Stage.SelectZ:
                    BuildZStage();
                    break;
                case Stage.SelectTime:
                    BuildTimeStage();
                    break;
                case Stage.Prompt:
                    BuildPromptStage();
                    break;
                case Stage.Result:
                    BuildResultStage();
                    break;
            }
        }

        private void RefreshStepColors()
        {
            for (int i = 0; i < 4; i++)
            {
                GameObject step = GameObject.Find("Step " + (i + 1));
                if (step == null)
                    continue;
                Image image = step.GetComponent<Image>();
                if (image != null)
                    image.color = i == (int)stage ? AccentColor : CardColor;
            }
        }

        private void BuildZStage()
        {
            CreateText(contentRoot, "Step 1 — Select dataset and Z layer", 25, FontStyle.Bold,
                new Vector2(0.0f, 225.0f), new Vector2(1190.0f, 40.0f), TextAnchor.MiddleLeft);

            if (datasets.Count == 0)
            {
                CreateText(contentRoot,
                    "Quest data folder:\n" + dataRoot +
                    "\n\nConnect USB and use the supplied ADB data command, then restart the app.",
                    21, FontStyle.Normal, Vector2.zero, new Vector2(1120.0f, 280.0f), TextAnchor.MiddleCenter);
                CreateButton(contentRoot, "Refresh datasets", new Vector2(0.0f, -170.0f),
                    new Vector2(260.0f, 54.0f), AccentColor, RefreshDatasets);
                return;
            }

            float datasetStart = -(datasets.Count - 1) * 105.0f;
            for (int i = 0; i < datasets.Count; i++)
            {
                int datasetIndex = i;
                Color color = datasets[i] == selectedDataset ? SelectedColor : CardColor;
                CreateButton(contentRoot, datasets[i].Name,
                    new Vector2(datasetStart + i * 210.0f, 180.0f), new Vector2(190.0f, 44.0f),
                    color, () => LoadDataset(datasetIndex));
            }

            if (selectedDataset == null)
            {
                CreateText(contentRoot, "Choose a dataset above to load its first 3D time frame.", 23,
                    FontStyle.Normal, new Vector2(0.0f, 10.0f), new Vector2(1100.0f, 100.0f), TextAnchor.MiddleCenter);
                return;
            }

            const int perPage = 32;
            const int columns = 8;
            int first = zPage * perPage;
            int count = Mathf.Min(perPage, zPreviews.Length - first);
            for (int local = 0; local < count; local++)
            {
                int z = first + local;
                int row = local / columns;
                int column = local % columns;
                float x = -535.0f + column * 153.0f;
                float y = 105.0f - row * 96.0f;
                CreateTextureButton(contentRoot, zPreviews[z], "z=" + z,
                    new Vector2(x, y), new Vector2(140.0f, 88.0f),
                    z == selectedZ ? SelectedColor : CardColor, () => SelectZ(z));
            }

            int pageCount = Mathf.Max(1, Mathf.CeilToInt(zPreviews.Length / (float)perPage));
            CreateButton(contentRoot, "< Previous Z", new Vector2(-470.0f, -225.0f),
                new Vector2(190.0f, 44.0f), CardColor, () => ChangeZPage(-1));
            CreateText(contentRoot, "Z page " + (zPage + 1) + " / " + pageCount,
                19, FontStyle.Normal, new Vector2(0.0f, -225.0f), new Vector2(250.0f, 42.0f), TextAnchor.MiddleCenter);
            CreateButton(contentRoot, "Next Z >", new Vector2(470.0f, -225.0f),
                new Vector2(190.0f, 44.0f), CardColor, () => ChangeZPage(1));
        }

        private void ChangeZPage(int direction)
        {
            int pages = Mathf.Max(1, Mathf.CeilToInt(zPreviews.Length / 32.0f));
            zPage = (zPage + direction + pages) % pages;
            BuildCurrentStage();
        }

        private void LoadDataset(int index)
        {
            if (index < 0 || index >= datasets.Count || jobRunning)
                return;
            VolumeSTCubeSliceDataset next = datasets[index];
            SetStatus("Loading " + next.Name + " into the 3D renderer...");

            if (currentView != null)
            {
                VolumeSTCubeAPI.DestroyView(currentView.viewId);
                currentView = null;
            }

            VolumeSTCubeConfig config = VolumeSTCubeConfig.Default("quest_slice_workbench");
            config.datasetName = next.Name;
            config.dataLayout = VolumeSTCubeDataLayout.XYZTimeSeries;
            config.showTimeline = false;
            config.timelineAutoPlay = false;
            config.opacity = 0.9f;
            if (!VolumeSTCubeAPI.TryCreateViewFromRawDirectory(next.DirectoryPath, config, out currentView, out string error))
            {
                SetStatus(error);
                return;
            }

            selectedDataset = next;
            selectedTime = 0;
            selectedZ = -1;
            zPage = 0;
            ClearChart();
            DestroyTextures(timePreviews);
            timePreviews = new Texture2D[0];
            BuildZPreviews();
            FrameVolume();
            SetStatus("Loaded " + next.Name + ". Point at a Z card and press the right trigger.");
            BuildCurrentStage();
        }

        private void BuildZPreviews()
        {
            DestroyTextures(zPreviews);
            zPreviews = selectedDataset == null ? new Texture2D[0] : new Texture2D[selectedDataset.DimZ];
            if (selectedDataset == null)
                return;
            try
            {
                for (int z = 0; z < selectedDataset.DimZ; z++)
                {
                    VolumeSTCubeRawSlice slice = VolumeSTCubeRawSliceReader.ReadSlice(
                        selectedDataset.RawPaths[selectedTime], selectedDataset.IniPaths[selectedTime], z);
                    zPreviews[z] = VolumeSTCubeRawSliceReader.CreatePreviewTexture(slice, 120, 72);
                }
            }
            catch (Exception exception)
            {
                SetStatus("Z preview failed: " + exception.Message);
            }
        }

        private void SelectZ(int z)
        {
            if (selectedDataset == null || z < 0 || z >= selectedDataset.DimZ)
                return;
            selectedZ = z;
            BuildTimePreviews();
            stage = Stage.SelectTime;
            SetStatus("Selected z=" + z + ". Choose one of the " + selectedDataset.TimeCount + " time cards.");
            BuildCurrentStage();
        }

        private void BuildTimePreviews()
        {
            DestroyTextures(timePreviews);
            timePreviews = selectedDataset == null ? new Texture2D[0] : new Texture2D[selectedDataset.TimeCount];
            if (selectedDataset == null || selectedZ < 0)
                return;
            try
            {
                for (int t = 0; t < selectedDataset.TimeCount; t++)
                {
                    VolumeSTCubeRawSlice slice = VolumeSTCubeRawSliceReader.ReadSlice(
                        selectedDataset.RawPaths[t], selectedDataset.IniPaths[t], selectedZ);
                    timePreviews[t] = VolumeSTCubeRawSliceReader.CreatePreviewTexture(slice, 155, 66);
                }
            }
            catch (Exception exception)
            {
                SetStatus("Time preview failed: " + exception.Message);
            }
        }

        private void BuildTimeStage()
        {
            CreateText(contentRoot, "Step 2 — Choose time at z=" + selectedZ, 25, FontStyle.Bold,
                new Vector2(-90.0f, 225.0f), new Vector2(1000.0f, 40.0f), TextAnchor.MiddleLeft);
            CreateButton(contentRoot, "Back to Z", new Vector2(520.0f, 225.0f),
                new Vector2(175.0f, 42.0f), CardColor, () => NavigateTo(Stage.SelectZ));

            const int columns = 6;
            for (int t = 0; t < timePreviews.Length; t++)
            {
                int timeIndex = t;
                int row = t / columns;
                int column = t % columns;
                float x = -500.0f + column * 200.0f;
                float y = 150.0f - row * 91.0f;
                CreateTextureButton(contentRoot, timePreviews[t], selectedDataset.GetTimeLabel(t),
                    new Vector2(x, y), new Vector2(184.0f, 82.0f),
                    t == selectedTime ? SelectedColor : CardColor, () => SelectTime(timeIndex));
            }
        }

        private void SelectTime(int timeIndex)
        {
            if (selectedDataset == null || timeIndex < 0 || timeIndex >= selectedDataset.TimeCount)
                return;
            selectedTime = timeIndex;
            if (currentView != null)
            {
                float minimum = timeIndex / (float)selectedDataset.TimeCount;
                float maximum = (timeIndex + 1) / (float)selectedDataset.TimeCount;
                currentView.ApplyTimeFilter(minimum, maximum);
            }
            BuildZPreviews();
            stage = Stage.Prompt;
            SetStatus("Selected " + selectedDataset.GetTimeLabel(timeIndex) + ". Enter a chart request.");
            BuildCurrentStage();
        }

        private void BuildPromptStage()
        {
            CreateText(contentRoot, "Step 3 — Natural-language 2D chart", 25, FontStyle.Bold,
                new Vector2(-90.0f, 225.0f), new Vector2(1000.0f, 40.0f), TextAnchor.MiddleLeft);
            CreateButton(contentRoot, "Back to time", new Vector2(520.0f, 225.0f),
                new Vector2(175.0f, 42.0f), CardColor, () => NavigateTo(Stage.SelectTime));
            CreateText(contentRoot,
                selectedDataset.Name + "   |   " + selectedDataset.GetTimeLabel(selectedTime) + "   |   z=" + selectedZ +
                "   |   CSV columns: x, y, value",
                19, FontStyle.Normal, new Vector2(0.0f, 180.0f), new Vector2(1120.0f, 36.0f), TextAnchor.MiddleCenter);

            CreateText(contentRoot, "MatPlotAgent URL", 18, FontStyle.Bold,
                new Vector2(-430.0f, 125.0f), new Vector2(250.0f, 34.0f), TextAnchor.MiddleLeft);
            urlDisplay = CreateTextBox(contentRoot, matPlotUrl,
                new Vector2(20.0f, 125.0f), new Vector2(620.0f, 48.0f), 18);
            CreateButton(contentRoot, "Edit URL", new Vector2(470.0f, 125.0f),
                new Vector2(170.0f, 48.0f), CardColor, () => OpenKeyboard(true));

            CreateText(contentRoot, "Chart request", 18, FontStyle.Bold,
                new Vector2(-430.0f, 55.0f), new Vector2(250.0f, 34.0f), TextAnchor.MiddleLeft);
            promptDisplay = CreateTextBox(contentRoot, prompt,
                new Vector2(0.0f, -45.0f), new Vector2(900.0f, 170.0f), 19);
            CreateButton(contentRoot, "Open keyboard", new Vector2(500.0f, -45.0f),
                new Vector2(190.0f, 64.0f), AccentColor, () => OpenKeyboard(false));

            string generateLabel = jobRunning
                ? "Generating... " + Mathf.RoundToInt(progress * 100.0f) + "%"
                : "Extract XY and generate chart";
            CreateButton(contentRoot, generateLabel, new Vector2(0.0f, -205.0f),
                new Vector2(520.0f, 62.0f), jobRunning ? CardColor : SelectedColor, StartMatPlotJob);
        }

        private void OpenKeyboard(bool url)
        {
            if (jobRunning)
                return;
            editingUrl = url;
            string initial = url ? matPlotUrl : prompt;
            keyboard = TouchScreenKeyboard.Open(initial, TouchScreenKeyboardType.Default, false, true, false, false,
                url ? "MatPlotAgent URL" : "Describe the chart");
        }

        private void StartMatPlotJob()
        {
            if (selectedDataset == null || selectedZ < 0 || jobRunning || string.IsNullOrWhiteSpace(prompt))
                return;
            try
            {
                string output = Path.Combine(Application.temporaryCachePath, "VolumeSTCubeMatPlot");
                exportedCsv = VolumeSTCubeRawSliceReader.ExportCsv(selectedDataset, selectedTime, selectedZ, output);
            }
            catch (Exception exception)
            {
                SetStatus("XY export failed: " + exception.Message);
                return;
            }

            jobRunning = true;
            progress = 0.0f;
            BuildCurrentStage();
            string contextualPrompt = prompt.Trim() +
                "\n\nThe uploaded CSV is one two-dimensional XY slice extracted by VolumeSTCube." +
                " Columns are exactly x, y, value. Dataset variable: " + selectedDataset.Name + "." +
                " Time step: " + selectedTime + " (" + selectedDataset.GetTimeLabel(selectedTime) + ")." +
                " Fixed Z layer: " + selectedZ + " of " + selectedDataset.DimZ + "." +
                " XY dimensions: " + selectedDataset.DimX + " by " + selectedDataset.DimY + "." +
                " The value column contains stored RAW uint8 visualization values; do not invent physical units.";
            VolumeSTCubeMatPlotClient client = new VolumeSTCubeMatPlotClient(matPlotUrl, 180);
            StartCoroutine(client.Run(contextualPrompt, exportedCsv, OnJobProgress, OnJobComplete));
        }

        private void OnJobProgress(string message, float value)
        {
            progress = value;
            SetStatus(message + " (" + Mathf.RoundToInt(value * 100.0f) + "%)");
        }

        private void OnJobComplete(VolumeSTCubeMatPlotResult result)
        {
            jobRunning = false;
            if (result == null || !result.Succeeded)
            {
                SetStatus(result != null ? result.Error : "MatPlotAgent returned no result.");
                BuildCurrentStage();
                return;
            }

            ClearChart();
            chartImage = result.Image;
            stage = Stage.Result;
            SetStatus("MatPlotAgent completed job " + result.JobId + ".");
            BuildCurrentStage();
        }

        private void BuildResultStage()
        {
            CreateText(contentRoot, "Step 4 — Generated 2D chart", 25, FontStyle.Bold,
                new Vector2(-150.0f, 225.0f), new Vector2(900.0f, 40.0f), TextAnchor.MiddleLeft);
            CreateButton(contentRoot, "Edit prompt", new Vector2(410.0f, 225.0f),
                new Vector2(160.0f, 42.0f), CardColor, () => NavigateTo(Stage.Prompt));
            CreateButton(contentRoot, "Choose time", new Vector2(575.0f, 225.0f),
                new Vector2(150.0f, 42.0f), CardColor, () => NavigateTo(Stage.SelectTime));

            GameObject frame = new GameObject("Chart frame", typeof(RectTransform));
            frame.transform.SetParent(contentRoot, false);
            RectTransform frameRect = frame.GetComponent<RectTransform>();
            frameRect.sizeDelta = new Vector2(1160.0f, 445.0f);
            frameRect.anchoredPosition = new Vector2(0.0f, -15.0f);
            Image frameImage = frame.AddComponent<Image>();
            frameImage.color = new Color(0.012f, 0.018f, 0.028f, 1.0f);

            if (chartImage != null)
            {
                GameObject imageObject = new GameObject("Generated chart", typeof(RectTransform));
                imageObject.transform.SetParent(frameRect, false);
                RectTransform imageRect = imageObject.GetComponent<RectTransform>();
                imageRect.anchorMin = new Vector2(0.02f, 0.02f);
                imageRect.anchorMax = new Vector2(0.98f, 0.98f);
                imageRect.offsetMin = imageRect.offsetMax = Vector2.zero;
                RawImage rawImage = imageObject.AddComponent<RawImage>();
                rawImage.texture = chartImage;
                rawImage.color = Color.white;
                AspectRatioFitter fitter = imageObject.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fitter.aspectRatio = chartImage.width / (float)Mathf.Max(1, chartImage.height);
            }
        }

        private void FrameVolume()
        {
            VolumeControllerObject controller = FindObjectOfType<VolumeControllerObject>();
            if (controller == null)
                return;
            controller.transform.position = new Vector3(0.0f, 3.55f, 0.0f);
            controller.transform.rotation = Quaternion.identity;
            controller.transform.localScale = Vector3.one * 0.45f;
        }

        private Button CreateButton(RectTransform parent, string label, Vector2 position, Vector2 size,
            Color color, Action action)
        {
            GameObject buttonObject = new GameObject(label, typeof(RectTransform));
            buttonObject.layer = 5;
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Image image = buttonObject.AddComponent<Image>();
            image.color = color;
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.2f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.18f);
            button.colors = colors;
            Text text = CreateText(rect, label, Mathf.RoundToInt(Mathf.Clamp(size.y * 0.38f, 16.0f, 22.0f)),
                FontStyle.Bold, Vector2.zero, size - new Vector2(12.0f, 8.0f), TextAnchor.MiddleCenter);
            text.raycastTarget = false;
            AddClickTarget(buttonObject, size, action);
            return button;
        }

        private void CreateTextureButton(RectTransform parent, Texture texture, string label, Vector2 position,
            Vector2 size, Color color, Action action)
        {
            GameObject card = new GameObject(label, typeof(RectTransform));
            card.layer = 5;
            card.transform.SetParent(parent, false);
            RectTransform cardRect = card.GetComponent<RectTransform>();
            cardRect.sizeDelta = size;
            cardRect.anchoredPosition = position;
            Image background = card.AddComponent<Image>();
            background.color = color;

            GameObject preview = new GameObject("Preview", typeof(RectTransform));
            preview.transform.SetParent(cardRect, false);
            RectTransform previewRect = preview.GetComponent<RectTransform>();
            previewRect.anchorMin = new Vector2(0.06f, 0.25f);
            previewRect.anchorMax = new Vector2(0.94f, 0.95f);
            previewRect.offsetMin = previewRect.offsetMax = Vector2.zero;
            RawImage raw = preview.AddComponent<RawImage>();
            raw.texture = texture;
            raw.color = Color.white;
            raw.raycastTarget = false;

            Text text = CreateText(cardRect, label, 16, FontStyle.Bold,
                new Vector2(0.0f, -size.y * 0.38f), new Vector2(size.x - 8.0f, size.y * 0.22f), TextAnchor.MiddleCenter);
            text.raycastTarget = false;
            AddClickTarget(card, size, action);
        }

        private void AddClickTarget(GameObject targetObject, Vector2 size, Action action)
        {
            BoxCollider collider = targetObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(size.x, size.y, 12.0f);
            VolumeSTCubeQuestClickTarget target = targetObject.AddComponent<VolumeSTCubeQuestClickTarget>();
            target.Clicked = action;
        }

        private Text CreateTextBox(RectTransform parent, string value, Vector2 position, Vector2 size, int fontSize)
        {
            GameObject box = new GameObject("Text box", typeof(RectTransform));
            box.transform.SetParent(parent, false);
            RectTransform boxRect = box.GetComponent<RectTransform>();
            boxRect.sizeDelta = size;
            boxRect.anchoredPosition = position;
            Image image = box.AddComponent<Image>();
            image.color = new Color(0.012f, 0.02f, 0.032f, 1.0f);
            return CreateText(boxRect, value, fontSize, FontStyle.Normal, Vector2.zero,
                size - new Vector2(26.0f, 18.0f), TextAnchor.MiddleLeft);
        }

        private Text CreateText(RectTransform parent, string value, int fontSize, FontStyle style,
            Vector2 position, Vector2 size, TextAnchor alignment)
        {
            GameObject textObject = new GameObject("Text", typeof(RectTransform));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Text text = textObject.AddComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = new Color(0.92f, 0.96f, 1.0f, 1.0f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
            Debug.Log("VolumeSTCube Quest: " + message);
        }

        private void ClearChart()
        {
            if (chartImage != null)
                Destroy(chartImage);
            chartImage = null;
            exportedCsv = null;
            progress = 0.0f;
        }

        private static void ClearChildren(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Destroy(parent.GetChild(i).gameObject);
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

        private void OnDestroy()
        {
            DestroyTextures(zPreviews);
            DestroyTextures(timePreviews);
            if (chartImage != null)
                Destroy(chartImage);
            if (currentView != null)
                VolumeSTCubeAPI.DestroyView(currentView.viewId);
        }
    }
}
