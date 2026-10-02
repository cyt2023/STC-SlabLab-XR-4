namespace UnityVolumeRendering
{
    /// <summary>
    /// One line per control, shown when the pointer rests on it.
    ///
    /// The Help card lists the keys, but nothing told an operator what a control
    /// *does* — "Reset View" only recently started restoring the camera and the
    /// Field pair as well as the volume, and "Snapshot" writes to a folder nobody
    /// would guess. A hint is the cheapest place to say so, and keeping the texts
    /// in one table means a guard can check them against the real button labels.
    /// </summary>
    public static class SlabLabHints
    {
        /// <summary>
        /// The hint for a button caption, or null when the control needs none.
        /// Prefix matching covers labels that carry a count, like "History (2)".
        /// </summary>
        public static string For(string caption)
        {
            if (string.IsNullOrEmpty(caption))
                return null;
            if (StartsWith(caption, "Previous"))
                return "Go back one step  ·  Page Up";
            if (StartsWith(caption, "Next"))
                return "Continue to the next step  ·  Page Down";
            if (StartsWith(caption, "Reset View"))
                return "Restore the authored camera and re-centre both " +
                    "Fields  ·  X";
            if (StartsWith(caption, "Help"))
                return "Every keyboard shortcut  ·  H or F1";
            if (StartsWith(caption, "History"))
                return "Reopen an analysis you already committed this session";
            if (StartsWith(caption, "Snapshot"))
                return "Save this screen as a PNG in Captures  ·  P";
            if (StartsWith(caption, "SET TIME RANGE"))
                return "Author the time span the analysis will use";
            if (StartsWith(caption, "CONFIRM TIME RANGE"))
                return "Keep this range and move on  ·  type one with T";
            if (StartsWith(caption, "MATPLOT INTENT"))
                return "Describe the question this chart should answer";
            if (StartsWith(caption, "FULL MATRIX"))
                return "Run every facet combination through S4D";
            if (StartsWith(caption, "PLAY"))
                return "Play the surface timeline";
            if (StartsWith(caption, "SPEED"))
                return "Cycle the playback speed";
            if (StartsWith(caption, "BACK"))
                return "Leave this edit without applying it";
            return null;
        }

        private static bool StartsWith(string caption, string prefix)
        {
            return caption.StartsWith(prefix,
                System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
