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
    internal const double PreferredCoverWidthDip = 170.0;
    internal const double PreferredSlotWidthDip = PreferredCoverWidthDip + MinimumGapDip;

    internal double SlotWidthDip => CoverWidthDip + ColumnGapDip;
    internal double SlotHeightDip => CoverHeightDip + RowGapDip;

    private const double MinimumSlotWidthDip = MinimumCoverWidthDip + MinimumGapDip;
    private const double MaximumSlotWidthDip = MaximumCoverWidthDip + MaximumGapDip;

    internal static bool TryCalculate(double availableWidthDip, out SteamCoverGridLayout layout) =>
        TryCalculate(availableWidthDip, 1.0, out layout);

    internal static bool TryCalculate(
        double availableWidthDip,
        double rasterizationScale,
        out SteamCoverGridLayout layout)
    {
        layout = default;
        if (!double.IsFinite(availableWidthDip) || availableWidthDip <= 0)
            return false;
        if (!double.IsFinite(rasterizationScale) || rasterizationScale <= 0)
            rasterizationScale = 1.0;

        var availableWidthPixelsExact = availableWidthDip * rasterizationScale;
        var maximumSlotPixelsExact = MaximumSlotWidthDip * rasterizationScale;
        if (!double.IsFinite(availableWidthPixelsExact)
            || availableWidthPixelsExact > long.MaxValue - 1024.0
            || !double.IsFinite(maximumSlotPixelsExact)
            || maximumSlotPixelsExact > long.MaxValue - 1024.0)
        {
            return false;
        }

        var availableWidthPixels = (long)Math.Floor(availableWidthPixelsExact + 1e-9);
        var minimumGapPixels = (long)Math.Ceiling(MinimumGapDip * rasterizationScale - 1e-9);
        var maximumGapPixels = (long)Math.Floor(MaximumGapDip * rasterizationScale + 1e-9);
        var minimumCoverPixels = (long)Math.Ceiling(MinimumCoverWidthDip * rasterizationScale - 1e-9);
        var maximumCoverPixels = (long)Math.Floor(MaximumCoverWidthDip * rasterizationScale + 1e-9);
        var maximumSlotPixels = maximumCoverPixels + maximumGapPixels;
        if (availableWidthPixels <= 0
            || minimumGapPixels <= 0
            || maximumGapPixels < minimumGapPixels
            || minimumCoverPixels <= 0
            || maximumCoverPixels < minimumCoverPixels)
        {
            return false;
        }

        var minimumSlotPixels = minimumCoverPixels + minimumGapPixels;
        var maximumFeasibleColumns = availableWidthPixels >= minimumSlotPixels
            ? Math.Max(1, availableWidthPixels / minimumSlotPixels)
            : 1;
        var preferredSlotPixelsExact = PreferredSlotWidthDip * rasterizationScale;
        if (!double.IsFinite(preferredSlotPixelsExact) || preferredSlotPixelsExact <= 0)
            return false;

        var preferredColumns = Math.Round(
            availableWidthPixels / preferredSlotPixelsExact,
            MidpointRounding.AwayFromZero);
        var columns = (int)Math.Clamp(
            Math.Max(1, preferredColumns),
            1,
            Math.Min(int.MaxValue, maximumFeasibleColumns));

        var slotWidthPixels = Math.Min(availableWidthPixels / columns, maximumSlotPixels);
        if (slotWidthPixels <= 0)
            return false;

        long gapPixels;
        long coverWidthPixels;
        if (columns == 1 && slotWidthPixels < minimumSlotPixels)
        {
            // A single card may shrink below the normal minimum when the measured
            // inner viewport cannot fit one minimum-size card and its trailing gap.
            gapPixels = slotWidthPixels > minimumGapPixels ? minimumGapPixels : 0;
            coverWidthPixels = slotWidthPixels - gapPixels;
        }
        else
        {
            var preferredCoverPixels = (long)Math.Round(
                PreferredCoverWidthDip * rasterizationScale,
                MidpointRounding.AwayFromZero);
            gapPixels = Math.Clamp(slotWidthPixels - preferredCoverPixels, minimumGapPixels, maximumGapPixels);
            coverWidthPixels = slotWidthPixels - gapPixels;

            if (coverWidthPixels > maximumCoverPixels)
            {
                coverWidthPixels = maximumCoverPixels;
                gapPixels = slotWidthPixels - coverWidthPixels;
            }

            if (coverWidthPixels < minimumCoverPixels
                || gapPixels < minimumGapPixels
                || gapPixels > maximumGapPixels)
            {
                return false;
            }
        }

        if (coverWidthPixels <= 0 || coverWidthPixels > maximumCoverPixels)
            return false;

        var coverWidthDip = coverWidthPixels / rasterizationScale;
        var gapDip = gapPixels / rasterizationScale;
        layout = new SteamCoverGridLayout(
            columns,
            coverWidthDip,
            coverWidthDip * 1.5,
            gapDip,
            gapDip);
        return double.IsFinite(layout.SlotWidthDip)
            && double.IsFinite(layout.SlotHeightDip)
            && layout.SlotWidthDip > 0
            && layout.SlotHeightDip > 0;
    }

    internal static bool HasMeaningfulWidthChange(double previousWidthDip, double newWidthDip) =>
        double.IsFinite(newWidthDip)
        && newWidthDip > 0
        && (!double.IsFinite(previousWidthDip)
            || previousWidthDip <= 0
            || Math.Abs(newWidthDip - previousWidthDip) >= 0.5);
}
