using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace SteamInputAddonforClaw.Overlay;

// OQ5-UI-06: pure, feature-agnostic value state for a numeric Overlay Quick Settings slider,
// mirroring the OQ5-UI-05 OverlayToggleModel pattern. It owns the visible preview value and one
// clamp/snap rule; it never persists, never talks to Runtime, never touches WinUI, and never
// implements the OQ5-UI-07 delayed-commit policy. A future Runtime feature binding calls
// ApplyState with the authoritative snapshot/readback.
internal sealed class OverlaySliderModel
{
    private readonly Action<double> _requestChange;

    internal OverlaySliderModel(Action<double> requestChange) => _requestChange = requestChange;

    internal bool ConstraintsValid { get; private set; }
    internal bool IsAvailable { get; private set; }
    internal double Minimum { get; private set; }
    internal double Maximum { get; private set; }
    internal double Step { get; private set; } = 1;
    internal double PreviewValue { get; private set; }

    // Authoritative state applied from outside. Fails closed (non-selectable) on a malformed
    // numeric contract. Never emits a desired-value callback.
    internal void ApplyState(bool isAvailable, double minimum, double maximum, double step, double value)
    {
        ConstraintsValid =
            double.IsFinite(minimum) && double.IsFinite(maximum) && double.IsFinite(step) &&
            double.IsFinite(value) && minimum <= maximum && step > 0;

        if (!ConstraintsValid)
        {
            IsAvailable = false;
            return;
        }

        Minimum = minimum;
        Maximum = maximum;
        Step = step;
        IsAvailable = isAvailable;
        PreviewValue = Normalize(value);
    }

    // Controller Left/Right: one configured semantic step from the current preview.
    internal void RequestAdjust(int delta)
    {
        if (IsAvailable)
            Edit(PreviewValue + delta * Step);
    }

    // Pointer/touch moved the slider to `desired`: same normalize + request seam.
    internal void RequestSet(double desired)
    {
        if (IsAvailable && double.IsFinite(desired))
            Edit(desired);
    }

    private void Edit(double candidate)
    {
        var normalized = Normalize(candidate);
        if (normalized == PreviewValue)
            return;
        PreviewValue = normalized;
        _requestChange(normalized);
    }

    // clamp -> snap to the semantic step relative to Minimum -> clamp again, with a small round
    // to keep the displayed/returned value free of obvious floating-point drift.
    private double Normalize(double candidate)
    {
        var clamped = Math.Clamp(candidate, Minimum, Maximum);
        var steps = Math.Round((clamped - Minimum) / Step, MidpointRounding.AwayFromZero);
        var snapped = Math.Clamp(Minimum + steps * Step, Minimum, Maximum);
        return Math.Round(snapped, 6);
    }
}

// OQ5-UI-06: the second reusable Quick Settings row primitive -- a standard WinUI 3 Slider row
// that plugs into the OQ5-UI-04 OverlayRowCapabilities model. Left/Right adjusts one step with no
// edit mode; A/Accept is a no-op (Activate is null). Authoritative state arrives via ApplyState,
// user intent leaves via requestChange, and the two never form a feedback loop.
internal sealed class OverlaySliderRow
{
    private readonly OverlaySliderModel _model;
    private readonly Func<double, string> _formatValue;
    private readonly Slider _slider;
    private readonly TextBlock _valueText;
    private bool _suppress;

    internal Border Container { get; }
    internal OverlayRowCapabilities Capabilities { get; }

    internal OverlaySliderRow(string label, Func<double, string> formatValue, Action<double> requestChange)
    {
        _model = new OverlaySliderModel(requestChange);
        _formatValue = formatValue;

        var labelText = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap };
        _valueText = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right };
        if (Application.Current.Resources.TryGetValue("BodyTextBlockStyle", out var style) && style is Style bodyStyle)
        {
            labelText.Style = bodyStyle;
            _valueText.Style = bodyStyle;
        }
        Grid.SetColumn(labelText, 0);
        Grid.SetColumn(_valueText, 1);

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        header.Children.Add(labelText);
        header.Children.Add(_valueText);

        _slider = new Slider { HorizontalAlignment = HorizontalAlignment.Stretch };
        _slider.ValueChanged += OnSliderValueChanged;

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(header);
        stack.Children.Add(_slider);

        Container = new Border
        {
            Child = stack,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
        };

        Capabilities = new OverlayRowCapabilities(
            IsSelectable: () => _model.IsAvailable,
            Activate: null,
            Adjust: OnControllerAdjust);
    }

    internal void ApplyState(bool isAvailable, double minimum, double maximum, double step, double value)
    {
        _model.ApplyState(isAvailable, minimum, maximum, step, value);
        Render();
    }

    private void OnControllerAdjust(int delta)
    {
        _model.RequestAdjust(delta);
        Render();
    }

    private void OnSliderValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_suppress)
            return;
        _model.RequestSet(_slider.Value);
        Render();
    }

    // Push the model's current facts into the WinUI Slider, suppressing the ValueChanged the
    // programmatic Value assignment raises so an authoritative readback cannot loop back out.
    private void Render()
    {
        _suppress = true;
        try
        {
            if (_model.ConstraintsValid)
            {
                _slider.Minimum = _model.Minimum;
                _slider.Maximum = _model.Maximum;
                _slider.StepFrequency = _model.Step;
                _slider.SmallChange = _model.Step;
                _slider.Value = _model.PreviewValue;
                _valueText.Text = _formatValue(_model.PreviewValue);
            }
            else
            {
                _valueText.Text = "--";
            }

            _slider.IsEnabled = _model.IsAvailable;
        }
        finally
        {
            _suppress = false;
        }
    }

    internal static string FormatInteger(double value) => value.ToString("0", CultureInfo.InvariantCulture);
}
