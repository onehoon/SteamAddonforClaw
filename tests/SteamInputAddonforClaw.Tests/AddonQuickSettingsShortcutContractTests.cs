using SteamInputAddonforClaw.Contracts.Frontend;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonQuickSettingsShortcutContractTests
{
    [Fact]
    public void Create_returns_the_exact_four_slots_in_canonical_order()
    {
        var snapshot = AddonQuickSettingsShortcutContract.Create();

        Assert.True(snapshot.Available);
        Assert.Equal(
            [
                AddonQuickSettingsShortcutSlotId.Slot1,
                AddonQuickSettingsShortcutSlotId.Slot2,
                AddonQuickSettingsShortcutSlotId.Slot3,
                AddonQuickSettingsShortcutSlotId.Slot4,
            ],
            snapshot.Slots.Select(slot => slot.SlotId));
        Assert.Equal(4, snapshot.Slots.Count);
    }

    [Fact]
    public void Create_owns_the_shared_labels_and_unassigned_status()
    {
        var slots = AddonQuickSettingsShortcutContract.Create().Slots;

        Assert.Equal(
            [
                ("Slot 1", "Unassigned"),
                ("Slot 2", "Unassigned"),
                ("Slot 3", "Unassigned"),
                ("Slot 4", "Unassigned"),
            ],
            slots.Select(slot => (slot.Label, slot.StatusLabel)));
    }

    [Fact]
    public void Unavailable_returns_an_empty_fail_closed_snapshot()
    {
        var snapshot = AddonQuickSettingsShortcutSnapshot.Unavailable();

        Assert.False(snapshot.Available);
        Assert.Empty(snapshot.Slots);
    }
}
