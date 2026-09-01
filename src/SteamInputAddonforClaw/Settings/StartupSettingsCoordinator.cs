using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.Contracts.Wing;
using SteamInputAddonforClaw.Install;

namespace SteamInputAddonforClaw.Settings;

public sealed class StartupSettingsCoordinator : ISteamInputRoutingPreference, IOem1MappingPreference, IWingMappingPreference
{
    private readonly SettingsStore _settingsStore;
    private readonly IWindowsStartupManager _startupManager;
    private readonly Func<bool> _isLaunchAtWindowsStartupRequired;

    internal const string LaunchAtWindowsStartupRequiredMessage = "Required while MSI Center M is disabled.";

    public StartupSettingsCoordinator(AppSettings settings, SettingsStore settingsStore, IWindowsStartupManager startupManager, Func<bool>? isLaunchAtWindowsStartupRequired = null)
    {
        Settings = settings;
        _settingsStore = settingsStore;
        _startupManager = startupManager;
        _isLaunchAtWindowsStartupRequired = isLaunchAtWindowsStartupRequired ?? (() => false);
    }

    public AppSettings Settings { get; private set; }

    /// <summary>True while MSI Center M is exactly Disabled: <c>LaunchAtWindowsStartup</c> is then a
    /// mandatory-ON policy, not a user preference (PR2.5 work order section 6).</summary>
    public bool IsLaunchAtWindowsStartupRequired => _isLaunchAtWindowsStartupRequired();
    public bool SteamInputRoutingEnabled => Settings.SteamInputRoutingEnabled;
    public bool SuppressDeveloperMenuWarning => Settings.SuppressDeveloperMenuWarning;
    public Oem1MappingSettings Oem1Mapping => Settings.Oem1Mapping;
    public WingMappingSettings WingMapping => Settings.WingMapping;
    public event EventHandler? SteamInputRoutingEnabledChanged;
    public event EventHandler? Oem1MappingChanged;
    public event EventHandler? WingMappingChanged;

    public StartupRegistrationResult ChangeLaunchAtWindowsStartup(bool enabled)
    {
        // Section 6.4: while mandatory, a request to turn startup OFF must never persist false or
        // delete the owned task. Prove/repair the required task and report why -- but do NOT persist
        // false first and only then discover the mandatory policy.
        if (!enabled && _isLaunchAtWindowsStartupRequired())
        {
            if (!Settings.LaunchAtWindowsStartup)
            {
                Settings = Settings with { LaunchAtWindowsStartup = true };
                _settingsStore.Save(Settings);
            }
            var repair = _startupManager.Synchronize(true);
            return repair.Success ? new StartupRegistrationResult(true, LaunchAtWindowsStartupRequiredMessage) : repair;
        }

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

    public void ChangeSteamInputRoutingEnabled(bool enabled)
    {
        if (Settings.SteamInputRoutingEnabled == enabled) return;
        var next = Settings with { SteamInputRoutingEnabled = enabled };
        _settingsStore.Save(next);
        Settings = next;
        SteamInputRoutingEnabledChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Persists a complete new OEM1 mapping (remapping switch + four slot bindings) and notifies the
    /// runtime. Save-then-publish, exactly like <see cref="ChangeSteamInputRoutingEnabled"/>: a
    /// subscriber must never observe a mapping that is not already on disk.
    /// </summary>
    /// <remarks>
    /// Takes the whole record rather than per-slot mutators so there is one write path and the
    /// "turning remapping off never erases the bindings" guarantee is structural -- the caller sends
    /// back the same bindings it was given with only <c>RemappingEnabled</c> changed.
    /// </remarks>
    public void ChangeOem1Mapping(Oem1MappingSettings mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (Settings.Oem1Mapping == mapping) return;
        var next = Settings with { Oem1Mapping = mapping };
        _settingsStore.Save(next);
        Settings = next;
        Oem1MappingChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ChangeWingMapping(WingMappingSettings mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (Settings.WingMapping == mapping) return;
        var next = Settings with { WingMapping = mapping };
        _settingsStore.Save(next);
        Settings = next;
        WingMappingChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SuppressDeveloperMenuWarningPermanently()
    {
        if (Settings.SuppressDeveloperMenuWarning) return;
        var next = Settings with { SuppressDeveloperMenuWarning = true };
        _settingsStore.Save(next);
        Settings = next;
    }

    /// <summary>At Runtime startup: synchronize the owned Task Scheduler task. When Center M is
    /// exactly Disabled the effective desired state is forced ON and a saved <c>false</c> is
    /// converged to <c>true</c>, so a machine that had Center M disabled before this architecture
    /// existed cannot stay in the unsupported "Disabled + startup off" state (section 6.3). A failed
    /// repair is returned as-is -- the already-running Runtime is never intentionally exited for it
    /// (section 6.5).</summary>
    public StartupRegistrationResult Repair()
    {
        var required = _isLaunchAtWindowsStartupRequired();
        if (required && !Settings.LaunchAtWindowsStartup)
        {
            Settings = Settings with { LaunchAtWindowsStartup = true };
            _settingsStore.Save(Settings);
        }
        return _startupManager.Synchronize(required || Settings.LaunchAtWindowsStartup);
    }
}

public interface ISteamInputRoutingPreference
{
    bool SteamInputRoutingEnabled { get; }
    event EventHandler? SteamInputRoutingEnabledChanged;
}

/// <summary>
/// The read side of the OEM1 mapping the runtime consumes: the current mapping, captured fresh on
/// every OEM1 gesture, plus a change notification so the global remapping switch can drive the
/// existing suppression lifecycle.
/// </summary>
/// <remarks>
/// Narrow on purpose -- the routing composition receives only this, never the settings coordinator
/// itself, so an OEM1 feature can never reach any unrelated preference. Mirrors the shape
/// <see cref="ISteamInputRoutingPreference"/> already established for the routing master switch.
/// </remarks>
public interface IOem1MappingPreference
{
    Oem1MappingSettings Oem1Mapping { get; }
    event EventHandler? Oem1MappingChanged;
}

public interface IWingMappingPreference
{
    WingMappingSettings WingMapping { get; }
    event EventHandler? WingMappingChanged;
}
