namespace SteamInputAddonforClaw.Overlay;

internal readonly record struct OverlayRect(int X, int Y, int Width, int Height);

internal static class OverlayWindowGeometry
{
    internal const double FloatingGapDip = 4.0;
    internal const double MaxSurfaceWidthDip = 432.0;
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

        var gapPx = DipToPixels(FloatingGapDip, effectiveDpi);
        var maxWidthPx = DipToPixels(MaxSurfaceWidthDip, effectiveDpi);
        var left = monitorLeft + gapPx;
        var top = monitorTop + gapPx;
        var right = monitorRight - gapPx;
        var bottom = monitorBottom - gapPx;

        var availableWidth = Math.Max(0, right - left);
        var width = Math.Min(availableWidth, maxWidthPx);
        var x = Math.Max(left, right - width);
        var height = Math.Max(0, bottom - top);

        return new OverlayRect(x, top, width, height);
    }

    private static int DipToPixels(double dip, uint dpi) =>
        (int)Math.Round(dip * dpi / DefaultDpi, MidpointRounding.AwayFromZero);
}
