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
    private readonly IRecoveryManager? _recoveryManager;
    private readonly IWindowsDeviceProbeContextFactory _probeContextFactory;
    private readonly IHardwareCompatibilityEvaluator _hardwareCompatibilityEvaluator;
    private readonly IStartupNativeBaselineValidator? _startupBaselineValidator;

    public StartupCoordinator(
        IUpdateGate updateGate,
        IControllerEnvironmentDetector environmentDetector,
        IControllerEnvironmentWaiter environmentWaiter,
        IWindowsDeviceProbeContextFactory probeContextFactory,
        IHardwareCompatibilityEvaluator hardwareCompatibilityEvaluator,
        IRecoveryManager? recoveryManager = null,
        IStartupNativeBaselineValidator? startupBaselineValidator = null)
    {
        _updateGate = updateGate;
        _environmentDetector = environmentDetector;
        _environmentWaiter = environmentWaiter;
        _recoveryManager = recoveryManager;
        _probeContextFactory = probeContextFactory ?? throw new ArgumentNullException(nameof(probeContextFactory));
        _hardwareCompatibilityEvaluator = hardwareCompatibilityEvaluator ?? throw new ArgumentNullException(nameof(hardwareCompatibilityEvaluator));
        _startupBaselineValidator = startupBaselineValidator;
    }

    public async Task<StartupResult> RunAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var recoverySafe = true;
        if (_recoveryManager is not null)
        {
            try
            {
                var recoveryResult = await _recoveryManager.RecoverIncompleteSessionAsync(cancellationToken).ConfigureAwait(false);
                if (!recoveryResult.IsSafeToContinue)
                {
                    recoverySafe = false;
                    AppLog.Warn("Startup", "Normal routing blocked by incomplete recovery.", null, ("Action", "Passive"), ("Reason", recoveryResult.Reason));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                recoverySafe = false;
                AppLog.Warn("Startup", "Incomplete recovery threw; normal routing remains blocked while update is allowed.", exception,
                    ("Action", "PassiveThenUpdate"), ("Reason", "RecoveryException:" + exception.GetType().Name));
            }
        }
        AppLog.Info("Startup", "Startup update gate entered.");
        var updateResult = await _updateGate.RunAsync(cancellationToken).ConfigureAwait(false);
        AppLog.Info("Startup", "Update gate completed.", ("Result", updateResult), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
        if (updateResult == UpdateGateResult.RestartScheduled)
        {
            AppLog.Info("Startup", "Runtime startup aborted because update restart was scheduled.", ("Action", "Exit"));
            return new StartupResult(false, ControllerEnvironmentMode.Indeterminate, ControllerEnvironmentReadiness.Indeterminate, RecoverySafe: recoverySafe);
        }

        if (!recoverySafe)
            return new StartupResult(true, ControllerEnvironmentMode.Indeterminate, ControllerEnvironmentReadiness.Indeterminate, RecoverySafe: false);

        var hardware = EvaluateHardwareCompatibility();
        AppLog.Info("Hardware", "Startup hardware compatibility assessment completed.",
            ("Status", hardware.Status), ("DeviceFamily", hardware.DeviceFamily), ("DeviceModel", hardware.DeviceModel), ("Reason", hardware.Reason),
            ("Action", hardware.Status == HardwareCompatibilityStatus.Supported ? "Continue" : "Passive"));
        if (hardware.Status == HardwareCompatibilityStatus.Unsupported)
            return new StartupResult(true, ControllerEnvironmentMode.Unsupported, ControllerEnvironmentReadiness.NotApplicable);
        if (hardware.Status == HardwareCompatibilityStatus.Indeterminate)
            return new StartupResult(true, ControllerEnvironmentMode.Indeterminate, ControllerEnvironmentReadiness.Indeterminate);

        AppLog.Info("Environment", "Initial environment detection started.");
        var environment = _environmentDetector.Detect();
        AppLog.Info("Environment", "Environment detection completed.", ("Mode", environment.Mode), ("ClawTweaksState", environment.ClawTweaksState));
        if (environment.Mode == ControllerEnvironmentMode.Indeterminate)
        {
            AppLog.Warn("Environment", "Environment decision is indeterminate.", null, ("Action", "Passive"), ("Reason", "EnvironmentDetectionIndeterminate"));
            return new StartupResult(true, environment.Mode, ControllerEnvironmentReadiness.Indeterminate);
        }
        if (environment.Mode is ControllerEnvironmentMode.HHCManaged or ControllerEnvironmentMode.Unsupported)
        {
            AppLog.Info("Environment", "Unsupported controller manager detected.", ("Manager", environment.Mode), ("Action", "Passive"), ("Reason", environment.Mode == ControllerEnvironmentMode.HHCManaged ? "HandheldCompanionNotSupportedByCurrentVersion" : "ClawTweaksNotSupportedByCurrentVersion"));
            return new StartupResult(true, environment.Mode, ControllerEnvironmentReadiness.NotApplicable);
        }
        var readinessStopwatch = Stopwatch.StartNew();
        AppLog.Info("Environment", "Controller environment readiness wait started.", ("Mode", environment.Mode));
        var readiness = await _environmentWaiter.WaitUntilStableAsync(environment.Mode, cancellationToken).ConfigureAwait(false);
        AppLog.Info("Environment", "Controller environment readiness completed.", ("Result", readiness), ("ReadinessElapsedMs", readinessStopwatch.ElapsedMilliseconds), ("StartupTotalElapsedMs", stopwatch.ElapsedMilliseconds));
        if (environment.Mode == ControllerEnvironmentMode.StockCenterM && readiness == ControllerEnvironmentReadiness.Stable && _startupBaselineValidator is not null)
        {
            var baseline = await _startupBaselineValidator.ValidateAsync(cancellationToken).ConfigureAwait(false);
            if (!baseline.IsSafeToContinue)
            {
                AppLog.Warn("Startup", "Startup native baseline validation failed; routing remains passive.", null, ("Reason", baseline.Reason));
                return new StartupResult(true, environment.Mode, readiness, RecoverySafe: false);
            }
        }
        else if (environment.Mode == ControllerEnvironmentMode.StockCenterM && readiness != ControllerEnvironmentReadiness.Stable)
            return new StartupResult(true, environment.Mode, readiness, RecoverySafe: false);
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
