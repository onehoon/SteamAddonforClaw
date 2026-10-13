using System.Xml.Linq;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SteamVirtualControllerUiTests
{
    [Fact]
    public void Global_selector_is_at_the_top_of_Controller_with_a_two_line_description()
    {
        var controllerXaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "ControllerPage.xaml");
        var settingsXaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml");
        var title = controllerXaml.IndexOf("Text=\"Controller\"", StringComparison.Ordinal);
        var card = controllerXaml.IndexOf("x:Name=\"SteamVirtualControllerCard\"", StringComparison.Ordinal);
        var unavailable = controllerXaml.IndexOf("x:Name=\"MappingUnavailableText\"", StringComparison.Ordinal);
        var mapping = controllerXaml.IndexOf("x:Name=\"MappingContent\"", StringComparison.Ordinal);

        Assert.True(title >= 0 && card > title && unavailable > card && mapping > card);
        Assert.Contains("Header=\"Steam Virtual Controller\"", controllerXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Choose the virtual controller used by Steam.\"", controllerXaml, StringComparison.Ordinal);
        Assert.Contains("SteamVirtualControllerActivityText", controllerXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Steam Deck (Default)\" Tag=\"SteamDeck\"", controllerXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"DualSense Edge\" Tag=\"DualSenseEdge\"", controllerXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SteamVirtualController", settingsXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Global_selector_requires_known_idle_Steam_state_and_no_pending_save()
    {
        Assert.True(ControllerPage.CanEditSteamVirtualController(false, mutationInProgress: false));
        Assert.False(ControllerPage.CanEditSteamVirtualController(true, mutationInProgress: false));
        Assert.False(ControllerPage.CanEditSteamVirtualController(null, mutationInProgress: false));
        Assert.False(ControllerPage.CanEditSteamVirtualController(false, mutationInProgress: true));

        var controller = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "ControllerPage.xaml.cs");
        Assert.Contains("_frontend.SetSteamVirtualControllerAsync(requested)", controller, StringComparison.Ordinal);
        Assert.Contains("settings.SteamVirtualController", controller, StringComparison.Ordinal);
        Assert.Contains("GetBootstrapAsync()", controller, StringComparison.Ordinal);
        Assert.Contains("SteamActivityRefreshRequested?.Invoke", controller, StringComparison.Ordinal);
        Assert.Contains("_steamActive = null", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void Steam_profile_selector_is_first_and_uses_the_existing_profile_mutation_contract()
    {
        var profileXaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "ProfilePage.xaml");
        var xboxXaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "XboxPage.xaml");
        var overlayXaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "OverlayPage.xaml");
        var profileCode = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "ProfilePage.xaml.cs");
        var selector = profileXaml.IndexOf("x:Name=\"SteamVirtualControllerComboBox\"", StringComparison.Ordinal);
        var tdp = profileXaml.IndexOf("x:Name=\"TdpExpander\"", StringComparison.Ordinal);
        var profileDocument = XDocument.Parse(profileXaml);
        var controllerCard = Assert.Single(profileDocument.Descendants(), element =>
            element.Name.LocalName == "SettingsCard" &&
            (string?)element.Attribute("Header") == "Virtual Controller");
        var headerIcon = Assert.Single(controllerCard.Elements(), element =>
            element.Name.LocalName == "SettingsCard.HeaderIcon");
        var controllerGlyph = Assert.Single(headerIcon.Elements(), element =>
            element.Name.LocalName == "FontIcon");

        Assert.True(selector >= 0 && tdp > selector);
        Assert.Equal("\uE7FC", (string?)controllerGlyph.Attribute("Glyph"));
        Assert.Contains("Header=\"Virtual Controller\"", profileXaml, StringComparison.Ordinal);
        Assert.Contains("Description=\"Overrides the Controller default for this Steam game.\"", profileXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Use default\" Tag=\"Default\"", profileXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Steam Deck\" Tag=\"SteamDeck\"", profileXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"DualSense Edge\" Tag=\"DualSenseEdge\"", profileXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SteamVirtualControllerComboBox", xboxXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SteamVirtualControllerComboBox", overlayXaml, StringComparison.Ordinal);
        Assert.Contains("\"Default\" => null", profileCode, StringComparison.Ordinal);
        Assert.Contains("_frontend.SetGameProfileSteamVirtualControllerAsync(appId, requested)", profileCode, StringComparison.Ordinal);
        Assert.Contains("RenderControllerMutationSnapshot(result.Snapshot)", profileCode, StringComparison.Ordinal);
        Assert.Contains("RestoreSelectedAfterMutationFailureAsync(", profileCode, StringComparison.Ordinal);
        Assert.Contains("preservePendingProfileDrafts: true", profileCode, StringComparison.Ordinal);
        Assert.Contains("SteamActivityRefreshRequested?.Invoke", profileCode, StringComparison.Ordinal);
        Assert.Contains("CanEditGameSteamVirtualController(_steamActive, _snapshot", profileCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Steam_profile_controller_choice_requires_enabled_writable_profile_and_known_idle_status()
    {
        var enabled = new FrontendGameProfileSnapshot(42, null, true, true, null!, null!, true, null);
        var disabled = enabled with { Enabled = false };
        var missing = enabled with { Exists = false };
        var readOnly = enabled with { PersistenceWritable = false };

        Assert.True(ProfilePage.CanEditGameSteamVirtualController(false, enabled, mutationInProgress: false));
        Assert.False(ProfilePage.CanEditGameSteamVirtualController(true, enabled, mutationInProgress: false));
        Assert.False(ProfilePage.CanEditGameSteamVirtualController(null, enabled, mutationInProgress: false));
        Assert.False(ProfilePage.CanEditGameSteamVirtualController(false, disabled, mutationInProgress: false));
        Assert.False(ProfilePage.CanEditGameSteamVirtualController(false, missing, mutationInProgress: false));
        Assert.False(ProfilePage.CanEditGameSteamVirtualController(false, readOnly, mutationInProgress: false));
        Assert.False(ProfilePage.CanEditGameSteamVirtualController(false, enabled, mutationInProgress: true));

        var profileCode = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "ProfilePage.xaml.cs");
        Assert.Contains("SteamVirtualControllerComboBox.SelectedItem = null", profileCode, StringComparison.Ordinal);
        Assert.Contains("RenderSteamVirtualController(snapshot.SteamVirtualController)", profileCode, StringComparison.Ordinal);
        Assert.Contains("steamActive == false", profileCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_propagates_status_and_keeps_both_selectors_locked_when_status_is_unknown()
    {
        var mainWindow = ReadSource("src", "SteamInputAddonforClaw.UI", "MainWindow.xaml.cs");
        var renderStart = mainWindow.IndexOf("private void RenderSystemStatus(FrontendStatusSnapshot snapshot)", StringComparison.Ordinal);
        var renderEnd = mainWindow.IndexOf("private void RequestPrerequisiteSetupActivation()", renderStart, StringComparison.Ordinal);
        Assert.True(renderStart >= 0 && renderEnd > renderStart);
        var render = mainWindow[renderStart..renderEnd];
        Assert.Contains("_controllerContent?.SetSteamActivity(snapshot.Steam.Active)", render, StringComparison.Ordinal);
        Assert.Contains("_profileContent?.SetSteamActivity(snapshot.Steam.Active)", render, StringComparison.Ordinal);

        var refreshStart = mainWindow.IndexOf("private async Task RefreshSystemStatusAsync()", StringComparison.Ordinal);
        var refreshEnd = mainWindow.IndexOf("private void RenderSystemStatus(", refreshStart, StringComparison.Ordinal);
        Assert.True(refreshStart >= 0 && refreshEnd > refreshStart);
        var refresh = mainWindow[refreshStart..refreshEnd];
        Assert.Contains("_controllerContent?.SetSteamActivity(null)", refresh, StringComparison.Ordinal);
        Assert.Contains("_profileContent?.SetSteamActivity(null)", refresh, StringComparison.Ordinal);
        Assert.Contains("_controllerContent.Activate()", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_profileContent.Activate()", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_ = RefreshSystemStatusAsync()", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_guides_use_the_new_selector_and_remove_stale_experimental_or_Settings_paths()
    {
        var english = ReadSource("README.md");
        var korean = ReadSource("docs", "howtouse", "README_KO.md");
        foreach (var guide in new[] { english, korean })
        {
            Assert.DoesNotContain("Experimental", guide, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("실험", guide, StringComparison.Ordinal);
            Assert.DoesNotContain("Settings → Steam / Big Picture Controller", guide, StringComparison.Ordinal);
            Assert.DoesNotContain("#### Steam / Big Picture Controller", guide, StringComparison.Ordinal);
            Assert.Contains("Controller → Steam Virtual Controller", guide, StringComparison.Ordinal);
            Assert.Contains("Use default", guide, StringComparison.Ordinal);
        }
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
