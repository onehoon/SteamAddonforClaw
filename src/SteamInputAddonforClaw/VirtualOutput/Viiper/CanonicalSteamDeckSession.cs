using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal enum CanonicalSteamDeckSessionState
{
    Clean,
    Active,
    DeviceRemoved,
    CleanupPending,
    Unsafe
}

/// <summary>
/// Steam Deck counterpart to <see cref="CanonicalSteamControllerSession"/>: same long-lived
/// canonical USB server / caller-owned bus / tracked-attachment lifecycle, driving VIIPER's typed
/// Steam Deck ABI (VIIPER main@ec64282c69e5587466b950332d7983fd53a7d778, PR #16) instead of Gordon.
/// This is a deliberate parallel implementation, not a shared abstraction with Gordon's session --
/// see docs/VIIPER_MIGRATION_TODO.md SD2.
/// </summary>
internal sealed class CanonicalSteamDeckSession : ICanonicalSteamDeckSession
{
    private const string LoopbackAddress = "127.0.0.1:3242";
    private readonly ICanonicalViiperNativeApi _native;
    private bool _disposed;
    private nuint _serverHandle;
    private uint _busId;
    private bool _busOwned;
    private nuint _deviceHandle;
    private uint? _logicalDeviceId;

    internal CanonicalSteamDeckSession(ICanonicalViiperNativeApi native)
    {
        _native = native;
    }

    internal CanonicalSteamDeckSessionState State { get; private set; } = CanonicalSteamDeckSessionState.Clean;
    internal CanonicalPendingCleanupPhase PendingCleanupPhase { get; private set; }
    internal nuint ServerHandle => _serverHandle;
    internal uint? BusId => _busOwned ? _busId : null;
    internal nuint DeviceHandle => _deviceHandle;
    internal uint? LogicalDeviceId => _logicalDeviceId;

    CanonicalSteamDeckSessionState ICanonicalSteamDeckSession.State => State;
    CanonicalPendingCleanupPhase ICanonicalSteamDeckSession.PendingCleanupPhase => PendingCleanupPhase;
    uint? ICanonicalSteamDeckSession.BusId => BusId;
    uint? ICanonicalSteamDeckSession.LogicalDeviceId => LogicalDeviceId;
    bool ICanonicalSteamDeckSession.Start() => Start();
    bool ICanonicalSteamDeckSession.SetNeutral() => SetNeutral();
    bool ICanonicalSteamDeckSession.RemoveDevice() => RemoveDevice();
    bool ICanonicalSteamDeckSession.RetryPendingCleanup() => RetryPendingCleanup();
    bool ICanonicalSteamDeckSession.CompleteRuntimeCleanup() => CompleteRuntimeCleanup();

    internal bool Start()
    {
        EnsureNotDisposed();
        if (State != CanonicalSteamDeckSessionState.Clean) return false;

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

            if (!_native.NewUSBServer(ref config, out _serverHandle, CanonicalViiperDiagnosticLog.Callback))
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

        // Zero VID/PID -> VIIPER's Steam Deck default identity 28DE:1205.
        if (!_native.CreateSteamDeckDevice(_serverHandle, out _deviceHandle, _busId, false, 0, 0))
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
            State = CanonicalSteamDeckSessionState.Unsafe;
            PendingCleanupPhase = CanonicalPendingCleanupPhase.None;
            return false;
        }

        State = CanonicalSteamDeckSessionState.Active;
        PendingCleanupPhase = CanonicalPendingCleanupPhase.None;
        return true;
    }

    public bool SetState(SteamDeckDeviceState state)
    {
        EnsureNotDisposed();
        return State == CanonicalSteamDeckSessionState.Active && _deviceHandle != 0 &&
            _native.SetSteamDeckDeviceState(_deviceHandle, state);
    }

    internal bool SetNeutral() => SetState(default);

    internal bool RemoveDevice()
    {
        EnsureNotDisposed();
        if (State != CanonicalSteamDeckSessionState.Active || _deviceHandle == 0) return false;
        return ApplyDeviceRemovalResult(_native.RemoveSteamDeckDeviceEx(_deviceHandle));
    }

    internal bool CompleteRuntimeCleanup()
    {
        EnsureNotDisposed();
        if (State != CanonicalSteamDeckSessionState.DeviceRemoved || PendingCleanupPhase != CanonicalPendingCleanupPhase.None)
            return false;

        PendingCleanupPhase = CanonicalPendingCleanupPhase.BusRemoval;
        State = CanonicalSteamDeckSessionState.CleanupPending;
        return ContinuePendingCleanup();
    }

    internal bool RetryPendingCleanup()
    {
        EnsureNotDisposed();
        if (State != CanonicalSteamDeckSessionState.CleanupPending)
            return false;

        return ContinuePendingCleanup();
    }

    private bool ContinuePendingCleanup()
    {
        if (PendingCleanupPhase == CanonicalPendingCleanupPhase.DeviceRemoval)
        {
            if (_deviceHandle == 0) return false;
            return ApplyDeviceRemovalResult(_native.RemoveSteamDeckDeviceEx(_deviceHandle));
        }

        if (PendingCleanupPhase == CanonicalPendingCleanupPhase.BusRemoval)
        {
            if (_serverHandle == 0 || !_native.RemoveUSBBus(_serverHandle, _busId))
            {
                State = CanonicalSteamDeckSessionState.CleanupPending;
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
                State = CanonicalSteamDeckSessionState.CleanupPending;
                return false;
            }
            _serverHandle = 0;
            _logicalDeviceId = null;
            PendingCleanupPhase = CanonicalPendingCleanupPhase.None;
            State = CanonicalSteamDeckSessionState.Clean;
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (State != CanonicalSteamDeckSessionState.Clean)
        {
            AppLog.Error("SteamOutput", "Canonical VIIPER Steam Deck session disposed with native ownership remaining.",
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

        var result = _native.RemoveSteamDeckDeviceEx(_deviceHandle);
        if (result != SteamDeckDeviceRemoveResult.Success)
        {
            if (result == SteamDeckDeviceRemoveResult.RetryableFailure)
                SetPending(CanonicalPendingCleanupPhase.DeviceRemoval);
            else
                SetUnsafe();
            return;
        }
        ClearDeviceOwnership();
        FailCreationAfterBus();
    }

    private bool ApplyDeviceRemovalResult(SteamDeckDeviceRemoveResult result)
    {
        switch (result)
        {
            case SteamDeckDeviceRemoveResult.Success:
                ClearDeviceOwnership();
                PendingCleanupPhase = CanonicalPendingCleanupPhase.None;
                State = CanonicalSteamDeckSessionState.DeviceRemoved;
                return true;
            case SteamDeckDeviceRemoveResult.RetryableFailure:
                SetPending(CanonicalPendingCleanupPhase.DeviceRemoval);
                return false;
            case SteamDeckDeviceRemoveResult.UnsafeOutcomeUnknown:
            case SteamDeckDeviceRemoveResult.Invalid:
            default:
                SetUnsafe();
                return false;
        }
    }

    private void SetUnsafe()
    {
        PendingCleanupPhase = CanonicalPendingCleanupPhase.None;
        State = CanonicalSteamDeckSessionState.Unsafe;
    }

    private void SetPending(CanonicalPendingCleanupPhase phase)
    {
        PendingCleanupPhase = phase;
        State = CanonicalSteamDeckSessionState.CleanupPending;
    }

    private void ClearDeviceOwnership()
    {
        _deviceHandle = 0;
        _logicalDeviceId = null;
    }

    private void EnsureNotDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CanonicalSteamDeckSession));
    }
}

internal interface ICanonicalSteamDeckStateSink
{
    bool SetState(SteamDeckDeviceState state);
}

internal interface ICanonicalSteamDeckSession : ICanonicalSteamDeckStateSink, IDisposable
{
    CanonicalSteamDeckSessionState State { get; }
    CanonicalPendingCleanupPhase PendingCleanupPhase { get; }
    uint? BusId { get; }
    uint? LogicalDeviceId { get; }
    bool Start();
    bool SetNeutral();
    bool RemoveDevice();
    bool RetryPendingCleanup();
    bool CompleteRuntimeCleanup();
}
