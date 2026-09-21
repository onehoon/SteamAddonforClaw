using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Overlay;

// OQ5-UI-10: one fixed row of the Setting-page tab-order editor. It reorders exactly one of the five
// known Overlay tabs; it is NOT a generic reorderable-item control. Left/Right (via the OQ5-UI-04
// row model) and the compact Move Earlier / Move Later buttons all raise the same one-position move
// request. The row never commits an order -- OverlayWindow only applies the authoritative order the
// Runtime republishes.
internal sealed class AddonQuickSettingsTabOrderRow
{
    private readonly Action<int> _requestMove;
    private readonly TextBlock _label;
    private readonly Button _moveEarlier;
    private readonly Button _moveLater;
    private bool _canMoveEarlier;
    private bool _canMoveLater;

    internal AddonQuickSettingsTabId Tab { get; }
    internal Border Container { get; }
    internal OverlayRowCapabilities Capabilities { get; }

    internal AddonQuickSettingsTabOrderRow(AddonQuickSettingsTabId tab, string label, Action<int> requestMove)
    {
        Tab = tab;
        _requestMove = requestMove;

        _label = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        if (Application.Current.Resources.TryGetValue("BodyTextBlockStyle", out var style) && style is Style bodyStyle)
            _label.Style = bodyStyle;
        Grid.SetColumn(_label, 0);

        _moveEarlier = CreateMoveButton("◂", "Move earlier", -1);
        _moveLater = CreateMoveButton("▸", "Move later", +1);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        buttons.Children.Add(_moveEarlier);
        buttons.Children.Add(_moveLater);
        Grid.SetColumn(buttons, 1);

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        grid.Children.Add(_label);
        grid.Children.Add(buttons);

        Container = OverlayRowChrome.Create(grid);

        // Always selectable; Left/Right adjusts, A does nothing (no reorder mode).
        Capabilities = new OverlayRowCapabilities(
            IsSelectable: () => true,
            Activate: null,
            Adjust: delta =>
            {
                if (delta < 0 && _canMoveEarlier) _requestMove(-1);
                else if (delta > 0 && _canMoveLater) _requestMove(1);
            });
    }

    // Reflect this row's position within the current authoritative order so the boundary buttons
    // disable. Runtime remains the final boundary guard, but the disabled state is still the primary UX guard.
    internal void SetPosition(int index, int count)
    {
        _canMoveEarlier = index > 0;
        _canMoveLater = index < count - 1;
        _moveEarlier.IsEnabled = _canMoveEarlier;
        _moveLater.IsEnabled = _canMoveLater;
    }

    internal void ApplyState(SteamInputAddonforClaw.Contracts.Frontend.AddonQuickSettingsTabOrderRow state)
    {
        _label.Text = state.Label;
        _canMoveEarlier = state.CanMoveEarlier;
        _canMoveLater = state.CanMoveLater;
        _moveEarlier.IsEnabled = state.CanMoveEarlier;
        _moveLater.IsEnabled = state.CanMoveLater;
    }

    private Button CreateMoveButton(string glyph, string accessibleName, int delta)
    {
        var button = new Button
        {
            Content = glyph,
            Padding = new Thickness(8, 2, 8, 2),
            MinWidth = 0,
        };
        AutomationProperties.SetName(button, accessibleName);
        ToolTipService.SetToolTip(button, accessibleName);
        button.Click += (_, _) => _requestMove(delta);
        return button;
    }
}
