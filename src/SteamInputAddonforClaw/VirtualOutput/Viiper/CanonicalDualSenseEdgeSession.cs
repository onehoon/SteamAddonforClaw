namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal enum CanonicalDualSenseEdgeSessionState { Clean, Active, CleanupPending, Unsafe }

/// <summary>A narrow attach/state/callback facade over the existing canonical VIIPER runtime.</summary>
internal sealed class CanonicalDualSenseEdgeSession : ICanonicalDualSenseEdgeSession
{
    private readonly CanonicalViiperRuntime _runtime;
    private bool _disposed;
    private bool _attached;

    internal CanonicalDualSenseEdgeSession(CanonicalViiperRuntime runtime) => _runtime = runtime;

    public CanonicalDualSenseEdgeSessionState State { get; private set; } = CanonicalDualSenseEdgeSessionState.Clean;
    internal uint? BusId => State == CanonicalDualSenseEdgeSessionState.Clean ? null : _runtime.BusId;
    internal uint? LogicalDeviceId => State == CanonicalDualSenseEdgeSessionState.Clean ? null : _runtime.DualSenseEdgeLogicalDeviceId;

    public bool Start()
    {
        EnsureNotDisposed();
        if (State != CanonicalDualSenseEdgeSessionState.Clean || _runtime.State != CanonicalViiperRuntimeState.Ready)
            return false;

        if (!_runtime.TryEnsureDualSenseEdgeDevice())
        {
            if (_runtime.State is CanonicalViiperRuntimeState.Unsafe or CanonicalViiperRuntimeState.CleanupPending)
                State = CanonicalDualSenseEdgeSessionState.Unsafe;
            return false;
        }
        if (!_runtime.TryGetDualSenseEdgeAttachmentState(out var attachment) || attachment != USBDeviceAttachmentState.Detached)
        {
            State = CanonicalDualSenseEdgeSessionState.Unsafe;
            return false;
        }

        var result = _runtime.AttachDualSenseEdge();
        if (result == USBDeviceAttachResult.Success)
        {
            _attached = true;
            State = CanonicalDualSenseEdgeSessionState.Active;
            return true;
        }
        if (result == USBDeviceAttachResult.RetryableFailure) return false;
        State = CanonicalDualSenseEdgeSessionState.Unsafe;
        return false;
    }

    public bool SetState(DSDeviceState state) => !_disposed && State == CanonicalDualSenseEdgeSessionState.Active && _runtime.SetDualSenseEdgeState(state);
    public bool SetNeutral() => SetState(default);
    public bool SetOutputCallback(DualSenseOutputCallback callback) => !_disposed && State == CanonicalDualSenseEdgeSessionState.Active && _runtime.SetDualSenseEdgeOutputCallback(callback);
    public bool ClearOutputCallback() => !_disposed && State == CanonicalDualSenseEdgeSessionState.Active && _runtime.SetDualSenseEdgeOutputCallback(null);

    public bool TryGetTrackedAttachmentState(out USBDeviceAttachmentState state)
    {
        if (_disposed || State != CanonicalDualSenseEdgeSessionState.Active)
        {
            state = default;
            return false;
        }
        return _runtime.TryGetDualSenseEdgeAttachmentState(out state);
    }

    public bool DetachDevice()
    {
        EnsureNotDisposed();
        if (!_attached || State is not (CanonicalDualSenseEdgeSessionState.Active or CanonicalDualSenseEdgeSessionState.CleanupPending)) return false;
        var result = _runtime.DetachDualSenseEdge();
        if (result == USBDeviceDetachResult.Success)
        {
            _attached = false;
            State = CanonicalDualSenseEdgeSessionState.Clean;
            return true;
        }
        if (result == USBDeviceDetachResult.RetryableFailure)
        {
            State = CanonicalDualSenseEdgeSessionState.CleanupPending;
            return false;
        }
        State = CanonicalDualSenseEdgeSessionState.Unsafe;
        return false;
    }

    public bool RetryPendingCleanup() => State == CanonicalDualSenseEdgeSessionState.CleanupPending && DetachDevice();
    public void Dispose() => _disposed = true;

    private void EnsureNotDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CanonicalDualSenseEdgeSession));
    }
}

internal interface ICanonicalDualSenseEdgeStateSink
{
    bool SetState(DSDeviceState state);
}

internal interface ICanonicalDualSenseEdgeSession : ICanonicalDualSenseEdgeStateSink, IDisposable
{
    CanonicalDualSenseEdgeSessionState State { get; }
    bool Start();
    bool SetNeutral();
    bool SetOutputCallback(DualSenseOutputCallback callback);
    bool ClearOutputCallback();
    bool DetachDevice();
    bool RetryPendingCleanup();
    bool TryGetTrackedAttachmentState(out USBDeviceAttachmentState state);
}
