using System.Collections.Generic;
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
            titleText = CreateText(titleBar, "STEP 1  ·  OPEN A DATASET");
            titleText.fontSize = 29;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleCenter;
            Stretch(titleText.rectTransform, 24.0f, 8.0f);

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
                workbench.ResetVolumeLayout, 170.0f, UtilityAction);
            CreateButton("Help", actionBar, ToggleHelp, 130.0f,
                HelpAction);

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
            statusLayout.minWidth = 540.0f;
            statusLayout.preferredWidth = 540.0f;
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
                workbench.DesktopBeginPivot, 140.0f, HelpAction);
            drillButton = CreateButton("DRILL", bottomRect,
                workbench.DesktopBeginDrill, 140.0f, PrimaryAction);
            rollUpButton = CreateButton("ROLL-UP", bottomRect,
                workbench.DesktopBeginRollUp, 160.0f,
                new Color(0.10f, 0.62f, 0.34f, 1.0f));
            bottomBar.SetActive(false);

            helpPanel = CreatePanel("Help", safeAreaRoot,
                new Color(0.025f, 0.04f, 0.07f, 0.98f));
            RectTransform helpRect = helpPanel.GetComponent<RectTransform>();
            helpRect.anchorMin = new Vector2(0.5f, 0.5f);
            helpRect.anchorMax = new Vector2(0.5f, 0.5f);
            helpRect.pivot = new Vector2(0.5f, 0.5f);
            helpRect.anchoredPosition = new Vector2(0.0f, -55.0f);
            helpRect.sizeDelta = new Vector2(720.0f, 250.0f);
            Text helpText = CreateText(helpRect,
                "DESKTOP\nLeft click: choose or interact    Right drag: look\n" +
                "Wheel: zoom    Previous / Next: guided workflow\n\n" +
                "TABLET\nOne finger: choose or interact    Two fingers: look\n" +
                "Pinch: zoom");
            helpText.fontSize = 23;
            Stretch(helpText.rectTransform, 28.0f, 20.0f);
            helpPanel.SetActive(false);
            ApplySafeArea();
        }

        private void Update()
        {
            if (titleText != null && workbench != null)
            {
                string title = workbench.DesktopWorkflowTitle;
                if (titleText.text != title)
                    titleText.text = title;
            }
            RefreshBottomBar();
            if (Input.GetKeyDown(KeyCode.H) || Input.GetKeyDown(KeyCode.F1))
                ToggleHelp();
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
            intentButton.gameObject.SetActive(workflow);
            fullMatrixButton.gameObject.SetActive(workflow);
            pivotButton.gameObject.SetActive(workflow);
            drillButton.gameObject.SetActive(workflow);
            rollUpButton.gameObject.SetActive(workflow);
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
            SetButtonText(playbackButton, workbench.DesktopPlaybackLabel);
            SetButtonText(speedButton, workbench.DesktopPlaybackSpeedLabel);
        }

        private static void SetWorkflowButtonState(Button button,
            bool available, Color activeColor)
        {
            if (button == null)
                return;
            button.interactable = available;
            Image image = button.GetComponent<Image>();
            if (image != null)
                image.color = available ? activeColor : UtilityAction;
        }

        private void EnsureWorkflowButtons()
        {
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
            if (bottomBar == null || workbench == null)
                return;
            if (intentButton == null)
                intentButton = CreateButton("MATPLOT INTENT", bottomBar.transform,
                    workbench.DesktopOpenIntent, 220.0f, HelpAction);
            if (fullMatrixButton == null)
                fullMatrixButton = CreateButton("FULL MATRIX", bottomBar.transform,
                    workbench.DesktopBuildFullMatrix, 200.0f, ConfirmAction);
            if (pivotButton == null)
                pivotButton = CreateButton("PIVOT", bottomBar.transform,
                    workbench.DesktopBeginPivot, 140.0f, HelpAction);
            if (drillButton == null)
                drillButton = CreateButton("DRILL", bottomBar.transform,
                    workbench.DesktopBeginDrill, 140.0f, PrimaryAction);
            if (rollUpButton == null)
                rollUpButton = CreateButton("ROLL-UP", bottomBar.transform,
                    workbench.DesktopBeginRollUp, 160.0f,
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
                bool intentPrimary = desktopComposer && workbench != null &&
                    workbench.DesktopIntentPanelPrimary;
                // Reuse the proven VR interaction surfaces for the advanced
                // analysis operations. On desktop they are treated as the one
                // central task surface and fitted between the fixed toolbars.
                bool desktopImportedPanel =
                    panel.name == "S4D Anchored Facet Grid" ||
                    panel.name == "AI Findings";
                bool desktopMatrixPanel = workbench != null &&
                    workbench.DesktopMatrixTaskActive &&
                    panel.name == "S4D Anchored Facet Grid";
                bool matrixPrimary = desktopMatrixPanel &&
                    workbench.DesktopMatrixPanelPrimary;
                bool desktopTaskSurface = desktopComposer ||
                    desktopImportedPanel;
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
                        (intentPrimary ? 0.48f : 0.25f)
                    : desktopMatrixPanel
                        ? viewHeight * camera.aspect *
                            (matrixPrimary ? 0.66f : 0.27f)
                        : horizontalWorld;
                float panelVerticalWorld = desktopComposer
                    ? viewHeight * (intentPrimary ? 0.68f : 0.38f)
                    : desktopMatrixPanel
                        ? viewHeight * (matrixPrimary ? 0.76f : 0.40f)
                        : verticalWorld;
                float scale = Mathf.Min(
                    panelHorizontalWorld / Mathf.Max(1.0f, rect.sizeDelta.x),
                    panelVerticalWorld / Mathf.Max(1.0f, rect.sizeDelta.y));
                Vector3 centre = camera.ViewportToWorldPoint(
                    new Vector3(desktopComposer
                            ? intentPrimary ? 0.70f : 0.84f
                            : desktopMatrixPanel
                                ? matrixPrimary ? 0.66f : 0.84f
                                : 0.5f,
                        desktopTaskSurface ? 0.50f :
                            central ? 0.48f : 0.075f,
                        distance));
                panel.transform.position = centre;
                panel.transform.rotation = Quaternion.LookRotation(
                    centre - camera.transform.position, camera.transform.up);
                panel.transform.localScale = Vector3.one * scale;
            }
        }

        private void ToggleHelp()
        {
            if (helpPanel != null)
                helpPanel.SetActive(!helpPanel.activeSelf);
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
            element.minWidth = width;
            element.preferredWidth = width;
            Text text = CreateText(buttonObject.transform, label);
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 24;
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
