using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    /// <summary>Split out of VolumeSTCubeQuestSpatialWorkbench: Palette side of the workbench.</summary>
    public sealed partial class VolumeSTCubeQuestSpatialWorkbench
    {
        private void CreateSpatialComponentPalette()
        {
            if (spatialAxisRigStates.Count == 0 ||
                spatialAxisRigStates[0].root == null)
                return;
            SpatialAxisRigState state = spatialAxisRigStates[0];
            variablePaletteTokens.Clear();
            variablePaletteLabels.Clear();
            variableFixedRoleButton = null;
            variableFacetedRoleButton = null;
            variableSharedScopeButton = null;
            variableCustomScopeButton = null;
            variablePaletteCollapseButton = null;

            // VARIABLE uses the same physical pill, label hierarchy, material,
            // and hit target as TIME/DEPTH.  Keeping the caption as a child of
            // the pill prevents it from disappearing when the token moves.
            GameObject palette = GameObject.CreatePrimitive(
                PrimitiveType.Sphere);
            palette.name = "VARIABLE draggable axis token";
            palette.layer = 5;
            palette.transform.SetParent(state.root.transform, false);
            variablePaletteRoot = palette;
            variablePaletteExpandedRoot = null;

            Vector3 variablePosition = state.variableAxis >= 0
                ? AxisRigOrigin + AxisSlotDirection(state.variableAxis) *
                    AxisRigLength
                : UnboundVariableTokenPosition;
            palette.transform.localPosition = variablePosition;
            palette.transform.localScale = new Vector3(
                0.205f, 0.082f, 0.054f);
            palette.GetComponent<Renderer>().material =
                CreateStableOpaqueMaterial(new Color(
                    VariableColor.r, VariableColor.g, VariableColor.b, 1.0f));
            palette.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked = () =>
                BeginPaletteComponentDrag(palette,
                    PaletteComponentKind.VariableAxis, -1);
            TextMesh variableLabel = CreateWorldLabel("VARIABLE",
                Vector3.back * 0.038f, 0.0049f,
                TextAnchor.MiddleCenter, Ink, palette.transform);
            variableLabel.fontStyle = FontStyle.Bold;
            variableLabel.transform.localScale =
                new Vector3(4.88f, 12.2f, 18.5f);
            state.variableToken = palette;

            // Only after VARIABLE has been snapped to an axis does its compact
            // categorical selector unfold from the purple token.
            if (state.variableAxis >= 0)
                CreateVariableSelectionPanel(state);
        }

        private void CreateVariablePaletteCollapseButton()
        {
            if (variablePaletteRoot == null)
                return;
            GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cube);
            button.name = "Variable palette collapse toggle";
            button.layer = 5;
            button.transform.SetParent(variablePaletteRoot.transform, false);
            button.transform.localScale = new Vector3(0.046f, 0.046f, 0.024f);
            button.GetComponent<Renderer>().material =
                CreateStableOpaqueMaterial(VariableColor);
            button.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                ToggleVariablePaletteCollapsed;
            variablePaletteCollapseButton = button;
            ApplyVariablePaletteCollapsedState();
        }

        private void ToggleVariablePaletteCollapsed()
        {
            if (draggedPaletteToken != null)
                return;
            viewState.variablePaletteCollapsed = !viewState.variablePaletteCollapsed;
            ApplyVariablePaletteCollapsedState();
            SetStatus(viewState.variablePaletteCollapsed
                ? "Variables collapsed. Select the purple VAR tile to reopen."
                : "Variables expanded.");
        }

        private void ApplyVariablePaletteCollapsedState()
        {
            if (variablePaletteExpandedRoot != null)
                variablePaletteExpandedRoot.SetActive(!viewState.variablePaletteCollapsed);
            if (variablePaletteCollapseButton == null)
                return;
            Transform button = variablePaletteCollapseButton.transform;
            button.localPosition = viewState.variablePaletteCollapsed
                ? new Vector3(0.0f, 0.0f, 0.010f)
                : new Vector3(0.126f, variablePaletteHeight * 0.5f - 0.024f,
                    0.010f);
            for (int child = button.childCount - 1; child >= 0; child--)
                Destroy(button.GetChild(child).gameObject);
            CreatePalettePhysicalText(button,
                viewState.variablePaletteCollapsed ? "VAR" : "HIDE",
                new Vector3(-0.006f, 0.0f, 0.020f), 0.0030f, Ink);
        }

        private GameObject CreatePhysicalPaletteLabel(Transform parent,
            string label, Vector3 position, float characterSize, Color color)
        {
            return CreatePalettePhysicalText(parent, label, position,
                characterSize, color);
        }

        private void CreatePaletteComponent(Transform parent, string label,
            Color color, Vector2 position, PaletteComponentKind kind,
            int variableIndex)
        {
            // Use the exact physical-object path used by TIME and DEPTH. A UI
            // Button clone depends on Canvas rebuild and the dynamic font atlas;
            // it could be correct for one pickup frame and disappear on the next.
            // This cube and TextMesh are one persistent object that is moved
            // directly during drag and returned to this same slot on release.
            GameObject token = new GameObject(label + " palette component");
            token.transform.SetParent(parent, false);
            token.transform.localPosition = new Vector3(
                position.x, position.y, 0.010f);
            token.transform.localRotation = Quaternion.identity;
            token.transform.localScale = Vector3.one;
            GameObject card = GameObject.CreatePrimitive(PrimitiveType.Cube);
            card.name = label + " physical card";
            card.layer = 5;
            card.transform.SetParent(token.transform, false);
            card.transform.localPosition = Vector3.zero;
            card.transform.localRotation = Quaternion.identity;
            card.transform.localScale = new Vector3(0.158f, 0.046f, 0.030f);
            if (variableDragBackingMaterial == null)
                variableDragBackingMaterial = CreateStableOpaqueMaterial(
                    new Color(color.r, color.g, color.b, 1.0f));
            card.GetComponent<Renderer>().sharedMaterial =
                variableDragBackingMaterial;
            PaletteComponentKind capturedKind = kind;
            int capturedVariable = variableIndex;
            card.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                () => BeginPaletteComponentDrag(token, capturedKind,
                    capturedVariable);
            if (kind == PaletteComponentKind.Variable)
                variablePaletteTokens[variableIndex] = token;
            CreatePalettePhysicalText(token.transform, label,
                new Vector3(-0.012f, 0.0f, 0.022f), 0.0072f, Ink);
        }

        private GameObject CreatePalettePhysicalText(Transform parent,
            string value, Vector3 localPosition, float characterSize,
            Color color, float requestedMaximumWidth = 0.0f,
            float requestedMaximumHeight = 0.0f)
        {
            int pixelColumns = Mathf.Max(5, value.Length * 6 - 1);
            float physicalTextScale = 1.0f;
            float maximumHeight = requestedMaximumHeight > 0.0f
                ? requestedMaximumHeight : 0.028f;
            float maximumWidth = requestedMaximumWidth > 0.0f
                ? requestedMaximumWidth : 0.120f;
#if UNITY_EDITOR || SLABLAB_FLAT
            if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
            {
                physicalTextScale = 1.55f;
                if (requestedMaximumHeight <= 0.0f)
                    maximumHeight = 0.038f;
                if (requestedMaximumWidth <= 0.0f)
                    maximumWidth = 0.145f;
            }
#endif
            float height = Mathf.Clamp(characterSize * 4.2f *
                physicalTextScale, 0.010f, maximumHeight);
            float pixelHeight = height / 7.0f;
            float pixelWidth = Mathf.Min(pixelHeight,
                maximumWidth / pixelColumns);
            float left = -pixelColumns * pixelWidth * 0.5f;
            float bottom = -height * 0.5f;
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            for (int index = 0; index < value.Length; index++)
            {
                string[] glyph = PaletteGlyph(value[index]);
                for (int row = 0; row < 7; row++)
                {
                    for (int column = 0; column < 5; column++)
                    {
                        if (glyph[row][column] != '1')
                            continue;
                        float x0 = left + (index * 6 + column) * pixelWidth;
                        float x1 = x0 + pixelWidth * 0.82f;
                        float y1 = bottom + (7 - row) * pixelHeight;
                        float y0 = y1 - pixelHeight * 0.82f;
                        int start = vertices.Count;
                        vertices.Add(new Vector3(x0, y0, 0.0f));
                        vertices.Add(new Vector3(x1, y0, 0.0f));
                        vertices.Add(new Vector3(x1, y1, 0.0f));
                        vertices.Add(new Vector3(x0, y1, 0.0f));
                        // Both windings make the glyph visible from either side
                        // of the moving spatial palette.
                        triangles.Add(start);
                        triangles.Add(start + 1);
                        triangles.Add(start + 2);
                        triangles.Add(start);
                        triangles.Add(start + 2);
                        triangles.Add(start + 3);
                        triangles.Add(start + 2);
                        triangles.Add(start + 1);
                        triangles.Add(start);
                        triangles.Add(start + 3);
                        triangles.Add(start + 2);
                        triangles.Add(start);
                    }
                }
            }
            Mesh mesh = new Mesh();
            mesh.name = value + " stable pixel caption mesh";
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            GameObject caption = new GameObject(value + " stable pixel caption",
                typeof(MeshFilter), typeof(MeshRenderer));
            caption.name = value + " stable pixel caption";
            caption.layer = 5;
            caption.transform.SetParent(parent, false);
            caption.transform.localPosition = localPosition;
            caption.transform.localRotation = Quaternion.Euler(
                0.0f, 180.0f, 0.0f);
            caption.transform.localScale = Vector3.one;
            caption.GetComponent<MeshFilter>().sharedMesh = mesh;
            Material material = CreateStableOpaqueMaterial(color);
            material.name = value + " stable pixel caption material";
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", 0.0f);
            if (material.HasProperty("_ZTest"))
                material.SetFloat("_ZTest",
                    (float)UnityEngine.Rendering.CompareFunction.Always);
            material.renderQueue = 4000;
            caption.GetComponent<Renderer>().material = material;
            caption.GetComponent<Renderer>().sortingOrder = 32760;
            return caption;
        }

        private static string[] PaletteGlyph(char raw)
        {
            char c = char.ToUpperInvariant(raw);
            switch (c)
            {
                case 'A': return new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" };
                case 'B': return new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" };
                case 'C': return new[] { "01111", "10000", "10000", "10000", "10000", "10000", "01111" };
                case 'D': return new[] { "11110", "10001", "10001", "10001", "10001", "10001", "11110" };
                case 'E': return new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" };
                case 'F': return new[] { "11111", "10000", "10000", "11110", "10000", "10000", "10000" };
                case 'G': return new[] { "01111", "10000", "10000", "10111", "10001", "10001", "01111" };
                case 'H': return new[] { "10001", "10001", "10001", "11111", "10001", "10001", "10001" };
                case 'I': return new[] { "11111", "00100", "00100", "00100", "00100", "00100", "11111" };
                case 'J': return new[] { "00111", "00010", "00010", "00010", "10010", "10010", "01100" };
                case 'K': return new[] { "10001", "10010", "10100", "11000", "10100", "10010", "10001" };
                case 'L': return new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" };
                case 'M': return new[] { "10001", "11011", "10101", "10101", "10001", "10001", "10001" };
                case 'N': return new[] { "10001", "11001", "10101", "10011", "10001", "10001", "10001" };
                case 'O': return new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" };
                case 'P': return new[] { "11110", "10001", "10001", "11110", "10000", "10000", "10000" };
                case 'Q': return new[] { "01110", "10001", "10001", "10001", "10101", "10010", "01101" };
                case 'R': return new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" };
                case 'S': return new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" };
                case 'T': return new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" };
                case 'U': return new[] { "10001", "10001", "10001", "10001", "10001", "10001", "01110" };
                case 'V': return new[] { "10001", "10001", "10001", "10001", "10001", "01010", "00100" };
                case 'W': return new[] { "10001", "10001", "10001", "10101", "10101", "10101", "01010" };
                case 'X': return new[] { "10001", "10001", "01010", "00100", "01010", "10001", "10001" };
                case 'Y': return new[] { "10001", "10001", "01010", "00100", "00100", "00100", "00100" };
                case 'Z': return new[] { "11111", "00001", "00010", "00100", "01000", "10000", "11111" };
                case '0': return new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" };
                case '1': return new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" };
                case '2': return new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" };
                case '3': return new[] { "11110", "00001", "00001", "01110", "00001", "00001", "11110" };
                case '4': return new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" };
                case '5': return new[] { "11111", "10000", "10000", "11110", "00001", "00001", "11110" };
                case '6': return new[] { "01110", "10000", "10000", "11110", "10001", "10001", "01110" };
                case '7': return new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" };
                case '8': return new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" };
                case '9': return new[] { "01110", "10001", "10001", "01111", "00001", "00001", "01110" };
                case '-': return new[] { "00000", "00000", "00000", "11111", "00000", "00000", "00000" };
                case '/': return new[] { "00001", "00010", "00010", "00100", "01000", "01000", "10000" };
                case '_': return new[] { "00000", "00000", "00000", "00000", "00000", "00000", "11111" };
                case ' ': return new[] { "00000", "00000", "00000", "00000", "00000", "00000", "00000" };
                default: return new[] { "01110", "10001", "00010", "00100", "00100", "00000", "00100" };
            }
        }

        private TMPro.TextMeshProUGUI CreatePaletteForegroundLabel(Transform parent,
            string value, Vector3 localPosition, float characterSize,
            float uniformScale, Color color)
        {
            // Use the same world-space UI rendering path as the legible workflow
            // toolbar. TextMesh is unreliable here because the palette cards use
            // thin, non-uniformly scaled 3D primitives.
            GameObject canvasObject = new GameObject(value + " surface label",
                typeof(RectTransform), typeof(Canvas));
            canvasObject.layer = 5;
            canvasObject.transform.SetParent(parent, false);
            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.localPosition = localPosition;
            // The palette holder faces its physical cards toward the viewer;
            // flip the one-sided UI surface back toward that same viewer.
            canvasRect.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);
            float labelWidth = Mathf.Clamp(value.Length * 22.0f, 130.0f, 430.0f);
            canvasRect.sizeDelta = new Vector2(labelWidth, 62.0f);
            float surfaceScale = 0.001f * Mathf.Clamp(
                uniformScale / 2.25f, 0.82f, 1.18f);
            // The physical card is deliberately non-uniformly scaled. Cancel
            // that parent scale so the child Canvas keeps square glyphs and a
            // constant readable world size both in the palette and while held.
            Vector3 parentScale = parent.localScale;
            canvasRect.localScale = new Vector3(
                surfaceScale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
                surfaceScale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
                surfaceScale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));

            Canvas labelCanvas = canvasObject.GetComponent<Canvas>();
            labelCanvas.renderMode = UnityEngine.RenderMode.WorldSpace;
            labelCanvas.worldCamera = xrCamera;
            labelCanvas.overrideSorting = true;
            labelCanvas.sortingOrder = 32760;

            GameObject textObject = new GameObject(value, typeof(RectTransform));
            textObject.layer = 5;
            textObject.transform.SetParent(canvasRect, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            TMPro.TextMeshProUGUI text =
                textObject.AddComponent<TMPro.TextMeshProUGUI>();
            text.font = crispFontAsset != null
                ? crispFontAsset : TMPro.TMP_Settings.defaultFontAsset;
            text.text = value;
            text.fontSize = Mathf.Clamp(characterSize * 6500.0f, 24.0f, 44.0f);
            text.fontStyle = TMPro.FontStyles.Bold;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.overflowMode = TMPro.TextOverflowModes.Overflow;
            text.color = color;
            text.raycastTarget = false;
            Material foregroundMaterial = new Material(text.fontSharedMaterial);
            foregroundMaterial.name = value + " palette foreground font";
            if (foregroundMaterial.HasProperty("_CullMode"))
                foregroundMaterial.SetFloat("_CullMode", 0.0f);
            if (foregroundMaterial.HasProperty("_ZTestMode"))
                foregroundMaterial.SetFloat("_ZTestMode",
                    (float)UnityEngine.Rendering.CompareFunction.Always);
            foregroundMaterial.renderQueue = 4000;
            text.fontMaterial = foregroundMaterial;
            text.canvasRenderer.cullTransparentMesh = false;
            text.ForceMeshUpdate(true, true);
            return text;
        }

        private void BeginPaletteComponentDrag(GameObject token,
            PaletteComponentKind kind, int variableIndex)
        {
            if (token == null || rayInteractor == null)
                return;
            CancelStaleAxisDragForPalette();
            CancelStalePaletteDrag();
            draggedPaletteKind = kind;
            draggedPaletteVariable = variableIndex;
            interaction.draggedPaletteUsesDesktopPointer = false;
#if UNITY_EDITOR || SLABLAB_FLAT
            interaction.draggedPaletteUsesDesktopPointer =
                VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled;
#endif
            bool pointerHeld = interaction.draggedPaletteUsesDesktopPointer
                ? FlatPointerHeld
                : rayInteractor.TriggerHeld;
            interaction.draggedPaletteSawTriggerHeld = pointerHeld;
            draggedPaletteStartTime = Time.unscaledTime;
            // Detach every palette component while it is held. The palette
            // itself follows the viewer and is scaled as a panel; leaving TIME
            // or DEPTH parented to it made that parent update cancel most of a
            // desktop mouse drag. Variables already used this detached path,
            // which is why they remained draggable while TIME appeared stuck.
            draggedPaletteSourceToken = token;
            draggedPaletteOriginalParent = token.transform.parent;
            draggedPaletteOriginalLocalPosition = token.transform.localPosition;
            draggedPaletteOriginalLocalRotation = token.transform.localRotation;
            draggedPaletteOriginalLocalScale = token.transform.localScale;
            token.transform.SetParent(null, true);
            draggedPaletteToken = token;
            draggedPaletteRestScale = draggedPaletteToken.transform.localScale;
            Ray pointerRay = PaletteDragPointerRay();
            draggedPaletteDistance = Mathf.Clamp(Vector3.Distance(
                pointerRay.origin,
                draggedPaletteToken.transform.position),
                0.35f, 2.6f);
            draggedPaletteStartRayPoint = pointerRay.origin +
                pointerRay.direction * draggedPaletteDistance;
            SetStatus(kind == PaletteComponentKind.Variable
                ? "Drag the variable onto a translucent outer frame."
                : kind == PaletteComponentKind.VariableAxis
                    ? "Drag VARIABLE onto an axis. Its variable selector will open after it snaps."
                : "Drag " + kind.ToString().ToUpperInvariant() +
                    " onto X, Y, or downward Z; the endpoint will glow and snap.");
        }

        private void UpdatePaletteComponentInteraction()
        {
            if (draggedPaletteToken == null || rayInteractor == null)
                return;
            float dragAge = Time.unscaledTime - draggedPaletteStartTime;
            Ray pointerRay = PaletteDragPointerRay();
            bool pointerHeld = rayInteractor.TriggerHeld;
            bool pointerReleased = rayInteractor.TriggerReleased;
#if UNITY_EDITOR || SLABLAB_FLAT
            if (interaction.draggedPaletteUsesDesktopPointer)
            {
                pointerHeld = FlatPointerHeld;
                pointerReleased = FlatPointerReleased;
            }
#endif
            // The interactor and workbench can update in either order. Keep a
            // short pickup grace window so the first moving frame is never lost
            // while TriggerHeld is being handed over.
            bool shouldFollow = pointerHeld || dragAge <= 0.14f;
            if (shouldFollow)
            {
                if (pointerHeld)
                    interaction.draggedPaletteSawTriggerHeld = true;
                Vector3 target = pointerRay.origin +
                    pointerRay.direction * draggedPaletteDistance;
                if (draggedPaletteKind == PaletteComponentKind.Variable)
                {
                    int rigIndex = NearestRigFrameFromRay(pointerRay,
                        out float distance, out Vector3 snap);
                    HighlightVariableFrame(rigIndex, distance < 0.34f);
                    if (rigIndex >= 0 && distance < 0.34f)
                        // Keep the card under the user's ray.  A strong snap here
                        // put it directly behind the DROP VARIABLE header and
                        // made the drag appear to vanish.
                        target = Vector3.Lerp(target, snap, 0.16f);
                }
                else
                {
                    FindNearestRigSlotFromRay(pointerRay,
                        out int rigIndex, out int slot, out float distance,
                        out Vector3 snap);
                    HighlightSpatialDropSlot(rigIndex, slot, distance < 0.19f);
                    if (rigIndex >= 0 && distance < 0.19f)
                        target = Vector3.Lerp(target, snap, 0.86f);
                }
                // Match the TIME/DEPTH token's responsive pickup and scale
                // animation so all three component types feel identical.
                float follow = 1.0f - Mathf.Exp(-20.0f * Time.unscaledDeltaTime);
                draggedPaletteToken.transform.position = Vector3.Lerp(
                    draggedPaletteToken.transform.position, target, follow);
                if (draggedPaletteKind == PaletteComponentKind.Variable &&
                    xrCamera != null)
                {
                    Vector3 facing = draggedPaletteToken.transform.position -
                        xrCamera.transform.position;
                    Vector3 uprightFacing = Vector3.ProjectOnPlane(
                        facing, Vector3.up);
                    if (uprightFacing.sqrMagnitude > 0.001f)
                    {
                        Quaternion targetRotation = Quaternion.LookRotation(
                            -uprightFacing.normalized, Vector3.up);
                        draggedPaletteToken.transform.rotation =
                            Quaternion.Slerp(
                                draggedPaletteToken.transform.rotation,
                                targetRotation,
                                1.0f - Mathf.Exp(-16.0f *
                                    Time.unscaledDeltaTime));
                    }
                }
                if (draggedPaletteKind == PaletteComponentKind.Variable)
                {
                    // The source palette card and moving physical card now keep
                    // the same apparent size. TIME/DEPTH can use their existing
                    // lift animation; variables must not jump larger and cover
                    // their own caption at pickup.
                    draggedPaletteToken.transform.localScale = Vector3.Lerp(
                        draggedPaletteToken.transform.localScale,
                        draggedPaletteRestScale * 1.04f,
                        1.0f - Mathf.Exp(-14.0f * Time.unscaledDeltaTime));
                }
                else
                {
                    Vector3 liftedScale = draggedPaletteRestScale * 1.22f;
                    draggedPaletteToken.transform.localScale = Vector3.Lerp(
                        draggedPaletteToken.transform.localScale, liftedScale,
                        1.0f - Mathf.Exp(-16.0f * Time.unscaledDeltaTime));
                }
            }
            // TriggerReleased is only true for one Update on the interactor.
            // Script execution order can otherwise make the workbench miss it,
            // leaving a drag permanently stuck.  Once this drag has observed a
            // held trigger, a not-held state is an unambiguous release.
            bool released = pointerReleased ||
                (interaction.draggedPaletteSawTriggerHeld && !pointerHeld) ||
                (!interaction.draggedPaletteSawTriggerHeld && !pointerHeld &&
                    dragAge > 0.32f);
            if (!released)
                return;

            if (draggedPaletteKind == PaletteComponentKind.Variable)
            {
                int rigIndex = NearestRigFrameFromRay(pointerRay,
                    out float distance, out Vector3 ignored);
                Vector3 currentRayPoint = pointerRay.origin +
                    pointerRay.direction * draggedPaletteDistance;
                float travel = Vector3.Distance(draggedPaletteStartRayPoint,
                    currentRayPoint);
                // The palette sits beside the rig. Without an intentional-travel
                // guard, a plain click can already be within the generous outer
                // frame threshold and looks like the button simply disappears.
                bool placed = travel >= 0.055f && rigIndex >= 0 &&
                    distance <= 0.34f;
                if (placed)
                    BindVariableToAxisRig(rigIndex, draggedPaletteVariable);
                else
                    SetStatus("Variable not placed. Drag the labelled card onto " +
                        "a translucent cube frame, then release.");
                // A miss changes no state. Rebuilding the entire composer and
                // palette here caused the full right-hand panel to flash.
            }
            else
            {
                FindNearestRigSlotFromRay(pointerRay,
                    out int rigIndex, out int slot, out float distance,
                    out Vector3 ignored);
                if (rigIndex >= 0 && distance <= 0.19f)
                {
                    if (draggedPaletteKind == PaletteComponentKind.VariableAxis)
                        BindVariableSelectorToAxis(slot);
                    else
                        BindAxisToken(rigIndex,
                            draggedPaletteKind == PaletteComponentKind.Time
                                ? 0 : 1, slot);
                }
                else
                    RefreshSpatialAxisControllers();
            }
            ClearAllSpatialDropHighlights();
            HighlightVariableFrame(-1, false);
            RestoreDraggedPaletteSource();
            draggedPaletteToken = null;
            draggedPaletteKind = PaletteComponentKind.None;
            draggedPaletteVariable = -1;
            interaction.draggedPaletteSawTriggerHeld = false;
            interaction.draggedPaletteUsesDesktopPointer = false;
            draggedPaletteStartTime = 0.0f;
            draggedPaletteStartRayPoint = Vector3.zero;
        }

        private Ray PaletteDragPointerRay()
        {
#if UNITY_EDITOR || SLABLAB_FLAT
            if (interaction.draggedPaletteUsesDesktopPointer && xrCamera != null)
                return xrCamera.ScreenPointToRay(FlatPointerPosition);
#endif
            return rayInteractor != null
                ? rayInteractor.PointerRay
                : new Ray(transform.position, transform.forward);
        }

        private void CancelStalePaletteDrag()
        {
            if (draggedPaletteToken == null)
                return;
            RestoreDraggedPaletteSource();
            ClearAllSpatialDropHighlights();
            HighlightVariableFrame(-1, false);
            draggedPaletteToken = null;
            draggedPaletteKind = PaletteComponentKind.None;
            draggedPaletteVariable = -1;
            interaction.draggedPaletteSawTriggerHeld = false;
            interaction.draggedPaletteUsesDesktopPointer = false;
            draggedPaletteStartTime = 0.0f;
            draggedPaletteStartRayPoint = Vector3.zero;
        }

        private void RestoreDraggedPaletteSource()
        {
            if (draggedPaletteSourceToken == null)
                return;
            if (draggedPaletteOriginalParent != null)
            {
                draggedPaletteSourceToken.transform.SetParent(
                    draggedPaletteOriginalParent, false);
                draggedPaletteSourceToken.transform.localPosition =
                    draggedPaletteOriginalLocalPosition;
                draggedPaletteSourceToken.transform.localRotation =
                    draggedPaletteOriginalLocalRotation;
                draggedPaletteSourceToken.transform.localScale =
                    draggedPaletteOriginalLocalScale;
            }
            Collider sourceCollider =
                draggedPaletteSourceToken.GetComponent<Collider>();
            if (sourceCollider != null)
                sourceCollider.enabled = true;
            draggedPaletteSourceToken = null;
            draggedPaletteOriginalParent = null;
            draggedPaletteOriginalLocalPosition = Vector3.zero;
            draggedPaletteOriginalLocalRotation = Quaternion.identity;
            draggedPaletteOriginalLocalScale = Vector3.one;
            UpdateVariablePaletteTokenVisibility();
        }

        private void ReassertVariablePaletteSelectionVisuals()
        {
            // Direct-interaction hover effects are allowed to add outlines, but
            // they must never erase the persistent selected/unselected state.
            if (variablePaletteRoot == null || variablePaletteTokens.Count == 0)
                return;
            foreach (KeyValuePair<int, GameObject> pair in variablePaletteTokens)
            {
                if (pair.Value == null || !pair.Value.activeInHierarchy)
                    continue;
                bool selected = spatialAxisRigStates.Exists(state =>
                    state != null && state.boundVariable == pair.Key);
                Renderer renderer = pair.Value.GetComponent<Renderer>();
                if (renderer != null)
                {
                    if (variableSelectionBlock == null)
                        variableSelectionBlock = new MaterialPropertyBlock();
                    variableSelectionBlock.Clear();
                    Color color = selected
                        ? new Color(0.72f, 0.18f, 0.92f, 1.0f)
                        : new Color(0.075f, 0.090f, 0.120f, 1.0f);
                    variableSelectionBlock.SetColor("_Color", color);
                    variableSelectionBlock.SetColor("_BaseColor", color);
                    renderer.SetPropertyBlock(variableSelectionBlock);
                }
                if (variablePaletteLabels.TryGetValue(pair.Key,
                    out TextMesh label) && label != null)
                {
                    string variableName = pair.Key >= 0 && pair.Key < datasets.Count &&
                        datasets[pair.Key] != null &&
                        !string.IsNullOrWhiteSpace(datasets[pair.Key].Name)
                            ? datasets[pair.Key].Name
                            : "VARIABLE " + (pair.Key + 1);
                    label.text = (selected ? "[X] " : "[ ] ") +
                        variableName.ToUpperInvariant();
                    label.color = selected ? Color.white : Muted;
                    label.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
                }
            }
        }

        private void UpdateVariablePaletteFollow(bool immediate)
        {
            if (variablePaletteRoot == null || xrCamera == null)
                return;
            bool shouldShow = datasetImportConfirmed && mainWorkspaceEntered &&
                spatialRoot != null && spatialRoot.activeInHierarchy;
            variablePaletteRoot.SetActive(shouldShow);
            if (!shouldShow)
                return;
            // The variable control is now a real tri-axis component, not an
            // independent head-follow panel. Its parent rig owns its pose.
            if (variablePaletteRoot.transform.parent != null)
                return;
            bool wasVisible = variablePaletteRoot.activeSelf;
            // Keep the source palette perfectly still while an item is held.
            // A head-following source panel moving behind a ray-following card
            // reads as flicker in-headset and makes the pickup feel detached.
            if (!immediate && draggedPaletteToken != null)
                return;
            if (!wasVisible)
                immediate = true;
            Vector3 gaze = xrCamera.transform.forward.normalized;
            // Keep the palette outside the three-Field viewing arc. It remains
            // comfortably reachable, but no longer sits in front of the right
            // variable's Continuous Field.
            Vector3 targetPosition = xrCamera.transform.position + gaze * 0.94f +
                xrCamera.transform.right * 0.64f + xrCamera.transform.up * 0.060f;
            Vector3 uprightGaze = Vector3.ProjectOnPlane(gaze, Vector3.up);
            if (uprightGaze.sqrMagnitude < 0.001f)
                uprightGaze = gaze;
            Quaternion targetRotation = Quaternion.LookRotation(
                -uprightGaze.normalized, Vector3.up);
            // The palette lives to the viewer's right, so a small clockwise yaw
            // presents its face instead of leaving the right edge receding from
            // the camera. Rotate the complete physical hierarchy as one unit so
            // captions, buttons and colliders remain perfectly aligned.
            targetRotation = Quaternion.AngleAxis(8.0f, Vector3.up) *
                targetRotation;
            // Head tracking contains sub-millimetre motion even while the user
            // is looking still. Re-rendering a high-resolution world-space UI
            // for every one of those samples causes visible shimmer in Quest.
            // Keep the palette pinned until the head actually moves.
            if (!immediate &&
                Vector3.Distance(variablePaletteRoot.transform.position,
                    targetPosition) < 0.035f &&
                Quaternion.Angle(variablePaletteRoot.transform.rotation,
                    targetRotation) < 3.0f)
                return;
            float follow = immediate ? 1.0f :
                1.0f - Mathf.Exp(-7.0f * Time.unscaledDeltaTime);
            variablePaletteRoot.transform.position = Vector3.Lerp(
                variablePaletteRoot.transform.position, targetPosition, follow);
            variablePaletteRoot.transform.rotation = Quaternion.Slerp(
                variablePaletteRoot.transform.rotation, targetRotation, follow);
        }
    }
}
