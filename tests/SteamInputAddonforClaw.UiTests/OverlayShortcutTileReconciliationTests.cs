using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayShortcutTileReconciliationTests
{
    [Fact]
    public void Unchanged_snapshot_has_no_tile_or_position_work()
    {
        var tiles = new[] { Tile("one"), Tile("two") };

        var diff = OverlayShortcutTileReconciliation.Between(tiles, tiles.ToArray());

        Assert.True(diff.IsUnchanged);
        Assert.Empty(diff.AddedTileIds);
        Assert.Empty(diff.RemovedTileIds);
        Assert.Empty(diff.UpdatedTileIds);
        Assert.Empty(diff.RepositionedTileIds);
    }

    [Fact]
    public void Single_tile_presentation_change_updates_only_that_tile()
    {
        var first = Tile("one");
        var second = Tile("two");
        var updated = second with
        {
            Title = "Updated title",
            StatusText = "Ready",
            State = FrontendShortcutTileState.Active,
            Enabled = false,
            IconGlyph = "new-icon",
        };

        var diff = OverlayShortcutTileReconciliation.Between([first, second], [first, updated]);

        Assert.Equal(new[] { second.TileId }, diff.UpdatedTileIds);
        Assert.Empty(diff.AddedTileIds);
        Assert.Empty(diff.RemovedTileIds);
        Assert.Empty(diff.RepositionedTileIds);
    }

    [Fact]
    public void Addition_and_removal_preserve_surviving_tile_identity_and_report_new_positions()
    {
        var removed = Tile("removed");
        var retained = Tile("retained");
        var added = Tile("added");

        var diff = OverlayShortcutTileReconciliation.Between([removed, retained], [retained, added]);

        Assert.Equal(new[] { added.TileId }, diff.AddedTileIds);
        Assert.Equal(new[] { removed.TileId }, diff.RemovedTileIds);
        Assert.Equal(new[] { retained.TileId }, diff.RepositionedTileIds);
        Assert.Empty(diff.UpdatedTileIds);
    }

    [Fact]
    public void Reordering_reports_only_tiles_whose_order_changed()
    {
        var first = Tile("first");
        var second = Tile("second");
        var third = Tile("third");

        var diff = OverlayShortcutTileReconciliation.Between(
            [first, second, third], [second, first, third]);

        Assert.Empty(diff.AddedTileIds);
        Assert.Empty(diff.RemovedTileIds);
        Assert.Empty(diff.UpdatedTileIds);
        Assert.Equal(new[] { second.TileId, first.TileId }, diff.RepositionedTileIds);
    }

    [Fact]
    public void Selection_follows_tile_id_and_falls_back_to_first_tile_when_removed()
    {
        var first = Tile("first");
        var selected = Tile("selected");
        var third = Tile("third");
        var reordered = new[] { third, first, selected };
        var selection = new OverlayShortcutSelection();

        var preferredIndex = OverlayShortcutSelection.FindTileIndex(selected.TileId, reordered);
        selection.Configure(reordered.Length, columnCount: 3, preferredIndex: preferredIndex);

        Assert.Equal(2, selection.SelectedIndex);
        Assert.Equal(selected.TileId, reordered[selection.SelectedIndex!.Value].TileId);

        var remaining = new[] { third, first };
        var selectedRemovedIndex = OverlayShortcutSelection.FindTileIndex(selected.TileId, remaining);
        selection.Configure(tileCount: remaining.Length, columnCount: 2,
            preferredIndex: selectedRemovedIndex >= 0 ? selectedRemovedIndex : 0);

        Assert.Equal(0, selection.SelectedIndex);
        Assert.Equal(third.TileId, remaining[selection.SelectedIndex!.Value].TileId);
        Assert.Equal(-1, OverlayShortcutSelection.FindTileIndex(first.TileId, Array.Empty<FrontendShortcutTile>()));
    }

    private static FrontendShortcutTile Tile(string id) => new(
        Guid.NewGuid(),
        id,
        null,
        FrontendShortcutTileState.Neutral,
        Enabled: true,
        IconGlyph: "icon");
}
