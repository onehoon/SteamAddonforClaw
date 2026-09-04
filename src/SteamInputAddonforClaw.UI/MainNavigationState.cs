namespace SteamInputAddonforClaw.Windowing;

internal enum MainNavigationPage
{
    Device,
    Profile,
    Controller,
    HowToUse,
    Settings,
    DeveloperMenu,
    ClawSensorProbe,
    CenterMButton,
    VibrationTest,
    FanHardwareProbe,
}

internal sealed class MainNavigationState
{
    internal MainNavigationPage CurrentPage { get; private set; } = MainNavigationPage.Device;

    internal MainNavigationPage SelectNavigationItem(bool isSettingsSelected, string? selectedTag)
    {
        CurrentPage = isSettingsSelected
            ? MainNavigationPage.Settings
            : selectedTag switch
            {
                "Device" => MainNavigationPage.Device,
                "Profile" => MainNavigationPage.Profile,
                "Controller" => MainNavigationPage.Controller,
                "HowToUse" => MainNavigationPage.HowToUse,
                _ => MainNavigationPage.Device
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

    /// <summary>The Center M Button detail page is a child of Controller, exactly as the Developer
    /// Menu is a child of Settings. It stays reachable regardless of whether remapping is switched
    /// on -- turning the feature off must never hide the user's saved mappings.</summary>
    internal MainNavigationPage OpenCenterMButton() => CurrentPage = MainNavigationPage.CenterMButton;

    internal MainNavigationPage ReturnToController() => CurrentPage = MainNavigationPage.Controller;

    internal MainNavigationPage OpenClawSensorProbe() => CurrentPage = MainNavigationPage.ClawSensorProbe;
    internal MainNavigationPage OpenVibrationTest() => CurrentPage = MainNavigationPage.VibrationTest;
    internal MainNavigationPage OpenFanHardwareProbe() => CurrentPage = MainNavigationPage.FanHardwareProbe;

    internal MainNavigationPage? GetMouseBackDestination() => CurrentPage switch
    {
        MainNavigationPage.DeveloperMenu => MainNavigationPage.Settings,
        MainNavigationPage.ClawSensorProbe => MainNavigationPage.DeveloperMenu,
        MainNavigationPage.CenterMButton => MainNavigationPage.Controller,
        MainNavigationPage.VibrationTest => MainNavigationPage.DeveloperMenu,
        MainNavigationPage.FanHardwareProbe => MainNavigationPage.DeveloperMenu,
        _ => null
    };
}
