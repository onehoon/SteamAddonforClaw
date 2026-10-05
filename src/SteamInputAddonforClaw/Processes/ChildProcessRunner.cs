using System.Diagnostics;

namespace SteamInputAddonforClaw.Processes;

internal enum ChildProcessResultKind { Completed, FailedToStart, TimedOut }
internal sealed record ChildProcessResult(ChildProcessResultKind Kind, int? ExitCode = null, string? Reason = null);
internal interface IChildProcessRunner { Task<ChildProcessResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken); }

internal sealed class ChildProcessRunner : IChildProcessRunner
{
    private readonly TimeSpan? _executionTimeout;

    internal ChildProcessRunner(TimeSpan? executionTimeout = null) => _executionTimeout = executionTimeout;

    internal static ProcessStartInfo CreateStartInfo(string fileName, string arguments) => new(fileName, arguments)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    public async Task<ChildProcessResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process? process;
        try { process = Process.Start(CreateStartInfo(fileName, arguments)); }
        catch (Exception exception) { return new(ChildProcessResultKind.FailedToStart, Reason: exception.Message); }
        if (process is null) return new(ChildProcessResultKind.FailedToStart, Reason: "ProcessStartReturnedNull");

        using (process)
        {
            if (_executionTimeout is not { } executionTimeout)
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
                return new(ChildProcessResultKind.Completed, process.ExitCode);
            }

            using var timeout = new CancellationTokenSource(executionTimeout);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                await process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                TryTerminate(process);
                return new(ChildProcessResultKind.TimedOut, Reason: "ProcessTimedOut");
            }
            catch (OperationCanceledException)
            {
                TryTerminate(process);
                throw;
            }

            return new(ChildProcessResultKind.Completed, process.ExitCode);
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch { }
    }
}
