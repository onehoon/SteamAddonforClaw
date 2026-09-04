using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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

    internal OverlayToggleRow(string label, Action<bool> requestChange)
    {
        _model = new OverlayToggleModel(requestChange);

        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        if (Application.Current.Resources.TryGetValue("BodyTextBlockStyle", out var style) && style is Style bodyStyle)
            text.Style = bodyStyle;
        Grid.SetColumn(text, 0);

        _toggle = new ToggleSwitch
        {
            OnContent = null,
            OffContent = null,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
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

        Container = new Border
        {
            Child = grid,
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
        };

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
}
