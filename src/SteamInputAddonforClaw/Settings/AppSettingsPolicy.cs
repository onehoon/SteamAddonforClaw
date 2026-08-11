namespace SteamInputAddonforClaw.Settings;

public static class AppSettingsPolicy
{
    public static AppLogPreference Normalize(string? value) =>
        string.Equals(value, nameof(AppLogPreference.Debug), StringComparison.OrdinalIgnoreCase)
            ? AppLogPreference.Debug : AppLogPreference.Info;
}
