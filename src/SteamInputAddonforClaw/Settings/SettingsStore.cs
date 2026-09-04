using System.Text.Json;
using System.Text.Json.Serialization;
using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.Contracts.Overlay;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        // The front-button mapping and the Overlay tab order all persist enums, which must round-trip
        // as names: a numeric action/key/domain/tab value in the settings file would silently change
        // meaning the moment an enum member is inserted.
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
            var logLevel = AppSettingsPolicy.Normalize(root.TryGetProperty("LogLevel", out var levelProperty) && levelProperty.ValueKind == JsonValueKind.String ? levelProperty.GetString() : null);
            var suppressDeveloperMenuWarning = root.TryGetProperty("SuppressDeveloperMenuWarning", out var warningProperty) && warningProperty.ValueKind == JsonValueKind.True && warningProperty.GetBoolean();
            var developerMenuEnabled = root.TryGetProperty("DeveloperMenuEnabled", out var developerMenuProperty) && developerMenuProperty.ValueKind == JsonValueKind.True && developerMenuProperty.GetBoolean();
            var settings = new AppSettings(
                LogLevel: logLevel,
                SuppressDeveloperMenuWarning: suppressDeveloperMenuWarning)
            {
                DeveloperMenuEnabled = developerMenuEnabled,
                FrontButtonMapping = ReadFrontButtonMapping(root),
                OverlayTabOrder = ReadOverlayTabOrder(root)
            };
            AppLog.Debug("Settings", "Settings loaded.", ("LogLevel", settings.LogLevel));
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

    /// <summary>
    /// Reads the persisted Overlay tab order. Absent (pre-OQ5-UI-08 file) or malformed -- not an
    /// array, wrong count, duplicate/missing/unknown tab, numeric enum, null/non-string element --
    /// resolves only this preference to the frozen default. It is parsed in isolation so a broken
    /// tab-order value can never throw into <see cref="Load"/>'s catch and reset every unrelated
    /// setting to defaults.
    /// </summary>
    private static IReadOnlyList<OverlayTabId> ReadOverlayTabOrder(JsonElement root)
    {
        if (!root.TryGetProperty("OverlayTabOrder", out var property) || property.ValueKind != JsonValueKind.Array)
            return OverlayTabOrderContract.DefaultOrder;

        var parsed = new List<OverlayTabId>(property.GetArrayLength());
        foreach (var element in property.EnumerateArray())
        {
            var name = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
            // Enum.TryParse would also accept "3" or an out-of-range "99"; require an actual name.
            if (string.IsNullOrEmpty(name) || !char.IsLetter(name[0])
                || !Enum.TryParse<OverlayTabId>(name, ignoreCase: false, out var tab))
            {
                AppLog.Warn("Settings", "Overlay tab order contains an invalid entry; using the default order.", null, ("Action", "Default"));
                return OverlayTabOrderContract.DefaultOrder;
            }
            parsed.Add(tab);
        }

        if (OverlayTabOrderContract.TryNormalize(parsed, out var normalized))
            return normalized;

        AppLog.Warn("Settings", "Overlay tab order is not a complete set of the five tabs; using the default order.", null, ("Action", "Default"));
        return normalized;
    }

    /// <summary>
    /// Reads the persisted front-button mapping (App UI PR-C). Parsed in isolation so a broken value
    /// can never throw into <see cref="Load"/>'s catch and reset unrelated log/developer/overlay-tab
    /// settings. Pre-release migration policy: absent, malformed, unknown-action, domain-invalid,
    /// duplicate, or incomplete all resolve to the frozen PR-C defaults -- there is no schema
    /// migration from the old split OEM1/WING structure, and the obsolete <c>Oem1Mapping</c> /
    /// <c>WingMapping</c> JSON members are simply ignored (the next normal save drops them).
    /// </summary>
    private static FrontButtonMappingSettings ReadFrontButtonMapping(JsonElement root)
    {
        if (!root.TryGetProperty("FrontButtonMapping", out var property) || property.ValueKind != JsonValueKind.Object)
            return FrontButtonMappingSettings.Default;

        try
        {
            var mapping = property.Deserialize<FrontButtonMappingSettings>(SerializerOptions);
            var reason = FrontButtonMappingValidation.Validate(mapping);
            if (reason is not null)
            {
                AppLog.Warn("Settings", "Front-button mapping is invalid; using the frozen defaults for this feature only.", null, ("Reason", reason));
                return FrontButtonMappingSettings.Default;
            }
            return mapping!;
        }
        catch (JsonException exception)
        {
            AppLog.Warn("Settings", "Front-button mapping could not be parsed; using the frozen defaults for this feature only.", exception);
            return FrontButtonMappingSettings.Default;
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AppLog.Debug("Settings", "Settings save started.", ("Path", _settingsPath), ("LogLevel", settings.LogLevel));

        var directory = Path.GetDirectoryName(_settingsPath) ?? throw new InvalidOperationException("The settings path does not have a parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_settingsPath}.tmp";
        var payload = new { LogLevel = settings.LogLevel.ToString(), settings.SuppressDeveloperMenuWarning, settings.DeveloperMenuEnabled, settings.FrontButtonMapping, OverlayTabOrder = OverlayTabOrderContract.NormalizeOrDefault(settings.OverlayTabOrder) };
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(payload, SerializerOptions));
        File.Move(temporaryPath, _settingsPath, overwrite: true);
        AppLog.Debug("Settings", "Settings save completed.");
    }
}
