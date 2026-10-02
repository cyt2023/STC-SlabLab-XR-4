using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Desktop composition constants that used to be inline in the workbench.
    /// Every value is the literal it replaced, so the layout is unchanged — the
    /// numbers simply have names now, and there is one place to tune them.
    /// </summary>
    internal static class SlabLabLayout
    {
        /// <summary>Sideways shift that centres the two-Field desktop pair.</summary>
        public const float FieldPairCentreShiftRight = 0.12f;
        /// <summary>Lift that keeps the surface timeline clear of the action bar.</summary>
        public const float FieldPairLiftUp = 0.22f;
        /// <summary>Compact scale of the Field pair while panels are on screen.</summary>
        public const float FieldPairCompactScale = 0.78f;
        /// <summary>Viewport anchor of the desktop tri-axis dock.</summary>
        public const float AxisDockViewportX = 0.52f;
        public const float AxisDockViewportY = 0.50f;
        /// <summary>Dock scale of the tri-axis body relative to its authored size.</summary>
        public const float AxisDockScale = 1.38f;
        /// <summary>Authored desktop scale of the tri-axis body.</summary>
        public const float AxisBodyScale = 1.30f;

        // Repeated panel sizes, named so a resize is one edit.
        public static readonly Vector2 ButtonSizeStandard = new Vector2(210, 42);
        public static readonly Vector2 ButtonSizeMedium = new Vector2(190, 42);
        public static readonly Vector2 BoundaryActionButtonSize = new Vector2(300, 54);
        public static readonly Vector2 RoleButtonSize = new Vector2(132, 36);
        public static readonly Vector2 StepperButtonSize = new Vector2(130, 26);
        public static readonly Vector2 RollupGroupButtonSize = new Vector2(48, 30);
        public static readonly Vector2 RowChipSize = new Vector2(150, 28);
        public static readonly Vector2 PanelCardSizeLarge = new Vector2(900, 440);
        public static readonly Vector2 PanelCanvasSize = new Vector2(1120.0f, 840.0f);
        public static readonly Vector2 AxisMarkerDotSize = new Vector2(14, 14);
        /// <summary>Scale applied to the Field pair presentation.</summary>
        public const float FieldPresentationScale = 1.15f;
    }
}
