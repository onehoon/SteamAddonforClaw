namespace SteamInputAddonforClaw.Views;

internal readonly record struct SteamCoverGridLayout(
    int Columns,
    double CoverWidthDip,
    double CoverHeightDip,
    double ColumnGapDip,
    double RowGapDip)
{
    internal const double MinimumCoverWidthDip = 118.0;
    internal const double MaximumCoverWidthDip = 180.0;
    internal const double MinimumGapDip = 8.0;
    internal const double MaximumGapDip = 24.0;

    internal static bool TryResolveAvailableWidthDip(
        double viewportWidthDip,
        double repeaterWidthDip,
        out double availableWidthDip)
    {
        availableWidthDip = IsValidWidth(viewportWidthDip) ? viewportWidthDip : repeaterWidthDip;
        return IsValidWidth(availableWidthDip);
    }

    internal static bool TryCalculate(double availableWidthDip, out SteamCoverGridLayout layout)
    {
        layout = default;
        if (!double.IsFinite(availableWidthDip) || availableWidthDip <= 0)
            return false;
        if (availableWidthDip > int.MaxValue * (MinimumCoverWidthDip + MinimumGapDip))
            return false;

        if (availableWidthDip <= MaximumCoverWidthDip)
        {
            var narrowCoverWidth = availableWidthDip;
            layout = Create(1, narrowCoverWidth, MinimumGapDip);
            return IsValid(layout);
        }

        var maximumColumns = GetMaximumColumns(availableWidthDip, MinimumCoverWidthDip, MinimumGapDip);
        if (maximumColumns < 1)
            return false;

        var columnsAtMaximumWidth = GetMaximumColumns(
            availableWidthDip, MaximumCoverWidthDip, MinimumGapDip);
        var columns = Math.Clamp(columnsAtMaximumWidth, 1, maximumColumns);

        // Avoid an oversized empty region at the end of a short row when another
        // minimum-size cover can fit.
        if (columns == 1 && maximumColumns > 1
            && availableWidthDip - MaximumCoverWidthDip > MaximumGapDip)
        {
            columns++;
        }

        var gapAtMaximumWidth = columns > 1
            ? (availableWidthDip - MaximumCoverWidthDip * columns) / (columns - 1)
            : MinimumGapDip;

        // Keep cover widths at the preferred maximum while the remaining space
        // can be represented by a comfortable gap. Otherwise add enough columns
        // to bring the gap back into range, then share the available width.
        if (columns < maximumColumns && gapAtMaximumWidth > MaximumGapDip)
        {
            var columnsForMaximumGap = Math.Ceiling(
                (availableWidthDip + MaximumGapDip) / (MaximumCoverWidthDip + MaximumGapDip));
            columns = Math.Min(maximumColumns, Math.Max(columns, (int)columnsForMaximumGap));
            gapAtMaximumWidth = columns > 1
                ? (availableWidthDip - MaximumCoverWidthDip * columns) / (columns - 1)
                : MinimumGapDip;
        }

        double coverWidthDip;
        double gapDip;
        if (columns > 1 && gapAtMaximumWidth >= MinimumGapDip
            && gapAtMaximumWidth <= MaximumGapDip)
        {
            coverWidthDip = MaximumCoverWidthDip;
            // Round down so fractional DIP precision cannot push the last card
            // beyond the viewport or make UniformGridLayout lose a column.
            gapDip = Math.Floor(gapAtMaximumWidth * 100.0) / 100.0;
        }
        else if (columns == 1)
        {
            coverWidthDip = Math.Min(MaximumCoverWidthDip, availableWidthDip);
            gapDip = MinimumGapDip;
        }
        else
        {
            gapDip = MinimumGapDip;
            coverWidthDip = (availableWidthDip - gapDip * (columns - 1)) / columns;
            coverWidthDip = Math.Min(MaximumCoverWidthDip, coverWidthDip);

            // A rounded-up hundredth can make the final card exceed the viewport.
            // Round down so every row remains inside the measured width.
            coverWidthDip = Math.Floor(coverWidthDip * 100.0) / 100.0;
            if (coverWidthDip < MinimumCoverWidthDip)
            {
                columns--;
                if (columns < 1)
                    return false;
                coverWidthDip = columns == 1
                    ? Math.Min(MaximumCoverWidthDip, availableWidthDip)
                    : Math.Floor(((availableWidthDip - gapDip * (columns - 1)) / columns) * 100.0) / 100.0;
            }
        }

        if (!TryFitUniformGridColumns(availableWidthDip, columns, ref coverWidthDip, ref gapDip))
            return false;

        layout = Create(columns, coverWidthDip, gapDip);
        return IsValid(layout);
    }

    internal static bool TryCalculate(
        double availableWidthDip,
        double rasterizationScale,
        out SteamCoverGridLayout layout)
    {
        if (!TryCalculate(availableWidthDip, out layout))
            return false;

        if (!double.IsFinite(rasterizationScale) || rasterizationScale <= 0)
            rasterizationScale = 1.0;

        return TryFitPhysicalPixels(availableWidthDip, rasterizationScale, ref layout);
    }

    internal static bool HasMeaningfulWidthChange(double previousWidthDip, double newWidthDip) =>
        double.IsFinite(newWidthDip)
        && newWidthDip > 0
        && (!double.IsFinite(previousWidthDip)
            || previousWidthDip <= 0
            || Math.Abs(newWidthDip - previousWidthDip) >= 0.5);

    private static bool TryFitPhysicalPixels(
        double availableWidthDip,
        double rasterizationScale,
        ref SteamCoverGridLayout layout)
    {
        var scaledWidth = availableWidthDip * rasterizationScale;
        var scaledMaximumCover = MaximumCoverWidthDip * rasterizationScale;
        if (!double.IsFinite(scaledWidth)
            || scaledWidth > long.MaxValue
            || !double.IsFinite(scaledMaximumCover)
            || scaledMaximumCover > long.MaxValue)
        {
            return false;
        }

        var availablePixels = (long)Math.Floor(scaledWidth + 1e-9);
        var minimumGap = (long)Math.Ceiling(MinimumGapDip * rasterizationScale - 1e-9);
        var maximumGap = (long)Math.Floor(MaximumGapDip * rasterizationScale + 1e-9);
        var minimumCover = (long)Math.Ceiling(MinimumCoverWidthDip * rasterizationScale - 1e-9);
        var maximumCover = (long)Math.Floor(MaximumCoverWidthDip * rasterizationScale + 1e-9);
        if (availablePixels <= 0 || minimumGap > maximumGap || minimumCover > maximumCover)
            return false;

        var columns = layout.Columns;
        var gapPixels = Math.Clamp(
            (long)Math.Round(layout.ColumnGapDip * rasterizationScale, MidpointRounding.AwayFromZero),
            minimumGap,
            maximumGap);
        var coverPixels = (long)Math.Floor(layout.CoverWidthDip * rasterizationScale + 1e-9);

        while (columns > 0)
        {
            var minimumCoverForLayout = columns == 1 && layout.CoverWidthDip < MinimumCoverWidthDip
                ? Math.Max(1, (long)Math.Ceiling(layout.CoverWidthDip * rasterizationScale - 1e-9))
                : minimumCover;
            coverPixels = Math.Clamp(coverPixels, minimumCoverForLayout, maximumCover);

            if (FitsPhysicalPixels(availablePixels, columns, coverPixels, gapPixels, rasterizationScale))
            {
                layout = Create(columns, coverPixels / rasterizationScale, gapPixels / rasterizationScale);
                return IsValid(layout);
            }

            if (gapPixels > minimumGap)
            {
                gapPixels--;
                continue;
            }

            if (coverPixels > minimumCoverForLayout)
            {
                coverPixels--;
                continue;
            }

            columns--;
            if (columns <= 0)
                return false;

            gapPixels = minimumGap;
            if (columns == 1)
            {
                coverPixels = Math.Min(maximumCover, availablePixels);
                if (coverPixels <= 0)
                    return false;
                continue;
            }

            var maxCoverGap = (availablePixels - (maximumCover * columns)) / (columns - 1.0);
            if (maxCoverGap >= minimumGap && maxCoverGap <= maximumGap)
            {
                coverPixels = maximumCover;
                gapPixels = (long)Math.Floor(maxCoverGap + 1e-9);
                gapPixels = Math.Clamp(gapPixels, minimumGap, maximumGap);
            }
            else
            {
                coverPixels = (long)Math.Floor(
                    (availablePixels - (minimumGap * (columns - 1.0))) / columns);
            }
        }

        return false;
    }

    private static bool FitsPhysicalPixels(
        long availablePixels,
        int columns,
        long coverPixels,
        long gapPixels,
        double rasterizationScale)
    {
        if (coverPixels <= 0 || coverPixels > availablePixels / columns)
            return false;

        var remainingPixels = availablePixels - (columns * coverPixels);
        if (columns > 1 && gapPixels > remainingPixels / (columns - 1L))
            return false;

        var coverWidthDip = coverPixels / rasterizationScale;
        var gapDip = gapPixels / rasterizationScale;
        return GetUniformGridColumnCount(availablePixels / rasterizationScale, coverWidthDip, gapDip) >= columns;
    }

    private static bool TryFitUniformGridColumns(
        double availableWidthDip,
        int columns,
        ref double coverWidthDip,
        ref double gapDip)
    {
        // UniformGridLayout performs its item-stride calculation with floats.
        // Leave a small logical-DIP fit margin when fractional arithmetic would
        // otherwise make it silently choose one fewer column.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (GetUniformGridColumnCount(availableWidthDip, coverWidthDip, gapDip) >= columns)
                return true;

            if (gapDip > MinimumGapDip)
            {
                gapDip = Math.Max(MinimumGapDip, gapDip - 0.01);
                continue;
            }

            if (columns > 1 && coverWidthDip > MinimumCoverWidthDip)
            {
                coverWidthDip = Math.Max(
                    MinimumCoverWidthDip,
                    Math.Floor((coverWidthDip - 0.01) * 100.0) / 100.0);
                continue;
            }

            return false;
        }

        return GetUniformGridColumnCount(availableWidthDip, coverWidthDip, gapDip) >= columns;
    }

    private static int GetUniformGridColumnCount(double availableWidthDip, double coverWidthDip, double gapDip)
    {
        var availableWidth = (float)availableWidthDip;
        var itemStride = (float)(coverWidthDip + gapDip);
        var columnCount = (availableWidth + (float)gapDip) / itemStride;
        if (!float.IsFinite(availableWidth) || !float.IsFinite(itemStride)
            || !float.IsFinite(columnCount) || itemStride <= 0 || columnCount > int.MaxValue)
        {
            return 0;
        }

        return Math.Max(1, (int)columnCount);
    }

    private static int GetMaximumColumns(double availableWidthDip, double coverWidthDip, double gapDip)
    {
        var count = Math.Floor((availableWidthDip + gapDip) / (coverWidthDip + gapDip));
        if (!double.IsFinite(count) || count < 1 || count > int.MaxValue)
            return count > int.MaxValue ? int.MaxValue : 0;
        return (int)count;
    }

    private static SteamCoverGridLayout Create(int columns, double coverWidthDip, double gapDip)
    {
        var coverHeightDip = Math.Round(coverWidthDip * 1.5, 2);
        return new SteamCoverGridLayout(columns, coverWidthDip, coverHeightDip, gapDip, gapDip);
    }

    private static bool IsValid(SteamCoverGridLayout layout) =>
        layout.Columns > 0
        && double.IsFinite(layout.CoverWidthDip)
        && double.IsFinite(layout.CoverHeightDip)
        && double.IsFinite(layout.ColumnGapDip)
        && double.IsFinite(layout.RowGapDip)
        && layout.CoverWidthDip > 0
        && layout.CoverHeightDip > 0
        && layout.ColumnGapDip >= MinimumGapDip
        && layout.RowGapDip >= MinimumGapDip;

    private static bool IsValidWidth(double widthDip) => double.IsFinite(widthDip) && widthDip > 0;
}
