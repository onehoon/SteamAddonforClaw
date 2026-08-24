using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.Contracts.Wing;

namespace SteamInputAddonforClaw.Settings;

public enum AppLogPreference { Off, Info, Debug }

public sealed record AppSettings(
    bool LaunchAtWindowsStartup = true,
    AppLogPreference LogLevel = AppLogPreference.Off,
    bool SteamInputRoutingEnabled = true,
    bool SuppressDeveloperMenuWarning = false)
{
    public bool DeveloperMenuEnabled { get; init; }

    /// <summary>
    /// The OEM1 (Center M button) remapping switch and the four slot bindings, persisted through
    /// this same settings file rather than a second store. Declared as an init-only property, not a
    /// positional parameter, purely so every existing positional construction site keeps compiling
    /// and automatically gets the locked first-install defaults.
    /// </summary>
    /// <remarks>
    /// Entirely independent of <see cref="SteamInputRoutingEnabled"/>: that is the Steam Input
    /// routing master switch, this is the Center M remapping feature. Neither reads the other.
    /// </remarks>
    public Oem1MappingSettings Oem1Mapping { get; init; } = Oem1MappingSettings.Default;
    public WingMappingSettings WingMapping { get; init; } = WingMappingSettings.Default;
}
