using System.Text.Json;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;

    public SettingsStore(string settingsPath)
    {
        _settingsPath = settingsPath ?? throw new ArgumentNullException(nameof(settingsPath));
    }

    public AppSettings Load()
    {
        AppLog.Debug("Settings", "Settings load started.", ("Path", _settingsPath), ("FileExists", File.Exists(_settingsPath)));
        try
        {
            if (!File.Exists(_settingsPath))
            {
                AppLog.Info("Settings", "Settings file not found. Using defaults.");
                return new AppSettings();
            }

            using var document = JsonDocument.Parse(File.ReadAllText(_settingsPath));
            var root = document.RootElement;
            var startup = root.TryGetProperty("LaunchAtWindowsStartup", out var startupProperty) && startupProperty.ValueKind is JsonValueKind.False or JsonValueKind.True
                ? startupProperty.GetBoolean() : true;
            var logLevel = AppSettingsPolicy.Normalize(root.TryGetProperty("LogLevel", out var levelProperty) && levelProperty.ValueKind == JsonValueKind.String ? levelProperty.GetString() : null);
            var settings = new AppSettings(startup, logLevel);
            AppLog.Debug("Settings", "Settings loaded.", ("LaunchAtWindowsStartup", settings.LaunchAtWindowsStartup), ("LogLevel", settings.LogLevel));
            return settings;
        }
        catch (JsonException exception)
        {
            AppLog.Warn("Settings", "Settings parsing failed. Using defaults.", exception, ("Action", "Defaults"));
                return new AppSettings();
        }
        catch (IOException exception)
        {
            AppLog.Warn("Settings", "Settings read failed. Using defaults.", exception, ("Action", "Defaults"));
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AppLog.Debug("Settings", "Settings save started.", ("Path", _settingsPath), ("LaunchAtWindowsStartup", settings.LaunchAtWindowsStartup), ("LogLevel", settings.LogLevel));

        var directory = Path.GetDirectoryName(_settingsPath) ?? throw new InvalidOperationException("The settings path does not have a parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_settingsPath}.tmp";
        var payload = new { settings.LaunchAtWindowsStartup, LogLevel = settings.LogLevel.ToString() };
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(payload, SerializerOptions));
        File.Move(temporaryPath, _settingsPath, overwrite: true);
        AppLog.Debug("Settings", "Settings save completed.");
    }
}
