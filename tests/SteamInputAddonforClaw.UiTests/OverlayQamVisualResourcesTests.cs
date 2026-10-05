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
    public void Overlay_root_has_a_fixed_title_and_preserves_the_full_width_scroll_viewport()
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
        var pageTitle = xaml.Descendants().Single(element =>
            (string?)element.Attribute(Xaml + "Name") == "PageTitle");
        var bodyScroll = xaml.Descendants().Single(element =>
            element.Name.LocalName == "ScrollViewer" && (string?)element.Attribute(Xaml + "Name") == "BodyScroll");
        var insetHost = bodyScroll.Descendants().Single(element =>
            (string?)element.Attribute("Padding") == "{StaticResource QamBodyContentPadding}");
        var tabBody = insetHost.Descendants().Single(element =>
            (string?)element.Attribute(Xaml + "Name") == "TabBody");
        var rowChrome = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayRowChrome.cs"));
        var quickSettings = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs"));
        var sectionCardStart = quickSettings.IndexOf("private static Border CreateOverlaySectionCard", StringComparison.Ordinal);
        var sectionCardEnd = quickSettings.IndexOf("private bool TryCreateQuickSettingsRow", sectionCardStart, StringComparison.Ordinal);
        var sectionCard = quickSettings[sectionCardStart..sectionCardEnd];
        var rightRows = rightContentHost.Elements().Single(element => element.Name.LocalName == "Grid.RowDefinitions")
            .Elements().Select(element => (string?)element.Attribute("Height")).ToArray();

        Assert.Equal("Dark", (string?)viewport.Attribute("RequestedTheme"));
        Assert.Equal("432", (string?)panel.Attribute("MaxWidth"));
        Assert.Equal("52", (string?)columns[0].Attribute("Width"));
        Assert.Equal("*", (string?)columns[1].Attribute("Width"));
        Assert.Same(rightContentHost, pageTitle.Parent);
        Assert.Equal("0", (string?)pageTitle.Attribute("Grid.Row"));
        Assert.Equal("1", (string?)bodyScroll.Attribute("Grid.Row"));
        Assert.Equal(new[] { "Auto", "*" }, rightRows);
        Assert.Same(rightContentHost, bodyScroll.Parent);
        Assert.Null(rightContentHost.Attribute("Margin"));
        Assert.DoesNotContain(bodyScroll.DescendantsAndSelf(), element => ReferenceEquals(element, pageTitle));
        Assert.Contains(bodyScroll, insetHost.Ancestors());
        Assert.Contains(insetHost, tabBody.Ancestors());
        Assert.Equal("Stretch", (string?)insetHost.Attribute("HorizontalAlignment"));
        Assert.Equal("Hidden", (string?)bodyScroll.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)bodyScroll.Attribute("HorizontalScrollBarVisibility"));
        Assert.Single(xaml.Descendants(), element => element.Name.LocalName == "ScrollViewer");
        Assert.Single(xaml.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "PageTitle");
        Assert.DoesNotContain(xaml.Descendants(), element =>
            element.Name.LocalName == "Grid" && (string?)element.Attribute("Grid.Column") == "1" &&
            (string?)element.Attribute("Margin") == "{StaticResource QamBodyContentPadding}");

        AssertResourceValue(resources, "QamPageTitleMargin", "16,20,16,16");
        AssertResourceValue(resources, "QamBodyContentPadding", "16,0,16,12");
        AssertResourceValue(resources, "QamRowPadding", "16,10,16,10");
        AssertResourceValue(resources, "QamRowMargin", "-16,0,-16,0");
        Assert.Contains("Margin = OverlayQamResources.Get(\"QamRowMargin\", new Thickness(-16, 0, -16, 0))", rowChrome);
        AssertResourceValue(resources, "QamRowMinHeight", "42");
        AssertResourceValue(resources, "QamRowCornerRadius", "2");
        Assert.Equal("#1AFFFFFF", Resource(resources, "QamSeparatorBrush").Attribute("Color")?.Value);
        AssertResourceValue(resources, "QamSectionSeparatorThickness", "0,1,0,0");
        AssertResourceValue(resources, "QamSectionPadding", "16,0,16,0");
        Assert.DoesNotContain(resources.Descendants().Attributes(Xaml + "Key"), attribute => attribute.Value == "QamRowSeparatorThickness");
        Assert.Contains("BorderThickness = OverlayQamResources.Get(\"QamSelectionBorderThickness\", new Thickness(0))", rowChrome);
        Assert.DoesNotContain("QamSeparatorBrush", rowChrome);
        Assert.DoesNotContain("QamSectionSeparatorThickness", rowChrome);
        Assert.Contains("private static void SetOverlaySectionSeparator(Border card, bool visible)", quickSettings);
        Assert.Contains("card.BorderBrush = OverlayQamResources.Brush(\"QamSeparatorBrush\")", quickSettings);
        Assert.Contains("\"QamSectionSeparatorThickness\"", quickSettings);
        Assert.Contains("Margin = OverlayQamResources.Get(\"QamRowMargin\", new Thickness(-16, 0, -16, 0))", quickSettings);
        Assert.Contains("BorderBrush = OverlayQamResources.Brush(\"QamSeparatorBrush\")", sectionCard);
        Assert.Contains("BorderThickness = new Thickness(0)", sectionCard);
        Assert.Contains("QamRowPadding", rowChrome);
        Assert.Contains("QamRowMinHeight", rowChrome);
        Assert.Contains("QamRowCornerRadius", rowChrome);
        AssertResourceValue(resources, "QamRailButtonSize", "52");
        AssertResourceValue(resources, "QamRailItemHeight", "64");
        AssertResourceValue(resources, "QamRailIconSize", "24");
        AssertResourceValue(resources, "QamSectionSpacing", "24");
        AssertResourceValue(resources, "QamSectionHeaderSpacing", "4");
        AssertResourceValue(resources, "QamRowSpacing", "0");
        AssertResourceValue(resources, "QamSliderHeight", "22");
        AssertResourceValue(resources, "QamSliderTrackHeight", "4");
        AssertResourceValue(resources, "QamSliderThumbWidth", "12");
        AssertResourceValue(resources, "QamSliderThumbHeight", "12");
        AssertResourceValue(resources, "QamValueButtonWidth", "40");
        AssertResourceValue(resources, "QamValueButtonHeight", "22");
        AssertResourceValue(resources, "QamToggleMinHeight", "22");
        AssertResourceValue(resources, "QamTogglePreContentMargin", "1");
        AssertResourceValue(resources, "QamTogglePostContentMargin", "1");
        Assert.Equal("Segoe UI Variable", Resource(resources, "QamFontFamily").Value.Trim());
        Assert.DoesNotContain(resources.Descendants().Attributes(Xaml + "Key"), attribute => attribute.Value is "QamContentPadding" or "QamPageContentSpacing");
        Assert.Equal("#26FFFFFF", Resource(resources, "QamSelectedFillBrush").Attribute("Color")?.Value);
        Assert.DoesNotContain("Footer", xaml.ToString());
    }

    [Fact]
    public void Passive_section_heading_uses_the_existing_strong_text_style_and_row_chrome()
    {
        var quickSettings = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs"));
        var rowChrome = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "SteamInputAddonforClaw.Overlay", "OverlayRowChrome.cs"));
        var resources = XDocument.Load(Path.Combine(
            RepoRoot(), "src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml"));
        var headingStart = quickSettings.IndexOf("private static Border CreateOverlaySectionHeadingRow", StringComparison.Ordinal);
        var headingEnd = quickSettings.IndexOf("private static StackPanel CreateOverlayDetailStack", headingStart, StringComparison.Ordinal);
        var heading = quickSettings[headingStart..headingEnd];
        var strongStyle = resources.Descendants().Single(element =>
            element.Name.LocalName == "Style" && (string?)element.Attribute(Xaml + "Key") == "QamBodyStrongTextStyle");
        var bodyStyle = resources.Descendants().Single(element =>
            element.Name.LocalName == "Style" && (string?)element.Attribute(Xaml + "Key") == "QamBodyTextStyle");
        string? SetterValue(XElement style, string property) => style.Elements().Single(element =>
            element.Name.LocalName == "Setter" && (string?)element.Attribute("Property") == property).Attribute("Value")?.Value;

        Assert.True(headingStart >= 0);
        Assert.True(headingEnd > headingStart);
        Assert.Contains("QamBodyStrongTextStyle", heading);
        Assert.Contains("OverlayRowChrome.Create(heading)", heading);
        Assert.Equal("SemiBold", SetterValue(strongStyle, "FontWeight"));
        Assert.Equal("{StaticResource QamPrimaryTextBrush}", SetterValue(strongStyle, "Foreground"));
        Assert.Equal("{StaticResource QamSecondaryTextBrush}", SetterValue(bodyStyle, "Foreground"));
        Assert.Contains("QamRowPadding", rowChrome);
        Assert.Contains("QamRowMinHeight", rowChrome);
        Assert.Contains("QamRowCornerRadius", rowChrome);
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
    public void One_canonical_page_title_stays_outside_the_scroll_body()
    {
        var root = RepoRoot();
        var shell = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs"));
        var xaml = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml"));
        var resources = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml"));
        var pageTitle = resources.Descendants().Single(element =>
            element.Name.LocalName == "Style" && (string?)element.Attribute(Xaml + "Key") == "QamPageTitleTextStyle");
        var buildPage = SliceMethod(shell, "private FrameworkElement BuildPage(", "private StackPanel BuildTabOrderEditorPage(");
        var selectedStateStart = shell.IndexOf("private void ApplySelectedTabVisualState()", StringComparison.Ordinal);
        Assert.True(selectedStateStart >= 0);
        var selectedState = shell[selectedStateStart..];
        var titleElement = xaml.Descendants().Single(element => (string?)element.Attribute(Xaml + "Name") == "PageTitle");

        Assert.Equal("{StaticResource QamSectionHeaderTextStyle}", (string?)pageTitle.Attribute("BasedOn"));
        Assert.Contains("var content = id switch", buildPage);
        Assert.Contains("BuildQuickSettingsPage(id, QuickSettingsPageId.Device)", buildPage);
        Assert.Contains("BuildProfilePage()", buildPage);
        Assert.Contains("BuildControllerPage(rows)", buildPage);
        Assert.Contains("BuildShortcutPage()", buildPage);
        Assert.Contains("BuildSettingPage(rows)", buildPage);
        Assert.Contains("return content;", buildPage);
        Assert.DoesNotContain("CreateQamPage", shell);
        Assert.Equal("{StaticResource QamPageTitleTextStyle}", (string?)titleElement.Attribute("Style"));
        Assert.Equal("{StaticResource QamPageTitleMargin}", (string?)titleElement.Attribute("Margin"));
        Assert.Contains("PageTitle.Text = LabelFor(selected);", selectedState);
        Assert.Equal(1, CountOccurrences(xaml.ToString(), "x:Name=\"PageTitle\""));
    }

    [Fact]
    public void Ordinary_row_selection_changes_only_fill_and_keeps_tile_selection_border()
    {
        var root = RepoRoot();
        var navigation = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs"));
        var profile = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs"));
        var window = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml.cs"));
        var selectionVisual = SliceMethod(
            navigation,
            "private void ApplyRowSelectionVisual()",
            "// Resolve against the current page order at interaction time.");

        Assert.Contains("_rowSelectedFillBrush = OverlayQamResources.Brush(\"QamSelectedFillBrush\")", window);
        Assert.Contains("selected ? _rowSelectedFillBrush : RowUnselectedFillBrush", selectionVisual);
        Assert.Contains("rows[i].Container.Background", selectionVisual);
        Assert.DoesNotContain("rows[i].Container.BorderBrush", selectionVisual);

        Assert.Contains("tile.BorderBrush = _rowSelectedBrush", navigation);
        Assert.Contains("_profileCatalogCards[index].BorderBrush = _rowSelectedBrush", profile);
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

        Assert.Contains("for (var i = 0; i < 2; i++)", profile);
        Assert.Contains("Grid.SetColumn(card, index % 2)", profile);
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
        Assert.Contains("toggle.Resources[\"ToggleSwitchPreContentMargin\"]", toggle);
        Assert.Contains("toggle.Resources[\"ToggleSwitchPostContentMargin\"]", toggle);
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
    public void Qam_toggle_and_value_controls_opt_out_of_native_focus()
    {
        var resources = XDocument.Load(Path.Combine(
            RepoRoot(), "src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml"));
        var focusProperties = new[]
        {
            "IsTabStop",
            "IsFocusEngagementEnabled",
            "AllowFocusOnInteraction",
            "UseSystemFocusVisuals",
        };

        foreach (var styleKey in new[] { "QamToggleStyle", "QamValueButtonStyle" })
        {
            var style = resources.Descendants().Single(element =>
                element.Name.LocalName == "Style" && (string?)element.Attribute(Xaml + "Key") == styleKey);
            foreach (var property in focusProperties)
            {
                var setter = style.Elements().Single(element =>
                    element.Name.LocalName == "Setter" && (string?)element.Attribute("Property") == property);
                Assert.Equal("False", (string?)setter.Attribute("Value"));
            }
        }
    }

    [Fact]
    public void Overlay_pages_render_only_minimal_text_and_keep_state_and_interaction_paths()
    {
        var root = RepoRoot();
        var sources = ReadOverlayVisualSources(root);
        var joined = string.Join(Environment.NewLine, sources.Values);
        var quickSettings = sources["OverlayWindow.QuickSettings.cs"];
        var controller = sources["OverlayWindow.Controller.cs"];
        var clawHud = sources["OverlayWindow.ClawHud.cs"];
        var profile = sources["OverlayWindow.Profile.cs"];
        var shortcuts = sources["OverlayWindow.Shortcuts.cs"];

        string[] forbidden =
        [
            "Waiting for ClawHUD state.", "Enabled · Ready", "VRR:",
            "Already using the native VRR range", "Use Left and Right to move the selected tab.",
            "Xbox 360 mode only. Steam Game / Big Picture keeps M1 as R4 and M2 as L4.",
            "Updating M1 / M2 mapping", "M1 / M2 mapping is unavailable.",
            "Loading games", "No games found.", "Loading shortcuts", "No shortcuts configured",
            "Loading Profile settings", "Shortcut settings are unavailable.", "Shortcut could not be executed.",
            "Quick Settings are unavailable.",
        ];
        foreach (var phrase in forbidden)
            Assert.DoesNotContain(phrase, joined, StringComparison.Ordinal);

        Assert.DoesNotContain("page.Message", quickSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("section.Message", quickSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("LastLocalFailureMessage", quickSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("tile.StatusText", shortcuts, StringComparison.Ordinal);
        Assert.Contains("Text = tile.Title", shortcuts, StringComparison.Ordinal);
        Assert.Contains("tile.Enabled ? 1.0", shortcuts, StringComparison.Ordinal);
        Assert.Contains("_shortcutExecutionInFlight", shortcuts, StringComparison.Ordinal);
        Assert.Contains("_clawHudSnapshot = snapshot", clawHud, StringComparison.Ordinal);
        Assert.Contains("RenderClawHudControls();", clawHud, StringComparison.Ordinal);
        Assert.Contains("state.Error", profile, StringComparison.Ordinal);
        Assert.Contains("OverlayLog.Warn(\"Profile\", state.Error)", profile, StringComparison.Ordinal);
        Assert.Contains("FailureMessage = response.Succeeded", controller, StringComparison.Ordinal);
        Assert.Contains("RenderBackButtonMappingRows();", controller, StringComparison.Ordinal);
        Assert.Contains("QamCaptionTextStyle", File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml")));
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
