using System.Xml.Linq;
using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
using SteamInputAddonforClaw.Windowing;
using Xunit;

namespace SteamInputAddonforClaw.UiTests;

public sealed class WindowsTweaksUiContractTests
{
    [Fact]
    public void Tweaks_navigation_is_inserted_before_how_to_use_and_keeps_device_default()
    {
        var root = FindRepositoryRoot();
        var main = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "MainWindow.xaml"));
        var menu = main.Descendants().Single(element => element.Name.LocalName == "NavigationView.MenuItems");
        var tags = menu.Elements().Where(element => element.Name.LocalName == "NavigationViewItem")
            .Select(element => (string?)element.Attribute("Tag")).ToArray();

        Assert.Equal(new[] { "Device", "Controller", "Profile", "Xbox", "Overlay", "Shortcut", "Tweaks", "HowToUse" }, tags);
        Assert.Equal("Device", (string?)menu.Elements().First().Attribute("Tag"));
        Assert.Equal(MainNavigationPage.Device, new MainNavigationState().CurrentPage);
    }

    [Fact]
    public void Page_contains_exactly_the_five_approved_switches_with_no_markup_default()
    {
        var root = FindRepositoryRoot();
        var xaml = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "WindowsTweaksPage.xaml"));
        var cards = xaml.Descendants().Where(element => element.Name.LocalName == "SettingsCard").ToArray();
        var names = cards.Select(card => (string?)card.Attribute("Header")).ToArray();

        Assert.Equal(new[]
        {
            "Memory Integrity",
            "Virtual Machine Platform",
            "Faster Menus",
            "Classic Context Menu",
            "Disable Password on Wake",
        }, names);
        foreach (var card in cards)
        {
            Assert.False(string.IsNullOrWhiteSpace((string?)card.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))));
            var toggle = Assert.Single(card.Descendants(), element => element.Name.LocalName == "ToggleSwitch");
            Assert.Equal("False", (string?)toggle.Attribute("IsEnabled"));
            Assert.Null(toggle.Attribute("IsOn"));
        }
    }

    [Fact]
    public void Page_groups_tweaks_under_the_approved_section_headers()
    {
        var root = FindRepositoryRoot();
        var xaml = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "WindowsTweaksPage.xaml"));
        var pageTitle = xaml.Descendants().Single(element =>
            element.Name.LocalName == "TextBlock" && (string?)element.Attribute("FontSize") == "24");
        Assert.Equal("Tweaks", (string?)pageTitle.Attribute("Text"));

        var panel = xaml.Descendants().Single(element =>
            element.Name.LocalName == "StackPanel"
            && (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "TweaksPanel");
        var items = panel.Elements().Select(element => element.Name.LocalName switch
        {
            "TextBlock" => (string?)element.Attribute("Text"),
            "SettingsCard" => (string?)element.Attribute("Header"),
            _ => null,
        }).ToArray();

        Assert.Equal(new[]
        {
            "Gaming Performance",
            "Memory Integrity",
            "Virtual Machine Platform",
            "Registry Tweaks",
            "Faster Menus",
            "Classic Context Menu",
            "Disable Password on Wake",
        }, items);
        var fasterMenus = panel.Descendants().Single(element =>
            element.Name.LocalName == "SettingsCard" && (string?)element.Attribute("Header") == "Faster Menus");
        Assert.Equal("Disable menu animation and reduce submenu delay to 10 ms.", (string?)fasterMenus.Attribute("Description"));
    }

    [Fact]
    public void Omitted_model_specific_tweak_hides_the_entire_card()
    {
        var modelSpecific = new FrontendWindowsTweakDefinition(
            FrontendWindowsTweakId.VirtualMachinePlatform,
            "Synthetic model-specific tweak",
            "Test only",
            FrontendWindowsTweakApplicability.A2vmOnly);
        // Runtime applicability filtering omits this A2VM-only row on non-A2VM devices.
        var runtimeFilteredSnapshot = new FrontendWindowsTweaksSnapshot(
            Array.Empty<FrontendWindowsTweakSnapshot>());

        Assert.Equal(
            Visibility.Collapsed,
            WindowsTweaksPage.GetCardVisibility(runtimeFilteredSnapshot, modelSpecific.Id));

        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "WindowsTweaksPage.xaml.cs"));
        Assert.Contains("card.Visibility = GetCardVisibility(snapshot, definition.Id)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_or_pending_warning_state_never_becomes_an_off_default()
    {
        var day = new DateOnly(2026, 10, 13);

        Assert.True(WindowsTweaksPage.ShouldShowWarning(day, persistedDate: null, acceptedThisProcess: null));
        Assert.True(WindowsTweaksPage.ShouldShowWarning(day, new DateOnly(2026, 10, 12), acceptedThisProcess: null));
        Assert.False(WindowsTweaksPage.ShouldShowWarning(day, day, acceptedThisProcess: null));
        Assert.False(WindowsTweaksPage.ShouldShowWarning(day, persistedDate: null, acceptedThisProcess: day));
        Assert.True(WindowsTweaksPage.ShouldShowWarning(day, persistedDate: null, acceptedThisProcess: new DateOnly(2026, 10, 12)));
    }

    [Fact]
    public void Mixed_menu_state_has_an_explicit_actionable_label()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "WindowsTweaksPage.xaml.cs"));

        Assert.Contains("FrontendWindowsTweakState.Mixed => \"Mixed — turn On to apply both Faster Menus values\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Safety_notice_gates_controls_and_os_mutation_is_read_back_not_optimistic()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "WindowsTweaksPage.xaml.cs"));
        var warningAccepted = page.IndexOf("if (!await ShowDailyWarningAsync()) return;", StringComparison.Ordinal);
        var acknowledge = page.IndexOf("AcknowledgeWindowsTweaksWarningAsync(today)", StringComparison.Ordinal);
        var controlsShown = page.IndexOf("TweaksPanel.Visibility = Visibility.Visible;", StringComparison.Ordinal);

        Assert.True(warningAccepted >= 0 && acknowledge > warningAccepted && controlsShown > acknowledge);
        Assert.Contains("toggle.IsOn = configured", page, StringComparison.Ordinal);
        Assert.Contains("toggle.IsOn = current.ConfiguredEnabled.Value", page, StringComparison.Ordinal);
        Assert.Contains("ApplySnapshot(result.Snapshot)", page, StringComparison.Ordinal);
        Assert.Contains("TryCaptureAfterMutationFailureAsync", page, StringComparison.Ordinal);
        Assert.Contains("ApplySnapshot(_snapshot)", page, StringComparison.Ordinal);
        Assert.Contains("HashSet<FrontendWindowsTweakId> _mutatingIds", page, StringComparison.Ordinal);
        Assert.Contains("!_mutatingIds.Contains(definition.Id)", page, StringComparison.Ordinal);
        Assert.Contains("_mutatingIds.Count > 0", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Restart-Computer", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Apply changes", File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "WindowsTweaksPage.xaml")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Opening_tweaks_does_not_skip_shortcut_page_deactivation()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "MainWindow.xaml.cs"));
        mainWindow = mainWindow.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains(
            "if (page == MainNavigationPage.Shortcut) ShortcutContent.Activate();\n        else if (wasShortcut) ShortcutContent.Deactivate();\n        if (page == MainNavigationPage.Tweaks) TweaksContent.Activate();",
            mainWindow,
            StringComparison.Ordinal);
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
