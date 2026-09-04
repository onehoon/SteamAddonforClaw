using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SteamInputAddonforClaw.Contracts.Overlay;

namespace SteamInputAddonforClaw.Overlay;

// OQ5-UI-10: one fixed row of the Setting-page tab-order editor. It reorders exactly one of the five
// known Overlay tabs; it is NOT a generic reorderable-item control. Left/Right (via the OQ5-UI-04
// row model) and the compact Move Earlier / Move Later buttons all raise the same one-position move
// request. The row never commits an order -- OverlayWindow only applies the authoritative order the
// Runtime republishes.
internal sealed class OverlayTabOrderRow
{
    private readonly Action<int> _requestMove;
    private readonly Button _moveEarlier;
    private readonly Button _moveLater;

    internal OverlayTabId Tab { get; }
    internal Border Container { get; }
    internal OverlayRowCapabilities Capabilities { get; }

    internal OverlayTabOrderRow(OverlayTabId tab, string label, Action<int> requestMove)
    {
        Tab = tab;
        _requestMove = requestMove;

        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        if (Application.Current.Resources.TryGetValue("BodyTextBlockStyle", out var style) && style is Style bodyStyle)
            text.Style = bodyStyle;
        Grid.SetColumn(text, 0);

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
        grid.Children.Add(text);
        grid.Children.Add(buttons);

        Container = new Border
        {
            Child = grid,
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
        };

        // Always selectable; Left/Right adjusts, A does nothing (no reorder mode).
        Capabilities = new OverlayRowCapabilities(
            IsSelectable: () => true,
            Activate: null,
            Adjust: delta => _requestMove(delta < 0 ? -1 : 1));
    }

    // Reflect this row's position within the current authoritative order so the boundary buttons
    // disable. A boundary controller move is separately rejected by OverlayTabState.TryCreateMovedOrder,
    // so it never sends a request either.
    internal void SetPosition(int index, int count)
    {
        _moveEarlier.IsEnabled = index > 0;
        _moveLater.IsEnabled = index < count - 1;
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
