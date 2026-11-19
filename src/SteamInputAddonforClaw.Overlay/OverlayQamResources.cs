using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SteamInputAddonforClaw.Overlay;

internal static class OverlayQamResources
{
    private static readonly string[] ScaledResourceKeys =
    [
        "QamBodyFontSize", "QamBodyLineHeight",
        "QamCaptionFontSize", "QamCaptionLineHeight",
        "QamSectionHeaderFontSize", "QamSectionHeaderLineHeight",
        "QamPageTitleMargin", "QamBodyContentPadding", "QamSectionSpacing",
        "QamSectionPadding", "QamSectionCornerRadius", "QamDetailIndent",
        "QamDetailRowPadding", "QamRowSpacing", "QamRowPadding", "QamRowMargin",
        "QamSectionSeparatorThickness", "QamRowMinHeight", "QamRowCornerRadius",
        "QamSelectionBorderThickness", "QamRailButtonSize", "QamRailItemHeight",
        "QamRailIconSize", "QamRailSpacing", "QamRailHintIconSize", "QamRailHintGap",
        "QamRailCornerRadius", "QamTilePadding", "QamShortcutTileSize", "QamTileCornerRadius",
        "QamTileMinHeight", "QamTileSpacing", "QamSliderTrackHeight", "QamSliderThumbWidth",
        "QamSliderThumbHeight", "QamSliderThumbCornerRadius", "QamSliderHeight",
        "QamValueButtonWidth", "QamValueButtonHeight", "QamValueButtonCornerRadius",
        "QamDiscreteValueMinWidth"
    ];

    private static Dictionary<string, object>? _baseScaledValues;
    private static double _sizeFactor = 1.0;

    internal static double SizeFactor => _sizeFactor;

    internal static double Scale(double baseDip) => baseDip * _sizeFactor;

    internal static bool SetSizeFactor(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0 || Math.Abs(_sizeFactor - factor) < 0.000001)
            return false;

        var resources = Application.Current.Resources;
        _baseScaledValues ??= ScaledResourceKeys
            .Where(key => resources.TryGetValue(key, out _))
            .ToDictionary(key => key, key => resources[key]);

        foreach (var (key, baseline) in _baseScaledValues)
            resources[key] = ScaleResource(baseline, factor);

        _sizeFactor = factor;
        return true;
    }

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

    private static object ScaleResource(object value, double factor) => value switch
    {
        double number => number * factor,
        Thickness thickness => new Thickness(
            thickness.Left * factor,
            thickness.Top * factor,
            thickness.Right * factor,
            thickness.Bottom * factor),
        CornerRadius radius => new CornerRadius(
            radius.TopLeft * factor,
            radius.TopRight * factor,
            radius.BottomRight * factor,
            radius.BottomLeft * factor),
        _ => value,
    };
}
