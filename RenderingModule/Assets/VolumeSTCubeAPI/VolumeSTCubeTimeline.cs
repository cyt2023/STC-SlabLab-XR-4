using DateUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    public class VolumeSTCubeTimeline : MonoBehaviour
    {
        private VolumeSTCubeView view;
        private Slider slider;
        private Text playButtonText;
        private Text valueText;
        private bool isPlaying;
        private bool hasDataTimeRange;
        private float dataTimeMinimum;
        private float dataTimeMaximum = 1.0f;
        private int timeSampleCount;
        private float timelineWindow = 0.05f;
        private bool singleSampleMode;
        private bool rawHourlyTimeline;
        private bool xyzTimeSeriesMode;
        private bool hasPendingSliderValue;
        private float pendingSliderValue;
        private float pendingSliderApplyTime;

        public float WindowWidth => timelineWindow;

        public static GameObject Create(VolumeSTCubeView targetView)
        {
            EnsureEventSystem();

            GameObject canvasObject = new GameObject(
                $"VolumeSTCubeTimeline_{targetView.viewId}",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = UnityEngine.RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920.0f, 1080.0f);
            scaler.matchWidthOrHeight = 0.5f;

            VolumeSTCubeTimeline timeline = canvasObject.AddComponent<VolumeSTCubeTimeline>();
            timeline.Build(targetView);
            return canvasObject;
        }

        private void Build(VolumeSTCubeView targetView)
        {
            view = targetView;
            CalculateDataTimeRange();

            RectTransform panel = CreateRect("Panel", transform);
            panel.anchorMin = new Vector2(1.0f, 0.0f);
            panel.anchorMax = new Vector2(1.0f, 0.0f);
            panel.pivot = new Vector2(1.0f, 0.0f);
            panel.anchoredPosition = new Vector2(-32.0f, 32.0f);
            panel.sizeDelta = new Vector2(720.0f, 72.0f);
            Image panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = new Color(0.055f, 0.065f, 0.08f, 0.92f);

            Button playButton = CreateButton(panel, "PlayButton", new Vector2(18.0f, 14.0f), new Vector2(86.0f, 44.0f));
            playButtonText = CreateText(playButton.transform, "Play", 18, TextAnchor.MiddleCenter);
            Stretch(playButtonText.rectTransform);
            playButton.onClick.AddListener(TogglePlayback);

            slider = CreateSlider(panel);
            RectTransform sliderRect = slider.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0.0f, 0.5f);
            sliderRect.anchorMax = new Vector2(1.0f, 0.5f);
            sliderRect.pivot = new Vector2(0.5f, 0.5f);
            sliderRect.offsetMin = new Vector2(122.0f, -18.0f);
            sliderRect.offsetMax = new Vector2(-260.0f, 18.0f);
            timelineWindow = Mathf.Clamp(view.config.timelineWindow, 0.0001f, 1.0f);
            // A raw stack stores time in texture Z. Showing the default 5% window
            // blends several dates together and makes the geographic footprint
            // look like a floating blob. Step through exactly one Z slice instead.
            singleSampleMode = !hasDataTimeRange && timeSampleCount > 1;
            if (singleSampleMode)
                timelineWindow = 1.0f / timeSampleCount;

            float halfWindow = timelineWindow * 0.5f;
            slider.minValue = halfWindow;
            slider.maxValue = 1.0f - halfWindow;
            Vector2 initialWindow = new Vector2(view.config.timeMin, view.config.timeMax);
            VolumeSTCubeTimeController timeController = view.GetTimeController();
            if (timeController != null)
                initialWindow = timeController.Window;
            bool rendererShowsWholeStack = initialWindow.y - initialWindow.x > timelineWindow * 1.5f;
            float initialCenter = rendererShowsWholeStack && singleSampleMode
                ? slider.minValue
                : (initialWindow.x + initialWindow.y) * 0.5f;
            slider.value = SnapToSampleCenter(Mathf.Clamp(initialCenter, slider.minValue, slider.maxValue));
            slider.onValueChanged.AddListener(OnSliderChanged);

            valueText = CreateText(panel, "Value", 14, TextAnchor.MiddleCenter);
            RectTransform valueRect = valueText.rectTransform;
            valueRect.anchorMin = new Vector2(1.0f, 0.5f);
            valueRect.anchorMax = new Vector2(1.0f, 0.5f);
            valueRect.pivot = new Vector2(1.0f, 0.5f);
            valueRect.anchoredPosition = new Vector2(-14.0f, 0.0f);
            valueRect.sizeDelta = new Vector2(238.0f, 44.0f);

            isPlaying = view.config.timelineAutoPlay;
            UpdatePlayButton();
            if (isPlaying || rendererShowsWholeStack && singleSampleMode)
            {
                if (isPlaying)
                    slider.SetValueWithoutNotify(slider.minValue);
                ApplyTime(slider.value);
            }
            else
            {
                valueText.text = FormatTimeWindow(initialWindow.x, initialWindow.y);
            }
        }

        private void Update()
        {
            if (slider == null || view == null)
                return;

            if (!isPlaying)
            {
                if (hasPendingSliderValue)
                {
                    if (Time.unscaledTime >= pendingSliderApplyTime)
                    {
                        hasPendingSliderValue = false;
                        ApplyTime(pendingSliderValue);
                    }
                    return;
                }
                SynchronizeFromView();
                return;
            }

            if (view.IsTimeTransitionPending())
                return;

            float duration = Mathf.Max(0.1f, view.config.timelinePlaybackSeconds);
            float playbackRange = slider.maxValue - slider.minValue;
            float next = slider.value + Time.unscaledDeltaTime * playbackRange / duration;
            if (next >= slider.maxValue)
            {
                next = slider.maxValue;
                isPlaying = false;
                UpdatePlayButton();
            }

            slider.SetValueWithoutNotify(next);
            ApplyTime(next);
        }

        private void SynchronizeFromView()
        {
            Vector2 normalizedWindow = new Vector2(view.config.timeMin, view.config.timeMax);
            VolumeSTCubeTimeController timeController = view.GetTimeController();
            if (timeController != null)
                normalizedWindow = timeController.Window;

            float value = Mathf.Clamp01((normalizedWindow.x + normalizedWindow.y) * 0.5f);

            if (!Mathf.Approximately(slider.value, value))
                slider.SetValueWithoutNotify(value);
            if (valueText != null)
                valueText.text = FormatTimeWindow(normalizedWindow.x, normalizedWindow.y);
        }

        private void TogglePlayback()
        {
            hasPendingSliderValue = false;
            if (!isPlaying && slider.value >= slider.maxValue - 0.0001f)
                slider.SetValueWithoutNotify(slider.minValue);

            isPlaying = !isPlaying;
            UpdatePlayButton();
            ApplyTime(slider.value);
        }

        private void OnSliderChanged(float value)
        {
            isPlaying = false;
            UpdatePlayButton();
            if (!xyzTimeSeriesMode)
            {
                ApplyTime(value);
                return;
            }

            pendingSliderValue = value;
            pendingSliderApplyTime = Time.unscaledTime + 0.12f;
            hasPendingSliderValue = true;
            float halfWindow = timelineWindow * 0.5f;
            float center = SnapToSampleCenter(Mathf.Clamp(value, halfWindow, 1.0f - halfWindow));
            if (valueText != null)
                valueText.text = FormatTimeWindow(center - halfWindow, center + halfWindow);
        }

        private void ApplyTime(float value)
        {
            if (view == null)
                return;

            float halfWindow = timelineWindow * 0.5f;
            float center = SnapToSampleCenter(Mathf.Clamp(value, halfWindow, 1.0f - halfWindow));
            view.ApplyTimeFilter(center - halfWindow, center + halfWindow);

            if (valueText != null)
                valueText.text = FormatTimeWindow(center - halfWindow, center + halfWindow);
        }

        public void SetNormalizedTime(float value)
        {
            if (slider == null)
                return;

            isPlaying = false;
            hasPendingSliderValue = false;
            UpdatePlayButton();
            float center = SnapToSampleCenter(Mathf.Clamp(value, slider.minValue, slider.maxValue));
            slider.SetValueWithoutNotify(center);
            ApplyTime(center);
        }

        private float SnapToSampleCenter(float normalizedCenter)
        {
            if (!singleSampleMode || timeSampleCount <= 1)
                return normalizedCenter;

            int sampleIndex = Mathf.Clamp(
                Mathf.FloorToInt(normalizedCenter * timeSampleCount),
                0,
                timeSampleCount - 1);
            return (sampleIndex + 0.5f) / timeSampleCount;
        }

        private void CalculateDataTimeRange()
        {
            if (view == null)
                return;

            xyzTimeSeriesMode = view.GetDataLayout() == VolumeSTCubeDataLayout.XYZTimeSeries;
            if (xyzTimeSeriesMode)
            {
                timeSampleCount = view.GetTimeSampleCount();
                return;
            }

            if (view.data == null || view.data.t == null || view.data.t.Count == 0)
            {
                bool foundRawDataset = false;
                bool foundNonRawDataset = false;
                for (int i = 0; i < view.volumeObjects.Count; i++)
                {
                    GameObject volumeObject = view.volumeObjects[i];
                    VolumeRenderedObject renderedObject = volumeObject != null
                        ? volumeObject.GetComponent<VolumeRenderedObject>()
                        : null;
                    if (renderedObject != null && renderedObject.dataset != null)
                    {
                        timeSampleCount += Mathf.Max(0, renderedObject.dataset.dimZ);
                        bool isRaw = !string.IsNullOrEmpty(renderedObject.dataset.filePath) &&
                            renderedObject.dataset.filePath.EndsWith(".raw", System.StringComparison.OrdinalIgnoreCase);
                        foundRawDataset |= isRaw;
                        foundNonRawDataset |= !isRaw;
                    }
                }
                rawHourlyTimeline = foundRawDataset && !foundNonRawDataset;
                return;
            }

            dataTimeMinimum = float.PositiveInfinity;
            dataTimeMaximum = float.NegativeInfinity;
            for (int i = 0; i < view.data.t.Count; i++)
            {
                dataTimeMinimum = Mathf.Min(dataTimeMinimum, view.data.t[i]);
                dataTimeMaximum = Mathf.Max(dataTimeMaximum, view.data.t[i]);
            }

            hasDataTimeRange = !float.IsInfinity(dataTimeMinimum)
                && !float.IsInfinity(dataTimeMaximum)
                && !float.IsNaN(dataTimeMinimum)
                && !float.IsNaN(dataTimeMaximum);
        }

        private string FormatTimeWindow(float normalizedMinimum, float normalizedMaximum)
        {
            normalizedMinimum = Mathf.Clamp01(normalizedMinimum);
            normalizedMaximum = Mathf.Clamp01(normalizedMaximum);
            if (!hasDataTimeRange)
            {
                if (timeSampleCount > 1)
                {
                    if (singleSampleMode && normalizedMaximum - normalizedMinimum <= timelineWindow * 1.1f)
                    {
                        float center = (normalizedMinimum + normalizedMaximum) * 0.5f;
                        int sampleIndex = Mathf.Clamp(
                            Mathf.FloorToInt(center * timeSampleCount),
                            0,
                            timeSampleCount - 1);
                        if (xyzTimeSeriesMode)
                            return $"XYZ + t  {sampleIndex + 1} / {timeSampleCount}";
                        if (rawHourlyTimeline)
                        {
                            System.DateTime sampleTime = DateUtil.DefaultStartDate.AddHours(sampleIndex);
                            return $"{sampleTime:yyyy-M-d HH:00}  (t -> z {sampleIndex + 1}/{timeSampleCount})";
                        }
                        return $"t -> z  {sampleIndex + 1} / {timeSampleCount}";
                    }

                    int minimumIndex = Mathf.FloorToInt(normalizedMinimum * (timeSampleCount - 1)) + 1;
                    int maximumIndex = Mathf.CeilToInt(normalizedMaximum * (timeSampleCount - 1)) + 1;
                    return $"t -> z  {minimumIndex}-{maximumIndex} / {timeSampleCount}";
                }

                return $"t -> z  {normalizedMinimum:0.000}-{normalizedMaximum:0.000}";
            }

            float minimum = Mathf.Lerp(dataTimeMinimum, dataTimeMaximum, normalizedMinimum);
            float maximum = Mathf.Lerp(dataTimeMinimum, dataTimeMaximum, normalizedMaximum);
            return $"t {minimum:0.###} - {maximum:0.###}";
        }

        private void UpdatePlayButton()
        {
            if (playButtonText != null)
                playButtonText.text = isPlaying ? "Pause" : "Play";
        }

        private static Slider CreateSlider(Transform parent)
        {
            RectTransform root = CreateRect("TimelineSlider", parent);
            Slider result = root.gameObject.AddComponent<Slider>();

            RectTransform background = CreateRect("Background", root);
            background.anchorMin = new Vector2(0.0f, 0.5f);
            background.anchorMax = new Vector2(1.0f, 0.5f);
            background.sizeDelta = new Vector2(0.0f, 8.0f);
            Image backgroundImage = background.gameObject.AddComponent<Image>();
            backgroundImage.color = new Color(0.25f, 0.28f, 0.32f, 1.0f);

            RectTransform fillArea = CreateRect("Fill Area", root);
            fillArea.anchorMin = new Vector2(0.0f, 0.5f);
            fillArea.anchorMax = new Vector2(1.0f, 0.5f);
            fillArea.offsetMin = new Vector2(8.0f, -4.0f);
            fillArea.offsetMax = new Vector2(-8.0f, 4.0f);
            RectTransform fill = CreateRect("Fill", fillArea);
            Stretch(fill);
            Image fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.color = new Color(0.15f, 0.65f, 1.0f, 1.0f);

            RectTransform handleArea = CreateRect("Handle Slide Area", root);
            Stretch(handleArea);
            handleArea.offsetMin = new Vector2(10.0f, 0.0f);
            handleArea.offsetMax = new Vector2(-10.0f, 0.0f);
            RectTransform handle = CreateRect("Handle", handleArea);
            handle.sizeDelta = new Vector2(22.0f, 22.0f);
            Image handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = Color.white;

            result.fillRect = fill;
            result.handleRect = handle;
            result.targetGraphic = handleImage;
            result.direction = Slider.Direction.LeftToRight;
            return result;
        }

        private static Button CreateButton(Transform parent, string name, Vector2 position, Vector2 size)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.12f, 0.52f, 0.85f, 1.0f);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            return button;
        }

        private static Text CreateText(Transform parent, string name, int fontSize, TextAnchor alignment)
        {
            RectTransform rect = CreateRect(name, parent);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            return text;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject child = new GameObject(name, typeof(RectTransform));
            RectTransform rect = child.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null)
                return;

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }
    }
}
