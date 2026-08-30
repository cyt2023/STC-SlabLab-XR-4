using System;
using System.Collections.Generic;
using UnityEditor;

namespace UnityVolumeRendering.EditorTools
{
    /// <summary>Keeps Desktop and VR player symbols mutually exclusive.</summary>
    public static class VolumeSTCubeBuildModeDefines
    {
        public const string DesktopDefine = "SLABLAB_DESKTOP";
        public const string LegacyFlatDefine = "SLABLAB_FLAT";
        public const string VrDefine = "SLABLAB_VR";

        public static void Configure(BuildTargetGroup group, bool desktop)
        {
            HashSet<string> symbols = new HashSet<string>(
                PlayerSettings.GetScriptingDefineSymbolsForGroup(group).Split(
                    new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
            symbols.Remove(DesktopDefine);
            symbols.Remove(LegacyFlatDefine);
            symbols.Remove(VrDefine);

            if (desktop)
            {
                symbols.Add(DesktopDefine);
                // Existing platform guards still use this compatibility symbol.
                symbols.Add(LegacyFlatDefine);
            }
            else
            {
                symbols.Add(VrDefine);
            }

            PlayerSettings.SetScriptingDefineSymbolsForGroup(group,
                string.Join(";", symbols));
        }
    }
}
