using System.Xml.Linq;
using Xunit;

namespace SteamInputAddonforClaw.UiTests;

public sealed class MainNavigationIconContractTests
{
    [Fact]
    public void Main_navigation_and_overlay_rail_use_the_approved_icons()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainWindow = XDocument.Load(Path.Combine(repositoryRoot, "src", "SteamInputAddonforClaw.UI", "MainWindow.xaml"));
        var menuItems = mainWindow.Descendants().Single(element => element.Name.LocalName == "NavigationView.MenuItems");
        var navigationItems = menuItems.Elements()
            .Where(element => element.Name.LocalName == "NavigationViewItem")
            .ToArray();
        var expectedNavigationIcons = new[]
        {
            (Tag: "Device", Content: "Device", Icon: "FontIcon:E772"),
            (Tag: "MsiUpdates", Content: "MSI Update", Icon: "FontIcon:E895"),
            (Tag: "Controller", Content: "Controller", Icon: "FontIcon:E7FC"),
            (Tag: "Profile", Content: "Steam", Icon: "FontIcon:E8F1"),
            (Tag: "Xbox", Content: "XBOX", Icon: "FontIcon:E990"),
            (Tag: "Overlay", Content: "ClawHUD", Icon: "FontIcon:E7F4"),
            (Tag: "Shortcut", Content: "Shortcut", Icon: "FontIcon:E71B"),
            (Tag: "Tweaks", Content: "Tweaks", Icon: "SymbolIcon:Repair"),
            (Tag: "HowToUse", Content: "How to Use", Icon: "SymbolIcon:Help"),
        };

        Assert.Equal(expectedNavigationIcons.Length, navigationItems.Length);
        foreach (var expected in expectedNavigationIcons)
        {
            var item = Assert.Single(navigationItems, element =>
                (string?)element.Attribute("Tag") == expected.Tag);
            Assert.Equal(expected.Content, (string?)item.Attribute("Content"));
            Assert.Equal(expected.Icon, ReadNavigationIcon(item));
        }

        var overlayShell = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "SteamInputAddonforClaw.Overlay",
            "OverlayWindow.Shell.cs"));
        var expectedRailGlyphs = new[]
        {
            (Tab: "Device", Glyph: "E772"),
            (Tab: "Profile", Glyph: "E8A5"),
            (Tab: "Controller", Glyph: "E7FC"),
            (Tab: "Shortcut", Glyph: "E71B"),
            (Tab: "Setting", Glyph: "E713"),
        };

        foreach (var expected in expectedRailGlyphs)
        {
            Assert.Contains(
                $"AddonQuickSettingsTabId.{expected.Tab} => \"\\u{expected.Glyph}\"",
                overlayShell,
                StringComparison.Ordinal);
        }

        Assert.Equal("E772", expectedNavigationIcons.Single(item => item.Tag == "Device").Icon[9..]);
        Assert.Equal("E7FC", expectedNavigationIcons.Single(item => item.Tag == "Controller").Icon[9..]);
        Assert.Equal("E71B", expectedNavigationIcons.Single(item => item.Tag == "Shortcut").Icon[9..]);
    }

    private static string ReadNavigationIcon(XElement item)
    {
        var iconContainer = Assert.Single(item.Elements(), element =>
            element.Name.LocalName == "NavigationViewItem.Icon");
        var icon = Assert.Single(iconContainer.Elements());

        return icon.Name.LocalName switch
        {
            "FontIcon" => $"FontIcon:{Convert.ToInt32(((string?)icon.Attribute("Glyph"))![0]):X4}",
            "SymbolIcon" => $"SymbolIcon:{(string?)icon.Attribute("Symbol")}",
            _ => $"UnexpectedIcon:{icon.Name.LocalName}",
        };
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
