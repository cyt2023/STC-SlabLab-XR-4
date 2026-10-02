using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    /// <summary>Split out of VolumeSTCubeQuestSpatialWorkbench: Interaction side of the workbench.</summary>
    public sealed partial class VolumeSTCubeQuestSpatialWorkbench
    {
        /// <summary>
        /// Desktop keyboard entry point, called by the flat-screen HUD with the
        /// key it saw. Returns true when the shell acted on it.
        ///
        /// Typing owns the keyboard: while the desktop prompt or a text field is
        /// open every shortcut is ignored, so a stray key press cannot step the
        /// workflow underneath the operator. Stepping itself goes through the
        /// same DesktopPreviousStep / DesktopNextStep the buttons call — there is
        /// no second navigation path to keep in sync.
        /// </summary>
        public bool DesktopHandleShortcut(KeyCode key)
        {
            if (input.textInputActive || input.desktopEditingPrompt)
                return false;
            switch (SlabLabShortcuts.Map(key))
            {
                case SlabLabShortcut.PreviousStep:
                    DesktopPreviousStep();
                    return true;
                case SlabLabShortcut.NextStep:
                    DesktopNextStep();
                    return true;
                case SlabLabShortcut.RangeEntry:
                    return DesktopRangeEntry();
                default:
                    return false;
            }
        }

        private void OnAggregatePreviewComplete(Texture2D atlas, string error)
        {
            SetPending(PendingJob.Analysis, false);
            if (atlas == null)
            {
                SetStatus(string.IsNullOrWhiteSpace(error)
                    ? "Aggregate preview returned no image."
                    : error);
            }
            else
            {
                matrixPreviewAtlas = atlas;
                SetStatus(
                    "Preview ready: interval mean, missing values excluded, shared scale across all " +
                        Mathf.Max(1, activeGridColumns * activeGridRows) + " cells.");
            }
            if (slabPreviewCanvas != null && slabPreviewCanvas.gameObject.activeSelf)
                BuildSlabPreviewPanel();
            BuildStage();
        }

        private void NudgeFixedDepth(int direction)
        {
            if (selectedDataset == null)
                return;
            selectedZ = Mathf.Clamp(selectedZ + direction, 0,
                selectedDataset.DimZ - 1);
            slabNormalized = selectedDataset.DimZ > 1
                ? selectedZ / (float)(selectedDataset.DimZ - 1)
                : 0.5f;
            UpdateSlabVisual(true);
            RefreshSlabTexture();
            RefreshVariableFacetStacks();
            InvalidateSlabConfiguration("Fixed Depth changed to z=" + selectedZ);
            BuildStage();
        }

        private void ToggleDataVisibility()
        {
            viewState.cubeVisible = !viewState.cubeVisible;
            if (spatialRoot != null)
                spatialRoot.SetActive(viewState.cubeVisible);
            if (currentView != null)
                currentView.SetVisible(viewState.cubeVisible &&
                    roles[3] != DimensionRole.Faceted &&
                    !(groundDocked && groundMode == GroundMode.Aggregate &&
                      groundAggregateVolume != null));
            SetGroundAggregateVisible(viewState.cubeVisible && groundDocked &&
                groundMode == GroundMode.Aggregate);
            SetStatus(viewState.cubeVisible ? "Continuous Data Container shown." : "Continuous Data Container hidden.");
            BuildMainMenu();
        }

        private void ToggleDataPresentation()
        {
            smallMultiples = !smallMultiples;
            SetStatus(smallMultiples
                ? "Small-multiples presentation selected. Time remains linked to the same continuous field."
                : "Animated-volume presentation selected. Use the time rail to scrub.");
            BuildMainMenu();
        }

        private string TickSelectionSummary()
        {
            string time = SelectedDraftBucketLabels(0);
            string depth = SelectedDraftBucketLabels(1);
            if (!string.IsNullOrEmpty(time) && !string.IsNullOrEmpty(depth))
                return time + " + " + depth;
            return !string.IsNullOrEmpty(time) ? time :
                !string.IsNullOrEmpty(depth) ? depth : "no buckets";
        }

        private void ToggleRegionDrawing()
        {
            interaction.drawingRegion = !interaction.drawingRegion;
            SetStatus(interaction.drawingRegion ? "Draw mode: drag directly across the cyan slab." : "Draw mode cancelled.");
            BuildStage();
        }

        private void UpdateRegionVisual()
        {
            if (regionRoot == null)
                return;
            Vector2 center = region.center;
            float radius = Mathf.Min(region.width, region.height) * 0.5f;
            for (int index = 0; index < regionLines.Length; index++)
            {
                float angleA = Mathf.PI * 2.0f * index / regionLines.Length;
                float angleB = Mathf.PI * 2.0f * (index + 1) / regionLines.Length;
                Vector3 a = new Vector3(
                    (center.x + Mathf.Cos(angleA) * radius - 0.5f) * 1.08f,
                    0.0f,
                    (center.y + Mathf.Sin(angleA) * radius - 0.5f) * 1.08f);
                Vector3 b = new Vector3(
                    (center.x + Mathf.Cos(angleB) * radius - 0.5f) * 1.08f,
                    0.0f,
                    (center.y + Mathf.Sin(angleB) * radius - 0.5f) * 1.08f);
                SetLine(regionLines[index], a, b);
            }
        }

        private void OnJobProgress(string message, float value)
        {
            progress = value;
            SetStatus(message + " (" + Mathf.RoundToInt(value * 100) + "%)");
        }

        private void OnJobComplete(VolumeSTCubeMatPlotResult result)
        {
            SetPending(PendingJob.Analysis, false);
            if (result == null || !result.Succeeded)
            {
                SetStatus(result != null ? result.Error : "MatPlotAgent returned no result.");
                BuildStage();
                return;
            }
            ClearChart();
            chartImage = result.Image;
            stage = Stage.Result;
            SetStatus("Verified result ready. Job " + result.JobId + ".");
            BuildStage();
        }

        private void CenterRegion()
        {
            interaction.drawingRegion = false;
            region = new Rect(0.28f, 0.28f, 0.44f, 0.44f);
            UpdateRegionVisual();
            SetStatus("Centered region restored.");
            BuildStage();
        }
    }
}
