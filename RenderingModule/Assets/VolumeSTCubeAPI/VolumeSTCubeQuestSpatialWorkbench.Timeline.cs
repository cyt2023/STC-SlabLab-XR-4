using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    /// <summary>Split out of VolumeSTCubeQuestSpatialWorkbench: Timeline side of the workbench.</summary>
    public sealed partial class VolumeSTCubeQuestSpatialWorkbench
    {
        public void DesktopTogglePlayback()
        {
            if (stage != Stage.Field || !boundaryEditActive)
            {
                RejectDesktopAction("Open Set Time Range before using playback.");
                return;
            }
            if (forVrSurfacePlayer == null)
            {
                RejectDesktopAction("Playback is not ready yet.");
                return;
            }
            forVrSurfacePlayer.TogglePlayback();
        }

        public void DesktopCyclePlaybackSpeed()
        {
            if (stage != Stage.Field || !boundaryEditActive)
            {
                RejectDesktopAction("Open Set Time Range before changing playback speed.");
                return;
            }
            if (forVrSurfacePlayer == null)
            {
                RejectDesktopAction("Playback is not ready yet.");
                return;
            }
            forVrSurfacePlayer.CyclePlaybackSpeed();
        }

        private void RebuildTimeMarkers()
        {
            for (int i = 0; i < timeMarkers.Count; i++)
                Destroy(timeMarkers[i]);
            timeMarkers.Clear();
            if (selectedDataset == null)
                return;

            Transform rail = spatialRoot.transform.Find("Day 1 to Day 30 rail");
            int groundTimeFirst = 0;
            int groundTimeLast = -1;
            int ignoredDepthFirst;
            int ignoredDepthLast;
            bool hasGroundRange = false;
            if (groundDocked)
                hasGroundRange = TryGetGroundBucketRanges(
                    out groundTimeFirst, out groundTimeLast,
                    out ignoredDepthFirst, out ignoredDepthLast);
            for (int i = 0; i < selectedDataset.TimeCount; i++)
            {
                int timeIndex = i;
                bool inGroundRange = hasGroundRange &&
                    i >= groundTimeFirst && i <= groundTimeLast;
                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "Time " + (i + 1);
                marker.layer = 5;
                marker.transform.SetParent(rail, false);
                float x = Mathf.Lerp(-TimeRailHalfWidth, TimeRailHalfWidth,
                    i / (float)Mathf.Max(1, selectedDataset.TimeCount - 1));
                marker.transform.localPosition = new Vector3(x, 0.0f, 0.0f);
                marker.transform.localScale = Vector3.one *
                    (i == selectedTime ? 0.074f : inGroundRange ? 0.046f : 0.026f);
                Material material = new Material(Shader.Find("Sprites/Default"));
                material.color = i == selectedTime
                    ? Amber
                    : inGroundRange
                        ? TimeColor
                        : new Color(0.20f, 0.37f, 0.44f, 0.78f);
                marker.GetComponent<Renderer>().material = material;
                VolumeSTCubeQuestClickTarget target = marker.AddComponent<VolumeSTCubeQuestClickTarget>();
                target.Clicked = () => SetTime(timeIndex);
                timeMarkers.Add(marker);
            }
        }

        private string[] TimeBucketButtonLabels()
        {
            if (authorBoundaryConfirmed && authoredTimeBuckets != null &&
                authoredTimeBuckets.Length == 3)
                return AuthoredBucketButtonLabels(authoredTimeBuckets, true);
            int count = selectedDataset != null ? selectedDataset.TimeCount : 30;
            int firstCut = Mathf.Clamp(timeBoundaryStart, 1, Mathf.Max(1, count - 2));
            int secondCut = Mathf.Clamp(timeBoundaryEnd + 1, firstCut + 1, count - 1);
            return new[]
            {
                "BEFORE\n1-" + firstCut,
                "DURING\n" + (firstCut + 1) + "-" + secondCut,
                "AFTER\n" + (secondCut + 1) + "-" + count
            };
        }

        private int DisplayTimeBucketIndex(int column, int row)
        {
            return activeGridTransposed ? row : column;
        }

        private void NudgeFixedTime(int direction)
        {
            if (selectedDataset == null)
                return;
            SetTime(Mathf.Clamp(selectedTime + direction, 0,
                selectedDataset.TimeCount - 1));
            InvalidateSlabConfiguration("Fixed Time changed");
            BuildStage();
        }

        private System.Collections.IEnumerator PlayGroundTimeBucket()
        {
            int timeIndex = DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow);
            if (activeTimeBuckets == null || timeIndex < 0 ||
                timeIndex >= activeTimeBuckets.Length ||
                activeTimeBuckets[timeIndex] == null ||
                activeTimeBuckets[timeIndex].indices == null ||
                activeTimeBuckets[timeIndex].indices.Length == 0)
            {
                groundPlaybackCoroutine = null;
                yield break;
            }

            int[] frames = activeTimeBuckets[timeIndex].indices;
            int cursor = 0;
            while (groundDocked && stage == Stage.Analyze &&
                   groundMode == GroundMode.Playback)
            {
                int frame = Mathf.Clamp(frames[cursor], 0, selectedDataset.TimeCount - 1);
                selectedTime = frame;
                ApplyTimeFilter();
                RefreshSlabTexture();
                UpdateSlabVisual(false);
                RebuildTimeMarkers();
                SetStatus("Playback  " + CellLabel(selectedGridColumn, selectedGridRow) +
                    "  |  " + selectedDataset.GetTimeLabel(frame) +
                    "  (" + (cursor + 1) + "/" + frames.Length + ")");
                cursor = (cursor + 1) % frames.Length;
                yield return new WaitForSeconds(1.35f);
            }
            groundPlaybackCoroutine = null;
        }

        private void StopGroundPlayback()
        {
            if (groundPlaybackCoroutine == null)
                return;
            StopCoroutine(groundPlaybackCoroutine);
            groundPlaybackCoroutine = null;
        }

        private void SetTime(int timeIndex)
        {
            if (selectedDataset == null)
                return;
            selectedTime = Mathf.Clamp(timeIndex, 0, selectedDataset.TimeCount - 1);
            if (forVrSurfacePlayer != null)
                forVrSurfacePlayer.ShowFrame(selectedTime, false);
            else
            {
                ApplyTimeFilter();
                FrameVolume();
                StartCoroutine(RefitVolumeAfterFrameChange());
            }
            // Multi-variable views each own a current-day 3D volume. Rebuild
            // only entries whose day changed; never replace them with slices.
            RefreshVariableFacetStacks();
            RefreshSlabTexture();
            RebuildTimeMarkers();
            SetStatus(IsForVrSurfaceDataset
                ? "Surface time: " + selectedDataset.GetTimeLabel(selectedTime) + "."
                : "Time pivot: " + selectedDataset.GetTimeLabel(selectedTime) +
                    ", z=" + selectedZ + ".");
            BuildStage();
        }

        private void OnForVrSurfaceTimeChanged(int timeIndex)
        {
            if (selectedDataset == null)
                return;
            selectedTime = Mathf.Clamp(timeIndex, 0, selectedDataset.TimeCount - 1);
            // Playback reads the 2D surface frame directly. Avoid scheduling a
            // hidden 3D texture upload on every tick; the selected index is
            // still used by the downstream S4D request when playback pauses.
        }

        private void ApplyTimeFilter()
        {
            if (currentView == null || selectedDataset == null)
                return;
            float minimum = selectedTime / (float)selectedDataset.TimeCount;
            float maximum = (selectedTime + 1) / (float)selectedDataset.TimeCount;
            currentView.ApplyTimeFilter(minimum, maximum);
            VolumeControllerObject controller = currentView.GetManagedController();
            if (controller == null && currentView.rootObject != null)
                controller = currentView.rootObject.GetComponent<VolumeControllerObject>();
            VolumeSTCubeOriginalSceneAdapter.ApplyVariableOpacityPreset(
                controller, selectedDataset.Name, 0.82f);
        }

        private void InvokeButtonWithoutInterruptingPlayback(Action action)
        {
            VolumeSTCubeForVrSurfacePlayer playerBefore = forVrSurfacePlayer;
            bool wasPlaying = playerBefore != null && playerBefore.IsPlaying;
            action?.Invoke();
            // Normal workflow buttons may rebuild panels, boundaries and
            // previews, but they do not own the independent surface player.
            // If the same player survived the action, preserve its running
            // state and current frame. START/PAUSE lives in the player itself
            // and therefore remains the sole playback toggle.
            if (wasPlaying && playerBefore != null &&
                playerBefore == forVrSurfacePlayer)
                playerBefore.EnsurePlaybackContinues();
        }
    }
}
