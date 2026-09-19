namespace SteamInputAddonforClaw.Contracts.Frontend;

/// <summary>The one surface-neutral product identity for Addon Quick Settings.</summary>
public enum AddonQuickSettingsTabId
{
    Device,
    Profile,
    Controller,
    Shortcut,
    Setting,
}

/// <summary>Validates the closed five-tab Addon Quick Settings order.</summary>
public static class AddonQuickSettingsTabOrderContract
{
    private static readonly AddonQuickSettingsTabId[] Default =
    [
        AddonQuickSettingsTabId.Device,
        AddonQuickSettingsTabId.Profile,
        AddonQuickSettingsTabId.Controller,
        AddonQuickSettingsTabId.Shortcut,
        AddonQuickSettingsTabId.Setting,
    ];

    public static IReadOnlyList<AddonQuickSettingsTabId> DefaultOrder => (AddonQuickSettingsTabId[])Default.Clone();

    public static bool TryNormalize(IReadOnlyList<AddonQuickSettingsTabId>? requested, out IReadOnlyList<AddonQuickSettingsTabId> normalized)
    {
        if (requested is null || requested.Count != Default.Length)
        {
            normalized = DefaultOrder;
            return false;
        }

        var seen = new HashSet<AddonQuickSettingsTabId>();
        foreach (var id in requested)
        {
            if (!Enum.IsDefined(id) || !seen.Add(id))
            {
                normalized = DefaultOrder;
                return false;
            }
        }

        normalized = requested.ToArray();
        return true;
    }

    public static IReadOnlyList<AddonQuickSettingsTabId> NormalizeOrDefault(IReadOnlyList<AddonQuickSettingsTabId>? requested)
    {
        TryNormalize(requested, out var normalized);
        return normalized;
    }
}

public sealed record AddonQuickSettingsShellTab(AddonQuickSettingsTabId TabId, string Label);

public sealed record AddonQuickSettingsShellSnapshot(
    bool Available,
    IReadOnlyList<AddonQuickSettingsShellTab> Tabs)
{
    public static AddonQuickSettingsShellSnapshot Unavailable() => new(false, Array.Empty<AddonQuickSettingsShellTab>());
}

/// <summary>Builds the closed product-level shell projection consumed by both frontend surfaces.</summary>
public static class AddonQuickSettingsShellContract
{
    public static string LabelFor(AddonQuickSettingsTabId tabId) => tabId switch
    {
        AddonQuickSettingsTabId.Device => "Device",
        AddonQuickSettingsTabId.Profile => "Profile",
        AddonQuickSettingsTabId.Controller => "Controller",
        AddonQuickSettingsTabId.Shortcut => "Shortcut",
        AddonQuickSettingsTabId.Setting => "Setting",
        _ => throw new ArgumentOutOfRangeException(nameof(tabId), tabId, "Unknown Addon Quick Settings tab.")
    };

    public static AddonQuickSettingsShellSnapshot Create(IReadOnlyList<AddonQuickSettingsTabId>? order)
    {
        var normalized = AddonQuickSettingsTabOrderContract.NormalizeOrDefault(order);
        return new(true, normalized.Select(tab => new AddonQuickSettingsShellTab(tab, LabelFor(tab))).ToArray());
    }
}
