namespace SteamInputAddonforClaw.Overlay;

internal readonly record struct OverlayRect(int X, int Y, int Width, int Height);

internal readonly record struct OverlayGeometryMetrics(
    int MonitorWidth,
    int MonitorHeight,
    int WorkWidth,
    int WorkHeight,
    int ReservedEdgePx,
    int ReferenceTaskbarPx,
    int ExtraGapPx,
    int OuterMarginPx);

internal static class OverlayWindowGeometry
{
    internal const double ReferenceTaskbarDip = 48.0;
    internal const double FloatingGapDip = 12.0;
    internal const double MaxSurfaceWidthDip = 960.0;
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
        var reservedEdgePx = Math.Max(Math.Max(reservedLeft, reservedTop), Math.Max(reservedRight, reservedBottom));
        var referenceTaskbarPx = DipToPixels(ReferenceTaskbarDip, effectiveDpi);
        var extraGapPx = DipToPixels(FloatingGapDip, effectiveDpi);
        var requestedOuterMarginPx = Math.Max(reservedEdgePx, referenceTaskbarPx) + extraGapPx;

        // Keep one margin value on all four sides, but clamp it to the smallest monitor
        // dimension so an unusually small display never produces an inverted rectangle.
        var outerMarginPx = Math.Min(requestedOuterMarginPx, Math.Min(monitorWidth, monitorHeight) / 2);
        outerMarginPx = Math.Max(0, outerMarginPx);

        var requestedWidth = Math.Max(0, monitorWidth - 2 * outerMarginPx);
        var maxSurfaceWidthPx = DipToPixels(MaxSurfaceWidthDip, effectiveDpi);
        var width = Math.Min(requestedWidth, maxSurfaceWidthPx);
        var height = Math.Max(0, monitorHeight - 2 * outerMarginPx);

        metrics = new OverlayGeometryMetrics(
            monitorWidth,
            monitorHeight,
            workWidth,
            workHeight,
            reservedEdgePx,
            referenceTaskbarPx,
            extraGapPx,
            outerMarginPx);

        var x = monitorLeft + Math.Max(0, (monitorWidth - width) / 2);
        return new OverlayRect(x, monitorTop + outerMarginPx, width, height);
    }

    private static int DipToPixels(double dip, uint dpi) =>
        (int)Math.Round(dip * dpi / DefaultDpi, MidpointRounding.AwayFromZero);
}
