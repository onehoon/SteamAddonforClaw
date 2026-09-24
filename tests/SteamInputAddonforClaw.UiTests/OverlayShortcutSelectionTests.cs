using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayShortcutSelectionTests
{
    private static OverlayShortcutSelection At(int index)
    {
        var selection = new OverlayShortcutSelection();
        selection.Select(index);
        return selection;
    }

    [Fact]
    public void StartsOnFirstTemporaryTile()
    {
        Assert.Equal(0, new OverlayShortcutSelection().SelectedIndex);
    }

    [Fact]
    public void DirectionalMovesFollowTheTemporary2x2Geometry()
    {
        (int From, string Direction, int Expected)[] cases =
        [
            (0, "Right", 1),
            (0, "Down", 2),
            (1, "Left", 0),
            (1, "Down", 3),
            (2, "Up", 0),
            (2, "Right", 3),
            (3, "Up", 1),
            (3, "Left", 2),
        ];

        foreach (var (from, direction, expected) in cases)
        {
            var selection = At(from);
            Assert.True(Move(selection, direction), $"{from} {direction}");
            Assert.Equal(expected, selection.SelectedIndex);
        }
    }

    [Fact]
    public void OuterEdgesAreBoundedNoOps()
    {
        (int From, string Direction)[] cases =
        [
            (0, "Left"),
            (0, "Up"),
            (1, "Right"),
            (1, "Up"),
            (2, "Left"),
            (2, "Down"),
            (3, "Right"),
            (3, "Down"),
        ];

        foreach (var (from, direction) in cases)
        {
            var selection = At(from);
            Assert.False(Move(selection, direction), $"{from} {direction}");
            Assert.Equal(from, selection.SelectedIndex);
        }
    }

    [Fact]
    public void ResetReturnsToFirstTemporaryTile()
    {
        var selection = At(3);

        selection.Reset();

        Assert.Equal(0, selection.SelectedIndex);
    }

    [Fact]
    public void SelectChangesTheIndexAndRejectsInvalidIndexes()
    {
        var selection = new OverlayShortcutSelection();

        Assert.True(selection.Select(2));
        Assert.Equal(2, selection.SelectedIndex);
        Assert.False(selection.Select(2));
        Assert.False(selection.Select(-1));
        Assert.False(selection.Select(4));
        Assert.Equal(2, selection.SelectedIndex);
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
