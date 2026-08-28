using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Converts a RAW directory into the API's transport-neutral data model.
    /// It performs file discovery only; it never mutates the Unity scene.
    /// </summary>
    internal static class VolumeSTCubeRawDirectorySource
    {
        public static bool TryCreate(string directory, out VolumeSTCubeData data, out string error)
        {
            data = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                error = $"Directory does not exist: {directory}";
                return false;
            }

            List<string> rawFiles = Directory.GetFiles(directory, "*.raw", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, NaturalPathComparer.Instance)
                .ToList();
            if (rawFiles.Count == 0)
            {
                error = $"No .raw files were found in: {directory}";
                return false;
            }

            List<string> missingMetadata = rawFiles
                .Where(path => !File.Exists(path + ".ini"))
                .ToList();
            if (missingMetadata.Count > 0)
            {
                error = "Missing matching .raw.ini metadata for: " + string.Join(", ", missingMetadata);
                return false;
            }

            data = new VolumeSTCubeData
            {
                datasetName = new DirectoryInfo(directory).Name
            };
            data.rawFilePaths.AddRange(rawFiles);
            for (int i = 0; i < rawFiles.Count; i++)
                data.iniFilePaths.Add(rawFiles[i] + ".ini");
            return true;
        }

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
                        int result = av.CompareTo(bv);
                        if (result != 0)
                            return result;
                        continue;
                    }

                    int charResult = char.ToUpperInvariant(a[ai]).CompareTo(char.ToUpperInvariant(b[bi]));
                    if (charResult != 0)
                        return charResult;
                    ai++;
                    bi++;
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
    }
}
