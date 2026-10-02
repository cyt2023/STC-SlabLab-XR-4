using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityVolumeRendering;

namespace UnityVolumeRendering.Tests
{
    /// <summary>
    /// Guards for the typed Time-range entry. The point of these is that the
    /// typed path is not a second set of rules: it must clamp exactly like a
    /// dragged cut and produce exactly the readout the drag produces.
    /// </summary>
    public sealed class VolumeSTCubeBoundaryEntryTests
    {
        private const BindingFlags Private =
            BindingFlags.Instance | BindingFlags.NonPublic;
        private const int Frames = 24;

        [Test]
        public void TypedCutDaysReachTheSameReadoutAsADrag()
        {
            Assert.IsTrue(SlabLabBoundaryEntry.TryParseTimeCuts("9,20", Frames,
                out int start, out int end));
            Assert.AreEqual(9, start);
            Assert.AreEqual(19, end,
                "The second typed day is inclusive, so the internal cut is one " +
                "lower — the same value a drag to that boundary produces");

            GameObject root = new GameObject("Typed range readout");
            root.SetActive(false);
            try
            {
                var workbench = root.AddComponent<
                    VolumeSTCubeQuestSpatialWorkbench>();
                Type type = workbench.GetType();
                type.GetField("selectedDataset", Private)
                    .SetValue(workbench, SliceDataset(Frames));
                type.GetField("timeBoundaryStart", Private)
                    .SetValue(workbench, start);
                type.GetField("timeBoundaryEnd", Private)
                    .SetValue(workbench, end);
                string summary = type.GetMethod("TimeRangeSummary", Private)
                    .Invoke(workbench, null) as string;
                Assert.AreEqual("before 1-9  /  during 10-20  /  after 21-24",
                    summary,
                    "The typed days must appear in the readout unchanged");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TypedCutsAreClampedLikeADraggedCut()
        {
            Assert.IsTrue(SlabLabBoundaryEntry.TryParseTimeCuts("0,99", Frames,
                out int low, out int high));
            Assert.AreEqual(1, low);
            Assert.AreEqual(22, high,
                "Out-of-range days clamp to the last frame, never past it");

            Assert.IsTrue(SlabLabBoundaryEntry.TryParseTimeCuts("19,9", Frames,
                out int reversedStart, out int reversedEnd));
            Assert.AreEqual(9, reversedStart,
                "The lower day is the first cut whichever order it is typed in");
            Assert.AreEqual(18, reversedEnd);

            // Rubbish must be rejected instead of silently moving a cut.
            foreach (string text in new[] { "", "9", "abc", "9,10,11", "9," })
                Assert.IsFalse(SlabLabBoundaryEntry.TryParseTimeCuts(text,
                    Frames, out _, out _), "should reject " + text);
            Assert.IsFalse(SlabLabBoundaryEntry.TryParseTimeCuts("1,2", 1,
                out _, out _), "a one-frame dataset has no range to type");
        }

        [Test]
        public void OnlyATimeRangeOpensTheEntry()
        {
            GameObject root = new GameObject("Typed range entry");
            root.SetActive(false);
            try
            {
                var workbench = root.AddComponent<
                    VolumeSTCubeQuestSpatialWorkbench>();
                Type type = workbench.GetType();
                type.GetField("selectedDataset", Private)
                    .SetValue(workbench, SliceDataset(Frames));
                FieldInfo stage = type.GetField("stage", Private);
                stage.SetValue(workbench, Enum.Parse(stage.FieldType, "Field"));
                type.GetField("boundaryEditActive", Private)
                    .SetValue(workbench, true);
                FieldInfo dimension = type.GetField("boundaryDimension", Private);
                FieldInfo input = type.GetField("input", Private);

                // Depth is not a typed range in this app, so it must refuse and
                // leave the keyboard alone.
                SetStructField(input, workbench, "boundaryRangeEntry", false);
                dimension.SetValue(workbench,
                    Enum.Parse(dimension.FieldType, "Depth"));
                Assert.IsFalse(workbench.DesktopRangeEntry(),
                    "Depth must not open the Time-range entry");
                Assert.IsFalse((bool)GetStructField(input, workbench,
                    "boundaryRangeEntry"), "A refusal must not take the keyboard");

                dimension.SetValue(workbench,
                    Enum.Parse(dimension.FieldType, "Time"));
                Assert.IsTrue(workbench.DesktopRangeEntry());
                Assert.IsTrue((bool)GetStructField(input, workbench,
                    "boundaryRangeEntry"));
                Assert.IsTrue((bool)GetStructField(input, workbench,
                    "textInputActive"),
                    "The entry owns the keyboard while it is open");

                // Esc leaves the cuts exactly where they were.
                int before = (int)type.GetField("timeBoundaryStart", Private)
                    .GetValue(workbench);
                workbench.DesktopCancelRangeEntry();
                Assert.AreEqual(before, (int)type.GetField("timeBoundaryStart",
                    Private).GetValue(workbench));
                Assert.IsFalse((bool)GetStructField(input, workbench,
                    "boundaryRangeEntry"));

                // Enter applies through the same clamps.
                // A typo keeps the entry open instead of moving a cut. (The happy
                // path redraws the 3D rig and the boundary bar, so it is proven
                // in Play Mode where those objects exist; here it is the rules
                // that are under test.)
                Assert.IsTrue(workbench.DesktopRangeEntry());
                type.GetField("boundaryRangeEntryText", Private)
                    .SetValue(workbench, "oops");
                workbench.DesktopApplyRangeEntry();
                Assert.AreEqual(before, (int)type.GetField("timeBoundaryStart",
                    Private).GetValue(workbench),
                    "A typo must not move the cuts");
                Assert.IsTrue((bool)GetStructField(input, workbench,
                    "boundaryRangeEntry"), "A typo keeps the entry open");
                workbench.DesktopCancelRangeEntry();
                Assert.IsFalse((bool)GetStructField(input, workbench,
                    "boundaryRangeEntry"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // ----------------------------------------------------------- helpers
        private static VolumeSTCubeSliceDataset SliceDataset(int frames)
        {
            var dataset = new VolumeSTCubeSliceDataset();
            // TimeCount is derived from the frame list, so that is what a
            // dataset with N frames means here.
            typeof(VolumeSTCubeSliceDataset)
                .GetProperty("RawPaths",
                    BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic)
                .SetValue(dataset, new string[frames]);
            return dataset;
        }

        private static object GetStructField(FieldInfo input,
            VolumeSTCubeQuestSpatialWorkbench workbench, string name)
        {
            object boxed = input.GetValue(workbench);
            return input.FieldType.GetField(name).GetValue(boxed);
        }

        private static void SetStructField(FieldInfo input,
            VolumeSTCubeQuestSpatialWorkbench workbench, string name,
            object value)
        {
            object boxed = input.GetValue(workbench);
            input.FieldType.GetField(name).SetValue(boxed, value);
            input.SetValue(workbench, boxed);
        }

        [MenuItem("VolumeSTCube/Desktop/Validate Range Entry")]
        public static void RunChecks()
        {
            var tests = new VolumeSTCubeBoundaryEntryTests();
            tests.TypedCutDaysReachTheSameReadoutAsADrag();
            tests.TypedCutsAreClampedLikeADraggedCut();
            tests.OnlyATimeRangeOpensTheEntry();
            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath, "../../.runtime/range-entry-validation.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "PASS: 3 range-entry guards\n" +
                DateTime.UtcNow.ToString("O"));
            Debug.Log("PASS: 3 range-entry guards");
        }
    }
}
