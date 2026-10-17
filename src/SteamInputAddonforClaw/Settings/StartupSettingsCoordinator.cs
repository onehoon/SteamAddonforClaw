using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Contracts.ControllerLed;
using SteamInputAddonforClaw.Contracts.ControllerVibration;
using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Diagnostics;

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
    public bool ClawHudEnabled => Settings.ClawHudEnabled;
    public bool QuickSettingsCurrentPowerSourceOnly => Settings.QuickSettingsCurrentPowerSourceOnly;
    public bool ExperimentalVirtualTouchpadEnabled => Settings.ExperimentalVirtualTouchpadEnabled;
    public FrontButtonMappingSettings FrontButtonMapping => Settings.FrontButtonMapping;
    public BackButtonMappingSettings BackButtonMapping => Settings.BackButtonMapping;
    public ControllerLedSettings ControllerLed => Settings.ControllerLed;
    public ControllerVibrationSettings ControllerVibration => Settings.ControllerVibration;
    public SteamVirtualControllerPreference SteamVirtualController => Settings.SteamVirtualController;
    public bool GyroscopeEnabled => Settings.GyroscopeEnabled;
    public IReadOnlyList<AddonQuickSettingsTabId> AddonQuickSettingsTabOrder => Settings.AddonQuickSettingsTabOrder;
    public DateOnly? WindowsTweaksWarningAcknowledgedLocalDate => Settings.WindowsTweaksWarningAcknowledgedLocalDate;
    public event EventHandler? FrontButtonMappingChanged;

    /// <summary>Installed-app lifecycle infrastructure: make sure the owned Task Scheduler task
    /// exists. Background startup is not a user preference -- there is nothing to persist and no OFF
    /// request. A failed repair is returned as-is; the already-running Runtime is never intentionally
    /// exited for it.</summary>
    public StartupRegistrationResult? LatestStartupRegistration { get; private set; }

    public StartupRegistrationResult EnsureStartupRegistration()
    {
        StartupRegistrationResult result;
        try
        {
            result = _startupManager.Synchronize(true);
        }
        catch (Exception exception)
        {
            AppLog.Warn("TaskScheduler", "Startup task verification or repair failed.", exception);
            result = StartupRegistrationResult.Failed(StartupTaskFailureReason.RegistrationFailed);
        }

        LatestStartupRegistration = result;
        return result;
    }

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

        var reason = FrontButtonMappingValidation.ValidateCandidate(mapping, Settings.FrontButtonMapping);
        if (reason is not null)
        {
            SteamInputAddonforClaw.Diagnostics.AppLog.Warn("Settings", "Rejected an invalid front-button mapping candidate.", null, ("Reason", reason));
            return false;
        }

        if (Settings.FrontButtonMapping == mapping) return true;

        var next = Settings with { FrontButtonMapping = mapping };
        _settingsStore.Save(next);
        Settings = next;
        SteamInputAddonforClaw.Diagnostics.AppLog.Debug("Settings", "Front-button mapping saved.",
            ("NormalGamebar", Settings.FrontButtonMapping.Normal.Gamebar.Action),
            ("NormalCenterM", Settings.FrontButtonMapping.Normal.CenterM.Action),
            ("SteamGamebar", Settings.FrontButtonMapping.Steam.Gamebar.Action),
            ("SteamCenterM", Settings.FrontButtonMapping.Steam.CenterM.Action));
        FrontButtonMappingChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Persists the whole M1/M2 mapping before publishing it as current state.</summary>
    public bool ChangeBackButtonMapping(BackButtonMappingSettings mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        var reason = BackButtonMappingValidation.Validate(mapping);
        if (reason is not null)
        {
            SteamInputAddonforClaw.Diagnostics.AppLog.Warn("Settings", "Rejected an invalid back-button mapping candidate.", null, ("Reason", reason));
            return false;
        }

        if (Settings.BackButtonMapping == mapping) return true;

        var next = Settings with { BackButtonMapping = mapping };
        _settingsStore.Save(next);
        Settings = next;
        SteamInputAddonforClaw.Diagnostics.AppLog.Debug("Settings", "Back-button mapping saved.",
            ("M1", Settings.BackButtonMapping.M1), ("M2", Settings.BackButtonMapping.M2));
        return true;
    }

    /// <summary>Validates, saves, then publishes the whole desired controller LED setting.</summary>
    public bool ChangeControllerLedSettings(ControllerLedSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var reason = ControllerLedSettingsValidation.Validate(settings);
        if (reason is not null)
        {
            SteamInputAddonforClaw.Diagnostics.AppLog.Warn("Settings", "Rejected invalid controller LED settings.", null, ("Reason", reason));
            return false;
        }

        if (Settings.ControllerLed == settings) return true;

        var next = Settings with { ControllerLed = settings };
        _settingsStore.Save(next);
        Settings = next;
        SteamInputAddonforClaw.Diagnostics.AppLog.Debug("Settings", "Controller LED settings saved.",
            ("Enabled", settings.Enabled), ("Brightness", settings.Brightness),
            ("Red", settings.Red), ("Green", settings.Green), ("Blue", settings.Blue));
        return true;
    }

    /// <summary>Validates and persists the whole desired vibration pair before publishing it.</summary>
    public bool ChangeControllerVibrationSettings(ControllerVibrationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var reason = ControllerVibrationSettingsValidation.Validate(settings);
        if (reason is not null)
        {
            SteamInputAddonforClaw.Diagnostics.AppLog.Warn("Settings", "Rejected invalid controller vibration settings.", null,
                ("Reason", reason));
            return false;
        }

        if (Settings.ControllerVibration == settings) return true;

        var next = Settings with { ControllerVibration = settings };
        _settingsStore.Save(next);
        Settings = next;
        SteamInputAddonforClaw.Diagnostics.AppLog.Debug("Settings", "Controller vibration settings saved.",
            ("LeftPercent", settings.LeftPercent), ("RightPercent", settings.RightPercent));
        return true;
    }

    /// <summary>Persists one validated Steam-facing controller choice before publishing it to the
    /// existing presentation owner. A same-value request is an accepted no-op.</summary>
    public bool ChangeSteamVirtualController(SteamVirtualControllerPreference preference)
    {
        if (!Enum.IsDefined(preference))
        {
            SteamInputAddonforClaw.Diagnostics.AppLog.Warn("Settings", "Rejected an invalid Steam virtual controller preference.", null,
                ("Preference", preference));
            return false;
        }

        if (Settings.SteamVirtualController == preference) return true;

        var next = Settings with { SteamVirtualController = preference };
        _settingsStore.Save(next);
        Settings = next;
        SteamInputAddonforClaw.Diagnostics.AppLog.Info("Settings", "Steam virtual controller preference saved.",
            ("Preference", preference));
        return true;
    }

    public bool ChangeGyroscopeEnabled(bool enabled)
    {
        if (Settings.GyroscopeEnabled == enabled) return true;

        var next = Settings with { GyroscopeEnabled = enabled };
        _settingsStore.Save(next);
        Settings = next;
        SteamInputAddonforClaw.Diagnostics.AppLog.Info("ControllerMotion", "Gyroscope preference saved.",
            ("Enabled", enabled));
        return true;
    }

    /// <summary>
    /// The narrow validated mutation seam OQ5-UI-09 will call when the Overlay requests a reorder.
    /// A valid complete order (all five tabs, each once) is normalized, persisted, then published
    /// (save-then-current, like FrontButtonMapping). A request equal to the current order is an
    /// accepted no-op. An invalid request is rejected: it is NOT silently converted to the default --
    /// the user's current valid order stays exactly as it is, and nothing is written to disk.
    /// </summary>
    public bool TryChangeAddonQuickSettingsTabOrder(IReadOnlyList<AddonQuickSettingsTabId> requested)
    {
        if (!AddonQuickSettingsTabOrderContract.TryNormalize(requested, out var normalized))
            return false;

        if (normalized.SequenceEqual(Settings.AddonQuickSettingsTabOrder))
            return true;

        var next = Settings with { AddonQuickSettingsTabOrder = normalized };
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

    public bool TryAcknowledgeWindowsTweaksWarning(DateOnly localDate)
    {
        if (Settings.WindowsTweaksWarningAcknowledgedLocalDate == localDate)
            return true;

        var next = Settings with { WindowsTweaksWarningAcknowledgedLocalDate = localDate };
        _settingsStore.Save(next);
        Settings = next;
        return true;
    }

    public void ChangeQuickSettingsCurrentPowerSourceOnly(bool enabled)
    {
        if (Settings.QuickSettingsCurrentPowerSourceOnly == enabled) return;
        var next = Settings with { QuickSettingsCurrentPowerSourceOnly = enabled };
        _settingsStore.Save(next);
        Settings = next;
    }

    /// <summary>Persists the experimental touch preference before Runtime capture is reconciled.</summary>
    public void ChangeExperimentalVirtualTouchpadEnabled(bool enabled)
    {
        if (Settings.ExperimentalVirtualTouchpadEnabled == enabled) return;
        var next = Settings with { ExperimentalVirtualTouchpadEnabled = enabled };
        _settingsStore.Save(next);
        Settings = next;
    }

    /// <summary>Persists the ClawHUD top-level desired state before publishing it.</summary>
    public void ChangeClawHudEnabled(bool enabled)
    {
        if (Settings.ClawHudEnabled == enabled) return;
        var next = Settings with { ClawHudEnabled = enabled };
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
