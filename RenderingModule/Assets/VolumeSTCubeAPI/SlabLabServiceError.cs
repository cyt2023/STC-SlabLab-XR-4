using System;
using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>
    /// The sentence out of a service error body.
    ///
    /// All three local services are FastAPI and answer failures as
    /// `{"detail": "..."}`. Showing that body raw buries the sentence in quotes
    /// and escapes and overruns the status line, so every client asks this helper
    /// for the sentence instead. The Wave client used to dump the body, the S4D
    /// client had its own hand-rolled parser, and the MatPlot client did both —
    /// one helper keeps the three of them honest.
    /// </summary>
    public static class SlabLabServiceError
    {
        [Serializable]
        private sealed class Payload
        {
            public string detail;
        }

        /// <summary>The service's own sentence, when the body carries one.</summary>
        public static bool TryDetail(string body, out string detail)
        {
            detail = string.Empty;
            if (string.IsNullOrWhiteSpace(body))
                return false;
            try
            {
                Payload payload = JsonUtility.FromJson<Payload>(body);
                if (payload != null && !string.IsNullOrWhiteSpace(payload.detail))
                {
                    detail = payload.detail;
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // Not JSON: an HTML page or a plain-text proxy message.
            }
            return false;
        }

        /// <summary>
        /// The operator-facing sentence: the `detail` when the body carries one,
        /// the body itself when it is not that shape, and the caller's own
        /// message when there is no body at all.
        /// </summary>
        public static string Detail(string body, string fallback)
        {
            if (TryDetail(body, out string detail))
                return detail;
            return string.IsNullOrWhiteSpace(body) ? fallback : body.Trim();
        }

        /// <summary>
        /// Add the launcher's explanation to a failure that it caused. A teacher
        /// who forgot to run `Setup Backend.command` used to read "cannot connect"
        /// on the opening screen while the reason sat in the log; the two belong
        /// together on screen.
        /// </summary>
        public static string WithSetupHint(string message, string hint)
        {
            if (string.IsNullOrWhiteSpace(hint))
                return message;
            if (string.IsNullOrEmpty(message))
                return hint;
            if (message.Contains(hint))
                return message;
            return message + "   ·   " + hint;
        }
    }
}
