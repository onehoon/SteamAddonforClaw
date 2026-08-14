using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Input;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// Publishes <see cref="IControllerStateSnapshotSource.LatestState"/> to the canonical VIIPER Gordon sink
/// on a fixed period. Real-hardware M5 diagnostics showed the previous Task.Delay-based tick source only
/// achieved ~83 Hz against a 250 Hz target (Gordon expects a steady ~4 ms cadence), so production now
/// drives this from a dedicated worker thread waiting on a real Windows high-resolution periodic timer
/// (<see cref="WindowsHighResolutionPeriodicTimer"/>) instead of an awaited delay -- the wait and the
/// SetState call happen on the same thread, with no thread-pool continuation in between.
/// </summary>
/// <remarks>
/// The existing <see cref="IInputReportTickSource"/> async seam is kept for deterministic tests only: when
/// a tick source is supplied explicitly (as every existing test does), <see cref="Start"/> uses the
/// original async loop against it. Production composition never supplies one, so it always takes the
/// dedicated-thread/high-resolution-timer path. Both paths funnel through the same
/// <see cref="PublishCurrentStateOnce"/> so mapping, diagnostics, and fault handling are not duplicated.
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

    // Production path only: dedicated worker thread driven by a real Windows high-resolution periodic
    // timer. Never used when a test supplies its own IInputReportTickSource.
    private WindowsHighResolutionPeriodicTimer? _timer;
    private ManualResetEvent? _workerStopEvent;
    private Thread? _workerThread;

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
    /// Starts publishing. Production (no explicit tick source): creates and arms a real Windows
    /// high-resolution periodic timer and starts a dedicated worker thread. This throws synchronously,
    /// and cleans up any partially-created native handle, if the timer cannot be created or armed --
    /// fail closed rather than silently falling back to the known-defective ~83 Hz Task.Delay behavior.
    /// The caller's existing SteamOutput creation/rollback path handles the thrown exception exactly like
    /// any other startup failure.
    /// </summary>
    internal void Start()
    {
        if (IsRunning) throw new InvalidOperationException("The canonical Steam Controller publisher is already running.");
        _lastHeartbeatTimestamp = _timestampProvider();

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
        WindowsHighResolutionPeriodicTimer timer;
        try
        {
            timer = new WindowsHighResolutionPeriodicTimer(ProductionPeriod);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("Failed to create or arm the canonical Gordon publisher's high-resolution timer.", exception);
        }

        var stopEvent = new ManualResetEvent(false);
        var thread = new Thread(() => WorkerLoop(timer, stopEvent))
        {
            IsBackground = true,
            Name = "SteamInputAddon.GordonPublisher",
            Priority = ThreadPriority.Normal,
        };

        try
        {
            (WorkerThreadStartOverrideForTests ?? (static t => t.Start()))(thread);
        }
        catch (Exception exception)
        {
            // Thread.Start() can fail (e.g. OutOfMemoryException creating the OS thread); the timer and
            // stop event were already successfully created, so clean those up rather than leaking a
            // native handle, and never leave _workerThread pointing at a thread that was never started
            // (StopAsync's Join would throw ThreadStateException on it).
            timer.Dispose();
            stopEvent.Dispose();
            throw new InvalidOperationException("Failed to start the canonical Gordon publisher worker thread.", exception);
        }

        _timer = timer;
        _workerStopEvent = stopEvent;
        _workerThread = thread;
    }

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
    /// Runs on the dedicated worker thread only. Waits for the timer or the stop event, then publishes
    /// the current state exactly once per wake -- this is a latest-state publisher, not an event queue,
    /// so a delayed/missed interval never causes a catch-up burst; the next wake just publishes whatever
    /// LatestState is current at that moment.
    /// </summary>
    private void WorkerLoop(WindowsHighResolutionPeriodicTimer timer, ManualResetEvent stopEvent)
    {
        try
        {
            // Stop must be index 0: WaitHandle.WaitAny returns the lowest-index signaled handle when
            // several are signaled at once. If a slow SetState call runs past a timer period while
            // StopAsync is concurrently signaling the stop event, both handles can be signaled by the
            // time this thread comes back to wait -- putting the timer first would let one more publish
            // start after stop was requested (and, under sustained SetState delay, could starve stop
            // indefinitely). Stop must always win that race.
            WaitHandle[] handles = [stopEvent, timer];
            while (true)
            {
                var signaled = WaitHandle.WaitAny(handles);
                if (signaled == 0) return; // stop event
                if (!PublishCurrentStateOnce()) return;
            }
        }
        catch (Exception exception)
        {
            ReportFault(exception);
        }
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

        AppLog.Info("SteamOutput", "Canonical Steam Controller publisher heartbeat",
            ("SetStateCallsLastSecond", _setStateCallsSinceHeartbeat),
            ("TotalPublishedStateCount", _publishedStateCount),
            ("SetStateFailures", _totalSetStateFailures),
            ("MaxSetStateDurationMs", Stopwatch.GetElapsedTime(0, _maxSetStateTicksSinceHeartbeat).TotalMilliseconds),
            ("HeartbeatElapsedMs", elapsedMs),
            ("EffectiveSetStateHz", effectiveHz));

        _lastHeartbeatTimestamp = now;
        _setStateCallsSinceHeartbeat = 0;
        _maxSetStateTicksSinceHeartbeat = 0;
    }

    private void ReportFault(Exception exception)
    {
        if (Interlocked.Exchange(ref _faultReported, 1) != 0) return;
        AppLog.Error("SteamOutput", "Canonical Steam Controller publisher fault.", exception,
            ("PublishedStateCount", _publishedStateCount));
        _fault?.Invoke(exception);
    }
}
