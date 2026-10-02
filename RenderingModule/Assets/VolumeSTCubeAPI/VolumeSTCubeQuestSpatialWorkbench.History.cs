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
        private void RetainCurrentResultBesideLiveGrid()
        {
            if (facetGridCanvas == null)
                return;
            AnalysisNodeState source = FindAnalysisNode(draftSourceNodeId) ??
                currentAnalysisNode;
            if (source == null || source.gridImage == null ||
                string.IsNullOrWhiteSpace(source.nodeId))
                return;
            for (int index = 0; index < retainedResultViews.Count; index++)
                if (retainedResultViews[index] != null &&
                    retainedResultViews[index].nodeId == source.nodeId)
                    return;

            // Keep at most the two most recent ancestors in the immediate
            // comparison lane. Older branches remain recoverable in SlabTrail.
            while (retainedResultViews.Count >= 2)
                DestroyRetainedResultView(retainedResultViews[0]);

            Vector3 right = facetGridCanvas.transform.right.normalized;
            // Each panel moves half a lane, producing roughly 0.84 m between
            // centers: enough for the 0.59 m snapshot and 0.92 m live panel
            // to sit side by side without pushing either outside the VR view.
            const float branchSpacing = 0.42f;
            for (int index = 0; index < retainedResultViews.Count; index++)
            {
                Canvas retained = retainedResultViews[index].canvas;
                if (retained != null)
                    retained.transform.position -= right * branchSpacing;
            }

            Canvas snapshot = CreateFloatingCanvas(
                "Retained MatPlot Result " + source.nodeId,
                Vector3.zero, new Vector2(1180, 720), 0.00050f, Purple);
            snapshot.sortingOrder = 103;
            snapshot.transform.position = facetGridCanvas.transform.position -
                right * branchSpacing;
            snapshot.transform.rotation = facetGridCanvas.transform.rotation;
            snapshot.gameObject.SetActive(true);
            RetainedResultView view = new RetainedResultView
            {
                nodeId = source.nodeId,
                canvas = snapshot
            };
            retainedResultViews.Add(view);
            BuildRetainedResultView(view, source);

            // The active surface is the destination for the new branch. Moving
            // it to the adjacent lane makes generation progress visible while
            // the immutable source remains available for comparison.
            facetGridCanvas.transform.position += right * branchSpacing;
            RecordTrailEvent("BRANCH",
                source.nodeId + " retained beside the new " + draftOperation +
                " result", source);
        }

        private void BuildRetainedResultView(RetainedResultView view,
            AnalysisNodeState node)
        {
            if (view == null || view.canvas == null || node == null ||
                node.gridImage == null)
                return;
            RectTransform root = view.canvas.GetComponent<RectTransform>();
            CreateText(root, "PREVIOUS RESULT", 14, FontStyle.Bold,
                new Vector2(-520, 315), new Vector2(240, 28),
                TextAnchor.MiddleLeft, Purple);
            CreateText(root, string.IsNullOrWhiteSpace(node.title)
                    ? node.nodeId : node.title,
                25, FontStyle.Bold, new Vector2(-520, 278),
                new Vector2(760, 42), TextAnchor.MiddleLeft, Ink);
            string variable = string.IsNullOrWhiteSpace(node.variableId)
                ? "VARIABLE" : node.variableId.ToUpperInvariant();
            CreateText(root, node.nodeId + "  /  " + variable,
                13, FontStyle.Bold, new Vector2(-520, 242),
                new Vector2(760, 28), TextAnchor.MiddleLeft, Muted);

            int timeCount = Mathf.Clamp(node.timeBuckets != null
                ? node.timeBuckets.Length : 1, 1, MaxFacetAxisBuckets);
            int depthCount = Mathf.Clamp(node.depthBuckets != null
                ? node.depthBuckets.Length : 1, 1, MaxFacetAxisBuckets);
            int columns = node.gridTransposed ? depthCount : timeCount;
            int rows = node.gridTransposed ? timeCount : depthCount;
            Vector2 gridCenter = new Vector2(-55, -20);
            Vector2 gridSize = new Vector2(1000, 470);
            float cellWidth = gridSize.x / columns;
            float cellHeight = gridSize.y / rows;
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int timeIndex = node.gridTransposed ? row : column;
                    int depthIndex = node.gridTransposed ? column : row;
                    string timeLabel = ActiveBucketLabel(node.timeBuckets,
                        timeIndex, new[] { "before", "during", "after" });
                    string depthLabel = ActiveBucketLabel(node.depthBuckets,
                        depthIndex, new[] { "surface", "middle", "deep" });
                    Vector2 center = gridCenter + new Vector2(
                        -gridSize.x * 0.5f + cellWidth * (column + 0.5f),
                        gridSize.y * 0.5f - cellHeight * (row + 0.5f));
                    CreateRetainedResultCell(root, node.gridImage,
                        center, new Vector2(cellWidth - 12, cellHeight - 12),
                        timeIndex, depthIndex, timeCount, depthCount,
                        depthLabel + "  /  " + timeLabel);
                }
            }

            CreateText(root, "IMMUTABLE SOURCE", 11, FontStyle.Bold,
                new Vector2(-485, -318), new Vector2(260, 24),
                TextAnchor.MiddleLeft, Muted);
            CreateButton(root, "CLOSE", new Vector2(465, -315),
                new Vector2(170, 42), Card,
                () => DestroyRetainedResultView(view));
        }

        private void CreateRetainedResultCell(RectTransform parent,
            Texture2D atlas, Vector2 position, Vector2 size, int timeIndex,
            int depthIndex, int timeCount, int depthCount, string label)
        {
            GameObject cellObject = new GameObject(
                "Retained " + label, typeof(RectTransform));
            cellObject.layer = 5;
            cellObject.transform.SetParent(parent, false);
            RectTransform cell = cellObject.GetComponent<RectTransform>();
            cell.sizeDelta = size;
            cell.anchoredPosition = position;
            Image background = cellObject.AddComponent<Image>();
            background.sprite = RoundedUiSprite();
            background.type = Image.Type.Sliced;
            background.color = new Color(0.010f, 0.024f, 0.035f, 1.0f);
            background.raycastTarget = false;
            Outline outline = cellObject.AddComponent<Outline>();
            outline.effectColor = new Color(Purple.r, Purple.g, Purple.b, 0.58f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            GameObject imageObject = new GameObject(
                "Retained chart", typeof(RectTransform));
            imageObject.transform.SetParent(cell, false);
            RectTransform imageRect = imageObject.GetComponent<RectTransform>();
            imageRect.sizeDelta = new Vector2(
                Mathf.Max(24, size.x - 14), Mathf.Max(20, size.y - 38));
            imageRect.anchoredPosition = new Vector2(0, -6);
            RawImage image = imageObject.AddComponent<RawImage>();
            image.texture = atlas;
            image.color = Color.white;
            image.raycastTarget = false;
            image.uvRect = new Rect(
                timeIndex / (float)Mathf.Max(1, timeCount),
                (Mathf.Max(1, depthCount) - 1 - depthIndex) /
                    (float)Mathf.Max(1, depthCount),
                1.0f / Mathf.Max(1, timeCount),
                1.0f / Mathf.Max(1, depthCount));
            CreateText(cell, label.ToUpperInvariant(),
                Mathf.Clamp(15 - Mathf.Max(timeCount, depthCount), 8, 12),
                FontStyle.Bold, new Vector2(0, size.y * 0.5f - 14),
                new Vector2(size.x - 16, 22), TextAnchor.MiddleLeft, Purple);
        }

        private void DestroyRetainedResultView(RetainedResultView view)
        {
            if (view == null)
                return;
            retainedResultViews.Remove(view);
            if (view.canvas != null)
                Destroy(view.canvas.gameObject);
        }

        private void ClearRetainedResultViews()
        {
            for (int index = retainedResultViews.Count - 1; index >= 0; index--)
            {
                RetainedResultView view = retainedResultViews[index];
                if (view != null && view.canvas != null)
                    Destroy(view.canvas.gameObject);
            }
            retainedResultViews.Clear();
        }

        private void ToggleCurrentGridPin()
        {
            if (currentAnalysisNode == null)
                return;
            currentAnalysisNode.pinned = !currentAnalysisNode.pinned;
            SetStatus(currentAnalysisNode.pinned
                ? currentAnalysisNode.nodeId +
                    " pinned in the workspace and SlabTrail."
                : currentAnalysisNode.nodeId + " grid pin released.");
            BuildFacetGridPanel();
            if (trailCanvas != null && trailCanvas.gameObject.activeSelf)
                BuildTrailPanel();
        }

        private void DismissCurrentGrid()
        {
            if (currentAnalysisNode != null)
                currentAnalysisNode.dismissed = true;
            SetFacetSelectionEvidencePreview(false);
            if (facetGridCanvas != null)
                facetGridCanvas.gameObject.SetActive(false);
            ShowPrimaryTool(trailCanvas);
            BuildTrailPanel();
            SetStatus("Grid dismissed from the workspace. Select its SlabTrail " +
                "node to restore the immutable snapshot.");
        }

        private void DeleteCurrentGridLeaf()
        {
            if (currentAnalysisNode == null)
                return;
            AnalysisNodeState target = currentAnalysisNode;
            DeleteAnalysisLeaf(target);
            if (currentAnalysisNode == target &&
                facetGridCanvas != null &&
                facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
        }

        private int AnalysisNodeDepth(AnalysisNodeState node)
        {
            int depth = 1;
            string parentId = node != null ? node.parentNodeId : string.Empty;
            int guard = 0;
            while (!string.IsNullOrEmpty(parentId) && guard++ < analysisNodes.Count)
            {
                AnalysisNodeState parent = FindAnalysisNode(parentId);
                if (parent == null)
                    break;
                depth++;
                parentId = parent.parentNodeId;
            }
            return depth;
        }

        private void NavigateToAnalysisNode(AnalysisNodeState node)
        {
            if (DesktopOperationPending())
            {
                RejectDesktopAction("Wait for the current operation to finish.");
                return;
            }
            if (node == null || node.gridImage == null) return;
            int index = datasets.FindIndex(item =>
                item.DirectoryPath == node.datasetDirectory);
            if (index < 0)
            {
                RejectDesktopAction("Open the original dataset folder to restore this analysis.");
                return;
            }
            if (selectedDataset != datasets[index])
                StartCoroutine(RestoreAnalysisVariable(node, index));
            else
                ApplyAnalysisNode(node);
        }

        private IEnumerator RestoreAnalysisVariable(AnalysisNodeState node, int index)
        {
            historyRestoring = true;
            try
            {
                LoadDataset(index);
                while (IsPending(PendingJob.VariableLoad) || IsPending(PendingJob.DatasetManifest))
                    yield return null;
                if (index < datasets.Count && selectedDataset == datasets[index] && analysisNodes.Contains(node))
                    ApplyAnalysisNode(node);
                else
                    RejectDesktopAction("Could not restore the original variable. Try again.");
            }
            finally { historyRestoring = false; }
        }

        private void ApplyAnalysisNode(AnalysisNodeState node)
        {
            if (node == null || node.gridImage == null)
                return;
            node.dismissed = false;
            currentAnalysisNode = node;
            s4dGridImage = node.gridImage;
            s4dChartResultJson = node.chartResultJson;
            s4dSnapshotId = node.snapshotId;
            s4dJobId = node.jobId;
            s4dSharedMinimum = node.sharedMinimum;
            s4dSharedMaximum = node.sharedMaximum;
            s4dSharedUnit = node.sharedUnit;
            prompt = node.rawIntent ?? string.Empty;
            analysisQuestion = node.analysisQuestion ?? string.Empty;
            intentTask = string.IsNullOrWhiteSpace(node.analyticTask)
                ? "characterize_distribution"
                : node.analyticTask;
            intentMode = string.IsNullOrWhiteSpace(node.intentDisplayLabel)
                ? intentTask.Replace("_", " ").ToUpperInvariant()
                : node.intentDisplayLabel;
            intentConfigured = node.hasResolvedIntent;
            currentDigest = node.digest;
            digestError = node.digestError ?? string.Empty;
            SetPending(PendingJob.Digest, node.digestPending);
            Array.Clear(facetCellSnapshotIds, 0, facetCellSnapshotIds.Length);
            if (node.cellSnapshotIds != null)
                Array.Copy(node.cellSnapshotIds, facetCellSnapshotIds,
                    Mathf.Min(facetCellSnapshotIds.Length,
                        node.cellSnapshotIds.Length));
            timeBoundaryStart = node.timeBoundaryStart;
            timeBoundaryEnd = node.timeBoundaryEnd;
            depthBoundaryLow = node.depthBoundaryLow;
            depthBoundaryHigh = node.depthBoundaryHigh;
            if (node.roleValues != null)
                for (int index = 0; index < roles.Length && index < node.roleValues.Length; index++)
                    roles[index] = (DimensionRole)node.roleValues[index];
            activeTimeBuckets = CopyAnalysisBuckets(node.timeBuckets);
            activeDepthBuckets = CopyAnalysisBuckets(node.depthBuckets);
            facetGridLayered = false;
            facetGridPeeledLayers = 0;
            activeGridTransposed = node.gridTransposed;
            pivotTransposed = node.gridTransposed;
            int timeBucketCount = activeTimeBuckets != null
                ? Mathf.Max(1, activeTimeBuckets.Length) : 3;
            int depthBucketCount = activeDepthBuckets != null
                ? Mathf.Max(1, activeDepthBuckets.Length) : 3;
            activeGridColumns = activeGridTransposed
                ? depthBucketCount : timeBucketCount;
            activeGridRows = activeGridTransposed
                ? timeBucketCount : depthBucketCount;
            UpdateActiveRepresentativeIndices();
            inspected = node.inspected;
            boundarySuspect = node.boundarySuspect;
            placementConfirmed = true;
            gridStale = node.stale;
            Array.Clear(facetCellStale, 0, facetCellStale.Length);
            if (node.staleCells != null)
                Array.Copy(node.staleCells, facetCellStale,
                    Mathf.Min(facetCellStale.Length, node.staleCells.Length));
            Array.Clear(facetCellInspected, 0, facetCellInspected.Length);
            Array.Clear(facetCellBoundarySuspect, 0,
                facetCellBoundarySuspect.Length);
            Array.Clear(facetCellLocalized, 0, facetCellLocalized.Length);
            evidenceLocalized = false;
            if (node.verifiedCells != null)
                Array.Copy(node.verifiedCells, facetCellInspected,
                    Mathf.Min(facetCellInspected.Length,
                        node.verifiedCells.Length));
            if (node.suspectCells != null)
                Array.Copy(node.suspectCells, facetCellBoundarySuspect,
                    Mathf.Min(facetCellBoundarySuspect.Length,
                        node.suspectCells.Length));
            if (node.localizedCells != null)
                Array.Copy(node.localizedCells, facetCellLocalized,
                    Mathf.Min(facetCellLocalized.Length,
                        node.localizedCells.Length));
            evidenceLocalized = Array.Exists(facetCellLocalized, value => value);
            Array.Clear(facetCellPinned, 0, facetCellPinned.Length);
            if (node.pinnedCells != null)
                Array.Copy(node.pinnedCells, facetCellPinned,
                    Mathf.Min(facetCellPinned.Length,
                        node.pinnedCells.Length));
            selectedCellPinned = false;
            draftOperation = DraftOperation.None;
            draftSourceNodeId = string.Empty;
            ResetSelectedTicksToActiveBuckets();
            if (trailCanvas != null)
                trailCanvas.gameObject.SetActive(false);
            if (panelCanvas != null)
                panelCanvas.gameObject.SetActive(false);
            if (facetGridCanvas != null)
            {
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
            }
            stage = Stage.Matrix;
            spatialWorkflowStep = SpatialWorkflowStep.Result;
            if (aiFindingsCanvas != null) aiFindingsCanvas.gameObject.SetActive(false);
            SetDesktopFocusView(DesktopFocusView.SlabAxis, false);
            VolumeSTCubeFlatScreenHUD.NotifyWorkflowChanged();
            SetStatus("Returned to " + node.nodeId + "  /  " +
                OperationLabel(node.bornFrom) + ".");
        }

        private void DeleteAnalysisLeaf(AnalysisNodeState node)
        {
            if (node == null || !IsLeafNode(node.nodeId))
            {
                SetStatus("Only a leaf analysis node can be deleted.");
                return;
            }
            if (pendingDeleteNodeId != node.nodeId)
            {
                pendingDeleteNodeId = node.nodeId;
                SetStatus("Delete " + node.nodeId +
                    "? Select CONFIRM to remove its Grid and resources.");
                BuildTrailPanel();
                return;
            }
            pendingDeleteNodeId = string.Empty;
            AnalysisNodeState parent = FindAnalysisNode(node.parentNodeId);
            bool deletingCurrent = node == currentAnalysisNode;
            if (deletingCurrent)
            {
                currentAnalysisNode = null;
                s4dGridImage = null;
                s4dChartResultJson = string.Empty;
                s4dSnapshotId = string.Empty;
            }
            analysisNodes.Remove(node);
            if (node.gridImage != null)
                Destroy(node.gridImage);
            if (deletingCurrent && parent != null)
                NavigateToAnalysisNode(parent);
            else if (deletingCurrent)
            {
                placementConfirmed = false;
                stage = Stage.Field;
                if (facetGridCanvas != null)
                    facetGridCanvas.gameObject.SetActive(false);
                ReturnToSpatialWorkflow();
                BuildStage();
            }
            SetStatus("Deleted leaf " + node.nodeId + " and its Facet Grid.");
            if (trailCanvas != null)
            {
                trailCanvas.gameObject.SetActive(true);
                BuildTrailPanel();
            }
        }

        private AnalysisNodeState FindAnalysisNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
                return null;
            for (int index = 0; index < analysisNodes.Count; index++)
                if (analysisNodes[index].nodeId == nodeId)
                    return analysisNodes[index];
            return null;
        }

        private AnalysisNodeState CommitAnalysisNode(S4DFacetGridResult result)
        {
            DraftOperation operation = draftOperation;
            string parentId = !string.IsNullOrWhiteSpace(draftSourceNodeId)
                ? draftSourceNodeId
                : currentAnalysisNode != null ? currentAnalysisNode.nodeId : string.Empty;
            AnalysisNodeState node = new AnalysisNodeState
            {
                nodeId = "SLAB-" + (nextAnalysisNodeNumber++).ToString("00"),
                parentNodeId = parentId,
                bornFrom = operation,
                jobId = result != null ? result.JobId : string.Empty,
                snapshotId = result != null ? result.SnapshotId : string.Empty,
                datasetId = selectedDataset != null ? selectedDataset.DatasetId : string.Empty,
                variableId = selectedDataset != null ? selectedDataset.VariableId : string.Empty,
                datasetDirectory = selectedDataset != null ? selectedDataset.DirectoryPath : string.Empty,
                variableName = selectedDataset != null ? selectedDataset.Name : string.Empty,
                rawIntent = prompt,
                analysisQuestion = analysisQuestion,
                analyticTask = intentTask,
                intentDisplayLabel = intentMode,
                hasResolvedIntent = intentConfigured,
                title = OperationNodeTitle(operation),
                subtitle = DraftSelectionSummary(operation),
                gridImage = s4dGridImage,
                chartResultJson = s4dChartResultJson,
                timeBoundaryStart = timeBoundaryStart,
                timeBoundaryEnd = timeBoundaryEnd,
                depthBoundaryLow = depthBoundaryLow,
                depthBoundaryHigh = depthBoundaryHigh,
                roleValues = new[]
                {
                    (int)roles[0], (int)roles[1], (int)roles[2], (int)roles[3]
                },
                timeBuckets = CopyAnalysisBuckets(activeTimeBuckets),
                depthBuckets = CopyAnalysisBuckets(activeDepthBuckets),
                gridTransposed = activeGridTransposed,
                inspected = inspected,
                boundarySuspect = boundarySuspect,
                pinned = false,
                dismissed = false,
                staleCells = new bool[MaxFacetCells],
                verifiedCells = new bool[MaxFacetCells],
                suspectCells = new bool[MaxFacetCells],
                localizedCells = new bool[MaxFacetCells],
                pinnedCells = new bool[MaxFacetCells],
                sharedMinimum = s4dSharedMinimum,
                sharedMaximum = s4dSharedMaximum,
                sharedUnit = s4dSharedUnit,
                cellSnapshotIds = (string[])facetCellSnapshotIds.Clone(),
                digest = null,
                digestError = string.Empty,
                digestPending = false
            };
            analysisNodes.Add(node);
            currentAnalysisNode = node;
            return node;
        }

        private bool IsAnalysisNodeTexture(Texture2D texture)
        {
            for (int index = 0; index < analysisNodes.Count; index++)
                if (analysisNodes[index].gridImage == texture)
                    return true;
            return false;
        }

        private void ClearAnalysisHistory()
        {
            DestroyGroundAggregateVolume();
            ClearRetainedResultViews();
            for (int index = 0; index < analysisNodes.Count; index++)
            {
                Texture2D texture = analysisNodes[index].gridImage;
                if (texture != null)
                    Destroy(texture);
            }
            analysisNodes.Clear();
            trailEvents.Clear();
            nextTrailEventSequence = 1;
            currentAnalysisNode = null;
            nextAnalysisNodeNumber = 1;
            s4dGridImage = null;
            s4dChartResultJson = string.Empty;
            s4dSnapshotId = string.Empty;
            s4dJobId = string.Empty;
            currentDigest = null;
            digestError = string.Empty;
            SetPending(PendingJob.Digest, false);
            Array.Clear(facetCellSnapshotIds, 0,
                facetCellSnapshotIds.Length);
            activeTimeBuckets = null;
            activeDepthBuckets = null;
            activeGridColumns = 3;
            activeGridRows = 3;
            activeGridTransposed = false;
            pivotTransposed = false;
            draftSourceNodeId = string.Empty;
            draftOperation = DraftOperation.None;
        }

        private void CreateTrailNode(RectTransform parent, string title, string subtitle,
            Vector2 position, Color accent, Action action)
        {
            GameObject nodeObject = new GameObject(title, typeof(RectTransform));
            nodeObject.layer = 5;
            nodeObject.transform.SetParent(parent, false);
            RectTransform node = nodeObject.GetComponent<RectTransform>();
            node.sizeDelta = new Vector2(230, 104);
            node.anchoredPosition = position;
            nodeObject.AddComponent<Image>().color = Card;
            Outline outline = nodeObject.AddComponent<Outline>();
            outline.effectColor = accent;
            outline.effectDistance = new Vector2(3, -3);
            CreateText(node, title, 16, FontStyle.Bold, new Vector2(0, 20),
                new Vector2(200, 30), TextAnchor.MiddleCenter, Ink);
            CreateText(node, subtitle, 10, FontStyle.Bold, new Vector2(0, -22),
                new Vector2(204, 44), TextAnchor.MiddleCenter, accent);
            BoxCollider collider = nodeObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(230, 104, 12);
            nodeObject.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked = action;
        }

        private void CreateTrailLink(Vector2 from, Vector2 to, string label)
        {
            Vector2 delta = to - from;
            GameObject linkObject = new GameObject(label, typeof(RectTransform));
            linkObject.transform.SetParent(trailContent, false);
            RectTransform link = linkObject.GetComponent<RectTransform>();
            link.sizeDelta = new Vector2(delta.magnitude, 3);
            link.anchoredPosition = (from + to) * 0.5f;
            link.localRotation = Quaternion.Euler(0, 0,
                Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            linkObject.AddComponent<Image>().color = new Color(Muted.r, Muted.g, Muted.b, 0.72f);
            CreateText(trailContent, label, 11, FontStyle.Bold, (from + to) * 0.5f + Vector2.up * 14,
                new Vector2(100, 20), TextAnchor.MiddleCenter, Muted);
        }

        private void ToggleSelectedCellPin()
        {
            if (!gridCellSelected)
                return;
            selectedCellPinned = !selectedCellPinned;
            int index = SourceCellIndex(selectedGridColumn, selectedGridRow);
            facetCellPinned[Mathf.Clamp(index, 0, facetCellPinned.Length - 1)] =
                selectedCellPinned;
            if (currentAnalysisNode != null)
            {
                EnsureNodeGroundStatus(currentAnalysisNode);
                currentAnalysisNode.pinnedCells[Mathf.Clamp(index, 0,
                    currentAnalysisNode.pinnedCells.Length - 1)] =
                    selectedCellPinned;
            }
            SetStatus(selectedCellPinned
                ? CellLabel(selectedGridColumn, selectedGridRow) + " pinned to this Grid snapshot."
                : "Cell pin released; the selection remains active.");
            if (stage == Stage.Matrix && panelCanvas != null &&
                panelCanvas.gameObject.activeSelf)
                BuildStage();
            BuildFacetGridPanel();
        }

        private void DismissSelectedCell()
        {
            gridCellSelected = false;
            selectedCellPinned = false;
            SetFacetSelectionEvidencePreview(false);
            SetStatus("Findings closed. The Full Matrix remains anchored.");
            if (stage == Stage.Matrix && panelCanvas != null &&
                panelCanvas.gameObject.activeSelf)
                BuildStage();
            BuildFacetGridPanel();
        }

        private void UpdateNodeStaleDependencies(AnalysisNodeState node)
        {
            if (node == null)
                return;
            if (node.staleCells == null ||
                node.staleCells.Length != MaxFacetCells)
                node.staleCells = new bool[MaxFacetCells];
            Array.Clear(node.staleCells, 0, node.staleCells.Length);
            bool timeDependsOnBuckets = node.roleValues == null ||
                node.roleValues.Length < 1 ||
                (DimensionRole)node.roleValues[0] != DimensionRole.Mapped;
            bool depthDependsOnBuckets = node.roleValues == null ||
                node.roleValues.Length < 2 ||
                (DimensionRole)node.roleValues[1] != DimensionRole.Mapped;
            int timeBucketCount = node.timeBuckets != null
                ? Mathf.Min(node.timeBuckets.Length, MaxFacetAxisBuckets) : 0;
            int depthBucketCount = node.depthBuckets != null
                ? Mathf.Min(node.depthBuckets.Length, MaxFacetAxisBuckets) : 0;
            for (int depth = 0; depth < depthBucketCount; depth++)
            {
                for (int time = 0; time < timeBucketCount; time++)
                {
                    bool timeChanged = timeDependsOnBuckets &&
                        BucketChanged(node.timeBuckets, authoredTimeBuckets, time);
                    bool depthChanged = depthDependsOnBuckets &&
                        BucketChanged(node.depthBuckets, authoredDepthBuckets, depth);
                    node.staleCells[depth * timeBucketCount + time] =
                        timeChanged || depthChanged;
                }
            }
            node.stale = Array.Exists(node.staleCells, value => value);
        }

        private void CreateTrailPanel()
        {
            trailCanvas = CreateFloatingCanvas(
                "S4D SlabTrail",
                PrimaryToolDockPosition,
                new Vector2(920, 650),
                0.00068f,
                Purple);
            trailContent = trailCanvas.GetComponent<RectTransform>();
            trailCanvas.gameObject.SetActive(false);
        }

        private void ToggleTrailPanel()
        {
            if (trailCanvas == null)
                return;
            bool next = !trailCanvas.gameObject.activeSelf;
            if (next)
            {
                ShowPrimaryTool(trailCanvas);
                BuildTrailPanel();
            }
            else
            {
                trailCanvas.gameObject.SetActive(false);
            }
        }

        private void BuildTrailPanel()
        {
            if (trailContent == null)
                return;
            ClearChildren(trailContent);
            CreateText(trailContent, "QUESTION + EVIDENCE HISTORY", 28, FontStyle.Bold,
                new Vector2(0, 270), new Vector2(840, 44), TextAnchor.MiddleLeft, Ink);
            CreateText(trailContent, analysisQuestion,
                15, FontStyle.Normal, new Vector2(0, 235), new Vector2(840, 28),
                TextAnchor.MiddleLeft, Muted);

            Vector2 rootPosition = new Vector2(-315, 55);
            CreateTrailNode(trailContent, "FIELD", "ROOT", rootPosition,
                Cyan, () => NavigateFromTrail(Stage.Field));

            Dictionary<string, Vector2> positions = new Dictionary<string, Vector2>();
            List<AnalysisNodeState> visibleNodes = VisibleTrailNodes();
            Dictionary<int, int> nodesAtDepth = new Dictionary<int, int>();
            for (int index = 0; index < visibleNodes.Count; index++)
            {
                AnalysisNodeState node = visibleNodes[index];
                int depth = Mathf.Clamp(AnalysisNodeDepth(node), 1, 3);
                nodesAtDepth[depth] = nodesAtDepth.TryGetValue(depth, out int count)
                    ? count + 1
                    : 1;
            }
            Dictionary<int, int> placedAtDepth = new Dictionary<int, int>();
            for (int index = 0; index < visibleNodes.Count; index++)
            {
                AnalysisNodeState node = visibleNodes[index];
                int depth = Mathf.Clamp(AnalysisNodeDepth(node), 1, 3);
                int lane = placedAtDepth.TryGetValue(depth, out int placed)
                    ? placed
                    : 0;
                placedAtDepth[depth] = lane + 1;
                int laneCount = nodesAtDepth[depth];
                float y = laneCount == 1
                    ? 55
                    : 165 - lane * (300.0f / (laneCount - 1));
                Vector2 nodePosition = new Vector2(-315 + depth * 225, y);
                positions[node.nodeId] = nodePosition;
            }
            for (int index = 0; index < visibleNodes.Count; index++)
            {
                AnalysisNodeState node = visibleNodes[index];
                Vector2 nodePosition = positions[node.nodeId];
                Vector2 parentPosition = rootPosition;
                if (!string.IsNullOrEmpty(node.parentNodeId) &&
                    positions.TryGetValue(node.parentNodeId, out Vector2 visibleParent))
                    parentPosition = visibleParent;
                CreateTrailLink(parentPosition + Vector2.right * 105,
                    nodePosition - Vector2.right * 105, OperationLabel(node.bornFrom));
                Color accent = node.stale || node.boundarySuspect ? Amber :
                    node == currentAnalysisNode ? Green : OperationColor(node.bornFrom);
                AnalysisNodeState selectedNode = node;
                CreateTrailNode(trailContent, node.title,
                    TrailNodeSubtitle(node), nodePosition, accent,
                    () => NavigateToAnalysisNode(selectedNode));
                if (IsLeafNode(node.nodeId))
                {
                    bool confirmingDelete = pendingDeleteNodeId == node.nodeId;
                    CreateButton(trailContent,
                        confirmingDelete ? "CONFIRM" : "DELETE",
                        nodePosition + new Vector2(62, -62), new Vector2(92, 26),
                        Danger, () => DeleteAnalysisLeaf(selectedNode));
                }
            }

            CreateText(trailContent,
                analysisNodes.Count == 0
                    ? "No committed analysis yet. Drafts do not enter SlabTrail."
                    : analysisNodes.Count + " COMMITTED  /  " + LeafNodeCount() +
                        " LEAF  /  Latest: " +
                        analysisNodes[analysisNodes.Count - 1].nodeId + "  " +
                        OperationLabel(analysisNodes[analysisNodes.Count - 1].bornFrom),
                14, FontStyle.Bold, new Vector2(0, -190), new Vector2(820, 28),
                TextAnchor.MiddleLeft, inspected ? Green : boundarySuspect ? Amber : Muted);
            CreateText(trailContent, RecentTrailEventSummary(),
                10, FontStyle.Bold, new Vector2(0, -238),
                new Vector2(820, 88), TextAnchor.MiddleLeft, Purple);
            CreateButton(trailContent, "Arrange Workspace", new Vector2(-190, -304),
                new Vector2(330, 56), Cyan, ArrangeWorkspace);
            CreateButton(trailContent, "Close", new Vector2(230, -304),
                new Vector2(220, 56), Card, ToggleTrailPanel);
        }

        private string RecentTrailEventSummary()
        {
            if (trailEvents.Count == 0)
                return "EVENT LOG  /  Variable, role, boundary, intent, Grid and Ground actions appear here.";
            int start = Mathf.Max(0, trailEvents.Count - 2);
            List<string> labels = new List<string>();
            for (int index = start; index < trailEvents.Count; index++)
            {
                TrailEventState item = trailEvents[index];
                labels.Add("#" + item.sequence + " " + item.nodeId + " " +
                    item.kind + "  " + item.detail);
            }
            return string.Join("\n", labels.ToArray());
        }

        private void RecordTrailEvent(string kind, string detail,
            AnalysisNodeState node = null)
        {
            AnalysisNodeState owner = node ?? currentAnalysisNode;
            trailEvents.Add(new TrailEventState
            {
                sequence = nextTrailEventSequence++,
                nodeId = owner != null ? owner.nodeId : "ROOT",
                kind = kind,
                detail = detail
            });
            if (trailEvents.Count > 48)
                trailEvents.RemoveAt(0);
            if (trailCanvas != null && trailCanvas.gameObject.activeSelf)
                BuildTrailPanel();
        }

        private List<AnalysisNodeState> VisibleTrailNodes()
        {
            const int recentNodeCount = 6;
            HashSet<string> visibleIds = new HashSet<string>();
            int recentStart = Mathf.Max(0, analysisNodes.Count - recentNodeCount);
            for (int index = recentStart; index < analysisNodes.Count; index++)
            {
                visibleIds.Add(analysisNodes[index].nodeId);
                AnalysisNodeState ancestor = FindAnalysisNode(
                    analysisNodes[index].parentNodeId);
                int ancestorGuard = 0;
                while (ancestor != null && ancestorGuard++ <= analysisNodes.Count)
                {
                    visibleIds.Add(ancestor.nodeId);
                    ancestor = FindAnalysisNode(ancestor.parentNodeId);
                }
            }

            AnalysisNodeState cursor = currentAnalysisNode ??
                (analysisNodes.Count > 0 ? analysisNodes[analysisNodes.Count - 1] : null);
            int guard = 0;
            while (cursor != null && guard++ <= analysisNodes.Count)
            {
                visibleIds.Add(cursor.nodeId);
                cursor = FindAnalysisNode(cursor.parentNodeId);
            }

            List<AnalysisNodeState> result = new List<AnalysisNodeState>();
            for (int index = 0; index < analysisNodes.Count; index++)
                if (visibleIds.Contains(analysisNodes[index].nodeId))
                    result.Add(analysisNodes[index]);
            return result;
        }

        private string TrailNodeSubtitle(AnalysisNodeState node)
        {
            string parent = string.IsNullOrWhiteSpace(node.parentNodeId)
                ? "FIELD"
                : node.parentNodeId;
            string state = node.stale ? "STALE" :
                node.boundarySuspect ? "SUSPECT" :
                node.localizedCells != null && Array.Exists(node.localizedCells, value => value)
                    ? "LOCAL" :
                node.inspected ? "VERIFIED" :
                node.pinned ? "PINNED" : "READY";
            string digestState = node.digestPending ? "  /  DIGEST..." :
                node.digest != null ? "  /  DIGEST READY" :
                !string.IsNullOrWhiteSpace(node.digestError)
                    ? "  /  DIGEST FALLBACK" : string.Empty;
            string task = string.IsNullOrWhiteSpace(node.analyticTask)
                ? "ANALYSIS" : node.analyticTask.Replace("_", " ").ToUpperInvariant();
            return node.nodeId + "  /  " + task +
                "\nFROM " + parent + "  /  " + state + digestState;
        }

        private bool IsLeafNode(string nodeId)
        {
            for (int index = 0; index < analysisNodes.Count; index++)
                if (analysisNodes[index].parentNodeId == nodeId)
                    return false;
            return true;
        }

        private int LeafNodeCount()
        {
            int count = 0;
            for (int index = 0; index < analysisNodes.Count; index++)
                if (IsLeafNode(analysisNodes[index].nodeId))
                    count++;
            return count;
        }
    }
}
