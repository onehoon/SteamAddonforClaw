using System.Xml.Linq;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SteamVirtualControllerUiTests
{
    [Fact]
    public void Global_selector_is_a_native_expander_with_a_two_choice_header_and_nested_gyro_card()
    {
        var controllerXaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "ControllerPage.xaml");
        var settingsXaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml");
        var controllerDocument = XDocument.Parse(controllerXaml);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var title = controllerXaml.IndexOf("Text=\"Controller\"", StringComparison.Ordinal);
        var controllerCode = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "ControllerPage.xaml.cs");
        var expander = Assert.Single(controllerDocument.Descendants(), element => element.Name.LocalName == "SettingsExpander"
            && (string?)element.Attribute(x + "Name") == "SteamVirtualControllerExpander");
        var unavailable = controllerXaml.IndexOf("x:Name=\"MappingUnavailableText\"", StringComparison.Ordinal);
        var mapping = controllerXaml.IndexOf("x:Name=\"MappingContent\"", StringComparison.Ordinal);

        Assert.True(title >= 0 && unavailable > controllerXaml.IndexOf("SteamVirtualControllerExpander", StringComparison.Ordinal) && mapping > unavailable);
        Assert.Equal("Controller for Steam Game", (string?)expander.Attribute("Header"));
        Assert.Equal("Controller_SteamVirtualControllerExpander", (string?)expander.Attribute(x + "Uid"));
        Assert.Equal("True", (string?)expander.Attribute("IsExpanded"));
        Assert.Contains("Text=\"Choose the controller used by Steam and configure gyroscope input.\"", controllerXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Controller_SteamControllerRow", controllerXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Controller_GyroscopeRow", controllerXaml, StringComparison.Ordinal);
        Assert.Contains("Controller_GyroscopeToggle", controllerXaml, StringComparison.Ordinal);
        Assert.Contains("SteamVirtualControllerActivityText", controllerXaml, StringComparison.Ordinal);
        var headerContent = Assert.Single(expander.Elements(), element => element.Name.LocalName == "Grid");
        var comboBox = Assert.Single(headerContent.Elements(), element => element.Name.LocalName == "ComboBox");
        var unavailableText = Assert.Single(headerContent.Elements(), element => element.Name.LocalName == "TextBlock"
            && (string?)element.Attribute(x + "Name") == "SteamVirtualControllerUnavailableText");
        Assert.Equal("Controller_SteamControllerUnavailable", (string?)unavailableText.Attribute(x + "Uid"));
        Assert.Equal("Collapsed", (string?)unavailableText.Attribute("Visibility"));
        var options = comboBox.Elements().Where(element => element.Name.LocalName == "ComboBoxItem").ToArray();
        Assert.Equal(2, options.Length);
        var steamDeckOption = Assert.Single(options, element => (string?)element.Attribute("Tag") == "SteamDeck");
        var dualSenseOption = Assert.Single(options, element => (string?)element.Attribute("Tag") == "DualSenseEdge");
        Assert.Equal("Steam Deck", (string?)steamDeckOption.Attribute("Content"));
        Assert.Equal("DualSense Edge", (string?)dualSenseOption.Attribute("Content"));
        Assert.Equal("Controller_SteamDeckOption", (string?)steamDeckOption.Attribute(x + "Uid"));
        Assert.Equal("Controller_DualSenseEdgeOption", (string?)dualSenseOption.Attribute(x + "Uid"));
        Assert.DoesNotContain(options, element => (string?)element.Attribute("Tag") == "Unavailable");
        var gyroItems = Assert.Single(expander.Elements(), element => element.Name.LocalName == "SettingsExpander.Items");
        var gyroCard = Assert.Single(gyroItems.Elements(), element => element.Name.LocalName == "SettingsCard");
        Assert.Equal("Controller_GyroscopeCard", (string?)gyroCard.Attribute(x + "Uid"));
        Assert.Equal("Gyroscope", (string?)gyroCard.Attribute("Header"));
        var gyroToggle = Assert.Single(gyroCard.Elements(), element => element.Name.LocalName == "ToggleSwitch"
            && (string?)element.Attribute(x + "Name") == "GyroscopeEnabledToggle");
        Assert.Equal("False", (string?)gyroToggle.Attribute("IsOn"));
        Assert.Contains("SteamVirtualControllerComboBox.Visibility = available ? Visibility.Visible : Visibility.Collapsed", controllerCode, StringComparison.Ordinal);
        Assert.Contains("SteamVirtualControllerUnavailableText.Visibility = available ? Visibility.Collapsed : Visibility.Visible", controllerCode, StringComparison.Ordinal);
        Assert.Contains("SteamVirtualControllerComboBox.IsEnabled = CanEditSteamVirtualController", controllerCode, StringComparison.Ordinal);
        Assert.Contains("GyroscopeEnabledToggle.IsEnabled = Volatile.Read(ref _gyroscopeMutationInProgress) == 0", controllerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("SteamVirtualControllerExpander.IsEnabled", controllerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("SteamVirtualController", settingsXaml, StringComparison.Ordinal);
        foreach (var culture in new[] { "en-US", "ko-KR" })
        {
            var resources = ReadSource("src", "SteamInputAddonforClaw.UI", "Strings", culture, "Resources.resw");
            Assert.DoesNotContain("Steam Deck (Default)", resources, StringComparison.Ordinal);
            Assert.DoesNotContain("Controller_SteamControllerRow.Text", resources, StringComparison.Ordinal);
            Assert.DoesNotContain("Controller_GyroscopeRow.Text", resources, StringComparison.Ordinal);
            Assert.Contains("Controller_SteamVirtualControllerExpander.Header", resources, StringComparison.Ordinal);
            Assert.Contains("Controller_GyroscopeCard.Header", resources, StringComparison.Ordinal);
            Assert.Contains("Controller_SteamControllerUnavailable.Text", resources, StringComparison.Ordinal);
            Assert.DoesNotContain("Controller_SteamControllerUnavailable.Content", resources, StringComparison.Ordinal);
            var resourceData = XDocument.Parse(resources).Descendants("data")
                .ToDictionary(element => (string)element.Attribute("name")!, element => (string?)element.Element("value"));
            Assert.Equal(culture == "en-US" ? "Controller for Steam Game" : "Steam 게임용 컨트롤러",
                resourceData["Controller_SteamVirtualControllerExpander.Header"]);
            Assert.Equal(culture == "en-US" ? "Gyroscope" : "자이로스코프", resourceData["Controller_GyroscopeCard.Header"]);
            Assert.Equal(culture == "en-US" ? "Unavailable" : "사용할 수 없음",
                resourceData["Controller_SteamControllerUnavailable.Text"]);
            Assert.Equal("Steam Deck", resourceData["Controller_SteamDeckOption.Content"]);
            Assert.Equal("DualSense Edge", resourceData["Controller_DualSenseEdgeOption.Content"]);
        }
    }

    [Fact]
    public void Global_selector_uses_the_runtime_projection_and_gyro_mutation_is_independent()
    {
        var idle = new FrontendControllerForSteamSnapshot(true, SteamVirtualControllerPreference.SteamDeck, null, true);
        var activeGameProfileOverride = new FrontendControllerForSteamSnapshot(true, SteamVirtualControllerPreference.SteamDeck, SteamVirtualControllerPreference.DualSenseEdge, false);
        var unavailable = FrontendControllerForSteamSnapshot.Unavailable(SteamVirtualControllerPreference.SteamDeck);
        Assert.True(ControllerPage.CanEditSteamVirtualController(idle, mutationInProgress: false));
        Assert.False(ControllerPage.CanEditSteamVirtualController(activeGameProfileOverride, mutationInProgress: false));
        Assert.False(ControllerPage.CanEditSteamVirtualController(unavailable, mutationInProgress: false));
        Assert.False(ControllerPage.CanEditSteamVirtualController(idle, mutationInProgress: true));

        var controller = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "ControllerPage.xaml.cs");
        Assert.Contains("_frontend.SetSteamVirtualControllerAsync(requested)", controller, StringComparison.Ordinal);
        Assert.Contains("settings.ControllerForSteam", controller, StringComparison.Ordinal);
        Assert.Contains("settings.SteamVirtualController", controller, StringComparison.Ordinal);
        Assert.Contains("GetBootstrapAsync()", controller, StringComparison.Ordinal);
        Assert.Contains("_frontend.SetGyroscopeEnabledAsync(requested)", controller, StringComparison.Ordinal);
        Assert.Contains("ShowGyroscopeUpdateFailure()", controller, StringComparison.Ordinal);
        Assert.Contains("confirmedValue ?? _lastKnownGyroscopeEnabled", controller, StringComparison.Ordinal);
        Assert.Contains("_frontend.StateInvalidated += OnFrontendStateInvalidated", controller, StringComparison.Ordinal);
        Assert.Contains("_ = RefreshControllerForSteamAsync()", controller, StringComparison.Ordinal);
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
    public void Main_window_materializes_and_refreshes_the_runtime_owned_controller_projection()
    {
        var mainWindow = ReadSource("src", "SteamInputAddonforClaw.UI", "MainWindow.xaml.cs");
        Assert.Contains("page.ApplyControllerForSteamSettings(_bootstrap.Settings)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_controllerContent!.Activate()", mainWindow, StringComparison.Ordinal);
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
            Assert.Contains("Controller → Controller for Steam Game", guide, StringComparison.Ordinal);
            Assert.Contains("Use default", guide, StringComparison.Ordinal);
            Assert.Contains("Gyroscope", guide, StringComparison.Ordinal);
            Assert.Contains("Quick Settings Overlay", guide, StringComparison.Ordinal);
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
