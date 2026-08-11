using System.Text.Json;

namespace SteamInputAddonforClaw.Settings;

public static class LogLevelBootstrap
{
    public static AppLogPreference Read(string settingsPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            return AppSettingsPolicy.Normalize(document.RootElement.TryGetProperty("LogLevel", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null);
        }
        catch { return AppLogPreference.Info; }
    }
}
