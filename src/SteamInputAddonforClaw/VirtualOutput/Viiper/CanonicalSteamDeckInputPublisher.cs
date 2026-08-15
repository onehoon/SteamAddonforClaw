using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Input;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// Steam Deck counterpart to <see cref="CanonicalSteamControllerInputPublisher"/>: publishes
/// <see cref="IControllerStateSnapshotSource.LatestState"/>, mapped through
/// <see cref="SteamDeckDeviceStateMapper"/>, to the canonical VIIPER Steam Deck sink on the same
/// ~250 Hz (4 ms) monotonic-deadline schedule as the proven Gordon publisher, driven by a dedicated
/// worker thread waiting on <see cref="WindowsHighResolutionOneShotTimer"/> and re-armed via
/// <see cref="CanonicalPublisherDeadlineMath"/> -- see that class and
/// <see cref="CanonicalSteamControllerInputPublisher"/> for the detailed real-hardware timing
/// rationale this reuses unchanged. This is a deliberate simple duplication for SD2, not a shared
/// publisher framework refactor -- see docs/VIIPER_MIGRATION_TODO.md SD2.
/// </summary>
internal sealed class CanonicalSteamDeckInputPublisher
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(1);
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
    /// Starts publishing. See <see cref="CanonicalSteamControllerInputPublisher.Start"/> for the
    /// detailed fail-closed startup rationale this mirrors: on the production (no explicit tick
    /// source) path this throws synchronously and cleans up any partially-created native handle if
    /// the timer cannot be created or armed, rather than silently falling back to a defective cadence.
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

    /// <summary>Test-only seam mirroring <see cref="CanonicalSteamControllerInputPublisher.ArmForDeadlineOverrideForTests"/>.</summary>
    internal Action<WindowsHighResolutionOneShotTimer, long, long>? ArmForDeadlineOverrideForTests { get; set; }

    private void ArmForDeadlineViaSeam(WindowsHighResolutionOneShotTimer timer, long deadlineTicks, long nowTicks) =>
        (ArmForDeadlineOverrideForTests ?? ArmForDeadline)(timer, deadlineTicks, nowTicks);

    /// <summary>Test-only seam mirroring <see cref="CanonicalSteamControllerInputPublisher.WorkerThreadStartOverrideForTests"/>.</summary>
    internal Action<Thread>? WorkerThreadStartOverrideForTests { get; set; }

    /// <summary>
    /// Signals the worker to stop and waits for it to actually exit. Fail closed on a join timeout
    /// for the same reason as <see cref="CanonicalSteamControllerInputPublisher"/>: the caller
    /// proceeds from a successful StopAsync() straight into native Steam Deck device removal, so a
    /// timed-out join must not silently allow that race -- it throws instead.
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
                var signaled = WaitHandle.WaitAny(handles);
                if (signaled == 0) return; // stop event -- never counted as a timer wake

                var keepGoing = PublishCurrentStateOnce();
                if (!keepGoing) return;

                if (stopEvent.WaitOne(0)) return;

                var now = _timestampProvider();
                var advance = CanonicalPublisherDeadlineMath.AdvanceDeadline(_nextDeadlineTicks, _periodTicks, now);
                _nextDeadlineTicks = advance.NextDeadlineTicks;

                ArmForDeadlineViaSeam(timer, _nextDeadlineTicks, now);
            }
        }
        catch (Exception exception)
        {
            ReportFault(exception);
        }
    }

    private void ResetTimingDiagnosticsForNewRun()
    {
        _setStateCallsSinceHeartbeat = 0;
        _maxSetStateTicksSinceHeartbeat = 0;
        _totalSetStateFailures = 0;
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

        AppLog.Info("SteamOutput", "Canonical Steam Deck publisher heartbeat",
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
        AppLog.Error("SteamOutput", "Canonical Steam Deck publisher fault.", exception,
            ("PublishedStateCount", _publishedStateCount));
        _fault?.Invoke(exception);
    }
}
