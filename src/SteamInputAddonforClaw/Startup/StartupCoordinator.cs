namespace SteamInputAddonforClaw.Startup;

using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Status;

internal sealed class StartupCoordinator
{
    private readonly IUpdateGate _updateGate;
    private readonly IControllerEnvironmentAssessmentProvider _environmentAssessmentProvider;
    private readonly IControllerEnvironmentWaiter _environmentWaiter;
    private readonly IRecoveryJournalStore _recoveryJournalStore;
    private readonly IStockCenterMStartupBaseline? _stockCenterMBaseline;
    private readonly IWindowsDeviceProbeContextFactory _probeContextFactory;
    private readonly IHardwareCompatibilityEvaluator _hardwareCompatibilityEvaluator;

    public StartupCoordinator(
        IUpdateGate updateGate,
        IControllerEnvironmentAssessmentProvider environmentAssessmentProvider,
        IControllerEnvironmentWaiter environmentWaiter,
        IWindowsDeviceProbeContextFactory probeContextFactory,
        IHardwareCompatibilityEvaluator hardwareCompatibilityEvaluator,
        IRecoveryJournalStore recoveryJournalStore,
        IStockCenterMStartupBaseline? stockCenterMBaseline = null)
    {
        _updateGate = updateGate;
        _environmentAssessmentProvider = environmentAssessmentProvider;
        _environmentWaiter = environmentWaiter;
        _recoveryJournalStore = recoveryJournalStore ?? throw new ArgumentNullException(nameof(recoveryJournalStore));
        _stockCenterMBaseline = stockCenterMBaseline;
        _probeContextFactory = probeContextFactory ?? throw new ArgumentNullException(nameof(probeContextFactory));
        _hardwareCompatibilityEvaluator = hardwareCompatibilityEvaluator ?? throw new ArgumentNullException(nameof(hardwareCompatibilityEvaluator));
    }


    public async Task<StartupResult> RunAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        AppLog.Info("Startup", "Startup update gate entered.");
        var updateResult = await _updateGate.RunAsync(cancellationToken).ConfigureAwait(false);
        AppLog.Info("Startup", "Update gate completed.", ("Result", updateResult), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
        if (updateResult == UpdateGateResult.RestartScheduled)
        {
            AppLog.Info("Startup", "Runtime startup aborted because update restart was scheduled.", ("Action", "Exit"));
            return new StartupResult(false, ControllerEnvironmentMode.Indeterminate, ControllerEnvironmentReadiness.Indeterminate, RecoverySafe: false);
        }

        var hardware = EvaluateHardwareCompatibility();
        AppLog.Info("Hardware", "Startup hardware compatibility assessment completed.",
            ("Status", hardware.Status), ("DeviceFamily", hardware.DeviceFamily), ("DeviceModel", hardware.DeviceModel), ("Reason", hardware.Reason),
            ("Action", hardware.Status == HardwareCompatibilityStatus.Supported ? "Continue" : "Passive"));
        if (hardware.Status == HardwareCompatibilityStatus.Unsupported)
            return new StartupResult(true, ControllerEnvironmentMode.Unsupported, ControllerEnvironmentReadiness.NotApplicable);
        if (hardware.Status == HardwareCompatibilityStatus.Indeterminate)
            return new StartupResult(true, ControllerEnvironmentMode.Indeterminate, ControllerEnvironmentReadiness.Indeterminate);

        AppLog.Info("Environment", "Initial environment detection started.");
        var assessment = _environmentAssessmentProvider.Capture();
        var environment = StartupControllerEnvironmentMapper.Map(assessment);
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
        if (environment.Mode != ControllerEnvironmentMode.StockCenterM)
        {
            AppLog.Warn("Environment", "Stock MSI Center M baseline is not permitted for this controller environment.", null,
                ("Mode", environment.Mode), ("Action", "Passive"));
            return new StartupResult(true, environment.Mode, ControllerEnvironmentReadiness.NotApplicable);
        }
        var readinessStopwatch = Stopwatch.StartNew();
        AppLog.Info("Environment", "Controller environment readiness wait started.", ("Mode", environment.Mode));
        var readiness = await _environmentWaiter.WaitUntilStableAsync(environment.Mode, cancellationToken).ConfigureAwait(false);
        AppLog.Info("Environment", "Controller environment readiness completed.", ("Result", readiness), ("ReadinessElapsedMs", readinessStopwatch.ElapsedMilliseconds), ("StartupTotalElapsedMs", stopwatch.ElapsedMilliseconds));
        if (readiness != ControllerEnvironmentReadiness.Stable)
            return new StartupResult(true, environment.Mode, readiness);

        if (_stockCenterMBaseline is null)
        {
            AppLog.Warn("Startup", "Stock MSI Center M baseline service is unavailable; routing remains passive.", null, ("Action", "Passive"));
            return new StartupResult(true, environment.Mode, readiness, RecoverySafe: false);
        }

        var baseline = await _stockCenterMBaseline.EstablishAsync(cancellationToken).ConfigureAwait(false);
        if (!baseline.Succeeded)
            return new StartupResult(true, environment.Mode, readiness, RecoverySafe: false);

        if (!TryRetireStaleStartupJournal(out var reason))
        {
            AppLog.Warn("Startup", "Stale startup journal could not be discarded after the live XInput baseline; routing remains passive.", null,
                ("Action", "Passive"), ("Reason", reason));
            return new StartupResult(true, environment.Mode, readiness, RecoverySafe: false);
        }
        return new StartupResult(true, environment.Mode, readiness, RecoverySafe: true);
    }

    private bool TryRetireStaleStartupJournal(out string reason)
    {
        try
        {
            if (!_recoveryJournalStore.Exists())
            {
                reason = "Recovery journal does not exist.";
                return true;
            }
            AppLog.Info("Recovery", "Stale startup journal retirement started.", ("JournalPath", _recoveryJournalStore.JournalPath), ("Action", "DiscardOnly"));
            _recoveryJournalStore.Delete();
            if (_recoveryJournalStore.Exists())
            {
                reason = "Recovery journal still exists after deletion.";
                return false;
            }
            AppLog.Info("Recovery", "Stale startup journal discarded.", ("JournalPath", _recoveryJournalStore.JournalPath), ("JournalDeleted", true));
            reason = "Stale startup journal discarded.";
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error("Recovery", "Stale startup journal could not be discarded.", exception, ("JournalPath", _recoveryJournalStore.JournalPath), ("Action", "Passive"));
            reason = exception.Message;
            return false;
        }
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

internal sealed record StartupResult(bool ShouldStartRuntime, ControllerEnvironmentMode EnvironmentMode, ControllerEnvironmentReadiness EnvironmentReadiness, bool RecoverySafe = false);
