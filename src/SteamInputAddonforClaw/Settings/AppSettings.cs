namespace SteamInputAddonforClaw.Settings;

public enum AppLogPreference { Info, Debug }

public sealed record AppSettings(bool LaunchAtWindowsStartup = true, AppLogPreference LogLevel = AppLogPreference.Info);
