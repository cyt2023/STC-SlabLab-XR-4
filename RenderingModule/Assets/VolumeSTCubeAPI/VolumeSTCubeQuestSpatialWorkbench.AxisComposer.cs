using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    /// <summary>Split out of VolumeSTCubeQuestSpatialWorkbench: AxisComposer side of the workbench.</summary>
    public sealed partial class VolumeSTCubeQuestSpatialWorkbench
    {
        public void DesktopContinueFromAxis()
        {
            DesktopOpenIntent();
        }

        private void ShowComposerTool(Canvas tool)
        {
            // Composer panels replace the current task surface. The persistent
            // toolbar and 3D Field remain as spatial context.
            HidePrimaryToolsExcept(tool);
            if (slabPreviewCanvas != null && slabPreviewCanvas != tool)
                slabPreviewCanvas.gameObject.SetActive(false);
            if (intentCanvas != null && intentCanvas != tool)
                intentCanvas.gameObject.SetActive(false);
            if (draftCanvas != null && draftCanvas != tool)
                draftCanvas.gameObject.SetActive(false);
            if (tool != null)
            {
                if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled &&
                    tool == slabPreviewCanvas)
                {
                    // The source-preview panel shares the desktop task lane
                    // with Intent. Its authored position is for the wider VR
                    // workspace and otherwise leaves it tiny behind the axis.
                    tool.transform.localPosition = IntentToolDockPosition;
                    if (intentCanvas != null)
                    {
                        tool.transform.localRotation =
                            intentCanvas.transform.localRotation;
                    }
                }
                tool.gameObject.SetActive(true);
            }
        }

        private SpatialAxisRigState FindAxisRigByRoot(Transform candidate)
        {
            if (candidate == null)
                return null;
            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                SpatialAxisRigState state = spatialAxisRigStates[index];
                if (state.root != null && state.root.transform == candidate)
                    return state;
            }
            return null;
        }

        private void SnapAxisRigToNearestFieldFace(SpatialAxisRigState state)
        {
            if (state == null || state.root == null || spatialRoot == null)
                return;
            int rigIndex = spatialAxisRigStates.IndexOf(state);
            int boundCount = 0;
            for (int index = 0; index < spatialAxisRigStates.Count; index++)
                if (spatialAxisRigStates[index].boundVariable >= 0)
                    boundCount++;
            int count = roles[3] == DimensionRole.Fixed ? 1 :
                Mathf.Min(datasets.Count, Mathf.Max(1, boundCount + 1));
            Vector3 center = PairedFieldCenter(Mathf.Max(0, rigIndex), count);
            Quaternion fieldRotation = Quaternion.Euler(0.0f,
                PairedFieldYaw(Mathf.Max(0, rigIndex), count), 0.0f);
            const float bodyClearance = 0.48f;
            Vector3[] faceOffsets =
            {
                new Vector3(FieldHalfWidth + bodyClearance, 0.0f, 0.0f),
                new Vector3(-FieldHalfWidth - bodyClearance, 0.0f, 0.0f),
                new Vector3(0.0f, FieldHalfHeight + bodyClearance, 0.0f),
                new Vector3(0.0f, -FieldHalfHeight - bodyClearance, 0.0f),
                new Vector3(0.0f, 0.0f, FieldHalfDepth + bodyClearance),
                new Vector3(0.0f, 0.0f, -FieldHalfDepth - bodyClearance)
            };
            Vector3 bestFieldPosition = center + fieldRotation * faceOffsets[0];
            Vector3 bestWorldPosition = spatialRoot.transform.TransformPoint(
                bestFieldPosition);
            float bestDistance = Vector3.SqrMagnitude(
                state.root.transform.position - bestWorldPosition);
            for (int face = 1; face < faceOffsets.Length; face++)
            {
                Vector3 fieldPosition = center + fieldRotation * faceOffsets[face];
                Vector3 worldPosition = spatialRoot.transform.TransformPoint(
                    fieldPosition);
                float distance = Vector3.SqrMagnitude(
                    state.root.transform.position - worldPosition);
                if (distance >= bestDistance)
                    continue;
                bestDistance = distance;
                bestFieldPosition = fieldPosition;
                bestWorldPosition = worldPosition;
            }
            state.hasCustomDock = true;
            state.customDockFieldPosition = bestFieldPosition;
            state.customDockFieldRotation = Quaternion.Inverse(
                spatialRoot.transform.rotation) * state.root.transform.rotation;
            StartCoroutine(AnimateWorldMove(state.root.transform,
                bestWorldPosition));
        }

        private void CreateSpatialAxisComposerRoot()
        {
            spatialAxisComposerRoot = new GameObject("Spatial dimension composer");
            spatialAxisComposerRoot.transform.SetParent(spatialRoot.transform, false);
            spatialAxisComposerRoot.transform.localPosition =
                new Vector3(ActiveSpatialAxisDockX(), 0.06f, -0.18f);
            // The composer is an interaction object, not distant scenery. Pull
            // it toward the viewer so controller dragging remains comfortable.
            if (xrCamera != null)
            {
                Vector3 towardViewer = xrCamera.transform.position -
                    spatialAxisComposerRoot.transform.position;
                if (towardViewer.sqrMagnitude > 0.01f)
                    spatialAxisComposerRoot.transform.position +=
                        towardViewer.normalized * 0.20f;
            }
            RefreshSpatialAxisControllers();
        }

        private float ActiveSpatialAxisDockX()
        {
            if (!IsForVrSurfaceDataset)
                return SpatialAxisDockX;
            // The animated Field is shifted left and the independent XYT Field
            // occupies the presentation center. Dock the tri-axis body beyond
            // the XYT Field's right edge instead of beside the old Field.
            return VolumeSTCubeForVrFieldSwapLayout.ActiveSeparation +
                VolumeSTCubeForVrXytCompanion.IndependentFieldHalfWidth + 0.48f;
        }

        private void EnsureSpatialAxisRigStates()
        {
            while (spatialAxisRigStates.Count < datasets.Count)
                spatialAxisRigStates.Add(new SpatialAxisRigState());
            while (spatialAxisRigStates.Count > datasets.Count)
                spatialAxisRigStates.RemoveAt(spatialAxisRigStates.Count - 1);
        }

        private void RefreshSpatialAxisControllers()
        {
            if (spatialAxisComposerRoot == null)
                return;
            for (int child = spatialAxisComposerRoot.transform.childCount - 1;
                child >= 0; child--)
                Destroy(spatialAxisComposerRoot.transform.GetChild(child).gameObject);
            EnsureSpatialAxisRigStates();
            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                spatialAxisRigStates[index].root = null;
                spatialAxisRigStates[index].timeToken = null;
                spatialAxisRigStates[index].depthToken = null;
                spatialAxisRigStates[index].variableToken = null;
                spatialAxisRigStates[index].frameRenderers.Clear();
                spatialAxisRigStates[index].frameColors.Clear();
                spatialAxisRigStates[index].frameVisibility = 0.0f;
                spatialAxisRigStates[index].frameRequestedVisible = false;
                for (int slot = 0; slot < 3; slot++)
                    spatialAxisRigStates[index].slotRenderers[slot] = null;
            }
            if (datasets.Count == 0)
                return;

            // The composer is one shared spatial controller. Variable state is
            // still stored per dataset, but additional variables create Field
            // visualisations around this rig rather than duplicate controllers.
            int count = 1;
            for (int index = 0; index < count; index++)
            {
                SpatialAxisRigState state = spatialAxisRigStates[index];
                string rigName = BoundVariableIndices().Count > 0
                    ? "shared variables" : "empty";
                GameObject rig = new GameObject(rigName + " axis body");
                rig.transform.SetParent(spatialAxisComposerRoot.transform, false);
                // Fixed slots prevent existing bodies from jumping or changing
                // size when the next variable body is progressively revealed.
                Quaternion pairedFieldRotation = Quaternion.Euler(
                    0.0f, PairedFieldYaw(index, count), 0.0f);
                // Keep the shared controller in the original, readable dock to
                // the right of the primary Field. Additional Fields grow around
                // this stable anchor: the second above it, the third to its
                // right. Binding a variable therefore never moves the controls.
                Vector3 desiredPositionInFieldSpace = new Vector3(
                    ActiveSpatialAxisDockX(), 0.06f, -0.18f);
                // Position each controller beside its own complete Field copy.
                // Converting through world space keeps the pairing correct even
                // after the headset-anchored workspace has been rotated.
                if (state.hasCustomDock && !IsForVrSurfaceDataset)
                    desiredPositionInFieldSpace = state.customDockFieldPosition;
                bool independentDesktopAxis =
                    VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled &&
                    spatialAxisComposerRoot.transform.parent !=
                        spatialRoot.transform;
                // On desktop the axis composer is a self-contained right-hand
                // work area. Its body must be local to that area; calculating
                // the dock through a moving Field left the controls offset far
                // outside the visible viewport.
                Vector3 dockedWorldPosition = independentDesktopAxis
                    ? spatialAxisComposerRoot.transform.position
                    : spatialRoot.transform.TransformPoint(
                        desiredPositionInFieldSpace);
                bool animateDock = state.pendingDockAnimation;
                rig.transform.position = animateDock && !independentDesktopAxis
                    ? spatialRoot.transform.TransformPoint(
                        desiredPositionInFieldSpace +
                        new Vector3(0.18f, 0.0f, 0.0f))
                    : dockedWorldPosition;
                // Look exactly along the horizontal X/Y angle bisector. A pure
                // +45 degree yaw keeps every vertical cube edge upright, sends
                // X and Y to opposite sides on screen, and leaves Z downward.
                rig.transform.rotation = independentDesktopAxis
                    ? spatialAxisComposerRoot.transform.rotation *
                        pairedFieldRotation * Quaternion.Euler(0.0f, 45.0f, 0.0f)
                    : state.hasCustomDock &&
                    !IsForVrSurfaceDataset
                    ? spatialRoot.transform.rotation * state.customDockFieldRotation
                    : spatialRoot.transform.rotation * pairedFieldRotation *
                        Quaternion.Euler(0.0f, 45.0f, 0.0f);
                // Grow the frame, axes, labels, and hit targets as one unit so
                // the composer remains comfortably readable and interactive in Quest.
                rig.transform.localScale = Vector3.one * 0.84f;
                // Grip anywhere on the physical axis body to reposition it. The
                // same marker used by floating panels keeps Quest interaction
                // consistent without competing with Trigger-based token drags.
                rig.AddComponent<VolumeSTCubeQuestPanelHandle>().accent = Cyan;
                state.root = rig;
                if (animateDock)
                {
                    state.pendingDockAnimation = false;
                    StartCoroutine(AnimateWorldMove(rig.transform,
                        dockedWorldPosition));
                }
                CreateVariableBindingShell(index, state);
                CreateAxisRigLines(index, state);
                state.timeToken = CreateAxisDimensionToken(index, 0, state,
                    "TIME", TimeColor);
                state.depthToken = CreateAxisDimensionToken(index, 1, state,
                    "DEPTH", DepthColor);
                if (state.timeAxis >= 0)
                    CreateAxisRoleButtons(index, 0, state);
                if (state.depthAxis >= 0)
                    CreateAxisRoleButtons(index, 1, state);
                UpdateAxisRigTokenPositions(index, false);
            }
            CreateSpatialComponentPalette();
        }

        private void CreateMagneticAxisDock(Transform parent, Color color,
            bool bound)
        {
            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            plate.name = "Magnetic field dock";
            plate.transform.SetParent(parent, false);
            plate.transform.localPosition = new Vector3(-0.47f, 0.0f, 0.0f);
            plate.transform.localScale = new Vector3(0.035f, 0.34f, 0.32f);
            Destroy(plate.GetComponent<Collider>());
            Color dockColor = bound ? VariableColor : Cyan;
            plate.GetComponent<Renderer>().material = CreateStableOpaqueMaterial(
                new Color(dockColor.r * 0.35f, dockColor.g * 0.35f,
                    dockColor.b * 0.35f, 1.0f));

            for (int index = 0; index < 4; index++)
            {
                GameObject node = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                node.name = "Magnetic dock node";
                node.transform.SetParent(parent, false);
                node.transform.localPosition = new Vector3(-0.495f,
                    index < 2 ? -0.13f : 0.13f,
                    (index & 1) == 0 ? -0.12f : 0.12f);
                node.transform.localScale = Vector3.one * 0.045f;
                Destroy(node.GetComponent<Collider>());
                node.GetComponent<Renderer>().material =
                    CreateStableOpaqueMaterial(new Color(dockColor.r,
                        dockColor.g, dockColor.b, 1.0f));
            }
        }

        private static Vector3 AxisSlotDirection(int slot)
        {
            return slot == 0 ? Vector3.right :
                slot == 1 ? Vector3.back : Vector3.down;
        }

        private void CreateAxisRigLines(int variableIndex,
            SpatialAxisRigState state)
        {
            Color[] colors = { TimeColor, VariableColor, DepthAxisColor };
            if (state.timeAxis >= 0 && state.depthAxis >= 0 &&
                state.timeAxis != state.depthAxis)
            {
                for (int slot = 0; slot < 3; slot++)
                    colors[slot] = slot == state.timeAxis ? TimeColor :
                        slot == state.depthAxis ? DepthColor : VariableColor;
            }
            string[] names = { "X", "Y", "Z" };
            GameObject hub = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hub.name = "Axis origin hub";
            hub.transform.SetParent(state.root.transform, false);
            hub.transform.localPosition = AxisRigOrigin;
            hub.transform.localScale = Vector3.one * 0.075f;
            Destroy(hub.GetComponent<Collider>());
            Material hubMaterial = CreateStableOpaqueMaterial(
                new Color(0.58f, 0.86f, 1.0f, 1.0f));
            hub.GetComponent<Renderer>().material = hubMaterial;
            for (int slot = 0; slot < 3; slot++)
            {
                Vector3 direction = AxisSlotDirection(slot);
                Color axisColor = new Color(colors[slot].r, colors[slot].g,
                    colors[slot].b, 0.98f);
                LineRenderer axisLine = CreateWorldLine(
                    "Axis slot " + names[slot], state.root.transform,
                    AxisRigOrigin,
                    AxisRigOrigin + direction * (AxisRigLength - 0.035f),
                    axisColor, 0.014f);
                axisLine.startWidth = 0.009f;
                axisLine.endWidth = 0.018f;
                axisLine.startColor = new Color(axisColor.r, axisColor.g,
                    axisColor.b, 0.48f);
                axisLine.endColor = axisColor;
                GameObject target = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                target.name = names[slot] + " axis drop slot";
                target.layer = 5;
                target.transform.SetParent(state.root.transform, false);
                target.transform.localPosition = AxisRigOrigin +
                    direction * AxisRigLength;
                target.transform.localScale = Vector3.one * 0.082f;
                Material material = CreateStableOpaqueMaterial(new Color(
                    colors[slot].r, colors[slot].g, colors[slot].b, 1.0f));
                state.slotRenderers[slot] = target.GetComponent<Renderer>();
                state.slotRenderers[slot].material = material;
                CreateWorldLabel(names[slot], AxisRigOrigin +
                    direction * (AxisRigLength + 0.145f), 0.0072f,
                    TextAnchor.MiddleCenter, colors[slot], state.root.transform);
            }
            if (state.timeAxis >= 0 && state.depthAxis >= 0)
            {
#if false // Redundant panel-count caption removed for the compact VR layout.
                int selectedTimes = state.timeRole == DimensionRole.Fixed
                    ? 1 : SelectedBucketCount(selectedTimeBucketMask);
                int selectedDepths = state.depthRole == DimensionRole.Fixed
                    ? 1 : SelectedBucketCount(selectedDepthBucketMask);
                CreateWorldLabel("MATPLOT  " + selectedTimes + " × " +
                    selectedDepths + "   •   " +
                    (selectedTimes * selectedDepths) + " PANELS",
                    new Vector3(0.0f, -0.585f, 0.275f), 0.0042f,
                    TextAnchor.MiddleCenter, Muted, state.root.transform);
#endif
                int valueSlot = state.variableAxis >= 0
                    ? state.variableAxis
                    : RemainingAxis(state.timeAxis, state.depthAxis);
#if false // Variable markers already communicate this axis.
                CreateWorldLabel("VARIABLES", AxisRigOrigin +
                    AxisSlotDirection(valueSlot) * 0.35f +
                    new Vector3(0.0f, 0.07f, 0.0f), 0.0055f,
                    TextAnchor.MiddleCenter, VariableColor,
                    state.root.transform);
#endif
                if (BoundVariableIndices().Count > 0)
                    CreateFacetedVariableAxisMarkers(state, valueSlot);
            }
        }

        private void CreateFacetedVariableAxisMarkers(
            SpatialAxisRigState state, int valueSlot)
        {
            List<int> variables = BoundVariableIndices();
            if (state == null || state.root == null || variables.Count == 0)
                return;
            Vector3 direction = AxisSlotDirection(valueSlot);
            int count = variables.Count;
            for (int index = 0; index < count; index++)
            {
                int variableIndex = variables[index];
                float along = count == 1 ? 0.43f :
                    Mathf.Lerp(0.20f, 0.50f, index / (float)(count - 1));
                GameObject marker = GameObject.CreatePrimitive(
                    PrimitiveType.Sphere);
                marker.name = datasets[variableIndex].Name +
                    " faceted variable marker";
                marker.layer = 5;
                marker.transform.SetParent(state.root.transform, false);
                marker.transform.localPosition = AxisRigOrigin +
                    direction * along;
                marker.transform.localScale = new Vector3(
                    0.105f, 0.072f, 0.060f);
                bool active = selectedDataset == datasets[variableIndex];
                marker.GetComponent<Renderer>().material =
                    CreateStableOpaqueMaterial(active
                        ? new Color(0.78f, 0.25f, 0.95f, 1.0f)
                        : new Color(VariableColor.r * 0.72f,
                            VariableColor.g * 0.72f,
                            VariableColor.b * 0.72f, 1.0f));
                int capturedVariable = variableIndex;
                marker.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                    () => LoadDataset(capturedVariable);

                Vector3 labelOffset = valueSlot == 2
                    ? new Vector3(0.15f, 0.0f, 0.0f)
                    : new Vector3(0.0f, 0.095f, 0.0f);
                TextMesh label = CreateWorldLabel(
                    GetFieldDatasetShortLabel(datasets[variableIndex]),
                    marker.transform.localPosition + labelOffset,
                    0.0038f, TextAnchor.MiddleLeft,
                    active ? Ink : VariableColor, state.root.transform);
                label.transform.localScale = new Vector3(1.0f, 1.0f, 1.0f);
            }
        }

        private GameObject CreateAxisDimensionToken(int variableIndex,
            int dimension, SpatialAxisRigState state, string label, Color color)
        {
            GameObject token = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            token.name = label + " draggable axis token";
            token.layer = 5;
            token.transform.SetParent(state.root.transform, false);
            token.transform.localScale = new Vector3(0.205f, 0.082f, 0.054f);
            Material material = CreateStableOpaqueMaterial(
                new Color(color.r, color.g, color.b, 1.0f));
            token.GetComponent<Renderer>().material = material;
            int capturedVariable = variableIndex;
            int capturedDimension = dimension;
            token.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                () => BeginAxisTokenDrag(capturedVariable, capturedDimension);
            TextMesh tokenLabel = CreateWorldLabel(label,
                Vector3.back * 0.038f, 0.0052f,
                TextAnchor.MiddleCenter, Ink, token.transform);
            tokenLabel.transform.localScale = new Vector3(4.88f, 12.2f, 18.5f);
            return token;
        }

        private void CreateAxisRoleButtons(int variableIndex, int dimension,
            SpatialAxisRigState state)
        {
            // Time and Depth are authored once in the pre-workspace Field.
            // The tri-axis only maps those saved definitions to axes, so the
            // values shown here are deliberately read-only. Put the summary
            // directly over its token so it reads as part of that button,
            // rather than as unrelated text floating above the controller.
            int axis = dimension == 0 ? state.timeAxis : state.depthAxis;
            if (axis < 0)
                return;
            Vector3 tokenPosition = AxisRigOrigin +
                AxisSlotDirection(axis) * AxisRigLength;
            CreateAxisBoundaryStrip(dimension, state, tokenPosition);
        }

        private void CreateAxisBoundaryStrip(int dimension,
            SpatialAxisRigState state, Vector3 tokenPosition)
        {
            Color accent = dimension == 0 ? TimeColor : DepthColor;
            Color backing = new Color(accent.r * 0.24f,
                accent.g * 0.24f, accent.b * 0.24f, 0.72f);
            Color light = Color.Lerp(accent, Color.white, 0.42f);
            light.a = 0.94f;
            // Lift the selector away from its main TIME/DEPTH token.  The extra
            // gap keeps the three bucket buttons and split labels from reading
            // as one crowded control in the headset.
            Vector3 outward = (tokenPosition - AxisRigOrigin).normalized;
            Vector3 center = tokenPosition + outward * 0.065f +
                new Vector3(0.0f, 0.215f, -0.060f);

            // A short stem visually attaches the range strip to the draggable
            // Time/Depth pill while leaving enough air around the label.
            CreateWorldLine((dimension == 0 ? "Time" : "Depth") +
                " range connector", state.root.transform,
                tokenPosition + new Vector3(0.0f, 0.055f, -0.050f),
                center + new Vector3(0.0f, -0.045f, 0.0f),
                new Color(light.r, light.g, light.b, 0.66f), 0.011f);

            bool fixedRole = dimension == 0
                ? state.timeRole == DimensionRole.Fixed
                : state.depthRole == DimensionRole.Fixed;
            if (fixedRole)
            {
                const float fixedHalfWidth = 0.145f;
                CreateWorldLine((dimension == 0 ? "Time" : "Depth") +
                    " fixed range backing", state.root.transform,
                    center + Vector3.left * fixedHalfWidth,
                    center + Vector3.right * fixedHalfWidth,
                    backing, 0.070f);
                CreateWorldLine((dimension == 0 ? "Time" : "Depth") +
                    " fixed range", state.root.transform,
                    center + Vector3.left * fixedHalfWidth,
                    center + Vector3.right * fixedHalfWidth,
                    light, 0.050f);
                string value = dimension == 0
                    ? (selectedDataset != null
                        ? selectedDataset.GetTimeLabel(Mathf.Clamp(
                            state.usesSharedBoundaries
                                ? sharedSelectedTime : state.customSelectedTime,
                            0, Mathf.Max(0, selectedDataset.TimeCount - 1)))
                        : "DAY")
                    : "Z=" + (state.usesSharedBoundaries
                        ? sharedSelectedZ : state.customSelectedZ);
                CreateWorldLabel(value.ToUpperInvariant(),
                    center + new Vector3(0.0f, 0.072f, -0.004f),
                    0.0034f, TextAnchor.MiddleCenter, light,
                    state.root.transform);
                return;
            }

            S4DIndexBucketRequest[] buckets = dimension == 0
                ? authoredTimeBuckets : authoredDepthBuckets;
            if (buckets == null || buckets.Length != 3)
            {
                EnsureSavedAuthorBoundaries();
                buckets = dimension == 0
                    ? authoredTimeBuckets : authoredDepthBuckets;
            }
            if (buckets == null || buckets.Length != 3)
                return;

            // All three choices get equal, generous hit targets.  Exact source
            // ranges remain in the label rather than shrinking shorter buckets.
            const float halfWidth = 0.270f;
            const float buttonGap = 0.014f;
            const float buttonWidth =
                (halfWidth * 2.0f - buttonGap * 2.0f) / 3.0f;
            CreateWorldLine((dimension == 0 ? "Time" : "Depth") +
                " range backing", state.root.transform,
                center + Vector3.left * halfWidth,
                center + Vector3.right * halfWidth,
                backing, 0.078f);
            for (int index = 0; index < 3; index++)
            {
                float left = -halfWidth + index * (buttonWidth + buttonGap);
                float right = left + buttonWidth;
                Color segmentColor = Color.Lerp(accent, Color.white,
                    0.28f + index * 0.13f);
                segmentColor.a = 0.94f;
                CreateAxisBucketToggle(dimension, index, state,
                    center + Vector3.right * ((left + right) * 0.5f),
                    buttonWidth,
                    buckets[index], segmentColor);
                if (index < 2)
                {
                    Vector3 split = center + Vector3.right *
                        (right + buttonGap * 0.5f);
                    CreateWorldLine((dimension == 0 ? "Time" : "Depth") +
                        " range split " + index, state.root.transform,
                        split + Vector3.down * 0.045f,
                        split + Vector3.up * 0.045f,
                        light, 0.010f);
                    int splitValue = buckets[index] != null &&
                        buckets[index].indices != null &&
                        buckets[index].indices.Length > 0
                            ? buckets[index].indices[
                                buckets[index].indices.Length - 1]
                            : 0;
                    if (dimension == 0)
                        splitValue++;
                    CreateWorldLabel(splitValue.ToString(),
                        split + new Vector3(0.0f, 0.086f, -0.004f),
                        0.0032f, TextAnchor.MiddleCenter, light,
                        state.root.transform);
                }
            }
        }

        private void CreateAxisBucketToggle(int dimension, int bucketIndex,
            SpatialAxisRigState state, Vector3 position, float width,
            S4DIndexBucketRequest bucket, Color accent)
        {
            bool selected = dimension == 0
                ? selectedTimeBucketMask[bucketIndex]
                : selectedDepthBucketMask[bucketIndex];
            GameObject facingGroup = new GameObject(
                (dimension == 0 ? "Time " : "Depth ") +
                "bucket facing group " + bucketIndex);
            facingGroup.transform.SetParent(state.root.transform, false);
            facingGroup.transform.localPosition = position;
            facingGroup.transform.localRotation = Quaternion.identity;
            axisBucketFacingGroups.Add(facingGroup.transform);

            GameObject button = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            button.name = (dimension == 0 ? "Time " : "Depth ") +
                "bucket toggle " + bucketIndex;
            button.layer = 5;
            button.transform.SetParent(facingGroup.transform, false);
            button.transform.localPosition = Vector3.zero;
            button.transform.localScale = new Vector3(width, 0.086f, 0.055f);
            Color fill = selected
                ? new Color(accent.r, accent.g, accent.b, 1.0f)
                : new Color(0.045f, 0.085f, 0.105f, 1.0f);
            button.GetComponent<Renderer>().material =
                CreateStableOpaqueMaterial(fill);
            int capturedDimension = dimension;
            int capturedBucket = bucketIndex;
            button.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked = () =>
                ToggleAxisBucketSelection(capturedDimension, capturedBucket);

            string semantic = bucket != null &&
                !string.IsNullOrWhiteSpace(bucket.label)
                    ? bucket.label.ToUpperInvariant()
                    : (dimension == 0
                        ? new[] { "BEFORE", "DURING", "AFTER" }[bucketIndex]
                        : new[] { "SURFACE", "MIDDLE", "DEEP" }[bucketIndex]);
            TextMesh label = CreateWorldLabel(semantic,
                new Vector3(0.0f, 0.001f, -0.033f),
                0.0028f, TextAnchor.MiddleCenter,
                selected ? Ink : Muted, facingGroup.transform);
            label.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
        }

        private void UpdateAxisBucketFacing()
        {
            if (xrCamera == null)
                return;
            for (int index = axisBucketFacingGroups.Count - 1;
                index >= 0; index--)
            {
                Transform group = axisBucketFacingGroups[index];
                if (group == null)
                {
                    axisBucketFacingGroups.RemoveAt(index);
                    continue;
                }
                Vector3 awayFromViewer = group.position -
                    xrCamera.transform.position;
                if (awayFromViewer.sqrMagnitude < 0.0001f)
                    continue;
                group.rotation = Quaternion.LookRotation(
                    awayFromViewer.normalized, xrCamera.transform.up);
            }
        }

        private static string AxisBucketRangeText(
            S4DIndexBucketRequest bucket, bool oneBased)
        {
            if (bucket == null || bucket.indices == null ||
                bucket.indices.Length == 0)
                return "--";
            int first = bucket.indices[0] + (oneBased ? 1 : 0);
            int last = bucket.indices[bucket.indices.Length - 1] +
                (oneBased ? 1 : 0);
            return first == last ? first.ToString() : first + "–" + last;
        }

        private void ToggleAxisBucketSelection(int dimension, int bucketIndex)
        {
            bool[] mask = dimension == 0
                ? selectedTimeBucketMask : selectedDepthBucketMask;
            if (bucketIndex < 0 || bucketIndex >= mask.Length)
                return;
            int selectedCount = 0;
            for (int index = 0; index < mask.Length; index++)
                if (mask[index])
                    selectedCount++;
            if (mask[bucketIndex] && selectedCount <= 1)
            {
                SetStatus("Keep at least one " +
                    (dimension == 0 ? "Time" : "Depth") +
                    " range selected for MatPlot.");
                return;
            }
            mask[bucketIndex] = !mask[bucketIndex];
            InvalidateSlabConfiguration(
                "MatPlot range selection changed", true);
            RefreshSpatialAxisControllers();
            int timeCount = SelectedBucketCount(selectedTimeBucketMask);
            int depthCount = SelectedBucketCount(selectedDepthBucketMask);
            SetStatus("MATPLOT GRID  " + timeCount + " × " + depthCount +
                "  =  " + (timeCount * depthCount) + " PANELS. " +
                "Open MatPlot Intent to continue.");
        }

        private void ResetAxisBucketSelection()
        {
            for (int index = 0; index < selectedTimeBucketMask.Length; index++)
                selectedTimeBucketMask[index] = true;
            for (int index = 0; index < selectedDepthBucketMask.Length; index++)
                selectedDepthBucketMask[index] = true;
        }

        private string AxisBoundarySummary(int dimension,
            SpatialAxisRigState state)
        {
            if (dimension == 0)
            {
                if (state != null && state.timeRole == DimensionRole.Fixed)
                {
                    int value = state.usesSharedBoundaries
                        ? sharedSelectedTime : state.customSelectedTime;
                    return selectedDataset != null
                        ? selectedDataset.GetTimeLabel(Mathf.Clamp(value, 0,
                            Mathf.Max(0, selectedDataset.TimeCount - 1)))
                        : "DAY " + (value + 1);
                }
                if (authoredTimeBuckets != null &&
                    authoredTimeBuckets.Length == 3)
                    return AuthoredBucketSummary(authoredTimeBuckets, true)
                        .ToUpperInvariant();
                return TimeRangeSummary().ToUpperInvariant();
            }
            if (state != null && state.depthRole == DimensionRole.Fixed)
            {
                int value = state.usesSharedBoundaries
                    ? sharedSelectedZ : state.customSelectedZ;
                return "Z=" + value;
            }
            if (authoredDepthBuckets != null &&
                authoredDepthBuckets.Length == 3)
                return AuthoredBucketSummary(authoredDepthBuckets, false)
                    .ToUpperInvariant();
            return DepthRangeSummary().ToUpperInvariant();
        }

        private static int RemainingAxis(int first, int second)
        {
            for (int slot = 0; slot < 3; slot++)
                if (slot != first && slot != second)
                    return slot;
            return 1;
        }

        private void BeginAxisTokenDrag(int variableIndex, int dimension)
        {
            if (variableIndex < 0 || variableIndex >= spatialAxisRigStates.Count ||
                rayInteractor == null)
                return;
            float now = Time.unscaledTime;
            bool doubleClick = lastAxisTokenClickVariable == variableIndex &&
                lastAxisTokenClickDimension == dimension &&
                now - lastAxisTokenClickTime <= 0.36f;
            lastAxisTokenClickTime = now;
            lastAxisTokenClickVariable = variableIndex;
            lastAxisTokenClickDimension = dimension;
            if (doubleClick)
            {
                UnbindAxisToken(variableIndex, dimension);
                return;
            }

            SpatialAxisRigState state = spatialAxisRigStates[variableIndex];
            draggedAxisToken = dimension == 0 ? state.timeToken : state.depthToken;
            if (draggedAxisToken == null)
                return;
            draggedAxisVariable = variableIndex;
            draggedAxisDimension = dimension;
            interaction.draggedAxisUsesDesktopPointer = false;
#if UNITY_EDITOR || SLABLAB_FLAT
            interaction.draggedAxisUsesDesktopPointer =
                VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled;
#endif
            bool pointerHeld = interaction.draggedAxisUsesDesktopPointer
                ? FlatPointerHeld
                : rayInteractor.TriggerHeld;
            interaction.draggedAxisSawTriggerHeld = pointerHeld;
            draggedAxisRestScale = draggedAxisToken.transform.localScale;
            Ray pointerRay = AxisDragPointerRay();
            draggedAxisDistance = Mathf.Clamp(Vector3.Distance(
                pointerRay.origin, draggedAxisToken.transform.position),
                0.35f, 2.4f);
            SetStatus("Drag " + (dimension == 0 ? "Time" : "Depth") +
                " to a glowing axis slot; release Trigger to bind. Double-click to unbind.");
        }

        private void UpdateAxisTokenInteraction()
        {
            if (draggedAxisToken == null || rayInteractor == null ||
                draggedAxisVariable < 0 ||
                draggedAxisVariable >= spatialAxisRigStates.Count)
                return;
            SpatialAxisRigState state = spatialAxisRigStates[draggedAxisVariable];
            Ray pointerRay = AxisDragPointerRay();
            bool pointerHeld = rayInteractor.TriggerHeld;
            bool pointerReleased = rayInteractor.TriggerReleased;
#if UNITY_EDITOR || SLABLAB_FLAT
            if (interaction.draggedAxisUsesDesktopPointer)
            {
                pointerHeld = FlatPointerHeld;
                pointerReleased = FlatPointerReleased;
            }
#endif
            if (pointerHeld)
            {
                interaction.draggedAxisSawTriggerHeld = true;
                Vector3 target = pointerRay.origin +
                    pointerRay.direction * draggedAxisDistance;
                int rayNearest = NearestAxisSlotFromRay(state,
                    pointerRay, out float rayDistance,
                    out Vector3 raySnapPoint);
                if (rayDistance < 0.19f)
                    target = Vector3.Lerp(target, raySnapPoint, 0.84f);
                float follow = 1.0f - Mathf.Exp(-28.0f * Time.unscaledDeltaTime);
                draggedAxisToken.transform.position = Vector3.Lerp(
                    draggedAxisToken.transform.position, target, follow);
                draggedAxisToken.transform.localScale = Vector3.Lerp(
                    draggedAxisToken.transform.localScale,
                    draggedAxisRestScale * 1.22f,
                    1.0f - Mathf.Exp(-16.0f * Time.unscaledDeltaTime));
                for (int slot = 0; slot < state.slotRenderers.Length; slot++)
                {
                    Renderer renderer = state.slotRenderers[slot];
                    if (renderer == null)
                        continue;
                    Color color = slot == rayNearest && rayDistance < 0.19f
                        ? Green : new Color(0.18f, 0.52f, 0.66f, 0.50f);
                    renderer.material.color = color;
                }
            }
            bool released = pointerReleased ||
                (interaction.draggedAxisSawTriggerHeld && !pointerHeld);
            if (!released)
                return;

            int selectedSlot = NearestAxisSlotFromRay(state,
                pointerRay, out float selectedDistance,
                out Vector3 ignoredSnapPoint);
            if (selectedDistance <= 0.19f)
                BindAxisToken(draggedAxisVariable, draggedAxisDimension,
                    selectedSlot);
            else
            {
                draggedAxisToken.transform.localScale = draggedAxisRestScale;
                UpdateAxisRigTokenPositions(draggedAxisVariable, true);
            }
            ClearAxisDragHighlight(state);
            draggedAxisToken = null;
            draggedAxisVariable = -1;
            draggedAxisDimension = -1;
            interaction.draggedAxisSawTriggerHeld = false;
            interaction.draggedAxisUsesDesktopPointer = false;
        }

        private Ray AxisDragPointerRay()
        {
#if UNITY_EDITOR || SLABLAB_FLAT
            if (interaction.draggedAxisUsesDesktopPointer && xrCamera != null)
                return xrCamera.ScreenPointToRay(FlatPointerPosition);
#endif
            return rayInteractor != null
                ? rayInteractor.PointerRay
                : new Ray(transform.position, transform.forward);
        }

        private int NearestAxisSlotFromRay(SpatialAxisRigState state,
            Ray ray, out float distance, out Vector3 snapPoint)
        {
            int nearest = 0;
            distance = float.MaxValue;
            snapPoint = ray.origin;
            Vector3 direction = ray.direction.normalized;
            // TIME, DEPTH and VARIABLE are symmetric draggable components.
            // Whichever component the user is holding may target any axis;
            // occupied components swap back to the free/source position.
            int slotCount = 3;
            for (int slot = 0; slot < slotCount; slot++)
            {
                Vector3 point = state.root.transform.TransformPoint(
                    AxisRigOrigin + AxisSlotDirection(slot) * AxisRigLength);
                float along = Mathf.Max(0.0f,
                    Vector3.Dot(point - ray.origin, direction));
                Vector3 closest = ray.origin + direction * along;
                float next = Vector3.Distance(point, closest);
                if (next < distance)
                {
                    distance = next;
                    nearest = slot;
                    snapPoint = point;
                }
            }
            return nearest;
        }

        private void CancelStaleAxisDragForPalette()
        {
            if (draggedAxisToken == null)
                return;
            if (draggedAxisVariable >= 0 &&
                draggedAxisVariable < spatialAxisRigStates.Count)
            {
                SpatialAxisRigState state =
                    spatialAxisRigStates[draggedAxisVariable];
                draggedAxisToken.transform.localScale = draggedAxisRestScale;
                UpdateAxisRigTokenPositions(draggedAxisVariable, true);
                ClearAxisDragHighlight(state);
            }
            draggedAxisToken = null;
            draggedAxisVariable = -1;
            draggedAxisDimension = -1;
            interaction.draggedAxisSawTriggerHeld = false;
            interaction.draggedAxisUsesDesktopPointer = false;
        }

        private void UpdateVariablePaletteTokenVisibility()
        {
            foreach (KeyValuePair<int, GameObject> pair in variablePaletteTokens)
            {
                if (pair.Value == null)
                    continue;
                bool bound = spatialAxisRigStates.Exists(state =>
                    state.boundVariable == pair.Key);
                // Choices are toggles, not consumable drag cards. A selected
                // variable stays visible so a second click can deselect it.
                pair.Value.SetActive(true);
                Renderer renderer = pair.Value.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.material.color = bound
                        ? new Color(0.70f, 0.20f, 0.88f, 1.0f)
                        : new Color(0.095f, 0.115f, 0.145f, 1.0f);
                pair.Value.transform.localScale = bound
                    ? new Vector3(0.282f, 0.080f, 0.050f)
                    : new Vector3(0.270f, 0.076f, 0.046f);
                if (variablePaletteLabels.TryGetValue(pair.Key,
                    out TextMesh label) && label != null)
                {
                    label.text = (bound ? "[X] " : "[ ] ") +
                        datasets[pair.Key].Name.ToUpperInvariant();
                    label.color = bound ? Ink : Muted;
                    label.fontStyle = bound ? FontStyle.Bold : FontStyle.Normal;
                }
            }
        }

        private void FindNearestRigSlotFromRay(Ray ray, out int rigIndex,
            out int slot, out float distance, out Vector3 snapPoint)
        {
            rigIndex = -1;
            slot = 0;
            distance = float.MaxValue;
            snapPoint = ray.origin;
            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                SpatialAxisRigState state = spatialAxisRigStates[index];
                if (state.root == null)
                    continue;
                int nextSlot = NearestAxisSlotFromRay(state, ray,
                    out float nextDistance, out Vector3 nextPoint);
                if (nextDistance < distance)
                {
                    rigIndex = index;
                    slot = nextSlot;
                    distance = nextDistance;
                    snapPoint = nextPoint;
                }
            }
        }

        private int NearestRigFrameFromRay(Ray ray, out float distance,
            out Vector3 snapPoint)
        {
            int nearest = -1;
            distance = float.MaxValue;
            snapPoint = ray.origin;
            Vector3 direction = ray.direction.normalized;
            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                Transform root = spatialAxisRigStates[index].root != null
                    ? spatialAxisRigStates[index].root.transform : null;
                if (root == null)
                    continue;
                Ray localRay = new Ray(root.InverseTransformPoint(ray.origin),
                    root.InverseTransformDirection(ray.direction).normalized);
                Bounds dropBounds = new Bounds(Vector3.zero,
                    Vector3.one * 1.06f);
                if (dropBounds.IntersectRay(localRay, out float enter))
                {
                    nearest = index;
                    distance = 0.0f;
                    snapPoint = root.TransformPoint(
                        new Vector3(0.0f, 0.50f, 0.0f));
                    break;
                }
                Vector3 point = root.TransformPoint(new Vector3(0.0f, 0.12f, 0.0f));
                float along = Mathf.Max(0.0f,
                    Vector3.Dot(point - ray.origin, direction));
                float next = Vector3.Distance(point,
                    ray.origin + direction * along);
                if (next < distance)
                {
                    nearest = index;
                    distance = next;
                    snapPoint = point;
                }
            }
            return nearest;
        }

        private void UpdateAxisRigHoverVisuals()
        {
            int hoverRig = -1;
            bool hoverActive = false;
            if (rayInteractor != null && spatialAxisRigStates.Count > 0)
            {
                hoverRig = NearestRigFrameFromRay(rayInteractor.PointerRay,
                    out float distance, out Vector3 ignored);
                hoverActive = hoverRig >= 0 && distance <= 0.14f;
            }
            HighlightVariableFrame(hoverRig, hoverActive);

            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                SpatialAxisRigState state = spatialAxisRigStates[index];
                float target = state.frameRequestedVisible ? 1.0f : 0.0f;
                float speed = state.frameRequestedVisible ? 8.0f : 4.5f;
                state.frameVisibility = Mathf.MoveTowards(
                    state.frameVisibility, target,
                    Time.unscaledDeltaTime * speed);
                for (int edge = 0; edge < state.frameRenderers.Count; edge++)
                {
                    Renderer renderer = state.frameRenderers[edge];
                    if (renderer == null)
                        continue;
                    renderer.enabled = state.frameVisibility > 0.015f;
                    if (!renderer.enabled)
                        continue;
                    Color baseColor = state.frameColors[Mathf.Min(edge,
                        state.frameColors.Count - 1)];
                    renderer.material.color = new Color(baseColor.r,
                        baseColor.g, baseColor.b,
                        baseColor.a * state.frameVisibility);
                }
            }
        }

        private void HighlightSpatialDropSlot(int rigIndex, int slot,
            bool active)
        {
            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                SpatialAxisRigState state = spatialAxisRigStates[index];
                for (int axis = 0; axis < state.slotRenderers.Length; axis++)
                    if (state.slotRenderers[axis] != null)
                        state.slotRenderers[axis].material.color =
                            active && index == rigIndex && axis == slot
                            ? new Color(Green.r, Green.g, Green.b, 1.0f)
                            : new Color(0.18f, 0.52f, 0.66f, 1.0f);
            }
        }

        private void BindVariableToAxisRig(int rigIndex, int variableIndex)
        {
            if (spatialAxisRigStates.Count == 0 ||
                variableIndex < 0 || variableIndex >= datasets.Count)
                return;
            rigIndex = 0;
            int existing = spatialAxisRigStates.FindIndex(state =>
                state.boundVariable == variableIndex);
            if (existing < 0)
            {
                int empty = spatialAxisRigStates.FindIndex(state =>
                    state.boundVariable < 0);
                if (empty < 0)
                {
                    SetStatus("All detected variables are already bound.");
                    RestoreDraggedPaletteSource();
                    return;
                }
                SpatialAxisRigState shared = spatialAxisRigStates[0];
                SpatialAxisRigState target = spatialAxisRigStates[empty];
                target.boundVariable = variableIndex;
                target.timeAxis = shared.timeAxis;
                target.depthAxis = shared.depthAxis;
                target.timeRole = shared.timeRole;
                target.depthRole = shared.depthRole;
            }
            UpdateAutomaticVariableRole();
            spatialAxisRigStates[0].pendingDockAnimation = true;
            // The first detected variable is commonly already being prepared
            // when the user drops its card. Do not silently discard that drop
            // or destroy/recreate the same view: make the existing STC visible
            // and let its texture-ready coroutine finish normally.
            if (currentView != null && selectedDataset == datasets[variableIndex])
            {
                currentView.SetVisible(viewState.cubeVisible);
                RefreshVariableFacetStacks();
                FrameVolume();
                StartCoroutine(RefitVolumeAfterFrameChange());
            }
            else
            {
                LoadDataset(variableIndex);
            }
            InvalidateSlabConfiguration("Variable binding changed", true);
            RefreshSpatialAxisControllers();
            UpdateVariablePaletteTokenVisibility();
            SetStatus(datasets[variableIndex].Name +
                " bound to the shared axis controller" +
                (roles[3] == DimensionRole.Fixed
                    ? ". One variable means FIXED."
                    : ". Multiple variables mean FACETED."));
        }

        private void ToggleAxisVariableSelection(int variableIndex)
        {
            if (variableIndex < 0 || variableIndex >= datasets.Count ||
                spatialAxisRigStates.Count == 0)
                return;
            int existing = spatialAxisRigStates.FindIndex(state =>
                state.boundVariable == variableIndex);
            if (existing < 0)
            {
                BindVariableToAxisRig(0, variableIndex);
                return;
            }

            string variableName = datasets[variableIndex].Name;
            spatialAxisRigStates[existing].boundVariable = -1;
            UpdateAutomaticVariableRole();
            List<int> remaining = BoundVariableIndices();
            if (selectedDataset == datasets[variableIndex] &&
                remaining.Count > 0)
                LoadDataset(remaining[0]);
            else
            {
                RefreshVariableFacetStacks();
                RefreshSpatialAxisControllers();
            }
            InvalidateSlabConfiguration("Variable selection changed", true);
            RefreshSpatialAxisControllers();
            UpdateVariablePaletteTokenVisibility();
            SetStatus(variableName + " deselected. " + remaining.Count +
                " variable" + (remaining.Count == 1 ? string.Empty : "s") +
                " will be used by MatPlot.");
        }

        private int NearestAxisSlot(SpatialAxisRigState state,
            Vector3 worldPosition, out float distance)
        {
            int nearest = 0;
            distance = float.MaxValue;
            for (int slot = 0; slot < 3; slot++)
            {
                Vector3 point = state.root.transform.TransformPoint(
                    AxisRigOrigin + AxisSlotDirection(slot) * AxisRigLength);
                float next = Vector3.Distance(point, worldPosition);
                if (next < distance)
                {
                    distance = next;
                    nearest = slot;
                }
            }
            return nearest;
        }

        private void ClearAxisDragHighlight(SpatialAxisRigState state)
        {
            for (int slot = 0; slot < state.slotRenderers.Length; slot++)
                if (state.slotRenderers[slot] != null)
                    state.slotRenderers[slot].material.color =
                        new Color(0.18f, 0.52f, 0.66f, 0.50f);
        }

        private void BindAxisToken(int variableIndex, int dimension, int slot)
        {
            // Nothing to bind onto yet: the shared controller is only created
            // once a dataset exists. Without this guard the index below threw
            // ArgumentOutOfRangeException and aborted the surrounding rebuild.
            if (spatialAxisRigStates.Count == 0)
            {
                SetStatus(
                    "Axis controller is still being prepared. Try the binding again.");
                return;
            }
            variableIndex = 0;
            SpatialAxisRigState state = spatialAxisRigStates[0];
            slot = Mathf.Clamp(slot, 0, 2);
            bool variableDisplaced = slot == state.variableAxis;
            if (dimension == 0)
            {
                if (slot == state.variableAxis)
                    state.variableAxis = -1;
                if (slot == state.depthAxis)
                    state.depthAxis = state.timeAxis;
                state.timeAxis = slot;
            }
            else
            {
                if (slot == state.variableAxis)
                    state.variableAxis = -1;
                if (slot == state.timeAxis)
                    state.timeAxis = state.depthAxis;
                state.depthAxis = slot;
            }
            for (int index = 1; index < spatialAxisRigStates.Count; index++)
            {
                spatialAxisRigStates[index].timeAxis = state.timeAxis;
                spatialAxisRigStates[index].depthAxis = state.depthAxis;
                spatialAxisRigStates[index].variableAxis = state.variableAxis;
            }
            UpdateAxisRigTokenPositions(variableIndex, true);
            if (state.boundVariable >= 0 &&
                state.boundVariable < datasets.Count &&
                datasets[state.boundVariable] == selectedDataset)
                ApplySelectedAxisRigState(variableIndex, true);
            StartCoroutine(RefreshAxisControllersAfterSnap());
            InvalidateSlabConfiguration("Axis binding changed", true);
            SetStatus((dimension == 0 ? "Time" : "Depth") +
                " bound to " + new[] { "X", "Y", "Z" }[slot] +
                (variableDisplaced
                    ? ". Variable returned to its lower dock."
                    : ". VALUE moved to the remaining axis."));
        }

        private void BindVariableSelectorToAxis(int slot)
        {
            if (spatialAxisRigStates.Count == 0)
                return;
            SpatialAxisRigState state = spatialAxisRigStates[0];
            slot = Mathf.Clamp(slot, 0, 2);
            int previous = state.variableAxis;
            if (slot == state.timeAxis)
                state.timeAxis = previous;
            if (slot == state.depthAxis)
                state.depthAxis = previous;
            state.variableAxis = slot;
            for (int index = 1; index < spatialAxisRigStates.Count; index++)
            {
                spatialAxisRigStates[index].timeAxis = state.timeAxis;
                spatialAxisRigStates[index].depthAxis = state.depthAxis;
                spatialAxisRigStates[index].variableAxis = slot;
            }
            InvalidateSlabConfiguration("Variable axis binding changed", true);
            StartCoroutine(RefreshAxisControllersAfterSnap());
            SetStatus("VARIABLE bound to " + new[] { "X", "Y", "Z" }[slot] +
                ". Select one or more variables in the panel emitted from the purple button.");
        }

        private IEnumerator RefreshAxisControllersAfterSnap()
        {
            yield return new WaitForSecondsRealtime(0.27f);
            RefreshSpatialAxisControllers();
            // Rebuild the paired low-resolution STC views only after the token
            // has completed its snap animation. Disk reads during the drag/drop
            // frame made the controller feel as if it stalled on release.
            RefreshVariableFacetStacks();
        }

        private void UnbindAxisToken(int variableIndex, int dimension)
        {
            if (variableIndex < 0 || variableIndex >= spatialAxisRigStates.Count)
                return;
            SpatialAxisRigState state = spatialAxisRigStates[variableIndex];
            if (dimension == 0)
                state.timeAxis = -1;
            else
                state.depthAxis = -1;
            UpdateAxisRigTokenPositions(variableIndex, true);
            StartCoroutine(RefreshAxisControllersAfterSnap());
            InvalidateSlabConfiguration("Axis binding removed", true);
            SetStatus((dimension == 0 ? "Time" : "Depth") +
                " unbound. Drag it onto an available axis to continue.");
        }

        private void UpdateAxisRigTokenPositions(int variableIndex, bool animate)
        {
            if (variableIndex < 0 || variableIndex >= spatialAxisRigStates.Count)
                return;
            SpatialAxisRigState state = spatialAxisRigStates[variableIndex];
            Vector3 timePosition = state.timeAxis >= 0
                ? AxisRigOrigin + AxisSlotDirection(state.timeAxis) * AxisRigLength
                : UnboundTimeTokenPosition;
            Vector3 depthPosition = state.depthAxis >= 0
                ? AxisRigOrigin + AxisSlotDirection(state.depthAxis) * AxisRigLength
                : UnboundDepthTokenPosition;
            Vector3 variablePosition = state.variableAxis >= 0
                ? AxisRigOrigin + AxisSlotDirection(state.variableAxis) *
                    AxisRigLength
                : UnboundVariableTokenPosition;
            if (state.timeToken != null)
            {
                if (animate)
                    StartCoroutine(AnimateLocalMove(state.timeToken.transform,
                        timePosition));
                else
                    state.timeToken.transform.localPosition = timePosition;
            }
            if (state.depthToken != null)
            {
                if (animate)
                    StartCoroutine(AnimateLocalMove(state.depthToken.transform,
                        depthPosition));
                else
                    state.depthToken.transform.localPosition = depthPosition;
            }
            if (variableIndex == 0 && variablePaletteRoot != null)
            {
                if (animate)
                    StartCoroutine(AnimateLocalMove(
                        variablePaletteRoot.transform, variablePosition));
                else
                    variablePaletteRoot.transform.localPosition =
                        variablePosition;
            }
        }

        private void SetSpatialAxisRole(int variableIndex, int dimension,
            DimensionRole role)
        {
            if (mainWorkspaceEntered)
            {
                SetStatus("Time and Depth were saved in Field Setup and are read-only in the tri-axis workspace.");
                return;
            }
            if (spatialAxisRigStates.Count == 0)
                return;
            variableIndex = 0;
            SpatialAxisRigState state = spatialAxisRigStates[0];
            if (dimension == 0)
                state.timeRole = role;
            else
                state.depthRole = role;
            for (int index = 1; index < spatialAxisRigStates.Count; index++)
            {
                spatialAxisRigStates[index].timeRole = state.timeRole;
                spatialAxisRigStates[index].depthRole = state.depthRole;
            }
            if (state.boundVariable >= 0 &&
                state.boundVariable < datasets.Count &&
                datasets[state.boundVariable] == selectedDataset)
                ApplySelectedAxisRigState(variableIndex, false);
            RefreshVariableFacetStacks();
            InvalidateSlabConfiguration((dimension == 0 ? "Time" : "Depth") +
                " role changed");
            RefreshSpatialAxisControllers();
            SetStatus((dimension == 0 ? "Time" : "Depth") + " is now " +
                RoleLabel(role) + " for " +
                (state.boundVariable >= 0 && state.boundVariable < datasets.Count
                    ? datasets[state.boundVariable].Name : "this axis body") + ".");
        }

        private void SelectAxisRigVariable(int rigIndex)
        {
            List<int> boundVariables = BoundVariableIndices();
            if (boundVariables.Count == 0)
            {
                SetStatus("The shared axis controller is empty. Drag a variable onto it.");
                return;
            }
            rigIndex = 0;
            int selectedIndex = selectedDataset != null
                ? datasets.IndexOf(selectedDataset) : -1;
            int activeOffset = boundVariables.IndexOf(selectedIndex);
            int targetVariable = boundVariables[(activeOffset + 1) %
                boundVariables.Count];
            float now = Time.unscaledTime;
            bool doubleClick = lastVariableShellClickRig == rigIndex &&
                now - lastVariableShellClickTime <= 0.36f;
            lastVariableShellClickRig = rigIndex;
            lastVariableShellClickTime = now;
            if (doubleClick)
            {
                int stateIndex = spatialAxisRigStates.FindIndex(item =>
                    item.boundVariable == selectedIndex);
                if (stateIndex < 0)
                    stateIndex = spatialAxisRigStates.FindIndex(item =>
                        item.boundVariable == targetVariable);
                string name = datasets[spatialAxisRigStates[stateIndex].boundVariable].Name;
                spatialAxisRigStates[stateIndex].boundVariable = -1;
                UpdateAutomaticVariableRole();
                InvalidateSlabConfiguration("Variable binding removed", true);
                RefreshSpatialAxisControllers();
                RefreshVariableFacetStacks();
                UpdateVariablePaletteTokenVisibility();
                SetStatus(name + " unbound from this axis body.");
                return;
            }
            if (datasets[targetVariable] != selectedDataset)
                LoadDataset(targetVariable);
            else
                ApplySelectedAxisRigState(0, true);
        }

        private void ApplySelectedAxisRigState(int variableIndex,
            bool animateGraph)
        {
            if (spatialAxisRigStates.Count == 0)
                return;
            SpatialAxisRigState state = spatialAxisRigStates[0];
            roles[0] = state.timeRole;
            roles[1] = state.depthRole;
            roles[2] = DimensionRole.Mapped;
            // Axis assignment controls Preview/Matrix ordering only. The STC
            // Field remains upright and spatially independent from the rig.
            fieldAxisRemapRotation = Quaternion.identity;
            Transform volumeRoot = currentView != null &&
                currentView.rootObject != null
                    ? currentView.rootObject.transform : null;
            if (volumeRoot != null)
                volumeRoot.localRotation = Quaternion.identity;
            FrameVolume();
            UpdateAnalysisAxisLabels();
            InvalidateSlabConfiguration("Spatial axis mapping changed", true);
        }

        private static Quaternion FieldRotationForAxisRig(
            SpatialAxisRigState state)
        {
            if (state == null || state.timeAxis < 0 || state.depthAxis < 0 ||
                state.timeAxis == state.depthAxis)
                return Quaternion.identity;
            Vector3 timeDirection = AxisSlotDirection(state.timeAxis);
            Vector3 depthDirection = AxisSlotDirection(state.depthAxis);
            return Quaternion.LookRotation(
                Vector3.Cross(timeDirection, depthDirection), depthDirection);
        }

        private void StartFieldAxisRemap(Quaternion target)
        {
            if (fieldAxisRemapCoroutine != null)
                StopCoroutine(fieldAxisRemapCoroutine);
            fieldAxisRemapCoroutine = StartCoroutine(
                AnimateFieldAxisRemap(target));
        }

        private IEnumerator AnimateFieldAxisRemap(Quaternion target)
        {
            Quaternion start = fieldAxisRemapRotation;
            Transform volumeRoot = currentView != null &&
                currentView.rootObject != null
                    ? currentView.rootObject.transform : null;
            float elapsed = 0.0f;
            const float duration = 0.34f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0.0f, 1.0f,
                    Mathf.Clamp01(elapsed / duration));
                fieldAxisRemapRotation = Quaternion.Slerp(start, target, t);
                if (volumeRoot != null)
                    volumeRoot.localRotation = fieldAxisRemapRotation;
                yield return null;
            }
            fieldAxisRemapRotation = target;
            if (volumeRoot != null)
                volumeRoot.localRotation = target;
            // Rotation changes the renderer's world-space bounds. Refit after
            // every axis remap so the corresponding STC remains fully inside
            // its field frame instead of escaping through a side of the cube.
            FrameVolume();
            fieldAxisRemapCoroutine = null;
        }

        private void CreateAxisOriginHub(Vector3 origin)
        {
            GameObject hub = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hub.name = "XYZ axis origin hub";
            Destroy(hub.GetComponent<Collider>());
            hub.transform.SetParent(spatialRoot.transform, false);
            hub.transform.localPosition = origin;
            hub.transform.localScale = Vector3.one * 0.085f;
            Material hubMaterial = new Material(Shader.Find("Sprites/Default"));
            hubMaterial.color = new Color(0.88f, 0.96f, 1.0f, 0.98f);
            Renderer hubRenderer = hub.GetComponent<Renderer>();
            hubRenderer.material = hubMaterial;
            hubRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            hubRenderer.receiveShadows = false;

            CreateWorldLine("Origin X glow", spatialRoot.transform, origin,
                origin + Vector3.right * 0.24f, TimeColor, 0.026f);
            CreateWorldLine("Origin Y glow", spatialRoot.transform, origin,
                origin + Vector3.back * 0.24f, VariableColor, 0.026f);
            CreateWorldLine("Origin Z glow", spatialRoot.transform, origin,
                origin + Vector3.up * 0.24f, DepthAxisColor, 0.026f);
            CreateWorldLabel("X", origin + Vector3.right * 0.29f,
                0.0065f, TextAnchor.MiddleCenter, TimeColor);
            CreateWorldLabel("Y", origin + Vector3.back * 0.29f,
                0.0065f, TextAnchor.MiddleCenter, VariableColor);
            CreateWorldLabel("Z", origin + Vector3.up * 0.29f,
                0.0065f, TextAnchor.MiddleCenter, DepthAxisColor);
            CreateWorldLabel("0", origin + new Vector3(0.045f, 0.045f, -0.045f),
                0.0055f, TextAnchor.MiddleCenter, Ink);
        }

        private void UpdateAnalysisAxisLabels()
        {
            if (timeAxisLabel != null)
            {
                timeAxisLabel.text = selectedDataset == null
                    ? "X  TIME"
                    : "X  ·  TIME";
                timeAxisLabel.transform.localPosition = new Vector3(
                    0.0f,
                    boundaryEditActive &&
                    boundaryDimension == BoundaryDimension.Time ? 0.335f : 0.205f,
                    -0.045f);
            }
            if (variableAxisLabel != null)
            {
                variableAxisLabel.text = selectedDataset == null
                    ? "Y  VARIABLE"
                    : "Y  ·  " + selectedDataset.Name;
            }
            if (depthAxisLabel != null)
            {
                // A For_VR Hong Kong dataset is a 2D surface changing over
                // time. Its vertical extrusion represents the displayed value,
                // not an observed depth layer, so never label that axis DEPTH.
                depthAxisLabel.text = selectedDataset != null &&
                    IsForVrSurfaceDataset
                        ? "Z  ·  VALUE"
                        : selectedDataset == null
                            ? "Z  DEPTH"
                            : "Z  ·  DEPTH";
            }
            UpdateAxisBucketGuides();
        }

        private void UpdateAxisBucketGuides()
        {
            // CUT handles and finalized buckets are two different visual modes.
            // Never draw both at once: the shared corner becomes unreadable in VR.
            bool hasSavedBuckets =
                authoredTimeBuckets != null && authoredTimeBuckets.Length == 3 &&
                authoredDepthBuckets != null && authoredDepthBuckets.Length == 3;
            bool showBuckets = selectedDataset != null &&
                hasSavedBuckets && !boundaryEditActive;
            if (selectedDataset == null)
            {
                for (int index = 0; index < 3; index++)
                {
                    if (timeBucketAxisSegments[index] != null)
                        timeBucketAxisSegments[index].gameObject.SetActive(false);
                    if (timeBucketAxisLabels[index] != null)
                        timeBucketAxisLabels[index].gameObject.SetActive(false);
                    if (depthBucketAxisSegments[index] != null)
                        depthBucketAxisSegments[index].gameObject.SetActive(false);
                    if (depthBucketAxisLabels[index] != null)
                        depthBucketAxisLabels[index].gameObject.SetActive(false);
                }
                return;
            }
            int timeCount = selectedDataset != null
                ? Mathf.Max(1, selectedDataset.TimeCount)
                : 30;
            int depthCount = selectedDataset != null
                ? Mathf.Max(1, selectedDataset.DimZ)
                : 92;

            int[] timeFirst = { 0, timeBoundaryStart + 1, timeBoundaryEnd + 1 };
            int[] timeLast = { timeBoundaryStart, timeBoundaryEnd, timeCount - 1 };
            int depthCutA = Mathf.Clamp(
                Mathf.RoundToInt(depthBoundaryLow * depthCount),
                1, Mathf.Max(1, depthCount - 2));
            int depthCutB = Mathf.Clamp(
                Mathf.RoundToInt(depthBoundaryHigh * depthCount),
                depthCutA + 1, depthCount - 1);
            int[] depthFirst = { 0, depthCutA, depthCutB };
            int[] depthLast = { depthCutA - 1, depthCutB - 1, depthCount - 1 };

            if (authorBoundaryConfirmed)
            {
                for (int index = 0; index < 3; index++)
                {
                    if (authoredTimeBuckets != null &&
                        index < authoredTimeBuckets.Length)
                        TryGetIndexRange(authoredTimeBuckets[index],
                            out timeFirst[index], out timeLast[index]);
                    if (authoredDepthBuckets != null &&
                        index < authoredDepthBuckets.Length)
                        TryGetIndexRange(authoredDepthBuckets[index],
                            out depthFirst[index], out depthLast[index]);
                }
            }

            string[] timeNames = { "BEFORE", "DURING", "AFTER" };
            string[] depthNames = { "SURFACE", "MIDDLE", "DEEP" };
            float timeDenominator = Mathf.Max(1, timeCount - 1);
            float depthDenominator = Mathf.Max(1, depthCount - 1);
            float depthAxisBottom = -FieldHalfHeight + 0.075f;
            float depthAxisTop = FieldHalfHeight - 0.075f;
            float depthAxisX = -FieldHalfWidth + 0.075f;
            float depthAxisZ = FieldHalfDepth - 0.070f;

            for (int index = 0; index < 3; index++)
            {
                if (timeBucketAxisSegments[index] != null)
                {
                    timeBucketAxisSegments[index].gameObject.SetActive(showBuckets);
                    float x0 = Mathf.Lerp(-TimeRailHalfWidth, TimeRailHalfWidth,
                        timeFirst[index] / timeDenominator);
                    float x1 = Mathf.Lerp(-TimeRailHalfWidth, TimeRailHalfWidth,
                        timeLast[index] / timeDenominator);
                    SetLine(timeBucketAxisSegments[index],
                        new Vector3(x0, 0.025f, -0.008f),
                        new Vector3(x1, 0.025f, -0.008f));
                    if (timeBucketAxisLabels[index] != null)
                    {
                        timeBucketAxisLabels[index].gameObject.SetActive(showBuckets);
                        timeBucketAxisLabels[index].text = timeNames[index] + "  " +
                            selectedDataset.GetTimeLabel(timeFirst[index]) + "–" +
                            selectedDataset.GetTimeLabel(timeLast[index]);
                        // The colored segment already communicates the range;
                        // keep only the semantic bucket name in the Field.
                        timeBucketAxisLabels[index].text = timeNames[index];
                        timeBucketAxisLabels[index].transform.localPosition =
                            new Vector3((x0 + x1) * 0.5f, 0.105f, -0.012f);
                    }
                }

                if (depthBucketAxisSegments[index] != null)
                {
                    depthBucketAxisSegments[index].gameObject.SetActive(showBuckets);
                    float y0 = Mathf.Lerp(depthAxisBottom, depthAxisTop,
                        depthFirst[index] / depthDenominator);
                    float y1 = Mathf.Lerp(depthAxisBottom, depthAxisTop,
                        depthLast[index] / depthDenominator);
                    SetLine(depthBucketAxisSegments[index],
                        new Vector3(depthAxisX, y0, depthAxisZ),
                        new Vector3(depthAxisX, y1, depthAxisZ));
                    if (depthBucketAxisLabels[index] != null)
                    {
                        depthBucketAxisLabels[index].gameObject.SetActive(showBuckets);
                        depthBucketAxisLabels[index].text = depthNames[index] +
                            "  z" + depthFirst[index] + "–" + depthLast[index];
                        depthBucketAxisLabels[index].text = depthNames[index];
                        depthBucketAxisLabels[index].transform.localPosition =
                            new Vector3(depthAxisX + 0.045f, (y0 + y1) * 0.5f,
                                depthAxisZ - 0.018f);
                    }
                }
            }
        }

        private bool AreSpatialAxisBindingsComplete(out string missing)
        {
            missing = string.Empty;
            if (spatialAxisRigStates.Count == 0)
            {
                missing = "the axis controller is still loading. Wait a moment, "
                    + "then try again.";
                return false;
            }
            if (BoundVariableIndices().Count == 0)
            {
                missing = "bind at least one variable to the shared controller.";
                return false;
            }
            SpatialAxisRigState state = spatialAxisRigStates[0];
            if (state.variableAxis < 0)
            {
                missing = "bind Variable to an axis.";
                return false;
            }
            if (state.timeAxis < 0)
            {
                missing = "bind Time to an axis.";
                return false;
            }
            if (state.depthAxis < 0)
            {
                missing = "bind Depth to a different axis.";
                return false;
            }
            if (state.timeAxis == state.depthAxis)
            {
                missing = "Time and Depth need different axes.";
                return false;
            }
            return true;
        }

        private void BuildFacetAxisGizmo()
        {
            CreateText(facetGridContent, "AXIS GIZMO", 9, FontStyle.Bold,
                new Vector2(608, 218), new Vector2(90, 16),
                TextAnchor.MiddleRight, Muted);
            CreateUiRule(facetGridContent, new Vector2(630, 198),
                new Vector2(42, 4), TimeColor, 0.0f);
            CreateUiRule(facetGridContent, new Vector2(609, 177),
                new Vector2(42, 4), VariableColor, 90.0f);
            CreateUiRule(facetGridContent, new Vector2(614, 185),
                new Vector2(34, 4), DepthAxisColor, 42.0f);
            CreateText(facetGridContent, "X", 9, FontStyle.Bold,
                new Vector2(655, 198), SlabLabLayout.AxisMarkerDotSize,
                TextAnchor.MiddleCenter, TimeColor);
            CreateText(facetGridContent, "Y", 9, FontStyle.Bold,
                new Vector2(609, 153), SlabLabLayout.AxisMarkerDotSize,
                TextAnchor.MiddleCenter, VariableColor);
            CreateText(facetGridContent, "Z", 9, FontStyle.Bold,
                new Vector2(638, 165), SlabLabLayout.AxisMarkerDotSize,
                TextAnchor.MiddleCenter, DepthAxisColor);
        }

        private void SetGroundDock(bool active)
        {
            if (panelCanvas == null)
                return;
            if (active && !groundDocked)
            {
                panelPreGroundPosition = panelCanvas.transform.localPosition;
                panelPreGroundRotation = panelCanvas.transform.localRotation;
                panelPreGroundScale = panelCanvas.transform.localScale;
                groundDocked = true;
                // Ground is the next workflow stage, so it temporarily replaces the
                // immutable grid instead of competing with it for space and pointer
                // hits. Return to Grid restores the snapshot at its anchored dock.
                panelCanvas.transform.localPosition = PrimaryToolDockPosition;
                panelCanvas.transform.localScale = Vector3.one * 0.00062f;
                FacePanelTowardViewer(panelCanvas.transform);
            }
            else if (!active && groundDocked)
            {
                StopGroundPlayback();
                groundDocked = false;
                panelCanvas.transform.localPosition = panelPreGroundPosition;
                panelCanvas.transform.localRotation = panelPreGroundRotation;
                panelCanvas.transform.localScale = panelPreGroundScale;
            }

            SetGroundEvidenceVisuals(active);
            if (groundLink != null)
            {
                SetMatPlotStcDashedLinkVisible(false);
                groundLink.gameObject.SetActive(active);
                if (active)
                    UpdateGroundEvidenceLink();
            }
        }

        private void FocusDesktopAxis()
        {
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled &&
                stage == Stage.Slab)
                SetDesktopFocusView(DesktopFocusView.SlabAxis, true);
        }

        private string FacetAxisSummary()
        {
            string[] names = { "Time", "Depth", "Horizontal", "Variable" };
            List<string> facets = new List<string>();
            for (int index = 0; index < roles.Length; index++)
                if (roles[index] == DimensionRole.Faceted)
                    facets.Add(names[index]);
            return facets.Count == 0
                ? "single panel (all non-spatial dimensions Fixed)"
                : string.Join(" x ", facets.ToArray()) + " facets";
        }

        private void BuildPivotAxisVisibilityControls(Vector2 gridPosition,
            Vector2 gridSize, int columns, int rows)
        {
            // These controls sit on top of the *source* matrix.  Use the
            // source orientation here; pivotTransposed describes the target
            // preview.  Mixing the two made valid source columns look like
            // nonexistent target buckets and covered them with HIDDEN washes.
            bool columnsAreDepth = activeGridTransposed;
            int columnDimension = columnsAreDepth ? 1 : 0;
            int rowDimension = columnsAreDepth ? 0 : 1;
            bool[] columnMask = columnsAreDepth
                ? selectedDepthTicks : selectedTimeTicks;
            bool[] rowMask = columnsAreDepth
                ? selectedTimeTicks : selectedDepthTicks;
            S4DIndexBucketRequest[] columnBuckets =
                DraftSourceBuckets(columnDimension);
            S4DIndexBucketRequest[] rowBuckets = DraftSourceBuckets(rowDimension);
            string[] columnFallback = columnsAreDepth
                ? new[] { "surface", "middle", "deep" }
                : new[] { "before", "during", "after" };
            string[] rowFallback = columnsAreDepth
                ? new[] { "before", "during", "after" }
                : new[] { "surface", "middle", "deep" };
            Color columnColor = columnsAreDepth ? DepthColor : TimeColor;
            Color rowColor = columnsAreDepth ? TimeColor : DepthColor;
            float columnWidth = gridSize.x / Mathf.Max(1, columns);
            float rowHeight = gridSize.y / Mathf.Max(1, rows);

            for (int column = 0; column < columns; column++)
            {
                int captured = column;
                bool visible = column < columnMask.Length && columnMask[column];
                Vector2 center = gridPosition + new Vector2(
                    -gridSize.x * 0.5f + columnWidth * (column + 0.5f),
                    gridSize.y * 0.5f + 25.0f);
                CreateButton(facetGridContent,
                    ActiveBucketLabel(columnBuckets, column, columnFallback)
                        .ToUpperInvariant(), center,
                    new Vector2(Mathf.Min(190, columnWidth - 18), 38),
                    visible ? columnColor : Card,
                    () => TogglePivotBucketVisibility(columnDimension, captured));
                if (!visible)
                {
                    Vector2 cellCenter = gridPosition + new Vector2(
                        -gridSize.x * 0.5f + columnWidth * (column + 0.5f), 0);
                    CreatePivotHiddenWash(cellCenter,
                        new Vector2(columnWidth - 12, gridSize.y - 12));
                }
            }

            for (int row = 0; row < rows; row++)
            {
                int captured = row;
                bool visible = row < rowMask.Length && rowMask[row];
                Vector2 center = gridPosition + new Vector2(
                    -gridSize.x * 0.5f - 56.0f,
                    gridSize.y * 0.5f - rowHeight * (row + 0.5f));
                CreateButton(facetGridContent,
                    ActiveBucketLabel(rowBuckets, row, rowFallback)
                        .ToUpperInvariant(), center,
                    new Vector2(108, Mathf.Min(58, rowHeight - 14)),
                    visible ? rowColor : Card,
                    () => TogglePivotBucketVisibility(rowDimension, captured));
                if (!visible)
                {
                    Vector2 cellCenter = gridPosition + new Vector2(0,
                        gridSize.y * 0.5f - rowHeight * (row + 0.5f));
                    CreatePivotHiddenWash(cellCenter,
                        new Vector2(gridSize.x - 12, rowHeight - 12));
                }
            }
        }

        private void BuildPivotAxisControls()
        {
            CreatePanelCard(panelContent, new Vector2(0, -91), new Vector2(1010, 126), Purple);
            CreateText(panelContent, "CHOOSE COMPARISON ORIENTATION", 13, FontStyle.Bold,
                new Vector2(-315, -52), new Vector2(390, 22),
                TextAnchor.MiddleLeft, Purple);
            CreateText(panelContent,
                "Pivot copies the current Slab. It changes only the Grid axes; the source snapshot remains in SlabTrail.",
                12, FontStyle.Normal, new Vector2(0, -77), new Vector2(940, 22),
                TextAnchor.MiddleLeft, Muted);
            CreateButton(panelContent, "TIME COLUMNS  /  DEPTH ROWS",
                new Vector2(-245, -117), new Vector2(450, 40),
                pivotTransposed ? Card : TimeColor, () => SetPivotOrientation(false));
            CreateButton(panelContent, "DEPTH COLUMNS  /  TIME ROWS",
                new Vector2(245, -117), new Vector2(450, 40),
                pivotTransposed ? Purple : Card, () => SetPivotOrientation(true));
        }

        private static void HideLegacyAxis()
        {
            MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour != null && behaviour.GetType().Name == "AxisContainer")
                    behaviour.gameObject.SetActive(false);
            }
        }

        private void ClearAllSpatialDropHighlights()
        {
            HighlightSpatialDropSlot(-1, -1, false);
        }

        private void CycleDimensionRole(int index)
        {
            if (index < 0 || index >= roles.Length || IsPending(PendingJob.Analysis))
                return;
            if (index == 2)
            {
                OpenBoundaryFromSlab(BoundaryDimension.Horizontal);
                SetStatus("Horizontal remains the mapped XY panel axis. Draw or reset its green analysis region here.");
                return;
            }

            // The current MatPlot contract is a 2D Facet Grid with Horizontal
            // mapped inside every panel. Time, Depth and Variable therefore
            // switch between Fixed and Faceted; at most two may be grid axes.
            roles[index] = roles[index] == DimensionRole.Faceted
                ? DimensionRole.Fixed
                : DimensionRole.Faceted;
            if (roles[index] == DimensionRole.Faceted)
            {
                int faceted = 0;
                for (int roleIndex = 0; roleIndex < roles.Length; roleIndex++)
                    if (roles[roleIndex] == DimensionRole.Faceted)
                        faceted++;
                if (faceted > 2)
                {
                    int demote = index == 3 ? 1 :
                        roles[3] == DimensionRole.Faceted ? 3 :
                        index == 1 ? 0 : 1;
                    if (demote != index)
                        roles[demote] = DimensionRole.Fixed;
                }
            }
            slabPreviewBuilt = false;
            intentConfigured = false;
            if (slabPreviewCanvas != null)
                slabPreviewCanvas.gameObject.SetActive(false);
            if (intentCanvas != null)
                intentCanvas.gameObject.SetActive(false);
            UpdateAnalysisAxisLabels();
            RefreshVariableFacetStacks();
            RecordTrailEvent("ROLE", new[] { "Time", "Depth", "Horizontal", "Variable" }[index] +
                " -> " + RoleLabel(roles[index]));
            SetStatus("Role changed: " + FacetAxisSummary() +
                ". Generate the Slab Frame again before Full Matrix.");
            BuildStage();
        }
    }
}
