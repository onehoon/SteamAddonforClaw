using System.Diagnostics;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.VirtualOutput.Viiper;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

public sealed class MsiClawInputSource : IMsiClawPreparedInputSource, IControllerStateSnapshotSource
{
    private static readonly int M1AuxiliaryIndex = MsiClawControls.Catalog.GetIndex(MsiClawControls.M1);
    private static readonly int M2AuxiliaryIndex = MsiClawControls.Catalog.GetIndex(MsiClawControls.M2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(4);
    private static readonly TimeSpan CadenceDiagnosticPollInterval = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan CadenceDiagnosticDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ProductionCadenceSummaryWindow = TimeSpan.FromSeconds(10);
    private const int MaximumKnownInvalidInitialStates = 16;
    private readonly Func<IDirectInputDeviceEnumerator> _enumeratorFactory;
    private readonly Func<WindowsHighResolutionOneShotTimer> _cadenceDiagnosticTimerFactory;
    private readonly Func<WindowsHighResolutionOneShotTimer> _productionTimerFactory;
    private readonly Func<long> _timestampProvider;
    private readonly Lock _sync = new();
    private InputSession? _currentSession;
    private int _testSession;
    private bool _disposed;

    public MsiClawInputSource(Func<IDirectInputDeviceEnumerator> enumeratorFactory)
        : this(enumeratorFactory, static () => new WindowsHighResolutionOneShotTimer(), static () => new WindowsHighResolutionOneShotTimer())
    {
    }

    internal MsiClawInputSource(
        Func<IDirectInputDeviceEnumerator> enumeratorFactory,
        Func<WindowsHighResolutionOneShotTimer> cadenceDiagnosticTimerFactory,
        Func<WindowsHighResolutionOneShotTimer>? productionTimerFactory = null,
        Func<long>? timestampProvider = null)
    {
        _enumeratorFactory = enumeratorFactory ?? throw new ArgumentNullException(nameof(enumeratorFactory));
        _cadenceDiagnosticTimerFactory = cadenceDiagnosticTimerFactory ?? throw new ArgumentNullException(nameof(cadenceDiagnosticTimerFactory));
        _productionTimerFactory = productionTimerFactory ?? (static () => new WindowsHighResolutionOneShotTimer());
        _timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
    }

    public MsiClawInputSource(IDirectInputDeviceEnumerator enumerator)
        : this(() => enumerator)
    {
    }

    public event EventHandler<ControllerState>? StateChanged;
    // Full1902 Cleanup G: originally a diagnostic name, this is the one live Full1902 owned-input
    // completion signal -- AddonProcessHost subscribes to it to classify actual owned DirectInput
    // session loss and schedule physical recovery. StopReason on the summary carries the cause.
    public event EventHandler<MsiClawInputTestSummary>? TestCompleted;

    private sealed class StateBox(ControllerState value) { internal ControllerState Value { get; } = value; }
    private static ControllerState NeutralState() => new(new AuxiliaryButtonState(Enumerable.Repeat(false, MsiClawControls.Catalog.Count).ToArray()));
    private StateBox _latestState = new(NeutralState());
    public ControllerState LatestState => Volatile.Read(ref _latestState).Value;

    /// <summary>Full1902 Suspend/Resume section 9: overwrite ONLY the published snapshot with neutral.
    /// This is the exact write the poll loop's finally block already performs on stop; it never
    /// touches the DirectInput session, its generation, or the <see cref="StateChanged"/> event. The
    /// next successful poll read writes the current mapped state straight back.</summary>
    public void ResetLatestStateToNeutral() => Volatile.Write(ref _latestState, new StateBox(NeutralState()));

    internal static bool IsM1Pressed(ControllerState state) => state.Auxiliary[M1AuxiliaryIndex];
    internal static bool IsM2Pressed(ControllerState state) => state.Auxiliary[M2AuxiliaryIndex];

    // Empty PointOfViewControllers is a legitimate, if uncommon, DirectInput read shape;
    // -1 matches the mapper's own neutral-POV convention.
    internal static int ResolvePov(DirectInputState input) => input.PointOfViewControllers.Count == 0 ? -1 : input.PointOfViewControllers[0];

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _currentSession is not null && !_currentSession.Cancellation.IsCancellationRequested;
            }
        }
    }

    public MsiClawInputStartResult StartPrepared(DirectInputDeviceDescriptor descriptor)
    {
        lock (_sync)
        {
            if (_disposed) return new(MsiClawInputStartStatus.InitializationFailed, "DirectInput is unavailable because the input source is disposed.");
            if (_currentSession is not null) return new(MsiClawInputStartStatus.AlreadyRunning, "M1/M2 DirectInput test is already running.");
            if (!MsiClawDirectInputDeviceSelector.Select([descriptor]).IsSelected)
                return new(MsiClawInputStartStatus.Indeterminate, "The prepared DirectInput descriptor could not be verified. No changes were made.");

            IDirectInputDeviceEnumerator enumerator;
            try
            {
                enumerator = _enumeratorFactory();
            }
            catch (Exception exception)
            {
                AppLog.Warn("DirectInput", "DirectInput initialization failed.", exception, ("Reason", "DirectInputInitializationFailed"), ("Action", "AbortInput"), ("NoChangesMade", true));
                return new(MsiClawInputStartStatus.InitializationFailed, "DirectInput initialization failed. No controller settings were changed.");
            }

            return StartCoreLocked(enumerator, descriptor, _testSession + 1, "Routing");
        }
    }

    private MsiClawInputStartResult StartCoreLocked(IDirectInputDeviceEnumerator enumerator, DirectInputDeviceDescriptor descriptor, int sessionId, string logCategory)
    {
        IDirectInputDevice? device = null;
        try
        {
            device = enumerator.CreateDevice(descriptor);
        }
        catch (Exception exception)
        {
            AppLog.Warn("DirectInput", "DirectInput device creation failed.", exception, ("Reason", "CreateDeviceFailed"), ("Action", "AbortInput"), ("NoChangesMade", true));
            TryDisposeEnumerator(enumerator, sessionId);
            return new(MsiClawInputStartStatus.CreateDeviceFailed, "DirectInput device creation failed. No controller settings were changed.");
        }

        var session = new InputSession(++_testSession, enumerator, device, new CancellationTokenSource());
        try
        {
            AppLog.Info("DirectInput", "Device acquire started.", ("TestSession", session.Id), ("InstanceGuid", descriptor.InstanceGuid));
            var stopwatch = Stopwatch.StartNew();
            device.Acquire();
            session.AcquiredAt = Stopwatch.GetTimestamp();
            session.AcquireDurationMs = stopwatch.ElapsedMilliseconds;
            AppLog.Info("DirectInput", "Device acquire succeeded.", ("TestSession", session.Id), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
        }
        catch (Exception exception)
        {
            AppLog.Warn("DirectInput", "Device acquire failed.", exception, ("TestSession", session.Id), ("Reason", "AcquireFailed"), ("Action", "AbortInput"), ("NoChangesMade", true));
            CleanupBeforePolling(session);
            return new(MsiClawInputStartStatus.AcquireFailed, "DirectInput device acquisition failed. No controller settings were changed.");
        }

        try
        {
            session.ProductionTimer = _productionTimerFactory();
            session.ProductionPeriodTicks = CanonicalPublisherDeadlineMath.StopwatchTicksFromTimeSpan(PollInterval, Stopwatch.Frequency);
            var origin = _timestampProvider();
            session.NextProductionDeadlineTicks = checked(origin + session.ProductionPeriodTicks);
            ArmForDeadline(session.ProductionTimer, session.NextProductionDeadlineTicks, origin);

            var worker = new Thread(() => PollWorker(session))
            {
                IsBackground = true,
                Name = "SteamInputAddon.PID1902PhysicalInput",
                Priority = ThreadPriority.AboveNormal,
            };
            session.PollingThread = worker;
            _currentSession = session;
            (WorkerThreadStartOverrideForTests ?? (static thread => thread.Start()))(worker);
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_currentSession, session))
                _currentSession = null;
            AppLog.Warn("DirectInput", "Physical input worker initialization failed.", exception,
                ("TestSession", session.Id), ("Reason", "InputWorkerInitializationFailed"), ("Action", "AbortInput"));
            CleanupBeforePolling(session);
            return new(MsiClawInputStartStatus.InitializationFailed, "The physical input polling worker could not be initialized.");
        }

        AppLog.Info(logCategory, "M1/M2 input source started.", ("TestSession", session.Id), ("VID", MsiClawHardware.FormatVendorId()), ("PID", MsiClawHardware.FormatDirectInputProductId()), ("InstanceGuid", descriptor.InstanceGuid));
        return new(MsiClawInputStartStatus.Started, "M1/M2 DirectInput test is running.");
    }

    internal Action<WindowsHighResolutionOneShotTimer, long, long>? ArmForDeadlineOverrideForTests { get; set; }
    internal Action<Thread>? WorkerThreadStartOverrideForTests { get; set; }

    public async Task StopAsync()
    {
        InputSession? session;
        lock (_sync)
        {
            session = _currentSession;
        }

        if (session is null)
        {
            return;
        }

        try
        {
            session.Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        await session.PollingCompletion.Task.ConfigureAwait(false);
    }

    public Task<bool> WaitForFirstValidStateAsync(CancellationToken cancellationToken)
    {
        InputSession? session;
        lock (_sync) session = _currentSession;
        return session is null ? Task.FromResult(false) : session.FirstValidState.Task.WaitAsync(cancellationToken);
    }

    public Task<FrontendPid1902InputCadenceResult> RunPid1902InputCadenceDiagnosticAsync(CancellationToken cancellationToken = default) =>
        RunPid1902InputCadenceDiagnosticAsync(CadenceDiagnosticDuration, cancellationToken);

    internal async Task<FrontendPid1902InputCadenceResult> RunPid1902InputCadenceDiagnosticAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var requestedDurationMs = (long)duration.TotalMilliseconds;
        if (cancellationToken.IsCancellationRequested)
            return new(FrontendPid1902InputCadenceOutcome.Cancelled, "Cancelled.", requestedDurationMs, 0, 0, null, 0, 0, null, null, null, null, null, null, null);

        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));

        InputSession session;
        MsiClawInputCadenceCollector? collector = null;
        Exception? timerCreationFailure = null;
        lock (_sync)
        {
            if (_disposed || _currentSession is null || _currentSession.Cancellation.IsCancellationRequested)
                return new(FrontendPid1902InputCadenceOutcome.Unavailable, "The live PID1902 DirectInput source is unavailable.", requestedDurationMs, 0, 0, null, 0, 0, null, null, null, null, null, null, null);

            session = _currentSession;
            if (session.CadenceDiagnostic is not null)
                return new(FrontendPid1902InputCadenceOutcome.AlreadyRunning, "A PID1902 input cadence diagnostic is already running.", requestedDurationMs, 0, 0, null, 0, 0, null, null, null, null, null, null, null);

            try
            {
                collector = new MsiClawInputCadenceCollector(duration, timer: _cadenceDiagnosticTimerFactory());
                session.CadenceDiagnostic = collector;
            }
            catch (Exception exception)
            {
                timerCreationFailure = exception;
            }
        }

        if (collector is null)
        {
            AppLog.Warn("Diagnostics", "PID1902 input cadence diagnostic could not create its high-resolution timer.", timerCreationFailure,
                ("Reason", "CadenceTimerCreateFailed"),
                ("Action", "FailDiagnostic"));
            return EmptyCadenceResult(FrontendPid1902InputCadenceOutcome.Failed,
                "The PID1902 cadence diagnostic could not create its high-resolution timer.", requestedDurationMs);
        }

        AppLog.Info("Diagnostics", "PID1902 input cadence diagnostic started.",
            ("DurationMs", requestedDurationMs),
            ("DiagnosticPollIntervalMs", (long)CadenceDiagnosticPollInterval.TotalMilliseconds),
            ("ProductionPollIntervalMs", (long)PollInterval.TotalMilliseconds));

        using var cancellationRegistration = cancellationToken.Register(static state =>
        {
            var value = ((MsiClawInputCadenceCollector Collector, string Status))state!;
            value.Collector.Cancel(value.Status);
        }, (collector, "The PID1902 input cadence diagnostic was cancelled."));

        FrontendPid1902InputCadenceResult result;
        try
        {
            result = await collector.Completion.ConfigureAwait(false);
        }
        finally
        {
            ClearCadenceDiagnostic(session, collector);
            collector.Dispose();
        }
        AppLog.Info("Diagnostics", "PID1902 input cadence diagnostic completed.",
            ("Outcome", result.Outcome),
            ("ActualDurationMs", result.ActualDurationMs),
            ("SuccessfulReadCount", result.SuccessfulReadCount),
            ("ObservedReadHz", result.ObservedReadHz),
            ("DistinctStateCount", result.DistinctStateCount),
            ("DistinctStateHz", result.DistinctStateHz),
            ("DuplicatePercent", result.DuplicatePercent),
            ("MedianDistinctIntervalMs", result.MedianDistinctIntervalMs),
            ("P95DistinctIntervalMs", result.P95DistinctIntervalMs));
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _disposed = true;
        }

        await StopAsync().ConfigureAwait(false);
    }

    private void PollWorker(InputSession session)
    {
        var stopwatch = Stopwatch.StartNew();
        var previous = NeutralState();
        var hasPrevious = false;
        var m1Observed = false;
        var m2Observed = false;
        var m1OnlyObserved = false;
        var m2OnlyObserved = false;
        var independent = false;
        var readFailures = 0;
        var cleanupSucceeded = true;
        var stopReason = MsiClawInputStopReason.Stopped;
        var firstReadLogged = false;
        var invalidInitialStateCount = 0;
        var nextProductionDeadlineTicks = session.NextProductionDeadlineTicks;
        var firstProductionWait = true;
        var diagnosticModeActive = false;
        var productionCadenceStartedAt = Stopwatch.GetTimestamp();
        var productionCadenceReadCount = 0;
        var productionCadenceDiagnosticObserved = false;
        var productionCadenceSummaryLogged = false;

        try
        {
            while (!session.Cancellation.IsCancellationRequested)
            {
                bool schedulerFailed;
                DirectInputState input;
                try
                {
                    input = session.Device.ReadState();
                }
                catch (Exception exception)
                {
                    readFailures++;
                    stopReason = MsiClawInputStopReason.ReadStateFailed;
                    AppLog.Warn("DirectInput", "Controller state read failed.", exception, ("TestSession", session.Id), ("Attempt", readFailures), ("Reason", "ReadStateFailed"), ("Action", "StopDiagnostic"));
                    AppLog.Debug("RoutingTrace", "Physical input read failed.", ("Event", "PhysicalInputReadFailed"), ("RoutingExecution", (object?)RoutingTraceContext.Current), ("TestSession", session.Id), ("SessionAgeMs", Elapsed(session.StartedAt)), ("LastSuccessfulReadAgeMs", session.LastSuccessfulReadAt is { } last ? Elapsed(last) : -1), ("SuccessfulReadCount", session.SuccessfulReadCount), ("ReadFailures", readFailures), ("ExceptionType", exception.GetType().Name));
                    break;
                }

                if (input.Buttons.Count < MsiClawHardware.RequiredDirectInputButtonCount)
                {
                    stopReason = MsiClawInputStopReason.InvalidButtonLayout;
                    AppLog.Warn("MsiInput", "DirectInput state layout is invalid.", null, ("TestSession", session.Id), ("ButtonCount", input.Buttons.Count), ("RequiredButtonCount", MsiClawHardware.RequiredDirectInputButtonCount), ("Action", "StopDiagnostic"), ("Reason", "InsufficientButtonCount"));
                    break;
                }

                if (!firstReadLogged && MsiClawControllerStateMapper.IsKnownInvalidInitialState(input))
                {
                    invalidInitialStateCount++;
                    if (invalidInitialStateCount <= MaximumKnownInvalidInitialStates)
                    {
                        if (invalidInitialStateCount == 1)
                            AppLog.Debug("MsiInput", "Known invalid DirectInput initial state was ignored.", ("TestSession", session.Id), ("ButtonCount", input.Buttons.Count), ("MaximumInitialStates", MaximumKnownInvalidInitialStates), ("Action", "AwaitNextState"), ("Reason", "KnownInvalidInitialState"));
                        if (!WaitForNextPoll(session, GetCadenceDiagnostic(session), ref nextProductionDeadlineTicks, ref firstProductionWait, ref diagnosticModeActive, out schedulerFailed))
                        {
                            if (schedulerFailed) stopReason = MsiClawInputStopReason.PollSchedulerFailed;
                            break;
                        }
                        continue;
                    }

                    stopReason = MsiClawInputStopReason.InitialStateNotReady;
                    AppLog.Warn("MsiInput", "Known invalid DirectInput initial state persisted beyond the startup allowance.", null, ("TestSession", session.Id), ("ButtonCount", input.Buttons.Count), ("MaximumInitialStates", MaximumKnownInvalidInitialStates), ("Action", "StopDiagnostic"), ("Reason", "InitialStateNotReady"));
                    break;
                }

                var cadenceDiagnostic = GetCadenceDiagnostic(session);
                var cadenceDiagnosticActive = cadenceDiagnostic?.IsActive == true;
                cadenceDiagnostic?.Observe(input, Stopwatch.GetTimestamp());

                if (!TryMapState(input, out var current))
                {
                    stopReason = MsiClawInputStopReason.InvalidButtonLayout;
                    AppLog.Warn("MsiInput", "DirectInput state layout is invalid.", null, ("TestSession", session.Id), ("ButtonCount", input.Buttons.Count), ("RequiredButtonCount", MsiClawHardware.RequiredDirectInputButtonCount), ("Action", "StopDiagnostic"), ("Reason", "KnownInvalidState"));
                    break;
                }

                // Gated per-poll: LogPovIfChanged itself takes a lock and compares state even when the
                // eventual AppLog.Debug call would be filtered, so check the level here rather than
                // relying only on AppLog's own internal filter.
                if (!cadenceDiagnosticActive && AppLog.IsEnabled(AppLogLevel.Debug)) ControllerStateDiagnostics.LogPovIfChanged(session.Id, ResolvePov(input));

                var successfulReadAt = Stopwatch.GetTimestamp();
                Volatile.Write(ref _latestState, new StateBox(current));
                session.SuccessfulReadCount++;
                session.LastSuccessfulReadAt = successfulReadAt;
                if (cadenceDiagnosticActive)
                    productionCadenceDiagnosticObserved = true;
                else
                    productionCadenceReadCount++;

                if (!productionCadenceSummaryLogged && Stopwatch.GetElapsedTime(productionCadenceStartedAt, successfulReadAt) >= ProductionCadenceSummaryWindow)
                {
                    productionCadenceSummaryLogged = true;
                    if (!productionCadenceDiagnosticObserved)
                    {
                        var elapsedSeconds = Stopwatch.GetElapsedTime(productionCadenceStartedAt, successfulReadAt).TotalSeconds;
                        AppLog.Debug("DirectInput", "PID1902 production input cadence summary.",
                            ("TestSession", session.Id),
                            ("WindowMs", (long)(elapsedSeconds * 1000)),
                            ("SuccessfulReadCount", productionCadenceReadCount),
                            ("ObservedReadHz", elapsedSeconds > 0 ? Math.Round(productionCadenceReadCount / elapsedSeconds, 1) : 0),
                            ("TargetPeriodMs", (long)PollInterval.TotalMilliseconds));
                    }
                }

                if (!firstReadLogged)
                {
                    firstReadLogged = true;
                    session.FirstValidState.TrySetResult(true);
                    AppLog.Debug("RoutingTrace", "Physical input first read succeeded.", ("Event", "PhysicalInputFirstRead"), ("RoutingExecution", (object?)RoutingTraceContext.Current), ("TestSession", session.Id), ("AcquireElapsedMs", session.AcquireDurationMs), ("FirstReadAfterAcquireMs", ElapsedBetween(session.AcquiredAt, successfulReadAt)), ("SessionAgeMs", Elapsed(session.StartedAt)));
                }

                if (!hasPrevious)
                {
                    if (!cadenceDiagnosticActive && AppLog.IsEnabled(AppLogLevel.Debug))
                        AppLog.Debug("MsiInput", "Initial ControllerState.", ("TestSession", session.Id), ("M1", IsM1Pressed(current)), ("M2", IsM2Pressed(current)));
                    // M5: record the first observed physical D-pad state too, not just later
                    // transitions, so it lines up with the canonical publisher's own first-tick log.
                    if (AppLog.IsEnabled(AppLogLevel.Info))
                        ControllerStateDiagnostics.LogDPadTransitionIfChanged(current.Buttons, session.Id);
                    StateChanged?.Invoke(this, current);
                    previous = current;
                    hasPrevious = true;
                }
                else if (current != previous)
                {
                    if (AppLog.IsEnabled(AppLogLevel.Debug))
                    {
                        LogStateChange(session.Id, previous, current);
                        ControllerStateDiagnostics.LogChanges(previous, current, session.Id);
                    }
                    if (AppLog.IsEnabled(AppLogLevel.Info))
                        ControllerStateDiagnostics.LogDPadTransitionIfChanged(current.Buttons, session.Id);
                    StateChanged?.Invoke(this, current);
                    previous = current;
                }

                if (IsM1Pressed(current) && !m1Observed)
                {
                    m1Observed = true;
                    AppLog.Info("Diagnostics", "M1 input verified.", ("TestSession", session.Id), ("ButtonIndex", MsiClawHardware.M1DirectInputButtonIndex));
                }

                if (IsM2Pressed(current) && !m2Observed)
                {
                    m2Observed = true;
                    AppLog.Info("Diagnostics", "M2 input verified.", ("TestSession", session.Id), ("ButtonIndex", MsiClawHardware.M2DirectInputButtonIndex));
                }

                if (IsM1Pressed(current) && !IsM2Pressed(current)) m1OnlyObserved = true;
                if (!IsM1Pressed(current) && IsM2Pressed(current)) m2OnlyObserved = true;
                if (!independent && m1OnlyObserved && m2OnlyObserved)
                {
                    independent = true;
                    AppLog.Info("Diagnostics", "Independent M1/M2 input verified.", ("TestSession", session.Id), ("M1OnlyObserved", true), ("M2OnlyObserved", true), ("M1ButtonIndex", MsiClawHardware.M1DirectInputButtonIndex), ("M2ButtonIndex", MsiClawHardware.M2DirectInputButtonIndex));
                }

                if (!WaitForNextPoll(session, GetCadenceDiagnostic(session), ref nextProductionDeadlineTicks, ref firstProductionWait, ref diagnosticModeActive, out schedulerFailed))
                {
                    if (schedulerFailed) stopReason = MsiClawInputStopReason.PollSchedulerFailed;
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            stopReason = MsiClawInputStopReason.PollWorkerFailed;
            AppLog.Error("DirectInput", "Physical input worker failed unexpectedly.", exception,
                ("TestSession", session.Id), ("Reason", "PollWorkerFailed"));
        }
        finally
        {
            var cadenceDiagnostic = GetCadenceDiagnostic(session);
            if (cadenceDiagnostic is not null)
            {
                if (session.Cancellation.IsCancellationRequested)
                    cadenceDiagnostic.Cancel("The PID1902 input cadence diagnostic was cancelled with the input session.");
                else
                    cadenceDiagnostic.Fail($"The PID1902 DirectInput session stopped: {stopReason}.");
            }
            session.FirstValidState.TrySetResult(false);
            Volatile.Write(ref _latestState, new StateBox(NeutralState()));
            cleanupSucceeded = CleanupSession(session);
            var summary = new MsiClawInputTestSummary(session.Id, stopwatch.ElapsedMilliseconds, m1Observed, m2Observed, independent, readFailures, cleanupSucceeded, stopReason);
            lock (_sync)
            {
                if (ReferenceEquals(_currentSession, session))
                {
                    _currentSession = null;
                }
            }
            session.Cancellation.Dispose();
            try
            {
                AppLog.Info("Diagnostics", "M1/M2 input diagnostic completed.", ("TestSession", summary.TestSession), ("DurationMs", summary.DurationMs), ("M1Observed", summary.M1Observed), ("M2Observed", summary.M2Observed), ("Independent", summary.Independent), ("ReadFailures", summary.ReadFailures), ("CleanupSucceeded", summary.CleanupSucceeded), ("StopReason", summary.StopReason));
                TestCompleted?.Invoke(this, summary);
            }
            catch (Exception exception)
            {
                AppLog.Error("DirectInput", "Physical input completion callback failed.", exception,
                    ("TestSession", session.Id), ("StopReason", summary.StopReason));
            }
            finally
            {
                session.PollingCompletion.TrySetResult(true);
            }
        }
    }

    private static bool TryMapState(DirectInputState input, out ControllerState state)
    {
        if (input.Buttons.Count < MsiClawHardware.RequiredDirectInputButtonCount)
        {
            state = default;
            return false;
        }

        return MsiClawControllerStateMapper.TryMap(input, out state);
    }

    private static bool CleanupSession(InputSession session)
    {
        var cleanupSucceeded = true;
        if (session.ProductionTimer is { } timer)
        {
            try
            {
                timer.Dispose();
                session.ProductionTimer = null;
            }
            catch (Exception exception)
            {
                cleanupSucceeded = false;
                AppLog.Error("DirectInput", "Physical polling timer cleanup failed.", exception,
                    ("TestSession", session.Id), ("Operation", "TimerDispose"));
            }
        }

        try
        {
            AppLog.Info("DirectInput", "Device unacquire started.", ("TestSession", session.Id));
            session.Device.Unacquire();
            AppLog.Info("DirectInput", "Device unacquire completed.", ("TestSession", session.Id), ("Success", true));
        }
        catch (Exception exception)
        {
            cleanupSucceeded = false;
            AppLog.Error("DirectInput", "Device cleanup failed.", exception, ("TestSession", session.Id), ("Operation", "Unacquire"));
        }

        try
        {
            session.Device.Dispose();
            AppLog.Info("DirectInput", "Device disposed.", ("TestSession", session.Id));
        }
        catch (Exception exception)
        {
            cleanupSucceeded = false;
            AppLog.Error("DirectInput", "Device cleanup failed.", exception, ("TestSession", session.Id), ("Operation", "Dispose"));
        }

        cleanupSucceeded &= TryDisposeEnumerator(session.Enumerator, session.Id);

        return cleanupSucceeded;
    }

    private static void CleanupBeforePolling(InputSession session)
    {
        CleanupSession(session);
        session.Cancellation.Dispose();
    }

    private static bool TryDisposeEnumerator(IDirectInputDeviceEnumerator enumerator, int testSession)
    {
        try
        {
            enumerator.Dispose();
            AppLog.Info("DirectInput", "DirectInput enumerator disposed.", ("TestSession", testSession));
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error("DirectInput", "DirectInput enumerator cleanup failed.", exception, ("TestSession", testSession), ("Operation", "EnumeratorDispose"));
            return false;
        }
    }

    internal static void LogStateChange(int session, ControllerState previous, ControllerState current)
    {
        if (IsM1Pressed(previous) != IsM1Pressed(current))
        {
            AppLog.Debug("MsiInput", "M1 state changed.", ("TestSession", session), ("ButtonIndex", MsiClawHardware.M1DirectInputButtonIndex), ("Previous", IsM1Pressed(previous)), ("Current", IsM1Pressed(current)));
        }
        if (IsM2Pressed(previous) != IsM2Pressed(current))
        {
            AppLog.Debug("MsiInput", "M2 state changed.", ("TestSession", session), ("ButtonIndex", MsiClawHardware.M2DirectInputButtonIndex), ("Previous", IsM2Pressed(previous)), ("Current", IsM2Pressed(current)));
        }
        // Full1902 0903 cleanup (section 6): the former generic "ControllerState changed." line only
        // printed M1/M2 old/new, so any B / D-pad / stick / trigger change produced a misleading
        // "M1=False->False M2=False->False" entry (~200 per session). The dedicated M1/M2 logs above
        // carry the MSI-specific evidence; actual ControllerState field changes are the Input
        // category's job (Diagnostics/DiagnosticSession).
    }

    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    private static long ElapsedBetween(long started, long ended) => (long)Stopwatch.GetElapsedTime(started, ended).TotalMilliseconds;

    internal static TimeSpan ResolvePollInterval(bool cadenceDiagnosticActive) =>
        cadenceDiagnosticActive ? CadenceDiagnosticPollInterval : PollInterval;

    private bool WaitForNextPoll(
        InputSession session,
        MsiClawInputCadenceCollector? cadenceDiagnostic,
        ref long nextProductionDeadlineTicks,
        ref bool firstProductionWait,
        ref bool diagnosticModeActive,
        out bool schedulerFailed)
    {
        schedulerFailed = false;
        if (cadenceDiagnostic?.IsActive == true)
        {
            diagnosticModeActive = true;
            try
            {
                if (cadenceDiagnostic.WaitForNextSample(CadenceDiagnosticPollInterval, session.Cancellation.Token))
                    return !session.Cancellation.IsCancellationRequested;
            }
            catch (Exception exception)
            {
                cadenceDiagnostic.Fail("The PID1902 cadence diagnostic high-resolution timer failed.");
                AppLog.Warn("Diagnostics", "PID1902 input cadence diagnostic high-resolution timer failed; normal polling will continue.", exception,
                    ("Reason", "CadenceTimerArmFailed"),
                    ("Action", "FailDiagnostic"),
                    ("TestSession", session.Id));
            }
        }

        if (session.Cancellation.IsCancellationRequested)
            return false;

        try
        {
            var now = _timestampProvider();
            if (diagnosticModeActive)
            {
                diagnosticModeActive = false;
                nextProductionDeadlineTicks = checked(now + session.ProductionPeriodTicks);
                ArmForDeadline(session.ProductionTimer!, nextProductionDeadlineTicks, now);
            }
            else if (firstProductionWait)
            {
                // The initial production deadline was armed synchronously before the worker started.
                firstProductionWait = false;
            }
            else
            {
                var advance = CanonicalPublisherDeadlineMath.AdvanceDeadline(
                    nextProductionDeadlineTicks,
                    session.ProductionPeriodTicks,
                    now);
                nextProductionDeadlineTicks = advance.NextDeadlineTicks;
                ArmForDeadline(session.ProductionTimer!, nextProductionDeadlineTicks, now);
            }

            var signaled = WaitHandle.WaitAny([session.Cancellation.Token.WaitHandle, session.ProductionTimer!]);
            return signaled == 1 && !session.Cancellation.IsCancellationRequested;
        }
        catch (Exception exception)
        {
            schedulerFailed = true;
            AppLog.Error("DirectInput", "PID1902 production polling scheduler failed.", exception,
                ("TestSession", session.Id), ("Reason", "PollSchedulerFailed"), ("Action", "StopPhysicalInput"));
            return false;
        }
    }

    private void ArmForDeadline(WindowsHighResolutionOneShotTimer timer, long deadlineTicks, long nowTicks)
    {
        var arm = ArmForDeadlineOverrideForTests;
        if (arm is not null)
        {
            arm(timer, deadlineTicks, nowTicks);
            return;
        }

        var remainingTicks = deadlineTicks - nowTicks;
        var due100ns = CanonicalPublisherDeadlineMath.ConvertToRelativeDueTime100ns(remainingTicks, Stopwatch.Frequency);
        timer.ArmRelative(TimeSpan.FromTicks(due100ns));
    }

    private static FrontendPid1902InputCadenceResult EmptyCadenceResult(
        FrontendPid1902InputCadenceOutcome outcome,
        string status,
        long requestedDurationMs) =>
        new(outcome, status, requestedDurationMs, 0, 0, null, 0, 0, null, null, null, null, null, null, null);

    private MsiClawInputCadenceCollector? GetCadenceDiagnostic(InputSession session)
    {
        lock (_sync)
            return ReferenceEquals(_currentSession, session) ? session.CadenceDiagnostic : null;
    }

    private void ClearCadenceDiagnostic(InputSession session, MsiClawInputCadenceCollector collector)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_currentSession, session) && ReferenceEquals(session.CadenceDiagnostic, collector))
                session.CadenceDiagnostic = null;
        }
    }

    private sealed class InputSession(int id, IDirectInputDeviceEnumerator enumerator, IDirectInputDevice device, CancellationTokenSource cancellation)
    {
        public int Id { get; } = id;
        public IDirectInputDeviceEnumerator Enumerator { get; } = enumerator;
        public IDirectInputDevice Device { get; } = device;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Thread? PollingThread { get; set; }
        public TaskCompletionSource<bool> PollingCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public WindowsHighResolutionOneShotTimer? ProductionTimer { get; set; }
        public long ProductionPeriodTicks { get; set; }
        public long NextProductionDeadlineTicks { get; set; }
        public long StartedAt { get; } = Stopwatch.GetTimestamp();
        public long AcquiredAt { get; set; }
        public long AcquireDurationMs { get; set; }
        public long? LastSuccessfulReadAt { get; set; }
        public int SuccessfulReadCount { get; set; }
        public MsiClawInputCadenceCollector? CadenceDiagnostic { get; set; }
        public TaskCompletionSource<bool> FirstValidState { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

}
