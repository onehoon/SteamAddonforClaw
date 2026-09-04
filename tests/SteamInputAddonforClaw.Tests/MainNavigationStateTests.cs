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
    public void Default_page_is_device()
    {
        Assert.Equal(MainNavigationPage.Device, new MainNavigationState().CurrentPage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Status")]
    [InlineData("Unknown")]
    public void Unknown_top_level_tag_falls_back_to_device(string? tag)
    {
        var navigation = new MainNavigationState();

        Assert.Equal(MainNavigationPage.Device, navigation.SelectNavigationItem(false, tag));
    }

    [Fact]
    public void HowToUse_navigation_tag_opens_how_to_use_page()
    {
        var navigation = new MainNavigationState();

        Assert.Equal(MainNavigationPage.HowToUse, navigation.SelectNavigationItem(false, "HowToUse"));
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
    public void MouseBack_destinations_match_developer_page_hierarchy()
    {
        var navigation = new MainNavigationState();

        navigation.SelectNavigationItem(isSettingsSelected: true, selectedTag: null);
        navigation.OpenDeveloperMenu();
        Assert.Equal(MainNavigationPage.Settings, navigation.GetMouseBackDestination());

        navigation.OpenClawSensorProbe();
        Assert.Equal(MainNavigationPage.DeveloperMenu, navigation.GetMouseBackDestination());
    }

    [Fact]
    public void Device_navigation_tag_opens_device_page()
    {
        var navigation = new MainNavigationState();

        Assert.Equal(MainNavigationPage.Device, navigation.SelectNavigationItem(false, "Device"));
        Assert.Equal(MainNavigationPage.Device, navigation.CurrentPage);
    }

    [Fact]
    public void Profile_navigation_tag_opens_profile_page()
    {
        var navigation = new MainNavigationState();

        Assert.Equal(MainNavigationPage.Profile, navigation.SelectNavigationItem(false, "Profile"));
        Assert.Equal(MainNavigationPage.Profile, navigation.CurrentPage);
    }

    [Fact]
    public void Device_and_Profile_are_top_level_pages_with_no_mouse_back_destination()
    {
        // Work order PR277 section 14: Device/Profile are independent top-level pages, not child
        // detail pages of anything -- mouse-back must not treat them as one.
        var navigation = new MainNavigationState();

        navigation.SelectNavigationItem(false, "Device");
        Assert.Null(navigation.GetMouseBackDestination());

        navigation.SelectNavigationItem(false, "Profile");
        Assert.Null(navigation.GetMouseBackDestination());
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
