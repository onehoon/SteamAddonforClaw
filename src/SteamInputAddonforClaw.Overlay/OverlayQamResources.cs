using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SteamInputAddonforClaw.Overlay;

internal static class OverlayQamResources
{
    internal static T Get<T>(string key, T fallback)
    {
        return Application.Current.Resources.TryGetValue(key, out var value) && value is T typed
            ? typed
            : fallback;
    }

    internal static Brush Brush(string key) =>
        Get(key, new SolidColorBrush(Colors.Transparent));

    internal static Style? Style(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as Style : null;

    internal static void ApplyTextStyle(TextBlock text, string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Style style)
            text.Style = style;
    }
}
