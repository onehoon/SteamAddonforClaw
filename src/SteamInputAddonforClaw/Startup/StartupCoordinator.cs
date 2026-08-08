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
            return new StartupResult(false, ControllerEnvironmentMode.Indeterminate, ControllerEnvironmentReadiness.Indeterminate);
        }

        var deadline = DateTimeOffset.UtcNow + _clawTweaksStartingTimeout;
        var environment = _environmentDetector.Detect();
        while (environment.ClawTweaksState == ClawTweaksState.Starting)
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                return new StartupResult(true, ControllerEnvironmentMode.Indeterminate, ControllerEnvironmentReadiness.Indeterminate);
            }

            await Task.Delay(_clawTweaksStartingCheckInterval, cancellationToken).ConfigureAwait(false);
            environment = _environmentDetector.Detect();
        }
        if (environment.Mode == ControllerEnvironmentMode.Indeterminate)
        {
            return new StartupResult(true, environment.Mode, ControllerEnvironmentReadiness.Indeterminate);
        }
        var readiness = await _environmentWaiter.WaitUntilStableAsync(environment.Mode, cancellationToken).ConfigureAwait(false);
        return new StartupResult(true, environment.Mode, readiness);
    }
}

internal sealed record StartupResult(bool ShouldStartRuntime, ControllerEnvironmentMode EnvironmentMode, ControllerEnvironmentReadiness EnvironmentReadiness);
