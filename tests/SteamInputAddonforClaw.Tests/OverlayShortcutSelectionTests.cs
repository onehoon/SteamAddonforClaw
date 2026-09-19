using SteamInputAddonforClaw.Overlay;
using SteamInputAddonforClaw.Contracts.Frontend;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayShortcutSelectionTests
{
    private static OverlayShortcutSelection At(AddonQuickSettingsShortcutSlotId slot)
    {
        var selection = new OverlayShortcutSelection();
        selection.Select(slot);
        return selection;
    }

    [Fact]
    public void StartsOnSlot1()
    {
        Assert.Equal(AddonQuickSettingsShortcutSlotId.Slot1, new OverlayShortcutSelection().SelectedSlot);
    }

    [Fact]
    public void DirectionalMovesFollowThe2x2Geometry()
    {
        (AddonQuickSettingsShortcutSlotId From, string Direction, AddonQuickSettingsShortcutSlotId Expected)[] cases =
        [
            (AddonQuickSettingsShortcutSlotId.Slot1, "Right", AddonQuickSettingsShortcutSlotId.Slot2),
            (AddonQuickSettingsShortcutSlotId.Slot1, "Down", AddonQuickSettingsShortcutSlotId.Slot3),
            (AddonQuickSettingsShortcutSlotId.Slot2, "Left", AddonQuickSettingsShortcutSlotId.Slot1),
            (AddonQuickSettingsShortcutSlotId.Slot2, "Down", AddonQuickSettingsShortcutSlotId.Slot4),
            (AddonQuickSettingsShortcutSlotId.Slot3, "Up", AddonQuickSettingsShortcutSlotId.Slot1),
            (AddonQuickSettingsShortcutSlotId.Slot3, "Right", AddonQuickSettingsShortcutSlotId.Slot4),
            (AddonQuickSettingsShortcutSlotId.Slot4, "Up", AddonQuickSettingsShortcutSlotId.Slot2),
            (AddonQuickSettingsShortcutSlotId.Slot4, "Left", AddonQuickSettingsShortcutSlotId.Slot3),
        ];

        foreach (var (from, direction, expected) in cases)
        {
            var selection = At(from);
            Assert.True(Move(selection, direction), $"{from} {direction}");
            Assert.Equal(expected, selection.SelectedSlot);
        }
    }

    [Fact]
    public void OuterEdgesAreBoundedNoOps()
    {
        (AddonQuickSettingsShortcutSlotId From, string Direction)[] cases =
        [
            (AddonQuickSettingsShortcutSlotId.Slot1, "Left"),
            (AddonQuickSettingsShortcutSlotId.Slot1, "Up"),
            (AddonQuickSettingsShortcutSlotId.Slot2, "Right"),
            (AddonQuickSettingsShortcutSlotId.Slot2, "Up"),
            (AddonQuickSettingsShortcutSlotId.Slot3, "Left"),
            (AddonQuickSettingsShortcutSlotId.Slot3, "Down"),
            (AddonQuickSettingsShortcutSlotId.Slot4, "Right"),
            (AddonQuickSettingsShortcutSlotId.Slot4, "Down"),
        ];

        foreach (var (from, direction) in cases)
        {
            var selection = At(from);
            Assert.False(Move(selection, direction), $"{from} {direction}");
            Assert.Equal(from, selection.SelectedSlot);
        }
    }

    [Fact]
    public void ResetReturnsToSlot1()
    {
        var selection = At(AddonQuickSettingsShortcutSlotId.Slot4);

        selection.Reset();

        Assert.Equal(AddonQuickSettingsShortcutSlotId.Slot1, selection.SelectedSlot);
    }

    [Fact]
    public void SelectChangesTheIdentityAndReportsWhetherItMoved()
    {
        var selection = new OverlayShortcutSelection();

        Assert.True(selection.Select(AddonQuickSettingsShortcutSlotId.Slot3));
        Assert.Equal(AddonQuickSettingsShortcutSlotId.Slot3, selection.SelectedSlot);

        Assert.False(selection.Select(AddonQuickSettingsShortcutSlotId.Slot3)); // already there
    }

    private static bool Move(OverlayShortcutSelection selection, string direction) => direction switch
    {
        "Up" => selection.MoveUp(),
        "Down" => selection.MoveDown(),
        "Left" => selection.MoveLeft(),
        "Right" => selection.MoveRight(),
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };
}
