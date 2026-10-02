using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityVolumeRendering;

namespace UnityVolumeRendering.Tests
{
    /// <summary>
    /// Guards for the Snapshot export. The Play-mode run proves a real PNG comes
    /// out of the button; these keep the naming and validation rules honest in
    /// milliseconds, so a file can never be overwritten or reported as a
    /// snapshot when it is not one.
    /// </summary>
    public sealed class VolumeSTCubeExportTests
    {
        [Test]
        public void SnapshotNamesAreUniqueAndLandInTheCapturesFolder()
        {
            var stamp = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Local);
            string first = SlabLabSnapshot.NextPath(stamp);

            Assert.AreEqual(SlabLabSnapshot.Folder(),
                Path.GetDirectoryName(first));
            Assert.AreEqual(".png", Path.GetExtension(first));
            StringAssert.Contains("20260924-120000", Path.GetFileName(first));

            // A second shot inside the same millisecond must not overwrite the
            // first one.
            Directory.CreateDirectory(SlabLabSnapshot.Folder());
            File.WriteAllBytes(first, new byte[] { 1, 2, 3 });
            try
            {
                string second = SlabLabSnapshot.NextPath(stamp);
                Assert.AreNotEqual(first, second);
            }
            finally
            {
                File.Delete(first);
            }
        }

        [Test]
        public void OnlyRealPngFilesCountAsSnapshots()
        {
            string folder = SlabLabSnapshot.Folder();
            Directory.CreateDirectory(folder);
            string real = Path.Combine(folder, "guard-real.png");
            string junk = Path.Combine(folder, "guard-junk.png");
            byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            var bytes = new byte[signature.Length + 32];
            Array.Copy(signature, bytes, signature.Length);
            try
            {
                File.WriteAllBytes(real, bytes);
                File.WriteAllBytes(junk, new byte[] { 0x00, 0x01, 0x02, 0x03 });
                Assert.IsTrue(SlabLabSnapshot.IsPng(real));
                Assert.IsFalse(SlabLabSnapshot.IsPng(junk),
                    "A file without the PNG signature is not a snapshot");
                Assert.IsFalse(SlabLabSnapshot.IsPng(
                    Path.Combine(folder, "guard-missing.png")));
            }
            finally
            {
                File.Delete(real);
                File.Delete(junk);
            }
        }

        [Test]
        public void TheHudWiresTheSnapshotButton()
        {
            Type hud = typeof(VolumeSTCubeFlatScreenHUD);
            Assert.IsNotNull(hud.GetMethod("SaveSnapshot",
                BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.IsNotNull(hud.GetMethod("SaveSnapshotRoutine",
                BindingFlags.Instance | BindingFlags.NonPublic));
            // The notice surface is how the operator learns where the file went.
            Assert.IsNotNull(hud.GetMethod("ShowNotice",
                BindingFlags.Public | BindingFlags.Static));
        }

        [MenuItem("VolumeSTCube/Desktop/Validate Export")]
        public static void RunChecks()
        {
            var tests = new VolumeSTCubeExportTests();
            tests.SnapshotNamesAreUniqueAndLandInTheCapturesFolder();
            tests.OnlyRealPngFilesCountAsSnapshots();
            tests.TheHudWiresTheSnapshotButton();
            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath, "../../.runtime/export-validation.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "PASS: 3 export guards\n" +
                DateTime.UtcNow.ToString("O"));
            Debug.Log("PASS: 3 export guards");
        }
    }
}
