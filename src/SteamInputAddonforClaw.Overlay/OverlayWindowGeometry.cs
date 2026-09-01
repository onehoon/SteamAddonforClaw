namespace SteamInputAddonforClaw.Overlay;

internal readonly record struct OverlayRect(int X, int Y, int Width, int Height);

internal static class OverlayWindowGeometry
{
    internal const double PocPanelWidthDip = 400.0;
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
        var width = (int)Math.Round(PocPanelWidthDip * effectiveDpi / DefaultDpi, MidpointRounding.AwayFromZero);
        return new OverlayRect(workLeft, workTop, Math.Min(Math.Max(0, width), workWidth), workHeight);
    }
}
