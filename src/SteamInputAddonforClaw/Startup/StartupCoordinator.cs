namespace SteamInputAddonforClaw.Startup;

internal sealed class StartupCoordinator
{
    private readonly IUpdateGate _updateGate;
    private readonly IControllerEnvironmentDetector _environmentDetector;
    private readonly IControllerEnvironmentWaiter _environmentWaiter;

    public StartupCoordinator(IUpdateGate updateGate, IControllerEnvironmentDetector environmentDetector, IControllerEnvironmentWaiter environmentWaiter)
    {
        _updateGate = updateGate;
        _environmentDetector = environmentDetector;
        _environmentWaiter = environmentWaiter;
    }

    public async Task<StartupResult> RunAsync(CancellationToken cancellationToken)
    {
        if (await _updateGate.RunAsync(cancellationToken).ConfigureAwait(false) == UpdateGateResult.RestartScheduled)
        {
            return new StartupResult(false, ControllerEnvironmentReadiness.Indeterminate);
        }

        _ = _environmentDetector.DetectClawTweaksState();
        var readiness = await _environmentWaiter.WaitUntilStableAsync(cancellationToken).ConfigureAwait(false);
        return new StartupResult(true, readiness);
    }
}

internal sealed record StartupResult(bool ShouldStartRuntime, ControllerEnvironmentReadiness EnvironmentReadiness);
