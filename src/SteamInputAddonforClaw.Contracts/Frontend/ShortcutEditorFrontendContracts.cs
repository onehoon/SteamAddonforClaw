using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamInputAddonforClaw.Contracts.Frontend;

public enum FrontendShortcutEditorActionKind
{
    Executable,
    PowerShell,
    Url,
    ScreenshotFullscreen,
    Unsupported,
}

public enum FrontendShortcutMutationKind
{
    Create,
    Update,
    Delete,
    Move,
}

public sealed record FrontendShortcutEditorAction(
    FrontendShortcutEditorActionKind Kind,
    string TypeId,
    int SchemaVersion,
    bool Editable,
    string? ExecutablePath = null,
    string? ExecutableArguments = null,
    string? PowerShellScript = null,
    string? Url = null,
    bool ConfigurationValid = true,
    string? ValidationMessage = null);

public sealed record FrontendShortcutEditorTile(
    Guid TileId,
    string Title,
    string TargetSummary,
    FrontendShortcutEditorAction Action);

public sealed record FrontendScreenshotFolderSnapshot(
    bool UsingDefault,
    string EffectiveFolder,
    string? ConfiguredFolder);

public sealed record FrontendShortcutEditorSnapshot(
    bool Available,
    IReadOnlyList<FrontendShortcutEditorTile> Tiles,
    FrontendScreenshotFolderSnapshot ScreenshotFolder,
    string? FailureMessage = null)
{
    public static FrontendShortcutEditorSnapshot Unavailable(
        FrontendScreenshotFolderSnapshot screenshotFolder,
        string? message = null) => new(false, [], screenshotFolder, message);
}

public sealed record FrontendShortcutActionInput(
    FrontendShortcutEditorActionKind Kind,
    string? ExecutablePath = null,
    string? ExecutableArguments = null,
    string? PowerShellScript = null,
    string? Url = null);

public sealed record FrontendShortcutMutationIntent(
    FrontendShortcutMutationKind Kind,
    Guid? TileId = null,
    string? Title = null,
    FrontendShortcutActionInput? Action = null,
    int? TargetIndex = null);

public sealed record FrontendShortcutMutationResult(
    bool Succeeded,
    bool Changed,
    string? FailureMessage,
    FrontendShortcutEditorSnapshot Snapshot);

public sealed record FrontendScreenshotFolderMutationResult(
    bool Succeeded,
    string? FailureMessage,
    FrontendScreenshotFolderSnapshot Snapshot);

/// <summary>Shared size limits for the Main App Shortcut editor contract.</summary>
public static class FrontendShortcutEditorPayloadPolicy
{
    public const int MaxFieldUtf8Bytes = 256 * 1024;
    public const int MaxMutationBytes = 512 * 1024;
    public const int MaxSnapshotBytes = 512 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    public static bool IsFieldWithinLimit(string? value) =>
        value is null || System.Text.Encoding.UTF8.GetByteCount(value) <= MaxFieldUtf8Bytes;

    public static bool IsMutationWithinLimit(FrontendShortcutMutationIntent? intent)
    {
        if (intent is null || !MutationFieldsWithinLimit(intent)) return false;
        return IsWithinSerializedLimit(intent, MaxMutationBytes);
    }

    public static bool IsScreenshotFolderRequestWithinLimit(string? folder) =>
        IsFieldWithinLimit(folder) && IsWithinSerializedLimit(new ScreenshotFolderRequest(folder), MaxMutationBytes);

    public static bool IsSnapshotWithinLimit(FrontendShortcutEditorSnapshot? snapshot) =>
        snapshot is not null && IsWithinSerializedLimit(snapshot, MaxSnapshotBytes);

    public static bool IsMutationResultWithinLimit(FrontendShortcutMutationResult? result) =>
        result is not null && IsSnapshotWithinLimit(result.Snapshot) && IsWithinSerializedLimit(result, MaxSnapshotBytes);

    public static bool IsScreenshotFolderResultWithinLimit(FrontendScreenshotFolderMutationResult? result) =>
        result is not null && IsWithinSerializedLimit(result, MaxSnapshotBytes);

    private static bool MutationFieldsWithinLimit(FrontendShortcutMutationIntent intent) =>
        IsFieldWithinLimit(intent.Title)
        && (intent.Action is null
            || IsFieldWithinLimit(intent.Action.ExecutablePath)
            && IsFieldWithinLimit(intent.Action.ExecutableArguments)
            && IsFieldWithinLimit(intent.Action.PowerShellScript)
            && IsFieldWithinLimit(intent.Action.Url));

    private static bool IsWithinSerializedLimit<T>(T value, int limit)
    {
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions).Length <= limit;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private sealed record ScreenshotFolderRequest(string? Folder);
}
