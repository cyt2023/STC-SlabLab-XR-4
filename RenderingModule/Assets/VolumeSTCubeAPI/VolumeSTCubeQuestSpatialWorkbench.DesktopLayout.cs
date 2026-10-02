using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    /// <summary>Split out of VolumeSTCubeQuestSpatialWorkbench: DesktopLayout side of the workbench.</summary>
    public sealed partial class VolumeSTCubeQuestSpatialWorkbench
    {
        public void DesktopOpenFieldSetup()
        {
            if (stage != Stage.Field)
            {
                RejectDesktopAction(
                    "Time Range is only available in Step 2: Configure Field.");
                return;
            }
            if (DesktopOperationPending())
            {
                RejectDesktopAction("Please wait for the current operation to finish.");
                return;
            }
            if (authorBoundaryConfirmed)
                EnterMainWorkspace();
            else
                OpenInitialAuthorBoundary();
        }

        public bool DesktopHistoryBusy { get { return DesktopOperationPending() || historyRestoring; } }

        public void DesktopBeginPivot()
        {
            if (!RequireDesktopMatrix("Pivot"))
                return;
            BeginDraft(DraftOperation.Pivot);
        }

        public void DesktopBeginDrill()
        {
            if (!RequireDesktopMatrix("Drill"))
                return;
            BeginDraft(DraftOperation.Drill);
        }

        public void DesktopBeginRollUp()
        {
            if (!RequireDesktopMatrix("Roll-up"))
                return;
            BeginDraft(DraftOperation.RollUp);
        }

        private bool DesktopOperationPending()
        {
            return historyRestoring || IsPending(PendingJob.Analysis) || resumeMaterializationAfterManifest ||
                IsPending(PendingJob.DatasetManifest) || IsPending(PendingJob.Intent) ||
                IsPending(PendingJob.VariableLoad);
        }

        private void RejectDesktopAction(string message)
        {
            SetStatus(message);
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
                VolumeSTCubeFlatScreenHUD.ShowNotice(message);
        }

        private void RevealFieldPresentation()
        {
            pendingFieldPresentationReveal = false;
            if (spatialRoot != null)
                spatialRoot.SetActive(true);
        }

        public void ToggleSlabFrame()
        {
            if (panelCanvas == null)
                return;
            bool next = !panelCanvas.gameObject.activeSelf;
            if (!next)
            {
                panelCanvas.gameObject.SetActive(false);
                if (slabPreviewCanvas != null)
                    slabPreviewCanvas.gameObject.SetActive(false);
                if (intentCanvas != null)
                    intentCanvas.gameObject.SetActive(false);
                return;
            }
            ShowPrimaryTool(panelCanvas);
            if (slabPreviewBuilt && slabPreviewCanvas != null)
            {
                BuildSlabPreviewPanel();
                if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
                {
                    // The desktop Slab is the large axis work area. Keep the
                    // two Fields beside it as clickable previews instead of
                    // covering all three with the VR preview card.
                    slabPreviewCanvas.gameObject.SetActive(false);
                    ShowPrimaryTool(panelCanvas);
                    SetDesktopFocusView(DesktopFocusView.SlabAxis, true);
                }
                else
                {
                    ShowComposerTool(slabPreviewCanvas);
                }
            }
            if (!VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled &&
                leftController != null)
                PlaceSlabFrameNearLeftHand();
        }

        public void RotateField(float yawDegrees)
        {
            if (spatialRoot == null || Mathf.Abs(yawDegrees) < 0.0001f)
                return;
            spatialRoot.transform.Rotate(Vector3.up, yawDegrees, Space.World);
        }

        private void PlaceSlabFrameNearLeftHand()
        {
            if (panelCanvas == null || leftController == null || xrCamera == null)
                return;
            Vector3 towardView = Vector3.ProjectOnPlane(xrCamera.transform.forward, Vector3.up).normalized;
            if (towardView.sqrMagnitude < 0.01f)
                towardView = transform.forward;
            panelCanvas.transform.position =
                leftController.position + towardView * 0.42f + Vector3.up * 0.14f;
            FacePanelTowardViewer(panelCanvas.transform);
        }

        private static float PairedFieldYaw(int rigIndex, int count)
        {
            if (count <= 1 || rigIndex <= 0)
                return 0.0f;
            return rigIndex == 1 ? -35.0f : rigIndex == 2 ? 35.0f : 0.0f;
        }

        private void RecenterVolumeBoundsOnField(Transform volumeRoot)
        {
            if (volumeRoot == null || spatialRoot == null)
                return;
            Renderer[] renderers = volumeRoot.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds combined = new Bounds(volumeRoot.position, Vector3.zero);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null || !renderer.enabled)
                    continue;
                if (!hasBounds)
                {
                    combined = renderer.bounds;
                    hasBounds = true;
                }
                else
                    combined.Encapsulate(renderer.bounds);
            }
            if (hasBounds)
                volumeRoot.position += spatialRoot.transform.position - combined.center;
        }

        private void ConfigureDesktopConsoleShell(bool compact)
        {
            if (panelCanvas == null || panelContent == null)
                return;
            RectTransform panelRect = panelCanvas.GetComponent<RectTransform>();
            if (panelRect == null)
                return;
            panelRect.sizeDelta = compact
                ? new Vector2(1120.0f, 150.0f)
                : SlabLabLayout.PanelCanvasSize;
            panelContent.sizeDelta = compact
                ? new Vector2(1070.0f, 132.0f)
                : new Vector2(1070.0f, 595.0f);
            panelContent.anchoredPosition = compact
                ? Vector2.zero
                : new Vector2(0.0f, -34.0f);
            BoxCollider collider = panelCanvas.GetComponent<BoxCollider>();
            if (collider != null)
                collider.size = new Vector3(1120.0f,
                    compact ? 150.0f : 840.0f, 8.0f);
            for (int index = 0; index < panelRect.childCount; index++)
            {
                Transform child = panelRect.GetChild(index);
                if (child == panelContent)
                    continue;
                child.gameObject.SetActive(!compact);
            }
        }

        private void AlignDesktopVisualization()
        {
            if (spatialRoot == null || xrCamera == null)
                return;
            if (!viewState.desktopVisualizationAligned)
            {
                // First establish the position where the independent STC is
                // centred by itself. The overview position then centres the
                // midpoint between the surface Field and STC Field.
                // Keep the complete two-Field composition centred in the
                // desktop viewport.  The former offset centred the left Field
                // itself and left the companion clipped by the right edge.
                // Start from the authored pose every time: offsetting the live
                // transform made Step 2 inherit whatever a previous stage left
                // behind, so the pair drifted off centre (and the scale could
                // compound until the Fields shrank).
                spatialRoot.transform.position = desktopFieldAuthoredPosition;
                spatialRoot.transform.localScale = desktopFieldAuthoredScale;
                spatialRoot.transform.position +=
                    xrCamera.transform.right * SlabLabLayout.FieldPairCentreShiftRight;
                // Reserve a dedicated row below the surface Field for its
                // timeline.  Without this lift, the desktop bottom action bar
                // occludes the lower half of the playback controls.
                spatialRoot.transform.position +=
                    xrCamera.transform.up * SlabLabLayout.FieldPairLiftUp;
                // This anchored position centres the midpoint of the compact
                // desktop pair. Boundary editing then moves the pair left by
                // half its separation so the remaining STC is centred alone.
                desktopOverviewFieldPosition = spatialRoot.transform.position;
                desktopBoundaryFieldPosition = desktopOverviewFieldPosition -
                    spatialRoot.transform.right *
                    (VolumeSTCubeForVrFieldSwapLayout.ActiveSeparation * 0.5f);
                desktopFieldScale = desktopFieldAuthoredScale;
                viewState.desktopVisualizationAligned = true;
            }
            spatialRoot.transform.position = desktopOverviewFieldPosition;
            spatialRoot.transform.localScale = desktopFieldScale * SlabLabLayout.FieldPairCompactScale;
            VolumeSTCubeForVrFieldSwapLayout swapLayout =
                spatialRoot.GetComponent<VolumeSTCubeForVrFieldSwapLayout>();
            if (swapLayout != null)
                swapLayout.KeepCurrentShiftedPosition();
            if (forVrSurfacePlayer != null)
                forVrSurfacePlayer.SetSurfaceContextVisible(true);
        }

        private void FocusDesktopStcVisualization()
        {
            if (!VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled ||
                !viewState.desktopVisualizationAligned || spatialRoot == null)
                return;
            spatialRoot.transform.position = desktopBoundaryFieldPosition;
            spatialRoot.transform.localScale = desktopFieldScale;
            VolumeSTCubeForVrFieldSwapLayout swapLayout =
                spatialRoot.GetComponent<VolumeSTCubeForVrFieldSwapLayout>();
            if (swapLayout != null)
                swapLayout.KeepCurrentShiftedPosition();
        }

        /// <summary>
        /// Re-centre the desktop two-Field composition from its authored pose
        /// (the "X: reset view" shortcut, via VolumeSTCubeQuestRuntime).
        ///
        /// Stage transitions only align once — after that the pair follows
        /// desktopOverviewFieldPosition — so a preview focus move or a bad camera
        /// drag had no way back short of restarting. This clears the "already
        /// aligned" latch and rebuilds from desktopFieldAuthoredPosition, which is
        /// the same code path the first alignment takes, so it lands on the
        /// recorded framing instead of compounding an offset.
        /// </summary>
        public void DesktopRecentreField()
        {
            if (!VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled ||
                spatialRoot == null || xrCamera == null)
                return;
            viewState.desktopVisualizationAligned = false;
            AlignDesktopVisualization();
        }

        private void UpdateDesktopFocusPresentation()
        {
            if (!VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled ||
                spatialRoot == null || xrCamera == null)
                return;
            if (stage != Stage.Matrix)
                viewState.desktopMatrixPresentationReady = false;
            if (stage == Stage.Matrix && panelCanvas != null &&
                (!viewState.desktopMatrixPresentationReady ||
                 (intentCanvas != null && intentCanvas.gameObject.activeSelf) ||
                 (slabPreviewCanvas != null &&
                  slabPreviewCanvas.gameObject.activeSelf)))
            {
                // Also repair an in-progress session after a script reload.
                // The transition hook below handles new jobs; this guard
                // cleans stale Step 3 composer panels already left on screen.
                if (!viewState.desktopMatrixPresentationReady)
                    SetDesktopFocusView(DesktopFocusView.SlabAxis, false);
                ShowPrimaryTool(panelCanvas);
                RestoreSelectedDatasetForMatrix();
                viewState.desktopMatrixPresentationReady = true;
                BuildStage();
                VolumeSTCubeFlatScreenHUD.NotifyWorkflowChanged();
            }
            VolumeSTCubeForVrXytCompanion companion =
                forVrSurfacePlayer != null
                    ? forVrSurfacePlayer.XytCompanion : null;
            if (companion == null || companion.DesktopFieldTransform == null)
                return;

            EnsureDesktopFocusTargets(companion);
            bool timeSelection = stage == Stage.Field && boundaryEditActive;
            // Step 3 owns this presentation regardless of which confirmation
            // path entered it. Tying it to mainWorkspaceEntered left the axis
            // area empty after some valid time-range transitions.
            bool slabLayout = stage == Stage.Slab || stage == Stage.Matrix;
            bool composerPanel = DesktopComposerPanelActive;
            if (slabLayout)
            {
                spatialRoot.SetActive(true);
                companion.DesktopFieldTransform.gameObject.SetActive(true);
                // Intent replaces the axis body in the right-hand task lane;
                // both Fields remain visible and keep their click-to-focus
                // behaviour on the left.
                if (composerPanel && spatialAxisComposerRoot != null)
                    spatialAxisComposerRoot.SetActive(false);
            }
            if (timeSelection)
            {
                if (desktopFocusView != DesktopFocusView.TimeSurface &&
                    desktopFocusView != DesktopFocusView.TimeStc)
                    SetDesktopFocusView(DesktopFocusView.TimeStc, false);
            }
            else if (slabLayout)
            {
                if (desktopFocusView != DesktopFocusView.SlabAxis &&
                    desktopFocusView != DesktopFocusView.SlabSurface &&
                    desktopFocusView != DesktopFocusView.SlabStc)
                    SetDesktopFocusView(DesktopFocusView.SlabAxis, false);
            }
            else if (desktopFocusView != DesktopFocusView.None)
            {
                RestoreDesktopFocusPresentation(companion);
                return;
            }

            if (desktopFocusAnimation == null &&
                desktopFocusView != DesktopFocusView.None)
                ApplyDesktopFocusPose(companion, desktopFocusView);
        }

        private void EnsureDesktopFocusTargets(
            VolumeSTCubeForVrXytCompanion companion)
        {
            if (!viewState.desktopFocusTargetsReady)
            {
                viewState.desktopFocusTargetsReady = true;
                if (spatialAxisComposerRoot != null)
                {
                    desktopAxisParent = spatialAxisComposerRoot.transform.parent;
                    desktopAxisOriginalLocalPosition =
                        spatialAxisComposerRoot.transform.localPosition;
                    desktopAxisOriginalLocalRotation =
                        spatialAxisComposerRoot.transform.localRotation;
                    desktopAxisOriginalLocalScale =
                        spatialAxisComposerRoot.transform.localScale;
                    spatialAxisComposerRoot.transform.SetParent(transform, true);
                    // The composer used to inherit the Field's world scale, so
                    // the desktop axis body shrank whenever the field layout
                    // changed and ended up about half the designed size with
                    // its labels and axis ends crowding together. Pin the
                    // authored desktop scale instead of the inherited one.
                    spatialAxisComposerRoot.transform.localScale =
                        Vector3.one * SlabLabLayout.AxisBodyScale;
                    desktopAxisBaseScale = Vector3.one * SlabLabLayout.AxisBodyScale;
                }
            }

            AddDesktopFocusTarget(spatialRoot.transform,
                "Desktop Surface Field Focus", FocusDesktopSurfaceField,
                new Vector3(1.72f, 1.62f, 0.035f));
            AddDesktopFocusTarget(companion.DesktopFieldTransform,
                "Desktop STC Field Focus", FocusDesktopStcField,
                new Vector3(1.72f, 1.62f, 0.035f));

            if (spatialAxisComposerRoot != null)
            {
                // RefreshSpatialAxisControllers rebuilds all children. Re-add
                // this rear catch surface whenever that happens so clicking
                // the axis body always restores it to the primary position.
                AddDesktopFocusTarget(spatialAxisComposerRoot.transform,
                    "Desktop Axis Focus", FocusDesktopAxis,
                    new Vector3(2.15f, 1.65f, 0.035f));
            }
        }

        private void AddDesktopFocusTarget(Transform owner, string name,
            Action action, Vector3 size)
        {
            if (owner == null || owner.Find(name) != null)
                return;
            // Child meshes may already own colliders. A target on the Field
            // root lets the desktop ray resolve those hits to the same focus
            // action, while a button's nearer target still keeps precedence.
            VolumeSTCubeQuestClickTarget ownerTarget =
                owner.GetComponent<VolumeSTCubeQuestClickTarget>();
            if (ownerTarget == null)
                ownerTarget = owner.gameObject.AddComponent<
                    VolumeSTCubeQuestClickTarget>();
            ownerTarget.AllowDesktopMouseDown = true;
            ownerTarget.Clicked = action;
            GameObject targetObject = new GameObject(name);
            targetObject.layer = 5;
            targetObject.transform.SetParent(owner, false);
            float cameraSide = owner.InverseTransformPoint(
                xrCamera.transform.position).z >= 0.0f ? 1.0f : -1.0f;
            // The catch surface sits behind the visualization, so existing
            // sliders, cuts and buttons remain the first raycast targets.
            targetObject.transform.localPosition =
                new Vector3(0.0f, 0.0f, -cameraSide * 0.74f);
            BoxCollider collider = targetObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = size;
            VolumeSTCubeQuestClickTarget clickTarget =
                targetObject.AddComponent<VolumeSTCubeQuestClickTarget>();
            clickTarget.AllowDesktopMouseDown = true;
            clickTarget.Clicked = action;
        }

        private void FocusDesktopSurfaceField()
        {
            if (!VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
                return;
            if (stage == Stage.Field && boundaryEditActive)
                SetDesktopFocusView(DesktopFocusView.TimeSurface, true);
            else if (stage == Stage.Slab || stage == Stage.Matrix)
                SetDesktopFocusView(DesktopFocusView.SlabSurface, true);
        }

        private void FocusDesktopStcField()
        {
            if (!VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
                return;
            if (stage == Stage.Field && boundaryEditActive)
                SetDesktopFocusView(DesktopFocusView.TimeStc, true);
            else if (stage == Stage.Slab || stage == Stage.Matrix)
                SetDesktopFocusView(DesktopFocusView.SlabStc, true);
        }

        private void SetDesktopFocusView(DesktopFocusView next, bool animate)
        {
            if (desktopFocusView == next && animate)
                return;
            desktopFocusView = next;
            VolumeSTCubeForVrXytCompanion companion =
                forVrSurfacePlayer != null
                    ? forVrSurfacePlayer.XytCompanion : null;
            if (companion == null || companion.DesktopFieldTransform == null)
                return;
            EnsureDesktopFocusTargets(companion);
            if (desktopFocusAnimation != null)
                StopCoroutine(desktopFocusAnimation);
            desktopFocusAnimation = animate
                ? StartCoroutine(AnimateDesktopFocusPose(companion, next))
                : null;
            if (!animate)
                ApplyDesktopFocusPose(companion, next);
        }

        private Vector3 DesktopViewportPosition(float x, float y)
        {
            float distance = Vector3.Dot(
                desktopOverviewFieldPosition - xrCamera.transform.position,
                xrCamera.transform.forward);
            distance = Mathf.Clamp(distance, 1.55f, 3.6f);
            return xrCamera.ViewportToWorldPoint(new Vector3(x, y, distance));
        }

        private void GetDesktopFocusTargets(DesktopFocusView focus,
            out Vector3 surfacePosition, out Vector3 surfaceScale,
            out Vector3 stcPosition, out Vector3 stcScale,
            out Vector3 axisPosition, out Vector3 axisScale)
        {
            bool timeSurface = focus == DesktopFocusView.TimeSurface;
            bool slab = focus == DesktopFocusView.SlabAxis ||
                focus == DesktopFocusView.SlabSurface ||
                focus == DesktopFocusView.SlabStc;
            bool slabSurface = focus == DesktopFocusView.SlabSurface;
            bool slabStc = focus == DesktopFocusView.SlabStc;

            if (!slab)
            {
                surfacePosition = DesktopViewportPosition(
                    timeSurface ? 0.61f : 0.21f, 0.52f);
                surfaceScale = desktopFieldScale *
                    (timeSurface ? 0.95f : 0.46f) *
                    SlabLabLayout.FieldPresentationScale;
                stcPosition = DesktopViewportPosition(
                    timeSurface ? 0.21f : 0.65f, 0.52f);
                stcScale = desktopFieldScale *
                    (timeSurface ? 0.46f : 0.95f) *
                    SlabLabLayout.FieldPresentationScale;
                axisPosition = DesktopViewportPosition(0.84f, 0.50f);
                axisScale = desktopAxisBaseScale * 0.42f;
                return;
            }

            if (!slabSurface && !slabStc)
            {
                surfacePosition = DesktopViewportPosition(0.19f, 0.67f);
                stcPosition = DesktopViewportPosition(0.19f, 0.29f);
                surfaceScale = desktopFieldScale * 0.50f;
                stcScale = desktopFieldScale * 0.50f;
                // Fixed dock: the body is bigger than the free space to its
                // right once variables are bound, so keep it well inside the
                // frame instead of correcting its position at runtime.
                axisPosition = DesktopViewportPosition(SlabLabLayout.AxisDockViewportX, SlabLabLayout.AxisDockViewportY);
                axisScale = desktopAxisBaseScale * SlabLabLayout.AxisDockScale;
                return;
            }

            bool surfacePrimary = slabSurface;
            surfacePosition = DesktopViewportPosition(
                surfacePrimary ? 0.47f : 0.14f,
                surfacePrimary ? 0.50f : 0.68f);
            surfaceScale = desktopFieldScale *
                (surfacePrimary ? 1.02f : 0.40f);
            stcPosition = DesktopViewportPosition(
                surfacePrimary ? 0.14f : 0.47f,
                surfacePrimary ? 0.28f : 0.50f);
            stcScale = desktopFieldScale *
                (surfacePrimary ? 0.40f : 1.02f);
            axisPosition = DesktopViewportPosition(0.84f, 0.50f);
            axisScale = desktopAxisBaseScale * 0.76f;
        }

        private void ApplyDesktopFocusPose(
            VolumeSTCubeForVrXytCompanion companion, DesktopFocusView focus)
        {
            GetDesktopFocusTargets(focus,
                out Vector3 surfacePosition, out Vector3 surfaceScale,
                out Vector3 stcPosition, out Vector3 stcScale,
                out Vector3 axisPosition, out Vector3 axisScale);
            spatialRoot.transform.position = surfacePosition;
            spatialRoot.transform.localScale = surfaceScale;
            VolumeSTCubeForVrFieldSwapLayout swapLayout =
                spatialRoot.GetComponent<VolumeSTCubeForVrFieldSwapLayout>();
            if (swapLayout != null)
                swapLayout.KeepCurrentShiftedPosition();
            companion.SetDesktopFieldPose(stcPosition, stcScale);
            if (spatialAxisComposerRoot != null)
            {
                spatialAxisComposerRoot.SetActive(
                    stage == Stage.Slab && !DesktopComposerPanelActive &&
                    (focus == DesktopFocusView.SlabAxis ||
                     focus == DesktopFocusView.SlabSurface ||
                     focus == DesktopFocusView.SlabStc));
                spatialAxisComposerRoot.transform.position = axisPosition;
                spatialAxisComposerRoot.transform.localScale = axisScale;
            }
        }

        private IEnumerator AnimateDesktopFocusPose(
            VolumeSTCubeForVrXytCompanion companion, DesktopFocusView focus)
        {
            Transform stc = companion.DesktopFieldTransform;
            Vector3 surfaceStart = spatialRoot.transform.position;
            Vector3 surfaceScaleStart = spatialRoot.transform.localScale;
            Vector3 stcStart = stc.position;
            Vector3 stcScaleStart = stc.localScale;
            Vector3 axisStart = spatialAxisComposerRoot != null
                ? spatialAxisComposerRoot.transform.position : Vector3.zero;
            Vector3 axisScaleStart = spatialAxisComposerRoot != null
                ? spatialAxisComposerRoot.transform.localScale : Vector3.one;
            GetDesktopFocusTargets(focus,
                out Vector3 surfaceTarget, out Vector3 surfaceScaleTarget,
                out Vector3 stcTarget, out Vector3 stcScaleTarget,
                out Vector3 axisTarget, out Vector3 axisScaleTarget);
            bool slabFocus = focus == DesktopFocusView.SlabAxis ||
                focus == DesktopFocusView.SlabSurface ||
                focus == DesktopFocusView.SlabStc;
            if (spatialAxisComposerRoot != null)
                spatialAxisComposerRoot.SetActive(
                    stage == Stage.Slab && slabFocus &&
                    !DesktopComposerPanelActive);

            float elapsed = 0.0f;
            const float duration = 0.58f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float linear = Mathf.Clamp01(elapsed / duration);
                float t = Mathf.SmoothStep(0.0f, 1.0f, linear);
                float arc = Mathf.Sin(linear * Mathf.PI) * 0.19f;
                spatialRoot.transform.position = Vector3.Lerp(
                    surfaceStart, surfaceTarget, t) + xrCamera.transform.up * arc;
                spatialRoot.transform.localScale = Vector3.Lerp(
                    surfaceScaleStart, surfaceScaleTarget, t);
                companion.SetDesktopFieldPose(
                    Vector3.Lerp(stcStart, stcTarget, t) -
                        xrCamera.transform.up * arc,
                    Vector3.Lerp(stcScaleStart, stcScaleTarget, t));
                if (spatialAxisComposerRoot != null && slabFocus)
                {
                    spatialAxisComposerRoot.transform.position = Vector3.Lerp(
                        axisStart, axisTarget, t) + xrCamera.transform.up *
                        (arc * 0.45f);
                    spatialAxisComposerRoot.transform.localScale = Vector3.Lerp(
                        axisScaleStart, axisScaleTarget, t);
                }
                VolumeSTCubeForVrFieldSwapLayout swapLayout =
                    spatialRoot.GetComponent<VolumeSTCubeForVrFieldSwapLayout>();
                if (swapLayout != null)
                    swapLayout.KeepCurrentShiftedPosition();
                yield return null;
            }
            desktopFocusAnimation = null;
            ApplyDesktopFocusPose(companion, focus);
        }

        private void RestoreDesktopFocusPresentation(
            VolumeSTCubeForVrXytCompanion companion)
        {
            if (desktopFocusAnimation != null)
                StopCoroutine(desktopFocusAnimation);
            desktopFocusAnimation = null;
            desktopFocusView = DesktopFocusView.None;
            spatialRoot.transform.position = desktopOverviewFieldPosition;
            spatialRoot.transform.localScale = desktopFieldScale * SlabLabLayout.FieldPairCompactScale;
            companion.ClearDesktopFieldPose();
            if (spatialAxisComposerRoot != null && desktopAxisParent != null)
            {
                spatialAxisComposerRoot.transform.SetParent(desktopAxisParent,
                    false);
                spatialAxisComposerRoot.transform.localPosition =
                    desktopAxisOriginalLocalPosition;
                spatialAxisComposerRoot.transform.localRotation =
                    desktopAxisOriginalLocalRotation;
                spatialAxisComposerRoot.transform.localScale =
                    desktopAxisOriginalLocalScale;
            }
            viewState.desktopFocusTargetsReady = false;
        }

        private void FocusDigestPageOnSelection()
        {
            digestColumnPage = Mathf.Max(0, selectedGridColumn / 3);
            digestRowPage = Mathf.Max(0, selectedGridRow / 3);
        }

        private void FocusSourcePreviewLayer(int layer)
        {
            if (layer <= 0 || layer >= sourcePreviewLayerAtlases.Count)
                return;
            Texture2D atlas = sourcePreviewLayerAtlases[layer];
            sourcePreviewLayerAtlases.RemoveAt(layer);
            sourcePreviewLayerAtlases.Insert(0, atlas);
            int variable = sourcePreviewVariableIndices[layer];
            sourcePreviewVariableIndices.RemoveAt(layer);
            sourcePreviewVariableIndices.Insert(0, variable);
            BuildAllSourcePreviewLayers();
            SetStatus(datasets[variable].Name +
                " source preview moved to the inspection layer.");
        }

        private void FitPairedVolumeToField(Transform volumeRoot,
            Transform fieldFrame, float halfWidth, float halfHeight,
            float halfDepth)
        {
            if (volumeRoot == null || fieldFrame == null)
                return;
            volumeRoot.localPosition = Vector3.zero;
            volumeRoot.localScale = Vector3.one;
            Renderer[] renderers =
                volumeRoot.GetComponentsInChildren<Renderer>(true);
            if (!TryRendererBoundsInSpace(renderers, fieldFrame,
                    out Bounds localBounds))
                return;
            Vector3 size = localBounds.size;
            float fit = Mathf.Min(
                halfWidth * 1.56f / Mathf.Max(0.0001f, size.x),
                Mathf.Min(
                    halfHeight * 1.48f / Mathf.Max(0.0001f, size.y),
                    halfDepth * 1.56f / Mathf.Max(0.0001f, size.z)));
            volumeRoot.localScale *= Mathf.Clamp(fit, 0.001f, 20.0f);
            if (!TryRendererBoundsInSpace(renderers, fieldFrame,
                    out localBounds))
                return;
            float stretch = Mathf.Clamp(
                halfHeight * 1.42f / Mathf.Max(0.0001f, localBounds.size.y),
                1.0f, FieldVerticalExaggeration);
            Vector3 scale = volumeRoot.localScale;
            scale.y *= stretch;
            volumeRoot.localScale = scale;
            if (TryRendererBoundsInSpace(renderers, fieldFrame,
                    out localBounds))
                volumeRoot.localPosition -= localBounds.center;
        }

        private void FrameVolume()
        {
            Transform volumeRoot = currentView != null && currentView.rootObject != null
                ? currentView.rootObject.transform
                : null;
            if (volumeRoot == null)
            {
                VolumeControllerObject controller = FindObjectOfType<VolumeControllerObject>();
                volumeRoot = controller != null ? controller.transform : null;
            }
            if (volumeRoot == null || spatialRoot == null)
                return;

            // The field, its semantic axes and every authoring cut must share one
            // transform.  Keeping the imported renderer as a loose world object
            // made controller rotation move the axes while the data stayed behind.
            if (volumeRoot.parent != spatialRoot.transform)
                volumeRoot.SetParent(spatialRoot.transform, true);
            volumeRoot.position = spatialRoot.transform.position;
            volumeRoot.rotation = spatialRoot.transform.rotation;
            volumeRoot.localRotation = boundaryAuthoringCanonicalView
                ? Quaternion.identity : fieldAxisRemapRotation;
            volumeRoot.localScale = Vector3.one * 0.105f;

            Renderer[] renderers = volumeRoot.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds combined = new Bounds(volumeRoot.position, Vector3.zero);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null || !renderer.enabled)
                    continue;
                // The volume is an emissive analytical surface. Shadows and
                // dynamic occlusion can produce large black cards on Quest when
                // its thin transparent layers are viewed edge-on.
                renderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.allowOcclusionWhenDynamic = false;
                renderer.sortingOrder = -100;
                if (!hasBounds)
                {
                    combined = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(renderer.bounds);
                }
            }
            if (!hasBounds)
                return;

            Vector3 size = combined.size;
            float fit = Mathf.Min(
                (FieldHalfWidth * 1.64f) / Mathf.Max(0.0001f, size.x),
                Mathf.Min(
                    (FieldHalfHeight * 1.54f) / Mathf.Max(0.0001f, size.y),
                    (FieldHalfDepth * 1.64f) / Mathf.Max(0.0001f, size.z)));
            volumeRoot.localScale *= Mathf.Clamp(fit, 0.05f, 6.0f);

            hasBounds = false;
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null || !renderer.enabled)
                    continue;
                if (!hasBounds)
                {
                    combined = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(renderer.bounds);
                }
            }
            if (hasBounds)
            {
                // The source ocean layers are physically very thin. Preserve X/Z fit,
                // but exaggerate the display height so depth cuts remain legible in VR.
                float targetHeight = FieldHalfHeight * 1.48f;
                float stretch = Mathf.Clamp(
                    targetHeight / Mathf.Max(0.0001f, combined.size.y),
                    1.0f, FieldVerticalExaggeration);
                Vector3 stretchedScale = volumeRoot.localScale;
                stretchedScale.y *= stretch;
                volumeRoot.localScale = stretchedScale;

                hasBounds = false;
                for (int index = 0; index < renderers.Length; index++)
                {
                    Renderer renderer = renderers[index];
                    if (renderer == null || !renderer.enabled)
                        continue;
                    if (!hasBounds)
                    {
                        combined = renderer.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        combined.Encapsulate(renderer.bounds);
                    }
                }
            }
            if (hasBounds)
            {
                // Axis remapping can rotate the locally exaggerated dimension into
                // world X or Z. Refit once more after exaggeration so no variable
                // can escape the Continuous Field wire cube.
                Vector3 stretchedBounds = combined.size;
                float containment = Mathf.Min(
                    (FieldHalfWidth * 1.60f) /
                        Mathf.Max(0.0001f, stretchedBounds.x),
                    Mathf.Min(
                        (FieldHalfHeight * 1.50f) /
                            Mathf.Max(0.0001f, stretchedBounds.y),
                        (FieldHalfDepth * 1.60f) /
                            Mathf.Max(0.0001f, stretchedBounds.z)));
                if (containment < 1.0f)
                {
                    volumeRoot.localScale *= Mathf.Clamp(containment, 0.05f, 1.0f);
                    hasBounds = false;
                    for (int index = 0; index < renderers.Length; index++)
                    {
                        Renderer renderer = renderers[index];
                        if (renderer == null || !renderer.enabled)
                            continue;
                        if (!hasBounds)
                        {
                            combined = renderer.bounds;
                            hasBounds = true;
                        }
                        else
                        {
                            combined.Encapsulate(renderer.bounds);
                        }
                    }
                }
            }
            // Renderer.bounds is world-axis aligned, while the Field frame can be
            // rotated. Fit and centre once in the frame's own coordinates so the
            // visible data cannot cross any of the six Field faces.
            if (TryGetLocalRendererBounds(spatialRoot.transform, renderers,
                out Bounds localBounds))
            {
                float localContainment = Mathf.Min(
                    (FieldHalfWidth * 1.20f) / Mathf.Max(0.0001f, localBounds.size.x),
                    Mathf.Min(
                        (FieldHalfHeight * 1.20f) / Mathf.Max(0.0001f, localBounds.size.y),
                        (FieldHalfDepth * 1.20f) / Mathf.Max(0.0001f, localBounds.size.z)));
                if (localContainment < 1.0f)
                {
                    volumeRoot.localScale *= Mathf.Clamp(localContainment, 0.05f, 1.0f);
                    TryGetLocalRendererBounds(spatialRoot.transform, renderers,
                        out localBounds);
                }

                volumeRoot.localPosition -= localBounds.center;
                if (TryGetLocalRendererBounds(spatialRoot.transform, renderers,
                    out localBounds))
                {
                    volumeLocalMinY = Mathf.Clamp(localBounds.min.y,
                        -FieldHalfHeight * 0.90f, FieldHalfHeight * 0.78f);
                    volumeLocalMaxY = Mathf.Clamp(localBounds.max.y,
                        volumeLocalMinY + 0.12f, FieldHalfHeight * 0.90f);
                    UpdateDepthBoundaryPlanes();
                }
            }
        }

        private System.Collections.IEnumerator RefitVolumeAfterFrameChange()
        {
            yield return null;
            yield return null;
            FrameVolume();
        }

        private static TMPro.TextAlignmentOptions ToTmpAlignment(
            TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft:
                    return TMPro.TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter:
                    return TMPro.TextAlignmentOptions.Top;
                case TextAnchor.UpperRight:
                    return TMPro.TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft:
                    return TMPro.TextAlignmentOptions.Left;
                case TextAnchor.MiddleRight:
                    return TMPro.TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft:
                    return TMPro.TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter:
                    return TMPro.TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight:
                    return TMPro.TextAlignmentOptions.BottomRight;
                default:
                    return TMPro.TextAlignmentOptions.Center;
            }
        }

        /// <summary>
        /// The anchored facet grid is authored wide enough for the headset view.
        /// On a desktop viewport its right column (stat evidence, pivot preview,
        /// expanded result) ran past the screen edge, so measure the panel and
        /// scale/shift it back inside the visible frame.
        /// </summary>
        private void KeepDesktopPanelInView(RectTransform panel)
        {
            if (!VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled ||
                panel == null || xrCamera == null)
                return;
            const float margin = 0.035f;
            for (int pass = 0; pass < 2; pass++)
            {
                Vector3[] corners = new Vector3[4];
                panel.GetWorldCorners(corners);
                float minX = 1.0f, maxX = 0.0f, minY = 1.0f, maxY = 0.0f;
                for (int index = 0; index < corners.Length; index++)
                {
                    Vector3 viewport = xrCamera.WorldToViewportPoint(
                        corners[index]);
                    if (viewport.z <= 0.0f)
                        return;
                    minX = Mathf.Min(minX, viewport.x);
                    maxX = Mathf.Max(maxX, viewport.x);
                    minY = Mathf.Min(minY, viewport.y);
                    maxY = Mathf.Max(maxY, viewport.y);
                }
                float spanX = Mathf.Max(0.01f, maxX - minX);
                float spanY = Mathf.Max(0.01f, maxY - minY);
                float allowed = 1.0f - 2.0f * margin;
                float scale = Mathf.Min(1.0f, allowed / spanX, allowed / spanY);
                if (scale < 0.999f)
                {
                    panel.localScale *= scale;
                    continue;
                }
                float shiftX = maxX > 1.0f - margin ? (1.0f - margin) - maxX :
                    minX < margin ? margin - minX : 0.0f;
                float shiftY = maxY > 1.0f - margin ? (1.0f - margin) - maxY :
                    minY < margin ? margin - minY : 0.0f;
                if (Mathf.Abs(shiftX) < 0.001f && Mathf.Abs(shiftY) < 0.001f)
                    return;
                float distance = Vector3.Dot(panel.position -
                    xrCamera.transform.position, xrCamera.transform.forward);
                distance = Mathf.Max(0.5f, distance);
                float halfHeight = Mathf.Tan(xrCamera.fieldOfView * 0.5f *
                    Mathf.Deg2Rad) * distance;
                float halfWidth = halfHeight * Mathf.Max(0.2f, xrCamera.aspect);
                panel.position +=
                    xrCamera.transform.right * (shiftX * 2.0f * halfWidth) +
                    xrCamera.transform.up * (shiftY * 2.0f * halfHeight);
                return;
            }
        }
    }
}
