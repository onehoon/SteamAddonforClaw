using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace SteamInputAddonforClaw.CenterMStartup;

/// <summary>Short-lived elevated child entrypoint for the Enter BIOS firmware restart handshake.
/// It authorizes first, waits for the Runtime to prove MSI GamepadMode BIOS mode 5, and executes
/// the firmware restart only after receiving the explicit command over the parent-owned pipe.</summary>
internal static class FirmwareRestartHelper
{
    internal const string Argument = "--firmware-restart-helper";
    private static readonly TimeSpan ParentCommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RestartCommandTimeout = TimeSpan.FromSeconds(5);

    internal static bool TryRun(IReadOnlyList<string> args, out int exitCode)
    {
        if (args.Count != 2 || !string.Equals(args[0], Argument, StringComparison.OrdinalIgnoreCase))
        {
            exitCode = 0;
            return false;
        }

        exitCode = RunAsync(args[1]).GetAwaiter().GetResult();
        return true;
    }

    private static async Task<int> RunAsync(string pipeName)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var connectTimeout = new CancellationTokenSource(ParentCommandTimeout);
            await pipe.ConnectAsync(connectTimeout.Token).ConfigureAwait(false);

            using var reader = new StreamReader(pipe);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(new FirmwareRestartHelperResponse("Ready", null, null))).ConfigureAwait(false);

            using var requestTimeout = new CancellationTokenSource(ParentCommandTimeout);
            var line = await reader.ReadLineAsync(requestTimeout.Token).ConfigureAwait(false);
            if (line is null)
                return 0;

            var request = JsonSerializer.Deserialize<FirmwareRestartHelperRequest>(line);
            if (request?.Operation is not "RestartFirmware")
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(new FirmwareRestartHelperResponse("Failed", null, "UnknownOperation"))).ConfigureAwait(false);
                return 1;
            }

            var restart = ExecuteFirmwareRestart();
            await writer.WriteLineAsync(JsonSerializer.Serialize(new FirmwareRestartHelperResponse(
                restart.Accepted ? "Accepted" : "Failed", restart.ExitCode, restart.Error))).ConfigureAwait(false);
            return restart.Accepted ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            return 1;
        }
        catch
        {
            return 1;
        }
    }

    private static FirmwareRestartExecution ExecuteFirmwareRestart()
    {
        try
        {
            using var started = Process.Start(new ProcessStartInfo("shutdown.exe", "/r /fw /t 0")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (started is null)
                return new(false, null, "ProcessStartReturnedNull");
            if (!started.WaitForExit(milliseconds: (int)RestartCommandTimeout.TotalMilliseconds))
                return new(false, null, "Timeout");

            return started.ExitCode == 0
                ? new(true, started.ExitCode, null)
                : new(false, started.ExitCode, "ShutdownCommandFailed");
        }
        catch (Exception exception)
        {
            return new(false, null, exception.GetType().Name);
        }
    }

    private sealed record FirmwareRestartExecution(bool Accepted, int? ExitCode, string? Error);
}

internal sealed record FirmwareRestartHelperRequest(string Operation);

internal sealed record FirmwareRestartHelperResponse(string Status, int? ExitCode, string? Error);
