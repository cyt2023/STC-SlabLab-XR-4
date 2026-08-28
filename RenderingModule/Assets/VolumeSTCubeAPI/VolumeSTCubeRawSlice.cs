using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Description of one XYZ+T RAW directory. Each RAW file is one XYZ time step.
    /// </summary>
    public sealed class VolumeSTCubeSliceDataset
    {
        public string Name { get; internal set; }
        public string DatasetId { get; internal set; }
        public string DatasetVersion { get; internal set; }
        public string VariableId { get; internal set; }
        public string Unit { get; internal set; }
        public string ValueSemantics { get; internal set; }
        public string DirectoryPath { get; internal set; }
        public string[] RawPaths { get; internal set; }
        public string[] IniPaths { get; internal set; }
        public DatasetIniData Metadata { get; internal set; }

        public int TimeCount => RawPaths != null ? RawPaths.Length : 0;
        public int DimX => Metadata != null ? Metadata.dimX : 0;
        public int DimY => Metadata != null ? Metadata.dimY : 0;
        public int DimZ => Metadata != null ? Metadata.dimZ : 0;

        public string GetTimeLabel(int index)
        {
            if (RawPaths == null || RawPaths.Length == 0)
                return "t";
            index = Mathf.Clamp(index, 0, RawPaths.Length - 1);
            Match match = Regex.Match(
                Path.GetFileNameWithoutExtension(RawPaths[index]),
                @"(?:^|[_-])time[_-]?(\d+)(?:[_-]|$)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success ? "t=" + match.Groups[1].Value : "t=" + index;
        }
    }

    public sealed class VolumeSTCubeRawSlice
    {
        public int Width { get; internal set; }
        public int Height { get; internal set; }
        public int ZIndex { get; internal set; }
        public float[] Values { get; internal set; }
        public float Minimum { get; internal set; }
        public float Maximum { get; internal set; }
    }

    /// <summary>
    /// Reads one XY plane directly from a RAW XYZ volume. It deliberately avoids
    /// importing the full volume when only a MatPlotAgent slice is needed.
    /// </summary>
    public static class VolumeSTCubeRawSliceReader
    {
        private static readonly Regex NumberPattern = new Regex("(\\d+)", RegexOptions.Compiled);

        public static List<VolumeSTCubeSliceDataset> DiscoverDatasets(string rootDirectory)
        {
            List<VolumeSTCubeSliceDataset> datasets = new List<VolumeSTCubeSliceDataset>();
            if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
                return datasets;

            string[] directories = Directory.GetDirectories(rootDirectory, "*", SearchOption.TopDirectoryOnly);
            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < directories.Length; i++)
            {
                if (TryOpenDataset(directories[i], out VolumeSTCubeSliceDataset dataset, out _))
                    datasets.Add(dataset);
            }
            return datasets;
        }

        public static bool TryOpenDataset(
            string directory,
            out VolumeSTCubeSliceDataset dataset,
            out string error)
        {
            dataset = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                error = "RAW dataset directory does not exist: " + directory;
                return false;
            }

            string[] rawPaths = Directory.GetFiles(directory, "*.raw", SearchOption.TopDirectoryOnly);
            Array.Sort(rawPaths, CompareNaturalPaths);
            if (rawPaths.Length == 0)
            {
                error = "No .raw files were found in: " + directory;
                return false;
            }

            string[] iniPaths = new string[rawPaths.Length];
            DatasetIniData first = null;
            for (int i = 0; i < rawPaths.Length; i++)
            {
                iniPaths[i] = rawPaths[i] + ".ini";
                DatasetIniData metadata = DatasetIniReader.ParseIniFile(iniPaths[i]);
                if (metadata == null)
                {
                    error = "Missing or invalid metadata: " + iniPaths[i];
                    return false;
                }

                if (metadata.dimX <= 0 || metadata.dimY <= 0 || metadata.dimZ <= 0)
                {
                    error = "Invalid RAW dimensions in: " + iniPaths[i];
                    return false;
                }

                if (first == null)
                    first = metadata;
                else if (!HasSameLayout(first, metadata))
                {
                    error = "All time files must use the same dimensions and RAW format.";
                    return false;
                }
            }

            dataset = new VolumeSTCubeSliceDataset
            {
                Name = new DirectoryInfo(directory).Name,
                DirectoryPath = Path.GetFullPath(directory),
                RawPaths = rawPaths,
                IniPaths = iniPaths,
                Metadata = first
            };
            return true;
        }

        public static VolumeSTCubeRawSlice ReadSlice(string rawPath, string iniPath, int zIndex)
        {
            DatasetIniData metadata = DatasetIniReader.ParseIniFile(iniPath);
            if (metadata == null)
                throw new InvalidDataException("Could not read RAW metadata: " + iniPath);
            if (zIndex < 0 || zIndex >= metadata.dimZ)
                throw new ArgumentOutOfRangeException(nameof(zIndex));

            int sampleSize = GetSampleSize(metadata.format);
            int sampleCount = checked(metadata.dimX * metadata.dimY);
            long byteOffset = metadata.bytesToSkip + (long)zIndex * sampleCount * sampleSize;
            long byteCount = (long)sampleCount * sampleSize;

            using (FileStream stream = new FileStream(rawPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (stream.Length < byteOffset + byteCount)
                    throw new InvalidDataException("RAW file is shorter than its INI dimensions require: " + rawPath);

                stream.Seek(byteOffset, SeekOrigin.Begin);
                float[] values = new float[sampleCount];
                float minimum = float.PositiveInfinity;
                float maximum = float.NegativeInfinity;
                for (int i = 0; i < sampleCount; i++)
                {
                    float value = ReadValue(reader, metadata.format, metadata.endianness);
                    values[i] = value;
                    minimum = Mathf.Min(minimum, value);
                    maximum = Mathf.Max(maximum, value);
                }

                return new VolumeSTCubeRawSlice
                {
                    Width = metadata.dimX,
                    Height = metadata.dimY,
                    ZIndex = zIndex,
                    Values = values,
                    Minimum = minimum,
                    Maximum = maximum
                };
            }
        }

        public static Texture2D CreatePreviewTexture(
            VolumeSTCubeRawSlice slice,
            int maximumWidth,
            int maximumHeight,
            bool useFormatRange = true)
        {
            if (slice == null || slice.Values == null || slice.Width <= 0 || slice.Height <= 0)
                return null;

            float scale = Mathf.Min(
                Mathf.Min(maximumWidth / (float)slice.Width, maximumHeight / (float)slice.Height),
                1.0f);
            int width = Mathf.Max(1, Mathf.RoundToInt(slice.Width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(slice.Height * scale));
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = "STC_XY_Z" + slice.ZIndex,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            float minimum = slice.Minimum;
            float maximum = slice.Maximum;
            if (useFormatRange && minimum >= 0.0f && maximum <= 255.0f)
            {
                minimum = 0.0f;
                maximum = 255.0f;
            }
            float range = Mathf.Max(0.000001f, maximum - minimum);
            Color32[] pixels = new Color32[width * height];
            for (int py = 0; py < height; py++)
            {
                int sourceY = Mathf.Min(slice.Height - 1, Mathf.FloorToInt(py * slice.Height / (float)height));
                for (int px = 0; px < width; px++)
                {
                    int sourceX = Mathf.Min(slice.Width - 1, Mathf.FloorToInt(px * slice.Width / (float)width));
                    float value = slice.Values[sourceX + sourceY * slice.Width];
                    float normalized = Mathf.Clamp01((value - minimum) / range);
                    pixels[px + (height - 1 - py) * width] = EvaluateColor(normalized, value <= minimum);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        public static string ExportCsv(
            VolumeSTCubeSliceDataset dataset,
            int timeIndex,
            int zIndex,
            string outputDirectory)
        {
            if (dataset == null)
                throw new ArgumentNullException(nameof(dataset));
            if (timeIndex < 0 || timeIndex >= dataset.TimeCount)
                throw new ArgumentOutOfRangeException(nameof(timeIndex));

            VolumeSTCubeRawSlice slice = ReadSlice(
                dataset.RawPaths[timeIndex],
                dataset.IniPaths[timeIndex],
                zIndex);
            Directory.CreateDirectory(outputDirectory);
            string safeName = Regex.Replace(dataset.Name ?? "dataset", @"[^A-Za-z0-9._-]", "_");
            string path = Path.Combine(outputDirectory, safeName + "_t" + timeIndex + "_z" + zIndex + ".csv");

            using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(false), 1024 * 1024))
            {
                writer.WriteLine("x,y,value");
                for (int y = 0; y < slice.Height; y++)
                {
                    for (int x = 0; x < slice.Width; x++)
                    {
                        writer.Write(x.ToString(CultureInfo.InvariantCulture));
                        writer.Write(',');
                        writer.Write(y.ToString(CultureInfo.InvariantCulture));
                        writer.Write(',');
                        writer.WriteLine(slice.Values[x + y * slice.Width].ToString("R", CultureInfo.InvariantCulture));
                    }
                }
            }
            return path;
        }

        public static string ExportRegionCsv(
            VolumeSTCubeSliceDataset dataset,
            int timeIndex,
            int zIndex,
            string outputDirectory,
            Rect normalizedRegion)
        {
            if (dataset == null)
                throw new ArgumentNullException(nameof(dataset));
            if (timeIndex < 0 || timeIndex >= dataset.TimeCount)
                throw new ArgumentOutOfRangeException(nameof(timeIndex));

            VolumeSTCubeRawSlice slice = ReadSlice(
                dataset.RawPaths[timeIndex], dataset.IniPaths[timeIndex], zIndex);
            Directory.CreateDirectory(outputDirectory);
            string safeName = Regex.Replace(dataset.Name ?? "dataset", @"[^A-Za-z0-9._-]", "_");
            string path = Path.Combine(outputDirectory, safeName + "_t" + timeIndex + "_z" + zIndex + "_region.csv");
            normalizedRegion.xMin = Mathf.Clamp01(normalizedRegion.xMin);
            normalizedRegion.xMax = Mathf.Clamp01(normalizedRegion.xMax);
            normalizedRegion.yMin = Mathf.Clamp01(normalizedRegion.yMin);
            normalizedRegion.yMax = Mathf.Clamp01(normalizedRegion.yMax);

            using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(false), 1024 * 1024))
            {
                writer.WriteLine("x,y,value,region");
                for (int y = 0; y < slice.Height; y++)
                {
                    float ny = (y + 0.5f) / slice.Height;
                    for (int x = 0; x < slice.Width; x++)
                    {
                        float nx = (x + 0.5f) / slice.Width;
                        bool selected = normalizedRegion.Contains(new Vector2(nx, ny));
                        writer.Write(x.ToString(CultureInfo.InvariantCulture));
                        writer.Write(',');
                        writer.Write(y.ToString(CultureInfo.InvariantCulture));
                        writer.Write(',');
                        writer.Write(slice.Values[x + y * slice.Width].ToString("R", CultureInfo.InvariantCulture));
                        writer.Write(',');
                        writer.WriteLine(selected ? "selected" : "rest");
                    }
                }
            }
            return path;
        }

        private static bool HasSameLayout(DatasetIniData left, DatasetIniData right)
        {
            return left.dimX == right.dimX &&
                   left.dimY == right.dimY &&
                   left.dimZ == right.dimZ &&
                   left.format == right.format &&
                   left.endianness == right.endianness &&
                   left.bytesToSkip == right.bytesToSkip;
        }

        private static int CompareNaturalPaths(string left, string right)
        {
            string a = Path.GetFileName(left);
            string b = Path.GetFileName(right);
            MatchCollection aNumbers = NumberPattern.Matches(a);
            MatchCollection bNumbers = NumberPattern.Matches(b);
            int shared = Mathf.Min(aNumbers.Count, bNumbers.Count);
            for (int i = 0; i < shared; i++)
            {
                if (long.TryParse(aNumbers[i].Value, out long av) &&
                    long.TryParse(bNumbers[i].Value, out long bv) && av != bv)
                    return av.CompareTo(bv);
            }
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetSampleSize(DataContentFormat format)
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
                    throw new NotSupportedException("Unsupported RAW format: " + format);
            }
        }

        private static float ReadValue(BinaryReader reader, DataContentFormat format, Endianness endianness)
        {
            switch (format)
            {
                case DataContentFormat.Int8:
                    return reader.ReadSByte();
                case DataContentFormat.Uint8:
                    return reader.ReadByte();
                case DataContentFormat.Int16:
                    return ReadInt16(reader, endianness);
                case DataContentFormat.Uint16:
                    return ReadUInt16(reader, endianness);
                case DataContentFormat.Int32:
                    return ReadInt32(reader, endianness);
                case DataContentFormat.Uint32:
                    return ReadUInt32(reader, endianness);
                default:
                    throw new NotSupportedException("Unsupported RAW format: " + format);
            }
        }

        private static short ReadInt16(BinaryReader reader, Endianness endianness)
        {
            byte[] bytes = reader.ReadBytes(2);
            MaybeReverse(bytes, endianness);
            return BitConverter.ToInt16(bytes, 0);
        }

        private static ushort ReadUInt16(BinaryReader reader, Endianness endianness)
        {
            byte[] bytes = reader.ReadBytes(2);
            MaybeReverse(bytes, endianness);
            return BitConverter.ToUInt16(bytes, 0);
        }

        private static int ReadInt32(BinaryReader reader, Endianness endianness)
        {
            byte[] bytes = reader.ReadBytes(4);
            MaybeReverse(bytes, endianness);
            return BitConverter.ToInt32(bytes, 0);
        }

        private static uint ReadUInt32(BinaryReader reader, Endianness endianness)
        {
            byte[] bytes = reader.ReadBytes(4);
            MaybeReverse(bytes, endianness);
            return BitConverter.ToUInt32(bytes, 0);
        }

        private static void MaybeReverse(byte[] bytes, Endianness endianness)
        {
            bool sourceIsLittleEndian = endianness == Endianness.LittleEndian;
            if (sourceIsLittleEndian != BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
        }

        private static Color32 EvaluateColor(float value, bool atMinimum)
        {
            if (atMinimum)
                return new Color32(7, 12, 24, 255);
            Color low = new Color(0.08f, 0.16f, 0.52f, 1.0f);
            Color middle = new Color(0.0f, 0.82f, 0.78f, 1.0f);
            Color high = new Color(1.0f, 0.86f, 0.18f, 1.0f);
            return value < 0.5f
                ? (Color32)Color.Lerp(low, middle, value * 2.0f)
                : (Color32)Color.Lerp(middle, high, (value - 0.5f) * 2.0f);
        }
    }
}
