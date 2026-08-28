using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Input model accepted by VolumeSTCubeAPI. Populate exactly one source kind:
    /// RAW/INI paths, a CSV path, or parallel x/y/t/value arrays.
    /// </summary>
    [Serializable]
    public class VolumeSTCubeData
    {
        public string datasetName;
        public string csvFilePath;
        public string csvXColumn = "x";
        public string csvYColumn = "y";
        public string csvTColumn = "t";
        public string csvVariableColumn = "variable";
        public bool preprocessCsvToRaw = false;
        /// <summary>Ordered RAW files. Order is significant for both supported layouts.</summary>
        public List<string> rawFilePaths = new List<string>();
        /// <summary>INI path at index i describes RAW path at index i.</summary>
        public List<string> iniFilePaths = new List<string>();

        public List<float> x = new List<float>();
        public List<float> y = new List<float>();
        public List<float> t = new List<float>();
        public List<float> variable = new List<float>();

        public bool Validate()
        {
            if (HasRawFiles())
            {
                return true;
            }

            if (HasCsvFile())
            {
                if (!File.Exists(csvFilePath))
                {
                    Debug.LogError($"VolumeSTCubeData validation failed: CSV file does not exist: {csvFilePath}");
                    return false;
                }

                return true;
            }

            if (!HasPointData())
            {
                Debug.LogError("VolumeSTCubeData validation failed: provide rawFilePaths, csvFilePath, or point data x/y/t arrays.");
                return false;
            }

            int count = x.Count;
            if (y.Count != count || t.Count != count)
            {
                Debug.LogError("VolumeSTCubeData validation failed: x, y, and t arrays must have the same length.");
                return false;
            }

            if (variable != null && variable.Count > 0 && variable.Count != count)
            {
                Debug.LogError("VolumeSTCubeData validation failed: variable array must be empty or have the same length as x.");
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                if (!IsFinite(x[i]) || !IsFinite(y[i]) || !IsFinite(t[i]))
                {
                    Debug.LogError($"VolumeSTCubeData validation failed: point {i} contains NaN or Infinity.");
                    return false;
                }

                if (variable != null && variable.Count > 0 && !IsFinite(variable[i]))
                {
                    Debug.LogError($"VolumeSTCubeData validation failed: variable value {i} contains NaN or Infinity.");
                    return false;
                }
            }

            return true;
        }

        public int Count()
        {
            if (HasRawFiles())
                return rawFilePaths.Count;
            if (HasPointData())
                return x.Count;
            return 0;
        }

        public bool HasRawFiles()
        {
            return rawFilePaths != null && rawFilePaths.Count > 0;
        }

        public bool HasPointData()
        {
            return x != null && y != null && t != null && x.Count > 0;
        }

        public bool HasCsvFile()
        {
            return !string.IsNullOrEmpty(csvFilePath);
        }

        internal VolumeSTCubeData CopyForLoad()
        {
            VolumeSTCubeData copy = (VolumeSTCubeData)MemberwiseClone();
            copy.rawFilePaths = rawFilePaths != null
                ? new List<string>(rawFilePaths)
                : new List<string>();
            copy.iniFilePaths = iniFilePaths != null
                ? new List<string>(iniFilePaths)
                : new List<string>();
            copy.x = x != null ? new List<float>(x) : new List<float>();
            copy.y = y != null ? new List<float>(y) : new List<float>();
            copy.t = t != null ? new List<float>(t) : new List<float>();
            copy.variable = variable != null ? new List<float>(variable) : new List<float>();
            return copy;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
