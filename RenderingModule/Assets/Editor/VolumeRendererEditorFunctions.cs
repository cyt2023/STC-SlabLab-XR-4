using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace UnityVolumeRendering
{
    public class VolumeRendererEditorFunctions
    {
        private static void ShowDatasetImporter()
        {
            string file = EditorUtility.OpenFilePanel("Select a dataset to load", "DataFiles", "");
            if (File.Exists(file))
            {
                RAWDatasetImporterEditorWindow wnd = (RAWDatasetImporterEditorWindow)EditorWindow.GetWindow(typeof(RAWDatasetImporterEditorWindow));
                if (wnd != null)
                    wnd.Close();

                wnd = EditorWindow.CreateInstance<RAWDatasetImporterEditorWindow>();
                wnd.Initialise(file);
                wnd.Show();
            }
            else
            {
                Debug.LogError("File doesn't exist: " + file);
            }
        }

        private static void ImportCsvDataset()
        {
            if (!EditorUtility.DisplayDialog(
                "Quick CSV import is not the original VolumeSTCube pipeline",
                "This path only creates a quick Unity-side preview. For the paper/demo effect with the China map, axis labels, kriging, clipping, smoothing, and raw-stack layout, use \"Preprocess CSV and load as XY+T\" instead.\n\nContinue with quick preview?",
                "Quick preview",
                "Use original pipeline"))
            {
                EditorApplication.ExecuteMenuItem("Volume Rendering/Load dataset/Preprocess CSV and load as XY+T");
                return;
            }

            string file = EditorUtility.OpenFilePanel("Select a CSV dataset to import", "DataFiles", "csv");
            if (!File.Exists(file))
            {
                Debug.LogError("File doesn't exist: " + file);
                return;
            }

            VolumeSTCubeConfig config = VolumeSTCubeConfig.Default(Path.GetFileNameWithoutExtension(file));
            config.datasetName = Path.GetFileNameWithoutExtension(file);
            config.pointGridDimX = 128;
            config.pointGridDimY = 128;
            config.pointGridDimT = 32;
            config.pointSplatRadius = 2;

            VolumeSTCubeView view = VolumeSTCubeAPI.CreateViewFromCsvRaw(file, config);
            if (view == null)
            {
                Debug.LogError("CSV import failed. Check that the CSV has x/y/z columns, or lng/lat/val aliases.");
                return;
            }

            Selection.activeGameObject = view.rootObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log($"CSV import succeeded. Generated raw data and created view '{view.viewId}'.");
        }

        [MenuItem("Volume Rendering/Load dataset/Load RAW folder (auto XY+T or XYZ+T)", false, 10)]
        private static void ImportRawStackWithOriginalScenePreset()
        {
            if (!ValidateOriginalSceneForImport())
                return;

            string folder = EditorUtility.OpenFolderPanel("Select the folder containing .raw/.ini files", "DataFiles", "");
            if (!Directory.Exists(folder))
            {
                Debug.LogError("Folder doesn't exist: " + folder);
                return;
            }

            List<string> rawFiles = Directory.GetFiles(folder, "*.raw", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, NaturalPathComparer.Instance)
                .ToList();

            if (rawFiles.Count == 0)
            {
                Debug.LogError("No .raw files were found in: " + folder);
                return;
            }

            VolumeSTCubeDataLayout dataLayout = VolumeSTCubeDataLayoutDetector.DetectRawFiles(rawFiles);
            if (dataLayout == VolumeSTCubeDataLayout.XYZTimeSeries)
            {
                DatasetIniData firstIni = DatasetIniReader.ParseIniFile(rawFiles[0] + ".ini");
                string dimensions = firstIni != null
                    ? $"{firstIni.dimX} x {firstIni.dimY} x {firstIni.dimZ}"
                    : "unknown";
                if (!EditorUtility.DisplayDialog(
                    "Detected XYZ + Time dataset",
                    $"Mode: {VolumeSTCubeDataLayoutDetector.Describe(dataLayout)}\n"
                    + $"Time files: {rawFiles.Count}\n"
                    + $"XYZ dimensions per time: {dimensions}\n\n"
                    + "Unity will load one complete 3D time step at a time. The Timeline switches files, so the 30-file Hong Kong dataset is not flattened into Z and does not consume several GB at once.",
                    "Import XYZ + Time",
                    "Cancel"))
                {
                    return;
                }
            }
            else if (rawFiles.Count != VolumeSTCubeOriginalSceneAdapter.OriginalLayerCount)
            {
                if (!EditorUtility.DisplayDialog(
                    "Raw stack count does not match the original scene",
                    $"The original VolumeSTCube scene preset expects {VolumeSTCubeOriginalSceneAdapter.OriginalLayerCount} .raw chunks, but this folder contains {rawFiles.Count}.\n\nIf these files are consecutive time slices or chunks with the same X/Y dimensions and data format, Unity can repack them into 8 original-style chunks before importing.",
                    "Repack to 8 chunks",
                    "Cancel"))
                {
                    return;
                }

                string packedFolder = PackRawFilesToOriginalLayerStack(folder, rawFiles);
                if (string.IsNullOrEmpty(packedFolder))
                    return;

                rawFiles = Directory.GetFiles(packedFolder, "*.raw", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, NaturalPathComparer.Instance)
                    .ToList();
                dataLayout = VolumeSTCubeDataLayout.XYTime;
            }

            List<string> missingIniFiles = rawFiles.Where(path => !File.Exists(path + ".ini")).ToList();
            if (missingIniFiles.Count > 0)
            {
                Debug.LogError("Raw stack import failed. Missing .raw.ini metadata for:\n" + string.Join("\n", missingIniFiles));
                return;
            }

            VolumeSTCubeData data = new VolumeSTCubeData
            {
                datasetName = Path.GetFileName(folder)
            };
            data.rawFilePaths.AddRange(rawFiles);
            for (int i = 0; i < rawFiles.Count; i++)
                data.iniFilePaths.Add(rawFiles[i] + ".ini");

            VolumeSTCubeConfig config = VolumeSTCubeConfig.Default(data.datasetName);
            config.datasetName = data.datasetName;
            config.autoGroupUnderVolumeController = true;
            config.renderMode = VolumeSTCubeRenderMode.Volume;
            config.opacity = 0.5f;
            config.timeAxis = VolumeSTCubeTimeAxis.Z;
            config.dataLayout = dataLayout;
            config.showTimeline = true;

            VolumeSTCubeOriginalSceneAdapter.ClearExistingVolumes(VolumeSTCubeOriginalSceneAdapter.EnsureController());
            VolumeSTCubeView view = VolumeSTCubeAPI.CreateView(data, config);
            if (view == null)
            {
                Debug.LogError("Raw stack import failed. Check that every .raw file has a matching .raw.ini file.");
                return;
            }

            Selection.activeGameObject = view.rootObject;
            VolumeSTCubeOriginalSceneAdapter.SetPresentationCamera();
            AlignSceneViewToMainCamera();
            Debug.Log($"Imported {rawFiles.Count} raw files as {VolumeSTCubeDataLayoutDetector.Describe(dataLayout)} using the VolumeSTCube scene preset.");
        }

        private static void ImportSingleRawWithOriginalScenePreset()
        {
            if (!ValidateOriginalSceneForImport())
                return;

            string file = EditorUtility.OpenFilePanel("Select a .raw dataset to load", "DataFiles", "raw");
            if (!File.Exists(file))
            {
                Debug.LogError("File doesn't exist: " + file);
                return;
            }

            string iniFile = file + ".ini";
            if (!File.Exists(iniFile))
            {
                Debug.LogError("Missing metadata file: " + iniFile);
                return;
            }

            VolumeSTCubeData data = new VolumeSTCubeData
            {
                datasetName = Path.GetFileNameWithoutExtension(file)
            };
            data.rawFilePaths.Add(file);
            data.iniFilePaths.Add(iniFile);

            VolumeSTCubeConfig config = VolumeSTCubeConfig.Default(data.datasetName);
            config.datasetName = data.datasetName;
            config.autoGroupUnderVolumeController = true;
            config.renderMode = VolumeSTCubeRenderMode.Volume;
            config.opacity = 0.5f;
            config.timeAxis = VolumeSTCubeTimeAxis.Z;
            config.dataLayout = VolumeSTCubeDataLayout.XYTime;
            config.showTimeline = true;

            VolumeSTCubeOriginalSceneAdapter.ClearExistingVolumes(VolumeSTCubeOriginalSceneAdapter.EnsureController());
            VolumeSTCubeView view = VolumeSTCubeAPI.CreateView(data, config);
            if (view == null)
            {
                Debug.LogError("Single raw import failed. Check the .raw/.raw.ini metadata.");
                return;
            }

            Selection.activeGameObject = view.rootObject;
            VolumeSTCubeOriginalSceneAdapter.SetPresentationCamera();
            AlignSceneViewToMainCamera();
            Debug.Log($"Imported raw file into VolumeController using the VolumeSTCube scene preset: {file}");
        }

        [MenuItem("Volume Rendering/Camera/Set angled presentation camera")]
        private static void SetAngledPresentationCamera()
        {
            VolumeSTCubeOriginalSceneAdapter.SetPresentationCamera();
            AlignSceneViewToMainCamera();
            Debug.Log("Main Camera and Scene view were moved to the angled presentation view.");
        }

        private static void AlignSceneViewToMainCamera()
        {
            Camera camera = Camera.main;
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (camera == null || sceneView == null)
                return;

            sceneView.AlignViewToObject(camera.transform);
            sceneView.Repaint();
        }

        [MenuItem("Volume Rendering/Timeline/Use Z as time axis and enable timeline")]
        private static void EnableZTimeAxisAndTimeline()
        {
            VolumeControllerObject controller = UnityEngine.Object.FindObjectOfType<VolumeControllerObject>();
            if (controller == null)
            {
                Debug.LogError("No VolumeControllerObject was found in the current scene.");
                return;
            }

            VolumeSTCubeRenderMode renderMode = controller.GetRenderMode() == RenderMode.IsosurfaceRendering
                ? VolumeSTCubeRenderMode.Surface
                : VolumeSTCubeRenderMode.Volume;
            VolumeSTCubeOriginalSceneAdapter.RefreshController(controller, renderMode, VolumeSTCubeTimeAxis.Z);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            SceneView.lastActiveSceneView?.Repaint();
            Debug.Log("VolumeSTCube now stores t in texture Z and stacks it on world Y above the map. The runtime timeline will appear in Play Mode.");
        }

        [MenuItem("Volume Rendering/Load dataset/Clear current dataset", false, 90)]
        private static void ClearImportedVolumeSTCubeVolumes()
        {
            VolumeControllerObject controller = VolumeSTCubeOriginalSceneAdapter.EnsureController();
            VolumeSTCubeOriginalSceneAdapter.ClearExistingVolumes(controller);
            VolumeSTCubeRawTimeSeries series = controller != null
                ? controller.GetComponent<VolumeSTCubeRawTimeSeries>()
                : null;
            if (series != null)
                UnityEngine.Object.DestroyImmediate(series);

            GameObject oneClickRunner = GameObject.Find("VolumeSTCube_OneClickTestRunner");
            if (oneClickRunner != null)
                UnityEngine.Object.DestroyImmediate(oneClickRunner);

            Debug.Log("Cleared imported VolumeSTCube volumes and one-click test runner objects.");
        }

        //[MenuItem("Volume Rendering/Load dataset/Load DICOM")]
        //private static void ShowDICOMImporter()
        //{
        //    DicomImportAsync(true);
        //}

        private sealed class NaturalPathComparer : IComparer<string>
        {
            public static readonly NaturalPathComparer Instance = new NaturalPathComparer();

            public int Compare(string left, string right)
            {
                string a = Path.GetFileName(left);
                string b = Path.GetFileName(right);
                int ai = 0;
                int bi = 0;

                while (ai < a.Length && bi < b.Length)
                {
                    if (char.IsDigit(a[ai]) && char.IsDigit(b[bi]))
                    {
                        long av = ReadNumber(a, ref ai);
                        long bv = ReadNumber(b, ref bi);
                        int numberCompare = av.CompareTo(bv);
                        if (numberCompare != 0)
                            return numberCompare;
                    }
                    else
                    {
                        int charCompare = char.ToUpperInvariant(a[ai]).CompareTo(char.ToUpperInvariant(b[bi]));
                        if (charCompare != 0)
                            return charCompare;

                        ai++;
                        bi++;
                    }
                }

                return a.Length.CompareTo(b.Length);
            }

            private static long ReadNumber(string value, ref int index)
            {
                long result = 0;
                while (index < value.Length && char.IsDigit(value[index]))
                {
                    result = result * 10 + value[index] - '0';
                    index++;
                }

                return result;
            }
        }

        private static bool ValidateOriginalSceneForImport()
        {
            if (VolumeSTCubeOriginalSceneAdapter.HasOriginalSceneGuides())
                return true;

            string sceneName = SceneManager.GetActiveScene().name;
            EditorUtility.DisplayDialog(
                "Open mainScene first",
                $"The original VolumeSTCube map and axis objects are not in the active scene '{sceneName}'.\n\nOpen Assets/Scenes/mainScene.unity, then run this import again.",
                "OK");
            return false;
        }

        private static string PackRawFilesToOriginalLayerStack(string sourceFolder, List<string> rawFiles)
        {
            List<RawFileInfo> infos = new List<RawFileInfo>();
            for (int i = 0; i < rawFiles.Count; i++)
            {
                string iniPath = rawFiles[i] + ".ini";
                DatasetIniData ini = DatasetIniReader.ParseIniFile(iniPath);
                if (ini == null)
                {
                    Debug.LogError("Cannot repack raw folder because metadata is missing: " + iniPath);
                    return null;
                }

                if (ini.dimX <= 0 || ini.dimY <= 0 || ini.dimZ <= 0)
                {
                    Debug.LogError("Cannot repack raw folder because metadata has invalid dimensions: " + iniPath);
                    return null;
                }

                infos.Add(new RawFileInfo(rawFiles[i], ini));
            }

            RawFileInfo first = infos[0];
            int sampleSize = GetSampleFormatSize(first.ini.format);
            int sliceByteCount = first.ini.dimX * first.ini.dimY * sampleSize;
            int totalSlices = 0;

            for (int i = 0; i < infos.Count; i++)
            {
                DatasetIniData ini = infos[i].ini;
                if (ini.dimX != first.ini.dimX ||
                    ini.dimY != first.ini.dimY ||
                    ini.format != first.ini.format ||
                    ini.endianness != first.ini.endianness ||
                    ini.bytesToSkip != first.ini.bytesToSkip)
                {
                    Debug.LogError("Cannot repack raw folder. All raw files must have the same dimX, dimY, format, endianness, and skip value.");
                    return null;
                }

                long expectedBytes = (long)sliceByteCount * ini.dimZ + ini.bytesToSkip;
                FileInfo fileInfo = new FileInfo(infos[i].path);
                if (fileInfo.Length < expectedBytes)
                {
                    Debug.LogError($"Cannot repack raw folder. File is smaller than its .ini dimensions require: {infos[i].path}");
                    return null;
                }

                totalSlices += ini.dimZ;
            }

            if (totalSlices < VolumeSTCubeOriginalSceneAdapter.OriginalLayerCount)
            {
                Debug.LogError($"Cannot repack raw folder into {VolumeSTCubeOriginalSceneAdapter.OriginalLayerCount} chunks because it only contains {totalSlices} total Z/time slices.");
                return null;
            }

            int[] chunkSliceCounts = SplitSlices(totalSlices, VolumeSTCubeOriginalSceneAdapter.OriginalLayerCount);
            string outputFolder = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Library",
                "VolumeSTCubePackedRaw",
                MakeSafeFileName(Path.GetFileName(sourceFolder)) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(outputFolder);

            int sourceIndex = 0;
            int sourceSliceOffset = 0;
            byte[] buffer = new byte[sliceByteCount * 16];

            for (int chunk = 0; chunk < chunkSliceCounts.Length; chunk++)
            {
                string outputRaw = Path.Combine(outputFolder, $"packed_{chunk:00}.raw");
                using (FileStream output = new FileStream(outputRaw, FileMode.Create, FileAccess.Write))
                {
                    int remainingSlices = chunkSliceCounts[chunk];
                    while (remainingSlices > 0)
                    {
                        RawFileInfo source = infos[sourceIndex];
                        int availableSlices = source.ini.dimZ - sourceSliceOffset;
                        int slicesToCopy = Mathf.Min(remainingSlices, availableSlices);
                        CopyRawSlices(source.path, source.ini.bytesToSkip, sourceSliceOffset, slicesToCopy, sliceByteCount, buffer, output);

                        remainingSlices -= slicesToCopy;
                        sourceSliceOffset += slicesToCopy;
                        if (sourceSliceOffset >= source.ini.dimZ)
                        {
                            sourceIndex++;
                            sourceSliceOffset = 0;
                        }
                    }
                }

                string iniText = $"dimx:{first.ini.dimX}\n"
                    + $"dimy:{first.ini.dimY}\n"
                    + $"dimz:{chunkSliceCounts[chunk]}\n"
                    + "skip:0\n"
                    + $"format:{GetFormatName(first.ini.format)}\n"
                    + $"endianness:{GetEndiannessName(first.ini.endianness)}";
                File.WriteAllText(outputRaw + ".ini", iniText);
            }

            Debug.Log($"Repacked {rawFiles.Count} raw files ({totalSlices} total slices) into {VolumeSTCubeOriginalSceneAdapter.OriginalLayerCount} original-style chunks: {outputFolder}");
            return outputFolder;
        }

        private static void CopyRawSlices(string rawPath, int bytesToSkip, int sliceOffset, int sliceCount, int sliceByteCount, byte[] buffer, FileStream output)
        {
            long bytesRemaining = (long)sliceCount * sliceByteCount;
            using (FileStream input = new FileStream(rawPath, FileMode.Open, FileAccess.Read))
            {
                input.Seek(bytesToSkip + (long)sliceOffset * sliceByteCount, SeekOrigin.Begin);
                while (bytesRemaining > 0)
                {
                    int readSize = (int)Math.Min(buffer.Length, bytesRemaining);
                    int bytesRead = input.Read(buffer, 0, readSize);
                    if (bytesRead <= 0)
                        throw new EndOfStreamException("Unexpected end of raw file: " + rawPath);

                    output.Write(buffer, 0, bytesRead);
                    bytesRemaining -= bytesRead;
                }
            }
        }

        private static int[] SplitSlices(int totalSlices, int chunkCount)
        {
            int[] chunks = new int[chunkCount];
            int baseCount = totalSlices / chunkCount;
            int remainder = totalSlices % chunkCount;
            for (int i = 0; i < chunkCount; i++)
                chunks[i] = baseCount + (i < remainder ? 1 : 0);
            return chunks;
        }

        private static int GetSampleFormatSize(DataContentFormat format)
        {
            switch (format)
            {
                case DataContentFormat.Int8:
                case DataContentFormat.Uint8:
                    return 1;
                case DataContentFormat.Int16:
                case DataContentFormat.Uint16:
                    return 2;
                case DataContentFormat.Int32:
                case DataContentFormat.Uint32:
                    return 4;
                default:
                    return 1;
            }
        }

        private static string GetFormatName(DataContentFormat format)
        {
            switch (format)
            {
                case DataContentFormat.Int8:
                    return "int8";
                case DataContentFormat.Int16:
                    return "int16";
                case DataContentFormat.Int32:
                    return "int32";
                case DataContentFormat.Uint16:
                    return "uint16";
                case DataContentFormat.Uint32:
                    return "uint32";
                default:
                    return "uint8";
            }
        }

        private static string GetEndiannessName(Endianness endianness)
        {
            return endianness == Endianness.BigEndian ? "bigendian" : "littleendian";
        }

        private static string MakeSafeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return string.IsNullOrEmpty(value) ? "raw_stack" : value;
        }

        private struct RawFileInfo
        {
            public readonly string path;
            public readonly DatasetIniData ini;

            public RawFileInfo(string path, DatasetIniData ini)
            {
                this.path = path;
                this.ini = ini;
            }
        }

        //[MenuItem("Assets/Volume Rendering/Import dataset/Import DICOM")]
        //private static void ImportDICOMAsset()
        //{
        //    DicomImportAsync(false);
        //}

        private static async void DicomImportAsync(bool spawnInScene)
        {
            string dir = EditorUtility.OpenFolderPanel("Select a folder to load", "", "");
            if (Directory.Exists(dir))
            {
                Debug.Log("Async dataset load. Hold on.");
                using (ProgressHandler progressHandler = new ProgressHandler(new EditorProgressView()))
                {
                    progressHandler.StartStage(0.7f, "Importing dataset");
                    Task<VolumeDataset[]> importTask = DicomImportDirectoryAsync(dir, progressHandler);
                    await importTask;
                    progressHandler.EndStage();
                    progressHandler.StartStage(0.3f, "Spawning dataset");
                    for (int i = 0; i < importTask.Result.Length; i++)
                    {
                        if (spawnInScene)
                        {
                            VolumeDataset dataset = importTask.Result[i];
                            VolumeRenderedObject obj = await VolumeObjectFactory.CreateObjectAsync(dataset);
                            obj.transform.position = new Vector3(i, 0, 0);
                        }
                        else
                        {
                            VolumeDataset dataset = importTask.Result[i];
                            ProjectWindowUtil.CreateAsset(dataset, $"{dataset.datasetName}.asset");
                            AssetDatabase.SaveAssets();
                        }
                    }
                    progressHandler.EndStage();
                }
            }
            else
            {
                Debug.LogError("Directory doesn't exist: " + dir);
            }
        }

        private static async Task<VolumeDataset[]> DicomImportDirectoryAsync(string dir, ProgressHandler progressHandler)
        {
            Debug.Log("Async dataset load. Hold on.");

            List<VolumeDataset> importedDatasets = new List<VolumeDataset>();
            bool recursive = true;

            // Read all files
            IEnumerable<string> fileCandidates = Directory.EnumerateFiles(dir, "*.*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(p => p.EndsWith(".dcm", StringComparison.InvariantCultureIgnoreCase) || p.EndsWith(".dicom", StringComparison.InvariantCultureIgnoreCase) || p.EndsWith(".dicm", StringComparison.InvariantCultureIgnoreCase));

            if (!fileCandidates.Any())
            {
                if (UnityEditor.EditorUtility.DisplayDialog("Could not find any DICOM files",
                    $"Failed to find any files with DICOM file extension.{Environment.NewLine}Do you want to include files without DICOM file extension?", "Yes", "No"))
                {
                    fileCandidates = Directory.EnumerateFiles(dir, "*.*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                }
            }

            if (fileCandidates.Any())
            {
                progressHandler.StartStage(0.2f, "Loading DICOM series");

                IImageSequenceImporter importer = ImporterFactory.CreateImageSequenceImporter(ImageSequenceFormat.DICOM);
                IEnumerable<IImageSequenceSeries> seriesList = await importer.LoadSeriesAsync(fileCandidates, new ImageSequenceImportSettings { progressHandler = progressHandler });

                progressHandler.EndStage();
                progressHandler.StartStage(0.8f);

                int seriesIndex = 0, numSeries = seriesList.Count();
                foreach (IImageSequenceSeries series in seriesList)
                {
                    progressHandler.StartStage(1.0f / numSeries, $"Importing series {seriesIndex + 1} of {numSeries}");
                    VolumeDataset dataset = await importer.ImportSeriesAsync(series, new ImageSequenceImportSettings { progressHandler = progressHandler });
                    if (dataset != null)
                    {
                        await OptionallyDownscale(dataset);
                        importedDatasets.Add(dataset);
                    }
                    seriesIndex++;
                    progressHandler.EndStage();
                }

                progressHandler.EndStage();
            }
            else
                Debug.LogError("Could not find any DICOM files to import.");

            return importedDatasets.ToArray();
        }

        //[MenuItem("Volume Rendering/Load dataset/Load NRRD dataset")]
        //private static void ShowNRRDDatasetImporter()
        //{
        //    ImportNRRDDatasetAsync(true);
        //}

        //[MenuItem("Assets/Volume Rendering/Import dataset/Import NRRD")]
        //private static void ImportNRRDAsset()
        //{
        //    ImportNRRDDatasetAsync(false);
        //}

        private static async void ImportNRRDDatasetAsync(bool spawnInScene)
        {
            if (!SimpleITKManager.IsSITKEnabled())
            {
                if (EditorUtility.DisplayDialog("Missing SimpleITK", "You need to download SimpleITK to load NRRD datasets from the import settings menu.\n" +
                    "Do you want to open the import settings menu?", "Yes", "No"))
                {
                    ImportSettingsEditorWindow.ShowWindow();
                }
                return;
            }

            string file = EditorUtility.OpenFilePanel("Select a dataset to load (.nrrd)", "DataFiles", "");
            if (File.Exists(file))
            {
                Debug.Log("Async dataset load. Hold on.");
                using (ProgressHandler progressHandler = new ProgressHandler(new EditorProgressView(), "NRRD import"))
                {
                    progressHandler.ReportProgress(0.0f, "Importing NRRD dataset");

                    IImageFileImporter importer = ImporterFactory.CreateImageFileImporter(ImageFileFormat.NRRD);
                    VolumeDataset dataset = await importer.ImportAsync(file);

                    progressHandler.ReportProgress(0.8f, "Creating object");
                    if (dataset != null)
                    {
                        await OptionallyDownscale(dataset);
                        if (spawnInScene)
                        {
                            await VolumeObjectFactory.CreateObjectAsync(dataset);
                        }
                        else    
                        {
                            ProjectWindowUtil.CreateAsset(dataset, $"{dataset.datasetName}.asset");
                            AssetDatabase.SaveAssets();
                        }
                    }
                    else
                    {
                        Debug.LogError("Failed to import datset");
                    }
                }
            }
            else
            {
                Debug.LogError("File doesn't exist: " + file);
            }
        }

        //[MenuItem("Volume Rendering/Load dataset/Load NIFTI dataset")]
        //private static void ShowNIFTIDatasetImporter()
        //{
        //    ImportNIFTIDatasetAsync(true);
        //}

        //[MenuItem("Assets/Volume Rendering/Import dataset/Import NIFTI")]
        //private static void ImportNIFTIAsset()
        //{
        //    ImportNIFTIDatasetAsync(false);
        //}

        private static async void ImportNIFTIDatasetAsync(bool spawnInScene)
        {
            string file = EditorUtility.OpenFilePanel("Select a dataset to load (.nii)", "DataFiles", "");
            if (File.Exists(file))
            {
                Debug.Log("Async dataset load. Hold on.");
                using (ProgressHandler progressHandler = new ProgressHandler(new EditorProgressView(), "NIFTI import"))
                {
                    progressHandler.ReportProgress(0.0f, "Importing NIfTI dataset");

                    IImageFileImporter importer = ImporterFactory.CreateImageFileImporter(ImageFileFormat.NIFTI);
                    VolumeDataset dataset = await importer.ImportAsync(file);

                    progressHandler.ReportProgress(0.0f, "Creating object");

                    if (dataset != null)
                    {
                        await OptionallyDownscale(dataset);
                        if (spawnInScene)
                        {
                            await VolumeObjectFactory.CreateObjectAsync(dataset);
                        }
                        else    
                        {
                            ProjectWindowUtil.CreateAsset(dataset, $"{dataset.datasetName}.asset");
                            AssetDatabase.SaveAssets();
                        }
                    }
                    else
                    {
                        Debug.LogError("Failed to import datset");
                    }
                }
            }
            else
            {
                Debug.LogError("File doesn't exist: " + file);
            }
        }

        //[MenuItem("Volume Rendering/Load dataset/Load PARCHG dataset")]
        //private static void ShowParDatasetImporter()
        //{
        //    ImportParDatasetAsync(true);
        //}

        //[MenuItem("Assets/Volume Rendering/Import dataset/Import PARCHG")]
        //private static void ImportParAsset()
        //{
        //    ImportParDatasetAsync(false);
        //}

        private static async void ImportParDatasetAsync(bool spawnInScene)
        {
            string file = EditorUtility.OpenFilePanel("Select a dataset to load", "DataFiles", "");
            if (File.Exists(file))
            {
                Debug.Log("Async dataset load. Hold on.");
                using (ProgressHandler progressHandler = new ProgressHandler(new EditorProgressView(), "AVSP import"))
                {
                    progressHandler.ReportProgress(0.0f, "Importing VASP dataset");

                    IImageFileImporter importer = ImporterFactory.CreateImageFileImporter(ImageFileFormat.VASP);
                    VolumeDataset dataset = await importer.ImportAsync(file);

                    progressHandler.ReportProgress(0.0f, "Creating object");

                    if (dataset != null)
                    {
                        await OptionallyDownscale(dataset);
                        if (spawnInScene)
                        {
                            await VolumeObjectFactory.CreateObjectAsync(dataset);
                        }
                        else    
                        {
                            ProjectWindowUtil.CreateAsset(dataset, $"{dataset.datasetName}.asset");
                            AssetDatabase.SaveAssets();
                        }
                    }
                    else
                    {
                        Debug.LogError("Failed to import datset");
                    }
                }
            }
            else
            {
                Debug.LogError("File doesn't exist: " + file);
            }
        }

        //[MenuItem("Volume Rendering/Load dataset/Load image sequence")]
        //private static void ShowSequenceImporter()
        //{
        //    ImportSequenceAsync();
        //}

        //private static async void ImportSequenceAsync()
        //{
        //    string dir = EditorUtility.OpenFolderPanel("Select a folder to load", "", "");

        //    if (Directory.Exists(dir))
        //    {
        //        Debug.Log("Async dataset load. Hold on.");

        //        List<string> filePaths = Directory.GetFiles(dir).ToList();
        //        IImageSequenceImporter importer = ImporterFactory.CreateImageSequenceImporter(ImageSequenceFormat.ImageSequence);

        //        IEnumerable<IImageSequenceSeries> seriesList = await importer.LoadSeriesAsync(filePaths);

        //        foreach (IImageSequenceSeries series in seriesList)
        //        {
        //            VolumeDataset dataset = await importer.ImportSeriesAsync(series);
        //            if (dataset != null)
        //            {
        //                await OptionallyDownscale(dataset);
        //                await VolumeObjectFactory.CreateObjectAsync(dataset);
        //            }
        //        }
        //    }
        //    else
        //    {
        //        Debug.LogError("Directory doesn't exist: " + dir);
        //    }
        //}

        private static async Task OptionallyDownscale(VolumeDataset dataset)
        {
            if (EditorPrefs.GetBool("DownscaleDatasetPrompt"))
            {
                if (EditorUtility.DisplayDialog("Optional DownScaling",
                    $"Do you want to downscale the dataset? The dataset's dimension is: {dataset.dimX} x {dataset.dimY} x {dataset.dimZ}", "Yes", "No"))
                {
                    Debug.Log("Async dataset downscale. Hold on.");
                    await Task.Run(() => dataset.DownScaleData());
                }
            }
        }

        //[MenuItem("Volume Rendering/Cross section/Cross section plane")]
        //private static void OnMenuItemClick()
        //{
        //    VolumeRenderedObject[] objects = GameObject.FindObjectsOfType<VolumeRenderedObject>();
        //    if (objects.Length == 1)
        //        VolumeObjectFactory.SpawnCrossSectionPlane(objects[0]);
        //    else
        //    {
        //        CrossSectionPlaneEditorWindow wnd = new CrossSectionPlaneEditorWindow();
        //        wnd.Show();
        //    }
        //}

        //[MenuItem("Volume Rendering/Cross section/Box cutout")]
        //private static void SpawnCutoutBox()
        //{
        //    VolumeRenderedObject[] objects = GameObject.FindObjectsOfType<VolumeRenderedObject>();
        //    if (objects.Length == 1)
        //        VolumeObjectFactory.SpawnCutoutBox(objects[0]);
        //}
        //[MenuItem("Volume Rendering/Cross section/Sphere cutout")]
        //private static void SpawnCutoutSphere()
        //{
        //    VolumeRenderedObject[] objects = GameObject.FindObjectsOfType<VolumeRenderedObject>();
        //    if (objects.Length == 1)
        //        VolumeObjectFactory.SpawnCutoutSphere(objects[0]);
        //}

        //[MenuItem("Volume Rendering/1D Transfer Function")]
        //private static void Show1DTFWindow()
        //{
        //    VolumeRenderedObject volRendObj = SelectionHelper.GetSelectedVolumeObject();
        //    if (volRendObj != null)
        //    {
        //        volRendObj.SetTransferFunctionMode(TFRenderMode.TF1D);
        //        TransferFunctionEditorWindow.ShowWindow(volRendObj);
        //    }
        //    else
        //    {
        //        EditorUtility.DisplayDialog("No imported dataset", "You need to import a dataset first", "Ok");
        //    }
        //}

        //[MenuItem("Volume Rendering/2D Transfer Function")]
        //private static void Show2DTFWindow()
        //{
        //    TransferFunction2DEditorWindow.ShowWindow();
        //}

        //[MenuItem("Volume Rendering/Slice renderer")]
        //private static void ShowSliceRenderer()
        //{
        //    SliceRenderingEditorWindow.ShowWindow();
        //}

        //[MenuItem("Volume Rendering/Value range")]
        //private static void ShowValueRangeWindow()
        //{
        //    ValueRangeEditorWindow.ShowWindow();
        //}

        //[MenuItem("Volume Rendering/Settings")]
        //private static void ShowSettingsWindow()
        //{
        //    ImportSettingsEditorWindow.ShowWindow();
        //}
    }
}
