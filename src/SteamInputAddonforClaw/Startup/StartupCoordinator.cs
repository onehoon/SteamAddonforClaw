namespace SteamInputAddonforClaw.Startup;

using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Status;

internal sealed class StartupCoordinator
{
    private readonly IUpdateGate _updateGate;
    private readonly IControllerEnvironmentAssessmentProvider _environmentAssessmentProvider;
    private readonly IControllerEnvironmentWaiter _environmentWaiter;
    private readonly IRecoveryJournalStore _recoveryJournalStore;
    private readonly RecoveryManager _recoveryManager;
    private readonly IStartupHidHideRecoveryCleaner? _hidHideRecoveryCleaner;
    private readonly IStockCenterMStartupBaseline? _stockCenterMBaseline;
    private readonly IWindowsDeviceProbeContextFactory _probeContextFactory;
    private readonly IHardwareCompatibilityEvaluator _hardwareCompatibilityEvaluator;
    private readonly TimeSpan _hardwareProbePollInterval;
    private readonly TimeSpan _hardwareProbeTimeout;
    private readonly Func<TimeSpan, CancellationToken, Task> _hardwareProbeDelay;

    public StartupCoordinator(
        IUpdateGate updateGate,
        IControllerEnvironmentAssessmentProvider environmentAssessmentProvider,
        IControllerEnvironmentWaiter environmentWaiter,
        IWindowsDeviceProbeContextFactory probeContextFactory,
        IHardwareCompatibilityEvaluator hardwareCompatibilityEvaluator,
        IRecoveryJournalStore recoveryJournalStore,
        IStockCenterMStartupBaseline? stockCenterMBaseline = null,
        IStartupHidHideRecoveryCleaner? hidHideRecoveryCleaner = null,
        TimeSpan? hardwareProbePollInterval = null,
        TimeSpan? hardwareProbeTimeout = null,
        Func<TimeSpan, CancellationToken, Task>? hardwareProbeDelay = null)
    {
        _updateGate = updateGate;
        _environmentAssessmentProvider = environmentAssessmentProvider;
        _environmentWaiter = environmentWaiter;
        _recoveryJournalStore = recoveryJournalStore ?? throw new ArgumentNullException(nameof(recoveryJournalStore));
        _recoveryManager = new RecoveryManager(_recoveryJournalStore);
        _hidHideRecoveryCleaner = hidHideRecoveryCleaner;
        _stockCenterMBaseline = stockCenterMBaseline;
        _probeContextFactory = probeContextFactory ?? throw new ArgumentNullException(nameof(probeContextFactory));
        _hardwareCompatibilityEvaluator = hardwareCompatibilityEvaluator ?? throw new ArgumentNullException(nameof(hardwareCompatibilityEvaluator));
        _hardwareProbePollInterval = hardwareProbePollInterval ?? TimeSpan.FromMilliseconds(500);
        _hardwareProbeTimeout = hardwareProbeTimeout ?? TimeSpan.FromSeconds(5);
        _hardwareProbeDelay = hardwareProbeDelay ?? Task.Delay;
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

        var hardware = await EvaluateHardwareCompatibilityWithStabilizationAsync(cancellationToken).ConfigureAwait(false);
        // The ONE decision of "is this a supported MSI Claw" for the whole process lifetime. It is
        // carried on StartupResult so every downstream consumer -- routing, and the OEM1 mapping
        // hardware-availability gate -- reads the same result instead of re-evaluating hardware.
        var hardwareSupported = hardware.Status == HardwareCompatibilityStatus.Supported;
        var hardwareDeviceModel = hardwareSupported ? hardware.DeviceModel : null;
        AppLog.Info("Hardware", "Startup hardware compatibility assessment completed.",
            ("Status", hardware.Status), ("DeviceFamily", hardware.DeviceFamily), ("DeviceModel", hardware.DeviceModel), ("Reason", hardware.Reason),
            ("Action", hardwareSupported ? "Continue" : "Passive"));
        if (hardware.Status == HardwareCompatibilityStatus.Unsupported)
            return new StartupResult(false, ControllerEnvironmentMode.Unsupported, ControllerEnvironmentReadiness.NotApplicable,
                HardwareStatus: hardware.Status, HardwareDeviceModel: hardware.DeviceModel);
        if (hardware.Status == HardwareCompatibilityStatus.Indeterminate)
            return new StartupResult(false, ControllerEnvironmentMode.Indeterminate, ControllerEnvironmentReadiness.Indeterminate,
                HardwareStatus: hardware.Status, HardwareDeviceModel: hardware.DeviceModel);

        AppLog.Info("Environment", "Initial environment detection started.");
        var assessment = _environmentAssessmentProvider.Capture();
        var environment = StartupControllerEnvironmentMapper.Map(assessment);
        AppLog.Info("Environment", "Environment detection completed.", ("Mode", environment.Mode));
        if (environment.Mode == ControllerEnvironmentMode.Indeterminate)
        {
            AppLog.Warn("Environment", "Environment decision is indeterminate.", null, ("Action", "Passive"), ("Reason", "EnvironmentDetectionIndeterminate"));
            return new StartupResult(true, environment.Mode, ControllerEnvironmentReadiness.Indeterminate, HardwareSupported: hardwareSupported, HardwareDeviceModel: hardwareDeviceModel, HardwareStatus: hardware.Status);
        }
        if (environment.Mode != ControllerEnvironmentMode.StockCenterM)
        {
            AppLog.Warn("Environment", "Stock MSI Center M baseline is not permitted for this controller environment.", null,
                ("Mode", environment.Mode), ("Action", "Passive"));
            return new StartupResult(true, environment.Mode, ControllerEnvironmentReadiness.NotApplicable, HardwareSupported: hardwareSupported, HardwareDeviceModel: hardwareDeviceModel, HardwareStatus: hardware.Status);
        }
        var readinessStopwatch = Stopwatch.StartNew();
        AppLog.Info("Environment", "Controller environment readiness wait started.", ("Mode", environment.Mode));
        var readiness = await _environmentWaiter.WaitUntilStableAsync(environment.Mode, cancellationToken).ConfigureAwait(false);
        AppLog.Info("Environment", "Controller environment readiness completed.", ("Result", readiness), ("ReadinessElapsedMs", readinessStopwatch.ElapsedMilliseconds), ("StartupTotalElapsedMs", stopwatch.ElapsedMilliseconds));
        if (readiness != ControllerEnvironmentReadiness.Stable)
            return new StartupResult(true, environment.Mode, readiness, HardwareSupported: hardwareSupported, HardwareDeviceModel: hardwareDeviceModel, HardwareStatus: hardware.Status);

        if (_stockCenterMBaseline is null)
        {
            AppLog.Warn("Startup", "Stock MSI Center M baseline service is unavailable; routing remains passive.", null, ("Action", "Passive"));
            return new StartupResult(true, environment.Mode, readiness, RecoverySafe: false, HardwareSupported: hardwareSupported, HardwareDeviceModel: hardwareDeviceModel, HardwareStatus: hardware.Status);
        }

        var baseline = await _stockCenterMBaseline.EstablishAsync(cancellationToken).ConfigureAwait(false);
        if (!baseline.Succeeded)
            return new StartupResult(true, environment.Mode, readiness, RecoverySafe: false, HardwareSupported: hardwareSupported, HardwareDeviceModel: hardwareDeviceModel, HardwareStatus: hardware.Status);

        var recoverySafe = await ResolveStaleRecoveryAsync(cancellationToken).ConfigureAwait(false);
        return new StartupResult(true, environment.Mode, readiness, RecoverySafe: recoverySafe, HardwareSupported: hardwareSupported, HardwareDeviceModel: hardwareDeviceModel, HardwareStatus: hardware.Status);
    }

    /// <summary>
    /// Uses the validated stale recovery journal only as immutable ownership evidence for
    /// removing persistent HidHide mutations proven addon-owned. Previous routing/native/VIIPER
    /// state is never replayed. The journal is discarded only after any required HidHide cleanup
    /// has been independently verified to succeed.
    /// </summary>
    private async Task<bool> ResolveStaleRecoveryAsync(CancellationToken cancellationToken)
    {
        var loaded = _recoveryManager.LoadJournal();
        switch (loaded.Status)
        {
            case RecoveryStatus.NoRecoveryNeeded:
                return true;
            case RecoveryStatus.Failure:
                AppLog.Warn("Recovery", "Stale startup journal could not be validated; routing remains passive.", null,
                    ("Action", "Passive"), ("Reason", loaded.Reason));
                return false;
        }

        var journal = loaded.Journal!;
        if (StartupHidHideRecoveryCleaner.RequiresCleanup(journal))
        {
            if (_hidHideRecoveryCleaner is null)
            {
                AppLog.Warn("Recovery", "Stale startup journal requires HidHide cleanup, but no cleaner is configured; routing remains passive.", null, ("Action", "Passive"));
                return false;
            }
            if (!_hidHideRecoveryCleaner.TryClean(journal, out var cleanupReason))
            {
                AppLog.Warn("Recovery", "Stale startup HidHide cleanup failed; routing remains passive.", null, ("Action", "Passive"), ("Reason", cleanupReason));
                return false;
            }
        }

        if (!TryRetireStaleStartupJournal(out var reason))
        {
            AppLog.Warn("Startup", "Stale startup journal could not be discarded after the live XInput baseline; routing remains passive.", null,
                ("Action", "Passive"), ("Reason", reason));
            return false;
        }
        return true;
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

    /// <summary>
    /// Retries the hardware compatibility probe/evaluate for a short bounded window at
    /// immediate Windows logon, when PnP/WMI enumeration has not yet reported the MSI Claw
    /// controller. Without this, a single-shot Indeterminate or a registry no-adapter-matched
    /// Unsupported result -- both transient at boot -- would return Passive before ever
    /// reaching the existing physical topology waiter. This never waits for MSI Center M or any
    /// other process/service; it only re-probes the same PnP/WMI-backed hardware compatibility
    /// evaluation already used everywhere else.
    /// </summary>
    private async Task<HardwareCompatibilityAssessment> EvaluateHardwareCompatibilityWithStabilizationAsync(CancellationToken cancellationToken)
    {
        var assessment = EvaluateHardwareCompatibility();
        if (!IsTransientHardwareProbeResult(assessment)) return assessment;

        var stopwatch = Stopwatch.StartNew();
        var attempts = 1;
        AppLog.Info("Hardware", "Startup hardware probe stabilization wait started.",
            ("PollIntervalMs", _hardwareProbePollInterval.TotalMilliseconds), ("TimeoutMs", _hardwareProbeTimeout.TotalMilliseconds),
            ("Status", assessment.Status), ("Reason", assessment.Reason));

        while (IsTransientHardwareProbeResult(assessment))
        {
            if (stopwatch.Elapsed >= _hardwareProbeTimeout)
            {
                AppLog.Warn("Hardware", "Startup hardware probe stabilization timed out.", null,
                    ("Attempts", attempts), ("ElapsedMs", stopwatch.ElapsedMilliseconds), ("Status", assessment.Status), ("Reason", assessment.Reason), ("Action", "Passive"));
                return assessment;
            }

            await _hardwareProbeDelay(_hardwareProbePollInterval, cancellationToken).ConfigureAwait(false);
            attempts++;
            assessment = EvaluateHardwareCompatibility();
            AppLog.Debug("Hardware", "Startup hardware probe stabilization poll.", ("Attempts", attempts), ("ElapsedMs", stopwatch.ElapsedMilliseconds), ("Status", assessment.Status));
        }

        AppLog.Info("Hardware", "Startup hardware probe stabilization satisfied.", ("Attempts", attempts), ("ElapsedMs", stopwatch.ElapsedMilliseconds), ("Status", assessment.Status), ("Reason", assessment.Reason));
        return assessment;
    }

    private static bool IsTransientHardwareProbeResult(HardwareCompatibilityAssessment assessment) =>
        assessment.Status == HardwareCompatibilityStatus.Indeterminate
        || (assessment.Status == HardwareCompatibilityStatus.Unsupported && assessment.Reason == "No handheld-device adapter matched.");
}

/// <param name="HardwareSupported">The single startup decision of whether this machine is a
/// supported MSI Claw (<see cref="HardwareCompatibilityStatus.Supported"/>). Defaults to false so a
/// construction path that does not reach the hardware gate can never report support it never
/// established. Consumed by the OEM1 mapping availability gate; never recomputed downstream.</param>
internal sealed record StartupResult(
    bool ShouldStartRuntime,
    ControllerEnvironmentMode EnvironmentMode,
    ControllerEnvironmentReadiness EnvironmentReadiness,
    bool RecoverySafe = false,
    bool HardwareSupported = false,
    HandheldDeviceModelId? HardwareDeviceModel = null,
    HardwareCompatibilityStatus? HardwareStatus = null);
