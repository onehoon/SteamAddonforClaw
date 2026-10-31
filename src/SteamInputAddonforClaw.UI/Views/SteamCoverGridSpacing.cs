namespace SteamInputAddonforClaw.Views;

internal readonly record struct SteamCoverGridSpacing(int Columns, double GapDip)
{
    internal const double MinimumGapDip = 2.0;
    private const double LayoutFitSlackDip = 0.01; // Account for UniformGridLayout's single-precision item stride.

    internal static bool TryResolveAvailableWidthDip(
        double viewportWidthDip,
        double repeaterWidthDip,
        out double availableWidthDip)
    {
        availableWidthDip = IsValidWidth(viewportWidthDip) ? viewportWidthDip : repeaterWidthDip;
        return IsValidWidth(availableWidthDip);
    }

    internal static bool TryCalculate(
        double availableWidthDip,
        double coverWidthDip,
        out SteamCoverGridSpacing spacing)
    {
        spacing = default;
        if (!double.IsFinite(availableWidthDip) || availableWidthDip <= 0
            || !double.IsFinite(coverWidthDip) || coverWidthDip <= 0)
            return false;

        // UniformGridLayout derives items per line from single-precision available width and item stride.
        var availableWidth = (float)availableWidthDip;
        var minimumItemStride = (float)(coverWidthDip + MinimumGapDip);
        var columnCount = (availableWidth + MinimumGapDip) / minimumItemStride;
        if (!double.IsFinite(columnCount) || columnCount > int.MaxValue)
            return false;

        var columns = Math.Max(1, (int)columnCount);
        var idealGapDip = columns > 1
            ? (availableWidthDip - columns * coverWidthDip) / (columns - 1)
            : MinimumGapDip;
        if (!double.IsFinite(idealGapDip))
            return false;

        var gapDip = columns > 1
            ? Math.Max(MinimumGapDip, idealGapDip - LayoutFitSlackDip)
            : MinimumGapDip;
        spacing = new SteamCoverGridSpacing(columns, gapDip);
        return true;
    }

    private static bool IsValidWidth(double widthDip) => double.IsFinite(widthDip) && widthDip > 0;
}
