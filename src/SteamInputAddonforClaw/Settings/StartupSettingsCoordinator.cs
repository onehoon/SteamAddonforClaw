using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.Contracts.Overlay;
using SteamInputAddonforClaw.Contracts.Wing;
using SteamInputAddonforClaw.Install;

namespace SteamInputAddonforClaw.Settings;

public sealed class StartupSettingsCoordinator : IOem1MappingPreference, IWingMappingPreference
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

    public bool SuppressDeveloperMenuWarning => Settings.SuppressDeveloperMenuWarning;
    public Oem1MappingSettings Oem1Mapping => Settings.Oem1Mapping;
    public WingMappingSettings WingMapping => Settings.WingMapping;
    public IReadOnlyList<OverlayTabId> OverlayTabOrder => Settings.OverlayTabOrder;
    public event EventHandler? Oem1MappingChanged;
    public event EventHandler? WingMappingChanged;

    /// <summary>Installed-app lifecycle infrastructure: make sure the owned Task Scheduler task
    /// exists. Background startup is not a user preference -- there is nothing to persist and no OFF
    /// request. A failed repair is returned as-is; the already-running Runtime is never intentionally
    /// exited for it.</summary>
    public StartupRegistrationResult EnsureStartupRegistration() => _startupManager.Synchronize(true);

    /// <summary>Uninstall preparation only: remove the owned startup task. Named narrowly so ordinary
    /// feature code cannot look like it can turn application startup on and off.</summary>
    public StartupRegistrationResult RemoveStartupRegistrationForUninstall() => _startupManager.Synchronize(false);

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

    /// <summary>
    /// Persists a complete new OEM1 mapping (remapping switch + four slot bindings) and notifies the
    /// runtime. Save-then-publish: a subscriber must never observe a mapping that is not already on
    /// disk.
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

    /// <summary>
    /// The narrow validated mutation seam OQ5-UI-09 will call when the Overlay requests a reorder.
    /// A valid complete order (all five tabs, each once) is normalized, persisted, then published
    /// (save-then-current, like OEM1/WING). A request equal to the current order is an accepted
    /// no-op. An invalid request is rejected: it is NOT silently converted to the default -- the
    /// user's current valid order stays exactly as it is, and nothing is written to disk.
    /// </summary>
    public bool TryChangeOverlayTabOrder(IReadOnlyList<OverlayTabId> requested)
    {
        if (!OverlayTabOrderContract.TryNormalize(requested, out var normalized))
            return false;

        if (normalized.SequenceEqual(Settings.OverlayTabOrder))
            return true;

        var next = Settings with { OverlayTabOrder = normalized };
        _settingsStore.Save(next);
        Settings = next;
        return true;
    }

    public void SuppressDeveloperMenuWarningPermanently()
    {
        if (Settings.SuppressDeveloperMenuWarning) return;
        var next = Settings with { SuppressDeveloperMenuWarning = true };
        _settingsStore.Save(next);
        Settings = next;
    }

}

/// <summary>
/// The read side of the OEM1 mapping the runtime consumes: the current mapping, captured fresh on
/// every OEM1 gesture, plus a change notification so the global remapping switch can drive the
/// existing suppression lifecycle.
/// </summary>
/// <remarks>
/// Narrow on purpose -- the routing composition receives only this, never the settings coordinator
/// itself, so an OEM1 feature can never reach any unrelated preference.
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
