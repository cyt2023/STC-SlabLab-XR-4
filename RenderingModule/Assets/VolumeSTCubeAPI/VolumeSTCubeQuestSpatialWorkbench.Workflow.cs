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
        private void SelectApplicationMode(VolumeSTCubeApplicationMode mode)
        {
            if (VolumeSTCubeMode.Current == mode)
            {
                SetStatus(mode == VolumeSTCubeApplicationMode.Desktop
                    ? "Desktop mode is active."
                    : "VR mode is active.");
                BuildStage();
                return;
            }
            VolumeSTCubeMode.SelectAndReload(mode);
        }

        public void DesktopPreviousStep()
        {
            if (DesktopOperationPending())
            {
                RejectDesktopAction("Please wait for the current operation to finish.");
                return;
            }
            if (stage == Stage.Field && boundaryEditActive)
            {
                RejectDesktopAction(
                    "Confirm or cancel the current Time Range edit before going back.");
                return;
            }
            switch (stage)
            {
                case Stage.Field: OpenDatasetImportStage(); break;
                case Stage.Slab: NavigateDesktopStep(Stage.Field); break;
                case Stage.Matrix: NavigateDesktopStep(Stage.Slab); break;
                case Stage.Analyze: NavigateDesktopStep(Stage.Matrix); break;
                case Stage.Result: NavigateDesktopStep(Stage.Analyze); break;
                default: RejectDesktopAction("This is the first step."); break;
            }
        }

        public void DesktopNextStep()
        {
            if (DesktopOperationPending())
            {
                RejectDesktopAction("Please wait for the current operation to finish.");
                return;
            }
            switch (stage)
            {
                case Stage.DatasetImport:
                    if (datasets.Count == 0)
                        RejectDesktopAction(
                            waveClient != null && waveClient.IsImporting
                                ? "Wait for the Wave server connection to finish."
                                : "Live Wave data is not ready. Retry the connection.");
                    else
                        ConfirmDatasetImport();
                    break;
                case Stage.Field:
                    if (boundaryEditActive)
                        RejectDesktopAction(
                            "Confirm or cancel the current Time Range edit first.");
                    else if (!authorBoundaryConfirmed)
                        OpenInitialAuthorBoundary();
                    else
                        NavigateDesktopStep(Stage.Slab);
                    break;
                case Stage.Slab:
                    if (!intentConfigured)
                        DesktopOpenIntent();
                    else
                        DesktopBuildFullMatrix();
                    break;
                case Stage.Matrix:
                    if (s4dGridImage == null)
                        RejectDesktopAction(
                            "Build the Full Matrix before continuing.");
                    else if (!gridCellSelected)
                        RejectDesktopAction(
                            "Choose a Matrix cell before continuing.");
                    else
                        SelectS4DGridCell(selectedGridColumn, selectedGridRow);
                    break;
                case Stage.Analyze: NavigateDesktopStep(Stage.Result); break;
                default: RejectDesktopAction("This is the final step."); break;
            }
        }

        private void NavigateDesktopStep(Stage next)
        {
            if (DesktopOperationPending())
            {
                RejectDesktopAction("Wait for the current operation to finish.");
                return;
            }
            // Never advance into a stage the workspace cannot serve. Without a
            // dataset there is no shared axis controller and no manifest, so
            // every later action would fail with a message about missing
            // internals. Send the operator back to the import step instead.
            if (next != Stage.DatasetImport && datasets.Count == 0)
            {
                RejectDesktopAction(
                    "No dataset is loaded yet. Reconnect the Wave server, " +
                    "then continue.");
                OpenDatasetImportStage();
                return;
            }
            if (boundaryCanvas != null)
                boundaryCanvas.gameObject.SetActive(false);
            if (slabPreviewCanvas != null)
                slabPreviewCanvas.gameObject.SetActive(false);
            if (intentCanvas != null)
                intentCanvas.gameObject.SetActive(false);
            if (draftCanvas != null)
                draftCanvas.gameObject.SetActive(false);
            if (facetGridCanvas != null)
                facetGridCanvas.gameObject.SetActive(false);
            if (aiFindingsCanvas != null)
                aiFindingsCanvas.gameObject.SetActive(false);
            boundaryEditActive = false;
            initialBoundarySetupActive = false;
            SetTimeBoundaryHandleVisibility(false);
            SetDepthBoundaryVisibility(false);
            if (next != Stage.Analyze && next != Stage.Result)
                SetGroundDock(false);
            if (next == Stage.Field)
            {
                if (spatialRoot != null)
                    spatialRoot.SetActive(true);
                if (forVrSurfacePlayer != null)
                {
                    forVrSurfacePlayer.CloseCombinedXytTimeSelection();
                    forVrSurfacePlayer.SetVisible(true);
                }
                if (spatialAxisComposerRoot != null)
                    spatialAxisComposerRoot.SetActive(false);
                if (variablePaletteRoot != null)
                    variablePaletteRoot.SetActive(false);
            }
            stage = next;
            ShowPrimaryTool(panelCanvas);
            BuildStage();
            VolumeSTCubeFlatScreenHUD.NotifyWorkflowChanged();
        }

        private void RestartApplicationWorkflow()
        {
            // Return to the first screen without restarting the Android process.
            // Clear every transient analysis object first so re-entering the
            // workspace cannot reveal a previous Matrix, draft, or boundary.
            if (s4dClient != null)
                s4dClient.Cancel();
            SetPending(PendingJob.Analysis, false);
            SetPending(PendingJob.SourcePreview, false);
            SetPending(PendingJob.Digest, false);
            SetPending(PendingJob.Intent, false);
            intentConfigured = false;
            slabPreviewBuilt = false;
            spatialWorkflowStep = SpatialWorkflowStep.AxisBinding;
            draftOperation = DraftOperation.None;
            if (gridProgressAnimation != null)
            {
                StopCoroutine(gridProgressAnimation);
                gridProgressAnimation = null;
            }

            ClearSourcePreviewLayers();
            ClearAnalysisHistory();
            ClearChart();
            ClearS4DGrid();
            ClearPairedVariableVolumes();
            ResetAxisBucketSelection();
            Array.Clear(facetCellPinned, 0, facetCellPinned.Length);
            Array.Clear(facetCellInspected, 0, facetCellInspected.Length);
            Array.Clear(facetCellBoundarySuspect, 0,
                facetCellBoundarySuspect.Length);
            Array.Clear(facetCellLocalized, 0, facetCellLocalized.Length);
            Array.Clear(facetCellStale, 0, facetCellStale.Length);

            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                SpatialAxisRigState state = spatialAxisRigStates[index];
                state.boundVariable = -1;
                state.timeAxis = -1;
                state.depthAxis = -1;
                state.variableAxis = -1;
                state.timeRole = DimensionRole.Faceted;
                state.depthRole = DimensionRole.Faceted;
                state.usesSharedBoundaries = true;
                state.pendingDockAnimation = false;
                state.hasCustomDock = false;
            }
            roles[0] = DimensionRole.Faceted;
            roles[1] = DimensionRole.Faceted;
            roles[2] = DimensionRole.Mapped;
            roles[3] = DimensionRole.Fixed;
            prompt = DefaultSpatialPrompt;
            progress = 0.0f;
            displayedGridProgress = 0.0f;
            targetGridProgress = 0.0f;
            selectedCellPinned = false;
            gridCellSelected = false;
            boundaryEditActive = false;
            initialBoundarySetupActive = false;

            OpenDatasetImportStage();
            SetStatus(datasets.Count > 0
                ? "Application restarted with the live Wave dataset."
                : "Application restarted. Reconnect to the Wave server.");
        }

        private void ReturnToSpatialWorkflow()
        {
            if (viewState.legacyPanelVisible && panelCanvas != null)
                ShowPrimaryTool(panelCanvas);
            else if (panelCanvas != null)
                panelCanvas.gameObject.SetActive(false);
            if (workflowToolbarCanvas != null)
                workflowToolbarCanvas.gameObject.SetActive(mainWorkspaceEntered);
        }

        private void ReturnToSlabFromPlacement()
        {
            SetFacetSelectionEvidencePreview(false);
            facetGridCanvas.gameObject.SetActive(false);
            ReturnToSpatialWorkflow();
            Navigate(Stage.Slab);
        }

        private void Navigate(Stage next)
        {
            if (IsPending(PendingJob.Analysis))
                return;
            if (!datasetImportConfirmed && next != Stage.DatasetImport)
            {
                SetStatus("Import and confirm a compatible dataset first.");
                stage = Stage.DatasetImport;
                BuildStage();
                return;
            }
            bool matrixResultCanContinue = stage == Stage.Matrix &&
                next == Stage.Analyze && s4dGridImage != null;
            if (selectedDataset == null && next != Stage.Field &&
                !matrixResultCanContinue)
            {
                SetStatus("Choose a field first.");
                return;
            }
            if (next == Stage.Slab && !mainWorkspaceEntered &&
                !authorBoundaryConfirmed &&
                draftOperation == DraftOperation.None)
            {
                OpenInitialAuthorBoundary();
                return;
            }
            if (next == Stage.Result && s4dGridImage == null)
            {
                SetStatus("Finding is available after a Full Matrix is committed.");
                return;
            }
            if (next != Stage.Analyze && next != Stage.Result)
                SetGroundDock(false);
            stage = next;
            BuildStage();
            if (next == Stage.Slab && slabPreviewBuilt && slabPreviewCanvas != null)
            {
                ShowComposerTool(slabPreviewCanvas);
                BuildSlabPreviewPanel();
            }
        }

        /// <summary>
        /// Unity's script-domain reload can preserve the generated Matrix texture
        /// while dropping the plain C# dataset reference that produced it. Restore
        /// that reference before redrawing the desktop Matrix task surface.
        /// </summary>
        private void RestoreSelectedDatasetForMatrix()
        {
            if (selectedDataset != null || datasets.Count == 0)
                return;

            int variableIndex = -1;
            if (materializationVariableIndices.Count > 0)
                variableIndex = materializationVariableIndices[
                    Mathf.Clamp(materializationVariableCursor, 0,
                        materializationVariableIndices.Count - 1)];
            if (variableIndex < 0 || variableIndex >= datasets.Count)
                variableIndex = importSelectedVariableIndex;
            if (variableIndex < 0 || variableIndex >= datasets.Count)
            {
                List<int> activeVariables = ActiveBoundVariableIndices();
                if (activeVariables.Count > 0)
                    variableIndex = activeVariables[0];
            }
            if (variableIndex < 0 || variableIndex >= datasets.Count)
                variableIndex = 0;

            selectedDataset = datasets[variableIndex];
            importSelectedVariableIndex = variableIndex;
        }

        private bool HasSavedAuthorBoundaries()
        {
            return authorBoundaryConfirmed &&
                authoredTimeBuckets != null && authoredTimeBuckets.Length > 0 &&
                authoredDepthBuckets != null && authoredDepthBuckets.Length > 0;
        }

        private void EnsureSavedAuthorBoundaries()
        {
            if (HasSavedAuthorBoundaries() || selectedDataset == null)
                return;
            // The numeric cuts/selected values were committed in the Field
            // setup. Rebuild only their request buckets if a later variable or
            // axis refresh discarded the cached arrays; never reopen authoring.
            CommitAuthorBoundaryBuckets();
            authorBoundaryConfirmed = authoredTimeBuckets != null &&
                authoredTimeBuckets.Length > 0 &&
                authoredDepthBuckets != null && authoredDepthBuckets.Length > 0;
        }

        private void EnterMainWorkspace()
        {
            if (!authorBoundaryConfirmed || selectedDataset == null)
            {
                OpenInitialAuthorBoundary();
                return;
            }
            preconfigurationActive = false;
            mainWorkspaceEntered = true;
            EnsureSavedAuthorBoundaries();
            stage = Stage.Slab;
            viewState.legacyPanelVisible = false;
            spatialWorkflowStep = SpatialWorkflowStep.AxisBinding;
            if (spatialAxisComposerRoot != null)
                spatialAxisComposerRoot.SetActive(true);
            if (workflowToolbarCanvas != null)
                workflowToolbarCanvas.gameObject.SetActive(true);
            if (panelCanvas != null)
                panelCanvas.gameObject.SetActive(false);
            RefreshSpatialAxisControllers();
            UpdateVariablePaletteFollow(true);
            SetStatus("Ranges saved. Drag one or more variables, Time, and Depth onto the tri-axis controller.");
            BuildWorkflowToolbar();
            BuildStage();
        }

        /// <summary>
        /// Neutral panel for a later workflow step that has lost its dataset —
        /// a Wave reconnect clears the live dataset while the workspace keeps
        /// its saved Time and Depth ranges.
        /// </summary>
        private void BuildWaitingForDatasetStage()
        {
            bool connecting = waveClient != null && waveClient.IsImporting;
            CreateText(panelContent, "WAITING FOR LIVE DATA", 26, FontStyle.Bold,
                new Vector2(0, 44), new Vector2(1030, 40),
                TextAnchor.MiddleCenter, Amber);
            CreateText(panelContent,
                connecting
                    ? "Reconnecting to the Wave server. The saved Time and Depth " +
                      "ranges are kept; this step continues when the dataset is back."
                    : "The live dataset is unavailable. Retry the connection from " +
                      "Step 1 to continue.",
                16, FontStyle.Normal, new Vector2(0, -12), new Vector2(980, 70),
                TextAnchor.MiddleCenter, Muted);
        }

        private void NavigateFromTrail(Stage next)
        {
            trailCanvas.gameObject.SetActive(false);
            if (next == Stage.Matrix && placementConfirmed && facetGridCanvas != null)
            {
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
                if (panelCanvas != null)
                    panelCanvas.gameObject.SetActive(false);
                return;
            }
            ReturnToSpatialWorkflow();
            Navigate(next);
        }

        private void ArrangeWorkspace()
        {
            if (panelCanvas != null)
                panelCanvas.transform.localPosition = PrimaryToolDockPosition;
            if (mainMenuCanvas != null)
                mainMenuCanvas.transform.localPosition = PrimaryToolDockPosition;
            if (boundaryCanvas != null)
                boundaryCanvas.transform.localPosition = BoundaryToolDockPosition;
            if (trailCanvas != null)
                trailCanvas.transform.localPosition = PrimaryToolDockPosition;
            SetStatus("Workspace arranged. The cube stays central and the active tool is docked on its right.");
            BuildTrailPanel();
        }
    }
}
