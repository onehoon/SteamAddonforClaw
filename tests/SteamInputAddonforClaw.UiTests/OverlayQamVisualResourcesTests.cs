using System.Xml.Linq;
using Xunit;

namespace SteamInputAddonforClaw.UiTests;

public sealed class OverlayQamVisualResourcesTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string[] RequiredBrushKeys =
    [
        "QamSurfaceBrush", "QamRailBrush", "QamContentBrush",
        "QamPrimaryTextBrush", "QamSecondaryTextBrush", "QamDisabledTextBrush", "QamErrorTextBrush",
        "QamAccentBrush", "QamSteamBlueBrush", "QamSelectedFillBrush", "QamHoverFillBrush", "QamPressedFillBrush",
        "QamFocusBorderBrush", "QamSeparatorBrush", "QamSectionBrush", "QamTileBrush",
        "QamTileSelectedBrush", "QamRailIconBrush", "QamRailIconSelectedBrush", "QamRailSelectedFillBrush",
        "QamToggleOffBrush", "QamToggleOnBrush", "QamToggleDisabledBrush", "QamToggleThumbBrush",
        "QamToggleThumbDisabledBrush", "QamSliderTrackBrush", "QamSliderValueTrackBrush",
        "QamSliderDisabledTrackBrush", "QamSliderThumbBrush", "QamSliderDisabledThumbBrush",
        "QamValueButtonForeground", "QamValueButtonDisabledForeground", "QamValueButtonHoverFill",
        "QamValueButtonPressedFill",
    ];

    private static readonly string[] RequiredStyleKeys =
    [
        "QamBodyTextStyle", "QamBodyStrongTextStyle", "QamCaptionTextStyle",
        "QamSectionHeaderTextStyle", "QamPageTitleTextStyle", "QamValueTextStyle", "QamTileTitleTextStyle",
        "QamFlatButtonStyle", "QamTileButtonStyle", "QamRailButtonStyle",
        "QamToggleStyle", "QamSliderStyle", "QamValueButtonStyle",
    ];

    [Fact]
    public void Qam_resources_are_merged_after_winui_controls_and_define_each_required_key_once()
    {
        var root = RepoRoot();
        var app = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "App.xaml"));
        var resources = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml"));
        var merged = app.Descendants().Single(element => element.Name.LocalName.EndsWith(".MergedDictionaries", StringComparison.Ordinal)).Elements().ToArray();
        var controlsIndex = Array.FindIndex(merged, element => element.Name.LocalName == "XamlControlsResources");
        var qamIndex = Array.FindIndex(merged, element => (string?)element.Attribute("Source") == "Themes/QamOverlayResources.xaml");

        Assert.True(controlsIndex >= 0);
        Assert.True(qamIndex > controlsIndex);

        var keys = resources.Descendants().Attributes(Xaml + "Key").Select(attribute => attribute.Value).ToArray();
        foreach (var key in RequiredBrushKeys.Concat(RequiredStyleKeys))
            Assert.Equal(1, keys.Count(existing => existing == key));
        Assert.Equal(1, keys.Count(existing => existing == "QamFontFamily"));
    }

    [Fact]
    public void Overlay_root_is_dark_and_uses_the_wider_shell_without_changing_the_rail()
    {
        var root = RepoRoot();
        var xaml = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml"));
        var viewport = xaml.Descendants().Single(element => (string?)element.Attribute(Xaml + "Name") == "AnimationViewport");
        var resources = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml"));
        var panel = xaml.Descendants().Single(element => (string?)element.Attribute(Xaml + "Name") == "OpaquePanel");
        var shell = xaml.Descendants().Single(element => (string?)element.Attribute(Xaml + "Name") == "QamShell");
        var columns = shell.Elements().Single(element => element.Name.LocalName == "Grid.ColumnDefinitions").Elements().ToArray();
        var rightContentHost = xaml.Descendants().Single(element =>
            element.Name.LocalName == "Grid" && (string?)element.Attribute("Grid.Column") == "1");
        var bodyScroll = xaml.Descendants().Single(element =>
            element.Name.LocalName == "ScrollViewer" && (string?)element.Attribute(Xaml + "Name") == "BodyScroll");
        var insetHost = bodyScroll.Descendants().Single(element =>
            (string?)element.Attribute("Padding") == "{StaticResource QamContentPadding}");
        var tabBody = insetHost.Descendants().Single(element =>
            (string?)element.Attribute(Xaml + "Name") == "TabBody");

        Assert.Equal("Dark", (string?)viewport.Attribute("RequestedTheme"));
        Assert.Equal("432", (string?)panel.Attribute("MaxWidth"));
        Assert.Equal("52", (string?)columns[0].Attribute("Width"));
        Assert.Equal("*", (string?)columns[1].Attribute("Width"));
        Assert.Same(rightContentHost, bodyScroll.Parent);
        Assert.Null(rightContentHost.Attribute("Margin"));
        Assert.Contains(bodyScroll, insetHost.Ancestors());
        Assert.Contains(insetHost, tabBody.Ancestors());
        Assert.Equal("Stretch", (string?)insetHost.Attribute("HorizontalAlignment"));
        Assert.Equal("Hidden", (string?)bodyScroll.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)bodyScroll.Attribute("HorizontalScrollBarVisibility"));
        Assert.Single(xaml.Descendants(), element => element.Name.LocalName == "ScrollViewer");
        Assert.DoesNotContain(xaml.Descendants(), element =>
            element.Name.LocalName == "Grid" && (string?)element.Attribute("Grid.Column") == "1" &&
            (string?)element.Attribute("Margin") == "{StaticResource QamContentPadding}");

        AssertResourceValue(resources, "QamContentPadding", "16,16,16,12");
        AssertResourceValue(resources, "QamRowPadding", "16,10,16,10");
        AssertResourceValue(resources, "QamRowMargin", "-16,0,-16,0");
        AssertResourceValue(resources, "QamRowMinHeight", "42");
        AssertResourceValue(resources, "QamRowCornerRadius", "2");
        AssertResourceValue(resources, "QamRailButtonSize", "52");
        AssertResourceValue(resources, "QamRailItemHeight", "64");
        AssertResourceValue(resources, "QamRailIconSize", "24");
        Assert.Equal("#26FFFFFF", Resource(resources, "QamSelectedFillBrush").Attribute("Color")?.Value);
        Assert.DoesNotContain("Footer", xaml.ToString());
    }

    [Fact]
    public void Toggle_on_uses_the_evidence_backed_steam_blue_resource()
    {
        var root = RepoRoot();
        var resources = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml"));
        var steamBlue = Resource(resources, "QamSteamBlueBrush");
        var toggleOn = Resource(resources, "QamToggleOnBrush");

        Assert.Equal("SolidColorBrush", steamBlue.Name.LocalName);
        Assert.Equal("#FF1A9FFF", (string?)steamBlue.Attribute("Color"));
        Assert.Equal("StaticResource", toggleOn.Name.LocalName);
        Assert.Equal("QamSteamBlueBrush", (string?)toggleOn.Attribute("ResourceKey"));
    }

    [Fact]
    public void Qam_row_padding_maps_css_vertical_horizontal_values_to_winui_thickness_order()
    {
        var root = RepoRoot();
        var resources = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml"));
        var rowChrome = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayRowChrome.cs"));

        Assert.Contains("<Thickness x:Key=\"QamRowPadding\">16,10,16,10</Thickness>", resources);
        Assert.Contains("new Thickness(16, 10, 16, 10)", rowChrome);
    }

    [Fact]
    public void Canonical_page_titles_are_added_once_around_each_existing_page_builder()
    {
        var root = RepoRoot();
        var shell = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs"));
        var resources = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml"));
        var pageTitle = resources.Descendants().Single(element =>
            element.Name.LocalName == "Style" && (string?)element.Attribute(Xaml + "Key") == "QamPageTitleTextStyle");
        var buildPage = SliceMethod(shell, "private FrameworkElement BuildPage(", "private static FrameworkElement CreateQamPage(");
        var createPage = SliceMethod(shell, "private static FrameworkElement CreateQamPage(", "private StackPanel BuildTabOrderEditorPage(");

        Assert.Equal("{StaticResource QamSectionHeaderTextStyle}", (string?)pageTitle.Attribute("BasedOn"));
        Assert.Equal("QamPageContentSpacing", Assert.Single(
            resources.Descendants().Attributes(Xaml + "Key"),
            attribute => attribute.Value == "QamPageContentSpacing").Value);
        Assert.Equal(1, CountOccurrences(buildPage, "CreateQamPage("));
        Assert.Contains("var content = id switch", buildPage);
        Assert.Contains("BuildQuickSettingsPage(id, QuickSettingsPageId.Device)", buildPage);
        Assert.Contains("BuildProfilePage()", buildPage);
        Assert.Contains("BuildControllerPage(rows)", buildPage);
        Assert.Contains("BuildShortcutPage()", buildPage);
        Assert.Contains("BuildSettingPage(rows)", buildPage);
        Assert.Contains("return CreateQamPage(id, content);", buildPage);
        Assert.Contains("Text = LabelFor(id)", createPage);
        Assert.Contains("QamPageContentSpacing", createPage);
        Assert.Contains("QamPageTitleTextStyle", createPage);
        Assert.DoesNotContain("Quick Settings", createPage);
        Assert.Equal(1, CountOccurrences(shell, "QamPageTitleTextStyle"));
    }

    [Fact]
    public void Large_product_buttons_use_flat_qam_template_without_changing_value_button_chrome()
    {
        var root = RepoRoot();
        var resources = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml"));
        var shell = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs"));
        var profile = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs"));
        var setting = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.ClawHud.cs"));
        var valueRow = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayValueRow.cs"));
        var tabOrder = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayTabOrderRow.cs"));
        var flatStyle = resources.Descendants().Single(element =>
            element.Name.LocalName == "Style" && (string?)element.Attribute(Xaml + "Key") == "QamFlatButtonStyle");
        var template = flatStyle.Descendants().Single(element => element.Name.LocalName == "ControlTemplate");
        var states = template.Descendants().Where(element => element.Name.LocalName == "VisualState")
            .Select(element => (string?)element.Attribute(Xaml + "Name")).ToArray();

        Assert.Equal(new[] { "Normal", "PointerOver", "Pressed", "Disabled" }, states);
        Assert.Contains("Property=\"IsTabStop\" Value=\"False\"", flatStyle.ToString());
        Assert.Contains("Property=\"UseSystemFocusVisuals\" Value=\"False\"", flatStyle.ToString());
        Assert.Contains("QamHoverFillBrush", template.ToString());
        Assert.Contains("QamPressedFillBrush", template.ToString());
        Assert.Contains("QamDisabledTextBrush", template.ToString());
        Assert.DoesNotContain("Accent", flatStyle.ToString());
        Assert.DoesNotContain("SystemAccent", flatStyle.ToString());

        Assert.Contains("BasedOn=\"{StaticResource QamFlatButtonStyle}\"", resources.Descendants().Single(element =>
            element.Name.LocalName == "Style" && (string?)element.Attribute(Xaml + "Key") == "QamRailButtonStyle").ToString());
        Assert.Contains("BasedOn=\"{StaticResource QamFlatButtonStyle}\"", resources.Descendants().Single(element =>
            element.Name.LocalName == "Style" && (string?)element.Attribute(Xaml + "Key") == "QamTileButtonStyle").ToString());
        Assert.Contains("Style = OverlayQamResources.Style(\"QamRailButtonStyle\")", shell);
        Assert.Contains("Style = OverlayQamResources.Style(\"QamTileButtonStyle\")", profile);
        Assert.Contains("Style = OverlayQamResources.Style(\"QamFlatButtonStyle\")", setting);

        Assert.Contains("x:Key=\"QamValueButtonStyle\" TargetType=\"primitives:ButtonBase\"", File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml")));
        Assert.Contains("Style = OverlayQamResources.Style(\"QamValueButtonStyle\")", valueRow);
        Assert.Contains("Style = OverlayQamResources.Style(\"QamValueButtonStyle\")", tabOrder);

        Assert.Contains("for (var i = 0; i < 3; i++)", profile);
        Assert.Contains("Grid.SetColumn(card, index % 3)", profile);
        Assert.Contains("Math.Min(2, _shortcutSnapshot.Tiles.Count)", File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shortcuts.cs")));
    }

    [Fact]
    public void Qam_control_styles_are_keyed_and_keep_theme_overrides_local_to_each_native_control()
    {
        var root = RepoRoot();
        var resources = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml"));
        var toggle = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayToggleRow.cs"));
        var slider = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayNumericSliderRow.cs"));
        var valueRow = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayValueRow.cs"));
        var tabOrder = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayTabOrderRow.cs"));
        var app = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "App.xaml"));

        Assert.Contains("x:Key=\"QamToggleStyle\" TargetType=\"ToggleSwitch\" BasedOn=\"{StaticResource DefaultToggleSwitchStyle}\"", resources);
        Assert.Contains("x:Key=\"QamSliderStyle\" TargetType=\"Slider\" BasedOn=\"{StaticResource DefaultSliderStyle}\"", resources);
        Assert.Contains("x:Key=\"QamValueButtonStyle\" TargetType=\"primitives:ButtonBase\"", resources);
        Assert.Contains("_toggle.Style = toggleStyle", toggle);
        Assert.Contains("toggle.Resources[key] = brush is", toggle);
        Assert.Contains("_suppress = true", toggle);
        Assert.Contains("_model.RequestToggle", toggle);
        Assert.Contains("_model.RequestSet(_toggle.IsOn)", toggle);
        Assert.Contains("_toggle.IsEnabled = isAvailable", toggle);
        Assert.Contains("slider.Resources[key] = brush", slider);
        Assert.Contains("Style = OverlayQamResources.Style(\"QamValueButtonStyle\")", valueRow);
        Assert.Contains("Style = OverlayQamResources.Style(\"QamValueButtonStyle\")", tabOrder);
        Assert.DoesNotContain("ToggleSwitchFillOn", app);
        Assert.DoesNotContain("SliderTrackValueFill", app);
    }

    [Fact]
    public void Overlay_product_chrome_no_longer_uses_light_surface_or_windows_accent_and_card_resources()
    {
        var root = RepoRoot();
        var sources = ReadOverlayVisualSources(root);
        var joined = string.Join(Environment.NewLine, sources.Values);

        Assert.DoesNotContain("#FFE7E7E7", joined);
        Assert.DoesNotContain("#FFD6D6D6", joined);
        Assert.DoesNotContain("Colors.DimGray", joined);
        Assert.DoesNotContain("AccentFillColorDefaultBrush", joined);
        Assert.DoesNotContain("SubtleFillColorSecondaryBrush", joined);
        Assert.DoesNotContain("CardBackgroundFillColorDefaultBrush", joined);
    }

    [Fact]
    public void Existing_overlay_surfaces_consume_the_shared_qam_visual_authority()
    {
        var root = RepoRoot();
        var sources = ReadOverlayVisualSources(root);

        Assert.Contains("QamRowPadding", sources["OverlayRowChrome.cs"]);
        Assert.Contains("QamRowMinHeight", sources["OverlayRowChrome.cs"]);
        Assert.Contains("QamSelectedFillBrush", sources["OverlayWindow.xaml.cs"]);
        Assert.Contains("QamRailButtonStyle", sources["OverlayWindow.Shell.cs"]);
        Assert.Contains("QamRailSelectedFillBrush", sources["OverlayWindow.Shell.cs"]);
        Assert.Contains("QamSectionBrush", sources["OverlayWindow.QuickSettings.cs"]);
        Assert.Contains("QamSectionCornerRadius", sources["OverlayWindow.QuickSettings.cs"]);
        Assert.Contains("QamTileBrush", sources["OverlayWindow.Shortcuts.cs"]);
        Assert.Contains("QamTileSelectedBrush", sources["OverlayWindow.Navigation.cs"]);
        Assert.Contains("QamTileBrush", sources["OverlayWindow.Profile.cs"]);
        Assert.Contains("QamTileSelectedBrush", sources["OverlayWindow.Profile.cs"]);
    }

    [Fact]
    public void Programmatic_overlay_text_uses_qam_keyed_styles_not_generic_windows_text_styles()
    {
        var root = RepoRoot();
        var sources = ReadOverlayVisualSources(root);
        var programmatic = string.Join(Environment.NewLine, sources
            .Where(entry => entry.Key.EndsWith(".cs", StringComparison.Ordinal))
            .Select(entry => entry.Value));

        Assert.DoesNotContain("BodyTextBlockStyle", programmatic);
        Assert.DoesNotContain("BodyStrongTextBlockStyle", programmatic);
        Assert.DoesNotContain("CaptionTextBlockStyle", programmatic);
        Assert.Contains("QamBodyTextStyle", programmatic);
        Assert.Contains("QamBodyStrongTextStyle", programmatic);
        Assert.Contains("QamCaptionTextStyle", programmatic);
        Assert.Contains("QamValueTextStyle", programmatic);
        Assert.Contains("QamTileTitleTextStyle", programmatic);
    }

    private static Dictionary<string, string> ReadOverlayVisualSources(string root)
    {
        var directory = Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay");
        var fileNames = new[]
        {
            "OverlayWindow.xaml", "OverlayWindow.xaml.cs", "OverlayWindow.Shell.cs",
            "OverlayWindow.Navigation.cs", "OverlayRowChrome.cs", "OverlayToggleRow.cs",
            "OverlayValueRow.cs", "OverlayNumericSliderRow.cs", "OverlayTabOrderRow.cs", "OverlayWindow.QuickSettings.cs",
            "OverlayWindow.Profile.cs", "OverlayWindow.Controller.cs", "OverlayWindow.ClawHud.cs",
            "OverlayWindow.Shortcuts.cs",
        };
        return fileNames.ToDictionary(name => name, name => File.ReadAllText(Path.Combine(directory, name)));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static XElement Resource(XDocument document, string key) => document.Descendants()
        .Single(element => (string?)element.Attribute(Xaml + "Key") == key);

    private static void AssertResourceValue(XDocument document, string key, string value) =>
        Assert.Equal(value, Resource(document, key).Value.Trim());

    private static string SliceMethod(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return source[startIndex..endIndex];
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
