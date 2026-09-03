using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayShortcutSelectionTests
{
    private static OverlayShortcutSelection At(OverlayShortcutSlotId slot)
    {
        var selection = new OverlayShortcutSelection();
        selection.Select(slot);
        return selection;
    }

    [Fact]
    public void StartsOnSlot1()
    {
        Assert.Equal(OverlayShortcutSlotId.Slot1, new OverlayShortcutSelection().SelectedSlot);
    }

    [Fact]
    public void DirectionalMovesFollowThe2x2Geometry()
    {
        (OverlayShortcutSlotId From, string Direction, OverlayShortcutSlotId Expected)[] cases =
        [
            (OverlayShortcutSlotId.Slot1, "Right", OverlayShortcutSlotId.Slot2),
            (OverlayShortcutSlotId.Slot1, "Down", OverlayShortcutSlotId.Slot3),
            (OverlayShortcutSlotId.Slot2, "Left", OverlayShortcutSlotId.Slot1),
            (OverlayShortcutSlotId.Slot2, "Down", OverlayShortcutSlotId.Slot4),
            (OverlayShortcutSlotId.Slot3, "Up", OverlayShortcutSlotId.Slot1),
            (OverlayShortcutSlotId.Slot3, "Right", OverlayShortcutSlotId.Slot4),
            (OverlayShortcutSlotId.Slot4, "Up", OverlayShortcutSlotId.Slot2),
            (OverlayShortcutSlotId.Slot4, "Left", OverlayShortcutSlotId.Slot3),
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
        (OverlayShortcutSlotId From, string Direction)[] cases =
        [
            (OverlayShortcutSlotId.Slot1, "Left"),
            (OverlayShortcutSlotId.Slot1, "Up"),
            (OverlayShortcutSlotId.Slot2, "Right"),
            (OverlayShortcutSlotId.Slot2, "Up"),
            (OverlayShortcutSlotId.Slot3, "Left"),
            (OverlayShortcutSlotId.Slot3, "Down"),
            (OverlayShortcutSlotId.Slot4, "Right"),
            (OverlayShortcutSlotId.Slot4, "Down"),
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
        var selection = At(OverlayShortcutSlotId.Slot4);

        selection.Reset();

        Assert.Equal(OverlayShortcutSlotId.Slot1, selection.SelectedSlot);
    }

    [Fact]
    public void SelectChangesTheIdentityAndReportsWhetherItMoved()
    {
        var selection = new OverlayShortcutSelection();

        Assert.True(selection.Select(OverlayShortcutSlotId.Slot3));
        Assert.Equal(OverlayShortcutSlotId.Slot3, selection.SelectedSlot);

        Assert.False(selection.Select(OverlayShortcutSlotId.Slot3)); // already there
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
