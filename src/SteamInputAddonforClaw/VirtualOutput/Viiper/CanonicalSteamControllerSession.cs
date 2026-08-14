using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal enum CanonicalSteamControllerSessionState
{
    Clean,
    Active,
    DeviceRemoved,
    CleanupPending,
    Unsafe
}

internal enum CanonicalPendingCleanupPhase
{
    None,
    DeviceRemoval,
    BusRemoval,
    ServerClose
}

internal sealed class CanonicalSteamControllerSession : ICanonicalSteamControllerSession
{
    private const string LoopbackAddress = "127.0.0.1:3241";
    private readonly ICanonicalViiperNativeApi _native;
    private bool _disposed;
    private nuint _serverHandle;
    private uint _busId;
    private bool _busOwned;
    private nuint _deviceHandle;
    private uint? _logicalDeviceId;

    internal CanonicalSteamControllerSession(ICanonicalViiperNativeApi native)
    {
        _native = native;
    }

    internal CanonicalSteamControllerSessionState State { get; private set; } = CanonicalSteamControllerSessionState.Clean;
    internal CanonicalPendingCleanupPhase PendingCleanupPhase { get; private set; }
    internal nuint ServerHandle => _serverHandle;
    internal uint? BusId => _busOwned ? _busId : null;
    internal nuint DeviceHandle => _deviceHandle;
    internal uint? LogicalDeviceId => _logicalDeviceId;

    CanonicalSteamControllerSessionState ICanonicalSteamControllerSession.State => State;
    CanonicalPendingCleanupPhase ICanonicalSteamControllerSession.PendingCleanupPhase => PendingCleanupPhase;
    uint? ICanonicalSteamControllerSession.BusId => BusId;
    uint? ICanonicalSteamControllerSession.LogicalDeviceId => LogicalDeviceId;
    bool ICanonicalSteamControllerSession.Start() => Start();
    bool ICanonicalSteamControllerSession.SetNeutral() => SetNeutral();
    bool ICanonicalSteamControllerSession.RemoveDevice() => RemoveDevice();
    bool ICanonicalSteamControllerSession.RetryPendingCleanup() => RetryPendingCleanup();
    bool ICanonicalSteamControllerSession.CompleteRuntimeCleanup() => CompleteRuntimeCleanup();

    internal bool Start()
    {
        EnsureNotDisposed();
        if (State != CanonicalSteamControllerSessionState.Clean) return false;

        var address = Marshal.StringToCoTaskMemUTF8(LoopbackAddress);
        try
        {
            var config = new USBServerConfig
            {
                Addr = address,
                ConnectionTimeoutMs = 30_000,
                DeviceHandlerConnectTimeoutMs = 5_000,
                WriteBatchFlushIntervalMs = 1
            };

            if (!_native.NewUSBServer(ref config, out _serverHandle))
            {
                _serverHandle = 0;
                return false;
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(address);
        }

        _busId = 0;
        if (!_native.CreateUSBBus(_serverHandle, ref _busId))
        {
            FailCreationAfterServer();
            return false;
        }
        _busOwned = true;

        if (!_native.CreateSteamControllerDevice(_serverHandle, out _deviceHandle, _busId, false, 0x28DE, 0x1102))
        {
            _deviceHandle = 0;
            FailCreationAfterBus();
            return false;
        }

        if (!_native.GetUSBDeviceIdentity(_deviceHandle, out var identityBusId, out var logicalDeviceId) || identityBusId != _busId)
        {
            FailCreationWithUnattachedDevice();
            return false;
        }

        _logicalDeviceId = logicalDeviceId;
        if (!_native.AttachUSBDevice(_deviceHandle))
        {
            State = CanonicalSteamControllerSessionState.Unsafe;
            PendingCleanupPhase = CanonicalPendingCleanupPhase.None;
            return false;
        }

        State = CanonicalSteamControllerSessionState.Active;
        PendingCleanupPhase = CanonicalPendingCleanupPhase.None;
        return true;
    }

    public bool SetState(SteamControllerDeviceState state)
    {
        EnsureNotDisposed();
        return State == CanonicalSteamControllerSessionState.Active && _deviceHandle != 0 &&
            _native.SetSteamControllerDeviceState(_deviceHandle, state);
    }

    internal bool SetNeutral() => SetState(default);

    internal bool RemoveDevice()
    {
        EnsureNotDisposed();
        if (State != CanonicalSteamControllerSessionState.Active || _deviceHandle == 0) return false;
        return ApplyDeviceRemovalResult(_native.RemoveSteamControllerDeviceEx(_deviceHandle));
    }

    internal bool CompleteRuntimeCleanup()
    {
        EnsureNotDisposed();
        if (State != CanonicalSteamControllerSessionState.DeviceRemoved || PendingCleanupPhase != CanonicalPendingCleanupPhase.None)
            return false;

        PendingCleanupPhase = CanonicalPendingCleanupPhase.BusRemoval;
        State = CanonicalSteamControllerSessionState.CleanupPending;
        return ContinuePendingCleanup();
    }

    internal bool RetryPendingCleanup()
    {
        EnsureNotDisposed();
        if (State != CanonicalSteamControllerSessionState.CleanupPending)
            return false;

        return ContinuePendingCleanup();
    }

    private bool ContinuePendingCleanup()
    {
        if (PendingCleanupPhase == CanonicalPendingCleanupPhase.DeviceRemoval)
        {
            if (_deviceHandle == 0) return false;
            return ApplyDeviceRemovalResult(_native.RemoveSteamControllerDeviceEx(_deviceHandle));
        }

        if (PendingCleanupPhase == CanonicalPendingCleanupPhase.BusRemoval)
        {
            if (_serverHandle == 0 || !_native.RemoveUSBBus(_serverHandle, _busId))
            {
                State = CanonicalSteamControllerSessionState.CleanupPending;
                return false;
            }
            _busId = 0;
            _busOwned = false;
            PendingCleanupPhase = CanonicalPendingCleanupPhase.ServerClose;
        }

        if (PendingCleanupPhase == CanonicalPendingCleanupPhase.ServerClose)
        {
            if (_serverHandle == 0 || !_native.CloseUSBServer(_serverHandle))
            {
                State = CanonicalSteamControllerSessionState.CleanupPending;
                return false;
            }
            _serverHandle = 0;
            _logicalDeviceId = null;
            PendingCleanupPhase = CanonicalPendingCleanupPhase.None;
            State = CanonicalSteamControllerSessionState.Clean;
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (State != CanonicalSteamControllerSessionState.Clean)
        {
            AppLog.Error("SteamOutput", "Canonical VIIPER session disposed with native ownership remaining.",
                new InvalidOperationException("Canonical session disposal is non-destructive."),
                ("State", State), ("PendingCleanupPhase", PendingCleanupPhase),
                ("ServerHandleOwned", _serverHandle != 0), ("BusOwned", _busOwned),
                ("DeviceHandleOwned", _deviceHandle != 0));
        }
    }

    private void FailCreationAfterServer()
    {
        if (_serverHandle == 0) return;
        if (_native.CloseUSBServer(_serverHandle)) _serverHandle = 0;
        else SetPending(CanonicalPendingCleanupPhase.ServerClose);
    }

    private void FailCreationAfterBus()
    {
        if (_serverHandle == 0) return;
        if (!_native.RemoveUSBBus(_serverHandle, _busId))
        {
            SetPending(CanonicalPendingCleanupPhase.BusRemoval);
            return;
        }
        _busId = 0;
        _busOwned = false;
        if (_native.CloseUSBServer(_serverHandle)) _serverHandle = 0;
        else SetPending(CanonicalPendingCleanupPhase.ServerClose);
    }

    private void FailCreationWithUnattachedDevice()
    {
        if (_deviceHandle == 0)
        {
            FailCreationAfterBus();
            return;
        }

        var result = _native.RemoveSteamControllerDeviceEx(_deviceHandle);
        if (result != SteamControllerDeviceRemoveResult.Success)
        {
            if (result == SteamControllerDeviceRemoveResult.RetryableFailure)
                SetPending(CanonicalPendingCleanupPhase.DeviceRemoval);
            else
                SetUnsafe();
            return;
        }
        ClearDeviceOwnership();
        FailCreationAfterBus();
    }

    private bool ApplyDeviceRemovalResult(SteamControllerDeviceRemoveResult result)
    {
        switch (result)
        {
            case SteamControllerDeviceRemoveResult.Success:
                ClearDeviceOwnership();
                PendingCleanupPhase = CanonicalPendingCleanupPhase.None;
                State = CanonicalSteamControllerSessionState.DeviceRemoved;
                return true;
            case SteamControllerDeviceRemoveResult.RetryableFailure:
                SetPending(CanonicalPendingCleanupPhase.DeviceRemoval);
                return false;
            case SteamControllerDeviceRemoveResult.UnsafeOutcomeUnknown:
            case SteamControllerDeviceRemoveResult.Invalid:
            default:
                SetUnsafe();
                return false;
        }
    }

    private void SetUnsafe()
    {
        PendingCleanupPhase = CanonicalPendingCleanupPhase.None;
        State = CanonicalSteamControllerSessionState.Unsafe;
    }

    private void SetPending(CanonicalPendingCleanupPhase phase)
    {
        PendingCleanupPhase = phase;
        State = CanonicalSteamControllerSessionState.CleanupPending;
    }

    private void ClearDeviceOwnership()
    {
        _deviceHandle = 0;
        _logicalDeviceId = null;
    }

    private void EnsureNotDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CanonicalSteamControllerSession));
    }
}

internal interface ICanonicalSteamControllerStateSink
{
    bool SetState(SteamControllerDeviceState state);
}

internal interface ICanonicalSteamControllerSession : ICanonicalSteamControllerStateSink, IDisposable
{
    CanonicalSteamControllerSessionState State { get; }
    CanonicalPendingCleanupPhase PendingCleanupPhase { get; }
    uint? BusId { get; }
    uint? LogicalDeviceId { get; }
    bool Start();
    bool SetNeutral();
    bool RemoveDevice();
    bool RetryPendingCleanup();
    bool CompleteRuntimeCleanup();
}
