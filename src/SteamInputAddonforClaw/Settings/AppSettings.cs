using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.Contracts.Overlay;
using SteamInputAddonforClaw.Contracts.Wing;

namespace SteamInputAddonforClaw.Settings;

public enum AppLogPreference { Off, Info, Debug }

public sealed record AppSettings(
    bool LaunchAtWindowsStartup = true,
    AppLogPreference LogLevel = AppLogPreference.Off,
    bool SuppressDeveloperMenuWarning = false)
{
    public bool DeveloperMenuEnabled { get; init; }

    /// <summary>
    /// The OEM1 (Center M button) remapping switch and the four slot bindings, persisted through
    /// this same settings file rather than a second store. Declared as an init-only property, not a
    /// positional parameter, purely so every existing positional construction site keeps compiling
    /// and automatically gets the locked first-install defaults.
    /// </summary>
    public Oem1MappingSettings Oem1Mapping { get; init; } = Oem1MappingSettings.Default;
    public WingMappingSettings WingMapping { get; init; } = WingMappingSettings.Default;

    /// <summary>
    /// The order of the five fixed top-level Overlay tabs. Same init-only compatibility pattern as
    /// OEM1/WING above. Always a complete normalized order (all five tabs, each once); the first
    /// entry is the tab selected on every Overlay Show. Transported to Overlay.exe by OQ5-UI-09.
    /// </summary>
    public IReadOnlyList<OverlayTabId> OverlayTabOrder { get; init; } = OverlayTabOrderContract.DefaultOrder;
}
