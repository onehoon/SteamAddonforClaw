using System.Xml.Linq;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SteamVirtualControllerUiTests
{
    [Fact]
    public void Global_controller_choices_keep_semantic_values_and_gyroscope_controls()
    {
        var controllerXaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "ControllerPage.xaml");
        var settingsXaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml");
        var controllerDocument = XDocument.Parse(controllerXaml);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var controllerCode = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "ControllerPage.xaml.cs");
        var expander = Assert.Single(controllerDocument.Descendants(), element => element.Name.LocalName == "SettingsExpander"
            && (string?)element.Attribute(x + "Name") == "SteamVirtualControllerExpander");
        var comboBox = Assert.Single(expander.Descendants(), element => element.Name.LocalName == "ComboBox"
            && (string?)element.Attribute(x + "Name") == "SteamVirtualControllerComboBox");
        var unavailableText = Assert.Single(expander.Descendants(), element => element.Name.LocalName == "TextBlock"
            && (string?)element.Attribute(x + "Name") == "SteamVirtualControllerUnavailableText");
        var options = comboBox.Elements().Where(element => element.Name.LocalName == "ComboBoxItem").ToArray();
        Assert.Equal(2, options.Length);
        var steamDeckOption = Assert.Single(options, element => (string?)element.Attribute("Tag") == "SteamDeck");
        var dualSenseOption = Assert.Single(options, element => (string?)element.Attribute("Tag") == "DualSenseEdge");
        Assert.DoesNotContain(options, element => (string?)element.Attribute("Tag") == "Unavailable");
        var gyroItems = Assert.Single(expander.Elements(), element => element.Name.LocalName == "SettingsExpander.Items");
        var gyroCard = Assert.Single(gyroItems.Elements(), element => element.Name.LocalName == "SettingsCard");
        var gyroToggle = Assert.Single(gyroCard.Elements(), element => element.Name.LocalName == "ToggleSwitch"
            && (string?)element.Attribute(x + "Name") == "GyroscopeEnabledToggle");
        Assert.Contains("SteamVirtualControllerComboBox.Visibility = available ? Visibility.Visible : Visibility.Collapsed", controllerCode, StringComparison.Ordinal);
        Assert.Contains("SteamVirtualControllerUnavailableText.Visibility = available ? Visibility.Collapsed : Visibility.Visible", controllerCode, StringComparison.Ordinal);
        Assert.Contains("SteamVirtualControllerComboBox.IsEnabled = CanEditSteamVirtualController", controllerCode, StringComparison.Ordinal);
        Assert.Contains("GyroscopeEnabledToggle.IsEnabled = _featureSupport.Motion && Volatile.Read(ref _gyroscopeMutationInProgress) == 0", controllerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("SteamVirtualController", settingsXaml, StringComparison.Ordinal);
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
    public void Steam_profile_controller_choice_uses_semantic_values_and_existing_mutation_contract()
    {
        var profileXaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "ProfilePage.xaml");
        var xboxXaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "XboxPage.xaml");
        var overlayXaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "OverlayPage.xaml");
        var profileCode = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "ProfilePage.xaml.cs");
        var profileDocument = XDocument.Parse(profileXaml);
        var controllerCard = Assert.Single(profileDocument.Descendants(), element =>
            element.Name.LocalName == "SettingsCard" &&
            (string?)element.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Uid") == "Steam_VirtualControllerCard");
        var controllerSelector = Assert.Single(controllerCard.Descendants(), element => element.Name.LocalName == "ComboBox"
            && (string?)element.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name") == "SteamVirtualControllerComboBox");
        var optionTags = controllerSelector.Elements().Where(element => element.Name.LocalName == "ComboBoxItem")
            .Select(element => (string?)element.Attribute("Tag"))
            .OrderBy(tag => tag, StringComparer.Ordinal);
        Assert.Equal(new[] { "Default", "DualSenseEdge", "SteamDeck" }, optionTags);
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
        Assert.Contains("_profilesContent.Activate()", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_ = RefreshSystemStatusAsync()", mainWindow, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
