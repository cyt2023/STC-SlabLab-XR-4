using System;
using System.IO;
using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Saves what is on screen to a PNG, so a demo or a write-up gets a file
    /// instead of a full-desktop screenshot. Files follow the project's
    /// convention of living under Application.persistentDataPath, which is
    /// always writable and never a system-prompted location.
    /// </summary>
    public static class SlabLabSnapshot
    {
        public const string FolderName = "Captures";
        private static readonly byte[] PngSignature =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
        };

        public static string Folder()
        {
            return Path.Combine(Application.persistentDataPath, FolderName);
        }

        /// <summary>
        /// A free file name for the given moment. Two shots inside the same
        /// millisecond, or a shot taken while an earlier file is still there,
        /// must not overwrite each other.
        /// </summary>
        public static string NextPath(DateTime stamp)
        {
            string folder = Folder();
            string stem = "slablab-" + stamp.ToString("yyyyMMdd-HHmmss-fff");
            string path = Path.Combine(folder, stem + ".png");
            for (int index = 1; index < 100 && File.Exists(path); index++)
                path = Path.Combine(folder, stem + "-" + index + ".png");
            return path;
        }

        /// <summary>True when the file really starts with the PNG signature.</summary>
        public static bool IsPng(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return false;
                using (FileStream stream = File.OpenRead(path))
                {
                    if (stream.Length < PngSignature.Length)
                        return false;
                    for (int index = 0; index < PngSignature.Length; index++)
                    {
                        if (stream.ReadByte() != PngSignature[index])
                            return false;
                    }
                }
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
