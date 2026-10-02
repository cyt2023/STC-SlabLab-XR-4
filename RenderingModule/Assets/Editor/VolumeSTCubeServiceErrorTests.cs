using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityVolumeRendering;

namespace UnityVolumeRendering.Tests
{
    /// <summary>
    /// Guards for the sentence shown when one of the local services refuses a
    /// request. The packaging README promises that a failure names the window,
    /// the field and the folder it looked in — that only holds if the service's
    /// `detail` survives the trip to the status line instead of arriving as
    /// escaped JSON, and if all three clients ask the same helper for it.
    /// </summary>
    public sealed class VolumeSTCubeServiceErrorTests
    {
        [Test]
        public void TheServiceSentenceSurvivesTheBody()
        {
            // The exact shape /wave/live-dataset answers with (measured).
            const string body =
                "{\"detail\":\"Wave API key is missing and no bundled dataset " +
                "covers 2019-11-30T00:00:00Z (field hs). No bundled datasets " +
                "found in /tmp/empty-wave-cache.\"}";
            string detail = SlabLabServiceError.Detail(body, "HTTP 422");
            StringAssert.StartsWith("Wave API key is missing", detail);
            StringAssert.Contains("no bundled dataset covers", detail);
            Assert.IsFalse(detail.Contains("{"), "the JSON braces must be gone");
            Assert.IsFalse(detail.Contains("\\\""), "escapes must be gone");

            // And a body the S4D service really answers with.
            Assert.IsTrue(SlabLabServiceError.TryDetail(
                "{\"detail\":\"Unknown analysis job: does-not-exist\"}",
                out string jobDetail));
            Assert.AreEqual("Unknown analysis job: does-not-exist", jobDetail);
        }

        [Test]
        public void AnythingElseIsStillSaidOutLoud()
        {
            // Not JSON (a proxy or an HTML error page): keep the body.
            Assert.AreEqual("503 Service Unavailable",
                SlabLabServiceError.Detail("  503 Service Unavailable  ",
                    "HTTP 503"));
            // Nothing at all: fall back to what Unity reported.
            Assert.AreEqual("Cannot connect to destination host",
                SlabLabServiceError.Detail(string.Empty,
                    "Cannot connect to destination host"));
            // JSON, but without the field we look for.
            Assert.AreEqual("{\"error\":\"nope\"}",
                SlabLabServiceError.Detail("{\"error\":\"nope\"}", "HTTP 500"));
            // TryDetail says so rather than inventing a sentence.
            Assert.IsFalse(SlabLabServiceError.TryDetail("{\"error\":\"nope\"}",
                out _));
            Assert.IsFalse(SlabLabServiceError.TryDetail(string.Empty, out _));
        }

        [Test]
        public void EveryClientUsesItInsteadOfTheRawBody()
        {
            string wave = ClientSource("VolumeSTCubeWaveClient.cs");
            StringAssert.Contains(
                "SlabLabServiceError.Detail(response, request.error)", wave);
            Assert.IsFalse(wave.Contains("error += \"  \" + response"),
                "the Wave client must not dump the raw body");

            string matplot = ClientSource("VolumeSTCubeMatPlotClient.cs");
            StringAssert.Contains("SlabLabServiceError.Detail(body, request.error)",
                matplot);
            Assert.IsFalse(matplot.Contains("request.error + \" | \" + body"),
                "the MatPlot client must not dump the raw body");

            string s4d = ClientSource("VolumeSTCubeS4DAnalysisClient.cs");
            StringAssert.Contains("SlabLabServiceError.TryDetail(body, out string detail)",
                s4d);
            Assert.IsFalse(s4d.Contains("ExtractDetail"),
                "the hand-rolled parser belongs to the shared helper now");
        }

        [Test]
        public void AFailureCarriesTheFixWithIt()
        {
            const string hint =
                "The backend is not installed. Run Setup Backend.command once, " +
                "then start the app again.";
            string combined = SlabLabServiceError.WithSetupHint(
                "Wave import failed: Cannot connect to destination host", hint);
            StringAssert.Contains("Cannot connect to destination host", combined);
            StringAssert.Contains("Run Setup Backend.command once", combined);

            // A healthy run says nothing extra, and a message that already
            // explains itself is not repeated.
            Assert.AreEqual("Wave import failed: x",
                SlabLabServiceError.WithSetupHint("Wave import failed: x",
                    string.Empty));
            Assert.AreEqual("Wave import failed: x",
                SlabLabServiceError.WithSetupHint("Wave import failed: x", null));
            Assert.AreEqual("Run Setup Backend.command once",
                SlabLabServiceError.WithSetupHint(string.Empty,
                    "Run Setup Backend.command once"));
            string once = SlabLabServiceError.WithSetupHint(hint, hint);
            Assert.AreEqual(hint, once, "the hint must not be appended twice");
        }

        private static string ClientSource(string fileName)
        {
            return File.ReadAllText(Path.GetFullPath(Path.Combine(
                Application.dataPath, "VolumeSTCubeAPI", fileName)));
        }

        [MenuItem("VolumeSTCube/Desktop/Validate Service Error")]
        public static void RunChecks()
        {
            var tests = new VolumeSTCubeServiceErrorTests();
            tests.TheServiceSentenceSurvivesTheBody();
            tests.AnythingElseIsStillSaidOutLoud();
            tests.EveryClientUsesItInsteadOfTheRawBody();
            tests.AFailureCarriesTheFixWithIt();
            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "../../.runtime/service-error-validation.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "PASS: 4 service-error guards\n" +
                DateTime.UtcNow.ToString("O"));
            Debug.Log("PASS: 4 service-error guards");
        }
    }
}
