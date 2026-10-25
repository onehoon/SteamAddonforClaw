namespace SteamInputAddonforClaw.Overlay;

internal readonly record struct OverlayRect(int X, int Y, int Width, int Height);

internal static class OverlayWindowGeometry
{
    private const int LegacyRightInsetForLeftAnchorPx = 48;
    private const double LeftAnchorWidthDip = 452.0;
    private const uint DefaultDpi = 96;

    internal static OverlayRect Calculate(
        int monitorLeft,
        int monitorTop,
        int monitorRight,
        int monitorBottom,
        uint dpi)
    {
        var monitorWidth = Math.Max(0, monitorRight - monitorLeft);
        var monitorHeight = Math.Max(0, monitorBottom - monitorTop);
        var effectiveDpi = dpi == 0 ? DefaultDpi : dpi;

        if (monitorWidth == 0 || monitorHeight == 0)
            return new OverlayRect(monitorLeft, monitorTop, 0, 0);

        // Preserve the prior left edge by calculating it from the old capped and inset surface.
        // The final window then fills from that anchor to the monitor's physical right edge.
        var legacyRightInsetPx = Math.Min(LegacyRightInsetForLeftAnchorPx, monitorWidth);
        var legacyRight = monitorRight - legacyRightInsetPx;
        var legacyAvailableWidth = Math.Max(0, legacyRight - monitorLeft);
        var legacyWidthPx = DipToPixels(LeftAnchorWidthDip, effectiveDpi);
        var legacyWidth = Math.Min(legacyAvailableWidth, legacyWidthPx);
        var x = legacyRight - legacyWidth;

        return new OverlayRect(x, monitorTop, Math.Max(0, monitorRight - x), monitorHeight);
    }

    private static int DipToPixels(double dip, uint dpi) =>
        (int)Math.Round(dip * dpi / DefaultDpi, MidpointRounding.AwayFromZero);
}
