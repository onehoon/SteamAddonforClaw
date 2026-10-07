using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SteamInputAddonforClaw.WindowsGaming;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.Shortcuts;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Shortcuts;

internal enum ShortcutExecutionOutcome
{
    Succeeded,
    NotFound,
    Unsupported,
    InvalidConfiguration,
    Unavailable,
    Failed
}

internal sealed record ShortcutExecutionResult(
    ShortcutExecutionOutcome Outcome,
    string? FailureMessage = null,
    bool RetireOverlayAfterExecution = false);

/// <summary>
/// Runtime owner for the persisted Shortcut document and its currently supported external actions.
/// Frontends edit through typed intents; callers execute by TileId so action payloads never become
/// execution authority or a second persistence source.
/// </summary>
internal sealed class ShortcutRuntime
{
    private const int MaxSafeProcessCommandLineChars = 30_000;
    private const int SupportedActionSchemaVersion = 1;
    private const string ShortcutUnavailableMessage = "Shortcut storage is unavailable.";
    private const string EditorTooLargeMessage = "Shortcut configuration is too large to edit in this version.";
    private const string SaveFailedMessage = "Failed to save Shortcut changes.";
    private const string TargetUnavailableMessage = "Shortcut target is unavailable.";
    private const string UnsupportedMessage = "Shortcut action is unsupported.";
    private const string InvalidConfigurationMessage = "Shortcut configuration is invalid.";
    private const string LaunchFailedMessage = "Shortcut could not be launched.";
    private const string TileNotFoundMessage = "Shortcut tile was not found.";
    private const string ExecutableTooLongMessage = "Executable path or arguments are too long to run.";
    private const string PowerShellTooLongMessage = "PowerShell script is too long to run.";

    private readonly ShortcutStore _store;
    private readonly Action<ShortcutDocument>? _saveDocument;
    private ShortcutDocument _document;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string?, CancellationToken, Task<ShortcutExecutionResult>>? _screenshotAction;
    private readonly bool _available;

    internal ShortcutRuntime(
        ShortcutStore store,
        Func<ProcessStartInfo, Process?>? startProcess = null,
        Func<string, bool>? fileExists = null,
        Func<string?, CancellationToken, Task<ShortcutExecutionResult>>? screenshotAction = null,
        Action<ShortcutDocument>? saveDocument = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        _startProcess = startProcess ?? Process.Start;
        _fileExists = fileExists ?? File.Exists;
        _screenshotAction = screenshotAction;
        _store = store;
        _saveDocument = saveDocument;

        var load = store.Load();
        _available = load.Status is ShortcutLoadStatus.Loaded or ShortcutLoadStatus.NotFound;
        _document = load.Document;

        if (!_available)
        {
            AppLog.Warn("Shortcuts", "Shortcut Runtime is unavailable; continuing without Shortcut actions.", null,
                ("Status", load.Status));
        }
    }

    internal FrontendShortcutDashboardSnapshot Capture()
    {
        if (!_available)
            return FrontendShortcutDashboardSnapshot.Unavailable(ShortcutUnavailableMessage);

        var tiles = new List<FrontendShortcutTile>(_document.Dashboard.Tiles.Count);
        foreach (var tile in _document.Dashboard.Tiles)
        {
            var resolution = Resolve(tile);
            tiles.Add(new FrontendShortcutTile(
                tile.TileId,
                tile.Title,
                resolution.StatusText,
                resolution.State,
                resolution.Enabled));
        }

        return new FrontendShortcutDashboardSnapshot(true, tiles);
    }

    internal FrontendShortcutEditorSnapshot CaptureEditor()
    {
        if (!_available)
            return FrontendShortcutEditorSnapshot.Unavailable(ShortcutUnavailableMessage);

        var snapshot = ProjectEditorSnapshot(_document);
        return FrontendShortcutEditorPayloadPolicy.IsSnapshotWithinLimit(snapshot)
            ? snapshot
            : FrontendShortcutEditorSnapshot.Unavailable(EditorTooLargeMessage);
    }

    internal FrontendShortcutMutationResult MutateEditor(FrontendShortcutMutationIntent? intent)
    {
        if (!FrontendShortcutEditorPayloadPolicy.IsMutationWithinLimit(intent))
            return MutationFailure("Shortcut edit is too large or invalid.");
        if (!_available)
            return MutationFailure(ShortcutUnavailableMessage);
        if (!FrontendShortcutEditorPayloadPolicy.IsSnapshotWithinLimit(ProjectEditorSnapshot(_document)))
            return MutationFailure(EditorTooLargeMessage);

        if (!TryBuildMutation(intent!, out var candidate, out var changed, out var failureMessage))
            return MutationFailure(failureMessage!);

        if (!changed)
            return CreateMutationResult(true, false, null, CaptureEditor());

        var candidateSnapshot = ProjectEditorSnapshot(candidate!);
        var candidateResult = new FrontendShortcutMutationResult(true, true, null, candidateSnapshot);
        if (!FrontendShortcutEditorPayloadPolicy.IsSnapshotWithinLimit(candidateSnapshot)
            || !FrontendShortcutEditorPayloadPolicy.IsMutationResultWithinLimit(candidateResult))
        {
            return MutationFailure(EditorTooLargeMessage);
        }

        try
        {
            if (_saveDocument is { } saveDocument) saveDocument(candidate!);
            else _store.Save(candidate!);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Shortcuts", "Shortcut document save failed.", null,
                ("Mutation", intent!.Kind),
                ("FailureCategory", exception.GetType().Name));
            return MutationFailure(SaveFailedMessage);
        }

        _document = candidate!;
        AppLog.Info("Shortcuts", "Shortcut document changed.",
            ("Mutation", intent!.Kind),
            ("TileCount", _document.Dashboard.Tiles.Count));
        return candidateResult;
    }

    private bool TryBuildMutation(
        FrontendShortcutMutationIntent intent,
        out ShortcutDocument? candidate,
        out bool changed,
        out string? failureMessage)
    {
        candidate = null;
        changed = false;
        failureMessage = null;
        var tiles = _document.Dashboard.Tiles.ToList();

        switch (intent.Kind)
        {
            case FrontendShortcutMutationKind.Create:
            {
                if (intent.TileId is not null || intent.Title is null || intent.Action is null
                    || intent.CloseOverlayAfterLaunch is null || intent.TargetIndex is not null)
                    return Fail("Shortcut create request is invalid.", out failureMessage);
                if (!IsValidTitle(intent.Title))
                    return Fail("Enter a valid Shortcut title.", out failureMessage);
                if (!TryBuildAction(intent.Action, out var action, out failureMessage)) return false;
                if (IsScreenshotAction(action!) && tiles.Any(IsScreenshotAction))
                    return Fail("Only one Screenshot Shortcut can be added.", out failureMessage);
                tiles.Add(new ShortcutTileDefinition(Guid.NewGuid(), intent.Title,
                    IsScreenshotAction(action!) ? false : intent.CloseOverlayAfterLaunch.Value, action!));
                changed = true;
                break;
            }
            case FrontendShortcutMutationKind.Update:
            {
                if (intent.TileId is not { } tileId || tileId == Guid.Empty || intent.Title is null
                    || intent.Action is null || intent.CloseOverlayAfterLaunch is null || intent.TargetIndex is not null)
                    return Fail("Shortcut update request is invalid.", out failureMessage);
                if (!IsValidTitle(intent.Title))
                    return Fail("Enter a valid Shortcut title.", out failureMessage);
                var index = tiles.FindIndex(tile => tile.TileId == tileId);
                if (index < 0) return Fail("Shortcut tile was not found.", out failureMessage);
                if (!ProjectEditorAction(tiles[index].Action).Editable)
                    return Fail("This Shortcut action cannot be edited in this version.", out failureMessage);
                if (!TryBuildAction(intent.Action, out var action, out failureMessage)) return false;
                if (IsScreenshotAction(action!)
                    && tiles.Any(tile => tile.TileId != tileId && IsScreenshotAction(tile)))
                    return Fail("Only one Screenshot Shortcut can be added.", out failureMessage);
                tiles[index] = tiles[index] with
                {
                    Title = intent.Title,
                    CloseOverlayAfterLaunch = IsScreenshotAction(action!) ? false : intent.CloseOverlayAfterLaunch.Value,
                    Action = action!
                };
                changed = true;
                break;
            }
            case FrontendShortcutMutationKind.Delete:
            {
                if (intent.TileId is not { } tileId || tileId == Guid.Empty || intent.Title is not null
                    || intent.Action is not null || intent.CloseOverlayAfterLaunch is not null || intent.TargetIndex is not null)
                    return Fail("Shortcut delete request is invalid.", out failureMessage);
                var index = tiles.FindIndex(tile => tile.TileId == tileId);
                if (index < 0) return Fail("Shortcut tile was not found.", out failureMessage);
                tiles.RemoveAt(index);
                changed = true;
                break;
            }
            case FrontendShortcutMutationKind.Move:
            {
                if (intent.TileId is not { } tileId || tileId == Guid.Empty || intent.Title is not null
                    || intent.Action is not null || intent.CloseOverlayAfterLaunch is not null
                    || intent.TargetIndex is not { } targetIndex)
                    return Fail("Shortcut move request is invalid.", out failureMessage);
                var currentIndex = tiles.FindIndex(tile => tile.TileId == tileId);
                if (currentIndex < 0) return Fail("Shortcut tile was not found.", out failureMessage);
                if (targetIndex < 0 || targetIndex >= tiles.Count)
                    return Fail("Shortcut order is invalid.", out failureMessage);
                if (currentIndex == targetIndex) return true;
                var tile = tiles[currentIndex];
                tiles.RemoveAt(currentIndex);
                tiles.Insert(targetIndex, tile);
                changed = true;
                break;
            }
            default:
                return Fail("Shortcut edit request is invalid.", out failureMessage);
        }

        var dashboard = new ShortcutDashboardDefinition(tiles);
        if (ShortcutDefinitionValidation.Validate(dashboard) is not null)
            return Fail("Shortcut definition is invalid.", out failureMessage);

        candidate = _document with { Dashboard = dashboard };
        if (!FrontendShortcutEditorPayloadPolicy.IsSnapshotWithinLimit(ProjectEditorSnapshot(candidate)))
            return Fail(EditorTooLargeMessage, out failureMessage);
        return true;
    }

    private static FrontendShortcutEditorSnapshot ProjectEditorSnapshot(ShortcutDocument document) =>
        new(true, document.Dashboard.Tiles.Select(tile => new FrontendShortcutEditorTile(
            tile.TileId,
            tile.Title,
            GetEditorTargetSummary(tile.Action),
            tile.CloseOverlayAfterLaunch,
            ProjectEditorAction(tile.Action))).ToArray());

    private static FrontendShortcutEditorAction ProjectEditorAction(ShortcutActionSpec action)
    {
        var kind = GetEditorActionKind(action.TypeId);
        if (kind is null || action.SchemaVersion != SupportedActionSchemaVersion)
            return new(FrontendShortcutEditorActionKind.Unsupported, action.TypeId, action.SchemaVersion, false,
                ConfigurationValid: false, ValidationMessage: "Unsupported in this version.");

        var executablePath = ReadStringOrEmpty(action.Parameters, "path");
        var executableArguments = ReadStringOrEmpty(action.Parameters, "arguments");
        var script = ReadStringOrEmpty(action.Parameters, "script");
        var url = ReadStringOrEmpty(action.Parameters, "url");
        var screenshotParametersValid = TryReadScreenshotParameters(action.Parameters, out var screenshotFolder);
        var valid = kind.Value switch
        {
            FrontendShortcutEditorActionKind.Executable => TryReadExecutableParameters(action.Parameters, out _, out _),
            FrontendShortcutEditorActionKind.PowerShell => TryReadPowerShellParameters(action.Parameters, out _),
            FrontendShortcutEditorActionKind.Url => TryReadUrlParameters(action.Parameters, out _),
            FrontendShortcutEditorActionKind.ScreenshotFullscreen => screenshotParametersValid,
            FrontendShortcutEditorActionKind.SteamBigPicture or
            FrontendShortcutEditorActionKind.SteamClient or
            FrontendShortcutEditorActionKind.XboxApp => HasEmptyObjectParameters(action.Parameters),
            _ => false
        };

        return new(kind.Value, action.TypeId, action.SchemaVersion, true,
            kind == FrontendShortcutEditorActionKind.Executable ? executablePath : null,
            kind == FrontendShortcutEditorActionKind.Executable ? executableArguments : null,
            kind == FrontendShortcutEditorActionKind.PowerShell ? script : null,
            kind == FrontendShortcutEditorActionKind.Url ? url : null,
            valid,
            valid ? null : "Needs attention. Review this action's configuration.",
            kind == FrontendShortcutEditorActionKind.ScreenshotFullscreen && screenshotParametersValid
                ? screenshotFolder
                : null);
    }

    private static FrontendShortcutEditorActionKind? GetEditorActionKind(string typeId) => typeId switch
    {
        ShortcutActionTypeIds.Executable => FrontendShortcutEditorActionKind.Executable,
        ShortcutActionTypeIds.PowerShell => FrontendShortcutEditorActionKind.PowerShell,
        ShortcutActionTypeIds.Url => FrontendShortcutEditorActionKind.Url,
        ShortcutActionTypeIds.ScreenshotFullscreen => FrontendShortcutEditorActionKind.ScreenshotFullscreen,
        ShortcutActionTypeIds.SteamBigPicture => FrontendShortcutEditorActionKind.SteamBigPicture,
        ShortcutActionTypeIds.SteamClient => FrontendShortcutEditorActionKind.SteamClient,
        ShortcutActionTypeIds.XboxApp => FrontendShortcutEditorActionKind.XboxApp,
        _ => null
    };

    private static string GetEditorTargetSummary(ShortcutActionSpec action)
    {
        var kind = GetEditorActionKind(action.TypeId);
        if (kind is null || action.SchemaVersion != SupportedActionSchemaVersion)
            return $"Unsupported in this version · {action.TypeId}";

        return kind.Value switch
        {
            FrontendShortcutEditorActionKind.Executable =>
                SafeExecutableName(ReadStringOrEmpty(action.Parameters, "path")),
            FrontendShortcutEditorActionKind.PowerShell => "PowerShell",
            FrontendShortcutEditorActionKind.Url => Uri.TryCreate(ReadStringOrEmpty(action.Parameters, "url"), UriKind.Absolute, out var uri)
                ? uri.Host : "Website",
            FrontendShortcutEditorActionKind.ScreenshotFullscreen => "Fullscreen screenshot",
            FrontendShortcutEditorActionKind.SteamBigPicture => "Steam Big Picture",
            FrontendShortcutEditorActionKind.SteamClient => "Steam client",
            FrontendShortcutEditorActionKind.XboxApp => "Xbox app",
            _ => "Unsupported in this version"
        };
    }

    private static string SafeExecutableName(string path)
    {
        try { return Path.GetFileName(path) is { Length: > 0 } name ? name : "Application"; }
        catch (ArgumentException) { return "Application"; }
    }

    private static string ReadStringOrEmpty(JsonElement parameters, string propertyName) =>
        parameters.ValueKind == JsonValueKind.Object
        && parameters.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool IsValidTitle(string title) =>
        !string.IsNullOrWhiteSpace(title) && FrontendShortcutEditorPayloadPolicy.IsFieldWithinLimit(title);

    private static bool TryBuildAction(
        FrontendShortcutActionInput input,
        out ShortcutActionSpec? action,
        out string? failureMessage)
    {
        action = null;
        failureMessage = null;
        if (input.Kind != FrontendShortcutEditorActionKind.ScreenshotFullscreen && input.ScreenshotFolder is not null)
        {
            failureMessage = "Shortcut action input is invalid.";
            return false;
        }

        if (input.Kind == FrontendShortcutEditorActionKind.Executable
            && input.PowerShellScript is null && input.Url is null
            && !string.IsNullOrWhiteSpace(input.ExecutablePath)
            && IsValidExecutablePath(input.ExecutablePath))
        {
            var parameters = JsonSerializer.SerializeToElement(new
            {
                path = input.ExecutablePath,
                arguments = input.ExecutableArguments
            });
            if (!TryReadExecutableParameters(parameters, out _, out _))
            {
                failureMessage = ExecutableTooLongMessage;
                return false;
            }

            action = new(ShortcutActionTypeIds.Executable, SupportedActionSchemaVersion,
                parameters);
            return true;
        }

        if (input.Kind == FrontendShortcutEditorActionKind.PowerShell
            && input.ExecutablePath is null && input.ExecutableArguments is null && input.Url is null
            && !string.IsNullOrWhiteSpace(input.PowerShellScript))
        {
            var parameters = JsonSerializer.SerializeToElement(new { script = input.PowerShellScript });
            if (!TryReadPowerShellParameters(parameters, out _))
            {
                failureMessage = PowerShellTooLongMessage;
                return false;
            }

            action = new(ShortcutActionTypeIds.PowerShell, SupportedActionSchemaVersion,
                parameters);
            return true;
        }

        if (input.Kind == FrontendShortcutEditorActionKind.Url
            && input.ExecutablePath is null && input.ExecutableArguments is null && input.PowerShellScript is null
            && TryReadUrlInput(input.Url, out var uri))
        {
            action = new(ShortcutActionTypeIds.Url, SupportedActionSchemaVersion,
                JsonSerializer.SerializeToElement(new { url = uri!.AbsoluteUri }));
            return true;
        }

        if (input.Kind == FrontendShortcutEditorActionKind.ScreenshotFullscreen
            && input.ExecutablePath is null && input.ExecutableArguments is null
            && input.PowerShellScript is null && input.Url is null)
        {
            if (!string.IsNullOrWhiteSpace(input.ScreenshotFolder)
                && (!FrontendShortcutEditorPayloadPolicy.IsFieldWithinLimit(input.ScreenshotFolder)
                    || !IsValidScreenshotFolder(input.ScreenshotFolder)))
            {
                failureMessage = "Choose a fully qualified folder path.";
                return false;
            }

            var parameters = string.IsNullOrWhiteSpace(input.ScreenshotFolder)
                ? JsonSerializer.SerializeToElement(new { })
                : JsonSerializer.SerializeToElement(new { folder = input.ScreenshotFolder });
            action = new(ShortcutActionTypeIds.ScreenshotFullscreen, SupportedActionSchemaVersion,
                parameters);
            return true;
        }

        if ((input.Kind == FrontendShortcutEditorActionKind.SteamBigPicture
                || input.Kind == FrontendShortcutEditorActionKind.SteamClient
                || input.Kind == FrontendShortcutEditorActionKind.XboxApp)
            && input.ExecutablePath is null && input.ExecutableArguments is null
            && input.PowerShellScript is null && input.Url is null)
        {
            var typeId = input.Kind switch
            {
                FrontendShortcutEditorActionKind.SteamBigPicture => ShortcutActionTypeIds.SteamBigPicture,
                FrontendShortcutEditorActionKind.SteamClient => ShortcutActionTypeIds.SteamClient,
                _ => ShortcutActionTypeIds.XboxApp
            };
            action = new(typeId, SupportedActionSchemaVersion, JsonSerializer.SerializeToElement(new { }));
            return true;
        }

        failureMessage = input.Kind switch
        {
            FrontendShortcutEditorActionKind.Executable => "Enter a fully qualified .exe path.",
            FrontendShortcutEditorActionKind.PowerShell => "Enter a PowerShell script.",
            FrontendShortcutEditorActionKind.Url => "Enter an absolute http or https URL.",
            FrontendShortcutEditorActionKind.ScreenshotFullscreen => "Screenshot action input is invalid.",
            FrontendShortcutEditorActionKind.SteamBigPicture or
            FrontendShortcutEditorActionKind.SteamClient or
            FrontendShortcutEditorActionKind.XboxApp => "Built-in Shortcut action input is invalid.",
            _ => "Unsupported Shortcut action."
        };
        return false;
    }

    private static bool TryReadUrlInput(string? value, out Uri? uri)
    {
        uri = null;
        return !string.IsNullOrWhiteSpace(value)
            && Uri.TryCreate(value, UriKind.Absolute, out uri)
            && !string.IsNullOrWhiteSpace(uri.Host)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsValidExecutablePath(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path)
                && string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private FrontendShortcutMutationResult MutationFailure(string message) =>
        CreateMutationResult(false, false, message, CaptureEditor());

    private static FrontendShortcutMutationResult CreateMutationResult(
        bool succeeded,
        bool changed,
        string? failureMessage,
        FrontendShortcutEditorSnapshot snapshot)
    {
        var result = new FrontendShortcutMutationResult(succeeded, changed, failureMessage, snapshot);
        if (FrontendShortcutEditorPayloadPolicy.IsMutationResultWithinLimit(result)) return result;
        var unavailable = FrontendShortcutEditorSnapshot.Unavailable(EditorTooLargeMessage);
        return new(false, false, EditorTooLargeMessage, unavailable);
    }

    private static bool Fail(string message, out string? failureMessage)
    {
        failureMessage = message;
        return false;
    }

    internal async Task<ShortcutExecutionResult> ExecuteAsync(Guid tileId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_available)
            return RuntimeUnavailable();

        var tile = _document.Dashboard.Tiles.FirstOrDefault(candidate => candidate.TileId == tileId);
        if (tile is null)
            return new ShortcutExecutionResult(ShortcutExecutionOutcome.NotFound, TileNotFoundMessage);

        if (string.Equals(tile.Action.TypeId, ShortcutActionTypeIds.ScreenshotFullscreen, StringComparison.Ordinal))
            return await ExecuteScreenshotAsync(tile, cancellationToken).ConfigureAwait(false);

        var result = tile.Action.TypeId switch
        {
            ShortcutActionTypeIds.Executable => ExecuteExecutable(tile, cancellationToken),
            ShortcutActionTypeIds.PowerShell => ExecutePowerShell(tile, cancellationToken),
            ShortcutActionTypeIds.Url => ExecuteUrl(tile, cancellationToken),
            ShortcutActionTypeIds.SteamBigPicture => ExecuteParameterlessBuiltIn(tile,
                new ProcessStartInfo { FileName = "steam://open/bigpicture", UseShellExecute = true }, cancellationToken),
            ShortcutActionTypeIds.SteamClient => ExecuteParameterlessBuiltIn(tile,
                new ProcessStartInfo { FileName = "steam://open/main", UseShellExecute = true }, cancellationToken),
            ShortcutActionTypeIds.XboxApp => ExecuteParameterlessBuiltIn(tile,
                new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"shell:AppsFolder\\{XboxGamingHomeAppIdentity.Aumid}",
                    UseShellExecute = true
                }, cancellationToken),
            _ => new ShortcutExecutionResult(ShortcutExecutionOutcome.Unsupported, UnsupportedMessage)
        };

        return result.Outcome == ShortcutExecutionOutcome.Succeeded && tile.CloseOverlayAfterLaunch
            ? result with { RetireOverlayAfterExecution = true }
            : result;
    }

    private TileResolution Resolve(ShortcutTileDefinition tile)
    {
        if (string.Equals(tile.Action.TypeId, ShortcutActionTypeIds.ScreenshotFullscreen, StringComparison.Ordinal))
        {
            if (tile.Action.SchemaVersion != SupportedActionSchemaVersion)
                return TileResolution.Unsupported;
            if (!TryReadScreenshotParameters(tile.Action.Parameters, out _))
                return TileResolution.Invalid;
            return _screenshotAction is null ? TileResolution.Unavailable : TileResolution.Available;
        }

        if (!IsSupportedAction(tile.Action))
        {
            if (!IsParameterlessBuiltIn(tile.Action.TypeId))
                return TileResolution.Unsupported;
            if (tile.Action.SchemaVersion != SupportedActionSchemaVersion)
                return TileResolution.Unsupported;
            return HasEmptyObjectParameters(tile.Action.Parameters)
                ? TileResolution.Available
                : TileResolution.Invalid;
        }

        return tile.Action.TypeId switch
        {
            ShortcutActionTypeIds.Executable => ResolveExecutable(tile.Action.Parameters),
            ShortcutActionTypeIds.PowerShell => ResolvePowerShell(tile.Action.Parameters),
            ShortcutActionTypeIds.Url => ResolveUrl(tile.Action.Parameters),
            _ => TileResolution.Unsupported
        };
    }

    private async Task<ShortcutExecutionResult> ExecuteScreenshotAsync(
        ShortcutTileDefinition tile,
        CancellationToken cancellationToken)
    {
        if (tile.Action.SchemaVersion != SupportedActionSchemaVersion)
            return new ShortcutExecutionResult(ShortcutExecutionOutcome.Unsupported, UnsupportedMessage);
        if (!TryReadScreenshotParameters(tile.Action.Parameters, out var folder))
            return InvalidConfiguration();
        if (_screenshotAction is null)
            return new ShortcutExecutionResult(ShortcutExecutionOutcome.Unavailable, "Screenshot is unavailable.");

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = await _screenshotAction(folder, cancellationToken).ConfigureAwait(false);
            return result with { RetireOverlayAfterExecution = false };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            AppLog.Warn("Shortcuts", "Screenshot action failed.", null,
                ("TypeId", tile.Action.TypeId),
                ("Outcome", ShortcutExecutionOutcome.Failed),
                ("ExceptionType", exception.GetType().Name));
            return new ShortcutExecutionResult(ShortcutExecutionOutcome.Failed, "Screenshot could not be saved.");
        }
    }

    private static bool HasEmptyObjectParameters(JsonElement parameters) =>
        parameters.ValueKind == JsonValueKind.Object && !parameters.EnumerateObject().Any();

    private static bool TryReadScreenshotParameters(JsonElement parameters, out string? folder)
    {
        folder = null;
        if (parameters.ValueKind != JsonValueKind.Object) return false;

        using var properties = parameters.EnumerateObject().GetEnumerator();
        if (!properties.MoveNext()) return true;

        var property = properties.Current;
        if (!string.Equals(property.Name, "folder", StringComparison.Ordinal)
            || property.Value.ValueKind != JsonValueKind.String
            || properties.MoveNext())
            return false;

        var configuredFolder = property.Value.GetString();
        if (string.IsNullOrWhiteSpace(configuredFolder)
            || !FrontendShortcutEditorPayloadPolicy.IsFieldWithinLimit(configuredFolder)
            || !IsValidScreenshotFolder(configuredFolder))
            return false;

        folder = configuredFolder;
        return true;
    }

    private static bool IsValidScreenshotFolder(string folder)
    {
        try { return Path.IsPathFullyQualified(folder); }
        catch (ArgumentException) { return false; }
    }

    private static bool IsScreenshotAction(ShortcutTileDefinition tile) =>
        string.Equals(tile.Action.TypeId, ShortcutActionTypeIds.ScreenshotFullscreen, StringComparison.Ordinal);

    private static bool IsScreenshotAction(ShortcutActionSpec action) =>
        string.Equals(action.TypeId, ShortcutActionTypeIds.ScreenshotFullscreen, StringComparison.Ordinal);

    private ShortcutExecutionResult ExecuteExecutable(ShortcutTileDefinition tile, CancellationToken cancellationToken)
    {
        if (!IsSupportedAction(tile.Action))
            return new ShortcutExecutionResult(ShortcutExecutionOutcome.Unsupported, UnsupportedMessage);

        if (!TryReadExecutableParameters(tile.Action.Parameters, out var path, out var arguments))
            return InvalidConfiguration();

        if (!_fileExists(path))
            return Unavailable();

        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            Arguments = arguments,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty
        };

        return StartExternal(tile, startInfo, cancellationToken);
    }

    private ShortcutExecutionResult ExecutePowerShell(ShortcutTileDefinition tile, CancellationToken cancellationToken)
    {
        if (!IsSupportedAction(tile.Action))
            return new ShortcutExecutionResult(ShortcutExecutionOutcome.Unsupported, UnsupportedMessage);

        if (!TryReadPowerShellParameters(tile.Action.Parameters, out var script))
            return InvalidConfiguration();

        cancellationToken.ThrowIfCancellationRequested();

        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo
        {
            FileName = WindowsPowerShellPath(),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(encodedScript);

        return StartExternal(tile, startInfo, cancellationToken);
    }

    private ShortcutExecutionResult ExecuteUrl(ShortcutTileDefinition tile, CancellationToken cancellationToken)
    {
        if (!IsSupportedAction(tile.Action))
            return new ShortcutExecutionResult(ShortcutExecutionOutcome.Unsupported, UnsupportedMessage);

        if (!TryReadUrlParameters(tile.Action.Parameters, out var url))
            return InvalidConfiguration();

        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        };

        return StartExternal(tile, startInfo, cancellationToken);
    }

    private ShortcutExecutionResult ExecuteParameterlessBuiltIn(
        ShortcutTileDefinition tile,
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        if (tile.Action.SchemaVersion != SupportedActionSchemaVersion)
            return new ShortcutExecutionResult(ShortcutExecutionOutcome.Unsupported, UnsupportedMessage);
        if (!HasEmptyObjectParameters(tile.Action.Parameters))
            return InvalidConfiguration();

        return StartExternal(tile, startInfo, cancellationToken);
    }

    private ShortcutExecutionResult StartExternal(
        ShortcutTileDefinition tile,
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var process = _startProcess(startInfo);
            return process is null
                ? LogLaunchFailure(tile, ShortcutExecutionOutcome.Failed, null)
                : new ShortcutExecutionResult(ShortcutExecutionOutcome.Succeeded);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FileNotFoundException exception)
        {
            return LogLaunchFailure(tile, ShortcutExecutionOutcome.Unavailable, exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            return LogLaunchFailure(tile, ShortcutExecutionOutcome.Unavailable, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return LogLaunchFailure(tile, ShortcutExecutionOutcome.Failed, exception);
        }
        catch (Win32Exception exception)
        {
            return LogLaunchFailure(tile, ShortcutExecutionOutcome.Failed, exception);
        }
        catch (InvalidOperationException exception)
        {
            return LogLaunchFailure(tile, ShortcutExecutionOutcome.Failed, exception);
        }
        catch (Exception exception)
        {
            return LogLaunchFailure(tile, ShortcutExecutionOutcome.Failed, exception);
        }
    }

    private ShortcutExecutionResult LogLaunchFailure(
        ShortcutTileDefinition tile,
        ShortcutExecutionOutcome outcome,
        Exception? exception)
    {
        AppLog.Warn("Shortcuts", "Shortcut action launch failed.", null,
            ("TileId", tile.TileId),
            ("TypeId", tile.Action.TypeId),
            ("Outcome", outcome),
            ("ExceptionType", exception?.GetType().Name ?? "None"));

        return new ShortcutExecutionResult(
            outcome,
            outcome == ShortcutExecutionOutcome.Unavailable ? TargetUnavailableMessage : LaunchFailedMessage);
    }

    private TileResolution ResolveExecutable(JsonElement parameters)
    {
        if (!TryReadExecutableParameters(parameters, out var path, out _))
            return TileResolution.Invalid;

        return _fileExists(path) ? TileResolution.Available : TileResolution.NotFound;
    }

    private static TileResolution ResolvePowerShell(JsonElement parameters) =>
        TryReadPowerShellParameters(parameters, out _) ? TileResolution.Available : TileResolution.Invalid;

    private static TileResolution ResolveUrl(JsonElement parameters) =>
        TryReadUrlParameters(parameters, out _) ? TileResolution.Available : TileResolution.Invalid;

    private static bool IsSupportedAction(ShortcutActionSpec action) =>
        action.SchemaVersion == SupportedActionSchemaVersion
        && action.TypeId is ShortcutActionTypeIds.Executable or ShortcutActionTypeIds.PowerShell or ShortcutActionTypeIds.Url;

    private static bool IsParameterlessBuiltIn(string typeId) =>
        typeId is ShortcutActionTypeIds.SteamBigPicture or ShortcutActionTypeIds.SteamClient or ShortcutActionTypeIds.XboxApp;

    private static bool TryReadExecutableParameters(JsonElement parameters, out string path, out string arguments)
    {
        path = string.Empty;
        arguments = string.Empty;

        if (!TryReadRequiredString(parameters, "path", out path)
            || !IsValidExecutablePath(path))
        {
            return false;
        }

        if (!parameters.TryGetProperty("arguments", out var argumentsElement)
            || argumentsElement.ValueKind == JsonValueKind.Null)
            return FitsExecutableCommandLine(path, arguments);

        if (argumentsElement.ValueKind != JsonValueKind.String)
            return false;

        arguments = argumentsElement.GetString() ?? string.Empty;
        return FitsExecutableCommandLine(path, arguments);
    }

    private static bool TryReadPowerShellParameters(JsonElement parameters, out string script)
    {
        script = string.Empty;
        return TryReadRequiredString(parameters, "script", out script)
            && FitsPowerShellCommandLine(script);
    }

    private static bool FitsExecutableCommandLine(string executablePath, string arguments) =>
        (long)executablePath.Length + arguments.Length + 4 <= MaxSafeProcessCommandLineChars;

    private static bool FitsPowerShellCommandLine(string script)
    {
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var fixedArgumentChars = "-NoLogo".Length
            + "-NoProfile".Length
            + "-NonInteractive".Length
            + "-ExecutionPolicy".Length
            + "Bypass".Length
            + "-EncodedCommand".Length;
        var estimatedChars = (long)WindowsPowerShellPath().Length
            + encodedScript.Length
            + fixedArgumentChars
            + 32;
        return estimatedChars <= MaxSafeProcessCommandLineChars;
    }

    private static bool TryReadUrlParameters(JsonElement parameters, out string url)
    {
        url = string.Empty;
        if (!TryReadRequiredString(parameters, "url", out url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || string.IsNullOrWhiteSpace(uri.Host)
            || (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    private static bool TryReadRequiredString(JsonElement parameters, string propertyName, out string value)
    {
        value = string.Empty;
        if (!parameters.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
            return false;

        value = element.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string WindowsPowerShellPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

    private static ShortcutExecutionResult Unavailable() =>
        new(ShortcutExecutionOutcome.Unavailable, TargetUnavailableMessage);

    private static ShortcutExecutionResult RuntimeUnavailable() =>
        new(ShortcutExecutionOutcome.Unavailable, ShortcutUnavailableMessage);

    private static ShortcutExecutionResult InvalidConfiguration() =>
        new(ShortcutExecutionOutcome.InvalidConfiguration, InvalidConfigurationMessage);

    private readonly record struct TileResolution(
        FrontendShortcutTileState State,
        bool Enabled,
        string? StatusText)
    {
        internal static TileResolution Available { get; } = new(FrontendShortcutTileState.Neutral, true, null);
        internal static TileResolution Unavailable { get; } = new(FrontendShortcutTileState.Unavailable, false, "Unavailable");
        internal static TileResolution NotFound { get; } = new(FrontendShortcutTileState.Unavailable, false, "Not found");
        internal static TileResolution Unsupported { get; } = new(FrontendShortcutTileState.Unavailable, false, "Unsupported");
        internal static TileResolution Invalid { get; } = new(FrontendShortcutTileState.Unavailable, false, "Invalid configuration");
    }
}
