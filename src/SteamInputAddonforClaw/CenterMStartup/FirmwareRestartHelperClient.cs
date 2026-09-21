using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterMStartup;

internal enum FirmwareRestartAuthorizationOutcome
{
    Ready,
    Cancelled,
    Failed,
}

internal sealed record FirmwareRestartAuthorizationResult(
    FirmwareRestartAuthorizationOutcome Outcome,
    IFirmwareRestartSession? Session,
    string? FailureMessage)
{
    internal bool Succeeded => Outcome == FirmwareRestartAuthorizationOutcome.Ready && Session is not null;
}

internal interface IFirmwareRestartSession : IAsyncDisposable
{
    Task<WindowsRestartRequestResult> RequestRestartAsync(CancellationToken cancellationToken);
}

/// <summary>Starts the current Runtime executable elevated, waits for its Ready response, and
/// keeps the one-shot named-pipe session open until the controller transition has been verified.
/// The elevated child cannot execute shutdown until the parent sends RestartFirmware.</summary>
internal sealed class FirmwareRestartHelperClient
{
    private const int ErrorCancelled = 1223; // ERROR_CANCELLED -- the UAC consent prompt was dismissed.
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(5);

    private readonly string _executablePath;

    internal FirmwareRestartHelperClient()
        : this(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SteamInputAddonforClaw.exe")) { }

    internal FirmwareRestartHelperClient(string executablePath) => _executablePath = executablePath;

    internal async Task<FirmwareRestartAuthorizationResult> PrepareAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_executablePath))
        {
            AppLog.Warn("CenterM.Authority", "Firmware restart helper executable was not found.", null,
                ("Path", _executablePath));
            return Failed("The elevated firmware restart helper is missing.");
        }

        var pipeName = $"SteamInputAddonforClaw.FirmwareRestart.{Environment.ProcessId}.{Guid.NewGuid():N}";
        var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        Process? process = null;
        StreamReader? reader = null;
        StreamWriter? writer = null;
        FirmwareRestartHelperSession? session = null;
        try
        {
            var startInfo = new ProcessStartInfo(_executablePath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory,
            };
            startInfo.ArgumentList.Add(FirmwareRestartHelper.Argument);
            startInfo.ArgumentList.Add(pipeName);
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The elevated firmware restart helper could not be started.");

            using var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readyTimeout.CancelAfter(ReadyTimeout);
            await pipe.WaitForConnectionAsync(readyTimeout.Token).ConfigureAwait(false);

            reader = new StreamReader(pipe, leaveOpen: true);
            writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            var line = await reader.ReadLineAsync(readyTimeout.Token).ConfigureAwait(false);
            var response = line is null ? null : JsonSerializer.Deserialize<FirmwareRestartHelperResponse>(line);
            if (response?.Status is not "Ready")
                return Failed("The elevated firmware restart helper did not become ready.");

            session = new FirmwareRestartHelperSession(pipe, reader, writer, process);
            return new(FirmwareRestartAuthorizationOutcome.Ready, session, null);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            AppLog.Info("CenterM.Authority", "Firmware restart helper elevation was cancelled by the user.");
            return new(FirmwareRestartAuthorizationOutcome.Cancelled, null,
                "Enter BIOS was cancelled before the controller transition began.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AppLog.Warn("CenterM.Authority", "Firmware restart helper authorization failed.", exception);
            return Failed("The elevated firmware restart helper could not be authorized.");
        }
        finally
        {
            if (session is null)
            {
                reader?.Dispose();
                writer?.Dispose();
                pipe.Dispose();
                DisposeProcess(process);
            }
        }
    }

    private static FirmwareRestartAuthorizationResult Failed(string message) =>
        new(FirmwareRestartAuthorizationOutcome.Failed, null, message);

    private static void DisposeProcess(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.WaitForExit(2000);
                if (!process.HasExited) process.Kill();
            }
        }
        catch { /* best effort */ }
        finally { process.Dispose(); }
    }

    private sealed class FirmwareRestartHelperSession(
        NamedPipeServerStream pipe,
        StreamReader reader,
        StreamWriter writer,
        Process process) : IFirmwareRestartSession
    {
        private int _disposed;

        public async Task<WindowsRestartRequestResult> RequestRestartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(new FirmwareRestartHelperRequest("RestartFirmware"))).ConfigureAwait(false);
                using var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                responseTimeout.CancelAfter(ResponseTimeout);
                var line = await reader.ReadLineAsync(responseTimeout.Token).ConfigureAwait(false);
                var response = line is null ? null : JsonSerializer.Deserialize<FirmwareRestartHelperResponse>(line);
                if (response?.Status == "Accepted")
                    return WindowsRestartRequestResult.Requested;

                AppLog.Warn("CenterM.Authority", "Elevated firmware restart helper rejected the restart command.", null,
                    ("ExitCode", response?.ExitCode), ("Error", response?.Error));
                return WindowsRestartRequestResult.Failed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return WindowsRestartRequestResult.Failed;
            }
            catch (Exception exception)
            {
                AppLog.Warn("CenterM.Authority", "Elevated firmware restart helper communication failed.", exception);
                return WindowsRestartRequestResult.Failed;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return ValueTask.CompletedTask;

            try { reader.Dispose(); }
            catch { /* best effort */ }
            try { writer.Dispose(); }
            catch { /* best effort */ }
            try { pipe.Dispose(); }
            catch { /* best effort */ }
            DisposeProcess(process);
            return ValueTask.CompletedTask;
        }
    }
}
