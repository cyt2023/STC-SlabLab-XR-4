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
        private void BuildResultStage()
        {
            FindDigestExtremes(out int minimumIndex, out int maximumIndex,
                out int widestIndex);
            int columnPageCount = Mathf.Max(1,
                Mathf.CeilToInt(activeGridColumns / 3.0f));
            int rowPageCount = Mathf.Max(1,
                Mathf.CeilToInt(activeGridRows / 3.0f));
            digestColumnPage = Mathf.Clamp(digestColumnPage, 0,
                columnPageCount - 1);
            digestRowPage = Mathf.Clamp(digestRowPage, 0,
                rowPageCount - 1);
            CreateText(panelContent, "FINDINGS", 32, FontStyle.Bold,
                new Vector2(0, 214), new Vector2(1030, 36),
                TextAnchor.MiddleLeft, Ink);
            CreateText(panelContent,
                (activeGridColumns * activeGridRows) +
                " PANELS  ·  " +
                IntentDisplayLabel(),
                16, FontStyle.Bold, new Vector2(-170, 181),
                new Vector2(690, 24),
                TextAnchor.MiddleLeft, Muted);
            if (columnPageCount > 1 || rowPageCount > 1)
            {
                CreateButton(panelContent, "PREV PAGE",
                    new Vector2(315, 184), new Vector2(145, 30), Card,
                    () => ChangeDigestPage(-1));
                CreateButton(panelContent, "NEXT PAGE",
                    new Vector2(475, 184), new Vector2(145, 30), Cyan,
                    () => ChangeDigestPage(1));
            }

            CreatePanelCard(panelContent, new Vector2(-242, 12),
                new Vector2(570, 318), Cyan);
            CreateText(panelContent,
                FacetAxisSummary().ToUpperInvariant() + "  /  PAGE " +
                (digestColumnPage + 1) + " of " + columnPageCount,
                15,
                FontStyle.Bold, new Vector2(-242, 145), new Vector2(530, 22),
                TextAnchor.MiddleLeft, Cyan);
            float cellWidth = 170.0f;
            float cellHeight = 78.0f;
            for (int localRow = 0; localRow < 3; localRow++)
            {
                int row = digestRowPage * 3 + localRow;
                if (row >= activeGridRows)
                    continue;
                for (int localColumn = 0; localColumn < 3; localColumn++)
                {
                    int column = digestColumnPage * 3 + localColumn;
                    if (column >= activeGridColumns)
                        continue;
                    int selectedColumn = column;
                    int selectedRow = row;
                    int sourceIndex = SourceCellIndex(column, row);
                    string tag = DigestTaskTag(sourceIndex, minimumIndex,
                        maximumIndex, widestIndex);
                    Color accent = DigestTaskColor(sourceIndex, minimumIndex,
                        maximumIndex, widestIndex);
                    Vector2 cellPosition = new Vector2(
                        -412 + localColumn * cellWidth,
                        91 - localRow * cellHeight);
                    CreateButton(panelContent, string.Empty, cellPosition,
                        new Vector2(cellWidth - 10, cellHeight - 10), accent,
                        () => SelectDigestCell(selectedColumn, selectedRow));
                    CreateText(panelContent,
                        CellLabel(column, row).ToUpperInvariant(),
                        13, FontStyle.Bold, cellPosition + new Vector2(0, 14),
                        new Vector2(cellWidth - 28, 22),
                        TextAnchor.MiddleLeft, Ink).raycastTarget = false;
                    CreateText(panelContent,
                        "MEAN " + matrixMeans[sourceIndex].ToString("0.##") +
                        "   " + tag,
                        12, FontStyle.Bold, cellPosition + new Vector2(0, -13),
                        new Vector2(cellWidth - 28, 20),
                        TextAnchor.MiddleLeft, accent).raycastTarget = false;
                }
            }

            int selectedIndex = SourceCellIndex(selectedGridColumn, selectedGridRow);
            CreatePanelCard(panelContent, new Vector2(360, 65),
                new Vector2(360, 180), Purple);
            CreateText(panelContent, "SELECTED CELL", 16, FontStyle.Bold,
                new Vector2(360, 132), new Vector2(314, 22),
                TextAnchor.MiddleLeft, Purple);
            CreateText(panelContent,
                CellLabel(selectedGridColumn, selectedGridRow).ToUpperInvariant(),
                20, FontStyle.Bold, new Vector2(360, 102),
                new Vector2(314, 30), TextAnchor.MiddleLeft, Ink);
            CreateText(panelContent,
                "minimum   " + matrixMinimums[selectedIndex].ToString("0.##") +
                "\nmean       " + matrixMeans[selectedIndex].ToString("0.##") +
                "\nmaximum   " + matrixMaximums[selectedIndex].ToString("0.##"),
                15, FontStyle.Bold, new Vector2(360, 52),
                new Vector2(314, 60), TextAnchor.UpperLeft, Ink);
            CreateText(panelContent,
                facetCellStale[selectedIndex] ? "STALE / RE-MATERIALIZE" :
                facetCellBoundarySuspect[selectedIndex] ? "RECHECK BOUNDARY" :
                facetCellLocalized[selectedIndex] ? "LOCAL PATTERN" :
                facetCellInspected[selectedIndex] ? "SUPPORTED" :
                DigestTaskTag(selectedIndex, minimumIndex, maximumIndex, widestIndex),
                13, FontStyle.Bold, new Vector2(360, 17),
                new Vector2(314, 22), TextAnchor.MiddleLeft,
                DigestTaskColor(selectedIndex, minimumIndex, maximumIndex,
                    widestIndex));
            CreateButton(panelContent, "GROUND SELECTED",
                new Vector2(360, -5), new Vector2(314, 34), Green,
                () =>
                {
                    gridCellSelected = true;
                    SelectS4DGridCell(selectedGridColumn, selectedGridRow);
                });

            CreatePanelCard(panelContent, new Vector2(360, -112),
                new Vector2(360, 154), Amber);
            CreateText(panelContent, ActiveDigestHeadline(), 15,
                FontStyle.Bold, new Vector2(360, -52),
                new Vector2(314, 20), TextAnchor.MiddleLeft,
                currentDigest != null ? Green :
                IsPending(PendingJob.Digest) ? Cyan : Amber);
            CreateText(panelContent,
                ActiveDigestNarrative(minimumIndex, maximumIndex, widestIndex),
                13, FontStyle.Normal, new Vector2(360, -124),
                new Vector2(314, 112), TextAnchor.UpperLeft, Ink);

            CreateButton(panelContent, "BACK TO GRID", new Vector2(-315, -216),
                new Vector2(260, 40), Cyan, ReturnToFacetGrid);
            CreateButton(panelContent, "SLABTRAIL", new Vector2(-30, -216),
                new Vector2(260, 40), Purple, ToggleTrailPanel);
            CreateButton(panelContent, "GROUND", new Vector2(255, -216),
                new Vector2(260, 40), Card, () => Navigate(Stage.Analyze));
        }

        private Color DigestTaskColor(int sourceIndex, int minimumIndex,
            int maximumIndex, int widestIndex)
        {
            sourceIndex = Mathf.Clamp(sourceIndex, 0, facetCellStale.Length - 1);
            if (facetCellStale[sourceIndex] ||
                facetCellBoundarySuspect[sourceIndex])
                return Amber;
            if (facetCellInspected[sourceIndex])
                return Green;
            string task = (intentTask ?? string.Empty).ToLowerInvariant();
            if (task.Contains("anomal") && sourceIndex == widestIndex)
                return Purple;
            if (sourceIndex == maximumIndex)
                return TimeColor;
            if (sourceIndex == minimumIndex)
                return DepthColor;
            return Card;
        }

        private string DigestSourceCellLabel(int sourceIndex)
        {
            int sourceColumns = Mathf.Max(1,
                activeTimeBuckets != null ? activeTimeBuckets.Length : 3);
            int timeIndex = Mathf.Clamp(sourceIndex % sourceColumns, 0,
                Mathf.Max(0, sourceColumns - 1));
            int sourceRows = Mathf.Max(1,
                activeDepthBuckets != null ? activeDepthBuckets.Length : 3);
            int depthIndex = Mathf.Clamp(sourceIndex / sourceColumns, 0,
                Mathf.Max(0, sourceRows - 1));
            int column = activeGridTransposed ? depthIndex : timeIndex;
            int row = activeGridTransposed ? timeIndex : depthIndex;
            return CellLabel(column, row);
        }

        private void SelectFindingSourceCell(int sourceIndex)
        {
            if (sourceIndex < 0)
                return;
            for (int row = 0; row < activeGridRows; row++)
            {
                for (int column = 0; column < activeGridColumns; column++)
                {
                    if (SourceCellIndex(column, row) != sourceIndex)
                        continue;
                    SelectFacetPreviewCell(column, row);
                    SetStatus("Finding selected: " + CellLabel(column, row) +
                        ". Inspect its source footprint or send it to Ground.");
                    return;
                }
            }
        }

        private void SelectDigestCell(int column, int row)
        {
            selectedGridColumn = Mathf.Clamp(column, 0,
                Mathf.Max(0, activeGridColumns - 1));
            selectedGridRow = Mathf.Clamp(row, 0,
                Mathf.Max(0, activeGridRows - 1));
            gridCellSelected = true;
            int sourceIndex = SourceCellIndex(selectedGridColumn, selectedGridRow);
            selectedCellPinned = facetCellPinned[Mathf.Clamp(sourceIndex, 0,
                facetCellPinned.Length - 1)];
            BuildStage();
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
        }

        private void OnDigestComplete(AnalysisNodeState node,
            S4DDigestResult digest, string error)
        {
            if (node == null || FindAnalysisNode(node.nodeId) != node)
                return;
            node.digestPending = false;
            node.digest = digest;
            node.digestError = error ?? string.Empty;
            if (digest != null && !string.IsNullOrWhiteSpace(node.jobId))
                layerDigestCache[node.jobId] = digest;
            if (node == currentAnalysisNode)
            {
                currentDigest = digest;
                digestError = node.digestError;
                SetPending(PendingJob.Digest, false);
                if (stage == Stage.Result)
                    BuildStage();
                // The world-space Facet Grid is normally still open when the
                // asynchronous LLM digest finishes. Refresh its docked summary
                // card as well; otherwise it remains stuck on the initial
                // "select a cell" placeholder until another interaction occurs.
                if (facetGridCanvas != null &&
                    facetGridCanvas.gameObject.activeSelf)
                    BuildFacetGridPanel();
                if (aiFindingsCanvas != null &&
                    aiFindingsCanvas.gameObject.activeSelf)
                    BuildAiFindingsPanel();
                else if (digest != null && stage == Stage.Result)
                    OpenAiFindingsPanel();
            }
            if (trailCanvas != null && trailCanvas.gameObject.activeSelf)
                BuildTrailPanel();
            if (node != currentAnalysisNode) return;
            SetStatus(digest != null
                ? ((digest.generatedBy != null &&
                    digest.generatedBy.StartsWith("llm:",
                        StringComparison.OrdinalIgnoreCase))
                    ? "AI " + Mathf.Max(1, activeGridColumns * activeGridRows) +
                        "-panel summary ready for "
                    : "Evidence-based Findings ready for ") + node.nodeId + "."
                : "Findings fallback active for " + node.nodeId + ": " +
                    node.digestError);
        }

        private void CreateAiFindingsPanel()
        {
            aiFindingsCanvas = CreateFloatingCanvas(
                "AI Findings",
                new Vector3(0.22f, 1.76f, 1.12f),
                new Vector2(1360, 980),
                0.00058f,
                Green);
            aiFindingsContent = aiFindingsCanvas.GetComponent<RectTransform>();
            aiFindingsCanvas.sortingOrder = 132;
            AddDesktopPanelFocusTarget(aiFindingsCanvas,
                FocusDesktopMatrixPanel);
            aiFindingsCanvas.gameObject.SetActive(false);
        }

        private void OpenAiFindingsPanel()
        {
            if (aiFindingsCanvas == null)
                return;
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled &&
                stage == Stage.Matrix)
                SetDesktopFocusView(DesktopFocusView.SlabAxis, true);
            if (facetGridCanvas != null)
            {
                aiFindingsCanvas.transform.position =
                    facetGridCanvas.transform.position;
                aiFindingsCanvas.transform.rotation =
                    facetGridCanvas.transform.rotation;
            }
            HidePrimaryToolsExcept(aiFindingsCanvas);
            aiFindingsCanvas.gameObject.SetActive(true);
            BuildAiFindingsPanel();
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
                VolumeSTCubeFlatScreenHUD.NotifyWorkflowChanged();
        }

        private void CloseAiFindingsPanel()
        {
            if (aiFindingsCanvas != null)
                aiFindingsCanvas.gameObject.SetActive(false);
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled &&
                stage == Stage.Matrix)
            {
                SetDesktopFocusView(DesktopFocusView.SlabAxis, true);
                if (s4dGridImage != null && facetGridCanvas != null)
                {
                    HidePrimaryToolsExcept(facetGridCanvas);
                    facetGridCanvas.gameObject.SetActive(true);
                    BuildFacetGridPanel();
                }
                else if (panelCanvas != null)
                {
                    ShowPrimaryTool(panelCanvas);
                    BuildStage();
                }
                VolumeSTCubeFlatScreenHUD.NotifyWorkflowChanged();
                SetStatus("Findings closed. Returned to the Full Matrix.");
                return;
            }
            if (facetGridCanvas != null)
            {
                HidePrimaryToolsExcept(facetGridCanvas);
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
            }
        }

        private void BuildAiFindingsPanel()
        {
            if (aiFindingsContent == null)
                return;
            ClearChildren(aiFindingsContent);
            CreateText(aiFindingsContent, "AI FINDINGS", 38, FontStyle.Bold,
                new Vector2(-565, 420), new Vector2(820, 54),
                TextAnchor.MiddleLeft, Green);
            CreateButton(aiFindingsContent, "BACK TO MATRIX",
                new Vector2(520, 420), new Vector2(240, 46), Card,
                CloseAiFindingsPanel);

            if (IsPending(PendingJob.Digest))
            {
                CreatePanelCard(aiFindingsContent, new Vector2(0, 32),
                    new Vector2(1080, 430), Cyan);
                CreateText(aiFindingsContent, "AI IS COMPARING THE MATRIX",
                    31, FontStyle.Bold, new Vector2(0, 92),
                    new Vector2(960, 54), TextAnchor.MiddleCenter, Cyan);
                CreateText(aiFindingsContent,
                    "The charts remain available as evidence. This panel will update when the interpretation is ready.",
                    23, FontStyle.Normal, new Vector2(0, 22),
                    new Vector2(900, 100), TextAnchor.UpperCenter, Ink);
                return;
            }

            if (currentDigest == null)
            {
                CreatePanelCard(aiFindingsContent, new Vector2(0, 32),
                    new Vector2(1080, 430), Amber);
                CreateText(aiFindingsContent, "AI INTERPRETATION UNAVAILABLE",
                    31, FontStyle.Bold, new Vector2(0, 92),
                    new Vector2(960, 54), TextAnchor.MiddleCenter, Amber);
                CreateText(aiFindingsContent,
                    string.IsNullOrWhiteSpace(digestError)
                        ? "No AI interpretation has been returned for this matrix yet."
                        : CompactAiFinding(digestError, 260),
                    22, FontStyle.Normal, new Vector2(0, 12),
                    new Vector2(900, 120), TextAnchor.UpperCenter, Ink);
                return;
            }

            string headline = string.IsNullOrWhiteSpace(currentDigest.headline)
                ? "What this matrix suggests"
                : HumanizeDigestCellIds(currentDigest.headline);
            CreatePanelCard(aiFindingsContent, new Vector2(0, 304),
                new Vector2(1200, 170), Green);
            Text headlineText = CreateText(aiFindingsContent,
                WrapAiFinding(headline, 68),
                28, FontStyle.Bold, new Vector2(0, 338),
                new Vector2(1110, 58), TextAnchor.MiddleLeft, Ink);
            headlineText.resizeTextForBestFit = false;
            Text summaryText = CreateText(aiFindingsContent,
                WrapAiFinding(
                    HumanizeDigestCellIds(currentDigest.summary), 96),
                19, FontStyle.Normal, new Vector2(0, 274),
                new Vector2(1110, 82), TextAnchor.UpperLeft, Muted);
            summaryText.resizeTextForBestFit = false;

            string[] findings = currentDigest.findings ?? new string[0];
            int findingCount = Mathf.Min(5, findings.Length);
            if (findingCount == 0)
            {
                CreatePanelCard(aiFindingsContent, new Vector2(0, -38),
                    new Vector2(1100, 330), Purple);
                CreateText(aiFindingsContent,
                    "No additional AI conclusions were returned. Use the evidence controls in the matrix to inspect individual cells.",
                    24, FontStyle.Normal, new Vector2(0, -16),
                    new Vector2(990, 150), TextAnchor.UpperLeft, Ink);
            }
            else
            {
                for (int index = 0; index < findingCount; index++)
                {
                    int column = index % 2;
                    int row = index / 2;
                    Vector2 position = new Vector2(
                        findingCount == 5 && index == 4
                            ? 0 : (column == 0 ? -305 : 305),
                        125 - row * 190);
                    CreatePanelCard(aiFindingsContent, position,
                        new Vector2(580, 174), Purple);
                    CreateText(aiFindingsContent, "0" + (index + 1),
                        21, FontStyle.Bold, position + new Vector2(-242, 56),
                        new Vector2(54, 34), TextAnchor.MiddleLeft, Purple);
                    Text findingText = CreateText(aiFindingsContent,
                        WrapAiFinding(
                            HumanizeDigestCellIds(findings[index]), 48),
                        20, FontStyle.Normal, position + new Vector2(28, -3),
                        new Vector2(468, 132), TextAnchor.UpperLeft, Ink);
                    // Do not let Unity shrink an entire finding to make one
                    // long line fit. Word-wrap it into the available card and
                    // preserve a consistent, readable evidence-text size.
                    findingText.resizeTextForBestFit = false;
                }
            }

            CreateText(aiFindingsContent,
                "AI INTERPRETATION  /  Verify each claim against the matrix and its source cells.",
                15, FontStyle.Bold, new Vector2(0, -458),
                new Vector2(1200, 28), TextAnchor.MiddleCenter, Muted);
        }

        private void BuildDigestCard()
        {
            CreatePanelCard(panelContent, new Vector2(400, -55), new Vector2(250, 332), Purple);
            CreateText(panelContent,
                "FINDINGS",
                18, FontStyle.Bold,
                new Vector2(400, 84), new Vector2(205, 28), TextAnchor.MiddleLeft, Purple);
            CreateText(panelContent,
                gridCellSelected
                    ? CellLabel(selectedGridColumn, selectedGridRow)
                    : Mathf.Max(1, activeGridColumns * activeGridRows) +
                        " CELLS  /  CURRENT GRID",
                11, FontStyle.Bold,
                new Vector2(400, 58), new Vector2(205, 20), TextAnchor.MiddleLeft, Muted);
            if (gridCellSelected)
            {
                int index = SourceCellIndex(selectedGridColumn, selectedGridRow);
                int timeIndex = DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow);
                int depthIndex = DisplayDepthBucketIndex(selectedGridColumn, selectedGridRow);
                string datasetName = selectedDataset != null
                    ? selectedDataset.Name : "ACTIVE DATASET";
                string timeLabel = selectedDataset != null &&
                    matrixTimes != null && matrixTimes.Length > 0
                        ? selectedDataset.GetTimeLabel(matrixTimes[Mathf.Clamp(
                            timeIndex, 0, matrixTimes.Length - 1)])
                        : "time bucket " + (timeIndex + 1);
                string depthLabel = matrixDepths != null && matrixDepths.Length > 0
                    ? matrixDepths[Mathf.Clamp(depthIndex, 0,
                        matrixDepths.Length - 1)].ToString()
                    : (depthIndex + 1).ToString();
                CreateText(panelContent,
                    "FINDINGS\n" +
                    CellLabel(selectedGridColumn, selectedGridRow).ToUpperInvariant() + "\n" +
                    "MIN  " + matrixMinimums[index].ToString("0.##") +
                    "    MEAN  " + matrixMeans[index].ToString("0.##") + "\n" +
                    "MAX  " + matrixMaximums[index].ToString("0.##") + "\n" +
                    datasetName + "\n" + timeLabel + "  /  z " + depthLabel,
                    11, FontStyle.Bold, new Vector2(400, -32), new Vector2(205, 208),
                    TextAnchor.UpperLeft, Ink);
            }
            else
            {
                FindDigestExtremes(out int minimumIndex, out int maximumIndex,
                    out int widestIndex);
                CreateText(panelContent, ActiveDigestHeadline(), 11,
                    FontStyle.Bold, new Vector2(400, 27),
                    new Vector2(205, 38), TextAnchor.UpperLeft,
                    currentDigest != null ? Green :
                    IsPending(PendingJob.Digest) ? Cyan : Amber);
                CreateText(panelContent,
                    ActiveDigestSummary(minimumIndex, maximumIndex,
                        widestIndex),
                    11, FontStyle.Normal, new Vector2(400, -70),
                    new Vector2(205, 146),
                    TextAnchor.UpperLeft, Ink);
            }
            CreateText(panelContent,
                gridStale ? "PREVIOUSLY INSPECTED" :
                inspected ? "SUPPORTED BY SOURCE" :
                evidenceLocalized ? "LOCAL PATTERN" :
                boundarySuspect ? "RECHECK BOUNDARY" :
                gridCellSelected ? (selectedCellPinned ? "PINNED" : "READY TO GROUND") :
                "SELECT A CELL TO GROUND",
                11, FontStyle.Bold,
                new Vector2(400, gridCellSelected ? -145 : -174),
                new Vector2(205, 22),
                TextAnchor.MiddleLeft,
                gridStale ? Muted : inspected ? Green : boundarySuspect ? Amber :
                selectedCellPinned ? Amber : Cyan);
            if (gridCellSelected)
                CreateButton(panelContent, "GROUND THIS CELL",
                    new Vector2(400, -184), new Vector2(205, 38),
                    Cyan, () => SelectS4DGridCell(
                        selectedGridColumn, selectedGridRow));
        }

        private void CreateDigestRow(string title, string detail, float y, Color color)
        {
            CreateText(panelContent, title, 12, FontStyle.Bold,
                new Vector2(400, y), new Vector2(205, 20), TextAnchor.MiddleLeft, color);
            CreateText(panelContent, detail, 11, FontStyle.Normal,
                new Vector2(400, y - 18), new Vector2(205, 18), TextAnchor.MiddleLeft, Muted);
        }

        private string ActiveDigestHeadline()
        {
            if (currentDigest != null &&
                !string.IsNullOrWhiteSpace(currentDigest.headline))
                return (currentDigest.generatedBy != null &&
                    currentDigest.generatedBy.StartsWith("llm:",
                        StringComparison.OrdinalIgnoreCase)
                        ? "AI GRID SUMMARY  /  "
                        : "EVIDENCE SUMMARY  /  ") + currentDigest.headline;
            if (IsPending(PendingJob.Digest))
                return "AI COMPARING ALL " +
                    Mathf.Max(1, activeGridColumns * activeGridRows) +
                    " MATPLOT PANELS...";
            if (!string.IsNullOrWhiteSpace(digestError))
                return "DETERMINISTIC FALLBACK  /  DIGEST SERVICE ERROR";
            return "DETERMINISTIC GRID COMPARISON";
        }

        private string ActiveDigestSummary(int minimumIndex, int maximumIndex,
            int widestIndex)
        {
            if (currentDigest != null &&
                !string.IsNullOrWhiteSpace(currentDigest.summary))
                return HumanizeDigestCellIds(currentDigest.summary);
            string fallback = DigestComparativeSummary(minimumIndex,
                maximumIndex, widestIndex);
            if (IsPending(PendingJob.Digest))
                return fallback + "\nSnapshot statistics remain visible while the " +
                    "asynchronous Digest is prepared.";
            if (!string.IsNullOrWhiteSpace(digestError))
                return fallback + "\nThe comparison remains evidence-only.";
            return fallback;
        }

        private string ActiveDigestNarrative(int minimumIndex, int maximumIndex,
            int widestIndex)
        {
            string narrative = ActiveDigestSummary(minimumIndex, maximumIndex,
                widestIndex);
            if (currentDigest == null || currentDigest.findings == null)
                return narrative;
            int count = Mathf.Min(2, currentDigest.findings.Length);
            for (int index = 0; index < count; index++)
            {
                if (!string.IsNullOrWhiteSpace(currentDigest.findings[index]))
                    narrative += "\n- " +
                        HumanizeDigestCellIds(currentDigest.findings[index]);
            }
            return narrative;
        }

        private static string CompactAiFinding(string value, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "No interpretation was returned.";
            string compact = value.Replace("\r", " ").Replace("\n", " ");
            while (compact.Contains("  "))
                compact = compact.Replace("  ", " ");
            compact = compact.Trim();
            if (compact.Length <= maximumLength)
                return compact;
            int sentenceEnd = compact.LastIndexOf('.', maximumLength - 1);
            if (sentenceEnd >= maximumLength / 2)
                return compact.Substring(0, sentenceEnd + 1);
            return compact.Substring(0, maximumLength - 3).TrimEnd() + "...";
        }

        private static string WrapAiFinding(string value, int lineLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "No interpretation was returned.";
            string compact = value.Replace("\r", " ").Replace("\n", " ");
            while (compact.Contains("  "))
                compact = compact.Replace("  ", " ");
            string[] words = compact.Trim().Split(' ');
            StringBuilder wrapped = new StringBuilder(compact.Length + 8);
            int currentLineLength = 0;
            for (int index = 0; index < words.Length; index++)
            {
                string word = words[index];
                if (currentLineLength > 0 &&
                    currentLineLength + 1 + word.Length > lineLength)
                {
                    wrapped.Append('\n');
                    currentLineLength = 0;
                }
                else if (currentLineLength > 0)
                {
                    wrapped.Append(' ');
                    currentLineLength++;
                }
                wrapped.Append(word);
                currentLineLength += word.Length;
            }
            return wrapped.ToString();
        }

        private string HumanizeDigestCellIds(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                activeTimeBuckets == null || activeDepthBuckets == null)
                return value;
            string result = value;
            for (int time = 0; time < activeTimeBuckets.Length; time++)
            {
                for (int depth = 0; depth < activeDepthBuckets.Length; depth++)
                {
                    string cellId = activeTimeBuckets[time].id + "__" +
                        activeDepthBuckets[depth].id;
                    string label = activeTimeBuckets[time].label + " / " +
                        activeDepthBuckets[depth].label;
                    result = result.Replace(cellId, label);
                }
            }
            return result;
        }

        private void ChangeDigestPage(int direction)
        {
            int columnPageCount = Mathf.Max(1,
                Mathf.CeilToInt(activeGridColumns / 3.0f));
            int rowPageCount = Mathf.Max(1,
                Mathf.CeilToInt(activeGridRows / 3.0f));
            int flatPage = digestRowPage * columnPageCount +
                digestColumnPage;
            int pageCount = columnPageCount * rowPageCount;
            flatPage = (flatPage + direction + pageCount) % pageCount;
            digestColumnPage = flatPage % columnPageCount;
            digestRowPage = flatPage / columnPageCount;
            BuildStage();
        }

        private void FindDigestExtremes(out int minimumIndex,
            out int maximumIndex, out int widestIndex)
        {
            // Work from the displayed grid rather than assuming that the
            // active cells occupy the first N source slots.  Filtering,
            // Pivot and transposition can all make that assumption false.
            List<int> visibleSourceIndices = new List<int>();
            for (int row = 0; row < Mathf.Max(1, activeGridRows); row++)
            {
                for (int column = 0; column < Mathf.Max(1, activeGridColumns);
                    column++)
                {
                    int sourceIndex = SourceCellIndex(column, row);
                    if (!visibleSourceIndices.Contains(sourceIndex))
                        visibleSourceIndices.Add(sourceIndex);
                }
            }

            if (visibleSourceIndices.Count == 0)
                visibleSourceIndices.Add(0);

            minimumIndex = visibleSourceIndices[0];
            maximumIndex = visibleSourceIndices[0];
            widestIndex = visibleSourceIndices[0];

            // Extreme-cell navigation is always derived from the immutable
            // numeric matrix loaded by Unity.  The LLM may summarize the
            // evidence, but it must never decide which cell a metric button
            // opens.  This also protects an active Unity session from stale
            // digest responses produced by an older backend process.
            //
            // The interval mean is the primary comparison.  Older snapshots
            // can contain identical/default means even though their ranges
            // differ, so fall back to the range midpoint and then maximum.
            // This keeps Highest and Lowest tied to meaningful, distinct
            // visible panels instead of both resolving to source slot zero.
            float meanSpread = StatisticSpread(visibleSourceIndices,
                matrixMeans);
            float midpointSpread = MidpointSpread(visibleSourceIndices);
            Func<int, float> score = meanSpread > 0.000001f
                ? new Func<int, float>(index => SafeStatistic(matrixMeans[index]))
                : midpointSpread > 0.000001f
                    ? new Func<int, float>(index =>
                        SafeStatistic((matrixMinimums[index] +
                            matrixMaximums[index]) * 0.5f))
                    : new Func<int, float>(index =>
                        SafeStatistic(matrixMaximums[index]));

            float minimumScore = score(minimumIndex);
            float maximumScore = score(maximumIndex);
            float widestRange = float.MinValue;
            for (int candidate = 0; candidate < visibleSourceIndices.Count;
                candidate++)
            {
                int index = visibleSourceIndices[candidate];
                float candidateScore = score(index);
                if (candidateScore < minimumScore)
                {
                    minimumScore = candidateScore;
                    minimumIndex = index;
                }
                if (candidateScore > maximumScore)
                {
                    maximumScore = candidateScore;
                    maximumIndex = index;
                }
                float range = SafeStatistic(matrixMaximums[index]) -
                    SafeStatistic(matrixMinimums[index]);
                if (range > widestRange)
                {
                    widestRange = range;
                    widestIndex = index;
                }
            }
        }

        private bool TrySourceIndexForCellId(string cellId, out int sourceIndex)
        {
            sourceIndex = 0;
            if (string.IsNullOrWhiteSpace(cellId) || activeTimeBuckets == null ||
                activeDepthBuckets == null)
                return false;
            for (int depth = 0; depth < activeDepthBuckets.Length; depth++)
            {
                for (int time = 0; time < activeTimeBuckets.Length; time++)
                {
                    string candidate = activeTimeBuckets[time].id + "__" +
                        activeDepthBuckets[depth].id;
                    if (!string.Equals(candidate, cellId,
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    sourceIndex = Mathf.Clamp(
                        depth * activeTimeBuckets.Length + time, 0,
                        matrixMeans.Length - 1);
                    return true;
                }
            }
            return false;
        }

        private void ApplyAuthoritativeCellStatistics(S4DFacetGridResult result)
        {
            if (result == null || result.CellStatistics == null)
                return;
            Array.Clear(matrixHasData, 0, matrixHasData.Length);
            Array.Clear(matrixValidFractions, 0, matrixValidFractions.Length);
            for (int item = 0; item < result.CellStatistics.Length; item++)
            {
                S4DCellStatistic statistic = result.CellStatistics[item];
                if (statistic == null ||
                    !TrySourceIndexForCellId(statistic.cellId, out int index))
                    continue;
                matrixMinimums[index] = statistic.minimum;
                matrixMeans[index] = statistic.mean;
                matrixMaximums[index] = statistic.maximum;
                // Coverage by itself is not proof that min/mean/max exist.
                // The service marks legacy coverage-only snapshots as empty.
                matrixHasData[index] = statistic.hasData ||
                    statistic.validCount > 0;
                matrixValidFractions[index] = statistic.validFraction;
            }
        }

        private static float SafeStatistic(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0.0f : value;
        }

        private static float StatisticSpread(List<int> indices, float[] values)
        {
            float minimum = float.MaxValue;
            float maximum = float.MinValue;
            for (int candidate = 0; candidate < indices.Count; candidate++)
            {
                int index = Mathf.Clamp(indices[candidate], 0, values.Length - 1);
                float value = SafeStatistic(values[index]);
                minimum = Mathf.Min(minimum, value);
                maximum = Mathf.Max(maximum, value);
            }
            return maximum - minimum;
        }

        private float MidpointSpread(List<int> indices)
        {
            float minimum = float.MaxValue;
            float maximum = float.MinValue;
            for (int candidate = 0; candidate < indices.Count; candidate++)
            {
                int index = Mathf.Clamp(indices[candidate], 0,
                    matrixMinimums.Length - 1);
                float value = SafeStatistic((matrixMinimums[index] +
                    matrixMaximums[index]) * 0.5f);
                minimum = Mathf.Min(minimum, value);
                maximum = Mathf.Max(maximum, value);
            }
            return maximum - minimum;
        }

        private string DigestTaskTag(int sourceIndex, int minimumIndex,
            int maximumIndex, int widestIndex)
        {
            sourceIndex = Mathf.Clamp(sourceIndex, 0, facetCellStale.Length - 1);
            if (facetCellStale[sourceIndex])
                return "STALE";
            if (facetCellBoundarySuspect[sourceIndex])
                return "SUSPECT";
            if (facetCellInspected[sourceIndex])
                return "VERIFIED";
            string task = (intentTask ?? string.Empty).ToLowerInvariant();
            if (task.Contains("anomal") && sourceIndex == widestIndex)
                return "ANOMALY";
            if (task.Contains("range"))
            {
                if (sourceIndex == maximumIndex)
                    return "HIGHEST";
                if (sourceIndex == minimumIndex)
                    return "LOWEST";
            }
            return sourceIndex == maximumIndex ? "HIGH" :
                sourceIndex == minimumIndex ? "LOW" : "COMPARED";
        }

        private string DigestComparativeSummary(int minimumIndex,
            int maximumIndex, int widestIndex)
        {
            return "Highest mean: " + DigestSourceCellLabel(maximumIndex) +
                "; lowest: " + DigestSourceCellLabel(minimumIndex) + ".\n" +
                "Widest range: " + DigestSourceCellLabel(widestIndex) +
                "; all cells use the same scale.";
        }

        private string DigestMetricSummary(int minimumIndex,
            int maximumIndex, int widestIndex)
        {
            minimumIndex = Mathf.Clamp(minimumIndex, 0,
                matrixMeans.Length - 1);
            maximumIndex = Mathf.Clamp(maximumIndex, 0,
                matrixMeans.Length - 1);
            widestIndex = Mathf.Clamp(widestIndex, 0,
                matrixMeans.Length - 1);
            float widestSpread = Mathf.Max(0.0f,
                matrixMaximums[widestIndex] - matrixMinimums[widestIndex]);
            return "HIGHEST CELL AVERAGE  " +
                matrixMeans[maximumIndex].ToString("0.###") +
                FindingUnitSuffix() +
                "\n" + DigestSourceCellLabel(maximumIndex).ToUpperInvariant() +
                "\n\nLOWEST CELL AVERAGE  " +
                matrixMeans[minimumIndex].ToString("0.###") +
                FindingUnitSuffix() +
                "\n" + DigestSourceCellLabel(minimumIndex).ToUpperInvariant() +
                "\n\nBIGGEST MIN-MAX SPREAD  " +
                widestSpread.ToString("0.###") + FindingUnitSuffix() +
                "\n" + DigestSourceCellLabel(widestIndex).ToUpperInvariant();
        }

        private string FindingUnitSuffix()
        {
            return selectedDataset != null &&
                !string.IsNullOrWhiteSpace(selectedDataset.Unit)
                    ? " " + selectedDataset.Unit
                    : " dataset-value";
        }

        private void StartDigestForNode(AnalysisNodeState node)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.jobId))
                return;
            node.digest = null;
            node.digestError = string.Empty;
            node.digestPending = true;
            if (node == currentAnalysisNode)
            {
                currentDigest = null;
                digestError = string.Empty;
                SetPending(PendingJob.Digest, true);
            }
            VolumeSTCubeS4DAnalysisClient digestClient =
                new VolumeSTCubeS4DAnalysisClient(s4dUrl, 90, 0.5f);
            StartCoroutine(digestClient.GenerateDigest(node.jobId,
                (digest, error) => OnDigestComplete(node, digest, error)));
        }
    }
}
