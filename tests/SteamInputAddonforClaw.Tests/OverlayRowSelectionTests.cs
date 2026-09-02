using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayRowSelectionTests
{
    private static OverlayRowCapabilities Row(bool selectable = true, Action? activate = null, Action<int>? adjust = null) =>
        new(() => selectable, activate, adjust);

    [Fact]
    public void NoRowsMeansNoSelection()
    {
        var selection = new OverlayRowSelection();

        selection.SetRows([]);

        Assert.Null(selection.SelectedIndex);
    }

    [Fact]
    public void SetRowsSelectsTheFirstSelectableRow()
    {
        var selection = new OverlayRowSelection();

        selection.SetRows([Row(selectable: false), Row(selectable: false), Row(), Row()]);

        Assert.Equal(2, selection.SelectedIndex);
    }

    [Fact]
    public void MoveNextAndPreviousStepThroughSelectableRows()
    {
        var selection = new OverlayRowSelection();
        selection.SetRows([Row(), Row(), Row()]);

        Assert.True(selection.MoveNext());
        Assert.Equal(1, selection.SelectedIndex);
        Assert.True(selection.MoveNext());
        Assert.Equal(2, selection.SelectedIndex);
        Assert.True(selection.MovePrevious());
        Assert.Equal(1, selection.SelectedIndex);
    }

    [Fact]
    public void MovesSkipUnselectableRows()
    {
        var selection = new OverlayRowSelection();
        selection.SetRows([Row(), Row(selectable: false), Row(selectable: false), Row()]);

        Assert.True(selection.MoveNext());
        Assert.Equal(3, selection.SelectedIndex);
        Assert.True(selection.MovePrevious());
        Assert.Equal(0, selection.SelectedIndex);
    }

    [Fact]
    public void SelectionIsBoundedAndDoesNotWrap()
    {
        var selection = new OverlayRowSelection();
        selection.SetRows([Row(), Row()]);

        Assert.False(selection.MovePrevious());
        Assert.Equal(0, selection.SelectedIndex);

        Assert.True(selection.MoveNext());
        Assert.False(selection.MoveNext());
        Assert.Equal(1, selection.SelectedIndex);
    }

    [Fact]
    public void AllRowsUnavailableClearsSelection()
    {
        var selection = new OverlayRowSelection();

        selection.SetRows([Row(selectable: false), Row(selectable: false)]);

        Assert.Null(selection.SelectedIndex);
        Assert.False(selection.MoveNext());
        Assert.False(selection.MovePrevious());
    }

    [Fact]
    public void SelectionNormalizesWhenTheCurrentRowBecomesUnselectable()
    {
        var secondSelectable = true;
        var rows = new[] { Row(selectable: false), new OverlayRowCapabilities(() => secondSelectable), Row() };
        var selection = new OverlayRowSelection();
        selection.SetRows(rows);
        Assert.Equal(1, selection.SelectedIndex);

        secondSelectable = false;

        // Next action re-evaluates IsSelectable and snaps to the first selectable row (index 2).
        Assert.False(selection.MoveNext());
        Assert.Equal(2, selection.SelectedIndex);
    }

    [Fact]
    public void AdjustDispatchesTheDeltaOnlyToAnAdjustableSelectedRow()
    {
        var deltas = new List<int>();
        var selection = new OverlayRowSelection();
        selection.SetRows([Row(adjust: deltas.Add), Row()]);

        selection.AdjustSelected(-1);
        selection.AdjustSelected(+1);
        Assert.Equal(new[] { -1, 1 }, deltas);

        selection.MoveNext(); // row without Adjust
        selection.AdjustSelected(-1);
        Assert.Equal(new[] { -1, 1 }, deltas);
    }

    [Fact]
    public void ActivateInvokesActivationOnlyForAnActivatableSelectedRow()
    {
        var activations = 0;
        var selection = new OverlayRowSelection();
        selection.SetRows([Row(activate: () => activations++), Row()]);

        selection.ActivateSelected();
        Assert.Equal(1, activations);

        selection.MoveNext(); // row without Activate
        selection.ActivateSelected();
        Assert.Equal(1, activations);
    }

    [Fact]
    public void NoSelectionMeansNoCapabilityCallback()
    {
        var activations = 0;
        var deltas = new List<int>();
        var selection = new OverlayRowSelection();
        selection.SetRows([new OverlayRowCapabilities(() => false, () => activations++, deltas.Add)]);

        selection.ActivateSelected();
        selection.AdjustSelected(1);

        Assert.Equal(0, activations);
        Assert.Empty(deltas);
    }
}
