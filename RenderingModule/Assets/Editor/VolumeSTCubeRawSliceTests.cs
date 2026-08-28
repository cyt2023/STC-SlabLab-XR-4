using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace UnityVolumeRendering.Tests
{
    public sealed class VolumeSTCubeRawSliceTests
    {
        private string temporaryDirectory;

        [SetUp]
        public void SetUp()
        {
            temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "VolumeSTCubeRawSliceTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, true);
        }

        [Test]
        public void ReadSlice_UsesXYZOffsetAndExportsXYCsv()
        {
            string rawPath = Path.Combine(temporaryDirectory, "sample_time_0.raw");
            File.WriteAllBytes(rawPath, new byte[]
            {
                0, 1, 2, 3, 4, 5,
                10, 11, 12, 13, 14, 15
            });
            File.WriteAllText(
                rawPath + ".ini",
                "dimx:3\ndimy:2\ndimz:2\nskip:0\nformat:uint8\n");

            Assert.IsTrue(VolumeSTCubeRawSliceReader.TryOpenDataset(
                temporaryDirectory,
                out VolumeSTCubeSliceDataset dataset,
                out string error), error);
            VolumeSTCubeRawSlice slice = VolumeSTCubeRawSliceReader.ReadSlice(rawPath, rawPath + ".ini", 1);

            Assert.AreEqual(3, slice.Width);
            Assert.AreEqual(2, slice.Height);
            CollectionAssert.AreEqual(new float[] { 10, 11, 12, 13, 14, 15 }, slice.Values);
            string csvPath = VolumeSTCubeRawSliceReader.ExportCsv(dataset, 0, 1, temporaryDirectory);
            string[] lines = File.ReadAllLines(csvPath);
            Assert.AreEqual("x,y,value", lines[0]);
            Assert.AreEqual("0,0,10", lines[1]);
            Assert.AreEqual("2,1,15", lines[6]);
        }

        [Test]
        public void TryOpenDataset_SortsExplicitTimeNumbersNaturally()
        {
            CreateSingleVoxelTimeFile("volume_NO3_data_time_10_255.raw", 10);
            CreateSingleVoxelTimeFile("volume_NO3_data_time_2_255.raw", 2);
            CreateSingleVoxelTimeFile("volume_NO3_data_time_1_255.raw", 1);

            Assert.IsTrue(VolumeSTCubeRawSliceReader.TryOpenDataset(
                temporaryDirectory,
                out VolumeSTCubeSliceDataset dataset,
                out string error), error);

            Assert.AreEqual("t=1", dataset.GetTimeLabel(0));
            Assert.AreEqual("t=2", dataset.GetTimeLabel(1));
            Assert.AreEqual("t=10", dataset.GetTimeLabel(2));
        }

        [Test]
        public void CreatePreviewTexture_DownsamplesWithoutChangingSliceData()
        {
            string rawPath = Path.Combine(temporaryDirectory, "preview_time_0.raw");
            File.WriteAllBytes(rawPath, new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });
            File.WriteAllText(
                rawPath + ".ini",
                "dimx:4\ndimy:2\ndimz:1\nskip:0\nformat:uint8\n");
            VolumeSTCubeRawSlice slice = VolumeSTCubeRawSliceReader.ReadSlice(rawPath, rawPath + ".ini", 0);

            Texture2D texture = VolumeSTCubeRawSliceReader.CreatePreviewTexture(slice, 2, 2, false);
            try
            {
                Assert.NotNull(texture);
                Assert.AreEqual(2, texture.width);
                Assert.AreEqual(1, texture.height);
                CollectionAssert.AreEqual(new float[] { 0, 1, 2, 3, 4, 5, 6, 7 }, slice.Values);
            }
            finally
            {
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private void CreateSingleVoxelTimeFile(string name, byte value)
        {
            string rawPath = Path.Combine(temporaryDirectory, name);
            File.WriteAllBytes(rawPath, new[] { value });
            File.WriteAllText(
                rawPath + ".ini",
                "dimx:1\ndimy:1\ndimz:1\nskip:0\nformat:uint8\n");
        }
    }
}
