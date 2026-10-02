using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityVolumeRendering;

namespace UnityVolumeRendering.Tests
{
    /// <summary>
    /// Guards the invariants the refactor relies on: the layout numbers, the
    /// PlayerPrefs keys/defaults and the derived workflow phase. These run
    /// without entering Play Mode, so a change to any of them fails here first.
    /// </summary>
    public sealed class VolumeSTCubeRefactorTests
    {
        private const BindingFlags Private =
            BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags StaticPrivate =
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        private static Type TypeOf(string name)
        {
            Type type = typeof(VolumeSTCubeQuestSpatialWorkbench)
                .Assembly.GetType("UnityVolumeRendering." + name);
            Assert.IsNotNull(type, name + " not found in the runtime assembly");
            return type;
        }

        private static string Const(Type type, string name)
        {
            FieldInfo field = type.GetField(name, StaticPrivate);
            Assert.IsNotNull(field, name + " is missing");
            return Convert.ToString(field.GetRawConstantValue());
        }

        [Test]
        public void LayoutConstantsKeepTheirDocumentedValues()
        {
            Type layout = TypeOf("SlabLabLayout");
            var expected = new Dictionary<string, string>
            {
                { "FieldPairCentreShiftRight", "0.12" },
                { "FieldPairLiftUp", "0.22" },
                { "FieldPairCompactScale", "0.78" },
                { "AxisDockViewportX", "0.52" },
                { "AxisDockViewportY", "0.5" },
                { "AxisDockScale", "1.38" },
                { "AxisBodyScale", "1.3" },
                { "FieldPresentationScale", "1.15" },
            };
            foreach (KeyValuePair<string, string> entry in expected)
            {
                FieldInfo field = layout.GetField(entry.Key, StaticPrivate);
                Assert.IsNotNull(field, entry.Key + " is missing");
                float value = (float)field.GetRawConstantValue();
                Assert.AreEqual(float.Parse(entry.Value), value, 0.0001f,
                    entry.Key + " changed value");
            }
            var sizes = new Dictionary<string, Vector2>
            {
                { "ButtonSizeStandard", new Vector2(210, 42) },
                { "ButtonSizeMedium", new Vector2(190, 42) },
                { "BoundaryActionButtonSize", new Vector2(300, 54) },
                { "RoleButtonSize", new Vector2(132, 36) },
                { "StepperButtonSize", new Vector2(130, 26) },
                { "RollupGroupButtonSize", new Vector2(48, 30) },
                { "RowChipSize", new Vector2(150, 28) },
                { "PanelCardSizeLarge", new Vector2(900, 440) },
                { "PanelCanvasSize", new Vector2(1120f, 840f) },
                { "AxisMarkerDotSize", new Vector2(14, 14) },
            };
            foreach (KeyValuePair<string, Vector2> entry in sizes)
            {
                FieldInfo field = layout.GetField(entry.Key,
                    BindingFlags.Public | BindingFlags.Static);
                Assert.IsNotNull(field, entry.Key + " is missing");
                var value = (Vector2)field.GetValue(null);
                Assert.AreEqual(entry.Value.x, value.x, 0.0001f,
                    entry.Key + ".x changed");
                Assert.AreEqual(entry.Value.y, value.y, 0.0001f,
                    entry.Key + ".y changed");
            }
        }

        [Test]
        public void SettingsKeysAndDefaultsAreStable()
        {
            Type settings = TypeOf("SlabLabSettings");
            var expected = new Dictionary<string, string>
            {
                { "SpatialPromptKey", "VolumeSTCube.Quest.SpatialPrompt" },
                { "QuestPromptKey", "VolumeSTCube.Quest.Prompt" },
                { "BarPromptMigrationKey", "VolumeSTCube.Quest.BarPromptMigrationV2" },
                { "MatPlotUrlKey", "VolumeSTCube.Quest.MatPlotUrl" },
                { "WaveUrlKey", "VolumeSTCube.Quest.S4DUrl" },
                { "DatasetRootKey", "VolumeSTCube.Quest.DatasetRoot" },
                { "WaveStartUtcKey", "VolumeSTCube.Wave.StartUtc" },
                { "WaveEndUtcKey", "VolumeSTCube.Wave.EndUtc" },
                { "RuntimeApplicationModeKey", "VolumeSTCube.SlabLabRuntimeApplicationMode" },
            };
            foreach (KeyValuePair<string, string> entry in expected)
            {
                Assert.AreEqual(entry.Value, Const(settings, entry.Key),
                    entry.Key + " changed key");
            }
            Assert.AreEqual("2019-11-30T00:00:00Z",
                Const(settings, "DefaultWaveStartUtc"));
            Assert.AreEqual("2019-12-01T00:00:00Z",
                Const(settings, "DefaultWaveEndUtc"));
        }

        [Test]
        public void WorkbenchNeverTouchesPlayerPrefsDirectly()
        {
            string folder = Path.Combine(Application.dataPath, "VolumeSTCubeAPI");
            string[] files = Directory.GetFiles(folder,
                "VolumeSTCubeQuestSpatialWorkbench*.cs");
            Assert.Greater(files.Length, 0, "workbench sources not found");
            foreach (string file in files)
            {
                string[] lines = File.ReadAllLines(file);
                for (int index = 0; index < lines.Length; index++)
                {
                    string line = lines[index];
                    int code = line.IndexOf("PlayerPrefs.",
                        StringComparison.Ordinal);
                    if (code < 0)
                        continue;
                    int comment = line.IndexOf("//", StringComparison.Ordinal);
                    Assert.IsTrue(comment >= 0 && comment < code,
                        Path.GetFileName(file) + ":" + (index + 1) +
                        " uses PlayerPrefs directly; use SlabLabSettings");
                }
            }
        }

        [Test]
        public void PhaseIsDerivedFromTheExistingFlags()
        {
            GameObject root = new GameObject("Phase regression");
            root.SetActive(false);
            try
            {
                var workbench = root.AddComponent<VolumeSTCubeQuestSpatialWorkbench>();
                Type type = workbench.GetType();
                FieldInfo stage = type.GetField("stage", Private);

                Assert.AreEqual("ImportConnecting", workbench.CurrentPhase.ToString());

                stage.SetValue(workbench, Enum.Parse(stage.FieldType, "DatasetImport"));
                type.GetField("datasetImportConfirmed", Private)
                    .SetValue(workbench, true);
                Assert.AreEqual("ImportReady", workbench.CurrentPhase.ToString());

                stage.SetValue(workbench, Enum.Parse(stage.FieldType, "Field"));
                type.GetField("datasetImportConfirmed", Private)
                    .SetValue(workbench, false);
                type.GetField("authorBoundaryConfirmed", Private)
                    .SetValue(workbench, true);
                Assert.AreEqual("FieldReady", workbench.CurrentPhase.ToString());

                type.GetField("boundaryEditActive", Private)
                    .SetValue(workbench, true);
                Assert.AreEqual("FieldBoundaryAuthoring",
                    workbench.CurrentPhase.ToString());
                type.GetField("boundaryEditActive", Private)
                    .SetValue(workbench, false);

                stage.SetValue(workbench, Enum.Parse(stage.FieldType, "Slab"));
                FieldInfo step = type.GetField("spatialWorkflowStep", Private);
                step.SetValue(workbench, Enum.Parse(step.FieldType, "AxisBinding"));
                Assert.AreEqual("SlabAxisBinding", workbench.CurrentPhase.ToString());
                step.SetValue(workbench, Enum.Parse(step.FieldType, "Intent"));
                Assert.AreEqual("SlabIntent", workbench.CurrentPhase.ToString());
                step.SetValue(workbench, Enum.Parse(step.FieldType, "Materializing"));
                Assert.AreEqual("SlabMaterializing",
                    workbench.CurrentPhase.ToString());
                step.SetValue(workbench,
                    Enum.Parse(step.FieldType, "SourcePreviewReady"));
                Assert.AreEqual("SlabPreviewReady",
                    workbench.CurrentPhase.ToString());

                stage.SetValue(workbench, Enum.Parse(stage.FieldType, "Matrix"));
                Assert.AreEqual("MatrixBuilding", workbench.CurrentPhase.ToString());
                type.GetField("gridCellSelected", Private)
                    .SetValue(workbench, true);
                Assert.AreEqual("MatrixCellPicked",
                    workbench.CurrentPhase.ToString());

                stage.SetValue(workbench, Enum.Parse(stage.FieldType, "Analyze"));
                Assert.AreEqual("AnalyzeReady", workbench.CurrentPhase.ToString());
                FieldInfo pendingJobs = type.GetField("pendingJobs", Private);
                pendingJobs.SetValue(workbench, Enum.Parse(
                    pendingJobs.FieldType, "Analysis"));
                Assert.AreEqual("AnalyzeRunning", workbench.CurrentPhase.ToString());

                stage.SetValue(workbench, Enum.Parse(stage.FieldType, "Result"));
                Assert.AreEqual("ResultReady", workbench.CurrentPhase.ToString());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void StateStructsMatchTheRetiredFlags()
        {
            Type type = typeof(VolumeSTCubeQuestSpatialWorkbench);
            var groups = new[]
            {
                new
                {
                    Field = "interaction",
                    Struct = "InteractionState",
                    Members = new[]
                    {
                        "draggingSlab", "regionDragging", "timeBoundaryDragging",
                        "depthBoundaryDragging", "depthInspectionActive",
                        "drawingRegion", "draftPivotPreviewDragging",
                        "draggedAxisSawTriggerHeld",
                        "draggedAxisUsesDesktopPointer",
                        "draggedPaletteSawTriggerHeld",
                        "draggedPaletteUsesDesktopPointer",
                    },
                },
                new
                {
                    Field = "input",
                    Struct = "InputState",
                    Members = new[]
                    {
                        "textInputActive", "keyboardInputWasVoice",
                        "vrKeyboardVisible", "voiceInputActive",
                        "voiceReviewPending", "questVoiceRecording",
                        "questVoiceUploading", "desktopEditingPrompt",
                        // Added with the typed Time-range entry: it is an input
                        // flag like the others, and the workbench routes around
                        // it exactly like desktopEditingPrompt.
                        "boundaryRangeEntry",
                    },
                },
                new
                {
                    Field = "viewState",
                    Struct = "DesktopViewState",
                    Members = new[]
                    {
                        "desktopVisualizationAligned", "desktopFocusTargetsReady",
                        "desktopMatrixPresentationReady", "workflowToolbarPinned",
                        "variablePaletteCollapsed", "cubeVisible",
                        "legacyPanelVisible",
                    },
                },
            };
            GameObject root = new GameObject("State struct regression");
            root.SetActive(false);
            try
            {
                var workbench = root.AddComponent<
                    VolumeSTCubeQuestSpatialWorkbench>();
                foreach (var group in groups)
                {
                    Type structType = type.GetNestedType(group.Struct,
                        BindingFlags.NonPublic);
                    Assert.IsNotNull(structType, group.Struct + " is missing");
                    string[] names = new string[group.Members.Length];
                    for (int index = 0; index < group.Members.Length; index++)
                    {
                        FieldInfo member = structType.GetField(
                            group.Members[index]);
                        Assert.IsNotNull(member,
                            group.Struct + "." + group.Members[index] +
                            " disappeared");
                        Assert.AreEqual(typeof(bool), member.FieldType);
                        names[index] = member.Name;
                    }
                    Assert.AreEqual(group.Members.Length,
                        structType.GetFields(BindingFlags.Public |
                            BindingFlags.Instance).Length,
                        group.Struct + " gained a member");
                    FieldInfo holder = type.GetField(group.Field, Private);
                    Assert.IsNotNull(holder, group.Field + " is missing");
                    Assert.AreEqual(structType, holder.FieldType);
                    object value = holder.GetValue(workbench);
                    foreach (string name in names)
                    {
                        bool flag = (bool)structType.GetField(name)
                            .GetValue(value);
                        bool expected = name == "cubeVisible"; // the only true default
                        Assert.AreEqual(expected, flag,
                            group.Field + "." + name + " has the wrong default");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PendingJobBitsMatchTheRetiredBooleans()
        {
            Type type = typeof(VolumeSTCubeQuestSpatialWorkbench);
            Type pending = type.GetNestedType("PendingJob",
                BindingFlags.NonPublic);
            Assert.IsNotNull(pending, "PendingJob enum is missing");
            var expected = new Dictionary<string, int>
            {
                { "None", 0 }, { "Analysis", 1 }, { "VariableLoad", 2 },
                { "DatasetManifest", 4 }, { "Intent", 8 },
                { "SourcePreview", 16 }, { "Digest", 32 },
                { "GroundAggregate", 64 },
            };
            string[] names = Enum.GetNames(pending);
            Assert.AreEqual(expected.Count, names.Length,
                "PendingJob gained or lost a member");
            foreach (KeyValuePair<string, int> entry in expected)
            {
                Assert.IsTrue(Enum.IsDefined(pending, entry.Key),
                    entry.Key + " disappeared");
                Assert.AreEqual(entry.Value,
                    Convert.ToInt32(Enum.Parse(pending, entry.Key)),
                    entry.Key + " changed bit");
            }
            // the helpers must drive exactly that field
            GameObject root = new GameObject("PendingJob regression");
            root.SetActive(false);
            try
            {
                var workbench = root.AddComponent<
                    VolumeSTCubeQuestSpatialWorkbench>();
                FieldInfo field = type.GetField("pendingJobs", Private);
                MethodInfo set = type.GetMethod("SetPending", Private);
                MethodInfo isSet = type.GetMethod("IsPending", Private);
                object digest = Enum.Parse(pending, "Digest");
                object intent = Enum.Parse(pending, "Intent");
                set.Invoke(workbench, new[] { digest, (object)true });
                Assert.IsTrue((bool)isSet.Invoke(workbench, new[] { digest }));
                Assert.IsFalse((bool)isSet.Invoke(workbench, new[] { intent }));
                set.Invoke(workbench, new[] { intent, (object)true });
                Assert.IsTrue((bool)isSet.Invoke(workbench, new[] { digest }),
                    "setting one bit must not clear another");
                set.Invoke(workbench, new[] { digest, (object)false });
                Assert.IsFalse((bool)isSet.Invoke(workbench, new[] { digest }));
                Assert.IsTrue((bool)isSet.Invoke(workbench, new[] { intent }));
                Assert.AreEqual(Convert.ToInt32(intent),
                    Convert.ToInt32(field.GetValue(workbench)),
                    "clearing one bit must leave the other bits alone");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [MenuItem("VolumeSTCube/Desktop/Validate Refactor")]
        public static void RunChecks()
        {
            var tests = new VolumeSTCubeRefactorTests();
            tests.LayoutConstantsKeepTheirDocumentedValues();
            tests.SettingsKeysAndDefaultsAreStable();
            tests.WorkbenchNeverTouchesPlayerPrefsDirectly();
            tests.PhaseIsDerivedFromTheExistingFlags();
            tests.PendingJobBitsMatchTheRetiredBooleans();
            tests.StateStructsMatchTheRetiredFlags();
            string path = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../.runtime/refactor-validation.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "PASS: 6 refactor guards\n" +
                DateTime.UtcNow.ToString("O"));
            Debug.Log("PASS: 6 refactor guards");
        }
    }
}
