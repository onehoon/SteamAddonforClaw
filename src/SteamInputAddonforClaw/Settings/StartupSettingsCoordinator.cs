using SteamInputAddonforClaw.Install;

namespace SteamInputAddonforClaw.Settings;

public sealed class StartupSettingsCoordinator : ISteamBigPictureRoutingPreference
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
    public bool RouteInSteamBigPicture => Settings.RouteInSteamBigPicture;
    public bool SuppressDeveloperMenuWarning => Settings.SuppressDeveloperMenuWarning;
    public event EventHandler? RouteInSteamBigPictureChanged;

    public StartupRegistrationResult ChangeLaunchAtWindowsStartup(bool enabled)
    {
        Settings = Settings with { LaunchAtWindowsStartup = enabled };
        _settingsStore.Save(Settings);
        return _startupManager.Synchronize(enabled);
    }

    public void ChangeLogLevel(AppLogPreference level)
    {
        var previous = Settings.LogLevel;
        Settings = Settings with { LogLevel = level };
        _settingsStore.Save(Settings);
        SteamInputAddonforClaw.Diagnostics.AppLog.MinimumLevelOverride = AppSettingsPolicy.ToAppLogLevel(Settings.LogLevel);
        // Silent when the new level is Off: the message would be filtered anyway, and logging nothing
        // is the whole point of turning it off.
        SteamInputAddonforClaw.Diagnostics.AppLog.Info("Settings", "Log level changed.", ("Previous", previous), ("Current", Settings.LogLevel));
    }

    public void ChangeRouteInSteamBigPicture(bool enabled)
    {
        if (Settings.RouteInSteamBigPicture == enabled) return;
        var next = Settings with { RouteInSteamBigPicture = enabled };
        _settingsStore.Save(next);
        Settings = next;
        RouteInSteamBigPictureChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SuppressDeveloperMenuWarningPermanently()
    {
        if (Settings.SuppressDeveloperMenuWarning) return;
        var next = Settings with { SuppressDeveloperMenuWarning = true };
        _settingsStore.Save(next);
        Settings = next;
    }

    public StartupRegistrationResult Repair() => _startupManager.Synchronize(Settings.LaunchAtWindowsStartup);
}

public interface ISteamBigPictureRoutingPreference
{
    bool RouteInSteamBigPicture { get; }
    event EventHandler? RouteInSteamBigPictureChanged;
}
