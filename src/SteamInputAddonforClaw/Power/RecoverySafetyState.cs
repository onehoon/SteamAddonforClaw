namespace SteamInputAddonforClaw.Power;

internal enum RecoverySafety { Safe, Unsafe, Indeterminate }
internal interface IRecoverySafetyState { RecoverySafety Current { get; } }
internal sealed class RecoverySafetyState(RecoverySafety initial) : IRecoverySafetyState
{
    private readonly Lock _sync = new();
    private int _current = (int)initial;
    private long _version;
    public RecoverySafety Current => (RecoverySafety)Volatile.Read(ref _current);
    internal long Set(RecoverySafety value)
    {
        lock (_sync) { Volatile.Write(ref _current, (int)value); return ++_version; }
    }
    internal bool TrySet(long expectedVersion, RecoverySafety value)
    {
        lock (_sync)
        {
            if (_version != expectedVersion) return false;
            Volatile.Write(ref _current, (int)value);
            ++_version;
            return true;
        }
    }
}
