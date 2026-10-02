namespace UnityVolumeRendering
{
    public sealed partial class VolumeSTCubeQuestSpatialWorkbench
    {
        // Transient state grouped by what it describes. The flags keep
        // their old names so every use site stays a 1:1 rename; what
        // changes is that a whole group can now be cleared in one
        // statement, and a reader can see the state belongs to the
        // gesture, the keyboard, or the desktop presentation.

        /// <summary>Was: 11 separate bools in the workbench.</summary>
        private struct InteractionState
        {
            public bool draggingSlab;
            public bool regionDragging;
            public bool timeBoundaryDragging;
            public bool depthBoundaryDragging;
            public bool depthInspectionActive;
            public bool drawingRegion;
            public bool draftPivotPreviewDragging;
            public bool draggedAxisSawTriggerHeld;
            public bool draggedAxisUsesDesktopPointer;
            public bool draggedPaletteSawTriggerHeld;
            public bool draggedPaletteUsesDesktopPointer;
        }

        private InteractionState interaction;

        /// <summary>Was: 8 separate bools in the workbench.</summary>
        private struct InputState
        {
            public bool textInputActive;
            public bool keyboardInputWasVoice;
            public bool vrKeyboardVisible;
            public bool voiceInputActive;
            public bool voiceReviewPending;
            public bool questVoiceRecording;
            public bool questVoiceUploading;
            public bool desktopEditingPrompt;
            /// <summary>
            /// The typed Time-range entry in the boundary bar. Additive: the drag
            /// path does not read it, and while it is set the keyboard belongs to
            /// the entry (same rule as desktopEditingPrompt).
            /// </summary>
            public bool boundaryRangeEntry;
        }

        private InputState input;

        /// <summary>Was: 7 separate bools in the workbench.</summary>
        private struct DesktopViewState
        {
            public bool desktopVisualizationAligned;
            public bool desktopFocusTargetsReady;
            public bool desktopMatrixPresentationReady;
            public bool workflowToolbarPinned;
            public bool variablePaletteCollapsed;
            public bool cubeVisible;
            public bool legacyPanelVisible;
        }

        private DesktopViewState viewState = new DesktopViewState { cubeVisible = true };
    }
}
