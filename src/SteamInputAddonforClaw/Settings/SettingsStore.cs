using System.Text.Json;
using System.Text.Json.Serialization;
using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.Contracts.Wing;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        // The OEM1 mapping is the only nested payload here, and its enums must round-trip as names:
        // a numeric action/key/slot value in the settings file would silently change meaning the
        // moment an enum member is inserted.
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };
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
            var steamInputRoutingEnabled = root.TryGetProperty("SteamInputRoutingEnabled", out var routeProperty)
                && routeProperty.ValueKind == JsonValueKind.True;
            var suppressDeveloperMenuWarning = root.TryGetProperty("SuppressDeveloperMenuWarning", out var warningProperty) && warningProperty.ValueKind == JsonValueKind.True && warningProperty.GetBoolean();
            var developerMenuEnabled = root.TryGetProperty("DeveloperMenuEnabled", out var developerMenuProperty) && developerMenuProperty.ValueKind == JsonValueKind.True && developerMenuProperty.GetBoolean();
            var settings = new AppSettings(startup, logLevel, steamInputRoutingEnabled, suppressDeveloperMenuWarning)
            {
                DeveloperMenuEnabled = developerMenuEnabled,
                Oem1Mapping = ReadOem1Mapping(root),
                WingMapping = ReadWingMapping(root)
            };
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

    private static WingMappingSettings ReadWingMapping(JsonElement root)
    {
        if (!root.TryGetProperty("WingMapping", out var property) || property.ValueKind != JsonValueKind.Object)
            return WingMappingSettings.Default;
        try
        {
            var mapping = property.Deserialize<WingMappingSettings>(SerializerOptions) ?? throw new JsonException("WING mapping was null.");
            if (mapping.Single is null || mapping.Double is null
                || mapping.Single.Hotkey is null || mapping.Single.Launch is null
                || mapping.Double.Hotkey is null || mapping.Double.Launch is null
                || !WingActionCapabilities.Supports(mapping.Single.Action)
                || !WingActionCapabilities.Supports(mapping.Double.Action))
                throw new JsonException("WING mapping contains an invalid action or slot.");
            return mapping;
        }
        catch (JsonException exception)
        {
            AppLog.Warn("Settings", "WING mapping settings could not be parsed; using defaults.", exception);
            return WingMappingSettings.Default;
        }
    }

    /// <summary>
    /// Reads the persisted OEM1 mapping. Missing entirely (no value has ever been saved / first
    /// install) uses the locked first-install defaults, including remapping ON. A present but
    /// corrupt/unreadable value is a DIFFERENT case and must not silently turn suppression back on.
    /// </summary>
    /// <remarks>
    /// Review fix (MAJOR): the previous fallback was unconditionally <see cref="Oem1MappingSettings.Default"/>
    /// (remapping <c>true</c>) for both "never saved" and "saved but unparseable" -- but an existing
    /// settings file with a corrupt <c>Oem1Mapping</c> object can still legitimately record
    /// <c>RemappingEnabled: false</c> at the top level of that same object. Falling back to the ON
    /// default in that case would silently re-enable OEM1 suppression against an explicit persisted
    /// Off, which is exactly the kind of behind-the-user's-back reactivation this setting exists to
    /// prevent. The salvage below reads only the top-level <c>RemappingEnabled</c> boolean directly
    /// (itself defaulting to <see langword="false"/> -- fail-OPEN to native Center M, never fail
    /// toward suppression -- if even that cannot be read) and otherwise falls back to the default
    /// bindings; bindings are not safety-relevant the way the switch is.
    /// </remarks>
    private static Oem1MappingSettings ReadOem1Mapping(JsonElement root)
    {
        if (!root.TryGetProperty("Oem1Mapping", out var mappingProperty) || mappingProperty.ValueKind != JsonValueKind.Object)
            return Oem1MappingSettings.Default; // genuinely absent -- first install, locked defaults apply

        var salvagedEnabled = mappingProperty.TryGetProperty("RemappingEnabled", out var enabledProperty)
            && enabledProperty.ValueKind is JsonValueKind.True or JsonValueKind.False
            && enabledProperty.GetBoolean();

        try
        {
            var parsed = mappingProperty.Deserialize<Oem1MappingSettings>(SerializerOptions)
                ?? throw new JsonException("OEM1 mapping was null.");

            if (parsed.NormalSingle is null || parsed.NormalDouble is null || parsed.RoutingSingle is null || parsed.RoutingDouble is null)
                throw new JsonException("OEM1 mapping contains a null slot binding.");

            return parsed;
        }
        catch (JsonException exception)
        {
            AppLog.Warn("Settings", "OEM1 mapping settings could not be parsed; preserving the persisted remapping switch and falling back to default bindings.", exception, ("SalvagedRemappingEnabled", salvagedEnabled));
            return Oem1MappingSettings.Default with { RemappingEnabled = salvagedEnabled };
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
            var route = root.TryGetProperty("SteamInputRoutingEnabled", out var routeProperty)
                ? routeProperty.ValueKind is JsonValueKind.True or JsonValueKind.False ? routeProperty.GetBoolean() : throw new JsonException("SteamInputRoutingEnabled must be boolean.")
                : false;
            // Developer-menu warning suppression and the OEM1 mapping are UI/feature preference data,
            // not safety-gate inputs. Keep malformed values from affecting the prerequisite mutation
            // decision -- and note this result is only ever evaluated, never saved back, so omitting
            // them here cannot erase what is persisted.
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
        AppLog.Debug("Settings", "Settings save started.", ("Path", _settingsPath), ("LaunchAtWindowsStartup", settings.LaunchAtWindowsStartup), ("LogLevel", settings.LogLevel), ("SteamInputRoutingEnabled", settings.SteamInputRoutingEnabled));

        var directory = Path.GetDirectoryName(_settingsPath) ?? throw new InvalidOperationException("The settings path does not have a parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_settingsPath}.tmp";
        var payload = new { settings.LaunchAtWindowsStartup, LogLevel = settings.LogLevel.ToString(), settings.SteamInputRoutingEnabled, settings.SuppressDeveloperMenuWarning, settings.DeveloperMenuEnabled, settings.Oem1Mapping, settings.WingMapping };
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(payload, SerializerOptions));
        File.Move(temporaryPath, _settingsPath, overwrite: true);
        AppLog.Debug("Settings", "Settings save completed.");
    }
}

internal sealed record SettingsLoadResult(AppSettings Settings, bool IsReliable, string Reason);
