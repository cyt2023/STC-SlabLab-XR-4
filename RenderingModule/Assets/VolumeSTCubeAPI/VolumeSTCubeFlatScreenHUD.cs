using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Small screen-space shell for the desktop/tablet player. The analytical
    /// workbench remains world-space, while these controls stay reachable on
    /// every aspect ratio and inside tablet safe areas.
    /// </summary>
    public sealed class VolumeSTCubeFlatScreenHUD : MonoBehaviour
    {
        private static VolumeSTCubeFlatScreenHUD activeHud;
        private VolumeSTCubeQuestSpatialWorkbench workbench;
        private RectTransform safeAreaRoot;
        private GameObject helpPanel;
        private GraphicRaycaster hudRaycaster;
        private Rect lastSafeArea;
        private Vector2Int lastScreenSize;

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

            GameObject toolbar = CreatePanel("Toolbar", safeAreaRoot,
                new Color(0.035f, 0.055f, 0.09f, 0.92f));
            RectTransform toolbarRect = toolbar.GetComponent<RectTransform>();
            toolbarRect.anchorMin = new Vector2(0.0f, 1.0f);
            toolbarRect.anchorMax = new Vector2(0.0f, 1.0f);
            toolbarRect.pivot = new Vector2(0.0f, 1.0f);
            toolbarRect.anchoredPosition = new Vector2(20.0f, -20.0f);
            toolbarRect.sizeDelta = new Vector2(480.0f, 76.0f);

            HorizontalLayoutGroup layout = toolbar.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 8.0f;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = true;

            CreateButton("Menu", toolbar.transform, workbench.TogglePanel);
            CreateButton("Reset", toolbar.transform, workbench.ResetVolumeLayout);
            CreateButton("Help", toolbar.transform, ToggleHelp);

            helpPanel = CreatePanel("Help", safeAreaRoot,
                new Color(0.025f, 0.04f, 0.07f, 0.95f));
            RectTransform helpRect = helpPanel.GetComponent<RectTransform>();
            helpRect.anchorMin = new Vector2(0.0f, 1.0f);
            helpRect.anchorMax = new Vector2(0.0f, 1.0f);
            helpRect.pivot = new Vector2(0.0f, 1.0f);
            helpRect.anchoredPosition = new Vector2(20.0f, -108.0f);
            helpRect.sizeDelta = new Vector2(610.0f, 190.0f);

            Text helpText = CreateText(helpPanel.transform,
                "DESKTOP\nLeft click: select / drag    Right drag: look\n" +
                "Wheel: move    WASD: move    Shift: faster\n\n" +
                "TABLET\nOne finger: select / drag    Two fingers: look\n" +
                "Pinch: move    Three fingers: panel grip");
            RectTransform textRect = helpText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(22.0f, 16.0f);
            textRect.offsetMax = new Vector2(-22.0f, -16.0f);
            helpPanel.SetActive(false);

            ApplySafeArea();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.H) || Input.GetKeyDown(KeyCode.F1))
                ToggleHelp();
            if (Input.GetKeyDown(KeyCode.Tab))
                workbench.TogglePanel();

            if (lastSafeArea != Screen.safeArea ||
                lastScreenSize.x != Screen.width || lastScreenSize.y != Screen.height)
                ApplySafeArea();
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

        private void OnDestroy()
        {
            if (activeHud == this)
                activeHud = null;
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

        private static GameObject CreatePanel(string name, Transform parent, Color color)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = color;
            return panel;
        }

        private static void CreateButton(string label, Transform parent,
            UnityEngine.Events.UnityAction action)
        {
            GameObject buttonObject = new GameObject(label, typeof(RectTransform),
                typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.08f, 0.25f, 0.34f, 1.0f);
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            buttonObject.GetComponent<LayoutElement>().minWidth = 130.0f;

            Text text = CreateText(buttonObject.transform, label);
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 25;
            text.fontStyle = FontStyle.Bold;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
        }

        private static Text CreateText(Transform parent, string content)
        {
            GameObject textObject = new GameObject("Text", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.color = new Color(0.88f, 0.95f, 1.0f, 1.0f);
            text.fontSize = 22;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
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
