using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Desktop/tablet guided shell: title on the first row, actions on the
    /// second row, and exactly one current task surface in the centre.
    /// Quest continues to use freely positioned world-space panels.
    /// </summary>
    public sealed class VolumeSTCubeFlatScreenHUD : MonoBehaviour
    {
        private static readonly Color PrimaryAction =
            new Color(0.00f, 0.67f, 0.78f, 1.0f);
        private static readonly Color ConfirmAction =
            new Color(0.88f, 0.53f, 0.06f, 1.0f);
        private static readonly Color SecondaryAction =
            new Color(0.07f, 0.28f, 0.38f, 1.0f);
        private static readonly Color UtilityAction =
            new Color(0.18f, 0.18f, 0.29f, 1.0f);
        private static readonly Color BackAction =
            new Color(0.34f, 0.19f, 0.22f, 1.0f);
        private static readonly Color HelpAction =
            new Color(0.30f, 0.18f, 0.42f, 1.0f);
        private static VolumeSTCubeFlatScreenHUD activeHud;
        private VolumeSTCubeQuestSpatialWorkbench workbench;
        private RectTransform safeAreaRoot;
        private Text titleText;
        private Text noticeText;
        private float noticeExpiresAt;
        private bool hoverHintShown;
        private GameObject helpPanel;
        private GameObject bottomBar;
        private Text bottomStatusText;
        private Button primaryButton;
        private Button playbackButton;
        private Button speedButton;
        private Button backButton;
        private Button confirmButton;
        private Button intentButton;
        private Button fullMatrixButton;
        private Button pivotButton;
        private Button drillButton;
        private Button rollUpButton;
        private Button historyButton;
        private GameObject historyPanel;
        private RectTransform historyList;
        private Text historyPageText;
        private Button historyPrevious;
        private Button historyNext;
        private int historyPage;
        private const int HistoryPageSize = 5;
        private GraphicRaycaster hudRaycaster;
        private readonly List<Canvas> workflowPanels = new List<Canvas>();
        private Rect lastSafeArea;
        private Vector2Int lastScreenSize;
        private float nextPanelDiscoveryTime;

        public static void Install(GameObject rig,
            VolumeSTCubeQuestSpatialWorkbench workbench)
        {
            if (rig == null || workbench == null ||
                rig.GetComponent<VolumeSTCubeFlatScreenHUD>() != null)
                return;
            VolumeSTCubeFlatScreenHUD hud =
                rig.AddComponent<VolumeSTCubeFlatScreenHUD>();
            activeHud = hud;
            hud.workbench = workbench;
            hud.Build();
        }

        public static void NotifyWorkflowChanged()
        {
            if (activeHud == null)
                return;
            activeHud.RefreshBottomBar();
            activeHud.DockCurrentTaskInCentre();
        }

        private void Build()
        {
            EnsureEventSystem();
            GameObject canvasObject = new GameObject("Flat Screen HUD",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = UnityEngine.RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            hudRaycaster = canvasObject.GetComponent<GraphicRaycaster>();

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920.0f, 1080.0f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GameObject safeObject = new GameObject("Safe Area", typeof(RectTransform));
            safeObject.transform.SetParent(canvasObject.transform, false);
            safeAreaRoot = safeObject.GetComponent<RectTransform>();
            safeAreaRoot.anchorMin = Vector2.zero;
            safeAreaRoot.anchorMax = Vector2.one;
            safeAreaRoot.offsetMin = Vector2.zero;
            safeAreaRoot.offsetMax = Vector2.zero;

            RectTransform titleBar = CreatePanel("Step Title Bar", safeAreaRoot,
                new Color(0.020f, 0.038f, 0.062f, 0.99f))
                .GetComponent<RectTransform>();
            AnchorTopRow(titleBar, 0.0f, 66.0f);
            titleText = CreateText(titleBar, "STEP 1  ·  LOAD LIVE WAVE DATA");
            titleText.fontSize = 29;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleCenter;
            Stretch(titleText.rectTransform, 24.0f, 8.0f);
            noticeText = CreateText(titleBar, string.Empty);
            noticeText.fontSize = 24;
            noticeText.fontStyle = FontStyle.Bold;
            noticeText.alignment = TextAnchor.MiddleCenter;
            noticeText.color = new Color(1.0f, 0.76f, 0.24f, 1.0f);
            Stretch(noticeText.rectTransform, 24.0f, 8.0f);
            noticeText.gameObject.SetActive(false);

            RectTransform actionBar = CreatePanel("Step Action Bar", safeAreaRoot,
                new Color(0.030f, 0.055f, 0.082f, 0.97f))
                .GetComponent<RectTransform>();
            AnchorTopRow(actionBar, -68.0f, 78.0f);
            HorizontalLayoutGroup actions =
                actionBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            actions.padding = new RectOffset(24, 24, 11, 11);
            actions.spacing = 12.0f;
            actions.childAlignment = TextAnchor.MiddleCenter;
            actions.childForceExpandWidth = false;
            actions.childForceExpandHeight = true;

            CreateButton("Previous", actionBar,
                workbench.DesktopPreviousStep, 160.0f, BackAction);
            CreateButton("Next", actionBar,
                workbench.DesktopNextStep, 160.0f, PrimaryAction);
            CreateButton("Reset View", actionBar,
                ResetView, 170.0f, UtilityAction);
            CreateButton("Help", actionBar, ToggleHelp, 130.0f,
                HelpAction);

            historyButton = CreateButton("History", actionBar,
                ToggleHistory, 180.0f, SecondaryAction);
            // Snapshot is appended so the order the operator already knows
            // (Previous, Next, Reset View, Help, History) is untouched.
            CreateButton("Snapshot", actionBar, SaveSnapshot, 170.0f,
                UtilityAction);

            bottomBar = CreatePanel("Desktop Work Bar", safeAreaRoot,
                new Color(0.018f, 0.043f, 0.064f, 0.985f));
            RectTransform bottomRect = bottomBar.GetComponent<RectTransform>();
            bottomRect.anchorMin = new Vector2(0.0f, 0.0f);
            bottomRect.anchorMax = new Vector2(1.0f, 0.0f);
            bottomRect.pivot = new Vector2(0.5f, 0.0f);
            bottomRect.anchoredPosition = Vector2.zero;
            bottomRect.sizeDelta = new Vector2(0.0f, 104.0f);
            HorizontalLayoutGroup bottomLayout =
                bottomBar.AddComponent<HorizontalLayoutGroup>();
            bottomLayout.padding = new RectOffset(34, 34, 17, 17);
            bottomLayout.spacing = 14.0f;
            bottomLayout.childAlignment = TextAnchor.MiddleCenter;
            bottomLayout.childForceExpandWidth = false;
            bottomLayout.childForceExpandHeight = true;
            bottomStatusText = CreateText(bottomRect, string.Empty);
            LayoutElement statusLayout =
                bottomStatusText.gameObject.AddComponent<LayoutElement>();
            statusLayout.minWidth = 180.0f;
            statusLayout.preferredWidth = 430.0f;
            statusLayout.flexibleWidth = 1.0f;
            bottomStatusText.fontSize = 23;
            bottomStatusText.fontStyle = FontStyle.Bold;
            bottomStatusText.alignment = TextAnchor.MiddleLeft;
            primaryButton = CreateButton("SET TIME RANGE", bottomRect,
                workbench.DesktopOpenFieldSetup, 420.0f, ConfirmAction);
            playbackButton = CreateButton("PLAY", bottomRect,
                workbench.DesktopTogglePlayback, 150.0f, PrimaryAction);
            speedButton = CreateButton("SPEED 1x", bottomRect,
                workbench.DesktopCyclePlaybackSpeed, 180.0f,
                SecondaryAction);
            backButton = CreateButton("BACK", bottomRect,
                workbench.DesktopCancelBoundary, 150.0f, BackAction);
            confirmButton = CreateButton("CONFIRM TIME RANGE", bottomRect,
                workbench.DesktopConfirmBoundary, 300.0f, ConfirmAction);
            intentButton = CreateButton("MATPLOT INTENT", bottomRect,
                workbench.DesktopOpenIntent, 220.0f, HelpAction);
            fullMatrixButton = CreateButton("FULL MATRIX", bottomRect,
                workbench.DesktopBuildFullMatrix, 200.0f, ConfirmAction);
            pivotButton = CreateButton("PIVOT", bottomRect,
                workbench.DesktopBeginPivot, 240.0f, HelpAction);
            drillButton = CreateButton("DRILL", bottomRect,
                workbench.DesktopBeginDrill, 190.0f, PrimaryAction);
            rollUpButton = CreateButton("ROLL-UP", bottomRect,
                workbench.DesktopBeginRollUp, 190.0f,
                new Color(0.10f, 0.62f, 0.34f, 1.0f));
            bottomBar.SetActive(false);

            helpPanel = CreatePanel("Help", safeAreaRoot,
                new Color(0.025f, 0.04f, 0.07f, 0.98f));
            RectTransform helpRect = helpPanel.GetComponent<RectTransform>();
            helpRect.anchorMin = new Vector2(0.5f, 0.5f);
            helpRect.anchorMax = new Vector2(0.5f, 0.5f);
            helpRect.pivot = new Vector2(0.5f, 0.5f);
            helpRect.anchoredPosition = new Vector2(0.0f, -55.0f);
            // 250 tall fit the pointer/gesture rows exactly. The shortcut rows
            // below are kept under ~50 characters each (28 px insets leave about
            // that much at 23 pt) and the extra height stops any truncation.
            helpRect.sizeDelta = new Vector2(720.0f, 340.0f);
            Text helpText = CreateText(helpRect,
                "DESKTOP\nLeft click: choose or interact    Right drag: look\n" +
                "Wheel: zoom    Previous / Next: guided workflow\n\n" +
                "TABLET\nOne finger: choose or interact    Two fingers: look\n" +
                "Pinch: zoom\n\n" + SlabLabShortcuts.HelpLine);
            helpText.fontSize = 23;
            Stretch(helpText.rectTransform, 28.0f, 20.0f);
            helpPanel.SetActive(false);
            BuildHistoryPanel();
            ApplySafeArea();
        }

        private void BuildHistoryPanel()
        {
            historyPanel = CreatePanel("Analysis history overlay", safeAreaRoot,
                new Color(0.01f, 0.02f, 0.04f, 0.92f));
            RectTransform overlay = historyPanel.GetComponent<RectTransform>();
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = Vector2.zero;
            overlay.offsetMax = Vector2.zero;
            RectTransform card = CreatePanel("History card", overlay,
                new Color(0.035f, 0.08f, 0.11f, 1.0f)).GetComponent<RectTransform>();
            card.anchorMin = new Vector2(0.12f, 0.16f);
            card.anchorMax = new Vector2(0.88f, 0.84f);
            card.offsetMin = Vector2.zero;
            card.offsetMax = Vector2.zero;
            VerticalLayoutGroup layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 20, 20);
            layout.spacing = 10;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            Text title = CreateText(card, "Analysis history · this session");
            title.fontSize = 26;
            title.fontStyle = FontStyle.Bold;
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 42;
            GameObject list = new GameObject("Results", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            list.transform.SetParent(card, false);
            historyList = list.GetComponent<RectTransform>();
            list.GetComponent<LayoutElement>().flexibleHeight = 1;
            VerticalLayoutGroup rows = list.GetComponent<VerticalLayoutGroup>();
            rows.spacing = 10;
            rows.childControlHeight = true;
            rows.childControlWidth = true;
            rows.childForceExpandWidth = true;
            rows.childForceExpandHeight = true;
            GameObject footer = new GameObject("History navigation", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            footer.transform.SetParent(card, false);
            footer.GetComponent<LayoutElement>().preferredHeight = 54;
            HorizontalLayoutGroup navigation = footer.GetComponent<HorizontalLayoutGroup>();
            navigation.spacing = 12;
            navigation.childForceExpandWidth = false;
            historyPrevious = CreateButton("Earlier", footer.transform, () => { historyPage--; RefreshHistory(); }, 150, SecondaryAction);
            historyPageText = CreateText(footer.transform, string.Empty);
            historyPageText.alignment = TextAnchor.MiddleCenter;
            historyPageText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            historyNext = CreateButton("More", footer.transform, () => { historyPage++; RefreshHistory(); }, 150, SecondaryAction);
            CreateButton("Close", footer.transform, () => historyPanel.SetActive(false), 140, BackAction);
            historyPanel.SetActive(false);
        }

        private void ToggleHistory()
        {
            if (historyPanel == null || workbench == null || workbench.DesktopHistoryBusy) return;
            bool show = !historyPanel.activeSelf;
            historyPanel.SetActive(show);
            if (show)
            {
                historyPage = 0;
                RefreshHistory();
                helpPanel.SetActive(false);
                historyPanel.transform.SetAsLastSibling();
            }
        }

        private void RefreshHistory()
        {
            for (int i = historyList.childCount - 1; i >= 0; i--)
            {
                GameObject child = historyList.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            int count = workbench.DesktopAnalysisCount;
            int pages = Mathf.Max(1, (count + HistoryPageSize - 1) / HistoryPageSize);
            historyPage = Mathf.Clamp(historyPage, 0, pages - 1);
            historyPageText.text = (historyPage + 1) + " / " + pages;
            historyPrevious.interactable = historyPage > 0;
            historyNext.interactable = historyPage + 1 < pages;
            if (count == 0)
            {
                Text empty = CreateText(historyList, "Your completed analyses will appear here.\nGenerate charts to get started.");
                empty.alignment = TextAnchor.MiddleCenter;
                return;
            }
            for (int row = 0; row < HistoryPageSize; row++)
            {
                int index = count - 1 - historyPage * HistoryPageSize - row;
                if (index < 0) break;
                Button button = CreateButton(workbench.DesktopAnalysisLabel(index), historyList,
                    () => { workbench.DesktopOpenAnalysis(index); historyPanel.SetActive(false); }, 500, SecondaryAction);
                button.GetComponent<LayoutElement>().preferredHeight = 64;
                button.GetComponent<LayoutElement>().flexibleHeight = 0;
                Text label = button.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                Stretch(label.rectTransform, 18, 6);
            }
        }

        private void Update()
        {
            if (noticeText != null && noticeText.gameObject.activeSelf &&
                Time.unscaledTime >= noticeExpiresAt)
            {
                noticeText.gameObject.SetActive(false);
                if (titleText != null)
                    titleText.gameObject.SetActive(true);
            }
            if (titleText != null && workbench != null)
            {
                string title = workbench.DesktopWorkflowTitle;
                if (titleText.text != title)
                    titleText.text = title;
            }
            RefreshBottomBar();
            if (historyButton != null && workbench != null)
            {
                SetButtonText(historyButton, "History (" + workbench.DesktopAnalysisCount + ")");
                historyButton.interactable = !workbench.DesktopHistoryBusy;
            }
            SlabLabShortcut shortcut = PollShortcut(out KeyCode shortcutKey);
            if (shortcut != SlabLabShortcut.None)
                ApplyShortcut(shortcut, shortcutKey);
            UpdateHoverHint();
            if (lastSafeArea != Screen.safeArea ||
                lastScreenSize.x != Screen.width || lastScreenSize.y != Screen.height)
                ApplySafeArea();
            if (Time.unscaledTime >= nextPanelDiscoveryTime)
            {
                nextPanelDiscoveryTime = Time.unscaledTime + 0.25f;
                DiscoverWorkflowPanels();
            }
        }

        private void RefreshBottomBar()
        {
            if (bottomBar == null || workbench == null)
                return;
            EnsureWorkflowButtons();
            bool visible = workbench.DesktopCompactBarActive;
            bottomBar.SetActive(visible);
            if (!visible)
                return;
            // Runtime-created HUD references can be cleared by an Editor script
            // reload while their GameObjects survive. Never let that transient
            // state turn into an exception every frame.
            if (bottomStatusText == null || primaryButton == null ||
                playbackButton == null || speedButton == null ||
                backButton == null || confirmButton == null ||
                intentButton == null || fullMatrixButton == null ||
                pivotButton == null || drillButton == null ||
                rollUpButton == null)
                return;
            // Step 3 has its own axis-binding workflow. It takes precedence
            // over any stale boundary flag so time controls can never leak
            // into the next step.
            bool axis = workbench.DesktopAxisBarActive;
            bool boundary = !axis && workbench.DesktopBoundaryBarActive;
            bool workflow = !axis && !boundary &&
                workbench.DesktopWorkflowBarActive;
            bottomStatusText.gameObject.SetActive(boundary || axis);
            string status = axis
                ? workbench.DesktopAxisBindingLabel
                : boundary ? workbench.DesktopBoundaryRangeLabel : string.Empty;
            if (bottomStatusText.text != status)
                bottomStatusText.text = status;
            primaryButton.gameObject.SetActive(!boundary && !axis && !workflow);
            playbackButton.gameObject.SetActive(boundary);
            speedButton.gameObject.SetActive(boundary);
            backButton.gameObject.SetActive(boundary);
            confirmButton.gameObject.SetActive(boundary);
            bool result = workbench.DesktopMatrixTaskActive;
            intentButton.gameObject.SetActive(workflow && !result);
            fullMatrixButton.gameObject.SetActive(workflow);
            pivotButton.gameObject.SetActive(workflow && result);
            drillButton.gameObject.SetActive(workflow && result);
            rollUpButton.gameObject.SetActive(workflow && result);
            if (workflow)
            {
                // Desktop moves directly from axis binding to MatPlot Intent.
                // Slab preparation happens internally and has no separate UI.
                SetWorkflowButtonState(intentButton,
                    workbench.DesktopCanOpenIntent, HelpAction);
                SetWorkflowButtonState(fullMatrixButton,
                    workbench.DesktopCanBuildMatrix, ConfirmAction);
                SetButtonText(fullMatrixButton,
                    workbench.DesktopFullMatrixLabel);
                bool canTransform = workbench.DesktopCanTransformMatrix;
                SetWorkflowButtonState(pivotButton, canTransform, HelpAction);
                SetWorkflowButtonState(drillButton, canTransform,
                    PrimaryAction);
                SetWorkflowButtonState(rollUpButton, canTransform,
                    new Color(0.10f, 0.62f, 0.34f, 1.0f));
            }
            SetButtonText(intentButton, "Analysis setup");
            SetButtonText(pivotButton, "Change comparison");
            SetButtonText(drillButton, "Expand detail");
            SetButtonText(rollUpButton, "Group ranges");
            SetButtonText(primaryButton, "Set time range");
            SetButtonText(confirmButton, "Confirm range");
            SetButtonText(playbackButton, workbench.DesktopPlaybackLabel);
            SetButtonText(speedButton, workbench.DesktopPlaybackSpeedLabel);
        }

        private static void SetWorkflowButtonState(Button button,
            bool available, Color activeColor)
        {
            if (button == null)
                return;
            // A disabled Button swallows the user's intent without explaining
            // what is missing. Keep guarded actions clickable and let the
            // workbench report the exact prerequisite in the title strip.
            button.interactable = true;
            Image image = button.GetComponent<Image>();
            if (image != null)
                image.color = available ? activeColor : UtilityAction;
        }

        public static void ShowNotice(string message)
        {
            VolumeSTCubeFlatScreenHUD hud = activeHud;
            if (hud == null || hud.noticeText == null ||
                string.IsNullOrWhiteSpace(message))
                return;
            hud.noticeText.text = message;
            hud.noticeText.gameObject.SetActive(true);
            if (hud.titleText != null)
                hud.titleText.gameObject.SetActive(false);
            hud.noticeExpiresAt = Time.unscaledTime + 6.0f;
        }

        private void EnsureWorkflowButtons()
        {
            if (bottomBar == null)
                return;
            if (bottomStatusText == null)
            {
                for (int index = 0; index < bottomBar.transform.childCount;
                    index++)
                {
                    Transform child = bottomBar.transform.GetChild(index);
                    Text candidate = child.GetComponent<Text>();
                    if (candidate == null)
                        continue;
                    bottomStatusText = candidate;
                    break;
                }
            }
            if (primaryButton == null)
                primaryButton = FindBottomButton("SET TIME RANGE");
            if (playbackButton == null)
                playbackButton = FindBottomButton("PLAY");
            if (speedButton == null)
                speedButton = FindBottomButton("SPEED 1x");
            if (backButton == null)
                backButton = FindBottomButton("BACK");
            if (confirmButton == null)
                confirmButton = FindBottomButton("CONFIRM TIME RANGE");
            if (intentButton == null)
                intentButton = FindBottomButton("MATPLOT INTENT");
            if (fullMatrixButton == null)
                fullMatrixButton = FindBottomButton("FULL MATRIX");
            if (pivotButton == null)
                pivotButton = FindBottomButton("PIVOT");
            if (drillButton == null)
                drillButton = FindBottomButton("DRILL");
            if (rollUpButton == null)
                rollUpButton = FindBottomButton("ROLL-UP");
            if (workbench == null)
                return;
            if (intentButton == null)
                intentButton = CreateButton("MATPLOT INTENT", bottomBar.transform,
                    workbench.DesktopOpenIntent, 220.0f, HelpAction);
            if (fullMatrixButton == null)
                fullMatrixButton = CreateButton("FULL MATRIX", bottomBar.transform,
                    workbench.DesktopBuildFullMatrix, 200.0f, ConfirmAction);
            if (pivotButton == null)
                pivotButton = CreateButton("PIVOT", bottomBar.transform,
                    workbench.DesktopBeginPivot, 240.0f, HelpAction);
            if (drillButton == null)
                drillButton = CreateButton("DRILL", bottomBar.transform,
                    workbench.DesktopBeginDrill, 190.0f, PrimaryAction);
            if (rollUpButton == null)
                rollUpButton = CreateButton("ROLL-UP", bottomBar.transform,
                    workbench.DesktopBeginRollUp, 190.0f,
                    new Color(0.10f, 0.62f, 0.34f, 1.0f));
        }

        private Button FindBottomButton(string name)
        {
            if (bottomBar == null)
                return null;
            Transform child = bottomBar.transform.Find(name);
            return child != null ? child.GetComponent<Button>() : null;
        }

        private static void SetButtonText(Button button, string value)
        {
            if (button == null)
                return;
            Text label = button.GetComponentInChildren<Text>();
            if (label != null && label.text != value)
                label.text = value;
        }

        private void LateUpdate()
        {
            DockCurrentTaskInCentre();
        }

        private void DiscoverWorkflowPanels()
        {
            if (workbench == null)
                return;
            Canvas[] candidates = workbench.GetComponentsInChildren<Canvas>(true);
            for (int index = 0; index < candidates.Length; index++)
            {
                Canvas candidate = candidates[index];
                if (candidate == null || workflowPanels.Contains(candidate) ||
                    candidate.GetComponent<VolumeSTCubeQuestPanelHandle>() == null)
                    continue;
                if (candidate.name == "S4D persistent workflow toolbar")
                {
                    candidate.gameObject.SetActive(false);
                    continue;
                }
                workflowPanels.Add(candidate);
            }
        }

        private void DockCurrentTaskInCentre()
        {
            Camera camera = Camera.main;
            if (camera == null)
                return;
            bool central = workbench != null &&
                workbench.DesktopTaskPanelIsCentral;
            bool matrix = workbench != null &&
                workbench.DesktopMatrixTaskActive;
            // Reserve fixed render-safe lanes for both bars. World-space Fields
            // and axis tools are never allowed to render underneath the HUD.
            camera.rect = central && !matrix
                ? new Rect(0.0f, 0.0f, 1.0f, 0.86f)
                : new Rect(0.0f, 0.10f, 1.0f, 0.765f);
            float distance = 2.05f;
            float viewHeight = 2.0f * distance * Mathf.Tan(
                camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float verticalWorld = viewHeight *
                (matrix ? 0.80f : central ? 0.68f : 0.135f);
            float horizontalWorld = viewHeight * camera.aspect *
                (matrix ? 0.92f : central ? 0.86f : 0.94f);
            for (int index = 0; index < workflowPanels.Count; index++)
            {
                Canvas panel = workflowPanels[index];
                if (panel == null || !panel.gameObject.activeInHierarchy)
                    continue;
                bool desktopComposer = workbench != null &&
                    workbench.DesktopComposerPanelActive &&
                    (panel.name == "FacetSlab Configuration Preview" ||
                     panel.name == "MatPlotAgent Intent Composer");
                bool composerPrimary = desktopComposer && workbench != null &&
                    (workbench.DesktopIntentPanelPrimary ||
                     panel.name == "FacetSlab Configuration Preview");
                // Reuse the proven VR interaction surfaces for the advanced
                // analysis operations. On desktop they are treated as the one
                // central task surface and fitted between the fixed toolbars.
                bool desktopImportedPanel =
                    panel.name == "S4D Anchored Facet Grid" ||
                    panel.name == "AI Findings";
                bool desktopMatrixResultPanel = workbench != null &&
                    workbench.DesktopMatrixTaskActive &&
                    (panel.name == "S4D Anchored Facet Grid" ||
                     panel.name == "AI Findings");
                bool desktopMatrixProgressPanel = workbench != null &&
                    workbench.DesktopMatrixProgressActive &&
                    panel.name == "Slab Lab Spatial Console";
                bool desktopMatrixPanel = desktopMatrixResultPanel ||
                    desktopMatrixProgressPanel;
                bool matrixPrimary = desktopMatrixPanel &&
                    workbench.DesktopMatrixPanelPrimary;
                bool desktopTaskSurface = desktopComposer ||
                    desktopImportedPanel || desktopMatrixProgressPanel;
                if (workbench != null && workbench.DesktopCompactBarActive &&
                    !desktopTaskSurface)
                {
                    panel.enabled = false;
                    continue;
                }
                panel.enabled = true;
                RectTransform rect = panel.GetComponent<RectTransform>();
                if (rect == null)
                    continue;
                float panelHorizontalWorld = desktopComposer
                    ? viewHeight * camera.aspect *
                        (composerPrimary ? 0.48f : 0.25f)
                    : desktopMatrixPanel
                        ? viewHeight * camera.aspect *
                            (matrixPrimary ? 0.66f : 0.27f)
                        : horizontalWorld;
                float panelVerticalWorld = desktopComposer
                    ? viewHeight * (composerPrimary ? 0.68f : 0.38f)
                    : desktopMatrixPanel
                        ? viewHeight * (matrixPrimary ? 0.76f : 0.40f)
                        : verticalWorld;
                float scale = Mathf.Min(
                    panelHorizontalWorld / Mathf.Max(1.0f, rect.sizeDelta.x),
                    panelVerticalWorld / Mathf.Max(1.0f, rect.sizeDelta.y));
                Vector3 centre = camera.ViewportToWorldPoint(
                    new Vector3(desktopComposer
                            ? composerPrimary ? 0.70f : 0.84f
                            : desktopMatrixPanel
                                ? matrixPrimary ? 0.66f : 0.84f
                                : 0.5f,
                        desktopTaskSurface ? 0.50f :
                            central ? 0.48f : 0.075f,
                        distance));
                Quaternion targetRotation = Quaternion.LookRotation(
                    centre - camera.transform.position, camera.transform.up);
                Vector3 targetScale = Vector3.one * scale;
                float blend = 1.0f - Mathf.Exp(-9.0f *
                    Mathf.Max(0.0f, Time.unscaledDeltaTime));
                panel.transform.position = Vector3.Lerp(
                    panel.transform.position, centre, blend);
                panel.transform.rotation = Quaternion.Slerp(
                    panel.transform.rotation, targetRotation, blend);
                panel.transform.localScale = Vector3.Lerp(
                    panel.transform.localScale, targetScale, blend);
                if ((panel.transform.position - centre).sqrMagnitude < 0.000001f)
                    panel.transform.position = centre;
                if ((panel.transform.localScale - targetScale).sqrMagnitude <
                    0.000001f)
                    panel.transform.localScale = targetScale;
            }
        }

        private void ToggleHelp()
        {
            if (helpPanel != null)
                helpPanel.SetActive(!helpPanel.activeSelf);
        }

        /// <summary>
        /// Say what the control under the pointer does, in the line the notices
        /// already use — no new geometry, and a live notice always wins. The
        /// title comes back when the pointer leaves.
        /// </summary>
        private void UpdateHoverHint()
        {
            if (noticeText == null || hudRaycaster == null ||
                EventSystem.current == null)
                return;
            bool busy = noticeText.gameObject.activeSelf &&
                Time.unscaledTime < noticeExpiresAt &&
                !hoverHintShown;
            string hint = null;
            if (!busy)
            {
                var pointer = new PointerEventData(EventSystem.current)
                {
                    position = Input.mousePosition
                };
                var results = new List<RaycastResult>();
                hudRaycaster.Raycast(pointer, results);
                for (int index = 0; index < results.Count && hint == null;
                    index++)
                {
                    Button button = results[index].gameObject
                        .GetComponentInParent<Button>();
                    if (button == null)
                        continue;
                    Text caption =
                        button.GetComponentInChildren<Text>(true);
                    if (caption != null)
                        hint = SlabLabHints.For(caption.text);
                }
            }
            if (string.IsNullOrEmpty(hint))
            {
                if (hoverHintShown)
                {
                    hoverHintShown = false;
                    noticeText.gameObject.SetActive(false);
                    if (titleText != null)
                        titleText.gameObject.SetActive(true);
                }
                return;
            }
            if (hoverHintShown && noticeText.text == hint)
                return;
            hoverHintShown = true;
            noticeText.text = hint;
            noticeText.gameObject.SetActive(true);
            if (titleText != null)
                titleText.gameObject.SetActive(false);
            // A hover hint has no timer of its own; it lives until the pointer
            // leaves, so the shared expiry is pushed out of the way.
            noticeExpiresAt = float.MaxValue;
        }

        /// <summary>
        /// The "Reset View" button and the X key share one entry point. The
        /// button used to re-fit the volume only, which left the camera where a
        /// stray right-drag had put it and the Field pair where a focus move had
        /// left it — so the button did not actually give the view back.
        /// </summary>
        private void ResetView()
        {
            var locomotion = FindObjectOfType<VolumeSTCubeQuestLocomotion>();
            if (locomotion != null)
            {
                locomotion.ResetDesktopView();
                return;
            }
            if (workbench != null)
                workbench.ResetVolumeLayout();
        }

        /// <summary>
        /// Save what is on screen. ScreenCapture writes at the end of the frame,
        /// so the coroutine waits for the file instead of assuming it is there.
        /// </summary>
        private void SaveSnapshot()
        {
            StartCoroutine(SaveSnapshotRoutine());
        }

        private IEnumerator SaveSnapshotRoutine()
        {
            string folder = SlabLabSnapshot.Folder();
            string path = SlabLabSnapshot.NextPath(DateTime.Now);
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception exception)
            {
                ShowNotice("Snapshot folder is not writable: " +
                    exception.Message);
                Debug.LogWarning("Snapshot failed: " + exception.Message);
                yield break;
            }
            ScreenCapture.CaptureScreenshot(path, 1);
            yield return new WaitForEndOfFrame();
            float deadline = Time.realtimeSinceStartup + 5.0f;
            while (!File.Exists(path) && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (!SlabLabSnapshot.IsPng(path))
            {
                ShowNotice("Snapshot failed. See the log for the path.");
                Debug.LogWarning("Snapshot did not produce a PNG at " + path);
                yield break;
            }
            ShowNotice("Snapshot saved · " + SlabLabSnapshot.FolderName + "/" +
                Path.GetFileName(path));
            Debug.Log("Snapshot saved: " + path + " (" +
                new FileInfo(path).Length + " bytes)");
        }

        /// <summary>
        /// The key the operator pressed this frame, read through the shared
        /// shortcut table so the Help card, the handler and the guards agree.
        /// </summary>
        private static SlabLabShortcut PollShortcut(out KeyCode key)
        {
            for (int index = 0; index < SlabLabShortcuts.Keys.Length; index++)
            {
                KeyCode candidate = SlabLabShortcuts.Keys[index];
                if (!Input.GetKeyDown(candidate))
                    continue;
                key = candidate;
                return SlabLabShortcuts.Map(candidate);
            }
            key = KeyCode.None;
            return SlabLabShortcut.None;
        }

        private void ApplyShortcut(SlabLabShortcut shortcut, KeyCode key)
        {
            switch (shortcut)
            {
                case SlabLabShortcut.ToggleHelp:
                    ToggleHelp();
                    break;
                case SlabLabShortcut.CloseOverlay:
                    if (historyPanel != null)
                        historyPanel.SetActive(false);
                    if (helpPanel != null)
                        helpPanel.SetActive(false);
                    break;
                case SlabLabShortcut.Snapshot:
                    // Same entry point as the button, so a picture is a picture
                    // whichever way the operator asks for it.
                    SaveSnapshot();
                    break;
                case SlabLabShortcut.PreviousStep:
                case SlabLabShortcut.NextStep:
                case SlabLabShortcut.RangeEntry:
                    // An open overlay owns the keyboard, and the workbench
                    // refuses the key itself while a text field is open.
                    if (historyPanel != null && historyPanel.activeSelf)
                        return;
                    if (helpPanel != null && helpPanel.activeSelf)
                        return;
                    if (workbench != null)
                        workbench.DesktopHandleShortcut(key);
                    break;
            }
        }

        public static bool IsPointerOverHud(Vector2 screenPosition)
        {
            if (EventSystem.current == null)
                return false;
            VolumeSTCubeFlatScreenHUD hud = activeHud;
            if (hud == null || hud.hudRaycaster == null)
                return false;
            PointerEventData pointer = new PointerEventData(EventSystem.current)
            {
                position = screenPosition
            };
            List<RaycastResult> results = new List<RaycastResult>();
            hud.hudRaycaster.Raycast(pointer, results);
            return results.Count > 0;
        }

        private void ApplySafeArea()
        {
            if (safeAreaRoot == null || Screen.width <= 0 || Screen.height <= 0)
                return;
            Rect safe = Screen.safeArea;
            safeAreaRoot.anchorMin = new Vector2(
                safe.xMin / Screen.width, safe.yMin / Screen.height);
            safeAreaRoot.anchorMax = new Vector2(
                safe.xMax / Screen.width, safe.yMax / Screen.height);
            safeAreaRoot.offsetMin = Vector2.zero;
            safeAreaRoot.offsetMax = Vector2.zero;
            lastSafeArea = safe;
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        }

        private void OnDestroy()
        {
            Camera camera = Camera.main;
            if (camera != null)
                camera.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
            if (activeHud == this)
                activeHud = null;
        }

        private static GameObject CreatePanel(string name, Transform parent,
            Color color)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform),
                typeof(Image));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = color;
            return panel;
        }

        private static void AnchorTopRow(RectTransform rect, float top,
            float height)
        {
            rect.anchorMin = new Vector2(0.0f, 1.0f);
            rect.anchorMax = new Vector2(1.0f, 1.0f);
            rect.pivot = new Vector2(0.5f, 1.0f);
            rect.anchoredPosition = new Vector2(0.0f, top);
            rect.sizeDelta = new Vector2(0.0f, height);
        }

        private static void Stretch(RectTransform rect, float horizontal,
            float vertical)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(horizontal, vertical);
            rect.offsetMax = new Vector2(-horizontal, -vertical);
        }

        private static Button CreateButton(string label, Transform parent,
            UnityEngine.Events.UnityAction action, float width)
        {
            return CreateButton(label, parent, action, width,
                SecondaryAction);
        }

        private static Button CreateButton(string label, Transform parent,
            UnityEngine.Events.UnityAction action, float width, Color fill)
        {
            GameObject buttonObject = new GameObject(label, typeof(RectTransform),
                typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            Image image = buttonObject.GetComponent<Image>();
            image.color = fill;
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            LayoutElement element = buttonObject.GetComponent<LayoutElement>();
            element.minWidth = Mathf.Min(width, 120.0f);
            element.preferredWidth = width;
            Text text = CreateText(buttonObject.transform, label);
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 22;
            text.fontStyle = FontStyle.Bold;
            Stretch(text.rectTransform, 4.0f, 2.0f);
            return button;
        }

        private static Text CreateText(Transform parent, string content)
        {
            GameObject textObject = new GameObject("Text", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.color = new Color(0.90f, 0.96f, 1.0f, 1.0f);
            text.fontSize = 22;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;
            new GameObject("EventSystem", typeof(EventSystem),
                typeof(StandaloneInputModule));
        }
    }
}
