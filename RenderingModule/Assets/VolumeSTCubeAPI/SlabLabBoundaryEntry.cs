using System.Globalization;
using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Parsing and clamping for a typed Time range. Pure — no scene, no state —
    /// so the typed path is checkable without entering Play Mode, and so a typed
    /// range can only land on values the dragged path can also reach.
    /// </summary>
    public static class SlabLabBoundaryEntry
    {
        public const string TimePrompt = "TYPE CUT DAYS, THEN ENTER";
        public const string TimeUnavailable =
            "Numeric entry types the Time range. Open Set Time Range in Step 2 " +
            "first.";
        public const string TimeInvalid =
            "Enter two cut days. Esc cancels.";

        /// <summary>
        /// The operator types the two boundary days exactly as the readout shows
        /// them: the last day of BEFORE and the last day of DURING, so "9,20"
        /// means before 1-9 / during 10-20. Returns the internal cut indices,
        /// clamped the same way PreviewForVrCombinedTimeRange clamps a drag.
        /// </summary>
        public static bool TryParseTimeCuts(string text, int timeCount,
            out int start, out int end)
        {
            start = 0;
            end = 0;
            if (timeCount < 2 || !TryParsePair(text, out int first, out int second))
                return false;
            int last = Mathf.Max(1, timeCount - 1);
            int lowDay = Mathf.Clamp(Mathf.Min(first, second), 1,
                Mathf.Max(1, last - 1));
            int highDay = Mathf.Clamp(Mathf.Max(first, second), lowDay + 1, last);
            start = lowDay;
            end = highDay - 1;
            return true;
        }

        /// <summary>An example pair that is valid for this dataset.</summary>
        public static string ExampleLine(int timeCount)
        {
            if (timeCount < 3)
                return "e.g. 1,2";
            return "e.g. 3," + Mathf.Min(timeCount - 1, 14);
        }

        private static bool TryParsePair(string text, out int first,
            out int second)
        {
            first = 0;
            second = 0;
            if (string.IsNullOrEmpty(text))
                return false;
            string[] parts = text.Split(
                new[] { ',', ';', '/', '.', ' ', '\t' },
                System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                return false;
            return int.TryParse(parts[0], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out first) &&
                int.TryParse(parts[1], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out second);
        }
    }
}
