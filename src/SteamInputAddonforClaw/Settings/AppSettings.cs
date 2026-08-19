namespace SteamInputAddonforClaw.Settings;

public enum AppLogPreference { Off, Info, Debug }

public sealed record AppSettings(
    bool LaunchAtWindowsStartup = true,
    AppLogPreference LogLevel = AppLogPreference.Off,
    bool SteamInputRoutingEnabled = true,
    bool SuppressDeveloperMenuWarning = false);
