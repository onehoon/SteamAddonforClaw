using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Shortcuts;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Shortcuts;

public enum ShortcutLoadStatus
{
    Loaded,
    NotFound,
    Malformed,
    UnsupportedSchemaVersion,
    ReadFailure
}

public sealed record ShortcutLoadResult(ShortcutDocument Document, ShortcutLoadStatus Status)
{
    public bool CanSafelyReplace => Status is ShortcutLoadStatus.Loaded or ShortcutLoadStatus.NotFound;
}

/// <summary>
/// Runtime-owned persistence for the independent <c>shortcuts.json</c> document.
/// The caller supplies the canonical path; this store does not discover application paths or
/// depend on WinUI, Overlay, frontend transport, or action execution.
/// </summary>
public sealed class ShortcutStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _shortcutsPath;

    public ShortcutStore(string shortcutsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutsPath);
        _shortcutsPath = shortcutsPath;
    }

    public ShortcutLoadResult Load()
    {
        string text;
        try
        {
            text = File.ReadAllText(_shortcutsPath);
        }
        catch (FileNotFoundException)
        {
            AppLog.Info("Shortcuts", "Shortcut document not found. First run; using defaults.", ("Path", _shortcutsPath));
            return new ShortcutLoadResult(new ShortcutDocument(), ShortcutLoadStatus.NotFound);
        }
        catch (DirectoryNotFoundException)
        {
            AppLog.Info("Shortcuts", "Shortcut document directory not found. First run; using defaults.", ("Path", _shortcutsPath));
            return new ShortcutLoadResult(new ShortcutDocument(), ShortcutLoadStatus.NotFound);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            AppLog.Warn("Shortcuts", "Shortcut document could not be read.", exception, ("Path", _shortcutsPath));
            return new ShortcutLoadResult(new ShortcutDocument(), ShortcutLoadStatus.ReadFailure);
        }

        int schemaVersion;
        try
        {
            using var rootDocument = JsonDocument.Parse(text);
            if (rootDocument.RootElement.ValueKind != JsonValueKind.Object)
                return Malformed("Shortcut document root must be a JSON object.");

            if (!rootDocument.RootElement.TryGetProperty("schemaVersion", out var versionElement)
                || versionElement.ValueKind != JsonValueKind.Number
                || !versionElement.TryGetInt32(out schemaVersion))
            {
                return Malformed("Shortcut document is missing a valid schemaVersion.");
            }

            if (schemaVersion > ShortcutDocument.CurrentSchemaVersion)
            {
                AppLog.Warn("Shortcuts", "Shortcut document schema version is newer than this build supports. Refusing to load or overwrite it.", null,
                    ("Path", _shortcutsPath),
                    ("DocumentSchemaVersion", schemaVersion),
                    ("SupportedSchemaVersion", ShortcutDocument.CurrentSchemaVersion));
                return new ShortcutLoadResult(new ShortcutDocument(), ShortcutLoadStatus.UnsupportedSchemaVersion);
            }

            if (schemaVersion < 1)
                return Malformed("Shortcut document schema version is invalid.", schemaVersion: schemaVersion);

            if (!rootDocument.RootElement.TryGetProperty("dashboard", out var dashboardElement)
                || dashboardElement.ValueKind != JsonValueKind.Object)
            {
                return Malformed("Shortcut document dashboard is missing or is not a JSON object.", schemaVersion: schemaVersion);
            }
        }
        catch (JsonException exception)
        {
            return Malformed("Shortcut document is not valid JSON.", exception);
        }

        try
        {
            var document = JsonSerializer.Deserialize<ShortcutDocument>(text, SerializerOptions);
            var validationReason = document is null
                ? "Shortcut document deserialized to null."
                : ShortcutDefinitionValidation.Validate(document.Dashboard);

            if (document is null || validationReason is not null)
            {
                return Malformed(validationReason ?? "Shortcut document structure is invalid.", schemaVersion: schemaVersion);
            }

            AppLog.Debug("Shortcuts", "Shortcut document loaded.",
                ("Path", _shortcutsPath),
                ("TileCount", document.Dashboard.Tiles.Count),
                ("DocumentSchemaVersion", document.SchemaVersion));
            return new ShortcutLoadResult(document, ShortcutLoadStatus.Loaded);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            return Malformed("Shortcut document could not be deserialized.", exception, schemaVersion);
        }
    }

    public void Save(ShortcutDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.SchemaVersion != ShortcutDocument.CurrentSchemaVersion)
        {
            throw new ArgumentException(
                $"Shortcut document schema version must be {ShortcutDocument.CurrentSchemaVersion}.",
                nameof(document));
        }

        var validationReason = ShortcutDefinitionValidation.Validate(document.Dashboard);
        if (validationReason is not null)
        {
            throw new ArgumentException($"Shortcut document is invalid: {validationReason}", nameof(document));
        }

        var directory = Path.GetDirectoryName(_shortcutsPath)
            ?? throw new InvalidOperationException("The shortcut document path does not have a parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{_shortcutsPath}.tmp";
        var json = JsonSerializer.Serialize(document, SerializerOptions);
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _shortcutsPath, overwrite: true);

        AppLog.Debug("Shortcuts", "Shortcut document saved.",
            ("Path", _shortcutsPath),
            ("TileCount", document.Dashboard.Tiles.Count),
            ("DocumentSchemaVersion", document.SchemaVersion));
    }

    private ShortcutLoadResult Malformed(string message, Exception? exception = null, int? schemaVersion = null)
    {
        var fields = schemaVersion is int version
            ? new[] { ("Path", (object?)_shortcutsPath), ("DocumentSchemaVersion", (object?)version) }
            : new[] { ("Path", (object?)_shortcutsPath) };
        AppLog.Warn("Shortcuts", message + " Preserving the original file.", exception, fields);
        return new ShortcutLoadResult(new ShortcutDocument(), ShortcutLoadStatus.Malformed);
    }
}
