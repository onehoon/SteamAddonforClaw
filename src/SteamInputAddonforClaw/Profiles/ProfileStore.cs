using System.Text.Json;
using System.Text.Json.Serialization;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Profiles;

/// <summary>Outcome of <see cref="ProfileStore.Load"/>. <see cref="Loaded"/> and <see cref="NotFound"/>
/// are the two cases where <see cref="ProfileLoadResult.Document"/> is safe to treat as
/// authoritative and save back through; the others carry a fresh default document only so callers
/// always have something usable, but must not save it over the existing file -- the original stays
/// on disk for diagnosis/recovery.</summary>
public enum ProfileLoadStatus
{
    /// <summary>Deserialized successfully at a supported schema version.</summary>
    Loaded,

    /// <summary><c>profiles.json</c> does not exist -- a normal first-run condition, not an error.
    /// <see cref="ProfileLoadResult.Document"/> is a fresh default document.</summary>
    NotFound,

    /// <summary>The file exists but is not valid/expected JSON (parse failure, missing/invalid
    /// <c>schemaVersion</c>, or a document that fails to deserialize). The original file is left
    /// untouched.</summary>
    Malformed,

    /// <summary>The persisted <c>schemaVersion</c> is higher than <see cref="ProfileDocument.CurrentSchemaVersion"/>
    /// -- a newer build wrote this file. Never silently reinterpreted as the current version, and
    /// never overwritten.</summary>
    UnsupportedSchemaVersion,

    /// <summary>The file could not be read (I/O or access failure). Not the same as "absent" --
    /// must not be treated as first-run and saved over.</summary>
    ReadFailure
}

public sealed record ProfileLoadResult(ProfileDocument Document, ProfileLoadStatus Status)
{
    /// <summary>True only for <see cref="ProfileLoadStatus.Loaded"/>/<see cref="ProfileLoadStatus.NotFound"/>
    /// -- the two cases where saving <see cref="Document"/> back is safe. Malformed/unsupported/
    /// unreadable results must never be saved through: doing so would destroy the original file
    /// this owner could not actually read.</summary>
    public bool CanSafelyReplace => Status is ProfileLoadStatus.Loaded or ProfileLoadStatus.NotFound;
}

/// <summary>
/// Persistence for the Device/Profile document (<c>profiles.json</c>) -- a storage domain
/// separate from the existing operational <c>settings.json</c> (routing/OEM1/startup/log level;
/// see <see cref="SteamInputAddonforClaw.Settings.SettingsStore"/>), because the two have
/// different lifecycle and failure characteristics. Lives under the same canonical persistent
/// <c>-Data</c> root as the rest of the Addon's durable state (see
/// <see cref="SteamInputAddonforClaw.Install.AddonDataPaths"/>) -- this store does not rediscover
/// or invent its own storage root; the caller supplies the exact path.
///
/// Runtime-owned, UI-independent: this type has no dependency on WinUI, QamHost, or any frontend
/// connection. It is plain .NET code usable the moment the Addon Runtime starts, whether or not a
/// UI ever attaches.
/// </summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Keeps Games' Steam-AppID string keys exactly as supplied -- only property NAMES are
        // camelCased, not dictionary keys, which are already the plain numeric-string AppID.
        DictionaryKeyPolicy = null,
        Converters = { new JsonStringEnumConverter(namingPolicy: JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    private readonly string _profilesPath;

    public ProfileStore(string profilesPath)
    {
        _profilesPath = profilesPath ?? throw new ArgumentNullException(nameof(profilesPath));
    }

    public ProfileLoadResult Load()
    {
        if (!File.Exists(_profilesPath))
        {
            AppLog.Info("Profiles", "Profile document not found. First run; using defaults.", ("Path", _profilesPath));
            return new ProfileLoadResult(new ProfileDocument(), ProfileLoadStatus.NotFound);
        }

        string text;
        try
        {
            text = File.ReadAllText(_profilesPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            AppLog.Warn("Profiles", "Profile document could not be read.", exception, ("Path", _profilesPath));
            return new ProfileLoadResult(new ProfileDocument(), ProfileLoadStatus.ReadFailure);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            AppLog.Warn("Profiles", "Profile document is not valid JSON. Preserving the original file.", exception, ("Path", _profilesPath));
            return new ProfileLoadResult(new ProfileDocument(), ProfileLoadStatus.Malformed);
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("schemaVersion", out var versionElement)
                || versionElement.ValueKind != JsonValueKind.Number
                || !versionElement.TryGetInt32(out var schemaVersion))
            {
                AppLog.Warn("Profiles", "Profile document is missing a valid schemaVersion. Preserving the original file.", null, ("Path", _profilesPath));
                return new ProfileLoadResult(new ProfileDocument(), ProfileLoadStatus.Malformed);
            }

            if (schemaVersion > ProfileDocument.CurrentSchemaVersion)
            {
                AppLog.Warn("Profiles", "Profile document schema version is newer than this build supports. Refusing to load or overwrite it.", null,
                    ("Path", _profilesPath), ("DocumentSchemaVersion", schemaVersion), ("SupportedSchemaVersion", ProfileDocument.CurrentSchemaVersion));
                return new ProfileLoadResult(new ProfileDocument(), ProfileLoadStatus.UnsupportedSchemaVersion);
            }

            if (schemaVersion < 1)
            {
                AppLog.Warn("Profiles", "Profile document schema version is invalid.", null, ("Path", _profilesPath), ("DocumentSchemaVersion", schemaVersion));
                return new ProfileLoadResult(new ProfileDocument(), ProfileLoadStatus.Malformed);
            }

            // Only schemaVersion 1 exists today; a future version 2+ reader would branch here.
            // No migration machinery for versions that do not exist yet (work order section 5).
            try
            {
                var parsed = JsonSerializer.Deserialize<ProfileDocument>(text, SerializerOptions);
                if (!HasValidStructure(parsed))
                {
                    // A syntactically valid document can still carry an explicit JSON `null` for a
                    // required container (e.g. "device": null), which property initializers do not
                    // guard against -- Deserialize happily returns a non-null ProfileDocument with a
                    // null Device/Games/nested section. Treat that exactly like malformed JSON rather
                    // than letting a caller later NullReferenceException on it.
                    AppLog.Warn("Profiles", "Profile document has invalid null structural fields. Preserving the original file.", null, ("Path", _profilesPath));
                    return new ProfileLoadResult(new ProfileDocument(), ProfileLoadStatus.Malformed);
                }
                AppLog.Debug("Profiles", "Profile document loaded.", ("Path", _profilesPath), ("GameCount", parsed!.Games.Count));
                return new ProfileLoadResult(parsed, ProfileLoadStatus.Loaded);
            }
            catch (JsonException exception)
            {
                AppLog.Warn("Profiles", "Profile document could not be deserialized. Preserving the original file.", exception, ("Path", _profilesPath));
                return new ProfileLoadResult(new ProfileDocument(), ProfileLoadStatus.Malformed);
            }
        }
    }

    /// <summary>Guards against a syntactically valid document that still carries an explicit JSON
    /// <c>null</c> for a required structural container -- property initializers only apply when
    /// the property is absent, not when it is present with value <c>null</c>. Ensures
    /// <see cref="ProfileLoadStatus.Loaded"/> always means a structurally usable typed document, so
    /// no later caller can NullReferenceException on <c>Device</c>/<c>Games</c>/a nested section.</summary>
    private static bool HasValidStructure(ProfileDocument? document) =>
        document is not null
        && document.Device is not null
        && document.Device.Performance is not null
        && document.Device.Display is not null
        && (document.Device.Performance.Tdp is not { } tdp
            || (tdp.Ac is not null && tdp.Dc is not null))
        && document.Games is not null
        && document.Games.Values.All(game =>
            game is not null
            && game.Performance is not null
            && game.Display is not null
            && (!game.Enabled
                || (game.Performance.CpuBoost is not null
                    && game.Performance.Tdp is { Ac: not null, Dc: not null })));

    /// <summary>Crash-resistant replace: serialize to a same-directory temporary file, then
    /// atomically move it over the canonical path -- mirrors <see cref="SteamInputAddonforClaw.Settings.SettingsStore.Save"/>.
    /// A failure before the move leaves the last valid persisted document untouched.</summary>
    public void Save(ProfileDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        AppLog.Debug("Profiles", "Profile document save started.", ("Path", _profilesPath), ("GameCount", document.Games.Count));

        var directory = Path.GetDirectoryName(_profilesPath) ?? throw new InvalidOperationException("The profile document path does not have a parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{_profilesPath}.tmp";
        var json = JsonSerializer.Serialize(document, SerializerOptions);
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _profilesPath, overwrite: true);

        AppLog.Debug("Profiles", "Profile document save completed.");
    }
}
