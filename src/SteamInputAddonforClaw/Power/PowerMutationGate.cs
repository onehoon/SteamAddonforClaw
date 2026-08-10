namespace SteamInputAddonforClaw.Power;

internal readonly record struct PowerMutationToken(long Epoch);

internal sealed class PowerMutationGate
{
    private readonly Lock _sync = new();
    private long _epoch;
    private bool _open;

    internal PowerMutationGate(bool initiallyOpen = false) => _open = initiallyOpen;
    internal long Epoch { get { lock (_sync) return _epoch; } }
    internal bool IsOpen { get { lock (_sync) return _open; } }
    internal bool TryAcquire(out PowerMutationToken token)
    {
        lock (_sync) { token = new(_epoch); return _open; }
    }
    internal bool IsCurrent(PowerMutationToken token)
    {
        lock (_sync) return _open && token.Epoch == _epoch;
    }
    internal bool TryEnterBarrier(out long previousEpoch, out long epoch)
    {
        lock (_sync)
        {
            previousEpoch = _epoch;
            if (!_open) { epoch = _epoch; return false; }
            _open = false; epoch = ++_epoch; return true;
        }
    }
    internal void EnterNewCycleBarrier(out long previousEpoch, out long epoch)
    {
        lock (_sync) { previousEpoch = _epoch; _open = false; epoch = ++_epoch; }
    }
    internal void OpenAfterRecovery() { lock (_sync) _open = true; }
    internal bool TryOpenAfterRecovery(long expectedEpoch)
    {
        lock (_sync)
        {
            if (_epoch != expectedEpoch) return false;
            _open = true;
            return true;
        }
    }
    internal bool TryCommitRecovery(long expectedEpoch, bool openGate, Action commitState)
    {
        lock (_sync)
        {
            if (_epoch != expectedEpoch) return false;
            commitState();
            _open = openGate;
            return true;
        }
    }
    internal void Close() { lock (_sync) _open = false; }
}
