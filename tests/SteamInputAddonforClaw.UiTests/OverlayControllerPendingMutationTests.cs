using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Contracts.ControllerLed;
using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.UiTests;

public sealed class OverlayControllerPendingMutationTests
{
    [Theory]
    [InlineData(true, Xbox360BackButtonTarget.B)]
    [InlineData(false, Xbox360BackButtonTarget.Menu)]
    public void Back_button_preview_overlays_only_the_requested_member_on_latest_authority(
        bool isM1,
        Xbox360BackButtonTarget target)
    {
        var latest = new BackButtonMappingSettings(Xbox360BackButtonTarget.X, Xbox360BackButtonTarget.RightBumper);
        var preview = new OverlayBackButtonMappingPreview(isM1, target);

        var projected = OverlayWindow.ProjectBackButtonMappingPreview(latest, preview);

        Assert.Equal(isM1 ? target : Xbox360BackButtonTarget.X, projected.M1);
        Assert.Equal(isM1 ? Xbox360BackButtonTarget.RightBumper : target, projected.M2);
        Assert.Equal(latest, OverlayWindow.ProjectBackButtonMappingPreview(latest, null));
    }

    [Fact]
    public void Led_preview_overlays_only_fields_changed_by_the_pending_request()
    {
        var baseline = new ControllerLedSettings(true, 40, 10, 20, 30);
        var latest = new ControllerLedSettings(true, 42, 11, 21, 31);
        var cases = new[]
        {
            (baseline with { Enabled = false }, latest with { Enabled = false }),
            (baseline with { Brightness = 75 }, latest with { Brightness = 75 }),
            (baseline with { Red = 50 }, latest with { Red = 50 }),
            (baseline with { Green = 60 }, latest with { Green = 60 }),
            (baseline with { Blue = 70 }, latest with { Blue = 70 }),
        };

        foreach (var (desired, expected) in cases)
        {
            var projected = OverlayWindow.ProjectControllerLedPendingEdit(
                latest,
                new OverlayControllerLedPendingEdit(baseline, desired));

            Assert.Equal(expected, projected);
        }

        Assert.Equal(latest, OverlayWindow.ProjectControllerLedPendingEdit(latest, null));
    }

    [Fact]
    public void Disabled_controller_row_remains_selected_and_navigable_during_its_available_pending_mutation()
    {
        var acceptedRequests = 0;
        var pending = false;
        var genuinelyAvailable = true;
        var model = new OverlayValueModel(_ => acceptedRequests++);
        model.ApplyState(true, 0, 10, 1, 5);
        var nativeCapabilities = new OverlayRowCapabilities(
            IsSelectable: () => model.IsAvailable,
            Adjust: model.RequestAdjust);
        var capabilities = OverlayWindow.PreserveControllerPendingSelection(
            nativeCapabilities,
            () => pending && genuinelyAvailable);
        var selection = new OverlayRowSelection();
        selection.SetRows([
            new OverlayRowCapabilities(() => true),
            capabilities,
            new OverlayRowCapabilities(() => true),
        ]);
        Assert.True(selection.TrySelect(1));

        pending = true;
        model.ApplyState(false, 0, 10, 1, 7);

        Assert.Equal(1, selection.SelectedIndex);
        Assert.False(selection.AdjustSelected(+1));
        Assert.Equal(0, acceptedRequests);
        Assert.True(selection.MoveNext());
        Assert.Equal(2, selection.SelectedIndex);
        Assert.True(selection.MovePrevious());
        Assert.Equal(1, selection.SelectedIndex);

        genuinelyAvailable = false;
        selection.SetRows([
            new OverlayRowCapabilities(() => true),
            capabilities,
            new OverlayRowCapabilities(() => true),
        ], preferredIndex: 1);

        Assert.Equal(0, selection.SelectedIndex);
    }

    [Fact]
    public void Controller_row_models_keep_the_accepted_value_but_reject_edits_until_readback()
    {
        var valueRequests = 0;
        var value = new OverlayValueModel(_ => valueRequests++);
        value.ApplyState(false, 0, 10, 1, 7);
        Assert.Equal(7, value.PreviewValue);
        value.RequestAdjust(+1);
        Assert.Equal(0, valueRequests);
        value.ApplyState(true, 0, 10, 1, 7); // successful authoritative readback
        Assert.Equal(7, value.PreviewValue);

        var toggleRequests = 0;
        var toggle = new OverlayToggleModel(_ => toggleRequests++);
        toggle.ApplyState(false, true);
        Assert.True(toggle.IsOn);
        toggle.RequestToggle();
        Assert.Equal(0, toggleRequests);
        toggle.ApplyState(true, false); // failure readback restores the authoritative value
        Assert.False(toggle.IsOn);
    }
}
