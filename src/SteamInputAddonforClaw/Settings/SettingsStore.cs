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

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath)) ?? new AppSettings();
            AppLog.Info("Settings", "Settings loaded.", ("LaunchAtWindowsStartup", settings.LaunchAtWindowsStartup));
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
        AppLog.Info("Settings", "Settings save started.", ("Path", _settingsPath), ("LaunchAtWindowsStartup", settings.LaunchAtWindowsStartup));

        var directory = Path.GetDirectoryName(_settingsPath) ?? throw new InvalidOperationException("The settings path does not have a parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_settingsPath}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SerializerOptions));
        File.Move(temporaryPath, _settingsPath, overwrite: true);
        AppLog.Info("Settings", "Settings save completed.");
    }
}
