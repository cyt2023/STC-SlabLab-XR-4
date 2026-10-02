using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    /// <summary>Split out of VolumeSTCubeQuestSpatialWorkbench: Chrome side of the workbench.</summary>
    public sealed partial class VolumeSTCubeQuestSpatialWorkbench
    {
        private static readonly Color Ink = new Color(0.98f, 0.995f, 1.0f, 1.0f);

        private static readonly Color Panel = new Color(0.010f, 0.022f, 0.036f, 0.975f);

        private static readonly Color Card = new Color(0.036f, 0.063f, 0.090f, 0.985f);

        private static readonly Color Cyan = new Color(0.05f, 0.92f, 1.0f, 1.0f);

        private static readonly Color Amber = new Color(1.0f, 0.62f, 0.13f, 1.0f);

        private static readonly Color Green = new Color(0.2f, 1.0f, 0.55f, 1.0f);

        private static readonly Color Purple = new Color(0.66f, 0.23f, 0.88f, 1.0f);

        private static readonly Color Muted = new Color(0.53f, 0.65f, 0.73f, 1.0f);

        private static readonly Color Danger = new Color(1.0f, 0.28f, 0.28f, 1.0f);

        private static readonly Color TimeColor = new Color(1.0f, 0.70f, 0.0f, 1.0f);

        private static readonly Color DepthColor = new Color(0.0f, 0.72f, 0.83f, 1.0f);

        private static readonly Color DepthAxisColor = new Color(0.34f, 0.56f, 1.0f, 1.0f);

        private static readonly Color HorizontalColor = new Color(0.26f, 0.63f, 0.28f, 1.0f);

        private static readonly Color VariableColor = new Color(0.56f, 0.14f, 0.67f, 1.0f);

        private void CreateSpatialCube()
        {
            spatialRoot = new GameObject("Slab Lab Continuous Cube");
            spatialRoot.transform.SetParent(transform, false);
            spatialRoot.transform.localPosition = new Vector3(-0.90f, 1.47f, 2.35f);
            desktopFieldAuthoredPosition = spatialRoot.transform.position;
            desktopFieldAuthoredScale = spatialRoot.transform.localScale;

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
                {0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},{0,4},{1,5},{2,6},{3,7}
            };
            CreateHolographicFieldFrame(spatialRoot.transform, corners, edges,
                "Primary Field");

            CreateFieldDatasetSelector();
            CreateAnalysisAxes();

            slabObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slabObject.name = "Directly draggable slab";
            slabObject.layer = 5;
            slabObject.transform.SetParent(spatialRoot.transform, false);
            slabObject.transform.localScale = new Vector3(
                FieldHalfWidth * 1.72f, 0.024f, FieldHalfDepth * 1.70f);
            Material slabMaterial = new Material(Shader.Find("Sprites/Default"));
            slabMaterial.color = new Color(0.06f, 0.9f, 1.0f, 0.32f);
            Renderer defaultSlabRenderer = slabObject.GetComponent<Renderer>();
            defaultSlabRenderer.material = slabMaterial;
            // Keep the collider as the depth interaction surface, but remove the
            // old always-visible cyan plate. Author Boundary owns the visible cuts.
            defaultSlabRenderer.enabled = false;
            VolumeSTCubeQuestClickTarget slabTarget = slabObject.AddComponent<VolumeSTCubeQuestClickTarget>();
            slabTarget.Clicked = BeginSlabInteraction;
            CreateDepthBoundaryPlane(0);
            CreateDepthBoundaryPlane(1);
            UpdateDepthBoundaryPlanes();
            SetDepthBoundaryVisibility(false);

            slabPreviewObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            slabPreviewObject.name = "Slab XY data texture";
            Destroy(slabPreviewObject.GetComponent<Collider>());
            slabPreviewObject.transform.SetParent(spatialRoot.transform, false);
            slabPreviewObject.transform.localRotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            slabPreviewObject.transform.localScale =
                new Vector3(FieldHalfWidth * 1.64f, FieldHalfDepth * 1.64f, 1.0f);
            // Sprites/Default is already retained by the controller laser and supports
            // a main texture; Unlit/Texture may be stripped from an Android build.
            slabPreviewMaterial = new Material(Shader.Find("Sprites/Default"));
            slabPreviewObject.GetComponent<Renderer>().material = slabPreviewMaterial;
            slabPreviewObject.SetActive(false);
            CreateDepthInspectionVisuals();

            regionRoot = new GameObject("Selected XY region");
            regionRoot.transform.SetParent(spatialRoot.transform, false);
            for (int i = 0; i < regionLines.Length; i++)
                regionLines[i] = CreateWorldLine("Region edge", regionRoot.transform, Vector3.zero, Vector3.zero, Green, 0.012f);
            regionRoot.SetActive(false);

            CreateTimeRail();
            CreateGroundEvidenceVisuals();
            groundLink = CreateWorldLine("Ground evidence link", transform,
                Vector3.zero, Vector3.zero, Purple, 0.008f);
            groundLink.useWorldSpace = true;
            groundLink.gameObject.SetActive(false);
            for (int index = 0; index < matPlotStcLinkSegments.Length; index++)
            {
                matPlotStcLinkSegments[index] = CreateWorldLine(
                    "MatPlot to STC dashed provenance " + index, transform,
                    Vector3.zero, Vector3.zero, Purple, 0.010f);
                matPlotStcLinkSegments[index].useWorldSpace = true;
                matPlotStcLinkSegments[index].gameObject.SetActive(false);
            }
            UpdateSlabVisual(false);
            UpdateRegionVisual();
            CreateSpatialAxisComposerRoot();
        }

        private static Material CreateStableOpaqueMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            Material material = new Material(shader);
            color.a = 1.0f;
            material.color = color;
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            return material;
        }

        private void CreateDashedRectangle(Transform parent, string name,
            float left, float right, float bottom, float top,
            Color color, float width)
        {
            CreateDashedLocalLine(parent, name + " top",
                new Vector3(left, top, -0.024f),
                new Vector3(right, top, -0.024f), 8, color, width);
            CreateDashedLocalLine(parent, name + " right",
                new Vector3(right, top, -0.024f),
                new Vector3(right, bottom, -0.024f), 10, color, width);
            CreateDashedLocalLine(parent, name + " bottom",
                new Vector3(right, bottom, -0.024f),
                new Vector3(left, bottom, -0.024f), 8, color, width);
            CreateDashedLocalLine(parent, name + " left",
                new Vector3(left, bottom, -0.024f),
                new Vector3(left, top, -0.024f), 10, color, width);
        }

        private void CreateDashedLocalLine(Transform parent, string name,
            Vector3 start, Vector3 end, int dashCount, Color color, float width)
        {
            dashCount = Mathf.Max(1, dashCount);
            for (int index = 0; index < dashCount; index++)
            {
                float from = index / (float)dashCount;
                float to = Mathf.Min(1.0f, from + 0.56f / dashCount);
                CreateWorldLine(name + " " + index, parent,
                    Vector3.Lerp(start, end, from),
                    Vector3.Lerp(start, end, to), color, width);
            }
        }

        private Canvas CreateFloatingCanvas(string name, Vector3 position, Vector2 size, float scale, Color accent)
        {
            GameObject panelObject = new GameObject(name, typeof(RectTransform));
            panelObject.layer = 5;
            panelObject.transform.SetParent(transform, false);
            panelObject.transform.localPosition = position;
            panelObject.transform.localRotation = Quaternion.identity;
            float displayScale = scale;
#if UNITY_EDITOR || SLABLAB_FLAT
            if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
                displayScale *= name == "S4D Anchored Facet Grid" ? 1.24f : 1.12f;
#endif
            panelObject.transform.localScale = Vector3.one * displayScale;

            Canvas canvas = panelObject.AddComponent<Canvas>();
            canvas.renderMode = UnityEngine.RenderMode.WorldSpace;
            canvas.worldCamera = xrCamera;
            canvas.sortingOrder = 120;
            CanvasScaler scaler = panelObject.AddComponent<CanvasScaler>();
            // High-density text atlas for readable world-space UI in Quest 3.
            scaler.dynamicPixelsPerUnit =
#if UNITY_EDITOR || SLABLAB_FLAT
                VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled ? 48.0f :
#endif
                24.0f;
            scaler.referencePixelsPerUnit = 100.0f;
            panelObject.AddComponent<GraphicRaycaster>();

            RectTransform rect = panelObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            Image background = panelObject.AddComponent<Image>();
            background.color = Panel;
            background.sprite = RoundedUiSprite();
            background.type = Image.Type.Sliced;
            Shadow panelShadow = panelObject.AddComponent<Shadow>();
            panelShadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.68f);
            panelShadow.effectDistance = new Vector2(12.0f, -14.0f);
            Outline outline = panelObject.AddComponent<Outline>();
            outline.effectColor = new Color(accent.r, accent.g, accent.b, 0.62f);
            outline.effectDistance = new Vector2(2, -2);

            BoxCollider panelCollider = panelObject.AddComponent<BoxCollider>();
            panelCollider.isTrigger = true;
            panelCollider.center = new Vector3(0.0f, 0.0f, 14.0f);
            panelCollider.size = new Vector3(size.x, size.y, 8.0f);
            panelObject.AddComponent<VolumeSTCubeQuestPanelHandle>().accent = accent;

            GameObject chromeObject = new GameObject("Persistent panel chrome", typeof(RectTransform));
            chromeObject.transform.SetParent(rect, false);
            RectTransform chrome = chromeObject.GetComponent<RectTransform>();
            chrome.anchorMin = Vector2.zero;
            chrome.anchorMax = Vector2.one;
            chrome.sizeDelta = Vector2.zero;
            chrome.anchoredPosition = Vector2.zero;

            GameObject headerWash = new GameObject("Header wash", typeof(RectTransform));
            headerWash.transform.SetParent(chrome, false);
            RectTransform headerRect = headerWash.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0, 1);
            headerRect.anchorMax = new Vector2(1, 1);
            headerRect.pivot = new Vector2(0.5f, 1);
            headerRect.anchoredPosition = new Vector2(0, -5);
            headerRect.sizeDelta = new Vector2(-12, 66);
            Image headerWashImage = headerWash.AddComponent<Image>();
            headerWashImage.sprite = RoundedUiSprite();
            headerWashImage.type = Image.Type.Sliced;
            headerWashImage.color =
                new Color(accent.r * 0.12f, accent.g * 0.12f, accent.b * 0.12f, 0.72f);
            headerWashImage.raycastTarget = false;

            GameObject topRule = new GameObject("Accent rule", typeof(RectTransform));
            topRule.transform.SetParent(chrome, false);
            RectTransform ruleRect = topRule.GetComponent<RectTransform>();
            ruleRect.anchorMin = new Vector2(0, 1);
            ruleRect.anchorMax = new Vector2(1, 1);
            ruleRect.pivot = new Vector2(0.5f, 1);
            ruleRect.anchoredPosition = Vector2.zero;
            ruleRect.sizeDelta = new Vector2(0, 5);
            Image topRuleImage = topRule.AddComponent<Image>();
            topRuleImage.color = accent;
            topRuleImage.raycastTarget = false;

            GameObject sideRule = new GameObject("Accent edge", typeof(RectTransform));
            sideRule.transform.SetParent(chrome, false);
            RectTransform sideRect = sideRule.GetComponent<RectTransform>();
            sideRect.anchorMin = new Vector2(0, 0);
            sideRect.anchorMax = new Vector2(0, 1);
            sideRect.pivot = new Vector2(0, 0.5f);
            sideRect.anchoredPosition = new Vector2(2, 0);
            sideRect.sizeDelta = new Vector2(3, -10);
            Image sideRuleImage = sideRule.AddComponent<Image>();
            sideRuleImage.color = new Color(accent.r, accent.g, accent.b, 0.34f);
            sideRuleImage.raycastTarget = false;
            return canvas;
        }

        private static Sprite RoundedUiSprite()
        {
            if (roundedUiSprite != null)
                return roundedUiSprite;

            const int textureSize = 64;
            const float radius = 15.0f;
            Texture2D texture = new Texture2D(textureSize, textureSize,
                TextureFormat.RGBA32, false);
            texture.name = "S4D Rounded UI";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            Color32[] pixels = new Color32[textureSize * textureSize];
            Vector2 center = new Vector2(textureSize * 0.5f, textureSize * 0.5f);
            Vector2 half = new Vector2(textureSize * 0.5f - radius,
                textureSize * 0.5f - radius);
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    Vector2 point = new Vector2(x + 0.5f, y + 0.5f) - center;
                    Vector2 corner = new Vector2(
                        Mathf.Max(Mathf.Abs(point.x) - half.x, 0.0f),
                        Mathf.Max(Mathf.Abs(point.y) - half.y, 0.0f));
                    float distance = corner.magnitude - radius;
                    byte alpha = (byte)Mathf.RoundToInt(
                        Mathf.Clamp01(0.5f - distance) * 255.0f);
                    pixels[y * textureSize + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            texture.hideFlags = HideFlags.HideAndDontSave;
            roundedUiSprite = Sprite.Create(texture,
                new Rect(0, 0, textureSize, textureSize),
                new Vector2(0.5f, 0.5f), 100.0f, 0, SpriteMeshType.FullRect,
                new Vector4(18, 18, 18, 18));
            roundedUiSprite.name = "S4D Rounded UI Sprite";
            roundedUiSprite.hideFlags = HideFlags.HideAndDontSave;
            return roundedUiSprite;
        }

        private static Image CreateDecorativeSurface(RectTransform parent, string name,
            Vector2 position, Vector2 size, Color color)
        {
            GameObject surfaceObject = new GameObject(name, typeof(RectTransform));
            surfaceObject.transform.SetParent(parent, false);
            RectTransform surface = surfaceObject.GetComponent<RectTransform>();
            surface.sizeDelta = size;
            surface.anchoredPosition = position;
            Image image = surfaceObject.AddComponent<Image>();
            image.sprite = RoundedUiSprite();
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private void CreateMainMenu()
        {
            mainMenuCanvas = CreateFloatingCanvas(
                "S4D Main Menu",
                PrimaryToolDockPosition,
                new Vector2(560, 700),
                0.00072f,
                Cyan);
            mainMenuContent = mainMenuCanvas.GetComponent<RectTransform>();
            BuildMainMenu();
        }

        private void CreateMenuButton(RectTransform parent, string title, string subtitle,
            Vector2 position, Color accent, Action action)
        {
            GameObject cardObject = new GameObject(title, typeof(RectTransform));
            cardObject.layer = 5;
            cardObject.transform.SetParent(parent, false);
            RectTransform card = cardObject.GetComponent<RectTransform>();
            card.sizeDelta = new Vector2(480, 82);
            card.anchoredPosition = position;
            Image image = cardObject.AddComponent<Image>();
            image.sprite = RoundedUiSprite();
            image.type = Image.Type.Sliced;
            image.color = Card;
            Shadow cardShadow = cardObject.AddComponent<Shadow>();
            cardShadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.44f);
            cardShadow.effectDistance = new Vector2(4, -5);
            Outline outline = cardObject.AddComponent<Outline>();
            outline.effectColor = new Color(accent.r, accent.g, accent.b, 0.36f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            GameObject stripeObject = new GameObject("Dimension stripe", typeof(RectTransform));
            stripeObject.transform.SetParent(card, false);
            RectTransform stripe = stripeObject.GetComponent<RectTransform>();
            stripe.anchorMin = new Vector2(0, 0);
            stripe.anchorMax = new Vector2(0, 1);
            stripe.pivot = new Vector2(0, 0.5f);
            stripe.anchoredPosition = Vector2.zero;
            stripe.sizeDelta = new Vector2(7, 0);
            stripeObject.AddComponent<Image>().color = accent;

            CreateText(card, title, 20, FontStyle.Bold, new Vector2(16, 14),
                new Vector2(420, 30), TextAnchor.MiddleLeft, Ink);
            CreateText(card, subtitle, 14, FontStyle.Normal, new Vector2(16, -17),
                new Vector2(420, 24), TextAnchor.MiddleLeft, Muted);
            BoxCollider collider = cardObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(480, 82, 12);
            cardObject.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked = action;
        }

        private static void CreateUiRule(RectTransform parent, Vector2 position,
            Vector2 size, Color color, float angle)
        {
            GameObject ruleObject = new GameObject("Axis rule",
                typeof(RectTransform), typeof(Image));
            ruleObject.layer = 5;
            ruleObject.transform.SetParent(parent, false);
            RectTransform rule = ruleObject.GetComponent<RectTransform>();
            rule.sizeDelta = size;
            rule.anchoredPosition = position;
            rule.localRotation = Quaternion.Euler(0.0f, 0.0f, angle);
            ruleObject.GetComponent<Image>().color = color;
        }

        private static string OperationLabel(DraftOperation operation)
        {
            switch (operation)
            {
                case DraftOperation.Pivot: return "Pivot";
                case DraftOperation.Drill: return "Drill";
                case DraftOperation.RollUp: return "Roll-up";
                default: return "Full Matrix";
            }
        }

        private static Color OperationColor(DraftOperation operation)
        {
            switch (operation)
            {
                case DraftOperation.Pivot: return Purple;
                case DraftOperation.Drill: return TimeColor;
                case DraftOperation.RollUp: return Green;
                default: return Cyan;
            }
        }

        private string CellLabel(int column, int row)
        {
            int timeIndex = DisplayTimeBucketIndex(column, row);
            int depthIndex = DisplayDepthBucketIndex(column, row);
            string columnLabel = ActiveBucketLabel(activeTimeBuckets, timeIndex,
                new[] { "BEFORE", "DURING", "AFTER" });
            string rowLabel = ActiveBucketLabel(activeDepthBuckets, depthIndex,
                new[] { "SURFACE", "MID", "SEAFLOOR" });
            return columnLabel.ToUpperInvariant() + " x " + rowLabel.ToUpperInvariant();
        }

        private static string RoleLabel(DimensionRole role)
        {
            return role == DimensionRole.Fixed ? "FIXED" :
                role == DimensionRole.Faceted ? "FACETED" : "MAPPED";
        }

        private static Color RoleColor(DimensionRole role, Color dimensionColor)
        {
            return role == DimensionRole.Fixed
                ? new Color(dimensionColor.r * 0.45f, dimensionColor.g * 0.45f,
                    dimensionColor.b * 0.45f, 1.0f)
                : role == DimensionRole.Faceted ? dimensionColor :
                new Color(dimensionColor.r, dimensionColor.g, dimensionColor.b, 0.72f);
        }

        private void CreateTrackPreview(RectTransform parent, Vector2 position,
            int dimension)
        {
            S4DIndexBucketRequest[] buckets = DraftSourceBuckets(dimension);
            int count = DraftBucketCount(dimension);
            float width = Mathf.Min(104, 900.0f / count - 6);
            float step = width + 6;
            float start = -(count - 1) * step * 0.5f;
            bool[] selected = dimension == 0
                ? selectedTimeTicks : selectedDepthTicks;
            int[] groups = dimension == 0
                ? timeRollupGroups : depthRollupGroups;
            Color dimensionColor = dimension == 0 ? TimeColor : DepthColor;
            for (int index = 0; index < count; index++)
            {
                int tick = index;
                string label = buckets != null && index < buckets.Length &&
                    !string.IsNullOrWhiteSpace(buckets[index].label)
                        ? buckets[index].label
                        : "bucket " + (index + 1);
                Color color = draftOperation == DraftOperation.RollUp &&
                    groups[index] > 0
                        ? RollupGroupColor(groups[index])
                        : selected[index] ? dimensionColor : Card;
                string prefix = draftOperation == DraftOperation.RollUp &&
                    groups[index] > 0 ? "G" + groups[index] + "  " : string.Empty;
                CreateButton(parent, prefix + label,
                    position + new Vector2(start + index * step, 0),
                    new Vector2(width, 30), color,
                    () => ToggleDraftTick(dimension, tick));
            }
        }

        private static Color RollupGroupColor(int group)
        {
            return group == 1 ? Green : group == 2 ? Purple :
                group == 3 ? Amber : Card;
        }

        private void CreateBucketSummaryGroup(RectTransform parent, float centerX, float centerY,
            string title, string[] labels, Color color, Action editAction)
        {
            CreatePanelCard(parent, new Vector2(centerX, centerY), new Vector2(490, 118), color);
            CreateText(parent, title, 13, FontStyle.Bold,
                new Vector2(centerX - 120, centerY + 40), new Vector2(220, 22),
                TextAnchor.MiddleLeft, color);
            CreateButton(parent, "EDIT CUTS", new Vector2(centerX + 155, centerY + 40),
                SlabLabLayout.RowChipSize, new Color(color.r, color.g, color.b, 0.72f), editAction);

            for (int i = 0; i < 3; i++)
            {
                float x = centerX + (i - 1) * 150;
                CreatePanelCard(parent, new Vector2(x, centerY - 15),
                    new Vector2(136, 54),
                    new Color(color.r, color.g, color.b, 0.72f));
                CreateText(parent, labels[i], 12, FontStyle.Bold,
                    new Vector2(x, centerY - 15), new Vector2(128, 48),
                    TextAnchor.MiddleCenter, Ink);
            }
        }

        private string[] DepthBucketButtonLabels()
        {
            if (authorBoundaryConfirmed && authoredDepthBuckets != null &&
                authoredDepthBuckets.Length == 3)
                return AuthoredBucketButtonLabels(authoredDepthBuckets, false);
            int count = selectedDataset != null ? selectedDataset.DimZ : 3;
            int firstCut = Mathf.Clamp(Mathf.RoundToInt(depthBoundaryLow * count),
                1, Mathf.Max(1, count - 2));
            int secondCut = Mathf.Clamp(Mathf.RoundToInt(depthBoundaryHigh * count),
                firstCut + 1, count - 1);
            return new[]
            {
                "SURFACE\n0-" + (firstCut - 1),
                "MIDDLE\n" + firstCut + "-" + (secondCut - 1),
                "DEEP\n" + secondCut + "-" + (count - 1)
            };
        }

        private static string[] AuthoredBucketButtonLabels(
            S4DIndexBucketRequest[] buckets, bool oneBased)
        {
            string[] labels = new string[buckets.Length];
            for (int index = 0; index < buckets.Length; index++)
            {
                S4DIndexBucketRequest bucket = buckets[index];
                int first = bucket.indices != null && bucket.indices.Length > 0
                    ? bucket.indices[0]
                    : 0;
                int last = bucket.indices != null && bucket.indices.Length > 0
                    ? bucket.indices[bucket.indices.Length - 1]
                    : first;
                if (oneBased)
                {
                    first++;
                    last++;
                }
                labels[index] = bucket.label.ToUpperInvariant() +
                    "\n" + first + "-" + last;
            }
            return labels;
        }

        private void CreatePivotHiddenWash(Vector2 center, Vector2 size)
        {
            GameObject hiddenObject = new GameObject("Hidden pivot row or column",
                typeof(RectTransform), typeof(Image));
            hiddenObject.layer = 5;
            hiddenObject.transform.SetParent(facetGridContent, false);
            RectTransform rect = hiddenObject.GetComponent<RectTransform>();
            rect.anchoredPosition = center;
            rect.sizeDelta = size;
            Image image = hiddenObject.GetComponent<Image>();
            image.sprite = RoundedUiSprite();
            image.type = Image.Type.Sliced;
            image.color = new Color(0.005f, 0.012f, 0.020f, 0.90f);
            image.raycastTarget = false;
            CreateText(rect, "HIDDEN", 13, FontStyle.Bold, Vector2.zero,
                size, TextAnchor.MiddleCenter, Muted);
        }

        private static void CreateDashedRect(RectTransform parent, Vector2 center,
            Vector2 size, Color color, float thickness)
        {
            const float dash = 18.0f;
            const float gap = 10.0f;
            int horizontalCount = Mathf.Max(1,
                Mathf.FloorToInt((size.x + gap) / (dash + gap)));
            int verticalCount = Mathf.Max(1,
                Mathf.FloorToInt((size.y + gap) / (dash + gap)));
            float horizontalStep = size.x / horizontalCount;
            float verticalStep = size.y / verticalCount;
            for (int index = 0; index < horizontalCount; index++)
            {
                float x = -size.x * 0.5f + horizontalStep * (index + 0.5f);
                float width = Mathf.Max(3, horizontalStep - gap);
                CreateOverlayRule(parent, center + new Vector2(x, size.y * 0.5f),
                    new Vector2(width, thickness), color, 0);
                CreateOverlayRule(parent, center + new Vector2(x, -size.y * 0.5f),
                    new Vector2(width, thickness), color, 0);
            }
            for (int index = 0; index < verticalCount; index++)
            {
                float y = -size.y * 0.5f + verticalStep * (index + 0.5f);
                float height = Mathf.Max(3, verticalStep - gap);
                CreateOverlayRule(parent, center + new Vector2(size.x * 0.5f, y),
                    new Vector2(thickness, height), color, 0);
                CreateOverlayRule(parent, center + new Vector2(-size.x * 0.5f, y),
                    new Vector2(thickness, height), color, 0);
            }
        }

        private static void CreateOverlayArrow(RectTransform parent, Vector2 from,
            Vector2 to, Color color)
        {
            Vector2 delta = to - from;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            CreateOverlayRule(parent, (from + to) * 0.5f,
                new Vector2(delta.magnitude, 4), color, angle);
            Vector2 direction = delta.normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x);
            Vector2 head = to;
            CreateOverlayRule(parent, head - direction * 11 + normal * 7,
                new Vector2(21, 4), color, angle + 145);
            CreateOverlayRule(parent, head - direction * 11 - normal * 7,
                new Vector2(21, 4), color, angle - 145);
        }

        private static void CreateOverlayRule(RectTransform parent,
            Vector2 position, Vector2 size, Color color, float angle)
        {
            GameObject ruleObject = new GameObject("Draft guide",
                typeof(RectTransform), typeof(Image));
            ruleObject.layer = 5;
            ruleObject.transform.SetParent(parent, false);
            RectTransform rule = ruleObject.GetComponent<RectTransform>();
            rule.sizeDelta = size;
            rule.anchoredPosition = position;
            rule.localRotation = Quaternion.Euler(0, 0, angle);
            Image image = ruleObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static string ActiveBucketLabel(
            S4DIndexBucketRequest[] buckets, int index, string[] fallback)
        {
            if (buckets != null && index >= 0 && index < buckets.Length &&
                !string.IsNullOrWhiteSpace(buckets[index].label))
                return buckets[index].label;
            return index >= 0 && index < fallback.Length ? fallback[index] : "bucket";
        }

        private void CreateFixedValueGroup(RectTransform parent, float centerX,
            float centerY, string title, string value, Color color,
            Action previous, Action next)
        {
            CreatePanelCard(parent, new Vector2(centerX, centerY),
                new Vector2(490, 118), color);
            CreateText(parent, title + "  /  ONE VALUE", 13, FontStyle.Bold,
                new Vector2(centerX, centerY + 40), new Vector2(430, 22),
                TextAnchor.MiddleCenter, color);
            CreateButton(parent, "-", new Vector2(centerX - 155, centerY - 16),
                new Vector2(92, 54), Card, previous);
            CreatePanelCard(parent, new Vector2(centerX, centerY - 16),
                new Vector2(190, 54), color);
            CreateText(parent, value, 17, FontStyle.Bold,
                new Vector2(centerX, centerY - 16), new Vector2(170, 42),
                TextAnchor.MiddleCenter, Ink);
            CreateButton(parent, "+", new Vector2(centerX + 155, centerY - 16),
                new Vector2(92, 54), color, next);
        }

        private static string[] BucketLabels(
            S4DIndexBucketRequest[] buckets, string[] fallback)
        {
            int count = Mathf.Max(1, buckets != null ? buckets.Length : fallback.Length);
            string[] labels = new string[count];
            for (int index = 0; index < count; index++)
                labels[index] = ActiveBucketLabel(buckets, index, fallback);
            return labels;
        }

        private void CreateTransformSummary()
        {
            CreatePanelCard(panelContent, new Vector2(0, 146), new Vector2(1010, 70), Cyan);
            CreateText(panelContent,
                "VARIABLE  " + (selectedDataset != null
                    ? selectedDataset.Name : "ACTIVE DATASET") + "     " +
                    FacetAxisSummary().ToUpperInvariant() + "     MAPPED HORIZONTAL\n" +
                    "TIME MEAN  EQUAL-FRAME     DEPTH MEAN  VOXEL     " +
                    "MISSING  EXCLUDED     SCALE  SHARED ACROSS GRID",
                12, FontStyle.Bold, new Vector2(0, 146), new Vector2(950, 56),
                TextAnchor.MiddleLeft, Ink);
        }

        private string SelectedTimeRangeLabel()
        {
            int index = DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow);
            if (activeTimeBuckets == null || index < 0 || index >= activeTimeBuckets.Length)
                return "time bucket";
            return BucketRangeLabel(activeTimeBuckets[index], "frames");
        }

        private string SelectedDepthRangeLabel()
        {
            int index = DisplayDepthBucketIndex(selectedGridColumn, selectedGridRow);
            if (activeDepthBuckets == null || index < 0 || index >= activeDepthBuckets.Length)
                return "depth bucket";
            return BucketRangeLabel(activeDepthBuckets[index], "z");
        }

        private static string BucketRangeLabel(S4DIndexBucketRequest bucket, string prefix)
        {
            if (bucket == null || bucket.indices == null || bucket.indices.Length == 0)
                return prefix + " -";
            return prefix + " " + bucket.indices[0] + "-" +
                bucket.indices[bucket.indices.Length - 1];
        }

        private System.Collections.IEnumerator RevealVolumeWhenTexturesReady(
            VolumeSTCubeView targetView)
        {
            if (targetView == null || targetView.rootObject == null)
                yield break;

            SetStatus("Preparing the 3D field texture...");
            float deadline = Time.realtimeSinceStartup + 45.0f;
            bool ready = false;
            Renderer[] renderers =
                targetView.rootObject.GetComponentsInChildren<Renderer>(true);
            VolumeSTCubeRawTimeSeries series =
                targetView.rootObject.GetComponent<VolumeSTCubeRawTimeSeries>();
            int expectedIndex = targetView.config != null
                ? targetView.config.initialTimeIndex
                : selectedTime;
            while (targetView == currentView && Time.realtimeSinceStartup < deadline)
            {
                renderers = targetView.rootObject.GetComponentsInChildren<Renderer>(true);
                bool seriesReady = renderers.Length > 0 && series != null &&
                    series.CurrentIndex == expectedIndex &&
                    !series.IsTransitionPending;
                bool hasVolumeTexture = false;
                for (int index = 0; index < renderers.Length; index++)
                {
                    Renderer renderer = renderers[index];
                    Material material = renderer != null ? renderer.sharedMaterial : null;
                    if (material == null)
                        continue;
                    Texture data = material.HasProperty("_DataTex")
                        ? material.GetTexture("_DataTex")
                        : null;
                    // The Quest DVR path intentionally runs unlit to avoid keeping a
                    // second RGBA 3D gradient texture resident. The scalar texture is
                    // sufficient for the visible continuous-field rendering.
                    if (data is Texture3D)
                    {
                        hasVolumeTexture = true;
                        break;
                    }
                }
                // A view also contains helper/axis renderers without _DataTex.
                // Requiring every renderer to own a Texture3D kept the real STC
                // hidden until the 45-second timeout and left an apparently empty
                // Continuous Field after a variable was bound.
                ready = seriesReady && hasVolumeTexture;
                if (ready)
                    break;
                yield return null;
            }

            if (targetView != currentView)
                yield break;
            for (int index = 0; index < renderers.Length; index++)
                if (renderers[index] != null)
                    renderers[index].enabled = viewState.cubeVisible;
            // The texture exists now, so both Fields can appear together.
            RevealFieldPresentation();
            RefreshVariableFacetStacks();
            FrameVolume();
            // Renderer bounds can settle one frame after the Texture3D upload.
            // A second fit keeps the newly bound field centred in its paired box.
            StartCoroutine(RefitVolumeAfterFrameChange());
            SetStatus(ready
                ? selectedDataset.Name + " ready: " + selectedDataset.TimeCount +
                    " times x " + selectedDataset.DimZ + " depth layers."
                : "3D texture preparation timed out; showing the available field.");
            BuildStage();
        }

        private void CreateHolographicFieldFrame(Transform parent,
            Vector3[] corners, int[,] edges, string prefix)
        {
            // Dyson-inspired construction language: a very quiet structural
            // silhouette plus bright, rounded corner brackets. The Field stays
            // legible without twelve luminous bars crossing nearby workspaces.
            Color ghost = new Color(Cyan.r, Cyan.g, Cyan.b, 0.18f);
            Color bracket = new Color(Cyan.r, Cyan.g, Cyan.b, 0.86f);
            for (int edge = 0; edge < edges.GetLength(0); edge++)
            {
                Vector3 a = corners[edges[edge, 0]];
                Vector3 b = corners[edges[edge, 1]];
                CreateWorldLine(prefix + " soft edge", parent, a, b,
                    ghost, 0.0024f);

                float length = Vector3.Distance(a, b);
                float fraction = Mathf.Clamp(0.19f /
                    Mathf.Max(0.001f, length), 0.10f, 0.24f);
                Vector3 nearA = Vector3.Lerp(a, b, fraction);
                Vector3 nearB = Vector3.Lerp(b, a, fraction);
                CreateWorldLine(prefix + " corner A", parent, a, nearA,
                    bracket, 0.0062f);
                CreateWorldLine(prefix + " corner B", parent, b, nearB,
                    bracket, 0.0062f);
            }
        }

        private void CreatePairedFieldWireFrame(Transform parent,
            float halfWidth, float halfHeight, float halfDepth, int rigIndex,
            string variableName, bool drawOuterFrame)
        {
            Vector3[] corners =
            {
                new Vector3(-halfWidth,-halfHeight,-halfDepth),
                new Vector3(halfWidth,-halfHeight,-halfDepth),
                new Vector3(halfWidth,-halfHeight,halfDepth),
                new Vector3(-halfWidth,-halfHeight,halfDepth),
                new Vector3(-halfWidth,halfHeight,-halfDepth),
                new Vector3(halfWidth,halfHeight,-halfDepth),
                new Vector3(halfWidth,halfHeight,halfDepth),
                new Vector3(-halfWidth,halfHeight,halfDepth)
            };
            int[,] edges =
            {
                {0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},
                {0,4},{1,5},{2,6},{3,7}
            };
            if (drawOuterFrame)
            {
                CreateHolographicFieldFrame(parent, corners, edges,
                    "Paired Field");
#if false // Compact secondary Fields only need frame and variable selector.
                float bottom = -halfHeight + 0.115f;
                float innerDepth = halfDepth - 0.070f;
                float left = -halfWidth + 0.075f;
                CreateWorldLine("Paired field X Time", parent,
                    new Vector3(left, bottom, innerDepth),
                    new Vector3(halfWidth - 0.075f, bottom, innerDepth),
                    TimeColor, 0.009f);
                CreateWorldLine("Paired field Y Variable", parent,
                    new Vector3(left, bottom, -halfDepth + 0.07f),
                    new Vector3(left, bottom, innerDepth),
                    VariableColor, 0.009f);
                CreateWorldLine("Paired field Z Depth", parent,
                    new Vector3(left, -halfHeight + 0.075f, innerDepth),
                    new Vector3(left, halfHeight - 0.075f, innerDepth),
                    DepthAxisColor, 0.010f);
                CreateWorldLabel("X  TIME",
                    new Vector3(0.24f, bottom + 0.040f, innerDepth),
                    0.0072f, TextAnchor.MiddleCenter, TimeColor, parent);
                CreateWorldLabel("Y  VARIABLE",
                    new Vector3(left + 0.025f, bottom + 0.050f,
                        -halfDepth + 0.12f),
                    0.0068f, TextAnchor.LowerLeft, VariableColor, parent);
                CreateWorldLabel("Z  DEPTH",
                    new Vector3(left + 0.035f, halfHeight - 0.055f,
                        innerDepth - 0.020f),
                    0.0068f, TextAnchor.UpperLeft, DepthAxisColor, parent);
#endif
            }

#if false // The clickable selector below already carries this variable name.
            string number = rigIndex >= 0 ? (rigIndex + 1).ToString() : "?";
            CreateWorldLabel(number + "  |  " + variableName.ToUpperInvariant(),
                new Vector3(0.0f, halfHeight - 0.075f, halfDepth), 0.0062f,
                TextAnchor.MiddleCenter, VariableColor, parent);
#endif

            if (rigIndex >= 0 && rigIndex < spatialAxisRigStates.Count)
            {
                int boundVariable = spatialAxisRigStates[rigIndex].boundVariable;
                if (boundVariable >= 0 && boundVariable < datasets.Count)
                {
                    GameObject selector = GameObject.CreatePrimitive(
                        PrimitiveType.Cube);
                    selector.name = variableName + " active Field selector";
                    selector.layer = 5;
                    selector.transform.SetParent(parent, false);
                    selector.transform.localPosition = new Vector3(
                        0.0f, halfHeight - 0.075f, halfDepth + 0.025f);
                    selector.transform.localScale =
                        new Vector3(0.62f, 0.080f, 0.035f);
                    selector.GetComponent<Renderer>().material =
                        CreateStableOpaqueMaterial(new Color(
                            VariableColor.r * 0.28f,
                            VariableColor.g * 0.28f,
                            VariableColor.b * 0.28f, 1.0f));
                    int capturedVariable = boundVariable;
                    selector.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                        () => LoadDataset(capturedVariable);
                    CreatePalettePhysicalText(selector.transform,
                        variableName.ToUpperInvariant(),
                        new Vector3(-0.012f, 0.0f, 0.021f),
                        0.0038f, Ink);
                }
            }

            if (rigIndex < 0 || rigIndex >= spatialAxisRigStates.Count)
                return;
#if false // Mapping is already visible on the shared tri-axis composer.
            SpatialAxisRigState state = spatialAxisRigStates[rigIndex];
            int valueAxis = state.timeAxis >= 0 && state.depthAxis >= 0
                ? RemainingAxis(state.timeAxis, state.depthAxis) : -1;
            string[] names = { "X", "Y", "Z" };
            string timeAxis = state.timeAxis >= 0 ? names[state.timeAxis] : "—";
            string depthAxis = state.depthAxis >= 0 ? names[state.depthAxis] : "—";
            string value = valueAxis >= 0 ? names[valueAxis] : "—";
            CreateWorldLabel("TIME=" + timeAxis + "   DEPTH=" + depthAxis +
                    "   VALUE=" + value,
                new Vector3(0.0f, -halfHeight - 0.065f, halfDepth), 0.0041f,
                TextAnchor.MiddleCenter, Ink, parent);
#endif
        }

        private static int[] CreateIndexRange(int startInclusive, int endExclusive)
        {
            int count = Mathf.Max(0, endExclusive - startInclusive);
            int[] values = new int[count];
            for (int index = 0; index < count; index++)
                values[index] = startInclusive + index;
            return values;
        }

        private Button CreateButton(RectTransform parent, string label, Vector2 position, Vector2 size, Color color, Action action)
        {
            GameObject obj = new GameObject(label, typeof(RectTransform));
            obj.layer = 5;
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Image image = obj.AddComponent<Image>();
            image.sprite = RoundedUiSprite();
            image.type = Image.Type.Sliced;
            bool neutral = IsNeutralControlColor(color);
            Color buttonFill = ThemedButtonFill(color);
            image.color = buttonFill;
            Outline outline = obj.AddComponent<Outline>();
            outline.effectColor = new Color(
                Mathf.Min(1.0f, color.r * 1.18f + 0.08f),
                Mathf.Min(1.0f, color.g * 1.18f + 0.08f),
                Mathf.Min(1.0f, color.b * 1.18f + 0.08f),
                neutral ? 0.34f : 0.70f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            Shadow shadow = obj.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.52f);
            shadow.effectDistance = new Vector2(3.0f, -4.0f);
            Button button = obj.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.13f, 1.13f, 1.13f, 1.0f);
            colors.pressedColor = new Color(0.68f, 0.78f, 0.84f, 1.0f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.38f, 0.44f, 0.48f, 0.72f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            GameObject accentObject = new GameObject("Button accent", typeof(RectTransform));
            accentObject.transform.SetParent(rect, false);
            RectTransform accentRect = accentObject.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0, 0);
            accentRect.anchorMax = new Vector2(1, 0);
            accentRect.pivot = new Vector2(0.5f, 0);
            accentRect.anchoredPosition = new Vector2(0, 1);
            accentRect.sizeDelta = new Vector2(-6, neutral ? 2 : 4);
            Image accentImage = accentObject.AddComponent<Image>();
            accentImage.color = neutral
                ? new Color(Muted.r, Muted.g, Muted.b, 0.34f)
                : new Color(color.r, color.g, color.b, 0.94f);
            accentImage.raycastTarget = false;

            Color labelColor = IdealButtonLabel(buttonFill);
            Text buttonLabel = CreateText(rect, label,
                Mathf.RoundToInt(Mathf.Clamp(size.y * 0.31f, 14, 20)),
                FontStyle.Bold, new Vector2(0, 1),
                size - new Vector2(28, 14), TextAnchor.MiddleCenter,
                labelColor);
            buttonLabel.raycastTarget = false;
            buttonLabel.resizeTextForBestFit = true;
            buttonLabel.resizeTextMinSize = Mathf.RoundToInt(
                12 * ActiveUiFontScale);
            buttonLabel.resizeTextMaxSize = buttonLabel.fontSize;
            buttonLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            buttonLabel.verticalOverflow = VerticalWrapMode.Truncate;
            buttonLabel.lineSpacing = 0.94f;
            BoxCollider collider = obj.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(size.x, size.y, 12);
            VolumeSTCubeQuestClickTarget target = obj.AddComponent<VolumeSTCubeQuestClickTarget>();
            target.Clicked = () => InvokeButtonWithoutInterruptingPlayback(action);
            return button;
        }

        private static bool IsNeutralControlColor(Color color)
        {
            return Mathf.Abs(color.r - Card.r) < 0.02f &&
                Mathf.Abs(color.g - Card.g) < 0.02f &&
                Mathf.Abs(color.b - Card.b) < 0.02f;
        }

        private static Color ThemedButtonFill(Color color)
        {
            Color fill = IsNeutralControlColor(color)
                ? Color.Lerp(Panel, Card, 0.86f)
                : Color.Lerp(Card, color, 0.76f);
            fill.a = 1.0f;
            return fill;
        }

        private static Color IdealButtonLabel(Color fill)
        {
            float luminance = fill.r * 0.2126f + fill.g * 0.7152f +
                fill.b * 0.0722f;
            return luminance > 0.52f
                ? new Color(0.012f, 0.027f, 0.042f, 1.0f)
                : Ink;
        }

        private RawImage CreateRawImage(RectTransform parent, Texture texture, Vector2 position, Vector2 size)
        {
            GameObject frameObject = new GameObject("Data image frame", typeof(RectTransform));
            frameObject.transform.SetParent(parent, false);
            RectTransform frame = frameObject.GetComponent<RectTransform>();
            frame.sizeDelta = size;
            frame.anchoredPosition = position;
            Image frameImage = frameObject.AddComponent<Image>();
            frameImage.sprite = RoundedUiSprite();
            frameImage.type = Image.Type.Sliced;
            frameImage.color = new Color(0.004f, 0.012f, 0.020f, 0.96f);
            frameImage.raycastTarget = false;
            Outline frameOutline = frameObject.AddComponent<Outline>();
            frameOutline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.28f);
            frameOutline.effectDistance = new Vector2(1.5f, -1.5f);

            GameObject obj = new GameObject("Data image", typeof(RectTransform));
            obj.transform.SetParent(frame, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = size - new Vector2(10, 10);
            rect.anchoredPosition = Vector2.zero;
            RawImage image = obj.AddComponent<RawImage>();
            image.texture = texture;
            image.color = Color.white;
            image.raycastTarget = false;
            if (texture != null)
            {
                AspectRatioFitter fitter = obj.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fitter.aspectRatio = texture.width / (float)Mathf.Max(1, texture.height);
            }
            return image;
        }

        private Text CreateTextBox(RectTransform parent, string value, Vector2 position, Vector2 size, int fontSize)
        {
            GameObject obj = new GameObject("Question", typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            obj.AddComponent<Image>().color = new Color(0.008f, 0.017f, 0.028f, 1.0f);
            return CreateText(rect, value, fontSize, FontStyle.Normal, Vector2.zero,
                size - new Vector2(24, 18), TextAnchor.MiddleLeft, Ink);
        }

        private Text CreateText(RectTransform parent, string value, int fontSize, FontStyle style,
            Vector2 position, Vector2 size, TextAnchor anchor, Color color)
        {
            GameObject obj = new GameObject("Text", typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Text text = obj.AddComponent<Text>();
            text.font = font;
            text.text = value;
            float appliedFontScale = ActiveUiFontScale;
#if UNITY_EDITOR || SLABLAB_FLAT
            // Matrix labels live in compact chart/detail cells. The general
            // desktop multiplier made their minimum best-fit size taller than
            // the label rectangles, so Unity truncated the whole string.
            if (stage == Stage.Matrix &&
                VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
                appliedFontScale = UiFontScale * 1.22f;
#endif
            text.fontSize = Mathf.RoundToInt(fontSize * appliedFontScale);
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = color;
            text.lineSpacing = 1.0f;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
#if UNITY_EDITOR || SLABLAB_FLAT
            if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
            {
                // Start from the largest requested desktop size, then let each
                // label shrink only as far as necessary to remain inside its
                // own RectTransform. This prevents both overlap and clipping
                // while avoiding uniformly tiny late-workflow panels.
                text.resizeTextForBestFit = true;
                int desktopMinimum = Mathf.RoundToInt(
                    Mathf.Max(12.0f, fontSize * 0.62f) *
                    appliedFontScale);
                text.resizeTextMinSize = Mathf.Min(text.fontSize,
                    desktopMinimum);
                text.resizeTextMaxSize = text.fontSize;
            }
            else
#endif
            if (fontSize <= 18 && (value.Length > 28 || value.IndexOf('\n') >= 0))
            {
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = Mathf.RoundToInt(
                    11 * appliedFontScale);
                text.resizeTextMaxSize = text.fontSize;
            }
            text.raycastTarget = false;
            if (fontSize >= 24)
            {
                Shadow titleShadow = obj.AddComponent<Shadow>();
                titleShadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.72f);
                titleShadow.effectDistance = new Vector2(2.0f, -2.0f);
            }
            return text;
        }

        private void UpgradeButtonLabelToCrispText(Button button, string value)
        {
            if (button == null)
                return;
            Text legacy = button.GetComponentInChildren<Text>(true);
            if (legacy == null)
                return;
            bool multiline = value.IndexOf('\n') >= 0;
            AddCrispTextOverlay(legacy, value,
                multiline ? 38.0f : 56.0f,
                multiline ? 22.0f : 26.0f,
                true);
        }

        private void UpgradeCanvasLabelsToCrispText(RectTransform root,
            string primaryValue)
        {
            if (root == null)
                return;
            Text[] labels = root.GetComponentsInChildren<Text>(true);
            for (int index = 0; index < labels.Length; index++)
            {
                Text legacy = labels[index];
                if (legacy == null || !legacy.enabled)
                    continue;
                bool primary = !string.IsNullOrEmpty(primaryValue) &&
                    string.Equals(legacy.text, primaryValue,
                        StringComparison.Ordinal);
                Button ownerButton = legacy.GetComponentInParent<Button>();
                float height = Mathf.Max(18.0f,
                    legacy.rectTransform.rect.height);
                float maximum = primary
                    ? 64.0f
                    : ownerButton != null
                        ? Mathf.Clamp(height * 0.84f, 30.0f, 58.0f)
                        : Mathf.Clamp(height * 0.88f, 22.0f, 58.0f);
                float minimum = primary ? 38.0f :
                    ownerButton != null ? 18.0f : 14.0f;
                AddCrispTextOverlay(legacy, legacy.text, maximum, minimum,
                    ownerButton != null || primary);
            }
        }

        private TMPro.TextMeshProUGUI AddCrispTextOverlay(Text legacy, string value,
            float maximumSize, float minimumSize, bool bold)
        {
            if (legacy == null || legacy.transform.parent == null)
                return null;
            legacy.enabled = false;

            GameObject labelObject = new GameObject("Crisp SDF label",
                typeof(RectTransform));
            labelObject.layer = legacy.gameObject.layer;
            labelObject.transform.SetParent(legacy.rectTransform, false);
            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(3.0f, 2.0f);
            rect.offsetMax = new Vector2(-3.0f, -2.0f);

            TMPro.TextMeshProUGUI text =
                labelObject.AddComponent<TMPro.TextMeshProUGUI>();
            if (crispFontAsset != null)
                text.font = crispFontAsset;
            else if (TMPro.TMP_Settings.defaultFontAsset != null)
                text.font = TMPro.TMP_Settings.defaultFontAsset;
            text.text = value;
            text.color = legacy.color;
            text.fontStyle = bold || legacy.fontStyle == FontStyle.Bold
                ? TMPro.FontStyles.Bold : TMPro.FontStyles.Normal;
            text.alignment = ToTmpAlignment(legacy.alignment);
            text.enableWordWrapping = value.IndexOf('\n') >= 0;
            text.overflowMode = TMPro.TextOverflowModes.Truncate;
            text.enableAutoSizing = true;
            text.fontSizeMin = Mathf.Min(minimumSize, maximumSize);
            text.fontSizeMax = maximumSize;
            text.lineSpacing = value.IndexOf('\n') >= 0 ? -18.0f : 0.0f;
            text.raycastTarget = false;
            return text;
        }

        private static void ConfigureAlwaysVisibleMaterial(Material material,
            int renderQueue)
        {
            if (material == null)
                return;
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest",
                (int)UnityEngine.Rendering.CompareFunction.Always);
            material.SetInt("unity_GUIZTestMode",
                (int)UnityEngine.Rendering.CompareFunction.Always);
            material.renderQueue = renderQueue;
        }

        private LineRenderer CreateWorldLine(string name, Transform parent, Vector3 a, Vector3 b, Color color, float width)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            LineRenderer line = obj.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, a);
            line.SetPosition(1, b);
            line.startWidth = line.endWidth = width;
            line.numCapVertices = 10;
            line.numCornerVertices = 10;
            line.startColor = line.endColor = color;
            Material material = new Material(Shader.Find("Sprites/Default"));
            material.color = color;
            line.material = material;
            return line;
        }

        private TextMesh CreateWorldLabel(string value, Vector3 localPosition, float characterSize,
            TextAnchor anchor, Color color, Transform customParent = null)
        {
            GameObject obj = new GameObject(value);
            obj.transform.SetParent(customParent != null ? customParent : spatialRoot.transform, false);
            obj.transform.localPosition = localPosition;
            obj.transform.localRotation = Quaternion.identity;
            TextMesh text = obj.AddComponent<TextMesh>();
            // TextMesh requires a matching legacy font material. Keep spatial
            // axis labels on Unity's proven built-in face; Poppins remains the
            // shared SDF/UGUI face for panels, buttons, and MatPlot output.
            text.font = worldFont != null
                ? worldFont
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            // Rasterize well above the final physical size so small axis and
            // variable captions remain sharp instead of becoming a few blurred
            // pixels in the desktop Game view.
            float readableScale = 1.0f;
#if UNITY_EDITOR || SLABLAB_FLAT
            if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
                readableScale = characterSize <= 0.010f ? 1.70f : 1.18f;
#endif
            text.fontSize = 256;
            text.characterSize = characterSize * readableScale * 0.25f;
            text.anchor = anchor;
            text.alignment = TextAlignment.Center;
            text.color = color;
            return text;
        }

        private static void SetLine(LineRenderer line, Vector3 a, Vector3 b)
        {
            if (line == null)
                return;
            line.SetPosition(0, a);
            line.SetPosition(1, b);
        }

        private static void DestroyTextures(Texture2D[] textures)
        {
            if (textures == null)
                return;
            for (int i = 0; i < textures.Length; i++)
            {
                if (textures[i] != null)
                    Destroy(textures[i]);
                textures[i] = null;
            }
        }

        private static void ClearChildren(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (child.name == "Persistent panel chrome")
                    continue;
                // Destroy is deferred until the end of the frame. Disable the
                // old control immediately so a rebuilt panel cannot be covered
                // or clicked through its previous buttons for one frame.
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }
    }
}
