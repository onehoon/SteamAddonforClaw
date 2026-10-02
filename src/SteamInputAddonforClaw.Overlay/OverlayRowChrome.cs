using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SteamInputAddonforClaw.Overlay;

// Shared, concrete chrome for ordinary editable Overlay rows. This is intentionally a
// stateless construction helper rather than a row base class or visual framework.
internal static class OverlayRowChrome
{
    internal static Border Create(UIElement child) => new()
    {
        Child = child,
        Padding = OverlayQamResources.Get("QamRowPadding", new Thickness(10, 16, 10, 16)),
        Margin = OverlayQamResources.Get("QamRowMargin", new Thickness(-16, 0, -16, 0)),
        MinHeight = OverlayQamResources.Get("QamRowMinHeight", 42.0),
        CornerRadius = OverlayQamResources.Get("QamRowCornerRadius", new CornerRadius(2)),
        BorderThickness = OverlayQamResources.Get("QamSelectionBorderThickness", new Thickness(0)),
        BorderBrush = OverlayQamResources.Brush("QamFocusBorderBrush"),
        Background = OverlayQamResources.Brush("QamContentBrush"),
    };
}
