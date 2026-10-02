using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Everything SlabLab persists in PlayerPrefs, in one place. The keys, their
    /// defaults and the Save() calls used to be spelled out at each call site;
    /// they are unchanged here, only collected.
    /// </summary>
    /// <summary>
    /// The app's persisted settings. Public so the editor guards can drive the
    /// same API the app uses instead of copying preference keys into the tests.
    /// </summary>
    public static class SlabLabSettings
    {
        private const string SpatialPromptKey = "VolumeSTCube.Quest.SpatialPrompt";
        private const string BarPromptMigrationKey =
            "VolumeSTCube.Quest.BarPromptMigrationV2";
        private const string MatPlotUrlKey = "VolumeSTCube.Quest.MatPlotUrl";
        private const string WaveUrlKey = "VolumeSTCube.Quest.S4DUrl";
        private const string DatasetRootKey = "VolumeSTCube.Quest.DatasetRoot";
        private const string WaveStartUtcKey = "VolumeSTCube.Wave.StartUtc";
        private const string WaveEndUtcKey = "VolumeSTCube.Wave.EndUtc";

        /// <summary>Timeline used when the operator has not stored one yet.</summary>
        public const string DefaultWaveStartUtc = "2019-11-30T00:00:00Z";
        public const string DefaultWaveEndUtc = "2019-12-01T00:00:00Z";

        /// <summary>Last prompt used; the caller's own value is the fallback.</summary>
        public static string GetSpatialPrompt(string fallback)
        {
            return PlayerPrefs.GetString(SpatialPromptKey, fallback);
        }

        public static void SetSpatialPrompt(string value)
        {
            PlayerPrefs.SetString(SpatialPromptKey, value);
            PlayerPrefs.Save();
        }

        /// <summary>Zero until the bar-chart test prompt has been installed once.</summary>
        public static int BarPromptMigrationVersion
        {
            get { return PlayerPrefs.GetInt(BarPromptMigrationKey, 0); }
        }

        /// <summary>Stores the prompt and the migration mark in a single save.</summary>
        public static void SaveMigratedBarPrompt(string value)
        {
            PlayerPrefs.SetString(SpatialPromptKey, value);
            PlayerPrefs.SetInt(BarPromptMigrationKey, 1);
            PlayerPrefs.Save();
        }

        public static string GetMatPlotUrl(string fallback)
        {
            return PlayerPrefs.GetString(MatPlotUrlKey, fallback);
        }

        public static string GetWaveUrl(string fallback)
        {
            return PlayerPrefs.GetString(WaveUrlKey, fallback);
        }

        /// <summary>
        /// Drop any stored override so the app goes back to its built-in
        /// gateway. The setting is sticky and easy to leave behind — which is
        /// exactly what happened to a diagnostic run of ours — so there has to be
        /// a way home that does not require finding the preference by hand.
        /// </summary>
        public static void ClearWaveUrl()
        {
            PlayerPrefs.DeleteKey(WaveUrlKey);
            PlayerPrefs.Save();
        }

        public static string DatasetRoot
        {
            get { return PlayerPrefs.GetString(DatasetRootKey, string.Empty); }
            set
            {
                PlayerPrefs.SetString(DatasetRootKey, value);
                PlayerPrefs.Save();
            }
        }


        private const string QuestPromptKey = "VolumeSTCube.Quest.Prompt";

        /// <summary>PlayerPrefs key of the desktop/VR application mode.</summary>
        public const string RuntimeApplicationModeKey =
            "VolumeSTCube.SlabLabRuntimeApplicationMode";

        /// <summary>Prompt of the legacy Quest workbench.</summary>
        public static string GetQuestPrompt(string fallback)
        {
            return PlayerPrefs.GetString(QuestPromptKey, fallback);
        }

        /// <summary>Set without saving, for call sites that batch writes.</summary>
        public static void SetMatPlotUrl(string value)
        {
            PlayerPrefs.SetString(MatPlotUrlKey, value);
        }

        /// <summary>Set without saving, paired with <see cref="Save"/>.</summary>
        public static void SetQuestPrompt(string value)
        {
            PlayerPrefs.SetString(QuestPromptKey, value);
        }

        /// <summary>Flushes batched writes; same timing as the call sites had.</summary>
        public static void Save()
        {
            PlayerPrefs.Save();
        }

        /// <summary>"Desktop" or "VirtualReality" as last chosen on this device.</summary>
        public static string RuntimeApplicationMode
        {
            get { return PlayerPrefs.GetString(RuntimeApplicationModeKey, string.Empty); }
            set
            {
                PlayerPrefs.SetString(RuntimeApplicationModeKey, value);
                PlayerPrefs.Save();
            }
        }

        public static string WaveStartUtc
        {
            get { return PlayerPrefs.GetString(WaveStartUtcKey, DefaultWaveStartUtc); }
        }

        public static string WaveEndUtc
        {
            get { return PlayerPrefs.GetString(WaveEndUtcKey, DefaultWaveEndUtc); }
        }
    }
}
