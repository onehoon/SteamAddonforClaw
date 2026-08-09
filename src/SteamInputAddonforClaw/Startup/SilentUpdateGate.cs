using SteamInputAddonforClaw.Updates;

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
        catch (Exception)
        {
            return UpdateGateResult.Continue;
        }
    }
}
