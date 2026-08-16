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

    /// <summary>Test-only seam so the join-timeout fail-closed path can be exercised deterministically.</summary>
    internal TimeSpan WorkerJoinTimeoutForTests { get; set; } = DefaultWorkerJoinTimeout;

    private readonly IControllerStateSnapshotSource _snapshot;
    private readonly ICanonicalSteamDeckStateSink _sink;
    private readonly IInputReportTickSource? _ticks;
    private readonly Action<Exception>? _fault;
    private readonly Func<long> _timestampProvider;
    private CancellationTokenSource? _stop;
    private Task? _task;
    private int _publishedStateCount;
    private int _faultReported;

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

    internal CanonicalSteamDeckInputPublisher(
        IControllerStateSnapshotSource snapshot,
        ICanonicalSteamDeckStateSink sink,
        IInputReportTickSource? ticks = null,
        Action<Exception>? fault = null,
        Func<long>? timestampProvider = null)
    {
        _snapshot = snapshot;
        _sink = sink;
        _ticks = ticks;
        _fault = fault;
        _timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
    }

    internal bool IsRunning => _task is { IsCompleted: false } || _workerThread is { IsAlive: true };
    internal int PublishedStateCount => _publishedStateCount;

    /// <summary>
    /// Starts publishing. On the production (no explicit tick source) path this throws synchronously
    /// and cleans up any partially-created native handle if the timer cannot be created or armed --
    /// fail closed rather than silently falling back to a defective cadence.
    /// </summary>
    internal void Start()
    {
        if (IsRunning) throw new InvalidOperationException("The canonical Steam Deck publisher is already running.");
        _lastHeartbeatTimestamp = _timestampProvider();
        ResetTimingDiagnosticsForNewRun();

        if (_ticks is not null)
        {
            _stop = new CancellationTokenSource();
            _task = PublishAsync(_ticks, _stop.Token);
            return;
        }

        StartProductionWorker();
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
            return;
        }

        await StopProductionWorkerAsync().ConfigureAwait(false);
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
            WaitHandle[] handles = [stopEvent, timer];
            while (true)
            {
                var diagnosticsEnabled = AppLog.IsEnabled(AppLogLevel.Info);
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
                if (!keepGoing) return;

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

    private async Task PublishAsync(IInputReportTickSource ticks, CancellationToken token)
    {
        try
        {
            while (await ticks.WaitForTickAsync(token).ConfigureAwait(false))
            {
                if (!PublishCurrentStateOnce()) return;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { ReportFault(exception); }
    }

    /// <summary>
    /// The single publish operation shared by both the production worker thread and the test/async
    /// tick loop: map the current snapshot through <see cref="SteamDeckDeviceStateMapper"/>, call
    /// SetState, update counters, and trigger the existing fault path on rejection.
    /// </summary>
    private bool PublishCurrentStateOnce()
    {
        var state = SteamDeckDeviceStateMapper.Map(_snapshot.LatestState);

        var diagnosticsEnabled = AppLog.IsEnabled(AppLogLevel.Info);
        var callStart = diagnosticsEnabled ? _timestampProvider() : 0;
        var accepted = _sink.SetState(state);

        if (diagnosticsEnabled)
        {
            var callDuration = _timestampProvider() - callStart;
            _setStateCallsSinceHeartbeat++;
            if (callDuration > _maxSetStateTicksSinceHeartbeat) _maxSetStateTicksSinceHeartbeat = callDuration;
        }

        if (!accepted)
        {
            if (diagnosticsEnabled) _totalSetStateFailures++;
            ReportFault(new InvalidOperationException("Canonical VIIPER rejected a typed Steam Deck state."));
            return false;
        }
        _publishedStateCount++;

        if (diagnosticsEnabled) EmitHeartbeatIfDue();
        return true;
    }

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

        AppLog.Info("SteamOutput", "Canonical Steam Deck publisher heartbeat",
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
