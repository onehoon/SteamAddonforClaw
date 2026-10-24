using SteamInputAddonforClaw.Contracts.Frontend;
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
    public void SelectedSecondRowSurvivesTemporaryMutationDisableAndAuthoritativeRefresh()
    {
        var mutationPending = false;
        var rows = new[]
        {
            new OverlayRowCapabilities(() => !mutationPending),
            new OverlayRowCapabilities(() => !mutationPending),
        };
        var selection = new OverlayRowSelection();
        selection.SetRows(rows);
        Assert.True(selection.MoveNext());
        Assert.Equal(1, selection.SelectedIndex);

        mutationPending = true;
        // Mutation and an intervening authoritative refresh leave row capabilities disabled.
        // The renderer skips SetRows until settlement, so the logical M2 selection is retained.
        Assert.Equal(1, selection.SelectedIndex);

        mutationPending = false;
        selection.SetRows(rows, preferredIndex: selection.SelectedIndex);

        Assert.Equal(1, selection.SelectedIndex);
    }

    [Fact]
    public void ClawHud_siblings_remain_navigable_during_single_flight_but_not_when_runtime_is_not_ready()
    {
        var mutationPending = false;
        var runtimeReady = true;
        var cardExpanded = true;
        var blockedRequests = 0;
        OverlayRowCapabilities ClawHudDetailRow()
        {
            var model = new OverlayValueModel(_ => blockedRequests++);
            void ApplyNativeAvailability() => model.ApplyState(runtimeReady && !mutationPending, 0, 2, 1, 0);
            var nativeCapabilities = new OverlayRowCapabilities(
                () =>
                {
                    ApplyNativeAvailability();
                    return model.IsAvailable;
                },
                Adjust: delta =>
                {
                    ApplyNativeAvailability();
                    model.RequestAdjust(delta);
                });
            return new OverlayRowCapabilities(
                () => cardExpanded && (nativeCapabilities.IsSelectable() ||
                    mutationPending && runtimeReady),
                Adjust: nativeCapabilities.Adjust);
        }

        // Setting order: ClawHUD header, Enable HUD, Display Mode, HUD Size, Font,
        // Alignment, then the next card header.
        var rows = new[]
        {
            Row(),
            ClawHudDetailRow(),
            ClawHudDetailRow(),
            ClawHudDetailRow(),
            ClawHudDetailRow(),
            ClawHudDetailRow(),
            Row(),
        };
        var selection = new OverlayRowSelection();
        selection.SetRows(rows);
        Assert.True(selection.TrySelect(3)); // HUD Size.

        mutationPending = true;
        Assert.True(selection.MoveNext());
        Assert.Equal(4, selection.SelectedIndex); // Font, not the next card header.
        Assert.True(selection.MoveNext());
        Assert.Equal(5, selection.SelectedIndex); // Alignment remains a logical sibling too.
        Assert.False(selection.AdjustSelected(+1));
        Assert.Equal(0, blockedRequests); // The underlying disabled control model rejects edits.

        runtimeReady = false;
        selection.SetRows(rows, preferredIndex: 5);
        Assert.Equal(0, selection.SelectedIndex);
        Assert.False(selection.TrySelect(4));

        runtimeReady = true;
        cardExpanded = false;
        selection.SetRows(rows, preferredIndex: 4);
        Assert.Equal(0, selection.SelectedIndex);
        Assert.False(selection.TrySelect(4));
    }

    [Fact]
    public void SelectedControllerRowCanBeRestoredAfterAnIntermediateFallbackDuringRefresh()
    {
        var ledAvailable = true;
        var rows = new[]
        {
            Row(),
            Row(),
            Row(),
            new OverlayRowCapabilities(() => ledAvailable),
        };
        var selection = new OverlayRowSelection();
        selection.SetRows(rows);
        Assert.True(selection.TrySelect(3));

        var preferredIndex = selection.SelectedIndex;
        ledAvailable = false;
        selection.SetRows(rows, preferredIndex); // An earlier sibling renderer sees the stale LED availability.
        Assert.Equal(0, selection.SelectedIndex);

        ledAvailable = true;
        selection.SetRows(rows, preferredIndex); // Final Controller reconciliation restores the saved row.

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
    public void TrySelectMovesOnlyToAValidSelectableRow()
    {
        var selection = new OverlayRowSelection();
        selection.SetRows([Row(), Row(selectable: false), Row(), Row()]);

        Assert.True(selection.TrySelect(2));
        Assert.Equal(2, selection.SelectedIndex);
    }

    [Fact]
    public void TrySelectRejectsAlreadySelectedInvalidAndUnavailableRows()
    {
        var selection = new OverlayRowSelection();
        selection.SetRows([Row(), Row(selectable: false), Row()]);

        Assert.False(selection.TrySelect(0));
        Assert.False(selection.TrySelect(-1));
        Assert.False(selection.TrySelect(3));
        Assert.False(selection.TrySelect(1));
        Assert.Equal(0, selection.SelectedIndex);
    }

    [Fact]
    public void TrySelectDoesNotActivateOrAdjustTheSelectedRow()
    {
        var activations = 0;
        var adjustments = new List<int>();
        var selection = new OverlayRowSelection();
        selection.SetRows([Row(), Row(activate: () => activations++, adjust: adjustments.Add)]);

        Assert.True(selection.TrySelect(1));

        Assert.Equal(0, activations);
        Assert.Empty(adjustments);
        Assert.Equal(1, selection.SelectedIndex);
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

    [Fact]
    public void Numeric_slider_repeat_target_adjusts_only_the_original_selected_row_and_direction()
    {
        var deltas = new List<int>();
        var slider = new OverlayRowCapabilities(() => true, Adjust: deltas.Add, AllowsDpadHoldRepeat: true);
        var target = new OverlayNumericSliderRepeatTarget();

        target.Arm(AddonQuickSettingsTabId.Device, slider, +1);

        Assert.True(target.TryAdjust(AddonQuickSettingsTabId.Device, slider, +1));
        Assert.Equal(new[] { 1 }, deltas);
        Assert.False(target.TryAdjust(AddonQuickSettingsTabId.Device, slider, -1));
        Assert.Equal(new[] { 1 }, deltas);
    }

    [Fact]
    public void Numeric_slider_repeat_target_rejects_other_tabs_rows_and_replacements()
    {
        var deltas = new List<int>();
        var slider = new OverlayRowCapabilities(() => true, Adjust: deltas.Add, AllowsDpadHoldRepeat: true);
        var replacement = new OverlayRowCapabilities(() => true, Adjust: deltas.Add, AllowsDpadHoldRepeat: true);
        var target = new OverlayNumericSliderRepeatTarget();

        target.Arm(AddonQuickSettingsTabId.Device, slider, -1);
        Assert.False(target.TryAdjust(AddonQuickSettingsTabId.Profile, slider, -1));
        Assert.False(target.TryAdjust(AddonQuickSettingsTabId.Device, replacement, -1));
        Assert.Empty(deltas);
    }

    [Fact]
    public void Numeric_slider_repeat_target_rejects_default_non_slider_capabilities()
    {
        var deltas = new List<int>();
        var toggle = new OverlayRowCapabilities(() => true, Adjust: deltas.Add);
        var numericStepper = new OverlayRowCapabilities(() => true, Adjust: deltas.Add);
        var target = new OverlayNumericSliderRepeatTarget();

        target.Arm(AddonQuickSettingsTabId.Controller, toggle, +1);
        Assert.False(target.TryAdjust(AddonQuickSettingsTabId.Controller, toggle, +1));
        target.Arm(AddonQuickSettingsTabId.Controller, numericStepper, +1);
        Assert.False(target.TryAdjust(AddonQuickSettingsTabId.Controller, numericStepper, +1));

        Assert.Empty(deltas);
    }

    [Fact]
    public void Numeric_slider_repeat_target_requires_current_availability_and_clears_when_it_is_lost()
    {
        var available = true;
        var deltas = new List<int>();
        var slider = new OverlayRowCapabilities(() => available, Adjust: deltas.Add, AllowsDpadHoldRepeat: true);
        var target = new OverlayNumericSliderRepeatTarget();
        target.Arm(AddonQuickSettingsTabId.Profile, slider, +1);

        available = false;
        Assert.False(target.TryAdjust(AddonQuickSettingsTabId.Profile, slider, +1));
        available = true;
        Assert.False(target.TryAdjust(AddonQuickSettingsTabId.Profile, slider, +1));
        Assert.Empty(deltas);
    }

    [Fact]
    public void Numeric_slider_repeat_target_survives_value_only_refresh_when_the_row_is_reused()
    {
        var deltas = new List<int>();
        var slider = new OverlayRowCapabilities(() => true, Adjust: deltas.Add, AllowsDpadHoldRepeat: true);
        var target = new OverlayNumericSliderRepeatTarget();
        target.Arm(AddonQuickSettingsTabId.Device, slider, -1);

        // Value-only refresh retains the same capability/row instance and selectable state.
        Assert.True(target.TryAdjust(AddonQuickSettingsTabId.Device, slider, -1));
        Assert.Equal(new[] { -1 }, deltas);
    }

    [Fact]
    public void Clearing_a_repeat_target_blocks_the_held_gesture_until_a_fresh_horizontal_press_arms_it_again()
    {
        var deltas = new List<int>();
        var slider = new OverlayRowCapabilities(() => true, Adjust: deltas.Add, AllowsDpadHoldRepeat: true);
        var target = new OverlayNumericSliderRepeatTarget();
        target.Arm(AddonQuickSettingsTabId.Device, slider, +1);

        target.Clear();
        Assert.False(target.TryAdjust(AddonQuickSettingsTabId.Device, slider, +1));
        Assert.Empty(deltas);

        // A new ordinary horizontal action calls Arm after applying its initial step.
        deltas.Add(+1);
        target.Arm(AddonQuickSettingsTabId.Device, slider, +1);
        Assert.True(target.TryAdjust(AddonQuickSettingsTabId.Device, slider, +1));
        Assert.Equal(new[] { 1, 1 }, deltas);
    }
}
