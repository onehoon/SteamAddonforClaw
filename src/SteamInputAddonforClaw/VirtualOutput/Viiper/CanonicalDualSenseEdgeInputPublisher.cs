using System.Diagnostics;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Input;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>Publishes typed Edge state at 250 Hz from deterministic ticks or a high-resolution timer.</summary>
internal sealed class CanonicalDualSenseEdgeInputPublisher
{
    private static readonly TimeSpan ProductionPeriod = TimeSpan.FromMilliseconds(4);
    private static readonly TimeSpan DefaultWorkerJoinTimeout = TimeSpan.FromSeconds(5);

    private readonly IControllerStateSnapshotSource _snapshot;
    private readonly ICanonicalDualSenseEdgeStateSink _sink;
    private readonly IInputReportTickSource? _ticks;
    private readonly Action<Exception>? _fault;
    private readonly Func<long> _timestampProvider;
    private readonly Func<ControllerState, AuxiliaryButtonSlot, bool> _rearButtonSuppressionProvider;
    private readonly Func<MsiClawMotionState>? _motionSnapshotProvider;
    private readonly DualSenseEdgeSystemButtonOverlay? _systemButtonOverlay;
    private CancellationTokenSource? _stop;
    private Task? _task;
    private WindowsHighResolutionOneShotTimer? _timer;
    private ManualResetEvent? _workerStopEvent;
    private Thread? _workerThread;
    private long _periodTicks;
    private long _nextDeadlineTicks;
    private long _lastHeartbeatTicks;
    private int _publishedStateCount;
    private int _heartbeatCount;
    private int _faultReported;

    internal CanonicalDualSenseEdgeInputPublisher(
        IControllerStateSnapshotSource snapshot,
        ICanonicalDualSenseEdgeStateSink sink,
        IInputReportTickSource? ticks = null,
        Action<Exception>? fault = null,
        Func<long>? timestampProvider = null,
        Func<ControllerState, AuxiliaryButtonSlot, bool>? rearButtonSuppressionProvider = null,
        Func<MsiClawMotionState>? motionSnapshotProvider = null,
        DualSenseEdgeSystemButtonOverlay? systemButtonOverlay = null)
    {
        _snapshot = snapshot;
        _sink = sink;
        _ticks = ticks;
        _fault = fault;
        _timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
        _rearButtonSuppressionProvider = rearButtonSuppressionProvider ?? (static (_, _) => false);
        _motionSnapshotProvider = motionSnapshotProvider;
        _systemButtonOverlay = systemButtonOverlay;
    }

    internal static Action<Thread>? WorkerThreadStartOverrideForTests { get; set; }
    internal TimeSpan WorkerJoinTimeoutForTests { get; set; } = DefaultWorkerJoinTimeout;
    internal bool IsRunning => _task is { IsCompleted: false } || _workerThread is { IsAlive: true };
    internal int PublishedStateCount => Volatile.Read(ref _publishedStateCount);
    internal int HeartbeatCount => Volatile.Read(ref _heartbeatCount);

    internal void Start()
    {
        if (_task is not null || _stop is not null || _timer is not null || _workerStopEvent is not null || _workerThread is not null)
            throw new InvalidOperationException("The previous DualSense Edge publisher run has not been cleaned up.");
        Interlocked.Exchange(ref _faultReported, 0);
        _publishedStateCount = 0;
        _heartbeatCount = 0;
        _periodTicks = CanonicalPublisherDeadlineMath.StopwatchTicksFromTimeSpan(ProductionPeriod, Stopwatch.Frequency);
        _lastHeartbeatTicks = _timestampProvider();

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
            var task = _task;
            var stop = _stop;
            stop?.Cancel();
            await task.ConfigureAwait(false);
            stop?.Dispose();
            _stop = null;
            _task = null;
        }
        else if (_workerStopEvent is not null)
        {
            await StopProductionWorkerAsync().ConfigureAwait(false);
        }

        if (!_sink.SetState(default))
        {
            var exception = new InvalidOperationException("Canonical VIIPER rejected neutral DualSense Edge state after publisher stop.");
            ReportFault(exception);
            throw exception;
        }
    }

    internal bool PublishCurrentStateOnceForTests() => PublishCurrentStateOnce();

    private void StartProductionWorker()
    {
        WindowsHighResolutionOneShotTimer timer;
        try { timer = new WindowsHighResolutionOneShotTimer(); }
        catch (Exception exception) { throw new InvalidOperationException("Failed to create the DualSense Edge high-resolution timer.", exception); }

        var origin = _timestampProvider();
        _nextDeadlineTicks = origin + _periodTicks;
        try { ArmForDeadline(timer, _nextDeadlineTicks, origin); }
        catch (Exception exception)
        {
            timer.Dispose();
            throw new InvalidOperationException("Failed to arm the DualSense Edge high-resolution timer.", exception);
        }

        var stopEvent = new ManualResetEvent(false);
        Thread thread;
        try
        {
            thread = new Thread(() => WorkerLoop(timer, stopEvent))
            {
                IsBackground = true,
                Name = "SteamInputAddon.DualSenseEdgePublisher",
                Priority = ThreadPriority.AboveNormal
            };
            (WorkerThreadStartOverrideForTests ?? (static worker => worker.Start()))(thread);
        }
        catch (Exception exception)
        {
            timer.Dispose();
            stopEvent.Dispose();
            throw new InvalidOperationException("Failed to start the DualSense Edge publisher worker.", exception);
        }

        _timer = timer;
        _workerStopEvent = stopEvent;
        _workerThread = thread;
    }

    private void WorkerLoop(WindowsHighResolutionOneShotTimer timer, ManualResetEvent stopEvent)
    {
        PublisherThreadQoS.ApplyHighQoS("DualSenseEdge");
        var waits = new WaitHandle[] { timer, stopEvent };
        try
        {
            while (WaitHandle.WaitAny(waits) == 0)
            {
                if (!PublishCurrentStateOnce()) return;
                if (stopEvent.WaitOne(0)) return;
                var now = _timestampProvider();
                var advance = CanonicalPublisherDeadlineMath.AdvanceDeadline(_nextDeadlineTicks, _periodTicks, now);
                _nextDeadlineTicks = advance.NextDeadlineTicks;
                ArmForDeadline(timer, _nextDeadlineTicks, now);
            }
        }
        catch (Exception exception) { ReportFault(exception); }
    }

    private async Task StopProductionWorkerAsync()
    {
        var stopEvent = _workerStopEvent;
        if (stopEvent is null) return;
        stopEvent.Set();
        if (_workerThread is { } thread)
        {
            var joined = await Task.Run(() => thread.Join(WorkerJoinTimeoutForTests)).ConfigureAwait(false);
            if (!joined) throw new TimeoutException("DualSense Edge publisher did not stop before the native device teardown deadline.");
        }
        _timer?.Dispose();
        stopEvent.Dispose();
        _timer = null;
        _workerStopEvent = null;
        _workerThread = null;
    }

    private async Task PublishAsync(IInputReportTickSource ticks, CancellationToken token)
    {
        try
        {
            while (await ticks.WaitForTickAsync(token).ConfigureAwait(false))
                if (!PublishCurrentStateOnce()) return;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { ReportFault(exception); }
    }

    private bool PublishCurrentStateOnce()
    {
        if (Volatile.Read(ref _faultReported) != 0) return false;
        try
        {
            var state = _snapshot.LatestState;
            var motion = CaptureMotionSnapshot();
            var mapped = DualSenseEdgeDeviceStateMapper.Map(
                state,
                _rearButtonSuppressionProvider(state, AuxiliaryButtonSlot.RightRear),
                _rearButtonSuppressionProvider(state, AuxiliaryButtonSlot.LeftRear),
                motion,
                _timestampProvider(),
                Stopwatch.Frequency);
            if (_systemButtonOverlay is { } overlay)
                mapped = overlay.Apply(mapped);
            if (!_sink.SetState(mapped))
            {
                ReportFault(new InvalidOperationException("Canonical VIIPER rejected a typed DualSense Edge state."));
                return false;
            }
            var count = Interlocked.Increment(ref _publishedStateCount);
            var now = _timestampProvider();
            if (Stopwatch.GetElapsedTime(_lastHeartbeatTicks, now) >= TimeSpan.FromSeconds(1))
            {
                AppLog.Debug("SteamOutput", "Canonical DualSense Edge publisher heartbeat.",
                    ("Event", "DualSenseEdgePublisherHeartbeat"), ("PublishedStateCount", count));
                Interlocked.Increment(ref _heartbeatCount);
                _lastHeartbeatTicks = now;
            }
            return true;
        }
        catch (Exception exception)
        {
            ReportFault(exception);
            return false;
        }
    }

    private MsiClawMotionState CaptureMotionSnapshot()
    {
        try { return _motionSnapshotProvider?.Invoke() ?? MsiClawMotionState.Unavailable; }
        catch { return MsiClawMotionState.Unavailable; }
    }

    private void ReportFault(Exception exception)
    {
        if (Interlocked.Exchange(ref _faultReported, 1) != 0) return;
        try { _fault?.Invoke(exception); }
        catch { }
    }

    private static void ArmForDeadline(WindowsHighResolutionOneShotTimer timer, long deadlineTicks, long nowTicks)
    {
        var due100ns = CanonicalPublisherDeadlineMath.ConvertToRelativeDueTime100ns(deadlineTicks - nowTicks, Stopwatch.Frequency);
        timer.ArmRelative(TimeSpan.FromTicks(due100ns));
    }
}
