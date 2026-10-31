namespace SteamInputAddonforClaw.Views;

internal enum SteamCoverGridObservationStatus
{
    Valid,
    NotAtTop,
    InsufficientItems,
    PendingArrange
}

internal readonly record struct SteamCoverGridArrangedItem(int Index, double X, double Y);

internal readonly record struct SteamCoverGridObservation(
    SteamCoverGridObservationStatus Status,
    int? ColumnsObserved,
    int? FirstWrappedIndex,
    string Reason,
    IReadOnlyList<SteamCoverGridArrangedItem> FirstRow);

internal static class SteamCoverGridLayoutObservation
{
    internal const int MaximumSampledItems = 12;
    private const int MinimumItemsToDistinguishSevenOrEightColumns = 9;

    internal static SteamCoverGridObservation ObserveFirstRow(
        int itemCount,
        double verticalOffsetDip,
        IReadOnlyList<SteamCoverGridArrangedItem> arrangedItems,
        double rowToleranceDip)
    {
        if (!double.IsFinite(verticalOffsetDip) || verticalOffsetDip < 0
            || !double.IsFinite(rowToleranceDip) || rowToleranceDip < 0)
        {
            return Unknown(SteamCoverGridObservationStatus.PendingArrange, "invalid-layout-measurement");
        }

        if (verticalOffsetDip > 0.01)
            return Unknown(SteamCoverGridObservationStatus.NotAtTop, "scroll-offset-nonzero");

        if (itemCount < MinimumItemsToDistinguishSevenOrEightColumns)
            return Unknown(SteamCoverGridObservationStatus.InsufficientItems, "fewer-than-nine-catalog-items");

        var sampleCount = Math.Min(itemCount, MaximumSampledItems);
        if (arrangedItems.Count == 0 || arrangedItems[0].Index != 0)
            return Unknown(SteamCoverGridObservationStatus.PendingArrange, "first-item-not-arranged");

        var firstRow = new List<SteamCoverGridArrangedItem>(sampleCount);
        var firstRowY = arrangedItems[0].Y;
        for (var index = 0; index < sampleCount; index++)
        {
            if (index >= arrangedItems.Count || arrangedItems[index].Index != index)
            {
                return new SteamCoverGridObservation(
                    SteamCoverGridObservationStatus.PendingArrange,
                    null,
                    null,
                    $"item-{index}-not-arranged",
                    firstRow);
            }

            var item = arrangedItems[index];
            if (!double.IsFinite(item.X) || !double.IsFinite(item.Y))
            {
                return new SteamCoverGridObservation(
                    SteamCoverGridObservationStatus.PendingArrange,
                    null,
                    null,
                    $"item-{index}-has-invalid-position",
                    firstRow);
            }

            if (Math.Abs(item.Y - firstRowY) > rowToleranceDip)
            {
                return new SteamCoverGridObservation(
                    SteamCoverGridObservationStatus.Valid,
                    firstRow.Count,
                    index,
                    "first-wrapped-item-observed",
                    firstRow);
            }

            firstRow.Add(item);
        }

        return new SteamCoverGridObservation(
            itemCount > sampleCount
                ? SteamCoverGridObservationStatus.PendingArrange
                : SteamCoverGridObservationStatus.InsufficientItems,
            null,
            null,
            itemCount > sampleCount ? "wrapped-item-outside-sample" : "no-wrapped-item-in-catalog",
            firstRow);
    }

    private static SteamCoverGridObservation Unknown(SteamCoverGridObservationStatus status, string reason) =>
        new(status, null, null, reason, []);
}
