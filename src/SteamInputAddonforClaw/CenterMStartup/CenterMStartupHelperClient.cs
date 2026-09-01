using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterMStartup;

internal enum CenterMStartupHelperOutcome
{
    /// <summary>The privileged helper ran and returned a result.</summary>
    Completed,
    /// <summary>The user dismissed the UAC elevation prompt before the helper could run.</summary>
    Cancelled,
    /// <summary>The helper could not be launched or did not respond.</summary>
    HelperUnavailable,
}

internal sealed record CenterMStartupHelperResult(
    CenterMStartupHelperOutcome Outcome,
    bool Ok,
    /// <summary>True only when the helper actually observed all three roots. When false the
    /// three values below are placeholders ("not observed"), NOT "observed disabled" -- callers must
    /// treat that as <see cref="Contracts.Frontend.FrontendCenterMStartupState.Unavailable"/>
    /// (Addendum E), never as a real Disabled snapshot.</summary>
    bool SnapshotAvailable,
    bool ServerTaskEnabled,
    bool UpdaterTaskEnabled,
    CenterMFoundationServiceMode FoundationServiceMode,
    string? Error);

/// <summary>Runs one privileged MSI Center M startup mutation via
/// <c>SteamInputAddonforClaw.CenterMStartupHelper.exe</c> (work order PR1 / PR1 Addendum A). Mirrors
/// the <see cref="Devices.MSI.Claw.TdpHelperClient"/> pattern: a per-request named pipe, the helper
/// spawned with <c>Verb="runas"</c>, one JSON request line, one JSON result line, then the helper
/// exits. A cancelled UAC prompt surfaces as <see cref="CenterMStartupHelperOutcome.Cancelled"/>,
/// never as a fake success (Addendum E).</summary>
internal interface ICenterMStartupHelperInvoker
{
    Task<CenterMStartupHelperResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken);
}

internal sealed class CenterMStartupHelperClient : ICenterMStartupHelperInvoker
{
    private const int ErrorCancelled = 1223; // ERROR_CANCELLED -- the UAC consent prompt was dismissed.
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(30);

    private readonly string _helperPath;

    internal CenterMStartupHelperClient()
        : this(Path.Combine(AppContext.BaseDirectory, "SteamInputAddonforClaw.CenterMStartupHelper.exe")) { }

    internal CenterMStartupHelperClient(string helperPath) => _helperPath = helperPath;

    public async Task<CenterMStartupHelperResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
        => await InvokeAsync(new Request("SetEnabled", enabled), cancellationToken).ConfigureAwait(false);

    private async Task<CenterMStartupHelperResult> InvokeAsync(Request request, CancellationToken cancellationToken)
    {
        if (!File.Exists(_helperPath))
        {
            AppLog.Warn("CenterM.Startup", "Startup helper binary was not found.", null, ("Path", _helperPath));
            return Unavailable("The MSI Center M startup helper is missing.");
        }

        var pipeName = $"SteamInputAddonforClaw.CenterMStartup.{Environment.ProcessId}.{Guid.NewGuid():N}";
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        Process process;
        try
        {
            process = Process.Start(new ProcessStartInfo(_helperPath, pipeName)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory,
            }) ?? throw new InvalidOperationException("The MSI Center M startup helper could not be started.");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            AppLog.Info("CenterM.Startup", "Startup helper elevation was cancelled by the user.");
            return new CenterMStartupHelperResult(CenterMStartupHelperOutcome.Cancelled, false, false, false, false,
                CenterMFoundationServiceMode.Unavailable, null);
        }
        catch (Exception exception)
        {
            AppLog.Warn("CenterM.Startup", "Startup helper could not be started.", exception);
            return Unavailable("The MSI Center M startup helper could not be started.");
        }

        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectTimeout.CancelAfter(ConnectTimeout);
            await pipe.WaitForConnectionAsync(connectTimeout.Token).ConfigureAwait(false);

            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(request)).ConfigureAwait(false);

            using var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            responseTimeout.CancelAfter(ResponseTimeout);
            var line = await reader.ReadLineAsync(responseTimeout.Token).ConfigureAwait(false);
            var response = line is null ? null : JsonSerializer.Deserialize<Response>(line);
            if (response is null)
                return Unavailable("The MSI Center M startup helper did not return a result.");

            var mode = Enum.TryParse<CenterMFoundationServiceMode>(response.FoundationServiceMode, out var parsed)
                ? parsed : CenterMFoundationServiceMode.Unavailable;
            return new CenterMStartupHelperResult(CenterMStartupHelperOutcome.Completed,
                response.Ok, response.SnapshotAvailable, response.ServerTaskEnabled, response.UpdaterTaskEnabled,
                mode, response.Error);
        }
        catch (Exception exception)
        {
            AppLog.Warn("CenterM.Startup", "Startup helper communication failed.", exception);
            return Unavailable("The MSI Center M startup helper did not respond.");
        }
        finally
        {
            try { if (!process.HasExited) { process.WaitForExit(2000); if (!process.HasExited) process.Kill(); } }
            catch { /* best effort */ }
            process.Dispose();
        }
    }

    private static CenterMStartupHelperResult Unavailable(string message) =>
        new(CenterMStartupHelperOutcome.HelperUnavailable, false, false, false, false,
            CenterMFoundationServiceMode.Unavailable, message);

    private sealed record Request(string Operation, bool Enabled = false);
    private sealed record Response(
        bool Ok, bool SnapshotAvailable, bool ServerTaskEnabled, bool UpdaterTaskEnabled,
        string FoundationServiceMode, string? Error);
}
