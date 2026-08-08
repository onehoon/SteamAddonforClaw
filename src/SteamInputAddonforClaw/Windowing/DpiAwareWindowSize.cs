namespace SteamInputAddonforClaw.Windowing;

internal readonly record struct WindowSize(int Width, int Height);

internal static class DpiAwareWindowSize
{
    internal const int DefaultWidthDips = 1200;
    internal const int DefaultHeightDips = 720;

    internal static WindowSize Calculate(uint dpi, int availableWidth, int availableHeight)
    {
        var scale = dpi > 0 ? dpi / 96.0 : 1.0;
        var width = (int)(DefaultWidthDips * scale);
        var height = (int)(DefaultHeightDips * scale);

        if (availableWidth > 0)
        {
            width = Math.Min(width, availableWidth);
        }

        if (availableHeight > 0)
        {
            height = Math.Min(height, availableHeight);
        }

        return new WindowSize(width, height);
    }
}
