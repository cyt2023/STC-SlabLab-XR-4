using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    /// <summary>Split out of VolumeSTCubeQuestSpatialWorkbench: Analysis side of the workbench.</summary>
    public sealed partial class VolumeSTCubeQuestSpatialWorkbench
    {
        public void DesktopOpenIntent()
        {
            if (stage != Stage.Slab)
            {
                RejectDesktopAction(
                    "MatPlot Intent is available in Step 3: Define the Slab.");
                return;
            }
            if (DesktopOperationPending())
            {
                RejectDesktopAction("Please wait for the current operation to finish.");
                return;
            }
            if (!AreSpatialAxisBindingsComplete(out string missing))
            {
                RejectDesktopAction("Complete the axis controller first: " + missing);
                return;
            }
            if (!authorBoundaryConfirmed)
            {
                RejectDesktopAction(
                    "Confirm the Time and Depth ranges before opening Intent.");
                return;
            }
            // On desktop the Slab skeleton is an implementation detail. The
            // user moves directly from axis binding to Intent; prepare the
            // skeleton silently so there is no redundant Generate Slab step.
            if (!slabPreviewBuilt)
            {
                ToolbarGenerateSlab();
                if (!slabPreviewBuilt)
                    return;
                if (slabPreviewCanvas != null)
                    slabPreviewCanvas.gameObject.SetActive(false);
            }
            OpenIntentEditor();
        }

        public void DesktopBuildFullMatrix()
        {
            if (stage == Stage.Matrix && s4dGridImage != null)
            {
                SetDesktopFocusView(DesktopFocusView.SlabAxis, true);
                return;
            }
            if (DesktopOperationPending())
            {
                RejectDesktopAction("The Full Matrix is already being prepared. Please wait.");
                return;
            }
            if (stage != Stage.Slab)
            {
                RejectDesktopAction(
                    "Full Matrix is available after the Slab is defined in Step 3.");
                return;
            }
            if (!AreSpatialAxisBindingsComplete(out string missing))
            {
                RejectDesktopAction("Complete the axis controller first: " + missing);
                return;
            }
            if (!authorBoundaryConfirmed)
            {
                RejectDesktopAction("Confirm the Time and Depth ranges first.");
                return;
            }
            if (!intentConfigured)
            {
                RejectDesktopAction("Open MatPlot Intent and apply an analysis task first.");
                return;
            }
            BeginGridPlacement();
        }

        public string DesktopAnalysisLabel(int index)
        {
            if (index < 0 || index >= analysisNodes.Count)
                return string.Empty;
            AnalysisNodeState node = analysisNodes[index];
            return (node == currentAnalysisNode ? "• " : "") + node.nodeId +
                "  ·  " + (node.variableName ?? node.variableId) +
                "  ·  T " + node.timeBoundaryStart + "–" + node.timeBoundaryEnd;
        }

        public void DesktopOpenAnalysis(int index)
        {
            if (index >= 0 && index < analysisNodes.Count)
                NavigateToAnalysisNode(analysisNodes[index]);
        }

        private bool RequireDesktopMatrix(string operation)
        {
            if (DesktopOperationPending())
            {
                RejectDesktopAction("Please wait for the current operation to finish.");
                return false;
            }
            if (stage != Stage.Matrix || s4dGridImage == null)
            {
                RejectDesktopAction(operation +
                    " is available after Full Matrix finishes.");
                return false;
            }
            return true;
        }

        private void PlaceQuestAnalysisWorkspace(Vector3 head, Vector3 forward,
            Vector3 right)
        {
            // Keep the room upright, but lift/lower its centre to the user's
            // current gaze. Using yaw only caused the whole cube-and-panel layout
            // to sit below the lenses whenever the user confirmed while looking up.
            float gazeLift = xrCamera != null
                ? Mathf.Clamp(xrCamera.transform.forward.y * 1.35f, -0.46f, 0.46f)
                : 0.0f;
            Vector3 workspaceHead = head + Vector3.up * gazeLift;
            Vector3 panelGaze = (forward + Vector3.down * 0.02f).normalized;

            // Room-scale analysis layout: the cube owns the left half of the
            // workspace and the active controller panel owns the right half.
            PositionQuestPanel(panelCanvas, workspaceHead + panelGaze * 1.62f +
                right * 0.66f, 0.00102f);
            PositionQuestPanel(mainMenuCanvas, workspaceHead + panelGaze * 1.58f +
                right * 0.62f, 0.00104f);
            PositionQuestPanel(boundaryCanvas, workspaceHead + panelGaze * 1.60f +
                right * 0.62f, 0.00094f);
            PositionQuestPanel(trailCanvas, workspaceHead + panelGaze * 1.66f +
                right * 0.62f, 0.00094f);
            PositionQuestPanel(facetGridCanvas, workspaceHead + panelGaze * 1.72f +
                right * 0.54f + Vector3.up * 0.24f, 0.00090f);
            PositionQuestPanel(slabPreviewCanvas, workspaceHead + panelGaze * 1.74f -
                right * 0.08f + Vector3.up * 0.64f, 0.00084f);
            PositionQuestPanel(intentCanvas, workspaceHead + panelGaze * 1.70f +
                right * 0.62f + Vector3.up * 0.58f, 0.00084f);
            PositionQuestPanel(draftCanvas, workspaceHead + panelGaze * 1.58f +
                right * 1.02f + Vector3.down * 0.34f, 0.00082f);

            if (spatialRoot != null)
            {
                spatialRoot.SetActive(true);
                spatialRoot.transform.position = workspaceHead + forward * 1.92f -
                    right * 0.72f + Vector3.down * 0.08f;
                spatialRoot.transform.rotation = Quaternion.LookRotation(
                    forward, Vector3.up);
            }
            Debug.Log("VolumeSTCube Quest workspace anchored to headset. " +
                "head=" + head.ToString("F2") +
                ", gazeLift=" + gazeLift.ToString("F2") +
                ", panel=" + panelCanvas.transform.position.ToString("F2") +
                ", field=" + (spatialRoot != null
                    ? spatialRoot.transform.position.ToString("F2")
                    : "missing"));
        }

        private void CreateIntentPanel()
        {
            intentCanvas = CreateFloatingCanvas(
                "MatPlotAgent Intent Composer",
                // Separate upper-right lane. Keep the entire prompt and action row
                // inside the initial forward view instead of requiring a head turn.
                IntentToolDockPosition,
                new Vector2(650, 520),
                0.00060f,
                Purple);
            intentContent = intentCanvas.GetComponent<RectTransform>();
            intentCanvas.sortingOrder = 122;
            AddDesktopPanelFocusTarget(intentCanvas,
                FocusDesktopIntentPanel);
            intentCanvas.gameObject.SetActive(false);
        }

        private void BuildIntentPanel()
        {
            if (intentContent == null)
                return;
            ClearChildren(intentContent);

            if (input.vrKeyboardVisible)
            {
                BuildVrKeyboard();
                return;
            }

            CreateText(intentContent, "ANALYSIS TASK",
                30, FontStyle.Bold, new Vector2(0, 205), new Vector2(585, 42),
                TextAnchor.MiddleLeft, Ink);
            CreateText(intentContent, "CHOOSE THE QUESTION, THEN REFINE IT BY VOICE OR TEXT",
                14, FontStyle.Bold, new Vector2(0, 165), new Vector2(585, 26),
                TextAnchor.MiddleLeft, Purple);

            CreateButton(intentContent, "ANOMALY", new Vector2(-210, 126),
                SlabLabLayout.RoleButtonSize,
                analysisTaskMode == AnalysisTaskMode.Anomaly ? Purple : Card,
                () => SelectAnalysisTask(AnalysisTaskMode.Anomaly));
            CreateButton(intentContent, "COMPARE", new Vector2(-70, 126),
                SlabLabLayout.RoleButtonSize,
                analysisTaskMode == AnalysisTaskMode.Compare ? Purple : Card,
                () => SelectAnalysisTask(AnalysisTaskMode.Compare));
            CreateButton(intentContent, "DISTRIBUTION", new Vector2(70, 126),
                SlabLabLayout.RoleButtonSize,
                analysisTaskMode == AnalysisTaskMode.Distribution ? Purple : Card,
                () => SelectAnalysisTask(AnalysisTaskMode.Distribution));
            CreateButton(intentContent, "RELATION", new Vector2(210, 126),
                SlabLabLayout.RoleButtonSize,
                analysisTaskMode == AnalysisTaskMode.Relationship ? Purple : Card,
                () => SelectAnalysisTask(AnalysisTaskMode.Relationship));

            intentPromptText = CreateTextBox(intentContent, prompt,
                new Vector2(-72, 50), new Vector2(420, 92), 15);
            // The task field itself is an explicit text-entry target in VR.
            // Users may either press TYPE or point at the current sentence.
            RectTransform intentPromptBox = intentPromptText != null
                ? intentPromptText.transform.parent as RectTransform
                : null;
            if (intentPromptBox != null)
            {
                BoxCollider promptCollider = intentPromptBox.gameObject.AddComponent<BoxCollider>();
                promptCollider.isTrigger = true;
                promptCollider.size = new Vector3(
                    intentPromptBox.sizeDelta.x,
                    intentPromptBox.sizeDelta.y,
                    12.0f);
                intentPromptBox.gameObject.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                    OpenTextKeyboard;
            }
            if (input.voiceReviewPending)
            {
                CreateButton(intentContent, "CONFIRM", new Vector2(238, 88),
                    new Vector2(116, 34), Green, ConfirmVoiceInput);
                CreateButton(intentContent, "VOICE", new Vector2(238, 50),
                    new Vector2(116, 34), Purple, StartVoiceInput);
                CreateButton(intentContent, "TYPE", new Vector2(238, 12),
                    new Vector2(116, 34), Card, OpenTextKeyboard);
            }
            else
            {
                CreateButton(intentContent,
                    input.questVoiceRecording ? "STOP" :
                    input.questVoiceUploading ? "SENDING" :
                    input.voiceInputActive ? "LISTENING" : "VOICE",
                    new Vector2(238, 70), new Vector2(116, 42),
                    input.voiceInputActive ? Amber : Purple, StartVoiceInput);
                CreateButton(intentContent, input.textInputActive ? "TYPING" : "TYPE",
                    new Vector2(238, 25), new Vector2(116, 42),
                    input.textInputActive ? Amber : Card, OpenTextKeyboard);
            }
            CreatePanelCard(intentContent, new Vector2(0, -48),
                new Vector2(585, 66), intentConfigured ? Green : Purple);
            CreateText(intentContent,
                IsPending(PendingJob.Intent)
                    ? "UNDERSTANDING..."
                    : intentConfigured
                        ? "READY  /  " + intentMode.ToUpperInvariant()
                        : string.IsNullOrWhiteSpace(intentResolutionError)
                            ? "READY"
                            : intentResolutionError,
                14, FontStyle.Bold, new Vector2(0, -48),
                new Vector2(535, 44), TextAnchor.MiddleLeft,
                intentConfigured ? Green :
                    IsPending(PendingJob.Intent) ? Amber :
                    string.IsNullOrWhiteSpace(intentResolutionError) ? Ink : Danger);

            CreateButton(intentContent,
                IsPending(PendingJob.Intent) ? "WORKING" : "APPLY",
                new Vector2(
                    VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled ? 0 : -150,
                    -174),
                new Vector2(
                    VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled ? 320 : 260,
                    52), IsPending(PendingJob.Intent) ? Card : Purple,
                ApplyIntentToFrame);
            // Desktop keeps workflow navigation in the fixed bottom bar. The
            // duplicate Full Matrix and Close controls could bypass that
            // sequence, so retain them only on the original VR surface.
            if (!VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
            {
                CreateButton(intentContent,
                    intentConfigured ? "FULL MATRIX" : "APPLY FIRST",
                    new Vector2(165, -174),
                    new Vector2(280, 52), intentConfigured ? Amber : Card,
                    BeginGridPlacement);
                CreateButton(intentContent, "CLOSE", new Vector2(250, 214),
                    new Vector2(100, 34), Card,
                    () => intentCanvas.gameObject.SetActive(false));
            }
        }

        private void ApplyIntentToFrame()
        {
            if (IsPending(PendingJob.Intent))
            {
                RejectDesktopAction(
                    "The analysis task is already being resolved. Please wait.");
                return;
            }
            if (string.IsNullOrWhiteSpace(prompt))
            {
                RejectDesktopAction(
                    "Choose an analysis task or enter a question before applying.");
                return;
            }
            if (input.voiceReviewPending)
                ConfirmVoiceInput();
            ResolveCurrentIntent();
        }

        private void ResolveCurrentIntent()
        {
            intentConfigured = false;
            SetPending(PendingJob.Intent, true);
            intentResolutionError = string.Empty;
            SlabLabSettings.SetSpatialPrompt(prompt);
            BuildIntentPanel();
            SetStatus("Resolving the natural-language analysis intent...");
            VolumeSTCubeS4DAnalysisClient client =
                new VolumeSTCubeS4DAnalysisClient(s4dUrl, 30);
            StartCoroutine(client.ResolveIntent(
                new S4DIntentResolutionRequest
                {
                    text = prompt,
                    variableId = selectedDataset != null
                        ? selectedDataset.VariableId
                        : string.Empty,
                    variableDisplayName = selectedDataset != null
                        ? selectedDataset.Name
                        : string.Empty,
                    unit = selectedDataset != null
                        ? selectedDataset.Unit
                        : string.Empty
                },
                OnIntentResolved));
        }

        private void OnIntentResolved(S4DIntentResolution resolution, string error)
        {
            SetPending(PendingJob.Intent, false);
            if (resolution == null)
            {
                intentConfigured = false;
                intentResolutionError = string.IsNullOrWhiteSpace(error)
                    ? "The intent could not be resolved."
                    : error;
                BuildIntentPanel();
                SetStatus(intentResolutionError);
                return;
            }
            intentConfigured = true;
            intentMode = string.IsNullOrWhiteSpace(resolution.displayLabel)
                ? resolution.analyticTask.Replace("_", " ").ToUpperInvariant()
                : resolution.displayLabel;
            intentTask = string.IsNullOrWhiteSpace(resolution.analyticTask)
                ? "characterize_distribution"
                : resolution.analyticTask;
            intentFocus = resolution.focus;
            intentConfidence = resolution.confidence;
            intentUsedFallback = resolution.usedFallback;
            intentResolutionError = string.Empty;
            // Keep the user's raw text intact. The normalized instruction is
            // resolved metadata, not a replacement for the authored prompt.
            SlabLabSettings.SetSpatialPrompt(prompt);
            BuildIntentPanel();
            RefreshIntentSurfaces();
            RecordTrailEvent("INTENT", intentMode);
            // Time/Depth buckets were already authored in the initial Field
            // setup. Intent resolution can therefore prepare the immutable
            // request directly; a separate Slab/source-preview gate only made
            // the main workflow longer without changing the request.
            BuildS4DGridRequest();
            spatialWorkflowStep = SpatialWorkflowStep.SourcePreviewReady;
            BuildWorkflowToolbar();
            BuildIntentPanel();
            SetStatus(intentMode +
                " resolved. Full Matrix is ready; MatPlotAgent starts only when you confirm it.");
        }

        private void RefreshIntentSurfaces()
        {
            if (slabPreviewCanvas != null && slabPreviewCanvas.gameObject.activeSelf)
                BuildSlabPreviewPanel();
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
        }

        private void SelectAnalysisTask(AnalysisTaskMode mode)
        {
            if (IsPending(PendingJob.Intent))
            {
                RejectDesktopAction(
                    "Wait for the current Intent request before changing the task.");
                return;
            }
            analysisTaskMode = mode;
            string variable = importSelectedVariableIndex >= 0 &&
                importSelectedVariableIndex < datasets.Count
                    ? datasets[importSelectedVariableIndex].Name
                    : "the selected variable";
            switch (mode)
            {
                case AnalysisTaskMode.Compare:
                    analysisQuestion = "How does " + variable +
                        " differ across the selected time and depth regions?";
                    prompt = "Compare the selected time and depth regions and explain the strongest differences.";
                    intentTask = "determine_range";
                    break;
                case AnalysisTaskMode.Relationship:
                    analysisQuestion = "Which time and depth patterns in " +
                        variable + " move together?";
                    prompt = "Identify relationships and consistent patterns across the selected time and depth regions.";
                    intentTask = "correlate";
                    break;
                case AnalysisTaskMode.Distribution:
                    analysisQuestion = "How is " + variable +
                        " distributed across time and depth?";
                    prompt = "Characterize the distribution across the selected time and depth regions.";
                    intentTask = "characterize_distribution";
                    break;
                default:
                    analysisQuestion = "Where and when does " + variable +
                        " show the strongest anomaly?";
                    prompt = "Find the strongest anomalies across the selected time and depth regions.";
                    intentTask = "find_anomalies";
                    break;
            }
            intentConfigured = false;
            SlabLabSettings.SetSpatialPrompt(prompt);
            RecordTrailEvent("QUESTION", analysisQuestion);
            BuildStage();
            if (intentCanvas != null && intentCanvas.gameObject.activeSelf)
                BuildIntentPanel();
        }

        private void FocusDesktopIntentPanel()
        {
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled &&
                stage == Stage.Slab && DesktopComposerPanelActive)
                SetDesktopFocusView(DesktopFocusView.SlabAxis, true);
        }

        private string IntentDisplayLabel()
        {
            if (!intentConfigured)
                return "WAITING FOR MATPLOTAGENT INTENT";
            return intentMode.Replace("CHARACTERIZE ", string.Empty)
                .Replace("DETERMINE ", string.Empty);
        }

        private bool TryGetFacetGridPointerLocal(out Vector2 localPoint)
        {
            localPoint = Vector2.zero;
            if (rayInteractor == null || facetGridCanvas == null ||
                facetGridContent == null)
                return false;
            Plane plane = new Plane(facetGridCanvas.transform.forward,
                facetGridCanvas.transform.position);
            if (!plane.Raycast(rayInteractor.PointerRay, out float distance))
                return false;
            Vector3 local = facetGridContent.InverseTransformPoint(
                rayInteractor.PointerRay.GetPoint(distance));
            localPoint = new Vector2(local.x, local.y);
            return true;
        }

        private void CreateWireGrid(RectTransform parent, Vector2 position, Vector2 size,
            int columns, int rows, Color color)
        {
            float cellWidth = size.x / columns;
            float cellHeight = size.y / rows;
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    GameObject cellObject = new GameObject("Placement cell", typeof(RectTransform));
                    cellObject.transform.SetParent(parent, false);
                    RectTransform cell = cellObject.GetComponent<RectTransform>();
                    cell.sizeDelta = new Vector2(cellWidth - 10, cellHeight - 10);
                    cell.anchoredPosition = position + new Vector2(
                        -size.x * 0.5f + cellWidth * (column + 0.5f),
                        size.y * 0.5f - cellHeight * (row + 0.5f));
                    Image cellImage = cellObject.AddComponent<Image>();
                    cellImage.color = Color.Lerp(Panel,
                        new Color(color.r * 0.22f, color.g * 0.22f,
                            color.b * 0.22f, 1.0f), 0.34f);
                    cellImage.raycastTarget = false;
                    Shadow shadow = cellObject.AddComponent<Shadow>();
                    shadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.38f);
                    shadow.effectDistance = new Vector2(3, -4);
                    Outline outline = cellObject.AddComponent<Outline>();
                    outline.effectColor = new Color(color.r, color.g, color.b, 0.50f);
                    outline.effectDistance = new Vector2(1.5f, -1.5f);

                    GameObject corner = new GameObject("Cell corner accent",
                        typeof(RectTransform));
                    corner.transform.SetParent(cell, false);
                    RectTransform cornerRect = corner.GetComponent<RectTransform>();
                    cornerRect.anchorMin = new Vector2(0, 1);
                    cornerRect.anchorMax = new Vector2(0, 1);
                    cornerRect.pivot = new Vector2(0, 1);
                    cornerRect.anchoredPosition = new Vector2(0, 0);
                    cornerRect.sizeDelta = new Vector2(38, 4);
                    Image cornerImage = corner.AddComponent<Image>();
                    cornerImage.color = new Color(color.r, color.g, color.b, 0.90f);
                    cornerImage.raycastTarget = false;
                }
            }
        }

        private void OpenIntentEditor()
        {
            if (!AreSpatialAxisBindingsComplete(out string missing))
            {
                SetStatus("MatPlot Intent is locked: " + missing);
                return;
            }
            EnsureSavedAuthorBoundaries();
            if (!authorBoundaryConfirmed)
            {
                SetStatus("MatPlot Intent is locked until Time and Depth are confirmed.");
                return;
            }
            spatialWorkflowStep = SpatialWorkflowStep.Intent;
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
                SetDesktopFocusView(DesktopFocusView.SlabAxis, false);
            if (intentCanvas != null)
            {
                HidePrimaryToolsExcept(intentCanvas);
                intentCanvas.transform.localPosition = IntentToolDockPosition;
                ShowComposerTool(intentCanvas);
                BuildIntentPanel();
            }
            SetStatus("Intent composer opened. The saved Time and Depth choices determine the matrix size.");
        }

        private bool UsesFacetedVariableFields()
        {
            if (roles[3] != DimensionRole.Faceted)
                return false;

            int uniqueVariables = 0;
            HashSet<int> seen = new HashSet<int>();
            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                int variable = spatialAxisRigStates[index].boundVariable;
                if (variable < 0 || variable >= datasets.Count ||
                    !seen.Add(variable))
                    continue;
                uniqueVariables++;
                if (uniqueVariables > 1)
                    return true;
            }
            return false;
        }

        private void RefreshVariableFacetStacks()
        {
            // Preserve the actual volume objects while their wire frames are
            // rebuilt. Destroying the old frame without detaching these first
            // would also destroy their Texture3D-backed renderers.
            foreach (KeyValuePair<int, VolumeRenderedObject> entry in
                pairedVariableVolumes)
            {
                if (entry.Value != null && spatialRoot != null)
                    entry.Value.transform.SetParent(spatialRoot.transform, true);
            }
            if (variableFacetStacksRoot != null)
                Destroy(variableFacetStacksRoot);
            variableFacetStacksRoot = null;
            for (int index = 0; index < variableFacetStackTextures.Count; index++)
                if (variableFacetStackTextures[index] != null)
                    Destroy(variableFacetStackTextures[index]);
            variableFacetStackTextures.Clear();

            List<int> boundVariables = new List<int>();
            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                int variable = spatialAxisRigStates[index].boundVariable;
                if (variable >= 0 && variable < datasets.Count &&
                    !boundVariables.Contains(variable))
                    boundVariables.Add(variable);
            }
            bool showMultiples = roles[3] == DimensionRole.Faceted &&
                boundVariables.Count > 1 && spatialRoot != null;
            if (!showMultiples)
            {
                ClearPairedVariableVolumes();
                if (currentView != null)
                    currentView.SetVisible(viewState.cubeVisible &&
                        !(groundDocked && groundMode == GroundMode.Aggregate &&
                          groundAggregateVolume != null));
                if (slabObject != null)
                    slabObject.SetActive(true);
                if (slabPreviewObject != null)
                    // The textured XY preview is an authored/inspected slice,
                    // not a permanent mid-plane. Keeping it active here showed
                    // an opaque white sheet before a Depth selection existed.
                    slabPreviewObject.SetActive(interaction.depthInspectionActive ||
                        (boundaryEditActive &&
                         boundaryDimension == BoundaryDimension.Horizontal));
                return;
            }

            if (currentView != null)
                currentView.SetVisible(false);
            // A faceted variable layout is a set of independent continuous
            // fields. The authoring slab belongs to the later boundary step and
            // must not masquerade as the variable's data here.
            if (slabObject != null)
                slabObject.SetActive(false);
            if (slabPreviewObject != null)
                slabPreviewObject.SetActive(false);
            RemoveUnusedPairedVariableVolumes(boundVariables);
            variableFacetStacksRoot = new GameObject(
                "Variable Faceted Continuous STC Fields");
            variableFacetStacksRoot.transform.SetParent(spatialRoot.transform, false);

            int count = boundVariables.Count;
            for (int variableIndex = 0; variableIndex < count; variableIndex++)
            {
                int boundVariable = boundVariables[variableIndex];
                VolumeSTCubeSliceDataset dataset = datasets[boundVariable];
                int rigIndex = spatialAxisRigStates.FindIndex(state =>
                    state.boundVariable == boundVariable);
                SpatialAxisRigState rigState = rigIndex >= 0
                    ? spatialAxisRigStates[rigIndex] : null;

                // Each axis body owns one complete STC field. Field 1 reuses the
                // existing Continuous Field wire cube; later variables receive
                // full-size sibling copies outside it, never mini cubes inside it.
                GameObject fieldFrame = new GameObject(dataset.Name +
                    " paired STC field frame");
                fieldFrame.transform.SetParent(
                    variableFacetStacksRoot.transform, false);
                // Layout follows the visible variable order rather than the
                // backing rig-state slot. This keeps the remaining Fields in
                // the intended left / above-axis / right-axis positions after
                // a variable is removed and another one is added.
                fieldFrame.transform.localPosition = PairedFieldCenter(
                    variableIndex, count);
                // Every Field is a stable, upright data space. The shared
                // tri-axis controls semantic layout but never rotates Fields.
                fieldFrame.transform.localRotation = Quaternion.identity;
                // Keep the primary Field dominant. Extra variables remain
                // available as compact STC cards instead of three competing
                // full-size coordinate systems.
                fieldFrame.transform.localScale = variableIndex == 0
                    ? Vector3.one : Vector3.one * 0.62f;

                float halfWidth = FieldHalfWidth;
                float halfHeight = FieldHalfHeight;
                float halfDepth = FieldHalfDepth;
                CreatePairedFieldWireFrame(fieldFrame.transform, halfWidth,
                    halfHeight, halfDepth, rigIndex, dataset.Name,
                    variableIndex != 0);

                int requestedTime = rigState != null &&
                    !rigState.usesSharedBoundaries
                    ? rigState.customSelectedTime
                    : sharedSelectedTime;
                int time = Mathf.Clamp(requestedTime, 0,
                    Mathf.Max(0, dataset.TimeCount - 1));
                VolumeRenderedObject volume = GetOrCreatePairedVariableVolume(
                    boundVariable, time);
                if (volume != null)
                {
                    volume.gameObject.name = dataset.Name +
                        " continuous STC volume";
                    volume.transform.SetParent(fieldFrame.transform, false);
                    volume.transform.localPosition = Vector3.zero;
                    volume.transform.localRotation = Quaternion.identity;
                    volume.transform.localScale = Vector3.one;
                    FitPairedVolumeToField(volume.transform,
                        fieldFrame.transform, halfWidth, halfHeight, halfDepth);
                }
            }

            // Quiet magnetic rails make the T-shaped composition read as one
            // system without visually cutting through any volume rendering.
            Color railColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.28f);
            float railZ = -FieldHalfDepth - 0.055f;
            if (count > 1)
            {
                Vector3 upperCenter = PairedFieldCenter(1, count);
                CreateWorldLine("Upper Field magnetic rail",
                    variableFacetStacksRoot.transform,
                    new Vector3(SpatialAxisDockX, 0.56f, railZ),
                    new Vector3(upperCenter.x,
                        upperCenter.y - FieldHalfHeight - 0.055f, railZ),
                    railColor, 0.0045f);
            }
            if (count > 2)
            {
                Vector3 rightCenter = PairedFieldCenter(2, count);
                CreateWorldLine("Right Field magnetic rail",
                    variableFacetStacksRoot.transform,
                    new Vector3(SpatialAxisDockX + 0.58f, 0.0f, railZ),
                    new Vector3(rightCenter.x - FieldHalfWidth - 0.055f,
                        0.0f, railZ), railColor, 0.0045f);
            }
            if (count > 3)
            {
                Vector3 upperRightCenter = PairedFieldCenter(3, count);
                CreateWorldLine("Upper-right Field magnetic rail",
                    variableFacetStacksRoot.transform,
                    new Vector3(SpatialAxisDockX,
                        SpatialFieldOrbitY, railZ),
                    new Vector3(upperRightCenter.x -
                        FieldHalfWidth - 0.055f,
                        upperRightCenter.y, railZ), railColor, 0.0045f);
            }
        }

        private int RepresentativeFacetDepth(int layer, int depthCount)
        {
            if (authoredDepthBuckets != null && layer >= 0 &&
                layer < authoredDepthBuckets.Length)
                return Mathf.Clamp(RepresentativeIndex(
                    authoredDepthBuckets[layer].indices), 0,
                    Mathf.Max(0, depthCount - 1));
            return Mathf.Clamp(Mathf.RoundToInt((layer + 0.5f) *
                depthCount / 3.0f), 0, Mathf.Max(0, depthCount - 1));
        }

        private void StartS4DGridJob()
        {
            if (selectedDataset == null || IsPending(PendingJob.Analysis))
                return;
            // The current workflow goes directly from a resolved intent to
            // Full Matrix.  Source-preview atlases are optional UI evidence,
            // not a prerequisite for the MatPlotAgent request.  Keeping the
            // old atlas check here caused valid 1x1/2x2/3x3 requests to be
            // rejected locally before S4D or MatPlotAgent ever saw them.
            if (spatialWorkflowStep != SpatialWorkflowStep.Materializing ||
                !intentConfigured)
            {
                SetStatus("MatPlotAgent blocked: complete axis binding, boundaries, and intent first.");
                return;
            }
            if (!HasResolvedDatasetManifest())
            {
                resumeMaterializationAfterManifest = true;
                if (!IsPending(PendingJob.DatasetManifest))
                    ResolveSelectedDatasetManifest();
                SetStatus("Restoring validated dataset metadata, then Full Matrix will continue automatically...");
                return;
            }
            if (materializationVariableCursor < 0)
            {
                materializationVariableIndices.Clear();
                materializationVariableIndices.AddRange(
                    ActiveBoundVariableIndices());
                if (materializationVariableIndices.Count == 0)
                {
                    SetStatus("MatPlotAgent blocked: no bound variable layer.");
                    spatialWorkflowStep = SpatialWorkflowStep.SourcePreviewReady;
                    return;
                }
                for (int index = 0; index < materializedLayerAtlases.Count; index++)
                {
                    Texture2D oldLayer = materializedLayerAtlases[index];
                    if (oldLayer != null && oldLayer != s4dGridImage &&
                        !IsAnalysisNodeTexture(oldLayer))
                        Destroy(oldLayer);
                }
                materializedLayerAtlases.Clear();
                materializedLayerResults.Clear();
                materializationVariableCursor = 0;
            }
            BuildMatrixBucketSelections();
            SetPending(PendingJob.Analysis, true);
            progress = materializationVariableIndices.Count > 0
                ? materializationVariableCursor /
                    (float)materializationVariableIndices.Count
                : 0.0f;
            displayedGridProgress = progress;
            targetGridProgress = progress;
            DestroyTextures(streamingCellTextures);
            if (variableFacetStacksRoot != null)
                Destroy(variableFacetStacksRoot);
            for (int index = 0; index < variableFacetStackTextures.Count; index++)
                if (variableFacetStackTextures[index] != null)
                    Destroy(variableFacetStackTextures[index]);
            variableFacetStackTextures.Clear();
            s4dGridFailure = string.Empty;
            stage = Stage.Matrix;
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
                SetDesktopFocusView(DesktopFocusView.SlabAxis, true);
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled &&
                panelCanvas != null)
            {
                // Matrix is a new desktop task surface. The VR flow leaves the
                // Intent/facet canvases active as spatial context, which made
                // them pile up as tiny panels at the bottom of Step 4. Replace
                // them with the single Matrix progress/result panel instead.
                ShowPrimaryTool(panelCanvas);
            }
            int materializeVariableIndex = materializationVariableIndices[
                Mathf.Clamp(materializationVariableCursor, 0,
                    materializationVariableIndices.Count - 1)];
            VolumeSTCubeSliceDataset materializeDataset =
                datasets[materializeVariableIndex];
            SetStatus("Preparing MatPlotAgent variable layer " +
                (materializationVariableCursor + 1) + "/" +
                materializationVariableIndices.Count + ": " +
                materializeDataset.Name + "...");
            BuildStage();
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
                VolumeSTCubeFlatScreenHUD.NotifyWorkflowChanged();
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
            s4dClient = new VolumeSTCubeS4DAnalysisClient(s4dUrl, 300, 1.0f);
            S4DFacetGridRequest request =
                BuildS4DGridRequestForVariable(materializeVariableIndex);
            request.datasetId = materializeDataset.DatasetId;
            request.variableId = materializeDataset.VariableId;
            StartCoroutine(s4dClient.Materialize(
                request,
                OnS4DGridProgress,
                OnS4DGridComplete,
                OnS4DCellReady));
        }

        private void OnS4DCellReady(string cellId, Texture2D texture)
        {
            if (texture == null || string.IsNullOrWhiteSpace(cellId))
                return;
            int target = -1;
            for (int depth = 0; activeDepthBuckets != null &&
                depth < activeDepthBuckets.Length; depth++)
            {
                for (int time = 0; activeTimeBuckets != null &&
                    time < activeTimeBuckets.Length; time++)
                {
                    string expected = activeTimeBuckets[time].id + "__" +
                        activeDepthBuckets[depth].id;
                    if (expected == cellId)
                    {
                        target = depth * activeTimeBuckets.Length + time;
                        break;
                    }
                }
                if (target >= 0)
                    break;
            }
            if (target < 0 || target >= streamingCellTextures.Length)
            {
                Destroy(texture);
                return;
            }
            if (streamingCellTextures[target] != null)
                Destroy(streamingCellTextures[target]);
            streamingCellTextures[target] = texture;
            SetStatus("MatPlotAgent cell ready: " + cellId + ".");
            bool gridVisible = facetGridCanvas != null &&
                facetGridCanvas.gameObject.activeSelf;
            if (gridVisible)
            {
                RawImage image = facetGridCellImages[target];
                if (image != null)
                {
                    image.texture = texture;
                    image.uvRect = new Rect(0, 0, 1, 1);
                    image.color = Color.white;
                }
                if (facetGridCellPlaceholders[target] != null)
                    facetGridCellPlaceholders[target].SetActive(false);
                if (facetGridCellStateLabels[target] != null)
                {
                    facetGridCellStateLabels[target].text = "VALIDATED";
                    facetGridCellStateLabels[target].color = Green;
                }
                int ready = 0;
                for (int i = 0; i < streamingCellTextures.Length; i++)
                    if (streamingCellTextures[i] != null)
                        ready++;
                if (facetGridValidatedText != null)
                    facetGridValidatedText.text = ready + " / " +
                        Mathf.Max(1, activeGridColumns * activeGridRows) +
                        " PANELS READY";
            }
            else if (stage == Stage.Matrix)
                UpdateDesktopMatrixProgress();
        }

        private void MergeCellIntoGridAtlas(int sourceIndex, Texture2D cell)
        {
            if (s4dGridImage == null || cell == null)
                return;
            int columns = Mathf.Max(1, activeTimeBuckets != null
                ? activeTimeBuckets.Length : 3);
            int rows = Mathf.Max(1, activeDepthBuckets != null
                ? activeDepthBuckets.Length : 3);
            int time = Mathf.Clamp(sourceIndex % columns, 0, columns - 1);
            int depth = Mathf.Clamp(sourceIndex / columns, 0, rows - 1);
            int targetWidth = Mathf.Max(1, s4dGridImage.width / columns);
            int targetHeight = Mathf.Max(1, s4dGridImage.height / rows);
            Color[] pixels = new Color[targetWidth * targetHeight];
            for (int y = 0; y < targetHeight; y++)
            {
                float v = (y + 0.5f) / targetHeight;
                for (int x = 0; x < targetWidth; x++)
                {
                    float u = (x + 0.5f) / targetWidth;
                    pixels[y * targetWidth + x] =
                        cell.GetPixelBilinear(u, v);
                }
            }
            int xOffset = time * targetWidth;
            int yOffset = (rows - 1 - depth) * targetHeight;
            s4dGridImage.SetPixels(
                xOffset, yOffset, targetWidth, targetHeight, pixels);
            s4dGridImage.Apply(false, false);
        }

        private S4DFacetGridRequest BuildS4DGridRequestForVariable(
            int variableIndex)
        {
            int savedTimeStart = timeBoundaryStart;
            int savedTimeEnd = timeBoundaryEnd;
            int savedTime = selectedTime;
            float savedDepthLow = depthBoundaryLow;
            float savedDepthHigh = depthBoundaryHigh;
            int savedDepth = selectedZ;
            bool savedConfirmed = authorBoundaryConfirmed;
            SpatialAxisRigState state = spatialAxisRigStates.Find(item =>
                item.boundVariable == variableIndex);
            if (state != null && !state.usesSharedBoundaries)
            {
                timeBoundaryStart = state.customTimeBoundaryStart;
                timeBoundaryEnd = state.customTimeBoundaryEnd;
                selectedTime = state.customSelectedTime;
                depthBoundaryLow = state.customDepthBoundaryLow;
                depthBoundaryHigh = state.customDepthBoundaryHigh;
                selectedZ = state.customSelectedZ;
                // Authored buckets represent the shared ladder. A custom
                // variable intentionally derives its own buckets below.
                authorBoundaryConfirmed = false;
            }
            S4DFacetGridRequest request = BuildS4DGridRequest();
            timeBoundaryStart = savedTimeStart;
            timeBoundaryEnd = savedTimeEnd;
            selectedTime = savedTime;
            depthBoundaryLow = savedDepthLow;
            depthBoundaryHigh = savedDepthHigh;
            selectedZ = savedDepth;
            authorBoundaryConfirmed = savedConfirmed;
            return request;
        }

        private S4DFacetGridRequest BuildS4DGridRequest()
        {
            int timeCount = selectedDataset != null ? selectedDataset.TimeCount : 30;
            int depthCount = selectedDataset != null ? selectedDataset.DimZ : 92;
            int analyzableDepthCount = depthCount == 92 ? 91 : depthCount;

            int timeA = Mathf.Clamp(timeBoundaryStart, 1, Mathf.Max(1, timeCount - 2));
            int timeB = Mathf.Clamp(timeBoundaryEnd + 1, timeA + 1, timeCount - 1);
            int surfaceEnd = Mathf.Clamp(
                Mathf.RoundToInt(depthBoundaryLow * analyzableDepthCount),
                1, Mathf.Max(1, analyzableDepthCount - 2));
            int middleEnd = Mathf.Clamp(
                Mathf.RoundToInt(depthBoundaryHigh * analyzableDepthCount),
                surfaceEnd + 1, analyzableDepthCount - 1);

            S4DIndexBucketRequest[] timeBuckets =
            {
                Bucket("before", "Before", CreateIndexRange(0, timeA)),
                Bucket("during", "During", CreateIndexRange(timeA, timeB)),
                Bucket("after", "After", CreateIndexRange(timeB, timeCount))
            };
            S4DIndexBucketRequest[] depthBuckets =
            {
                Bucket("surface", "Surface", CreateIndexRange(0, surfaceEnd)),
                Bucket("middle", "Middle", CreateIndexRange(surfaceEnd, middleEnd)),
                Bucket("deep", "Deep", CreateIndexRange(middleEnd, analyzableDepthCount))
            };
            if (IsForVrSurfaceDataset)
                depthBuckets = new[]
                {
                    Bucket("surface", "Hong Kong surface", new[] { 0 })
                };
            // Slab Frame consumes the exact Author Boundary ladder that the
            // user confirmed before entering configuration.  It must not
            // silently derive a second, independent set of buckets.
            if (authorBoundaryConfirmed &&
                authoredTimeBuckets != null && authoredTimeBuckets.Length > 0)
                timeBuckets = authoredTimeBuckets;
            if (authorBoundaryConfirmed &&
                authoredDepthBuckets != null && authoredDepthBuckets.Length > 0)
                depthBuckets = authoredDepthBuckets;
            AnalysisNodeState draftSource = FindAnalysisNode(draftSourceNodeId);
            if (draftOperation != DraftOperation.None && draftSource != null)
            {
                if (draftSource.timeBuckets != null && draftSource.timeBuckets.Length > 0)
                    timeBuckets = draftSource.timeBuckets;
                if (draftSource.depthBuckets != null && draftSource.depthBuckets.Length > 0)
                    depthBuckets = draftSource.depthBuckets;
            }
            ApplyDraftBucketOperation(ref timeBuckets, ref depthBuckets);

            if (roles[0] == DimensionRole.Fixed)
            {
                int fixedTime = Mathf.Clamp(selectedTime, 0,
                    Mathf.Max(0, timeCount - 1));
                timeBuckets = new[]
                {
                    Bucket("time_fixed_" + fixedTime,
                        selectedDataset != null
                            ? selectedDataset.GetTimeLabel(fixedTime)
                            : "Fixed time",
                        new[] { fixedTime })
                };
            }
            if (roles[1] == DimensionRole.Fixed)
            {
                int fixedDepth = Mathf.Clamp(selectedZ, 0,
                    Mathf.Max(0, analyzableDepthCount - 1));
                depthBuckets = new[]
                {
                    Bucket("depth_fixed_" + fixedDepth,
                        "z=" + fixedDepth, new[] { fixedDepth })
                };
            }
            // The three buttons emitted by the Time and Depth tokens are the
            // authoritative Matrix selection.  Filter only the normal authored
            // three-part ladder; drill/pivot drafts may intentionally carry a
            // different number of buckets and retain their own topology.
            if (roles[0] == DimensionRole.Faceted && timeBuckets.Length == 3)
                timeBuckets = FilterBucketsByMask(timeBuckets,
                    selectedTimeBucketMask);
            if (roles[1] == DimensionRole.Faceted && depthBuckets.Length == 3)
                depthBuckets = FilterBucketsByMask(depthBuckets,
                    selectedDepthBucketMask);
            // Variable faceting is represented as independent spatial layers.
            // Never replace Depth rows with variable buckets: doing so silently
            // changed a requested 3x3 Time x Depth grid into nine unrelated rows.
            activeTimeBuckets = timeBuckets;
            activeDepthBuckets = depthBuckets;
            bool axisRequestsTranspose = spatialAxisRigStates.Count > 0 &&
                spatialAxisRigStates[0].depthAxis == 0 &&
                spatialAxisRigStates[0].timeAxis != 0;
            activeGridTransposed = draftOperation != DraftOperation.None
                ? pivotTransposed
                : currentAnalysisNode != null
                    ? currentAnalysisNode.gridTransposed
                    : axisRequestsTranspose;
            if (draftOperation == DraftOperation.Drill)
            {
                // Keep the expanded axis horizontal so its child panels remain
                // legible in VR instead of becoming nine compressed rows.
                if (depthBuckets.Length > 3 && timeBuckets.Length <= 3)
                    activeGridTransposed = true;
                else if (timeBuckets.Length > 3 && depthBuckets.Length <= 3)
                    activeGridTransposed = false;
            }
            activeGridColumns = activeGridTransposed
                ? depthBuckets.Length
                : timeBuckets.Length;
            activeGridRows = activeGridTransposed
                ? timeBuckets.Length
                : depthBuckets.Length;
            UpdateActiveRepresentativeIndices();

            return new S4DFacetGridRequest
            {
                datasetId = selectedDataset.DatasetId,
                variableId = selectedDataset.VariableId,
                timeBuckets = timeBuckets,
                depthBuckets = depthBuckets,
                dimensionRoles = BuildDimensionRoleRequest(),
                rawIntent = prompt,
                analysisQuestion = analysisQuestion,
                analyticTask = intentTask,
                // A re-materialization is committed atomically as a complete
                // Grid snapshot.  Streaming cells are previews only; they must
                // never be blended into the stale Grid that remains in
                // SlabTrail while the job is running.
                requestedCellIds = new string[0],
                hasSharedScaleOverride = rematerializingStaleCells,
                sharedScaleMinimum = s4dSharedMinimum,
                sharedScaleMaximum = s4dSharedMaximum
            };
        }

        private void BuildMatrixBucketSelections()
        {
            if (selectedDataset == null)
                return;
            if (s4dGridImage != null && draftOperation == DraftOperation.None &&
                activeTimeBuckets != null && activeDepthBuckets != null)
            {
                UpdateActiveRepresentativeIndices();
                return;
            }
            int timeCount = selectedDataset.TimeCount;
            int depthCount = selectedDataset.DimZ;
            int timeA = Mathf.Clamp(timeBoundaryStart, 1, Mathf.Max(1, timeCount - 2));
            int timeB = Mathf.Clamp(timeBoundaryEnd + 1, timeA + 1, timeCount - 1);
            int depthA = Mathf.Clamp(Mathf.RoundToInt(depthBoundaryLow * depthCount),
                1, Mathf.Max(1, depthCount - 2));
            int depthB = Mathf.Clamp(Mathf.RoundToInt(depthBoundaryHigh * depthCount),
                depthA + 1, depthCount - 1);
            matrixTimes[0] = Mathf.Clamp((timeA - 1) / 2, 0, timeCount - 1);
            matrixTimes[1] = Mathf.Clamp((timeA + timeB - 1) / 2, 0, timeCount - 1);
            matrixTimes[2] = Mathf.Clamp((timeB + timeCount - 1) / 2, 0, timeCount - 1);
            matrixDepths[0] = Mathf.Clamp((depthA - 1) / 2, 0, depthCount - 1);
            matrixDepths[1] = Mathf.Clamp((depthA + depthB - 1) / 2, 0, depthCount - 1);
            matrixDepths[2] = Mathf.Clamp((depthB + depthCount - 1) / 2, 0, depthCount - 1);
        }

        private void OnS4DGridProgress(string message, float value)
        {
            float layerProgress = Mathf.Clamp01(value);
            int layerCount = Mathf.Max(1, materializationVariableIndices.Count);
            int layerIndex = Mathf.Clamp(materializationVariableCursor, 0,
                layerCount - 1);
            progress = Mathf.Clamp01((layerIndex + layerProgress) / layerCount);
            targetGridProgress = Mathf.Max(targetGridProgress, progress);
            SetStatus("Layer " + (layerIndex + 1) + "/" + layerCount +
                "  " + message + " (" + Mathf.RoundToInt(progress * 100) + "%)");
            bool gridVisible = facetGridCanvas != null &&
                facetGridCanvas.gameObject.activeSelf;
            if (gridProgressAnimation == null)
                gridProgressAnimation = StartCoroutine(AnimateGridProgress());
            if (gridVisible)
            {
                if (facetGridProgressStageText != null)
                    facetGridProgressStageText.text =
                        MaterializationStageLabel().ToUpperInvariant();
            }
            else if (stage == Stage.Matrix)
            {
                // Keep the panel and all labels stable. Only mutate the three
                // progress widgets; rebuilding the hierarchy here caused the
                // visible text/layout jump on every network milestone.
                UpdateDesktopMatrixProgress();
            }
        }

        private void OnS4DGridComplete(S4DFacetGridResult result)
        {
            SetPending(PendingJob.Analysis, false);
            targetGridProgress = 1.0f;
            displayedGridProgress = 1.0f;
            UpdateFacetGenerationProgress();
            s4dClient = null;
            bool completedRematerialization = rematerializingStaleCells;
            if (result == null || !result.Succeeded)
            {
                spatialWorkflowStep = SpatialWorkflowStep.SourcePreviewReady;
                rematerializingStaleCells = false;
                Array.Clear(rematerializedCellMask, 0,
                    rematerializedCellMask.Length);
                progress = 0.0f;
                s4dGridFailure = result != null
                    ? result.Error
                    : "S4D analysis service returned no Grid result.";
                SetStatus(s4dGridFailure);
                BuildStage();
                if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                    BuildFacetGridPanel();
                return;
            }
            materializedLayerAtlases.Add(result.Panel);
            materializedLayerResults.Add(result);
            if (materializationVariableCursor + 1 <
                materializationVariableIndices.Count)
            {
                // Keep the completed atlas resident, release only the cell-sized
                // streaming textures, and proceed serially to avoid a 27-texture
                // peak on Quest.
                result.Panel = null;
                materializationVariableCursor++;
                spatialWorkflowStep = SpatialWorkflowStep.Materializing;
                SetStatus("Variable layer " + materializationVariableCursor +
                    " complete. Starting layer " +
                    (materializationVariableCursor + 1) + "/" +
                    materializationVariableIndices.Count + "...");
                StartS4DGridJob();
                return;
            }
            Texture2D previousImage = s4dGridImage;
            if (previousImage != null && !IsAnalysisNodeTexture(previousImage))
                Destroy(previousImage);
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
            s4dGridFailure = string.Empty;
            gridStale = false;
            Array.Clear(facetCellStale, 0, facetCellStale.Length);
            Array.Clear(facetCellInspected, 0, facetCellInspected.Length);
            Array.Clear(facetCellBoundarySuspect, 0,
                facetCellBoundarySuspect.Length);
            Array.Clear(facetCellPinned, 0, facetCellPinned.Length);
            selectedCellPinned = false;
            for (int index = 0; index < facetCellSnapshotIds.Length; index++)
                facetCellSnapshotIds[index] = result.SnapshotId;
            rematerializingStaleCells = false;
            Array.Clear(rematerializedCellMask, 0,
                rematerializedCellMask.Length);
            placementConfirmed = true;
            spatialWorkflowStep = SpatialWorkflowStep.Result;
            materializationVariableCursor = -1;
            progress = 1.0f;
            AnalysisNodeState committed = CommitAnalysisNode(result);
            if (completedRematerialization)
                RecordTrailEvent("RE-MATERIALIZE",
                    "complete snapshot " + result.SnapshotId, committed);
            StartDigestForNode(committed);
            draftOperation = DraftOperation.None;
            draftSourceNodeId = string.Empty;
            ResetSelectedTicksToActiveBuckets();
            SetStatus(completedRematerialization
                ? "Complete Grid re-materialized atomically as " +
                    committed.nodeId + ". The stale parent snapshot remains in SlabTrail."
                : "Validated MatPlotAgent Grid committed as " + committed.nodeId +
                    ". Job " + result.JobId + ".");
            BuildStage();
            if (facetGridCanvas != null)
            {
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
            }
            BuildMaterializedVariableLayerPanels();
            if (trailCanvas != null && trailCanvas.gameObject.activeSelf)
                BuildTrailPanel();
        }

        private void CancelS4DGridJob()
        {
            if (s4dClient != null)
                s4dClient.Cancel();
            SetStatus("Cancelling the S4D Grid job...");
        }

        private void RetryS4DGridJob()
        {
            if (IsPending(PendingJob.Analysis))
                return;
            spatialWorkflowStep = SpatialWorkflowStep.Materializing;
            StartS4DGridJob();
        }

        private void RematerializeS4DGrid()
        {
            Array.Clear(rematerializedCellMask, 0,
                rematerializedCellMask.Length);
            for (int index = 0; index < facetCellStale.Length; index++)
                rematerializedCellMask[index] = facetCellStale[index];
            rematerializingStaleCells =
                s4dGridImage != null &&
                Array.Exists(rematerializedCellMask, value => value);
            gridStale = s4dGridImage != null;
            if (facetGridCanvas != null)
            {
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
            }
            SetStatus(rematerializingStaleCells
                ? "Re-materializing a complete replacement Grid; the stale snapshot remains visible until commit..."
                : "Re-materializing the complete Facet Grid...");
            spatialWorkflowStep = SpatialWorkflowStep.Materializing;
            StartS4DGridJob();
        }

        private void ClearChart()
        {
            if (chartImage != null)
                Destroy(chartImage);
            chartImage = null;
            progress = 0;
        }

        private void ClearS4DGrid()
        {
            DestroyGroundAggregateVolume();
            DestroyTextures(streamingCellTextures);
            if (s4dGridImage != null && !IsAnalysisNodeTexture(s4dGridImage))
                Destroy(s4dGridImage);
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
        }

        private void ResetAnalysisWorkingView()
        {
            // Switching variables keeps committed snapshots in this session.
            ClearRetainedResultViews();
            ClearS4DGrid();
            currentAnalysisNode = null;
            draftSourceNodeId = string.Empty;
            draftOperation = DraftOperation.None;
            placementConfirmed = false;
        }


        private void StartMatPlotJob()
        {
            if (selectedDataset == null || IsPending(PendingJob.Analysis) || string.IsNullOrWhiteSpace(prompt))
                return;
            string csv;
            try
            {
                string output = Path.Combine(Application.temporaryCachePath, "VolumeSTCubeSpatial");
                csv = VolumeSTCubeRawSliceReader.ExportRegionCsv(selectedDataset, selectedTime, selectedZ, output, region);
            }
            catch (Exception exception)
            {
                SetStatus("Region export failed: " + exception.Message);
                return;
            }

            SetPending(PendingJob.Analysis, true);
            progress = 0.0f;
            BuildStage();
            string contextualPrompt = prompt.Trim() +
                "\n\nThis CSV is a grounded XY slab from a continuous XYZ+T field." +
                " Columns are x, y, value, region. Region is either selected or rest." +
                " Variable: " + selectedDataset.Name + ". Time: " + selectedDataset.GetTimeLabel(selectedTime) +
                ". Z layer: " + selectedZ + " of " + selectedDataset.DimZ + "." +
                " Clearly distinguish selected from rest and do not invent physical units.";
            VolumeSTCubeMatPlotClient client = new VolumeSTCubeMatPlotClient(matPlotUrl, 180);
            StartCoroutine(client.Run(contextualPrompt, csv, OnJobProgress, OnJobComplete));
        }
    }
}
