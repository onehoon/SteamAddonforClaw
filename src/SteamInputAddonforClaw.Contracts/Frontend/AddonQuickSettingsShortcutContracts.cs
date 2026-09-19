namespace SteamInputAddonforClaw.Contracts.Frontend;

/// <summary>The closed set of Shortcut slots currently exposed by Addon Quick Settings.</summary>
public enum AddonQuickSettingsShortcutSlotId
{
    Slot1,
    Slot2,
    Slot3,
    Slot4,
}

/// <summary>One read-only slot in the current shared Shortcut product.</summary>
public sealed record AddonQuickSettingsShortcutSlot(
    AddonQuickSettingsShortcutSlotId SlotId,
    string Label,
    string StatusLabel);

/// <summary>The static Shortcut projection consumed by both frontend renderers.</summary>
public sealed record AddonQuickSettingsShortcutSnapshot(
    bool Available,
    IReadOnlyList<AddonQuickSettingsShortcutSlot> Slots)
{
    public static AddonQuickSettingsShortcutSnapshot Unavailable() =>
        new(false, Array.Empty<AddonQuickSettingsShortcutSlot>());
}

/// <summary>Defines the current fixed, read-only Shortcut product without inventing Runtime state.</summary>
public static class AddonQuickSettingsShortcutContract
{
    public static AddonQuickSettingsShortcutSnapshot Create() =>
        new(
            true,
            [
                new(AddonQuickSettingsShortcutSlotId.Slot1, "Slot 1", "Unassigned"),
                new(AddonQuickSettingsShortcutSlotId.Slot2, "Slot 2", "Unassigned"),
                new(AddonQuickSettingsShortcutSlotId.Slot3, "Slot 3", "Unassigned"),
                new(AddonQuickSettingsShortcutSlotId.Slot4, "Slot 4", "Unassigned"),
            ]);
}
