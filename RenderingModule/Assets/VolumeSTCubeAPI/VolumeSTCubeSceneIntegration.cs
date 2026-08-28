using System;
using DateUtils;
using EventAnchor;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    [DefaultExecutionOrder(-10000)]
    public sealed class VolumeSTCubeSceneIntegration : MonoBehaviour
    {
        private const float ToolbarMinimumY = 800.0f;
        private const float ToolbarGapFromPanel = 40.0f;

        private static VolumeSTCubeSceneIntegration instance;

        private VolumeControllerObject controller;
        private VolumeSTCubeTimeController timeController;
        private TClipper clipper;
        private AnchorList anchorList;
        private ControlPanel controlPanel;
        private MapMouseTrigger mapMouseTrigger;
        private VolumeSTCubeView timelineView;
        private bool rendererWasReady;
        private bool restoreControlPanel;
        private int restoreControlPanelFrame = -1;
        private int legacyInitializationFrame = -1;
        private int nextResolveFrame;
        private bool legacyTimelineUnified;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null)
                return;

            GameObject integrationObject = new GameObject("VolumeSTCubeSceneIntegration");
            instance = integrationObject.AddComponent<VolumeSTCubeSceneIntegration>();
            DontDestroyOnLoad(integrationObject);
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            ResolveSceneObjects();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SubscribeToTimeController(null);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ResolveSceneObjects();
        }

        private void Update()
        {
            if (Time.frameCount >= nextResolveFrame && NeedsResolve())
                ResolveSceneObjects();

            bool rendererReady = IsRendererReady();
            ApplyLegacyLifecycle(rendererReady);

            if (rendererReady && !rendererWasReady)
            {
                rendererWasReady = true;
                if (VolumeSTCubeOriginalSceneAdapter.NeedsGeographicStackLayout(controller))
                {
                    VolumeSTCubeRenderMode renderMode = controller.GetRenderMode() == RenderMode.IsosurfaceRendering
                        ? VolumeSTCubeRenderMode.Surface
                        : VolumeSTCubeRenderMode.Volume;
                    VolumeSTCubeOriginalSceneAdapter.RefreshController(controller, renderMode, VolumeSTCubeTimeAxis.Z);
                }
                timeController?.NotifyRendererReady();
                EnsureTimeline();
                VolumeSTCubeOriginalSceneAdapter.AlignGeographicMapBelowStack(controller);
                VolumeSTCubeOriginalSceneAdapter.SetPresentationCamera();
                restoreControlPanelFrame = restoreControlPanel ? Time.frameCount + 2 : -1;
            }
            else if (!rendererReady)
            {
                rendererWasReady = false;
            }

            if (restoreControlPanelFrame >= 0 && Time.frameCount >= restoreControlPanelFrame)
            {
                if (controlPanel != null)
                    controlPanel.gameObject.SetActive(true);
                restoreControlPanelFrame = -1;
            }

            // Wait through the legacy ControlPanel.Start frame; its text fields
            // are populated there before the unified preset touches them.
            if (rendererReady && Time.frameCount > legacyInitializationFrame + 1)
                UnifyLegacyTimelineControls();

            if (mapMouseTrigger != null)
                mapMouseTrigger.enabled = mapMouseTrigger.isSetHighlightPosition;

            if (rendererReady && timeController != null)
                SynchronizeLegacyTimeVisuals(timeController.Window);
        }

        private void ResolveSceneObjects()
        {
            controller = FindObjectOfType<VolumeControllerObject>();
            SubscribeToTimeController(VolumeSTCubeTimeController.GetOrAdd(controller));
            clipper = FindObjectOfType<TClipper>();
            anchorList = FindObjectOfType<AnchorList>();
            controlPanel = FindObjectOfType<ControlPanel>();
            mapMouseTrigger = FindObjectOfType<MapMouseTrigger>();
            legacyTimelineUnified = false;
            nextResolveFrame = Time.frameCount + (controller == null ? 1 : 30);

            if (controlPanel != null)
                restoreControlPanel = controlPanel.gameObject.activeSelf;

            ApplyToolbarLayout();
            ApplyLegacyLifecycle(IsRendererReady());
        }

        private void SubscribeToTimeController(VolumeSTCubeTimeController nextController)
        {
            if (timeController == nextController)
                return;

            if (timeController != null)
                timeController.WindowChanged -= OnTimeWindowChanged;
            timeController = nextController;
            if (timeController != null)
                timeController.WindowChanged += OnTimeWindowChanged;
        }

        private void OnTimeWindowChanged(Vector2 window)
        {
            SynchronizeLegacyTimeVisuals(window);
        }

        private void SynchronizeLegacyTimeVisuals(Vector2 window)
        {
            if (controlPanel != null && controlPanel.timeRangeSlider != null)
            {
                float center = Mathf.Clamp01((window.x + window.y) * 0.5f);
                controlPanel.timeRangeSlider.SetValueWithoutNotify(center);
            }

            UpdateLegacyTimeLabels(window);
        }

        private void UpdateLegacyTimeLabels(Vector2 window)
        {
            if (clipper == null)
                return;

            float normalizedStart = IsFinite(window.x) ? Mathf.Clamp01(window.x) : 0.0f;
            float normalizedEnd = IsFinite(window.y) ? Mathf.Clamp01(window.y) : 1.0f;
            if (normalizedEnd < normalizedStart)
            {
                float swap = normalizedStart;
                normalizedStart = normalizedEnd;
                normalizedEnd = swap;
            }

            int sampleCount = GetRawTimeSampleCount();
            DateTime windowStart;
            DateTime windowEnd;
            string rangeText;
            if (sampleCount > 0)
            {
                int firstSample = Mathf.Clamp(Mathf.FloorToInt(normalizedStart * sampleCount), 0, sampleCount - 1);
                int lastSample = Mathf.Clamp(Mathf.CeilToInt(normalizedEnd * sampleCount) - 1, firstSample, sampleCount - 1);
                windowStart = DateUtil.DefaultStartDate.AddHours(firstSample);
                windowEnd = DateUtil.DefaultStartDate.AddHours(lastSample);
                int hourCount = lastSample - firstSample + 1;
                rangeText = hourCount == 1 ? "1 Hour" : $"{hourCount} Hours";
            }
            else
            {
                DateTime rangeStart = DateUtil.DefaultStartDate;
                DateTime rangeEnd = DateUtil.DefaultEndDate;
                TimeSpan fullRange = rangeEnd - rangeStart;
                windowStart = rangeStart.AddTicks((long)(fullRange.Ticks * normalizedStart));
                windowEnd = rangeStart.AddTicks((long)(fullRange.Ticks * normalizedEnd));
                int dayCount = Mathf.Max(1, (windowEnd.Date - windowStart.Date).Days + 1);
                rangeText = $"{dayCount} Days";
            }

            if (clipper.mapText != null)
                clipper.mapText.text = windowStart.ToString("yyyy-M-d HH:00");
            if (clipper.upperClipedText != null)
                clipper.upperClipedText.text = windowEnd.ToString("yyyy-M-d HH:00");
            if (clipper.timeRangeText != null)
                clipper.timeRangeText.text = rangeText;
        }

        private int GetRawTimeSampleCount()
        {
            if (controller == null || controller.volumeContainerObjects == null)
                return 0;

            VolumeSTCubeRawTimeSeries series = controller.GetComponent<VolumeSTCubeRawTimeSeries>();
            if (series != null && series.Count > 0)
                return series.Count;

            int sampleCount = 0;
            for (int i = 0; i < controller.volumeContainerObjects.Length; i++)
            {
                VolumeRenderedObject volume = controller.volumeContainerObjects[i];
                if (volume != null && volume.dataset != null &&
                    !string.IsNullOrEmpty(volume.dataset.filePath) &&
                    volume.dataset.filePath.EndsWith(".raw", StringComparison.OrdinalIgnoreCase))
                    sampleCount += Mathf.Max(0, volume.dataset.dimZ);
            }
            return sampleCount;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void ApplyLegacyLifecycle(bool rendererReady)
        {
            if (!rendererReady)
            {
                legacyInitializationFrame = -1;
                SetEnabled(clipper, false);
                SetEnabled(anchorList, false);
                SetEnabled(controlPanel, false);
                if (mapMouseTrigger != null)
                    mapMouseTrigger.enabled = false;
                return;
            }

            // TClipper owns the references used by the original control panel and
            // anchor list. Give its Start method one complete frame to initialise
            // before either dependent component is allowed to run its own Start.
            SetEnabled(clipper, true);
            if (legacyInitializationFrame < 0)
            {
                legacyInitializationFrame = Time.frameCount;
                SetEnabled(anchorList, false);
                SetEnabled(controlPanel, false);
                return;
            }

            bool dependenciesReady = Time.frameCount > legacyInitializationFrame;
            SetEnabled(anchorList, dependenciesReady);
            SetEnabled(controlPanel, dependenciesReady);
        }

        private static void SetEnabled(Behaviour behaviour, bool enabled)
        {
            if (behaviour != null && behaviour.enabled != enabled)
                behaviour.enabled = enabled;
        }

        private bool IsRendererReady()
        {
            return timeController != null && timeController.IsRendererReady;
        }

        private void EnsureTimeline()
        {
            if (!Application.isPlaying || controller == null || !IsRendererReady())
                return;

            if (FindObjectOfType<VolumeSTCubeTimeline>() != null)
                return;

            VolumeSTCubeConfig config = VolumeSTCubeConfig.Default("original_scene");
            config.viewId = "original_scene";
            config.datasetName = "original_scene";
            config.timeAxis = VolumeSTCubeOriginalSceneAdapter.GetTimeAxis(controller);
            config.dataLayout = timeController != null
                ? timeController.DataLayout
                : VolumeSTCubeDataLayout.Auto;
            config.showTimeline = true;
            config.timelineAutoPlay = false;

            timelineView = new VolumeSTCubeView
            {
                viewId = config.viewId,
                datasetName = config.datasetName,
                rootObject = controller.gameObject,
                config = config,
                isVisible = true,
                ownsRootObject = false
            };

            if (controller.volumeContainerObjects != null)
            {
                for (int i = 0; i < controller.volumeContainerObjects.Length; i++)
                {
                    VolumeRenderedObject volume = controller.volumeContainerObjects[i];
                    if (volume != null)
                        timelineView.volumeObjects.Add(volume.gameObject);
                }
            }

            timelineView.CreateTimeline();
        }

        private void UnifyLegacyTimelineControls()
        {
            if (legacyTimelineUnified || timeController == null || controlPanel == null)
                return;

            VolumeSTCubeTimeline timeline = FindObjectOfType<VolumeSTCubeTimeline>();
            if (timeline == null || controlPanel.timeRangeSlider == null)
                return;

            // The original slider moved the map plane vertically. In the unified
            // model the map is a fixed geographic base and both sliders select the
            // same texture-Z time slice.
            controlPanel.timeRangeSlider.onValueChanged = new Slider.SliderEvent();
            controlPanel.timeRangeSlider.minValue = 0.0f;
            controlPanel.timeRangeSlider.maxValue = 1.0f;
            controlPanel.timeRangeSlider.onValueChanged.AddListener(timeline.SetNormalizedTime);

            if (VolumeSTCubeOriginalSceneAdapter.IsRawGeographicStack(controller))
            {
                controlPanel.SetThreshold(0.01f);
                UnifyRawGeographicRenderControls();
                ApplyUnifiedOpacity(0.9f);
            }

            // The bottom timeline now owns the date display. Keeping the original
            // world-space labels would draw the same date as oversized 3D text on
            // top of the map.
            if (clipper != null)
            {
                if (clipper.mapText != null)
                    clipper.mapText.gameObject.SetActive(false);
                if (clipper.upperClipedText != null)
                    clipper.upperClipedText.gameObject.SetActive(false);
                if (clipper.timeRangeText != null)
                    clipper.timeRangeText.gameObject.SetActive(false);
            }

            MapController.Map map = FindObjectOfType<MapController.Map>();
            if (map != null)
                map.dragable = false;

            MapController.UpperPlane upperPlane = FindObjectOfType<MapController.UpperPlane>();
            if (upperPlane != null)
            {
                upperPlane.dragable = false;
                if (upperPlane.gameObject.activeSelf && clipper != null)
                {
                    if (clipper.upperPlaneText != null)
                        clipper.toggleUpperPlaneActive();
                    else
                        upperPlane.gameObject.SetActive(false);
                }
            }

            VolumeSTCubeOriginalSceneAdapter.AlignGeographicMapBelowStack(controller);
            legacyTimelineUnified = true;
        }

        private void UnifyRawGeographicRenderControls()
        {
            if (controlPanel.opacitySlider != null)
            {
                controlPanel.opacitySlider.onValueChanged = new Slider.SliderEvent();
                controlPanel.opacitySlider.onValueChanged.AddListener(ApplyUnifiedOpacity);

                TMP_InputField opacityInput = controlPanel.opacitySlider.GetComponentInChildren<TMP_InputField>(true);
                if (opacityInput != null)
                {
                    opacityInput.onEndEdit = new TMP_InputField.SubmitEvent();
                    opacityInput.onEndEdit.AddListener(ApplyUnifiedOpacityText);
                }
            }

            if (controlPanel.renderModeToogle != null)
            {
                controlPanel.renderModeToogle.onValueChanged = new Toggle.ToggleEvent();
                controlPanel.renderModeToogle.onValueChanged.AddListener(ApplyUnifiedRenderMode);
            }

            // Preserve the original Reset behaviour, then restore the geographic
            // clearer STC palette after its legacy SetOpacity call has completed.
            Canvas canvas = controlPanel.GetComponentInParent<Canvas>();
            Button[] buttons = canvas != null
                ? canvas.GetComponentsInChildren<Button>(true)
                : FindObjectsOfType<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button == null)
                    continue;

                for (int callIndex = 0; callIndex < button.onClick.GetPersistentEventCount(); callIndex++)
                {
                    if (button.onClick.GetPersistentMethodName(callIndex) != "resetAll")
                        continue;

                    button.onClick.RemoveListener(ApplyCurrentUnifiedOpacity);
                    button.onClick.AddListener(ApplyCurrentUnifiedOpacity);
                    break;
                }
            }
        }

        private void ApplyUnifiedOpacity(float opacity)
        {
            opacity = Mathf.Clamp01(opacity);
            VolumeSTCubeOriginalSceneAdapter.ApplyOpacityPreset(controller, opacity);

            if (controlPanel == null || controlPanel.opacitySlider == null)
                return;

            controlPanel.opacitySlider.SetValueWithoutNotify(opacity);
            TMP_InputField opacityInput = controlPanel.opacitySlider.GetComponentInChildren<TMP_InputField>(true);
            if (opacityInput != null)
                opacityInput.SetTextWithoutNotify(Mathf.FloorToInt(opacity * 100.0f).ToString());
        }

        private void ApplyUnifiedOpacityText(string value)
        {
            if (float.TryParse(value, out float percent))
                ApplyUnifiedOpacity(percent / 100.0f);
            else
                ApplyCurrentUnifiedOpacity();
        }

        private void ApplyCurrentUnifiedOpacity()
        {
            float opacity = controlPanel != null && controlPanel.opacitySlider != null
                ? controlPanel.opacitySlider.value
                : 0.9f;
            ApplyUnifiedOpacity(opacity);
        }

        private void ApplyUnifiedRenderMode(bool surfaceRendering)
        {
            if (controller == null)
                return;

            controller.SetRenderMode(surfaceRendering
                ? RenderMode.IsosurfaceRendering
                : RenderMode.DirectVolumeRendering);
            VolumeSTCubeRawTimeSeries series = controller.GetComponent<VolumeSTCubeRawTimeSeries>();
            if (series != null)
            {
                series.SetRenderMode(surfaceRendering
                    ? VolumeSTCubeRenderMode.Surface
                    : VolumeSTCubeRenderMode.Volume);
            }
            if (controlPanel != null)
                controlPanel.SetThreshold(surfaceRendering ? 0.35f : 0.01f);
            ApplyCurrentUnifiedOpacity();
        }

        private bool NeedsResolve()
        {
            return controller == null
                || timeController == null
                || clipper == null
                || controlPanel == null
                || mapMouseTrigger == null;
        }

        public static bool ApplyToolbarLayout()
        {
            bool changed = false;
            Canvas[] canvases = FindObjectsOfType<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas == null || canvas.name.StartsWith("VolumeSTCubeTimeline_"))
                    continue;

                ControlPanel panel = canvas.GetComponentInChildren<ControlPanel>(true);
                RectTransform panelRect = panel != null ? panel.transform as RectTransform : null;
                if (panelRect == null)
                    continue;

                Selectable[] controls = canvas.GetComponentsInChildren<Selectable>(true);
                float toolbarMinimumX = float.PositiveInfinity;
                for (int j = 0; j < controls.Length; j++)
                {
                    Selectable control = controls[j];
                    RectTransform rect = control != null ? control.transform as RectTransform : null;
                    if (rect == null || rect.anchoredPosition.y < ToolbarMinimumY)
                        continue;
                    if (rect.IsChildOf(panelRect))
                        continue;

                    Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(canvas.transform, rect);
                    toolbarMinimumX = Mathf.Min(toolbarMinimumX, bounds.min.x);
                }

                if (float.IsPositiveInfinity(toolbarMinimumX))
                    continue;

                Bounds panelBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(canvas.transform, panelRect);
                float shift = panelBounds.max.x + ToolbarGapFromPanel - toolbarMinimumX;
                if (shift <= 0.5f)
                    continue;

                for (int j = 0; j < controls.Length; j++)
                {
                    RectTransform rect = controls[j] != null ? controls[j].transform as RectTransform : null;
                    if (rect == null || rect.anchoredPosition.y < ToolbarMinimumY || rect.IsChildOf(panelRect))
                        continue;

                    rect.anchoredPosition += new Vector2(shift, 0.0f);
                }
                changed = true;
            }

            return changed;
        }
    }
}
