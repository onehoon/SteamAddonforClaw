using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace SteamInputAddonforClaw.Overlay;

// Pure, feature-agnostic ordered-value state for an Overlay Quick Settings row. It owns only
// availability, normalization, local preview, and one-step desired-value requests. It never
// persists, talks to Runtime, or owns delayed-commit policy.
internal sealed class OverlayValueModel
{
    private readonly Action<double> _requestChange;

    internal OverlayValueModel(Action<double> requestChange) => _requestChange = requestChange;

    internal bool ConstraintsValid { get; private set; }
    internal bool IsAvailable { get; private set; }
    internal double Minimum { get; private set; }
    internal double Maximum { get; private set; }
    internal double Step { get; private set; } = 1;
    internal double PreviewValue { get; private set; }
    internal bool CanDecrease => CanAdjust(-1);
    internal bool CanIncrease => CanAdjust(+1);

    // Authoritative state applied from outside. A malformed numeric contract fails closed and
    // never emits a desired-value callback.
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

    // Controller Left/Right and the ValueRow arrow buttons share this one-step semantic seam.
    internal void RequestAdjust(int delta)
    {
        if (!IsAvailable || delta == 0)
            return;

        Edit(PreviewValue + delta * Step);
    }

    private bool CanAdjust(int delta) =>
        ConstraintsValid && IsAvailable && Normalize(PreviewValue + delta * Step) != PreviewValue;

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

// A compact controller-first ordered-value row. Left/Right adjusts one semantic step without an
// edit mode; A/Accept is intentionally a no-op because Activate is null. Authoritative state
// arrives via ApplyState and user intent leaves via the requestChange callback.
internal enum OverlayValueButtonKind
{
    NumericStepper,
    DiscreteChoice,
}

internal sealed class OverlayValueRow
{
    private readonly OverlayValueModel _model;
    private readonly Func<double, string> _formatValue;
    private readonly ButtonBase _previousButton;
    private readonly TextBlock _valueText;
    private readonly ButtonBase _nextButton;

    internal Border Container { get; }
    internal OverlayRowCapabilities Capabilities { get; }

    internal OverlayValueRow(
        string label,
        Func<double, string> formatValue,
        Action<double> requestChange,
        OverlayValueButtonKind buttonKind = OverlayValueButtonKind.DiscreteChoice)
    {
        _model = new OverlayValueModel(requestChange);
        _formatValue = formatValue;

        var labelText = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        var valueText = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 88,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        };
        _valueText = valueText;

        if (Application.Current.Resources.TryGetValue("BodyTextBlockStyle", out var style) && style is Style bodyStyle)
        {
            labelText.Style = bodyStyle;
            _valueText.Style = bodyStyle;
        }

        _previousButton = CreateIconButton(buttonKind, increase: false, "Previous value", OnPreviousClicked);
        _nextButton = CreateIconButton(buttonKind, increase: true, "Next value", OnNextClicked);

        var valueControls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        valueControls.Children.Add(_previousButton);
        valueControls.Children.Add(_valueText);
        valueControls.Children.Add(_nextButton);
        Grid.SetColumn(valueControls, 1);

        Grid.SetColumn(labelText, 0);
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        grid.Children.Add(labelText);
        grid.Children.Add(valueControls);

        Container = OverlayRowChrome.Create(grid);

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

    private static ButtonBase CreateIconButton(
        OverlayValueButtonKind buttonKind,
        bool increase,
        string automationName,
        RoutedEventHandler click)
    {
        var glyph = buttonKind == OverlayValueButtonKind.NumericStepper
            ? increase ? "\uE710" : "\uE738"
            : increase ? "\uE76C" : "\uE76B";
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 16,
        };
        ButtonBase button = buttonKind == OverlayValueButtonKind.NumericStepper
            ? new RepeatButton
            {
                Delay = 300,
                Interval = 75,
            }
            : new Button();

        button.Content = icon;
        button.MinWidth = 40;
        button.MinHeight = 40;
        button.Padding = new Thickness(0);
        button.CornerRadius = new CornerRadius(6);
        button.BorderThickness = new Thickness(0);
        button.VerticalAlignment = VerticalAlignment.Center;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(button, automationName);
        button.Click += click;
        return button;
    }

    private void OnControllerAdjust(int delta)
    {
        _model.RequestAdjust(delta);
        Render();
    }

    private void OnPreviousClicked(object sender, RoutedEventArgs args)
    {
        _model.RequestAdjust(-1);
        Render();
    }

    private void OnNextClicked(object sender, RoutedEventArgs args)
    {
        _model.RequestAdjust(+1);
        Render();
    }

    // Push model state into the native arrow/value controls. The buttons are disabled at the
    // semantic boundaries and for unavailable or malformed rows.
    private void Render()
    {
        _valueText.Text = _model.ConstraintsValid ? _formatValue(_model.PreviewValue) : "--";
        _previousButton.IsEnabled = _model.CanDecrease;
        _nextButton.IsEnabled = _model.CanIncrease;
    }

    internal static string FormatInteger(double value) => value.ToString("0", CultureInfo.InvariantCulture);
}
