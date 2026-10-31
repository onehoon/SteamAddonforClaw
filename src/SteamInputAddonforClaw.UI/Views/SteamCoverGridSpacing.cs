namespace SteamInputAddonforClaw.Views;

internal readonly record struct SteamCoverGridSpacing(int Columns, double GapDip)
{
    internal const double MinimumGapDip = 6.0;

    internal static bool TryCalculate(
        double availableWidthDip,
        double coverWidthDip,
        out SteamCoverGridSpacing spacing)
    {
        spacing = default;
        if (!double.IsFinite(availableWidthDip) || availableWidthDip <= 0
            || !double.IsFinite(coverWidthDip) || coverWidthDip <= 0)
            return false;

        var columnCount = Math.Floor(
            (availableWidthDip + MinimumGapDip) / (coverWidthDip + MinimumGapDip));
        if (!double.IsFinite(columnCount) || columnCount > int.MaxValue)
            return false;

        var columns = Math.Max(1, (int)columnCount);
        var gapDip = columns > 1
            ? (availableWidthDip - columns * coverWidthDip) / (columns - 1)
            : MinimumGapDip;
        if (!double.IsFinite(gapDip))
            return false;

        spacing = new SteamCoverGridSpacing(columns, Math.Max(MinimumGapDip, gapDip));
        return true;
    }
}
