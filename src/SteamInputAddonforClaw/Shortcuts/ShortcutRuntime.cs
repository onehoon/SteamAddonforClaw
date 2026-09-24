using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
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
    string? FailureMessage = null);

/// <summary>
/// Runtime owner for the persisted Shortcut document and its currently supported external actions.
/// The document is loaded once; callers execute by TileId so action payloads never become a frontend
/// authority or a second persistence source.
/// </summary>
internal sealed class ShortcutRuntime
{
    private const int SupportedActionSchemaVersion = 1;
    private const string ShortcutUnavailableMessage = "Shortcut storage is unavailable.";
    private const string TargetUnavailableMessage = "Shortcut target is unavailable.";
    private const string UnsupportedMessage = "Shortcut action is unsupported.";
    private const string InvalidConfigurationMessage = "Shortcut configuration is invalid.";
    private const string LaunchFailedMessage = "Shortcut could not be launched.";
    private const string TileNotFoundMessage = "Shortcut tile was not found.";

    private readonly ShortcutDocument _document;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;
    private readonly Func<string, bool> _fileExists;
    private readonly bool _available;

    internal ShortcutRuntime(
        ShortcutStore store,
        Func<ProcessStartInfo, Process?>? startProcess = null,
        Func<string, bool>? fileExists = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        _startProcess = startProcess ?? Process.Start;
        _fileExists = fileExists ?? File.Exists;

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

    internal ShortcutExecutionResult Execute(Guid tileId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_available)
            return RuntimeUnavailable();

        var tile = _document.Dashboard.Tiles.FirstOrDefault(candidate => candidate.TileId == tileId);
        if (tile is null)
            return new ShortcutExecutionResult(ShortcutExecutionOutcome.NotFound, TileNotFoundMessage);

        return tile.Action.TypeId switch
        {
            ShortcutActionTypeIds.Executable => ExecuteExecutable(tile, cancellationToken),
            ShortcutActionTypeIds.PowerShell => ExecutePowerShell(tile, cancellationToken),
            ShortcutActionTypeIds.Url => ExecuteUrl(tile, cancellationToken),
            _ => new ShortcutExecutionResult(ShortcutExecutionOutcome.Unsupported, UnsupportedMessage)
        };
    }

    private TileResolution Resolve(ShortcutTileDefinition tile)
    {
        if (!IsSupportedAction(tile.Action))
            return TileResolution.Unsupported;

        return tile.Action.TypeId switch
        {
            ShortcutActionTypeIds.Executable => ResolveExecutable(tile.Action.Parameters),
            ShortcutActionTypeIds.PowerShell => ResolvePowerShell(tile.Action.Parameters),
            ShortcutActionTypeIds.Url => ResolveUrl(tile.Action.Parameters),
            _ => TileResolution.Unsupported
        };
    }

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

    private static bool TryReadExecutableParameters(JsonElement parameters, out string path, out string arguments)
    {
        path = string.Empty;
        arguments = string.Empty;

        if (!TryReadRequiredString(parameters, "path", out path)
            || !Path.IsPathFullyQualified(path)
            || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!parameters.TryGetProperty("arguments", out var argumentsElement)
            || argumentsElement.ValueKind == JsonValueKind.Null)
            return true;

        if (argumentsElement.ValueKind != JsonValueKind.String)
            return false;

        arguments = argumentsElement.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryReadPowerShellParameters(JsonElement parameters, out string script)
    {
        script = string.Empty;
        return TryReadRequiredString(parameters, "script", out script);
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
        internal static TileResolution NotFound { get; } = new(FrontendShortcutTileState.Unavailable, false, "Not found");
        internal static TileResolution Unsupported { get; } = new(FrontendShortcutTileState.Unavailable, false, "Unsupported");
        internal static TileResolution Invalid { get; } = new(FrontendShortcutTileState.Unavailable, false, "Invalid configuration");
    }
}
