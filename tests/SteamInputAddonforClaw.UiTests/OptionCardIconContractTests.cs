using System.Xml.Linq;
using Xunit;

namespace SteamInputAddonforClaw.UiTests;

public sealed class OptionCardIconContractTests
{
    private static readonly (string Page, string Header, string Icon)[] ExpectedIcons =
    [
        ("DevicePage.xaml", "MSI Center M", "FontIcon:E71D"),
        ("DevicePage.xaml", "TDP Control", "FontIcon:E945"),
        ("DevicePage.xaml", "CPU Boost", "FontIcon:EEA1"),
        ("DevicePage.xaml", "Maximum Processor State", "FontIcon:F42F"),
        ("DevicePage.xaml", "Windows Power Mode", "FontIcon:E7E8"),
        ("DevicePage.xaml", "Battery charge limit", "FontIcon:E86B"),

        ("ControllerPage.xaml", "Controller for Steam", "FontIcon:E7FC"),
        ("ControllerPage.xaml", "Gamebar Button", "FontIcon:E990"),
        ("ControllerPage.xaml", "Center M Button", "FontIcon:E8B0"),
        ("ControllerPage.xaml", "M1 / M2", "FontIcon:E8AB"),
        ("ControllerPage.xaml", "Joystick LED", "FontIcon:E781"),
        ("ControllerPage.xaml", "Vibration Strength", "FontIcon:E877"),

        ("ProfilePage.xaml", "Virtual Controller", "FontIcon:E7FC"),
        ("ProfilePage.xaml", "TDP Control", "FontIcon:E945"),
        ("ProfilePage.xaml", "CPU Boost", "FontIcon:EEA1"),
        ("ProfilePage.xaml", "Windows Power Mode", "FontIcon:E7E8"),
        ("ProfilePage.xaml", "Minimum GPU Clock", "FontIcon:E9D2"),
        ("ProfilePage.xaml", "Intel FPS Limit", "FontIcon:EC4A"),
        ("ProfilePage.xaml", "Resolution", "FontIcon:E7F4"),

        ("XboxPage.xaml", "M1 / M2 Button Mapping", "FontIcon:E8AB"),
        ("XboxPage.xaml", "TDP Control", "FontIcon:E945"),
        ("XboxPage.xaml", "CPU Boost", "FontIcon:EEA1"),
        ("XboxPage.xaml", "Windows Power Mode", "FontIcon:E7E8"),
        ("XboxPage.xaml", "Minimum GPU Clock", "FontIcon:E9D2"),
        ("XboxPage.xaml", "Intel FPS Limit", "FontIcon:EC4A"),
        ("XboxPage.xaml", "Resolution", "FontIcon:E7F4"),

        ("OverlayPage.xaml", "Enable HUD", "FontIcon:E7F4"),
        ("OverlayPage.xaml", "Display mode", "SymbolIcon:View"),
        ("OverlayPage.xaml", "HUD size", "SymbolIcon:FontSize"),
        ("OverlayPage.xaml", "Font", "SymbolIcon:Font"),
        ("OverlayPage.xaml", "Alignment", "SymbolIcon:AlignCenter"),
        ("OverlayPage.xaml", "Background width", "FontIcon:E745"),
        ("OverlayPage.xaml", "HUD opacity", "FontIcon:E794"),
        ("OverlayPage.xaml", "Intel VRR Range Fix", "SymbolIcon:Repair"),

        ("SettingsPage.xaml", "Application updates", "FontIcon:E777"),
        ("SettingsPage.xaml", "App theme", "FontIcon:E7A1"),
        ("SettingsPage.xaml", "Windows Gaming Full Screen Experience", "SymbolIcon:FullScreen"),
        ("SettingsPage.xaml", "Show only current power source", "SymbolIcon:View"),
        ("SettingsPage.xaml", "Logging", "SymbolIcon:List"),
        ("SettingsPage.xaml", "Required Components", "SymbolIcon:Repair"),
    ];

    [Fact]
    public void Main_app_option_card_icons_match_the_approved_cross_page_map()
    {
        Assert.Equal(40, ExpectedIcons.Length);
        var controllerIcons = ExpectedIcons
            .Where(expected => expected.Page == "ControllerPage.xaml")
            .Select(expected => expected.Icon)
            .ToArray();
        Assert.Equal(controllerIcons.Length, controllerIcons.Distinct(StringComparer.Ordinal).Count());

        var viewsDirectory = Path.Combine(FindRepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "Views");

        foreach (var pageGroup in ExpectedIcons.GroupBy(expected => expected.Page, StringComparer.Ordinal))
        {
            var document = XDocument.Load(Path.Combine(viewsDirectory, pageGroup.Key));
            var cardsWithIcons = document.Descendants()
                .Where(IsHeaderedOptionCard)
                .Where(HasHeaderIcon)
                .ToArray();
            var expectedPageIcons = pageGroup.ToArray();
            var excludedDeveloperCardCount = pageGroup.Key == "SettingsPage.xaml" ? 1 : 0;

            Assert.Equal(expectedPageIcons.Length + excludedDeveloperCardCount, cardsWithIcons.Length);

            foreach (var expected in expectedPageIcons)
            {
                var card = Assert.Single(cardsWithIcons, element =>
                    (string?)element.Attribute("Header") == expected.Header);
                Assert.Equal(expected.Icon, ReadHeaderIcon(card));
            }
        }

        var settingsDocument = XDocument.Load(Path.Combine(viewsDirectory, "SettingsPage.xaml"));
        var developerCard = Assert.Single(settingsDocument.Descendants(), element =>
            element.Name.LocalName == "SettingsCard" &&
            (string?)element.Attribute("Header") == "Developer Menu");
        Assert.Equal("SymbolIcon:Help", ReadHeaderIcon(developerCard));
    }

    private static bool IsHeaderedOptionCard(XElement element) =>
        (element.Name.LocalName == "SettingsCard" || element.Name.LocalName == "SettingsExpander") &&
        element.Attribute("Header") is not null;

    private static bool HasHeaderIcon(XElement element) =>
        element.Elements().Any(child => child.Name.LocalName == $"{element.Name.LocalName}.HeaderIcon");

    private static string ReadHeaderIcon(XElement card)
    {
        var headerIcon = Assert.Single(card.Elements(), child =>
            child.Name.LocalName == $"{card.Name.LocalName}.HeaderIcon");
        var icon = Assert.Single(headerIcon.Elements());

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
