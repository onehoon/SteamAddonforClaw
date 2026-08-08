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
}
