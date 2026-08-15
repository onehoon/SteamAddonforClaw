using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Input;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// Publishes <see cref="IControllerStateSnapshotSource.LatestState"/> to the canonical VIIPER Gordon sink
/// on a monotonic 4ms logical schedule. Real-hardware M5 diagnostics showed the original Task.Delay-based
/// tick source only achieved ~83 Hz against a 250 Hz target (Gordon expects a steady ~4 ms cadence), so
/// production drives this from a dedicated worker thread waiting on a real Windows high-resolution
/// waitable timer instead of an awaited delay -- the wait and the SetState call happen on the same
/// thread, with no thread-pool continuation in between.
/// </summary>
/// <remarks>
/// <para>
/// An earlier revision of this publisher used a periodic timer (re-arming itself automatically every 4ms
/// via <c>lPeriod</c>). Real MSI Claw hardware testing (Addon 0.1.71, PR #156 timing-decomposition
/// diagnostics) showed that design stabilizing at ~230.8 Hz instead of ~250 Hz: average wake-to-wake
/// interval ~4.3324ms, average wait-blocked time ~4.2885ms, average publish work only ~0.04345ms (so
/// <c>SetState</c> was not the bottleneck), and 64.9% of wakes exceeded 4.25ms. This is the observed
/// behavior at the publisher's own wait/wakeup boundary, not a documented guarantee about how
/// <c>SetWaitableTimerEx</c>'s periodic re-activation is internally scheduled -- Microsoft's
/// documentation says a periodic timer re-activates every <c>lPeriod</c> once its due time is reached,
/// but does not specify whether that re-activation is measured from the timer's original schedule or
/// from when it last actually fired. This change tests whether tracking an explicit monotonic deadline
/// prevents the observed per-wake lateness from shifting the publication schedule, without asserting a
/// specific claim about the periodic timer's internal implementation.
/// </para>
/// <para>
/// Production now instead tracks a monotonic absolute logical deadline (in
/// <see cref="Stopwatch"/> ticks: deadline₁ = origin + 4ms, deadline₂ = origin + 8ms, ...) and, after
/// each publish, re-arms a <see cref="WindowsHighResolutionOneShotTimer"/> for only the time remaining
/// until the *next* logical deadline -- not for a full 4ms again. A wake that lands 0.3ms late still
/// only gets a 3.7ms wait until the next deadline, so lateness against the fixed 4/8/12ms... grid should
/// not compound across wakes the way it can with a periodic re-arm, even though each individual wake may
/// still land slightly after its own target (see <c>AverageWakeLatenessMs</c> in the heartbeat below --
/// staying near-constant while <c>AverageWakeToWakeMs</c> returns close to 4.0ms is the expected
/// successful result, not a contradiction).
/// </para>
/// <para>
/// The existing <see cref="IInputReportTickSource"/> async seam is kept for deterministic tests only: when
/// a tick source is supplied explicitly (as every existing test does), <see cref="Start"/> uses the
/// original async loop against it -- the deadline scheduler is entirely unused on that path. Production
/// composition never supplies one, so it always takes the dedicated-thread/one-shot-timer path. Both
/// paths funnel through the same <see cref="PublishCurrentStateOnce"/> so mapping, diagnostics, and fault
/// handling are not duplicated.
/// </para>
/// </remarks>
internal sealed class CanonicalSteamControllerInputPublisher
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ProductionPeriod = TimeSpan.FromMilliseconds(4);
    private static readonly TimeSpan DefaultWorkerJoinTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Test-only seam so the join-timeout fail-closed path can be exercised deterministically
    /// without an actual multi-second wait. Production always uses the 5s default.</summary>
    internal TimeSpan WorkerJoinTimeoutForTests { get; set; } = DefaultWorkerJoinTimeout;

    private readonly IControllerStateSnapshotSource _snapshot;
    private readonly ICanonicalSteamControllerStateSink _sink;
    private readonly IInputReportTickSource? _ticks;
    private readonly Action<Exception>? _fault;
    private readonly Func<long> _timestampProvider;
    private CancellationTokenSource? _stop;
    private Task? _task;
    private int _publishedStateCount;
    private int _faultReported;

    // Production path only: dedicated worker thread driven by a real Windows high-resolution one-shot
    // timer, re-armed each iteration against the deadline schedule below. Never used when a test supplies
    // its own IInputReportTickSource.
    private WindowsHighResolutionOneShotTimer? _timer;
    private ManualResetEvent? _workerStopEvent;
    private Thread? _workerThread;

    // Deadline scheduler state: production-worker-only. _periodTicks is computed once per Start() from
    // ProductionPeriod; _nextDeadlineTicks is established synchronously in Start() (see
    // WindowsHighResolutionOneShotTimer's remarks) and thereafter only ever touched from the dedicated
    // worker thread inside WorkerLoop, so no synchronization is needed once the thread is running.
    private long _periodTicks;
    private long _nextDeadlineTicks;

    // M5 diagnostics: mapped (post-SteamControllerDeviceStateMapper) D-pad transition tracking. Instance
    // state (not static like ControllerStateDiagnostics) because multiple publishers can run in the same
    // process, e.g. in tests.
    private bool _hasLoggedDPadState;
    private (byte Up, byte Right, byte Down, byte Left) _lastLoggedDPad;

    // M5 diagnostics: ~1 Hz heartbeat counters, reset every HeartbeatInterval.
    private long _lastHeartbeatTimestamp;
    private int _setStateCallsSinceHeartbeat;
    private long _maxSetStateTicksSinceHeartbeat;
    private long _totalSetStateFailures;

    // M5+ timing decomposition: production-worker-only, added to separate "Windows timer/wake
    // scheduling" from "publisher work" from "occasional long stalls" after real-hardware M5 testing
    // showed the (then-periodic) timer publisher stabilizing at ~231 Hz instead of the ~250 Hz target.
    // Touched only from the single dedicated worker thread (WorkerLoop below), so no synchronization is
    // needed. These stay at their zero defaults on the test/manual-tick IInputReportTickSource path,
    // which never populates them -- they are meaningful for the production timer-driven path only.
    private long _previousTimerWakeTimestamp;
    private bool _hasPreviousTimerWake;
    private int _timerWakeCountSinceHeartbeat;
    // Distinct from _timerWakeCountSinceHeartbeat: _previousTimerWakeTimestamp intentionally survives a
    // heartbeat reset (see EmitHeartbeatIfDue), so the first wake of a window still yields one interval
    // sample carried over from the prior window -- the number of interval samples is not simply
    // wakeCount - 1 within a single window.
    private int _wakeToWakeSampleCountSinceHeartbeat;
    private long _wakeToWakeTicksSumSinceHeartbeat;
    private long _maxWakeToWakeTicksSinceHeartbeat;
    private int _wakeOver425MsCountSinceHeartbeat;
    private int _wakeOver5MsCountSinceHeartbeat;
    private long _waitBlockedTicksSumSinceHeartbeat;
    private long _maxWaitBlockedTicksSinceHeartbeat;
    private long _publishWorkTicksSumSinceHeartbeat;
    private long _maxPublishWorkTicksSinceHeartbeat;

    // Deadline-scheduler diagnostics: how late each wake landed relative to its own scheduled logical
    // deadline (not the interval between wakes -- see the class remarks), and how many logical deadlines
    // were intentionally skipped (never waited for) because the worker was already past them. Reset every
    // heartbeat, same as the block above.
    private long _wakeLatenessTicksSumSinceHeartbeat;
    private long _maxWakeLatenessTicksSinceHeartbeat;
    private long _skippedDeadlineCountSinceHeartbeat;

    private static readonly TimeSpan WakeWarnThreshold = TimeSpan.FromMilliseconds(4.25);
    private static readonly TimeSpan WakeAlarmThreshold = TimeSpan.FromMilliseconds(5);

    internal CanonicalSteamControllerInputPublisher(
        IControllerStateSnapshotSource snapshot,
        ICanonicalSteamControllerStateSink sink,
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
    /// Starts publishing. Production (no explicit tick source): creates a real Windows high-resolution
    /// timer, establishes the initial logical deadline, and synchronously arms the first one-shot wait
    /// before starting the dedicated worker thread. This throws synchronously, and cleans up any
    /// partially-created native handle, if the timer cannot be created or armed -- fail closed rather than
    /// silently falling back to the known-defective ~83 Hz Task.Delay behavior. The caller's existing
    /// SteamOutput creation/rollback path handles the thrown exception exactly like any other startup
    /// failure.
    /// </summary>
    internal void Start()
    {
        if (IsRunning) throw new InvalidOperationException("The canonical Steam Controller publisher is already running.");
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
            throw new InvalidOperationException("Failed to create the canonical Gordon publisher's high-resolution timer.", exception);
        }

        // Unlike the old periodic timer, the deadline scheduler's origin is established here -- a fresh
        // Start() must not reuse a previous run's logical schedule (mirrors ResetTimingDiagnosticsForNewRun's
        // reasoning: an arbitrary real-world gap can separate a Stop() from a later Start() on the same
        // instance, which production supports).
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
            throw new InvalidOperationException("Failed to arm the canonical Gordon publisher's high-resolution timer.", exception);
        }

        var stopEvent = new ManualResetEvent(false);
        Thread thread;
        try
        {
            // Construction, IsBackground/Name/Priority configuration, and Thread.Start() are all inside
            // this one try: a failure at any of those steps (Thread.Start() can fail, e.g.
            // OutOfMemoryException creating the OS thread; the property setters essentially never throw,
            // but must not be allowed to leak the already-created timer/event if they somehow did) is
            // handled identically -- clean up rather than leak a native handle, and never leave
            // _workerThread pointing at a thread that was never started (StopAsync's Join would throw
            // ThreadStateException on it).
            thread = new Thread(() => WorkerLoop(timer, stopEvent))
            {
                IsBackground = true,
                Name = "SteamInputAddon.GordonPublisher",
                // Real MSI Claw testing showed the #159 deadline scheduler maintaining ~250 Hz under
                // normal load but suffering severe scheduling starvation under CPU-heavy workloads
                // (sustained windows as low as ~60-150 Hz, tens to hundreds of skipped deadlines per
                // heartbeat, and occasional multi-hundred-millisecond stalls). This is a short-lived
                // input-delivery worker that spends most of each 4 ms interval waiting, not running, so
                // AboveNormal gives it a better chance to run promptly when CPU-heavy games saturate
                // available CPU -- without raising the whole process's priority or using realtime
                // scheduling. This does not guarantee 250 Hz under saturation and does not eliminate all
                // CPU starvation; see PR review notes for the hardware A/B comparison this is based on.
                Priority = ThreadPriority.AboveNormal,
            };
            (WorkerThreadStartOverrideForTests ?? (static t => t.Start()))(thread);
        }
        catch (Exception exception)
        {
            timer.Dispose();
            stopEvent.Dispose();
            throw new InvalidOperationException("Failed to start the canonical Gordon publisher worker thread.", exception);
        }

        _timer = timer;
        _workerStopEvent = stopEvent;
        _workerThread = thread;
    }

    /// <summary>Arms <paramref name="timer"/> for the time remaining until <paramref name="deadlineTicks"/>
    /// (a Stopwatch-tick absolute deadline), measured from <paramref name="nowTicks"/>.</summary>
    private static void ArmForDeadline(WindowsHighResolutionOneShotTimer timer, long deadlineTicks, long nowTicks)
    {
        var remainingTicks = deadlineTicks - nowTicks;
        var due100ns = CanonicalPublisherDeadlineMath.ConvertToRelativeDueTime100ns(remainingTicks, Stopwatch.Frequency);
        timer.ArmRelative(TimeSpan.FromTicks(due100ns));
    }

    /// <summary>Test-only seam: lets a test simulate a re-arm failure partway through a run (e.g. a real
    /// <c>SetWaitableTimerEx</c> failure after the timer has already been successfully created and armed
    /// once) without needing to fake the native waitable-timer API at the publisher level. Used for both
    /// the initial synchronous arm in <see cref="StartProductionWorker"/> and every re-arm inside
    /// <see cref="WorkerLoop"/>, so a test can install it once before <see cref="Start"/>.</summary>
    internal Action<WindowsHighResolutionOneShotTimer, long, long>? ArmForDeadlineOverrideForTests { get; set; }

    private void ArmForDeadlineViaSeam(WindowsHighResolutionOneShotTimer timer, long deadlineTicks, long nowTicks) =>
        (ArmForDeadlineOverrideForTests ?? ArmForDeadline)(timer, deadlineTicks, nowTicks);

    /// <summary>Test-only seam: lets a test make thread startup itself fail deterministically (Thread.Start()
    /// cannot be made to fail on demand) to exercise the partial-resource cleanup path above.</summary>
    internal Action<Thread>? WorkerThreadStartOverrideForTests { get; set; }

    /// <summary>
    /// Signals the worker to stop and waits for it to actually exit. Fail closed on a join timeout: the
    /// caller (ClassicSteamControllerOutputStage) proceeds from a successful StopAsync() straight into
    /// native Gordon device removal, so if this returned normally while the worker might still be inside
    /// an in-flight SetState, that removal could race the worker's own SetState call against the native
    /// handle being torn down. So a timed-out join does NOT dispose the timer/stop event or clear the
    /// worker references -- it throws instead, and the caller's existing SteamOutput failure path handles
    /// it like any other stop failure. The stop event stays set, so once the slow SetState call the worker
    /// was blocked in eventually returns, the worker still exits on its very next wait; a subsequent
    /// StopAsync() call (the caller's rollback path already retries operations) will then join and
    /// complete cleanup normally, without re-signaling anything that wasn't already signaled.
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
            // Off-thread so this doesn't block the async caller; bounded by WorkerJoinTimeoutForTests
            // (production always uses the 5s default -- see that property's doc comment).
            var joined = await Task.Run(() => thread.Join(WorkerJoinTimeoutForTests)).ConfigureAwait(false);
            if (!joined)
            {
                var timeout = new TimeoutException("The canonical Gordon publisher worker did not stop within the shutdown timeout; it may still be inside SetState. Refusing to proceed with teardown.");
                AppLog.Error("SteamOutput", "Canonical Gordon publisher worker did not stop within the shutdown timeout.", timeout, ("TimeoutMs", (long)WorkerJoinTimeoutForTests.TotalMilliseconds));
                throw timeout;
            }
        }

        timer?.Dispose();
        stopEvent.Dispose();
        _timer = null;
        _workerStopEvent = null;
        _workerThread = null;
    }

    /// <summary>
    /// Runs on the dedicated worker thread only. Waits for the timer or the stop event, publishes the
    /// current state exactly once per wake -- this is a latest-state publisher, not an event queue, so a
    /// delayed/missed deadline never causes a catch-up burst -- then re-arms the one-shot timer for only
    /// the time remaining until the next logical deadline, skipping over any deadlines that already
    /// expired while this iteration was running.
    /// </summary>
    /// <remarks>
    /// When Info diagnostics are enabled, also decomposes each iteration into wait-block, wake-to-wake,
    /// publish-work, and wake-lateness-vs-schedule timings so a real-hardware heartbeat log can
    /// distinguish "the timer/wake itself is slow" from "publisher work is extending the loop" from
    /// "occasional long stalls" -- see the class-level remarks. This never changes the 4 ms logical
    /// schedule itself; it only measures it. Scheduling itself always samples the clock (the deadline
    /// arithmetic needs it regardless of logging), but when diagnostics are off, no additional timing
    /// arithmetic, aggregate updates, or logging happen beyond that.
    /// </remarks>
    private void WorkerLoop(WindowsHighResolutionOneShotTimer timer, ManualResetEvent stopEvent)
    {
        try
        {
            // Stop must remain index 0: WaitHandle.WaitAny returns the lowest-index signaled handle when
            // several are signaled at once. During an ordinary armed wait, the timer may become signaled
            // at the same moment StopAsync signals the stop event; stop must win that race so no new
            // publish begins after shutdown has been requested. (The one-shot timer is not waiting, and so
            // cannot become signaled, while a SetState call is in flight -- see the post-publish
            // stopEvent.WaitOne(0) check below for that separate case.)
            WaitHandle[] handles = [stopEvent, timer];
            while (true)
            {
                var diagnosticsEnabled = AppLog.IsEnabled(AppLogLevel.Info);
                var waitStart = diagnosticsEnabled ? _timestampProvider() : 0;

                var signaled = WaitHandle.WaitAny(handles);
                if (signaled == 0) return; // stop event -- never counted as a timer wake

                // Always sampled: the deadline scheduler needs "now" below regardless of diagnostics.
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

                // Re-check stop before re-arming: don't unnecessarily arm another wait after shutdown was
                // requested while this iteration was publishing. This is a plain non-blocking poll, not a
                // second WaitAny -- it must never itself block.
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

    /// <summary>
    /// Clears all timing-decomposition state, including <see cref="_previousTimerWakeTimestamp"/> /
    /// <see cref="_hasPreviousTimerWake"/> (which a heartbeat reset intentionally leaves alone -- see
    /// <see cref="EmitHeartbeatIfDue"/>). Called once per <see cref="Start"/>: unlike the heartbeat
    /// boundary, a Stop-then-Start on the same publisher instance can have an arbitrarily long gap
    /// between them, so carrying the last wake timestamp across that gap would make the very first
    /// timer wake of the new run look like a multi-second-long "wake-to-wake interval" outlier.
    /// </summary>
    private void ResetTimingDiagnosticsForNewRun()
    {
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
    /// it. Clamped to zero rather than allowed to go negative -- a wake can (rarely) be observed slightly
    /// before its own scheduled deadline depending on timer/clock granularity, and that is not a
    /// meaningful "negative lateness" for this diagnostic.</summary>
    private void RecordWakeLateness(long wake, long scheduledDeadline)
    {
        var lateness = wake - scheduledDeadline;
        if (lateness < 0) lateness = 0;
        _wakeLatenessTicksSumSinceHeartbeat += lateness;
        if (lateness > _maxWakeLatenessTicksSinceHeartbeat) _maxWakeLatenessTicksSinceHeartbeat = lateness;
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
    /// The single publish operation shared by both the production worker thread and the test/async tick
    /// loop: map the current snapshot, run M5 diagnostics, call SetState, update counters, and trigger the
    /// existing fault path on rejection. Returns false when a SetState rejection has already stopped the
    /// publisher (fault reported) and the caller should not continue.
    /// </summary>
    private bool PublishCurrentStateOnce()
    {
        var state = SteamControllerDeviceStateMapper.Map(_snapshot.LatestState);

        // M5 diagnostics only run when Info (or Debug) is actually enabled: on the 4 ms hot path, avoid
        // the timestamp sampling / comparisons / heartbeat bookkeeping entirely when logging is Off,
        // rather than relying only on AppLog's own internal level check.
        var diagnosticsEnabled = AppLog.IsEnabled(AppLogLevel.Info);
        if (diagnosticsEnabled) LogMappedDPadTransitionIfChanged(state);

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
            ReportFault(new InvalidOperationException("Canonical VIIPER rejected a typed Gordon state."));
            return false;
        }
        _publishedStateCount++;

        if (diagnosticsEnabled) EmitHeartbeatIfDue();
        return true;
    }

    private void LogMappedDPadTransitionIfChanged(SteamControllerDeviceState state)
    {
        var current = (state.DPadUp, state.DPadRight, state.DPadDown, state.DPadLeft);
        if (_hasLoggedDPadState && _lastLoggedDPad == current) return;
        _hasLoggedDPadState = true;
        _lastLoggedDPad = current;
        AppLog.Info("SteamOutput", "Canonical mapped D-pad state changed",
            ("Up", state.DPadUp), ("Right", state.DPadRight), ("Down", state.DPadDown), ("Left", state.DPadLeft));
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

        AppLog.Info("SteamOutput", "Canonical Steam Controller publisher heartbeat",
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
        // wake-to-wake interval spanning the heartbeat boundary itself (last tick before this heartbeat
        // to the first tick after it) is still a real, valid sample for the next window.
    }

    private void ReportFault(Exception exception)
    {
        if (Interlocked.Exchange(ref _faultReported, 1) != 0) return;
        AppLog.Error("SteamOutput", "Canonical Steam Controller publisher fault.", exception,
            ("PublishedStateCount", _publishedStateCount));
        _fault?.Invoke(exception);
    }
}

/// <summary>
/// Pure, deterministically-testable arithmetic for the monotonic absolute-deadline scheduler used by
/// <see cref="CanonicalSteamControllerInputPublisher"/>'s production worker. Kept separate from the
/// publisher's instance state so the deadline-advance and clock-conversion logic can be tested directly
/// without needing a running worker thread or real timer.
/// </summary>
internal static class CanonicalPublisherDeadlineMath
{
    /// <summary>Converts a <see cref="TimeSpan"/> period into an equivalent duration in
    /// <see cref="Stopwatch"/> ticks for the given <paramref name="frequency"/> (<see cref="Stopwatch.Frequency"/>
    /// in production; injectable here so tests aren't tied to the actual machine's QPC frequency).</summary>
    internal static long StopwatchTicksFromTimeSpan(TimeSpan span, long frequency)
    {
        if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Frequency must be positive.");
        // decimal (128-bit) avoids overflow for any realistic period/frequency combination and keeps
        // full precision through the division; Math.Round to the nearest whole tick rather than
        // truncating, so a period does not end up systematically slightly short.
        var ticks = (decimal)span.Ticks * frequency / TimeSpan.TicksPerSecond;
        return (long)Math.Round(ticks, MidpointRounding.AwayFromZero);
    }

    /// <summary>Converts a positive duration expressed in <see cref="Stopwatch"/> ticks into the
    /// equivalent number of 100ns units (the unit <c>SetWaitableTimerEx</c>'s relative due time and
    /// <see cref="TimeSpan.Ticks"/> both use). Always rounds up (ceiling) and never returns less than 1:
    /// a genuinely-positive-but-tiny remaining duration must still arm a positive one-shot wait rather
    /// than truncating to zero (which <see cref="WindowsHighResolutionOneShotTimer.ArmRelative"/> would
    /// reject as non-positive).</summary>
    internal static long ConvertToRelativeDueTime100ns(long remainingStopwatchTicks, long frequency)
    {
        if (remainingStopwatchTicks <= 0) throw new ArgumentOutOfRangeException(nameof(remainingStopwatchTicks), remainingStopwatchTicks, "The remaining duration must be positive.");
        if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Frequency must be positive.");

        var hundredNsUnits = (decimal)remainingStopwatchTicks * TimeSpan.TicksPerSecond / frequency;
        var rounded = (long)Math.Ceiling(hundredNsUnits);
        return rounded < 1 ? 1 : rounded;
    }

    /// <summary>Result of <see cref="AdvanceDeadline"/>: the next logical deadline to arm for, and how
    /// many additional logical deadlines beyond the normal single-period step were skipped (never waited
    /// for) because the worker was already past them by the time it finished the current publish.</summary>
    internal readonly record struct DeadlineAdvanceResult(long NextDeadlineTicks, long SkippedCount);

    /// <summary>
    /// Advances the logical deadline schedule by exactly one period, then skips forward over any further
    /// whole periods that have already expired relative to <paramref name="nowTicks"/>, so the returned
    /// deadline is always strictly in the future. This is what keeps the schedule on the fixed
    /// 4/8/12/16ms... grid instead of drifting: the *next* deadline is always computed from the *previous
    /// deadline*, never from "now" -- "now" is only used to detect and skip deadlines that are already
    /// unreachable, never to redefine the grid itself.
    /// </summary>
    /// <remarks>
    /// In the steady-state case (the worker keeps up, even if each individual wake lands slightly late),
    /// <see cref="DeadlineAdvanceResult.SkippedCount"/> is always 0 -- advancing by exactly one period is
    /// the normal, expected behavior every iteration and is not itself counted as a "skip." A skip is
    /// only counted for a whole additional period that gets bypassed with no wait or publish of its own,
    /// which should be rare (a genuine multi-period stall) rather than routine.
    /// </remarks>
    internal static DeadlineAdvanceResult AdvanceDeadline(long currentDeadlineTicks, long periodTicks, long nowTicks)
    {
        if (periodTicks <= 0) throw new ArgumentOutOfRangeException(nameof(periodTicks), periodTicks, "The period must be positive.");

        checked
        {
            var next = currentDeadlineTicks + periodTicks;
            if (next > nowTicks) return new DeadlineAdvanceResult(next, 0);

            // next is already unusable (at or before "now"): skip forward by whole periods until the
            // deadline is strictly in the future. behindBy >= 0 here.
            var behindBy = nowTicks - next;
            var additionalSkips = behindBy / periodTicks + 1;
            next += additionalSkips * periodTicks;
            return new DeadlineAdvanceResult(next, additionalSkips);
        }
    }
}
