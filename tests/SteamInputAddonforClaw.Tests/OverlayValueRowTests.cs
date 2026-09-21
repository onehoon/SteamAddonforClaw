using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

// Covers the pure OverlayValueModel. The WinUI OverlayValueRow wrapper (arrow Buttons +
// event wiring) needs a XAML host and is validated on hardware per the work order.
public sealed class OverlayValueRowTests
{
    private static OverlayValueModel Model(out List<double> requests)
    {
        var captured = new List<double>();
        requests = captured;
        return new OverlayValueModel(captured.Add);
    }

    private static OverlayValueModel Available(out List<double> requests, double value = 50)
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
        Assert.True(model.CanDecrease);
        Assert.True(model.CanIncrease);
        Assert.Empty(requests);
    }

    [Fact]
    public void AuthoritativeValueOutsideRangeIsClampedForPreview()
    {
        var model = Model(out _);

        model.ApplyState(isAvailable: true, minimum: 0, maximum: 100, step: 5, value: 250);

        Assert.Equal(100, model.PreviewValue);
        Assert.True(model.CanDecrease);
        Assert.False(model.CanIncrease);
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
        Assert.False(model.CanDecrease);
        Assert.False(model.CanIncrease);

        model.RequestAdjust(1);

        Assert.Empty(requests);
    }

    [Fact]
    public void UnavailableRowRejectsAdjustments()
    {
        var model = Model(out var requests);
        model.ApplyState(isAvailable: false, minimum: 0, maximum: 100, step: 5, value: 50);

        model.RequestAdjust(1);
        model.RequestAdjust(-1);

        Assert.Empty(requests);
        Assert.False(model.CanDecrease);
        Assert.False(model.CanIncrease);
    }

    [Fact]
    public void ControllerAndArrowStepRaisesAndLowersExactlyOneStepFromPreview()
    {
        var model = Available(out var requests);

        model.RequestAdjust(+1);
        model.RequestAdjust(+1);
        model.RequestAdjust(-1);

        Assert.Equal(new[] { 55.0, 60.0, 55.0 }, requests);
        Assert.Equal(55, model.PreviewValue);
    }

    [Fact]
    public void AdjustmentsClampAtBothBoundariesWithoutDuplicateCallbacks()
    {
        var model = Available(out var requests, value: 95);

        model.RequestAdjust(+1);  // 100
        model.RequestAdjust(+1);  // clamp, no callback
        Assert.Equal(new[] { 100.0 }, requests);
        Assert.False(model.CanIncrease);
        Assert.True(model.CanDecrease);

        var low = Available(out var lowRequests, value: 5);
        low.RequestAdjust(-1);    // 0
        low.RequestAdjust(-1);    // clamp, no callback
        Assert.Equal(new[] { 0.0 }, lowRequests);
        Assert.False(low.CanDecrease);
        Assert.True(low.CanIncrease);
    }

    [Fact]
    public void NextArrowDisablesAtTheLastReachableStepWhenMaximumIsNotStepAligned()
    {
        var model = Model(out var requests);
        model.ApplyState(isAvailable: true, minimum: 0, maximum: 1, step: 0.3, value: 1);

        Assert.Equal(0.9, model.PreviewValue);
        Assert.False(model.CanIncrease);

        model.RequestAdjust(+1);

        Assert.Empty(requests);
    }

    [Fact]
    public void UnchangedNormalizedValueEmitsNoDuplicateCallback()
    {
        var model = Available(out var requests); // preview 50

        model.RequestAdjust(0);

        Assert.Empty(requests);
    }

    [Fact]
    public void RepeatedAdjustmentsContinueFromLocalPreviewWithoutReadback()
    {
        var model = Available(out var requests);

        model.RequestAdjust(+1);
        model.RequestAdjust(+1);
        model.RequestAdjust(+1);

        Assert.Equal(new[] { 55.0, 60.0, 65.0 }, requests);
        Assert.Equal(65, model.PreviewValue);
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
