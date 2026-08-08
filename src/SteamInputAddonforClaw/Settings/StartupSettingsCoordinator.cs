using SteamInputAddonforClaw.Install;

namespace SteamInputAddonforClaw.Settings;

public sealed class StartupSettingsCoordinator
{
    private readonly SettingsStore _settingsStore;
    private readonly IWindowsStartupManager _startupManager;

    public StartupSettingsCoordinator(AppSettings settings, SettingsStore settingsStore, IWindowsStartupManager startupManager)
    {
        Settings = settings;
        _settingsStore = settingsStore;
        _startupManager = startupManager;
    }

    public AppSettings Settings { get; private set; }

    public StartupRegistrationResult ChangeLaunchAtWindowsStartup(bool enabled)
    {
        Settings = Settings with { LaunchAtWindowsStartup = enabled };
        _settingsStore.Save(Settings);
        return _startupManager.Synchronize(enabled);
    }

    public StartupRegistrationResult Repair() => _startupManager.Synchronize(Settings.LaunchAtWindowsStartup);
}
