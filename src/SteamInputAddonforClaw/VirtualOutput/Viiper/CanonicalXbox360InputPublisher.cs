using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Input;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// Publishes <see cref="IControllerStateSnapshotSource.LatestState"/>, mapped through
/// <see cref="Xbox360DeviceStateMapper"/>, to a caller-supplied Xbox360 state sink on a monotonic
/// absolute ~250 Hz (4 ms) deadline schedule, driven by a dedicated worker thread waiting on
/// <see cref="WindowsHighResolutionOneShotTimer"/> and re-armed via
/// <see cref="CanonicalPublisherDeadlineMath"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the Xbox360 publisher used by the production temporary Game Bar presentation
/// (see docs/VIIPER_MIGRATION_TODO.md, SD7).
/// It reuses the same scheduling primitives, locking-free single-worker design, and fail-closed
/// lifecycle discipline already proven by <see cref="CanonicalSteamDeckInputPublisher"/> against real
/// MSI Claw hardware -- see that class's remarks for the timing rationale. Nothing in this class
/// creates, attaches, detaches, or otherwise owns the Xbox360 logical device; it is handed a plain
/// state sink delegate (matching <c>CanonicalViiperRuntime.SetXbox360State</c>'s signature) by its
/// caller and only ever writes mapped state through it. It also never writes neutral state on stop --
/// the Addon routing runtime explicitly owns publisher-stop -&gt; neutral -&gt; detach
/// ordering, not this class.
/// </para>
/// <para>
/// Production composes this publisher only for the temporary Game Bar presentation while an outer
/// Steam route is active. It remains detached and unpublished when that presentation is inactive.
/// </para>
/// </remarks>
internal sealed class CanonicalXbox360InputPublisher
{
    private static readonly TimeSpan ProductionPeriod = TimeSpan.FromMilliseconds(4);
    private static readonly TimeSpan DefaultWorkerJoinTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Test-only seam so the join-timeout fail-closed path can be exercised deterministically.</summary>
    internal TimeSpan WorkerJoinTimeoutForTests { get; set; } = DefaultWorkerJoinTimeout;

    private readonly IControllerStateSnapshotSource _snapshot;
    private readonly Func<Xbox360DeviceState, bool> _setState;
    private readonly IInputReportTickSource? _ticks;
    private readonly Action<Exception>? _fault;
    private readonly Func<long> _timestampProvider;
    private CancellationTokenSource? _stop;
    private Task? _task;
    private int _publishedStateCount;
    private int _faultReported;

    // Production path only: dedicated worker thread driven by a real Windows high-resolution
    // one-shot timer, re-armed each iteration against the deadline schedule below. Never used when a
    // test supplies its own IInputReportTickSource.
    private WindowsHighResolutionOneShotTimer? _timer;
    private ManualResetEvent? _workerStopEvent;
    private Thread? _workerThread;

    private long _periodTicks;
    private long _nextDeadlineTicks;

    internal CanonicalXbox360InputPublisher(
        IControllerStateSnapshotSource snapshot,
        Func<Xbox360DeviceState, bool> setState,
        IInputReportTickSource? ticks = null,
        Action<Exception>? fault = null,
        Func<long>? timestampProvider = null)
    {
        _snapshot = snapshot;
        _setState = setState;
        _ticks = ticks;
        _fault = fault;
        _timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
    }

    internal bool IsRunning => _task is { IsCompleted: false } || _workerThread is { IsAlive: true };
    internal int PublishedStateCount => _publishedStateCount;

    /// <summary>
    /// Whether the previous run's resources are still owned by this instance. Deliberately distinct
    /// from <see cref="IsRunning"/>: a rejected sink or an unexpected exception can self-terminate
    /// <c>_task</c>/the worker thread (so <see cref="IsRunning"/> goes false) while the CTS, timer,
    /// stop event, or thread reference are still un-disposed/un-cleared -- only <see cref="StopAsync"/>
    /// releases them. Gating <see cref="Start"/> on liveness alone would let a caller start a new run
    /// on top of a stranded previous one.
    /// </summary>
    private bool HasRunResources =>
        _task is not null ||
        _stop is not null ||
        _timer is not null ||
        _workerStopEvent is not null ||
        _workerThread is not null;

    /// <summary>
    /// Starts publishing. On the production (no explicit tick source) path this throws synchronously
    /// and cleans up any partially-created resources if the timer cannot be created or armed, or the
    /// worker thread cannot be started -- fail closed rather than silently falling back to a defective
    /// cadence. Safe to call again after a completed <see cref="StopAsync"/> (a Steam game may open and
    /// close Xbox Game Bar, and therefore this publisher, multiple times within one routing session).
    /// Throws if the previous run's resources have not yet been released via <see cref="StopAsync"/>,
    /// even if that run already self-terminated (see <see cref="HasRunResources"/>).
    /// </summary>
    internal void Start()
    {
        if (HasRunResources)
            throw new InvalidOperationException("The previous canonical Xbox360 publisher run has not been cleaned up; call StopAsync() before starting again.");
        Interlocked.Exchange(ref _faultReported, 0);

        if (_ticks is not null)
        {
            _stop = new CancellationTokenSource();
            _task = PublishAsync(_ticks, _stop.Token);
            return;
        }

        StartProductionWorker();
    }

    /// <summary>
    /// Signals the worker to stop and proves it has actually exited before returning -- the future
    /// Game Bar teardown sequence is "stop publisher -&gt; write Xbox360 neutral -&gt; DetachXbox360()",
    /// so a caller must be able to trust that a successful <see cref="StopAsync"/> means no further
    /// <c>SetState</c> call can originate from this publisher, before it proceeds to detach the native
    /// device. This does not itself write neutral state or touch attachment -- that is deliberately the
    /// future coordinator's responsibility, not this publisher's.
    /// </summary>
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
            throw new InvalidOperationException("Failed to create the canonical Xbox360 publisher's high-resolution timer.", exception);
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
            throw new InvalidOperationException("Failed to arm the canonical Xbox360 publisher's high-resolution timer.", exception);
        }

        var stopEvent = new ManualResetEvent(false);
        Thread thread;
        try
        {
            thread = new Thread(() => WorkerLoop(timer, stopEvent))
            {
                IsBackground = true,
                Name = "SteamInputAddon.Xbox360Publisher",
                Priority = ThreadPriority.AboveNormal,
            };
            (WorkerThreadStartOverrideForTests ?? (static t => t.Start()))(thread);
        }
        catch (Exception exception)
        {
            timer.Dispose();
            stopEvent.Dispose();
            throw new InvalidOperationException("Failed to start the canonical Xbox360 publisher worker thread.", exception);
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
    /// Signals the worker to stop and waits for it to actually exit. Fails closed (throws) on a join
    /// timeout rather than pretending the worker stopped -- see <see cref="StopAsync"/>'s remarks. Also
    /// runs (and cleans up timer/event resources) when the worker has already exited on its own because
    /// the sink rejected state or the loop threw: <c>_workerStopEvent</c> not being null is what proves
    /// there is still a previous run's resources to release, independent of whether the thread itself is
    /// still alive.
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
                var timeout = new TimeoutException("The canonical Xbox360 publisher worker did not stop within the shutdown timeout; it may still be inside SetState. Refusing to proceed with teardown.");
                AppLog.Error("SteamOutput", "Canonical Xbox360 publisher worker did not stop within the shutdown timeout.", timeout, ("TimeoutMs", (long)WorkerJoinTimeoutForTests.TotalMilliseconds));
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
                if (signaled == 0) return; // stop event

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
    /// tick loop: map the current snapshot through <see cref="Xbox360DeviceStateMapper"/>, invoke the
    /// sink, and report+stop once on rejection. No overlay, no Guide mutation, no other transformation.
    /// </summary>
    private bool PublishCurrentStateOnce()
    {
        var mapped = Xbox360DeviceStateMapper.Map(_snapshot.LatestState);
        if (!_setState(mapped))
        {
            ReportFault(new InvalidOperationException("Canonical VIIPER rejected a typed Xbox360 state."));
            return false;
        }
        _publishedStateCount++;
        return true;
    }

    private void ReportFault(Exception exception)
    {
        if (Interlocked.Exchange(ref _faultReported, 1) != 0) return;
        AppLog.Error("SteamOutput", "Canonical Xbox360 publisher fault.", exception,
            ("PublishedStateCount", _publishedStateCount));
        _fault?.Invoke(exception);
    }
}
