namespace SteamInputAddonforClaw.Overlay;

internal readonly record struct OverlayRect(int X, int Y, int Width, int Height);

internal static class OverlayWindowGeometry
{
    internal const int OuterEdgeInsetPx = 48;
    internal const double MaxSurfaceWidthDip = 452.0;
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

        var rightInsetPx = Math.Min(OuterEdgeInsetPx, monitorWidth);
        var verticalInsetPx = Math.Min(OuterEdgeInsetPx, monitorHeight / 2);
        var maxWidthPx = DipToPixels(MaxSurfaceWidthDip, effectiveDpi);
        var top = monitorTop + verticalInsetPx;
        var right = monitorRight - rightInsetPx;
        var bottom = monitorBottom - verticalInsetPx;

        var availableWidth = Math.Max(0, right - monitorLeft);
        var width = Math.Min(availableWidth, maxWidthPx);
        var x = right - width;
        var height = Math.Max(0, bottom - top);

        return new OverlayRect(x, top, width, height);
    }

    private static int DipToPixels(double dip, uint dpi) =>
        (int)Math.Round(dip * dpi / DefaultDpi, MidpointRounding.AwayFromZero);
}
