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
        "QamAccentBrush", "QamSelectedFillBrush", "QamHoverFillBrush", "QamPressedFillBrush",
        "QamFocusBorderBrush", "QamSeparatorBrush", "QamSectionBrush", "QamTileBrush",
        "QamTileSelectedBrush", "QamRailIconBrush", "QamRailIconSelectedBrush", "QamRailSelectedFillBrush",
    ];

    private static readonly string[] RequiredStyleKeys =
    [
        "QamBodyTextStyle", "QamBodyStrongTextStyle", "QamCaptionTextStyle",
        "QamSectionHeaderTextStyle", "QamValueTextStyle", "QamTileTitleTextStyle", "QamRailButtonStyle",
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
    public void Overlay_root_is_dark_and_keeps_the_frozen_shell_geometry()
    {
        var root = RepoRoot();
        var xaml = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml"));
        var viewport = xaml.Descendants().Single(element => (string?)element.Attribute(Xaml + "Name") == "AnimationViewport");
        var source = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml"));
        var resources = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml"));

        Assert.Equal("Dark", (string?)viewport.Attribute("RequestedTheme"));
        Assert.Contains("MaxWidth=\"416\"", source);
        Assert.Contains("<ColumnDefinition Width=\"52\" />", source);
        Assert.Contains("QamContentPadding", source);
        Assert.Contains("<Thickness x:Key=\"QamContentPadding\">16,16,16,12</Thickness>", resources);
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
            "OverlayValueRow.cs", "OverlayTabOrderRow.cs", "OverlayWindow.QuickSettings.cs",
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
}
