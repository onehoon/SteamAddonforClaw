using System.Globalization;
using SteamInputAddonforClaw.HidHide;

namespace SteamInputAddonforClaw.Diagnostics;

internal interface IXbox360UsbTraceCapture
{
    Task StartAsync(string runId, CancellationToken cancellationToken);
    Task StopAsync(string runId, string reason);
}

/// <summary>Captures the Windows USB providers and usbip2_ude WPP for one rumble diagnostic run.</summary>
internal sealed class Xbox360UsbTraceCapture : IXbox360UsbTraceCapture
{
    internal const string SessionName = "SteamInputAddon-X360RumbleTrace";
    internal static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);
    private const string ProviderFileName = "usbtrace-x360-rumble-providers.txt";
    private const string WppProvider = "{ed18c9c5-8322-48ae-bf78-d01d898a1562} 0xffffffff 0xff";

    private static readonly string[] Providers =
    [
        "Microsoft-Windows-USB-USBXHCI (Default,PartialDataBusTrace)",
        "Microsoft-Windows-USB-UCX (Default,HeadersBusTrace,FullDataBusTrace,IRP,HWVerifyHost,HWVerifyHub,HWVerifyDevice)",
        "Microsoft-Windows-USB-USBHUB3 (Default,PartialDataBusTrace)",
        "Microsoft-Windows-USB-USBPORT",
        "Microsoft-Windows-USB-USBHUB",
        "Microsoft-Windows-Kernel-IoTrace 0 2",
        WppProvider
    ];

    private readonly IElevatedProcessRunner _processRunner;
    private readonly Func<string> _outputDirectoryProvider;
    private readonly Func<string> _systemDirectoryProvider;
    private readonly TimeSpan _commandTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _runId;
    private string? _outputBasePath;
    private bool _started;
    private bool _finalized;

    internal bool IsStarted => _started;
    internal string? OutputBasePath => _outputBasePath;
    internal string? FinalOutputPath { get; private set; }

    internal Xbox360UsbTraceCapture(
        IElevatedProcessRunner processRunner,
        Func<string>? outputDirectoryProvider = null,
        Func<string>? systemDirectoryProvider = null,
        TimeSpan? commandTimeout = null)
    {
        _processRunner = processRunner;
        _outputDirectoryProvider = outputDirectoryProvider ?? (static () => AppLog.DirectoryPath);
        _systemDirectoryProvider = systemDirectoryProvider ?? (static () => Environment.GetFolderPath(Environment.SpecialFolder.System));
        _commandTimeout = commandTimeout ?? CommandTimeout;
    }

    public async Task StartAsync(string runId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_runId is not null) return;
            _runId = runId;
            var outputDirectory = Path.GetFullPath(_outputDirectoryProvider());
            _outputBasePath = Path.Combine(outputDirectory,
                $"usbtrace-x360-rumble-{runId}-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.etl");

            var cleanup = await StopAndDeleteStaleSessionAsync(cancellationToken).ConfigureAwait(false);
            if (cleanup == CommandStatus.Cancelled)
            {
                LogStart("Unavailable", "UacCancelled");
                return;
            }

            Directory.CreateDirectory(outputDirectory);
            var providerFile = Path.Combine(outputDirectory, ProviderFileName);
            try
            {
                await File.WriteAllLinesAsync(providerFile, Providers, cancellationToken).ConfigureAwait(false);
                var create = await RunLogmanAsync(
                    $"create trace -n {Quote(SessionName)} -o {Quote(_outputBasePath)} -nb 128 640 -bs 128 -pf {Quote(providerFile)}",
                    cancellationToken).ConfigureAwait(false);
                if (create.Status != CommandStatus.Succeeded)
                {
                    if (create.Status != CommandStatus.Cancelled)
                        await StopAndDeleteStaleSessionAsync(CancellationToken.None).ConfigureAwait(false);
                    LogStart(create.Status == CommandStatus.Cancelled ? "Unavailable" : "Failed", create.Reason);
                    return;
                }

                var start = await RunLogmanAsync($"start -n {Quote(SessionName)}", cancellationToken).ConfigureAwait(false);
                if (start.Status != CommandStatus.Succeeded)
                {
                    if (start.Status != CommandStatus.Cancelled)
                        await StopAndDeleteStaleSessionAsync(CancellationToken.None).ConfigureAwait(false);
                    LogStart(start.Status == CommandStatus.Cancelled ? "Unavailable" : "Failed", start.Reason);
                    return;
                }

                _started = true;
                AppLog.Debug("Rumble", "Xbox360 USB trace capture started.",
                    ("Event", "X360LoopProbeUsbTraceStart"), ("RunId", runId), ("Session", SessionName),
                    ("Status", "Started"), ("OutputBase", _outputBasePath));
            }
            finally
            {
                try { File.Delete(providerFile); }
                catch { }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogStart("Unavailable", "Cancelled");
        }
        catch (Exception exception)
        {
            LogStart("Failed", exception.GetType().Name);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(string runId, string reason)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_finalized) return;
            _finalized = true;
            if (!_started)
            {
                AppLog.Debug("Rumble", "Xbox360 USB trace capture was unavailable for finalization.",
                    ("Event", "X360LoopProbeUsbTraceStop"), ("RunId", runId), ("Session", SessionName),
                    ("Status", "Unavailable"), ("OutputPath", "NotAvailable"),
                    ("Reason", string.IsNullOrWhiteSpace(reason) ? "StartUnavailable" : reason));
                return;
            }

            var stop = await RunLogmanAsync($"stop -n {Quote(SessionName)}", CancellationToken.None).ConfigureAwait(false);
            var outputPath = ResolveOutputPath(_outputBasePath);
            FinalOutputPath = outputPath;
            var delete = await RunLogmanAsync($"delete -n {Quote(SessionName)} -y", CancellationToken.None).ConfigureAwait(false);
            var success = stop.Status == CommandStatus.Succeeded &&
                          delete.Status == CommandStatus.Succeeded &&
                          outputPath is not null;
            var failureReason = success
                ? reason
                : stop.Status != CommandStatus.Succeeded ? "Stop:" + stop.Reason
                : delete.Status != CommandStatus.Succeeded ? "Delete:" + delete.Reason
                : "EtlNotFound";
            _started = false;
            AppLog.Debug("Rumble", "Xbox360 USB trace capture finalized.",
                ("Event", "X360LoopProbeUsbTraceStop"), ("RunId", runId), ("Session", SessionName),
                ("Status", success ? "Stopped" : "Failed"),
                ("OutputPath", outputPath ?? _outputBasePath ?? "NotAvailable"),
                ("Reason", failureReason ?? "Completed"));
        }
        catch (Exception exception)
        {
            FinalOutputPath = ResolveOutputPath(_outputBasePath);
            AppLog.Debug("Rumble", "Xbox360 USB trace capture finalization failed.",
                ("Event", "X360LoopProbeUsbTraceStop"), ("RunId", runId), ("Session", SessionName),
                ("Status", "Failed"), ("OutputPath", ResolveOutputPath(_outputBasePath) ?? _outputBasePath ?? "NotAvailable"),
                ("Reason", exception.GetType().Name + ":" + (reason ?? "Unknown")));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CommandStatus> StopAndDeleteStaleSessionAsync(CancellationToken cancellationToken)
    {
        var stop = await RunLogmanAsync($"stop -n {Quote(SessionName)}", cancellationToken).ConfigureAwait(false);
        if (stop.Status == CommandStatus.Cancelled) return CommandStatus.Cancelled;
        var delete = await RunLogmanAsync($"delete -n {Quote(SessionName)} -y", cancellationToken).ConfigureAwait(false);
        return delete.Status == CommandStatus.Cancelled ? CommandStatus.Cancelled : CommandStatus.Succeeded;
    }

    private async Task<CommandResult> RunLogmanAsync(string arguments, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_commandTimeout);
        try
        {
            var executable = Path.Combine(_systemDirectoryProvider(), "logman.exe");
            var result = await _processRunner.RunAsync(executable, arguments, timeout.Token)
                .WaitAsync(_commandTimeout, cancellationToken).ConfigureAwait(false);
            if (result.Kind == ElevatedProcessResultKind.CancelledBeforeStart)
                return new(CommandStatus.Cancelled, "UacCancelled");
            if (result.Kind != ElevatedProcessResultKind.Completed)
                return new(CommandStatus.Failed, result.Reason ?? result.Kind.ToString());
            return result.ExitCode == 0
                ? new(CommandStatus.Succeeded, "ExitCode:0")
                : new(CommandStatus.Failed, "ExitCode:" + (result.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "NotAvailable"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(CommandStatus.Cancelled, "Cancelled");
        }
        catch (OperationCanceledException)
        {
            return new(CommandStatus.Failed, "ProcessTimeout");
        }
        catch (TimeoutException)
        {
            return new(CommandStatus.Failed, "ProcessTimeout");
        }
        catch (Exception exception)
        {
            return new(CommandStatus.Failed, exception.GetType().Name + ":" + exception.Message);
        }
    }

    private string? ResolveOutputPath(string? outputBasePath)
    {
        if (outputBasePath is null) return null;
        try
        {
            var directory = Path.GetFullPath(Path.GetDirectoryName(outputBasePath)!);
            var root = Path.GetFullPath(_outputDirectoryProvider());
            if (!string.Equals(directory.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                return null;

            var stem = Path.GetFileNameWithoutExtension(outputBasePath);
            return Directory.EnumerateFiles(directory, stem + "*.etl")
                .Select(Path.GetFullPath)
                .Where(path => string.Equals(Path.GetDirectoryName(path), root, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private void LogStart(string status, string? reason) =>
        AppLog.Debug("Rumble", "Xbox360 USB trace capture start completed.",
            ("Event", "X360LoopProbeUsbTraceStart"), ("RunId", _runId ?? "Unknown"),
            ("Session", SessionName), ("Status", status), ("OutputBase", _outputBasePath ?? "NotAvailable"),
            ("Reason", reason ?? "None"));

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private enum CommandStatus { Succeeded, Cancelled, Failed }
    private sealed record CommandResult(CommandStatus Status, string Reason);
}
