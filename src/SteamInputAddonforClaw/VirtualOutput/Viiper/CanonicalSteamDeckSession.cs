namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal enum CanonicalSteamDeckSessionState { Clean, Active, CleanupPending, Unsafe }
internal sealed class CanonicalSteamDeckSession : ICanonicalSteamDeckSession
{
    private readonly CanonicalViiperRuntime _runtime; private bool _disposed; private bool _attached;
    internal CanonicalSteamDeckSession(CanonicalViiperRuntime runtime) => _runtime = runtime;
    internal CanonicalSteamDeckSessionState State { get; private set; } = CanonicalSteamDeckSessionState.Clean;
    internal CanonicalPendingCleanupPhase PendingCleanupPhase { get; private set; }
    internal uint? BusId => State == CanonicalSteamDeckSessionState.Clean ? null : _runtime.BusId;
    internal uint? LogicalDeviceId => State == CanonicalSteamDeckSessionState.Clean ? null : _runtime.DeckLogicalDeviceId;
    CanonicalSteamDeckSessionState ICanonicalSteamDeckSession.State => State;
    CanonicalPendingCleanupPhase ICanonicalSteamDeckSession.PendingCleanupPhase => PendingCleanupPhase;
    uint? ICanonicalSteamDeckSession.BusId => BusId; uint? ICanonicalSteamDeckSession.LogicalDeviceId => LogicalDeviceId;
    bool ICanonicalSteamDeckSession.Start() => Start(); bool ICanonicalSteamDeckSession.SetNeutral() => SetNeutral();
    bool ICanonicalSteamDeckSession.SetOutputCallback(SteamDeckOutputCallback callback) => SetOutputCallback(callback);
    bool ICanonicalSteamDeckSession.ClearOutputCallback() => ClearOutputCallback(); bool ICanonicalSteamDeckSession.DetachDevice() => DetachDevice();
    bool ICanonicalSteamDeckSession.RetryPendingCleanup() => RetryPendingCleanup();
    internal bool Start()
    {
        Ensure(); if (State != CanonicalSteamDeckSessionState.Clean || _runtime.State != CanonicalViiperRuntimeState.Ready) return false;
        if (!_runtime.TryGetDeckAttachmentState(out var state) || state != USBDeviceAttachmentState.Detached) { State = CanonicalSteamDeckSessionState.Unsafe; return false; }
        var result = _runtime.AttachDeck();
        if (result == USBDeviceAttachResult.Success) { _attached = true; State = CanonicalSteamDeckSessionState.Active; return true; }
        if (result == USBDeviceAttachResult.RetryableFailure) { State = CanonicalSteamDeckSessionState.Clean; PendingCleanupPhase = CanonicalPendingCleanupPhase.None; return false; }
        State = CanonicalSteamDeckSessionState.Unsafe; return false;
    }
    public bool SetState(SteamDeckDeviceState state) => !_disposed && State == CanonicalSteamDeckSessionState.Active && _runtime.SetDeckState(state);
    internal bool SetNeutral() => SetState(default);
    internal bool SetOutputCallback(SteamDeckOutputCallback callback) => !_disposed && State == CanonicalSteamDeckSessionState.Active && _runtime.SetDeckOutputCallback(callback);
    internal bool ClearOutputCallback() => !_disposed && State == CanonicalSteamDeckSessionState.Active && _runtime.SetDeckOutputCallback(null);
    internal bool DetachDevice()
    {
        Ensure(); if (!_attached || State is not (CanonicalSteamDeckSessionState.Active or CanonicalSteamDeckSessionState.CleanupPending)) return false;
        var result = _runtime.DetachDeck();
        if (result == USBDeviceDetachResult.Success) { _attached = false; State = CanonicalSteamDeckSessionState.Clean; PendingCleanupPhase = CanonicalPendingCleanupPhase.None; return true; }
        if (result == USBDeviceDetachResult.RetryableFailure) { State = CanonicalSteamDeckSessionState.CleanupPending; PendingCleanupPhase = CanonicalPendingCleanupPhase.AttachmentDetach; return false; }
        State = CanonicalSteamDeckSessionState.Unsafe; return false;
    }
    internal bool RetryPendingCleanup() => State == CanonicalSteamDeckSessionState.CleanupPending && DetachDevice();
    public void Dispose() => _disposed = true;
    private void Ensure() { if (_disposed) throw new ObjectDisposedException(nameof(CanonicalSteamDeckSession)); }
}
internal interface ICanonicalSteamDeckStateSink { bool SetState(SteamDeckDeviceState state); }
internal interface ICanonicalSteamDeckSession : ICanonicalSteamDeckStateSink, IDisposable
{
    CanonicalSteamDeckSessionState State { get; } CanonicalPendingCleanupPhase PendingCleanupPhase { get; } uint? BusId { get; } uint? LogicalDeviceId { get; }
    bool Start(); bool SetNeutral(); bool SetOutputCallback(SteamDeckOutputCallback callback); bool ClearOutputCallback(); bool DetachDevice(); bool RetryPendingCleanup();
}

internal sealed class UnavailableCanonicalSteamDeckSession : ICanonicalSteamDeckSession
{
    public CanonicalSteamDeckSessionState State => CanonicalSteamDeckSessionState.Clean;
    public CanonicalPendingCleanupPhase PendingCleanupPhase => CanonicalPendingCleanupPhase.None;
    public uint? BusId => null; public uint? LogicalDeviceId => null;
    public bool Start() => false; public bool SetState(SteamDeckDeviceState state) => false; public bool SetNeutral() => false;
    public bool SetOutputCallback(SteamDeckOutputCallback callback) => false; public bool ClearOutputCallback() => false;
    public bool DetachDevice() => false; public bool RetryPendingCleanup() => false;
    public void Dispose() { }
}
