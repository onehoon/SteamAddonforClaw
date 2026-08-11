namespace SteamInputAddonforClaw.Startup;

using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Devices;

internal sealed class StartupCoordinator
{
    private readonly IUpdateGate _updateGate;
    private readonly IControllerEnvironmentDetector _environmentDetector;
    private readonly IControllerEnvironmentWaiter _environmentWaiter;
    private readonly TimeSpan _clawTweaksStartingTimeout;
    private readonly TimeSpan _clawTweaksStartingCheckInterval;
    private readonly IRecoveryManager? _recoveryManager;
    private readonly IWindowsDeviceProbeContextFactory _probeContextFactory;
    private readonly IHardwareCompatibilityEvaluator _hardwareCompatibilityEvaluator;

    public StartupCoordinator(
        IUpdateGate updateGate,
        IControllerEnvironmentDetector environmentDetector,
        IControllerEnvironmentWaiter environmentWaiter,
        IWindowsDeviceProbeContextFactory probeContextFactory,
        IHardwareCompatibilityEvaluator hardwareCompatibilityEvaluator,
        TimeSpan? clawTweaksStartingTimeout = null,
        TimeSpan? clawTweaksStartingCheckInterval = null,
        IRecoveryManager? recoveryManager = null)
    {
        _updateGate = updateGate;
        _environmentDetector = environmentDetector;
        _environmentWaiter = environmentWaiter;
        _clawTweaksStartingTimeout = clawTweaksStartingTimeout ?? TimeSpan.FromSeconds(5);
        _clawTweaksStartingCheckInterval = clawTweaksStartingCheckInterval ?? TimeSpan.FromMilliseconds(350);
        _recoveryManager = recoveryManager;
        _probeContextFactory = probeContextFactory ?? throw new ArgumentNullException(nameof(probeContextFactory));
        _hardwareCompatibilityEvaluator = hardwareCompatibilityEvaluator ?? throw new ArgumentNullException(nameof(hardwareCompatibilityEvaluator));
    }

    public async Task<StartupResult> RunAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        if (_recoveryManager is not null)
        {
            var recoveryResult = await _recoveryManager.RecoverIncompleteSessionAsync(cancellationToken).ConfigureAwait(false);
            if (!recoveryResult.IsSafeToContinue)
            {
                AppLog.Warn("Startup", "Normal routing blocked by incomplete recovery.", null, ("Action", "Passive"), ("Reason", recoveryResult.Reason));
                return new StartupResult(true, ControllerEnvironmentMode.Indeterminate, ControllerEnvironmentReadiness.Indeterminate, RecoverySafe: false);
            }
        }
        AppLog.Info("Startup", "Startup update gate entered.");
        var updateResult = await _updateGate.RunAsync(cancellationToken).ConfigureAwait(false);
        AppLog.Info("Startup", "Update gate completed.", ("Result", updateResult), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
        if (updateResult == UpdateGateResult.RestartScheduled)
        {
            AppLog.Info("Startup", "Runtime startup aborted because update restart was scheduled.", ("Action", "Exit"));
            return new StartupResult(false, ControllerEnvironmentMode.Indeterminate, ControllerEnvironmentReadiness.Indeterminate);
        }

        var hardware = EvaluateHardwareCompatibility();
        AppLog.Info("Hardware", "Startup hardware compatibility assessment completed.",
            ("Status", hardware.Status), ("DeviceFamily", hardware.DeviceFamily), ("DeviceModel", hardware.DeviceModel), ("Reason", hardware.Reason),
            ("Action", hardware.Status == HardwareCompatibilityStatus.Supported ? "Continue" : "Passive"));
        if (hardware.Status == HardwareCompatibilityStatus.Unsupported)
            return new StartupResult(true, ControllerEnvironmentMode.Unsupported, ControllerEnvironmentReadiness.NotApplicable);
        if (hardware.Status == HardwareCompatibilityStatus.Indeterminate)
            return new StartupResult(true, ControllerEnvironmentMode.Indeterminate, ControllerEnvironmentReadiness.Indeterminate);

        var deadline = DateTimeOffset.UtcNow + _clawTweaksStartingTimeout;
        var environmentStopwatch = Stopwatch.StartNew();
        var attempts = 0;
        AppLog.Info("Environment", "Initial environment detection started.");
        var environment = _environmentDetector.Detect();
        AppLog.Info("Environment", "Environment detection completed.", ("Mode", environment.Mode), ("ClawTweaksState", environment.ClawTweaksState));
        while (environment.ClawTweaksState == ClawTweaksState.Starting)
        {
            attempts++;
            if (DateTimeOffset.UtcNow >= deadline)
            {
                AppLog.Warn("ClawTweaks", "ClawTweaks startup stabilization timed out.", null, ("Attempts", attempts), ("ElapsedMs", environmentStopwatch.ElapsedMilliseconds), ("FinalState", environment.ClawTweaksState), ("Action", "Passive"), ("Reason", "TopologyNotReady"));
                return new StartupResult(true, ControllerEnvironmentMode.Indeterminate, ControllerEnvironmentReadiness.Indeterminate);
            }

            AppLog.Trace("ClawTweaks", "ClawTweaks startup wait.", ("RemainingMs", (deadline - DateTimeOffset.UtcNow).TotalMilliseconds));
            await Task.Delay(_clawTweaksStartingCheckInterval, cancellationToken).ConfigureAwait(false);
            environment = _environmentDetector.Detect();
        }
        if (environment.Mode == ControllerEnvironmentMode.Indeterminate)
        {
            AppLog.Warn("Environment", "Environment decision is indeterminate.", null, ("Action", "Passive"), ("Reason", "EnvironmentDetectionIndeterminate"));
            return new StartupResult(true, environment.Mode, ControllerEnvironmentReadiness.Indeterminate);
        }
        if (environment.Mode == ControllerEnvironmentMode.HHCManaged)
        {
            AppLog.Info("Environment", "Environment owned by Handheld Companion.", ("Action", "Passive"), ("Reason", "HandheldCompanionOwnsController"));
            return new StartupResult(true, environment.Mode, ControllerEnvironmentReadiness.Indeterminate);
        }
        var readinessStopwatch = Stopwatch.StartNew();
        AppLog.Info("Environment", "Controller environment readiness wait started.", ("Mode", environment.Mode));
        var readiness = await _environmentWaiter.WaitUntilStableAsync(environment.Mode, cancellationToken).ConfigureAwait(false);
        AppLog.Info("Environment", "Controller environment readiness completed.", ("Result", readiness), ("ReadinessElapsedMs", readinessStopwatch.ElapsedMilliseconds), ("StartupTotalElapsedMs", stopwatch.ElapsedMilliseconds));
        return new StartupResult(true, environment.Mode, readiness);
    }

    private HardwareCompatibilityAssessment EvaluateHardwareCompatibility()
    {
        try { return _hardwareCompatibilityEvaluator.Evaluate(_probeContextFactory.Capture()); }
        catch (Exception exception)
        {
            AppLog.Warn("Hardware", "Startup hardware compatibility assessment failed.", exception, ("Status", HardwareCompatibilityStatus.Indeterminate), ("Action", "Passive"));
            return new(HardwareCompatibilityStatus.Indeterminate, null, null, "HardwareCompatibilityEvaluationFailed:" + exception.GetType().Name);
        }
    }
}

internal sealed record StartupResult(bool ShouldStartRuntime, ControllerEnvironmentMode EnvironmentMode, ControllerEnvironmentReadiness EnvironmentReadiness, bool RecoverySafe = true);
