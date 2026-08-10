namespace SteamInputAddonforClaw.Power;

internal enum RecoverySafety { Safe, Unsafe, Indeterminate }
internal interface IRecoverySafetyState { RecoverySafety Current { get; } }
internal sealed class RecoverySafetyState(RecoverySafety initial) : IRecoverySafetyState
{
    private int _current = (int)initial;
    public RecoverySafety Current => (RecoverySafety)Volatile.Read(ref _current);
    internal void Set(RecoverySafety value) => Volatile.Write(ref _current, (int)value);
}
