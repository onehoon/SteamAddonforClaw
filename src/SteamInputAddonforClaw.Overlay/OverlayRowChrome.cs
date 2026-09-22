using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SteamInputAddonforClaw.Overlay;

// Shared, concrete chrome for ordinary editable Overlay rows. This is intentionally a
// stateless construction helper rather than a row base class or visual framework.
internal static class OverlayRowChrome
{
    internal const double SelectionAccentWidth = 3;

    internal static Border Create(UIElement child) => new()
    {
        Child = child,
        Padding = new Thickness(14, 7, 14, 7),
        MinHeight = 54,
        CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(SelectionAccentWidth, 0, 0, 0),
        BorderBrush = new SolidColorBrush(Colors.Transparent),
        Background = new SolidColorBrush(Colors.Transparent),
    };
}
