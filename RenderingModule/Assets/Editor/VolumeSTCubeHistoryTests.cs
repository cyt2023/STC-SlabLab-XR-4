using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UnityVolumeRendering.Tests
{
    public sealed class VolumeSTCubeHistoryTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void CommittedAnalysisOwnsItsQuestionAndBucketIndices()
        {
            GameObject root = new GameObject("History regression");
            root.SetActive(false);
            try
            {
                var workbench = root.AddComponent<VolumeSTCubeQuestSpatialWorkbench>();
                Type type = workbench.GetType();
                var buckets = new[] { new S4DIndexBucketRequest
                    { id = "t0", label = "First", indices = new[] { 1, 2 } } };
                type.GetField("activeTimeBuckets", Private).SetValue(workbench, buckets);
                type.GetField("analysisQuestion", Private).SetValue(workbench, "Original question");
                object node = type.GetMethod("CommitAnalysisNode", Private).Invoke(workbench, new object[] { null });
                buckets[0].indices[0] = 99;
                buckets[0].label = "Changed";
                type.GetField("analysisQuestion", Private).SetValue(workbench, "Different question");
                var saved = (S4DIndexBucketRequest[])node.GetType().GetField("timeBuckets").GetValue(node);
                Assert.AreEqual(1, saved[0].indices[0]);
                Assert.AreEqual("First", saved[0].label);
                Assert.AreEqual("Original question", node.GetType().GetField("analysisQuestion").GetValue(node));
                Assert.AreEqual(1, workbench.DesktopAnalysisCount);
                Assert.IsFalse(workbench.DesktopHistoryBusy);
                type.GetMethod("ResetAnalysisWorkingView", Private).Invoke(workbench, null);
                Assert.AreEqual(1, workbench.DesktopAnalysisCount,
                    "Changing the working variable must preserve committed history");
                Assert.IsNull(type.GetField("currentAnalysisNode", Private).GetValue(workbench));
                Assert.AreEqual("Original question", node.GetType().GetField("analysisQuestion").GetValue(node));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void RestoreCopiesBucketsAgain()
        {
            MethodInfo copy = typeof(VolumeSTCubeQuestSpatialWorkbench).GetMethod(
                "CopyAnalysisBuckets", BindingFlags.Static | BindingFlags.NonPublic);
            var snapshot = new[] { new S4DIndexBucketRequest
                { id = "z0", label = "Depth", variableId = "v", indices = new[] { 3 } } };
            var restored = (S4DIndexBucketRequest[])copy.Invoke(null, new object[] { snapshot });
            restored[0].indices[0] = 8;
            Assert.AreEqual(3, snapshot[0].indices[0]);
            Assert.AreEqual("v", restored[0].variableId);
            Assert.IsNull(copy.Invoke(null, new object[] { null }));
        }

        [Test]
        public void LiveTimeRangeShowsBothDraggableCuts()
        {
            GameObject root = new GameObject("Live time range regression");
            GameObject combinedBoundaries = new GameObject("Boundaries");
            GameObject interaction = new GameObject("Interaction");
            GameObject preview = new GameObject("Preview");
            GameObject cutA = new GameObject("Cut A");
            GameObject cutB = new GameObject("Cut B");
            try
            {
                var companion = root.AddComponent<VolumeSTCubeForVrXytCompanion>();
                Type type = companion.GetType();
                type.GetField("singleLiveSource", Private).SetValue(companion, true);
                type.GetField("showAllEvents", Private).SetValue(companion, true);
                type.GetField("combinedEventBoundariesRoot", Private)
                    .SetValue(companion, combinedBoundaries);
                type.GetField("combinedTimeInteractionRoot", Private)
                    .SetValue(companion, interaction);
                type.GetField("selectedSlicePreviewRoot", Private)
                    .SetValue(companion, preview);
                var cuts = (GameObject[])type.GetField(
                    "combinedTimeSelectorRoots", Private).GetValue(companion);
                cuts[0] = cutA;
                cuts[1] = cutB;
                combinedBoundaries.SetActive(false);
                interaction.SetActive(false);
                preview.SetActive(false);
                cutA.SetActive(false);
                cutB.SetActive(false);

                companion.OpenAllEventsTimeSelection();

                Assert.IsTrue(combinedBoundaries.activeSelf);
                Assert.IsTrue(interaction.activeSelf);
                Assert.IsTrue(cutA.activeSelf);
                Assert.IsTrue(cutB.activeSelf);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(combinedBoundaries);
                UnityEngine.Object.DestroyImmediate(interaction);
                UnityEngine.Object.DestroyImmediate(preview);
                UnityEngine.Object.DestroyImmediate(cutA);
                UnityEngine.Object.DestroyImmediate(cutB);
            }
        }

        [Test]
        public void PreviousHidesAxisAndIndependentFieldByStage()
        {
            GameObject root = new GameObject("Previous navigation regression");
            GameObject spatial = new GameObject("Spatial field");
            GameObject axis = new GameObject("Tri-axis");
            GameObject independent = new GameObject("Independent XYT field");
            root.SetActive(false);
            try
            {
                spatial.transform.SetParent(root.transform, false);
                axis.transform.SetParent(root.transform, false);
                var workbench = root.AddComponent<
                    VolumeSTCubeQuestSpatialWorkbench>();
                var player = spatial.AddComponent<VolumeSTCubeForVrSurfacePlayer>();
                var companion = spatial.AddComponent<VolumeSTCubeForVrXytCompanion>();
                Type playerType = player.GetType();
                playerType.GetField("xytCompanion", Private)
                    .SetValue(player, companion);
                companion.GetType().GetField("fieldRoot", Private)
                    .SetValue(companion, independent);

                Type type = workbench.GetType();
                type.GetField("spatialRoot", Private).SetValue(workbench, spatial);
                type.GetField("spatialAxisComposerRoot", Private)
                    .SetValue(workbench, axis);
                type.GetField("forVrSurfacePlayer", Private)
                    .SetValue(workbench, player);
                // A session only reaches Step 3 with imported data: without a
                // dataset NavigateDesktopStep deliberately refuses to open Step
                // 2 (that contract has its own test below).
                ((IList)type.GetField("datasets", Private).GetValue(workbench))
                    .Add(new VolumeSTCubeSliceDataset());
                FieldInfo stage = type.GetField("stage", Private);
                stage.SetValue(workbench, Enum.Parse(stage.FieldType, "Slab"));

                workbench.DesktopPreviousStep();
                Assert.IsTrue(spatial.activeSelf);
                Assert.IsFalse(axis.activeSelf,
                    "The tri-axis must not remain in Step 2");
                Assert.IsTrue(independent.activeSelf);

                workbench.DesktopPreviousStep();
                Assert.IsFalse(spatial.activeSelf);
                Assert.IsFalse(independent.activeSelf,
                    "The independent XYT field must not remain in Step 1");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(independent);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PreviousWithoutADatasetStaysOnTheImportStep()
        {
            GameObject root = new GameObject("Empty workspace navigation regression");
            GameObject spatial = new GameObject("Spatial field");
            GameObject axis = new GameObject("Tri-axis");
            root.SetActive(false);
            try
            {
                spatial.transform.SetParent(root.transform, false);
                axis.transform.SetParent(root.transform, false);
                var workbench = root.AddComponent<
                    VolumeSTCubeQuestSpatialWorkbench>();
                Type type = workbench.GetType();
                type.GetField("spatialRoot", Private).SetValue(workbench, spatial);
                type.GetField("spatialAxisComposerRoot", Private)
                    .SetValue(workbench, axis);
                FieldInfo stage = type.GetField("stage", Private);
                stage.SetValue(workbench, Enum.Parse(stage.FieldType, "Slab"));

                workbench.DesktopPreviousStep();

                Assert.AreEqual("DatasetImport", stage.GetValue(workbench).ToString(),
                    "Going back without a dataset must return to the import step");
                Assert.IsFalse(spatial.activeSelf,
                    "The workspace must not show a field without a dataset");
                Assert.IsFalse(axis.activeSelf,
                    "The tri-axis must not show without a dataset");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SlabPreviewCountsAsTheDesktopComposerPanel()
        {
            GameObject root = new GameObject("Composer layout regression");
            GameObject previewObject = new GameObject(
                "Source preview", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workbench = root.AddComponent<
                    VolumeSTCubeQuestSpatialWorkbench>();
                Type type = workbench.GetType();
                FieldInfo stage = type.GetField("stage", Private);
                stage.SetValue(workbench, Enum.Parse(stage.FieldType, "Slab"));
                Canvas preview = previewObject.GetComponent<Canvas>();
                type.GetField("slabPreviewCanvas", Private)
                    .SetValue(workbench, preview);
                previewObject.SetActive(true);

                Assert.IsTrue(workbench.DesktopComposerPanelActive,
                    "The preview must replace the axis in the desktop task lane");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(previewObject);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The packaged app starts its bundled backend in the same launch that
        /// opens the opening screen, so the first registration can lose that
        /// race. The workspace has to keep asking on its own — an operator who
        /// leaves the app open should never have to press Retry because the
        /// service was a few seconds late (WatchWaveImport, driven from the
        /// desktop update loop).
        /// </summary>
        [Test]
        public void TheOpeningStepKeepsAskingForDataByItself()
        {
            Type type = typeof(VolumeSTCubeQuestSpatialWorkbench);
            Assert.IsNotNull(type.GetMethod("WatchWaveImport",
                BindingFlags.Instance | BindingFlags.NonPublic),
                "the self-heal entry point must exist");
            FieldInfo retries = type.GetField("MaxWaveAutoRetries",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(retries, "the quick-retry budget must exist");
            Assert.Greater((int)retries.GetRawConstantValue(), 0);
            // It is only self-healing if something calls it: the desktop update
            // loop owns that call, so a rename there must not silently drop it.
            string interaction = File.ReadAllText(Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "VolumeSTCubeAPI/VolumeSTCubeQuestSpatialWorkbench.cs")));
            StringAssert.Contains("WatchWaveImport();", interaction,
                "the update loop must keep calling the self-heal");
        }

        [Test]
        public void FullMatrixLeavesIntentBeforeManifestResolution()
        {
            VolumeSTCubeMode.SetStartupPreference(
                VolumeSTCubeApplicationMode.Desktop);
            GameObject root = new GameObject("Matrix transition regression");
            GameObject panelObject = new GameObject(
                "Progress", typeof(RectTransform), typeof(Canvas));
            GameObject intentObject = new GameObject(
                "Intent", typeof(RectTransform), typeof(Canvas));
            GameObject gridObject = new GameObject(
                "Pending grid", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workbench = root.AddComponent<
                    VolumeSTCubeQuestSpatialWorkbench>();
                Type type = workbench.GetType();
                FieldInfo stage = type.GetField("stage", Private);
                stage.SetValue(workbench, Enum.Parse(stage.FieldType, "Slab"));
                FieldInfo workflow = type.GetField(
                    "spatialWorkflowStep", Private);
                workflow.SetValue(workbench, Enum.Parse(
                    workflow.FieldType, "SourcePreviewReady"));
                type.GetField("panelCanvas", Private).SetValue(
                    workbench, panelObject.GetComponent<Canvas>());
                type.GetField("intentCanvas", Private).SetValue(
                    workbench, intentObject.GetComponent<Canvas>());
                type.GetField("facetGridCanvas", Private).SetValue(
                    workbench, gridObject.GetComponent<Canvas>());
                panelObject.SetActive(false);
                intentObject.SetActive(true);
                gridObject.SetActive(true);

                type.GetMethod("ConfirmGridPlacement", Private).Invoke(
                    workbench, null);

                Assert.AreEqual("Matrix", stage.GetValue(workbench).ToString());
                Assert.IsTrue(panelObject.activeSelf);
                Assert.IsFalse(intentObject.activeSelf,
                    "Intent must close before manifest resolution starts");
                Assert.IsFalse(gridObject.activeSelf,
                    "The pending grid must not remain behind Intent");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gridObject);
                UnityEngine.Object.DestroyImmediate(intentObject);
                UnityEngine.Object.DestroyImmediate(panelObject);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [MenuItem("VolumeSTCube/Desktop/Validate History")]
        public static void RunChecks()
        {
            var tests = new VolumeSTCubeHistoryTests();
            tests.CommittedAnalysisOwnsItsQuestionAndBucketIndices();
            tests.RestoreCopiesBucketsAgain();
            tests.LiveTimeRangeShowsBothDraggableCuts();
            tests.PreviousHidesAxisAndIndependentFieldByStage();
            tests.PreviousWithoutADatasetStaysOnTheImportStep();
            tests.TheOpeningStepKeepsAskingForDataByItself();
            tests.SlabPreviewCountsAsTheDesktopComposerPanel();
            tests.FullMatrixLeavesIntentBeforeManifestResolution();
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.runtime/history-validation.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "PASS: 8 workflow regressions\n" + DateTime.UtcNow.ToString("O"));
            Debug.Log("PASS: 8 workflow regressions");
        }
    }
}
