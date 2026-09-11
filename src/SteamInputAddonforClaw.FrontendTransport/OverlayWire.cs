using System.Buffers.Binary;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    // Version 7 (SF-V2-06): replaces the pre-release v6 Device-specific Quick Settings state/mutation
    // wire (DeviceQuickSettingsState / DeviceMutationRequest / DeviceMutationResult,
    // OverlayDeviceMutationKind/Request/Response, OverlayDeviceMutationDispatch) with the shared
    // QuickSettingsPageSnapshot / QuickSettingsMutationIntent / QuickSettingsMutationResult contract
    // already consumed by .Frontend/.Qam (SF-V2-04/05), inside narrow transport correlation wrappers.
    // A v6 peer must fail the handshake rather than silently misinterpret the replaced frames.
    // Pre-release: no compatibility shim.
    internal const int CurrentVersion = 7;
    internal const int MaxFrameBytes = 64 * 1024;
}

internal enum OverlayWireMessageKind { Handshake, HandshakeAccepted, Command, Navigation, State, DismissRequested, ProtocolError, TabOrderState, SetTabOrder, QuickSettingsPageState, QuickSettingsMutationRequest, QuickSettingsMutationResult }
internal enum OverlayCommand { Show, Hide, Shutdown }
internal enum OverlayNavigationAction { NavigateUp, NavigateDown, NavigateLeft, NavigateRight, Accept, Back, PreviousTab, NextTab }
internal enum OverlayState { Ready, Visible, Hidden }

/// <summary>Overlay -> Runtime shared Quick Settings mutation request (SF-V2-06 section 9.2).
/// Correlation is transport-specific and deliberately not part of the shared product contract --
/// <see cref="Intent"/> is the exact same closed <see cref="QuickSettingsMutationIntent"/> QAM sends
/// over `.Qam`.</summary>
internal sealed record OverlayQuickSettingsMutationRequest(long RequestId, QuickSettingsMutationIntent Intent);

/// <summary>Runtime -> Overlay reply to one <see cref="OverlayQuickSettingsMutationRequest"/>. A
/// valid Runtime result -- including a normal typed feature failure -- arrives as <see cref="Result"/>;
/// <see cref="Error"/> is reserved for a thrown operation/transport-side failure, never a second copy
/// of <see cref="QuickSettingsMutationResult"/>'s own Succeeded/FailureMessage shape.</summary>
internal sealed record OverlayQuickSettingsMutationResponse(long RequestId, QuickSettingsMutationResult? Result = null, string? Error = null);

internal sealed record OverlayWireMessage(
    int ProtocolVersion,
    OverlayWireMessageKind Kind,
    OverlayCommand? Command = null,
    OverlayNavigationAction? Navigation = null,
    OverlayState? State = null,
    string? Error = null,
    IReadOnlyList<OverlayTabId>? TabOrder = null,
    QuickSettingsPageSnapshot? QuickSettingsPage = null,
    OverlayQuickSettingsMutationRequest? QuickSettingsMutationRequest = null,
    OverlayQuickSettingsMutationResponse? QuickSettingsMutationResponse = null);

/// <summary>SF-V2-06 section 10/11: the Overlay transport's own job is only wire-structural safety --
/// "is this a closed Quick Settings message that can be handed to the shared adapter without a
/// null-reference/enum-default hazard?" -- never product validation (row/value/TDP-group shape,
/// which/whether a row is mutable). That remains <c>QuickSettingsMutationAdapter</c>'s job.</summary>
internal static class OverlayQuickSettingsWireValidation
{
    /// <summary>Required-constructor enforcement (see <see cref="OverlayWireCodec.Json"/>) already
    /// proves every required member is PRESENT; it does not prove non-null collections/entries. This
    /// proves the narrow remaining structural safety before the request reaches
    /// <c>IAddonFrontendControl.MutateQuickSettingAsync</c>.</summary>
    internal static bool IsStructurallyValid(OverlayQuickSettingsMutationRequest? request) =>
        request is { RequestId: > 0, Intent: { } intent } &&
        Enum.IsDefined(intent.PageId) &&
        Enum.IsDefined(intent.EditedRowId) &&
        intent.Values is { } values &&
        values.All(IsStructurallyValid);

    private static bool IsStructurallyValid(QuickSettingsRowValue? entry) =>
        entry is { Value: { } value } &&
        Enum.IsDefined(entry.RowId) &&
        Enum.IsDefined(value.Kind) &&
        value.IsStructurallyValid;

    /// <summary>Narrow "this page/request is not currently admitted" result (SF-V2-06 section 16):
    /// reuses the shared <see cref="QuickSettingsMutationResult"/>/<see cref="QuickSettingsPageSnapshot"/>
    /// shapes -- never a second outcome enum -- with zero Runtime invocation.</summary>
    internal static QuickSettingsMutationResult NotAdmitted(QuickSettingsMutationIntent intent, string message) =>
        new(false, message, QuickSettingsPageSnapshot.Unavailable(intent.PageId, intent.AppId, "Quick Settings are unavailable for this Overlay session."));

    /// <summary>Section 11: the Overlay client must fail closed on a malformed outbound page frame
    /// rather than pass null collections into the future SF-V2-07 renderer. Narrow structural safety
    /// only -- not a re-run of the shared product projection's semantic validation.</summary>
    internal static bool IsStructurallyValid(QuickSettingsPageSnapshot? page) =>
        page is { Sections: { } sections, LinkedSliderConstraints: not null } &&
        sections.All(section => section is { Rows: not null } && section.Rows.All(row => row is not null));
}

internal static class OverlayWireCodec
{
    // SF-V2-06 section 10.1: required-constructor enforcement added alongside the existing strict
    // string enum converter. Without it, an omitted PageId/EditedRowId/RowId/Value.Kind would
    // silently default to enum member 0 (Device/DeviceTdpEnabled/Boolean) instead of failing closed.
    private static readonly JsonSerializerOptions Json = new()
    {
        RespectRequiredConstructorParameters = true,
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
    // SF-V2-02/06: bound once by OverlayProcessController onto the ONE _frontendControl. Without a
    // bind (tests, no-authority contexts) every Quick Settings mutation request is answered "not
    // admitted" and invokes zero Runtime operations. The delegate itself owns admission
    // (_overlayCaptureActive/shutdown/page-scope) -- this class only adds its own Ready/Visible check.
    private readonly Func<QuickSettingsMutationIntent, CancellationToken, Task<QuickSettingsMutationResult>>? _mutateQuickSettings;
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
        Func<QuickSettingsMutationIntent, CancellationToken, Task<QuickSettingsMutationResult>>? mutateQuickSettings = null)
        : this(pipeName, () => new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly), getTabOrder, tryChangeTabOrder, mutateQuickSettings) { }

    internal NamedPipeOverlayServer(string pipeName, Func<NamedPipeServerStream> pipeFactory,
        Func<IReadOnlyList<OverlayTabId>>? getTabOrder = null,
        Func<IReadOnlyList<OverlayTabId>, bool>? tryChangeTabOrder = null,
        Func<QuickSettingsMutationIntent, CancellationToken, Task<QuickSettingsMutationResult>>? mutateQuickSettings = null)
    {
        _pipeName = pipeName;
        _pipeFactory = pipeFactory;
        // OQ5-UI-09: the Runtime binds these onto the ONE StartupSettingsCoordinator. Without a bind
        // (tests, no-authority contexts) the server reports the frozen default and rejects mutations.
        _getTabOrder = getTabOrder ?? (() => OverlayTabOrderContract.DefaultOrder);
        _tryChangeTabOrder = tryChangeTabOrder ?? (_ => false);
        _mutateQuickSettings = mutateQuickSettings;
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
    internal async Task<bool> SendQuickSettingsPageStateAsync(QuickSettingsPageSnapshot page, CancellationToken token = default)
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
            await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.QuickSettingsPageState, QuickSettingsPage: page), _writeGate, linked.Token).ConfigureAwait(false);
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

            if (message.Kind == OverlayWireMessageKind.DismissRequested && message.Command is null && message.Navigation is null && message.State is null && message.Error is null && message.TabOrder is null && message.QuickSettingsPage is null && message.QuickSettingsMutationRequest is null && message.QuickSettingsMutationResponse is null)
            {
                DismissRequested?.Invoke(this);
                continue;
            }

            if (message.Kind == OverlayWireMessageKind.SetTabOrder)
            {
                if (message.TabOrder is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationRequest is not null || message.QuickSettingsMutationResponse is not null)
                    throw new FrontendProtocolException("Invalid Overlay SetTabOrder message.");
                // The authoritative state reply IS the mutation result: accepted / rejected-no-op /
                // invalid / persistence-failure all resolve to "read the coordinator again and send
                // whatever it now holds". A malformed persist must not tear this connection down.
                try { _tryChangeTabOrder(message.TabOrder); }
                catch { /* feature-local: the coordinator keeps its previous authoritative order */ }
                await SendTabOrderStateAsync(pipe, connection.Token).ConfigureAwait(false);
                continue;
            }

            if (message.Kind == OverlayWireMessageKind.QuickSettingsMutationRequest)
            {
                if (message.QuickSettingsMutationRequest is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrder is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationResponse is not null)
                    throw new FrontendProtocolException("Invalid Overlay Quick Settings mutation request.");
                var request = message.QuickSettingsMutationRequest;
                if (!OverlayQuickSettingsWireValidation.IsStructurallyValid(request))
                    throw new FrontendProtocolException("Invalid Overlay Quick Settings mutation request shape.");
                // SF-V2-02/06 [CRITICAL]: a generic Device intent can still reach SetDeviceTdpAsync,
                // which may await real hardware apply completion. Handling it inline here would block
                // this sole read loop and delay/break a concurrent Hide/DismissRequested/SetTabOrder
                // frame while the Overlay is modal. Run it as one exception-contained fire-and-forget
                // operation and resume reading immediately; the response is written later through the
                // shared _writeGate.
                _ = HandleQuickSettingsMutationRequestAsync(pipe, request, connection.Token);
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

    // SF-V2-02/06 section 12/15/17: runs OUTSIDE the ServeAsync read loop so a long TDP hardware-apply
    // wait never delays Hide/DismissRequested/SetTabOrder processing. Admission (Ready + Visible) is
    // the one transport-level fact this class owns; _mutateQuickSettings (bound by AddonProcessHost)
    // separately checks _overlayCaptureActive/process-shutdown/page-scope before touching
    // IAddonFrontendControl.MutateQuickSettingAsync. Exception-contained: nothing here may become an
    // unobserved exception.
    private async Task HandleQuickSettingsMutationRequestAsync(Stream pipe, OverlayQuickSettingsMutationRequest request, CancellationToken token)
    {
        try
        {
            bool admitted;
            lock (_sync) admitted = _readyState && _state == OverlayState.Visible;
            var mutate = _mutateQuickSettings;
            OverlayQuickSettingsMutationResponse response;
            if (!admitted || mutate is null)
            {
                response = new(request.RequestId, Result: OverlayQuickSettingsWireValidation.NotAdmitted(request.Intent, "The Overlay is not visible."));
            }
            else
            {
                try { response = new(request.RequestId, Result: await mutate(request.Intent, token).ConfigureAwait(false)); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception) { response = new(request.RequestId, Error: "Overlay Quick Settings mutation failed."); }
            }
            await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.QuickSettingsMutationResult, QuickSettingsMutationResponse: response), _writeGate, token).ConfigureAwait(false);
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
    // SF-V2-02/06 section 11/19.3: Quick Settings mutations are correlation-specific to this client
    // only -- Show/Hide/Navigation/State/TabOrder never gain a request id. Serializing sends through
    // one gate is an accepted foundation-level simplification; the id/pending-TCS pair is what
    // actually prevents a late, retired result from completing a newer request (section 23.9), not
    // the gate itself.
    private readonly SemaphoreSlim _quickSettingsMutationGate = new(1, 1);
    private readonly object _quickSettingsMutationSync = new();
    private long _quickSettingsRequestSequence;
    private long _pendingQuickSettingsRequestId;
    private TaskCompletionSource<OverlayQuickSettingsMutationResponse>? _pendingQuickSettingsMutation;
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
        Func<QuickSettingsPageSnapshot, Task>? quickSettingsPageHandler,
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
            if (message.Kind == OverlayWireMessageKind.QuickSettingsPageState)
            {
                if (message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrder is not null || message.QuickSettingsMutationRequest is not null || message.QuickSettingsMutationResponse is not null)
                    throw new FrontendProtocolException("Invalid Overlay Quick Settings page message.");
                // Section 11: fail closed on a malformed outbound page frame rather than pass null
                // collections into the future SF-V2-07 renderer.
                if (!OverlayQuickSettingsWireValidation.IsStructurallyValid(message.QuickSettingsPage))
                    throw new FrontendProtocolException("Invalid Overlay Quick Settings page shape.");
                if (quickSettingsPageHandler is not null)
                    await quickSettingsPageHandler(message.QuickSettingsPage!).ConfigureAwait(false);
                continue;
            }
            if (message.Kind == OverlayWireMessageKind.QuickSettingsMutationResult)
            {
                if (message.QuickSettingsMutationResponse is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrder is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationRequest is not null)
                    throw new FrontendProtocolException("Invalid Overlay Quick Settings mutation result.");
                // Section 23.9: only complete the CURRENT pending request. A late result whose id was
                // superseded by a newer send must never complete that newer request.
                TaskCompletionSource<OverlayQuickSettingsMutationResponse>? pending;
                lock (_quickSettingsMutationSync)
                    pending = message.QuickSettingsMutationResponse.RequestId == _pendingQuickSettingsRequestId ? _pendingQuickSettingsMutation : null;
                pending?.TrySetResult(message.QuickSettingsMutationResponse);
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

    // SF-V2-06: one narrow shared-product method carrying the exact closed QuickSettingsMutationIntent
    // contract QAM already sends over `.Qam`. A typed feature failure (Succeeded=false) is a normal
    // result here, not an exception. Serialized through _quickSettingsMutationGate (section 19.3); the
    // request id/pending-TCS pair still governs correctness if a wait is abandoned mid-flight.
    internal async Task<QuickSettingsMutationResult> SendQuickSettingsMutationAsync(QuickSettingsMutationIntent intent, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var pipe = _pipe ?? throw new IOException("Overlay pipe is not connected.");
        await _quickSettingsMutationGate.WaitAsync(token).ConfigureAwait(false);
        var requestId = Interlocked.Increment(ref _quickSettingsRequestSequence);
        try
        {
            var tcs = new TaskCompletionSource<OverlayQuickSettingsMutationResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_quickSettingsMutationSync) { _pendingQuickSettingsRequestId = requestId; _pendingQuickSettingsMutation = tcs; }
            var request = new OverlayQuickSettingsMutationRequest(requestId, intent);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            await OverlayWireCodec.WriteAsync(pipe,
                new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.QuickSettingsMutationRequest, QuickSettingsMutationRequest: request),
                _writeGate, linked.Token).ConfigureAwait(false);
            var response = await tcs.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            return response.Result ?? throw new FrontendProtocolException(response.Error ?? "Overlay Quick Settings mutation failed.");
        }
        finally
        {
            // A cancelled/abandoned wait must not let a later, unrelated result complete this same
            // slot -- only clear the pending fields if nothing newer already replaced them.
            lock (_quickSettingsMutationSync) { if (_pendingQuickSettingsRequestId == requestId) _pendingQuickSettingsMutation = null; }
            _quickSettingsMutationGate.Release();
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
