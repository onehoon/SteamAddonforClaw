using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Overlay;

internal readonly record struct OverlayRect(int X, int Y, int Width, int Height);

internal static class OverlayWindowGeometry
{
    internal static OverlayRect Calculate(
        int monitorLeft,
        int monitorTop,
        int monitorRight,
        int monitorBottom,
        uint dpi,
        int topInsetPx = 0)
    {
        var monitorWidth = Math.Max(0, monitorRight - monitorLeft);
        var monitorHeight = Math.Max(0, monitorBottom - monitorTop);

        if (monitorWidth == 0 || monitorHeight == 0)
            return new OverlayRect(monitorLeft, monitorTop, 0, 0);

        // The UI uses a fixed 135% design scale. Keep the old physical right-anchor extension,
        // while making the resulting window width independent of the monitor's Windows DPI.
        _ = dpi;
        var width = Math.Min(monitorWidth, OverlayUiScale.PhysicalWindowWidthPixels);
        var x = monitorRight - width;
        var inset = Math.Clamp(topInsetPx, 0, monitorHeight);

        return new OverlayRect(x, monitorTop + inset, width, monitorHeight - inset);
    }

    internal static int ClawHudTopInsetPx(
        bool desiredEnabled,
        FrontendClawHudFont font,
        int hudSizeOffset)
    {
        if (!desiredEnabled)
            return 0;

        var size = Math.Clamp(hudSizeOffset, -2, 2);

        return font == FrontendClawHudFont.SegoeUiVariable
            ? size switch
            {
                -2 => 34,
                -1 => 36,
                 0 => 37,
                 1 => 38,
                 2 => 40,
                _ => 37
            }
            : size switch
            {
                -2 => 32,
                -1 => 33,
                 0 => 34,
                 1 => 36,
                 2 => 37,
                _ => 34
            };
    }
}
