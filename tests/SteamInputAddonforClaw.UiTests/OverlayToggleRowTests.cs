using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

// Covers the pure OverlayToggleModel. The WinUI OverlayToggleRow wrapper (ToggleSwitch +
// event-suppression) needs a XAML host and is validated on hardware per the work order.
public sealed class OverlayToggleRowTests
{
    [Fact]
    public void AvailableOffPlusActivationRequestsTrue()
    {
        var requests = new List<bool>();
        var model = new OverlayToggleModel(requests.Add);
        model.ApplyState(isAvailable: true, isOn: false);

        model.RequestToggle();

        Assert.Equal(new[] { true }, requests);
    }

    [Fact]
    public void AvailableOnPlusActivationRequestsFalse()
    {
        var requests = new List<bool>();
        var model = new OverlayToggleModel(requests.Add);
        model.ApplyState(isAvailable: true, isOn: true);

        model.RequestToggle();

        Assert.Equal(new[] { false }, requests);
    }

    [Fact]
    public void UnavailableRowEmitsNoRequest()
    {
        var requests = new List<bool>();
        var model = new OverlayToggleModel(requests.Add);
        model.ApplyState(isAvailable: false, isOn: false);

        model.RequestToggle();
        model.RequestSet(true);

        Assert.Empty(requests);
    }

    [Fact]
    public void PointerSetRequestsTheDesiredStateWhenAvailable()
    {
        var requests = new List<bool>();
        var model = new OverlayToggleModel(requests.Add);
        model.ApplyState(isAvailable: true, isOn: false);

        model.RequestSet(true);

        Assert.Equal(new[] { true }, requests);
    }

    [Fact]
    public void ApplyStateChangesStateWithoutEmittingARequest()
    {
        var requests = new List<bool>();
        var model = new OverlayToggleModel(requests.Add);

        model.ApplyState(isAvailable: true, isOn: true);
        model.ApplyState(isAvailable: true, isOn: false);
        model.ApplyState(isAvailable: false, isOn: false);

        Assert.Empty(requests);
        Assert.False(model.IsAvailable);
        Assert.False(model.IsOn);
    }

    [Fact]
    public void AuthoritativeReadbackDoesNotCreateAFeedbackLoop()
    {
        var requests = new List<bool>();
        OverlayToggleModel model = null!;
        // Simulate a binding that echoes every request straight back as authoritative state.
        model = new OverlayToggleModel(desired =>
        {
            requests.Add(desired);
            model.ApplyState(isAvailable: true, isOn: desired);
        });
        model.ApplyState(isAvailable: true, isOn: false);

        model.RequestToggle();
        model.RequestToggle();

        Assert.Equal(new[] { true, false }, requests);
        Assert.False(model.IsOn);
    }
}
