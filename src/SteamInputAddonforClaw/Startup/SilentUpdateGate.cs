using SteamInputAddonforClaw.Updates;

namespace SteamInputAddonforClaw.Startup;

internal sealed class SilentUpdateGate : IUpdateGate
{
    public async Task<UpdateGateResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var restartScheduled = await new SilentUpdateService(new VelopackUpdateClient())
                .CheckDownloadAndScheduleAsync(cancellationToken)
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
