using SteamInputAddonforClaw.Diagnostics;
using System.Threading.Channels;

namespace SteamInputAddonforClaw.Power;

internal sealed class PowerTransitionCoordinator : IAsyncDisposable
{
    private readonly PowerMutationGate _gate;
    private readonly IReadOnlyList<IPowerTransitionParticipant> _participants;
    private readonly RecoverySafetyState _recovery;
    private readonly Func<CancellationToken, Task<bool>> _recover;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly Channel<QueuedNotification> _notifications = Channel.CreateUnbounded<QueuedNotification>(new() { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _reader;
    private long _cycle;
    private long _sequence;
    private long _resumeCycle = -1;
    private int _disposed;
    internal PowerTransitionState State { get; private set; } = PowerTransitionState.Awake;
    internal PowerTransitionCoordinator(PowerMutationGate gate, RecoverySafetyState recovery, Func<CancellationToken, Task<bool>> recover, IEnumerable<IPowerTransitionParticipant> participants)
    {
        (_gate, _recovery, _recover, _participants) = (gate, recovery, recover, participants.ToArray());
        _reader = Task.Run(ProcessNotificationsAsync);
    }
    internal long NextSequence() => Interlocked.Increment(ref _sequence);
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
                var deadline = observation.ObservedUtc.AddMilliseconds(1200);
                var success = true;
                foreach (var participant in _participants)
                {
                    var remaining = deadline - DateTimeOffset.UtcNow;
                    if (remaining <= TimeSpan.Zero) { success = false; break; }
                    using var timeout = new CancellationTokenSource(remaining);
                    try { success &= await participant.QuiesceForSuspendAsync(deadline, cycle, _gate.Epoch, timeout.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { success = false; }
                    catch (Exception e) { AppLog.Error("Power.Participant", "Power participant quiesce failed.", e, ("Participant", participant.Name), ("Cycle", cycle), ("Epoch", _gate.Epoch)); success = false; }
                }
                State = success ? PowerTransitionState.Suspended : PowerTransitionState.Unsafe;
                AppLog.Info("Power.Coordinator", "Suspend quiesce completed.", ("Cycle", cycle), ("Epoch", _gate.Epoch), ("Outcome", success ? "Succeeded" : "TimedOutOrFailed"), ("GateState", _gate.IsOpen ? "Open" : "Closed"), ("FinalPowerState", State));
                return;
            }
            if (observation.Signal is not (PowerSignal.ResumeAutomatic or PowerSignal.ResumeSuspend)) return;
            if (State == PowerTransitionState.Recovering || (_cycle != 0 && _resumeCycle == _cycle)) { AppLog.Debug("Power.Coordinator", "Duplicate resume ignored.", ("Cycle", _cycle), ("Epoch", _gate.Epoch)); return; }
            if (!observation.BarrierApplied) _gate.TryEnterBarrier(out _, out _);
            State = PowerTransitionState.Recovering; _recovery.Set(RecoverySafety.Indeterminate);
            var cycleForResume = _cycle == 0 ? Interlocked.Increment(ref _cycle) : _cycle;
            _resumeCycle = cycleForResume;
            var safe = false;
            try
            {
                safe = await _recover(cancellationToken).ConfigureAwait(false);
                foreach (var participant in _participants) safe &= await participant.ReconcileAfterResumeAsync(cycleForResume, _gate.Epoch, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) { AppLog.Error("Power.Recovery", "Resume reconciliation failed.", e, ("Cycle", cycleForResume), ("Epoch", _gate.Epoch)); safe = false; }
            _recovery.Set(safe ? RecoverySafety.Safe : RecoverySafety.Unsafe);
            State = safe ? PowerTransitionState.Awake : PowerTransitionState.Unsafe;
            if (safe) _gate.OpenAfterRecovery(); else _gate.Close();
            AppLog.Info("Power.Recovery", "Resume reconciliation completed.", ("Cycle", cycleForResume), ("Epoch", _gate.Epoch), ("Outcome", safe ? "Succeeded" : "Failed"), ("PowerGateOpened", _gate.IsOpen), ("FinalPowerState", State));
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
