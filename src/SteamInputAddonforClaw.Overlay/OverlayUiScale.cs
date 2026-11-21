using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SteamInputAddonforClaw.Overlay;

internal static class OverlayUiScale
{
    internal const double DesignScale = 1.35;
    internal const double BaseContentWidthDip = 452.0;
    internal const double TabRailWidthDip = 52.0;
    internal const int LegacyRightAnchorExtensionPx = 48;
    private const uint DefaultDpi = 96;

    internal static int DesignContentWidthPixels =>
        (int)Math.Round(BaseContentWidthDip * DesignScale, MidpointRounding.AwayFromZero);

    internal static int PhysicalWindowWidthPixels => DesignContentWidthPixels + LegacyRightAnchorExtensionPx;

    internal static double SizeFactorForDpi(uint dpi) =>
        SizeFactorForRasterizationScale((dpi == 0 ? DefaultDpi : dpi) / (double)DefaultDpi);

    internal static double SizeFactorForRasterizationScale(double rasterizationScale) =>
        double.IsFinite(rasterizationScale) && rasterizationScale > 0
            ? DesignScale / rasterizationScale
            : DesignScale;

    internal static double Scale(double baseDip, double factor) => baseDip * factor;

    // This traversal follows only content authored by the Overlay (Panel children, Border.Child,
    // ContentControl.Content and Viewbox.Child). It deliberately does not walk native control
    // templates, so WinUI's Slider and ToggleSwitch input implementations remain intact.
    internal static void ApplyExistingContentScale(FrameworkElement root, double ratio)
    {
        if (!double.IsFinite(ratio) || ratio <= 0 || Math.Abs(ratio - 1.0) < 0.000001)
            return;

        var pending = new Stack<FrameworkElement>();
        pending.Push(root);
        while (pending.TryPop(out var element))
        {
            ApplyLocalDimensions(element, ratio);
            ApplyNativeControlResources(element, ratio);
            AddAuthoredChildren(element, pending);
        }
    }

    private static void ApplyLocalDimensions(FrameworkElement element, double ratio)
    {
        // The parent Viewbox scales the complete native ToggleSwitch. Scaling its MinHeight here
        // would make the Viewbox shrink the native template instead of enlarging it.
        if (element is ToggleSwitch)
            return;

        if (element.Name != "ProfileArtworkHost" && element.Name != "OpaquePanel")
            ScaleEffectiveDouble(element, FrameworkElement.WidthProperty, ratio, requirePositive: true);

        if (element.Name != "ProfileArtworkHost")
            ScaleEffectiveDouble(element, FrameworkElement.HeightProperty, ratio, requirePositive: true);

        ScaleEffectiveDouble(element, FrameworkElement.MinWidthProperty, ratio, requirePositive: true);
        ScaleEffectiveDouble(element, FrameworkElement.MinHeightProperty, ratio, requirePositive: true);
        ScaleEffectiveDouble(element, FrameworkElement.MaxWidthProperty, ratio, requirePositive: true);
        ScaleEffectiveDouble(element, FrameworkElement.MaxHeightProperty, ratio, requirePositive: true);
        ScaleLocalThickness(element, FrameworkElement.MarginProperty, ratio);

        if (element is Control control)
            ScaleLocalThickness(control, Control.PaddingProperty, ratio);

        if (element is TextBlock textBlock)
        {
            ScaleEffectiveDouble(textBlock, TextBlock.FontSizeProperty, ratio, requirePositive: true);
            ScaleEffectiveDouble(textBlock, TextBlock.LineHeightProperty, ratio, requirePositive: true);
        }

        if (element is FontIcon fontIcon)
            ScaleEffectiveDouble(fontIcon, FontIcon.FontSizeProperty, ratio, requirePositive: true);

        if (element is Border border)
        {
            ScaleLocalThickness(border, Border.PaddingProperty, ratio);
            ScaleLocalThickness(border, Border.BorderThicknessProperty, ratio);
            ScaleLocalCornerRadius(border, ratio);
        }

        if (element is StackPanel stackPanel)
            ScaleLocalDouble(stackPanel, StackPanel.SpacingProperty, ratio);

        if (element is Grid grid)
        {
            ScaleLocalDouble(grid, Grid.RowSpacingProperty, ratio);
            ScaleLocalDouble(grid, Grid.ColumnSpacingProperty, ratio);
            foreach (var column in grid.ColumnDefinitions)
                ScaleGridLength(column, ratio);
            foreach (var row in grid.RowDefinitions)
                ScaleGridLength(row, ratio);
        }

        if (element.RenderTransform is TranslateTransform translate)
        {
            ScaleLocalDouble(translate, TranslateTransform.XProperty, ratio);
            ScaleLocalDouble(translate, TranslateTransform.YProperty, ratio);
        }
    }

    private static void ApplyNativeControlResources(FrameworkElement element, double ratio)
    {
        if (element is Slider slider)
        {
            ScaleResource(slider.Resources, "SliderTrackThemeHeight", ratio);
            ScaleResource(slider.Resources, "SliderThumbCornerRadius", ratio);
            ScaleResource(slider.Resources, "SliderHorizontalHeight", ratio);
            ScaleResource(slider.Resources, "SliderHorizontalThumbWidth", ratio);
            ScaleResource(slider.Resources, "SliderHorizontalThumbHeight", ratio);
            ScaleResource(slider.Resources, "SliderInnerThumbWidth", ratio);
            ScaleResource(slider.Resources, "SliderInnerThumbHeight", ratio);
        }
    }

    private static void AddAuthoredChildren(FrameworkElement element, Stack<FrameworkElement> pending)
    {
        if (element is Panel panel)
        {
            foreach (var child in panel.Children)
                if (child is FrameworkElement childElement)
                    pending.Push(childElement);
        }

        if (element is Border { Child: FrameworkElement borderChild })
            pending.Push(borderChild);
        else if (element is ScrollViewer { Content: FrameworkElement scrollContent })
            pending.Push(scrollContent);
        else if (element is ContentControl { Content: FrameworkElement content })
            pending.Push(content);
        else if (element is Viewbox { Child: FrameworkElement viewboxChild })
            pending.Push(viewboxChild);
    }

    private static void ScaleLocalDouble(DependencyObject target, DependencyProperty property, double ratio)
    {
        if (target.ReadLocalValue(property) is double value && double.IsFinite(value))
            target.SetValue(property, value * ratio);
    }

    private static void ScaleEffectiveDouble(
        DependencyObject target,
        DependencyProperty property,
        double ratio,
        bool requirePositive = false)
    {
        if (target.GetValue(property) is double value
            && double.IsFinite(value)
            && (!requirePositive || value > 0))
            target.SetValue(property, value * ratio);
    }

    private static void ScaleLocalThickness(DependencyObject target, DependencyProperty property, double ratio)
    {
        if (target.ReadLocalValue(property) is Thickness value)
            target.SetValue(property, new Thickness(
                value.Left * ratio,
                value.Top * ratio,
                value.Right * ratio,
                value.Bottom * ratio));
    }

    private static void ScaleLocalCornerRadius(Border border, double ratio)
    {
        if (border.ReadLocalValue(Border.CornerRadiusProperty) is CornerRadius value)
            border.CornerRadius = new CornerRadius(
                value.TopLeft * ratio,
                value.TopRight * ratio,
                value.BottomRight * ratio,
                value.BottomLeft * ratio);
    }

    private static void ScaleGridLength(ColumnDefinition column, double ratio)
    {
        if (column.ReadLocalValue(ColumnDefinition.WidthProperty) is GridLength value && value.IsAbsolute)
            column.Width = new GridLength(value.Value * ratio);
    }

    private static void ScaleGridLength(RowDefinition row, double ratio)
    {
        if (row.ReadLocalValue(RowDefinition.HeightProperty) is GridLength value && value.IsAbsolute)
            row.Height = new GridLength(value.Value * ratio);
    }

    private static void ScaleResource(ResourceDictionary resources, string key, double ratio)
    {
        if (!resources.TryGetValue(key, out var value)) return;
        resources[key] = value switch
        {
            double number when double.IsFinite(number) => number * ratio,
            CornerRadius radius => new CornerRadius(
                radius.TopLeft * ratio,
                radius.TopRight * ratio,
                radius.BottomRight * ratio,
                radius.BottomLeft * ratio),
            _ => value,
        };
    }
}
