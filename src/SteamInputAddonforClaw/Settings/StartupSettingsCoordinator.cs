using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.Contracts.Overlay;
using SteamInputAddonforClaw.Install;

namespace SteamInputAddonforClaw.Settings;

public sealed class StartupSettingsCoordinator : IFrontButtonMappingPreference
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
    public FrontButtonMappingSettings FrontButtonMapping => Settings.FrontButtonMapping;
    public IReadOnlyList<OverlayTabId> OverlayTabOrder => Settings.OverlayTabOrder;
    public event EventHandler? FrontButtonMappingChanged;

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
    /// The one validated mutation for the whole front-button mapping (App UI PR-C). Save-then-publish:
    /// a subscriber must never observe a mapping that is not already on disk. An invalid candidate --
    /// incomplete, unknown action, domain-invalid action, or a same-domain duplicate -- is REJECTED:
    /// nothing is written, current state is unchanged, and no changed event fires. The candidate is
    /// never silently repaired into a different action pair. Returns whether the mutation was applied
    /// (a candidate equal to the current mapping is an accepted no-op).
    /// </summary>
    public bool ChangeFrontButtonMapping(FrontButtonMappingSettings mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        var reason = FrontButtonMappingValidation.Validate(mapping);
        if (reason is not null)
        {
            SteamInputAddonforClaw.Diagnostics.AppLog.Warn("Settings", "Rejected an invalid front-button mapping candidate.", null, ("Reason", reason));
            return false;
        }

        if (Settings.FrontButtonMapping == mapping) return true;

        var next = Settings with { FrontButtonMapping = mapping };
        _settingsStore.Save(next);
        Settings = next;
        FrontButtonMappingChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// The narrow validated mutation seam OQ5-UI-09 will call when the Overlay requests a reorder.
    /// A valid complete order (all five tabs, each once) is normalized, persisted, then published
    /// (save-then-current, like FrontButtonMapping). A request equal to the current order is an
    /// accepted no-op. An invalid request is rejected: it is NOT silently converted to the default --
    /// the user's current valid order stays exactly as it is, and nothing is written to disk.
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
/// The read side of the front-button mapping the runtime consumes: the current mapping, captured
/// fresh on every physical button dispatch, plus a change notification. Narrow on purpose -- the
/// front-button runtime receives only this, never the settings coordinator itself.
/// </summary>
public interface IFrontButtonMappingPreference
{
    FrontButtonMappingSettings FrontButtonMapping { get; }
    event EventHandler? FrontButtonMappingChanged;
}
