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
        public void ToggleFacetGrid()
        {
            if (facetGridCanvas == null)
                return;
            bool next = !facetGridCanvas.gameObject.activeSelf;
            if (next)
            {
                HidePrimaryToolsExcept(facetGridCanvas);
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
                SetFacetSelectionEvidencePreview(gridCellSelected);
            }
            else
            {
                facetGridCanvas.gameObject.SetActive(false);
                SetFacetSelectionEvidencePreview(false);
                if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled &&
                    stage == Stage.Matrix && panelCanvas != null)
                {
                    ShowPrimaryTool(panelCanvas);
                    BuildStage();
                    VolumeSTCubeFlatScreenHUD.NotifyWorkflowChanged();
                }
            }
        }

        private void CreateAnalysisAxes()
        {
            float bottom = -FieldHalfHeight + 0.115f;
            float innerDepth = FieldHalfDepth - 0.070f;
            float left = -FieldHalfWidth + 0.075f;

            CreateWorldLine("Analysis axis Y Variable", spatialRoot.transform,
                new Vector3(left, bottom, -FieldHalfDepth + 0.07f),
                new Vector3(left, bottom, innerDepth),
                new Color(VariableColor.r, VariableColor.g, VariableColor.b, 0.82f),
                0.009f);
            CreateWorldLine("Analysis axis Z Depth", spatialRoot.transform,
                new Vector3(left, -FieldHalfHeight + 0.075f, innerDepth),
                new Vector3(left, FieldHalfHeight - 0.075f, innerDepth),
                new Color(DepthAxisColor.r, DepthAxisColor.g, DepthAxisColor.b, 0.92f),
                0.010f);

            variableAxisLabel = CreateWorldLabel("Y  VARIABLE", new Vector3(
                    left + 0.025f, bottom + 0.050f, -FieldHalfDepth + 0.12f),
                0.0080f, TextAnchor.LowerLeft, VariableColor);
            depthAxisLabel = CreateWorldLabel("Z  DEPTH", new Vector3(
                    left + 0.035f, FieldHalfHeight - 0.055f, innerDepth - 0.020f),
                0.0080f, TextAnchor.UpperLeft, DepthAxisColor);

            Color[] depthColors =
            {
                new Color(0.20f, 0.82f, 1.0f, 0.95f),
                new Color(0.36f, 0.55f, 1.0f, 0.95f),
                new Color(0.63f, 0.36f, 0.96f, 0.95f)
            };
            for (int index = 0; index < 3; index++)
            {
                depthBucketAxisSegments[index] = CreateWorldLine(
                    "Depth bucket axis " + index, spatialRoot.transform,
                    Vector3.zero, Vector3.zero, depthColors[index], 0.018f);
                depthBucketAxisLabels[index] = CreateWorldLabel(
                    "DEPTH BUCKET", Vector3.zero, 0.0065f,
                    TextAnchor.MiddleLeft, depthColors[index]);
                depthBucketAxisSegments[index].gameObject.SetActive(false);
                depthBucketAxisLabels[index].gameObject.SetActive(false);
            }
            CreateAxisOriginHub(new Vector3(left, bottom, innerDepth));
            UpdateAnalysisAxisLabels();
        }

        private void CreateFacetGridPanel()
        {
            facetGridCanvas = CreateFloatingCanvas(
                "S4D Anchored Facet Grid",
                new Vector3(0.28f, 1.76f, 1.20f),
                // One shared, larger editor surface is used by Pivot, Drill,
                // and Roll-up.  The extra margin keeps the row/column chips,
                // preview, and footer readable in Quest without overlap.
                new Vector2(1560, 900),
                // The facet grid is the only content of this panel now, so it is
                // authored larger and the shared colour scale sits outside it.
                0.00082f,
                Cyan);
            facetGridContent = facetGridCanvas.GetComponent<RectTransform>();
            facetGridCanvasGroup = facetGridCanvas.gameObject.AddComponent<CanvasGroup>();
            facetGridCanvas.sortingOrder = 105;
            AddDesktopPanelFocusTarget(facetGridCanvas,
                FocusDesktopMatrixPanel);
            facetGridCanvas.gameObject.SetActive(false);
        }

        private void BuildFacetGridPanel()
        {
            if (facetGridContent == null)
                return;
            ClearChildren(facetGridContent);
            facetGridProgressText = null;
            facetGridProgressStageText = null;
            facetGridValidatedText = null;
            facetGridProgressFill = null;
            Array.Clear(facetGridCellImages, 0, facetGridCellImages.Length);
            Array.Clear(facetGridCellStateLabels, 0, facetGridCellStateLabels.Length);
            Array.Clear(facetGridCellPlaceholders, 0,
                facetGridCellPlaceholders.Length);
            CreateText(facetGridContent,
                FacetAxisSummary().ToUpperInvariant() +
                    (gridStale ? "  /  STALE" : string.Empty), 33,
                FontStyle.Bold, new Vector2(-335, 350), new Vector2(700, 46),
                TextAnchor.MiddleLeft, gridStale ? Amber : Ink);

            if (placementConfirmed && s4dGridImage != null)
            {
                // The 2D / 3D layer switch used to sit here. The panel is a
                // chart surface now, so the atlas always shows as one grid.
            }

            // Once an operation is confirmed the result panel becomes a
            // dedicated materialization view.  Do not leave the old result
            // atlas or Pivot / Drill / Roll-up selection overlay underneath
            // the progress UI: it makes the target matrix look like another
            // editable draft and, for expanded grids, causes severe overlap.
            // Network callbacks may advance the semantic workflow state while
            // the current MatPlot job is still streaming.  The visual state is
            // therefore keyed by jobRunning as well, so the source draft can
            // never reappear underneath the progress view.
            VolumeSTCubeSliceDataset gridDataset = selectedDataset;
            if (materializedLayerAtlases.Count > 0 &&
                materializationVariableIndices.Count >= materializedLayerAtlases.Count)
            {
                int gridVariableIndex = materializationVariableIndices[
                    materializedLayerAtlases.Count - 1];
                if (gridVariableIndex >= 0 && gridVariableIndex < datasets.Count)
                    gridDataset = datasets[gridVariableIndex];
            }
            bool materializingView = IsPending(PendingJob.Analysis) ||
                spatialWorkflowStep == SpatialWorkflowStep.Materializing;
            if (materializingView)
            {
                CreateFacetPreviewCells(facetGridContent, new Vector2(-230, -20),
                    SlabLabLayout.PanelCardSizeLarge, true);
                BuildFacetGenerationOverlay();
            }
            else if (!placementConfirmed)
            {
                CreateFacetPreviewCells(facetGridContent, new Vector2(-230, -20),
                    SlabLabLayout.PanelCardSizeLarge, false);
                BuildFacetSelectionCard(true);
            }
            else if (s4dGridImage == null)
            {
                CreateMaterializedFacetCells(facetGridContent, null,
                    new Vector2(-230, -20), SlabLabLayout.PanelCardSizeLarge);
                if (IsPending(PendingJob.Analysis))
                    BuildFacetGenerationOverlay();
                else
                {
                    CreatePanelCard(facetGridContent, new Vector2(500, 10),
                        new Vector2(330, 390), Amber);
                    CreateText(facetGridContent, "FAILED", 48, FontStyle.Bold,
                        new Vector2(500, 78), new Vector2(270, 70),
                        TextAnchor.MiddleCenter, Amber);
                    CreateText(facetGridContent, "NO MATPLOT GRID COMMITTED",
                        18, FontStyle.Bold, new Vector2(500, 25),
                        new Vector2(270, 58), TextAnchor.MiddleCenter, Ink);
                    CreateText(facetGridContent,
                        !string.IsNullOrWhiteSpace(s4dGridFailure)
                            ? s4dGridFailure
                            : intentConfigured
                                ? "FULL MATRIX NOT STARTED\nSelect Retry MatPlotAgent to submit the resolved intent."
                                : "INTENT REQUIRED\nResolve the MatPlot intent before generating the Grid.",
                        14, FontStyle.Normal, new Vector2(500, -58),
                        new Vector2(270, 72), TextAnchor.MiddleCenter, Muted);
                }
            }
            else
            {
                Vector2 materializedGridPosition = draftOperation == DraftOperation.None
                    ? new Vector2(0, 20)
                    : new Vector2(-165, -85);
                Vector2 materializedGridSize = draftOperation == DraftOperation.None
                    ? new Vector2(1460, 600)
                    : SlabLabLayout.PanelCardSizeLarge;
                CreateMaterializedFacetCells(facetGridContent, s4dGridImage,
                    materializedGridPosition, materializedGridSize);
                if (draftOperation == DraftOperation.None)
                {
                    BuildSharedColorScale(facetGridContent);
                    BuildFacetSelectionCard(false);
                    // The panel is a display surface: one green entry point to
                    // the findings, everything else lives in the workflow bar.
                    CreateButton(facetGridContent,
                        IsPending(PendingJob.Digest) ? "AI FINDINGS PREPARING..." :
                            "OPEN AI FINDINGS",
                        new Vector2(0, -322), new Vector2(460, 56), Green,
                        OpenAiFindingsPanel);
                }
                else
                {
                    BuildDraftGridInteractionOverlay();
                }
                if (IsPending(PendingJob.Analysis))
                    BuildFacetGenerationOverlay();
            }

            // Draft mode owns the footer. Previously the normal result footer
            // was created after the draft controls, covering APPLY and leaving
            // Pivot / Drill / Roll-up looking like an empty result page.
            if (materializingView)
            {
                CreateButton(facetGridContent, "CANCEL GENERATION",
                    new Vector2(500, -238), new Vector2(300, 44), Amber,
                    CancelS4DGridJob);
            }
            else if (draftOperation != DraftOperation.None)
            {
                bool ready = DraftSelectionReady(out _);
                string confirmLabel = "CONFIRM " +
                    (draftOperation == DraftOperation.Pivot ? "PIVOT" :
                    draftOperation == DraftOperation.Drill ? "DRILL" :
                    "ROLL-UP") + "  >  GENERATE";
                CreateButton(facetGridContent, "CANCEL", new Vector2(-190, -365),
                    new Vector2(280, 56), Card, CancelDraft);
                CreateButton(facetGridContent, confirmLabel,
                    new Vector2(190, -365), new Vector2(420, 56),
                    ready ? OperationColor(draftOperation) : Card,
                    ConfirmDraftAndGenerate);
            }
            else if (!placementConfirmed)
            {
                CreateButton(facetGridContent, "Back to Slab", new Vector2(-205, -350),
                    new Vector2(330, 56), Card, ReturnToSlabFromPlacement);
                CreateButton(facetGridContent, "Confirm Placement", new Vector2(205, -350),
                    new Vector2(380, 56), Cyan, ConfirmGridPlacement);
            }
            else if (IsPending(PendingJob.Analysis))
            {
                CreateButton(facetGridContent, "Cancel Generation", new Vector2(-170, -350),
                    new Vector2(340, 56), Amber, CancelS4DGridJob);
                CreateButton(facetGridContent, "Keep Working", new Vector2(205, -350),
                    new Vector2(300, 56), Card, ToggleFacetGrid);
            }
            else if (s4dGridImage == null)
            {
                CreateButton(facetGridContent, "Retry MatPlotAgent", new Vector2(-170, -350),
                    new Vector2(360, 56), Purple, RematerializeS4DGrid);
                CreateButton(facetGridContent, "Close Grid", new Vector2(235, -350),
                    new Vector2(260, 56), Card, ToggleFacetGrid);
            }
            AnimatePanelRefresh(facetGridCanvasGroup,
                ref facetGridRefreshAnimation);
            RefreshMaterializedLayerVisibility();
            if (facetGridCanvas != null)
                KeepDesktopPanelInView(facetGridCanvas.GetComponent<RectTransform>());
        }

        private void ToggleFacetGridViewMode()
        {
            int selectedTimeIndex = DisplayTimeBucketIndex(
                selectedGridColumn, selectedGridRow);
            int selectedDepthIndex = DisplayDepthBucketIndex(
                selectedGridColumn, selectedGridRow);
            if (!facetGridLayered)
            {
                facetGridPreviousTransposed = activeGridTransposed;
                facetGridPreviousColumns = activeGridColumns;
                facetGridPreviousRows = activeGridRows;
                facetGridLayered = true;
                activeGridTransposed = false;
                activeGridColumns = Mathf.Max(1,
                    activeTimeBuckets != null ? activeTimeBuckets.Length : 3);
                activeGridRows = Mathf.Max(1,
                    activeDepthBuckets != null ? activeDepthBuckets.Length : 3);
                selectedGridColumn = Mathf.Clamp(selectedTimeIndex, 0,
                    activeGridColumns - 1);
                selectedGridRow = Mathf.Clamp(selectedDepthIndex, 0,
                    activeGridRows - 1);
            }
            else
            {
                facetGridLayered = false;
                activeGridTransposed = facetGridPreviousTransposed;
                activeGridColumns = facetGridPreviousColumns;
                activeGridRows = facetGridPreviousRows;
                selectedGridColumn = activeGridTransposed
                    ? Mathf.Clamp(selectedDepthIndex, 0, activeGridColumns - 1)
                    : Mathf.Clamp(selectedTimeIndex, 0, activeGridColumns - 1);
                selectedGridRow = activeGridTransposed
                    ? Mathf.Clamp(selectedTimeIndex, 0, activeGridRows - 1)
                    : Mathf.Clamp(selectedDepthIndex, 0, activeGridRows - 1);
            }
            facetGridPeeledLayers = 0;
            SetStatus(facetGridLayered
                ? "Facet Grid switched to 3D depth layers. Use PEEL to reveal deeper evidence."
                : "Facet Grid returned to the flat 2D comparison layout.");
            BuildFacetGridPanel();
        }

        private void PeelFacetGridLayer()
        {
            int rowCount = Mathf.Clamp(activeGridRows, 1, MaxFacetAxisBuckets);
            facetGridPeeledLayers = (facetGridPeeledLayers + 1) % rowCount;
            SetStatus(facetGridPeeledLayers == 0
                ? "Layer peel reset; all depth layers are visible."
                : facetGridPeeledLayers + " front depth layer(s) peeled away.");
            BuildFacetGridPanel();
        }

        private void ResetFacetGridPeel()
        {
            facetGridPeeledLayers = 0;
            SetStatus("Layer peel reset; surface, middle, and deep are visible.");
            BuildFacetGridPanel();
        }

        private void BuildFacetSelectionCard(bool placementPreview)
        {
            Color accent = gridCellSelected
                ? (selectedCellPinned ? Amber : Purple)
                : Cyan;
            // Clicking a cell keeps the dashed provenance line back to the 3D
            // field but no longer opens a statistics column; the caption names
            // what the line means instead.
            if (!placementPreview && gridCellSelected)
            {
                CreateText(facetGridContent,
                    "DASHED LINE  ·  WHERE THIS CELL SITS IN THE 3D FIELD",
                    13, FontStyle.Bold, new Vector2(0, -256),
                    new Vector2(1040, 24), TextAnchor.MiddleCenter, Purple);
                return;
            }
            // The panel is a chart surface now. Only the selected-cell readout
            // and the pre-anchor summary still need a right-hand column, so the
            // committed grid can use the full width for its three facet images.
            if (gridCellSelected || placementPreview)
            {
                CreatePanelCard(facetGridContent, new Vector2(515, 5),
                    new Vector2(340, 500), accent);
                BuildFacetAxisGizmo();
            }

            if (!gridCellSelected)
            {
                if (placementPreview)
                {
                    CreateText(facetGridContent, "READY TO ANCHOR",
                        21, FontStyle.Bold, new Vector2(515, 198),
                        new Vector2(280, 32), TextAnchor.MiddleLeft, Cyan);
                    CreateText(facetGridContent,
                        (activeGridColumns * activeGridRows) +
                            " SOURCE PREVIEW CELLS",
                        12, FontStyle.Bold,
                        new Vector2(515, 166), new Vector2(280, 22),
                        TextAnchor.MiddleLeft, Muted);
                    CreateText(facetGridContent,
                        IntentDisplayLabel() +
                        "\n\nShared color scale\nIdentical encoding" +
                        "\nTime x Depth ordering\nReal interval means",
                        15, FontStyle.Bold, new Vector2(515, 45),
                        new Vector2(280, 210), TextAnchor.UpperLeft, Ink);
                }
                else
                {
                    // Statistics moved out of the panel: the workflow bar owns
                    // the operations and AI findings has its own button below
                    // the grid, so the facets stay the only content here.
                }
                return;
            }

            int index = SourceCellIndex(selectedGridColumn, selectedGridRow);
            int timeIndex = DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow);
            int depthIndex = DisplayDepthBucketIndex(selectedGridColumn, selectedGridRow);
            string time = selectedDataset != null && matrixTimes.Length > timeIndex
                ? selectedDataset.GetTimeLabel(matrixTimes[timeIndex])
                : "-";
            string variable = selectedDataset != null ? selectedDataset.Name : "-";
            CreateText(facetGridContent, "CELL EVIDENCE", 25, FontStyle.Bold,
                new Vector2(515, 204), new Vector2(280, 32),
                TextAnchor.MiddleLeft, Cyan);
            CreateText(facetGridContent,
                CellLabel(selectedGridColumn, selectedGridRow),
                20, FontStyle.Bold, new Vector2(515, 168),
                new Vector2(280, 26), TextAnchor.MiddleLeft, Ink);
            FindDigestExtremes(out int cellMinimumIndex,
                out int cellMaximumIndex, out int cellWidestIndex);
            string findingRole = !matrixHasData[index]
                ? "STATISTICS UNAVAILABLE"
                : index == cellMaximumIndex
                    ? "HIGHEST CELL AVERAGE"
                    : index == cellMinimumIndex
                        ? "LOWEST CELL AVERAGE"
                        : index == cellWidestIndex
                            ? "BIGGEST WITHIN-CELL SPREAD"
                            : "SELECTED CELL";
            CreateText(facetGridContent, findingRole,
                18, FontStyle.Bold, new Vector2(515, 139),
                new Vector2(280, 24), TextAnchor.MiddleLeft,
                index == cellMaximumIndex ? Amber :
                index == cellMinimumIndex ? Cyan : Purple);
            string statisticSummary = matrixHasData[index]
                ? "CELL AVERAGE  " + matrixMeans[index].ToString("0.###") +
                    FindingUnitSuffix() +
                    "\nMIN-MAX  " + matrixMinimums[index].ToString("0.###") +
                    " - " + matrixMaximums[index].ToString("0.###") +
                    FindingUnitSuffix() +
                    "\nVALID DATA  " +
                    (matrixValidFractions[index] * 100.0f).ToString("0.0") + "%"
                : "LEGACY RESULT\nRe-materialize to compute statistics.";
            CreateText(facetGridContent, statisticSummary,
                20, FontStyle.Bold, new Vector2(515, 61),
                new Vector2(280, 82), TextAnchor.UpperLeft,
                matrixHasData[index] ? Ink : Amber);
            string groundSummary = !float.IsNaN(groundSnapshotCellMean) &&
                !float.IsNaN(groundReconstructedCellMean)
                    ? "GROUND VERIFIED  Δ " + Mathf.Abs(
                        groundSnapshotCellMean -
                        groundReconstructedCellMean).ToString("0.###")
                    : "GROUND NOT CHECKED";
            // Use an ASCII label so the Quest font fallback cannot corrupt the
            // delta marker inherited from an older source encoding.
            if (!float.IsNaN(groundSnapshotCellMean) &&
                !float.IsNaN(groundReconstructedCellMean))
                groundSummary = "GROUND VERIFIED  DELTA " + Mathf.Abs(
                    groundSnapshotCellMean -
                    groundReconstructedCellMean).ToString("0.###");
            CreateText(facetGridContent, groundSummary,
                12, FontStyle.Bold, new Vector2(515, -15),
                new Vector2(280, 22), TextAnchor.MiddleLeft,
                groundSummary.StartsWith("GROUND VERIFIED") ? Green : Cyan);
            int sourceTimeFirst;
            int sourceTimeLast;
            int sourceDepthFirst;
            int sourceDepthLast;
            bool hasSourceFootprint = TryGetGroundBucketRanges(
                out sourceTimeFirst, out sourceTimeLast,
                out sourceDepthFirst, out sourceDepthLast);
            string sourceSummary = selectedCellPinned
                ? "PINNED  /  source remains attached to this snapshot"
                : hasSourceFootprint && selectedDataset != null
                    ? variable + "  /  " +
                        selectedDataset.GetTimeLabel(sourceTimeFirst) +
                        " - " + selectedDataset.GetTimeLabel(sourceTimeLast) +
                        "  /  z " + sourceDepthFirst + "-" + sourceDepthLast
                    : "Source footprint unavailable for this cell.";
            CreateText(facetGridContent, sourceSummary,
                12, FontStyle.Normal, new Vector2(515, -52),
                new Vector2(280, 52), TextAnchor.UpperLeft,
                selectedCellPinned ? Amber : Green);
            CreateButton(facetGridContent,
                selectedCellPinned ? "UNPIN" : "PIN CELL",
                new Vector2(445, -170), new Vector2(135, 44),
                selectedCellPinned ? Amber : Card, ToggleSelectedCellPin);
            CreateButton(facetGridContent, "DISMISS",
                new Vector2(595, -170), new Vector2(135, 44),
                Card, DismissSelectedCell);
        }

        private void BuildGridSnapshotActions()
        {
            bool pinned = currentAnalysisNode != null &&
                currentAnalysisNode.pinned;
            bool confirmingDelete = currentAnalysisNode != null &&
                pendingDeleteNodeId == currentAnalysisNode.nodeId;
            CreateButton(facetGridContent, pinned ? "UNPIN GRID" : "PIN GRID",
                new Vector2(420, -221), new Vector2(94, 34),
                pinned ? Amber : Card, ToggleCurrentGridPin);
            CreateButton(facetGridContent, "DISMISS",
                new Vector2(520, -221), new Vector2(94, 34),
                Card, DismissCurrentGrid);
            CreateButton(facetGridContent,
                confirmingDelete ? "CONFIRM" : "DELETE",
                new Vector2(620, -221), new Vector2(94, 34),
                confirmingDelete ? Danger : Card, DeleteCurrentGridLeaf);
        }

        private void SelectFacetPreviewCell(int column, int row)
        {
            selectedGridColumn = Mathf.Clamp(column, 0, Mathf.Max(0, activeGridColumns - 1));
            selectedGridRow = Mathf.Clamp(row, 0, Mathf.Max(0, activeGridRows - 1));
            gridCellSelected = true;
            int selectedIndex = SourceCellIndex(selectedGridColumn, selectedGridRow);
            selectedCellPinned = facetCellPinned[Mathf.Clamp(
                selectedIndex, 0, facetCellPinned.Length - 1)];
            BuildMatrixBucketSelections();
            int timeIndex = DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow);
            int depthIndex = DisplayDepthBucketIndex(selectedGridColumn, selectedGridRow);
            selectedTime = matrixTimes[Mathf.Clamp(timeIndex, 0, matrixTimes.Length - 1)];
            selectedZ = matrixDepths[Mathf.Clamp(depthIndex, 0, matrixDepths.Length - 1)];
            slabNormalized = selectedDataset != null && selectedDataset.DimZ > 1
                ? selectedZ / (float)(selectedDataset.DimZ - 1)
                : 0.5f;
            ApplyTimeFilter();
            UpdateSlabVisual(false);
            int textureIndex = SourceCellIndex(selectedGridColumn, selectedGridRow);
            if (matrixTextures != null && textureIndex < matrixTextures.Length &&
                matrixTextures[textureIndex] != null)
            {
                slabTexture = matrixTextures[textureIndex];
                if (slabPreviewMaterial != null)
                    slabPreviewMaterial.mainTexture = slabTexture;
            }
            RebuildTimeMarkers();
            SetStatus("Selected " + CellLabel(selectedGridColumn, selectedGridRow) +
                ". Its source time range is highlighted in the XYT STC.");
            if (stage == Stage.Matrix && panelCanvas != null &&
                panelCanvas.gameObject.activeSelf)
                BuildStage();
            BuildFacetGridPanel();
            SetFacetSelectionEvidencePreview(true);
        }

        private void SetFacetSelectionEvidencePreview(bool visible)
        {
            bool show = visible && gridCellSelected && !groundDocked &&
                facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf;
            // A MatPlot-card preview is grounded in the independent XYT STC,
            // not in the legacy animated Field. Ground mode later in the
            // workflow still uses the original evidence visuals.
            SetGroundEvidenceVisuals(false);
            if (!show)
            {
                VolumeSTCubeForVrXytCompanion companion =
                    FindObjectOfType<VolumeSTCubeForVrXytCompanion>();
                if (companion != null)
                    companion.HideMatPlotSourceRange();
            }
            if (groundLink == null)
                return;
            groundLink.gameObject.SetActive(false);
            SetMatPlotStcDashedLinkVisible(show);
            if (show)
                UpdateGroundEvidenceLink();
        }

        private void OpenGroundFromGrid()
        {
            if (!gridCellSelected)
            {
                SetStatus("Select one Time x Depth cell before opening Ground.");
                BuildFacetGridPanel();
                return;
            }
            SelectS4DGridCell(selectedGridColumn, selectedGridRow);
        }

        private void ReturnToFacetGrid()
        {
            SetGroundDock(false);
            if (panelCanvas != null)
                panelCanvas.gameObject.SetActive(false);
            if (facetGridCanvas != null)
            {
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
                SetFacetSelectionEvidencePreview(gridCellSelected);
            }
            stage = Stage.Matrix;
            SetStatus("Returned to the anchored Facet Grid. Ground evidence remains linked to its snapshot.");
        }

        private void SetMatPlotStcDashedLink(Vector3 source, Vector3 target)
        {
            Vector3 delta = target - source;
            int segmentCount = matPlotStcLinkSegments.Length;
            for (int index = 0; index < segmentCount; index++)
            {
                LineRenderer segment = matPlotStcLinkSegments[index];
                if (segment == null)
                    continue;
                float start = index / (float)segmentCount;
                float end = (index + 0.58f) / segmentCount;
                segment.SetPosition(0, source + delta * start);
                segment.SetPosition(1, source + delta * Mathf.Min(1.0f, end));
                segment.gameObject.SetActive(true);
            }
        }

        private void SetMatPlotStcDashedLinkVisible(bool visible)
        {
            for (int index = 0; index < matPlotStcLinkSegments.Length; index++)
                if (matPlotStcLinkSegments[index] != null)
                    matPlotStcLinkSegments[index].gameObject.SetActive(visible);
        }

        private void FocusDesktopMatrixPanel()
        {
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled &&
                stage == Stage.Matrix)
                SetDesktopFocusView(DesktopFocusView.SlabAxis, true);
        }

        private void BuildMatrixStage()
        {
            CreateText(panelContent, "MATRIX", 27, FontStyle.Bold,
                new Vector2(-430, 206), new Vector2(160, 38),
                TextAnchor.MiddleLeft, Ink);

            if (!placementConfirmed && s4dGridImage == null)
            {
                CreateText(panelContent, "PLACE GRID", 15, FontStyle.Bold,
                    new Vector2(0, 160), new Vector2(1030, 24), TextAnchor.MiddleLeft, Amber);
                CreateText(panelContent,
                    "A translucent " + activeGridColumns + " x " + activeGridRows +
                        " preview appears 1.5 m ahead. Grip adjusts pose; trigger confirms.",
                    17, FontStyle.Normal, new Vector2(0, 129), new Vector2(980, 30),
                    TextAnchor.MiddleLeft, Muted);
                CreateWireGrid(panelContent, new Vector2(0, -5), new Vector2(720, 260),
                    Mathf.Max(1, activeGridColumns), Mathf.Max(1, activeGridRows), Cyan);
                CreateText(panelContent, "No collision  /  shared scale  /  " +
                    Mathf.Max(1, activeGridColumns * activeGridRows) + " cells",
                    15, FontStyle.Bold, new Vector2(0, -154), new Vector2(720, 24),
                    TextAnchor.MiddleCenter, Green);
                CreateButton(panelContent, "Back to Slab", new Vector2(-205, -207),
                    new Vector2(300, 52), Card, () => Navigate(Stage.Slab));
                CreateButton(panelContent, "Confirm Placement", new Vector2(205, -207),
                    new Vector2(360, 52), Amber, ConfirmGridPlacement);
                return;
            }

            if (IsPending(PendingJob.Analysis) || resumeMaterializationAfterManifest ||
                IsPending(PendingJob.DatasetManifest))
            {
                desktopMatrixProgressStageText = CreateText(panelContent,
                    resumeMaterializationAfterManifest
                        ? "Preparing dataset for MatPlot"
                        : "Materializing immutable snapshot",
                    23, FontStyle.Bold, new Vector2(0, 82), new Vector2(920, 40),
                    TextAnchor.MiddleCenter, Ink);
                CreateDesktopMatrixProgressBar(panelContent);
                if (!resumeMaterializationAfterManifest)
                    CreateButton(panelContent, "CANCEL", new Vector2(0, -155),
                        new Vector2(250, 42), Card, CancelS4DGridJob);
                return;
            }

            if (s4dGridImage == null)
            {
                CreateText(panelContent,
                    "The grid could not be materialized. The Slab configuration is still available.",
                    20, FontStyle.Normal, new Vector2(0, 42), new Vector2(900, 70),
                    TextAnchor.MiddleCenter, Ink);
                CreateButton(panelContent, "Retry Full Matrix",
                    new Vector2(0, -62), new Vector2(420, 58), Amber,
                    RetryS4DGridJob);
                return;
            }

            if (gridStale)
            {
                CreatePanelCard(panelContent, new Vector2(0, 155), new Vector2(1010, 38), Amber);
                CreateText(panelContent,
                    "STALE  /  Boundary changed since this snapshot was generated",
                    15, FontStyle.Bold, new Vector2(-90, 155), new Vector2(790, 26),
                    TextAnchor.MiddleLeft, Amber);
                CreateButton(panelContent, "Re-materialize", new Vector2(400, 155),
                    new Vector2(195, 32), Amber, RematerializeS4DGrid);
            }

            // Size the Matrix from the actual MatPlot atlas. This preserves the
            // source chart proportions on wide, square and portrait outputs.
            Vector2 matrixSize = DesktopMatrixGridSize(s4dGridImage,
                new Vector2(1010.0f, gridStale ? 520.0f : 600.0f));
            CreateMaterializedFacetCells(panelContent, s4dGridImage,
                new Vector2(0, gridStale ? -55 : -65), matrixSize);
            CreateButton(panelContent,
                IsPending(PendingJob.Digest) ? "FINDINGS ..." : "FINDINGS",
                new Vector2(430, 206), new Vector2(190, 46), Green,
                OpenAiFindingsPanel);
        }

        private void CreateDesktopMatrixProgressBar(RectTransform parent)
        {
            float shownProgress = resumeMaterializationAfterManifest
                ? Mathf.Max(0.04f, displayedGridProgress)
                : displayedGridProgress;

            GameObject trackObject = new GameObject("Matrix progress track",
                typeof(RectTransform));
            trackObject.transform.SetParent(parent, false);
            RectTransform track = trackObject.GetComponent<RectTransform>();
            track.sizeDelta = new Vector2(760.0f, 46.0f);
            track.anchoredPosition = new Vector2(0.0f, 5.0f);
            Image trackImage = trackObject.AddComponent<Image>();
            trackImage.sprite = RoundedUiSprite();
            trackImage.type = Image.Type.Sliced;
            trackImage.color = new Color(0.01f, 0.025f, 0.04f, 1.0f);
            trackImage.raycastTarget = false;

            GameObject fillObject = new GameObject("Matrix progress fill",
                typeof(RectTransform));
            fillObject.transform.SetParent(track, false);
            RectTransform fill = fillObject.GetComponent<RectTransform>();
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = new Vector2(5.0f, 5.0f);
            fill.offsetMax = new Vector2(-5.0f, -5.0f);
            desktopMatrixProgressFill = fillObject.AddComponent<Image>();
            desktopMatrixProgressFill.sprite = RoundedUiSprite();
            desktopMatrixProgressFill.type = Image.Type.Filled;
            desktopMatrixProgressFill.fillMethod = Image.FillMethod.Horizontal;
            desktopMatrixProgressFill.fillOrigin = 0;
            desktopMatrixProgressFill.fillAmount = Mathf.Clamp01(shownProgress);
            desktopMatrixProgressFill.color = Amber;
            desktopMatrixProgressFill.raycastTarget = false;

            desktopMatrixProgressText = CreateText(parent, string.Empty,
                32, FontStyle.Bold, new Vector2(0, -58),
                new Vector2(760, 46), TextAnchor.MiddleCenter, Amber);
            UpdateDesktopMatrixProgress();
        }

        private void UpdateDesktopMatrixProgress()
        {
            float shownProgress = resumeMaterializationAfterManifest
                ? Mathf.Max(0.04f, displayedGridProgress)
                : displayedGridProgress;
            if (desktopMatrixProgressFill != null)
                desktopMatrixProgressFill.fillAmount =
                    Mathf.Clamp01(shownProgress);
            if (desktopMatrixProgressText != null)
                desktopMatrixProgressText.text =
                    Mathf.RoundToInt(shownProgress * 100.0f) + "%";
            if (desktopMatrixProgressStageText != null)
                desktopMatrixProgressStageText.text =
                    resumeMaterializationAfterManifest
                        ? "Preparing dataset for MatPlot"
                        : MaterializationStageLabel().ToUpperInvariant();
        }

        private Vector2 DesktopMatrixGridSize(Texture texture, Vector2 maximum)
        {
            int columns = Mathf.Clamp(activeGridColumns, 1,
                MaxFacetAxisBuckets);
            int rows = Mathf.Clamp(activeGridRows, 1,
                MaxFacetAxisBuckets);
            int sourceColumns = Mathf.Max(1,
                activeTimeBuckets != null ? activeTimeBuckets.Length : columns);
            int sourceRows = Mathf.Max(1,
                activeDepthBuckets != null ? activeDepthBuckets.Length : rows);
            float plotAspect = 1.35f;
            if (texture != null && texture.height > 0)
            {
                plotAspect = (texture.width / (float)sourceColumns) /
                    (texture.height / (float)sourceRows);
            }
            plotAspect = Mathf.Clamp(plotAspect, 0.45f, 4.5f);
            float width = maximum.x;
            float cellWidth = width / columns;
            float cellHeight = cellWidth / plotAspect + 62.0f;
            float height = cellHeight * rows;
            if (height > maximum.y)
            {
                height = maximum.y;
                cellHeight = height / rows;
                width = Mathf.Min(maximum.x,
                    columns * Mathf.Max(90.0f,
                        (cellHeight - 62.0f) * plotAspect));
            }
            return new Vector2(Mathf.Max(320.0f, width),
                Mathf.Max(150.0f, height));
        }

        private void SelectS4DGridCell(int column, int row)
        {
            selectedGridColumn = Mathf.Clamp(column, 0, Mathf.Max(0, activeGridColumns - 1));
            selectedGridRow = Mathf.Clamp(row, 0, Mathf.Max(0, activeGridRows - 1));
            gridCellSelected = true;
            BuildMatrixBucketSelections();
            SelectMatrixCell(
                DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow),
                DisplayDepthBucketIndex(selectedGridColumn, selectedGridRow));
            groundMode = GroundMode.Aggregate;
            stage = Stage.Analyze;
            if (!viewState.cubeVisible)
                ToggleDataVisibility();
            if (facetGridCanvas != null)
                facetGridCanvas.gameObject.SetActive(false);
            ReturnToSpatialWorkflow();
            SetGroundDock(true);
            LoadGroundAggregateVolume();
            SetStatus("Ground opened for " + CellLabel(column, row) +
                " using the snapshot footprint.");
            BuildStage();
        }

        private void BuildFacetGenerationOverlay()
        {
            CreatePanelCard(facetGridContent, new Vector2(500, -20),
                new Vector2(350, 350), Amber);
            CreateText(facetGridContent, "MATPLOTAGENT  /  RE-MATERIALIZE",
                13, FontStyle.Bold, new Vector2(500, 126),
                new Vector2(305, 24), TextAnchor.MiddleCenter, Amber);
            facetGridProgressText = CreateText(facetGridContent,
                Mathf.RoundToInt(displayedGridProgress * 100.0f) + "%",
                46, FontStyle.Bold, new Vector2(500, 74),
                new Vector2(300, 72), TextAnchor.MiddleCenter, Ink);
            facetGridProgressStageText = CreateText(facetGridContent,
                MaterializationStageLabel().ToUpperInvariant(), 15,
                FontStyle.Bold, new Vector2(500, 29),
                new Vector2(300, 34), TextAnchor.MiddleCenter, Muted);

            GameObject trackObject = new GameObject("Generation progress track",
                typeof(RectTransform));
            trackObject.transform.SetParent(facetGridContent, false);
            RectTransform track = trackObject.GetComponent<RectTransform>();
            track.sizeDelta = new Vector2(290, 30);
            track.anchoredPosition = new Vector2(500, -12);
            Image trackImage = trackObject.AddComponent<Image>();
            trackImage.sprite = RoundedUiSprite();
            trackImage.type = Image.Type.Sliced;
            trackImage.color = new Color(0.01f, 0.025f, 0.04f, 1.0f);
            trackImage.raycastTarget = false;

            GameObject fillObject = new GameObject("Generation progress fill",
                typeof(RectTransform));
            fillObject.transform.SetParent(track, false);
            RectTransform fill = fillObject.GetComponent<RectTransform>();
            fill.anchorMin = new Vector2(0, 0);
            fill.anchorMax = new Vector2(1, 1);
            fill.offsetMin = new Vector2(4, 4);
            fill.offsetMax = new Vector2(-4, -4);
            facetGridProgressFill = fillObject.AddComponent<Image>();
            facetGridProgressFill.sprite = RoundedUiSprite();
            facetGridProgressFill.type = Image.Type.Filled;
            facetGridProgressFill.fillMethod = Image.FillMethod.Horizontal;
            facetGridProgressFill.fillOrigin = 0;
            facetGridProgressFill.fillAmount = displayedGridProgress;
            facetGridProgressFill.color = Amber;
            facetGridProgressFill.raycastTarget = false;

            int ready = 0;
            for (int i = 0; i < streamingCellTextures.Length; i++)
                if (streamingCellTextures[i] != null)
                    ready++;
            facetGridValidatedText = CreateText(facetGridContent, ready + " / " +
                Mathf.Max(1, activeGridColumns * activeGridRows) +
                " PANELS READY",
                13, FontStyle.Normal, new Vector2(500, -76),
                new Vector2(300, 44), TextAnchor.MiddleCenter, Ink);
            CreateText(facetGridContent,
                "Completed images appear in the empty slots.",
                12, FontStyle.Normal, new Vector2(500, -137),
                new Vector2(300, 46), TextAnchor.MiddleCenter, Muted);
            UpdateFacetGenerationProgress();
        }

        private void UpdateFacetGenerationProgress()
        {
            if (facetGridProgressText != null)
                facetGridProgressText.text =
                    Mathf.RoundToInt(displayedGridProgress * 100.0f) + "%";
            if (facetGridProgressStageText != null)
                facetGridProgressStageText.text =
                    MaterializationStageLabel().ToUpperInvariant();
            if (facetGridProgressFill != null)
                facetGridProgressFill.fillAmount = displayedGridProgress;
            UpdateDesktopMatrixProgress();
        }

        private IEnumerator AnimateGridProgress()
        {
            while (displayedGridProgress + 0.001f < targetGridProgress)
            {
                displayedGridProgress = Mathf.MoveTowards(displayedGridProgress,
                    targetGridProgress, Time.unscaledDeltaTime * 0.42f);
                UpdateFacetGenerationProgress();
                yield return null;
            }
            displayedGridProgress = targetGridProgress;
            UpdateFacetGenerationProgress();
            gridProgressAnimation = null;
        }

        private void ActivateMaterializedLayerResult(
            S4DFacetGridResult result, int variableIndex)
        {
            if (result == null)
                return;
            if (variableIndex >= 0 && variableIndex < datasets.Count)
                selectedDataset = datasets[variableIndex];
            s4dGridImage = result.Panel;
            s4dChartResultJson = result.ChartResultJson;
            s4dSnapshotId = result.SnapshotId;
            s4dJobId = result.JobId;
            ApplyAuthoritativeCellStatistics(result);
            if (result.SharedScale != null)
            {
                s4dSharedMinimum = result.SharedScale.minimum;
                s4dSharedMaximum = result.SharedScale.maximum;
                s4dSharedUnit = result.SharedScale.unit;
            }

            // A multi-variable result is a stack of immutable 3 x 3 snapshots.
            // Refresh Findings for the layer that the user actually brought
            // forward instead of leaving the final variable's digest attached.
            currentDigest = null;
            digestError = string.Empty;
            string requestedJobId = result.JobId;
            S4DDigestResult cachedDigest;
            if (layerDigestCache.TryGetValue(requestedJobId, out cachedDigest))
            {
                currentDigest = cachedDigest;
                SetPending(PendingJob.Digest, false);
                return;
            }
            SetPending(PendingJob.Digest, true);
            VolumeSTCubeS4DAnalysisClient layerDigestClient =
                new VolumeSTCubeS4DAnalysisClient(s4dUrl, 90, 0.5f);
            StartCoroutine(layerDigestClient.GenerateDigest(requestedJobId,
                (digest, error) =>
                {
                    if (!string.Equals(s4dJobId, requestedJobId,
                            StringComparison.Ordinal))
                        return;
                    currentDigest = digest;
                    if (digest != null)
                        layerDigestCache[requestedJobId] = digest;
                    digestError = error ?? string.Empty;
                    SetPending(PendingJob.Digest, false);
                    if (facetGridCanvas != null &&
                        facetGridCanvas.gameObject.activeSelf)
                        BuildFacetGridPanel();
                    if (aiFindingsCanvas != null &&
                        aiFindingsCanvas.gameObject.activeSelf)
                        BuildAiFindingsPanel();
                }));
        }

        private void CreateFacetPreviewCells(RectTransform parent, Vector2 position,
            Vector2 size, bool showProgress)
        {
            int columnCount = Mathf.Clamp(activeGridColumns, 1,
                MaxFacetAxisBuckets);
            int rowCount = Mathf.Clamp(activeGridRows, 1,
                MaxFacetAxisBuckets);
            int cellCount = Mathf.Max(1, columnCount * rowCount);
            float cellWidth = size.x / columnCount;
            float cellHeight = size.y / rowCount;

            for (int row = 0; row < rowCount; row++)
            {
                for (int column = 0; column < columnCount; column++)
                {
                    int timeIndex = activeGridTransposed ? row : column;
                    int depthIndex = activeGridTransposed ? column : row;
                    int index = SourceCellIndex(column, row);
                    bool ready = showProgress && index >= 0 &&
                        index < streamingCellTextures.Length &&
                        streamingCellTextures[index] != null;
                    bool selected = gridCellSelected &&
                        selectedGridColumn == column && selectedGridRow == row;
                    int cellSourceIndex = SourceCellIndex(column, row);
                    bool pinned = facetCellPinned[Mathf.Clamp(
                        cellSourceIndex, 0, facetCellPinned.Length - 1)];
                    Color accent = selected
                        ? Purple
                        : pinned ? Amber
                        : showProgress
                            ? (ready ? Green : Amber)
                            : (intentConfigured ? Purple : Cyan);

                    string timeLabel = ActiveBucketLabel(activeTimeBuckets,
                        timeIndex, new[] { "before", "during", "after" });
                    string depthLabel = ActiveBucketLabel(activeDepthBuckets,
                        depthIndex, new[] { "surface", "middle", "deep" });
                    GameObject cellObject = new GameObject(
                        depthLabel + " x " + timeLabel + " pending panel",
                        typeof(RectTransform));
                    cellObject.layer = 5;
                    cellObject.transform.SetParent(parent, false);
                    RectTransform cell = cellObject.GetComponent<RectTransform>();
                    cell.sizeDelta = new Vector2(cellWidth - 14, cellHeight - 14);
                    cell.anchoredPosition = position + new Vector2(
                        -size.x * 0.5f + cellWidth * (column + 0.5f),
                        size.y * 0.5f - cellHeight * (row + 0.5f));
                    Image pendingBackground = cellObject.AddComponent<Image>();
                    pendingBackground.sprite = RoundedUiSprite();
                    pendingBackground.type = Image.Type.Sliced;
                    pendingBackground.color = new Color(0.015f, 0.033f, 0.050f, 1.0f);
                    Shadow pendingShadow = cellObject.AddComponent<Shadow>();
                    pendingShadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.42f);
                    pendingShadow.effectDistance = new Vector2(3, -4);
                    Outline outline = cellObject.AddComponent<Outline>();
                    outline.effectColor = new Color(accent.r, accent.g, accent.b, 0.52f);
                    outline.effectDistance = new Vector2(1.5f, -1.5f);

                    Texture2D texture = showProgress
                        ? streamingCellTextures[index]
                        : matrixTextures != null && index < matrixTextures.Length
                            ? matrixTextures[index]
                            : null;
                    RawImage streamedImage = null;
                    GameObject waitingPlaceholder = null;
                    if (texture != null)
                    {
                        streamedImage = CreateRawImage(cell, texture, new Vector2(0, -2),
                            new Vector2(cellWidth - 30, cellHeight - 58));
                    }
                    else
                    {
                        // Keep a transparent RawImage in every generation slot
                        // so OnS4DCellReady can stream the completed panel into
                        // this exact card without rebuilding the whole Canvas.
                        if (showProgress)
                            streamedImage = CreateRawImage(cell, null,
                                new Vector2(0, -2),
                                new Vector2(cellWidth - 30, cellHeight - 58));
                        Text waiting = CreateText(cell, showProgress ? "WAITING" :
                            "XY SLICE\nUNAVAILABLE", 12, FontStyle.Bold,
                            new Vector2(0, -2), new Vector2(cellWidth - 30, cellHeight - 58),
                            TextAnchor.MiddleCenter, Muted);
                        waitingPlaceholder = waiting.gameObject;
                    }

                    CreateText(cell,
                        timeLabel.ToUpperInvariant() + "  /  " +
                            depthLabel.ToUpperInvariant(),
                        Mathf.Clamp(15 - columnCount, 9, 12), FontStyle.Bold,
                        new Vector2(0, cellHeight * 0.5f - 18),
                        new Vector2(cellWidth - 30, 24), TextAnchor.MiddleLeft,
                        Color.Lerp(TimeColor, DepthColor, 0.5f));
                    Text stateLabel = CreateText(cell,
                        showProgress
                            ? (ready ? "VALIDATED" : "GENERATING")
                            : IntentDisplayLabel(),
                        10, FontStyle.Bold, new Vector2(0, -cellHeight * 0.5f + 18),
                        new Vector2(cellWidth - 30, 22), TextAnchor.MiddleLeft, accent);

                    if (showProgress && index >= 0 &&
                        index < facetGridCellImages.Length)
                    {
                        facetGridCellImages[index] = streamedImage;
                        facetGridCellPlaceholders[index] = waitingPlaceholder;
                        facetGridCellStateLabels[index] = stateLabel;
                    }

                    int selectedColumn = column;
                    int selectedRow = row;
                    if (selected)
                        selectedFacetCellAnchor = cell;
                    BoxCollider collider = cellObject.AddComponent<BoxCollider>();
                    collider.isTrigger = true;
                    collider.size = new Vector3(cell.sizeDelta.x, cell.sizeDelta.y, 12);
                    bool draftInteraction = parent == facetGridContent &&
                        draftOperation != DraftOperation.None &&
                        spatialWorkflowStep != SpatialWorkflowStep.Materializing;
                    cellObject.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                        draftInteraction
                            ? () => SelectDraftGridCell(selectedColumn, selectedRow)
                            : () => SelectFacetPreviewCell(selectedColumn, selectedRow);
                }
            }
        }

        private void CreateMaterializedFacetCells(RectTransform parent, Texture texture,
            Vector2 position, Vector2 size)
        {
            // Aspect of a single panel inside the committed atlas, so the grid
            // can be laid out without letterboxing every chart.
            float MaterializedCellAspect(Texture source, bool isLayered)
            {
                if (isLayered || source == null || source.height <= 0)
                    return 0.0f;
                int sourceColumns = Mathf.Max(1,
                    activeTimeBuckets != null ? activeTimeBuckets.Length : 3);
                int sourceRows = Mathf.Max(1,
                    activeDepthBuckets != null ? activeDepthBuckets.Length : 3);
                float panelWidth = source.width / (float)sourceColumns;
                float panelHeight = source.height / (float)sourceRows;
                if (panelWidth <= 0.0f || panelHeight <= 0.0f)
                    return 0.0f;
                return Mathf.Clamp(panelWidth / panelHeight, 0.1f, 10.0f);
            }

            int columnCount = Mathf.Clamp(activeGridColumns, 1,
                MaxFacetAxisBuckets);
            int rowCount = Mathf.Clamp(activeGridRows, 1,
                MaxFacetAxisBuckets);
            bool layered = facetGridLayered && parent == facetGridContent;
            bool draftInteraction = parent == facetGridContent &&
                draftOperation != DraftOperation.None;
            // The panel is wide, so a 1 x 3 request used to stack three small
            // charts in one narrow column and leave most of the surface empty.
            // Pick whichever orientation fits the largest chart and read the
            // buckets through the transposed indices.
            bool layoutTransposed = false;
            float FittedCellArea(Vector2 area, int columns, int rows)
            {
                float aspect = MaterializedCellAspect(texture, layered);
                float slotWidth = area.x / Mathf.Max(1, columns);
                float slotHeight = area.y / Mathf.Max(1, rows);
                if (aspect <= 0.01f)
                    return slotWidth * slotHeight;
                float width = Mathf.Min(slotWidth, slotHeight * aspect);
                return width * (width / aspect);
            }
            if (!layered && !draftInteraction && columnCount != rowCount)
            {
                float straightArea = FittedCellArea(size, columnCount, rowCount);
                float swappedArea = FittedCellArea(size, rowCount, columnCount);
                if (swappedArea > straightArea * 1.02f)
                {
                    layoutTransposed = true;
                    int swap = columnCount;
                    columnCount = rowCount;
                    rowCount = swap;
                }
            }
            float cellWidth = size.x / columnCount;
            float cellHeight = layered
                ? Mathf.Min(142.0f, size.y / Mathf.Max(1, rowCount))
                : size.y / rowCount;
            // Every committed atlas panel is rendered at the same aspect. Size
            // the cell to that aspect so the chart fills its panel instead of
            // being letterboxed into the middle of a wide strip.
            float chartAspect = MaterializedCellAspect(texture, layered);
            if (chartAspect > 0.01f)
            {
                if (cellWidth / cellHeight > chartAspect)
                    cellWidth = cellHeight * chartAspect;
                else
                    cellHeight = cellWidth / chartAspect;
            }
            float gridWidth = cellWidth * columnCount;
            float gridHeight = cellHeight * rowCount;
            facetGridCellSize = new Vector2(cellWidth, cellHeight);
            facetGridCellOrigin = position;
            int labelFontSize = columnCount > 6 ? 8 :
                columnCount > 3 ? 9 : 11;
            int stateFontSize = columnCount > 6 ? 7 :
                columnCount > 3 ? 8 : 10;
            if (draftInteraction)
            {
                labelFontSize += 2;
                stateFontSize += 2;
            }

            for (int row = 0; row < rowCount; row++)
            {
                if (layered && row < facetGridPeeledLayers)
                    continue;
                for (int column = 0; column < columnCount; column++)
                {
                    // Buckets are read through the transposed indices so the
                    // images stay attached to the right time / depth pair.
                    int selectedColumn = layoutTransposed ? row : column;
                    int selectedRow = layoutTransposed ? column : row;
                    bool selected = gridCellSelected &&
                        selectedGridColumn == selectedColumn &&
                        selectedGridRow == selectedRow;
                    int cellSourceIndex = SourceCellIndex(
                        selectedColumn, selectedRow);
                    bool pinned = facetCellPinned[Mathf.Clamp(
                        cellSourceIndex, 0, facetCellPinned.Length - 1)];
                    int timeIndex = DisplayTimeBucketIndex(
                        selectedColumn, selectedRow);
                    int depthIndex = DisplayDepthBucketIndex(
                        selectedColumn, selectedRow);
                    string timeLabel = ActiveBucketLabel(activeTimeBuckets, timeIndex,
                        new[] { "before", "during", "after" });
                    string depthLabel = ActiveBucketLabel(activeDepthBuckets, depthIndex,
                        new[] { "surface", "middle", "deep" });
                    GameObject cellObject = new GameObject(
                        depthLabel + " x " + timeLabel + " panel",
                        typeof(RectTransform));
                    cellObject.layer = 5;
                    cellObject.transform.SetParent(parent, false);
                    RectTransform cell = cellObject.GetComponent<RectTransform>();
                    cell.sizeDelta = new Vector2(cellWidth - 12, cellHeight - 12);
                    if (layered)
                    {
                        int visibleLayer = row - facetGridPeeledLayers;
                        float layerX = visibleLayer * 28.0f;
                        float layerY = size.y * 0.31f - visibleLayer * 142.0f;
                        cell.anchoredPosition = position + new Vector2(
                            -size.x * 0.5f + cellWidth * (column + 0.5f) + layerX,
                            layerY);
                        Vector3 local = cell.localPosition;
                        local.z = visibleLayer * 24.0f;
                        cell.localPosition = local;
                        cell.localRotation = Quaternion.Euler(7.0f, -3.0f, 0.0f);
                    }
                    else
                    {
                        cell.anchoredPosition = position + new Vector2(
                            -gridWidth * 0.5f + cellWidth * (column + 0.5f),
                            gridHeight * 0.5f - cellHeight * (row + 0.5f));
                    }
                    Image materializedBackground = cellObject.AddComponent<Image>();
                    materializedBackground.sprite = RoundedUiSprite();
                    materializedBackground.type = Image.Type.Sliced;
                    materializedBackground.color =
                        new Color(0.015f, 0.030f, 0.046f, 1.0f);
                    Shadow materializedShadow = cellObject.AddComponent<Shadow>();
                    materializedShadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.46f);
                    materializedShadow.effectDistance = new Vector2(3, -4);
                    Outline outline = cellObject.AddComponent<Outline>();
                    bool stale = facetCellStale[Mathf.Clamp(
                        cellSourceIndex, 0, facetCellStale.Length - 1)];
                    bool suspect = facetCellBoundarySuspect[Mathf.Clamp(
                        cellSourceIndex, 0, facetCellBoundarySuspect.Length - 1)];
                    bool verified = facetCellInspected[Mathf.Clamp(
                        cellSourceIndex, 0, facetCellInspected.Length - 1)];
                    Color accent = stale || suspect ? Amber : verified ? Green : selected
                        ? Purple
                        : pinned ? Amber : Cyan;
                    outline.effectColor = new Color(accent.r, accent.g, accent.b,
                        selected ? 0.86f : 0.44f);
                    outline.effectDistance = new Vector2(selected ? 2.5f : 1.5f,
                        selected ? -2.5f : -1.5f);

                    Texture2D streamedTexture = texture == null &&
                        cellSourceIndex >= 0 &&
                        cellSourceIndex < streamingCellTextures.Length
                            ? streamingCellTextures[cellSourceIndex]
                            : null;
                    if (texture != null || streamedTexture != null)
                    {
                        GameObject viewportObject = new GameObject(
                            "Cell chart viewport", typeof(RectTransform));
                        viewportObject.transform.SetParent(cell, false);
                        RectTransform viewportRect =
                            viewportObject.GetComponent<RectTransform>();
                        viewportRect.sizeDelta = new Vector2(
                            Mathf.Max(28, cellWidth - 20),
                            Mathf.Max(24, cellHeight - 62));
                        viewportRect.anchoredPosition = Vector2.zero;

                        GameObject imageObject = new GameObject(
                            "Cell chart", typeof(RectTransform));
                        imageObject.transform.SetParent(viewportRect, false);
                        RectTransform imageRect =
                            imageObject.GetComponent<RectTransform>();
                        imageRect.anchorMin = Vector2.zero;
                        imageRect.anchorMax = Vector2.one;
                        imageRect.offsetMin = Vector2.zero;
                        imageRect.offsetMax = Vector2.zero;
                        RawImage image = imageObject.AddComponent<RawImage>();
                        image.texture = texture != null ? texture : streamedTexture;
                        image.color = Color.white;
                        image.raycastTarget = false;
                        if (cellSourceIndex >= 0 &&
                            cellSourceIndex < facetGridCellImages.Length)
                            facetGridCellImages[cellSourceIndex] = image;
                        if (texture != null)
                        {
                            int sourceColumns = Mathf.Max(1,
                                activeTimeBuckets != null
                                    ? activeTimeBuckets.Length : 3);
                            int sourceRows = Mathf.Max(1,
                                activeDepthBuckets != null
                                    ? activeDepthBuckets.Length : 3);
                            image.uvRect = new Rect(
                                timeIndex / (float)sourceColumns,
                                (sourceRows - 1 - depthIndex) /
                                    (float)sourceRows,
                                1.0f / sourceColumns,
                                1.0f / sourceRows);
                            AspectRatioFitter fitter =
                                imageObject.AddComponent<AspectRatioFitter>();
                            fitter.aspectMode =
                                AspectRatioFitter.AspectMode.FitInParent;
                            fitter.aspectRatio = Mathf.Clamp(
                                (texture.width / (float)sourceColumns) /
                                (texture.height / (float)sourceRows),
                                0.1f, 10.0f);
                        }
                        else if (streamedTexture != null &&
                            streamedTexture.height > 0)
                        {
                            AspectRatioFitter fitter =
                                imageObject.AddComponent<AspectRatioFitter>();
                            fitter.aspectMode =
                                AspectRatioFitter.AspectMode.FitInParent;
                            fitter.aspectRatio = Mathf.Clamp(
                                streamedTexture.width /
                                    (float)streamedTexture.height,
                                0.1f, 10.0f);
                        }
                    }
                    else
                    {
                        GameObject imageObject = new GameObject(
                            "Cell chart placeholder", typeof(RectTransform));
                        imageObject.transform.SetParent(cell, false);
                        RectTransform imageRect =
                            imageObject.GetComponent<RectTransform>();
                        imageRect.sizeDelta = new Vector2(
                            Mathf.Max(28, cellWidth - 20),
                            Mathf.Max(24, cellHeight - 55));
                        imageRect.anchoredPosition = new Vector2(0, 3);
                        RawImage placeholder = imageObject.AddComponent<RawImage>();
                        placeholder.color = Color.clear;
                        placeholder.raycastTarget = false;
                        if (cellSourceIndex >= 0 &&
                            cellSourceIndex < facetGridCellImages.Length)
                            facetGridCellImages[cellSourceIndex] = placeholder;
                        Text computingLabel = CreateText(cell, IsPending(PendingJob.Analysis)
                                ? "MATPLOTAGENT\nCOMPUTING"
                                : "PANEL\nUNAVAILABLE",
                            stateFontSize, FontStyle.Bold, new Vector2(0, 3),
                            new Vector2(Mathf.Max(30, cellWidth - 16),
                                Mathf.Max(24, cellHeight - 55)),
                            TextAnchor.MiddleCenter,
                            IsPending(PendingJob.Analysis) ? Amber : Muted);
                        if (cellSourceIndex >= 0 &&
                            cellSourceIndex < facetGridCellPlaceholders.Length)
                            facetGridCellPlaceholders[cellSourceIndex] =
                                computingLabel.gameObject;
                    }

                    CreateText(cell, depthLabel + "  x  " + timeLabel,
                        labelFontSize, FontStyle.Bold,
                        new Vector2(0, cellHeight * 0.5f - 14),
                        new Vector2(Mathf.Max(30, cellWidth - 16), 20),
                        TextAnchor.MiddleLeft,
                        Color.Lerp(TimeColor, DepthColor, 0.5f));
                    int draftTick = draftTargetDimension == 0
                        ? timeIndex : depthIndex;
                    bool draftTickMarked = draftInteraction &&
                        draftOperation != DraftOperation.Pivot &&
                        draftTick >= 0 && draftTick < MaxFacetAxisBuckets &&
                        (draftTargetDimension == 0
                            ? selectedTimeTicks[draftTick]
                            : selectedDepthTicks[draftTick]);
                    Text stateLabel = CreateText(cell,
                        texture == null && streamedTexture == null && IsPending(PendingJob.Analysis)
                            ? "GENERATING" :
                        draftInteraction && draftOperation == DraftOperation.Pivot
                            ? "CURRENT LAYOUT" :
                        draftInteraction && draftOperation == DraftOperation.Drill &&
                            draftTickMarked ? "EXPAND INTO 3" :
                        draftInteraction && draftOperation == DraftOperation.Drill
                            ? "SELECT TO EXPAND" :
                        draftInteraction && draftOperation == DraftOperation.RollUp &&
                            draftTickMarked ? "MERGE SELECTED" :
                        draftInteraction && draftOperation == DraftOperation.RollUp
                            ? "SELECT NEIGHBOUR" :
                        stale ? "STALE / RE-MATERIALIZE" :
                        suspect ? "BOUNDARY SUSPECT" :
                        verified ? "BOUNDARY VERIFIED" :
                        selected ? "SELECTED" :
                        pinned ? "PINNED" :
                        "CLICK TO INSPECT",
                        stateFontSize, FontStyle.Bold,
                        new Vector2(0, -cellHeight * 0.5f + 17),
                        new Vector2(Mathf.Max(30, cellWidth - 16), 20),
                        TextAnchor.MiddleCenter, accent);
                    if (cellSourceIndex >= 0 &&
                        cellSourceIndex < facetGridCellStateLabels.Length)
                        facetGridCellStateLabels[cellSourceIndex] = stateLabel;
                    if (selected)
                        selectedFacetCellAnchor = cell;
                    BoxCollider collider = cellObject.AddComponent<BoxCollider>();
                    collider.isTrigger = true;
                    collider.size = new Vector3(cell.sizeDelta.x, cell.sizeDelta.y, 12);
                    cellObject.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                        draftInteraction
                            ? () => SelectDraftGridCell(selectedColumn, selectedRow)
                            : () => SelectFacetPreviewCell(selectedColumn, selectedRow);
                }

                if (layered)
                {
                    int visibleLayer = row - facetGridPeeledLayers;
                    int depthIndex = activeGridTransposed ? 0 : row;
                    string layerLabel = ActiveBucketLabel(activeDepthBuckets, depthIndex,
                        new[] { "surface", "middle", "deep" });
                    CreateText(parent,
                        "Z  " + layerLabel.ToUpperInvariant() +
                        "  /  LAYER " + (row + 1),
                        11, FontStyle.Bold,
                        position + new Vector2(-size.x * 0.5f + 18 + visibleLayer * 28.0f,
                            size.y * 0.31f - visibleLayer * 142.0f),
                        new Vector2(135, 30), TextAnchor.MiddleLeft, DepthAxisColor);
                }
            }
        }

        private void BeginGridPlacement()
        {
            if (!AreSpatialAxisBindingsComplete(out string missing))
            {
                SetStatus("Full Matrix is locked: " + missing);
                return;
            }
            if (!authorBoundaryConfirmed)
            {
                SetStatus("Full Matrix is locked until Time and Depth are confirmed.");
                return;
            }
            if (!intentConfigured)
            {
                SetStatus("Full Matrix is locked until MatPlot Intent is resolved.");
                return;
            }
            BuildS4DGridRequest();
            spatialWorkflowStep = SpatialWorkflowStep.SourcePreviewReady;
            // FULL MATRIX is the user's commit action.  The matrix already has a
            // stable dock position, so an extra placement-preview confirmation
            // only duplicates the choice and interrupts the analysis flow.
            // Enter materialization immediately and let the normal Matrix panel
            // report progress/results once the job starts.
            ConfirmGridPlacement();
        }

        private void ContinueGridPlacement()
        {
            placementConfirmed = false;
            stage = Stage.Matrix;
            SetStatus("Placement preview ready. Grip to adjust, trigger to confirm.");
            if (facetGridCanvas != null)
            {
                HidePrimaryToolsExcept(facetGridCanvas);
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
            }
            BuildStage();
        }

        private void ConfirmGridPlacement()
        {
            if (spatialWorkflowStep != SpatialWorkflowStep.SourcePreviewReady ||
                IsPending(PendingJob.Analysis))
            {
                SetStatus(IsPending(PendingJob.Analysis)
                    ? "MatPlotAgent is already materializing this Grid."
                    : "Confirm the source preview before materializing Full Matrix.");
                return;
            }
            placementConfirmed = true;
            spatialWorkflowStep = SpatialWorkflowStep.Materializing;
            SetStatus("Grid anchored. Starting immutable snapshot materialization.");
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
            {
                // Enter the Matrix task surface before manifest resolution.
                // Otherwise the pending grid is rendered behind Intent while
                // the asynchronous resolver is still keeping us in Step 3.
                stage = Stage.Matrix;
                ShowPrimaryTool(panelCanvas);
                BuildStage();
                VolumeSTCubeFlatScreenHUD.NotifyWorkflowChanged();
            }
            else if (facetGridCanvas != null)
            {
                HidePrimaryToolsExcept(facetGridCanvas);
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
            }
            StartS4DGridJob();
        }

        private void BuildMatrixTextures()
        {
            DestroyTextures(matrixTextures);
            int timeBucketCount = Mathf.Max(1,
                activeTimeBuckets != null ? activeTimeBuckets.Length : 1);
            int depthBucketCount = Mathf.Max(1,
                activeDepthBuckets != null ? activeDepthBuckets.Length : 1);
            int cellCount = Mathf.Clamp(timeBucketCount * depthBucketCount,
                1, MaxFacetCells);
            matrixTextures = new Texture2D[cellCount];
            if (selectedDataset == null || activeTimeBuckets == null ||
                activeDepthBuckets == null)
                return;
            UpdateActiveRepresentativeIndices();
            try
            {
                for (int row = 0; row < depthBucketCount; row++)
                {
                    VolumeSTCubeSliceDataset cellDataset =
                        DatasetForBucket(activeDepthBuckets[row]);
                    int z = Mathf.Clamp(RepresentativeIndex(
                        activeDepthBuckets[row].indices), 0,
                        Mathf.Max(0, cellDataset.DimZ - 1));
                    for (int column = 0; column < timeBucketCount; column++)
                    {
                        int time = Mathf.Clamp(RepresentativeIndex(
                            activeTimeBuckets[column].indices), 0,
                            Mathf.Max(0, cellDataset.TimeCount - 1));
                        VolumeSTCubeRawSlice slice = VolumeSTCubeRawSliceReader.ReadSlice(
                            cellDataset.RawPaths[time],
                            cellDataset.IniPaths[time], z);
                        int index = row * timeBucketCount + column;
                        matrixTextures[index] = VolumeSTCubeRawSliceReader.CreatePreviewTexture(slice, 260, 92);
                        matrixMinimums[index] = slice.Minimum;
                        matrixMaximums[index] = slice.Maximum;
                        double sum = 0.0;
                        if (slice.Values != null)
                            for (int valueIndex = 0; valueIndex < slice.Values.Length; valueIndex++)
                                sum += slice.Values[valueIndex];
                        matrixMeans[index] = slice.Values != null && slice.Values.Length > 0
                            ? (float)(sum / slice.Values.Length)
                            : 0.0f;
                    }
                }
                SetStatus("Slab preview materialized: " + activeGridColumns +
                    " x " + activeGridRows + " real XY panels for " +
                    FacetAxisSummary() + ".");
            }
            catch (Exception exception)
            {
                SetStatus("Fill-Matrix failed: " + exception.Message);
            }
        }

        private void SelectMatrixCell(int column, int row)
        {
            selectedTime = matrixTimes[Mathf.Clamp(column, 0,
                matrixTimes.Length - 1)];
            selectedZ = matrixDepths[Mathf.Clamp(row, 0,
                matrixDepths.Length - 1)];
            slabNormalized = selectedDataset.DimZ > 1 ? selectedZ / (float)(selectedDataset.DimZ - 1) : 0.5f;
            ApplyTimeFilter();
            UpdateSlabVisual(false);
            RefreshSlabTexture();
            RebuildTimeMarkers();
            SetStatus("Grounded matrix cell: " + selectedDataset.GetTimeLabel(selectedTime) + ", z=" + selectedZ + ".");
            BuildStage();
        }
    }
}
