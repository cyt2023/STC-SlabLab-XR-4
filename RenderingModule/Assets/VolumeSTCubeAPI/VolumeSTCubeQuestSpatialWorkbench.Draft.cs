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
        private void CreateDraftPanel()
        {
            draftCanvas = CreateFloatingCanvas(
                "Spatial Analysis Draft",
                DraftToolDockPosition,
                new Vector2(760, 270),
                0.00062f,
                Purple);
            draftContent = draftCanvas.GetComponent<RectTransform>();
            draftCanvas.sortingOrder = 124;
            draftCanvas.gameObject.SetActive(false);
        }

        private void BuildSpatialDraftPanel()
        {
            if (draftContent == null || draftOperation == DraftOperation.None)
                return;
            ClearChildren(draftContent);
            Color accent = OperationColor(draftOperation);
            string title = draftOperation == DraftOperation.Pivot
                ? "PIVOT"
                : draftOperation == DraftOperation.Drill
                    ? "DRILL"
                    : "ROLL-UP";
            CreateText(draftContent, title,
                28, FontStyle.Bold, new Vector2(-150, 96), new Vector2(400, 40),
                TextAnchor.MiddleLeft, Ink);
            CreateText(draftContent,
                draftOperation == DraftOperation.Pivot
                    ? "CLICK THE GRID TO SWAP AXES"
                    : draftOperation == DraftOperation.Drill
                        ? "CLICK A ROW OR COLUMN TO EXPAND"
                        : "CLICK ADJACENT CELLS TO MERGE",
                14, FontStyle.Bold, new Vector2(90, 96), new Vector2(460, 30),
                TextAnchor.MiddleRight, accent);

            if (draftOperation == DraftOperation.Pivot)
            {
                CreateText(draftContent,
                    pivotTransposed ? "DEPTH ACROSS  /  TIME DOWN" :
                        "TIME ACROSS  /  DEPTH DOWN",
                    16, FontStyle.Bold, new Vector2(0, 34),
                    new Vector2(690, 30), TextAnchor.MiddleCenter, Ink);
            }
            else
            {
                CreateButton(draftContent,
                    "TIME", new Vector2(-105, 34), SlabLabLayout.ButtonSizeMedium,
                    draftTargetDimension == 0 ? TimeColor : Card,
                    () => SetDraftTargetDimension(0));
                CreateButton(draftContent,
                    "DEPTH", new Vector2(105, 34), SlabLabLayout.ButtonSizeMedium,
                    draftTargetDimension == 1 ? DepthColor : Card,
                    () => SetDraftTargetDimension(1));
            }

            bool ready = DraftSelectionReady(out string selectionHint);
            CreateText(draftContent, ready ? DraftGridSummary() : selectionHint,
                13, FontStyle.Bold, new Vector2(-55, -18), new Vector2(570, 28),
                TextAnchor.MiddleLeft, ready ? Muted : Danger);
            CreateButton(draftContent, "CANCEL", new Vector2(-205, -91),
                new Vector2(250, 48), Card, CancelDraft);
            CreateButton(draftContent, "APPLY",
                new Vector2(155, -91), new Vector2(420, 48),
                ready ? accent : Card, ConfirmDraftAndGenerate);
        }

        private void ConfirmDraftAndGenerate()
        {
            if (!DraftSelectionReady(out string hint))
            {
                SetStatus(hint);
                if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                    BuildFacetGridPanel();
                return;
            }
            // A bucket operation creates a new analytical branch. Preserve the
            // source matrix as an immutable world-space result before the live
            // Facet Grid is cleared and reused for the new MatPlotAgent job.
            // This makes the before/after relationship visible instead of
            // replacing the evidence the user operated on.
            RetainCurrentResultBesideLiveGrid();
            // Pivot commits the copied Slab's role changes only now. Cancelling
            // the draft therefore leaves both the source Slab and the active
            // axis composer untouched.
            ApplyPivotDraftConfiguration();
            // These operations transform an existing materialized result, so
            // reuse its resolved chart intent and start the new Grid directly.
            if (!intentConfigured)
            {
                AnalysisNodeState source = FindAnalysisNode(draftSourceNodeId) ??
                    currentAnalysisNode;
                if (source != null)
                {
                    if (!string.IsNullOrWhiteSpace(source.rawIntent))
                        prompt = source.rawIntent;
                    if (!string.IsNullOrWhiteSpace(source.analyticTask))
                        intentTask = source.analyticTask;
                    if (!string.IsNullOrWhiteSpace(source.intentDisplayLabel))
                        intentMode = source.intentDisplayLabel;
                    intentConfigured = source.hasResolvedIntent ||
                        !string.IsNullOrWhiteSpace(source.rawIntent);
                }
            }
            if (!intentConfigured)
            {
                // A transformed result must never reopen the composer. When a
                // legacy node lacks persisted intent metadata, retain the
                // current chart wording and use the safe distribution task.
                if (string.IsNullOrWhiteSpace(prompt))
                    prompt = "Recreate the transformed comparison using the current chart style.";
                if (string.IsNullOrWhiteSpace(intentTask))
                    intentTask = "distribution";
                if (string.IsNullOrWhiteSpace(intentMode))
                    intentMode = "DISTRIBUTION";
                intentConfigured = true;
            }
            BuildS4DGridRequest();
            placementConfirmed = true;
            spatialWorkflowStep = SpatialWorkflowStep.Materializing;
            stage = Stage.Matrix;
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
            {
                // Generation is the active task now. Bring its progress panel
                // to the primary lane even when the user started Drill/Pivot/
                // Roll-up while a Field was enlarged.
                SetDesktopFocusView(DesktopFocusView.SlabAxis, true);
            }
            materializationVariableCursor = -1;
            // Clear every visual trace of the source result before rebuilding
            // the generation view. StartS4DGridJob also initializes these
            // values, but the panel is intentionally rebuilt once before that
            // call so the transition feels immediate.
            DestroyTextures(streamingCellTextures);
            progress = 0.0f;
            displayedGridProgress = 0.0f;
            targetGridProgress = 0.0f;
            if (intentCanvas != null)
                intentCanvas.gameObject.SetActive(false);
            if (draftCanvas != null)
                draftCanvas.gameObject.SetActive(false);
            SetStatus(draftOperation +
                " confirmed. Starting MatPlotAgent generation...");
            if (facetGridCanvas != null)
            {
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
            }
            StartS4DGridJob();
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled &&
                resumeMaterializationAfterManifest)
            {
                // Resolving dataset metadata is asynchronous. Move to the
                // Matrix progress task immediately so FULL MATRIX never looks
                // like an unresponsive click while that request is pending.
                stage = Stage.Matrix;
                ShowPrimaryTool(panelCanvas);
                BuildStage();
                VolumeSTCubeFlatScreenHUD.NotifyWorkflowChanged();
            }
        }

        private bool DraftSelectionReady(out string hint)
        {
            hint = string.Empty;
            if (draftOperation == DraftOperation.None ||
                draftOperation == DraftOperation.Pivot)
                return true;
            if (draftTargetDimension < 0 || draftTargetDimension > 1 ||
                roles[draftTargetDimension] != DimensionRole.Faceted)
            {
                hint = "CHOOSE A FACETED TIME OR DEPTH AXIS";
                return false;
            }
            bool[] selected = draftTargetDimension == 0
                ? selectedTimeTicks : selectedDepthTicks;
            if (draftOperation == DraftOperation.Drill)
            {
                if (CountSelectedTicks(selected) > 0)
                    return true;
                hint = "SELECT AT LEAST ONE BUCKET TO OPEN";
                return false;
            }
            int[] groups = draftTargetDimension == 0
                ? timeRollupGroups : depthRollupGroups;
            int count = DraftBucketCount(draftTargetDimension);
            for (int index = 1; index < count; index++)
            {
                if (groups[index] > 0 && groups[index] == groups[index - 1])
                    return true;
            }
            hint = "MARK TWO ADJACENT BUCKETS WITH THE SAME GROUP";
            return false;
        }

        private string DraftGridSummary()
        {
            S4DIndexBucketRequest[] time = DraftSourceBuckets(0);
            S4DIndexBucketRequest[] depth = DraftSourceBuckets(1);
            int oldTime = time != null ? Mathf.Max(1, time.Length) : 1;
            int oldDepth = depth != null ? Mathf.Max(1, depth.Length) : 1;
            int nextTime = oldTime;
            int nextDepth = oldDepth;
            if (draftOperation == DraftOperation.Drill)
            {
                if (draftTargetDimension == 0 && time != null)
                    nextTime = ExpandSelectedBuckets(time,
                        selectedTimeTicks, "preview_time").Length;
                else if (draftTargetDimension == 1 && depth != null)
                    nextDepth = ExpandSelectedBuckets(depth,
                        selectedDepthTicks, "preview_depth").Length;
            }
            else if (draftOperation == DraftOperation.RollUp)
            {
                if (draftTargetDimension == 0 && time != null)
                    nextTime = MergeBucketGroups(time,
                        timeRollupGroups, "preview_time").Length;
                else if (draftTargetDimension == 1 && depth != null)
                    nextDepth = MergeBucketGroups(depth,
                        depthRollupGroups, "preview_depth").Length;
            }
            int oldColumns = activeGridTransposed ? oldDepth : oldTime;
            int oldRows = activeGridTransposed ? oldTime : oldDepth;
            int nextColumns = pivotTransposed ? nextDepth : nextTime;
            int nextRows = pivotTransposed ? nextTime : nextDepth;
            string direction = pivotTransposed
                ? "DEPTH ACROSS · TIME DOWN"
                : "TIME ACROSS · DEPTH DOWN";
            return oldColumns + " × " + oldRows + "  →  " +
                nextColumns + " × " + nextRows + " PANELS   ·   " + direction;
        }

        private void OpenDraftFromGrid()
        {
            SetFacetSelectionEvidencePreview(false);
            BeginDraft(DraftOperation.Pivot);
        }

        private void BuildDraftTickBlockControls()
        {
            bool drill = draftOperation == DraftOperation.Drill;
            Color accent = drill ? TimeColor : Green;
            CreatePanelCard(panelContent, new Vector2(0, -91),
                new Vector2(1010, 150), accent);
            CreateText(panelContent,
                drill ? "SELECT ONE OR MORE PARENT TICK-BLOCKS" :
                    "ASSIGN ADJACENT BLOCKS TO ROLL-UP GROUPS",
                13, FontStyle.Bold, new Vector2(-310, -39),
                new Vector2(390, 22),
                TextAnchor.MiddleLeft, accent);
            CreateButton(panelContent, "TIME", new Vector2(185, -39),
                SlabLabLayout.RowChipSize,
                draftTargetDimension == 0 ? TimeColor : Card,
                () => SetDraftTargetDimension(0));
            CreateButton(panelContent, "DEPTH", new Vector2(355, -39),
                SlabLabLayout.RowChipSize,
                draftTargetDimension == 1 ? DepthColor : Card,
                () => SetDraftTargetDimension(1));
            CreateTrackPreview(panelContent, new Vector2(0, -83),
                draftTargetDimension);
            if (drill)
            {
                CreateText(panelContent,
                    "Selected parents expand into three children each; unselected buckets remain unchanged.",
                    10, FontStyle.Normal, new Vector2(0, -132),
                    new Vector2(930, 20), TextAnchor.MiddleCenter, Muted);
            }
            else
            {
                CreateText(panelContent, "ACTIVE GROUP", 10, FontStyle.Bold,
                    new Vector2(-330, -132), new Vector2(150, 20),
                    TextAnchor.MiddleLeft, Muted);
                CreateButton(panelContent, "GROUP 1", new Vector2(-105, -132),
                    SlabLabLayout.StepperButtonSize,
                    activeRollupGroup == 1 ? Green : Card,
                    () => SelectRollupGroup(1));
                CreateButton(panelContent, "GROUP 2", new Vector2(45, -132),
                    SlabLabLayout.StepperButtonSize,
                    activeRollupGroup == 2 ? Purple : Card,
                    () => SelectRollupGroup(2));
                CreateButton(panelContent, "GROUP 3", new Vector2(195, -132),
                    SlabLabLayout.StepperButtonSize,
                    activeRollupGroup == 3 ? Amber : Card,
                    () => SelectRollupGroup(3));
                CreateButton(panelContent, "ERASE", new Vector2(345, -132),
                    SlabLabLayout.StepperButtonSize,
                    activeRollupGroup == 0 ? Danger : Card,
                    () => SelectRollupGroup(0));
            }
        }

        private void SetDraftTargetDimension(int dimension)
        {
            if (dimension < 0 || dimension > 1 ||
                roles[dimension] != DimensionRole.Faceted)
            {
                SetStatus("Drill and Roll-up are available only on a Faceted dimension.");
                return;
            }
            if (draftTargetDimension == dimension)
                return;
            draftTargetDimension = dimension;
            ClearDraftTickSelections();
            if (draftOperation == DraftOperation.Drill)
            {
                int count = DraftBucketCount(dimension);
                (dimension == 0 ? selectedTimeTicks : selectedDepthTicks)[
                    Mathf.Clamp(count / 2, 0, MaxFacetAxisBuckets - 1)] = true;
            }
            else if (draftOperation == DraftOperation.RollUp)
            {
                int[] groups = dimension == 0
                    ? timeRollupGroups : depthRollupGroups;
                bool[] ticks = dimension == 0
                    ? selectedTimeTicks : selectedDepthTicks;
                for (int index = 0;
                    index < Mathf.Min(2, DraftBucketCount(dimension)); index++)
                {
                    groups[index] = 1;
                    ticks[index] = true;
                }
            }
            SetStatus((dimension == 0 ? "Time" : "Depth") +
                " selected as the operation target.");
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
        }

        private void ToggleDraftTick(int dimension, int tick)
        {
            if (tick < 0 || tick >= DraftBucketCount(dimension) ||
                dimension != draftTargetDimension)
                return;
            bool[] ticks = dimension == 0 ? selectedTimeTicks : selectedDepthTicks;
            bool[] otherTicks = dimension == 0 ? selectedDepthTicks : selectedTimeTicks;
            SetAllTicks(otherTicks, false);
            if (draftOperation == DraftOperation.RollUp)
            {
                int[] groups = dimension == 0
                    ? timeRollupGroups : depthRollupGroups;
                if (groups[tick] > 0)
                {
                    groups[tick] = 0;
                }
                else
                {
                    int first = -1;
                    int last = -1;
                    for (int index = 0; index < DraftBucketCount(dimension); index++)
                    {
                        if (groups[index] <= 0)
                            continue;
                        if (first < 0)
                            first = index;
                        last = index;
                    }
                    if (first >= 0 && tick != first - 1 && tick != last + 1)
                    {
                        SetStatus("Roll-up only merges neighbouring buckets. Select next to the green outline.");
                        return;
                    }
                    groups[tick] = 1;
                }
                ticks[tick] = groups[tick] > 0;
            }
            else
            {
                ticks[tick] = !ticks[tick];
            }
            string operation = draftOperation == DraftOperation.Drill ? "drill" :
                draftOperation == DraftOperation.RollUp ? "roll-up" : "selection";
            string groupSuffix = string.Empty;
            SetStatus((ticks[tick] ? "Added to " : "Removed from ") + operation +
                groupSuffix + ": " + (dimension == 0 ? "Time" : "Depth") +
                " tick " + (tick + 1) + ".");
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
        }

        private void SelectDraftGridCell(int column, int row)
        {
            if (draftOperation == DraftOperation.None)
                return;
            if (draftOperation == DraftOperation.Pivot)
            {
                return;
            }
            int tick = draftTargetDimension == 0
                ? DisplayTimeBucketIndex(column, row)
                : DisplayDepthBucketIndex(column, row);
            ToggleDraftTick(draftTargetDimension, tick);
        }

        private void RotatePivotMatrix()
        {
            SetPivotOrientation(!pivotTransposed);
        }

        private void BeginDraftPivotPreviewDrag()
        {
            if (draftOperation != DraftOperation.Pivot ||
                draftPivotPreviewRoot == null || rayInteractor == null ||
                !TryGetFacetGridPointerLocal(out Vector2 pointer))
                return;
            Vector2 delta = pointer - draftPivotPreviewRoot.anchoredPosition;
            draftPivotPreviewStartPointerAngle = Mathf.Atan2(delta.y, delta.x) *
                Mathf.Rad2Deg;
            draftPivotPreviewVisualAngle = 0.0f;
            interaction.draftPivotPreviewDragging = true;
            SetStatus("Rotate the preview a quarter turn, then release to swap Time and Depth.");
        }

        private void UpdateDraftPivotPreviewDrag()
        {
            if (!interaction.draftPivotPreviewDragging)
                return;
            if (draftOperation != DraftOperation.Pivot ||
                draftPivotPreviewRoot == null || rayInteractor == null)
            {
                interaction.draftPivotPreviewDragging = false;
                return;
            }
            if (rayInteractor.TriggerHeld &&
                TryGetFacetGridPointerLocal(out Vector2 pointer))
            {
                Vector2 delta = pointer - draftPivotPreviewRoot.anchoredPosition;
                float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
                draftPivotPreviewVisualAngle = Mathf.Clamp(
                    Mathf.DeltaAngle(draftPivotPreviewStartPointerAngle, angle),
                    -95.0f, 95.0f);
                draftPivotPreviewRoot.localRotation = Quaternion.Euler(
                    0, 0, draftPivotPreviewVisualAngle);
                return;
            }
            if (!rayInteractor.TriggerReleased && rayInteractor.TriggerHeld)
                return;
            bool commit = Mathf.Abs(draftPivotPreviewVisualAngle) >= 32.0f;
            interaction.draftPivotPreviewDragging = false;
            if (commit)
            {
                pivotTransposed = !pivotTransposed;
                SetStatus(pivotTransposed
                    ? "Pivot ready: Depth across, Time down."
                    : "Pivot ready: Time across, Depth down.");
                BuildFacetGridPanel();
            }
            else if (draftPivotPreviewRoot != null)
            {
                draftPivotPreviewVisualAngle = 0.0f;
                draftPivotPreviewRoot.localRotation = Quaternion.identity;
            }
        }

        private void BuildDraftGridInteractionOverlay()
        {
            if (facetGridContent == null || draftOperation == DraftOperation.None)
                return;
            Color accent = OperationColor(draftOperation);
            // Keep one generous header band across Pivot, Drill and Roll-up.
            // The result matrix and its outcome preview share the same lower
            // visual baseline, leaving operation labels clear and readable.
            int columns = Mathf.Clamp(activeGridColumns, 1,
                MaxFacetAxisBuckets);
            int rows = Mathf.Clamp(activeGridRows, 1,
                MaxFacetAxisBuckets);
            // Follow the size the cards were actually given: the atlas is
            // aspect-fitted inside its slot, so equal-slot maths drew outlines
            // that no longer hugged the images.
            bool haveLayout = facetGridCellSize.x > 1.0f &&
                facetGridCellSize.y > 1.0f;
            Vector2 cellSize = haveLayout
                ? facetGridCellSize : new Vector2(900 / columns, 440 / rows);
            Vector2 gridPosition = haveLayout
                ? facetGridCellOrigin : new Vector2(-165, -85);
            Vector2 gridSize = new Vector2(cellSize.x * columns,
                cellSize.y * rows);
            // Materialized cells leave a six-pixel gutter on every side of
            // their logical slot. Draft outlines must follow the visible card
            // edges rather than the larger slot bounds.
            Vector2 visibleGridSize = gridSize - new Vector2(12, 12);

            string currentColumns = activeGridTransposed ? "DEPTH" : "TIME";
            string currentRows = activeGridTransposed ? "TIME" : "DEPTH";
            string nextColumns = pivotComparisonMode == 1 ? "DEPTH" :
                pivotComparisonMode == 3 ? "DEPTH" : "TIME";
            string nextRows = pivotComparisonMode >= 2 ? "VARIABLE" :
                pivotComparisonMode == 1 ? "TIME" : "DEPTH";
            string operationLabel = draftOperation == DraftOperation.Pivot
                ? "PIVOT   " + currentColumns + " COLUMNS × " + currentRows +
                    " ROWS   →   " + nextColumns + " COLUMNS × " + nextRows + " ROWS"
                : draftOperation == DraftOperation.Drill
                    ? "DRILL  ·  SELECT BUCKETS"
                    : "ROLL-UP  ·  GROUP NEIGHBOURS";
            operationLabel = draftOperation == DraftOperation.Pivot
                ? "PIVOT   SHOW / HIDE ROWS AND COLUMNS"
                : draftOperation == DraftOperation.Drill
                    ? "DRILL   SELECT ROWS OR COLUMNS TO EXPAND"
                    : "ROLL-UP   SELECT ADJACENT ROWS OR COLUMNS";
            CreateText(facetGridContent, operationLabel,
                21, FontStyle.Bold, new Vector2(-285, 205),
                new Vector2(720, 32), TextAnchor.MiddleLeft, accent);

            if (draftOperation != DraftOperation.Pivot)
            {
                CreateButton(facetGridContent, "TIME", new Vector2(-90, 165),
                    new Vector2(160, 38),
                    draftTargetDimension == 0 ? TimeColor : Card,
                    () => SetDraftTargetDimension(0));
                CreateButton(facetGridContent, "DEPTH", new Vector2(90, 165),
                    new Vector2(160, 38),
                    draftTargetDimension == 1 ? DepthColor : Card,
                    () => SetDraftTargetDimension(1));
            }

            if (draftOperation == DraftOperation.Pivot)
            {
                CreateDashedRect(facetGridContent, gridPosition, visibleGridSize,
                    new Color(accent.r, accent.g, accent.b, 0.84f), 4.0f);
                BuildPivotAxisVisibilityControls(gridPosition, gridSize,
                    columns, rows);
            }
            else
            {
                bool[] selected = draftTargetDimension == 0
                    ? selectedTimeTicks : selectedDepthTicks;
                int[] groups = draftTargetDimension == 0
                    ? timeRollupGroups : depthRollupGroups;
                int count = Mathf.Min(DraftBucketCount(draftTargetDimension),
                    selected.Length);
                bool targetAcross = draftTargetDimension == 0
                    ? !activeGridTransposed : activeGridTransposed;
                for (int tick = 0; tick < count; tick++)
                {
                    bool marked = draftOperation == DraftOperation.RollUp
                        ? groups[tick] > 0
                        : selected[tick];
                    if (!marked)
                        continue;
                    Color markColor = draftOperation == DraftOperation.RollUp
                        ? RollupGroupColor(groups[tick]) : accent;
                    Vector2 center;
                    Vector2 size;
                    if (targetAcross)
                    {
                        float width = gridSize.x / columns;
                        center = gridPosition + new Vector2(
                            -gridSize.x * 0.5f + width * (tick + 0.5f), 0);
                        size = new Vector2(width - 12, visibleGridSize.y);
                    }
                    else
                    {
                        float height = gridSize.y / rows;
                        center = gridPosition + new Vector2(0,
                            gridSize.y * 0.5f - height * (tick + 0.5f));
                        size = new Vector2(visibleGridSize.x, height - 12);
                    }
                    CreateDraftSelectionWash(facetGridContent, center, size,
                        markColor);
                    CreateDashedRect(facetGridContent, center, size,
                        markColor, 4.0f);
                    string tag = draftOperation == DraftOperation.RollUp
                        ? "MERGE " + (char)('A' + groups[tick] - 1)
                        : "OPEN ×3";
                    if (LegacyRollupGroupUiVisible()) CreateText(facetGridContent, tag, 12, FontStyle.Bold,
                        center, new Vector2(Mathf.Min(160, size.x - 8), 25),
                        TextAnchor.MiddleCenter, markColor);
                }
            }

            CreateDraftOutcomePreview(accent, columns, rows);
        }

        private void CreateDraftOutcomePreview(Color accent, int oldColumns,
            int oldRows)
        {
            Vector2 cardCenter = new Vector2(515, -55);
            Vector2 cardSize = new Vector2(390, 440);
            CreatePanelCard(facetGridContent, cardCenter, cardSize, accent);
            CreateText(facetGridContent,
                draftOperation == DraftOperation.Pivot ? "PIVOT PREVIEW" :
                draftOperation == DraftOperation.Drill ? "EXPANDED RESULT" :
                "MERGED RESULT",
                22, FontStyle.Bold, cardCenter + new Vector2(0, 176),
                new Vector2(320, 34), TextAnchor.MiddleCenter, accent);
            if (LegacyRollupGroupUiVisible() &&
                draftOperation == DraftOperation.RollUp)
            {
                CreateButton(facetGridContent, "A", cardCenter + new Vector2(-86, 138),
                    SlabLabLayout.RollupGroupButtonSize, activeRollupGroup == 1 ? Green : Card,
                    () => SelectRollupGroup(1));
                CreateButton(facetGridContent, "B", cardCenter + new Vector2(-28, 138),
                    SlabLabLayout.RollupGroupButtonSize, activeRollupGroup == 2 ? Purple : Card,
                    () => SelectRollupGroup(2));
                CreateButton(facetGridContent, "C", cardCenter + new Vector2(30, 138),
                    SlabLabLayout.RollupGroupButtonSize, activeRollupGroup == 3 ? Amber : Card,
                    () => SelectRollupGroup(3));
                CreateButton(facetGridContent, "–", cardCenter + new Vector2(88, 138),
                    SlabLabLayout.RollupGroupButtonSize, activeRollupGroup == 0 ? Danger : Card,
                    () => SelectRollupGroup(0));
            }
            if (draftOperation == DraftOperation.RollUp)
                CreateText(facetGridContent,
                    "SELECT ADJACENT CELLS  >  MERGE INTO ONE",
                    14, FontStyle.Bold, cardCenter + new Vector2(0, 143),
                    new Vector2(330, 28), TextAnchor.MiddleCenter, Green);

            int previewColumns = oldColumns;
            int previewRows = oldRows;
            if (draftOperation == DraftOperation.Pivot)
            {
                int timeCount = activeTimeBuckets != null
                    ? Mathf.Max(1, activeTimeBuckets.Length)
                    : (activeGridTransposed ? oldRows : oldColumns);
                int depthCount = activeDepthBuckets != null
                    ? Mathf.Max(1, activeDepthBuckets.Length)
                    : (activeGridTransposed ? oldColumns : oldRows);
                int variableCount = Mathf.Max(1,
                    ActiveBoundVariableIndices().Count);
                timeCount = Mathf.Max(1, CountSelectedTicks(selectedTimeTicks));
                depthCount = Mathf.Max(1, CountSelectedTicks(selectedDepthTicks));
                previewColumns = pivotComparisonMode == 1 ||
                    pivotComparisonMode == 3 ? depthCount : timeCount;
                previewRows = pivotComparisonMode >= 2 ? variableCount :
                    pivotComparisonMode == 1 ? timeCount : depthCount;
            }
            else
            {
                S4DIndexBucketRequest[] time = DraftSourceBuckets(0);
                S4DIndexBucketRequest[] depth = DraftSourceBuckets(1);
                int nextTime = time != null ? time.Length : 1;
                int nextDepth = depth != null ? depth.Length : 1;
                if (draftOperation == DraftOperation.Drill)
                {
                    if (draftTargetDimension == 0 && time != null)
                        nextTime = ExpandSelectedBuckets(time,
                            selectedTimeTicks, "ghost_time").Length;
                    else if (draftTargetDimension == 1 && depth != null)
                        nextDepth = ExpandSelectedBuckets(depth,
                            selectedDepthTicks, "ghost_depth").Length;
                }
                else
                {
                    if (draftTargetDimension == 0 && time != null)
                        nextTime = MergeBucketGroups(time,
                            timeRollupGroups, "ghost_time").Length;
                    else if (draftTargetDimension == 1 && depth != null)
                        nextDepth = MergeBucketGroups(depth,
                            depthRollupGroups, "ghost_depth").Length;
                }
                previewColumns = pivotTransposed ? nextDepth : nextTime;
                previewRows = pivotTransposed ? nextTime : nextDepth;
            }
            previewColumns = Mathf.Clamp(previewColumns, 1, 9);
            previewRows = Mathf.Clamp(previewRows, 1, 9);

            Vector2 previewCenter = cardCenter + new Vector2(12, -7);
            Vector2 previewSize = new Vector2(258, 232);
            RectTransform previewParent = facetGridContent;
            Vector2 previewOrigin = previewCenter;
            if (draftOperation == DraftOperation.Pivot)
            {
                GameObject previewObject = new GameObject(
                    "Pivot matrix - grab and rotate", typeof(RectTransform),
                    typeof(Image));
                previewObject.layer = 5;
                previewObject.transform.SetParent(facetGridContent, false);
                draftPivotPreviewRoot = previewObject.GetComponent<RectTransform>();
                draftPivotPreviewRoot.anchoredPosition = previewCenter;
                draftPivotPreviewRoot.sizeDelta = previewSize + new Vector2(42, 46);
                Image previewHitArea = previewObject.GetComponent<Image>();
                previewHitArea.color = new Color(accent.r, accent.g, accent.b, 0.018f);
                previewHitArea.raycastTarget = false;
                previewParent = draftPivotPreviewRoot;
                previewOrigin = Vector2.zero;
            }
            float cellWidth = previewSize.x / previewColumns;
            float cellHeight = previewSize.y / previewRows;
            for (int row = 0; row < previewRows; row++)
            {
                for (int column = 0; column < previewColumns; column++)
                {
                    Vector2 center = previewOrigin + new Vector2(
                        -previewSize.x * 0.5f + cellWidth * (column + 0.5f),
                        previewSize.y * 0.5f - cellHeight * (row + 0.5f));
                    CreateDashedRect(previewParent, center,
                        new Vector2(cellWidth - 5, cellHeight - 5), accent,
                        2.2f);
                }
            }
            if (draftOperation == DraftOperation.Pivot)
            {
                string columnDimension = pivotComparisonMode == 1 ||
                    pivotComparisonMode == 3 ? "DEPTH" : "TIME";
                string rowDimension = pivotComparisonMode >= 2 ? "VARIABLE" :
                    pivotComparisonMode == 1 ? "TIME" : "DEPTH";
                Color columnColor = columnDimension == "DEPTH"
                    ? DepthColor : TimeColor;
                Color rowColor = rowDimension == "VARIABLE" ? VariableColor :
                    rowDimension == "TIME" ? TimeColor : DepthColor;
                CreateText(previewParent, columnDimension + "  COLUMNS",
                    15, FontStyle.Bold,
                    previewOrigin + new Vector2(0, previewSize.y * 0.5f + 19),
                    new Vector2(260, 26), TextAnchor.MiddleCenter, columnColor);
                Text previewRowLabel = CreateText(previewParent,
                    rowDimension + "  ROWS", 15, FontStyle.Bold,
                    previewOrigin + new Vector2(-previewSize.x * 0.5f - 17, 0),
                    new Vector2(190, 22), TextAnchor.MiddleCenter, rowColor);
                previewRowLabel.rectTransform.localRotation =
                    Quaternion.Euler(0, 0, 90.0f);

                bool depthAcross = columnDimension == "DEPTH";
                S4DIndexBucketRequest[] columnBuckets = depthAcross
                    ? DraftSourceBuckets(1) : DraftSourceBuckets(0);
                S4DIndexBucketRequest[] rowBuckets = rowDimension == "TIME"
                    ? DraftSourceBuckets(0) : rowDimension == "DEPTH"
                        ? DraftSourceBuckets(1) : null;
                string[] columnFallback = depthAcross
                    ? new[] { "surface", "middle", "deep" }
                    : new[] { "before", "during", "after" };
                string[] rowFallback = rowDimension == "TIME"
                    ? new[] { "before", "during", "after" }
                    : rowDimension == "DEPTH"
                        ? new[] { "surface", "middle", "deep" }
                        : ActiveVariableNames();
                for (int column = 0; column < previewColumns; column++)
                {
                    float x = -previewSize.x * 0.5f +
                        cellWidth * (column + 0.5f);
                    CreateText(previewParent,
                        ActiveBucketLabel(columnBuckets, column, columnFallback),
                        11, FontStyle.Bold,
                        previewOrigin + new Vector2(x,
                            previewSize.y * 0.5f - 12),
                        new Vector2(Mathf.Max(42, cellWidth - 8), 18),
                        TextAnchor.MiddleCenter, columnColor);
                }
                for (int row = 0; row < previewRows; row++)
                {
                    float y = previewSize.y * 0.5f -
                        cellHeight * (row + 0.5f);
                    CreateText(previewParent,
                        ActiveBucketLabel(rowBuckets, row, rowFallback),
                        11, FontStyle.Bold,
                        previewOrigin + new Vector2(-previewSize.x * 0.5f + 23, y),
                        new Vector2(54, 18), TextAnchor.MiddleCenter, rowColor);
                }
                if (pivotComparisonMode == 2 || pivotComparisonMode == 3)
                {
                    string fixedLabel = pivotComparisonMode == 2
                        ? "DEPTH FIXED  z=" + pivotFixedDepth
                        : "TIME FIXED  " +
                            (selectedDataset != null
                                ? selectedDataset.GetTimeLabel(pivotFixedTime)
                                : "day " + (pivotFixedTime + 1));
                    CreateButton(facetGridContent, "−",
                        cardCenter + new Vector2(-125, -118),
                        new Vector2(44, 32), Card,
                        () => NudgePivotFixedBucket(
                            pivotComparisonMode == 2 ? 1 : 0, -1));
                    CreateText(facetGridContent, fixedLabel, 14,
                        FontStyle.Bold, cardCenter + new Vector2(0, -118),
                        new Vector2(200, 30), TextAnchor.MiddleCenter,
                        pivotComparisonMode == 2 ? DepthColor : TimeColor);
                    CreateButton(facetGridContent, "+",
                        cardCenter + new Vector2(125, -118),
                        new Vector2(44, 32), Card,
                        () => NudgePivotFixedBucket(
                            pivotComparisonMode == 2 ? 1 : 0, 1));
                }
            }
            if (draftOperation == DraftOperation.Pivot)
                CreateButton(facetGridContent, "↻",
                    cardCenter + new Vector2(135, -170),
                    new Vector2(68, 52), Purple, RotatePivotMatrix);
            CreateText(facetGridContent,
                previewColumns + " COLUMNS  ×  " + previewRows + " ROWS",
                13, FontStyle.Bold, cardCenter + new Vector2(-35, -158),
                new Vector2(220, 24), TextAnchor.MiddleCenter, Ink);
            CreateOverlayArrow(facetGridContent, new Vector2(226, -55),
                new Vector2(320, -55), accent);
        }

        private static void CreateDraftSelectionWash(RectTransform parent,
            Vector2 center, Vector2 size, Color color)
        {
            GameObject washObject = new GameObject("Draft selection wash",
                typeof(RectTransform), typeof(Image));
            washObject.layer = 5;
            washObject.transform.SetParent(parent, false);
            RectTransform rect = washObject.GetComponent<RectTransform>();
            rect.anchoredPosition = center;
            rect.sizeDelta = size;
            Image image = washObject.GetComponent<Image>();
            image.color = new Color(color.r, color.g, color.b, 0.09f);
            image.raycastTarget = false;
        }

        private string DraftInstruction()
        {
            switch (draftOperation)
            {
                case DraftOperation.Pivot:
                    return "Copied from the current analysis. Change roles or fixed buckets, then preview and submit.";
                case DraftOperation.Drill:
                    return "Copied from the current analysis. Select one or more parent buckets to expand into an explicit child comparison.";
                case DraftOperation.RollUp:
                    return "Copied from the current analysis. Group adjacent faceted tick-blocks into coarser buckets.";
                default:
                    return string.Empty;
            }
        }

        private void ApplyPivotDraftConfiguration()
        {
            if (draftOperation != DraftOperation.Pivot)
                return;
            for (int index = 0; index < roles.Length; index++)
                roles[index] = pivotSourceRoles[index];
            roles[2] = DimensionRole.Mapped;
            if (pivotComparisonMode == 2)
            {
                roles[0] = DimensionRole.Faceted;
                roles[1] = DimensionRole.Fixed;
                roles[3] = DimensionRole.Faceted;
                selectedZ = pivotFixedDepth;
            }
            else if (pivotComparisonMode == 3)
            {
                roles[0] = DimensionRole.Fixed;
                roles[1] = DimensionRole.Faceted;
                roles[3] = DimensionRole.Faceted;
                selectedTime = pivotFixedTime;
            }
        }

        private void BeginDraft(DraftOperation operation)
        {
            if (selectedDataset == null)
            {
                SetStatus("Choose a dataset before creating an analysis draft.");
                return;
            }
            if (draftOperation == operation && facetGridCanvas != null &&
                facetGridCanvas.gameObject.activeSelf)
            {
                BuildFacetGridPanel();
                return;
            }
            draftSourceNodeId = currentAnalysisNode != null
                ? currentAnalysisNode.nodeId
                : string.Empty;
            draftOperation = operation;
            pivotTransposed = operation == DraftOperation.Pivot
                ? currentAnalysisNode == null || !currentAnalysisNode.gridTransposed
                : currentAnalysisNode != null && currentAnalysisNode.gridTransposed;
            for (int roleIndex = 0; roleIndex < roles.Length; roleIndex++)
                pivotSourceRoles[roleIndex] = roles[roleIndex];
            pivotComparisonMode = pivotTransposed ? 1 : 0;
            pivotFixedTime = selectedTime;
            pivotFixedDepth = selectedZ;
            ClearDraftTickSelections();
            draftTargetDimension = roles[0] == DimensionRole.Faceted
                ? 0 : 1;
            if (operation == DraftOperation.Pivot)
            {
                ResetSelectedTicksToActiveBuckets();
            }
            else if (operation == DraftOperation.Drill)
            {
                int count = DraftBucketCount(draftTargetDimension);
                (draftTargetDimension == 0
                    ? selectedTimeTicks
                    : selectedDepthTicks)[Mathf.Clamp(count / 2, 0,
                        MaxFacetAxisBuckets - 1)] = true;
            }
            else if (operation == DraftOperation.RollUp)
            {
                int[] groups = draftTargetDimension == 0
                    ? timeRollupGroups : depthRollupGroups;
                bool[] ticks = draftTargetDimension == 0
                    ? selectedTimeTicks : selectedDepthTicks;
                int count = DraftBucketCount(draftTargetDimension);
                for (int index = 0; index < Mathf.Min(2, count); index++)
                {
                    groups[index] = 1;
                    ticks[index] = true;
                }
            }
            facetGridLayered = false;
            facetGridPeeledLayers = 0;
            // Keep the completed spatial workspace visible. Draft operations
            // use their own compact editor and never reopen the legacy slab
            // controller.
            viewState.legacyPanelVisible = false;
            if (panelCanvas != null)
                panelCanvas.gameObject.SetActive(false);
            if (slabPreviewCanvas != null)
                slabPreviewCanvas.gameObject.SetActive(false);
            if (intentCanvas != null)
                intentCanvas.gameObject.SetActive(false);
            if (facetGridCanvas != null)
            {
                HidePrimaryToolsExcept(facetGridCanvas);
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
            }
            // Pivot, Drill, and Roll-up are edited directly on the result grid.
            // A second abstract panel duplicated state and obscured the cells.
            if (draftCanvas != null)
                draftCanvas.gameObject.SetActive(false);
            SetStatus(operation +
                " is active on the current result grid.");
        }

        private void ClearDraftTickSelections()
        {
            Array.Clear(selectedTimeTicks, 0, selectedTimeTicks.Length);
            Array.Clear(selectedDepthTicks, 0, selectedDepthTicks.Length);
            Array.Clear(timeRollupGroups, 0, timeRollupGroups.Length);
            Array.Clear(depthRollupGroups, 0, depthRollupGroups.Length);
            activeRollupGroup = 1;
        }

        private int DraftBucketCount(int dimension)
        {
            S4DIndexBucketRequest[] buckets = DraftSourceBuckets(dimension);
            return buckets != null && buckets.Length > 0
                ? Mathf.Min(buckets.Length, MaxFacetAxisBuckets)
                : 3;
        }

        private S4DIndexBucketRequest[] DraftSourceBuckets(int dimension)
        {
            AnalysisNodeState source = FindAnalysisNode(draftSourceNodeId) ??
                currentAnalysisNode;
            S4DIndexBucketRequest[] buckets = source != null
                ? dimension == 0 ? source.timeBuckets : source.depthBuckets
                : dimension == 0 ? activeTimeBuckets : activeDepthBuckets;
            return buckets;
        }

        private void CancelDraft()
        {
            draftOperation = DraftOperation.None;
            draftSourceNodeId = string.Empty;
            pivotTransposed = currentAnalysisNode != null &&
                currentAnalysisNode.gridTransposed;
            slabPreviewBuilt = false;
            ResetSelectedTicksToActiveBuckets();
            if (draftCanvas != null)
                draftCanvas.gameObject.SetActive(false);
            if (panelCanvas != null)
                panelCanvas.gameObject.SetActive(false);
            if (facetGridCanvas != null && s4dGridImage != null)
            {
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
            }
            SetStatus("Draft cancelled. No analysis node was created.");
        }

        private void ApplyDraftBucketOperation(
            ref S4DIndexBucketRequest[] timeBuckets,
            ref S4DIndexBucketRequest[] depthBuckets)
        {
            if (draftOperation == DraftOperation.Drill)
            {
                if (roles[0] == DimensionRole.Faceted &&
                    CountSelectedTicks(selectedTimeTicks) > 0)
                    timeBuckets = ExpandSelectedBuckets(
                        timeBuckets, selectedTimeTicks, "time");
                else if (roles[1] == DimensionRole.Faceted &&
                    CountSelectedTicks(selectedDepthTicks) > 0)
                    depthBuckets = ExpandSelectedBuckets(
                        depthBuckets, selectedDepthTicks, "depth");
            }
            else if (draftOperation == DraftOperation.RollUp)
            {
                if (draftTargetDimension == 0 &&
                    roles[0] == DimensionRole.Faceted)
                    timeBuckets = MergeBucketGroups(timeBuckets,
                        timeRollupGroups, "time_group");
                else if (draftTargetDimension == 1 &&
                    roles[1] == DimensionRole.Faceted)
                    depthBuckets = MergeBucketGroups(depthBuckets,
                        depthRollupGroups, "depth_group");
            }
            else if (draftOperation == DraftOperation.Pivot)
            {
                // Pivot's row/column chips are direct visibility controls.
                // What is switched off in the preview must also be absent from
                // the materialization request.
                timeBuckets = FilterBucketsByMask(timeBuckets,
                    selectedTimeTicks);
                depthBuckets = FilterBucketsByMask(depthBuckets,
                    selectedDepthTicks);
            }
        }

        private string DraftSelectionSummary(DraftOperation operation)
        {
            if (operation == DraftOperation.Pivot)
                return RoleLabel(roles[0]) + " Time  /  " +
                    RoleLabel(roles[1]) + " Depth";
            if (operation == DraftOperation.Drill)
                return "EXPAND  " + TickSelectionSummary();
            if (operation == DraftOperation.RollUp)
                return "MERGE  " + TickSelectionSummary();
            return FacetAxisSummary().ToUpperInvariant() + "  /  " +
                (selectedDataset != null ? selectedDataset.Name : "dataset");
        }

        private string SelectedDraftBucketLabels(int dimension)
        {
            bool[] ticks = dimension == 0
                ? selectedTimeTicks : selectedDepthTicks;
            int[] groups = dimension == 0
                ? timeRollupGroups : depthRollupGroups;
            S4DIndexBucketRequest[] buckets = DraftSourceBuckets(dimension);
            List<string> selected = new List<string>();
            int count = DraftBucketCount(dimension);
            for (int index = 0; index < count && index < ticks.Length; index++)
            {
                if (ticks[index])
                {
                    string label = buckets != null && index < buckets.Length
                        ? buckets[index].label : "bucket " + (index + 1);
                    selected.Add(draftOperation == DraftOperation.RollUp &&
                        groups[index] > 0
                            ? "G" + groups[index] + ":" + label
                            : label);
                }
            }
            return string.Join("+", selected.ToArray());
        }

        private void SelectRollupGroup(int group)
        {
            activeRollupGroup = Mathf.Clamp(group, 0, 3);
            SetStatus(activeRollupGroup == 0
                ? "Roll-up erase mode: select a tick-block to remove its group."
                : "Assign adjacent tick-blocks to Roll-up group " +
                    activeRollupGroup + ".");
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
        }

        private void OnSourcePreviewLayerComplete(int variableIndex,
            Texture2D atlas, string error)
        {
            if (!IsPending(PendingJob.SourcePreview))
            {
                if (atlas != null)
                    Destroy(atlas);
                return;
            }
            if (atlas == null)
            {
                SetPending(PendingJob.SourcePreview, false);
                spatialWorkflowStep = SpatialWorkflowStep.Intent;
                intentResolutionError = string.IsNullOrWhiteSpace(error)
                    ? "Raw interval-mean preview returned no image."
                    : error;
                SetStatus(intentResolutionError + " Resolve & Apply to retry.");
                BuildIntentPanel();
                BuildWorkflowToolbar();
                return;
            }
            sourcePreviewLayerAtlases.Add(atlas);
            sourcePreviewRequestCursor++;
            RequestNextSourcePreviewLayer();
        }

        private void TogglePivotBucketVisibility(int dimension, int index)
        {
            if (draftOperation != DraftOperation.Pivot)
                return;
            bool[] mask = dimension == 0 ? selectedTimeTicks : selectedDepthTicks;
            int count = DraftBucketCount(dimension);
            index = Mathf.Clamp(index, 0, Mathf.Max(0, count - 1));
            if (index >= mask.Length)
                return;
            if (mask[index] && CountSelectedTicks(mask) <= 1)
            {
                SetStatus("Pivot keeps at least one visible " +
                    (dimension == 0 ? "Time column." : "Depth row."));
                return;
            }
            mask[index] = !mask[index];
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
        }

        private void NudgePivotFixedBucket(int dimension, int delta)
        {
            if (draftOperation != DraftOperation.Pivot)
                return;
            if (dimension == 0)
            {
                int count = selectedDataset != null
                    ? Mathf.Max(1, selectedDataset.TimeCount) : 1;
                pivotFixedTime = Mathf.Clamp(pivotFixedTime + delta, 0,
                    count - 1);
            }
            else
            {
                int count = selectedDataset != null
                    ? Mathf.Max(1, selectedDataset.DimZ == 92
                        ? 91 : selectedDataset.DimZ) : 1;
                pivotFixedDepth = Mathf.Clamp(pivotFixedDepth + delta, 0,
                    count - 1);
            }
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
        }

        private void ResetSelectedTicksToActiveBuckets()
        {
            ClearDraftTickSelections();
            int timeCount = Mathf.Clamp(DraftBucketCount(0), 1,
                selectedTimeTicks.Length);
            int depthCount = Mathf.Clamp(DraftBucketCount(1), 1,
                selectedDepthTicks.Length);
            for (int index = 0; index < timeCount; index++)
                selectedTimeTicks[index] = true;
            for (int index = 0; index < depthCount; index++)
                selectedDepthTicks[index] = true;
        }

        private void PreviewSlab()
        {
            if (!AreSpatialAxisBindingsComplete(out string missing))
            {
                spatialWorkflowStep = SpatialWorkflowStep.AxisBinding;
                SetStatus("Generate Slab locked: " + missing);
                BuildWorkflowToolbar();
                return;
            }
            if (selectedDataset == null)
            {
                SetStatus("Generate Slab locked: bind and select a variable first.");
                return;
            }
            gridCellSelected = false;
            selectedCellPinned = false;
            ClearSourcePreviewLayers();
            if (mainWorkspaceEntered)
                EnsureSavedAuthorBoundaries();
            bool reusePreconfiguredBoundaries = HasSavedAuthorBoundaries();
            intentConfigured = false;
            intentResolutionError = string.Empty;
            slabPreviewBuilt = true;
            spatialWorkflowStep = reusePreconfiguredBoundaries
                ? SpatialWorkflowStep.Intent
                : SpatialWorkflowStep.SlabSkeleton;
            // Build provisional dimensions only to draw the empty skeleton.
            // No aggregate or MatPlot request is made at this step.
            BuildS4DGridRequest();
            if (slabPreviewCanvas != null)
            {
                if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
                {
                    // Desktop Intent owns the central task surface. Do not
                    // flash the intermediate VR Slab preview before opening it.
                    slabPreviewCanvas.gameObject.SetActive(false);
                }
                else
                {
                    slabPreviewCanvas.transform.localPosition =
                        SlabPreviewDockPosition;
                    ShowComposerTool(slabPreviewCanvas);
                    BuildSlabPreviewPanel();
                }
            }
            RecordTrailEvent("SLAB", "axis skeleton generated");
            BuildStage();
            if (reusePreconfiguredBoundaries)
            {
                SetStatus("Slab ready with the saved Time and Depth ranges. Open MatPlot Intent.");
                BuildWorkflowToolbar();
            }
            else if (!mainWorkspaceEntered)
            {
                OpenInitialAuthorBoundary();
            }
            else
            {
                SetStatus("Saved Time and Depth ranges could not be restored. Return to Field Setup.");
            }
        }

        // Retained only so older saved drafts can still deserialize their
        // numeric groups. The current direct-manipulation UI intentionally
        // never exposes those implementation identifiers.
        private static bool LegacyRollupGroupUiVisible()
        {
            return false;
        }

        private void BeginSourcePreviewGeneration()
        {
            if (!intentConfigured || !authorBoundaryConfirmed)
            {
                SetStatus("Source preview locked: confirm both boundaries and resolve the intent first.");
                return;
            }
            List<int> variables = ActiveBoundVariableIndices();
            if (variables.Count < 1)
            {
                SetStatus("Source preview locked: select at least one variable.");
                return;
            }

            ClearSourcePreviewLayers();
            sourcePreviewVariableIndices.AddRange(variables);
            sourcePreviewRequestCursor = 0;
            SetPending(PendingJob.SourcePreview, true);
            spatialWorkflowStep = SpatialWorkflowStep.Intent;
            // Recompute the final 1/3 by 1/3 layout from the confirmed buckets.
            BuildS4DGridRequest();
            if (slabPreviewCanvas != null)
            {
                ShowComposerTool(slabPreviewCanvas);
                BuildSlabPreviewPanel();
            }
            BuildIntentPanel();
            BuildWorkflowToolbar();
            RequestNextSourcePreviewLayer();
        }

        private void RequestNextSourcePreviewLayer()
        {
            if (!IsPending(PendingJob.SourcePreview))
                return;
            if (sourcePreviewRequestCursor >= sourcePreviewVariableIndices.Count)
            {
                SetPending(PendingJob.SourcePreview, false);
                spatialWorkflowStep = SpatialWorkflowStep.SourcePreviewReady;
                BuildAllSourcePreviewLayers();
                BuildIntentPanel();
                BuildWorkflowToolbar();
                SetStatus("Raw interval-mean preview ready for " +
                    sourcePreviewLayerAtlases.Count + " variable layer" +
                    (sourcePreviewLayerAtlases.Count == 1 ? string.Empty : "s") +
                    ". MatPlotAgent has not run; Full Matrix is now enabled.");
                return;
            }

            int variableIndex =
                sourcePreviewVariableIndices[sourcePreviewRequestCursor];
            VolumeSTCubeSliceDataset dataset = datasets[variableIndex];
            SetStatus("Building raw interval-mean preview " +
                (sourcePreviewRequestCursor + 1) + "/" +
                sourcePreviewVariableIndices.Count + ": " + dataset.Name + "...");
            S4DFacetGridRequest request =
                BuildS4DGridRequestForVariable(variableIndex);
            request.datasetId = dataset.DatasetId;
            request.variableId = dataset.VariableId;
            VolumeSTCubeS4DAnalysisClient previewClient =
                new VolumeSTCubeS4DAnalysisClient(s4dUrl, 60);
            StartCoroutine(previewClient.PreviewAtlas(request,
                (atlas, error) => OnSourcePreviewLayerComplete(
                    variableIndex, atlas, error)));
        }

        private void DestroySourcePreviewLayerCanvases()
        {
            for (int index = 0; index < sourcePreviewLayerCanvases.Count; index++)
                if (sourcePreviewLayerCanvases[index] != null)
                    Destroy(sourcePreviewLayerCanvases[index].gameObject);
            sourcePreviewLayerCanvases.Clear();
        }

        private void InvalidateSlabConfiguration(string reason,
            bool preserveAuthoredBoundaries = false)
        {
            preserveAuthoredBoundaries = preserveAuthoredBoundaries ||
                mainWorkspaceEntered;
            slabPreviewBuilt = false;
            intentConfigured = false;
            spatialWorkflowStep = SpatialWorkflowStep.AxisBinding;
            if (!preserveAuthoredBoundaries)
            {
                authorBoundaryConfirmed = false;
                authoredTimeBuckets = null;
                authoredDepthBuckets = null;
            }
            else if (mainWorkspaceEntered)
            {
                EnsureSavedAuthorBoundaries();
            }
            ClearSourcePreviewLayers();
            if (slabPreviewCanvas != null)
                slabPreviewCanvas.gameObject.SetActive(false);
            if (intentCanvas != null)
                intentCanvas.gameObject.SetActive(false);
            BuildWorkflowToolbar();
            SetStatus(reason + (preserveAuthoredBoundaries &&
                authorBoundaryConfirmed
                    ? ". Saved Time and Depth ranges are retained; complete the axis bindings, then open MatPlot Intent."
                    : ". Complete the axis bindings and Time/Depth setup, then open MatPlot Intent."));
        }

        private void ClearSourcePreviewLayers()
        {
            Texture2D previousAtlas = matrixPreviewAtlas;
            bool previousAtlasIsLayer = previousAtlas != null &&
                sourcePreviewLayerAtlases.Contains(previousAtlas);
            SetPending(PendingJob.SourcePreview, false);
            sourcePreviewRequestCursor = 0;
            DestroySourcePreviewLayerCanvases();
            matrixPreviewAtlas = null;
            for (int index = 0; index < sourcePreviewLayerAtlases.Count; index++)
                if (sourcePreviewLayerAtlases[index] != null)
                    Destroy(sourcePreviewLayerAtlases[index]);
            sourcePreviewLayerAtlases.Clear();
            sourcePreviewVariableIndices.Clear();
            sourcePreviewRenderVariableIndex = -1;
            // The alias normally points at layer zero and was destroyed in the
            // loop. Standalone legacy preview atlases still need cleanup.
            if (previousAtlas != null && !previousAtlasIsLayer)
                Destroy(previousAtlas);
            materializationVariableCursor = -1;
            materializationVariableIndices.Clear();
            DestroyMaterializedLayerCanvases();
            for (int index = 0; index < materializedLayerAtlases.Count; index++)
            {
                Texture2D layer = materializedLayerAtlases[index];
                if (layer != null && layer != s4dGridImage &&
                    !IsAnalysisNodeTexture(layer))
                    Destroy(layer);
            }
            materializedLayerAtlases.Clear();
            materializedLayerResults.Clear();
        }

        private void SetPivotOrientation(bool transposed)
        {
            if (draftOperation != DraftOperation.Pivot || pivotTransposed == transposed)
                return;
            pivotComparisonMode = transposed ? 1 : 0;
            pivotTransposed = transposed;
            SetStatus(transposed
                ? "Pivot draft set to Depth columns x Time rows."
                : "Pivot draft set to Time columns x Depth rows.");
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
        }

        private void SetPivotComparisonMode(int mode)
        {
            if (draftOperation != DraftOperation.Pivot)
                return;
            pivotComparisonMode = Mathf.Clamp(mode, 0, 3);
            pivotTransposed = pivotComparisonMode == 1 ||
                pivotComparisonMode == 3;
            string[] labels =
            {
                "Time columns x Depth rows",
                "Depth columns x Time rows",
                "Time columns x Variable layers; Depth fixed",
                "Depth columns x Variable layers; Time fixed"
            };
            SetStatus("Pivot copied from " +
                (string.IsNullOrWhiteSpace(draftSourceNodeId)
                    ? "the current Slab" : draftSourceNodeId) +
                ". New comparison: " + labels[pivotComparisonMode] + ".");
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
        }

        private static string OperationNodeTitle(DraftOperation operation)
        {
            switch (operation)
            {
                case DraftOperation.Pivot: return "PIVOT VIEW";
                case DraftOperation.Drill: return "DRILLED VIEW";
                case DraftOperation.RollUp: return "ROLLED-UP VIEW";
                default: return "FULL MATRIX";
            }
        }
    }
}
