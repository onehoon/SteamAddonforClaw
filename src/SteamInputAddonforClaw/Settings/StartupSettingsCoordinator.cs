using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.Install;

namespace SteamInputAddonforClaw.Settings;

public sealed class StartupSettingsCoordinator : ISteamInputRoutingPreference, IOem1MappingPreference
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
    public bool SteamInputRoutingEnabled => Settings.SteamInputRoutingEnabled;
    public bool SuppressDeveloperMenuWarning => Settings.SuppressDeveloperMenuWarning;
    public Oem1MappingSettings Oem1Mapping => Settings.Oem1Mapping;
    public event EventHandler? SteamInputRoutingEnabledChanged;
    public event EventHandler? Oem1MappingChanged;

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

    public void SuppressDeveloperMenuWarningPermanently()
    {
        if (Settings.SuppressDeveloperMenuWarning) return;
        var next = Settings with { SuppressDeveloperMenuWarning = true };
        _settingsStore.Save(next);
        Settings = next;
    }

    public StartupRegistrationResult Repair() => _startupManager.Synchronize(Settings.LaunchAtWindowsStartup);

    internal void RefreshCenterMAutoRunOwnershipFromDisk()
    {
        var persisted = _settingsStore.Load();
        Settings = Settings with
        {
            CenterMAutoRunOwnedByAddon = persisted.CenterMAutoRunOwnedByAddon,
            CenterMAutoRunMutationPending = persisted.CenterMAutoRunMutationPending,
            OriginalAutoRun = persisted.OriginalAutoRun,
            AppliedAutoRun = persisted.AppliedAutoRun
        };
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
