using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    /// <summary>Split out of VolumeSTCubeQuestSpatialWorkbench: Dataset side of the workbench.</summary>
    public sealed partial class VolumeSTCubeQuestSpatialWorkbench
    {
        private readonly List<int> boundaryVariableQueue = new List<int>();

        private void UpdateQuestImportHeadLock(bool immediate = false)
        {
            if (!questImportHeadLocked || panelCanvas == null || xrCamera == null ||
                stage != Stage.DatasetImport || grabbedPanel != null)
                return;
            Vector3 gaze = xrCamera.transform.forward.normalized;
            Vector3 targetPosition = xrCamera.transform.position + gaze * 1.30f;
            Quaternion targetRotation = Quaternion.LookRotation(gaze,
                xrCamera.transform.up);
            float blend = immediate ? 1.0f : 1.0f - Mathf.Exp(-10.0f * Time.deltaTime);
            panelCanvas.transform.position = Vector3.Lerp(
                panelCanvas.transform.position, targetPosition, blend);
            panelCanvas.transform.rotation = Quaternion.Slerp(
                panelCanvas.transform.rotation, targetRotation, blend);
            panelCanvas.transform.localScale = Vector3.one * 0.00152f;
        }

        private void CreateFieldDatasetSelector()
        {
            fieldDatasetSelectorRoot = new GameObject("Field display dataset selector");
            fieldDatasetSelectorRoot.transform.SetParent(spatialRoot.transform, false);
            // Mount the selector on the upper front face of the Field.  It stays
            // outside the volume renderer, so its colliders remain easy to hit.
            fieldDatasetSelectorRoot.transform.localPosition = new Vector3(
                0.0f, FieldHalfHeight - 0.085f, -FieldHalfDepth - 0.052f);
            // Text meshes are authored toward local +Z. The viewer looks at the
            // Field from its -Z side, so turn this front-mounted strip around.
            fieldDatasetSelectorRoot.transform.localRotation =
                Quaternion.Euler(0.0f, 180.0f, 0.0f);
            RefreshFieldDatasetSelector();
        }

        private void RefreshFieldDatasetSelector()
        {
            if (fieldDatasetSelectorRoot == null)
                return;

            Transform root = fieldDatasetSelectorRoot.transform;
            for (int index = root.childCount - 1; index >= 0; index--)
                Destroy(root.GetChild(index).gameObject);

            fieldDatasetSelectorRoot.SetActive(datasets.Count > 0);
            if (datasets.Count == 0)
                return;

            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Display data selector backing";
            plate.transform.SetParent(root, false);
            plate.transform.localPosition = new Vector3(0.0f, 0.0f, -0.018f);
            plate.transform.localScale = new Vector3(1.68f, 0.145f, 0.026f);
            Destroy(plate.GetComponent<Collider>());
            plate.GetComponent<Renderer>().material = CreateStableOpaqueMaterial(
                new Color(0.01f, 0.08f, 0.10f, 0.96f));

            CreatePalettePhysicalText(root, "DISPLAY DATA",
                new Vector3(0.0f, 0.044f, 0.002f), 0.0090f, Cyan,
                0.38f, 0.035f);

            int count = datasets.Count;
            float gap = 0.018f;
            float availableWidth = 1.56f;
            float buttonWidth = Mathf.Min(0.37f,
                (availableWidth - gap * Mathf.Max(0, count - 1)) / count);
            float totalWidth = buttonWidth * count + gap * Mathf.Max(0, count - 1);
            float startX = -totalWidth * 0.5f + buttonWidth * 0.5f;
            for (int index = 0; index < count; index++)
            {
                int capturedIndex = index;
                bool selected = datasets[index] == selectedDataset;
                GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cube);
                button.name = "Display " + datasets[index].Name;
                button.layer = 5;
                button.transform.SetParent(root, false);
                button.transform.localPosition = new Vector3(
                    -(startX + index * (buttonWidth + gap)), -0.025f, 0.006f);
                button.transform.localScale = new Vector3(buttonWidth, 0.060f, 0.036f);
                button.GetComponent<Renderer>().material = CreateStableOpaqueMaterial(
                    selected
                        ? new Color(0.02f, 0.78f, 0.92f, 1.0f)
                        : new Color(0.055f, 0.10f, 0.15f, 1.0f));
                button.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                    () => SelectFieldDisplayDataset(capturedIndex);
                // Keep the caption outside the scaled cube transform. Parenting
                // it to the button would shrink the glyph mesh a second time.
                CreatePalettePhysicalText(root,
                    GetFieldDatasetShortLabel(datasets[index]),
                    new Vector3(button.transform.localPosition.x, -0.025f,
                        0.027f), 0.0092f,
                    selected ? Ink : Color.white,
                    Mathf.Max(0.12f, buttonWidth - 0.035f), 0.039f);
            }
        }

        private static string GetFieldDatasetShortLabel(
            VolumeSTCubeSliceDataset dataset)
        {
            string name = dataset != null ? dataset.Name.ToUpperInvariant() : "DATA";
            bool prediction = name.Contains("PREDICTION") || name.Contains("PRED");
            bool water = name.Contains("WATER") || name.Contains("LEVEL");
            if (prediction)
                return water ? "PRED WATER" : "PRED HS";
            if (name.Contains("GROUNDTRUTH") || name.Contains("TRUTH") || name.Contains("GROUND"))
                return water ? "TRUE WATER" : "TRUE HS";
            return name.Length <= 12 ? name : name.Substring(0, 12);
        }

        private void SelectFieldDisplayDataset(int index)
        {
            if (index < 0 || index >= datasets.Count)
                return;
            if (datasets[index] == selectedDataset)
            {
                SetStatus("Field is already showing " + datasets[index].Name + ".");
                RefreshFieldDatasetSelector();
                return;
            }
            LoadDataset(index);
        }

        private void CreateVariableBindingShell(int variableIndex,
            SpatialAxisRigState state)
        {
            List<int> boundVariables = BoundVariableIndices();
            bool bound = boundVariables.Count > 0;
            bool selected = selectedDataset != null && boundVariables.Contains(
                datasets.IndexOf(selectedDataset));
            Color color = selected ? VariableColor : bound
                ? new Color(VariableColor.r, VariableColor.g,
                    VariableColor.b, 0.72f)
                : new Color(0.20f, 0.68f, 0.82f, 0.58f);
            // Leave breathing room around the upper range selectors.
            float half = 0.54f;
            Vector3[] corners =
            {
                new Vector3(-half,-half,-half), new Vector3(half,-half,-half),
                new Vector3(half,-half,half), new Vector3(-half,-half,half),
                new Vector3(-half,half,-half), new Vector3(half,half,-half),
                new Vector3(half,half,half), new Vector3(-half,half,half)
            };
            int[,] edges =
            {
                {0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},
                {0,4},{1,5},{2,6},{3,7}
            };
            for (int edge = 0; edge < edges.GetLength(0); edge++)
            {
                float meanDepth = (corners[edges[edge, 0]].z +
                    corners[edges[edge, 1]].z) * 0.5f;
                float edgeAlpha = meanDepth >= 0.0f ? 0.78f : 0.38f;
                Color frameColor = new Color(color.r, color.g, color.b, edgeAlpha);
                LineRenderer frameLine = CreateWorldLine(
                    "Variable shell", state.root.transform,
                    corners[edges[edge, 0]], corners[edges[edge, 1]],
                    frameColor,
                    meanDepth >= 0.0f ? 0.0082f : 0.0055f);
                state.frameRenderers.Add(frameLine);
                state.frameColors.Add(frameColor);
                frameLine.enabled = false;
            }

            CreateMagneticAxisDock(state.root.transform, color, bound);

            GameObject shellTarget = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shellTarget.name = "Axis composer header";
            shellTarget.layer = 5;
            shellTarget.transform.SetParent(state.root.transform, false);
            shellTarget.transform.localPosition = new Vector3(0.0f, 0.52f, 0.0f);
            shellTarget.transform.localRotation =
                Quaternion.Inverse(state.root.transform.localRotation);
            shellTarget.transform.localScale = new Vector3(0.34f, 0.085f, 0.050f);
            Material shellMaterial = new Material(Shader.Find("Sprites/Default"));
            // Keep the drop header opaque.  A translucent cube directly in
            // front of the wire frame caused Quest's mobile renderer to
            // alternate its draw order with the frame and appear to flash.
            shellMaterial.color = bound
                ? new Color(color.r * 0.34f, color.g * 0.34f,
                    color.b * 0.34f, 1.0f)
                : new Color(0.025f, 0.18f, 0.24f, 1.0f);
            shellTarget.GetComponent<Renderer>().material = shellMaterial;
            TextMesh variableLabel = CreateWorldLabel(
                state.variableAxis >= 0 ? "VARIABLE" : "AXIS",
                Vector3.back * 0.038f, 0.0060f,
                TextAnchor.MiddleCenter, Ink, shellTarget.transform);
            variableLabel.transform.localScale =
                new Vector3(2.5f, 8.3f, 15.4f);
        }

        private List<int> BoundVariableIndices()
        {
            List<int> result = new List<int>();
            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                int variable = spatialAxisRigStates[index].boundVariable;
                if (variable >= 0 && variable < datasets.Count &&
                    !result.Contains(variable))
                    result.Add(variable);
            }
            return result;
        }

        private string BoundVariableLabel(List<int> variables)
        {
            if (variables == null || variables.Count == 0)
                return "DROP VARIABLE HERE";
            StringBuilder label = new StringBuilder();
            for (int index = 0; index < variables.Count; index++)
            {
                if (index > 0)
                    label.Append("  +  ");
                label.Append(datasets[variables[index]].Name.ToUpperInvariant());
            }
            return label.ToString();
        }

        private void CreateVariableSelectionPanel(SpatialAxisRigState state)
        {
            int itemCount = datasets.Count;
            if (itemCount <= 0)
                return;
            GameObject panel = new GameObject("Variable selection flyout");
            // Dock the selector to the controller rather than the non-uniformly
            // scaled VARIABLE pill.  It therefore stays square, readable, and
            // consistently available at the lower-right of the tri-axis even
            // when VARIABLE is bound to the downward Z axis.
            panel.transform.SetParent(state.root.transform, false);
            panel.transform.localPosition = new Vector3(
                0.53f, -0.21f, -0.045f);
            panel.transform.localRotation = Quaternion.identity;
            panel.transform.localScale = Vector3.one * 0.08f;
            StartCoroutine(AnimateLocalScale(panel.transform, Vector3.one));
            GameObject backing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backing.name = "Variable selector backing";
            backing.transform.SetParent(panel.transform, false);
            backing.transform.localPosition = new Vector3(0.0f,
                -(itemCount - 1) * 0.055f, 0.018f);
            backing.transform.localScale = new Vector3(0.305f,
                0.105f + itemCount * 0.105f, 0.024f);
            backing.GetComponent<Renderer>().material =
                CreateStableOpaqueMaterial(new Color(
                    0.025f, 0.030f, 0.050f, 1.0f));
            Destroy(backing.GetComponent<Collider>());
            CreateWorldLabel("VARIABLES", new Vector3(0.0f, 0.082f,
                    -0.004f), 0.0038f, TextAnchor.MiddleCenter,
                VariableColor, panel.transform);

            for (int index = 0; index < itemCount; index++)
                CreateVariableChoiceButton(panel.transform, index,
                    new Vector3(0.0f, 0.018f - index * 0.105f, -0.005f));
        }

        private void CreateVariableChoiceButton(Transform parent,
            int variableIndex, Vector3 position)
        {
            bool selected = spatialAxisRigStates.Exists(state =>
                state.boundVariable == variableIndex);
            GameObject button = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            button.name = datasets[variableIndex].Name +
                " variable choice";
            button.layer = 5;
            button.transform.SetParent(parent, false);
            button.transform.localPosition = position;
            button.transform.localScale = new Vector3(0.270f, 0.076f, 0.046f);
            button.GetComponent<Renderer>().material =
                CreateStableOpaqueMaterial(selected
                    ? new Color(0.70f, 0.20f, 0.88f, 1.0f)
                    : new Color(0.095f, 0.115f, 0.145f, 1.0f));
            int capturedVariable = variableIndex;
            button.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                () => ToggleAxisVariableSelection(capturedVariable);
            variablePaletteTokens[variableIndex] = button;
            TextMesh label = CreateWorldLabel(
                (selected ? "✓  " : string.Empty) +
                    datasets[variableIndex].Name.ToUpperInvariant(),
                position + new Vector3(0.0f, 0.0f, -0.030f),
                0.0037f, TextAnchor.MiddleCenter,
                selected ? Ink : Muted, parent);
            // Assign a plain ASCII state marker after construction. This also
            // avoids platform-dependent glyph fallback on Quest.
            label.text = (selected ? "[X] " : "[ ] ") +
                GetFieldDatasetShortLabel(datasets[variableIndex]);
            label.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
            variablePaletteLabels[variableIndex] = label;
        }

        private GameObject CreateVariableRoleButton(Transform parent, string label,
            DimensionRole role, Vector2 position)
        {
            GameObject root = new GameObject(label + " variable role root");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(
                position.x, position.y, 0.010f);
            GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cube);
            button.name = label + " variable role button";
            button.layer = 5;
            button.transform.SetParent(root.transform, false);
            button.transform.localPosition = Vector3.zero;
            // Match the proven TIME/DEPTH physical-button proportions. Keeping
            // real depth here prevents the caption from falling into the card's
            // own depth buffer when the palette or held card rotates.
            button.transform.localScale = new Vector3(0.090f, 0.030f, 0.024f);
            button.GetComponent<Renderer>().material = CreateStableOpaqueMaterial(
                roles[3] == role ? VariableColor :
                    new Color(0.10f, 0.16f, 0.21f, 1.0f));
            button.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                () => SetSpatialVariableRole(role);
            CreatePalettePhysicalText(root.transform, label,
                // Keep the caption just above the physical face. The previous
                // 5 cm stand-off produced noticeable rightward parallax when
                // the head-follow panel was viewed at an angle.
                new Vector3(-0.012f, 0.0f, 0.020f), 0.0030f, Ink);
            return button;
        }

        private void UpdateVariableRoleButtons()
        {
            UpdateVariableRoleButton(variableFixedRoleButton,
                DimensionRole.Fixed);
            UpdateVariableRoleButton(variableFacetedRoleButton,
                DimensionRole.Faceted);
        }

        private void UpdateVariableRoleButton(GameObject button,
            DimensionRole role)
        {
            if (button == null)
                return;
            Renderer renderer = button.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = roles[3] == role
                    ? VariableColor : new Color(0.10f, 0.16f, 0.21f, 1.0f);
        }

        private void HighlightVariableFrame(int rigIndex, bool active)
        {
            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                SpatialAxisRigState state = spatialAxisRigStates[index];
                state.frameRequestedVisible = active && index == rigIndex;
            }
        }

        private void SetSpatialVariableRole(DimensionRole role)
        {
            if (role != DimensionRole.Fixed && role != DimensionRole.Faceted)
                return;
            if (roles[3] == role)
                return;
            roles[3] = role;
            if (role == DimensionRole.Faceted &&
                spatialAxisRigStates.Count > 0)
            {
                // Faceted variables are categorical Z positions. Reserve Z
                // for those variable nodes and normalize Time/Depth to X/Y.
                SpatialAxisRigState shared = spatialAxisRigStates[0];
                shared.timeAxis = 0;
                shared.depthAxis = 1;
                for (int index = 1; index < spatialAxisRigStates.Count; index++)
                {
                    spatialAxisRigStates[index].timeAxis = 0;
                    spatialAxisRigStates[index].depthAxis = 1;
                }
            }
            if (role == DimensionRole.Fixed && spatialAxisRigStates.Count > 0)
            {
                int selectedIndex = selectedDataset != null
                    ? datasets.IndexOf(selectedDataset) : -1;
                int sourceIndex = spatialAxisRigStates.FindIndex(state =>
                    state.boundVariable == selectedIndex);
                if (sourceIndex < 0)
                    sourceIndex = spatialAxisRigStates.FindIndex(state =>
                        state.boundVariable >= 0);
                SpatialAxisRigState target = spatialAxisRigStates[0];
                if (sourceIndex > 0)
                {
                    SpatialAxisRigState source = spatialAxisRigStates[sourceIndex];
                    target.boundVariable = source.boundVariable;
                    target.timeAxis = source.timeAxis;
                    target.depthAxis = source.depthAxis;
                    target.timeRole = source.timeRole;
                    target.depthRole = source.depthRole;
                }
                for (int index = 1; index < spatialAxisRigStates.Count; index++)
                    spatialAxisRigStates[index].boundVariable = -1;
            }
            RefreshVariableFacetStacks();
            RefreshSpatialAxisControllers();
            UpdateVariablePaletteTokenVisibility();
            UpdateAnalysisAxisLabels();
            InvalidateSlabConfiguration("Variable role changed");
            SetStatus(role == DimensionRole.Fixed
                ? "Variable FIXED: one variable controls one STC field."
                : "Variable FACETED: bind multiple variables to the shared axis controller; each receives an upright Field.");
        }

        private void UpdateAutomaticVariableRole()
        {
            List<int> bound = BoundVariableIndices();
            roles[3] = bound.Count > 1
                ? DimensionRole.Faceted : DimensionRole.Fixed;
            if (spatialAxisRigStates.Count == 0)
                return;
            SpatialAxisRigState shared = spatialAxisRigStates[0];
            for (int index = 1; index < spatialAxisRigStates.Count; index++)
            {
                spatialAxisRigStates[index].timeAxis = shared.timeAxis;
                spatialAxisRigStates[index].depthAxis = shared.depthAxis;
                spatialAxisRigStates[index].variableAxis = shared.variableAxis;
                spatialAxisRigStates[index].timeRole = shared.timeRole;
                spatialAxisRigStates[index].depthRole = shared.depthRole;
            }
        }

        private void RefreshDatasets()
        {
            RefreshDatasets(null);
        }

        private void RefreshDatasets(string requestedRoot)
        {
            string previousRoot = dataRoot;
            dataRoot = string.IsNullOrWhiteSpace(requestedRoot)
                ? ResolveDataRoot()
                : Path.GetFullPath(requestedRoot);
            if (!string.IsNullOrWhiteSpace(previousRoot) &&
                !string.Equals(previousRoot, dataRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                spatialAxisRigStates.Clear();
                selectedDataset = null;
                importSelectedVariableIndex = -1;
            }
            datasets.Clear();
            try
            {
                datasets.AddRange(VolumeSTCubeRawSliceReader.DiscoverDatasets(dataRoot));
                if (datasets.Count == 0 &&
                    VolumeSTCubeRawSliceReader.TryOpenDataset(
                        dataRoot, out VolumeSTCubeSliceDataset directDataset, out _))
                {
                    datasets.Add(directDataset);
                }
                SetStatus(datasets.Count > 0
                    ? "Detected " + datasets.Count + " variable" +
                      (datasets.Count == 1 ? "" : "s") + " in " + dataRoot + "."
                    : "No RAW datasets found at " + dataRoot);
            }
            catch (Exception exception)
            {
                SetStatus("Dataset discovery failed: " + exception.Message);
            }
            EnsureDefaultImportVariable();
            RefreshFieldDatasetSelector();
            RefreshSpatialAxisControllers();
            if (startupReadySeconds < 0.0f && datasets.Count > 0 &&
                startupStartedAt > 0.0f)
                startupReadySeconds = Time.realtimeSinceStartup - startupStartedAt;
            BuildStage();
        }

        private string ResolveDataRoot()
        {
#if UNITY_EDITOR || SLABLAB_FLAT
            string editorForVrRoot = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "For_VR", "UnityRaw"));
            if (LooksLikeDatasetLocation(editorForVrRoot))
                return editorForVrRoot;
#endif
            string savedRoot = SlabLabSettings.DatasetRoot;
            // A previously selected folder can remain in PlayerPrefs after its files
            // have been moved or after the APK has been reinstalled.  Merely checking
            // Directory.Exists then traps the import screen on an empty, stale folder.
            if (LooksLikeDatasetLocation(savedRoot))
                return Path.GetFullPath(savedRoot);
            if (Application.platform == RuntimePlatform.Android)
            {
                string folderName = VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled
                    ? "Datasets"
                    : "OneDrive_1_4-30-2026";
                string privateRoot = Path.Combine("/data/user/0", Application.identifier, "files", folderName);
                string persistentRoot = Path.Combine(Application.persistentDataPath, folderName);
                string externalRoot = Path.Combine("/sdcard/Android/data", Application.identifier,
                    "files", folderName);
                string[] candidates = { persistentRoot, externalRoot, privateRoot };
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (LooksLikeDatasetLocation(candidates[i]))
                        return Path.GetFullPath(candidates[i]);
                }

                // Keep the normal Unity persistent location as the actionable path in
                // the empty-state message, even when no candidate is populated yet.
                return persistentRoot;
            }
            if (Application.platform == RuntimePlatform.IPhonePlayer)
                return Path.Combine(Application.persistentDataPath, "Datasets");
            return Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "OneDrive_1_4-30-2026"));
        }

        private static bool LooksLikeDatasetLocation(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return false;
            try
            {
                // Accept both a root containing variable directories and a directly
                // selected variable directory. Checking only filenames avoids parsing
                // every RAW metadata file during startup on Quest.
                if (Directory.GetFiles(root, "*.raw", SearchOption.TopDirectoryOnly).Length > 0)
                    return true;
                string[] directories = Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < directories.Length; i++)
                {
                    if (Directory.GetFiles(directories[i], "*.raw", SearchOption.TopDirectoryOnly).Length > 0)
                        return true;
                }
            }
            catch (Exception)
            {
                // Android can expose a directory entry before the app can enumerate it;
                // continue probing the remaining candidate roots in that case.
            }
            return false;
        }

        private void AnimatePanelRefresh(CanvasGroup group, ref Coroutine running)
        {
            if (group == null || !group.gameObject.activeInHierarchy)
                return;
            if (running != null)
                StopCoroutine(running);
            // Keep the surface visually stable while a local value changes.
            // The old whole-panel fade looked like flicker in a headset.
            group.alpha = 1.0f;
            running = null;
        }

        private IEnumerator FadePanelRefresh(CanvasGroup group)
        {
            group.alpha = 0.72f;
            float elapsed = 0.0f;
            const float duration = 0.16f;
            while (group != null && elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                group.alpha = Mathf.SmoothStep(0.72f, 1.0f, t);
                yield return null;
            }
            if (group != null)
                group.alpha = 1.0f;
        }

        private void RefreshStepColors()
        {
            bool importOnly = stage == Stage.DatasetImport;
            bool compactField = stage == Stage.Field && preconfigurationActive;
            bool hideWorkflowChrome = importOnly || compactField;
            if (panelCanvas != null)
            {
                RectTransform panelRect = panelCanvas.GetComponent<RectTransform>();
                if (panelRect != null)
                    panelRect.sizeDelta = compactField
                        ? new Vector2(980.0f, 620.0f)
                        : SlabLabLayout.PanelCanvasSize;
                BoxCollider panelCollider = panelCanvas.GetComponent<BoxCollider>();
                if (panelCollider != null)
                    panelCollider.size = compactField
                        ? new Vector3(980.0f, 620.0f, 8.0f)
                        : new Vector3(1120.0f, 840.0f, 8.0f);
                if (panelContent != null)
                {
                    panelContent.sizeDelta = compactField
                        ? new Vector2(930.0f, 540.0f)
                        : new Vector2(1070.0f, 595.0f);
                    panelContent.anchoredPosition = compactField
                        ? new Vector2(0.0f, -2.0f)
                        : new Vector2(0.0f, -34.0f);
                }
                Transform[] chrome = panelCanvas.GetComponentsInChildren<Transform>(true);
                for (int index = 0; index < chrome.Length; index++)
                {
                    string objectName = chrome[index].gameObject.name;
                    if (objectName == "Navigation dock" ||
                        objectName.StartsWith("Spatial step ",
                            StringComparison.Ordinal))
                        chrome[index].gameObject.SetActive(!hideWorkflowChrome);
                    else if (objectName == "Console header wash")
                        chrome[index].gameObject.SetActive(!compactField);
                    else if (objectName == "Stage surface")
                        chrome[index].gameObject.SetActive(!compactField);
                }
            }
            if (panelFlowText != null)
                panelFlowText.gameObject.SetActive(!hideWorkflowChrome);
            if (statusText != null)
            {
                statusText.gameObject.SetActive(!importOnly);
                statusText.rectTransform.anchoredPosition = compactField
                    ? new Vector2(0.0f, -276.0f)
                    : new Vector2(0.0f, -396.0f);
                statusText.rectTransform.sizeDelta = compactField
                    ? new Vector2(910.0f, 48.0f)
                    : new Vector2(1060.0f, 32.0f);
                statusText.alignment = compactField
                    ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
                statusText.fontSize = Mathf.RoundToInt(
                    (compactField ? 14 : 17) * ActiveUiFontScale);
                if (statusCrispText != null)
                {
                    statusCrispText.fontSizeMin = compactField ? 20.0f : 16.0f;
                    statusCrispText.fontSizeMax = compactField ? 32.0f : 26.0f;
                }
            }
            if (panelBrandText != null)
                panelBrandText.gameObject.SetActive(!hideWorkflowChrome);
            if (panelGripHintText != null)
                panelGripHintText.gameObject.SetActive(!hideWorkflowChrome);

            for (int i = 0; i < 5; i++)
            {
                GameObject item = GameObject.Find("Spatial step " + i);
                if (item != null)
                {
                    bool active = datasetImportConfirmed && i == (int)stage - 1;
                    Color accent = active ? Cyan : Card;
                    Image image = item.GetComponent<Image>();
                    if (image != null)
                        image.color = ThemedButtonFill(accent);
                    Transform accentTransform = item.transform.Find("Button accent");
                    if (accentTransform != null)
                    {
                        Image accentImage = accentTransform.GetComponent<Image>();
                        if (accentImage != null)
                            accentImage.color = active
                                ? new Color(Cyan.r, Cyan.g, Cyan.b, 0.96f)
                                : new Color(Muted.r, Muted.g, Muted.b, 0.30f);
                    }
                    Text label = item.GetComponentInChildren<Text>();
                    if (label != null)
                        label.color = IdealButtonLabel(ThemedButtonFill(accent));
                }
            }
            if (panelTitleText != null)
            {
                panelTitleText.gameObject.SetActive(!compactField);
                panelTitleText.text = stage == Stage.DatasetImport
                    ? "LIVE WAVE DATA"
                    : stage == Stage.Field
                        ? "FIELD"
                        : "SLAB FRAME";
                panelTitleText.alignment = importOnly
                    ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
                panelTitleText.rectTransform.anchoredPosition = importOnly
                    ? new Vector2(0, 255) : new Vector2(0, 367);
                panelTitleText.rectTransform.sizeDelta = importOnly
                    ? new Vector2(900, 58) : new Vector2(1060, 42);
                panelTitleText.fontSize = Mathf.RoundToInt(
                    (importOnly ? 38 : 31) * ActiveUiFontScale);
                if (panelTitleCrispText != null)
                {
                    panelTitleCrispText.text = panelTitleText.text;
                    panelTitleCrispText.alignment = importOnly
                        ? TMPro.TextAlignmentOptions.Center
                        : TMPro.TextAlignmentOptions.Left;
                    panelTitleCrispText.fontSizeMin = importOnly ? 38.0f : 30.0f;
                    panelTitleCrispText.fontSizeMax = importOnly ? 64.0f : 54.0f;
                }
            }
            if (panelFlowText != null)
                panelFlowText.text = stage == Stage.DatasetImport
                    ? "Connect -> Stream visible data -> Continuous Field"
                    : "Author Boundary -> Configure Slab -> MatPlot Grid -> Ground -> Re-materialize";
        }

        private void BuildDatasetImportStage()
        {
            bool connecting = waveClient != null && waveClient.IsImporting;
            bool ready = datasets.Count > 0;
            CreateText(panelContent, "WAVE SERVER", 20, FontStyle.Bold,
                new Vector2(0, 180), new Vector2(780, 34),
                TextAnchor.MiddleCenter, Ink);
            CreateText(panelContent,
                connecting
                    ? "CONNECTING TO LIVE DATA..."
                    : ready
                        ? "LIVE DATA CONNECTED  ·  24 HOURS"
                        : string.IsNullOrWhiteSpace(waveImportError)
                            ? "PREPARING LIVE DATA..."
                            : "CONNECTION FAILED",
                21, FontStyle.Bold, new Vector2(0, 116),
                new Vector2(780, 48), TextAnchor.MiddleCenter,
                ready ? Green : connecting ? Cyan : Amber);

            waveImportProgressFill = null;
            if (connecting)
            {
                CreateDecorativeSurface(panelContent, "Wave progress track",
                    new Vector2(0, 80), new Vector2(860, 10),
                    new Color(0.06f, 0.12f, 0.15f, 1.0f));
                waveImportProgressFill = CreateDecorativeSurface(panelContent,
                    "Wave progress fill", new Vector2(0, 80),
                    new Vector2(860, 10), Cyan);
                waveImportProgressFill.type = Image.Type.Filled;
                waveImportProgressFill.fillMethod = Image.FillMethod.Horizontal;
                waveImportProgressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
                waveImportProgressFill.fillAmount = waveClient.Progress;
            }
            else if (!ready)
            {
                CreateButton(panelContent, "RETRY CONNECTION",
                    new Vector2(0, 76), new Vector2(330, 58), Cyan,
                    ImportWaveServerDataset);
            }

            CreatePanelCard(panelContent, new Vector2(0, -10),
                new Vector2(860, 170), ready ? Cyan : Card);
            CreateText(panelContent,
                ready
                    ? "VARIABLES FOUND (" + datasets.Count + ")\n" +
                        DatasetNamesMultiline()
                    : connecting
                        ? "REQUESTING DATASET METADATA"
                        : string.IsNullOrWhiteSpace(waveImportError)
                            ? "WAITING FOR WAVE SERVER"
                            : waveImportError,
                13, FontStyle.Bold, new Vector2(0, -10),
                new Vector2(810, 154), TextAnchor.MiddleCenter,
                ready ? Ink : string.IsNullOrWhiteSpace(waveImportError)
                    ? Muted : Amber);
            CreateButton(panelContent,
                ready ? "CONTINUE" : connecting
                    ? "CONNECTING..." : "LIVE DATA UNAVAILABLE",
                new Vector2(0, -164), new Vector2(700, 72),
                ready ? Cyan : Card,
                ConfirmDatasetImport);

            // Which source answered is worth stating on the opening page: a
            // packaged app falls back to the bundled hydrated snapshot, and the
            // operator should know the values came from there and not from the
            // live gateway. Purely additive — the band between CONTINUE and
            // MODE was empty, so no existing control moves.
            if (ready && waveClient != null)
            {
                bool bundled = string.Equals(waveClient.Source, "cache",
                    StringComparison.OrdinalIgnoreCase);
                CreateText(panelContent,
                    bundled
                        ? "DATA SOURCE · LOCAL CACHE (NO NETWORK)"
                        : "DATA SOURCE · LIVE",
                    15, FontStyle.Bold, new Vector2(0, -221),
                    new Vector2(700, 34), TextAnchor.MiddleCenter,
                    bundled ? Amber : Green);
            }

            // Mode selection belongs on the opening page and remains separate
            // from dataset choice. Reloading the scene rebuilds the complete rig
            // from one locked mode instead of mutating a live desktop/VR rig.
            CreateText(panelContent, "MODE", 17, FontStyle.Bold,
                new Vector2(120, -266), new Vector2(110, 48),
                TextAnchor.MiddleRight, Muted);
            VolumeSTCubeApplicationMode activeMode = VolumeSTCubeMode.Current;
            CreateButton(panelContent, "DESKTOP",
                new Vector2(265, -266), new Vector2(180, 54),
                activeMode == VolumeSTCubeApplicationMode.Desktop ? Cyan : Card,
                () => SelectApplicationMode(VolumeSTCubeApplicationMode.Desktop));
            CreateButton(panelContent, "VR",
                new Vector2(425, -266), new Vector2(110, 54),
                activeMode == VolumeSTCubeApplicationMode.VirtualReality ? Cyan : Card,
                () => SelectApplicationMode(
                    VolumeSTCubeApplicationMode.VirtualReality));

            // The opening screen is the user's first readability checkpoint.
            // Use the largest SDF glyphs that fit the existing content and
            // button rectangles; no panel or control geometry is changed.
            UpgradeCanvasLabelsToCrispText(panelContent, null);
        }

        private bool EnsureDefaultImportVariable()
        {
            if (datasets.Count == 0)
            {
                importSelectedVariableIndex = -1;
                return false;
            }
            if (importSelectedVariableIndex >= 0 &&
                importSelectedVariableIndex < datasets.Count)
                return true;
            importSelectedVariableIndex = 0;
            for (int index = 0; index < datasets.Count; index++)
            {
                if (!string.Equals(datasets[index].Name, "Prediction_HS",
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                importSelectedVariableIndex = index;
                break;
            }
            return true;
        }

        private string DatasetCompatibilitySummary()
        {
            if (datasets.Count == 0)
                return "no layout";
            VolumeSTCubeSliceDataset first = datasets[0];
            for (int i = 1; i < datasets.Count; i++)
            {
                VolumeSTCubeSliceDataset current = datasets[i];
                if (current.DimX != first.DimX || current.DimY != first.DimY ||
                    current.DimZ != first.DimZ || current.TimeCount != first.TimeCount)
                    return "mixed layouts (usable individually)";
            }
            return "common layout  " + first.TimeCount + "T × " + first.DimZ + "Z";
        }

        private string DatasetNamesSummary()
        {
            if (datasets.Count == 0)
                return "no variables";
            string[] names = new string[datasets.Count];
            for (int i = 0; i < datasets.Count; i++)
                names[i] = datasets[i].Name;
            return string.Join(" / ", names);
        }

        private string DatasetNamesMultiline()
        {
            if (datasets.Count == 0)
                return "no variables";
            string[] names = new string[datasets.Count];
            for (int i = 0; i < datasets.Count; i++)
                names[i] = datasets[i].Name;
            return string.Join("\n", names);
        }

        private void ChooseDatasetFolder()
        {
            if (Application.platform == RuntimePlatform.Android ||
                Application.platform == RuntimePlatform.IPhonePlayer)
            {
                string device = VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled
                    ? "Tablet"
                    : "Quest";
                SetStatus(device + ": copy the dataset root to " + ResolveDataRoot() +
                    ", then choose RESCAN CURRENT FOLDER.");
                return;
            }
            RuntimeFileBrowser.ShowOpenDirectoryDialog(OnDatasetFolderSelected, dataRoot);
        }

        private void ImportWaveServerDataset()
        {
            if (waveClient == null || waveClient.IsImporting)
                return;
            waveImportAttempted = true;
            waveImportError = string.Empty;
            datasets.Clear();
            selectedDataset = null;
            importSelectedVariableIndex = -1;
            datasetImportConfirmed = false;
            string start = SlabLabSettings.WaveStartUtc;
            string end = SlabLabSettings.WaveEndUtc;
            SetStatus("Opening a live Wave timeline for " + start +
                " to " + end + ". Frames load only when displayed.");
            waveClient.Import(start, end, "hs", "elev");
            BuildStage();
        }

        private void OnWaveImportProgress(string message, float progress)
        {
            SetStatus(message + "  " + Mathf.RoundToInt(progress * 100.0f) + "%");
            if (waveImportProgressFill != null)
                waveImportProgressFill.fillAmount = Mathf.Clamp01(progress);
        }

        private void OnWaveImportCompleted(string importedRoot, string error)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                // If the bundled backend never started, the reason belongs on
                // screen with the failure instead of only in the log.
                waveImportError = SlabLabServiceError.WithSetupHint(error,
                    VolumeSTCubePackagedBackendLauncher.SetupHint);
                SetStatus(waveImportError);
                BuildStage();
                return;
            }
            waveImportError = string.Empty;
            SlabLabSettings.DatasetRoot = importedRoot;
            RefreshDatasets(importedRoot);
            BindImportedWaveManifest();
            SetStatus("Wave server data is ready. Review the detected variables, then continue.");
        }

        private void BindImportedWaveManifest()
        {
            if (waveClient == null ||
                string.IsNullOrWhiteSpace(waveClient.DatasetId))
                return;
            for (int index = 0; index < datasets.Count; index++)
            {
                VolumeSTCubeSliceDataset dataset = datasets[index];
                string variableId = dataset.Name;
                if (waveClient.VariableIds != null)
                {
                    for (int variable = 0;
                        variable < waveClient.VariableIds.Length; variable++)
                    {
                        if (string.Equals(waveClient.VariableIds[variable],
                            dataset.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            variableId = waveClient.VariableIds[variable];
                            break;
                        }
                    }
                }
                dataset.DatasetId = waveClient.DatasetId;
                dataset.DatasetVersion = waveClient.DatasetVersion;
                dataset.VariableId = variableId;
            }
        }

        private void OnDatasetFolderSelected(RuntimeFileBrowser.DialogResult result)
        {
            if (result.cancelled || string.IsNullOrWhiteSpace(result.path))
            {
                SetStatus("Dataset selection cancelled.");
                return;
            }
            if (!Directory.Exists(result.path))
            {
                SetStatus("Dataset folder does not exist: " + result.path);
                return;
            }

            if (currentView != null)
            {
                VolumeSTCubeAPI.DestroyView(currentView.viewId);
                currentView = null;
            }
            ClearPairedVariableVolumes();
            selectedDataset = null;
            datasetImportConfirmed = false;
            importSelectedVariableIndex = -1;
            preconfigurationActive = false;
            mainWorkspaceEntered = false;
            authorBoundaryConfirmed = false;
            authoredTimeBuckets = null;
            authoredDepthBuckets = null;
            ResetAxisBucketSelection();
            stage = Stage.DatasetImport;
            if (!VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
            {
                questImportHeadLocked = true;
                UpdateQuestImportHeadLock(true);
            }
            if (spatialRoot != null)
                spatialRoot.SetActive(false);
            if (forVrSurfacePlayer != null)
            {
                forVrSurfacePlayer.CloseCombinedXytTimeSelection();
                forVrSurfacePlayer.SetVisible(false);
            }
            if (spatialAxisComposerRoot != null)
                spatialAxisComposerRoot.SetActive(false);
            if (variablePaletteRoot != null)
                variablePaletteRoot.SetActive(false);
            SlabLabSettings.DatasetRoot = Path.GetFullPath(result.path);
            RefreshDatasets(result.path);
        }

        private void ConfirmDatasetImport()
        {
            if (datasets.Count == 0)
            {
                SetStatus(waveClient != null && waveClient.IsImporting
                    ? "Wait for the Wave server connection to finish."
                    : "Live Wave data is unavailable. Retry the connection.");
                return;
            }
            if (!EnsureDefaultImportVariable())
            {
                SetStatus("No valid variable is available in this dataset.");
                BuildStage();
                return;
            }
            datasetImportConfirmed = true;
            preconfigurationActive = true;
            mainWorkspaceEntered = false;
            authorBoundaryConfirmed = false;
            authoredTimeBuckets = null;
            authoredDepthBuckets = null;
            ResetAxisBucketSelection();
            stage = Stage.Field;
            if (!VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled &&
                xrCamera != null)
            {
                questImportHeadLocked = false;
                Vector3 forward = Vector3.ProjectOnPlane(
                    xrCamera.transform.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.01f)
                    forward = transform.forward;
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                PlaceQuestAnalysisWorkspace(xrCamera.transform.position, forward, right);
            }
            // Hold the Field back until its first texture exists. Showing the
            // empty frame first (and filling it a moment later) is what made the
            // workspace look like it flashed a broken field; the reveal below
            // raises both Fields together. A short deadline keeps a failed load
            // from leaving the stage permanently blank.
            if (spatialRoot != null)
                spatialRoot.SetActive(false);
            pendingFieldPresentationReveal = true;
            pendingFieldRevealDeadline = Time.realtimeSinceStartup + 8.0f;
            if (workflowToolbarCanvas != null)
                workflowToolbarCanvas.gameObject.SetActive(false);
            if (spatialAxisComposerRoot != null)
                spatialAxisComposerRoot.SetActive(false);
            if (variablePaletteRoot != null)
                variablePaletteRoot.SetActive(false);
            viewState.legacyPanelVisible = true;
            if (panelCanvas != null)
                panelCanvas.gameObject.SetActive(true);
            LoadDataset(importSelectedVariableIndex);
            SetStatus("Preparing the selected variable for Time and Depth setup.");
            BuildStage();
        }

        private void OpenDatasetImportStage()
        {
            // The header Previous button can leave Step 2 while the dedicated
            // time-range canvas is still active. Close that edit session first;
            // changing only the Stage leaves the orange boundary bar enabled on
            // top of the dataset page.
            if (boundaryEditActive)
                CancelBoundaryEdit();
            else if (boundaryCanvas != null)
                boundaryCanvas.gameObject.SetActive(false);

            datasetImportConfirmed = false;
            preconfigurationActive = false;
            mainWorkspaceEntered = false;
            authorBoundaryConfirmed = false;
            authoredTimeBuckets = null;
            authoredDepthBuckets = null;
            importSelectedVariableIndex = selectedDataset != null
                ? datasets.IndexOf(selectedDataset) : -1;
            stage = Stage.DatasetImport;
            if (!VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
            {
                questImportHeadLocked = true;
                UpdateQuestImportHeadLock(true);
            }
            if (spatialRoot != null)
                spatialRoot.SetActive(false);
            if (forVrSurfacePlayer != null)
            {
                forVrSurfacePlayer.CloseCombinedXytTimeSelection();
                forVrSurfacePlayer.SetVisible(false);
            }
            if (spatialAxisComposerRoot != null)
                spatialAxisComposerRoot.SetActive(false);
            if (variablePaletteRoot != null)
                variablePaletteRoot.SetActive(false);
            if (workflowToolbarCanvas != null)
                workflowToolbarCanvas.gameObject.SetActive(false);
            viewState.legacyPanelVisible = true;
            if (panelCanvas != null)
                panelCanvas.gameObject.SetActive(true);
            if (currentView != null)
                currentView.SetVisible(false);
            SetStatus(datasets.Count > 0
                ? "Live Wave data is ready. Continue when ready."
                : waveImportAttempted
                    ? "Live Wave data is unavailable. Retry the connection."
                    : "Connecting to the Wave server...");
            BuildStage();
        }

        private List<int> ActiveBoundVariableIndices()
        {
            List<int> indices = new List<int>(datasets.Count);
            for (int rigIndex = 0; rigIndex < spatialAxisRigStates.Count;
                rigIndex++)
            {
                int variableIndex = spatialAxisRigStates[rigIndex].boundVariable;
                if (variableIndex >= 0 && variableIndex < datasets.Count &&
                    !indices.Contains(variableIndex))
                    indices.Add(variableIndex);
            }
            if (indices.Count == 0 && selectedDataset != null)
            {
                int selectedIndex = datasets.IndexOf(selectedDataset);
                if (selectedIndex >= 0)
                    indices.Add(selectedIndex);
            }
            return indices;
        }

        private string[] ActiveVariableNames()
        {
            List<int> indices = ActiveBoundVariableIndices();
            if (indices.Count == 0)
                return new[] { "variable" };
            string[] names = new string[indices.Count];
            for (int index = 0; index < indices.Count; index++)
            {
                int variableIndex = indices[index];
                names[index] = variableIndex >= 0 && variableIndex < datasets.Count
                    ? datasets[variableIndex].Name.ToLowerInvariant()
                    : "variable " + (index + 1);
            }
            return names;
        }

        private void DestroyMaterializedLayerCanvases()
        {
            for (int index = 0; index < materializedLayerCanvases.Count; index++)
                if (materializedLayerCanvases[index] != null)
                    Destroy(materializedLayerCanvases[index].gameObject);
            materializedLayerCanvases.Clear();
        }

        private void RefreshMaterializedLayerVisibility()
        {
            // Extra variable canvases are a 3D-layer affordance. Keeping them
            // alive in the normal 2D result view lets their opaque panel backs
            // sit in front of the interactive grid on some Quest/URP sorting
            // paths, which looks like an entirely empty result panel.
            bool visible = facetGridLayered && facetGridCanvas != null &&
                facetGridCanvas.gameObject.activeSelf;
            for (int index = 0; index < materializedLayerCanvases.Count; index++)
            {
                Canvas layerCanvas = materializedLayerCanvases[index];
                if (layerCanvas != null)
                    layerCanvas.gameObject.SetActive(visible);
            }
        }

        private void BuildMaterializedVariableLayerPanels()
        {
            DestroyMaterializedLayerCanvases();
            if (materializedLayerAtlases.Count <= 1 || facetGridCanvas == null)
                return;
            // The normal Facet Grid remains the interactive front layer. Earlier
            // variables are shown as spatially offset, full-resolution layers
            // behind it and can be inspected by moving or peeling the front grid.
            int extraCount = materializedLayerAtlases.Count - 1;
            for (int layer = 0; layer < extraCount; layer++)
            {
                int variableIndex = layer < materializationVariableIndices.Count
                    ? materializationVariableIndices[layer] : -1;
                string variableName = variableIndex >= 0 &&
                    variableIndex < datasets.Count
                    ? datasets[variableIndex].Name : "variable " + (layer + 1);
                Canvas layerCanvas = CreateFloatingCanvas(
                    "MatPlot Full Matrix " + variableName,
                    Vector3.zero, new Vector2(1120, 650), 0.00058f,
                    VariableColor);
                layerCanvas.sortingOrder = facetGridCanvas.sortingOrder - layer - 1;
                layerCanvas.transform.position = facetGridCanvas.transform.position +
                    facetGridCanvas.transform.forward * (0.070f * (layer + 1)) +
                    facetGridCanvas.transform.right * (0.060f * (layer + 1)) -
                    facetGridCanvas.transform.up * (0.045f * (layer + 1));
                layerCanvas.transform.rotation = facetGridCanvas.transform.rotation;
                layerCanvas.transform.localScale = facetGridCanvas.transform.localScale;
                RectTransform content = layerCanvas.GetComponent<RectTransform>();
                CreateText(content, "MATPLOTAGENT  /  FULL MATRIX",
                    15, FontStyle.Bold, new Vector2(0, 280),
                    new Vector2(1030, 26), TextAnchor.MiddleLeft, Muted);
                CreateText(content, variableName.ToUpperInvariant(),
                    29, FontStyle.Bold, new Vector2(0, 242),
                    new Vector2(1030, 40), TextAnchor.MiddleLeft,
                    VariableColor);
                CreateMaterializedFacetCells(content,
                    materializedLayerAtlases[layer], new Vector2(0, -18),
                    new Vector2(940, 450));
                int capturedLayer = layer;
                CreateButton(content, "BRING FORWARD",
                    new Vector2(390, 242), new Vector2(220, 42),
                    VariableColor,
                    () => FocusMaterializedLayer(capturedLayer));
                // The flat result owns the screen by default. These canvases
                // become visible only after the explicit 3D LAYERS action.
                layerCanvas.gameObject.SetActive(facetGridLayered &&
                    facetGridCanvas.gameObject.activeSelf);
                materializedLayerCanvases.Add(layerCanvas);
            }
        }

        private void FocusMaterializedLayer(int layer)
        {
            int front = materializedLayerAtlases.Count - 1;
            if (layer < 0 || layer >= front ||
                materializationVariableIndices.Count <= front)
                return;
            Texture2D atlas = materializedLayerAtlases[layer];
            materializedLayerAtlases[layer] = materializedLayerAtlases[front];
            materializedLayerAtlases[front] = atlas;
            if (materializedLayerResults.Count > front)
            {
                S4DFacetGridResult result = materializedLayerResults[layer];
                materializedLayerResults[layer] = materializedLayerResults[front];
                materializedLayerResults[front] = result;
            }
            int variable = materializationVariableIndices[layer];
            materializationVariableIndices[layer] =
                materializationVariableIndices[front];
            materializationVariableIndices[front] = variable;
            s4dGridImage = atlas;
            if (materializedLayerResults.Count > front)
                ActivateMaterializedLayerResult(
                    materializedLayerResults[front], variable);
            BuildFacetGridPanel();
            BuildMaterializedVariableLayerPanels();
            SetStatus(datasets[variable].Name +
                " MatPlot layer moved forward for inspection.");
        }

        private string MaterializationStageLabel()
        {
            if (progress < 0.08f)
                return "resolving footprints";
            if (progress < 0.3f)
                return "computing cells";
            if (progress < 0.9f)
                return "rendering shared-scale panels";
            return "validating snapshot";
        }

        private void LoadDataset(int index)
        {
            if (index < 0 || index >= datasets.Count || IsPending(PendingJob.Analysis))
                return;
            if (selectedDataset != null && datasets[index] != selectedDataset)
            {
                // Dataset switching is a display operation. Preserve the day and
                // playback state so choosing another variable does not reset the
                // temporal exploration the user is already watching.
                pendingDatasetDisplayTime = selectedTime;
                resumePlaybackAfterDatasetLoad = forVrSurfacePlayer != null &&
                    forVrSurfacePlayer.IsPlaying;
            }
            if (IsPending(PendingJob.VariableLoad))
            {
                pendingDatasetLoadIndex = index;
                SetStatus("Queued " + datasets[index].Name +
                    "; finishing the current STC texture upload first...");
                return;
            }
            SetPending(PendingJob.VariableLoad, true);
            pendingDatasetLoadIndex = -1;
            VolumeSTCubeSliceDataset next = datasets[index];
            SetStatus("Materializing " + next.Name + " as a continuous cube...");
            BuildStage();
            StartCoroutine(LoadDatasetAfterFeedback(index));
        }

        private IEnumerator LoadDatasetAfterFeedback(int index)
        {
            // Give the loading card one complete frame before the unavoidable GPU
            // texture upload. Without this yield Quest appears frozen after click.
            yield return null;
            // Batch-mode guards do not render a frame, so this wait would never
            // resume and the variable-load pending flag would stay set forever.
            if (!Application.isBatchMode)
                yield return new WaitForEndOfFrame();
            bool loaded = false;
            try
            {
                loaded = LoadDatasetNow(index);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                HandleDatasetLoadFailure(
                    "Unexpected error while opening the dataset: " +
                    exception.Message);
            }
            finally
            {
                // Never leave the application permanently locked in a loading
                // state when a malformed RAW file or GPU upload throws.
                SetPending(PendingJob.VariableLoad, false);
            }
            if (!loaded)
            {
                pendingDatasetLoadIndex = -1;
                BuildStage();
                yield break;
            }
            if (pendingDatasetLoadIndex >= 0 &&
                pendingDatasetLoadIndex < datasets.Count &&
                datasets[pendingDatasetLoadIndex] != selectedDataset)
            {
                int queuedIndex = pendingDatasetLoadIndex;
                pendingDatasetLoadIndex = -1;
                LoadDataset(queuedIndex);
                yield break;
            }
            pendingDatasetLoadIndex = -1;
            BuildStage();
        }

        private bool LoadDatasetNow(int index)
        {
            if (index < 0 || index >= datasets.Count)
                return false;
            VolumeSTCubeSliceDataset next = datasets[index];
            int requestedDisplayTime = pendingDatasetDisplayTime;
            bool shouldResumePlayback = resumePlaybackAfterDatasetLoad;
            pendingDatasetDisplayTime = -1;
            resumePlaybackAfterDatasetLoad = false;
            // Entering the main tri-axis workspace permanently locks the
            // Time/Depth setup. Switching the active variable must not turn
            // that into a second Author Boundary session.
            bool preservePreconfiguredBoundaries = mainWorkspaceEntered;
            if (forVrSurfacePlayer != null)
            {
                if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled &&
                    forVrSurfacePlayer.XytCompanion != null)
                {
                    // The Field is about to be replaced, so the desktop focus
                    // targets have to be rebuilt against the new one. Restoring
                    // the whole presentation first snapped the Fields and the
                    // tri-axis body back to their authored pose for a frame and
                    // then re-docked them, which is what an operator sees as the
                    // body flickering and stretching while variables are bound.
                    // The per-frame desktop pose re-applies everything anyway.
                    viewState.desktopFocusTargetsReady = false;
                }
                Destroy(forVrSurfacePlayer);
                forVrSurfacePlayer = null;
            }
            if (currentView != null)
                VolumeSTCubeAPI.DestroyView(currentView.viewId);

            VolumeSTCubeConfig config = VolumeSTCubeConfig.Default("quest_spatial_slab_lab");
            config.datasetName = next.Name;
            config.dataLayout = VolumeSTCubeDataLayout.XYZTimeSeries;
            config.showTimeline = false;
            config.timelineAutoPlay = false;
            config.opacity = FieldOpacity;
            int nextVariableIndex = datasets.IndexOf(next);
            int defaultTimeStart = Mathf.Clamp(
                Mathf.FloorToInt(next.TimeCount / 3.0f) - 1,
                0, Mathf.Max(0, next.TimeCount - 2));
            int defaultTimeEnd = Mathf.Clamp(
                Mathf.FloorToInt(next.TimeCount * 2.0f / 3.0f) - 1,
                defaultTimeStart + 1,
                Mathf.Max(defaultTimeStart + 1, next.TimeCount - 1));
            if (!sharedBoundariesInitialized)
            {
                sharedTimeBoundaryStart = defaultTimeStart;
                sharedTimeBoundaryEnd = defaultTimeEnd;
                sharedSelectedTime = Mathf.Clamp(next.TimeCount / 2, 0,
                    Mathf.Max(0, next.TimeCount - 1));
                sharedDepthBoundaryLow = 1.0f / 3.0f;
                sharedDepthBoundaryHigh = 2.0f / 3.0f;
                sharedSelectedZ = Mathf.Clamp(next.DimZ / 2, 0,
                    Mathf.Max(0, next.DimZ - 1));
                sharedBoundariesInitialized = true;
            }
            SpatialAxisRigState nextBoundaryState = nextVariableIndex >= 0
                ? spatialAxisRigStates.Find(state =>
                    state.boundVariable == nextVariableIndex) : null;
            selectedTime = Mathf.Clamp(nextBoundaryState != null &&
                    !nextBoundaryState.usesSharedBoundaries
                        ? nextBoundaryState.customSelectedTime
                        : sharedSelectedTime,
                0, Mathf.Max(0, next.TimeCount - 1));
            if (requestedDisplayTime >= 0)
                selectedTime = Mathf.Clamp(requestedDisplayTime, 0,
                    Mathf.Max(0, next.TimeCount - 1));
            config.initialTimeIndex = selectedTime;
            config.timeMin = selectedTime / (float)Mathf.Max(1, next.TimeCount);
            config.timeMax = (selectedTime + 1) /
                (float)Mathf.Max(1, next.TimeCount);
            if (!VolumeSTCubeAPI.TryCreateViewFromRawDataset(
                next, config, out currentView, out string error))
            {
                HandleDatasetLoadFailure(error);
                return false;
            }

            selectedDataset = next;
            RefreshFieldDatasetSelector();
            LoadEffectiveBoundaryValues(nextBoundaryState);
            ResolveSelectedDatasetManifest();
            if (!preservePreconfiguredBoundaries)
            {
                authorBoundaryConfirmed = false;
                initialBoundarySetupActive = false;
                initialTimeBoundaryComplete = false;
                initialDepthBoundaryComplete = false;
                authoredTimeBuckets = null;
                authoredDepthBuckets = null;
            }
            activeTimeBuckets = null;
            activeDepthBuckets = null;
            ResetAnalysisWorkingView();
            slabNormalized = next.DimZ > 1 ? selectedZ / (float)(next.DimZ - 1) : 0.5f;
            ClearChart();
            HideLegacyAxis();
            ApplyTimeFilter();
            if (IsForVrSurfaceDataset)
            {
                forVrSurfacePlayer = spatialRoot.AddComponent<VolumeSTCubeForVrSurfacePlayer>();
                forVrSurfacePlayer.Initialize(next, selectedTime,
                    currentView.rootObject.transform, OnForVrSurfaceTimeChanged);
                if (shouldResumePlayback)
                    forVrSurfacePlayer.EnsurePlaybackContinues();
                // The surface player builds its first frame synchronously during
                // Initialize, so the Field is complete by the time control gets
                // here. Waiting for the hold-back deadline is only needed on the
                // asynchronous 3D-texture path, and doing so here left the whole
                // presentation hidden for a fixed eight seconds after the live
                // variable had already loaded.
                RevealFieldPresentation();
            }
            else
            {
                FrameVolume();
                StartCoroutine(RevealVolumeWhenTexturesReady(currentView));
                StartCoroutine(RefitVolumeAfterFrameChange());
            }
            RefreshSlabTexture();
            RefreshVariableFacetStacks();
            RefreshSpatialAxisControllers();
            int datasetIndex = datasets.IndexOf(next);
            int rigIndex = spatialAxisRigStates.FindIndex(
                state => state.boundVariable == datasetIndex);
            if (rigIndex >= 0)
                ApplySelectedAxisRigState(rigIndex, false);
            RebuildTimeMarkers();
            UpdateTimeBoundaryHandles();
            UpdateDepthBoundaryPlanes();
            UpdateSlabVisual(false);
            RecordTrailEvent("VARIABLE", "loaded " + next.Name);
            SetStatus(IsForVrSurfaceDataset
                ? next.Name + " ready: " + next.TimeCount +
                    " hourly Hong Kong surface frames."
                : next.Name + " ready: " + next.TimeCount + " times x " +
                    next.DimZ + " depth layers.");
            if (workflowToolbarCanvas != null)
                workflowToolbarCanvas.gameObject.SetActive(mainWorkspaceEntered);
            if (preconfigurationActive)
            {
                viewState.legacyPanelVisible = true;
                if (panelCanvas != null)
                    panelCanvas.gameObject.SetActive(true);
                if (spatialAxisComposerRoot != null)
                    spatialAxisComposerRoot.SetActive(false);
            }
            else
            {
                viewState.legacyPanelVisible = false;
                if (panelCanvas != null)
                    panelCanvas.gameObject.SetActive(false);
            }
            return true;
        }

        private void ResolveSelectedDatasetManifest()
        {
            if (selectedDataset == null)
                return;
            SetPending(PendingJob.DatasetManifest, true);
            datasetManifestError = string.Empty;
            selectedDataset.DatasetId = string.Empty;
            selectedDataset.VariableId = string.Empty;
            VolumeSTCubeS4DAnalysisClient resolver =
                new VolumeSTCubeS4DAnalysisClient(s4dUrl, 30, 1.0f);
            StartCoroutine(resolver.ResolveDataset(
                selectedDataset.Name,
                selectedDataset.DimX,
                selectedDataset.DimY,
                selectedDataset.DimZ,
                selectedDataset.TimeCount,
                selectedDataset.DirectoryPath,
                selectedDataset.RawPaths != null && selectedDataset.RawPaths.Length > 0
                    ? selectedDataset.RawPaths[0]
                    : string.Empty,
                OnDatasetManifestResolved));
        }

        private void OnDatasetManifestResolved(
            S4DDatasetResolution resolution,
            string error)
        {
            SetPending(PendingJob.DatasetManifest, false);
            if (selectedDataset == null)
                return;
            if (resolution == null)
            {
                resumeMaterializationAfterManifest = false;
                datasetManifestError = string.IsNullOrWhiteSpace(error)
                    ? "No validated S4D manifest matches this variable."
                    : error;
                SetStatus(datasetManifestError);
                BuildStage();
                return;
            }
            selectedDataset.DatasetId = resolution.datasetId;
            selectedDataset.DatasetVersion = resolution.datasetVersion;
            selectedDataset.VariableId = resolution.variableId;
            selectedDataset.Unit = resolution.unit;
            selectedDataset.ValueSemantics = resolution.valueSemantics;
            datasetManifestError = string.Empty;
            SetStatus(
                selectedDataset.Name + " linked to manifest " +
                resolution.datasetId + " / " + resolution.datasetVersion + ".");
            BuildStage();
            if (resumeMaterializationAfterManifest &&
                spatialWorkflowStep == SpatialWorkflowStep.Materializing)
            {
                resumeMaterializationAfterManifest = false;
                SetStatus("Dataset metadata restored. Continuing Full Matrix materialization.");
                StartS4DGridJob();
            }
        }

        private void RefreshSlabTexture()
        {
            if (selectedDataset == null)
                return;
            try
            {
                if (slabTexture != null)
                    Destroy(slabTexture);
                VolumeSTCubeRawSlice slice = VolumeSTCubeRawSliceReader.ReadSlice(
                    selectedDataset.RawPaths[selectedTime], selectedDataset.IniPaths[selectedTime], selectedZ);
                slabTexture = VolumeSTCubeRawSliceReader.CreatePreviewTexture(slice, 512, 512);
                slabPreviewMaterial.mainTexture = slabTexture;
                slabPreviewMaterial.mainTextureScale = Vector2.one;
                slabPreviewMaterial.mainTextureOffset = Vector2.zero;
            }
            catch (Exception exception)
            {
                SetStatus("Slab read failed: " + exception.Message);
            }
        }

        private VolumeRenderedObject GetOrCreatePairedVariableVolume(
            int variableIndex, int timeIndex)
        {
            if (variableIndex < 0 || variableIndex >= datasets.Count)
                return null;
            if (pairedVariableVolumes.TryGetValue(variableIndex,
                    out VolumeRenderedObject existing) && existing != null &&
                pairedVariableVolumeTimes.TryGetValue(variableIndex,
                    out int existingTime) && existingTime == timeIndex)
                return existing;

            DestroyPairedVariableVolume(variableIndex);
            VolumeSTCubeSliceDataset dataset = datasets[variableIndex];
            if (dataset.RawPaths == null || dataset.IniPaths == null ||
                timeIndex < 0 || timeIndex >= dataset.RawPaths.Length ||
                timeIndex >= dataset.IniPaths.Length)
                return null;
            try
            {
                VolumeRenderedObject volume = VolumeSTCubeRawVolumeFactory.Import(
                    dataset.RawPaths[timeIndex], dataset.IniPaths[timeIndex],
                    dataset.Name);
                if (volume == null)
                    return null;
                // Keep Quest on the scalar unlit DVR path. Enabling lighting
                // allocates an additional gradient Texture3D for every variable.
                volume.SetLightingEnabled(false);
                ApplyPairedVolumeAppearance(volume);
                pairedVariableVolumes[variableIndex] = volume;
                pairedVariableVolumeTimes[variableIndex] = timeIndex;
                Renderer[] renderers = volume.GetComponentsInChildren<Renderer>(true);
                for (int index = 0; index < renderers.Length; index++)
                {
                    Renderer renderer = renderers[index];
                    if (renderer == null)
                        continue;
                    // Match the established STC convention used by the primary
                    // controller: texture Z is shown as the vertical depth axis.
                    renderer.transform.localRotation =
                        Quaternion.Euler(90.0f, 0.0f, 0.0f);
                    renderer.shadowCastingMode =
                        UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.allowOcclusionWhenDynamic = false;
                    renderer.sortingOrder = -100;
                }
                if (volume.dataset != null)
                    volume.dataset.rotation =
                        Quaternion.Euler(90.0f, 0.0f, 0.0f);
                Collider[] colliders = volume.GetComponentsInChildren<Collider>(true);
                for (int index = 0; index < colliders.Length; index++)
                    if (colliders[index] != null)
                        Destroy(colliders[index]);
                return volume;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Continuous paired STC volume failed: " +
                    exception.Message);
                return null;
            }
        }

        private void RemoveUnusedPairedVariableVolumes(List<int> active)
        {
            List<int> stale = new List<int>();
            foreach (KeyValuePair<int, VolumeRenderedObject> entry in
                pairedVariableVolumes)
                if (!active.Contains(entry.Key))
                    stale.Add(entry.Key);
            for (int index = 0; index < stale.Count; index++)
                DestroyPairedVariableVolume(stale[index]);
        }

        private void DestroyPairedVariableVolume(int variableIndex)
        {
            if (pairedVariableVolumes.TryGetValue(variableIndex,
                    out VolumeRenderedObject volume) && volume != null)
            {
                // Destroy is deferred until the end of the Unity frame. Hide
                // the outgoing renderers immediately so a newly imported day
                // can never overlap the previous day for one visible frame.
                Renderer[] outgoingRenderers =
                    volume.GetComponentsInChildren<Renderer>(true);
                for (int index = 0; index < outgoingRenderers.Length; index++)
                    if (outgoingRenderers[index] != null)
                        outgoingRenderers[index].enabled = false;
                VolumeDataset dataset = volume.dataset;
                if (dataset != null)
                {
                    dataset.ReleaseRuntimeTextures();
                    Destroy(dataset);
                }
                Destroy(volume.gameObject);
            }
            pairedVariableVolumes.Remove(variableIndex);
            pairedVariableVolumeTimes.Remove(variableIndex);
        }

        private void ClearPairedVariableVolumes()
        {
            List<int> variables = new List<int>(pairedVariableVolumes.Keys);
            for (int index = 0; index < variables.Count; index++)
                DestroyPairedVariableVolume(variables[index]);
        }

        private bool HasResolvedDatasetManifest()
        {
            return selectedDataset != null &&
                !string.IsNullOrWhiteSpace(selectedDataset.DatasetId) &&
                !string.IsNullOrWhiteSpace(selectedDataset.VariableId);
        }

        private VolumeSTCubeSliceDataset DatasetForBucket(
            S4DIndexBucketRequest bucket)
        {
            if (bucket != null && !string.IsNullOrWhiteSpace(bucket.variableId))
            {
                for (int index = 0; index < datasets.Count; index++)
                {
                    VolumeSTCubeSliceDataset candidate = datasets[index];
                    if (string.Equals(candidate.VariableId, bucket.variableId,
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(candidate.Name, bucket.variableId,
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(candidate.Name, bucket.label,
                            StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
            }
            return selectedDataset;
        }

        /// <summary>
        /// Self-healing guard for the opening step. The Wave registration is
        /// started once at boot; if that attempt never left the client (a
        /// domain reload while it was in flight, a backend that was still
        /// starting) the workspace would sit on an empty dataset list while the
        /// operator could still walk into later steps. Retry a few times with a
        /// short delay and say so, instead of leaving a dead end.
        /// </summary>
        private void WatchWaveImport()
        {
            if (datasets.Count > 0 || IsPending(PendingJob.Analysis) || IsPending(PendingJob.DatasetManifest))
            {
                waveRetryDeadline = 0.0f;
                return;
            }
            if (waveClient == null || waveClient.IsImporting)
                return;
            // Three quick retries cover a backend that is still starting, then
            // keep trying slowly: an operator who leaves the app open should not
            // have to press Retry themselves when the service comes back.
            float wait = waveRetryCount < MaxWaveAutoRetries
                ? 4.0f : 30.0f;
            if (waveRetryDeadline <= 0.0f)
            {
                waveRetryDeadline = Time.realtimeSinceStartup + wait;
                return;
            }
            if (Time.realtimeSinceStartup < waveRetryDeadline)
                return;
            waveRetryDeadline = 0.0f;
            waveRetryCount++;
            SetStatus(
                waveRetryCount <= MaxWaveAutoRetries
                    ? "Reconnecting to the Wave server (retry " +
                        waveRetryCount + " of " + MaxWaveAutoRetries + ")..."
                    : "Still waiting for the Wave server...");
            ImportWaveServerDataset();
        }
    }
}
