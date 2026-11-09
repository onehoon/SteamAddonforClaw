namespace SteamInputAddonforClaw.Views;

internal readonly record struct SteamCoverGridLayout(
    int Columns,
    double CoverWidthDip,
    double CoverHeightDip,
    double ColumnGapDip,
    double RowGapDip)
{
    internal const double MinimumCoverWidthDip = 120.0;
    internal const double MaximumCoverWidthDip = 180.0;
    internal const double MinimumGapDip = 10.0;
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
            var narrowCoverWidth = Math.Max(1.0, availableWidthDip);
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
            gapDip = Math.Round(gapAtMaximumWidth, 2);
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

        layout = Create(columns, coverWidthDip, gapDip);
        return IsValid(layout);
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
