namespace SteamInputAddonforClaw.Power;

internal readonly record struct PowerMutationToken(long Epoch);

internal sealed class PowerMutationGate
{
    private readonly Lock _sync = new();
    private long _epoch;
    private bool _open;
    private bool _suspendCleanupAllowed;

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
    internal bool TryAcquireCleanup(out PowerMutationToken token)
    {
        lock (_sync) { token = new(_epoch); return _open || _suspendCleanupAllowed; }
    }
    internal bool IsCurrentCleanup(PowerMutationToken token)
    {
        lock (_sync) return token.Epoch == _epoch && (_open || _suspendCleanupAllowed);
    }
    internal bool TryEnterBarrier(out long previousEpoch, out long epoch)
    {
        lock (_sync)
        {
            previousEpoch = _epoch;
            if (!_open) { _suspendCleanupAllowed = false; epoch = _epoch; return false; }
            _open = false; _suspendCleanupAllowed = false; epoch = ++_epoch; return true;
        }
    }
    internal void EnterNewCycleBarrier(out long previousEpoch, out long epoch)
    {
        lock (_sync)
        {
            previousEpoch = _epoch;
            _open = false;
            _suspendCleanupAllowed = true;
            epoch = ++_epoch;
        }
    }
    internal bool TrySealSuspendCleanup(long expectedEpoch)
    {
        lock (_sync)
        {
            if (_epoch != expectedEpoch) return false;
            _suspendCleanupAllowed = false;
            return true;
        }
    }
    internal void OpenAfterRecovery() { lock (_sync) { _open = true; _suspendCleanupAllowed = false; } }
    internal bool TryOpenAfterRecovery(long expectedEpoch)
    {
        lock (_sync)
        {
            if (_epoch != expectedEpoch) return false;
            _open = true; _suspendCleanupAllowed = false;
            return true;
        }
    }
    internal bool TryCommitRecovery(long expectedEpoch, bool openGate, Action commitState)
    {
        lock (_sync)
        {
            if (_epoch != expectedEpoch) return false;
            commitState();
            _open = openGate; _suspendCleanupAllowed = false;
            return true;
        }
    }
    internal bool TryCommitMutation(PowerMutationToken token, Action commitState)
    {
        lock (_sync)
        {
            if (!_open || _epoch != token.Epoch) return false;
            commitState();
            return true;
        }
    }
    internal void Close() { lock (_sync) { _open = false; _suspendCleanupAllowed = false; } }
}
