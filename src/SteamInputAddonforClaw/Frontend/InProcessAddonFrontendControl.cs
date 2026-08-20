using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;
using SteamInputAddonforClaw.Diagnostics.EnvironmentDiscovery;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Profiles.Performance;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Runtime;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.FrontendTransport;
using System.Diagnostics;

namespace SteamInputAddonforClaw.Frontend;

internal sealed class InProcessAddonFrontendControl : IAddonFrontendControl
{
    private readonly StartupSettingsCoordinator _settings;
    private readonly ISystemStatusProvider _status;
    private readonly AddonRuntimeHost? _runtime;
    private readonly Func<RoutingRuntimeStatusSnapshot> _captureRoutingStatus;
    private readonly DeveloperTestModeState _developer;
    private string _registrationMessage;
    private readonly IFrontendPrerequisiteSetupExecutor _setupExecutor;
    private readonly Func<string?> _processPath;
    private readonly bool _oem1MappingAvailable;
    private int _shutdownStarted;
    private readonly object _vibrationSessionGate = new();
    private Feedback.VibrationTestSessionWriter? _vibrationSession;
    private readonly object _clawSensorProbeGate = new();
    private ClawSensorProbeSession? _clawSensorProbe;

    /// <summary>Wraps the Runtime-owned <see cref="ClawSensorProbeCoordinator"/> for one active
    /// diagnostic session, plus the device identity captured at Open time (so a stale-but-still-open
    /// session keeps reporting the identity it was opened with) and the last operation's error text.</summary>
    private sealed class ClawSensorProbeSession(ClawSensorProbeCoordinator coordinator, string manufacturer, string model, string baseBoard, string resolvedModel)
    {
        public ClawSensorProbeCoordinator Coordinator { get; } = coordinator;
        public string Manufacturer { get; } = manufacturer;
        public string Model { get; } = model;
        public string BaseBoard { get; } = baseBoard;
        public string ResolvedModel { get; } = resolvedModel;
        public string? ErrorMessage { get; set; }
        public required string HardwareStatus { get; init; }
        public required string HardwareFamily { get; init; }
        public required string HardwareModel { get; init; }
        public required string HardwareReason { get; init; }
    }
    // Device/Profile CPU Boost -- a sibling capability, not a member of Routing/OEM1 (work order
    // PR277 section 1): this projection deliberately has NO dependency on _runtime/routing status
    // and must keep working when _runtime is null (no routing composition at all).
    private readonly CpuBoostRuntime? _cpuBoostRuntime;

    /// <param name="oem1MappingAvailable">The startup hardware-support result
    /// (<see cref="Startup.StartupResult.HardwareSupported"/>), reported verbatim on bootstrap so the
    /// UI gates the Center M Button feature on the SAME fact the routing composition's OEM1 action
    /// path gates on. Defaults to false so any construction path that never established hardware
    /// support reports the feature unavailable rather than offering it.</param>
    /// <param name="cpuBoostRuntime">The Device/Profile CPU Boost Runtime authority (owned by
    /// <c>AddonProcessHost</c>, independent of <paramref name="runtime"/>). Null is a valid, passive
    /// state -- CPU Boost frontend operations simply report unavailable, exactly like every other
    /// null-runtime fallback on this class.</param>
    internal InProcessAddonFrontendControl(StartupSettingsCoordinator settings, ISystemStatusProvider status, AddonRuntimeHost? runtime, DeveloperTestModeState developer, string registrationMessage, IFrontendPrerequisiteSetupExecutor? setupExecutor = null, Func<string?>? processPath = null, Func<RoutingRuntimeStatusSnapshot>? captureRoutingStatus = null, bool oem1MappingAvailable = false, CpuBoostRuntime? cpuBoostRuntime = null)
    {
        _oem1MappingAvailable = oem1MappingAvailable;
        _cpuBoostRuntime = cpuBoostRuntime;
        _settings = settings;
        _status = status;
        _runtime = runtime;
        _captureRoutingStatus = captureRoutingStatus ?? (() => _runtime?.CaptureRoutingStatus() ?? throw new InvalidOperationException("Routing status is unavailable."));
        _developer = developer;
        _registrationMessage = registrationMessage;
        _setupExecutor = setupExecutor ?? new FrontendPrerequisiteSetupExecutor();
        _processPath = processPath ?? (() => Environment.ProcessPath);
        if (_runtime is not null)
        {
            _runtime.SteamSessionStateChanged += (_, _) => StateInvalidated?.Invoke(this, EventArgs.Empty);
            _runtime.StatusRefreshRequested += (_, _) => StateInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? StateInvalidated;

    public Task<FrontendBootstrapSnapshot> GetBootstrapAsync(CancellationToken cancellationToken = default) => Task.FromResult(new FrontendBootstrapSnapshot(MapSettings(), _registrationMessage, new(_developer.IsEnabled), AppLog.DirectoryPath, _oem1MappingAvailable));

    public async Task<FrontendStatusSnapshot> CaptureStatusAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _status.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var setup = _setupExecutor.Evaluate(snapshot);
        return FrontendSnapshotMapper.ApplySetup(FrontendSnapshotMapper.Map(snapshot, _captureRoutingStatus()), setup);
    }

    public Task<FrontendLaunchAtStartupResult> SetLaunchAtWindowsStartupAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        var result = _settings.ChangeLaunchAtWindowsStartup(enabled);
        _registrationMessage = result.Message;
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(new FrontendLaunchAtStartupResult(MapSettings(), _registrationMessage));
    }

    public Task<FrontendSettingsSnapshot> SetSteamInputRoutingEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        _settings.ChangeSteamInputRoutingEnabled(enabled);
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(MapSettings());
    }

    public Task<FrontendSettingsSnapshot> SetLogLevelAsync(FrontendLogLevel level, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        _settings.ChangeLogLevel(level switch { FrontendLogLevel.Debug => AppLogPreference.Debug, FrontendLogLevel.Info => AppLogPreference.Info, _ => AppLogPreference.Off });
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(MapSettings());
    }

    public Task<FrontendSettingsSnapshot> SetOem1MappingAsync(Contracts.Oem1.Oem1MappingSettings mapping, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ThrowIfShuttingDown();
        _settings.ChangeOem1Mapping(mapping);
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(MapSettings());
    }

    public Task<FrontendSettingsSnapshot> SuppressDeveloperMenuWarningAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        _settings.SuppressDeveloperMenuWarningPermanently();
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(MapSettings());
    }

    public Task<FrontendDeveloperSnapshot> SetDeveloperTestModeAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        _developer.SetEnabled(enabled);
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(new FrontendDeveloperSnapshot(_developer.IsEnabled));
    }

    /// <summary>Called when the Vibration Test detail page is entered: creates the dedicated session
    /// log immediately (even if no command is ever run) so the file always has a header recording
    /// current Test Mode/routing state at page entry. Idempotent -- a call while a session is
    /// already open returns that same session's file rather than starting a second one.</summary>
    public Task<FrontendVibrationTestResult> OpenVibrationTestSessionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        var session = GetOrOpenVibrationSession();
        return Task.FromResult(new FrontendVibrationTestResult(true, "SessionOpened", session.FilePath));
    }

    public async Task<FrontendVibrationTestResult> RunVibrationTestAsync(FrontendVibrationTestCommand command, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        var testModeEnabled = _developer.IsEnabled;
        var steamOutputActive = _captureRoutingStatus().SteamOutputActive;
        if (!testModeEnabled) return new FrontendVibrationTestResult(false, "Enable Test Mode from Developer Menu.", null);
        if (!steamOutputActive) return new FrontendVibrationTestResult(false, "Steam Deck output is not active.", null);

        var session = GetOrOpenVibrationSession();
        var started = Stopwatch.GetTimestamp();
        WriteVibrationSessionIfCurrent(session, $"Command={command} Opcode={VibrationTestOpcode(command)} TestModeEnabled={testModeEnabled} SteamOutputActive={steamOutputActive}");
        var outcome = await (_runtime?.RunDeveloperVibrationTestAsync(command, cancellationToken) ?? Task.FromResult(new Feedback.DeveloperVibrationTestOutcome(false, null, null))).ConfigureAwait(false);
        // Accepted (authority/sequence) and physically-successful are different questions: a real MSI
        // HID write failure must be visible here even when the write was accepted, so PhysicalStatus/
        // PhysicalReason are logged separately from Succeeded rather than folded into one boolean.
        WriteVibrationSessionIfCurrent(session, $"Result Command={command} Succeeded={outcome.Succeeded} DurationMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} {DecodeFields(outcome.Decode)} PhysicalStatus={outcome.CommandResult?.Status} PhysicalReason={outcome.CommandResult?.Reason} StopPhysicalStatus={outcome.StopResult?.Status} StopPhysicalReason={outcome.StopResult?.Reason}");

        var (succeeded, reason) = MapVibrationTestOutcome(outcome);
        return new FrontendVibrationTestResult(succeeded, reason, session.FilePath);
    }

    /// <summary>Pure mapping, factored out for direct unit testing: <see cref="Feedback.DeveloperVibrationTestOutcome.Succeeded"/>
    /// only means the write was ACCEPTED (authority/sequence), not that the physical MSI HID write
    /// succeeded -- a truthful diagnostic result must require the actual write(s) to have succeeded
    /// too, or a real hardware failure would be reported to the page as "Succeeded".</summary>
    internal static (bool Succeeded, string Reason) MapVibrationTestOutcome(Feedback.DeveloperVibrationTestOutcome outcome)
    {
        var commandPhysicalOk = outcome.CommandResult is { } commandResult && commandResult.Succeeded;
        var stopRequired = outcome.Decode?.Command == Feedback.SteamDeckFeedbackCommand.HapticPulse;
        var stopPhysicalOk = outcome.StopResult is { } stopResult
            ? stopResult.Succeeded
            : !stopRequired;
        var succeeded = outcome.Succeeded && commandPhysicalOk && stopPhysicalOk;
        var reason = !outcome.Succeeded
            ? "Feedback bridge is unavailable, superseded, or the test was cancelled."
            : !commandPhysicalOk
                ? $"Physical write failed: {outcome.CommandResult?.Reason ?? "Unknown"}"
                : !stopPhysicalOk
                    ? $"Physical STOP failed: {outcome.StopResult?.Reason ?? "Unknown"}"
                    : "Succeeded";
        return (succeeded, reason);
    }

    /// <summary>Test-only seam so the disposed-writer race fix can be exercised directly without
    /// reproducing the exact 250ms async interleaving through a real runtime.</summary>
    internal Feedback.VibrationTestSessionWriter? TestOnly_CurrentVibrationSession { get { lock (_vibrationSessionGate) return _vibrationSession; } }

    /// <summary>Writes to the session only if it is still the currently open one: a page exit can
    /// close (detach + dispose) the session while an in-flight EB/EA developer command is still
    /// awaiting its 250ms delayed STOP, and writing to an already-disposed <c>StreamWriter</c> would
    /// throw from an <c>async void</c> UI click handler. Serializes against the same
    /// <see cref="_vibrationSessionGate"/> <see cref="CloseVibrationTestSessionAsync"/> detaches
    /// under, so a write either completes before detach or observes the session is stale and no-ops.</summary>
    internal void WriteVibrationSessionIfCurrent(Feedback.VibrationTestSessionWriter session, string message)
    {
        lock (_vibrationSessionGate)
        {
            if (ReferenceEquals(_vibrationSession, session)) session.Write(message);
        }
    }

    /// <summary>Called when the Vibration Test detail page is left, regardless of how: cancels any
    /// pending developer-owned delayed STOP so it cannot later stop newer real Steam feedback, issues
    /// a best-effort production-path STOP, and flushes/closes the dedicated session log.</summary>
    public async Task<FrontendVibrationTestResult> CloseVibrationTestSessionAsync(CancellationToken cancellationToken = default)
    {
        Feedback.VibrationTestSessionWriter? session;
        lock (_vibrationSessionGate) { session = _vibrationSession; _vibrationSession = null; }
        if (session is null) return new FrontendVibrationTestResult(true, "NoSessionActive", null);

        var stop = _runtime?.CancelDeveloperVibrationTest();
        session.Write($"SessionClosed CancelledPendingDeveloperStop=True BestEffortStopRequested=True PhysicalStatus={stop?.Status} PhysicalReason={stop?.Reason}");
        var path = session.FilePath;
        await session.DisposeAsync().ConfigureAwait(false);
        return new FrontendVibrationTestResult(true, "SessionClosed", path);
    }

    private Feedback.VibrationTestSessionWriter GetOrOpenVibrationSession()
    {
        lock (_vibrationSessionGate)
        {
            if (_vibrationSession is { } existing) return existing;
            var session = new Feedback.VibrationTestSessionWriter(AppLog.DirectoryPath);
            var routing = _captureRoutingStatus();
            var appVersion = typeof(InProcessAddonFrontendControl).Assembly.GetName().Version?.ToString() ?? "Unknown";
            session.Write($"SessionStarted AppVersion={appVersion} TestModeEnabled={_developer.IsEnabled} RoutingState={routing.OperationalState} SteamOutputActive={routing.SteamOutputActive} NativeDirectInputActive={routing.NativeDirectInputActive}");
            _vibrationSession = session;
            return session;
        }
    }

    private static string VibrationTestOpcode(FrontendVibrationTestCommand command) => command switch
    {
        FrontendVibrationTestCommand.Rumble => "0xEB",
        FrontendVibrationTestCommand.Haptic => "0xEA",
        FrontendVibrationTestCommand.HapticPulse => "0x8F",
        FrontendVibrationTestCommand.Stop => "0xEB(zero)",
        _ => "Unknown"
    };

    private static string DecodeFields(Feedback.SteamDeckFeedbackDecodeResult? decoded) => decoded switch
    {
        { Command: Feedback.SteamDeckFeedbackCommand.Rumble, Rumble: var rumble } => $"Decode=Rumble Large16={rumble.LargeMotor} Small16={rumble.SmallMotor} Large8={rumble.LargeMotor >> 8} Small8={rumble.SmallMotor >> 8}",
        { Command: Feedback.SteamDeckFeedbackCommand.Haptic, Rumble: var rumble, Intensity: var intensity, Gain: var gain, Strength8: var strength } => $"Decode=Haptic Intensity={intensity} Gain={gain} Strength8={strength} Strength16={rumble.LargeMotor}",
        { Command: Feedback.SteamDeckFeedbackCommand.HapticPulse, Rumble: var rumble, PulsePeriod: var period, PulseCount: var count, Gain: var gain, Strength8: var strength, PulseDurationMilliseconds: var duration } => $"Decode=HapticPulse Period={period} Count={count} Gain={gain} Strength8={strength} Strength16={rumble.LargeMotor} PulseDurationMs={duration}",
        _ => "Decode=Unavailable"
    };

    public async Task<FrontendPrerequisiteSetupResult> RunPrerequisiteSetupAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        var current = await _status.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var setup = _setupExecutor.Evaluate(current);
        AppLog.Info("PrerequisiteSetup", "Prerequisite setup requested.",
            ("HidHideStatus", current.Prerequisites.HidHide.Status),
            ("UsbIpWin2Status", current.Prerequisites.UsbIpWin2.Status),
            ("CompatibilityStatus", current.Compatibility.Status),
            ("CompatibilityReason", current.Compatibility.Reason),
            ("SteamActive", current.Steam.IsActive),
            ("RecoverySafe", current.RecoverySafe),
            ("AddonOwnedOutputIdentityUncertain", current.AddonOwnedOutputIdentityUncertain),
            ("SetupStatus", setup.Status));
        var mapped = FrontendSnapshotMapper.ApplySetup(FrontendSnapshotMapper.Map(current, _captureRoutingStatus()), setup);
        if (!PrerequisiteSetupPromptPolicy.IsInstallable(setup))
            return new(FrontendPrerequisiteSetupResultKind.NotInstallable, mapped);
        ThrowIfShuttingDown();
        var executable = _processPath() ?? throw new InvalidOperationException("The executable path is unavailable.");
        var result = await _setupExecutor.RunAsync(setup, executable, cancellationToken).ConfigureAwait(false);
        // RunIfInstallableAsync returns null only when its safety policy declines to launch.
        // Preserve that distinction from an elevated helper that actually returns Blocked.
        if (result is null) return new(FrontendPrerequisiteSetupResultKind.NotInstallable, mapped);
        var resultKind = MapResultKind(ElevatedPrerequisiteSetup.TranslateExitCode(result));
        // No OEM1 reconcile here: HidHide/usbip setup no longer mutates any OEM1 prerequisite. OEM1
        // arming is owned entirely by the mapping-change/startup lifecycle plus the coordinator's own
        // environment/Launcher/Server/process/helper reconciliation.
        FrontendStatusSnapshot? postStatus = null;
        try
        {
            postStatus = await CaptureStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLog.Warn("PrerequisiteSetup", "Post-setup status refresh failed.", exception, ("Result", resultKind));
        }
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return new(resultKind, postStatus);
    }

    private static FrontendPrerequisiteSetupResultKind MapResultKind(ElevatedPrerequisiteSetup.ResultKind kind) => kind switch
    {
        ElevatedPrerequisiteSetup.ResultKind.Ready => FrontendPrerequisiteSetupResultKind.Ready,
        ElevatedPrerequisiteSetup.ResultKind.Installed => FrontendPrerequisiteSetupResultKind.Installed,
        ElevatedPrerequisiteSetup.ResultKind.RebootRequired => FrontendPrerequisiteSetupResultKind.RebootRequired,
        ElevatedPrerequisiteSetup.ResultKind.Cancelled => FrontendPrerequisiteSetupResultKind.Cancelled,
        ElevatedPrerequisiteSetup.ResultKind.Blocked => FrontendPrerequisiteSetupResultKind.Blocked,
        ElevatedPrerequisiteSetup.ResultKind.AlreadyInProgress => FrontendPrerequisiteSetupResultKind.AlreadyInProgress,
        _ => FrontendPrerequisiteSetupResultKind.Failed
    };

    internal void BeginProcessShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return;
        Feedback.VibrationTestSessionWriter? session;
        lock (_vibrationSessionGate) { session = _vibrationSession; _vibrationSession = null; }
        if (session is not null)
        {
            var stop = _runtime?.CancelDeveloperVibrationTest();
            session.Write($"SessionClosed Reason=RuntimeShutdown CancelledPendingDeveloperStop=True BestEffortStopRequested=True PhysicalStatus={stop?.Status} PhysicalReason={stop?.Reason}");
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        ClawSensorProbeSession? probe;
        lock (_clawSensorProbeGate) { probe = _clawSensorProbe; _clawSensorProbe = null; }
        if (probe is not null)
        {
            try { probe.Coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception exception) { AppLog.Warn("ClawSensorProbe", "Probe shutdown cleanup failed.", exception); }
        }
    }

    // ---- Claw Sensor Probe (developer-only gyro/accelerometer diagnostic) ----

    public async Task<FrontendClawSensorProbeSnapshot> OpenClawSensorProbeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        ClawSensorProbeSession? existing;
        lock (_clawSensorProbeGate) existing = _clawSensorProbe;
        if (existing is not null && existing.Coordinator.State is not (ClawSensorProbeState.Completed or ClawSensorProbeState.Failed))
            return MapClawSensorProbeSnapshot(existing);

        var status = await _status.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var hardware = status.HardwareCompatibility;
        if (!ClawSensorProbeCoordinator.AllowsReadOnlyDiagnostic(hardware))
            return FrontendClawSensorProbeSnapshot.Unavailable with
            {
                Manufacturer = status.Device.Manufacturer,
                Model = status.Device.Model,
                BaseBoard = status.Device.BaseBoardProduct,
                ErrorMessage = "This diagnostic is available only on an identified MSI Claw device."
            };

        // A previous session that Completed/Failed is disposed and replaced with a fresh one -- the
        // old report/output directory remains on disk, but the next Open starts a clean session.
        if (existing is not null)
            await existing.Coordinator.DisposeAsync().ConfigureAwait(false);

        var resolvedModel = hardware.DeviceModel?.Value ?? "Unknown / unresolved";
        var coordinator = new ClawSensorProbeCoordinator();
        coordinator.Prepare();
        // Identity/compatibility are captured now but NOT written yet: ClawSensorProbeCoordinator's
        // SetDeviceIdentity/SetHardwareCompatibility write through the session writer, which Start()
        // does not create until StartClawSensorProbeAsync() runs. Writing here would silently no-op
        // and drop this metadata from the finalized report (review finding #1 on PR #290).
        var session = new ClawSensorProbeSession(coordinator, status.Device.Manufacturer, status.Device.Model, status.Device.BaseBoardProduct, resolvedModel)
        {
            HardwareStatus = hardware.Status.ToString(),
            HardwareFamily = hardware.DeviceFamily?.Value ?? "Unavailable",
            HardwareModel = hardware.DeviceModel?.Value ?? "Unavailable",
            HardwareReason = hardware.Reason,
        };

        // The initial ThrowIfShuttingDown() above only covers the time before the awaited
        // _status.CaptureAsync() call: BeginProcessShutdown() can run its one-time session
        // detach/dispose pass while this request is suspended there, and the named-pipe server isn't
        // torn down until later in process disposal, so a request already past that first check could
        // otherwise resume and commit a brand-new coordinator after shutdown began. Re-check the flag
        // atomically with the commit under the same gate used by BeginProcessShutdown/Close, and
        // dispose a rejected candidate outside the lock (PR #290 re-review).
        bool rejectForShutdown;
        lock (_clawSensorProbeGate)
        {
            rejectForShutdown = Volatile.Read(ref _shutdownStarted) != 0;
            if (!rejectForShutdown) _clawSensorProbe = session;
        }
        if (rejectForShutdown)
        {
            await coordinator.DisposeAsync().ConfigureAwait(false);
            throw new FrontendProtocolException("Runtime is shutting down.");
        }

        return MapClawSensorProbeSnapshot(session);
    }

    public async Task<FrontendClawSensorProbeSnapshot> StartClawSensorProbeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        var session = CurrentClawSensorProbeSession();
        if (session is null) return FrontendClawSensorProbeSnapshot.Unavailable;
        try
        {
            session.Coordinator.Start();
            session.Coordinator.SetDeviceIdentity(session.Manufacturer, session.Model, session.BaseBoard, session.ResolvedModel);
            session.Coordinator.SetHardwareCompatibility(session.HardwareStatus, session.HardwareFamily, session.HardwareModel, session.HardwareReason);

            // Link the RPC's own token with the coordinator's lifecycle token so a Runtime shutdown
            // (BeginProcessShutdown -> coordinator disposal) promptly cancels an in-flight countdown
            // instead of letting it run to BeginRecording() against an already-disposed coordinator
            // (review finding #2 on PR #290).
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Coordinator.LifecycleCancellation);
            await session.Coordinator.StartCaptureAsync(linked.Token).ConfigureAwait(false);
            await session.Coordinator.CountdownAsync(_ => Task.CompletedTask, ClawSensorProbePhaseLabel, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            session.Coordinator.BeginRecording();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            session.ErrorMessage = exception.Message;
            try { await session.Coordinator.FailAsync(exception.Message, CancellationToken.None).ConfigureAwait(false); } catch { /* best-effort */ }
        }
        return MapClawSensorProbeSnapshot(session);
    }

    public async Task<FrontendClawSensorProbeSnapshot> CaptureClawSensorProbeAsync(CancellationToken cancellationToken = default)
    {
        // Obtain the session AND its lifecycle token together, under the same gate
        // BeginProcessShutdown()/Close() use to detach+dispose the coordinator: reading
        // session.Coordinator.LifecycleCancellation outside the lock (after only checking the
        // session reference was non-null) leaves a window where shutdown can dispose the coordinator
        // -- and therefore the CancellationTokenSource backing LifecycleCancellation -- in between,
        // turning an ordinary in-flight poll into an unexpected ObjectDisposedException instead of a
        // graceful Unavailable (PR #290 re-review).
        ClawSensorProbeSession? session;
        CancellationToken lifecycle;
        lock (_clawSensorProbeGate)
        {
            if (Volatile.Read(ref _shutdownStarted) != 0) return FrontendClawSensorProbeSnapshot.Unavailable;
            session = _clawSensorProbe;
            if (session is null) return FrontendClawSensorProbeSnapshot.Unavailable;
            lifecycle = session.Coordinator.LifecycleCancellation;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // The old page's 200ms UI timer explicitly promoted a dead sensor reader to a finalized
        // Failed diagnostic (ClawSensorProbeUiTimer_Tick -> FailOnReaderFaultAsync). The restored
        // page polls this method at the same ~200ms cadence, so run the same reconciliation here --
        // FailOnReaderFaultAsync no-ops when there is no fault, so this preserves the old behavior
        // without introducing a second health authority (PR #290 re-review finding #1).
        //
        // Deliberately NOT linked to Coordinator.LifecycleCancellation: FailAsync() cancels that same
        // token as part of entering terminal failure, so a linked token here would self-cancel
        // ShutdownReadersAndApiAsync mid-teardown and skip FinalizeAsync() (PR #290 re-review, fixed
        // at the coordinator level too). Once lifecycle cancellation has already fired (Runtime
        // shutdown/dispose in flight), skip reconciliation entirely and report the session's last
        // known snapshot instead of racing that teardown.
        try
        {
            if (!lifecycle.IsCancellationRequested)
                await session.Coordinator.FailOnReaderFaultAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _shutdownStarted) != 0) { return FrontendClawSensorProbeSnapshot.Unavailable; }

        if (Volatile.Read(ref _shutdownStarted) != 0) return FrontendClawSensorProbeSnapshot.Unavailable;
        return MapClawSensorProbeSnapshot(session);
    }

    public Task<FrontendClawSensorProbeSnapshot> NextClawSensorProbePhaseAsync(CancellationToken cancellationToken = default) =>
        AdvanceClawSensorProbeAsync(forward: true, cancellationToken);

    public Task<FrontendClawSensorProbeSnapshot> PreviousClawSensorProbePhaseAsync(CancellationToken cancellationToken = default) =>
        AdvanceClawSensorProbeAsync(forward: false, cancellationToken);

    private async Task<FrontendClawSensorProbeSnapshot> AdvanceClawSensorProbeAsync(bool forward, CancellationToken cancellationToken)
    {
        ThrowIfShuttingDown();
        var session = CurrentClawSensorProbeSession();
        if (session is null) return FrontendClawSensorProbeSnapshot.Unavailable;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Coordinator.LifecycleCancellation);
            if (forward)
                await session.Coordinator.AdvancePhaseAsync(_ => Task.CompletedTask, ClawSensorProbePhaseLabel, () => { }, linked.Token).ConfigureAwait(false);
            else
                await session.Coordinator.RevisitPreviousPhaseAsync(_ => Task.CompletedTask, ClawSensorProbePhaseLabel, () => { }, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            if (session.Coordinator.State == ClawSensorProbeState.Completed)
                await session.Coordinator.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            session.ErrorMessage = exception.Message;
            try { await session.Coordinator.FailAsync(exception.Message, CancellationToken.None).ConfigureAwait(false); } catch { /* best-effort */ }
        }
        return MapClawSensorProbeSnapshot(session);
    }

    public async Task<FrontendClawSensorProbeSnapshot> StopClawSensorProbeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        var session = CurrentClawSensorProbeSession();
        if (session is null) return FrontendClawSensorProbeSnapshot.Unavailable;
        try { await session.Coordinator.StopAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { session.ErrorMessage = exception.Message; }
        return MapClawSensorProbeSnapshot(session);
    }

    public async Task<FrontendClawSensorProbeSnapshot> CloseClawSensorProbeAsync(CancellationToken cancellationToken = default)
    {
        ClawSensorProbeSession? session;
        lock (_clawSensorProbeGate) { session = _clawSensorProbe; _clawSensorProbe = null; }
        if (session is null) return FrontendClawSensorProbeSnapshot.Unavailable;
        try
        {
            if (session.Coordinator.State is ClawSensorProbeState.Starting or ClawSensorProbeState.Countdown or ClawSensorProbeState.RecordingPhase)
                await session.Coordinator.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) { AppLog.Warn("ClawSensorProbe", "Probe stop-on-close failed.", exception); }
        finally { await session.Coordinator.DisposeAsync().ConfigureAwait(false); }
        return FrontendClawSensorProbeSnapshot.Unavailable;
    }

    private ClawSensorProbeSession? CurrentClawSensorProbeSession() { lock (_clawSensorProbeGate) return _clawSensorProbe; }

    private static string ClawSensorProbePhaseLabel(ClawSensorProbePhase phase) => phase switch
    {
        ClawSensorProbePhase.REST => "Keep Still",
        ClawSensorProbePhase.ROLL_LEFT => "Roll Left",
        ClawSensorProbePhase.ROLL_RIGHT => "Roll Right",
        ClawSensorProbePhase.PITCH_UP => "Pitch Up",
        ClawSensorProbePhase.PITCH_DOWN => "Pitch Down",
        ClawSensorProbePhase.YAW_LEFT => "Yaw Left",
        _ => "Yaw Right"
    };

    private static FrontendClawSensorProbeSnapshot MapClawSensorProbeSnapshot(ClawSensorProbeSession session)
    {
        var coordinator = session.Coordinator;
        var workflow = coordinator.Workflow;
        var phase = workflow.CurrentIndex >= 0 ? workflow.Visits[^1].Phase : ClawSensorProbePhase.REST;
        var gyro = coordinator.LiveSnapshot?.Gyro;
        var accel = coordinator.LiveSnapshot?.Accel;
        return new FrontendClawSensorProbeSnapshot(
            Available: true,
            State: MapClawSensorProbeState(coordinator.State),
            Phase: MapClawSensorProbePhase(phase),
            PhaseIndex: workflow.CurrentIndex,
            PhaseCount: ClawSensorProbeWorkflow.Phases.Count,
            Discovery: MapClawSensorProbeDiscovery(coordinator.Discovery),
            Gyro: gyro is { } g ? new(g.X, g.Y, g.Z, g.Hz, g.Count) : FrontendClawSensorProbeAxisSnapshot.Empty,
            Accel: accel is { } a ? new(a.X, a.Y, a.Z, a.Hz, a.Count) : FrontendClawSensorProbeAxisSnapshot.Empty,
            GyroscopeSummary: MapClawSensorProbeStatistics(coordinator.GyroscopeSummary),
            AccelerometerSummary: MapClawSensorProbeStatistics(coordinator.AccelerometerSummary),
            DroppedSampleCount: coordinator.DroppedSampleCount,
            DroppedGyroscopeCount: coordinator.DroppedGyroscopeCount,
            DroppedAccelerometerCount: coordinator.DroppedAccelerometerCount,
            ReaderErrors: coordinator.ReaderErrors,
            OutputDirectory: coordinator.OutputDirectory,
            HasReport: coordinator.HasReport,
            ErrorMessage: session.ErrorMessage,
            Manufacturer: session.Manufacturer,
            Model: session.Model,
            BaseBoard: session.BaseBoard,
            ResolvedModel: session.ResolvedModel);
    }

    private static FrontendClawSensorProbeState MapClawSensorProbeState(ClawSensorProbeState state) => state switch
    {
        ClawSensorProbeState.Idle => FrontendClawSensorProbeState.Idle,
        ClawSensorProbeState.Discovering => FrontendClawSensorProbeState.Discovering,
        ClawSensorProbeState.Ready => FrontendClawSensorProbeState.Ready,
        ClawSensorProbeState.Starting => FrontendClawSensorProbeState.Starting,
        ClawSensorProbeState.Countdown => FrontendClawSensorProbeState.Countdown,
        ClawSensorProbeState.RecordingPhase => FrontendClawSensorProbeState.RecordingPhase,
        ClawSensorProbeState.Stopping => FrontendClawSensorProbeState.Stopping,
        ClawSensorProbeState.Completed => FrontendClawSensorProbeState.Completed,
        _ => FrontendClawSensorProbeState.Failed
    };

    private static FrontendClawSensorProbePhase MapClawSensorProbePhase(ClawSensorProbePhase phase) => phase switch
    {
        ClawSensorProbePhase.REST => FrontendClawSensorProbePhase.Rest,
        ClawSensorProbePhase.ROLL_LEFT => FrontendClawSensorProbePhase.RollLeft,
        ClawSensorProbePhase.ROLL_RIGHT => FrontendClawSensorProbePhase.RollRight,
        ClawSensorProbePhase.PITCH_UP => FrontendClawSensorProbePhase.PitchUp,
        ClawSensorProbePhase.PITCH_DOWN => FrontendClawSensorProbePhase.PitchDown,
        ClawSensorProbePhase.YAW_LEFT => FrontendClawSensorProbePhase.YawLeft,
        _ => FrontendClawSensorProbePhase.YawRight
    };

    private static FrontendClawSensorProbeDiscovery? MapClawSensorProbeDiscovery(ClawSensorDiscovery? discovery)
    {
        if (discovery is null) return null;
        return new FrontendClawSensorProbeDiscovery(
            [.. discovery.Sensors.Select(MapClawSensorProbeCandidate)],
            discovery.Gyroscope is { } gyro ? MapClawSensorProbeCandidate(gyro) : null,
            discovery.Accelerometer is { } accel ? MapClawSensorProbeCandidate(accel) : null,
            discovery.Errors,
            discovery.IsValid);
    }

    private static FrontendClawSensorProbeCandidate MapClawSensorProbeCandidate(ClawSensorProbeCandidate candidate) => new(
        candidate.FriendlyName, candidate.SensorId, candidate.TypeGuid, candidate.CategoryGuid,
        candidate.Manufacturer, candidate.Model, candidate.PersistentUniqueId, candidate.MinimumReportInterval, candidate.CustomUsage);

    private static FrontendClawSensorProbeStatistics? MapClawSensorProbeStatistics(ClawSensorProbeStatistics? statistics) => statistics is null
        ? null
        : new(statistics.SampleCount, statistics.DroppedSampleCount, statistics.DurationMs, statistics.AverageIntervalMs, statistics.MinimumIntervalMs, statistics.MaximumIntervalMs, statistics.EffectiveHz);

    private void ThrowIfShuttingDown()
    {
        if (Volatile.Read(ref _shutdownStarted) != 0)
            throw new FrontendProtocolException("Runtime is shutting down.");
    }

    public async Task<FrontendEnvironmentReportResult> GenerateEnvironmentReportAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await new EnvironmentDiscoveryReportGenerator(new WindowsEnvironmentDiscoverySnapshotSource(), new EnvironmentDiscoveryReportStore(AppLog.DirectoryPath), new EnvironmentDiscoveryReportWriter()).GenerateAsync().ConfigureAwait(false);
            return new(true, null);
        }
        catch (Exception exception)
        {
            AppLog.Warn("EnvironmentDiscovery", "Environment discovery report generation failed.", exception, ("Reason", exception.GetType().Name));
            return new(false, exception.Message);
        }
    }

    private FrontendSettingsSnapshot MapSettings() => new(_settings.Settings.LaunchAtWindowsStartup, _settings.Settings.LogLevel switch { AppLogPreference.Debug => FrontendLogLevel.Debug, AppLogPreference.Info => FrontendLogLevel.Info, _ => FrontendLogLevel.Off }, _settings.SteamInputRoutingEnabled, _settings.SuppressDeveloperMenuWarning, _settings.Oem1Mapping);

    // ---- Device/Profile CPU Boost (work order PR277) -- deliberately independent of Routing/OEM1:
    // none of these three methods reads _runtime, _captureRoutingStatus, or any routing/Steam/OEM1
    // state. Read-only: CaptureCpuBoostAsync never mutates ProfileStore or Windows (section 8/21). ----

    public Task<FrontendCpuBoostSnapshot> CaptureCpuBoostAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_cpuBoostRuntime is null ? FrontendCpuBoostSnapshot.Unavailable : MapCpuBoostSnapshot(_cpuBoostRuntime.Snapshot));

    public Task<FrontendCpuBoostMutationResult> SetDeviceCpuBoostAcAsync(CpuBoostMode mode, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        return Task.FromResult(MutateCpuBoost(ac: true, mode));
    }

    public Task<FrontendCpuBoostMutationResult> SetDeviceCpuBoostDcAsync(CpuBoostMode mode, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        return Task.FromResult(MutateCpuBoost(ac: false, mode));
    }

    /// <summary>Device CPU Boost Toggle addendum: turns the Device/global apply path on or off.
    /// Not an application-wide switch, never gates a future Game Profile CPU Boost path.</summary>
    public Task<FrontendCpuBoostMutationResult> SetDeviceCpuBoostEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        if (_cpuBoostRuntime is null)
            return Task.FromResult(new FrontendCpuBoostMutationResult(FrontendCpuBoostMutationOutcome.PersistenceFailed, "CPU Boost is unavailable.", FrontendCpuBoostSnapshot.Unavailable));

        var result = _cpuBoostRuntime.SetDeviceCpuBoostEnabled(enabled);
        var snapshot = MapCpuBoostSnapshot(_cpuBoostRuntime.Snapshot);
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(new FrontendCpuBoostMutationResult(MapMutationOutcome(result.Outcome), result.FailureMessage, snapshot));
    }

    private FrontendCpuBoostMutationResult MutateCpuBoost(bool ac, CpuBoostMode mode)
    {
        if (_cpuBoostRuntime is null)
            return new FrontendCpuBoostMutationResult(FrontendCpuBoostMutationOutcome.PersistenceFailed, "CPU Boost is unavailable.", FrontendCpuBoostSnapshot.Unavailable);

        var result = ac ? _cpuBoostRuntime.SetDeviceCpuBoostAc(mode) : _cpuBoostRuntime.SetDeviceCpuBoostDc(mode);
        var snapshot = MapCpuBoostSnapshot(_cpuBoostRuntime.Snapshot);
        // Fire regardless of outcome: PersistenceFailed means the page must refresh/restore to the
        // authoritative (unchanged) snapshot, and ApplyFailed means the NEW desired value is now
        // authoritative -- both are real state changes the page must re-render (work order section 7).
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return new FrontendCpuBoostMutationResult(MapMutationOutcome(result.Outcome), result.FailureMessage, snapshot);
    }

    private static FrontendCpuBoostMutationOutcome MapMutationOutcome(CpuBoostMutationOutcome outcome) => outcome switch
    {
        CpuBoostMutationOutcome.Succeeded => FrontendCpuBoostMutationOutcome.Succeeded,
        CpuBoostMutationOutcome.PersistenceFailed => FrontendCpuBoostMutationOutcome.PersistenceFailed,
        _ => FrontendCpuBoostMutationOutcome.ApplyFailed
    };

    private static FrontendCpuBoostSnapshot MapCpuBoostSnapshot(CpuBoostRuntimeSnapshot snapshot) => new(
        MapCpuBoostSide(snapshot.AcCurrent, snapshot.AcDesired),
        MapCpuBoostSide(snapshot.DcCurrent, snapshot.DcDesired),
        snapshot.Enabled,
        snapshot.PersistenceWritable,
        snapshot.LastFailure);

    private static FrontendCpuBoostSideSnapshot MapCpuBoostSide(CpuBoostSideReading current, CpuBoostMode? desired) => new(
        current.Status switch
        {
            CpuBoostReadStatus.Known => FrontendCpuBoostReadStatus.Known,
            CpuBoostReadStatus.Unknown => FrontendCpuBoostReadStatus.Unknown,
            _ => FrontendCpuBoostReadStatus.Unavailable
        },
        current.Mode,
        desired);

}
