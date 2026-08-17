namespace SteamInputAddonforClaw.Windowing;

internal enum MainNavigationPage
{
    Status,
    Controller,
    HowToUse,
    Settings,
    DeveloperMenu,
    ClawSensorProbe,
}

internal sealed class MainNavigationState
{
    internal MainNavigationPage CurrentPage { get; private set; } = MainNavigationPage.Status;

    internal MainNavigationPage SelectNavigationItem(bool isSettingsSelected, string? selectedTag)
    {
        CurrentPage = isSettingsSelected
            ? MainNavigationPage.Settings
            : selectedTag switch
            {
                "Controller" => MainNavigationPage.Controller,
                "HowToUse" => MainNavigationPage.HowToUse,
                _ => MainNavigationPage.Status
            };

        return CurrentPage;
    }

    internal MainNavigationPage OpenDeveloperMenu()
    {
        CurrentPage = MainNavigationPage.DeveloperMenu;
        return CurrentPage;
    }

    internal MainNavigationPage ReturnToSettings()
    {
        CurrentPage = MainNavigationPage.Settings;
        return CurrentPage;
    }

    internal MainNavigationPage ReturnToDeveloperMenu() => CurrentPage = MainNavigationPage.DeveloperMenu;

    internal MainNavigationPage OpenClawSensorProbe() => CurrentPage = MainNavigationPage.ClawSensorProbe;

    internal MainNavigationPage? GetMouseBackDestination() => CurrentPage switch
    {
        MainNavigationPage.DeveloperMenu => MainNavigationPage.Settings,
        MainNavigationPage.ClawSensorProbe => MainNavigationPage.DeveloperMenu,
        _ => null
    };
}
