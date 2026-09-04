namespace SteamInputAddonforClaw.Overlay;

internal readonly record struct OverlayRect(int X, int Y, int Width, int Height);

internal static class OverlayWindowGeometry
{
    internal const double PocPanelWidthDip = 400.0;

    // OQ5 UI Polish A: a small floating-surface separation from the work-area edge. Applied on
    // left/top/bottom only -- the panel is left-anchored and does not reach the work-area right
    // edge, so no right inset is needed. ~4 DIP is about 6 physical px at the 150% reference scale.
    internal const double PanelEdgeInsetDip = 4.0;
    private const uint DefaultDpi = 96;

    internal static OverlayRect Calculate(
        int workLeft,
        int workTop,
        int workRight,
        int workBottom,
        uint dpi)
    {
        var workWidth = Math.Max(0, workRight - workLeft);
        var workHeight = Math.Max(0, workBottom - workTop);
        var effectiveDpi = dpi == 0 ? DefaultDpi : dpi;

        var insetPx = (int)Math.Round(PanelEdgeInsetDip * effectiveDpi / DefaultDpi, MidpointRounding.AwayFromZero);
        // Never inset more than half of the available space, so an unusually small work area still
        // yields a non-negative width/height instead of an inverted/degenerate rectangle.
        insetPx = Math.Max(0, Math.Min(insetPx, Math.Min(workWidth, workHeight) / 2));

        var availableWidth = Math.Max(0, workWidth - insetPx);
        var width = (int)Math.Round(PocPanelWidthDip * effectiveDpi / DefaultDpi, MidpointRounding.AwayFromZero);
        width = Math.Min(Math.Max(0, width), availableWidth);
        var height = Math.Max(0, workHeight - 2 * insetPx);

        return new OverlayRect(workLeft + insetPx, workTop + insetPx, width, height);
    }
}
