using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayShortcutSelectionTests
{
    [Fact]
    public void Zero_tiles_have_no_columns_or_selection()
    {
        var selection = new OverlayShortcutSelection();

        selection.Configure(tileCount: 0, columnCount: 0);

        Assert.Equal(0, selection.TileCount);
        Assert.Equal(0, selection.ColumnCount);
        Assert.Null(selection.SelectedIndex);
        Assert.False(selection.MoveDown());
    }

    [Fact]
    public void Logical_selection_columns_stay_bounded_to_populated_rows()
    {
        var selection = new OverlayShortcutSelection();

        selection.Configure(tileCount: 1, columnCount: 1);
        Assert.Equal(0, selection.SelectedIndex);
        Assert.Equal(1, selection.ColumnCount);
        Assert.False(selection.MoveRight());

        selection.Configure(tileCount: 2, columnCount: 2);
        Assert.Equal(0, selection.SelectedIndex);
        Assert.Equal(2, selection.ColumnCount);
        Assert.True(selection.MoveRight());
        Assert.Equal(1, selection.SelectedIndex);
    }

    [Fact]
    public void Three_columns_match_full_six_tile_rows()
    {
        var selection = At(tileCount: 6, columnCount: 3, index: 1);

        Assert.True(selection.MoveDown());
        Assert.Equal(4, selection.SelectedIndex);
        Assert.True(selection.MoveUp());
        Assert.Equal(1, selection.SelectedIndex);
        selection.Select(2);
        Assert.True(selection.MoveDown());
        Assert.Equal(5, selection.SelectedIndex);
        Assert.False(selection.MoveDown());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(5, 3)]
    [InlineData(6, 3)]
    public void One_through_six_tiles_use_the_populated_three_column_selection_geometry(int tileCount, int columnCount)
    {
        var selection = At(tileCount, columnCount, index: tileCount - 1);

        Assert.Equal(tileCount, selection.TileCount);
        Assert.Equal(columnCount, selection.ColumnCount);
        Assert.Equal(tileCount - 1, selection.SelectedIndex);
        Assert.False(selection.MoveRight());
    }

    [Fact]
    public void Vertical_moves_follow_three_column_geometry_and_clamp_a_short_final_row()
    {
        var selection = At(tileCount: 5, columnCount: 3, index: 2);

        Assert.True(selection.MoveDown());
        Assert.Equal(4, selection.SelectedIndex);
        Assert.True(selection.MoveUp());
        Assert.Equal(1, selection.SelectedIndex);

        selection.Select(3);
        Assert.False(selection.MoveDown());
        Assert.True(selection.MoveUp());
        Assert.Equal(0, selection.SelectedIndex);
    }

    [Fact]
    public void Horizontal_moves_stay_within_the_current_row()
    {
        var selection = At(tileCount: 5, columnCount: 3, index: 2);

        Assert.True(selection.MoveLeft());
        Assert.Equal(1, selection.SelectedIndex);
        selection.Select(2);
        Assert.False(selection.MoveRight());
        selection.Select(3);
        Assert.False(selection.MoveLeft());
        Assert.True(selection.MoveRight());
        Assert.Equal(4, selection.SelectedIndex);
        Assert.False(selection.MoveRight());
    }

    [Fact]
    public void First_and_last_row_edges_are_bounded()
    {
        var selection = At(tileCount: 5, columnCount: 3, index: 0);
        Assert.False(selection.MoveUp());

        selection.Select(4);
        Assert.False(selection.MoveDown());
        Assert.Equal(4, selection.SelectedIndex);
    }

    [Fact]
    public void Invalid_selection_is_rejected()
    {
        var selection = At(tileCount: 5, columnCount: 3, index: 1);

        Assert.False(selection.Select(-1));
        Assert.False(selection.Select(5));
        Assert.Equal(1, selection.SelectedIndex);
    }

    [Fact]
    public void Reconfigure_normalizes_deleted_selection_and_restores_a_valid_preferred_index()
    {
        var selection = At(tileCount: 6, columnCount: 3, index: 5);

        selection.Configure(tileCount: 3, columnCount: 3);
        Assert.Equal(0, selection.SelectedIndex);

        selection.Configure(tileCount: 5, columnCount: 3, preferredIndex: 3);
        Assert.Equal(3, selection.SelectedIndex);

        selection.Configure(tileCount: 2, columnCount: 2, preferredIndex: 4);
        Assert.Equal(0, selection.SelectedIndex);
    }

    [Fact]
    public void Reset_clears_layout_and_selection()
    {
        var selection = At(tileCount: 4, columnCount: 3, index: 3);

        selection.Reset();

        Assert.Equal(0, selection.TileCount);
        Assert.Equal(0, selection.ColumnCount);
        Assert.Null(selection.SelectedIndex);
    }

    private static OverlayShortcutSelection At(int tileCount, int columnCount, int index)
    {
        var selection = new OverlayShortcutSelection();
        selection.Configure(tileCount, columnCount, index);
        return selection;
    }
}
