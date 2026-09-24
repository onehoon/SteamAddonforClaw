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
    public void One_and_two_tiles_use_their_actual_column_count()
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
    public void More_than_four_tiles_are_supported_without_wrapping_rows()
    {
        var selection = At(tileCount: 6, columnCount: 2, index: 3);

        Assert.False(selection.MoveRight());
        Assert.True(selection.MoveDown());
        Assert.Equal(5, selection.SelectedIndex);
        Assert.False(selection.MoveRight());
    }

    [Fact]
    public void Vertical_moves_preserve_column_and_clamp_a_short_final_row()
    {
        var selection = At(tileCount: 5, columnCount: 2, index: 3);

        Assert.True(selection.MoveDown());
        Assert.Equal(4, selection.SelectedIndex);
        Assert.True(selection.MoveUp());
        Assert.Equal(2, selection.SelectedIndex);
    }

    [Fact]
    public void Horizontal_moves_stay_within_the_current_row()
    {
        var selection = At(tileCount: 5, columnCount: 2, index: 2);

        Assert.False(selection.MoveLeft());
        Assert.True(selection.MoveRight());
        Assert.Equal(3, selection.SelectedIndex);
        Assert.False(selection.MoveRight());
    }

    [Fact]
    public void First_and_last_row_edges_are_bounded()
    {
        var selection = At(tileCount: 5, columnCount: 2, index: 0);
        Assert.False(selection.MoveUp());

        selection.Select(4);
        Assert.False(selection.MoveDown());
        Assert.Equal(4, selection.SelectedIndex);
    }

    [Fact]
    public void Invalid_selection_is_rejected()
    {
        var selection = At(tileCount: 5, columnCount: 2, index: 1);

        Assert.False(selection.Select(-1));
        Assert.False(selection.Select(5));
        Assert.Equal(1, selection.SelectedIndex);
    }

    [Fact]
    public void Reconfigure_normalizes_deleted_selection_and_restores_a_valid_preferred_index()
    {
        var selection = At(tileCount: 6, columnCount: 2, index: 5);

        selection.Configure(tileCount: 3, columnCount: 2);
        Assert.Equal(0, selection.SelectedIndex);

        selection.Configure(tileCount: 5, columnCount: 2, preferredIndex: 3);
        Assert.Equal(3, selection.SelectedIndex);

        selection.Configure(tileCount: 2, columnCount: 2, preferredIndex: 4);
        Assert.Equal(0, selection.SelectedIndex);
    }

    [Fact]
    public void Reset_clears_layout_and_selection()
    {
        var selection = At(tileCount: 4, columnCount: 2, index: 3);

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
