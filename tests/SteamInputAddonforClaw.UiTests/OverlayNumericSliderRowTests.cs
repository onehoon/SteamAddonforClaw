using System.IO;
using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayNumericSliderRowTests
{
    [Fact]
    public void Numeric_slider_model_applies_valid_authoritative_state_without_a_request()
    {
        var requests = new List<double>();
        var model = new OverlayValueModel(requests.Add);

        model.ApplyState(isAvailable: true, minimum: 0, maximum: 100, step: 5, value: 52);

        Assert.True(model.ConstraintsValid);
        Assert.True(model.IsAvailable);
        Assert.Equal(50, model.PreviewValue);
        Assert.Empty(requests);
    }

    [Fact]
    public void Numeric_slider_model_snaps_pointer_values_relative_to_minimum()
    {
        var requests = new List<double>();
        var model = Available(requests, minimum: 7, maximum: 19, step: 4, value: 7);

        model.RequestSet(13.6);

        Assert.Equal(15, model.PreviewValue);
        Assert.Equal(new[] { 15.0 }, requests);
    }

    [Fact]
    public void Numeric_slider_controller_adjustment_moves_one_step_and_a_has_no_activation()
    {
        var requests = new List<double>();
        var model = Available(requests, minimum: 10, maximum: 30, step: 5, value: 20);

        model.RequestAdjust(-1);
        model.RequestAdjust(+1);

        Assert.Equal(new[] { 15.0, 20.0 }, requests);
        Assert.Equal(20, model.PreviewValue);
    }

    [Fact]
    public void Numeric_slider_model_clamps_at_boundaries_without_duplicate_requests()
    {
        var requests = new List<double>();
        var model = Available(requests, minimum: 5, maximum: 16, step: 5, value: 10);

        model.RequestSet(100);
        model.RequestSet(100);
        model.RequestAdjust(+1);

        Assert.Equal(new[] { 15.0 }, requests);
        Assert.Equal(15, model.PreviewValue);
        Assert.False(model.CanIncrease);
    }

    [Fact]
    public void Numeric_slider_model_rejects_nonfinite_pointer_values_and_malformed_constraints()
    {
        var requests = new List<double>();
        var model = Available(requests, minimum: 0, maximum: 10, step: 1, value: 5);

        model.RequestSet(double.NaN);
        model.RequestSet(double.PositiveInfinity);
        model.ApplyState(isAvailable: true, minimum: 0, maximum: 10, step: 0, value: 5);
        model.RequestSet(7);
        model.RequestAdjust(+1);

        Assert.False(model.ConstraintsValid);
        Assert.False(model.IsAvailable);
        Assert.Empty(requests);
    }

    [Fact]
    public void Numeric_slider_model_rejects_pointer_and_controller_input_when_unavailable()
    {
        var requests = new List<double>();
        var model = new OverlayValueModel(requests.Add);
        model.ApplyState(isAvailable: false, minimum: 0, maximum: 10, step: 1, value: 5);

        model.RequestSet(6);
        model.RequestAdjust(-1);

        Assert.Empty(requests);
        Assert.False(model.CanDecrease);
        Assert.False(model.CanIncrease);
    }

    [Fact]
    public void Numeric_slider_authoritative_readback_replaces_preview_without_emitting()
    {
        var requests = new List<double>();
        var model = Available(requests, minimum: 0, maximum: 100, step: 5, value: 50);
        model.RequestSet(63); // Preview snaps to 65.
        requests.Clear();

        model.ApplyState(isAvailable: true, minimum: 0, maximum: 100, step: 5, value: 20);

        Assert.Equal(20, model.PreviewValue);
        Assert.Empty(requests);
    }

    [Fact]
    public void Numeric_slider_row_keeps_native_range_semantics_and_no_focus_or_edit_mode()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "SteamInputAddonforClaw.Overlay", "OverlayNumericSliderRow.cs"));

        Assert.Contains("new Slider", source);
        Assert.Contains("AutomationProperties.SetName(_slider, label)", source);
        Assert.Contains("_slider.ValueChanged += OnSliderValueChanged", source);
        Assert.Contains("_model.RequestSet(args.NewValue)", source);
        Assert.Contains("_model.RequestAdjust(delta)", source);
        Assert.Contains("Activate: null", source);
        Assert.Contains("IsTabStop = false", source);
        Assert.Contains("IsFocusEngagementEnabled = false", source);
        Assert.Contains("_suppress", source);
    }

    private static OverlayValueModel Available(List<double> requests, double minimum, double maximum, double step, double value)
    {
        var model = new OverlayValueModel(requests.Add);
        model.ApplyState(isAvailable: true, minimum, maximum, step, value);
        return model;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
