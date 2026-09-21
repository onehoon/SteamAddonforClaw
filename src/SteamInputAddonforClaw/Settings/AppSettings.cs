using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Settings;

public enum AppLogPreference { Off, Info, Debug }

public sealed record AppSettings(
    AppLogPreference LogLevel = AppLogPreference.Off,
    bool SuppressDeveloperMenuWarning = false)
{
    /// <summary>Top-level desired state for the optional ClawHUD Managed Runtime.</summary>
    public bool ClawHudEnabled { get; init; }

    public bool DeveloperMenuEnabled { get; init; }

    /// <summary>When enabled, compact Quick Settings surfaces show only the currently active AC/DC
    /// side. The complete saved configuration is always retained.</summary>
    public bool QuickSettingsCurrentPowerSourceOnly { get; init; }

    /// <summary>
    /// The one atomic front-button mapping (Gamebar Button and Center M Button, each with a Normal
    /// and a Steam Game / Big Picture action), persisted through this same settings file rather than
    /// a second store. Declared as an init-only property, not a positional parameter, purely so every
    /// existing positional construction site keeps compiling and automatically gets the locked
    /// first-install defaults.
    /// </summary>
    public FrontButtonMappingSettings FrontButtonMapping { get; init; } = FrontButtonMappingSettings.Default;

    /// <summary>Global Xbox360-only mapping for the physical Full1902 M1/M2 rear buttons.</summary>
    public BackButtonMappingSettings BackButtonMapping { get; init; } = BackButtonMappingSettings.Default;

    /// <summary>
    /// The order of the five fixed top-level Overlay tabs. Same init-only compatibility pattern as
    /// FrontButtonMapping above. Always a complete normalized order (all five tabs, each once); the
    /// first entry is the tab selected on every Overlay Show. Transported to Overlay.exe by OQ5-UI-09.
    /// </summary>
    public IReadOnlyList<AddonQuickSettingsTabId> AddonQuickSettingsTabOrder { get; init; } = AddonQuickSettingsTabOrderContract.DefaultOrder;
}
