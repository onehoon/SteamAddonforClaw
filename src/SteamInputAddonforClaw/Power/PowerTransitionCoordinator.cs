using SteamInputAddonforClaw.Diagnostics;
using System.Threading.Channels;

namespace SteamInputAddonforClaw.Power;

internal sealed class PowerTransitionCoordinator : IAsyncDisposable
{
    private readonly PowerMutationGate _gate;
    private readonly IReadOnlyList<IPowerSuspendParticipant> _participants;
    private readonly RecoverySafetyState _recovery;
    private readonly Func<CancellationToken, Task<bool>> _establishBaseline;
    private readonly Func<CancellationToken, Task<bool>>? _afterRecovery;
    private readonly bool _recoveryEnabled;
    private readonly TimeSpan _suspendQuiesceBudget;
    private readonly Action? _resumeObserved;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly Channel<QueuedNotification> _notifications = Channel.CreateUnbounded<QueuedNotification>(new() { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _reader;
    private long _cycle;
    private long _sequence;
    private long _resumeCycle = -1;
    private int _disposed;
    internal PowerTransitionState State { get; private set; } = PowerTransitionState.Awake;
    internal PowerTransitionCoordinator(PowerMutationGate gate, RecoverySafetyState recovery, IEnumerable<IPowerSuspendParticipant> participants, Func<CancellationToken, Task<bool>>? afterRecovery = null, bool recoveryEnabled = true, Func<CancellationToken, Task<bool>>? establishBaseline = null, TimeSpan? suspendQuiesceBudget = null, Action? resumeObserved = null)
    {
        (_gate, _recovery, _participants, _afterRecovery) = (gate, recovery, participants.ToArray(), afterRecovery);
        _recoveryEnabled = recoveryEnabled;
        _establishBaseline = establishBaseline ?? (_ => Task.FromResult(true));
        // Real Windows suspend grace period budget for participants to quiesce. Kept as an
        // injectable value (default unchanged) so tests can give slower/contended CI runners
        // headroom against enqueue-to-processing scheduling delay without weakening the real
        // production deadline.
        _suspendQuiesceBudget = suspendQuiesceBudget ?? TimeSpan.FromMilliseconds(1200);
        _resumeObserved = resumeObserved;
        _reader = Task.Run(ProcessNotificationsAsync);
    }
    internal long NextSequence() => Interlocked.Increment(ref _sequence);
    internal void InvalidateForBarrier()
    {
        _recovery.Set(RecoverySafety.Indeterminate);
        State = PowerTransitionState.Quiescing;
    }
    internal Task Enqueue(PowerNotificationObservation observation)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_notifications.Writer.TryWrite(new(observation, completion))) completion.TrySetCanceled();
        return completion.Task;
    }
    private async Task ProcessNotificationsAsync()
    {
        try
        {
            await foreach (var queued in _notifications.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                try { await HandleAsync(queued.Observation, _shutdown.Token).ConfigureAwait(false); queued.Completion.TrySetResult(); }
                catch (Exception exception) { queued.Completion.TrySetException(exception); }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }
    private sealed record QueuedNotification(PowerNotificationObservation Observation, TaskCompletionSource Completion);
    internal async Task HandleAsync(PowerNotificationObservation observation, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppLog.Info("Power.Notify", "Power notification observed.", ("Cycle", _cycle), ("Epoch", _gate.Epoch), ("Signal", observation.Signal), ("ObservedUtc", observation.ObservedUtc), ("BarrierApplied", observation.BarrierApplied));
            if (observation.Signal == PowerSignal.Suspend)
            {
                if (!observation.BarrierApplied) { AppLog.Debug("Power.Coordinator", "Duplicate suspend ignored.", ("Cycle", _cycle), ("Epoch", _gate.Epoch)); return; }
                var cycle = Interlocked.Increment(ref _cycle); _resumeCycle = -1; State = PowerTransitionState.Quiescing;
                var suspendEpoch = observation.EpochAfter;
                var deadline = observation.ObservedUtc.Add(_suspendQuiesceBudget);
                var success = true;
                var cleanupSealed = false;
                try
                {
                    foreach (var participant in _participants)
                    {
                        var remaining = deadline - DateTimeOffset.UtcNow;
                        if (remaining <= TimeSpan.Zero) { success = false; break; }
                        using var timeout = new CancellationTokenSource(remaining);
                        try { success &= await participant.QuiesceForSuspendAsync(deadline, cycle, suspendEpoch, timeout.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) { success = false; }
                        catch (Exception e) { AppLog.Error("Power.Participant", "Power participant quiesce failed.", e, ("Participant", participant.Name), ("Cycle", cycle), ("Epoch", suspendEpoch)); success = false; }
                    }
                }
                finally { cleanupSealed = _gate.TrySealSuspendCleanup(suspendEpoch); }
                if (!cleanupSealed)
                {
                    AppLog.Warn("Power.Coordinator", "Suspend completion ignored because a newer power epoch is authoritative.", null,
                        ("Cycle", cycle), ("SuspendEpoch", suspendEpoch), ("CurrentEpoch", _gate.Epoch));
                    return;
                }
                State = success ? PowerTransitionState.Suspended : PowerTransitionState.Unsafe;
                AppLog.Info("Power.Coordinator", "Suspend quiesce completed.", ("Cycle", cycle), ("Epoch", _gate.Epoch), ("Outcome", success ? "Succeeded" : "TimedOutOrFailed"), ("GateState", _gate.IsOpen ? "Open" : "Closed"), ("FinalPowerState", State));
                return;
            }
            if (observation.Signal is not (PowerSignal.ResumeAutomatic or PowerSignal.ResumeSuspend)) return;
            if (State == PowerTransitionState.Recovering || (_cycle != 0 && _resumeCycle == _cycle)) { AppLog.Debug("Power.Coordinator", "Duplicate resume ignored.", ("Cycle", _cycle), ("Epoch", _gate.Epoch)); return; }
            // A queued resume is authoritative only for the epoch recorded when the
            // observation was created. Check before applying a fallback barrier so a newer
            // suspend cannot be adopted by this stale handler.
            var recoveryEpoch = observation.EpochAfter;
            if (_gate.Epoch != recoveryEpoch)
            {
                AppLog.Warn("Power.Recovery", "Stale resume ignored because a newer power barrier is authoritative.", null,
                    ("ObservedEpoch", recoveryEpoch), ("CurrentEpoch", _gate.Epoch));
                return;
            }
            if (!observation.BarrierApplied)
                _gate.TryEnterBarrier(out _, out recoveryEpoch);

            // Commit the accepted resume cycle and emit the observer BEFORE the disabled-recovery
            // fail-close: a Sleep/Hibernate -> Resume while Addon authority is active (RecoverySafe=false,
            // so recoveryEnabled=false) is a real supported lifecycle path, and AddonProcessHost's
            // current Full1902 owned-controller reconcile hooks run off PowerResumeObserved. The
            // generic forward-mutation gate still stays closed for that state.
            State = PowerTransitionState.Recovering; _recovery.Set(RecoverySafety.Indeterminate);
            var cycleForResume = _cycle == 0 ? Interlocked.Increment(ref _cycle) : _cycle;
            _resumeCycle = cycleForResume;
            try { _resumeObserved?.Invoke(); } catch (Exception exception) { AppLog.Warn("Power.Resume", "Resume observer failed.", exception); }

            if (!_recoveryEnabled)
            {
                State = PowerTransitionState.Unsafe;
                _recovery.Set(RecoverySafety.Unsafe);
                AppLog.Warn("Power.Recovery", "Resume recovery is disabled because this process did not establish a safe startup boundary.", null, ("Action", "RemainPassive"));
                return;
            }
            var resumeStartedUtc = DateTimeOffset.UtcNow;
            var recoveryManagerStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var recoveryElapsedMs = 0d;
            var recoveryCompleted = false;
            var safe = false;
            try
            {
                safe = await _establishBaseline(cancellationToken).ConfigureAwait(false);
                AppLog.Info("Power.Resume", "Resume Stock baseline completed.", ("Action", "EstablishStockBaseline"), ("Result", safe ? "Succeeded" : "Failed"));
                recoveryElapsedMs = recoveryManagerStopwatch.Elapsed.TotalMilliseconds;
                recoveryCompleted = true;
                if (_gate.Epoch != recoveryEpoch) { AppLog.Warn("Power.Recovery", "Resume reconciliation invalidated by a newer power barrier.", null, ("Cycle", cycleForResume), ("CapturedEpoch", recoveryEpoch), ("CurrentEpoch", _gate.Epoch)); return; }
            }
            catch (Exception e) { if (!recoveryCompleted) recoveryElapsedMs = recoveryManagerStopwatch.Elapsed.TotalMilliseconds; AppLog.Error("Power.Recovery", "Resume reconciliation failed.", e, ("Cycle", cycleForResume), ("Epoch", _gate.Epoch)); safe = false; }
            if (_gate.Epoch != recoveryEpoch) return;
            if (!_gate.TryCommitRecovery(recoveryEpoch, safe, () =>
                {
                    _recovery.Set(safe ? RecoverySafety.Safe : RecoverySafety.Unsafe);
                    State = safe ? PowerTransitionState.Awake : PowerTransitionState.Unsafe;
                })) return;
            if (safe && _afterRecovery is not null)
            {
                try { safe &= await _afterRecovery(cancellationToken).ConfigureAwait(false); }
                catch (Exception e) { AppLog.Error("Power.Recovery", "Post-recovery session reconciliation failed.", e, ("Cycle", cycleForResume), ("Epoch", recoveryEpoch)); safe = false; }
                if (!safe)
                {
                    if (!_gate.TryCommitRecovery(recoveryEpoch, openGate: false, () =>
                    {
                        _recovery.Set(RecoverySafety.Unsafe);
                        State = PowerTransitionState.Unsafe;
                    }))
                    {
                        AppLog.Warn("Power.Recovery", "Stale post-resume failure ignored because a newer power epoch is authoritative.", null,
                            ("CapturedEpoch", recoveryEpoch), ("CurrentEpoch", _gate.Epoch));
                        return;
                    }
                }
            }
            AppLog.Info("Power.Recovery", "Resume reconciliation completed.", ("Cycle", cycleForResume), ("Epoch", _gate.Epoch), ("Outcome", safe ? "Succeeded" : "Failed"), ("PowerGateOpened", _gate.IsOpen), ("FinalPowerState", State), ("ResumeObservedUtc", observation.ObservedUtc), ("ResumeDispatchDelayMs", (resumeStartedUtc - observation.ObservedUtc).TotalMilliseconds), ("RecoveryElapsedMs", recoveryElapsedMs), ("ResumeToGateReadyMs", (DateTimeOffset.UtcNow - observation.ObservedUtc).TotalMilliseconds));
        }
        finally { _serial.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _gate.Close(); _notifications.Writer.TryComplete(); _shutdown.Cancel();
        try { await _reader.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _shutdown.Dispose(); _serial.Dispose();
    }
}
