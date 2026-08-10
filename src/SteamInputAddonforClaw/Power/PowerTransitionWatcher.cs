namespace SteamInputAddonforClaw.Power;

internal interface IPowerSuspendResumeNotificationSource : IDisposable
{
    event Action<uint>? Notification;
    bool TryRegister(out int nativeError);
}

internal sealed class PowerTransitionWatcher : IDisposable
{
    private readonly IPowerSuspendResumeNotificationSource _source;
    private readonly PowerMutationGate _gate;
    private readonly PowerTransitionCoordinator _coordinator;
    private readonly Action _cancelLifecycle;
    private int _disposed;
    // 0 = awake/unknown, 1 = suspend observed, 2 = first resume observed.
    private int _phase;
    internal PowerTransitionWatcher(IPowerSuspendResumeNotificationSource source, PowerMutationGate gate, PowerTransitionCoordinator coordinator, Action cancelLifecycle)
        => (_source, _gate, _coordinator, _cancelLifecycle) = (source, gate, coordinator, cancelLifecycle);
    internal bool Start()
    {
        _source.Notification += OnNotification;
        if (_source.TryRegister(out _)) return true;
        _source.Notification -= OnNotification; _gate.Close(); return false;
    }
    private void OnNotification(uint rawCode)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        var signal = Map(rawCode);
        var before = _gate.Epoch;
        var newSuspend = signal == PowerSignal.Suspend && Interlocked.Exchange(ref _phase, 1) != 1;
        var firstResumeForSuspend = signal is PowerSignal.ResumeAutomatic or PowerSignal.ResumeSuspend && Interlocked.CompareExchange(ref _phase, 2, 1) == 1;
        var duplicateResume = signal is PowerSignal.ResumeAutomatic or PowerSignal.ResumeSuspend && Volatile.Read(ref _phase) == 2;
        var barrier = newSuspend || (signal is PowerSignal.ResumeAutomatic or PowerSignal.ResumeSuspend && !firstResumeForSuspend && !duplicateResume && _gate.IsOpen);
        var applied = false;
        if (barrier)
        {
            if (newSuspend) { _gate.EnterNewCycleBarrier(out before, out _); applied = true; }
            else applied = _gate.TryEnterBarrier(out before, out _);
            if (applied)
            {
                if (signal is PowerSignal.ResumeAutomatic or PowerSignal.ResumeSuspend) Volatile.Write(ref _phase, 2);
                try { _cancelLifecycle(); } catch { _gate.Close(); }
            }
        }
        var observation = new PowerNotificationObservation(rawCode, signal, DateTimeOffset.UtcNow, _coordinator.NextSequence(), Environment.CurrentManagedThreadId, before, _gate.Epoch, applied);
        _coordinator.Enqueue(observation);
    }
    internal static PowerSignal Map(uint rawCode) => rawCode switch { 4 => PowerSignal.Suspend, 18 => PowerSignal.ResumeAutomatic, 7 => PowerSignal.ResumeSuspend, _ => PowerSignal.Unknown };
    public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) != 0) return; _source.Notification -= OnNotification; _source.Dispose(); }
}
