using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayUiScaleTests
{
    [Theory]
    [InlineData(96, 1.35)]
    [InlineData(120, 1.08)]
    [InlineData(144, 0.90)]
    [InlineData(168, 0.7714285714285715)]
    [InlineData(192, 0.675)]
    public void Design_size_factor_compensates_for_windows_rasterization_scale(uint dpi, double expectedFactor)
    {
        Assert.Equal(expectedFactor, OverlayUiScale.SizeFactorForDpi(dpi), precision: 10);
        Assert.Equal(expectedFactor, OverlayUiScale.SizeFactorForRasterizationScale(dpi / 96.0), precision: 10);

        var physicalFontPixels = OverlayUiScale.Scale(16, expectedFactor) * dpi / 96.0;
        var physicalSliderTrackPixels = OverlayUiScale.Scale(4, expectedFactor) * dpi / 96.0;
        var physicalShortcutTilePixels = OverlayUiScale.Scale(117.33333333333333, expectedFactor) * dpi / 96.0;
        var physicalShortcutGapPixels = OverlayUiScale.Scale(8, expectedFactor) * dpi / 96.0;
        var physicalRailPixels = OverlayUiScale.Scale(52, expectedFactor) * dpi / 96.0;
        var physicalToggleTrackWidthPixels = OverlayUiScale.Scale(40, expectedFactor) * dpi / 96.0;
        var physicalToggleControlWidthPixels = OverlayUiScale.Scale(52, expectedFactor) * dpi / 96.0;
        var physicalSliderHeightPixels = OverlayUiScale.Scale(22, expectedFactor) * dpi / 96.0;
        var physicalSliderThumbPixels = OverlayUiScale.Scale(12, expectedFactor) * dpi / 96.0;
        var physicalSliderThumbRadiusPixels = OverlayUiScale.Scale(6, expectedFactor) * dpi / 96.0;

        Assert.Equal(21.6, physicalFontPixels, precision: 10);
        Assert.Equal(5.4, physicalSliderTrackPixels, precision: 10);
        Assert.Equal(158.4, physicalShortcutTilePixels, precision: 10);
        Assert.Equal(10.8, physicalShortcutGapPixels, precision: 10);
        Assert.Equal(70.2, physicalRailPixels, precision: 10);
        Assert.Equal(54, physicalToggleTrackWidthPixels, precision: 10);
        Assert.Equal(70.2, physicalToggleControlWidthPixels, precision: 10);
        Assert.Equal(29.7, physicalSliderHeightPixels, precision: 10);
        Assert.Equal(16.2, physicalSliderThumbPixels, precision: 10);
        Assert.Equal(8.1, physicalSliderThumbRadiusPixels, precision: 10);
    }

    [Fact]
    public void Design_surface_width_includes_the_legacy_right_anchor_extension()
    {
        Assert.Equal(610, OverlayUiScale.DesignContentWidthPixels);
        Assert.Equal(658, OverlayUiScale.PhysicalWindowWidthPixels);
    }

    [Fact]
    public void Sizing_updates_apply_before_show_and_on_xaml_root_changes_without_rebuilding_pages()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SteamInputAddonforClaw.Overlay",
            "OverlayWindow.Presentation.cs"));

        Assert.Contains("ApplyOverlayUiScale(_lastConfiguredDpi / 96.0)", source);
        Assert.Contains("_overlayScaleObservedRoot.Changed += OverlayXamlRoot_Changed", source);
        Assert.Contains("ApplyExistingContentScale(AnimationViewport, factor / previousFactor)", source);
        Assert.Contains("previousScrollOffset * previousRasterizationScale / rasterizationScale", source);
        Assert.DoesNotContain("BuildShell();", source[source.IndexOf("private bool ApplyOverlayUiScale", StringComparison.Ordinal)..]);
    }

    [Fact]
    public void Toggle_scaling_wraps_only_the_native_toggle_and_preserves_its_existing_instance()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SteamInputAddonforClaw.Overlay",
            "OverlayToggleRow.cs"));
        var qamResourceSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SteamInputAddonforClaw.Overlay",
            "Themes",
            "QamOverlayResources.xaml"));

        Assert.Contains("var toggleView = new Viewbox", source);
        Assert.Contains("private const double NativeToggleWidthDip = 52", source);
        Assert.Contains("private const double NativeToggleHeightDip = 22", source);
        Assert.Contains("Width = OverlayQamResources.Scale(NativeToggleWidthDip)", source);
        Assert.Contains("Height = OverlayQamResources.Scale(NativeToggleHeightDip)", source);
        Assert.Contains("ToggleSwitchPreContentMargin", source);
        Assert.Contains("ToggleSwitchPostContentMargin", source);
        Assert.Contains("<x:Double x:Key=\"QamToggleMinHeight\">22</x:Double>", qamResourceSource);
        Assert.Contains("<x:Double x:Key=\"QamTogglePreContentMargin\">1</x:Double>", qamResourceSource);
        Assert.Contains("<x:Double x:Key=\"QamTogglePostContentMargin\">1</x:Double>", qamResourceSource);
        Assert.Contains("Child = _toggle", source);
        Assert.Contains("grid.Children.Add(toggleView)", source);
        Assert.Contains("_toggle.Toggled += OnToggleSwitchToggled", source);
    }

    [Fact]
    public void Effective_style_dimensions_fonts_and_font_icons_are_scaled_for_existing_controls()
    {
        var root = FindRepositoryRoot();
        var scaleSource = File.ReadAllText(Path.Combine(
            root, "src", "SteamInputAddonforClaw.Overlay", "OverlayUiScale.cs"));
        var resourceSource = File.ReadAllText(Path.Combine(
            root, "src", "SteamInputAddonforClaw.Overlay", "OverlayQamResources.cs"));
        var styleSource = File.ReadAllText(Path.Combine(
            root, "src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml"));

        Assert.Contains("target.GetValue(property)", scaleSource);
        Assert.Contains("FrameworkElement.WidthProperty, ratio, requirePositive: true", scaleSource);
        Assert.Contains("FrameworkElement.MinWidthProperty, ratio, requirePositive: true", scaleSource);
        Assert.Contains("TextBlock.FontSizeProperty, ratio, requirePositive: true", scaleSource);
        Assert.Contains("TextBlock.LineHeightProperty, ratio, requirePositive: true", scaleSource);
        Assert.Contains("FontIcon.FontSizeProperty, ratio, requirePositive: true", scaleSource);
        Assert.Contains("Setter Property=\"Height\" Value=\"{ThemeResource QamSliderHeight}\"", styleSource);
        Assert.Contains("Setter Property=\"Width\" Value=\"{ThemeResource QamRailButtonSize}\"", styleSource);

        Assert.Contains("text.FontSize = Scale(text.FontSize)", resourceSource);
        Assert.Contains("text.LineHeight = Scale(text.LineHeight)", resourceSource);
        Assert.DoesNotContain("\"QamBodyFontSize\"", resourceSource);
        Assert.DoesNotContain("\"QamRailButtonSize\"", resourceSource);
        Assert.DoesNotContain("\"QamSliderHeight\"", resourceSource);
    }

    [Fact]
    public void Late_created_slider_and_value_buttons_use_the_active_size_factor()
    {
        var root = FindRepositoryRoot();
        var sliderSource = File.ReadAllText(Path.Combine(
            root, "src", "SteamInputAddonforClaw.Overlay", "OverlayNumericSliderRow.cs"));
        var valueRowSource = File.ReadAllText(Path.Combine(
            root, "src", "SteamInputAddonforClaw.Overlay", "OverlayValueRow.cs"));
        var tabOrderSource = File.ReadAllText(Path.Combine(
            root, "src", "SteamInputAddonforClaw.Overlay", "OverlayTabOrderRow.cs"));

        Assert.Contains("slider.Height = OverlayQamResources.Scale(OverlayQamResources.Get(\"QamSliderHeight\", 22.0))", sliderSource);
        Assert.Contains("slider.Resources[\"SliderHorizontalHeight\"] = OverlayQamResources.Scale", sliderSource);
        Assert.Contains("button.MinWidth = OverlayQamResources.Scale(OverlayQamResources.Get(\"QamValueButtonWidth\", 40.0))", valueRowSource);
        Assert.Contains("button.MinHeight = OverlayQamResources.Scale(OverlayQamResources.Get(\"QamValueButtonHeight\", 22.0))", valueRowSource);
        Assert.Contains("MinWidth = OverlayQamResources.Scale(OverlayQamResources.Get(\"QamValueButtonWidth\", 40.0))", tabOrderSource);
    }

    [Fact]
    public void Late_created_font_icons_use_the_active_size_factor_or_scaled_rail_resource()
    {
        var root = FindRepositoryRoot();
        var valueRowSource = File.ReadAllText(Path.Combine(
            root, "src", "SteamInputAddonforClaw.Overlay", "OverlayValueRow.cs"));
        var tabOrderSource = File.ReadAllText(Path.Combine(
            root, "src", "SteamInputAddonforClaw.Overlay", "OverlayTabOrderRow.cs"));
        var shortcutsSource = File.ReadAllText(Path.Combine(
            root, "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shortcuts.cs"));
        var shellSource = File.ReadAllText(Path.Combine(
            root, "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs"));
        var qamSource = File.ReadAllText(Path.Combine(
            root, "src", "SteamInputAddonforClaw.Overlay", "OverlayQamResources.cs"));

        Assert.Contains("FontSize = OverlayQamResources.Scale(16)", valueRowSource);
        Assert.Contains("FontSize = OverlayQamResources.Scale(16)", tabOrderSource);
        Assert.Contains("FontSize = OverlayQamResources.Scale(24)", shortcutsSource);
        Assert.Contains("FontSize = OverlayQamResources.Get(\"QamRailIconSize\", 24.0)", shellSource);
        Assert.Contains("\"QamRailIconSize\"", qamSource);
    }

    [Fact]
    public void Existing_native_slider_templates_receive_each_size_resource_on_scale_changes()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SteamInputAddonforClaw.Overlay",
            "OverlayUiScale.cs"));

        Assert.Contains("ScaleResource(slider.Resources, \"SliderTrackThemeHeight\", ratio)", source);
        Assert.Contains("ScaleResource(slider.Resources, \"SliderHorizontalHeight\", ratio)", source);
        Assert.Contains("ScaleResource(slider.Resources, \"SliderHorizontalThumbWidth\", ratio)", source);
        Assert.Contains("ScaleResource(slider.Resources, \"SliderHorizontalThumbHeight\", ratio)", source);
        Assert.Contains("ScaleResource(slider.Resources, \"SliderInnerThumbWidth\", ratio)", source);
        Assert.Contains("ScaleResource(slider.Resources, \"SliderInnerThumbHeight\", ratio)", source);
        Assert.Contains("ScaleResource(slider.Resources, \"SliderThumbCornerRadius\", ratio)", source);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
