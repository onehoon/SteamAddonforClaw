using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

// Covers the pure OverlaySliderModel. The WinUI OverlaySliderRow wrapper (Slider +
// event-suppression) needs a XAML host and is validated on hardware per the work order.
public sealed class OverlaySliderRowTests
{
    private static OverlaySliderModel Model(out List<double> requests)
    {
        var captured = new List<double>();
        requests = captured;
        return new OverlaySliderModel(captured.Add);
    }

    private static OverlaySliderModel Available(out List<double> requests, double value = 50)
    {
        var model = Model(out requests);
        model.ApplyState(isAvailable: true, minimum: 0, maximum: 100, step: 5, value: value);
        return model;
    }

    [Fact]
    public void ValidAvailableStateBecomesSelectableAndSnapsTheInitialValue()
    {
        var model = Model(out var requests);

        model.ApplyState(isAvailable: true, minimum: 0, maximum: 100, step: 5, value: 52);

        Assert.True(model.IsAvailable);
        Assert.True(model.ConstraintsValid);
        Assert.Equal(50, model.PreviewValue);
        Assert.Empty(requests);
    }

    [Fact]
    public void AuthoritativeValueOutsideRangeIsClampedForPreview()
    {
        var model = Model(out _);

        model.ApplyState(isAvailable: true, minimum: 0, maximum: 100, step: 5, value: 250);

        Assert.Equal(100, model.PreviewValue);
    }

    [Theory]
    [InlineData(10, 0, 5)]     // minimum > maximum
    [InlineData(0, 100, 0)]    // step <= 0
    [InlineData(double.NaN, 100, 5)]
    [InlineData(0, double.PositiveInfinity, 5)]
    public void MalformedConstraintsFailClosed(double minimum, double maximum, double step)
    {
        var model = Model(out var requests);

        model.ApplyState(isAvailable: true, minimum, maximum, step, value: 50);

        Assert.False(model.IsAvailable);
        Assert.False(model.ConstraintsValid);

        model.RequestAdjust(1);
        model.RequestSet(20);
        Assert.Empty(requests);
    }

    [Fact]
    public void UnavailableRowRejectsControllerAndPointerEdits()
    {
        var model = Model(out var requests);
        model.ApplyState(isAvailable: false, minimum: 0, maximum: 100, step: 5, value: 50);

        model.RequestAdjust(1);
        model.RequestSet(75);

        Assert.Empty(requests);
    }

    [Fact]
    public void ControllerStepRaisesAndLowersExactlyOneStepAndContinuesFromPreview()
    {
        var model = Available(out var requests);

        model.RequestAdjust(+1);
        model.RequestAdjust(+1);
        model.RequestAdjust(-1);

        Assert.Equal(new[] { 55.0, 60.0, 55.0 }, requests);
        Assert.Equal(55, model.PreviewValue);
    }

    [Fact]
    public void ControllerStepClampsAtBothBoundariesWithoutDuplicateCallbacks()
    {
        var model = Available(out var requests, value: 95);

        model.RequestAdjust(+1);  // 100
        model.RequestAdjust(+1);  // clamp, no callback
        Assert.Equal(new[] { 100.0 }, requests);

        var low = Available(out var lowRequests, value: 5);
        low.RequestAdjust(-1);    // 0
        low.RequestAdjust(-1);    // clamp, no callback
        Assert.Equal(new[] { 0.0 }, lowRequests);
    }

    [Fact]
    public void PointerValueIsClampedAndSnappedToStep()
    {
        var model = Available(out var requests);

        model.RequestSet(97);   // clamp within range, snap to 95
        model.RequestSet(-40);  // clamp to 0

        Assert.Equal(new[] { 95.0, 0.0 }, requests);
        Assert.Equal(0, model.PreviewValue);
    }

    [Fact]
    public void UnchangedNormalizedValueEmitsNoDuplicateCallback()
    {
        var model = Available(out var requests); // preview 50

        model.RequestSet(51);   // snaps back to 50 -> no change
        model.RequestSet(50);

        Assert.Empty(requests);
    }

    [Fact]
    public void PointerAndControllerProduceTheSameNormalizedValue()
    {
        var pointer = Available(out var pointerRequests);
        var controller = Available(out _);

        pointer.RequestSet(58);       // -> snap to 60
        controller.RequestAdjust(+1); // 50 -> 55
        controller.RequestAdjust(+1); // 55 -> 60

        Assert.Equal(60.0, pointerRequests[^1]);
        Assert.Equal(60, pointer.PreviewValue);
        Assert.Equal(controller.PreviewValue, pointer.PreviewValue);
    }

    [Fact]
    public void AuthoritativeApplyStateReplacesTheLocalPreviewWithoutEmitting()
    {
        var model = Available(out var requests);
        model.RequestAdjust(+1); // preview 55, one request
        requests.Clear();

        model.ApplyState(isAvailable: true, minimum: 0, maximum: 100, step: 5, value: 20);

        Assert.Equal(20, model.PreviewValue);
        Assert.Empty(requests);
    }
}
