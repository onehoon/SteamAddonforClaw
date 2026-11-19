using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Input;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// Publishes <see cref="IControllerStateSnapshotSource.LatestState"/>, mapped through
/// <see cref="SteamDeckDeviceStateMapper"/>, to the canonical VIIPER Steam Deck sink on a monotonic
/// absolute ~250 Hz (4 ms) deadline schedule, driven by a dedicated worker thread waiting on
/// <see cref="WindowsHighResolutionOneShotTimer"/> and re-armed via
/// <see cref="CanonicalPublisherDeadlineMath"/>.
/// </summary>
/// <remarks>
/// <para>
/// This scheduling design is the result of real MSI Claw hardware testing. An earlier
/// <c>Task.Delay</c>-based tick source only achieved ~83 Hz against the 250 Hz target. A later
/// periodic high-resolution timer (re-arming itself automatically every 4 ms via <c>lPeriod</c>)
/// stabilized at ~230.8 Hz instead: average wake-to-wake interval ~4.3324 ms, average wait-blocked
/// time ~4.2885 ms, average publish work only ~0.04345 ms (so <c>SetState</c> itself was not the
/// bottleneck), and 64.9% of wakes exceeded 4.25 ms -- the wait/wakeup boundary itself was
/// consistently a few tenths of a millisecond late.
/// </para>
/// <para>
/// This publisher instead tracks a monotonic absolute logical deadline (in
/// <see cref="System.Diagnostics.Stopwatch"/> ticks) and, after each publish, re-arms the one-shot
/// timer for only the time remaining until the *next* logical deadline -- not for a full 4 ms
/// again -- so a late wake shortens the following wait instead of shifting the whole schedule
/// forward. <see cref="IInputReportTickSource"/> remains a deterministic async test seam; production
/// always uses the dedicated-thread/one-shot-timer path below, never the tick-source path.
/// </para>
/// <para>
/// Real CPU-heavy testing showed scheduler starvation could still occur even with the deadline
/// scheduler (sustained windows as low as ~60-150 Hz under saturation), which motivated running the
/// worker thread at <see cref="ThreadPriority.AboveNormal"/> -- this improves scheduling opportunity
/// but does not guarantee 250 Hz under saturation. Shutdown must join the worker thread before native
/// device teardown so an in-flight <c>SetState</c> call cannot race the native handle being removed;
/// a join timeout fails closed (throws) rather than pretending the worker stopped.
/// </para>
/// </remarks>
internal sealed class CanonicalSteamDeckInputPublisher
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WakeWarnThreshold = TimeSpan.FromMilliseconds(4.25);
    private static readonly TimeSpan WakeAlarmThreshold = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan ProductionPeriod = TimeSpan.FromMilliseconds(4);
    private static readonly TimeSpan DefaultWorkerJoinTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OverlayReturnSampleInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan OverlayReturnMaximumDuration = TimeSpan.FromSeconds(60);
    private const int DiagnosticStickActivityThreshold = 4096;
    private const ushort DiagnosticTriggerActivityThreshold = 1024;
    private const int DiagnosticTriggerMovementThreshold = 2048;
    private static readonly (ulong Mask, string Name)[] OverlayReturnButtonNames =
    [
        (1UL << 0, "A"), (1UL << 1, "B"), (1UL << 2, "X"), (1UL << 3, "Y"),
        (1UL << 4, "DPadUp"), (1UL << 5, "DPadRight"), (1UL << 6, "DPadDown"), (1UL << 7, "DPadLeft"),
        (1UL << 8, "LB"), (1UL << 9, "RB"), (1UL << 10, "View"), (1UL << 11, "Menu"),
        (1UL << 12, "L3"), (1UL << 13, "R3"), (1UL << 14, "LTFull"), (1UL << 15, "RTFull"),
        (1UL << 16, "M2"), (1UL << 17, "M1"),
    ];

    /// <summary>Test-only seam so the join-timeout fail-closed path can be exercised deterministically.</summary>
    internal TimeSpan WorkerJoinTimeoutForTests { get; set; } = DefaultWorkerJoinTimeout;

    private readonly IControllerStateSnapshotSource _snapshot;
    private readonly ICanonicalSteamDeckStateSink _sink;
    private readonly IInputReportTickSource? _ticks;
    private readonly Action<Exception>? _fault;
    private readonly Func<long> _timestampProvider;
    private readonly Func<ControllerState, AuxiliaryButtonSlot, bool> _rearButtonSuppressionProvider;
    private readonly Func<MsiClawMotionState>? _motionSnapshotProvider;
    private readonly Func<SteamDeckRightPadContact>? _rightPadProvider;
    private readonly SteamDeckSystemButtonOverlay _systemButtonOverlay;
    private CancellationTokenSource? _stop;
    private Task? _task;
    private int _publishedStateCount;
    private int _faultReported;
    private long _publisherStartedTimestamp;
    private bool _firstOrdinaryInputPublished;
    private bool _overlayReturnDiagnosticsArmed;
    private bool _overlayReturnTraceActive;
    private long _overlayReturnTraceStartTimestamp;
    private long _overlayReturnLastSampleTimestamp;
    private int _overlayReturnSampleCount;
    private bool _overlayReturnFirstStickMovementLogged;
    private bool _overlayReturnFirstButtonPressLogged;
    private bool _overlayReturnHasButtonBaseline;
    private ulong _overlayReturnPreviousButtonMask;

    // Production path only: dedicated worker thread driven by a real Windows high-resolution
    // one-shot timer, re-armed each iteration against the deadline schedule below. Never used when
    // a test supplies its own IInputReportTickSource.
    private WindowsHighResolutionOneShotTimer? _timer;
    private ManualResetEvent? _workerStopEvent;
    private Thread? _workerThread;

    private long _periodTicks;
    private long _nextDeadlineTicks;

    private long _lastHeartbeatTimestamp;
    private int _setStateCallsSinceHeartbeat;
    private long _maxSetStateTicksSinceHeartbeat;
    private long _totalSetStateFailures;

    // Timing-decomposition diagnostics: production-worker-only (see the class remarks above for the
    // real-hardware rationale). Touched only from the single dedicated worker thread (WorkerLoop
    // below), so no synchronization is needed. These stay at their zero defaults on the test/manual-tick
    // IInputReportTickSource path, which never populates them.
    private long _previousTimerWakeTimestamp;
    private bool _hasPreviousTimerWake;
    private int _timerWakeCountSinceHeartbeat;
    private int _wakeToWakeSampleCountSinceHeartbeat;
    private long _wakeToWakeTicksSumSinceHeartbeat;
    private long _maxWakeToWakeTicksSinceHeartbeat;
    private int _wakeOver425MsCountSinceHeartbeat;
    private int _wakeOver5MsCountSinceHeartbeat;
    private long _waitBlockedTicksSumSinceHeartbeat;
    private long _maxWaitBlockedTicksSinceHeartbeat;
    private long _publishWorkTicksSumSinceHeartbeat;
    private long _maxPublishWorkTicksSinceHeartbeat;
    private long _wakeLatenessTicksSumSinceHeartbeat;
    private long _maxWakeLatenessTicksSinceHeartbeat;
    private long _skippedDeadlineCountSinceHeartbeat;

    // Motion summaries piggyback the existing publisher operation and are touched only by its tick thread.
    private bool _motionImuDiagnosticsActive;
    private long _motionImuDiagnosticsWindowStart;
    private long _motionImuPublishTickCount;
    private long _motionImuUsableTickCount;
    private long _motionImuUnavailableTickCount;
    private long _motionGyroUnavailableTickCount;
    private long _motionAccelUnavailableTickCount;
    private long _motionGyroUniqueReceiveTickCount;
    private long _motionGyroRepeatedReceiveTickCount;
    private long _motionPreviousGyroReceiveTicks;
    private bool _hasTriggerDiagnosticSnapshot;
    private ushort _lastLoggedLTrigger;
    private ushort _lastLoggedRTrigger;
    private byte _lastLoggedL2Digital;
    private byte _lastLoggedR2Digital;

    internal CanonicalSteamDeckInputPublisher(
        IControllerStateSnapshotSource snapshot,
        ICanonicalSteamDeckStateSink sink,
        IInputReportTickSource? ticks = null,
        Action<Exception>? fault = null,
        Func<long>? timestampProvider = null,
        SteamDeckSystemButtonOverlay? systemButtonOverlay = null,
        Func<ControllerState, AuxiliaryButtonSlot, bool>? rearButtonSuppressionProvider = null,
        Func<MsiClawMotionState>? motionSnapshotProvider = null,
        Func<SteamDeckRightPadContact>? rightPadProvider = null)
    {
        _snapshot = snapshot;
        _sink = sink;
        _ticks = ticks;
        _fault = fault;
        _timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
        _systemButtonOverlay = systemButtonOverlay ?? new SteamDeckSystemButtonOverlay(timestampProvider: _timestampProvider);
        _rearButtonSuppressionProvider = rearButtonSuppressionProvider ?? (static (_, _) => false);
        _motionSnapshotProvider = motionSnapshotProvider;
        _rightPadProvider = rightPadProvider;
    }

    internal bool IsRunning => _task is { IsCompleted: false } || _workerThread is { IsAlive: true };
    internal int PublishedStateCount => _publishedStateCount;

    /// <summary>Arms one Debug-only trace for the next publisher start after a successful Overlay return.</summary>
    internal void ArmOverlayReturnDiagnostics()
    {
        _overlayReturnDiagnosticsArmed = AppLog.IsEnabled(AppLogLevel.Debug);
    }

    /// <summary>
    /// Starts publishing. On the production (no explicit tick source) path this throws synchronously
    /// and cleans up any partially-created native handle if the timer cannot be created or armed --
    /// fail closed rather than silently falling back to a defective cadence.
    /// </summary>
    internal void Start()
    {
        if (IsRunning) throw new InvalidOperationException("The canonical Steam Deck publisher is already running.");
        _publisherStartedTimestamp = _timestampProvider();
        _lastHeartbeatTimestamp = _publisherStartedTimestamp;
        _firstOrdinaryInputPublished = false;
        ResetTriggerDiagnosticSnapshot();
        ResetTimingDiagnosticsForNewRun();
        var traceOverlayReturn = _overlayReturnDiagnosticsArmed && AppLog.IsEnabled(AppLogLevel.Debug);
        _overlayReturnDiagnosticsArmed = false;
        ResetOverlayReturnDiagnostics();
        if (traceOverlayReturn)
            StartOverlayReturnDiagnostics(_publisherStartedTimestamp);

        if (_ticks is not null)
        {
            _stop = new CancellationTokenSource();
            _task = PublishAsync(_ticks, _stop.Token);
            return;
        }

        try
        {
            StartProductionWorker();
        }
        catch
        {
            CompleteOverlayReturnDiagnostics("PublisherStartFailed");
            throw;
        }
    }

    internal async Task StopAsync()
    {
        if (_task is not null)
        {
            if (_stop is null) return;
            _stop.Cancel();
            await _task.ConfigureAwait(false);
            _stop.Dispose();
            _stop = null;
            _task = null;
            CompleteOverlayReturnDiagnostics("PublisherStopped");
            return;
        }

        await StopProductionWorkerAsync().ConfigureAwait(false);
        CompleteOverlayReturnDiagnostics("PublisherStopped");
    }

    private void StartProductionWorker()
    {
        WindowsHighResolutionOneShotTimer timer;
        try
        {
            timer = new WindowsHighResolutionOneShotTimer();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("Failed to create the canonical Steam Deck publisher's high-resolution timer.", exception);
        }

        _periodTicks = CanonicalPublisherDeadlineMath.StopwatchTicksFromTimeSpan(ProductionPeriod, Stopwatch.Frequency);
        var origin = _timestampProvider();
        _nextDeadlineTicks = origin + _periodTicks;

        try
        {
            ArmForDeadlineViaSeam(timer, _nextDeadlineTicks, origin);
        }
        catch (Exception exception)
        {
            timer.Dispose();
            throw new InvalidOperationException("Failed to arm the canonical Steam Deck publisher's high-resolution timer.", exception);
        }

        var stopEvent = new ManualResetEvent(false);
        Thread thread;
        try
        {
            thread = new Thread(() => WorkerLoop(timer, stopEvent))
            {
                IsBackground = true,
                Name = "SteamInputAddon.SteamDeckPublisher",
                Priority = ThreadPriority.AboveNormal,
            };
            (WorkerThreadStartOverrideForTests ?? (static t => t.Start()))(thread);
        }
        catch (Exception exception)
        {
            timer.Dispose();
            stopEvent.Dispose();
            throw new InvalidOperationException("Failed to start the canonical Steam Deck publisher worker thread.", exception);
        }

        _timer = timer;
        _workerStopEvent = stopEvent;
        _workerThread = thread;
    }

    private static void ArmForDeadline(WindowsHighResolutionOneShotTimer timer, long deadlineTicks, long nowTicks)
    {
        var remainingTicks = deadlineTicks - nowTicks;
        var due100ns = CanonicalPublisherDeadlineMath.ConvertToRelativeDueTime100ns(remainingTicks, Stopwatch.Frequency);
        timer.ArmRelative(TimeSpan.FromTicks(due100ns));
    }

    /// <summary>Test-only seam for deterministic initial-arm and runtime re-arm failure coverage.</summary>
    internal Action<WindowsHighResolutionOneShotTimer, long, long>? ArmForDeadlineOverrideForTests { get; set; }

    private void ArmForDeadlineViaSeam(WindowsHighResolutionOneShotTimer timer, long deadlineTicks, long nowTicks) =>
        (ArmForDeadlineOverrideForTests ?? ArmForDeadline)(timer, deadlineTicks, nowTicks);

    /// <summary>Test-only seam for deterministic worker-thread-start failure and configuration coverage.</summary>
    internal Action<Thread>? WorkerThreadStartOverrideForTests { get; set; }

    /// <summary>
    /// Signals the worker to stop and waits for it to actually exit. The caller proceeds from a
    /// successful <see cref="StopAsync"/> straight into native Steam Deck device removal, so the join
    /// must complete before that removal so an in-flight <c>SetState</c> call cannot race the native
    /// handle being torn down. Fail closed on a join timeout -- a timed-out join must not silently
    /// allow that race, so it throws instead of returning as if the worker had stopped.
    /// </summary>
    private async Task StopProductionWorkerAsync()
    {
        if (_workerStopEvent is null) return;
        var stopEvent = _workerStopEvent;
        var thread = _workerThread;
        var timer = _timer;

        stopEvent.Set();
        if (thread is not null)
        {
            var joined = await Task.Run(() => thread.Join(WorkerJoinTimeoutForTests)).ConfigureAwait(false);
            if (!joined)
            {
                var timeout = new TimeoutException("The canonical Steam Deck publisher worker did not stop within the shutdown timeout; it may still be inside SetState. Refusing to proceed with teardown.");
                AppLog.Error("SteamOutput", "Canonical Steam Deck publisher worker did not stop within the shutdown timeout.", timeout, ("TimeoutMs", (long)WorkerJoinTimeoutForTests.TotalMilliseconds));
                throw timeout;
            }
        }

        timer?.Dispose();
        stopEvent.Dispose();
        _timer = null;
        _workerStopEvent = null;
        _workerThread = null;
    }

    private void WorkerLoop(WindowsHighResolutionOneShotTimer timer, ManualResetEvent stopEvent)
    {
        try
        {
            PublisherThreadQoS.ApplyHighQoS("SteamDeck");
            WaitHandle[] handles = [stopEvent, timer];
            while (true)
            {
                var diagnosticsEnabled = AppLog.IsEnabled(AppLogLevel.Debug);
                var waitStart = diagnosticsEnabled ? _timestampProvider() : 0;

                var signaled = WaitHandle.WaitAny(handles);
                if (signaled == 0) return; // stop event -- never counted as a timer wake

                var wake = _timestampProvider();

                if (diagnosticsEnabled)
                {
                    RecordWaitBlocked(waitStart, wake);
                    RecordTimerWake(wake);
                    RecordWakeLateness(wake, _nextDeadlineTicks);
                }

                var keepGoing = PublishCurrentStateOnce();
                if (diagnosticsEnabled) RecordPublishWork(wake, _timestampProvider());
                if (!keepGoing)
                {
                    CompleteOverlayReturnDiagnostics("PublisherStopped");
                    return;
                }

                if (stopEvent.WaitOne(0)) return;

                var now = _timestampProvider();
                var advance = CanonicalPublisherDeadlineMath.AdvanceDeadline(_nextDeadlineTicks, _periodTicks, now);
                _nextDeadlineTicks = advance.NextDeadlineTicks;
                if (diagnosticsEnabled && advance.SkippedCount > 0) _skippedDeadlineCountSinceHeartbeat += advance.SkippedCount;

                ArmForDeadlineViaSeam(timer, _nextDeadlineTicks, now);
            }
        }
        catch (Exception exception)
        {
            ReportFault(exception);
            CompleteOverlayReturnDiagnostics("PublisherFaulted");
        }
    }

    /// <summary>Test-only seam: lets a test drive the timing-decomposition accounting with known fake
    /// timestamps directly, without needing a real WaitAny/timer wake to happen.</summary>
    internal void RecordWaitBlockedForTests(long waitStart, long wake) => RecordWaitBlocked(waitStart, wake);
    internal void RecordTimerWakeForTests(long wake) => RecordTimerWake(wake);
    internal void RecordPublishWorkForTests(long publishStart, long publishEnd) => RecordPublishWork(publishStart, publishEnd);
    internal void RecordWakeLatenessForTests(long wake, long scheduledDeadline) => RecordWakeLateness(wake, scheduledDeadline);

    private void RecordWaitBlocked(long waitStart, long wake)
    {
        var duration = wake - waitStart;
        _waitBlockedTicksSumSinceHeartbeat += duration;
        if (duration > _maxWaitBlockedTicksSinceHeartbeat) _maxWaitBlockedTicksSinceHeartbeat = duration;
    }

    private void RecordTimerWake(long wake)
    {
        _timerWakeCountSinceHeartbeat++;
        if (_hasPreviousTimerWake)
        {
            var interval = wake - _previousTimerWakeTimestamp;
            _wakeToWakeSampleCountSinceHeartbeat++;
            _wakeToWakeTicksSumSinceHeartbeat += interval;
            if (interval > _maxWakeToWakeTicksSinceHeartbeat) _maxWakeToWakeTicksSinceHeartbeat = interval;
            var intervalSpan = Stopwatch.GetElapsedTime(0, interval);
            if (intervalSpan >= WakeAlarmThreshold) _wakeOver5MsCountSinceHeartbeat++;
            if (intervalSpan >= WakeWarnThreshold) _wakeOver425MsCountSinceHeartbeat++;
        }
        _previousTimerWakeTimestamp = wake;
        _hasPreviousTimerWake = true;
    }

    private void RecordPublishWork(long publishStart, long publishEnd)
    {
        var duration = publishEnd - publishStart;
        _publishWorkTicksSumSinceHeartbeat += duration;
        if (duration > _maxPublishWorkTicksSinceHeartbeat) _maxPublishWorkTicksSinceHeartbeat = duration;
    }

    /// <summary>How late <paramref name="wake"/> landed relative to the deadline that was scheduled for
    /// it. Clamped to zero rather than allowed to go negative -- a wake can (rarely) be observed
    /// slightly before its own scheduled deadline depending on timer/clock granularity, and that is
    /// not a meaningful "negative lateness" for this diagnostic.</summary>
    private void RecordWakeLateness(long wake, long scheduledDeadline)
    {
        var lateness = wake - scheduledDeadline;
        if (lateness < 0) lateness = 0;
        _wakeLatenessTicksSumSinceHeartbeat += lateness;
        if (lateness > _maxWakeLatenessTicksSinceHeartbeat) _maxWakeLatenessTicksSinceHeartbeat = lateness;
    }

    private void ResetTimingDiagnosticsForNewRun()
    {
        ResetMotionImuPublishDiagnostics();
        _setStateCallsSinceHeartbeat = 0;
        _maxSetStateTicksSinceHeartbeat = 0;
        _totalSetStateFailures = 0;

        _previousTimerWakeTimestamp = 0;
        _hasPreviousTimerWake = false;
        _timerWakeCountSinceHeartbeat = 0;
        _wakeToWakeSampleCountSinceHeartbeat = 0;
        _wakeToWakeTicksSumSinceHeartbeat = 0;
        _maxWakeToWakeTicksSinceHeartbeat = 0;
        _wakeOver425MsCountSinceHeartbeat = 0;
        _wakeOver5MsCountSinceHeartbeat = 0;
        _waitBlockedTicksSumSinceHeartbeat = 0;
        _maxWaitBlockedTicksSinceHeartbeat = 0;
        _publishWorkTicksSumSinceHeartbeat = 0;
        _maxPublishWorkTicksSinceHeartbeat = 0;
        _wakeLatenessTicksSumSinceHeartbeat = 0;
        _maxWakeLatenessTicksSinceHeartbeat = 0;
        _skippedDeadlineCountSinceHeartbeat = 0;
    }

    private void ResetMotionImuPublishDiagnostics()
    {
        _motionImuDiagnosticsActive = false;
        _motionImuDiagnosticsWindowStart = 0;
        _motionImuPublishTickCount = 0;
        _motionImuUsableTickCount = 0;
        _motionImuUnavailableTickCount = 0;
        _motionGyroUnavailableTickCount = 0;
        _motionAccelUnavailableTickCount = 0;
        _motionGyroUniqueReceiveTickCount = 0;
        _motionGyroRepeatedReceiveTickCount = 0;
        _motionPreviousGyroReceiveTicks = 0;
    }

    private void StartMotionImuDiagnosticsWindow(long now)
    {
        ResetMotionImuPublishDiagnostics();
        _motionImuDiagnosticsActive = true;
        _motionImuDiagnosticsWindowStart = now;
    }

    private void TrackMotionImuPublishDiagnostics(MsiClawMotionState motion, bool enabled)
    {
        try
        {
            if (!enabled)
            {
                if (_motionImuDiagnosticsActive)
                    ResetMotionImuPublishDiagnostics();
                return;
            }

            var now = _timestampProvider();
            if (!_motionImuDiagnosticsActive || now < _motionImuDiagnosticsWindowStart)
                StartMotionImuDiagnosticsWindow(now);

            _motionImuPublishTickCount++;
            if (motion.IsUsableForSteamDeckImu)
                _motionImuUsableTickCount++;
            else
                _motionImuUnavailableTickCount++;

            if (!motion.HasGyro)
                _motionGyroUnavailableTickCount++;
            else if (motion.GyroReceiveTicks > 0)
            {
                if (motion.GyroReceiveTicks == _motionPreviousGyroReceiveTicks)
                    _motionGyroRepeatedReceiveTickCount++;
                else
                    _motionGyroUniqueReceiveTickCount++;
                _motionPreviousGyroReceiveTicks = motion.GyroReceiveTicks;
            }

            if (!motion.HasAccelerometer)
                _motionAccelUnavailableTickCount++;

            var elapsedTicks = now - _motionImuDiagnosticsWindowStart;
            if (elapsedTicks < Stopwatch.Frequency * 10L)
                return;

            AppLog.Debug("SteamOutput", "MotionImuPublishDiagnosticsSummary",
                ("Event", "MotionImuPublishDiagnosticsSummary"),
                ("WindowMs", Stopwatch.GetElapsedTime(_motionImuDiagnosticsWindowStart, now).TotalMilliseconds),
                ("PublishTickCount", _motionImuPublishTickCount),
                ("ImuUsableTickCount", _motionImuUsableTickCount),
                ("ImuUnavailableTickCount", _motionImuUnavailableTickCount),
                ("GyroUnavailableTickCount", _motionGyroUnavailableTickCount),
                ("AccelUnavailableTickCount", _motionAccelUnavailableTickCount),
                ("GyroUniqueReceiveTickCount", _motionGyroUniqueReceiveTickCount),
                ("GyroRepeatedReceiveTickCount", _motionGyroRepeatedReceiveTickCount));
            StartMotionImuDiagnosticsWindow(now);
        }
        catch
        {
            // Optional telemetry must remain fail-passive if a diagnostic clock or formatter fails.
            ResetMotionImuPublishDiagnostics();
        }
    }

    private async Task PublishAsync(IInputReportTickSource ticks, CancellationToken token)
    {
        try
        {
            while (await ticks.WaitForTickAsync(token).ConfigureAwait(false))
            {
                if (!PublishCurrentStateOnce())
                {
                    CompleteOverlayReturnDiagnostics("PublisherStopped");
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ReportFault(exception);
            CompleteOverlayReturnDiagnostics("PublisherFaulted");
        }
    }

    /// <summary>
    /// The single publish operation shared by both the production worker thread and the test/async
    /// tick loop: map the current snapshot through <see cref="SteamDeckDeviceStateMapper"/>, call
    /// SetState, update counters, and trigger the existing fault path on rejection.
    /// </summary>
    private bool PublishCurrentStateOnce()
    {
        var rawState = _snapshot.LatestState;
        var motion = CaptureMotionSnapshot();
        var rightPad = CaptureRightPadContact();
        var mapped = SteamDeckDeviceStateMapper.Map(
            rawState,
            _rearButtonSuppressionProvider(rawState, AuxiliaryButtonSlot.RightRear),
            _rearButtonSuppressionProvider(rawState, AuxiliaryButtonSlot.LeftRear),
            motion,
            rightPad);
        var state = _systemButtonOverlay.Apply(mapped);

        var diagnosticsEnabled = AppLog.IsEnabled(AppLogLevel.Debug);
        var pulseDiagnosticsEnabled = AppLog.IsEnabled(AppLogLevel.Debug);
        var callStart = diagnosticsEnabled || pulseDiagnosticsEnabled ? _timestampProvider() : 0;
        var accepted = _sink.SetState(state);
        TraceOverlayReturnIfDue(rawState, state, accepted);

        if (diagnosticsEnabled)
        {
            var callDuration = _timestampProvider() - callStart;
            _setStateCallsSinceHeartbeat++;
            if (callDuration > _maxSetStateTicksSinceHeartbeat) _maxSetStateTicksSinceHeartbeat = callDuration;
        }

        TrackMotionImuPublishDiagnostics(motion, pulseDiagnosticsEnabled);

        if (!accepted)
        {
            if (diagnosticsEnabled) _totalSetStateFailures++;
            if (pulseDiagnosticsEnabled) _systemButtonOverlay.RecordPublishResult(state, accepted, _timestampProvider());
            ReportFault(new InvalidOperationException("Canonical VIIPER rejected a typed Steam Deck state."));
            return false;
        }
        _publishedStateCount++;
        LogTriggerTransitionIfNeeded(state, diagnosticsEnabled);

        if (pulseDiagnosticsEnabled)
        {
            _systemButtonOverlay.RecordPublishResult(state, accepted, _timestampProvider());
            if (!_firstOrdinaryInputPublished && HasOrdinaryInput(mapped))
            {
                _firstOrdinaryInputPublished = true;
                AppLog.Debug("SteamOutput", "FirstOrdinaryInputPublished",
                    ("ElapsedSincePublisherStartMs", Stopwatch.GetElapsedTime(_publisherStartedTimestamp, _timestampProvider()).TotalMilliseconds));
            }
        }

        if (diagnosticsEnabled) EmitHeartbeatIfDue();
        return true;
    }

    private void ResetOverlayReturnDiagnostics()
    {
        _overlayReturnTraceActive = false;
        _overlayReturnTraceStartTimestamp = 0;
        _overlayReturnLastSampleTimestamp = 0;
        _overlayReturnSampleCount = 0;
        _overlayReturnFirstStickMovementLogged = false;
        _overlayReturnFirstButtonPressLogged = false;
        _overlayReturnHasButtonBaseline = false;
        _overlayReturnPreviousButtonMask = 0;
    }

    private void StartOverlayReturnDiagnostics(long startTimestamp)
    {
        _overlayReturnTraceActive = true;
        _overlayReturnTraceStartTimestamp = startTimestamp;
        _overlayReturnLastSampleTimestamp = startTimestamp;
        AppLog.Debug("OverlayReturn", "Overlay-return input trace started.",
            ("Event", "OverlayReturnTraceStarted"),
            ("Presentation", "SteamDeck"),
            ("ElapsedMs", 0),
            ("TimeOrigin", "OverlayResumePublisherStart"));
    }

    /// <summary>
    /// Observes the already-read physical snapshot and the exact state/result submitted by the
    /// existing SetState call. Sink acceptance does not establish that Steam Input or a game consumed it.
    /// This optional trace is fail-passive and never performs another input read or output write.
    /// </summary>
    private void TraceOverlayReturnIfDue(ControllerState rawState, SteamDeckDeviceState submittedState, bool accepted)
    {
        if (!_overlayReturnTraceActive) return;
        if (!AppLog.IsEnabled(AppLogLevel.Debug))
        {
            _overlayReturnTraceActive = false;
            return;
        }

        try
        {
            var now = _timestampProvider();
            var elapsed = Stopwatch.GetElapsedTime(_overlayReturnTraceStartTimestamp, now);
            var buttonMask = GetOverlayReturnButtonMask(rawState);
            if (!_overlayReturnHasButtonBaseline)
            {
                // A button held through the Overlay transition is a baseline, not a new press edge.
                _overlayReturnPreviousButtonMask = buttonMask;
                _overlayReturnHasButtonBaseline = true;
            }
            else
            {
                var pressedEdges = buttonMask & ~_overlayReturnPreviousButtonMask;
                _overlayReturnPreviousButtonMask = buttonMask;
                if (!_overlayReturnFirstButtonPressLogged && pressedEdges != 0)
                {
                    _overlayReturnFirstButtonPressLogged = true;
                    LogOverlayReturnObservation("OverlayReturnFirstButtonPress", elapsed, rawState, submittedState, accepted,
                        string.Join(",", OverlayReturnButtonNames.Where(button => (pressedEdges & button.Mask) != 0).Select(button => button.Name)));
                }
            }

            if (!_overlayReturnFirstStickMovementLogged && HasMeaningfulPhysicalStickMovement(rawState))
            {
                _overlayReturnFirstStickMovementLogged = true;
                LogOverlayReturnObservation("OverlayReturnFirstStickMovement", elapsed, rawState, submittedState, accepted);
            }

            if (Stopwatch.GetElapsedTime(_overlayReturnLastSampleTimestamp, now) >= OverlayReturnSampleInterval)
            {
                LogOverlayReturnObservation("OverlayReturnInputSample", elapsed, rawState, submittedState, accepted);
                _overlayReturnLastSampleTimestamp = now;
                _overlayReturnSampleCount++;
            }

            if (elapsed >= OverlayReturnMaximumDuration)
                CompleteOverlayReturnDiagnostics("WindowElapsed", now);
        }
        catch
        {
            // Diagnostics are optional and must never alter the normal publication/fault path.
            _overlayReturnTraceActive = false;
        }
    }

    private void LogOverlayReturnObservation(
        string eventName,
        TimeSpan elapsed,
        ControllerState rawState,
        SteamDeckDeviceState submittedState,
        bool accepted,
        string? button = null)
    {
        var fields = new List<(string Key, object? Value)>
        {
            ("Event", eventName),
            ("ElapsedMs", elapsed.TotalMilliseconds),
            ("RawLX", rawState.LeftStick.X),
            ("RawLY", rawState.LeftStick.Y),
            ("RawRX", rawState.RightStick.X),
            ("RawRY", rawState.RightStick.Y),
            ("DeckLX", submittedState.LStickX),
            ("DeckLY", submittedState.LStickY),
            ("DeckRX", submittedState.RStickX),
            ("DeckRY", submittedState.RStickY),
            ("SetStateAccepted", accepted),
        };
        if (button is not null) fields.Add(("Button", button));
        AppLog.Debug("OverlayReturn", "Existing sink submission observed; Steam/game consumption is not confirmed.", [.. fields]);
    }

    private void CompleteOverlayReturnDiagnostics(string reason, long? timestamp = null)
    {
        if (!_overlayReturnTraceActive) return;
        _overlayReturnTraceActive = false;
        if (!AppLog.IsEnabled(AppLogLevel.Debug)) return;

        try
        {
            var now = timestamp ?? _timestampProvider();
            AppLog.Debug("OverlayReturn", "Overlay-return input trace completed.",
                ("Event", "OverlayReturnTraceCompleted"),
                ("ElapsedMs", Stopwatch.GetElapsedTime(_overlayReturnTraceStartTimestamp, now).TotalMilliseconds),
                ("SampleCount", _overlayReturnSampleCount),
                ("FirstStickMovementObserved", _overlayReturnFirstStickMovementLogged),
                ("FirstButtonPressObserved", _overlayReturnFirstButtonPressLogged),
                ("Reason", reason));
        }
        catch
        {
            // Diagnostics are optional and must never alter publisher shutdown.
        }
    }

    private static bool HasMeaningfulPhysicalStickMovement(ControllerState state) =>
        Math.Abs((int)state.LeftStick.X) >= DiagnosticStickActivityThreshold ||
        Math.Abs((int)state.LeftStick.Y) >= DiagnosticStickActivityThreshold ||
        Math.Abs((int)state.RightStick.X) >= DiagnosticStickActivityThreshold ||
        Math.Abs((int)state.RightStick.Y) >= DiagnosticStickActivityThreshold;

    private static ulong GetOverlayReturnButtonMask(ControllerState state)
    {
        var buttons = state.Buttons;
        ulong mask = 0;
        if (buttons.A) mask |= 1UL << 0;
        if (buttons.B) mask |= 1UL << 1;
        if (buttons.X) mask |= 1UL << 2;
        if (buttons.Y) mask |= 1UL << 3;
        if (buttons.DPadUp) mask |= 1UL << 4;
        if (buttons.DPadRight) mask |= 1UL << 5;
        if (buttons.DPadDown) mask |= 1UL << 6;
        if (buttons.DPadLeft) mask |= 1UL << 7;
        if (buttons.LeftBumper) mask |= 1UL << 8;
        if (buttons.RightBumper) mask |= 1UL << 9;
        if (buttons.Back) mask |= 1UL << 10;
        if (buttons.Start) mask |= 1UL << 11;
        if (buttons.LeftStickClick) mask |= 1UL << 12;
        if (buttons.RightStickClick) mask |= 1UL << 13;
        if (buttons.LeftTriggerFull) mask |= 1UL << 14;
        if (buttons.RightTriggerFull) mask |= 1UL << 15;
        if (state.Auxiliary.Count > (int)AuxiliaryButtonSlot.LeftRear && state.Auxiliary[(int)AuxiliaryButtonSlot.LeftRear]) mask |= 1UL << 16;
        if (state.Auxiliary.Count > (int)AuxiliaryButtonSlot.RightRear && state.Auxiliary[(int)AuxiliaryButtonSlot.RightRear]) mask |= 1UL << 17;
        return mask;
    }

    private void LogTriggerTransitionIfNeeded(SteamDeckDeviceState state, bool diagnosticsEnabled)
    {
        if (!diagnosticsEnabled)
        {
            ResetTriggerDiagnosticSnapshot();
            return;
        }

        string? reason = null;
        if (!_hasTriggerDiagnosticSnapshot)
        {
            reason = "Baseline";
        }
        else
        {
            if (_lastLoggedLTrigger == 0 && state.LTrigger > 0) AppendTriggerDiagnosticReason(ref reason, "LTriggerPress");
            if (_lastLoggedLTrigger > 0 && state.LTrigger == 0) AppendTriggerDiagnosticReason(ref reason, "LTriggerRelease");
            if (Math.Abs((int)state.LTrigger - _lastLoggedLTrigger) >= DiagnosticTriggerMovementThreshold)
                AppendTriggerDiagnosticReason(ref reason, "LTriggerAnalogChange");
            if (_lastLoggedL2Digital != state.L2Digital) AppendTriggerDiagnosticReason(ref reason, "L2FullPullEdge");

            if (_lastLoggedRTrigger == 0 && state.RTrigger > 0) AppendTriggerDiagnosticReason(ref reason, "RTriggerPress");
            if (_lastLoggedRTrigger > 0 && state.RTrigger == 0) AppendTriggerDiagnosticReason(ref reason, "RTriggerRelease");
            if (Math.Abs((int)state.RTrigger - _lastLoggedRTrigger) >= DiagnosticTriggerMovementThreshold)
                AppendTriggerDiagnosticReason(ref reason, "RTriggerAnalogChange");
            if (_lastLoggedR2Digital != state.R2Digital) AppendTriggerDiagnosticReason(ref reason, "R2FullPullEdge");
        }

        if (reason is null) return;

        AppLog.Debug("SteamOutput", "Steam Deck trigger state published.",
            ("Event", "SteamDeckTriggerTransition"),
            ("LTrigger", state.LTrigger),
            ("RTrigger", state.RTrigger),
            ("L2Digital", state.L2Digital),
            ("R2Digital", state.R2Digital),
            ("PublishedStateCount", _publishedStateCount),
            ("Reason", reason));

        _lastLoggedLTrigger = state.LTrigger;
        _lastLoggedRTrigger = state.RTrigger;
        _lastLoggedL2Digital = state.L2Digital;
        _lastLoggedR2Digital = state.R2Digital;
        _hasTriggerDiagnosticSnapshot = true;
    }

    private static void AppendTriggerDiagnosticReason(ref string? reason, string value)
        => reason = reason is null ? value : string.Concat(reason, "+", value);

    private void ResetTriggerDiagnosticSnapshot()
    {
        _hasTriggerDiagnosticSnapshot = false;
        _lastLoggedLTrigger = 0;
        _lastLoggedRTrigger = 0;
        _lastLoggedL2Digital = 0;
        _lastLoggedR2Digital = 0;
    }

    private MsiClawMotionState CaptureMotionSnapshot()
    {
        try
        {
            return _motionSnapshotProvider?.Invoke() ?? MsiClawMotionState.Unavailable;
        }
        catch
        {
            // Motion is optional to ordinary gamepad publication; do not fault the publisher or
            // emit a high-rate log when its snapshot provider fails.
            return MsiClawMotionState.Unavailable;
        }
    }

    private SteamDeckRightPadContact CaptureRightPadContact()
    {
        try
        {
            return _rightPadProvider?.Invoke() ?? SteamDeckRightPadContact.Neutral;
        }
        catch
        {
            // Touch is optional to ordinary gamepad publication; provider failures neutralize this
            // tick only and must not fault the publisher or emit a high-rate log.
            return SteamDeckRightPadContact.Neutral;
        }
    }

    private static bool HasOrdinaryInput(SteamDeckDeviceState state) =>
        state.A != 0 || state.X != 0 || state.B != 0 || state.Y != 0 ||
        state.L1 != 0 || state.R1 != 0 || state.L2Digital != 0 || state.R2Digital != 0 ||
        state.L5 != 0 || state.Menu != 0 || state.Options != 0 ||
        state.DPadDown != 0 || state.DPadLeft != 0 || state.DPadRight != 0 || state.DPadUp != 0 ||
        state.L3 != 0 || state.R3 != 0 || state.R4 != 0 || state.L4 != 0 ||
        state.LTrigger >= DiagnosticTriggerActivityThreshold || state.RTrigger >= DiagnosticTriggerActivityThreshold ||
        Math.Abs((int)state.LStickX) >= DiagnosticStickActivityThreshold || Math.Abs((int)state.LStickY) >= DiagnosticStickActivityThreshold ||
        Math.Abs((int)state.RStickX) >= DiagnosticStickActivityThreshold || Math.Abs((int)state.RStickY) >= DiagnosticStickActivityThreshold;

    private void EmitHeartbeatIfDue()
    {
        var now = _timestampProvider();
        var elapsed = Stopwatch.GetElapsedTime(_lastHeartbeatTimestamp, now);
        if (elapsed < HeartbeatInterval) return;

        var elapsedMs = elapsed.TotalMilliseconds;
        var effectiveHz = elapsedMs > 0 ? _setStateCallsSinceHeartbeat / (elapsedMs / 1000.0) : 0.0;
        var expectedTicksAt4Ms = elapsedMs / 4.0;

        var wakeCount = _timerWakeCountSinceHeartbeat;
        var wakeToWakeSampleCount = _wakeToWakeSampleCountSinceHeartbeat;
        var averageWakeToWakeMs = wakeToWakeSampleCount > 0 ? Stopwatch.GetElapsedTime(0, _wakeToWakeTicksSumSinceHeartbeat / wakeToWakeSampleCount).TotalMilliseconds : 0.0;
        var averageWaitBlockedMs = wakeCount > 0 ? Stopwatch.GetElapsedTime(0, _waitBlockedTicksSumSinceHeartbeat / wakeCount).TotalMilliseconds : 0.0;
        var averagePublishWorkMs = wakeCount > 0 ? Stopwatch.GetElapsedTime(0, _publishWorkTicksSumSinceHeartbeat / wakeCount).TotalMilliseconds : 0.0;
        var averageWakeLatenessMs = wakeCount > 0 ? Stopwatch.GetElapsedTime(0, _wakeLatenessTicksSumSinceHeartbeat / wakeCount).TotalMilliseconds : 0.0;

        AppLog.Debug("SteamOutput", "Canonical Steam Deck publisher heartbeat",
            ("SetStateCallsLastSecond", _setStateCallsSinceHeartbeat),
            ("TotalPublishedStateCount", _publishedStateCount),
            ("SetStateFailures", _totalSetStateFailures),
            ("MaxSetStateDurationMs", Stopwatch.GetElapsedTime(0, _maxSetStateTicksSinceHeartbeat).TotalMilliseconds),
            ("HeartbeatElapsedMs", elapsedMs),
            ("EffectiveSetStateHz", effectiveHz),
            ("TimerWakeCount", wakeCount),
            ("ExpectedTicksAt4ms", expectedTicksAt4Ms),
            ("AverageWakeToWakeMs", averageWakeToWakeMs),
            ("MaxWakeToWakeMs", Stopwatch.GetElapsedTime(0, _maxWakeToWakeTicksSinceHeartbeat).TotalMilliseconds),
            ("AverageWaitBlockedMs", averageWaitBlockedMs),
            ("MaxWaitBlockedMs", Stopwatch.GetElapsedTime(0, _maxWaitBlockedTicksSinceHeartbeat).TotalMilliseconds),
            ("AveragePublishWorkMs", averagePublishWorkMs),
            ("MaxPublishWorkMs", Stopwatch.GetElapsedTime(0, _maxPublishWorkTicksSinceHeartbeat).TotalMilliseconds),
            ("WakeOver4_25MsCount", _wakeOver425MsCountSinceHeartbeat),
            ("WakeOver5MsCount", _wakeOver5MsCountSinceHeartbeat),
            ("AverageWakeLatenessMs", averageWakeLatenessMs),
            ("MaxWakeLatenessMs", Stopwatch.GetElapsedTime(0, _maxWakeLatenessTicksSinceHeartbeat).TotalMilliseconds),
            ("SkippedDeadlineCount", _skippedDeadlineCountSinceHeartbeat));

        _lastHeartbeatTimestamp = now;
        _setStateCallsSinceHeartbeat = 0;
        _maxSetStateTicksSinceHeartbeat = 0;

        _timerWakeCountSinceHeartbeat = 0;
        _wakeToWakeSampleCountSinceHeartbeat = 0;
        _wakeToWakeTicksSumSinceHeartbeat = 0;
        _maxWakeToWakeTicksSinceHeartbeat = 0;
        _wakeOver425MsCountSinceHeartbeat = 0;
        _wakeOver5MsCountSinceHeartbeat = 0;
        _waitBlockedTicksSumSinceHeartbeat = 0;
        _maxWaitBlockedTicksSinceHeartbeat = 0;
        _publishWorkTicksSumSinceHeartbeat = 0;
        _maxPublishWorkTicksSinceHeartbeat = 0;
        _wakeLatenessTicksSumSinceHeartbeat = 0;
        _maxWakeLatenessTicksSinceHeartbeat = 0;
        _skippedDeadlineCountSinceHeartbeat = 0;
        // _previousTimerWakeTimestamp / _hasPreviousTimerWake intentionally NOT reset here: the
        // wake-to-wake interval spanning the heartbeat boundary itself (last tick before this
        // heartbeat to the first tick after it) is still a real, valid sample for the next window.
    }

    private void ReportFault(Exception exception)
    {
        if (Interlocked.Exchange(ref _faultReported, 1) != 0) return;
        AppLog.Error("SteamOutput", "Canonical Steam Deck publisher fault.", exception,
            ("PublishedStateCount", _publishedStateCount));
        _fault?.Invoke(exception);
    }
}
