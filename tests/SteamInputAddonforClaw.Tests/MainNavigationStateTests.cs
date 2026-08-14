using SteamInputAddonforClaw.Windowing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MainNavigationStateTests
{
    [Fact]
    public void DeveloperMenu_CanReturnToSettings()
    {
        var navigation = new MainNavigationState();

        navigation.SelectNavigationItem(isSettingsSelected: true, selectedTag: null);
        navigation.OpenDeveloperMenu();

        Assert.Equal(MainNavigationPage.DeveloperMenu, navigation.CurrentPage);
        Assert.Equal(MainNavigationPage.Settings, navigation.ReturnToSettings());
        Assert.Equal(MainNavigationPage.Settings, navigation.CurrentPage);
    }

    [Fact]
    public void Controller_navigation_item_opens_controller_page()
    {
        var navigation = new MainNavigationState();

        Assert.Equal(MainNavigationPage.Controller, navigation.SelectNavigationItem(false, "Controller"));
        Assert.Equal(MainNavigationPage.Controller, navigation.CurrentPage);
    }

    [Fact]
    public void Settings_navigation_item_opens_developer_menu()
    {
        var navigation = new MainNavigationState();

        navigation.SelectNavigationItem(isSettingsSelected: true, selectedTag: null);

        Assert.Equal(MainNavigationPage.DeveloperMenu, navigation.OpenDeveloperMenu());
        Assert.Equal(MainNavigationPage.DeveloperMenu, navigation.CurrentPage);
    }

    [Fact]
    public void DeveloperMenu_opens_claw_sensor_probe()
    {
        var navigation = new MainNavigationState();

        navigation.SelectNavigationItem(isSettingsSelected: true, selectedTag: null);
        navigation.OpenDeveloperMenu();

        Assert.Equal(MainNavigationPage.ClawSensorProbe, navigation.OpenClawSensorProbe());
        Assert.Equal(MainNavigationPage.ClawSensorProbe, navigation.CurrentPage);
    }

    [Fact]
    public void ClawSensorProbe_returns_to_developer_menu()
    {
        var navigation = new MainNavigationState();

        navigation.SelectNavigationItem(isSettingsSelected: true, selectedTag: null);
        navigation.OpenDeveloperMenu();
        navigation.OpenClawSensorProbe();

        Assert.Equal(MainNavigationPage.DeveloperMenu, navigation.ReturnToDeveloperMenu());
        Assert.Equal(MainNavigationPage.DeveloperMenu, navigation.CurrentPage);
    }

    [Fact]
    public void Full_settings_to_sensor_probe_round_trip_returns_to_settings()
    {
        var navigation = new MainNavigationState();

        navigation.SelectNavigationItem(isSettingsSelected: true, selectedTag: null);
        navigation.OpenDeveloperMenu();
        navigation.OpenClawSensorProbe();
        navigation.ReturnToDeveloperMenu();

        Assert.Equal(MainNavigationPage.Settings, navigation.ReturnToSettings());
        Assert.Equal(MainNavigationPage.Settings, navigation.CurrentPage);
    }
}
