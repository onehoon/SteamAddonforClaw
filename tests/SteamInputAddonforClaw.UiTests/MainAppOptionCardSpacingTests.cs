using System.Xml.Linq;
using Xunit;

namespace SteamInputAddonforClaw.UiTests;

public sealed class MainAppOptionCardSpacingTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("DevicePage.xaml")]
    [InlineData("ControllerPage.xaml")]
    [InlineData("ProfilePage.xaml")]
    [InlineData("XboxPage.xaml")]
    [InlineData("OverlayPage.xaml")]
    [InlineData("QuickOverlayPage.xaml")]
    [InlineData("WindowsTweaksPage.xaml")]
    [InlineData("SettingsPage.xaml")]
    [InlineData("DeveloperPage.xaml")]
    public void Main_app_option_card_stack_uses_eight_effective_pixels(string file)
    {
        var document = ReadPage(file);
        var optionStacks = document.Descendants()
            .Where(element => element.Name.LocalName == "ScrollViewer")
            .SelectMany(scrollViewer => scrollViewer.Elements().Where(element => element.Name.LocalName == "StackPanel"))
            .ToArray();

        Assert.Contains(optionStacks, content => (string?)content.Attribute("Spacing") == "8");
    }

    [Fact]
    public void Controller_mapping_peer_card_groups_use_eight_pixels()
    {
        var document = ReadPage("ControllerPage.xaml");
        foreach (var name in new[] { "MappingContent", "FrontButtonMappingContent" })
        {
            var panel = FindNamed(document, "StackPanel", name);
            Assert.Equal("8", (string?)panel.Attribute("Spacing"));
        }
    }

    [Fact]
    public void Tweaks_uses_eight_pixel_card_gaps_and_twelve_pixel_section_boundaries()
    {
        var document = ReadPage("WindowsTweaksPage.xaml");
        var panel = FindNamed(document, "StackPanel", "TweaksPanel");
        Assert.Equal("8", (string?)panel.Attribute("Spacing"));

        foreach (var name in new[] { "Tweaks_GamingPerformanceHeader", "Tweaks_RegistryTweaksHeader" })
        {
            var header = Assert.Single(document.Descendants(), element =>
                element.Attribute(Xaml + "Uid")?.Value == name);
            Assert.Equal("0,4,0,0", (string?)header.Attribute("Margin"));
        }
    }

    [Fact]
    public void Msi_update_sections_and_shortcut_tiles_keep_their_distinct_spacing_contracts()
    {
        var msi = ReadPage("MsiUpdatesPage.xaml");
        var msiStack = msi.Descendants().First(element => element.Name.LocalName == "StackPanel");
        Assert.Equal("12", (string?)msiStack.Attribute("Spacing"));
        Assert.Contains(msi.Descendants().Where(element => element.Name.LocalName == "StackPanel"),
            element => (string?)element.Attribute("Spacing") == "8");

        var shortcuts = ReadPage("ShortcutPage.xaml");
        var tile = Assert.Single(shortcuts.Descendants(), element =>
            element.Name.LocalName == "Border" && element.Attribute("Tag") is not null);
        Assert.Equal("0,0,8,8", (string?)tile.Attribute("Margin"));
        Assert.Equal("16", (string?)shortcuts.Descendants().First(element => element.Name.LocalName == "Grid" && element.Attribute("RowSpacing") is not null).Attribute("RowSpacing"));
    }

    [Fact]
    public void Steam_and_xbox_catalog_grid_geometry_is_not_reduced_with_detail_card_gaps()
    {
        var steam = ReadPage("ProfilePage.xaml");
        var itemsPanel = Assert.Single(steam.Descendants(), element => element.Name.LocalName == "ItemsWrapGrid");
        Assert.Equal("183", (string?)itemsPanel.Attribute("ItemWidth"));
        Assert.Equal("270.5", (string?)itemsPanel.Attribute("ItemHeight"));
        var steamCard = Assert.Single(steam.Descendants(), element => element.Name.LocalName == "Border"
            && element.Attribute(Xaml + "Name")?.Value == "GameCardRoot");
        Assert.Equal("0,0,8,8", (string?)steamCard.Attribute("Margin"));

        var xbox = ReadPage("XboxPage.xaml");
        var xboxItemsPanel = Assert.Single(xbox.Descendants(), element => element.Name.LocalName == "ItemsWrapGrid");
        Assert.Equal("183", (string?)xboxItemsPanel.Attribute("ItemWidth"));
        Assert.Equal("270.5", (string?)xboxItemsPanel.Attribute("ItemHeight"));
        var xboxCard = Assert.Single(xbox.Descendants(), element => element.Name.LocalName == "Border"
            && element.Attribute(Xaml + "Name")?.Value == "GameCardRoot");
        Assert.Equal("0,0,8,8", (string?)xboxCard.Attribute("Margin"));
    }

    private static XElement ReadPage(string file)
        => XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "Views", file)).Root!;

    private static XElement FindNamed(XElement root, string localName, string name)
        => Assert.Single(root.Descendants(), element => element.Name.LocalName == localName
            && element.Attribute(Xaml + "Name")?.Value == name);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
