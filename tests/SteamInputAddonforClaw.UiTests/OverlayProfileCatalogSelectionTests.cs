using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.UiTests;

public sealed class OverlayProfileCatalogSelectionTests
{
    [Fact]
    public void Three_column_selection_is_bounded_and_handles_an_incomplete_last_row()
    {
        var selection = new OverlayProfileCatalogSelection();
        selection.Reset(5);

        Assert.Equal(0, selection.SelectedIndex);
        Assert.True(selection.MoveRight());
        Assert.True(selection.MoveRight());
        Assert.False(selection.MoveRight());
        Assert.True(selection.MoveDown());
        Assert.Equal(4, selection.SelectedIndex);
        Assert.False(selection.MoveDown());
        Assert.True(selection.MoveLeft());
        Assert.Equal(3, selection.SelectedIndex);
        Assert.True(selection.MoveUp());
        Assert.Equal(0, selection.SelectedIndex);
        Assert.False(selection.MoveUp());
    }

    [Fact]
    public void Reset_empty_catalog_has_no_selectable_item()
    {
        var selection = new OverlayProfileCatalogSelection();
        selection.Reset(0);

        Assert.Equal(-1, selection.SelectedIndex);
        Assert.False(selection.MoveRight());
        Assert.False(selection.MoveDown());
    }
}
