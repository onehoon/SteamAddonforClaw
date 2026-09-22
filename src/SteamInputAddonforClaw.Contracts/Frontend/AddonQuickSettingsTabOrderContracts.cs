namespace SteamInputAddonforClaw.Contracts.Frontend;

/// <summary>One authoritative row in the shared Setting tab-order editor.</summary>
public sealed record AddonQuickSettingsTabOrderRow(
    AddonQuickSettingsTabId TabId,
    string Label,
    bool CanMoveEarlier,
    bool CanMoveLater);

/// <summary>The Runtime-owned tab-order projection consumed by the Main UI and Overlay.</summary>
public sealed record AddonQuickSettingsTabOrderSnapshot(
    bool Available,
    IReadOnlyList<AddonQuickSettingsTabOrderRow> Rows)
{
    public static AddonQuickSettingsTabOrderSnapshot Unavailable() =>
        new(false, Array.Empty<AddonQuickSettingsTabOrderRow>());
}

/// <summary>A single bounded move in the shared Setting product.</summary>
public sealed record AddonQuickSettingsTabOrderMoveIntent(
    AddonQuickSettingsTabId TabId,
    int Delta);

/// <summary>The authoritative result of one shared Setting mutation.</summary>
public sealed record AddonQuickSettingsTabOrderMutationResult(
    bool Succeeded,
    string? FailureMessage,
    AddonQuickSettingsTabOrderSnapshot State);

/// <summary>Projects and validates the closed shared tab-order product.</summary>
public static class AddonQuickSettingsTabOrderProduct
{
    public static AddonQuickSettingsTabOrderSnapshot Create(
        IReadOnlyList<AddonQuickSettingsTabId>? order)
    {
        var normalized = AddonQuickSettingsTabOrderContract.NormalizeOrDefault(order);
        var rows = normalized
            .Select((tabId, index) => new AddonQuickSettingsTabOrderRow(
                tabId,
                AddonQuickSettingsShellContract.LabelFor(tabId),
                CanMoveEarlier: index > 0,
                CanMoveLater: index < normalized.Count - 1))
            .ToArray();

        return new(true, rows);
    }

    public static bool TryCreateMovedOrder(
        IReadOnlyList<AddonQuickSettingsTabId>? current,
        AddonQuickSettingsTabOrderMoveIntent? intent,
        out IReadOnlyList<AddonQuickSettingsTabId> proposed)
    {
        proposed = Array.Empty<AddonQuickSettingsTabId>();

        if (!AddonQuickSettingsTabOrderContract.TryNormalize(current, out var normalized) ||
            intent is null ||
            !Enum.IsDefined(intent.TabId) ||
            intent.Delta is not (-1 or 1))
        {
            return false;
        }

        var index = -1;
        for (var i = 0; i < normalized.Count; i++)
        {
            if (normalized[i] == intent.TabId)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            return false;

        var target = index + intent.Delta;
        if (target < 0 || target >= normalized.Count)
            return false;

        var moved = normalized.ToArray();
        (moved[index], moved[target]) = (moved[target], moved[index]);
        return AddonQuickSettingsTabOrderContract.TryNormalize(moved, out proposed);
    }
}
