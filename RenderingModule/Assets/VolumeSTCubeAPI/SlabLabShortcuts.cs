using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>What a desktop key press asks the shell to do.</summary>
    public enum SlabLabShortcut
    {
        None,
        PreviousStep,
        NextStep,
        ToggleHelp,
        CloseOverlay,
        ResetView,
        ToggleSlabFrame,
        RangeEntry,
        Snapshot,
    }

    /// <summary>
    /// The one table behind the desktop keyboard shortcuts. The flat-screen HUD,
    /// the workbench handler and the edit-mode guards all read it, so a shortcut
    /// cannot end up advertised in the Help card but wired to nothing (or the
    /// other way round). Pure mapping with no scene and no state — that is what
    /// makes it checkable without entering Play Mode.
    /// </summary>
    public static class SlabLabShortcuts
    {
        /// <summary>Every key the shell listens for, in table order.</summary>
        public static readonly KeyCode[] Keys =
        {
            // Not the arrow keys: Horizontal/Vertical already bind
            // left/right/up/down, so an arrow press walked the camera rig at the
            // same time as it changed step. tools/check_contracts.py fails if a
            // key listed here is ever bound to a movement axis again.
            KeyCode.PageUp,
            KeyCode.PageDown,
            KeyCode.X,
            KeyCode.Y,
            KeyCode.T,
            KeyCode.P,
            KeyCode.H,
            KeyCode.F1,
            KeyCode.Escape,
        };

        public const string HelpLine =
            "Keys: Page Up / Page Down: previous / next step\n" +
            "X: reset view    Y: slab frame    P: snapshot\n" +
            "T: type time range    H or F1: help    Esc: close";

        public static SlabLabShortcut Map(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.PageUp: return SlabLabShortcut.PreviousStep;
                case KeyCode.PageDown: return SlabLabShortcut.NextStep;
                case KeyCode.X: return SlabLabShortcut.ResetView;
                case KeyCode.Y: return SlabLabShortcut.ToggleSlabFrame;
                case KeyCode.T: return SlabLabShortcut.RangeEntry;
                case KeyCode.P: return SlabLabShortcut.Snapshot;
                case KeyCode.H:
                case KeyCode.F1: return SlabLabShortcut.ToggleHelp;
                case KeyCode.Escape: return SlabLabShortcut.CloseOverlay;
                default: return SlabLabShortcut.None;
            }
        }
    }
}
