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
        private static VolumeSTCubeFlatScreenHUD activeHud;
        private VolumeSTCubeQuestSpatialWorkbench workbench;
        private RectTransform safeAreaRoot;
        private Text titleText;
        private GameObject helpPanel;
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
                workbench.DesktopPreviousStep, 160.0f);
            CreateButton("Next", actionBar,
                workbench.DesktopNextStep, 160.0f);
            CreateButton("Reset View", actionBar,
                workbench.ResetVolumeLayout, 170.0f);
            CreateButton("Help", actionBar, ToggleHelp, 130.0f);

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
                titleText.text = workbench.DesktopWorkflowTitle;
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
            camera.rect = new Rect(0.0f, 0.0f, 1.0f, 0.86f);
            float distance = 2.05f;
            float verticalWorld = 2.0f * distance * Mathf.Tan(
                camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.68f;
            float horizontalWorld = verticalWorld * camera.aspect * 0.86f;
            for (int index = 0; index < workflowPanels.Count; index++)
            {
                Canvas panel = workflowPanels[index];
                if (panel == null || !panel.gameObject.activeInHierarchy)
                    continue;
                RectTransform rect = panel.GetComponent<RectTransform>();
                if (rect == null)
                    continue;
                float scale = Mathf.Min(
                    horizontalWorld / Mathf.Max(1.0f, rect.sizeDelta.x),
                    verticalWorld / Mathf.Max(1.0f, rect.sizeDelta.y));
                Vector3 centre = camera.ViewportToWorldPoint(
                    new Vector3(0.5f, 0.48f, distance));
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

        private static void CreateButton(string label, Transform parent,
            UnityEngine.Events.UnityAction action, float width)
        {
            GameObject buttonObject = new GameObject(label, typeof(RectTransform),
                typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.07f, 0.28f, 0.38f, 1.0f);
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
