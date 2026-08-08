namespace SteamInputAddonforClaw.Startup;

internal sealed class StartupCoordinator
{
    private readonly IUpdateGate _updateGate;
    private readonly IControllerEnvironmentDetector _environmentDetector;
    private readonly IControllerEnvironmentWaiter _environmentWaiter;
    private readonly TimeSpan _clawTweaksStartingTimeout;
    private readonly TimeSpan _clawTweaksStartingCheckInterval;

    public StartupCoordinator(
        IUpdateGate updateGate,
        IControllerEnvironmentDetector environmentDetector,
        IControllerEnvironmentWaiter environmentWaiter,
        TimeSpan? clawTweaksStartingTimeout = null,
        TimeSpan? clawTweaksStartingCheckInterval = null)
    {
        _updateGate = updateGate;
        _environmentDetector = environmentDetector;
        _environmentWaiter = environmentWaiter;
        _clawTweaksStartingTimeout = clawTweaksStartingTimeout ?? TimeSpan.FromSeconds(5);
        _clawTweaksStartingCheckInterval = clawTweaksStartingCheckInterval ?? TimeSpan.FromMilliseconds(350);
    }

    public async Task<StartupResult> RunAsync(CancellationToken cancellationToken)
    {
        if (await _updateGate.RunAsync(cancellationToken).ConfigureAwait(false) == UpdateGateResult.RestartScheduled)
        {
            return new StartupResult(false, ControllerEnvironmentReadiness.Indeterminate);
        }

        var deadline = DateTimeOffset.UtcNow + _clawTweaksStartingTimeout;
        while (_environmentDetector.DetectClawTweaksState() == ClawTweaksState.Starting)
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                return new StartupResult(true, ControllerEnvironmentReadiness.Indeterminate);
            }

            await Task.Delay(_clawTweaksStartingCheckInterval, cancellationToken).ConfigureAwait(false);
        }
        var readiness = await _environmentWaiter.WaitUntilStableAsync(cancellationToken).ConfigureAwait(false);
        return new StartupResult(true, readiness);
    }
}

internal sealed record StartupResult(bool ShouldStartRuntime, ControllerEnvironmentReadiness EnvironmentReadiness);
