using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.Overlay;

namespace SteamInputAddonforClaw.FrontendTransport;

internal static class OverlayTransportProtocol
{
    // Version 3 (OQ4): adds the Runtime -> Overlay Navigation message and OverlayNavigationAction.
    // Semantic, edge-driven navigation only -- no ControllerState / buttons / sticks / raw reports
    // ever cross this wire. A v2 peer must fail the handshake rather than silently ignore Navigation.
    // Version 4 (OQ5-UI-02): adds PreviousTab / NextTab semantic actions for LB/RB tab navigation.
    // Version 5 (OQ5-UI-09): adds TabOrderState (Runtime -> Overlay) and SetTabOrder (Overlay ->
    // Runtime) carrying the shared OverlayTabId list. The Runtime sends the authoritative order right
    // after HandshakeAccepted and the Overlay must apply it before it reports Ready. No fallback --
    // a v4 peer must fail the handshake.
    // Version 6 (SF-V2-02): adds typed Device Quick Settings state delivery
    // (DeviceQuickSettingsState) and the explicit CPU Boost/TDP/Power Mode mutation
    // request/result messages (DeviceMutationRequest / DeviceMutationResult). A v5 peer must fail
    // the handshake rather than silently miss the new frames.
    internal const int CurrentVersion = 6;
    internal const int MaxFrameBytes = 64 * 1024;
}

internal enum OverlayWireMessageKind { Handshake, HandshakeAccepted, Command, Navigation, State, DismissRequested, ProtocolError, TabOrderState, SetTabOrder, DeviceQuickSettingsState, DeviceMutationRequest, DeviceMutationResult }
internal enum OverlayCommand { Show, Hide, Shutdown }
internal enum OverlayNavigationAction { NavigateUp, NavigateDown, NavigateLeft, NavigateRight, Accept, Back, PreviousTab, NextTab }
internal enum OverlayState { Ready, Visible, Hidden }

/// <summary>The eight Device/global mutations approved for the Overlay transport (SF-V2-02 section
/// 9). Transport-specific wire intent only -- not a feature registry, and not exhaustive of every
/// CPU Boost/TDP/Power Mode operation <see cref="IAddonFrontendControl"/> exposes elsewhere.</summary>
internal enum OverlayDeviceMutationKind
{
    SetCpuBoostEnabled,
    SetCpuBoostAc,
    SetCpuBoostDc,
    SetTdpEnabled,
    SetTdp,
    SetPowerModeEnabled,
    SetPowerModeAc,
    SetPowerModeDc,
}

/// <summary>One Overlay -> Runtime Device mutation request. Exactly one of the value fields is
/// populated, matching <see cref="Kind"/> -- <see cref="OverlayWireValidation.IsValidDeviceMutationRequest"/>
/// enforces the shape before any Runtime method is invoked.</summary>
internal sealed record OverlayDeviceMutationRequest(
    long RequestId,
    OverlayDeviceMutationKind Kind,
    bool? Enabled = null,
    CpuBoostMode? CpuBoostMode = null,
    FrontendTdpConfiguration? TdpConfiguration = null,
    WindowsPowerMode? PowerMode = null);

/// <summary>The Runtime -> Overlay reply to one <see cref="OverlayDeviceMutationRequest"/>. A typed
/// feature failure (PersistenceFailed/ApplyFailed/InvalidTarget/Unavailable) is a normal result in
/// one of the three typed fields, not a transport failure. <see cref="Error"/> is reserved for a
/// thrown operation/transport-side failure or an unadmitted request -- never a second copy of the
/// frontend mutation outcome enums.</summary>
internal sealed record OverlayDeviceMutationResponse(
    long RequestId,
    OverlayDeviceMutationKind Kind,
    FrontendCpuBoostMutationResult? CpuBoost = null,
    FrontendTdpMutationResult? Tdp = null,
    FrontendPowerModeMutationResult? PowerMode = null,
    string? Error = null);

internal sealed record OverlayWireMessage(
    int ProtocolVersion,
    OverlayWireMessageKind Kind,
    OverlayCommand? Command = null,
    OverlayNavigationAction? Navigation = null,
    OverlayState? State = null,
    string? Error = null,
    IReadOnlyList<OverlayTabId>? TabOrder = null,
    FrontendDeviceQuickSettingsSnapshot? DeviceState = null,
    OverlayDeviceMutationRequest? DeviceMutationRequest = null,
    OverlayDeviceMutationResponse? DeviceMutationResponse = null);

/// <summary>Strict Overlay Device mutation request-shape validation (SF-V2-02 section 10.1): each
/// mutation kind accepts exactly one matching value field and rejects every other combination
/// before any Runtime method is invoked. Kept separate from feature outcome construction so the
/// same shape rule is not duplicated between the server dispatcher and any future caller.</summary>
internal static class OverlayWireValidation
{
    internal static bool IsValidDeviceMutationRequest(OverlayDeviceMutationRequest request) => request.RequestId > 0 && request.Kind switch
    {
        OverlayDeviceMutationKind.SetCpuBoostEnabled or OverlayDeviceMutationKind.SetTdpEnabled or OverlayDeviceMutationKind.SetPowerModeEnabled =>
            request.Enabled is not null && request.CpuBoostMode is null && request.TdpConfiguration is null && request.PowerMode is null,
        OverlayDeviceMutationKind.SetCpuBoostAc or OverlayDeviceMutationKind.SetCpuBoostDc =>
            request.CpuBoostMode is not null && request.Enabled is null && request.TdpConfiguration is null && request.PowerMode is null,
        OverlayDeviceMutationKind.SetTdp =>
            request.TdpConfiguration is not null && request.Enabled is null && request.CpuBoostMode is null && request.PowerMode is null,
        OverlayDeviceMutationKind.SetPowerModeAc or OverlayDeviceMutationKind.SetPowerModeDc =>
            request.PowerMode is not null && request.Enabled is null && request.CpuBoostMode is null && request.TdpConfiguration is null,
        _ => false,
    };

    /// <summary>The narrow "this Device mutation is not currently admitted" reply (SF-V2-02 section
    /// 15): reuses the existing typed feature outcome shapes -- never a second copy of the frontend
    /// mutation outcome enums -- with zero Runtime invocation.</summary>
    internal static OverlayDeviceMutationResponse NotAdmitted(OverlayDeviceMutationRequest request, string message) => request.Kind switch
    {
        OverlayDeviceMutationKind.SetCpuBoostEnabled or OverlayDeviceMutationKind.SetCpuBoostAc or OverlayDeviceMutationKind.SetCpuBoostDc =>
            new(request.RequestId, request.Kind, CpuBoost: new FrontendCpuBoostMutationResult(FrontendCpuBoostMutationOutcome.PersistenceFailed, message, FrontendCpuBoostSnapshot.Unavailable)),
        OverlayDeviceMutationKind.SetTdpEnabled or OverlayDeviceMutationKind.SetTdp =>
            new(request.RequestId, request.Kind, Tdp: new FrontendTdpMutationResult(FrontendTdpMutationOutcome.Unavailable, message, FrontendTdpSnapshot.Unavailable)),
        _ =>
            new(request.RequestId, request.Kind, PowerMode: new FrontendPowerModeMutationResult(FrontendPowerModeMutationOutcome.PersistenceFailed, message, FrontendPowerModeSnapshot.Unavailable)),
    };
}

/// <summary>The SF-V2-02 section 9/14 mapping from one approved <see cref="OverlayDeviceMutationKind"/>
/// onto the exact matching <see cref="IAddonFrontendControl"/> method already used by Main UI/QAM --
/// kept independently testable from <c>AddonProcessHost</c>'s much heavier composition. Admission
/// (Ready/Visible/_overlayCaptureActive/shutdown) is decided by the caller before this runs; this
/// class only ever touches the eight approved methods, never the full frontend surface.</summary>
internal static class OverlayDeviceMutationDispatch
{
    /// <summary>Invokes exactly the one <see cref="IAddonFrontendControl"/> method matching
    /// <see cref="OverlayDeviceMutationRequest.Kind"/>. Does not itself catch a thrown Runtime
    /// exception (including <see cref="OperationCanceledException"/>) -- the caller decides how to
    /// log it and turn it into the response's narrow <see cref="OverlayDeviceMutationResponse.Error"/>
    /// field (SF-V2-02 section 20 "Runtime mutation throws").</summary>
    internal static async Task<OverlayDeviceMutationResponse> DispatchAsync(IAddonFrontendControl control, OverlayDeviceMutationRequest request, CancellationToken token) => request.Kind switch
    {
        OverlayDeviceMutationKind.SetCpuBoostEnabled => new(request.RequestId, request.Kind, CpuBoost: await control.SetDeviceCpuBoostEnabledAsync(request.Enabled!.Value, token).ConfigureAwait(false)),
        OverlayDeviceMutationKind.SetCpuBoostAc => new(request.RequestId, request.Kind, CpuBoost: await control.SetDeviceCpuBoostAcAsync(request.CpuBoostMode!.Value, token).ConfigureAwait(false)),
        OverlayDeviceMutationKind.SetCpuBoostDc => new(request.RequestId, request.Kind, CpuBoost: await control.SetDeviceCpuBoostDcAsync(request.CpuBoostMode!.Value, token).ConfigureAwait(false)),
        OverlayDeviceMutationKind.SetTdpEnabled => new(request.RequestId, request.Kind, Tdp: await control.SetDeviceTdpEnabledAsync(request.Enabled!.Value, token).ConfigureAwait(false)),
        OverlayDeviceMutationKind.SetTdp => new(request.RequestId, request.Kind, Tdp: await control.SetDeviceTdpAsync(request.TdpConfiguration!, token).ConfigureAwait(false)),
        OverlayDeviceMutationKind.SetPowerModeEnabled => new(request.RequestId, request.Kind, PowerMode: await control.SetDevicePowerModeEnabledAsync(request.Enabled!.Value, token).ConfigureAwait(false)),
        OverlayDeviceMutationKind.SetPowerModeAc => new(request.RequestId, request.Kind, PowerMode: await control.SetDevicePowerModeAcAsync(request.PowerMode!.Value, token).ConfigureAwait(false)),
        OverlayDeviceMutationKind.SetPowerModeDc => new(request.RequestId, request.Kind, PowerMode: await control.SetDevicePowerModeDcAsync(request.PowerMode!.Value, token).ConfigureAwait(false)),
        _ => new OverlayDeviceMutationResponse(request.RequestId, request.Kind, Error: "Unsupported Overlay Device mutation."),
    };
}

internal static class OverlayWireCodec
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    internal static async Task WriteAsync(Stream stream, OverlayWireMessage message, SemaphoreSlim gate, CancellationToken token)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        if (payload.Length is 0 or > OverlayTransportProtocol.MaxFrameBytes)
            throw new FrontendProtocolException("Invalid Overlay frame length.");

        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(prefix, token).ConfigureAwait(false);
            await stream.WriteAsync(payload, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    internal static async Task<OverlayWireMessage> ReadAsync(Stream stream, CancellationToken token)
    {
        var prefix = new byte[4];
        await ReadExactlyAsync(stream, prefix, token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length <= 0 || length > OverlayTransportProtocol.MaxFrameBytes)
            throw new FrontendProtocolException("Invalid Overlay frame length.");

        var payload = new byte[length];
        await ReadExactlyAsync(stream, payload, token).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<OverlayWireMessage>(payload, Json)
                ?? throw new FrontendProtocolException("Invalid Overlay JSON frame.");
        }
        catch (JsonException exception)
        {
            throw new FrontendProtocolException($"Invalid Overlay JSON frame: {exception.Message}");
        }
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> target, CancellationToken token)
    {
        var offset = 0;
        while (offset < target.Length)
        {
            var read = await stream.ReadAsync(target[offset..], token).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }
}

internal sealed class NamedPipeOverlayServer : IAsyncDisposable
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);
    private readonly string _pipeName;
    private readonly Func<NamedPipeServerStream> _pipeFactory;
    private readonly Func<IReadOnlyList<OverlayTabId>> _getTabOrder;
    private readonly Func<IReadOnlyList<OverlayTabId>, bool> _tryChangeTabOrder;
    // SF-V2-02: bound once by OverlayProcessController onto the ONE _frontendControl. Without a bind
    // (tests, no-authority contexts) every Device mutation request is answered "not admitted" and
    // invokes zero Runtime operations.
    private readonly Func<OverlayDeviceMutationRequest, CancellationToken, Task<OverlayDeviceMutationResponse>>? _mutateDevice;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _sync = new();
    private readonly TaskCompletionSource _serverReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _ready = NewSignal();
    private TaskCompletionSource? _acknowledgement;
    private NamedPipeServerStream? _activePipe;
    private Task? _acceptLoop;
    private bool _readyState;
    private OverlayState _state = OverlayState.Hidden;
    private int _started;
    private int _disposed;

    internal event Action<NamedPipeOverlayServer>? DismissRequested;

    internal NamedPipeOverlayServer(string pipeName,
        Func<IReadOnlyList<OverlayTabId>>? getTabOrder = null,
        Func<IReadOnlyList<OverlayTabId>, bool>? tryChangeTabOrder = null,
        Func<OverlayDeviceMutationRequest, CancellationToken, Task<OverlayDeviceMutationResponse>>? mutateDevice = null)
        : this(pipeName, () => new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly), getTabOrder, tryChangeTabOrder, mutateDevice) { }

    internal NamedPipeOverlayServer(string pipeName, Func<NamedPipeServerStream> pipeFactory,
        Func<IReadOnlyList<OverlayTabId>>? getTabOrder = null,
        Func<IReadOnlyList<OverlayTabId>, bool>? tryChangeTabOrder = null,
        Func<OverlayDeviceMutationRequest, CancellationToken, Task<OverlayDeviceMutationResponse>>? mutateDevice = null)
    {
        _pipeName = pipeName;
        _pipeFactory = pipeFactory;
        // OQ5-UI-09: the Runtime binds these onto the ONE StartupSettingsCoordinator. Without a bind
        // (tests, no-authority contexts) the server reports the frozen default and rejects mutations.
        _getTabOrder = getTabOrder ?? (() => OverlayTabOrderContract.DefaultOrder);
        _tryChangeTabOrder = tryChangeTabOrder ?? (_ => false);
        _mutateDevice = mutateDevice;
    }

    internal bool IsReady { get { lock (_sync) return _readyState; } }
    internal OverlayState State { get { lock (_sync) return _state; } }

    internal async Task StartAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Overlay server already started.");
        _acceptLoop = AcceptLoopAsync();
        await _serverReady.Task.WaitAsync(token).ConfigureAwait(false);
    }

    internal async Task<bool> WaitForReadyAsync(TimeSpan timeout, CancellationToken token = default)
    {
        try
        {
            await _ready.Task.WaitAsync(timeout, token).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException) { return false; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
    }

    internal async Task<bool> SendCommandAsync(OverlayCommand command, CancellationToken token = default)
    {
        await _commandGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!await WaitForReadyAsync(CommandTimeout, token).ConfigureAwait(false)) return false;
            NamedPipeServerStream pipe;
            TaskCompletionSource acknowledgement;
            lock (_sync)
            {
                pipe = _activePipe ?? throw new IOException("Overlay pipe is disconnected.");
                acknowledgement = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _acknowledgement = acknowledgement;
            }

            var expected = command switch
            {
                OverlayCommand.Show => OverlayState.Visible,
                OverlayCommand.Hide => OverlayState.Hidden,
                OverlayCommand.Shutdown => OverlayState.Hidden,
                _ => throw new ArgumentOutOfRangeException(nameof(command))
            };
            await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.Command, Command: command), _writeGate, token).ConfigureAwait(false);
            if (command == OverlayCommand.Shutdown) return true;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            await acknowledgement.Task.WaitAsync(CommandTimeout, linked.Token).ConfigureAwait(false);
            return State == expected;
        }
        catch (TimeoutException) { return false; }
        catch (IOException) { return false; }
        catch (ObjectDisposedException) { return false; }
        catch (OperationCanceledException) when (token.IsCancellationRequested || _lifetime.IsCancellationRequested) { return false; }
        finally { _commandGate.Release(); }
    }

    // OQ4: fire-and-forget semantic navigation. No acknowledgement round-trip, no queue, no retry.
    // Only delivered while the connection is Ready and the surface is Visible; uses the same server
    // write gate as commands so navigation and command frames cannot interleave bytes.
    internal async Task<bool> SendNavigationAsync(OverlayNavigationAction action, CancellationToken token = default)
    {
        NamedPipeServerStream? pipe;
        lock (_sync)
        {
            if (!_readyState || _state != OverlayState.Visible) return false;
            pipe = _activePipe;
        }
        if (pipe is null) return false;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.Navigation, Navigation: action), _writeGate, linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or FrontendProtocolException)
        {
            return false;
        }
    }

    // SF-V2-02 section 16.1/16.2: best-effort state publish, only delivered while the connection is
    // Ready and the surface is Visible -- re-checked here at write time so a caller that captured the
    // snapshot before the surface became hidden does not still push it to a hidden session (section
    // 17.2). Uses the same server write gate as commands/navigation/tab-order so frames never
    // interleave on the one byte stream.
    internal async Task<bool> SendDeviceQuickSettingsStateAsync(FrontendDeviceQuickSettingsSnapshot snapshot, CancellationToken token = default)
    {
        NamedPipeServerStream? pipe;
        lock (_sync)
        {
            if (!_readyState || _state != OverlayState.Visible) return false;
            pipe = _activePipe;
        }
        if (pipe is null) return false;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.DeviceQuickSettingsState, DeviceState: snapshot), _writeGate, linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or FrontendProtocolException)
        {
            return false;
        }
    }

    // OQ5-UI-09: read the current authoritative order through the shared contract (so a caller that
    // somehow holds a malformed value still puts a valid five-tab order on the wire) and send it on
    // the one instance write gate shared with SendCommandAsync / SendNavigationAsync.
    private async Task SendTabOrderStateAsync(Stream pipe, CancellationToken token)
    {
        IReadOnlyList<OverlayTabId> order;
        try { order = OverlayTabOrderContract.NormalizeOrDefault(_getTabOrder()); }
        catch { order = OverlayTabOrderContract.DefaultOrder; }
        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.TabOrderState, TabOrder: order),
            _writeGate, token).ConfigureAwait(false);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = _pipeFactory();
                _serverReady.TrySetResult();
            }
            catch (Exception exception)
            {
                _serverReady.TrySetException(exception);
                return;
            }

            Interlocked.Exchange(ref _activePipe, pipe);
            try
            {
                await pipe.WaitForConnectionAsync(_lifetime.Token).ConfigureAwait(false);
                await ServeAsync(pipe).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception) when (!_lifetime.IsCancellationRequested) { }
            finally
            {
                Interlocked.Exchange(ref _activePipe, null)?.Dispose();
                lock (_sync)
                {
                    _readyState = false;
                    _ready = NewSignal();
                    _acknowledgement?.TrySetCanceled();
                    _acknowledgement = null;
                }
                await pipe.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task ServeAsync(Stream pipe)
    {
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        // OQ5-UI-09 blocker fix: every Runtime -> Overlay write goes through the ONE instance
        // _writeGate, including handshake and the post-Ready TabOrderState replies. A second
        // per-connection semaphore would let a tab-order reply and a SendNavigationAsync/
        // SendCommandAsync write interleave 4-byte prefixes and payloads on the same byte stream.
        var hello = await OverlayWireCodec.ReadAsync(pipe, connection.Token).ConfigureAwait(false);
        if (hello.Kind != OverlayWireMessageKind.Handshake || hello.ProtocolVersion != OverlayTransportProtocol.CurrentVersion)
        {
            await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.ProtocolError, Error: "Overlay protocol version mismatch."), _writeGate, connection.Token).ConfigureAwait(false);
            return;
        }

        await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.HandshakeAccepted), _writeGate, connection.Token).ConfigureAwait(false);
        // OQ5-UI-09 section 6: the authoritative tab order goes out immediately after acceptance so
        // the client can apply it before it reports Ready -- the Runtime never Shows a Ready Overlay
        // that still has only the default local order.
        await SendTabOrderStateAsync(pipe, connection.Token).ConfigureAwait(false);
        while (!connection.IsCancellationRequested)
        {
            var message = await OverlayWireCodec.ReadAsync(pipe, connection.Token).ConfigureAwait(false);
            if (message.ProtocolVersion != OverlayTransportProtocol.CurrentVersion)
                throw new FrontendProtocolException("Invalid Overlay state message.");

            if (message.Kind == OverlayWireMessageKind.DismissRequested && message.Command is null && message.Navigation is null && message.State is null && message.Error is null && message.TabOrder is null && message.DeviceState is null && message.DeviceMutationRequest is null && message.DeviceMutationResponse is null)
            {
                DismissRequested?.Invoke(this);
                continue;
            }

            if (message.Kind == OverlayWireMessageKind.SetTabOrder)
            {
                if (message.TabOrder is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.DeviceState is not null || message.DeviceMutationRequest is not null || message.DeviceMutationResponse is not null)
                    throw new FrontendProtocolException("Invalid Overlay SetTabOrder message.");
                // The authoritative state reply IS the mutation result: accepted / rejected-no-op /
                // invalid / persistence-failure all resolve to "read the coordinator again and send
                // whatever it now holds". A malformed persist must not tear this connection down.
                try { _tryChangeTabOrder(message.TabOrder); }
                catch { /* feature-local: the coordinator keeps its previous authoritative order */ }
                await SendTabOrderStateAsync(pipe, connection.Token).ConfigureAwait(false);
                continue;
            }

            if (message.Kind == OverlayWireMessageKind.DeviceMutationRequest)
            {
                if (message.DeviceMutationRequest is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrder is not null || message.DeviceState is not null || message.DeviceMutationResponse is not null)
                    throw new FrontendProtocolException("Invalid Overlay Device mutation request.");
                var request = message.DeviceMutationRequest;
                if (!OverlayWireValidation.IsValidDeviceMutationRequest(request))
                    throw new FrontendProtocolException("Invalid Overlay Device mutation request shape.");
                // SF-V2-02 section 12 [CRITICAL]: a TDP mutation may await real hardware apply
                // completion. Handling it inline here would block this sole read loop and delay/break
                // a concurrent Hide/DismissRequested/SetTabOrder frame while the Overlay is modal.
                // Run it as one exception-contained fire-and-forget operation and resume reading
                // immediately; the response is written later through the shared _writeGate.
                _ = HandleDeviceMutationRequestAsync(pipe, request, connection.Token);
                continue;
            }

            if (message.Kind != OverlayWireMessageKind.State || message.State is null)
                throw new FrontendProtocolException("Invalid Overlay state message.");

            lock (_sync)
            {
                if (message.State == OverlayState.Ready)
                {
                    _state = OverlayState.Hidden;
                    _readyState = true;
                    _ready.TrySetResult();
                }
                else
                {
                    _state = message.State.Value;
                }
                if (message.State is OverlayState.Visible or OverlayState.Hidden)
                    _acknowledgement?.TrySetResult();
            }
        }
    }

    // SF-V2-02 section 12/15: runs OUTSIDE the ServeAsync read loop so a long TDP hardware-apply wait
    // never delays Hide/DismissRequested/SetTabOrder processing. Admission (Ready + Visible) is the
    // one transport-level fact this class owns; _mutateDevice (bound by AddonProcessHost) separately
    // checks _overlayCaptureActive/process-shutdown before touching any Runtime frontend method.
    // Exception-contained: nothing here may become an unobserved exception.
    private async Task HandleDeviceMutationRequestAsync(Stream pipe, OverlayDeviceMutationRequest request, CancellationToken token)
    {
        try
        {
            bool admitted;
            lock (_sync) admitted = _readyState && _state == OverlayState.Visible;
            var mutate = _mutateDevice;
            OverlayDeviceMutationResponse response;
            if (!admitted || mutate is null)
            {
                response = OverlayWireValidation.NotAdmitted(request, "The Overlay is not visible.");
            }
            else
            {
                try { response = await mutate(request, token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception) { response = new OverlayDeviceMutationResponse(request.RequestId, request.Kind, Error: "Overlay Device mutation failed."); }
            }
            await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.DeviceMutationResult, DeviceMutationResponse: response), _writeGate, token).ConfigureAwait(false);
        }
        catch { /* connection torn down or disposed while this ran -- there is nothing left to notify */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        Interlocked.Exchange(ref _activePipe, null)?.Dispose();
        if (_acceptLoop is not null) try { await _acceptLoop.ConfigureAwait(false); } catch { }
        _commandGate.Dispose();
        _writeGate.Dispose();
        _lifetime.Dispose();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class NamedPipeOverlayClient : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    // SF-V2-02 section 11: Device mutations are correlation-specific to this client only -- Show/
    // Hide/Navigation/State/TabOrder never gain a request id. Serializing sends through one gate is
    // an accepted foundation-level simplification; the id/pending-TCS pair is what actually prevents
    // a late, retired result from completing a newer request (section 23.9), not the gate itself.
    private readonly SemaphoreSlim _deviceMutationGate = new(1, 1);
    private readonly object _deviceMutationSync = new();
    private long _deviceRequestSequence;
    private long _pendingDeviceRequestId;
    private TaskCompletionSource<OverlayDeviceMutationResponse>? _pendingDeviceMutation;
    private NamedPipeClientStream? _pipe;
    private int _disposed;

    internal NamedPipeOverlayClient(string pipeName) => _pipeName = pipeName;

    internal async Task RunAsync(Func<OverlayCommand, Task> commandHandler, CancellationToken token = default)
        => await RunAsync(commandHandler, null, null, null, token).ConfigureAwait(false);

    internal async Task RunAsync(Func<OverlayCommand, Task> commandHandler, Func<OverlayNavigationAction, Task>? navigationHandler, CancellationToken token = default)
        => await RunAsync(commandHandler, navigationHandler, null, null, token).ConfigureAwait(false);

    internal async Task RunAsync(
        Func<OverlayCommand, Task> commandHandler,
        Func<OverlayNavigationAction, Task>? navigationHandler,
        Func<IReadOnlyList<OverlayTabId>, Task>? tabOrderHandler,
        CancellationToken token = default)
        => await RunAsync(commandHandler, navigationHandler, tabOrderHandler, null, token).ConfigureAwait(false);

    internal async Task RunAsync(
        Func<OverlayCommand, Task> commandHandler,
        Func<OverlayNavigationAction, Task>? navigationHandler,
        Func<IReadOnlyList<OverlayTabId>, Task>? tabOrderHandler,
        Func<FrontendDeviceQuickSettingsSnapshot, Task>? deviceQuickSettingsHandler,
        CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        _pipe = pipe;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await pipe.ConnectAsync(5000, linked.Token).ConfigureAwait(false);
        await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.Handshake), _writeGate, linked.Token).ConfigureAwait(false);
        var accepted = await OverlayWireCodec.ReadAsync(pipe, linked.Token).ConfigureAwait(false);
        if (accepted.Kind != OverlayWireMessageKind.HandshakeAccepted || accepted.ProtocolVersion != OverlayTransportProtocol.CurrentVersion)
            throw new FrontendProtocolException("Overlay handshake was rejected.");

        // OQ5-UI-09 section 6: apply the mandatory initial authoritative order BEFORE reporting Ready.
        var initial = await OverlayWireCodec.ReadAsync(pipe, linked.Token).ConfigureAwait(false);
        if (initial.ProtocolVersion != OverlayTransportProtocol.CurrentVersion || initial.Kind != OverlayWireMessageKind.TabOrderState)
            throw new FrontendProtocolException("Overlay did not receive an initial tab-order state.");
        var initialOrder = ValidateTabOrderMessage(initial);
        if (tabOrderHandler is not null)
            await tabOrderHandler(initialOrder).ConfigureAwait(false);

        await SendStateAsync(pipe, OverlayState.Ready, linked.Token).ConfigureAwait(false);
        while (!linked.IsCancellationRequested)
        {
            var message = await OverlayWireCodec.ReadAsync(pipe, linked.Token).ConfigureAwait(false);
            if (message.ProtocolVersion != OverlayTransportProtocol.CurrentVersion)
                throw new FrontendProtocolException("Invalid Overlay message.");
            if (message.Kind == OverlayWireMessageKind.TabOrderState)
            {
                var order = ValidateTabOrderMessage(message);
                if (tabOrderHandler is not null)
                    await tabOrderHandler(order).ConfigureAwait(false);
                continue;
            }
            if (message.Kind == OverlayWireMessageKind.Navigation)
            {
                if (message.Navigation is null || message.Command is not null || message.State is not null || message.Error is not null || message.TabOrder is not null)
                    throw new FrontendProtocolException("Invalid Overlay navigation message.");
                if (navigationHandler is not null)
                    await navigationHandler(message.Navigation.Value).ConfigureAwait(false);
                continue;
            }
            if (message.Kind == OverlayWireMessageKind.DeviceQuickSettingsState)
            {
                if (message.DeviceState is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrder is not null || message.DeviceMutationRequest is not null || message.DeviceMutationResponse is not null)
                    throw new FrontendProtocolException("Invalid Overlay Device state message.");
                if (deviceQuickSettingsHandler is not null)
                    await deviceQuickSettingsHandler(message.DeviceState).ConfigureAwait(false);
                continue;
            }
            if (message.Kind == OverlayWireMessageKind.DeviceMutationResult)
            {
                if (message.DeviceMutationResponse is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrder is not null || message.DeviceState is not null || message.DeviceMutationRequest is not null)
                    throw new FrontendProtocolException("Invalid Overlay Device mutation result.");
                // Section 23.9: only complete the CURRENT pending request. A late result whose id was
                // superseded by a newer send must never complete that newer request.
                TaskCompletionSource<OverlayDeviceMutationResponse>? pending;
                lock (_deviceMutationSync)
                    pending = message.DeviceMutationResponse.RequestId == _pendingDeviceRequestId ? _pendingDeviceMutation : null;
                pending?.TrySetResult(message.DeviceMutationResponse);
                continue;
            }
            if (message.Kind != OverlayWireMessageKind.Command || message.Command is null || message.Navigation is not null || message.TabOrder is not null)
                throw new FrontendProtocolException("Invalid Overlay command message.");
            await commandHandler(message.Command.Value).ConfigureAwait(false);
            if (message.Command == OverlayCommand.Show)
                await SendStateAsync(pipe, OverlayState.Visible, linked.Token).ConfigureAwait(false);
            else if (message.Command == OverlayCommand.Hide)
                await SendStateAsync(pipe, OverlayState.Hidden, linked.Token).ConfigureAwait(false);
            else
                return;
        }
    }

    private async Task SendStateAsync(Stream pipe, OverlayState state, CancellationToken token) =>
        await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.State, State: state), _writeGate, token).ConfigureAwait(false);

    private static IReadOnlyList<OverlayTabId> ValidateTabOrderMessage(OverlayWireMessage message)
    {
        if (message.TabOrder is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null)
            throw new FrontendProtocolException("Invalid Overlay tab-order message.");
        if (!OverlayTabOrderContract.TryNormalize(message.TabOrder, out var order))
            throw new FrontendProtocolException("Overlay tab-order state was not a complete five-tab order.");
        return order;
    }

    // OQ5-UI-09 section 8: the OQ5-UI-10 reorder-editor seam. true only means the request frame was
    // written; the authoritative result is the TabOrderState the Runtime republishes afterwards.
    internal async Task<bool> SendSetTabOrderAsync(IReadOnlyList<OverlayTabId> requested, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var pipe = _pipe;
        if (pipe is null) return false;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            await OverlayWireCodec.WriteAsync(pipe,
                new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.SetTabOrder, TabOrder: requested),
                _writeGate, linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or FrontendProtocolException)
        {
            return false;
        }
    }

    // SF-V2-02 sections 10/11/18.3: the eight approved Device mutations. Each returns the same typed
    // frontend result the desktop/QAM surfaces already use -- a typed feature failure is a normal
    // result here, not an exception. Serialized through _deviceMutationGate (section 11); the request
    // id/pending-TCS pair still governs correctness if a wait is abandoned mid-flight.
    internal Task<FrontendCpuBoostMutationResult> SendDeviceCpuBoostEnabledAsync(bool enabled, CancellationToken token = default) =>
        RequireCpuBoost(SendDeviceMutationAsync(OverlayDeviceMutationKind.SetCpuBoostEnabled, enabled: enabled, cpuBoostMode: null, tdpConfiguration: null, powerMode: null, token));
    internal Task<FrontendCpuBoostMutationResult> SendDeviceCpuBoostAcAsync(CpuBoostMode mode, CancellationToken token = default) =>
        RequireCpuBoost(SendDeviceMutationAsync(OverlayDeviceMutationKind.SetCpuBoostAc, enabled: null, cpuBoostMode: mode, tdpConfiguration: null, powerMode: null, token));
    internal Task<FrontendCpuBoostMutationResult> SendDeviceCpuBoostDcAsync(CpuBoostMode mode, CancellationToken token = default) =>
        RequireCpuBoost(SendDeviceMutationAsync(OverlayDeviceMutationKind.SetCpuBoostDc, enabled: null, cpuBoostMode: mode, tdpConfiguration: null, powerMode: null, token));
    internal Task<FrontendTdpMutationResult> SendDeviceTdpEnabledAsync(bool enabled, CancellationToken token = default) =>
        RequireTdp(SendDeviceMutationAsync(OverlayDeviceMutationKind.SetTdpEnabled, enabled: enabled, cpuBoostMode: null, tdpConfiguration: null, powerMode: null, token));
    internal Task<FrontendTdpMutationResult> SendDeviceTdpAsync(FrontendTdpConfiguration configuration, CancellationToken token = default) =>
        RequireTdp(SendDeviceMutationAsync(OverlayDeviceMutationKind.SetTdp, enabled: null, cpuBoostMode: null, tdpConfiguration: configuration, powerMode: null, token));
    internal Task<FrontendPowerModeMutationResult> SendDevicePowerModeEnabledAsync(bool enabled, CancellationToken token = default) =>
        RequirePowerMode(SendDeviceMutationAsync(OverlayDeviceMutationKind.SetPowerModeEnabled, enabled: enabled, cpuBoostMode: null, tdpConfiguration: null, powerMode: null, token));
    internal Task<FrontendPowerModeMutationResult> SendDevicePowerModeAcAsync(WindowsPowerMode mode, CancellationToken token = default) =>
        RequirePowerMode(SendDeviceMutationAsync(OverlayDeviceMutationKind.SetPowerModeAc, enabled: null, cpuBoostMode: null, tdpConfiguration: null, powerMode: mode, token));
    internal Task<FrontendPowerModeMutationResult> SendDevicePowerModeDcAsync(WindowsPowerMode mode, CancellationToken token = default) =>
        RequirePowerMode(SendDeviceMutationAsync(OverlayDeviceMutationKind.SetPowerModeDc, enabled: null, cpuBoostMode: null, tdpConfiguration: null, powerMode: mode, token));

    private static async Task<FrontendCpuBoostMutationResult> RequireCpuBoost(Task<OverlayDeviceMutationResponse> response)
    {
        var result = await response.ConfigureAwait(false);
        return result.CpuBoost ?? throw new FrontendProtocolException(result.Error ?? "Overlay Device mutation failed.");
    }

    private static async Task<FrontendTdpMutationResult> RequireTdp(Task<OverlayDeviceMutationResponse> response)
    {
        var result = await response.ConfigureAwait(false);
        return result.Tdp ?? throw new FrontendProtocolException(result.Error ?? "Overlay Device mutation failed.");
    }

    private static async Task<FrontendPowerModeMutationResult> RequirePowerMode(Task<OverlayDeviceMutationResponse> response)
    {
        var result = await response.ConfigureAwait(false);
        return result.PowerMode ?? throw new FrontendProtocolException(result.Error ?? "Overlay Device mutation failed.");
    }

    private async Task<OverlayDeviceMutationResponse> SendDeviceMutationAsync(
        OverlayDeviceMutationKind kind, bool? enabled, CpuBoostMode? cpuBoostMode,
        FrontendTdpConfiguration? tdpConfiguration, WindowsPowerMode? powerMode, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var pipe = _pipe ?? throw new IOException("Overlay pipe is not connected.");
        await _deviceMutationGate.WaitAsync(token).ConfigureAwait(false);
        var requestId = Interlocked.Increment(ref _deviceRequestSequence);
        try
        {
            var tcs = new TaskCompletionSource<OverlayDeviceMutationResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_deviceMutationSync) { _pendingDeviceRequestId = requestId; _pendingDeviceMutation = tcs; }
            var request = new OverlayDeviceMutationRequest(requestId, kind, enabled, cpuBoostMode, tdpConfiguration, powerMode);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            await OverlayWireCodec.WriteAsync(pipe,
                new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.DeviceMutationRequest, DeviceMutationRequest: request),
                _writeGate, linked.Token).ConfigureAwait(false);
            return await tcs.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            // A cancelled/abandoned wait must not let a later, unrelated result complete this same
            // slot -- only clear the pending fields if nothing newer already replaced them.
            lock (_deviceMutationSync) { if (_pendingDeviceRequestId == requestId) _pendingDeviceMutation = null; }
            _deviceMutationGate.Release();
        }
    }

    internal async Task SendDismissRequestedAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var pipe = _pipe ?? throw new IOException("Overlay pipe is not connected.");
        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.DismissRequested), _writeGate, token).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _lifetime.Cancel();
            _pipe?.Dispose();
            _writeGate.Dispose();
            _lifetime.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}
