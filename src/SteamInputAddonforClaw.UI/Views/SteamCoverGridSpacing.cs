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
        double rasterizationScale,
        out SteamCoverGridSpacing spacing)
    {
        spacing = default;
        if (!double.IsFinite(availableWidthDip) || availableWidthDip <= 0
            || !double.IsFinite(coverWidthDip) || coverWidthDip <= 0
            || !double.IsFinite(rasterizationScale) || rasterizationScale <= 0)
            return false;

        var minGapPx = Math.Max(1, Math.Ceiling(MinimumGapDip * rasterizationScale));
        var minGapDip = minGapPx / rasterizationScale;
        var availablePx = availableWidthDip * rasterizationScale;
        var coverPx = coverWidthDip * rasterizationScale;
        var fitSlackPx = LayoutFitSlackDip * rasterizationScale;
        if (!double.IsFinite(minGapPx) || !double.IsFinite(minGapDip) || minGapDip < MinimumGapDip
            || !double.IsFinite(availablePx) || availablePx <= 0
            || !double.IsFinite(coverPx) || coverPx <= 0
            || !double.IsFinite(fitSlackPx))
            return false;

        // UniformGridLayout derives items per line from single-precision available width and item stride.
        var availableWidth = (float)availableWidthDip;
        var alignedMinimumItemStride = (float)(coverWidthDip + minGapDip);
        if (!float.IsFinite(availableWidth) || !float.IsFinite(alignedMinimumItemStride)
            || alignedMinimumItemStride <= 0)
            return false;

        var columnCount = (availableWidth + minGapDip) / alignedMinimumItemStride;
        if (!double.IsFinite(columnCount) || columnCount > int.MaxValue)
            return false;

        var columns = Math.Max(1, (int)columnCount);

        // Keep the float-based estimate, but never claim columns that cannot fit with the
        // whole-pixel minimum gap at the current rasterization scale.
        var physicalStridePx = coverPx + minGapPx;
        if (!double.IsFinite(physicalStridePx) || physicalStridePx <= 0)
            return false;

        var maximumFittingColumns = availablePx >= coverPx
            ? 1 + Math.Floor((availablePx - coverPx) / physicalStridePx)
            : 1;
        if (!double.IsFinite(maximumFittingColumns) || maximumFittingColumns < 1)
            return false;

        var maximumFittingColumnCount = maximumFittingColumns >= int.MaxValue
            ? int.MaxValue
            : (int)maximumFittingColumns;
        columns = Math.Min(columns, maximumFittingColumnCount);

        while (columns > 1
            && columns * coverPx + (columns - 1) * minGapPx > availablePx)
        {
            columns--;
        }

        var coveredWidthPx = columns * coverPx;
        if (!double.IsFinite(coveredWidthPx))
            return false;

        var idealGapPx = columns > 1
            ? (availablePx - coveredWidthPx) / (columns - 1)
            : minGapPx;
        if (!double.IsFinite(idealGapPx))
            return false;

        var gapPx = columns > 1
            ? Math.Max(minGapPx, Math.Floor(idealGapPx - fitSlackPx))
            : minGapPx;
        var gapDip = gapPx / rasterizationScale;
        if (!double.IsFinite(gapPx) || !double.IsFinite(gapDip) || gapDip < MinimumGapDip)
            return false;

        spacing = new SteamCoverGridSpacing(columns, gapDip);
        return true;
    }

    private static bool IsValidWidth(double widthDip) => double.IsFinite(widthDip) && widthDip > 0;
}
