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
    public void SetRowsWithAValidPreferredIndexKeepsThatRowSelected()
    {
        var selection = new OverlayRowSelection();

        selection.SetRows([Row(), Row(), Row(), Row(), Row()], preferredIndex: 3);

        Assert.Equal(3, selection.SelectedIndex);
    }

    [Fact]
    public void SetRowsWithAnInvalidOrUnselectablePreferredIndexFallsBackToFirstSelectable()
    {
        var selection = new OverlayRowSelection();

        selection.SetRows([Row(selectable: false), Row(), Row()], preferredIndex: 0); // unselectable
        Assert.Equal(1, selection.SelectedIndex);

        selection.SetRows([Row(), Row()], preferredIndex: 9); // out of range
        Assert.Equal(0, selection.SelectedIndex);

        selection.SetRows([Row(), Row()], preferredIndex: null); // default behaviour
        Assert.Equal(0, selection.SelectedIndex);
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
    public void MoveReportsNormalizationAsAnObservableSelectionChange()
    {
        var secondSelectable = true;
        var rows = new[] { Row(selectable: false), new OverlayRowCapabilities(() => secondSelectable), Row() };
        var selection = new OverlayRowSelection();
        selection.SetRows(rows);
        Assert.Equal(1, selection.SelectedIndex);

        secondSelectable = false;

        // Up/Down re-evaluates IsSelectable, snaps to the first selectable row (index 2), and
        // reports true so the caller refreshes the visible highlight.
        Assert.True(selection.MoveNext());
        Assert.Equal(2, selection.SelectedIndex);
    }

    [Fact]
    public void ActivateAndAdjustDoNotDispatchToTheFallbackRowOnTheSameInput()
    {
        var firstSelectable = true;
        var fallbackActivations = 0;
        var fallbackDeltas = new List<int>();
        var rows = new[]
        {
            new OverlayRowCapabilities(() => firstSelectable, () => Assert.Fail("stale row activated"), _ => Assert.Fail("stale row adjusted")),
            new OverlayRowCapabilities(() => true, () => fallbackActivations++, fallbackDeltas.Add),
        };
        var selection = new OverlayRowSelection();
        selection.SetRows(rows);
        Assert.Equal(0, selection.SelectedIndex);

        firstSelectable = false;

        // Accept: normalization moves selection to row 1 and reports true; row 1 is NOT activated.
        Assert.True(selection.ActivateSelected());
        Assert.Equal(1, selection.SelectedIndex);
        Assert.Equal(0, fallbackActivations);

        // A second Accept now actually activates the (already visible) row 1.
        Assert.False(selection.ActivateSelected());
        Assert.Equal(1, fallbackActivations);

        // Adjust follows the same rule after another forced normalization.
        selection.SetRows(rows);
        firstSelectable = true; // row 0 selectable again -> SetRows reselects it
        selection.SetRows(rows);
        firstSelectable = false;
        Assert.True(selection.AdjustSelected(+1));
        Assert.Empty(fallbackDeltas);
        Assert.False(selection.AdjustSelected(+1));
        Assert.Equal(new[] { 1 }, fallbackDeltas);
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
