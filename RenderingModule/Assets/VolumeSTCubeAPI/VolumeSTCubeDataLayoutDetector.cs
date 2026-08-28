using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace UnityVolumeRendering
{
    public static class VolumeSTCubeDataLayoutDetector
    {
        private static readonly Regex ExplicitTimeFilePattern = new Regex(
            @"(?:^|[_-])time[_-]?\d+(?:[_-]|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static VolumeSTCubeDataLayout Resolve(VolumeSTCubeData data, VolumeSTCubeDataLayout requestedLayout)
        {
            if (requestedLayout != VolumeSTCubeDataLayout.Auto)
                return requestedLayout;

            if (data == null || !data.HasRawFiles())
                return VolumeSTCubeDataLayout.XYTime;

            return DetectRawFiles(data.rawFilePaths, data.iniFilePaths);
        }

        public static VolumeSTCubeDataLayout DetectRawFiles(
            IList<string> rawFiles,
            IList<string> iniFiles = null)
        {
            if (rawFiles == null || rawFiles.Count == 0)
                return VolumeSTCubeDataLayout.XYTime;

            VolumeSTCubeDataLayout metadataLayout = DetectMetadataLayout(rawFiles, iniFiles);
            if (metadataLayout != VolumeSTCubeDataLayout.Auto)
                return metadataLayout;

            // XYZ+T exports one complete XYZ volume per explicitly numbered
            // time file (for example: chlorophyll_data_time_0_255.raw).
            if (rawFiles.Count < 2)
                return VolumeSTCubeDataLayout.XYTime;

            DatasetIniData first = null;
            for (int i = 0; i < rawFiles.Count; i++)
            {
                string fileName = Path.GetFileNameWithoutExtension(rawFiles[i]);
                if (!ExplicitTimeFilePattern.IsMatch(fileName))
                    return VolumeSTCubeDataLayout.XYTime;

                string iniPath = ResolveIniPath(rawFiles, iniFiles, i);
                DatasetIniData current = DatasetIniReader.ParseIniFile(iniPath);
                if (current == null || current.dimX <= 0 || current.dimY <= 0 || current.dimZ <= 1)
                    return VolumeSTCubeDataLayout.XYTime;

                if (first == null)
                {
                    first = current;
                    continue;
                }

                if (current.dimX != first.dimX ||
                    current.dimY != first.dimY ||
                    current.dimZ != first.dimZ ||
                    current.format != first.format ||
                    current.endianness != first.endianness)
                    return VolumeSTCubeDataLayout.XYTime;
            }

            return VolumeSTCubeDataLayout.XYZTimeSeries;
        }

        public static string Describe(VolumeSTCubeDataLayout layout)
        {
            return layout == VolumeSTCubeDataLayout.XYZTimeSeries
                ? "XYZ + Time (one 3D volume file per time step)"
                : "XY + Time (time stored in RAW Z slices)";
        }

        private static VolumeSTCubeDataLayout DetectMetadataLayout(IList<string> rawFiles, IList<string> iniFiles)
        {
            string iniPath = ResolveIniPath(rawFiles, iniFiles, 0);
            if (string.IsNullOrEmpty(iniPath) || !File.Exists(iniPath))
                return VolumeSTCubeDataLayout.Auto;

            string metadata = File.ReadAllText(iniPath).Replace(" ", string.Empty).ToLowerInvariant();
            if (metadata.Contains("layout:xyzt") || metadata.Contains("layout:xyz+time"))
                return VolumeSTCubeDataLayout.XYZTimeSeries;
            if (metadata.Contains("layout:xyt") || metadata.Contains("layout:xy+time"))
                return VolumeSTCubeDataLayout.XYTime;
            return VolumeSTCubeDataLayout.Auto;
        }

        private static string ResolveIniPath(IList<string> rawFiles, IList<string> iniFiles, int index)
        {
            if (iniFiles != null && index < iniFiles.Count && !string.IsNullOrEmpty(iniFiles[index]))
                return iniFiles[index];
            return rawFiles[index] + ".ini";
        }
    }
}
