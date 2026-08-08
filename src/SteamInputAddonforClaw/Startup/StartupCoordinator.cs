namespace SteamInputAddonforClaw.Startup;

internal sealed class StartupCoordinator
{
    private readonly IUpdateGate _updateGate;
    private readonly IControllerEnvironmentWaiter _environmentWaiter;

    public StartupCoordinator(IUpdateGate updateGate, IControllerEnvironmentWaiter environmentWaiter)
    {
        _updateGate = updateGate;
        _environmentWaiter = environmentWaiter;
    }

    public async Task<bool> CanStartRuntimeAsync(CancellationToken cancellationToken)
    {
        if (await _updateGate.RunAsync(cancellationToken).ConfigureAwait(false) == UpdateGateResult.RestartScheduled)
        {
            return false;
        }

        await _environmentWaiter.WaitUntilStableAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
