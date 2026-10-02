using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    /// <summary>Split out of VolumeSTCubeQuestSpatialWorkbench: Panels side of the workbench.</summary>
    public sealed partial class VolumeSTCubeQuestSpatialWorkbench
    {
        private void PositionQuestPanel(Canvas canvas, Vector3 worldPosition,
            float worldScale)
        {
            if (canvas == null)
                return;
            canvas.transform.position = worldPosition;
            canvas.transform.localScale = Vector3.one * worldScale;
            FacePanelTowardViewer(canvas.transform);
        }

        private void UpdatePanelTypography()
        {
            if (Time.unscaledTime < nextDesktopTypographyRefresh)
                return;
            nextDesktopTypographyRefresh = Time.unscaledTime + 0.35f;
            Canvas[] panels =
            {
                panelCanvas, mainMenuCanvas, boundaryCanvas, trailCanvas,
                facetGridCanvas, aiFindingsCanvas, slabPreviewCanvas,
                intentCanvas, draftCanvas
            };
            for (int panelIndex = 0; panelIndex < panels.Length; panelIndex++)
            {
                Canvas panel = panels[panelIndex];
                if (panel == null || !panel.gameObject.activeInHierarchy)
                    continue;
                MaximizeTextInsidePanel(panel);
            }
        }

        private void MaximizeTextInsidePanel(Canvas panel)
        {
            Text[] labels = panel.GetComponentsInChildren<Text>(true);
            for (int index = 0; index < labels.Length; index++)
            {
                Text label = labels[index];
                if (label == null || !label.gameObject.activeInHierarchy)
                    continue;
                RectTransform labelRect = label.rectTransform;
                Button ownerButton = label.GetComponentInParent<Button>();
                float availableHeight = Mathf.Abs(labelRect.rect.height);
                if (ownerButton != null &&
                    label.transform.IsChildOf(ownerButton.transform))
                {
                    RectTransform buttonRect = ownerButton.transform as RectTransform;
                    if (buttonRect != null)
                    {
                        // Only enlarge the text's usable inset. The button and
                        // panel geometry remain exactly as authored.
                        labelRect.sizeDelta = new Vector2(
                            Mathf.Max(8.0f, buttonRect.rect.width - 8.0f),
                            Mathf.Max(8.0f, buttonRect.rect.height - 6.0f));
                        labelRect.anchoredPosition = new Vector2(0.0f, 1.0f);
                        availableHeight = Mathf.Abs(labelRect.rect.height);
                    }
                }
                if (availableHeight < 4.0f)
                    continue;
                bool multiline = label.text.IndexOf('\n') >= 0;
                float fillRatio = ownerButton != null ? 0.84f :
                    multiline ? 0.76f : 0.92f;
                float platformMaximum = 52.0f;
                float platformMinimum = 14.0f;
#if UNITY_EDITOR || SLABLAB_FLAT
                if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
                {
                    platformMaximum = 72.0f;
                    platformMinimum = 18.0f;
                }
#endif
                float maximum = Mathf.Clamp(availableHeight * fillRatio,
                    platformMinimum, platformMaximum);
                float minimum = Mathf.Min(maximum, platformMinimum);
                TMPro.TextMeshProUGUI crisp =
                    label.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                if (crisp == null)
                {
                    crisp = AddCrispTextOverlay(label, label.text, maximum,
                        minimum, ownerButton != null ||
                        label.fontStyle == FontStyle.Bold);
                }
                if (crisp != null)
                {
                    // The workflow mutates legacy Text values in several later
                    // stages. Mirror them into one consistent SDF renderer so
                    // every panel stays as sharp as the opening screen.
                    crisp.text = label.text;
                    crisp.color = label.color;
                    crisp.alignment = ToTmpAlignment(label.alignment);
                    crisp.fontStyle = ownerButton != null ||
                        label.fontStyle == FontStyle.Bold
                            ? TMPro.FontStyles.Bold
                            : TMPro.FontStyles.Normal;
                    crisp.enableWordWrapping = multiline;
                    crisp.enableAutoSizing = true;
                    crisp.fontSizeMin = minimum;
                    crisp.fontSizeMax = maximum;
                    crisp.overflowMode = TMPro.TextOverflowModes.Truncate;
                    crisp.lineSpacing = multiline ? -12.0f : 0.0f;
                }
            }
        }

        public void TogglePanel()
        {
            if (mainMenuCanvas == null)
                return;
            bool next = !mainMenuCanvas.gameObject.activeSelf;
            if (next)
            {
                ShowPrimaryTool(mainMenuCanvas);
                BuildMainMenu();
            }
            else
            {
                mainMenuCanvas.gameObject.SetActive(false);
            }
        }

        private void UpdatePanelGrab()
        {
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
            {
                grabbedPanel = null;
                return;
            }
            if (rayInteractor == null)
                return;
            if (rayInteractor.GripPressed)
            {
                if (UnityEngine.Physics.Raycast(rayInteractor.PointerRay, out UnityEngine.RaycastHit hit,
                    rayInteractor.maxDistance, 1 << 5, QueryTriggerInteraction.Collide))
                {
                    VolumeSTCubeQuestPanelHandle handle =
                        hit.collider.GetComponentInParent<VolumeSTCubeQuestPanelHandle>();
                    if (handle != null)
                    {
                        if (!VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
                            questImportHeadLocked = false;
                        grabbedPanel = handle.transform;
                        if (workflowToolbarCanvas != null &&
                            grabbedPanel == workflowToolbarCanvas.transform)
                            viewState.workflowToolbarPinned = true;
                        grabbedPanelDistance = Mathf.Clamp(hit.distance, 0.45f, 2.2f);
                        SetStatus("Panel released from its anchor. Move the controller; release Grip to pin.");
                    }
                }
            }

            if (grabbedPanel != null && rayInteractor.GripHeld)
            {
                grabbedPanel.position = rayInteractor.PointerRay.origin +
                    rayInteractor.PointerRay.direction * grabbedPanelDistance;
                FacePanelTowardViewer(grabbedPanel);
            }

            if (grabbedPanel != null && rayInteractor.GripReleased)
            {
                SpatialAxisRigState axisRig = FindAxisRigByRoot(grabbedPanel);
                if (axisRig != null)
                {
                    SnapAxisRigToNearestFieldFace(axisRig);
                    SetStatus("Axis body magnetically attached to the nearest Field face.");
                }
                else
                    SetStatus(grabbedPanel.name + " pinned in the workspace.");
                grabbedPanel = null;
            }
        }

        private void FacePanelTowardViewer(Transform panel)
        {
            if (panel == null || xrCamera == null)
                return;
            Vector3 awayFromViewer = panel.position - xrCamera.transform.position;
            awayFromViewer.y = 0.0f;
            if (awayFromViewer.sqrMagnitude > 0.001f)
                panel.rotation = Quaternion.LookRotation(awayFromViewer.normalized, Vector3.up);
        }

        private void CreateWorkflowToolbar()
        {
            workflowToolbarCanvas = CreateFloatingCanvas(
                "S4D persistent workflow toolbar",
                new Vector3(0.0f, 1.05f, 0.82f),
                new Vector2(1510, 128), 0.00052f, Cyan);
            BuildWorkflowToolbar();
        }

        private void BuildWorkflowToolbar()
        {
            if (workflowToolbarCanvas == null)
                return;
            RectTransform content = workflowToolbarCanvas.GetComponent<RectTransform>();
            ClearChildren(content);
            string[] labels =
            {
                "MATPLOT\nINTENT", "FULL\nMATRIX",
                "PIVOT", "DRILL", "ROLL-UP", "LEGACY\nPANEL", "RESTART"
            };
            Action[] actions =
            {
                OpenIntentEditor, BeginGridPlacement,
                () => BeginDraft(DraftOperation.Pivot),
                () => BeginDraft(DraftOperation.Drill),
                () => BeginDraft(DraftOperation.RollUp), ToggleLegacyPanel,
                RestartApplicationWorkflow
            };
            Color[] colors =
            {
                AreSpatialAxisBindingsComplete(out _) && authorBoundaryConfirmed
                    ? Purple : Card,
                intentConfigured ? Amber : Card,
                Purple, TimeColor, Green, Card, Danger
            };
            float width = 188.0f;
            for (int index = 0; index < labels.Length; index++)
            {
                Button toolbarButton = CreateButton(content, labels[index],
                    new Vector2(-594 + index * 198, -5),
                    new Vector2(width - 8, 64), colors[index], actions[index]);
#if UNITY_EDITOR || SLABLAB_FLAT
                if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
                {
                    Text toolbarLabel = toolbarButton != null
                        ? toolbarButton.GetComponentInChildren<Text>() : null;
                    if (toolbarLabel != null)
                    {
                        bool multiline = labels[index].IndexOf('\n') >= 0;
                        int fillFontSize = multiline
                            ? 30
                            : labels[index].Length >= 7 ? 43 : 50;
                        toolbarLabel.fontSize = fillFontSize;
                        toolbarLabel.resizeTextForBestFit = false;
                        toolbarLabel.resizeTextMinSize = fillFontSize;
                        toolbarLabel.resizeTextMaxSize = fillFontSize;
                        toolbarLabel.fontStyle = FontStyle.Bold;
                        toolbarLabel.lineSpacing = multiline ? 0.76f : 1.0f;
                        toolbarLabel.rectTransform.sizeDelta =
                            new Vector2(width - 18.0f, 58.0f);
                    }
                }
#endif
                UpgradeButtonLabelToCrispText(toolbarButton, labels[index]);
            }
        }

        private void ToolbarGenerateSlab()
        {
            if (!AreSpatialAxisBindingsComplete(out string missing))
            {
                SetStatus("Generate Slab locked: " + missing);
                return;
            }
            stage = Stage.Slab;
            PreviewSlab();
        }

        private void ToggleLegacyPanel()
        {
            viewState.legacyPanelVisible = !viewState.legacyPanelVisible;
            if (panelCanvas != null)
            {
                panelCanvas.gameObject.SetActive(viewState.legacyPanelVisible);
                if (viewState.legacyPanelVisible)
                    BuildStage();
            }
            SetStatus(viewState.legacyPanelVisible
                ? "Legacy panel shown for comparison."
                : "Legacy panel hidden; spatial axis controls remain active.");
        }

        private void UpdateWorkflowToolbarFollow()
        {
            if (workflowToolbarCanvas == null || xrCamera == null ||
                viewState.workflowToolbarPinned ||
                !workflowToolbarCanvas.gameObject.activeSelf)
                return;
            Vector3 forward = Vector3.ProjectOnPlane(
                xrCamera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = xrCamera.transform.forward;
            Vector3 targetPosition = xrCamera.transform.position +
                forward * 1.02f - Vector3.up * 0.48f;
            float follow = 1.0f - Mathf.Exp(-3.8f * Time.unscaledDeltaTime);
            workflowToolbarCanvas.transform.position = Vector3.Lerp(
                workflowToolbarCanvas.transform.position, targetPosition, follow);
            Quaternion targetRotation = Quaternion.LookRotation(forward, Vector3.up);
            workflowToolbarCanvas.transform.rotation = Quaternion.Slerp(
                workflowToolbarCanvas.transform.rotation, targetRotation, follow);
        }

        private void BuildMainMenu()
        {
            if (mainMenuContent == null)
                return;
            ClearChildren(mainMenuContent);
            CreateText(mainMenuContent, "S4D CANVAS", 18, FontStyle.Bold,
                new Vector2(0, 300), new Vector2(480, 28), TextAnchor.MiddleLeft, Muted);
            CreateText(mainMenuContent, "MAIN MENU", 34, FontStyle.Bold,
                new Vector2(0, 258), new Vector2(480, 48), TextAnchor.MiddleLeft, Ink);
            CreateText(mainMenuContent, "Continuous evidence and analysis workspace",
                16, FontStyle.Normal, new Vector2(0, 222), new Vector2(480, 28),
                TextAnchor.MiddleLeft, Muted);

            mainMenuDataLabel = CreateText(mainMenuContent,
                viewState.cubeVisible ? "DATA CONTAINER  /  VISIBLE" : "DATA CONTAINER  /  HIDDEN",
                15, FontStyle.Bold, new Vector2(0, 166), new Vector2(460, 26),
                TextAnchor.MiddleLeft, viewState.cubeVisible ? Green : Muted);

            CreateMenuButton(mainMenuContent,
                viewState.cubeVisible ? "Hide continuous data" : "Show continuous data",
                "Toggle the S4D Cube Container",
                new Vector2(0, 105), Cyan, ToggleDataVisibility);
            CreateMenuButton(mainMenuContent,
                "Author Boundary",
                "Time / Depth / Horizontal buckets",
                new Vector2(0, 8), Amber, ToggleBoundaryPanel);
            CreateMenuButton(mainMenuContent,
                smallMultiples ? "Display: small multiples" : "Display: animated volume",
                "Switch the continuous-field presentation",
                new Vector2(0, -89), Green, ToggleDataPresentation);
            CreateMenuButton(mainMenuContent,
                "Slab Frame",
                "Configure a new analysis",
                new Vector2(0, -186), Purple, ToggleSlabFrame);
            CreateMenuButton(mainMenuContent,
                "SlabTrail",
                "History, snapshots and workspace navigation",
                new Vector2(0, -283), Cyan, ToggleTrailPanel);
        }

        private void CreateSlabPreviewPanel()
        {
            slabPreviewCanvas = CreateFloatingCanvas(
                "FacetSlab Configuration Preview",
                // Upper-left lane, inside the default Quest/desktop forward view.
                // Its right edge stops before the intent composer begins.
                SlabPreviewDockPosition,
                new Vector2(900, 610),
                0.00066f,
                Green);
            slabPreviewContent = slabPreviewCanvas.GetComponent<RectTransform>();
            slabPreviewCanvas.sortingOrder = 118;
            slabPreviewCanvas.gameObject.SetActive(false);
        }

        private static void AddDesktopPanelFocusTarget(Canvas panel,
            Action focusAction)
        {
            if (panel == null || focusAction == null)
                return;
            VolumeSTCubeQuestClickTarget target =
                panel.GetComponent<VolumeSTCubeQuestClickTarget>();
            if (target == null)
                target = panel.gameObject.AddComponent<
                    VolumeSTCubeQuestClickTarget>();
            target.AllowDesktopMouseDown = true;
            target.Clicked = focusAction;
        }

        private void BuildVrKeyboard()
        {
            CreateText(intentContent, "TYPE ANALYSIS TASK",
                27, FontStyle.Bold, new Vector2(0, 208), new Vector2(585, 38),
                TextAnchor.MiddleCenter, Ink);
            intentPromptText = CreateTextBox(intentContent, prompt,
                new Vector2(0, 155), new Vector2(575, 66), 16);

            string[] rows = { "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM" };
            float[] rowY = { 85.0f, 37.0f, -11.0f };
            for (int row = 0; row < rows.Length; row++)
            {
                string letters = rows[row];
                float width = letters.Length * 50.0f;
                for (int index = 0; index < letters.Length; index++)
                {
                    string key = letters[index].ToString();
                    float x = -width * 0.5f + 25.0f + index * 50.0f;
                    CreateButton(intentContent, key, new Vector2(x, rowY[row]),
                        new Vector2(44, 40), Card,
                        () => AppendVrKeyboardText(key));
                }
            }

            CreateButton(intentContent, "SPACE", new Vector2(-135, -65),
                SlabLabLayout.ButtonSizeStandard, Card, () => AppendVrKeyboardText(" "));
            CreateButton(intentContent, "BACKSPACE", new Vector2(65, -65),
                new Vector2(170, 42), Card, VrKeyboardBackspace);
            CreateButton(intentContent, "CLEAR", new Vector2(215, -65),
                new Vector2(110, 42), Card, VrKeyboardClear);
            CreateButton(intentContent, "CANCEL", new Vector2(-155, -151),
                new Vector2(240, 52), Card, () => CloseVrKeyboard(false));
            CreateButton(intentContent, "DONE", new Vector2(155, -151),
                new Vector2(240, 52), Purple, () => CloseVrKeyboard(true));
        }

        private void BuildSlabPreviewPanel()
        {
            if (slabPreviewContent == null)
                return;
            ClearChildren(slabPreviewContent);

            bool waitingForIntent =
                spatialWorkflowStep < SpatialWorkflowStep.SourcePreviewReady;
            CreateText(slabPreviewContent,
                waitingForIntent ? "SLAB FRAME READY" : "SOURCE PREVIEW",
                14, FontStyle.Bold, new Vector2(0, 266), new Vector2(820, 24),
                TextAnchor.MiddleLeft, Muted);
            CreateText(slabPreviewContent,
                waitingForIntent
                    ? activeGridColumns + " TIME RANGE" +
                        (activeGridColumns == 1 ? string.Empty : "S") +
                        "  ×  " + activeGridRows + " DEPTH RANGE" +
                        (activeGridRows == 1 ? string.Empty : "S")
                    : activeGridColumns + " × " + activeGridRows +
                        " INTERVAL-MEAN GRID",
                27, FontStyle.Bold, new Vector2(0, 232), new Vector2(820, 38),
                TextAnchor.MiddleLeft, Ink);
            if (sourcePreviewRenderVariableIndex >= 0 &&
                sourcePreviewRenderVariableIndex < datasets.Count)
            {
                VolumeSTCubeSliceDataset layerDataset =
                    datasets[sourcePreviewRenderVariableIndex];
                CreateText(slabPreviewContent,
                    "VARIABLE LAYER  /  " + layerDataset.Name.ToUpperInvariant() +
                        (string.IsNullOrWhiteSpace(layerDataset.Unit)
                            ? string.Empty : "  [" + layerDataset.Unit + "]"),
                    12, FontStyle.Bold, new Vector2(250, 266),
                    new Vector2(320, 24), TextAnchor.MiddleRight,
                    VariableColor);
            }
            CreateText(slabPreviewContent,
                matrixPreviewAtlas != null
                    ? "Raw STC evidence · missing values excluded · shared scale"
                    : IsPending(PendingJob.SourcePreview)
                        ? "Computing interval means..."
                        : spatialWorkflowStep < SpatialWorkflowStep.Intent
                            ? "Confirm Time and Depth to continue."
                            : "Apply an intent to build the preview.",
                14, FontStyle.Normal, new Vector2(0, 199), new Vector2(820, 25),
                TextAnchor.MiddleLeft, matrixPreviewAtlas != null ? Green : Amber);

            int columns = Mathf.Max(1, activeGridColumns);
            int rows = Mathf.Max(1, activeGridRows);
            string[] timeLabels = BucketLabels(activeTimeBuckets,
                new[] { "before", "during", "after" });
            string[] depthLabels = BucketLabels(activeDepthBuckets,
                new[] { "surface", "middle", "deep" });
            string[] columnLabels = activeGridTransposed ? depthLabels : timeLabels;
            string[] rowLabels = activeGridTransposed ? timeLabels : depthLabels;
            Color columnColor = activeGridTransposed ? DepthColor : TimeColor;
            Color rowColor = activeGridTransposed ? TimeColor : DepthColor;
            float cellWidth = Mathf.Min(190.0f, 570.0f / columns);
            float cellHeight = Mathf.Min(96.0f, 288.0f / rows);
            Vector2 gridCenter = new Vector2(58, 30);

            for (int column = 0; column < columns; column++)
            {
                CreateText(slabPreviewContent, columnLabels[column], 15, FontStyle.Bold,
                    gridCenter + new Vector2((column - (columns - 1) * 0.5f) * cellWidth, 166),
                    new Vector2(cellWidth - 10, 24), TextAnchor.MiddleCenter, columnColor);
            }

            for (int row = 0; row < rows; row++)
            {
                CreateText(slabPreviewContent, rowLabels[row], 14, FontStyle.Bold,
                    gridCenter + new Vector2(-315.0f,
                        ((rows - 1) * 0.5f - row) * cellHeight),
                    SlabLabLayout.RowChipSize, TextAnchor.MiddleRight, rowColor);
                for (int column = 0; column < columns; column++)
                {
                    int timeIndex = activeGridTransposed ? row : column;
                    int depthIndex = activeGridTransposed ? column : row;
                    GameObject cellObject = new GameObject(
                        depthLabels[depthIndex] + " x " + timeLabels[timeIndex] +
                            " data preview",
                        typeof(RectTransform));
                    cellObject.layer = 5;
                    cellObject.transform.SetParent(slabPreviewContent, false);
                    RectTransform cell = cellObject.GetComponent<RectTransform>();
                    cell.sizeDelta = new Vector2(cellWidth - 14, cellHeight - 14);
                    cell.anchoredPosition = gridCenter + new Vector2(
                        (column - (columns - 1) * 0.5f) * cellWidth,
                        ((rows - 1) * 0.5f - row) * cellHeight);
                    Image cellBackground = cellObject.AddComponent<Image>();
                    cellBackground.sprite = RoundedUiSprite();
                    cellBackground.type = Image.Type.Sliced;
                    cellBackground.color = new Color(0.015f, 0.033f, 0.050f, 1.0f);
                    Shadow cellShadow = cellObject.AddComponent<Shadow>();
                    cellShadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.48f);
                    cellShadow.effectDistance = new Vector2(3, -4);
                    Outline outline = cellObject.AddComponent<Outline>();
                    outline.effectColor = new Color(Green.r, Green.g, Green.b, 0.48f);
                    outline.effectDistance = new Vector2(1.5f, -1.5f);

                    if (matrixPreviewAtlas != null)
                    {
                        RawImage preview = CreateRawImage(
                            cell, matrixPreviewAtlas, new Vector2(0, -5),
                            new Vector2(cellWidth - 22, cellHeight - 28));
                        preview.uvRect = new Rect(
                            timeIndex / (float)Mathf.Max(1, activeTimeBuckets.Length),
                            (activeDepthBuckets.Length - 1 - depthIndex) /
                                (float)Mathf.Max(1, activeDepthBuckets.Length),
                            1.0f / Mathf.Max(1, activeTimeBuckets.Length),
                            1.0f / Mathf.Max(1, activeDepthBuckets.Length));
                    }
                    else if (matrixTextures != null &&
                        spatialWorkflowStep >= SpatialWorkflowStep.SourcePreviewReady)
                    {
                        int fallbackIndex = depthIndex *
                            Mathf.Max(1, activeTimeBuckets.Length) + timeIndex;
                        Texture2D fallback = fallbackIndex >= 0 &&
                            fallbackIndex < matrixTextures.Length
                                ? matrixTextures[fallbackIndex]
                                : null;
                        if (fallback != null)
                        {
                            CreateRawImage(cell, fallback, new Vector2(0, -5),
                                new Vector2(cellWidth - 22, cellHeight - 28));
                        }
                        else
                        {
                            CreateText(cell, IsPending(PendingJob.SourcePreview)
                                    ? "COMPUTING\nLOCAL PREVIEW"
                                    : timeLabels[timeIndex].ToUpperInvariant() +
                                        "\nWAITING FOR INTENT",
                                10, FontStyle.Bold, new Vector2(0, -5),
                                new Vector2(cellWidth - 22, cellHeight - 28),
                                TextAnchor.MiddleCenter, Muted);
                        }
                    }
                    else
                    {
                        CreateText(cell, IsPending(PendingJob.SourcePreview)
                                ? "COMPUTING\nINTERVAL MEAN"
                                : timeLabels[timeIndex].ToUpperInvariant() +
                                    "\nWAITING FOR MATPLOT INTENT",
                            10, FontStyle.Bold, new Vector2(0, -5),
                            new Vector2(cellWidth - 22, cellHeight - 28),
                            TextAnchor.MiddleCenter, Muted);
                    }

                    CreateText(cell,
                        depthLabels[depthIndex] + "  x  " + timeLabels[timeIndex],
                        10, FontStyle.Bold, new Vector2(0, cellHeight * 0.5f - 12),
                        new Vector2(cellWidth - 20, 18), TextAnchor.MiddleLeft,
                        intentConfigured ? Purple : Color.Lerp(TimeColor, DepthColor, 0.5f));
                }
            }

            CreateText(slabPreviewContent,
                FacetAxisSummary().ToUpperInvariant() + "  ·  " +
                    (selectedDataset != null
                        ? selectedDataset.Name.ToUpperInvariant() : "NO VARIABLE") +
                    "  ·  " + (intentConfigured ? intentMode : "WAITING FOR INTENT"),
                12, FontStyle.Bold, new Vector2(-150, -246), new Vector2(560, 28),
                TextAnchor.MiddleLeft, intentConfigured ? Purple : Amber);
            CreateButton(slabPreviewContent, "REBUILD PREVIEW",
                new Vector2(305, -242), new Vector2(235, 48), Green, PreviewSlab);
            CreateButton(slabPreviewContent, "CLOSE",
                new Vector2(355, 240), new Vector2(105, 36), Card,
                () => slabPreviewCanvas.gameObject.SetActive(false));

            // The first preview is viewed beside the full Field, so legacy
            // bitmap text becomes unreadable at this distance. Preserve the
            // complete layout and replace only its labels with SDF text.
            UpgradeCanvasLabelsToCrispText(slabPreviewContent, null);
        }

        private void BuildLegacySlabPreviewPanel()
        {
            if (slabPreviewContent == null)
                return;
            ClearChildren(slabPreviewContent);

            CreateText(slabPreviewContent, "FACETSLAB  /  CONFIGURATION PREVIEW",
                14, FontStyle.Bold, new Vector2(0, 240), new Vector2(750, 24),
                TextAnchor.MiddleLeft, Muted);
            CreateText(slabPreviewContent, "SLAB FRAME  —  FRAME",
                28, FontStyle.Bold, new Vector2(0, 207), new Vector2(750, 38),
                TextAnchor.MiddleLeft, Ink);
            CreateText(slabPreviewContent,
                "Wire skeleton only  •  selected buckets become tick-blocks  •  no charts",
                14, FontStyle.Normal, new Vector2(0, 174), new Vector2(750, 25),
                TextAnchor.MiddleLeft, Green);

            string[] timeLabels = { "before", "during", "after" };
            string[] depthLabels = { "surface", "middle", "deep" };
            const float cellWidth = 165.0f;
            const float cellHeight = 86.0f;
            Vector2 gridCenter = new Vector2(55, 18);

            for (int column = 0; column < 3; column++)
            {
                CreateText(slabPreviewContent, timeLabels[column], 15, FontStyle.Bold,
                    gridCenter + new Vector2((column - 1) * cellWidth, 145),
                    new Vector2(cellWidth - 10, 24), TextAnchor.MiddleCenter, TimeColor);
            }

            for (int row = 0; row < 3; row++)
            {
                CreateText(slabPreviewContent, depthLabels[row], 14, FontStyle.Bold,
                    gridCenter + new Vector2(-cellWidth * 2.0f, (1 - row) * cellHeight),
                    new Vector2(135, 28), TextAnchor.MiddleRight, DepthColor);
                for (int column = 0; column < 3; column++)
                {
                    GameObject cellObject = new GameObject(
                        depthLabels[row] + " x " + timeLabels[column] + " skeleton cell",
                        typeof(RectTransform));
                    cellObject.layer = 5;
                    cellObject.transform.SetParent(slabPreviewContent, false);
                    RectTransform cell = cellObject.GetComponent<RectTransform>();
                    cell.sizeDelta = new Vector2(cellWidth - 10, cellHeight - 10);
                    cell.anchoredPosition = gridCenter + new Vector2(
                        (column - 1) * cellWidth, (1 - row) * cellHeight);
                    Image skeletonBackground = cellObject.AddComponent<Image>();
                    skeletonBackground.sprite = RoundedUiSprite();
                    skeletonBackground.type = Image.Type.Sliced;
                    skeletonBackground.color = new Color(0.018f, 0.045f, 0.062f, 0.96f);
                    Outline outline = cellObject.AddComponent<Outline>();
                    outline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.42f);
                    outline.effectDistance = new Vector2(1.5f, -1.5f);
                    CreateText(cell,
                        intentConfigured
                            ? intentMode.Replace("CHARACTERIZE ", string.Empty) + "\nSPEC READY"
                            : depthLabels[row] + " × " + timeLabels[column] + "\nPENDING INTENT",
                        10, FontStyle.Bold, Vector2.zero,
                        new Vector2(cellWidth - 22, 42), TextAnchor.MiddleCenter,
                        intentConfigured ? Purple : new Color(Muted.r, Muted.g, Muted.b, 0.82f));
                }
            }

            CreateText(slabPreviewContent,
                "TIME  FACETED  ×  DEPTH  FACETED", 13, FontStyle.Bold,
                new Vector2(-205, -194), new Vector2(340, 25),
                TextAnchor.MiddleLeft, Color.Lerp(TimeColor, DepthColor, 0.5f));
            CreateText(slabPreviewContent,
                "HORIZONTAL  MAPPED   •   VARIABLE  FIXED: " +
                (selectedDataset != null ? selectedDataset.Name : "—"),
                12, FontStyle.Normal, new Vector2(-160, -220), new Vector2(430, 24),
                TextAnchor.MiddleLeft, Muted);
            CreateText(slabPreviewContent,
                intentConfigured ? "GRID TASK  /  " + intentMode : "GRID TASK  /  WAITING FOR INTENT",
                11, FontStyle.Bold, new Vector2(-160, -244), new Vector2(430, 22),
                TextAnchor.MiddleLeft, intentConfigured ? Purple : Amber);
            CreateButton(slabPreviewContent, "REGENERATE FRAME",
                new Vector2(265, -205), new Vector2(225, 46), Green, PreviewSlab);
            CreateButton(slabPreviewContent, "CLOSE",
                new Vector2(315, 215), new Vector2(105, 36), Card,
                () => slabPreviewCanvas.gameObject.SetActive(false));
        }

        private void CreatePanel()
        {
            GameObject panelObject = new GameObject("Slab Lab Spatial Console", typeof(RectTransform));
            panelObject.layer = 5;
            panelObject.transform.SetParent(transform, false);
            panelObject.transform.localPosition = PrimaryToolDockPosition;
            panelObject.transform.localRotation = Quaternion.identity;
            // About 69 cm wide at the default Quest placement: comfortably readable
            // without requiring the user to lean in, while remaining a hand-scale tool.
            panelObject.transform.localScale = Vector3.one * 0.00070f;
            panelCanvas = panelObject.AddComponent<Canvas>();
            panelCanvasGroup = panelObject.AddComponent<CanvasGroup>();
            panelCanvas.renderMode = UnityEngine.RenderMode.WorldSpace;
            panelCanvas.worldCamera = xrCamera;
            panelCanvas.sortingOrder = 100;
            CanvasScaler scaler = panelObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit =
#if UNITY_EDITOR || SLABLAB_FLAT
                VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled ? 48.0f :
#endif
                24.0f;
            scaler.referencePixelsPerUnit = 100.0f;
            panelObject.AddComponent<GraphicRaycaster>();

            RectTransform rect = panelObject.GetComponent<RectTransform>();
            rect.sizeDelta = SlabLabLayout.PanelCanvasSize;
            Image panelBackground = panelObject.AddComponent<Image>();
            panelBackground.color = Panel;
            panelBackground.sprite = RoundedUiSprite();
            panelBackground.type = Image.Type.Sliced;
            Shadow panelShadow = panelObject.AddComponent<Shadow>();
            panelShadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.70f);
            panelShadow.effectDistance = new Vector2(14.0f, -16.0f);
            Outline panelOutline = panelObject.AddComponent<Outline>();
            panelOutline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.56f);
            panelOutline.effectDistance = new Vector2(2, -2);
            BoxCollider panelCollider = panelObject.AddComponent<BoxCollider>();
            panelCollider.isTrigger = true;
            panelCollider.center = new Vector3(0.0f, 0.0f, 14.0f);
            panelCollider.size = new Vector3(1120.0f, 840.0f, 8.0f);
            panelObject.AddComponent<VolumeSTCubeQuestPanelHandle>().accent = Cyan;
            AddDesktopPanelFocusTarget(panelCanvas,
                FocusDesktopMatrixPanel);

            GameObject headerWash = new GameObject("Console header wash", typeof(RectTransform));
            headerWash.transform.SetParent(rect, false);
            RectTransform headerWashRect = headerWash.GetComponent<RectTransform>();
            headerWashRect.anchorMin = new Vector2(0, 1);
            headerWashRect.anchorMax = new Vector2(1, 1);
            headerWashRect.pivot = new Vector2(0.5f, 1);
            headerWashRect.anchoredPosition = new Vector2(0, -3);
            headerWashRect.sizeDelta = new Vector2(-6, 132);
            Image headerWashImage = headerWash.AddComponent<Image>();
            headerWashImage.sprite = RoundedUiSprite();
            headerWashImage.type = Image.Type.Sliced;
            headerWashImage.color = new Color(0.02f, 0.12f, 0.16f, 0.28f);
            headerWashImage.raycastTarget = false;

            GameObject topAccent = new GameObject("Console top accent", typeof(RectTransform));
            topAccent.transform.SetParent(rect, false);
            RectTransform topAccentRect = topAccent.GetComponent<RectTransform>();
            topAccentRect.anchorMin = new Vector2(0, 1);
            topAccentRect.anchorMax = new Vector2(1, 1);
            topAccentRect.pivot = new Vector2(0.5f, 1);
            topAccentRect.anchoredPosition = Vector2.zero;
            topAccentRect.sizeDelta = new Vector2(0, 6);
            Image topAccentImage = topAccent.AddComponent<Image>();
            topAccentImage.color = Cyan;
            topAccentImage.raycastTarget = false;

            CreateDecorativeSurface(rect, "Navigation dock", new Vector2(0, 298),
                new Vector2(1070, 58), new Color(0.018f, 0.043f, 0.064f, 0.94f));
            Image contentSurface = CreateDecorativeSurface(rect, "Stage surface",
                new Vector2(0, -50), new Vector2(1070, 620),
                new Color(0.014f, 0.030f, 0.047f, 0.82f));
            Shadow contentShadow = contentSurface.gameObject.AddComponent<Shadow>();
            contentShadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.26f);
            contentShadow.effectDistance = new Vector2(0, -7);

            panelBrandText = CreateText(rect, "S4D CANVAS", 15, FontStyle.Bold,
                new Vector2(0, 399), new Vector2(1060, 24), TextAnchor.MiddleLeft, Muted);
            panelGripHintText = CreateText(rect, "GRIP TO MOVE  /  RELEASE TO PIN", 11, FontStyle.Bold,
                new Vector2(0, 399), new Vector2(1060, 24), TextAnchor.MiddleRight, Cyan);
            panelTitleText = CreateText(rect, "DATASET IMPORT", 31, FontStyle.Bold,
                new Vector2(0, 367), new Vector2(1060, 42), TextAnchor.MiddleLeft, Ink);
            panelTitleCrispText = AddCrispTextOverlay(panelTitleText,
                panelTitleText.text, 64.0f, 34.0f, true);
            panelFlowText = CreateText(rect, "Connect -> Stream visible data -> Continuous Field", 16,
                FontStyle.Normal, new Vector2(0, 336), new Vector2(1060, 26), TextAnchor.MiddleLeft, Cyan);

            // The DATA / SLAB / FACET GRID / GROUND / FINDING step buttons used
            // to live in this panel. Desktop navigation now runs through the
            // persistent workflow bar and the Previous / Next buttons, so the
            // panel is left as a display surface (progress bar plus result).

            GameObject content = new GameObject("Spatial stage content", typeof(RectTransform));
            content.transform.SetParent(rect, false);
            panelContent = content.GetComponent<RectTransform>();
            panelContent.sizeDelta = new Vector2(1070, 595);
            panelContent.anchoredPosition = new Vector2(0, -34);
            statusText = CreateText(rect, "Starting...", 17, FontStyle.Normal,
                new Vector2(0, -396), new Vector2(1060, 32), TextAnchor.MiddleLeft, Ink);
            statusCrispText = AddCrispTextOverlay(statusText,
                statusText.text, 34.0f, 18.0f, true);
            if (statusCrispText != null)
                statusCrispText.enableWordWrapping = true;
        }

        private void BuildStage()
        {
            if (panelContent == null)
                return;
            ConfigureDesktopConsoleShell(
                VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled &&
                stage == Stage.Field);
            ClearChildren(panelContent);
            RefreshStepColors();
            if (stage != Stage.DatasetImport && selectedDataset == null)
            {
                // A Wave reconnect clears the dataset while the workspace is
                // already on a later step. Every stage builder dereferences that
                // dataset, so the panel threw once per retry and stayed half
                // drawn. Show the waiting state until live data comes back.
                BuildWaitingForDatasetStage();
                UpgradeCanvasLabelsToCrispText(panelContent, null);
                AnimatePanelRefresh(panelCanvasGroup, ref panelRefreshAnimation);
                return;
            }
            switch (stage)
            {
                case Stage.DatasetImport: BuildDatasetImportStage(); break;
                case Stage.Field: BuildFieldStage(); break;
                case Stage.Slab: BuildSlabStage(); break;
                case Stage.Matrix: BuildMatrixStage(); break;
                case Stage.Analyze: BuildAnalyzeStage(); break;
                case Stage.Result: BuildResultStage(); break;
            }
            // Apply the same SDF typography path at every workflow step;
            // later MatPlot and findings panels must not fall back to tiny
            // dynamic-font labels.
            // The Matrix combines many small labels with frequently rebuilt cell
            // controls. Keeping its native UI Text components avoids transient
            // empty TMP overlays after a pointer-driven redraw.
            if (stage != Stage.Matrix)
                UpgradeCanvasLabelsToCrispText(panelContent, null);
            AnimatePanelRefresh(panelCanvasGroup, ref panelRefreshAnimation);
        }

        private void BuildFieldStage()
        {
            bool surfaceDataset = IsForVrSurfaceDataset;
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
            {
                AlignDesktopVisualization();
                if (IsPending(PendingJob.VariableLoad) || selectedDataset == null)
                {
                    CreateText(panelContent, "LOADING FIELD", 24,
                        FontStyle.Bold, Vector2.zero, new Vector2(520, 60),
                        TextAnchor.MiddleCenter, Cyan);
                    return;
                }
                CreateButton(panelContent,
                    authorBoundaryConfirmed
                        ? "ENTER MAIN WORKSPACE"
                        : surfaceDataset ? "SET TIME RANGE" : "SET TIME + DEPTH",
                    Vector2.zero, new Vector2(470, 68),
                    authorBoundaryConfirmed ? Cyan : Amber,
                    () =>
                    {
                        if (authorBoundaryConfirmed)
                            EnterMainWorkspace();
                        else
                            OpenInitialAuthorBoundary();
                    });
                return;
            }
            CreateText(panelContent, surfaceDataset ? "SURFACE + TIME SETUP" : "FIELD SETUP", 27, FontStyle.Bold,
                new Vector2(-105, 216), new Vector2(680, 42), TextAnchor.MiddleLeft, Ink);
            CreateButton(panelContent, "CHANGE DATASET", new Vector2(360, 216),
                SlabLabLayout.ButtonSizeMedium, Card, OpenDatasetImportStage);
            if (IsPending(PendingJob.VariableLoad))
            {
                CreatePanelCard(panelContent, new Vector2(0, -30),
                    new Vector2(900, 210), Cyan);
                CreateText(panelContent, "LOADING FIELD", 25,
                    FontStyle.Bold, new Vector2(0, 35), new Vector2(820, 40),
                    TextAnchor.MiddleCenter, Cyan);
                CreateText(panelContent, "Preparing the selected STC volume...",
                    17, FontStyle.Normal, new Vector2(0, -22),
                    new Vector2(780, 36), TextAnchor.MiddleCenter, Muted);
                UpgradeCanvasLabelsToCrispText(panelContent,
                    "LOADING FIELD");
                return;
            }

            if (selectedDataset == null)
            {
                CreateText(panelContent, "PREPARING 3D FIELD...", 22,
                    FontStyle.Bold, new Vector2(0, -10), new Vector2(900, 90),
                    TextAnchor.MiddleCenter, Cyan);
                UpgradeCanvasLabelsToCrispText(panelContent,
                    "PREPARING 3D FIELD...");
                return;
            }

            CreatePanelCard(panelContent, new Vector2(0, 44),
                new Vector2(860, 166), Cyan);
            CreateText(panelContent, selectedDataset.Name.ToUpperInvariant(), 28,
                FontStyle.Bold, new Vector2(0, 82), new Vector2(820, 40),
                TextAnchor.MiddleCenter, Ink);
            fieldTimeSummaryText = CreateText(panelContent,
                surfaceDataset
                    ? "TIME  " + TimeRangeSummary() +
                        "\nGEOMETRY  HONG KONG WATER SURFACE (NO DEPTH AXIS)"
                    : "TIME  " + TimeRangeSummary() + "\nDEPTH  " + DepthRangeSummary(),
                18, FontStyle.Bold, new Vector2(0, 16), new Vector2(820, 76),
                TextAnchor.MiddleCenter, Cyan);
            CreateButton(panelContent,
                authorBoundaryConfirmed
                    ? "ENTER MAIN WORKSPACE"
                    : surfaceDataset ? "SET TIME RANGE" : "SET TIME + DEPTH",
                new Vector2(0, -185), new Vector2(460, 58),
                authorBoundaryConfirmed ? Cyan : Amber,
                () =>
                {
                    if (authorBoundaryConfirmed)
                        EnterMainWorkspace();
                    else
                        OpenInitialAuthorBoundary();
                });
            UpgradeCanvasLabelsToCrispText(panelContent,
                selectedDataset.Name.ToUpperInvariant());
        }

        private void BuildSlabStage()
        {
            CreateText(panelContent,
                draftOperation == DraftOperation.None ? "CONFIGURE ANALYSIS" : draftOperation.ToString().ToUpperInvariant() + " DRAFT",
                27, FontStyle.Bold, new Vector2(0, 266), new Vector2(1030, 38),
                TextAnchor.MiddleLeft, Ink);
            CreateText(panelContent,
                draftOperation == DraftOperation.None
                    ? "Assign one role to every dimension. Fixed/Faceted select buckets; Mapped stays continuous."
                    : DraftInstruction(),
                16, FontStyle.Normal, new Vector2(0, 234), new Vector2(1030, 28),
                TextAnchor.MiddleLeft, draftOperation == DraftOperation.None ? Muted : Amber);

            string[] names = { "TIME", "DEPTH", "HORIZONTAL", "VARIABLE" };
            string[] summaries =
            {
                roles[0] == DimensionRole.Faceted
                    ? TimeRangeSummary()
                    : selectedDataset.GetTimeLabel(selectedTime),
                roles[1] == DimensionRole.Faceted
                    ? DepthRangeSummary()
                    : "z " + selectedZ,
                roles[2] == DimensionRole.Mapped ? "continuous XY map" : "full basin",
                roles[3] == DimensionRole.Fixed ? selectedDataset.Name : DatasetNamesSummary()
            };
            Color[] colors = { TimeColor, DepthColor, HorizontalColor, VariableColor };
            for (int i = 0; i < names.Length; i++)
                CreateDimensionRow(i, names[i], summaries[i], colors[i], 174 - i * 55);

            if (draftOperation == DraftOperation.Pivot)
            {
                BuildPivotAxisControls();
            }
            else if (draftOperation == DraftOperation.Drill ||
                draftOperation == DraftOperation.RollUp)
            {
                BuildDraftTickBlockControls();
            }
            else
            {
                if (roles[0] == DimensionRole.Fixed)
                    CreateFixedValueGroup(panelContent, -260, -94, "FIXED TIME",
                        selectedDataset.GetTimeLabel(selectedTime), TimeColor,
                        () => NudgeFixedTime(-1), () => NudgeFixedTime(1));
                else
                    CreateBucketSummaryGroup(panelContent, -260, -94, "TIME RANGES",
                        TimeBucketButtonLabels(), TimeColor,
                        () => OpenBoundaryFromSlab(BoundaryDimension.Time));
                if (roles[1] == DimensionRole.Fixed)
                    CreateFixedValueGroup(panelContent, 260, -94, "FIXED DEPTH",
                        "z=" + selectedZ, DepthColor,
                        () => NudgeFixedDepth(-1), () => NudgeFixedDepth(1));
                else
                    CreateBucketSummaryGroup(panelContent, 260, -94, "DEPTH RANGES",
                        DepthBucketButtonLabels(), DepthColor,
                        () => OpenBoundaryFromSlab(BoundaryDimension.Depth));
            }

            CreateButton(panelContent, "MATPLOT INTENT", new Vector2(-185, -206),
                new Vector2(330, 52),
                AreSpatialAxisBindingsComplete(out _) && authorBoundaryConfirmed
                    ? Purple : Card,
                OpenIntentEditor);
            CreateButton(panelContent, "FULL MATRIX", new Vector2(185, -206),
                new Vector2(330, 50),
                intentConfigured ? Amber : Card, BeginGridPlacement);

            CreateText(panelContent, "1  DESCRIBE", 11, FontStyle.Bold,
                new Vector2(-185, -169), new Vector2(330, 20), TextAnchor.MiddleCenter, Purple);
            CreateText(panelContent, "2  MATERIALIZE", 11, FontStyle.Bold,
                new Vector2(185, -169), new Vector2(330, 20), TextAnchor.MiddleCenter, Amber);

            CreateButton(panelContent, draftOperation == DraftOperation.Pivot ? "PIVOT DRAFT" : "PIVOT",
                new Vector2(-315, -276), SlabLabLayout.ButtonSizeStandard,
                draftOperation == DraftOperation.Pivot ? Purple : Card,
                () => BeginDraft(DraftOperation.Pivot));
            CreateButton(panelContent, draftOperation == DraftOperation.Drill ? "DRILL DRAFT" : "DRILL",
                new Vector2(-75, -276), SlabLabLayout.ButtonSizeStandard,
                draftOperation == DraftOperation.Drill ? TimeColor : Card,
                () => BeginDraft(DraftOperation.Drill));
            CreateButton(panelContent, draftOperation == DraftOperation.RollUp ? "ROLL-UP DRAFT" : "ROLL-UP",
                new Vector2(165, -276), SlabLabLayout.ButtonSizeStandard,
                draftOperation == DraftOperation.RollUp ? Green : Card,
                () => BeginDraft(DraftOperation.RollUp));
            if (draftOperation != DraftOperation.None)
                CreateButton(panelContent, "CANCEL DRAFT", new Vector2(410, -276),
                    SlabLabLayout.ButtonSizeStandard, Danger, CancelDraft);
        }

        private void BuildAnalyzeStage()
        {
            CreateText(panelContent, "GROUND TO CONTINUOUS EVIDENCE", 27, FontStyle.Bold,
                new Vector2(0, 206), new Vector2(1030, 38), TextAnchor.MiddleLeft, Ink);
            CreateText(panelContent, GroundSelectionHeadline(), 17, FontStyle.Bold,
                new Vector2(0, 174), new Vector2(1030, 28), TextAnchor.MiddleLeft, Cyan);

            CreateButton(panelContent, "AGGREGATE", new Vector2(-365, 122),
                new Vector2(285, 48), groundMode == GroundMode.Aggregate ? Green : Card,
                () => SetGroundMode(GroundMode.Aggregate));
            CreateButton(panelContent, "PLAYBACK", new Vector2(-58, 122),
                new Vector2(285, 48), groundMode == GroundMode.Playback ? TimeColor : Card,
                () => SetGroundMode(GroundMode.Playback));
            CreateButton(panelContent, "Return to Grid", new Vector2(337, 122),
                new Vector2(310, 48), Card, ReturnToFacetGrid);

            CreatePanelCard(panelContent, new Vector2(-242, -3), new Vector2(540, 190),
                groundMode == GroundMode.Aggregate ? Green : TimeColor);
            if (s4dGridImage != null)
            {
                RawImage selectedPreview = CreateRawImage(panelContent, s4dGridImage,
                    new Vector2(-395, -4), new Vector2(205, 148));
                selectedPreview.uvRect = SelectedGridCellUv();
            }
            CreateText(panelContent,
                groundMode == GroundMode.Aggregate ? "AGGREGATE VOLUME" : "SOURCE FRAME PLAYBACK",
                17, FontStyle.Bold, new Vector2(-105, 57), new Vector2(255, 28),
                TextAnchor.MiddleLeft, groundMode == GroundMode.Aggregate ? Green : TimeColor);
            CreateText(panelContent,
                groundMode == GroundMode.Aggregate
                    ? "The selected MatPlot cell is placed on the representative slab; the full Time x Depth footprint is highlighted in the Cube."
                    : "The orange cursor advances only through source frames inside this bucket.",
                14, FontStyle.Normal, new Vector2(-105, 10), new Vector2(255, 66),
                TextAnchor.MiddleLeft, Ink);
            CreateText(panelContent, GroundFootprintSummary(), 14, FontStyle.Bold,
                new Vector2(-105, -59), new Vector2(255, 40), TextAnchor.MiddleLeft, Muted);

            CreatePanelCard(panelContent, new Vector2(328, -3), new Vector2(480, 190),
                gridStale ? Amber : Cyan);
            CreateText(panelContent, "EVIDENCE CHECK", 17, FontStyle.Bold,
                new Vector2(328, 57), new Vector2(420, 28), TextAnchor.MiddleLeft,
                gridStale ? Amber : Cyan);
            CreateText(panelContent,
                gridStale
                    ? "The source footprint changed. Re-materialize before judging the finding."
                    : GroundEvidenceComparison(),
                15, FontStyle.Normal, new Vector2(328, 0), new Vector2(420, 82),
                TextAnchor.MiddleLeft, Ink);
            if (gridStale)
                CreateButton(panelContent, "Re-materialize Grid", new Vector2(328, -67),
                    new Vector2(360, 38), Amber, RematerializeS4DGrid);

            CreateText(panelContent, "CONCLUSION", 14, FontStyle.Bold,
                new Vector2(0, -123), new Vector2(1010, 22), TextAnchor.MiddleLeft, Muted);
            CreateButton(panelContent, "SUPPORTED", new Vector2(-330, -172),
                SlabLabLayout.BoundaryActionButtonSize, Green, AcceptBoundary);
            CreateButton(panelContent, "LOCAL ONLY", new Vector2(0, -172),
                SlabLabLayout.BoundaryActionButtonSize, Purple, MarkEvidenceLocalized);
            CreateButton(panelContent, "RECHECK BOUNDARY", new Vector2(330, -172),
                SlabLabLayout.BoundaryActionButtonSize, Amber, MarkBoundarySuspect);
            CreateText(panelContent,
                "Your decision is saved with the finding and source footprint.",
                13, FontStyle.Normal, new Vector2(0, -222), new Vector2(1010, 24),
                TextAnchor.MiddleCenter, Muted);
        }

        private void CreatePanelCard(RectTransform parent, Vector2 position, Vector2 size, Color accent)
        {
            GameObject cardObject = new GameObject("Panel card", typeof(RectTransform));
            cardObject.transform.SetParent(parent, false);
            RectTransform card = cardObject.GetComponent<RectTransform>();
            card.sizeDelta = size;
            card.anchoredPosition = position;
            Image background = cardObject.AddComponent<Image>();
            background.sprite = RoundedUiSprite();
            background.type = Image.Type.Sliced;
            background.color = Color.Lerp(Card,
                new Color(accent.r * 0.25f, accent.g * 0.25f, accent.b * 0.25f, 1.0f), 0.18f);
            background.raycastTarget = false;
            Shadow shadow = cardObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.38f);
            shadow.effectDistance = new Vector2(4.0f, -5.0f);
            Outline outline = cardObject.AddComponent<Outline>();
            outline.effectColor = new Color(accent.r, accent.g, accent.b, 0.28f);
            outline.effectDistance = new Vector2(1, -1);

            GameObject railObject = new GameObject("Card accent rail", typeof(RectTransform));
            railObject.transform.SetParent(card, false);
            RectTransform rail = railObject.GetComponent<RectTransform>();
            rail.anchorMin = new Vector2(0, 0);
            rail.anchorMax = new Vector2(0, 1);
            rail.pivot = new Vector2(0, 0.5f);
            rail.anchoredPosition = new Vector2(0, 0);
            rail.sizeDelta = new Vector2(5, -4);
            Image railImage = railObject.AddComponent<Image>();
            railImage.color = new Color(accent.r, accent.g, accent.b, 0.86f);
            railImage.raycastTarget = false;

            GameObject sheenObject = new GameObject("Card top sheen", typeof(RectTransform));
            sheenObject.transform.SetParent(card, false);
            RectTransform sheen = sheenObject.GetComponent<RectTransform>();
            sheen.anchorMin = new Vector2(0, 1);
            sheen.anchorMax = new Vector2(1, 1);
            sheen.pivot = new Vector2(0.5f, 1);
            sheen.anchoredPosition = new Vector2(0, -1);
            sheen.sizeDelta = new Vector2(-8, 2);
            Image sheenImage = sheenObject.AddComponent<Image>();
            sheenImage.color = new Color(accent.r, accent.g, accent.b, 0.24f);
            sheenImage.raycastTarget = false;
        }

        private void CreateDimensionRow(int index, string name, string summary, Color color, float y)
        {
            CreatePanelCard(panelContent, new Vector2(0, y), new Vector2(1010, 48), color);
            CreateText(panelContent, name, 15, FontStyle.Bold,
                new Vector2(-420, y), new Vector2(170, 28), TextAnchor.MiddleLeft, color);
            CreateButton(panelContent,
                index == 2 ? "MAPPED / EDIT" : RoleLabel(roles[index]),
                new Vector2(-210, y), new Vector2(180, 36),
                RoleColor(roles[index], color), () => CycleDimensionRole(index));
            CreateText(panelContent, summary, 13, FontStyle.Bold,
                new Vector2(88, y), new Vector2(390, 28), TextAnchor.MiddleLeft,
                roles[index] == DimensionRole.Mapped ? color : Ink);
            CreateText(panelContent,
                index == 2 ? "EDIT XY REGION" :
                roles[index] == DimensionRole.Fixed ? "LOCKED" :
                roles[index] == DimensionRole.Faceted ? "GRID AXIS" : "PANEL AXIS",
                12, FontStyle.Bold, new Vector2(402, y), new Vector2(130, 24),
                TextAnchor.MiddleRight, Muted);
        }

        private void BuildAllSourcePreviewLayers()
        {
            DestroySourcePreviewLayerCanvases();
            if (sourcePreviewLayerAtlases.Count == 0 || slabPreviewCanvas == null)
                return;

            RectTransform baseContent = slabPreviewContent;
            Texture2D previousAtlas = matrixPreviewAtlas;
            matrixPreviewAtlas = sourcePreviewLayerAtlases[0];
            sourcePreviewRenderVariableIndex = sourcePreviewVariableIndices[0];
            ShowComposerTool(slabPreviewCanvas);
            BuildSlabPreviewPanel();
            if (sourcePreviewLayerAtlases.Count > 1)
                CreateButton(slabPreviewContent, "ACTIVE LAYER",
                    new Vector2(330, 270), new Vector2(190, 38),
                    VariableColor, () => FocusSourcePreviewLayer(0));

            for (int layer = 1; layer < sourcePreviewLayerAtlases.Count; layer++)
            {
                Canvas layerCanvas = CreateFloatingCanvas(
                    "Source Preview Layer " + (layer + 1),
                    SlabPreviewDockPosition, new Vector2(900, 610),
                    0.00066f, Green);
                layerCanvas.sortingOrder = slabPreviewCanvas.sortingOrder - layer;
                layerCanvas.transform.position = slabPreviewCanvas.transform.position +
                    slabPreviewCanvas.transform.forward * (0.055f * layer) +
                    slabPreviewCanvas.transform.right * (0.055f * layer) -
                    slabPreviewCanvas.transform.up * (0.035f * layer);
                layerCanvas.transform.rotation = slabPreviewCanvas.transform.rotation;
                layerCanvas.transform.localScale = slabPreviewCanvas.transform.localScale;
                sourcePreviewLayerCanvases.Add(layerCanvas);

                slabPreviewContent = layerCanvas.GetComponent<RectTransform>();
                matrixPreviewAtlas = sourcePreviewLayerAtlases[layer];
                sourcePreviewRenderVariableIndex =
                    sourcePreviewVariableIndices[layer];
                BuildSlabPreviewPanel();
                int capturedLayer = layer;
                CreateButton(slabPreviewContent, "BRING FORWARD",
                    new Vector2(330, 270), new Vector2(190, 38),
                    VariableColor,
                    () => FocusSourcePreviewLayer(capturedLayer));
            }

            slabPreviewContent = baseContent;
            matrixPreviewAtlas = sourcePreviewLayerAtlases[0];
            sourcePreviewRenderVariableIndex = sourcePreviewVariableIndices[0];
            if (previousAtlas != null &&
                !sourcePreviewLayerAtlases.Contains(previousAtlas))
                Destroy(previousAtlas);
        }

        private S4DDimensionRoleRequest[] BuildDimensionRoleRequest()
        {
            string[] dimensions = { "time", "depth", "horizontal", "variable" };
            S4DDimensionRoleRequest[] assignments =
                new S4DDimensionRoleRequest[dimensions.Length];
            for (int index = 0; index < dimensions.Length; index++)
            {
                assignments[index] = new S4DDimensionRoleRequest
                {
                    dimension = dimensions[index],
                    role = RoleLabel(roles[index]).ToLowerInvariant()
                };
            }
            return assignments;
        }

        private void CreateTextureCard(RectTransform parent, Texture texture, string label, Vector2 position, Vector2 size,
            Color color, Action action)
        {
            GameObject cardObject = new GameObject(label, typeof(RectTransform));
            cardObject.layer = 5;
            cardObject.transform.SetParent(parent, false);
            RectTransform card = cardObject.GetComponent<RectTransform>();
            card.sizeDelta = size;
            card.anchoredPosition = position;
            Image cardBackground = cardObject.AddComponent<Image>();
            cardBackground.sprite = RoundedUiSprite();
            cardBackground.type = Image.Type.Sliced;
            cardBackground.color = Color.Lerp(Card, color, 0.45f);
            CreateRawImage(card, texture, new Vector2(0, 8), new Vector2(size.x - 12, size.y - 30));
            CreateText(card, label, 14, FontStyle.Bold, new Vector2(0, -size.y * 0.4f),
                new Vector2(size.x - 8, 18), TextAnchor.MiddleCenter, Ink);
            BoxCollider collider = cardObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(size.x, size.y, 12);
            cardObject.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked = action;
        }

        private void ApplyAlwaysVisiblePanelMaterials()
        {
            // Canvas sorting is sufficient for normal panels. Applying one
            // generic always-on-top material to both panel backgrounds and
            // dynamic-font Text components broke the font atlas; applying it
            // only to backgrounds made those backgrounds cover the text. Keep
            // ordinary panels on Unity's normal UI material path.
            Canvas[] canvases =
            {
                panelCanvas, mainMenuCanvas, boundaryCanvas, trailCanvas,
                facetGridCanvas, aiFindingsCanvas, slabPreviewCanvas,
                intentCanvas, draftCanvas
            };
            for (int canvasIndex = 0; canvasIndex < canvases.Length; canvasIndex++)
            {
                Canvas canvas = canvases[canvasIndex];
                if (canvas == null)
                    continue;
                canvas.overrideSorting = true;
                canvas.sortingOrder = 30000 + canvasIndex;
            }
        }

        private void EnsureDragCardMaterials()
        {
            if (uiAlwaysVisibleMaterial == null)
            {
                Shader shader = Shader.Find("UI/Default");
                if (shader != null)
                {
                    uiAlwaysVisibleMaterial = new Material(shader);
                    uiAlwaysVisibleMaterial.name =
                        "Variable drag card always visible";
                    ConfigureAlwaysVisibleMaterial(uiAlwaysVisibleMaterial,
                        5000);
                }
            }

            Font targetFont = font != null ? font :
                Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (targetFont == null || targetFont.material == null)
                return;
            targetFont.RequestCharactersInTexture(
                "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-", 32,
                FontStyle.Bold);
            if (uiAlwaysVisibleFontMaterial == null)
            {
                uiAlwaysVisibleFontMaterial = new Material(targetFont.material);
                uiAlwaysVisibleFontMaterial.name =
                    "Variable drag font always visible";
                ConfigureAlwaysVisibleMaterial(uiAlwaysVisibleFontMaterial,
                    5001);
            }
            uiAlwaysVisibleFontMaterial.mainTexture =
                targetFont.material.mainTexture;
        }

        private void BuildSharedColorScale(RectTransform parent)
        {
            if (sharedColorbarTexture == null)
            {
                sharedColorbarTexture = new Texture2D(
                    12, 128, TextureFormat.RGBA32, false);
                sharedColorbarTexture.name = "S4D Shared Viridis Scale";
                for (int y = 0; y < sharedColorbarTexture.height; y++)
                {
                    float t = y / (float)(sharedColorbarTexture.height - 1);
                    Color color = t < 0.5f
                        ? Color.Lerp(
                            new Color(0.267f, 0.005f, 0.329f),
                            new Color(0.128f, 0.567f, 0.551f), t * 2.0f)
                        : Color.Lerp(
                            new Color(0.128f, 0.567f, 0.551f),
                            new Color(0.993f, 0.906f, 0.144f),
                            (t - 0.5f) * 2.0f);
                    for (int x = 0; x < sharedColorbarTexture.width; x++)
                        sharedColorbarTexture.SetPixel(x, y, color);
                }
                sharedColorbarTexture.Apply(false, false);
            }
            // The scale runs along the bottom so the three facet images can use
            // the panel's full width; a vertical bar had to sit inside the grid.
            CreateText(parent, "SHARED SCALE", 10, FontStyle.Bold,
                new Vector2(0, -206), new Vector2(240, 18),
                TextAnchor.MiddleCenter, Muted);
            CreateRawImage(parent, sharedColorbarTexture,
                new Vector2(0, -232), new Vector2(620, 20));
            CreateText(parent, s4dSharedMaximum.ToString("0.###"),
                10, FontStyle.Bold, new Vector2(350, -232),
                new Vector2(110, 20), TextAnchor.MiddleLeft, Ink);
            CreateText(parent, s4dSharedMinimum.ToString("0.###"),
                10, FontStyle.Bold, new Vector2(-350, -232),
                new Vector2(110, 20), TextAnchor.MiddleRight, Ink);
            CreateText(parent, string.IsNullOrWhiteSpace(s4dSharedUnit)
                    ? "value" : s4dSharedUnit,
                10, FontStyle.Normal, new Vector2(0, -256),
                new Vector2(320, 18), TextAnchor.MiddleCenter, Muted);
        }

        private void ShowPrimaryTool(Canvas tool)
        {
            if (tool == null)
                return;
            HidePrimaryToolsExcept(tool);
            tool.gameObject.SetActive(true);
        }

        private void HidePrimaryToolsExcept(Canvas exception)
        {
            if (mainMenuCanvas != null && mainMenuCanvas != exception)
                mainMenuCanvas.gameObject.SetActive(false);
            if (boundaryCanvas != null && boundaryCanvas != exception)
            {
                boundaryCanvas.gameObject.SetActive(false);
                SetTimeBoundaryHandleVisibility(false);
                SetDepthBoundaryVisibility(false);
            }
            if (trailCanvas != null && trailCanvas != exception)
                trailCanvas.gameObject.SetActive(false);
            if (panelCanvas != null && panelCanvas != exception)
            {
                SetGroundDock(false);
                panelCanvas.gameObject.SetActive(false);
            }
            // One focused task surface at a time. The Field and persistent
            // toolbar remain visible context; floating editors never stack on
            // top of one another.
            if (slabPreviewCanvas != null && slabPreviewCanvas != exception)
                slabPreviewCanvas.gameObject.SetActive(false);
            if (intentCanvas != null && intentCanvas != exception)
                intentCanvas.gameObject.SetActive(false);
            if (draftCanvas != null && draftCanvas != exception)
                draftCanvas.gameObject.SetActive(false);
            if (facetGridCanvas != null && facetGridCanvas != exception)
            {
                facetGridCanvas.gameObject.SetActive(false);
                SetFacetSelectionEvidencePreview(false);
            }
            if (aiFindingsCanvas != null && aiFindingsCanvas != exception)
                aiFindingsCanvas.gameObject.SetActive(false);
            if (exception != slabPreviewCanvas)
            {
                for (int index = 0; index < sourcePreviewLayerCanvases.Count; index++)
                    if (sourcePreviewLayerCanvases[index] != null)
                        sourcePreviewLayerCanvases[index].gameObject.SetActive(false);
            }
            if (exception != facetGridCanvas)
            {
                for (int index = 0; index < materializedLayerCanvases.Count; index++)
                    if (materializedLayerCanvases[index] != null)
                        materializedLayerCanvases[index].gameObject.SetActive(false);
            }
        }
    }
}
