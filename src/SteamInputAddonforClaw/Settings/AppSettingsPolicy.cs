using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Settings;

public static class AppSettingsPolicy
{
    /// <summary>Parses a persisted/user-facing log level string, defaulting to Off for anything unrecognized (missing, malformed, legacy).</summary>
    public static AppLogPreference Normalize(string? value) =>
        string.Equals(value, nameof(AppLogPreference.Debug), StringComparison.OrdinalIgnoreCase) ? AppLogPreference.Debug
        : string.Equals(value, nameof(AppLogPreference.Info), StringComparison.OrdinalIgnoreCase) ? AppLogPreference.Info
        : AppLogPreference.Off;

    /// <summary>Maps the user-facing log level preference to the runtime AppLog threshold.</summary>
    internal static AppLogLevel ToAppLogLevel(AppLogPreference preference) => preference switch
    {
        AppLogPreference.Debug => AppLogLevel.Debug,
        AppLogPreference.Info => AppLogLevel.Info,
        _ => AppLogLevel.Off,
    };
}
