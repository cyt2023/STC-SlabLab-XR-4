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
    /// Guards for the hover hints. The hint texts are the only place that says
    /// what a control *does*, so they have to cover the controls the shell
    /// actually builds and they must not name a key the shortcut table has since
    /// moved somewhere else.
    /// </summary>
    public sealed class VolumeSTCubeHintTests
    {
        [Test]
        public void EveryControlThatNeedsExplainingHasAHint()
        {
            string[] controls =
            {
                "Previous", "Next", "Reset View", "Help", "History (2)",
                "Snapshot", "SET TIME RANGE", "CONFIRM TIME RANGE",
                "MATPLOT INTENT", "FULL MATRIX", "PLAY", "SPEED 1x", "BACK",
            };
            for (int index = 0; index < controls.Length; index++)
                Assert.IsNotNull(SlabLabHints.For(controls[index]),
                    controls[index] + " has no hint");
            // Mode switches and unknown captions explain themselves.
            Assert.IsNull(SlabLabHints.For("DESKTOP"));
            Assert.IsNull(SlabLabHints.For("VR"));
            Assert.IsNull(SlabLabHints.For(string.Empty));
            Assert.IsNull(SlabLabHints.For(null));
        }

        [Test]
        public void AHintCannotNameAKeyTheTableMoved()
        {
            // Each of these hints promises a key; the key table is the authority.
            Assert.IsTrue(SlabLabHints.For("Previous").Contains("Page Up"));
            Assert.AreEqual(SlabLabShortcut.PreviousStep,
                SlabLabShortcuts.Map(KeyCode.PageUp));
            Assert.IsTrue(SlabLabHints.For("Next").Contains("Page Down"));
            Assert.AreEqual(SlabLabShortcut.NextStep,
                SlabLabShortcuts.Map(KeyCode.PageDown));
            Assert.IsTrue(SlabLabHints.For("Reset View").Contains("X"));
            Assert.AreEqual(SlabLabShortcut.ResetView,
                SlabLabShortcuts.Map(KeyCode.X));
            Assert.IsTrue(SlabLabHints.For("Snapshot").Contains("P"));
            Assert.AreEqual(SlabLabShortcut.Snapshot,
                SlabLabShortcuts.Map(KeyCode.P));
            Assert.IsTrue(SlabLabHints.For("Help").Contains("H or F1"));
            Assert.AreEqual(SlabLabShortcut.ToggleHelp,
                SlabLabShortcuts.Map(KeyCode.H));
            Assert.AreEqual(SlabLabShortcut.ToggleHelp,
                SlabLabShortcuts.Map(KeyCode.F1));
            Assert.IsTrue(SlabLabHints.For("CONFIRM TIME RANGE").Contains("T"));
            Assert.AreEqual(SlabLabShortcut.RangeEntry,
                SlabLabShortcuts.Map(KeyCode.T));
        }

        [Test]
        public void TheHudShowsTheHintWhileThePointerRests()
        {
            Type hud = typeof(VolumeSTCubeFlatScreenHUD);
            Assert.IsNotNull(hud.GetMethod("UpdateHoverHint",
                BindingFlags.Instance | BindingFlags.NonPublic),
                "the hover entry point must exist");
            string source = File.ReadAllText(Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "VolumeSTCubeAPI/VolumeSTCubeFlatScreenHUD.cs")));
            StringAssert.Contains("UpdateHoverHint();", source,
                "the update loop must keep calling the hover check");
            // It reuses the notice line rather than adding geometry.
            StringAssert.Contains("noticeText.text = hint;", source);
        }

        [MenuItem("VolumeSTCube/Desktop/Validate Hints")]
        public static void RunChecks()
        {
            var tests = new VolumeSTCubeHintTests();
            tests.EveryControlThatNeedsExplainingHasAHint();
            tests.AHintCannotNameAKeyTheTableMoved();
            tests.TheHudShowsTheHintWhileThePointerRests();
            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath, "../../.runtime/hint-validation.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "PASS: 3 hint guards\n" +
                DateTime.UtcNow.ToString("O"));
            Debug.Log("PASS: 3 hint guards");
        }
    }
}
