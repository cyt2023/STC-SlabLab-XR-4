using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace UnityVolumeRendering
{
    internal static class VolumeSTCubeCsvRawProcessor
    {
        internal static bool TryProcess(VolumeSTCubeData source, VolumeSTCubeConfig config, out VolumeSTCubeData rawData)
        {
            rawData = null;

            if (source == null || string.IsNullOrEmpty(source.csvFilePath) || !File.Exists(source.csvFilePath))
            {
                Debug.LogError($"VolumeSTCubeCsvRawProcessor failed: CSV file does not exist: {source?.csvFilePath}");
                return false;
            }

            string[] lines = File.ReadAllLines(source.csvFilePath);
            if (lines.Length < 2)
            {
                Debug.LogError($"VolumeSTCubeCsvRawProcessor failed: CSV must contain a header and at least one data row: {source.csvFilePath}");
                return false;
            }

            string[] headers = SplitCsvLine(lines[0]);
            int xIndex = FindHeader(headers, source.csvXColumn, "x", "lng", "longitude", "lon");
            int yIndex = FindHeader(headers, source.csvYColumn, "y", "lat", "latitude");
            int valueIndex = FindHeader(headers, source.csvVariableColumn, "z", "value", "val", "variable", "content", "amount");
            int tIndex = FindHeader(headers, source.csvTColumn, "t", "time", "time_index", "timestamp");
            bool hasTime = tIndex >= 0;

            if (xIndex < 0 || yIndex < 0 || valueIndex < 0)
            {
                Debug.LogError("VolumeSTCubeCsvRawProcessor failed: CSV header must include x/y/value columns. Accepted aliases include x/lng/longitude, y/lat/latitude, and z/value/val/variable.");
                return false;
            }

            int rowCount = CountDataRows(lines);
            if (rowCount <= 0)
            {
                Debug.LogError($"VolumeSTCubeCsvRawProcessor failed: CSV contains no usable data rows: {source.csvFilePath}");
                return false;
            }

            float[] xs = new float[rowCount];
            float[] ys = new float[rowCount];
            float[] ts = new float[rowCount];
            float[] values = new float[rowCount];

            int count = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;

                string[] parts = SplitCsvLine(lines[i]);
                if (!TryGetFloat(parts, xIndex, out float x) ||
                    !TryGetFloat(parts, yIndex, out float y) ||
                    !TryGetFloat(parts, valueIndex, out float value) ||
                    (hasTime && !TryGetFloat(parts, tIndex, out ts[count])))
                {
                    Debug.LogError($"VolumeSTCubeCsvRawProcessor failed: invalid numeric value at CSV line {i + 1}.");
                    return false;
                }

                xs[count] = x;
                ys[count] = y;
                if (!hasTime)
                    ts[count] = 0.0f;
                values[count] = value;
                count++;
            }

            int dimX = Mathf.Clamp(config != null ? config.pointGridDimX : 128, 2, 2048);
            int dimY = Mathf.Clamp(config != null ? config.pointGridDimY : 128, 2, 2048);
            int dimZ = hasTime ? Mathf.Clamp(config != null ? config.pointGridDimT : 32, 2, 2048) : 1;
            int radius = Mathf.Clamp(config != null ? config.pointSplatRadius : 1, 0, 8);
            int voxelCount = dimX * dimY * dimZ;

            float minX = Min(xs, count);
            float maxX = Max(xs, count);
            float minY = Min(ys, count);
            float maxY = Max(ys, count);
            float minT = Min(ts, count);
            float maxT = Max(ts, count);
            float minV = Min(values, count);
            float maxV = Max(values, count);

            float[] sum = new float[voxelCount];
            float[] weight = new float[voxelCount];

            for (int i = 0; i < count; i++)
            {
                int cx = ToGrid(xs[i], minX, maxX, dimX);
                int cy = ToGrid(ys[i], minY, maxY, dimY);
                int cz = hasTime ? ToGrid(ts[i], minT, maxT, dimZ) : 0;

                for (int dz = -radius; dz <= radius; dz++)
                {
                    int z = cz + dz;
                    if (z < 0 || z >= dimZ)
                        continue;

                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        int y = cy + dy;
                        if (y < 0 || y >= dimY)
                            continue;

                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int x = cx + dx;
                            if (x < 0 || x >= dimX)
                                continue;

                            float distance = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                            if (distance > radius + 0.001f)
                                continue;

                            float w = radius == 0 ? 1.0f : 1.0f - (distance / (radius + 1.0f));
                            int index = x + y * dimX + z * dimX * dimY;
                            sum[index] += values[i] * w;
                            weight[index] += w;
                        }
                    }
                }
            }

            byte[] rawBytes = new byte[voxelCount];
            for (int i = 0; i < voxelCount; i++)
            {
                if (weight[i] <= 0.0f)
                {
                    rawBytes[i] = 0;
                    continue;
                }

                float value = sum[i] / weight[i];
                rawBytes[i] = (byte)Mathf.RoundToInt(Mathf.Lerp(1.0f, 223.0f, Normalize(value, minV, maxV)));
            }

            string outputDir = Path.Combine(Application.persistentDataPath, "VolumeSTCubeGeneratedRaw");
            Directory.CreateDirectory(outputDir);
            string baseName = MakeSafeFileName(string.IsNullOrEmpty(source.datasetName) ? Path.GetFileNameWithoutExtension(source.csvFilePath) : source.datasetName);
            string rawPath = Path.Combine(outputDir, $"{baseName}_{dimX}x{dimY}x{dimZ}.raw");
            string iniPath = rawPath + ".ini";

            File.WriteAllBytes(rawPath, rawBytes);
            File.WriteAllText(iniPath, $"dimx:{dimX}\ndimy:{dimY}\ndimz:{dimZ}\nskip:0\nformat:uint8");

            rawData = new VolumeSTCubeData
            {
                datasetName = source.datasetName,
                csvFilePath = source.csvFilePath
            };
            rawData.rawFilePaths.Add(rawPath);
            rawData.iniFilePaths.Add(iniPath);

            Debug.Log($"VolumeSTCubeCsvRawProcessor generated raw dataset from CSV: {rawPath}");
            return true;
        }

        private static int CountDataRows(string[] lines)
        {
            int count = 0;
            for (int i = 1; i < lines.Length; i++)
                if (!string.IsNullOrWhiteSpace(lines[i]))
                    count++;
            return count;
        }

        private static string[] SplitCsvLine(string line)
        {
            return line.Split(',');
        }

        private static int FindHeader(string[] headers, params string[] names)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                string header = headers[i].Trim().ToLowerInvariant();
                for (int j = 0; j < names.Length; j++)
                {
                    if (!string.IsNullOrEmpty(names[j]) && header == names[j].Trim().ToLowerInvariant())
                        return i;
                }
            }

            return -1;
        }

        private static bool TryGetFloat(string[] parts, int index, out float value)
        {
            value = 0.0f;
            if (index < 0 || index >= parts.Length)
                return false;

            return float.TryParse(parts[index].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static int ToGrid(float value, float min, float max, int dimension)
        {
            return Mathf.Clamp(Mathf.RoundToInt(Normalize(value, min, max) * (dimension - 1)), 0, dimension - 1);
        }

        private static float Normalize(float value, float min, float max)
        {
            if (Mathf.Approximately(min, max))
                return 0.5f;
            return Mathf.Clamp01((value - min) / (max - min));
        }

        private static float Min(float[] values, int count)
        {
            float min = float.MaxValue;
            for (int i = 0; i < count; i++)
                min = Mathf.Min(min, values[i]);
            return min;
        }

        private static float Max(float[] values, int count)
        {
            float max = float.MinValue;
            for (int i = 0; i < count; i++)
                max = Mathf.Max(max, values[i]);
            return max;
        }

        private static string MakeSafeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return string.IsNullOrEmpty(value) ? "csv_volume" : value;
        }
    }
}
