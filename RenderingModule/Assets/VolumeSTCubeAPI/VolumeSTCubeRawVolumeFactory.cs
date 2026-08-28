using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Creates STC volume objects and preloaded datasets from RAW/INI pairs.
    /// This is the only integration-layer class that knows the original raw importer.
    /// </summary>
    internal static class VolumeSTCubeRawVolumeFactory
    {
        public static VolumeRenderedObject Import(string rawPath, string iniPath, string datasetName)
        {
            VolumeDataset dataset = ImportDataset(rawPath, iniPath);
            if (dataset == null)
                return null;

            ApplyDatasetName(dataset, datasetName);
            return CreateObject(dataset, rawPath);
        }

        public static async Task<VolumeDataset> PreloadDatasetAsync(
            string rawPath,
            string iniPath,
            string datasetName)
        {
            DatasetIniData metadata = ReadMetadata(rawPath, iniPath);
            if (metadata == null)
                return null;

            RawDatasetImporter importer = CreateImporter(rawPath, metadata);
            VolumeDataset dataset = await importer.ImportAsync();
            if (dataset == null)
                return null;

            ApplyDatasetName(dataset, datasetName);
            await dataset.GetDataTextureAsync();
            return dataset;
        }

        public static VolumeRenderedObject CreateObject(VolumeDataset dataset, string sourcePath)
        {
            if (dataset == null)
                return null;

            VolumeRenderedObject volume = VolumeObjectFactory.CreateObject(dataset);
            return Validate(volume, sourcePath) ? volume : null;
        }

        public static bool Validate(VolumeRenderedObject volume, string sourcePath)
        {
            if (volume == null)
            {
                Debug.LogError($"VolumeSTCube RAW import failed: renderer creation returned null for {sourcePath}.");
                return false;
            }

            if (volume.volumeContainerObject == null ||
                volume.volumeContainerObject.GetComponent<MeshRenderer>() == null)
            {
                Debug.LogError($"VolumeSTCube RAW import failed: generated volume container is invalid for {sourcePath}.");
                DestroyObject(volume.gameObject);
                return false;
            }

            return true;
        }

        private static VolumeDataset ImportDataset(string rawPath, string iniPath)
        {
            DatasetIniData metadata = ReadMetadata(rawPath, iniPath);
            if (metadata == null)
                return null;
            return CreateImporter(rawPath, metadata).Import();
        }

        private static DatasetIniData ReadMetadata(string rawPath, string iniPath)
        {
            if (string.IsNullOrEmpty(rawPath) || !File.Exists(rawPath))
            {
                Debug.LogError($"VolumeSTCube RAW import failed: file does not exist: {rawPath}");
                return null;
            }

            DatasetIniData metadata = DatasetIniReader.ParseIniFile(iniPath);
            if (metadata == null)
            {
                Debug.LogError($"VolumeSTCube RAW import failed: missing or invalid INI metadata: {iniPath}");
                return null;
            }
            return metadata;
        }

        private static RawDatasetImporter CreateImporter(string rawPath, DatasetIniData metadata)
        {
            return new RawDatasetImporter(
                rawPath,
                metadata.dimX,
                metadata.dimY,
                metadata.dimZ,
                metadata.format,
                metadata.endianness,
                metadata.bytesToSkip);
        }

        private static void ApplyDatasetName(VolumeDataset dataset, string datasetName)
        {
            if (!string.IsNullOrEmpty(datasetName))
                dataset.datasetName = datasetName;
            bool isSalt =
                (!string.IsNullOrEmpty(datasetName) &&
                 datasetName.IndexOf("salt", System.StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrEmpty(dataset.filePath) &&
                 dataset.filePath.IndexOf(
                     "salt", System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (isSalt)
            {
                // The preprocessing pipeline maps every variable from the shared
                // source range 1..500 into byte values 5..254. Salt must therefore
                // stay on the same 0..255 display scale; per-variable stretching
                // exaggerates its small voxel differences into false speckle.
                // Source values used by analysis remain untouched.
                dataset.normalizationMinimum = 0.0f;
                dataset.normalizationMaximum = 255.0f;
                Debug.Log(
                    "VolumeSTCube salt display normalization applied: 0..255 shared scale for " +
                    Path.GetFileName(dataset.filePath));
            }
        }

        private static void DestroyObject(GameObject target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }
    }
}
