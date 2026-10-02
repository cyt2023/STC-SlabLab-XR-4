using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    public sealed partial class VolumeSTCubeQuestSpatialWorkbench
    {
        private void CreateDepthInspectionVisuals()
        {
            depthInspectionUpperStack = CreateDepthInspectionStack(
                "Depth inspection upper remainder", 1.0f);
            depthInspectionLowerStack = CreateDepthInspectionStack(
                "Depth inspection lower remainder", -1.0f);
            depthInspectionLabel = CreateWorldLabel(
                "Z DEPTH SLICE", new Vector3(0.0f, 0.56f, -FieldHalfDepth * 0.98f),
                0.010f, TextAnchor.MiddleCenter, DepthColor);
            depthInspectionUpperStack.SetActive(false);
            depthInspectionLowerStack.SetActive(false);
            depthInspectionLabel.gameObject.SetActive(false);
        }

        private GameObject CreateDepthInspectionStack(string name, float direction)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(spatialRoot.transform, false);
            for (int layer = 0; layer < 4; layer++)
            {
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Context layer " + (layer + 1);
                quad.transform.SetParent(root.transform, false);
                Destroy(quad.GetComponent<Collider>());
                quad.transform.localRotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
                quad.transform.localPosition =
                    new Vector3(0.0f, direction * layer * 0.040f, 0.0f);
                quad.transform.localScale = new Vector3(
                    FieldHalfWidth * 1.74f, FieldHalfDepth * 1.74f, 1.0f);
                Material material = new Material(Shader.Find("Sprites/Default"));
                material.color = new Color(0.70f, 0.80f, 1.0f,
                    Mathf.Lerp(0.34f, 0.10f, layer / 3.0f));
                quad.GetComponent<Renderer>().material = material;
                depthInspectionStackRenderers.Add(quad.GetComponent<Renderer>());
            }
            return root;
        }

        private void CreateGroundEvidenceVisuals()
        {
            groundTimeRangeLine = CreateWorldLine("Ground time bucket range", timeRail,
                Vector3.zero, Vector3.zero, TimeColor, 0.052f);
            groundTimeRangeLine.gameObject.SetActive(false);
            groundTimeRangeLabel = CreateWorldLabel("TIME BUCKET", Vector3.zero, 0.007f,
                TextAnchor.MiddleCenter, TimeColor, timeRail);
            groundTimeRangeLabel.gameObject.SetActive(false);

            groundDepthBand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundDepthBand.name = "Ground depth bucket band";
            groundDepthBand.transform.SetParent(spatialRoot.transform, false);
            Destroy(groundDepthBand.GetComponent<Collider>());
            Material bandMaterial = new Material(Shader.Find("Sprites/Default"));
            // The range volume is only a spatial hint.  Keep it extremely faint so
            // it never turns into an opaque wall in Quest's forward renderer.
            bandMaterial.color = new Color(Purple.r, Purple.g, Purple.b, 0.022f);
            groundDepthBand.GetComponent<Renderer>().material = bandMaterial;

            for (int index = 0; index < groundDepthRangePlanes.Length; index++)
            {
                GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plane.name = index == 0
                    ? "Ground depth bucket lower"
                    : "Ground depth bucket upper";
                plane.transform.SetParent(spatialRoot.transform, false);
                plane.transform.localScale = new Vector3(
                    FieldHalfWidth * 1.84f, 0.010f, FieldHalfDepth * 1.78f);
                Destroy(plane.GetComponent<Collider>());
                Material planeMaterial = new Material(Shader.Find("Sprites/Default"));
                // Ground selection is an immutable MatPlot footprint, not an
                // editable depth boundary.  Both cuts therefore use the same
                // purple evidence colour; cyan remains reserved for authoring.
                Color boundaryColor = Purple;
                planeMaterial.color = new Color(
                    boundaryColor.r, boundaryColor.g, boundaryColor.b,
                    0.075f);
                // Evidence cuts must stay light and translucent on Quest.
                planeMaterial.SetInt("_ZWrite", 0);
                planeMaterial.renderQueue =
                    (int)UnityEngine.Rendering.RenderQueue.Transparent + 8;
                Renderer planeRenderer = plane.GetComponent<Renderer>();
                planeRenderer.material = planeMaterial;
                planeRenderer.sortingOrder = -20;
                groundDepthRangePlanes[index] = plane;
            }
            groundDepthRangeLabel = CreateWorldLabel("DEPTH BUCKET", Vector3.zero, 0.0065f,
                TextAnchor.MiddleLeft, Purple);
            SetGroundEvidenceVisuals(false);
        }

        private void UpdateDepthSliceInspection(float normalized)
        {
            if (selectedDataset == null)
                return;
            normalized = Mathf.Clamp01(normalized);
            int nextZ = Mathf.Clamp(
                Mathf.RoundToInt(normalized * (selectedDataset.DimZ - 1)),
                0, selectedDataset.DimZ - 1);
            slabNormalized = normalized;
            selectedZ = nextZ;
            Vector3 fieldCenter = ActiveBoundaryFieldCenter();
            if (depthInspectionZ != nextZ || slabTexture == null)
            {
                depthInspectionZ = nextZ;
                RefreshSlabTexture();
                for (int index = 0;
                    index < depthInspectionStackRenderers.Count; index++)
                {
                    Material material =
                        depthInspectionStackRenderers[index].material;
                    material.mainTexture = slabTexture;
                    material.mainTextureScale = Vector2.one;
                    material.mainTextureOffset = Vector2.zero;
                }
            }
            if (depthInspectionLabel != null)
            {
                depthInspectionLabel.text =
                    "Z  DEPTH SLICE  |  z=" + nextZ +
                    "\n90° INSPECTION VIEW";
                float side = DepthInspectionSide(fieldCenter);
                depthInspectionLabel.transform.localPosition = new Vector3(
                    fieldCenter.x + side * (FieldHalfWidth + 0.31f),
                    fieldCenter.y +
                        (activeDepthBoundary == 1 ? 0.72f : -0.27f),
                    fieldCenter.z - FieldHalfDepth * 0.78f);
            }
            if (interaction.depthInspectionActive && depthInspectionCoroutine == null &&
                slabPreviewObject != null)
            {
                // Once the slice has opened beside the cube, keep its vertical
                // placement tied to the ray-controlled cut instead of leaving it
                // behind at the position where dragging began.
                float side = DepthInspectionSide(fieldCenter);
                slabPreviewObject.transform.localPosition = new Vector3(
                    fieldCenter.x + side * (FieldHalfWidth + 0.31f),
                    fieldCenter.y + Mathf.Lerp(-0.48f, 0.48f, normalized),
                    fieldCenter.z - FieldHalfDepth * 0.78f);
            }
            UpdateAnalysisAxisLabels();
        }

        private void UpdateGroundEvidenceLink()
        {
            bool gridPreview = !groundDocked && gridCellSelected &&
                facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf;
            if ((!groundDocked && !gridPreview) || groundLink == null)
                return;
            Vector3 source = groundDepthBand != null
                ? groundDepthBand.transform.position
                : slabObject != null
                    ? slabObject.transform.position
                    : spatialRoot.transform.position;
            if (gridPreview && TryGetGroundBucketRanges(
                out int timeFirst, out int timeLast,
                out int ignoredDepthFirst, out int ignoredDepthLast))
            {
                VolumeSTCubeForVrXytCompanion companion =
                    FindObjectOfType<VolumeSTCubeForVrXytCompanion>();
                Vector3 stcSource;
                if (companion != null && companion.ShowMatPlotSourceRange(
                    timeFirst, timeLast,
                    selectedDataset != null ? selectedDataset.Name : string.Empty,
                    out stcSource))
                    source = stcSource;
            }
            Vector3 target = selectedFacetCellAnchor != null
                ? selectedFacetCellAnchor.TransformPoint(Vector3.zero)
                : facetGridCanvas != null
                    ? facetGridCanvas.transform.position
                    : panelCanvas.transform.position;
            if (gridPreview)
            {
                groundLink.gameObject.SetActive(false);
                SetMatPlotStcDashedLink(source, target);
            }
            else
            {
                SetMatPlotStcDashedLinkVisible(false);
                groundLink.gameObject.SetActive(true);
                groundLink.SetPosition(0, source);
                groundLink.SetPosition(1, target);
            }
        }

        private void OnGroundAggregateVolumeComplete(S4DGroundVolumeResult result)
        {
            SetPending(PendingJob.GroundAggregate, false);
            if (result == null || !result.Succeeded)
            {
                SetStatus(result != null
                    ? result.Error
                    : "Ground aggregate service returned no volume.");
                BuildStage();
                return;
            }
            groundSnapshotCellMean = result.SnapshotCellMean;
            groundReconstructedCellMean = result.ReconstructedCellMean;
            groundValidFraction = result.ValidFraction;
            float replacement = result.Minimum -
                Mathf.Max(0.0001f, result.Maximum - result.Minimum);
            float[] values = result.Values;
            for (int index = 0; index < values.Length; index++)
                if (float.IsNaN(values[index]) || float.IsInfinity(values[index]))
                    values[index] = replacement;
            groundAggregateDataset = ScriptableObject.CreateInstance<VolumeDataset>();
            groundAggregateDataset.datasetName =
                "Ground " + CellLabel(selectedGridColumn, selectedGridRow);
            groundAggregateDataset.filePath =
                "snapshot://" + groundAggregateSnapshotId;
            groundAggregateDataset.dimX = result.DimX;
            groundAggregateDataset.dimY = result.DimY;
            groundAggregateDataset.dimZ = result.DimZ;
            groundAggregateDataset.data = values;
            groundAggregateDataset.normalizationMinimum = result.Minimum;
            groundAggregateDataset.normalizationMaximum = result.Maximum;
            groundAggregateDataset.scale = Vector3.one;
            groundAggregateVolume = VolumeSTCubeRawVolumeFactory.CreateObject(
                groundAggregateDataset, groundAggregateDataset.filePath);
            if (groundAggregateVolume == null)
            {
                Destroy(groundAggregateDataset);
                groundAggregateDataset = null;
                SetStatus("Ground aggregate was computed but Unity could not create its volume.");
                return;
            }
            // The raw factory starts with its generic white transfer function.  In
            // Ground this used to flash as an opaque white slab and then compete
            // with the contextual field.  Keep the object hidden until it has the
            // exact same colour language as the source variable.
            groundAggregateVolume.gameObject.SetActive(false);
            VolumeControllerObject sourceController = currentView != null
                ? currentView.GetManagedController()
                : null;
            if (sourceController != null && sourceController.transferFunction != null)
            {
                groundAggregateVolume.SetTransferFunctionMode(TFRenderMode.TF1D);
                groundAggregateVolume.SetTransferFunction(sourceController.transferFunction);
            }
            groundAggregateVolume.SetLightingEnabled(false);
            groundAggregateVolume.name = "Snapshot Ground Aggregate Volume";
            groundAggregateVolume.transform.SetParent(spatialRoot.transform, false);
            int depthFirst;
            int depthLast;
            int ignoredTimeFirst;
            int ignoredTimeLast;
            TryGetGroundBucketRanges(
                out ignoredTimeFirst, out ignoredTimeLast, out depthFirst, out depthLast);
            float denominator = Mathf.Max(1, selectedDataset.DimZ - 1);
            float y0 = Mathf.Lerp(volumeLocalMinY, volumeLocalMaxY,
                depthFirst / (float)denominator);
            float y1 = Mathf.Lerp(volumeLocalMinY, volumeLocalMaxY,
                depthLast / (float)denominator);
            float lower = Mathf.Min(y0, y1);
            float upper = Mathf.Max(y0, y1);
            groundAggregateVolume.transform.localPosition =
                new Vector3(0.0f, (lower + upper) * 0.5f, 0.0f);
            groundAggregateVolume.transform.localRotation =
                Quaternion.Euler(90.0f, 0.0f, 0.0f);
            groundAggregateVolume.transform.localScale = new Vector3(
                FieldHalfWidth * 1.60f,
                FieldHalfDepth * 1.60f,
                Mathf.Max(0.08f, upper - lower));
            SetGroundAggregateVisible(
                groundDocked && groundMode == GroundMode.Aggregate);
            if (currentView != null)
            {
                currentView.SetVisible(viewState.cubeVisible);
                currentView.ApplyOpacity(GroundContextOpacity);
            }
            SetStatus("Ground verified: snapshot mean " +
                groundSnapshotCellMean.ToString("0.###") +
                ", reconstructed mean " +
                groundReconstructedCellMean.ToString("0.###") +
                ", difference " +
                Mathf.Abs(groundSnapshotCellMean -
                    groundReconstructedCellMean).ToString("0.###") +
                ", valid coverage " +
                (groundValidFraction * 100.0f).ToString("0.0") + "%.");
            BuildStage();
        }

        private string GroundSelectionHeadline()
        {
            return CellLabel(selectedGridColumn, selectedGridRow) + "  |  " +
                SelectedTimeRangeLabel() + "  |  " + SelectedDepthRangeLabel();
        }

        private void UpdateSlabInteraction()
        {
            if (rayInteractor == null || selectedDataset == null)
                return;
            if (interaction.draggingSlab)
            {
                if (rayInteractor.TriggerHeld)
                {
#if UNITY_EDITOR || SLABLAB_FLAT
                    if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
                        slabNormalized = Mathf.Clamp01(desktopDragStartSlab +
                            (FlatPointerPosition.y - desktopDragStartMouseY) / Mathf.Max(360.0f, Screen.height * 0.72f));
                    else
#endif
                    slabNormalized = Mathf.Clamp01(GetHandDepthNormalized() + slabDragOffset);
                    UpdateSlabVisual(false);
                }
                if (rayInteractor.TriggerReleased)
                {
                    interaction.draggingSlab = false;
                    RefreshSlabTexture();
                    SetStatus("Slab grounded at z=" + selectedZ + ". Materialize or draw a region.");
                    BuildStage();
                }
            }
            if (interaction.regionDragging)
            {
                if (rayInteractor.TriggerHeld && TryGetSlabPoint(rayInteractor.PointerRay, out Vector2 point))
                {
                    float radius = Mathf.Clamp(Vector2.Distance(regionStart, point), 0.002f, 0.48f);
                    region = new Rect(
                        regionStart.x - radius,
                        regionStart.y - radius,
                        radius * 2.0f,
                        radius * 2.0f);
                    UpdateRegionVisual();
                }
                if (rayInteractor.TriggerReleased)
                {
                    interaction.regionDragging = false;
                    interaction.drawingRegion = false;
                    if (region.width < 0.04f)
                        region = new Rect(0.28f, 0.28f, 0.44f, 0.44f);
                    UpdateRegionVisual();
                    SetStatus("Region grounded. It will be compared with the rest of the slab.");
                    BuildStage();
                }
            }
        }

        private void UpdateSlabVisual(bool updateLabel)
        {
            if (slabObject == null)
                return;
            float y = Mathf.Lerp(volumeLocalMinY, volumeLocalMaxY, slabNormalized);
            slabObject.transform.localPosition = new Vector3(0.0f, y, 0.0f);
            if (!interaction.depthInspectionActive)
            {
                slabPreviewObject.transform.localPosition =
                    new Vector3(0.0f, y + 0.013f, 0.0f);
                regionRoot.transform.localPosition =
                    new Vector3(0.0f, y + 0.024f, 0.0f);
            }
            if (selectedDataset != null)
                selectedZ = Mathf.Clamp(Mathf.RoundToInt(slabNormalized * (selectedDataset.DimZ - 1)), 0, selectedDataset.DimZ - 1);
            if (updateLabel && slabLabel != null && selectedDataset != null)
                slabLabel.text = selectedDataset.Name + "  |  " + selectedDataset.GetTimeLabel(selectedTime) + "  |  z=" + selectedZ;
            UpdateAnalysisAxisLabels();
        }

        /// <summary>Raises the Fields once their first texture is ready.</summary>
        private void WatchFieldPresentation()
        {
            if (!pendingFieldPresentationReveal)
                return;
            if (Time.realtimeSinceStartup < pendingFieldRevealDeadline)
                return;
            RevealFieldPresentation();
        }

        public void ResetVolumeLayout()
        {
            FrameVolume();
        }

        private void EnforceSlabPreviewVisibility()
        {
            if (slabPreviewObject == null)
                return;
            bool horizontalAuthoring = boundaryEditActive &&
                boundaryDimension == BoundaryDimension.Horizontal;
            bool depthLayerInspection = boundaryEditActive &&
                boundaryDimension == BoundaryDimension.Depth &&
                interaction.depthInspectionActive;
            // The permanent XY slab remains hidden, but while the user holds a
            // Depth cut the exact RAW z layer is an active inspection object and
            // must stay visible beside its Field.
            bool shouldShow = horizontalAuthoring || depthLayerInspection;
            if (boundaryEditActive &&
                boundaryDimension != BoundaryDimension.Horizontal &&
                !depthLayerInspection)
                shouldShow = false;
            if (slabPreviewObject.activeSelf != shouldShow)
                slabPreviewObject.SetActive(shouldShow);
            Renderer previewRenderer = slabPreviewObject.GetComponent<Renderer>();
            if (previewRenderer != null)
                previewRenderer.enabled = shouldShow;
            if (!shouldShow)
                HideAllAuxiliarySliceRenderers();
        }

        private void HideAllAuxiliarySliceRenderers()
        {
            if (spatialRoot == null || slabTexture == null)
                return;
            Transform volumeRoot = currentView != null &&
                currentView.rootObject != null
                    ? currentView.rootObject.transform : null;
            Renderer[] renderers = spatialRoot.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null ||
                    (volumeRoot != null && renderer.transform.IsChildOf(volumeRoot)) ||
                    (boundaryDayPreviewObject != null &&
                     renderer.transform.IsChildOf(boundaryDayPreviewObject.transform)))
                    continue;
                Material material = renderer.sharedMaterial;
                // Accessing Material.mainTexture on Unlit/Color emits a warning
                // every frame because that shader has no _MainTex property.
                // Besides filling Editor.log, the repeated logging causes an
                // avoidable hitch while authoring boundaries in VR.
                if (material != null && material.HasProperty("_MainTex") &&
                    material.mainTexture == slabTexture)
                    renderer.enabled = false;
            }
        }

        // A complete field plus its side-mounted axis body forms one spatial
        // work unit. Additional variables occupy a shallow arc around the user
        // instead of forming a distant straight row.
        private static Vector3 PairedFieldCenter(int rigIndex, int count)
        {
            if (count <= 1 || rigIndex <= 0)
                return Vector3.zero;
            if (rigIndex == 1)
                // Variable 2: above the shared tri-axis controller.
                return new Vector3(SpatialAxisDockX,
                    SpatialFieldOrbitY, 0.0f);
            if (rigIndex == 2)
                // Variable 3 mirrors the primary Field across the controller.
                return new Vector3(SpatialAxisDockX * 2.0f,
                    0.0f, 0.0f);
            // Variable 4 completes the upper-right position without moving the
            // established first three Fields.
            return new Vector3(SpatialAxisDockX * 2.0f,
                SpatialFieldOrbitY, 0.0f);
        }

        private IEnumerator AnimateLocalMove(Transform target, Vector3 destination)
        {
            if (target == null)
                yield break;
            Vector3 start = target.localPosition;
            float elapsed = 0.0f;
            const float duration = 0.24f;
            while (target != null && elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0.0f, 1.0f,
                    Mathf.Clamp01(elapsed / duration));
                target.localPosition = Vector3.Lerp(start, destination, t);
                yield return null;
            }
            if (target != null)
                target.localPosition = destination;
        }

        private IEnumerator AnimateLocalScale(Transform target,
            Vector3 destination)
        {
            if (target == null)
                yield break;
            Vector3 start = target.localScale;
            float elapsed = 0.0f;
            const float duration = 0.22f;
            while (target != null && elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                float eased = 1.0f - Mathf.Pow(1.0f - normalized, 3.0f);
                float settle = Mathf.Sin(normalized * Mathf.PI) *
                    (1.0f - normalized) * 0.08f;
                target.localScale = Vector3.LerpUnclamped(start,
                    destination, eased + settle);
                yield return null;
            }
            if (target != null)
                target.localScale = destination;
        }

        private IEnumerator AnimateWorldMove(Transform target,
            Vector3 destination)
        {
            if (target == null)
                yield break;
            Vector3 start = target.position;
            float elapsed = 0.0f;
            const float duration = 0.30f;
            while (target != null && elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                // Ease-out with a very small overshoot gives the side dock a
                // magnetic catch rather than a mechanical linear slide.
                float eased = 1.0f - Mathf.Pow(1.0f - normalized, 3.0f);
                float overshoot = Mathf.Sin(normalized * Mathf.PI) *
                    (1.0f - normalized) * 0.08f;
                target.position = Vector3.LerpUnclamped(start, destination,
                    eased + overshoot);
                yield return null;
            }
            if (target != null)
                target.position = destination;
        }

        private void BeginDepthSliceInspection(float normalized)
        {
            if (selectedDataset == null || slabPreviewObject == null)
                return;
            normalized = Mathf.Clamp01(normalized);
            if (!interaction.depthInspectionActive)
            {
                depthInspectionOriginalZ = selectedZ;
                depthInspectionOriginalNormalized = slabNormalized;
            }
            UpdateDepthSliceInspection(normalized);
            if (interaction.depthInspectionActive)
            {
                if (depthInspectionCoroutine != null)
                    StopCoroutine(depthInspectionCoroutine);
                float reopeningY = Mathf.Lerp(
                    volumeLocalMinY, volumeLocalMaxY, normalized);
                depthInspectionCoroutine = StartCoroutine(
                    AnimateDepthInspection(true, reopeningY));
                return;
            }

            interaction.depthInspectionActive = true;
            slabPreviewObject.SetActive(true);
            Renderer inspectionRenderer = slabPreviewObject.GetComponent<Renderer>();
            if (inspectionRenderer != null)
                inspectionRenderer.enabled = true;

            float selectedY = Mathf.Lerp(
                volumeLocalMinY, volumeLocalMaxY, normalized);
            Vector3 fieldCenter = ActiveBoundaryFieldCenter();
            // Keep the 3D field unobstructed. Only the exact selected z layer
            // travels outward; it updates continuously while the cut is held.
            depthInspectionUpperStack.SetActive(false);
            depthInspectionLowerStack.SetActive(false);
            depthInspectionLabel.gameObject.SetActive(true);
            slabPreviewObject.transform.localPosition =
                fieldCenter + new Vector3(0.0f, selectedY, 0.0f);
            slabPreviewObject.transform.localRotation =
                Quaternion.Euler(90.0f, 0.0f, 0.0f);
            slabPreviewObject.transform.localScale =
                new Vector3(FieldHalfWidth * 1.64f, FieldHalfDepth * 1.64f, 1.0f);

            if (depthInspectionCoroutine != null)
                StopCoroutine(depthInspectionCoroutine);
            depthInspectionCoroutine = StartCoroutine(
                AnimateDepthInspection(true, selectedY));
        }

        // Open the live layer into the nearest free lane. In particular the
        // right-most faceted Field opens inward, away from the Variables panel.
        private float DepthInspectionSide(Vector3 fieldCenter)
        {
            return fieldCenter.x > SpatialAxisDockX + 0.20f ? -1.0f : 1.0f;
        }

        private void EndDepthSliceInspection(bool immediate)
        {
            if (!interaction.depthInspectionActive)
                return;
            if (depthInspectionCoroutine != null)
                StopCoroutine(depthInspectionCoroutine);
            // Close back into the cut that produced the inspection view.
            // The original browsing depth is restored only after the visual
            // pieces have reached this exact cut plane.
            float selectedY = Mathf.Lerp(
                volumeLocalMinY, volumeLocalMaxY, slabNormalized);
            if (immediate)
            {
                FinishDepthInspectionRestore(selectedY);
                return;
            }
            depthInspectionCoroutine = StartCoroutine(
                AnimateDepthInspection(false, selectedY));
        }

        private System.Collections.IEnumerator AnimateDepthInspection(
            bool opening, float selectedY)
        {
            Vector3 previewStartPosition =
                slabPreviewObject.transform.localPosition;
            Quaternion previewStartRotation =
                slabPreviewObject.transform.localRotation;
            Vector3 previewStartScale =
                slabPreviewObject.transform.localScale;
            Vector3 previewTargetPosition = opening
                ? ActiveBoundaryFieldCenter() + new Vector3(
                    DepthInspectionSide(ActiveBoundaryFieldCenter()) *
                        (FieldHalfWidth + 0.31f),
                    activeDepthBoundary == 1 ? 0.50f : -0.50f,
                    -FieldHalfDepth * 0.78f)
                : ActiveBoundaryFieldCenter() +
                    new Vector3(0.0f, selectedY + 0.013f, 0.0f);
            Quaternion previewTargetRotation = opening
                ? Quaternion.identity
                : Quaternion.Euler(90.0f, 0.0f, 0.0f);
            Vector3 previewTargetScale = opening
                ? new Vector3(
                    0.46f, 0.34f, 1.0f)
                : new Vector3(
                    FieldHalfWidth * 1.64f, FieldHalfDepth * 1.64f, 1.0f);

            float elapsed = 0.0f;
            const float duration = 0.34f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(
                    0.0f, 1.0f, Mathf.Clamp01(elapsed / duration));
                slabPreviewObject.transform.localPosition =
                    Vector3.Lerp(previewStartPosition, previewTargetPosition, t);
                slabPreviewObject.transform.localRotation =
                    Quaternion.Slerp(previewStartRotation, previewTargetRotation, t);
                slabPreviewObject.transform.localScale =
                    Vector3.Lerp(previewStartScale, previewTargetScale, t);
                yield return null;
            }

            if (!opening)
                FinishDepthInspectionRestore(selectedY);
            depthInspectionCoroutine = null;
        }

        private void FinishDepthInspectionRestore(float selectedY)
        {
            interaction.depthInspectionActive = false;
            depthInspectionCoroutine = null;
            depthInspectionUpperStack.SetActive(false);
            depthInspectionLowerStack.SetActive(false);
            depthInspectionLabel.gameObject.SetActive(false);
            slabPreviewObject.SetActive(false);
            Vector3 fieldCenter = ActiveBoundaryFieldCenter();
            slabPreviewObject.transform.localPosition =
                fieldCenter + new Vector3(0.0f, selectedY + 0.013f, 0.0f);
            slabPreviewObject.transform.localRotation =
                Quaternion.Euler(90.0f, 0.0f, 0.0f);
            slabPreviewObject.transform.localScale =
                new Vector3(FieldHalfWidth * 1.64f, FieldHalfDepth * 1.64f, 1.0f);
            if (slabObject != null)
                slabObject.SetActive(!UsesFacetedVariableFields());
            if (regionRoot != null)
                regionRoot.SetActive(false);
            ApplyPrimaryVolumeVisibility();
            slabNormalized = depthInspectionOriginalNormalized;
            selectedZ = depthInspectionOriginalZ;
            depthInspectionZ = -1;
            RefreshSlabTexture();
            UpdateSlabVisual(true);
        }

        private void SetGroundEvidenceVisuals(bool visible)
        {
            if (!visible)
            {
                SetGroundAggregateVisible(false);
                if (currentView != null)
                {
                    currentView.SetVisible(viewState.cubeVisible);
                    currentView.ApplyOpacity(FieldOpacity);
                }
            }
            int timeFirst = 0;
            int timeLast = 0;
            int depthFirst = 0;
            int depthLast = 0;
            bool show = false;
            if (visible && selectedDataset != null && gridCellSelected)
                show = TryGetGroundBucketRanges(
                    out timeFirst, out timeLast, out depthFirst, out depthLast);
            if (groundTimeRangeLine != null)
                groundTimeRangeLine.gameObject.SetActive(show);
            if (groundTimeRangeLabel != null)
                groundTimeRangeLabel.gameObject.SetActive(show);
            // The immutable footprint is represented by two light boundary
            // slices. Never show the filled range volume: Quest renders it as
            // an opaque wall that hides the actual STC field.
            bool showFilledDepthEvidence = false;
            if (groundDepthBand != null)
                groundDepthBand.SetActive(showFilledDepthEvidence);
            for (int index = 0; index < groundDepthRangePlanes.Length; index++)
                if (groundDepthRangePlanes[index] != null)
                    groundDepthRangePlanes[index].SetActive(show);
            if (groundDepthRangeLabel != null)
                groundDepthRangeLabel.gameObject.SetActive(show);
            bool boundaryEditorVisible = boundaryCanvas != null &&
                boundaryCanvas.gameObject.activeSelf;
            // Boundary handles belong exclusively to Author Boundary.  A
            // stale result may mention that its source changed, but selecting
            // Highest/Lowest must never open those editable cyan/white planes.
            if (boundaryEditorVisible)
            {
                UpdateTimeBoundaryHandles();
                UpdateDepthBoundaryPlanes();
                SetTimeBoundaryHandleVisibility(true);
                SetDepthBoundaryVisibility(true);
            }
            else
            {
                SetTimeBoundaryHandleVisibility(false);
                SetDepthBoundaryVisibility(false);
            }
            if (!show)
                return;

            float timeDenominator = Mathf.Max(1, selectedDataset.TimeCount - 1);
            float timeX0 = Mathf.Lerp(-TimeRailHalfWidth, TimeRailHalfWidth,
                timeFirst / timeDenominator);
            float timeX1 = Mathf.Lerp(-TimeRailHalfWidth, TimeRailHalfWidth,
                timeLast / timeDenominator);
            SetLine(groundTimeRangeLine,
                new Vector3(timeX0, 0.018f, -0.006f),
                new Vector3(timeX1, 0.018f, -0.006f));
            groundTimeRangeLabel.text =
                "SELECTED  " + selectedDataset.GetTimeLabel(timeFirst) + "–" +
                selectedDataset.GetTimeLabel(timeLast);
            // Always replace any legacy encoded separator with readable ASCII.
            groundTimeRangeLabel.text =
                "SELECTED  " + selectedDataset.GetTimeLabel(timeFirst) + " - " +
                selectedDataset.GetTimeLabel(timeLast);
            groundTimeRangeLabel.transform.localPosition =
                new Vector3((timeX0 + timeX1) * 0.5f, 0.165f, 0.0f);

            float depthDenominator = Mathf.Max(1, selectedDataset.DimZ - 1);
            float depthY0 = Mathf.Lerp(volumeLocalMinY, volumeLocalMaxY,
                depthFirst / depthDenominator);
            float depthY1 = Mathf.Lerp(volumeLocalMinY, volumeLocalMaxY,
                depthLast / depthDenominator);
            float lowerY = Mathf.Min(depthY0, depthY1);
            float upperY = Mathf.Max(depthY0, depthY1);
            if (groundDepthBand != null)
            {
                groundDepthBand.transform.localPosition =
                    new Vector3(0.0f, (lowerY + upperY) * 0.5f, 0.0f);
                groundDepthBand.transform.localScale = new Vector3(
                    FieldHalfWidth * 1.94f, Mathf.Max(0.025f, upperY - lowerY),
                    FieldHalfDepth * 1.94f);
            }
            if (groundDepthRangePlanes[0] != null)
                groundDepthRangePlanes[0].transform.localPosition =
                    new Vector3(0.0f, lowerY, 0.0f);
            if (groundDepthRangePlanes[1] != null)
                groundDepthRangePlanes[1].transform.localPosition =
                    new Vector3(0.0f, upperY, 0.0f);
            groundDepthRangeLabel.text =
                "DEPTH  z=" + depthFirst + " - " + depthLast;
            // Keep the annotation inside the cube and just above the upper cut.
            // This avoids colliding with the analysis panel or floating outside
            // the field when the user views it from an oblique Quest angle.
            groundDepthRangeLabel.anchor = TextAnchor.MiddleRight;
            groundDepthRangeLabel.alignment = TextAlignment.Right;
            groundDepthRangeLabel.transform.localPosition =
                new Vector3(FieldHalfWidth - 0.045f,
                    Mathf.Min(volumeLocalMaxY - 0.035f, upperY + 0.065f),
                    -FieldHalfDepth * 0.91f);
            RebuildTimeMarkers();
        }

        private bool TryGetGroundBucketRanges(
            out int timeFirst, out int timeLast, out int depthFirst, out int depthLast)
        {
            timeFirst = timeLast = selectedTime;
            depthFirst = depthLast = selectedZ;
            int timeIndex = DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow);
            int depthIndex = DisplayDepthBucketIndex(selectedGridColumn, selectedGridRow);
            if (activeTimeBuckets == null || timeIndex < 0 ||
                timeIndex >= activeTimeBuckets.Length ||
                activeDepthBuckets == null || depthIndex < 0 ||
                depthIndex >= activeDepthBuckets.Length)
                return false;
            if (!TryGetIndexRange(activeTimeBuckets[timeIndex], out timeFirst, out timeLast) ||
                !TryGetIndexRange(activeDepthBuckets[depthIndex], out depthFirst, out depthLast))
                return false;
            if (selectedDataset == null)
                return true;
            timeFirst = Mathf.Clamp(timeFirst, 0, selectedDataset.TimeCount - 1);
            timeLast = Mathf.Clamp(timeLast, timeFirst, selectedDataset.TimeCount - 1);
            depthFirst = Mathf.Clamp(depthFirst, 0, selectedDataset.DimZ - 1);
            depthLast = Mathf.Clamp(depthLast, depthFirst, selectedDataset.DimZ - 1);
            return true;
        }

        private void SetGroundMode(GroundMode mode)
        {
            groundMode = mode;
            if (mode == GroundMode.Playback)
            {
                StopGroundPlayback();
                SetGroundAggregateVisible(false);
                if (currentView != null)
                {
                    currentView.SetVisible(viewState.cubeVisible);
                    currentView.ApplyOpacity(FieldOpacity);
                }
                groundPlaybackCoroutine = StartCoroutine(PlayGroundTimeBucket());
            }
            else
            {
                StopGroundPlayback();
                RestoreGroundRepresentative();
                ApplyGroundAggregateVisual();
            }
            SetStatus(mode == GroundMode.Aggregate
                ? "Aggregate selected: the MatPlot cell is grounded on its complete T x Z footprint."
                : "Playback running: only source frames in the selected time bucket are shown.");
            RecordTrailEvent("GROUND",
                mode == GroundMode.Aggregate ? "aggregate" : "playback");
            BuildStage();
        }

        private void RestoreGroundRepresentative()
        {
            if (selectedDataset == null)
                return;
            int timeIndex = Mathf.Clamp(
                DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow),
                0, matrixTimes.Length - 1);
            int depthIndex = Mathf.Clamp(
                DisplayDepthBucketIndex(selectedGridColumn, selectedGridRow),
                0, matrixDepths.Length - 1);
            selectedTime = Mathf.Clamp(matrixTimes[timeIndex], 0, selectedDataset.TimeCount - 1);
            selectedZ = Mathf.Clamp(matrixDepths[depthIndex], 0, selectedDataset.DimZ - 1);
            slabNormalized = selectedDataset.DimZ > 1
                ? selectedZ / (float)(selectedDataset.DimZ - 1)
                : 0.5f;
            ApplyTimeFilter();
            UpdateSlabVisual(false);
            RebuildTimeMarkers();
        }

        private void ApplyGroundAggregateVisual()
        {
            if (groundAggregateVolume == null && !IsPending(PendingJob.GroundAggregate))
                LoadGroundAggregateVolume();
            SetGroundAggregateVisible(groundAggregateVolume != null);
            if (currentView != null)
            {
                currentView.SetVisible(viewState.cubeVisible);
                currentView.ApplyOpacity(GroundContextOpacity);
            }
            SetGroundEvidenceVisuals(true);
        }

        private void LoadGroundAggregateVolume()
        {
            if (IsPending(PendingJob.GroundAggregate) || !gridCellSelected)
                return;
            groundAggregateSnapshotId = SelectedCellSnapshotId();
            if (string.IsNullOrWhiteSpace(groundAggregateSnapshotId))
            {
                SetStatus(
                    "Ground blocked: this Grid has no completed immutable snapshot. Re-materialize it.");
                return;
            }
            int timeIndex = DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow);
            int depthIndex = DisplayDepthBucketIndex(selectedGridColumn, selectedGridRow);
            if (activeTimeBuckets == null || activeDepthBuckets == null ||
                timeIndex < 0 || timeIndex >= activeTimeBuckets.Length ||
                depthIndex < 0 || depthIndex >= activeDepthBuckets.Length)
            {
                SetStatus("Ground blocked: selected cell footprint is unavailable.");
                return;
            }
            DestroyGroundAggregateVolume();
            SetPending(PendingJob.GroundAggregate, true);
            string cellId = activeTimeBuckets[timeIndex].id + "__" +
                activeDepthBuckets[depthIndex].id;
            VolumeSTCubeS4DAnalysisClient client =
                new VolumeSTCubeS4DAnalysisClient(s4dUrl, 300, 1.0f);
            SetStatus("Loading the snapshot aggregate volume for " +
                CellLabel(selectedGridColumn, selectedGridRow) + "...");
            StartCoroutine(client.GroundAggregateVolume(
                groundAggregateSnapshotId, cellId,
                OnGroundAggregateVolumeComplete));
        }

        private void SetGroundAggregateVisible(bool visible)
        {
            if (groundAggregateVolume != null)
                groundAggregateVolume.gameObject.SetActive(visible && viewState.cubeVisible);
        }

        private void DestroyGroundAggregateVolume()
        {
            if (groundAggregateVolume != null)
                Destroy(groundAggregateVolume.gameObject);
            if (groundAggregateDataset != null)
                Destroy(groundAggregateDataset);
            groundAggregateVolume = null;
            groundAggregateDataset = null;
            groundSnapshotCellMean = float.NaN;
            groundReconstructedCellMean = float.NaN;
            groundValidFraction = 0.0f;
        }

        private string GroundEvidenceComparison()
        {
            if (float.IsNaN(groundSnapshotCellMean) ||
                float.IsNaN(groundReconstructedCellMean))
            {
                return "Load the source volume to compare the immutable cell " +
                    "snapshot with a reconstruction from raw data.";
            }

            float difference = Mathf.Abs(
                groundSnapshotCellMean - groundReconstructedCellMean);
            return "SNAPSHOT MEAN  " + groundSnapshotCellMean.ToString("0.###") +
                "\nRAW REBUILD     " + groundReconstructedCellMean.ToString("0.###") +
                "\nDIFFERENCE      " + difference.ToString("0.###") +
                "   |   VALID " + (groundValidFraction * 100.0f).ToString("0.0") + "%";
        }

        private string GroundFootprintSummary()
        {
            int timeIndex = DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow);
            int depthIndex = DisplayDepthBucketIndex(selectedGridColumn, selectedGridRow);
            if (selectedDataset == null)
                return "ACTIVE DATASET\nRepresentative: time bucket " +
                    (timeIndex + 1) + ", z " +
                    (matrixDepths != null && matrixDepths.Length > 0
                        ? matrixDepths[Mathf.Clamp(depthIndex, 0,
                            matrixDepths.Length - 1)].ToString()
                        : (depthIndex + 1).ToString());
            return selectedDataset.Name + "\nRepresentative: " +
                selectedDataset.GetTimeLabel(matrixTimes[Mathf.Clamp(
                    timeIndex, 0, matrixTimes.Length - 1)]) +
                ", z " + matrixDepths[Mathf.Clamp(
                    depthIndex, 0, matrixDepths.Length - 1)];
        }

        private void MarkEvidenceLocalized()
        {
            inspected = false;
            boundarySuspect = false;
            evidenceLocalized = true;
            int cellIndex = Mathf.Clamp(
                SourceCellIndex(selectedGridColumn, selectedGridRow),
                0, facetCellLocalized.Length - 1);
            facetCellInspected[cellIndex] = false;
            facetCellBoundarySuspect[cellIndex] = false;
            facetCellLocalized[cellIndex] = true;
            if (currentAnalysisNode != null)
            {
                currentAnalysisNode.inspected = false;
                currentAnalysisNode.boundarySuspect = false;
                EnsureNodeGroundStatus(currentAnalysisNode);
                currentAnalysisNode.verifiedCells[cellIndex] = false;
                currentAnalysisNode.suspectCells[cellIndex] = false;
                currentAnalysisNode.localizedCells[cellIndex] = true;
            }
            FocusDigestPageOnSelection();
            stage = Stage.Result;
            RecordTrailEvent("EVIDENCE", "local pattern only");
            SetStatus("Conclusion saved: this pattern is local to the selected footprint.");
            BuildStage();
        }

        private static void EnsureNodeGroundStatus(AnalysisNodeState node)
        {
            if (node.verifiedCells == null ||
                node.verifiedCells.Length != MaxFacetCells)
                node.verifiedCells = new bool[MaxFacetCells];
            if (node.suspectCells == null ||
                node.suspectCells.Length != MaxFacetCells)
                node.suspectCells = new bool[MaxFacetCells];
            if (node.localizedCells == null ||
                node.localizedCells.Length != MaxFacetCells)
                node.localizedCells = new bool[MaxFacetCells];
            if (node.pinnedCells == null ||
                node.pinnedCells.Length != MaxFacetCells)
                node.pinnedCells = new bool[MaxFacetCells];
        }

        /// <summary>
        /// Toggles the workbench-side field presentation: frame, axis labels,
        /// DISPLAY DATA strip and day rail, all of which live under spatialRoot.
        /// </summary>
        private void SetFieldPresentationVisible(bool visible)
        {
            if (spatialRoot == null)
                return;
            Renderer[] chrome = spatialRoot.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < chrome.Length; index++)
                if (chrome[index] != null)
                    chrome[index].enabled = visible;
        }

        private void SetDepth(float normalized)
        {
            slabNormalized = Mathf.Clamp01(normalized);
            UpdateSlabVisual(true);
            RefreshSlabTexture();
            BuildStage();
        }

        private void BeginSlabInteraction()
        {
            if (selectedDataset == null || rayInteractor == null)
                return;
            if (interaction.drawingRegion)
            {
                if (TryGetSlabPoint(rayInteractor.PointerRay, out Vector2 point))
                {
                    interaction.regionDragging = true;
                    regionStart = point;
                    region = new Rect(point.x - 0.001f, point.y - 0.001f, 0.002f, 0.002f);
                    UpdateRegionVisual();
                }
                return;
            }

            interaction.draggingSlab = true;
#if UNITY_EDITOR || SLABLAB_FLAT
            if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
            {
                desktopDragStartMouseY = FlatPointerPosition.y;
                desktopDragStartSlab = slabNormalized;
                return;
            }
#endif
            float handNormalized = GetHandDepthNormalized();
            slabDragOffset = slabNormalized - handNormalized;
        }

        private float GetHandDepthNormalized()
        {
            if (spatialRoot == null || rayInteractor == null)
                return 0.5f;

            // Drag on a vertical interaction plane through the Field. The plane
            // follows the Field rotation, so the cut remains directly under the
            // visible controller ray even after the user rotates the workspace.
            Plane dragPlane = new Plane(spatialRoot.transform.forward,
                spatialRoot.transform.position);
            if (dragPlane.Raycast(rayInteractor.PointerRay, out float distance) &&
                distance >= 0.0f && distance <= rayInteractor.maxDistance)
            {
                Vector3 local = spatialRoot.transform.InverseTransformPoint(
                    rayInteractor.PointerRay.GetPoint(distance));
                return Mathf.Clamp01(Mathf.InverseLerp(
                    volumeLocalMinY, volumeLocalMaxY, local.y));
            }

            Vector3 fallbackLocal = spatialRoot.transform.InverseTransformPoint(
                rayInteractor.transform.position);
            return Mathf.Clamp01(Mathf.InverseLerp(
                volumeLocalMinY, volumeLocalMaxY, fallbackLocal.y));
        }

        private bool TryGetSlabPoint(Ray ray, out Vector2 normalized)
        {
            normalized = Vector2.zero;
            Plane plane = new Plane(spatialRoot.transform.up, slabObject.transform.position);
            if (!plane.Raycast(ray, out float distance) || distance < 0.0f || distance > rayInteractor.maxDistance)
                return false;
            Vector3 local = spatialRoot.transform.InverseTransformPoint(ray.GetPoint(distance));
            normalized = new Vector2(
                Mathf.Clamp01(local.x / (FieldHalfWidth * 1.88f) + 0.5f),
                Mathf.Clamp01(local.z / (FieldHalfDepth * 1.88f) + 0.5f));
            return true;
        }

        // currentView is the hidden source/import view used by time filtering.
        // In Variable=Faceted mode each visible Field owns a separate paired
        // volume, so showing currentView as well produces a second unrelated
        // volume inside the first Field during boundary authoring.
        private void ApplyPrimaryVolumeVisibility()
        {
            if (currentView == null)
                return;
            bool aggregateGroundOwnsView = groundDocked &&
                groundMode == GroundMode.Aggregate &&
                groundAggregateVolume != null;
            currentView.SetVisible(viewState.cubeVisible &&
                !UsesFacetedVariableFields() &&
                !aggregateGroundOwnsView);
        }

        private void ApplyPairedVolumeAppearance(VolumeRenderedObject volume)
        {
            if (volume == null)
                return;

            VolumeControllerObject sourceController = currentView != null
                ? currentView.GetManagedController() : null;
            if (sourceController == null && currentView != null &&
                currentView.rootObject != null)
                sourceController = currentView.rootObject.GetComponentInChildren<
                    VolumeControllerObject>(true);

            // Always establish the colour-capable DVR path. The raw object
            // factory otherwise starts with its generic grayscale transfer
            // function, which made faceted variables appear white or black.
            volume.SetRenderMode(RenderMode.DirectVolumeRendering);
            volume.SetTransferFunctionMode(TFRenderMode.TF1D);
            volume.SetLightingEnabled(false);
            if (sourceController == null)
                return;

            if (sourceController.transferFunction != null)
            {
                volume.transferFunction = sourceController.transferFunction;
                volume.SetTransferFunction(sourceController.transferFunction);
            }
            volume.transferFunction2D = sourceController.transferFunction2D;
            volume.SetVisibilityWindow(sourceController.GetVisibilityWindow().x,
                sourceController.GetVisibilityWindow().y);
            volume.SetRayTerminationEnabled(
                sourceController.GetRayTerminationEnabled());
            volume.SetCubicInterpolationEnabled(
                sourceController.GetCubicInterpolationEnabled());
            volume.SetHighlightPosition(sourceController.highlightPosition);
            volume.SetHighlightRadius(sourceController.highlightRadius);

            // Clone the already validated STC material so shader keywords and
            // colour sampling exactly match the primary Field. Restore the new
            // object's own scalar texture and dimensions after cloning.
            MeshRenderer sourceRenderer = null;
            if (sourceController.meshRenderers != null)
            {
                for (int index = 0; index < sourceController.meshRenderers.Length;
                    index++)
                {
                    if (sourceController.meshRenderers[index] != null &&
                        sourceController.meshRenderers[index].sharedMaterial != null)
                    {
                        sourceRenderer = sourceController.meshRenderers[index];
                        break;
                    }
                }
            }
            if (sourceRenderer == null || volume.meshRenderer == null ||
                volume.meshRenderer.sharedMaterial == null)
                return;

            Material previousMaterial = volume.meshRenderer.sharedMaterial;
            Texture dataTexture = previousMaterial.HasProperty("_DataTex")
                ? previousMaterial.GetTexture("_DataTex") : null;
            Material matchedMaterial = new Material(sourceRenderer.sharedMaterial);
            if (matchedMaterial.HasProperty("_DataTex"))
                matchedMaterial.SetTexture("_DataTex", dataTexture);
            if (matchedMaterial.HasProperty("_GradientTex"))
                matchedMaterial.SetTexture("_GradientTex", null);
            if (matchedMaterial.HasProperty("_TextureSize") &&
                volume.dataset != null)
                matchedMaterial.SetVector("_TextureSize", new Vector3(
                    volume.dataset.dimX, volume.dataset.dimY,
                    volume.dataset.dimZ));
            matchedMaterial.DisableKeyword("LIGHTING_ON");
            matchedMaterial.EnableKeyword("MODE_DVR");
            matchedMaterial.DisableKeyword("MODE_MIP");
            matchedMaterial.DisableKeyword("MODE_SURF");
            volume.meshRenderer.sharedMaterial = matchedMaterial;
            Destroy(previousMaterial);
        }

        private static bool TryRendererBoundsInSpace(Renderer[] renderers,
            Transform space, out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null || !renderer.enabled)
                    continue;
                Bounds world = renderer.bounds;
                Vector3 minimum = world.min;
                Vector3 maximum = world.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 worldPoint = new Vector3(
                        (corner & 1) == 0 ? minimum.x : maximum.x,
                        (corner & 2) == 0 ? minimum.y : maximum.y,
                        (corner & 4) == 0 ? minimum.z : maximum.z);
                    Vector3 localPoint = space.InverseTransformPoint(worldPoint);
                    if (!hasBounds)
                    {
                        bounds = new Bounds(localPoint, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(localPoint);
                    }
                }
            }
            return hasBounds;
        }

        private static bool TryGetLocalRendererBounds(
            Transform frame, Renderer[] renderers, out Bounds localBounds)
        {
            localBounds = new Bounds(Vector3.zero, Vector3.zero);
            bool found = false;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null || !renderer.enabled)
                    continue;
                Bounds worldBounds = renderer.bounds;
                Vector3 minimum = worldBounds.min;
                Vector3 maximum = worldBounds.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 worldCorner = new Vector3(
                        (corner & 1) == 0 ? minimum.x : maximum.x,
                        (corner & 2) == 0 ? minimum.y : maximum.y,
                        (corner & 4) == 0 ? minimum.z : maximum.z);
                    Vector3 localCorner = frame.InverseTransformPoint(worldCorner);
                    if (!found)
                    {
                        localBounds = new Bounds(localCorner, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localCorner);
                    }
                }
            }
            return found;
        }

        private static void HideInitialSceneVolumes()
        {
            VolumeControllerObject[] controllers = FindObjectsOfType<VolumeControllerObject>();
            for (int i = 0; i < controllers.Length; i++)
            {
                VolumeControllerObject controller = controllers[i];
                if (controller == null)
                    continue;
                HashSet<VolumeDataset> released = new HashSet<VolumeDataset>();
                VolumeRenderedObject[] volumes =
                    controller.GetComponentsInChildren<VolumeRenderedObject>(true);
                for (int volumeIndex = 0; volumeIndex < volumes.Length; volumeIndex++)
                {
                    VolumeDataset dataset = volumes[volumeIndex] != null
                        ? volumes[volumeIndex].dataset : null;
                    if (dataset == null || !released.Add(dataset))
                        continue;
                    dataset.ReleaseRuntimeTextures();
                    Destroy(dataset);
                }
                VolumeSTCubeOriginalSceneAdapter.ClearExistingVolumes(controller);
                // The controller is reused when a variable is selected. Keeping it
                // active but empty avoids retaining the authored demo volume while
                // still allowing the runtime loader to attach the chosen dataset.
                controller.gameObject.SetActive(true);
                controller.SetLightingEnabled(false);
            }
        }
    }
}
