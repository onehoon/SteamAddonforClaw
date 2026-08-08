using SteamInputAddonforClaw.Updates;

namespace SteamInputAddonforClaw.Startup;

internal sealed class SilentUpdateGate : IUpdateGate
{
    private static readonly TimeSpan UpdateGateTimeout = TimeSpan.FromMinutes(2);

    public async Task<UpdateGateResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var restartScheduled = await new SilentUpdateService(new VelopackUpdateClient())
                .CheckDownloadAndScheduleAsync(cancellationToken)
                .WaitAsync(UpdateGateTimeout, cancellationToken)
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
