using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace SteamInputAddonforClaw.Overlay;

// OQ5-UI-05: pure, feature-agnostic state for a boolean Overlay Quick Settings toggle. It knows
// only the last authoritative (available, on) pair and how to turn a user activation into a
// desired-state request. It never persists, never talks to Runtime, never touches WinUI. A future
// Runtime feature binding calls ApplyState with the authoritative snapshot/readback.
internal sealed class OverlayToggleModel
{
    private readonly Action<bool> _requestChange;

    internal OverlayToggleModel(Action<bool> requestChange) => _requestChange = requestChange;

    internal bool IsAvailable { get; private set; }
    internal bool IsOn { get; private set; }

    // Authoritative state applied from outside. Never emits a request.
    internal void ApplyState(bool isAvailable, bool isOn)
    {
        IsAvailable = isAvailable;
        IsOn = isOn;
    }

    // A / Accept on the selected row: request the opposite of the current authoritative state.
    internal void RequestToggle()
    {
        if (IsAvailable) _requestChange(!IsOn);
    }

    // Pointer/touch moved the switch to `desired`: same request seam.
    internal void RequestSet(bool desired)
    {
        if (IsAvailable) _requestChange(desired);
    }
}

// OQ5-UI-05: the first reusable Quick Settings row primitive -- a standard WinUI 3 ToggleSwitch
// row that plugs into the OQ5-UI-04 OverlayRowCapabilities model. It is a frontend primitive, not
// a feature authority: authoritative state arrives via ApplyState, user intent leaves via the
// requestChange callback, and the two never form a feedback loop.
internal sealed class OverlayToggleRow
{
    private readonly OverlayToggleModel _model;
    private readonly ToggleSwitch _toggle;
    private bool _suppress;

    internal Border Container { get; }
    internal OverlayRowCapabilities Capabilities { get; }

    internal OverlayToggleRow(string label, Action<bool> requestChange, bool strongLabel = false)
    {
        _model = new OverlayToggleModel(requestChange);

        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        OverlayQamResources.ApplyTextStyle(text, strongLabel ? "QamBodyStrongTextStyle" : "QamBodyTextStyle");
        Grid.SetColumn(text, 0);

        _toggle = new ToggleSwitch
        {
            OnContent = null,
            OffContent = null,
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        if (OverlayQamResources.Style("QamToggleStyle") is { } toggleStyle)
            _toggle.Style = toggleStyle;
        AutomationProperties.SetName(_toggle, label);
        ApplyQamToggleResources(_toggle);
        _toggle.Toggled += OnToggleSwitchToggled;
        Grid.SetColumn(_toggle, 1);

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        grid.Children.Add(text);
        grid.Children.Add(_toggle);

        Container = OverlayRowChrome.Create(grid);

        Capabilities = new OverlayRowCapabilities(
            IsSelectable: () => _model.IsAvailable,
            Activate: _model.RequestToggle,
            Adjust: null);
    }

    // Apply authoritative state, suppressing the Toggled event the IsOn assignment raises so a
    // Runtime readback never bounces back out as another change request.
    internal void ApplyState(bool isAvailable, bool isOn)
    {
        _model.ApplyState(isAvailable, isOn);
        _suppress = true;
        try
        {
            _toggle.IsEnabled = isAvailable;
            _toggle.IsOn = isOn;
        }
        finally
        {
            _suppress = false;
        }
    }

    private void OnToggleSwitchToggled(object sender, RoutedEventArgs args)
    {
        if (!_suppress)
            _model.RequestSet(_toggle.IsOn);
    }

    private static void ApplyQamToggleResources(ToggleSwitch toggle)
    {
        var off = OverlayQamResources.Brush("QamToggleOffBrush");
        var on = OverlayQamResources.Brush("QamToggleOnBrush");
        var disabled = OverlayQamResources.Brush("QamToggleDisabledBrush");
        var thumb = OverlayQamResources.Brush("QamToggleThumbBrush");
        var disabledThumb = OverlayQamResources.Brush("QamToggleThumbDisabledBrush");
        var transparent = OverlayQamResources.Brush("QamSectionBrush");

        // The keyed style retains WinUI's native ToggleSwitch template and drag/input behavior.
        // ThemeResource overrides are scoped to this instance and cannot recolor other controls.
        SetBrush(toggle, "ToggleSwitchContainerBackground", transparent);
        SetBrush(toggle, "ToggleSwitchContainerBackgroundPointerOver", transparent);
        SetBrush(toggle, "ToggleSwitchContainerBackgroundPressed", transparent);
        SetBrush(toggle, "ToggleSwitchContainerBackgroundDisabled", transparent);
        SetBrush(toggle, "ToggleSwitchFillOff", off);
        SetBrush(toggle, "ToggleSwitchFillOffPointerOver", off);
        SetBrush(toggle, "ToggleSwitchFillOffPressed", off);
        SetBrush(toggle, "ToggleSwitchFillOffDisabled", disabled);
        SetBrush(toggle, "ToggleSwitchStrokeOff", transparent);
        SetBrush(toggle, "ToggleSwitchStrokeOffPointerOver", transparent);
        SetBrush(toggle, "ToggleSwitchStrokeOffPressed", transparent);
        SetBrush(toggle, "ToggleSwitchStrokeOffDisabled", transparent);
        SetBrush(toggle, "ToggleSwitchFillOn", on);
        SetBrush(toggle, "ToggleSwitchFillOnPointerOver", on);
        SetBrush(toggle, "ToggleSwitchFillOnPressed", on);
        SetBrush(toggle, "ToggleSwitchFillOnDisabled", disabled);
        SetBrush(toggle, "ToggleSwitchStrokeOn", on);
        SetBrush(toggle, "ToggleSwitchStrokeOnPointerOver", on);
        SetBrush(toggle, "ToggleSwitchStrokeOnPressed", on);
        SetBrush(toggle, "ToggleSwitchStrokeOnDisabled", disabled);
        SetBrush(toggle, "ToggleSwitchKnobFillOff", thumb);
        SetBrush(toggle, "ToggleSwitchKnobFillOffPointerOver", thumb);
        SetBrush(toggle, "ToggleSwitchKnobFillOffPressed", thumb);
        SetBrush(toggle, "ToggleSwitchKnobFillOffDisabled", disabledThumb);
        SetBrush(toggle, "ToggleSwitchKnobFillOn", thumb);
        SetBrush(toggle, "ToggleSwitchKnobFillOnPointerOver", thumb);
        SetBrush(toggle, "ToggleSwitchKnobFillOnPressed", thumb);
        SetBrush(toggle, "ToggleSwitchKnobFillOnDisabled", disabledThumb);
        SetBrush(toggle, "ToggleSwitchKnobStrokeOn", thumb);
        SetBrush(toggle, "ToggleSwitchContentForeground", OverlayQamResources.Brush("QamPrimaryTextBrush"));
        SetBrush(toggle, "ToggleSwitchContentForegroundDisabled", OverlayQamResources.Brush("QamDisabledTextBrush"));
        SetBrush(toggle, "ToggleSwitchHeaderForeground", OverlayQamResources.Brush("QamPrimaryTextBrush"));
        SetBrush(toggle, "ToggleSwitchHeaderForegroundDisabled", OverlayQamResources.Brush("QamDisabledTextBrush"));
    }

    private static void SetBrush(ToggleSwitch toggle, string key, Microsoft.UI.Xaml.Media.Brush brush)
    {
        // The native template animates some brush Color properties. Clone each resource so an
        // animation cannot mutate the application-level QAM palette or another toggle's state.
        toggle.Resources[key] = brush is Microsoft.UI.Xaml.Media.SolidColorBrush solid
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(solid.Color) { Opacity = solid.Opacity }
            : brush;
    }
}
