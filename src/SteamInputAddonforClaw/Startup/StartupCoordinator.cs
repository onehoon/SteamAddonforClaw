namespace SteamInputAddonforClaw.Startup;

using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;

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
        var stopwatch = Stopwatch.StartNew();
        AppLog.Info("Startup", "Startup update gate entered.");
        var updateResult = await _updateGate.RunAsync(cancellationToken).ConfigureAwait(false);
        AppLog.Info("Startup", "Update gate completed.", ("Result", updateResult), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
        if (updateResult == UpdateGateResult.RestartScheduled)
        {
            return new StartupResult(false, ControllerEnvironmentMode.Indeterminate, ControllerEnvironmentReadiness.Indeterminate);
        }

        var deadline = DateTimeOffset.UtcNow + _clawTweaksStartingTimeout;
        AppLog.Info("Environment", "Initial environment detection started.");
        var environment = _environmentDetector.Detect();
        AppLog.Info("Environment", "Environment detection completed.", ("Mode", environment.Mode), ("ClawTweaksState", environment.ClawTweaksState));
        while (environment.ClawTweaksState == ClawTweaksState.Starting)
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                return new StartupResult(true, ControllerEnvironmentMode.Indeterminate, ControllerEnvironmentReadiness.Indeterminate);
            }

            AppLog.Trace("ClawTweaks", "ClawTweaks startup wait.", ("RemainingMs", (deadline - DateTimeOffset.UtcNow).TotalMilliseconds));
            await Task.Delay(_clawTweaksStartingCheckInterval, cancellationToken).ConfigureAwait(false);
            environment = _environmentDetector.Detect();
        }
        if (environment.Mode == ControllerEnvironmentMode.Indeterminate)
        {
            return new StartupResult(true, environment.Mode, ControllerEnvironmentReadiness.Indeterminate);
        }
        if (environment.Mode == ControllerEnvironmentMode.HHCManaged)
        {
            return new StartupResult(true, environment.Mode, ControllerEnvironmentReadiness.Indeterminate);
        }
        AppLog.Info("Environment", "Controller environment readiness wait started.", ("Mode", environment.Mode));
        var readiness = await _environmentWaiter.WaitUntilStableAsync(environment.Mode, cancellationToken).ConfigureAwait(false);
        AppLog.Info("Environment", "Controller environment readiness completed.", ("Result", readiness), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
        return new StartupResult(true, environment.Mode, readiness);
    }
}

internal sealed record StartupResult(bool ShouldStartRuntime, ControllerEnvironmentMode EnvironmentMode, ControllerEnvironmentReadiness EnvironmentReadiness);
