using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace SteamInputAddonforClaw.Overlay;

// Numeric Device/Profile Quick Settings row. The native Slider owns pointer/touch range input;
// OverlayValueModel remains the sole availability, preview, clamp, and step-snap authority.
internal sealed class OverlayNumericSliderRow
{
    private readonly OverlayValueModel _model;
    private readonly Func<double, string> _formatValue;
    private readonly Slider _slider;
    private readonly TextBlock _valueText;
    private bool _suppress;

    internal Border Container { get; }
    internal OverlayRowCapabilities Capabilities { get; }

    internal OverlayNumericSliderRow(
        string label,
        Func<double, string> formatValue,
        Action<double> requestChange,
        bool strongLabel = false)
    {
        _model = new OverlayValueModel(requestChange);
        _formatValue = formatValue;

        var labelText = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        _valueText = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            TextWrapping = TextWrapping.NoWrap,
        };
        OverlayQamResources.ApplyTextStyle(labelText, strongLabel ? "QamBodyStrongTextStyle" : "QamBodyTextStyle");
        OverlayQamResources.ApplyTextStyle(_valueText, "QamValueTextStyle");

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        Grid.SetColumn(labelText, 0);
        Grid.SetColumn(_valueText, 1);
        header.Children.Add(labelText);
        header.Children.Add(_valueText);

        _slider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = 0,
            StepFrequency = 1,
            IsEnabled = false,
            IsTabStop = false,
            IsFocusEngagementEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Style = OverlayQamResources.Style("QamSliderStyle"),
        };
        AutomationProperties.SetName(_slider, label);
        ApplyQamSliderResources(_slider);
        _slider.ValueChanged += OnSliderValueChanged;

        var content = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(header);
        content.Children.Add(_slider);
        Container = OverlayRowChrome.Create(content);

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

        _model.RequestSet(args.NewValue);
        Render();
    }

    private void Render()
    {
        _valueText.Text = _model.ConstraintsValid ? _formatValue(_model.PreviewValue) : "--";

        _suppress = true;
        try
        {
            _slider.IsEnabled = _model.IsAvailable;
            if (!_model.ConstraintsValid)
            {
                _slider.Minimum = 0;
                _slider.Maximum = 1;
                _slider.StepFrequency = 1;
                _slider.Value = 0;
                return;
            }

            // Expand before narrowing the range so an old slider value can never violate an
            // intermediate Minimum/Maximum assignment during authoritative range changes.
            _slider.Minimum = Math.Min(_slider.Minimum, _model.Minimum);
            _slider.Maximum = Math.Max(_slider.Maximum, _model.Maximum);
            _slider.Minimum = _model.Minimum;
            _slider.Maximum = _model.Maximum;
            _slider.StepFrequency = _model.Step;
            _slider.Value = _model.PreviewValue;
        }
        finally
        {
            _suppress = false;
        }
    }

    private static void ApplyQamSliderResources(Slider slider)
    {
        var track = OverlayQamResources.Brush("QamSliderTrackBrush");
        var valueTrack = OverlayQamResources.Brush("QamSliderValueTrackBrush");
        var disabledTrack = OverlayQamResources.Brush("QamSliderDisabledTrackBrush");
        var thumb = OverlayQamResources.Brush("QamSliderThumbBrush");
        var disabledThumb = OverlayQamResources.Brush("QamSliderDisabledThumbBrush");
        var transparent = OverlayQamResources.Brush("QamSectionBrush");

        // WinUI's native template consumes ThemeResource keys. Override them only in this Slider's
        // resource scope, leaving unrelated controls and application resources untouched.
        SetBrush(slider, "SliderTrackFill", track);
        SetBrush(slider, "SliderTrackFillPointerOver", track);
        SetBrush(slider, "SliderTrackFillPressed", track);
        SetBrush(slider, "SliderTrackFillDisabled", disabledTrack);
        SetBrush(slider, "SliderTrackValueFill", valueTrack);
        SetBrush(slider, "SliderTrackValueFillPointerOver", valueTrack);
        SetBrush(slider, "SliderTrackValueFillPressed", valueTrack);
        SetBrush(slider, "SliderTrackValueFillDisabled", disabledTrack);
        SetBrush(slider, "SliderThumbBackground", thumb);
        SetBrush(slider, "SliderThumbBackgroundPointerOver", thumb);
        SetBrush(slider, "SliderThumbBackgroundPressed", thumb);
        SetBrush(slider, "SliderThumbBackgroundDisabled", disabledThumb);
        SetBrush(slider, "SliderThumbBorderBrush", transparent);
        SetBrush(slider, "SliderOuterThumbBackground", transparent);
        slider.Resources["SliderTrackThemeHeight"] = OverlayQamResources.Get("QamSliderTrackHeight", 4.0);
        slider.Resources["SliderThumbCornerRadius"] = OverlayQamResources.Get("QamSliderThumbCornerRadius", new CornerRadius(6));
        slider.Resources["SliderHorizontalHeight"] = OverlayQamResources.Get("QamSliderHeight", 22.0);
        slider.Resources["SliderHorizontalThumbWidth"] = OverlayQamResources.Get("QamSliderThumbWidth", 12.0);
        slider.Resources["SliderHorizontalThumbHeight"] = OverlayQamResources.Get("QamSliderThumbHeight", 12.0);
        slider.Resources["SliderInnerThumbWidth"] = OverlayQamResources.Get("QamSliderThumbWidth", 12.0);
        slider.Resources["SliderInnerThumbHeight"] = OverlayQamResources.Get("QamSliderThumbHeight", 12.0);
    }

    private static void SetBrush(Slider slider, string key, Brush brush) => slider.Resources[key] = brush;
}
