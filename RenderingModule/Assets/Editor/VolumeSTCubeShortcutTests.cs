using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityVolumeRendering;

namespace UnityVolumeRendering.Tests
{
    /// <summary>
    /// Guards for the desktop keyboard shortcuts. The table is pure mapping, so
    /// these run in Edit mode in milliseconds: they pin the key table, the Help
    /// card wording, and the two rules that keep shortcuts from surprising an
    /// operator (they step through the same call the buttons use, and they are
    /// ignored while a text field owns the keyboard).
    /// </summary>
    public sealed class VolumeSTCubeShortcutTests
    {
        private const BindingFlags Private =
            BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void TheTableCoversEverythingTheHelpCardAdvertises()
        {
            Assert.AreEqual(SlabLabShortcut.PreviousStep,
                SlabLabShortcuts.Map(KeyCode.PageUp));
            Assert.AreEqual(SlabLabShortcut.NextStep,
                SlabLabShortcuts.Map(KeyCode.PageDown));
            Assert.AreEqual(SlabLabShortcut.ToggleHelp,
                SlabLabShortcuts.Map(KeyCode.H));
            Assert.AreEqual(SlabLabShortcut.ToggleHelp,
                SlabLabShortcuts.Map(KeyCode.F1));
            Assert.AreEqual(SlabLabShortcut.CloseOverlay,
                SlabLabShortcuts.Map(KeyCode.Escape));
            Assert.AreEqual(SlabLabShortcut.ResetView,
                SlabLabShortcuts.Map(KeyCode.X));
            Assert.AreEqual(SlabLabShortcut.ToggleSlabFrame,
                SlabLabShortcuts.Map(KeyCode.Y));
            Assert.AreEqual(SlabLabShortcut.Snapshot,
                SlabLabShortcuts.Map(KeyCode.P));
            Assert.AreEqual(SlabLabShortcut.None,
                SlabLabShortcuts.Map(KeyCode.A),
                "An ordinary letter must not be swallowed as a shortcut");

            // Every listed key must map somewhere, otherwise the HUD would poll
            // it forever without anything happening.
            for (int index = 0; index < SlabLabShortcuts.Keys.Length; index++)
                Assert.AreNotEqual(SlabLabShortcut.None,
                    SlabLabShortcuts.Map(SlabLabShortcuts.Keys[index]),
                    SlabLabShortcuts.Keys[index] + " is polled but unhandled");

            // The card tells the operator which keys work; keep that honest.
            Assert.IsTrue(
                SlabLabShortcuts.HelpLine.Contains("Page Up / Page Down"),
                "The Help card must name the step keys");
            Assert.IsTrue(SlabLabShortcuts.HelpLine.Contains("H or F1"),
                "The Help card must name the help keys");
            Assert.IsTrue(SlabLabShortcuts.HelpLine.Contains("Esc"),
                "The Help card must name the close key");
            // X and Y were handled by the runtime long before the card said so;
            // an undocumented recovery key is a recovery key nobody finds.
            Assert.IsTrue(SlabLabShortcuts.HelpLine.Contains("X: reset view"),
                "The Help card must name the reset-view key");
            Assert.IsTrue(SlabLabShortcuts.HelpLine.Contains("Y: slab frame"),
                "The Help card must name the slab-frame key");
            Assert.IsTrue(SlabLabShortcuts.HelpLine.Contains("P: snapshot"),
                "The Help card must name the snapshot key");
        }

        [Test]
        public void StepKeysDriveTheSameNavigationAsTheButtons()
        {
            GameObject root = new GameObject("Shortcut regression");
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
                ((IList)type.GetField("datasets", Private).GetValue(workbench))
                    .Add(new VolumeSTCubeSliceDataset());
                type.GetField("authorBoundaryConfirmed", Private)
                    .SetValue(workbench, true);
                SetStage(workbench, "Slab");

                Assert.IsTrue(workbench.DesktopHandleShortcut(KeyCode.PageUp),
                    "The previous-step key must be handled");
                Assert.AreEqual("Field", StageName(workbench));
                Assert.IsTrue(spatial.activeSelf,
                    "Step 2 must show the field the arrow went back to");
                Assert.IsFalse(axis.activeSelf,
                    "The tri-axis must not follow the operator back to Step 2");

                Assert.IsTrue(workbench.DesktopHandleShortcut(KeyCode.PageDown));
                Assert.AreEqual("Slab", StageName(workbench));

                Assert.IsFalse(workbench.DesktopHandleShortcut(KeyCode.A),
                    "A key outside the table must not be consumed");
                Assert.AreEqual("Slab", StageName(workbench));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TypingOwnsTheKeyboard()
        {
            GameObject root = new GameObject("Shortcut typing regression");
            root.SetActive(false);
            try
            {
                var workbench = root.AddComponent<
                    VolumeSTCubeQuestSpatialWorkbench>();
                Type type = workbench.GetType();
                ((IList)type.GetField("datasets", Private).GetValue(workbench))
                    .Add(new VolumeSTCubeSliceDataset());
                type.GetField("authorBoundaryConfirmed", Private)
                    .SetValue(workbench, true);
                SetStage(workbench, "Field");

                SetTextInputActive(workbench, true);
                Assert.IsFalse(
                    workbench.DesktopHandleShortcut(KeyCode.PageDown),
                    "A shortcut must be ignored while a text field is open");
                Assert.AreEqual("Field", StageName(workbench),
                    "Typing must not step the workflow");

                SetTextInputActive(workbench, false);
                Assert.IsTrue(workbench.DesktopHandleShortcut(KeyCode.PageDown));
                Assert.AreEqual("Slab", StageName(workbench));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // ----------------------------------------------------------- helpers
        private static void SetStage(
            VolumeSTCubeQuestSpatialWorkbench workbench, string name)
        {
            FieldInfo stage = workbench.GetType().GetField("stage", Private);
            stage.SetValue(workbench, Enum.Parse(stage.FieldType, name));
        }

        private static string StageName(
            VolumeSTCubeQuestSpatialWorkbench workbench)
        {
            return workbench.GetType().GetField("stage", Private)
                .GetValue(workbench).ToString();
        }

        /// <summary>
        /// The input flags live in a private struct, so it has to be read out,
        /// changed and written back.
        /// </summary>
        private static void SetTextInputActive(
            VolumeSTCubeQuestSpatialWorkbench workbench, bool value)
        {
            FieldInfo input = workbench.GetType().GetField("input", Private);
            object boxed = input.GetValue(workbench);
            input.FieldType.GetField("textInputActive")
                .SetValue(boxed, value);
            input.SetValue(workbench, boxed);
        }

        [MenuItem("VolumeSTCube/Desktop/Validate Shortcuts")]
        public static void RunChecks()
        {
            var tests = new VolumeSTCubeShortcutTests();
            tests.TheTableCoversEverythingTheHelpCardAdvertises();
            tests.StepKeysDriveTheSameNavigationAsTheButtons();
            tests.TypingOwnsTheKeyboard();
            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath, "../../.runtime/shortcut-validation.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "PASS: 3 shortcut guards\n" +
                DateTime.UtcNow.ToString("O"));
            Debug.Log("PASS: 3 shortcut guards");
        }
    }
}
