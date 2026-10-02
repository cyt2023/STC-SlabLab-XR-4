using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    /// <summary>Split out of VolumeSTCubeQuestSpatialWorkbench: Boundary side of the workbench.</summary>
    public sealed partial class VolumeSTCubeQuestSpatialWorkbench
    {
        public void DesktopCancelBoundary()
        {
            if (stage != Stage.Field || !boundaryEditActive)
            {
                RejectDesktopAction("There is no active Time Range edit to cancel.");
                return;
            }
            CancelBoundaryEdit();
        }

        /// <summary>
        /// Open the typed Time-range entry (the `T` shortcut). Additive: dragging
        /// the cuts keeps working untouched, this is a second way to reach the
        /// same two values. Returns false when there is no Time range to type.
        /// </summary>
        public bool DesktopRangeEntry()
        {
            if (!DesktopBoundaryBarActive ||
                boundaryDimension != BoundaryDimension.Time ||
                roles[0] == DimensionRole.Fixed)
            {
                SetStatus(SlabLabBoundaryEntry.TimeUnavailable);
                return false;
            }
            input.boundaryRangeEntry = true;
            input.textInputActive = true;
            boundaryRangeEntryText = CurrentCutDaysText();
            RefreshBoundaryEntryLabel();
            SetStatus("Type the two cut days, then press Enter. Esc cancels.");
            return true;
        }

        /// <summary>
        /// Enter. Invalid text keeps the entry open so a typo can be fixed
        /// instead of silently doing nothing.
        /// </summary>
        public void DesktopApplyRangeEntry()
        {
            if (!input.boundaryRangeEntry)
                return;
            int count = selectedDataset != null ? selectedDataset.TimeCount : 0;
            if (!SlabLabBoundaryEntry.TryParseTimeCuts(boundaryRangeEntryText,
                    count, out int start, out int end))
            {
                SetStatus(SlabLabBoundaryEntry.TimeInvalid + "  " +
                    SlabLabBoundaryEntry.ExampleLine(count));
                RefreshBoundaryEntryLabel(true);
                return;
            }
            input.boundaryRangeEntry = false;
            input.textInputActive = false;
            // Same working values and same refresh as NudgeActiveBoundary, so a
            // typed range is previewed and confirmed exactly like a dragged one.
            timeBoundaryStart = start;
            timeBoundaryEnd = end;
            UpdateTimeBoundaryHandles();
            PreviewBoundaryTime(timeBoundaryEnd);
            SetStatus("Time range typed: " + TimeRangeSummary() + ".");
            BuildBoundaryPanel();
        }

        /// <summary>Esc: leave the cuts where they were.</summary>
        public void DesktopCancelRangeEntry()
        {
            if (!input.boundaryRangeEntry)
                return;
            input.boundaryRangeEntry = false;
            input.textInputActive = false;
            SetStatus("Numeric entry cancelled. The cuts were left unchanged.");
            BuildBoundaryPanel();
        }

        /// <summary>The two days the readout shows, ready to be edited.</summary>
        private string CurrentCutDaysText()
        {
            int count = selectedDataset != null ? selectedDataset.TimeCount : 0;
            int firstCut = Mathf.Clamp(timeBoundaryStart, 1,
                Mathf.Max(1, count - 2));
            int secondCut = Mathf.Clamp(timeBoundaryEnd + 1, firstCut + 1,
                Mathf.Max(firstCut + 1, count - 1));
            return firstCut + "," + secondCut;
        }

        private void RefreshBoundaryEntryLabel(bool invalid = false)
        {
            if (boundaryCurrentRangeText == null)
                return;
            string caret = boundaryRangeEntryText + "_";
            boundaryCurrentRangeText.text = invalid
                ? SlabLabBoundaryEntry.TimeInvalid + "\n" + caret
                : SlabLabBoundaryEntry.TimePrompt + "\n" + caret;
        }

        public void DesktopConfirmBoundary()
        {
            if (stage != Stage.Field || !boundaryEditActive)
            {
                RejectDesktopAction("Open Set Time Range before confirming it.");
                return;
            }
            ApplyBoundaryChange();
        }

        private GameObject CreateVariableBoundaryScopeButton(Transform parent,
            string label, bool custom, Vector2 position)
        {
            GameObject root = new GameObject(label + " boundary scope root");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(
                position.x, position.y, 0.010f);
            GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cube);
            button.name = label + " boundary scope button";
            button.layer = 5;
            button.transform.SetParent(root.transform, false);
            button.transform.localScale = new Vector3(0.090f, 0.030f, 0.024f);
            button.GetComponent<Renderer>().material = CreateStableOpaqueMaterial(
                new Color(0.10f, 0.16f, 0.21f, 1.0f));
            button.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked = () =>
            {
                SetActiveBoundaryScope(custom);
                UpdateVariableBoundaryScopeButtons();
            };
            CreatePalettePhysicalText(root.transform, label,
                new Vector3(-0.012f, 0.0f, 0.020f), 0.0025f, Ink);
            return button;
        }

        private void UpdateVariableBoundaryScopeButtons()
        {
            SpatialAxisRigState state = ActiveVariableBoundaryState();
            bool custom = state != null && !state.usesSharedBoundaries;
            UpdateVariableBoundaryScopeButton(variableSharedScopeButton,
                !custom);
            UpdateVariableBoundaryScopeButton(variableCustomScopeButton,
                custom);
        }

        private void UpdateVariableBoundaryScopeButton(GameObject button,
            bool active)
        {
            if (button == null)
                return;
            Renderer renderer = button.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = active
                    ? Cyan : new Color(0.10f, 0.16f, 0.21f, 1.0f);
        }

        private void EnterBoundaryAuthoringView()
        {
            if (boundaryAuthoringCanonicalView)
                return;
            boundaryAuthoringCanonicalView = true;
            boundaryAuthoringRestoreRotation = fieldAxisRemapRotation;
            if (fieldAxisRemapCoroutine != null)
            {
                StopCoroutine(fieldAxisRemapCoroutine);
                fieldAxisRemapCoroutine = null;
            }
            StartBoundaryAuthoringRotation(Quaternion.identity);
        }

        private void ExitBoundaryAuthoringView()
        {
            if (!boundaryAuthoringCanonicalView)
                return;
            boundaryAuthoringCanonicalView = false;
            fieldAxisRemapRotation = boundaryAuthoringRestoreRotation;
            StartBoundaryAuthoringRotation(boundaryAuthoringRestoreRotation);
        }

        private void StartBoundaryAuthoringRotation(Quaternion target)
        {
            if (boundaryAuthoringRotationCoroutine != null)
                StopCoroutine(boundaryAuthoringRotationCoroutine);
            boundaryAuthoringRotationCoroutine = StartCoroutine(
                AnimateBoundaryAuthoringRotation(target));
        }

        private IEnumerator AnimateBoundaryAuthoringRotation(Quaternion target)
        {
            Transform volumeRoot = currentView != null &&
                currentView.rootObject != null
                    ? currentView.rootObject.transform : null;
            if (volumeRoot == null)
            {
                boundaryAuthoringRotationCoroutine = null;
                yield break;
            }
            Quaternion start = volumeRoot.localRotation;
            float elapsed = 0.0f;
            const float duration = 0.42f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // Smooth cubic easing makes the reorientation readable in VR
                // without the abrupt one-frame jump of the old slice editor.
                t = t * t * (3.0f - 2.0f * t);
                volumeRoot.localRotation = Quaternion.Slerp(start, target, t);
                RecenterVolumeBoundsOnField(volumeRoot);
                yield return null;
            }
            volumeRoot.localRotation = target;
            // Recompute scale and bounds for the finished orientation. This is
            // what makes "turn upright" an in-place Field animation rather than
            // an orbit around the imported RAW object's off-centre pivot.
            FrameVolume();
            boundaryAuthoringRotationCoroutine = null;
        }

        private void CreateDepthBoundaryPlane(int index)
        {
            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plane.name = index == 0 ? "Depth boundary lower" : "Depth boundary upper";
            plane.layer = 5;
            plane.transform.SetParent(spatialRoot.transform, false);
            plane.transform.localScale = new Vector3(
                FieldHalfWidth * 1.72f, 0.020f, FieldHalfDepth * 1.72f);
            Material material = new Material(Shader.Find("Sprites/Default"));
            material.color = index == 0
                ? new Color(DepthColor.r, DepthColor.g, DepthColor.b, 0.28f)
                : new Color(Cyan.r, Cyan.g, Cyan.b, 0.20f);
            plane.GetComponent<Renderer>().material = material;
            int boundaryIndex = index;
            plane.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                () => BeginDepthBoundaryDrag(boundaryIndex);
            depthBoundaryPlanes[index] = plane;
            depthBoundaryValueLabels[index] = CreateWorldLabel(
                index == 0 ? "LOWER  z=00" : "UPPER  z=00",
                Vector3.zero, 0.010f, TextAnchor.MiddleRight,
                index == 0 ? DepthColor : Cyan);
        }

        private void BeginDepthBoundaryDrag(int index)
        {
            if (boundaryCanvas == null || !boundaryCanvas.gameObject.activeSelf ||
                boundaryDimension != BoundaryDimension.Depth)
            {
                SetStatus("Open Author Boundary / Depth before moving depth planes.");
                return;
            }
            bool fixedDepth = roles[1] == DimensionRole.Fixed;
            activeDepthBoundary = fixedDepth ? 0 : Mathf.Clamp(index, 0, 1);
            interaction.depthBoundaryDragging = true;
            BeginDepthSliceInspection(
                fixedDepth ? slabNormalized :
                activeDepthBoundary == 0 ? depthBoundaryLow : depthBoundaryHigh);
#if UNITY_EDITOR || SLABLAB_FLAT
            if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
            {
                desktopDepthBoundaryStartMouseY = FlatPointerPosition.y;
                desktopDepthBoundaryStartValue =
                    fixedDepth ? slabNormalized :
                    activeDepthBoundary == 0 ? depthBoundaryLow : depthBoundaryHigh;
            }
#endif
            SetStatus(fixedDepth
                ? "Dragging the fixed Depth plane. Release to select one z value."
                : "Dragging the " + (activeDepthBoundary == 0 ? "lower" : "upper") +
                  " depth boundary plane.");
        }

        private void UpdateDepthBoundaryInteraction()
        {
            if (!interaction.depthBoundaryDragging || rayInteractor == null)
                return;
            if (rayInteractor.TriggerHeld)
            {
                bool fixedDepth = roles[1] == DimensionRole.Fixed;
                float next;
#if UNITY_EDITOR || SLABLAB_FLAT
                if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
                    next = Mathf.Clamp01(desktopDepthBoundaryStartValue +
                        (FlatPointerPosition.y - desktopDepthBoundaryStartMouseY) /
                        Mathf.Max(360.0f, Screen.height * 0.72f));
                else
#endif
                {
                    float target = GetHandDepthNormalized();
                    float current = fixedDepth
                        ? slabNormalized
                        : activeDepthBoundary == 0
                        ? depthBoundaryLow
                        : depthBoundaryHigh;
                    float follow = 1.0f - Mathf.Exp(-22.0f * Time.unscaledDeltaTime);
                    next = Mathf.Lerp(current, target, follow);
                }

                if (fixedDepth)
                {
                    slabNormalized = Mathf.Clamp01(next);
                    int maxDepth = selectedDataset != null
                        ? Mathf.Max(1, selectedDataset.DimZ - 1) : 90;
                    selectedZ = Mathf.RoundToInt(slabNormalized * maxDepth);
                }
                else if (activeDepthBoundary == 0)
                    depthBoundaryLow = Mathf.Clamp(next, 0.0f, depthBoundaryHigh - 0.05f);
                else
                    depthBoundaryHigh = Mathf.Clamp(next, depthBoundaryLow + 0.05f, 1.0f);
                UpdateDepthBoundaryPlanes();
                UpdateDepthSliceInspection(next);
            }
            if (rayInteractor.TriggerReleased)
            {
                bool fixedDepth = roles[1] == DimensionRole.Fixed;
                int maxDepth = selectedDataset != null
                    ? Mathf.Max(1, selectedDataset.DimZ - 1)
                    : 90;
                if (fixedDepth)
                {
                    selectedZ = Mathf.RoundToInt(slabNormalized * maxDepth);
                    slabNormalized = selectedZ / (float)maxDepth;
                    depthInspectionOriginalNormalized = slabNormalized;
                    depthInspectionOriginalZ = selectedZ;
                }
                else if (activeDepthBoundary == 0)
                    depthBoundaryLow = Mathf.Round(depthBoundaryLow * maxDepth) /
                        maxDepth;
                else
                    depthBoundaryHigh = Mathf.Round(depthBoundaryHigh * maxDepth) /
                        maxDepth;
                UpdateDepthBoundaryPlanes();
                interaction.depthBoundaryDragging = false;
                EndDepthSliceInspection(false);
                SetStatus(fixedDepth
                    ? "Fixed Depth selected: z=" + selectedZ + "."
                    : "Depth band grounded: " + DepthBoundaryLabel() + ".");
                if (boundaryCanvas != null && boundaryCanvas.gameObject.activeSelf)
                    BuildBoundaryPanel();
            }
        }

        private void UpdateDepthBoundaryPlanes()
        {
            bool fixedDepth = roles[1] == DimensionRole.Fixed;
            Vector3 fieldCenter = ActiveBoundaryFieldCenter();
            int maxDepth = selectedDataset != null
                ? Mathf.Max(1, selectedDataset.DimZ - 1)
                : 90;
            for (int index = 0; index < depthBoundaryPlanes.Length; index++)
            {
                if (depthBoundaryPlanes[index] == null)
                    continue;
                float value = fixedDepth && index == 0
                    ? slabNormalized
                    : index == 0 ? depthBoundaryLow : depthBoundaryHigh;
                float y = Mathf.Lerp(volumeLocalMinY, volumeLocalMaxY, value);
                bool highlighted = boundaryEditActive &&
                    boundaryDimension == BoundaryDimension.Depth &&
                    (!interaction.depthBoundaryDragging || index == activeDepthBoundary);
                depthBoundaryPlanes[index].transform.localPosition =
                    fieldCenter + new Vector3(0.0f, y, 0.0f);
                depthBoundaryPlanes[index].transform.localScale =
                    new Vector3(FieldHalfWidth * 1.72f,
                        highlighted ? 0.028f : 0.012f,
                        FieldHalfDepth * 1.72f);
                Renderer planeRenderer = depthBoundaryPlanes[index].GetComponent<Renderer>();
                if (planeRenderer != null)
                {
                    Color planeColor = index == 0 ? DepthColor : Cyan;
                    planeColor.a = highlighted
                        ? (interaction.depthBoundaryDragging ? 0.58f : 0.38f)
                        : 0.12f;
                    planeRenderer.material.color = planeColor;
                }
                if (depthBoundaryValueLabels[index] != null)
                {
                    int z = Mathf.RoundToInt(value * maxDepth);
                    depthBoundaryValueLabels[index].text = fixedDepth && index == 0
                        ? "FIXED DEPTH  z=" + z
                        : (index == 0 ? "LOWER  " : "UPPER  ") + "z=" + z;
                    depthBoundaryValueLabels[index].transform.localPosition =
                        fieldCenter + new Vector3(FieldHalfWidth - 0.025f,
                            y + 0.035f, -FieldHalfDepth * 0.92f);
                }
            }
            UpdateAxisBucketGuides();
        }

        private void SetDepthBoundaryVisibility(bool visible)
        {
            bool fixedDepth = roles[1] == DimensionRole.Fixed;
            for (int index = 0; index < depthBoundaryPlanes.Length; index++)
            {
                bool itemVisible = visible && (!fixedDepth || index == 0);
                if (depthBoundaryPlanes[index] != null)
                    depthBoundaryPlanes[index].SetActive(itemVisible);
                if (depthBoundaryValueLabels[index] != null)
                    depthBoundaryValueLabels[index].gameObject.SetActive(itemVisible);
            }
        }

        private string DepthBoundaryLabel()
        {
            int maxDepth = selectedDataset != null ? Mathf.Max(1, selectedDataset.DimZ - 1) : 90;
            int lower = Mathf.RoundToInt(depthBoundaryLow * maxDepth);
            int upper = Mathf.RoundToInt(depthBoundaryHigh * maxDepth);
            return "z" + lower.ToString("00") + "-z" + upper.ToString("00");
        }

        private void CreateTimeRail()
        {
            GameObject rail = new GameObject("Day 1 to Day 30 rail");
            rail.transform.SetParent(spatialRoot.transform, false);
            rail.transform.localPosition =
                new Vector3(0.0f, -FieldHalfHeight + 0.095f, FieldHalfDepth * 0.88f);
            timeRail = rail.transform;
            CreateWorldLine("Time rail backing", rail.transform,
                new Vector3(-TimeRailHalfWidth, 0.0f, 0.0f),
                new Vector3(TimeRailHalfWidth, 0.0f, 0.0f), Card, 0.032f);
            CreateWorldLine("Time rail", rail.transform,
                new Vector3(-TimeRailHalfWidth, 0.0f, -0.002f),
                new Vector3(TimeRailHalfWidth, 0.0f, -0.002f), Amber, 0.016f);
            timeAxisLabel = CreateWorldLabel("X  ·  TIME / DAY",
                new Vector3(0.0f, 0.235f, -0.045f),
                0.0090f, TextAnchor.MiddleCenter, TimeColor, rail.transform);
            CreateWorldLabel("day 1", new Vector3(-TimeRailHalfWidth, -0.075f, 0.0f),
                0.011f, TextAnchor.MiddleLeft, Ink, rail.transform);
            CreateWorldLabel("day 30", new Vector3(TimeRailHalfWidth, -0.075f, 0.0f),
                0.011f, TextAnchor.MiddleRight, Ink, rail.transform);

            Color[] timeColors =
            {
                new Color(1.0f, 0.48f, 0.10f, 0.96f),
                new Color(1.0f, 0.72f, 0.06f, 0.98f),
                new Color(1.0f, 0.88f, 0.24f, 0.96f)
            };
            for (int index = 0; index < 3; index++)
            {
                timeBucketAxisSegments[index] = CreateWorldLine(
                    "Time bucket axis " + index, rail.transform,
                    Vector3.zero, Vector3.zero, timeColors[index], 0.025f);
                timeBucketAxisLabels[index] = CreateWorldLabel(
                    "TIME BUCKET", Vector3.zero, 0.0058f,
                    TextAnchor.MiddleCenter, timeColors[index], rail.transform);
                timeBucketAxisSegments[index].gameObject.SetActive(false);
                timeBucketAxisLabels[index].gameObject.SetActive(false);
            }
            CreateTimeBoundaryHandle(0, "START");
            CreateTimeBoundaryHandle(1, "END");
            UpdateTimeBoundaryHandles();
            SetTimeBoundaryHandleVisibility(false);
            UpdateAxisBucketGuides();
        }

        private void CreateTimeBoundaryHandle(int index, string label)
        {
            GameObject handle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            handle.name = "Time boundary " + label;
            handle.layer = 5;
            handle.transform.SetParent(timeRail, false);
            handle.transform.localScale = new Vector3(0.035f, 0.135f, 0.035f);
            Material material = new Material(Shader.Find("Sprites/Default"));
            material.color = index == 0 ? TimeColor : Amber;
            handle.GetComponent<Renderer>().material = material;
            int boundaryIndex = index;
            handle.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                () => BeginTimeBoundaryDrag(boundaryIndex);
            timeBoundaryHandles[index] = handle;
            timeBoundaryValueLabels[index] = CreateWorldLabel(
                label + "  day --", Vector3.zero, 0.0115f,
                TextAnchor.MiddleCenter, index == 0 ? TimeColor : Amber, timeRail);
        }

        private void BeginTimeBoundaryDrag(int index)
        {
            if (boundaryCanvas == null || !boundaryCanvas.gameObject.activeSelf ||
                boundaryDimension != BoundaryDimension.Time)
            {
                SetStatus("Open Author Boundary / Time before moving interval flags.");
                return;
            }
            activeTimeBoundary = Mathf.Clamp(index, 0, 1);
            interaction.timeBoundaryDragging = true;
            SetStatus("Dragging " + (activeTimeBoundary == 0 ? "start" : "end") +
                " flag on the continuous time rail.");
        }

        private void UpdateTimeBoundaryInteraction()
        {
            if (!interaction.timeBoundaryDragging || rayInteractor == null || timeRail == null)
                return;
            if (rayInteractor.TriggerHeld)
            {
                bool fixedTime = roles[0] == DimensionRole.Fixed;
                Plane railPlane = new Plane(spatialRoot.transform.forward, timeRail.position);
                if (railPlane.Raycast(rayInteractor.PointerRay, out float distance))
                {
                    Vector3 local = timeRail.InverseTransformPoint(
                        rayInteractor.PointerRay.GetPoint(distance));
                    int count = selectedDataset != null ? selectedDataset.TimeCount : 30;
                    int index = Mathf.RoundToInt(Mathf.InverseLerp(
                        -TimeRailHalfWidth, TimeRailHalfWidth, local.x) *
                        Mathf.Max(1, count - 1));
                    if (fixedTime)
                        selectedTime = Mathf.Clamp(index, 0, count - 1);
                    else if (activeTimeBoundary == 0)
                        timeBoundaryStart = Mathf.Clamp(index, 0, timeBoundaryEnd - 1);
                    else
                        timeBoundaryEnd = Mathf.Clamp(index, timeBoundaryStart + 1, count - 1);
                    UpdateTimeBoundaryHandles();
                    PreviewBoundaryTime(fixedTime
                        ? selectedTime
                        : activeTimeBoundary == 0
                        ? timeBoundaryStart
                        : timeBoundaryEnd);
                }
            }
            if (rayInteractor.TriggerReleased)
            {
                bool fixedTime = roles[0] == DimensionRole.Fixed;
                interaction.timeBoundaryDragging = false;
                CommitBoundaryTimePreview();
                // Keep the selected-time evidence card pinned after release.
                // It is an independent scientific snapshot and must not follow
                // or flicker with the continuously playing ground surface.
                FrameVolume();
                StartCoroutine(RefitVolumeAfterFrameChange());
                SetStatus(fixedTime
                    ? "Fixed Time selected: " +
                      (selectedDataset != null
                          ? selectedDataset.GetTimeLabel(selectedTime)
                          : "time " + (selectedTime + 1)) + "."
                    : "Time interval grounded: day " + (timeBoundaryStart + 1) +
                      " to day " + (timeBoundaryEnd + 1) + ".");
                if (boundaryCanvas != null && boundaryCanvas.gameObject.activeSelf)
                    BuildBoundaryPanel();
            }
        }

        private void PreviewBoundaryTime(int timeIndex)
        {
            if (selectedDataset == null)
                return;
            int nextTime = Mathf.Clamp(timeIndex, 0, selectedDataset.TimeCount - 1);
            bool changed = selectedTime != nextTime ||
                boundaryDayPreviewTime != nextTime;
            selectedTime = nextTime;
            SpatialAxisRigState boundaryState = ActiveVariableBoundaryState();
            if (boundaryState != null && !boundaryState.usesSharedBoundaries)
                boundaryState.customSelectedTime = selectedTime;
            else
                sharedSelectedTime = selectedTime;
            // Keep the primary 3D Field stable while a boundary is moving.
            // Rebuilding the volume for every crossed day caused unrelated
            // textures to flash inside the cube. Only the rear day-preview
            // surface changes during the drag; the Field commits once on release.
            if (changed || slabTexture == null)
                RefreshSlabTexture();
            ShowBoundaryDayPreview(nextTime);
            RebuildTimeMarkers();
            UpdateSlabVisual(false);
            SetStatus("Boundary preview: " +
                selectedDataset.GetTimeLabel(selectedTime) +
                ". The rear Field panel shows this fixed-time geographic snapshot; release to pin it.");
        }

        private void CommitBoundaryTimePreview()
        {
            if (selectedDataset == null)
                return;
            // Boundary selection owns a separate rear-face preview. It must
            // never pause, reset, or replace the continuously playing surface.
            if (forVrSurfacePlayer != null)
            {
                // Intentionally leave the independent player untouched.
            }
            else if (UsesFacetedVariableFields())
                RefreshVariableFacetStacks();
            else
                ApplyTimeFilter();
            ApplyPrimaryVolumeVisibility();
            RefreshSlabTexture();
            RebuildTimeMarkers();
            UpdateSlabVisual(false);
        }

        private void EnsureBoundaryDayPreview()
        {
            if (boundaryDayPreviewObject != null || spatialRoot == null)
                return;
            boundaryDayPreviewObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            boundaryDayPreviewObject.name = "Boundary day preview on rear Field face";
            Collider previewCollider = boundaryDayPreviewObject.GetComponent<Collider>();
            if (previewCollider != null)
                Destroy(previewCollider);
            boundaryDayPreviewObject.transform.SetParent(spatialRoot.transform, false);
            boundaryDayPreviewMaterial = new Material(Shader.Find("Sprites/Default"));
            boundaryDayPreviewMaterial.color = new Color(1.0f, 1.0f, 1.0f, 0.0f);
            boundaryDayPreviewMaterial.renderQueue = 3050;
            Renderer previewRenderer = boundaryDayPreviewObject.GetComponent<Renderer>();
            previewRenderer.enabled = false;
            previewRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            previewRenderer.receiveShadows = false;

            boundaryDayPreviewMapObject = GameObject.CreatePrimitive(
                PrimitiveType.Quad);
            boundaryDayPreviewMapObject.name = "Selected-time Hong Kong map";
            Collider mapCollider = boundaryDayPreviewMapObject.GetComponent<Collider>();
            if (mapCollider != null)
                Destroy(mapCollider);
            boundaryDayPreviewMapObject.transform.SetParent(
                boundaryDayPreviewObject.transform, false);
            boundaryDayPreviewMapObject.transform.localPosition =
                new Vector3(0.205f, 0.0f, 0.0f);
            boundaryDayPreviewMapObject.transform.localScale =
                new Vector3(0.58f, 0.58f, 1.0f);
            Renderer mapRenderer = boundaryDayPreviewMapObject.GetComponent<Renderer>();
            mapRenderer.material = boundaryDayPreviewMaterial;
            mapRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            mapRenderer.receiveShadows = false;

            boundaryDayPreviewDataObject = new GameObject(
                "Exact geographic selected-time layer", typeof(MeshFilter),
                typeof(MeshRenderer));
            boundaryDayPreviewDataObject.transform.SetParent(
                boundaryDayPreviewMapObject.transform, false);
            boundaryDayPreviewDataMesh = new Mesh
            {
                name = "Selected-time exact Hong Kong mesh"
            };
            boundaryDayPreviewDataMesh.MarkDynamic();
            boundaryDayPreviewDataObject.GetComponent<MeshFilter>().sharedMesh =
                boundaryDayPreviewDataMesh;
            boundaryDayPreviewDataMaterial = new Material(Shader.Find("Sprites/Default"));
            boundaryDayPreviewDataMaterial.renderQueue = 3060;
            MeshRenderer dataRenderer =
                boundaryDayPreviewDataObject.GetComponent<MeshRenderer>();
            dataRenderer.material = boundaryDayPreviewDataMaterial;
            dataRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            dataRenderer.receiveShadows = false;
            boundaryDayPreviewDataObject.SetActive(false);

            boundaryDayPreviewLegendObject = GameObject.CreatePrimitive(
                PrimitiveType.Quad);
            boundaryDayPreviewLegendObject.name = "Selected-time shared color scale";
            Collider legendCollider = boundaryDayPreviewLegendObject.GetComponent<Collider>();
            if (legendCollider != null)
                Destroy(legendCollider);
            boundaryDayPreviewLegendObject.transform.SetParent(
                boundaryDayPreviewMapObject.transform, false);
            boundaryDayPreviewLegendObject.transform.localPosition =
                new Vector3(0.455f, 0.0f, -0.018f);
            boundaryDayPreviewLegendObject.transform.localScale =
                new Vector3(0.028f, 0.70f, 1.0f);
            boundaryDayPreviewLegendTexture = new Texture2D(
                16, 128, TextureFormat.RGBA32, false, false)
            {
                name = "Selected-time shared physical scale",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            boundaryDayPreviewLegendMaterial = new Material(
                Shader.Find("Sprites/Default"));
            boundaryDayPreviewLegendMaterial.mainTexture =
                boundaryDayPreviewLegendTexture;
            boundaryDayPreviewLegendMaterial.renderQueue = 3070;
            boundaryDayPreviewLegendObject.GetComponent<Renderer>().material =
                boundaryDayPreviewLegendMaterial;
            boundaryDayPreviewLegendObject.SetActive(false);

            boundaryDayPreviewLabel = CreateWorldLabel(
                "SELECTED TIME", new Vector3(-0.31f, 0.205f, 0.015f),
                0.0046f, TextAnchor.MiddleCenter, Ink,
                boundaryDayPreviewObject.transform);
            // TextMesh renders its readable face opposite the textured Quad.
            // Turn only the label around so the day text is never mirrored.
            boundaryDayPreviewLabel.transform.localRotation =
                Quaternion.Euler(0.0f, 180.0f, 0.0f);
            boundaryDayPreviewStatsLabel = CreateWorldLabel(
                "MIN\nMEAN\nMAX", new Vector3(-0.31f, -0.165f, 0.015f),
                0.0045f, TextAnchor.MiddleCenter, Ink,
                boundaryDayPreviewObject.transform);
            boundaryDayPreviewStatsLabel.transform.localRotation =
                Quaternion.Euler(0.0f, 180.0f, 0.0f);
            boundaryDayPreviewScaleLabel = CreateWorldLabel(
                "SCALE", new Vector3(0.405f, 0.0f, 0.018f),
                0.0038f, TextAnchor.MiddleRight, Ink,
                boundaryDayPreviewMapObject.transform);
            boundaryDayPreviewScaleLabel.transform.localRotation =
                Quaternion.Euler(0.0f, 180.0f, 0.0f);
            boundaryDayPreviewScaleLabel.gameObject.SetActive(false);
            Color calloutColor = new Color(0.20f, 0.92f, 0.96f, 0.92f);
            CreateDashedLocalLine(boundaryDayPreviewObject.transform,
                "Selected-time callout lead A",
                new Vector3(-0.085f, 0.10f, -0.024f),
                new Vector3(-0.125f, 0.10f, -0.024f), 3,
                calloutColor, 0.005f);
            CreateDashedLocalLine(boundaryDayPreviewObject.transform,
                "Selected-time callout lead B",
                new Vector3(-0.125f, 0.10f, -0.024f),
                new Vector3(-0.14f, 0.18f, -0.024f), 3,
                calloutColor, 0.005f);
            CreateDashedRectangle(boundaryDayPreviewObject.transform,
                "Selected-time callout box", -0.49f, -0.14f,
                -0.44f, 0.44f, calloutColor, 0.005f);
            boundaryDayPreviewObject.SetActive(false);
        }

        private void ShowBoundaryDayPreview(int timeIndex)
        {
            if (selectedDataset == null)
                return;
            EnsureBoundaryDayPreview();
            if (boundaryDayPreviewObject == null || boundaryDayPreviewMaterial == null)
                return;
            if (boundaryDayPreviewHideAnimation != null)
            {
                StopCoroutine(boundaryDayPreviewHideAnimation);
                boundaryDayPreviewHideAnimation = null;
            }
            bool wasVisible = boundaryDayPreviewObject.activeSelf;
            boundaryDayPreviewTime = timeIndex;
            string snapshotHeading = string.Empty;
            string snapshotStatistics = string.Empty;
            string snapshotScale = string.Empty;
            bool professionalGeographicPreview = forVrSurfacePlayer != null &&
                forVrSurfacePlayer.TryUpdateGeographicSnapshot(
                    boundaryDayPreviewDataMesh, timeIndex,
                    out snapshotHeading, out snapshotStatistics,
                    out snapshotScale);
            boundaryDayPreviewMaterial.mainTexture = professionalGeographicPreview
                ? Resources.Load<Texture2D>("HongKongOSM")
                : slabTexture;
            boundaryDayPreviewMaterial.mainTextureScale = Vector2.one;
            boundaryDayPreviewMaterial.mainTextureOffset = Vector2.zero;
            if (boundaryDayPreviewDataObject != null)
                boundaryDayPreviewDataObject.SetActive(professionalGeographicPreview);
            if (boundaryDayPreviewLegendObject != null)
                boundaryDayPreviewLegendObject.SetActive(professionalGeographicPreview);
            if (boundaryDayPreviewScaleLabel != null)
            {
                boundaryDayPreviewScaleLabel.gameObject.SetActive(
                    professionalGeographicPreview);
                boundaryDayPreviewScaleLabel.text = snapshotScale;
            }
            if (professionalGeographicPreview &&
                boundaryDayPreviewLegendTexture != null)
                forVrSurfacePlayer.UpdateSnapshotLegend(
                    boundaryDayPreviewLegendTexture);

            if (!wasVisible)
            {
                Vector3 localCamera = xrCamera != null
                    ? spatialRoot.transform.InverseTransformPoint(xrCamera.transform.position)
                    : new Vector3(0.0f, 0.0f, 2.0f);
                float viewerSign = localCamera.z >= 0.0f ? 1.0f : -1.0f;
                Vector3 previewPosition = new Vector3(
                    0.0f, 0.10f, -viewerSign * (FieldHalfDepth - 0.018f));
                boundaryDayPreviewObject.transform.localPosition = previewPosition;
                Vector3 towardViewer = localCamera - previewPosition;
                if (towardViewer.sqrMagnitude < 0.001f)
                    towardViewer = Vector3.forward * viewerSign;
                boundaryDayPreviewObject.transform.localRotation =
                    Quaternion.LookRotation(towardViewer.normalized, Vector3.up);
            }

            if (boundaryDayPreviewLabel != null)
                boundaryDayPreviewLabel.text = professionalGeographicPreview
                    ? snapshotHeading
                    : "SELECTED TIME " + (timeIndex + 1) + "\n" +
                      selectedDataset.GetTimeLabel(timeIndex);
            if (boundaryDayPreviewStatsLabel != null)
            {
                boundaryDayPreviewStatsLabel.gameObject.SetActive(
                    professionalGeographicPreview);
                boundaryDayPreviewStatsLabel.text = snapshotStatistics;
            }
            boundaryDayPreviewObject.SetActive(true);
            if (!wasVisible)
            {
                if (boundaryDayPreviewAnimation != null)
                    StopCoroutine(boundaryDayPreviewAnimation);
                boundaryDayPreviewAnimation = StartCoroutine(
                    AnimateBoundaryDayPreviewIn());
            }
            else
            {
                boundaryDayPreviewObject.transform.localScale =
                    BoundaryPreviewTargetScale(professionalGeographicPreview);
                boundaryDayPreviewMaterial.color =
                    BoundaryPreviewMapColor(professionalGeographicPreview, 0.96f);
                if (boundaryDayPreviewLabel != null)
                    boundaryDayPreviewLabel.color = Ink;
                if (boundaryDayPreviewStatsLabel != null)
                    boundaryDayPreviewStatsLabel.color = Ink;
            }
        }

        private IEnumerator AnimateBoundaryDayPreviewIn()
        {
            bool geographic = boundaryDayPreviewDataObject != null &&
                boundaryDayPreviewDataObject.activeSelf;
            Vector3 targetScale = BoundaryPreviewTargetScale(
                geographic);
            Vector3 startScale = targetScale * 0.91f;
            startScale.z = 1.0f;
            boundaryDayPreviewObject.transform.localScale = startScale;
            float elapsed = 0.0f;
            const float duration = 0.20f;
            while (elapsed < duration && boundaryDayPreviewObject != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0.0f, 1.0f,
                    Mathf.Clamp01(elapsed / duration));
                boundaryDayPreviewObject.transform.localScale =
                    Vector3.Lerp(startScale, targetScale, t);
                if (boundaryDayPreviewMaterial != null)
                    boundaryDayPreviewMaterial.color =
                        BoundaryPreviewMapColor(geographic,
                            Mathf.Lerp(0.16f, 0.96f, t));
                if (boundaryDayPreviewLabel != null)
                    boundaryDayPreviewLabel.color =
                        new Color(Ink.r, Ink.g, Ink.b, t);
                if (boundaryDayPreviewStatsLabel != null)
                    boundaryDayPreviewStatsLabel.color =
                        new Color(Ink.r, Ink.g, Ink.b, t);
                yield return null;
            }
            if (boundaryDayPreviewObject != null)
                boundaryDayPreviewObject.transform.localScale = targetScale;
            boundaryDayPreviewAnimation = null;
        }

        private static Vector3 BoundaryPreviewTargetScale(bool geographic)
        {
            float width = geographic
                ? FieldHalfWidth * 1.78f
                : FieldHalfWidth * 1.48f;
            // The geographic snapshot uses the projected Hong Kong aspect
            // ratio instead of stretching the map to fill the rear face.
            float height = geographic
                ? width / 1.397f
                : FieldHalfHeight * 1.18f;
            return new Vector3(width, height, 1.0f);
        }

        private static Color BoundaryPreviewMapColor(bool geographic, float alpha)
        {
            // Keep the basemap bright enough to read while the saturated,
            // high-opacity scientific layer remains visually dominant.
            return geographic
                ? new Color(0.80f, 0.82f, 0.82f, alpha)
                : new Color(1.0f, 1.0f, 1.0f, alpha);
        }

        private void HideBoundaryDayPreviewSmoothly()
        {
            if (boundaryDayPreviewObject == null ||
                !boundaryDayPreviewObject.activeSelf)
                return;
            if (boundaryDayPreviewHideAnimation != null)
                StopCoroutine(boundaryDayPreviewHideAnimation);
            boundaryDayPreviewHideAnimation = StartCoroutine(
                AnimateBoundaryDayPreviewOut());
        }

        private IEnumerator AnimateBoundaryDayPreviewOut()
        {
            yield return new WaitForSecondsRealtime(0.28f);
            float elapsed = 0.0f;
            const float duration = 0.18f;
            Color startColor = boundaryDayPreviewMaterial != null
                ? boundaryDayPreviewMaterial.color : Color.white;
            while (elapsed < duration && boundaryDayPreviewObject != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0.0f, 1.0f,
                    Mathf.Clamp01(elapsed / duration));
                if (boundaryDayPreviewMaterial != null)
                    boundaryDayPreviewMaterial.color = new Color(
                        startColor.r, startColor.g, startColor.b,
                        Mathf.Lerp(startColor.a, 0.0f, t));
                if (boundaryDayPreviewLabel != null)
                    boundaryDayPreviewLabel.color = new Color(
                        Ink.r, Ink.g, Ink.b, 1.0f - t);
                if (boundaryDayPreviewStatsLabel != null)
                    boundaryDayPreviewStatsLabel.color = new Color(
                        Ink.r, Ink.g, Ink.b, 1.0f - t);
                yield return null;
            }
            if (boundaryDayPreviewObject != null)
                boundaryDayPreviewObject.SetActive(false);
            boundaryDayPreviewHideAnimation = null;
        }

        private void UpdateTimeBoundaryHandles()
        {
            int count = selectedDataset != null ? selectedDataset.TimeCount : 30;
            bool fixedTime = roles[0] == DimensionRole.Fixed;
            for (int index = 0; index < timeBoundaryHandles.Length; index++)
            {
                if (timeBoundaryHandles[index] == null)
                    continue;
                int value = fixedTime && index == 0
                    ? selectedTime
                    : index == 0 ? timeBoundaryStart : timeBoundaryEnd;
                bool highlighted = true;
                float x = Mathf.Lerp(-TimeRailHalfWidth, TimeRailHalfWidth,
                    value / (float)Mathf.Max(1, count - 1));
                timeBoundaryHandles[index].transform.localPosition = new Vector3(x, 0.085f, 0.0f);
                timeBoundaryHandles[index].transform.localScale =
                    new Vector3(highlighted ? 0.050f : 0.026f,
                        highlighted ? 0.170f : 0.105f,
                        highlighted ? 0.050f : 0.026f);
                Renderer handleRenderer = timeBoundaryHandles[index].GetComponent<Renderer>();
                if (handleRenderer != null)
                {
                    Color handleColor = index == 0 ? TimeColor : Amber;
                    handleColor.a = highlighted ? 1.0f : 0.28f;
                    handleRenderer.material.color = handleColor;
                }
                if (timeBoundaryValueLabels[index] != null)
                {
                    timeBoundaryValueLabels[index].text = fixedTime && index == 0
                        ? "FIXED TIME  day " + (value + 1)
                        : (index == 0 ? "CUT A" : "CUT B") + "  day " + (value + 1);
                    timeBoundaryValueLabels[index].transform.localPosition =
                        new Vector3(x, 0.225f, 0.0f);
                }
            }
            UpdateAxisBucketGuides();
        }

        private void SetTimeBoundaryHandleVisibility(bool visible)
        {
            bool fixedTime = roles[0] == DimensionRole.Fixed;
            for (int index = 0; index < timeBoundaryHandles.Length; index++)
            {
                bool itemVisible = visible && (!fixedTime || index == 0);
                if (timeBoundaryHandles[index] != null)
                    timeBoundaryHandles[index].SetActive(itemVisible);
                if (timeBoundaryValueLabels[index] != null)
                    timeBoundaryValueLabels[index].gameObject.SetActive(itemVisible);
            }
        }

        private void CreateBoundaryPanel()
        {
            boundaryCanvas = CreateFloatingCanvas(
                "Author Boundary Panel",
                BoundaryToolDockPosition,
                new Vector2(900, 760),
                0.00060f,
                Amber);
            boundaryContent = boundaryCanvas.GetComponent<RectTransform>();
            boundaryCanvas.gameObject.SetActive(false);
        }

        private void ToggleBoundaryPanel()
        {
            if (boundaryCanvas == null)
                return;
            bool next = !boundaryCanvas.gameObject.activeSelf;
            if (next)
            {
                initialBoundarySetupActive = false;
                BeginBoundaryEditSession(boundaryDimension);
            }
            else
            {
                CancelBoundaryEdit();
            }
        }

        private void BeginBoundaryEditSession(BoundaryDimension dimension)
        {
            // A panel may have been grabbed in an earlier step. Every authored
            // boundary session starts from the tested, fully visible dock.
            if (boundaryCanvas != null)
                boundaryCanvas.transform.localPosition = BoundaryToolDockPosition;
            boundaryReturnStage = stage;
            boundaryDimension = dimension;
            savedTimeBoundaryStart = timeBoundaryStart;
            savedTimeBoundaryEnd = timeBoundaryEnd;
            savedSelectedTime = selectedTime;
            savedDepthBoundaryLow = depthBoundaryLow;
            savedDepthBoundaryHigh = depthBoundaryHigh;
            boundaryEditActive = true;
            bool horizontalEditing = dimension == BoundaryDimension.Horizontal;
            if (slabPreviewObject != null)
                slabPreviewObject.SetActive(horizontalEditing);
            if (regionRoot != null)
                regionRoot.SetActive(horizontalEditing);
            FrameVolume();
            if (dimension == BoundaryDimension.Time ||
                dimension == BoundaryDimension.Depth)
                EnterBoundaryAuthoringView();
            UpdateTimeBoundaryHandles();
            UpdateDepthBoundaryPlanes();
            ShowPrimaryTool(boundaryCanvas);
            if (!viewState.cubeVisible)
                ToggleDataVisibility();
            ApplyPrimaryVolumeVisibility();
            BuildBoundaryPanel();
        }

        private void CancelBoundaryEdit()
        {
            bool cancelledInitialSetup = initialBoundarySetupActive;
            EndDepthSliceInspection(true);
            if (boundaryEditActive)
            {
                timeBoundaryStart = savedTimeBoundaryStart;
                timeBoundaryEnd = savedTimeBoundaryEnd;
                depthBoundaryLow = savedDepthBoundaryLow;
                depthBoundaryHigh = savedDepthBoundaryHigh;
                UpdateTimeBoundaryHandles();
                UpdateDepthBoundaryPlanes();
                selectedTime = savedSelectedTime;
                CommitBoundaryTimePreview();
                HideBoundaryDayPreviewSmoothly();
                FrameVolume();
            }
            boundaryEditActive = false;
            initialBoundarySetupActive = false;
            initialTimeBoundaryComplete = false;
            initialDepthBoundaryComplete = false;
            boundaryVariableQueue.Clear();
            boundaryVariableQueueIndex = 0;
            ResetBoundaryInteractionFieldCenter();
            interaction.timeBoundaryDragging = false;
            interaction.depthBoundaryDragging = false;
            if (boundaryCanvas != null)
                boundaryCanvas.gameObject.SetActive(false);
            SetTimeBoundaryHandleVisibility(false);
            SetDepthBoundaryVisibility(false);
            if (slabPreviewObject != null)
                slabPreviewObject.SetActive(false);
            if (regionRoot != null)
                regionRoot.SetActive(false);
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled &&
                forVrSurfacePlayer != null)
                forVrSurfacePlayer.SetSurfaceContextVisible(true);
            if (forVrSurfacePlayer != null)
                forVrSurfacePlayer.CloseCombinedXytTimeSelection();
            ExitBoundaryAuthoringView();
            ReturnToSpatialWorkflow();
            stage = cancelledInitialSetup && !authorBoundaryConfirmed
                ? Stage.Field
                : boundaryReturnStage;
            BuildStage();
            SetStatus(cancelledInitialSetup
                ? "Author Boundary cancelled. Choose a variable to start again."
                : "Boundary edit cancelled. Previous bucket ranges restored.");
        }

        private void BuildBoundaryPanel()
        {
            if (boundaryContent == null)
                return;
            ClearChildren(boundaryContent);
            bool combinedForVrTime = IsForVrSurfaceDataset &&
                boundaryDimension == BoundaryDimension.Time;
            SetTimeBoundaryHandleVisibility(
                boundaryDimension == BoundaryDimension.Time &&
                !combinedForVrTime);
            SetDepthBoundaryVisibility(
                boundaryDimension == BoundaryDimension.Depth);
            UpdateTimeBoundaryHandles();
            UpdateDepthBoundaryPlanes();
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
            {
                BuildDesktopBoundaryBar(combinedForVrTime);
                return;
            }
            CreateText(boundaryContent, "TEST REGIONS", 29, FontStyle.Bold,
                new Vector2(0, 330), new Vector2(820, 44), TextAnchor.MiddleLeft, Ink);
            SpatialAxisRigState activeBoundaryState =
                ActiveVariableBoundaryState();
            bool customBoundary = activeBoundaryState != null &&
                !activeBoundaryState.usesSharedBoundaries;
            CreateText(boundaryContent, BoundaryVariableProgressLabel(),
                14, FontStyle.Bold, new Vector2(-275, 291),
                new Vector2(270, 28), TextAnchor.MiddleLeft, VariableColor);
            CreateButton(boundaryContent, "SHARED", new Vector2(105, 291),
                new Vector2(190, 34), customBoundary ? Card : Cyan,
                () => SetActiveBoundaryScope(false));
            CreateButton(boundaryContent, "CUSTOM", new Vector2(310, 291),
                new Vector2(190, 34), customBoundary ? VariableColor : Card,
                () => SetActiveBoundaryScope(true));

            string[] tabs = initialBoundarySetupActive
                ? IsForVrSurfaceDataset
                    ? new[] { initialTimeBoundaryComplete ? "TIME SAVED" : "TIME" }
                    : new[]
                {
                    initialTimeBoundaryComplete ? "1  TIME SAVED" : "1  TIME",
                    initialDepthBoundaryComplete ? "2  DEPTH SAVED" : "2  DEPTH"
                }
                : new[] { "TIME", "DEPTH", "HORIZONTAL", "VARIABLE" };
            Color[] colors = { TimeColor, DepthColor, HorizontalColor, VariableColor };
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                float tabX = tabs.Length == 1 ? 0 : tabs.Length == 2 ? -210 + i * 420 : -315 + i * 210;
                float tabWidth = tabs.Length == 1 ? 400 : tabs.Length == 2 ? 390 : 192;
                CreateButton(boundaryContent, tabs[i], new Vector2(tabX, 235),
                    new Vector2(tabWidth, 48), (int)boundaryDimension == i ? colors[i] : Card,
                    () => SelectBoundaryDimension(index));
            }

            string instruction;
            string current;
            Color active = colors[(int)boundaryDimension];
            switch (boundaryDimension)
            {
                case BoundaryDimension.Time:
                    if (roles[0] == DimensionRole.Fixed)
                    {
                        instruction = "TIME  /  FIXED";
                        current = "FIXED TIME  " +
                            (selectedDataset != null
                                ? selectedDataset.GetTimeLabel(selectedTime)
                                : "day " + (selectedTime + 1)) +
                            "";
                    }
                    else
                    {
                        instruction = "BEFORE   /   DURING   /   AFTER";
                        // Keep the primary Time readout short enough to fill the
                        // centre card. The selected days should be identifiable
                        // in one glance, even in the desktop Game view.
                        current = TimeRangeSummary().ToUpperInvariant();
                    }
                    break;
                case BoundaryDimension.Depth:
                    if (roles[1] == DimensionRole.Fixed)
                    {
                        instruction = "DEPTH  /  FIXED";
                        current = "FIXED DEPTH  z=" + selectedZ +
                            "";
                    }
                    else
                    {
                        instruction = "SURFACE  ·  MIDDLE  ·  DEEP";
                        current = DepthBoundaryLabel();
                    }
                    break;
                case BoundaryDimension.Horizontal:
                    instruction = "REGION";
                    current = "TRIGGER + DRAG";
                    break;
                default:
                    instruction = "VARIABLE GROUP";
                    current = DatasetNamesSummary();
                    break;
            }

            CreatePanelCard(boundaryContent, new Vector2(0, 105), new Vector2(820, 190), active);
            CreateText(boundaryContent, instruction, 30, FontStyle.Bold,
                new Vector2(0, 148), new Vector2(750, 58),
                TextAnchor.MiddleLeft, Ink);
            boundaryCurrentRangeText = CreateText(boundaryContent, current,
                27, FontStyle.Bold,
                new Vector2(0, 80), new Vector2(750, 58),
                TextAnchor.MiddleLeft, active);

            if (boundaryDimension == BoundaryDimension.Horizontal)
            {
                CreateButton(boundaryContent, interaction.drawingRegion ? "Cancel circle" : "Draw circle on Cube",
                    new Vector2(-205, -38), new Vector2(380, 62),
                    interaction.drawingRegion ? Danger : HorizontalColor, ToggleRegionDrawing);
                CreateButton(boundaryContent, "Reset region", new Vector2(225, -38),
                    new Vector2(360, 62), Card, CenterRegion);
            }
            else if (combinedForVrTime)
            {
                CreateText(boundaryContent,
                    "DRAG CUT A AND CUT B DIRECTLY INSIDE THE SIX-EVENT STC",
                    18, FontStyle.Bold, new Vector2(0, -38),
                    new Vector2(790, 62), TextAnchor.MiddleCenter, TimeColor);
            }
            else
            {
                bool fixedDimension =
                    boundaryDimension == BoundaryDimension.Time
                        ? roles[0] == DimensionRole.Fixed
                        : roles[1] == DimensionRole.Fixed;
                string lower = fixedDimension
                    ? boundaryDimension == BoundaryDimension.Time
                        ? "PREVIOUS DAY" : "SHALLOWER"
                    : boundaryDimension == BoundaryDimension.Time
                        ? "MOVE CUT A  -" : "MOVE LOWER  -";
                string upper = fixedDimension
                    ? boundaryDimension == BoundaryDimension.Time
                        ? "NEXT DAY" : "DEEPER"
                    : boundaryDimension == BoundaryDimension.Time
                        ? "MOVE CUT B  +" : "MOVE UPPER  +";
                CreateButton(boundaryContent, lower, new Vector2(-205, -38),
                    new Vector2(380, 62), Card, () =>
                    {
                        if (fixedDimension)
                            NudgeBoundaryFixedValue(-1);
                        else
                            NudgeActiveBoundary(-1);
                    });
                CreateButton(boundaryContent, upper, new Vector2(225, -38),
                    new Vector2(360, 62), Card, () =>
                    {
                        if (fixedDimension)
                            NudgeBoundaryFixedValue(1);
                        else
                            NudgeActiveBoundary(1);
                    });
            }

            CreateButton(boundaryContent,
                initialBoundarySetupActive ? "BACK TO DATA" : "CANCEL",
                new Vector2(-220, -285),
                new Vector2(330, 64), Card, CancelBoundaryEdit);
            string confirmLabel = initialBoundarySetupActive
                ? boundaryDimension == BoundaryDimension.Time
                    ? IsForVrSurfaceDataset
                        ? "CONFIRM TIME RANGE"
                        : "SAVE TIME  >  DEPTH"
                    : boundaryVariableQueueIndex + 1 < boundaryVariableQueue.Count
                        ? "SAVE FIELD  >  NEXT VARIABLE"
                        : "SAVE ALL  >  OPEN SLAB"
                : "CONFIRM & SAVE";
            CreateButton(boundaryContent, confirmLabel, new Vector2(205, -285),
                new Vector2(430, 64), active, ApplyBoundaryChange);

            // Match the crisp SDF UI used by the original STC project without
            // changing this panel's or any button's authored dimensions.
            UpgradeCanvasLabelsToCrispText(boundaryContent,
                boundaryDimension == BoundaryDimension.Time ? current : null);
        }

        private void BuildDesktopBoundaryBar(bool combinedForVrTime)
        {
            RectTransform rect = boundaryCanvas != null
                ? boundaryCanvas.GetComponent<RectTransform>() : null;
            if (rect != null)
                rect.sizeDelta = new Vector2(1500.0f, 150.0f);
            BoxCollider collider = boundaryCanvas != null
                ? boundaryCanvas.GetComponent<BoxCollider>() : null;
            if (collider != null)
                collider.size = new Vector3(1500.0f, 150.0f, 8.0f);

            Color active = boundaryDimension == BoundaryDimension.Time
                ? TimeColor : boundaryDimension == BoundaryDimension.Depth
                    ? DepthColor : Cyan;
            string current = boundaryDimension == BoundaryDimension.Time
                ? TimeRangeSummary().ToUpperInvariant()
                : boundaryDimension == BoundaryDimension.Depth
                    ? DepthBoundaryLabel() : BoundaryDefaultLabel();
            boundaryCurrentRangeText = CreateText(boundaryContent, current,
                22, FontStyle.Bold, new Vector2(-480, 0),
                new Vector2(500, 72), TextAnchor.MiddleLeft, active);

            float backX = 225.0f;
            if (combinedForVrTime && forVrSurfacePlayer != null)
            {
                CreateButton(boundaryContent,
                    forVrSurfacePlayer.PlaybackButtonLabel,
                    new Vector2(-180, 0), new Vector2(170, 62), Cyan,
                    () =>
                    {
                        forVrSurfacePlayer.TogglePlayback();
                        BuildBoundaryPanel();
                    });
                CreateButton(boundaryContent,
                    forVrSurfacePlayer.PlaybackSpeedLabel,
                    new Vector2(15, 0), new Vector2(190, 62), Card,
                    () =>
                    {
                        forVrSurfacePlayer.CyclePlaybackSpeed();
                        BuildBoundaryPanel();
                    });
            }
            else
            {
                backX = -15.0f;
            }
            CreateButton(boundaryContent,
                initialBoundarySetupActive ? "BACK" : "CANCEL",
                new Vector2(backX, 0), new Vector2(180, 62), Card,
                CancelBoundaryEdit);
            CreateButton(boundaryContent,
                boundaryDimension == BoundaryDimension.Time
                    ? "CONFIRM TIME RANGE" : "CONFIRM",
                new Vector2(backX + 245.0f, 0),
                new Vector2(280, 62), active, ApplyBoundaryChange);
            UpgradeCanvasLabelsToCrispText(boundaryContent, current);
        }

        private SpatialAxisRigState ActiveVariableBoundaryState()
        {
            if (selectedDataset == null)
                return null;
            int variable = datasets.IndexOf(selectedDataset);
            int stateIndex = spatialAxisRigStates.FindIndex(state =>
                state.boundVariable == variable);
            return stateIndex >= 0 ? spatialAxisRigStates[stateIndex] : null;
        }

        private void LoadEffectiveBoundaryValues(SpatialAxisRigState state)
        {
            bool custom = state != null && !state.usesSharedBoundaries;
            timeBoundaryStart = custom
                ? state.customTimeBoundaryStart : sharedTimeBoundaryStart;
            timeBoundaryEnd = custom
                ? state.customTimeBoundaryEnd : sharedTimeBoundaryEnd;
            selectedTime = custom
                ? state.customSelectedTime : sharedSelectedTime;
            depthBoundaryLow = custom
                ? state.customDepthBoundaryLow : sharedDepthBoundaryLow;
            depthBoundaryHigh = custom
                ? state.customDepthBoundaryHigh : sharedDepthBoundaryHigh;
            selectedZ = custom ? state.customSelectedZ : sharedSelectedZ;
            if (selectedDataset != null)
            {
                selectedTime = Mathf.Clamp(selectedTime, 0,
                    Mathf.Max(0, selectedDataset.TimeCount - 1));
                selectedZ = Mathf.Clamp(selectedZ, 0,
                    Mathf.Max(0, selectedDataset.DimZ - 1));
                slabNormalized = selectedDataset.DimZ > 1
                    ? selectedZ / (float)(selectedDataset.DimZ - 1) : 0.5f;
            }
        }

        private void StoreEffectiveBoundaryValues()
        {
            SpatialAxisRigState state = ActiveVariableBoundaryState();
            if (state != null && !state.usesSharedBoundaries)
            {
                state.customTimeBoundaryStart = timeBoundaryStart;
                state.customTimeBoundaryEnd = timeBoundaryEnd;
                state.customSelectedTime = selectedTime;
                state.customDepthBoundaryLow = depthBoundaryLow;
                state.customDepthBoundaryHigh = depthBoundaryHigh;
                state.customSelectedZ = selectedZ;
                return;
            }
            sharedTimeBoundaryStart = timeBoundaryStart;
            sharedTimeBoundaryEnd = timeBoundaryEnd;
            sharedSelectedTime = selectedTime;
            sharedDepthBoundaryLow = depthBoundaryLow;
            sharedDepthBoundaryHigh = depthBoundaryHigh;
            sharedSelectedZ = selectedZ;
            sharedBoundariesInitialized = true;
        }

        private void SetActiveBoundaryScope(bool custom)
        {
            SpatialAxisRigState state = ActiveVariableBoundaryState();
            if (state == null)
                return;
            List<int> boundVariables = BoundVariableIndices();
            for (int index = 0; index < boundVariables.Count; index++)
            {
                int variable = boundVariables[index];
                SpatialAxisRigState target = spatialAxisRigStates.Find(item =>
                    item.boundVariable == variable);
                if (target == null)
                    continue;
                if (custom && target.usesSharedBoundaries)
                {
                    target.customTimeBoundaryStart = sharedTimeBoundaryStart;
                    target.customTimeBoundaryEnd = sharedTimeBoundaryEnd;
                    target.customSelectedTime = sharedSelectedTime;
                    target.customDepthBoundaryLow = sharedDepthBoundaryLow;
                    target.customDepthBoundaryHigh = sharedDepthBoundaryHigh;
                    target.customSelectedZ = sharedSelectedZ;
                }
                target.usesSharedBoundaries = !custom;
            }
            if (initialBoundarySetupActive)
            {
                PrepareBoundaryVariableQueue();
                ActivateBoundaryVariableQueueEntry();
                state = ActiveVariableBoundaryState();
                initialTimeBoundaryComplete = false;
                initialDepthBoundaryComplete = false;
                boundaryDimension = BoundaryDimension.Time;
            }
            LoadEffectiveBoundaryValues(state);
            UpdateTimeBoundaryHandles();
            UpdateDepthBoundaryPlanes();
            if (UsesFacetedVariableFields())
                RefreshVariableFacetStacks();
            else
                ApplyTimeFilter();
            ApplyPrimaryVolumeVisibility();
            UpdateSlabVisual(false);
            UpdateVariableBoundaryScopeButtons();
            BuildBoundaryPanel();
            SetStatus(custom
                ? "Custom ranges enabled. Every variable Field will author its own Time and Depth."
                : "Shared ranges enabled. One Time/Depth selection controls every variable Field.");
        }

        private void OpenInitialAuthorBoundary()
        {
            if (selectedDataset == null || boundaryCanvas == null)
            {
                SetStatus("Choose a variable before defining Author Boundary buckets.");
                return;
            }
            if (IsForVrSurfaceDataset && forVrSurfacePlayer != null)
            {
                OpenForVrCombinedTimeBoundaryEditor(true);
                return;
            }
            if (mainWorkspaceEntered)
            {
                EnsureSavedAuthorBoundaries();
                spatialWorkflowStep = SpatialWorkflowStep.Intent;
                slabPreviewBuilt = true;
                boundaryCanvas.gameObject.SetActive(false);
                SetTimeBoundaryHandleVisibility(false);
                SetDepthBoundaryVisibility(false);
                SetStatus("Time and Depth are already saved. Open MatPlot Intent.");
                BuildWorkflowToolbar();
                return;
            }
            PrepareBoundaryVariableQueue();
            ActivateBoundaryVariableQueueEntry();
            initialBoundarySetupActive = true;
            initialTimeBoundaryComplete = false;
            initialDepthBoundaryComplete = false;
            spatialWorkflowStep = SpatialWorkflowStep.BoundaryAuthoring;
            BeginBoundaryEditSession(BoundaryDimension.Time);
            SetStatus(
                roles[0] == DimensionRole.Fixed
                    ? "Slab step 1 of 2: choose the single fixed Time value."
                    : "Slab step 1 of 2: place two Time cuts for Before, During, and After.");
        }

        private void OpenForVrCombinedTimeBoundaryEditor(bool initialSetup)
        {
            roles[0] = DimensionRole.Faceted;
            roles[1] = DimensionRole.Fixed;
            roles[2] = DimensionRole.Mapped;
            boundaryReturnStage = stage;
            boundaryDimension = BoundaryDimension.Time;
            savedTimeBoundaryStart = timeBoundaryStart;
            savedTimeBoundaryEnd = timeBoundaryEnd;
            savedSelectedTime = selectedTime;
            initialBoundarySetupActive = initialSetup;
            initialTimeBoundaryComplete = false;
            initialDepthBoundaryComplete = false;
            boundaryEditActive = true;
            spatialWorkflowStep = SpatialWorkflowStep.BoundaryAuthoring;
            boundaryVariableQueue.Clear();
            boundaryVariableQueueIndex = 0;
            viewState.legacyPanelVisible = false;
            if (panelCanvas != null)
                panelCanvas.gameObject.SetActive(false);
            if (boundaryCanvas != null)
            {
                boundaryCanvas.transform.localPosition = BoundaryToolDockPosition;
                ShowPrimaryTool(boundaryCanvas);
                BuildBoundaryPanel();
            }
            // The two evidence-bearing planes now live inside the combined
            // STC. Suppress the superseded rail handles behind the orange UI.
            SetTimeBoundaryHandleVisibility(false);
            SetDepthBoundaryVisibility(false);
            forVrSurfacePlayer.OpenCombinedXytTimeSelection(
                timeBoundaryStart, timeBoundaryEnd);
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
            {
                FocusDesktopStcVisualization();
                // Desktop time authoring keeps the geographic Field available
                // as a clickable thumbnail instead of hiding it behind the STC.
                forVrSurfacePlayer.SetSurfaceContextVisible(true);
                SetDesktopFocusView(DesktopFocusView.TimeStc, false);
            }
            RefreshSpatialAxisControllers();
            SetStatus("Use CUT A and CUT B in the STC to define Before, During, and After. The orange panel reports the current ranges.");
        }

        private void PrepareBoundaryVariableQueue()
        {
            boundaryVariableQueue.Clear();
            boundaryVariableQueueIndex = 0;
            List<int> boundVariables = BoundVariableIndices();
            bool customWorkspace = roles[3] == DimensionRole.Faceted &&
                boundVariables.Count > 1;
            for (int index = 0; customWorkspace &&
                index < boundVariables.Count; index++)
            {
                SpatialAxisRigState state = spatialAxisRigStates.Find(item =>
                    item.boundVariable == boundVariables[index]);
                customWorkspace = state != null && !state.usesSharedBoundaries;
            }
            if (customWorkspace)
                boundaryVariableQueue.AddRange(boundVariables);
            else
            {
                int selected = datasets.IndexOf(selectedDataset);
                if (selected >= 0)
                    boundaryVariableQueue.Add(selected);
            }
        }

        private void ActivateBoundaryVariableQueueEntry()
        {
            if (boundaryVariableQueue.Count == 0 ||
                boundaryVariableQueueIndex < 0 ||
                boundaryVariableQueueIndex >= boundaryVariableQueue.Count)
                return;
            int variable = boundaryVariableQueue[boundaryVariableQueueIndex];
            if (variable < 0 || variable >= datasets.Count)
                return;
            selectedDataset = datasets[variable];
            Vector3 fieldCenter = ActiveBoundaryFieldCenter();
            if (timeRail != null)
                timeRail.localPosition = fieldCenter + new Vector3(0.0f,
                    -FieldHalfHeight + 0.095f,
                    FieldHalfDepth * 0.88f);
            SpatialAxisRigState state = spatialAxisRigStates.Find(item =>
                item.boundVariable == variable);
            LoadEffectiveBoundaryValues(state);
            RefreshSlabTexture();
            RebuildTimeMarkers();
            UpdateTimeBoundaryHandles();
            UpdateDepthBoundaryPlanes();
            UpdateSlabVisual(false);
        }

        private Vector3 ActiveBoundaryFieldCenter()
        {
            List<int> boundVariables = BoundVariableIndices();
            if (roles[3] != DimensionRole.Faceted ||
                boundVariables.Count <= 1 || selectedDataset == null)
                return Vector3.zero;
            int selected = datasets.IndexOf(selectedDataset);
            int fieldIndex = boundVariables.IndexOf(selected);
            return PairedFieldCenter(Mathf.Max(0, fieldIndex),
                boundVariables.Count);
        }

        private void ResetBoundaryInteractionFieldCenter()
        {
            if (timeRail != null)
                timeRail.localPosition = new Vector3(0.0f,
                    -FieldHalfHeight + 0.095f,
                    FieldHalfDepth * 0.88f);
        }

        private string BoundaryVariableProgressLabel()
        {
            string variable = selectedDataset != null
                ? selectedDataset.Name.ToUpperInvariant() : "VARIABLE";
            return boundaryVariableQueue.Count > 1
                ? (boundaryVariableQueueIndex + 1) + "/" +
                  boundaryVariableQueue.Count + "  " + variable
                : variable;
        }

        private void OpenBoundaryFromSlab(BoundaryDimension dimension)
        {
            if (boundaryCanvas == null)
                return;
            if (dimension == BoundaryDimension.Time && IsForVrSurfaceDataset &&
                forVrSurfacePlayer != null)
            {
                OpenForVrCombinedTimeBoundaryEditor(false);
                return;
            }
            if (mainWorkspaceEntered &&
                (dimension == BoundaryDimension.Time ||
                 dimension == BoundaryDimension.Depth))
            {
                EnsureSavedAuthorBoundaries();
                SetStatus("Time and Depth are configured once in Field Setup and cannot be re-authored here.");
                return;
            }
            initialBoundarySetupActive = false;
            BeginBoundaryEditSession(dimension);
            SetStatus(dimension == BoundaryDimension.Time
                ? "Place CUT A and CUT B. They divide time into Before, During, and After."
                : "Place LOWER and UPPER. They divide depth into Surface, Middle, and Deep.");
        }

        private void AcceptBoundary()
        {
            inspected = true;
            boundarySuspect = false;
            evidenceLocalized = false;
            int cellIndex = SourceCellIndex(selectedGridColumn, selectedGridRow);
            cellIndex = Mathf.Clamp(cellIndex, 0, facetCellInspected.Length - 1);
            facetCellInspected[cellIndex] = true;
            facetCellBoundarySuspect[cellIndex] = false;
            facetCellLocalized[cellIndex] = false;
            if (currentAnalysisNode != null)
            {
                currentAnalysisNode.inspected = true;
                currentAnalysisNode.boundarySuspect = false;
                EnsureNodeGroundStatus(currentAnalysisNode);
                currentAnalysisNode.verifiedCells[cellIndex] = true;
                currentAnalysisNode.suspectCells[cellIndex] = false;
                currentAnalysisNode.localizedCells[cellIndex] = false;
            }
            FocusDigestPageOnSelection();
            stage = Stage.Result;
            RecordTrailEvent("EVIDENCE", "finding supported by source");
            SetStatus("Conclusion saved: finding supported by the source volume.");
            BuildStage();
        }

        private void MarkBoundarySuspect()
        {
            inspected = false;
            boundarySuspect = true;
            evidenceLocalized = false;
            int cellIndex = SourceCellIndex(selectedGridColumn, selectedGridRow);
            cellIndex = Mathf.Clamp(cellIndex, 0, facetCellInspected.Length - 1);
            facetCellInspected[cellIndex] = false;
            facetCellBoundarySuspect[cellIndex] = true;
            facetCellLocalized[cellIndex] = false;
            if (currentAnalysisNode != null)
            {
                currentAnalysisNode.inspected = false;
                currentAnalysisNode.boundarySuspect = true;
                EnsureNodeGroundStatus(currentAnalysisNode);
                currentAnalysisNode.verifiedCells[cellIndex] = false;
                currentAnalysisNode.suspectCells[cellIndex] = true;
                currentAnalysisNode.localizedCells[cellIndex] = false;
            }
            FocusDigestPageOnSelection();
            stage = Stage.Result;
            RecordTrailEvent("EVIDENCE", "boundary requires review");
            SetStatus("Conclusion saved: review the boundary before relying on this finding.");
            BuildStage();
        }

        private void SelectBoundaryDimension(int index)
        {
            int maximum = initialBoundarySetupActive
                ? (IsForVrSurfaceDataset ? 0 : 1) : 3;
            if (initialBoundarySetupActive && index == 1 &&
                !initialTimeBoundaryComplete)
            {
                SetStatus("Save the two Time cuts before continuing to Depth.");
                return;
            }
            if (boundaryDimension == BoundaryDimension.Depth &&
                index != (int)BoundaryDimension.Depth)
                EndDepthSliceInspection(true);
            if (boundaryDimension == BoundaryDimension.Time &&
                index != (int)BoundaryDimension.Time)
            {
                CommitBoundaryTimePreview();
                HideBoundaryDayPreviewSmoothly();
            }
            boundaryDimension = (BoundaryDimension)Mathf.Clamp(index, 0, maximum);
            if (boundaryDimension == BoundaryDimension.Time ||
                boundaryDimension == BoundaryDimension.Depth)
                EnterBoundaryAuthoringView();
            else
                ExitBoundaryAuthoringView();
            BuildBoundaryPanel();
        }

        private string BoundaryDefaultLabel()
        {
            switch (boundaryDimension)
            {
                case BoundaryDimension.Time: return "during";
                case BoundaryDimension.Depth: return "middle";
                case BoundaryDimension.Horizontal: return "Region 1";
                default: return "ocean variables";
            }
        }

        private void NudgeActiveBoundary(int direction)
        {
            if (selectedDataset == null)
                return;
            if (boundaryDimension == BoundaryDimension.Time)
            {
                if (direction < 0)
                    timeBoundaryStart = Mathf.Clamp(timeBoundaryStart - 1,
                        0, timeBoundaryEnd - 1);
                else
                    timeBoundaryEnd = Mathf.Clamp(timeBoundaryEnd + 1,
                        timeBoundaryStart + 1, selectedDataset.TimeCount - 1);
                UpdateTimeBoundaryHandles();
                PreviewBoundaryTime(direction < 0 ? timeBoundaryStart : timeBoundaryEnd);
            }
            else if (boundaryDimension == BoundaryDimension.Depth)
            {
                if (direction < 0)
                    depthBoundaryLow = Mathf.Clamp(depthBoundaryLow - 0.04f,
                        0.0f, depthBoundaryHigh - 0.05f);
                else
                    depthBoundaryHigh = Mathf.Clamp(depthBoundaryHigh + 0.04f,
                        depthBoundaryLow + 0.05f, 1.0f);
                UpdateDepthBoundaryPlanes();
                BeginDepthSliceInspection(
                    direction < 0 ? depthBoundaryLow : depthBoundaryHigh);
            }
            SetStatus("Boundary preview moved. Apply to update the shared bucket ladder.");
            BuildBoundaryPanel();
        }

        private void NudgeBoundaryFixedValue(int direction)
        {
            if (selectedDataset == null)
                return;
            if (boundaryDimension == BoundaryDimension.Time)
            {
                selectedTime = Mathf.Clamp(selectedTime + direction, 0,
                    Mathf.Max(0, selectedDataset.TimeCount - 1));
                PreviewBoundaryTime(selectedTime);
                SetStatus("Fixed Time preview: " +
                    selectedDataset.GetTimeLabel(selectedTime) + ".");
            }
            else if (boundaryDimension == BoundaryDimension.Depth)
            {
                selectedZ = Mathf.Clamp(selectedZ + direction, 0,
                    Mathf.Max(0, selectedDataset.DimZ - 1));
                slabNormalized = selectedDataset.DimZ > 1
                    ? selectedZ / (float)(selectedDataset.DimZ - 1) : 0.5f;
                UpdateSlabVisual(false);
                UpdateDepthBoundaryPlanes();
                RefreshSlabTexture();
                BeginDepthSliceInspection(slabNormalized);
                SetStatus("Fixed Depth preview: z=" + selectedZ + ".");
            }
            BuildBoundaryPanel();
        }

        private void ApplyBoundaryChange()
        {
            if (boundaryDimension == BoundaryDimension.Time)
            {
                CommitBoundaryTimePreview();
                HideBoundaryDayPreviewSmoothly();
            }
            if (initialBoundarySetupActive &&
                boundaryDimension == BoundaryDimension.Time &&
                !IsForVrSurfaceDataset)
            {
                initialTimeBoundaryComplete = true;
                boundaryDimension = BoundaryDimension.Depth;
                UpdateTimeBoundaryHandles();
                UpdateDepthBoundaryPlanes();
                BuildBoundaryPanel();
                SetStatus(
                    roles[1] == DimensionRole.Fixed
                        ? "Slab step 2 of 2: choose the single fixed Depth value."
                        : "Slab step 2 of 2: place two Depth cuts for Surface, Middle, and Deep.");
                return;
            }

            bool completedInitialSetup = initialBoundarySetupActive;
            EndDepthSliceInspection(true);
            StoreEffectiveBoundaryValues();
            if (initialBoundarySetupActive &&
                boundaryVariableQueueIndex + 1 < boundaryVariableQueue.Count)
            {
                boundaryVariableQueueIndex++;
                ActivateBoundaryVariableQueueEntry();
                RefreshVariableFacetStacks();
                ApplyPrimaryVolumeVisibility();
                initialTimeBoundaryComplete = false;
                initialDepthBoundaryComplete = false;
                boundaryDimension = BoundaryDimension.Time;
                UpdateTimeBoundaryHandles();
                UpdateDepthBoundaryPlanes();
                BuildBoundaryPanel();
                SetStatus("Custom Field " +
                    (boundaryVariableQueueIndex + 1) + "/" +
                    boundaryVariableQueue.Count + ": configure Time for " +
                    selectedDataset.Name + ".");
                return;
            }
            CommitAuthorBoundaryBuckets();
            RefreshVariableFacetStacks();
            ApplyPrimaryVolumeVisibility();
            authorBoundaryConfirmed = true;
            initialTimeBoundaryComplete = true;
            initialDepthBoundaryComplete = true;
            initialBoundarySetupActive = false;
            boundaryVariableQueue.Clear();
            boundaryVariableQueueIndex = 0;
            ResetBoundaryInteractionFieldCenter();
            boundaryEditActive = false;
            // CommitAuthorBoundaryBuckets runs while the editor is still active.
            // Refresh once more after leaving edit mode so the finalized ranges
            // replace the CUT handles immediately on the world axes.
            UpdateAnalysisAxisLabels();
            for (int index = 0; index < analysisNodes.Count; index++)
                UpdateNodeStaleDependencies(analysisNodes[index]);
            Array.Clear(facetCellStale, 0, facetCellStale.Length);
            if (currentAnalysisNode != null &&
                currentAnalysisNode.staleCells != null)
                Array.Copy(currentAnalysisNode.staleCells, facetCellStale,
                    Mathf.Min(facetCellStale.Length,
                        currentAnalysisNode.staleCells.Length));
            gridStale = currentAnalysisNode != null
                ? currentAnalysisNode.stale
                : s4dGridImage != null;
            RecordTrailEvent("BOUNDARY EDIT",
                boundaryDimension == BoundaryDimension.Time
                    ? "time buckets changed"
                    : boundaryDimension == BoundaryDimension.Depth
                        ? "depth buckets changed"
                        : "author buckets changed");
            slabPreviewBuilt = true;
            intentConfigured = false;
            spatialWorkflowStep = SpatialWorkflowStep.Intent;
            ClearSourcePreviewLayers();
            SetStatus(gridStale
                ? "Ranges saved. The previous Grid is stale; enter a new MatPlot intent."
                : "Time and Depth configuration saved. Open MatPlot Intent.");
            boundaryCanvas.gameObject.SetActive(false);
            SetTimeBoundaryHandleVisibility(false);
            SetDepthBoundaryVisibility(false);
            if (slabPreviewCanvas != null)
                slabPreviewCanvas.gameObject.SetActive(false);
            if (intentCanvas != null)
                intentCanvas.gameObject.SetActive(false);
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
            if (forVrSurfacePlayer != null)
                forVrSurfacePlayer.CloseCombinedXytTimeSelection();
            ExitBoundaryAuthoringView();
            stage = completedInitialSetup ? Stage.Slab : boundaryReturnStage;
            if (completedInitialSetup && preconfigurationActive)
            {
                preconfigurationActive = false;
                mainWorkspaceEntered = true;
                viewState.legacyPanelVisible = false;
                spatialWorkflowStep = SpatialWorkflowStep.AxisBinding;
                if (spatialAxisComposerRoot != null)
                    spatialAxisComposerRoot.SetActive(true);
                RefreshSpatialAxisControllers();
                UpdateVariablePaletteFollow(true);
            }
            ReturnToSpatialWorkflow();
            BuildStage();
            BuildWorkflowToolbar();
            BuildMainMenu();
        }

        private void CommitAuthorBoundaryBuckets()
        {
            if (selectedDataset == null)
                return;

            int timeCount = Mathf.Max(3, selectedDataset.TimeCount);
            int timeCutA = Mathf.Clamp(
                timeBoundaryStart + (IsForVrSurfaceDataset ? 0 : 1),
                1, timeCount - 2);
            int timeCutB = Mathf.Clamp(
                timeBoundaryEnd + 1, timeCutA + 1, timeCount - 1);
            authoredTimeBuckets = roles[0] == DimensionRole.Fixed
                ? new[]
                {
                    Bucket("time_fixed_" + selectedTime,
                        selectedDataset.GetTimeLabel(selectedTime),
                        new[] { Mathf.Clamp(selectedTime, 0, timeCount - 1) })
                }
                : new[]
                {
                    Bucket("before", "Before", CreateIndexRange(0, timeCutA)),
                    Bucket("during", "During", CreateIndexRange(timeCutA, timeCutB)),
                    Bucket("after", "After", CreateIndexRange(timeCutB, timeCount))
                };

            if (IsForVrSurfaceDataset)
            {
                authoredDepthBuckets = new[]
                {
                    Bucket("surface", "Hong Kong surface", new[] { 0 })
                };
                activeTimeBuckets = authoredTimeBuckets;
                activeDepthBuckets = authoredDepthBuckets;
                UpdateActiveRepresentativeIndices();
                UpdateAnalysisAxisLabels();
                return;
            }

            int depthCount = Mathf.Max(3, selectedDataset.DimZ);
            int analyzableDepthCount = depthCount == 92 ? 91 : depthCount;
            int depthCutA = Mathf.Clamp(
                Mathf.RoundToInt(depthBoundaryLow * analyzableDepthCount),
                1, analyzableDepthCount - 2);
            int depthCutB = Mathf.Clamp(
                Mathf.RoundToInt(depthBoundaryHigh * analyzableDepthCount),
                depthCutA + 1, analyzableDepthCount - 1);
            authoredDepthBuckets = roles[1] == DimensionRole.Fixed
                ? new[]
                {
                    Bucket("depth_fixed_" + selectedZ, "z=" + selectedZ,
                        new[] { Mathf.Clamp(selectedZ, 0,
                            analyzableDepthCount - 1) })
                }
                : new[]
                {
                    Bucket("surface", "Surface", CreateIndexRange(0, depthCutA)),
                    Bucket("middle", "Middle", CreateIndexRange(depthCutA, depthCutB)),
                    Bucket("deep", "Deep",
                        CreateIndexRange(depthCutB, analyzableDepthCount))
                };

            activeTimeBuckets = authoredTimeBuckets;
            activeDepthBuckets = authoredDepthBuckets;
            UpdateActiveRepresentativeIndices();
            UpdateAnalysisAxisLabels();
        }

        private void HandleDatasetLoadFailure(string error)
        {
            if (currentView != null)
            {
                VolumeSTCubeAPI.DestroyView(currentView.viewId);
                currentView = null;
            }
            selectedDataset = null;
            datasetImportConfirmed = false;
            preconfigurationActive = false;
            mainWorkspaceEntered = false;
            authorBoundaryConfirmed = false;
            stage = Stage.DatasetImport;
            if (spatialRoot != null)
                spatialRoot.SetActive(false);
            if (panelCanvas != null)
                ShowPrimaryTool(panelCanvas);
            string message = "Could not open the dataset. " +
                (string.IsNullOrWhiteSpace(error)
                    ? "Check the RAW and .raw.ini files."
                    : error);
            SetStatus(message);
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
                VolumeSTCubeFlatScreenHUD.ShowNotice(message);
        }

        private string TimeRangeSummary()
        {
            if (authorBoundaryConfirmed && authoredTimeBuckets != null &&
                authoredTimeBuckets.Length == 3)
                return AuthoredBucketSummary(authoredTimeBuckets, true);
            int count = selectedDataset != null ? selectedDataset.TimeCount : 30;
            int firstCut = Mathf.Clamp(timeBoundaryStart, 1, Mathf.Max(1, count - 2));
            int secondCut = Mathf.Clamp(timeBoundaryEnd + 1, firstCut + 1, count - 1);
            return "before 1-" + firstCut + "  /  during " + (firstCut + 1) + "-" + secondCut +
                "  /  after " + (secondCut + 1) + "-" + count;
        }

        private string DepthRangeSummary()
        {
            if (authorBoundaryConfirmed && authoredDepthBuckets != null &&
                authoredDepthBuckets.Length == 3)
                return AuthoredBucketSummary(authoredDepthBuckets, false);
            int count = selectedDataset != null ? selectedDataset.DimZ : 3;
            int firstCut = Mathf.Clamp(Mathf.RoundToInt(depthBoundaryLow * count),
                1, Mathf.Max(1, count - 2));
            int secondCut = Mathf.Clamp(Mathf.RoundToInt(depthBoundaryHigh * count),
                firstCut + 1, count - 1);
            return "surface 0-" + (firstCut - 1) + "  /  middle " + firstCut + "-" +
                (secondCut - 1) + "  /  deep " + secondCut + "-" + (count - 1);
        }

        /// <summary>
        /// Keeps the two time cuts synchronized while the user edits the
        /// combined six-event STC. The cuts form Before, During and After.
        /// </summary>
        public void PreviewForVrCombinedTimeRange(int firstCutIndex,
            int secondCutIndex, int activeCut)
        {
            if (selectedDataset == null || !IsForVrSurfaceDataset)
                return;
            int last = Mathf.Max(1, selectedDataset.TimeCount - 1);
            timeBoundaryStart = Mathf.Clamp(firstCutIndex, 0, last - 1);
            timeBoundaryEnd = Mathf.Clamp(secondCutIndex,
                timeBoundaryStart + 1, last);
            selectedTime = activeCut <= 0 ? timeBoundaryStart : timeBoundaryEnd;
            sharedSelectedTime = selectedTime;
            if (fieldTimeSummaryText != null)
                fieldTimeSummaryText.text = "TIME  " + TimeRangeSummary() +
                    "\nGEOMETRY  HONG KONG WATER SURFACE (NO DEPTH AXIS)";
            if (boundaryCurrentRangeText != null && boundaryCanvas != null &&
                boundaryCanvas.gameObject.activeSelf)
                boundaryCurrentRangeText.text =
                    TimeRangeSummary().ToUpperInvariant();
            SetStatus("STC CUT " + (activeCut <= 0 ? "A" : "B") +
                " preview: " + selectedDataset.GetTimeLabel(selectedTime) +
                ". Current ranges: " + TimeRangeSummary() + ".");
        }

        /// <summary>
        /// Commits the two STC cuts as the existing Set Time Range step.
        /// MatPlot remains later in the established axis/intent/matrix flow.
        /// </summary>
        public void ConfirmForVrCombinedTimeRange(int firstCutIndex,
            int secondCutIndex)
        {
            if (selectedDataset == null || !IsForVrSurfaceDataset)
            {
                SetStatus("Select one of the four For_VR variables before confirming the STC time range.");
                return;
            }

            roles[0] = DimensionRole.Faceted;
            roles[1] = DimensionRole.Fixed;
            roles[2] = DimensionRole.Mapped;
            int last = Mathf.Max(1, selectedDataset.TimeCount - 1);
            timeBoundaryStart = Mathf.Clamp(firstCutIndex, 0, last - 1);
            timeBoundaryEnd = Mathf.Clamp(secondCutIndex,
                timeBoundaryStart + 1, last);
            selectedTime = timeBoundaryStart;
            selectedZ = 0;
            slabNormalized = 0.0f;
            sharedSelectedTime = selectedTime;
            sharedSelectedZ = 0;
            sharedBoundariesInitialized = true;
            StoreEffectiveBoundaryValues();
            SetTime(selectedTime);

            // Rebuild the authoritative time bucket, but do not start MatPlot.
            // The normal downstream workflow owns intent, matrix and findings.
            CommitAuthorBoundaryBuckets();
            authorBoundaryConfirmed = true;
            initialTimeBoundaryComplete = true;
            initialDepthBoundaryComplete = true;
            initialBoundarySetupActive = false;
            slabPreviewBuilt = true;
            intentConfigured = false;
            intentResolutionError = string.Empty;
            ClearSourcePreviewLayers();
            UpdateTimeBoundaryHandles();
            UpdateDepthBoundaryPlanes();
            UpdateAnalysisAxisLabels();
            RecordTrailEvent("XYT TIME RANGE",
                TimeRangeSummary() + " selected from the combined six-event STC");

            // A combined-STC selection is the For_VR Set Time Range step.
            // Return to the normal workspace at its next stage.
            bool enteringMainWorkspace = !mainWorkspaceEntered;
            if (enteringMainWorkspace)
            {
                preconfigurationActive = false;
                mainWorkspaceEntered = true;
                stage = Stage.Slab;
                viewState.legacyPanelVisible = false;
                if (spatialAxisComposerRoot != null)
                    spatialAxisComposerRoot.SetActive(true);
                if (workflowToolbarCanvas != null)
                    workflowToolbarCanvas.gameObject.SetActive(true);
                if (panelCanvas != null)
                    panelCanvas.gameObject.SetActive(false);
                RefreshSpatialAxisControllers();
                UpdateVariablePaletteFollow(true);
            }
            stage = enteringMainWorkspace ? Stage.Slab : boundaryReturnStage;
            boundaryEditActive = false;
            initialBoundarySetupActive = false;
            boundaryVariableQueue.Clear();
            boundaryVariableQueueIndex = 0;
            if (boundaryCanvas != null)
                boundaryCanvas.gameObject.SetActive(false);
            SetTimeBoundaryHandleVisibility(false);
            SetDepthBoundaryVisibility(false);
            ExitBoundaryAuthoringView();
            if (forVrSurfacePlayer != null)
            {
                forVrSurfacePlayer.CloseCombinedXytTimeSelection();
                forVrSurfacePlayer.SetSurfaceContextVisible(true);
            }

            if (AreSpatialAxisBindingsComplete(out string missing))
            {
                spatialWorkflowStep = SpatialWorkflowStep.Intent;
                SetStatus("Three time ranges fixed from the combined STC: " +
                    TimeRangeSummary() +
                    ". Continue the existing workflow; MatPlot remains in the later analysis stage.");
            }
            else
            {
                spatialWorkflowStep = SpatialWorkflowStep.AxisBinding;
                if (workflowToolbarCanvas != null)
                    workflowToolbarCanvas.gameObject.SetActive(true);
                SetStatus("Three time ranges fixed from the combined STC: " +
                    TimeRangeSummary() +
                    ". Continue with axis binding (" + missing + ").");
            }
            BuildWorkflowToolbar();
            BuildStage();
            BuildMainMenu();
        }
    }
}
