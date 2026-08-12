using SteamInputAddonforClaw.Updates;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Startup;

internal sealed class SilentUpdateGate : IUpdateGate
{
    private static readonly TimeSpan UpdateGateTimeout = TimeSpan.FromMinutes(2);
    private readonly string[]? _restartArguments;

    public SilentUpdateGate(string[]? restartArguments = null)
    {
        _restartArguments = restartArguments;
    }

    public async Task<UpdateGateResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellationTokenSource.CancelAfter(UpdateGateTimeout);
            var restartScheduled = await new SilentUpdateService(new VelopackUpdateClient())
                .CheckDownloadAndScheduleAsync(timeoutCancellationTokenSource.Token, _restartArguments)
                .ConfigureAwait(false);
            return restartScheduled ? UpdateGateResult.RestartScheduled : UpdateGateResult.Continue;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            AppLog.Warn("Update", "Silent update gate timed out.", exception, ("TimeoutMs", UpdateGateTimeout.TotalMilliseconds), ("Action", "Continue"));
            return UpdateGateResult.Continue;
        }
        catch (Exception exception)
        {
            AppLog.Warn("Update", "Silent update gate failed.", exception, ("Action", "Continue"));
            return UpdateGateResult.Continue;
        }
    }
}
