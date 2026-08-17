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
            var routeInSteamBigPicture = !root.TryGetProperty("RouteInSteamBigPicture", out var routeProperty) || routeProperty.ValueKind == JsonValueKind.True;
            var suppressDeveloperMenuWarning = root.TryGetProperty("SuppressDeveloperMenuWarning", out var warningProperty) && warningProperty.ValueKind == JsonValueKind.True && warningProperty.GetBoolean();
            var settings = new AppSettings(startup, logLevel, routeInSteamBigPicture, suppressDeveloperMenuWarning);
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

    internal SettingsLoadResult LoadForSafetyGate()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new(new AppSettings(), true, "Defaults");
            using var document = JsonDocument.Parse(File.ReadAllText(_settingsPath));
            var root = document.RootElement;
            var startup = root.TryGetProperty("LaunchAtWindowsStartup", out var startupProperty)
                ? startupProperty.ValueKind is JsonValueKind.True or JsonValueKind.False ? startupProperty.GetBoolean() : throw new JsonException("LaunchAtWindowsStartup must be boolean.")
                : true;
            var logLevel = AppSettingsPolicy.Normalize(root.TryGetProperty("LogLevel", out var levelProperty)
                ? levelProperty.ValueKind == JsonValueKind.String ? levelProperty.GetString() : throw new JsonException("LogLevel must be string.")
                : null);
            var route = root.TryGetProperty("RouteInSteamBigPicture", out var routeProperty)
                ? routeProperty.ValueKind is JsonValueKind.True or JsonValueKind.False ? routeProperty.GetBoolean() : throw new JsonException("RouteInSteamBigPicture must be boolean.")
                : true;
            // Developer-menu warning suppression is UI preference data, not a safety-gate input.
            // Keep malformed values from affecting the prerequisite mutation decision.
            return new(new AppSettings(startup, logLevel, route), true, "Loaded");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            AppLog.Warn("Settings", "Reliable safety-gate settings read failed.", exception, ("Action", "BlockMutation"));
            return new(new AppSettings(), false, "SettingsUnreliable");
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AppLog.Debug("Settings", "Settings save started.", ("Path", _settingsPath), ("LaunchAtWindowsStartup", settings.LaunchAtWindowsStartup), ("LogLevel", settings.LogLevel), ("RouteInSteamBigPicture", settings.RouteInSteamBigPicture));

        var directory = Path.GetDirectoryName(_settingsPath) ?? throw new InvalidOperationException("The settings path does not have a parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_settingsPath}.tmp";
        var payload = new { settings.LaunchAtWindowsStartup, LogLevel = settings.LogLevel.ToString(), settings.RouteInSteamBigPicture, settings.SuppressDeveloperMenuWarning };
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(payload, SerializerOptions));
        File.Move(temporaryPath, _settingsPath, overwrite: true);
        AppLog.Debug("Settings", "Settings save completed.");
    }
}

internal sealed record SettingsLoadResult(AppSettings Settings, bool IsReliable, string Reason);
