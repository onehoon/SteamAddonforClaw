namespace SteamInputAddonforClaw.Windowing;

internal enum MainNavigationPage
{
    Status,
    Setup,
    HowToUse,
    Settings,
    DeveloperMenu
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

    internal MainNavigationPage OpenSetup()
    {
        CurrentPage = MainNavigationPage.Setup;
        return CurrentPage;
    }

    internal MainNavigationPage ReturnToSettings()
    {
        CurrentPage = MainNavigationPage.Settings;
        return CurrentPage;
    }
}
