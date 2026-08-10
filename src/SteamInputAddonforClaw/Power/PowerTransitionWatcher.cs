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
        var barrier = signal == PowerSignal.Suspend || (signal is PowerSignal.ResumeAutomatic or PowerSignal.ResumeSuspend && _gate.IsOpen);
        var applied = false;
        if (barrier)
        {
            applied = _gate.TryEnterBarrier(out before, out _);
            if (applied) { try { _cancelLifecycle(); } catch { _gate.Close(); } }
        }
        var observation = new PowerNotificationObservation(rawCode, signal, DateTimeOffset.UtcNow, _coordinator.NextSequence(), Environment.CurrentManagedThreadId, before, _gate.Epoch, applied);
        _ = Task.Run(() => _coordinator.HandleAsync(observation));
    }
    internal static PowerSignal Map(uint rawCode) => rawCode switch { 4 => PowerSignal.Suspend, 18 => PowerSignal.ResumeAutomatic, 7 => PowerSignal.ResumeSuspend, _ => PowerSignal.Unknown };
    public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) != 0) return; _source.Notification -= OnNotification; _source.Dispose(); }
}
