namespace SteamInputAddonforClaw.Overlay;

internal readonly record struct OverlayRect(int X, int Y, int Width, int Height);

internal readonly record struct OverlayGeometryMetrics(
    int MonitorWidth,
    int MonitorHeight,
    int WorkWidth,
    int WorkHeight,
    int ReservedLeftPx,
    int ReservedTopPx,
    int ReservedRightPx,
    int ReservedBottomPx,
    int FloatingGapPx);

internal static class OverlayWindowGeometry
{
    internal const double FloatingGapDip = 4.0;
    internal const double MaxSurfaceWidthDip = 416.0;
    private const uint DefaultDpi = 96;

    internal static OverlayRect Calculate(
        int monitorLeft,
        int monitorTop,
        int monitorRight,
        int monitorBottom,
        int workLeft,
        int workTop,
        int workRight,
        int workBottom,
        uint dpi) => Calculate(
            monitorLeft,
            monitorTop,
            monitorRight,
            monitorBottom,
            workLeft,
            workTop,
            workRight,
            workBottom,
            dpi,
            out _);

    internal static OverlayRect Calculate(
        int monitorLeft,
        int monitorTop,
        int monitorRight,
        int monitorBottom,
        int workLeft,
        int workTop,
        int workRight,
        int workBottom,
        uint dpi,
        out OverlayGeometryMetrics metrics)
    {
        var monitorWidth = Math.Max(0, monitorRight - monitorLeft);
        var monitorHeight = Math.Max(0, monitorBottom - monitorTop);
        var workWidth = Math.Max(0, workRight - workLeft);
        var workHeight = Math.Max(0, workBottom - workTop);
        var effectiveDpi = dpi == 0 ? DefaultDpi : dpi;

        var reservedLeft = Math.Max(0, workLeft - monitorLeft);
        var reservedTop = Math.Max(0, workTop - monitorTop);
        var reservedRight = Math.Max(0, monitorRight - workRight);
        var reservedBottom = Math.Max(0, monitorBottom - workBottom);
        var floatingGapPx = DipToPixels(FloatingGapDip, effectiveDpi);
        var maxSurfaceWidthPx = DipToPixels(MaxSurfaceWidthDip, effectiveDpi);

        metrics = new OverlayGeometryMetrics(
            monitorWidth,
            monitorHeight,
            workWidth,
            workHeight,
            reservedLeft,
            reservedTop,
            reservedRight,
            reservedBottom,
            floatingGapPx);

        if (monitorWidth == 0 || monitorHeight == 0)
            return new OverlayRect(monitorLeft, monitorTop, 0, 0);

        var leftUsable = monitorLeft + reservedLeft + floatingGapPx;
        var topUsable = monitorTop + reservedTop + floatingGapPx;
        var rightUsable = monitorRight - reservedRight - floatingGapPx;
        var bottomUsable = monitorBottom - reservedBottom - floatingGapPx;
        var availableWidth = Math.Max(0, rightUsable - leftUsable);
        var width = Math.Min(availableWidth, maxSurfaceWidthPx);
        var x = Math.Max(leftUsable, rightUsable - width);
        var height = Math.Max(0, bottomUsable - topUsable);

        return new OverlayRect(x, topUsable, width, height);
    }

    private static int DipToPixels(double dip, uint dpi) =>
        (int)Math.Round(dip * dpi / DefaultDpi, MidpointRounding.AwayFromZero);
}
