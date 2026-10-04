using System.IO.Pipes;
using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.FrontendTransport;

internal sealed class NamedPipeOverlayServer : IAsyncDisposable
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);
    private readonly string _pipeName;
    private readonly Func<NamedPipeServerStream> _pipeFactory;
    private readonly Func<CancellationToken, Task<AddonQuickSettingsTabOrderSnapshot>> _captureTabOrder;
    private readonly Func<AddonQuickSettingsTabOrderMoveIntent, CancellationToken, Task<AddonQuickSettingsTabOrderMutationResult>> _moveTabOrder;
    // SF-V2-02/06: bound once by OverlayProcessController onto the ONE _frontendControl. Without a
    // bind (tests, no-authority contexts) every Quick Settings mutation request is answered "not
    // admitted" and invokes zero Runtime operations. The delegate itself owns admission
    // (_overlayCaptureActive/shutdown/page-scope) -- this class only adds its own Ready/Visible check.
    private readonly Func<QuickSettingsMutationIntent, CancellationToken, Task<QuickSettingsMutationResult>>? _mutateQuickSettings;
    private readonly Func<CancellationToken, Task<FrontendClawHudSnapshot>> _captureClawHud;
    private readonly Func<bool, CancellationToken, Task<FrontendClawHudSnapshot>>? _setClawHudEnabled;
    private readonly Func<FrontendClawHudMutationIntent, CancellationToken, Task<FrontendClawHudMutationResult>>? _mutateClawHudSetting;
    private readonly Func<CancellationToken, Task<IReadOnlyList<FrontendProfileGameCatalogEntry>>> _scanProfileGames;
    private readonly Func<uint, CancellationToken, Task<QuickSettingsPageSnapshot>> _captureProfilePage;
    private readonly Func<CancellationToken, Task<FrontendShortcutDashboardSnapshot>>? _captureShortcut;
    private readonly Func<Guid, CancellationToken, Task<OverlayShortcutExecutionOutcome>>? _executeShortcut;
    private readonly Func<CancellationToken, Task<OverlayBackButtonMappingState>>? _captureBackButtonMapping;
    private readonly Func<BackButtonMappingSettings, CancellationToken, Task<OverlayBackButtonMappingMutationOutcome>>? _mutateBackButtonMapping;
    private readonly Func<OverlayFrontendSettingsMutationRequest, CancellationToken, Task<OverlayFrontendSettingsMutationResponse>>? _mutateFrontendSettings;
    private readonly Func<CancellationToken, Task<OverlayFrontendSettingsMutationResponse>>? _captureFrontendSettings;
    private readonly Func<CancellationToken, Task<FrontendControllerVibrationStrengthSnapshot>>? _captureControllerVibration;
    private readonly Func<OverlayControllerVibrationMutationRequest, CancellationToken, Task<FrontendControllerVibrationStrengthMutationResult>>? _mutateControllerVibration;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _sync = new();
    private readonly TaskCompletionSource _serverReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _ready = NewSignal();
    private TaskCompletionSource _disconnected = NewSignal();
    private TaskCompletionSource? _acknowledgement;
    private NamedPipeServerStream? _activePipe;
    private Task? _acceptLoop;
    private bool _readyState;
    private long _nextConnectionGeneration;
    private long _readyGeneration;
    private long _lastDisconnectedGeneration;
    private OverlayState _state = OverlayState.Hidden;
    private int _started;
    private int _disposed;

    internal event Action<NamedPipeOverlayServer>? DismissRequested;

    internal NamedPipeOverlayServer(string pipeName,
        Func<CancellationToken, Task<AddonQuickSettingsTabOrderSnapshot>>? captureTabOrder = null,
        Func<AddonQuickSettingsTabOrderMoveIntent, CancellationToken, Task<AddonQuickSettingsTabOrderMutationResult>>? moveTabOrder = null,
        Func<QuickSettingsMutationIntent, CancellationToken, Task<QuickSettingsMutationResult>>? mutateQuickSettings = null,
        Func<CancellationToken, Task<FrontendClawHudSnapshot>>? captureClawHud = null,
        Func<bool, CancellationToken, Task<FrontendClawHudSnapshot>>? setClawHudEnabled = null,
        Func<FrontendClawHudMutationIntent, CancellationToken, Task<FrontendClawHudMutationResult>>? mutateClawHudSetting = null,
        Func<CancellationToken, Task<IReadOnlyList<FrontendProfileGameCatalogEntry>>>? scanProfileGames = null,
        Func<uint, CancellationToken, Task<QuickSettingsPageSnapshot>>? captureProfilePage = null,
        Func<CancellationToken, Task<FrontendShortcutDashboardSnapshot>>? captureShortcut = null,
        Func<Guid, CancellationToken, Task<OverlayShortcutExecutionOutcome>>? executeShortcut = null,
        Func<CancellationToken, Task<OverlayBackButtonMappingState>>? captureBackButtonMapping = null,
        Func<BackButtonMappingSettings, CancellationToken, Task<OverlayBackButtonMappingMutationOutcome>>? mutateBackButtonMapping = null,
        Func<CancellationToken, Task<OverlayFrontendSettingsMutationResponse>>? captureFrontendSettings = null,
        Func<OverlayFrontendSettingsMutationRequest, CancellationToken, Task<OverlayFrontendSettingsMutationResponse>>? mutateFrontendSettings = null,
        Func<CancellationToken, Task<FrontendControllerVibrationStrengthSnapshot>>? captureControllerVibration = null,
        Func<OverlayControllerVibrationMutationRequest, CancellationToken, Task<FrontendControllerVibrationStrengthMutationResult>>? mutateControllerVibration = null)
        : this(pipeName, () => new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly), captureTabOrder, moveTabOrder, mutateQuickSettings,
            captureClawHud, setClawHudEnabled, mutateClawHudSetting, scanProfileGames, captureProfilePage, captureShortcut, executeShortcut,
            captureBackButtonMapping, mutateBackButtonMapping, captureFrontendSettings, mutateFrontendSettings,
            captureControllerVibration, mutateControllerVibration)
    { }

    internal NamedPipeOverlayServer(string pipeName, Func<NamedPipeServerStream> pipeFactory,
        Func<CancellationToken, Task<AddonQuickSettingsTabOrderSnapshot>>? captureTabOrder = null,
        Func<AddonQuickSettingsTabOrderMoveIntent, CancellationToken, Task<AddonQuickSettingsTabOrderMutationResult>>? moveTabOrder = null,
        Func<QuickSettingsMutationIntent, CancellationToken, Task<QuickSettingsMutationResult>>? mutateQuickSettings = null,
        Func<CancellationToken, Task<FrontendClawHudSnapshot>>? captureClawHud = null,
        Func<bool, CancellationToken, Task<FrontendClawHudSnapshot>>? setClawHudEnabled = null,
        Func<FrontendClawHudMutationIntent, CancellationToken, Task<FrontendClawHudMutationResult>>? mutateClawHudSetting = null,
        Func<CancellationToken, Task<IReadOnlyList<FrontendProfileGameCatalogEntry>>>? scanProfileGames = null,
        Func<uint, CancellationToken, Task<QuickSettingsPageSnapshot>>? captureProfilePage = null,
        Func<CancellationToken, Task<FrontendShortcutDashboardSnapshot>>? captureShortcut = null,
        Func<Guid, CancellationToken, Task<OverlayShortcutExecutionOutcome>>? executeShortcut = null,
        Func<CancellationToken, Task<OverlayBackButtonMappingState>>? captureBackButtonMapping = null,
        Func<BackButtonMappingSettings, CancellationToken, Task<OverlayBackButtonMappingMutationOutcome>>? mutateBackButtonMapping = null,
        Func<CancellationToken, Task<OverlayFrontendSettingsMutationResponse>>? captureFrontendSettings = null,
        Func<OverlayFrontendSettingsMutationRequest, CancellationToken, Task<OverlayFrontendSettingsMutationResponse>>? mutateFrontendSettings = null,
        Func<CancellationToken, Task<FrontendControllerVibrationStrengthSnapshot>>? captureControllerVibration = null,
        Func<OverlayControllerVibrationMutationRequest, CancellationToken, Task<FrontendControllerVibrationStrengthMutationResult>>? mutateControllerVibration = null)
    {
        _pipeName = pipeName;
        _pipeFactory = pipeFactory;
        _captureTabOrder = captureTabOrder ?? (_ => Task.FromResult(AddonQuickSettingsTabOrderSnapshot.Unavailable()));
        _moveTabOrder = moveTabOrder ?? ((_, _) => Task.FromResult(new AddonQuickSettingsTabOrderMutationResult(
            false, "Tab order is unavailable.", AddonQuickSettingsTabOrderSnapshot.Unavailable())));
        _mutateQuickSettings = mutateQuickSettings;
        _captureClawHud = captureClawHud ?? (_ => Task.FromResult(FrontendClawHudSnapshot.Unavailable(false, "HUD settings are unavailable.")));
        _setClawHudEnabled = setClawHudEnabled;
        _mutateClawHudSetting = mutateClawHudSetting;
        _scanProfileGames = scanProfileGames ?? (_ => Task.FromResult<IReadOnlyList<FrontendProfileGameCatalogEntry>>([]));
        _captureProfilePage = captureProfilePage ?? ((appId, _) => Task.FromResult(QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, appId, "Profile settings are unavailable.")));
        _captureShortcut = captureShortcut;
        _executeShortcut = executeShortcut;
        _captureBackButtonMapping = captureBackButtonMapping;
        _mutateBackButtonMapping = mutateBackButtonMapping;
        _captureFrontendSettings = captureFrontendSettings;
        _mutateFrontendSettings = mutateFrontendSettings;
        _captureControllerVibration = captureControllerVibration;
        _mutateControllerVibration = mutateControllerVibration;
    }

    internal bool IsReady { get { lock (_sync) return _readyState; } }
    internal long? ReadyGeneration { get { lock (_sync) return _readyState ? _readyGeneration : null; } }
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

    internal async Task<bool> WaitForDisconnectedAsync(long generation, TimeSpan timeout, CancellationToken token = default)
    {
        Task disconnected;
        lock (_sync)
        {
            if (_lastDisconnectedGeneration >= generation) return true;
            disconnected = _disconnected.Task;
        }

        try
        {
            await disconnected.WaitAsync(timeout, token).ConfigureAwait(false);
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

    internal async Task<bool> SendClawHudStateAsync(FrontendClawHudSnapshot state, CancellationToken token = default)
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
            await OverlayWireCodec.WriteAsync(pipe,
                new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.ClawHudState, ClawHudState: state),
                _writeGate, linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or FrontendProtocolException)
        {
            return false;
        }
    }

    internal async Task<bool> SendShortcutStateAsync(FrontendShortcutDashboardSnapshot state, CancellationToken token = default)
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
            await OverlayWireCodec.WriteAsync(pipe,
                OverlayShortcutWireValidation.CreateBoundedStateMessage(state), _writeGate, linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or FrontendProtocolException)
        {
            return false;
        }
    }

    internal async Task<bool> SendBackButtonMappingStateAsync(OverlayBackButtonMappingState state, CancellationToken token = default)
    {
        if (!OverlayBackButtonMappingWireValidation.IsStructurallyValid(state))
            throw new FrontendProtocolException("Invalid Overlay M1/M2 mapping state.");

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
            await OverlayWireCodec.WriteAsync(pipe,
                new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.BackButtonMappingState,
                    BackButtonMappingState: state),
                _writeGate, linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or FrontendProtocolException)
        {
            return false;
        }
    }

    internal async Task<bool> SendFrontendSettingsStateAsync(OverlayFrontendSettingsMutationResponse state, CancellationToken token = default)
    {
        var message = new OverlayWireMessage(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.FrontendSettingsState,
            FrontendSettingsState: state.Settings, ControllerLedAvailable: state.ControllerLedAvailable, FrontendSettingsAvailable: state.SettingsAvailable);
        if (!OverlayProductionControlsWireValidation.IsValidSettingsState(message))
            throw new FrontendProtocolException("Invalid Overlay frontend settings state.");
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
            await OverlayWireCodec.WriteAsync(pipe, message, _writeGate, linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or FrontendProtocolException) { return false; }
    }

    internal async Task<bool> SendControllerVibrationStateAsync(FrontendControllerVibrationStrengthSnapshot state, CancellationToken token = default)
    {
        var message = new OverlayWireMessage(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.ControllerVibrationState,
            ControllerVibrationState: state);
        if (!OverlayProductionControlsWireValidation.IsValidVibrationState(message))
            throw new FrontendProtocolException("Invalid Overlay controller vibration state.");
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
            await OverlayWireCodec.WriteAsync(pipe, message, _writeGate, linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or FrontendProtocolException) { return false; }
    }

    // PR3: send the typed authoritative tab-order state on the one instance write gate.
    private async Task SendTabOrderStateAsync(Stream pipe, AddonQuickSettingsTabOrderSnapshot state, CancellationToken token)
    {
        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.TabOrderState, TabOrderState: state),
            _writeGate, token).ConfigureAwait(false);
    }

    private async Task<AddonQuickSettingsTabOrderSnapshot> CaptureTabOrderSafelyAsync(CancellationToken token)
    {
        try
        {
            var state = await _captureTabOrder(token).ConfigureAwait(false);
            return OverlayQuickSettingsWireValidation.IsStructurallyValid(state)
                ? state
                : AddonQuickSettingsTabOrderSnapshot.Unavailable();
        }
        catch { return AddonQuickSettingsTabOrderSnapshot.Unavailable(); }
    }

    internal async Task<bool> SendTabOrderStateAsync(AddonQuickSettingsTabOrderSnapshot state, CancellationToken token = default)
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
            await SendTabOrderStateAsync(pipe, state, linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or FrontendProtocolException)
        { return false; }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            var connectionGeneration = Interlocked.Increment(ref _nextConnectionGeneration);
            try
            {
                pipe = _pipeFactory();
                _serverReady.TrySetResult();
                lock (_sync) _readyGeneration = connectionGeneration;
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
                    _lastDisconnectedGeneration = connectionGeneration;
                    _disconnected.TrySetResult();
                    _disconnected = NewSignal();
                    _readyGeneration = 0;
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
        try
        {
            await ServeConnectionAsync(pipe, connection).ConfigureAwait(false);
        }
        finally
        {
            connection.Cancel();
        }
    }

    private async Task ServeConnectionAsync(Stream pipe, CancellationTokenSource connection)
    {
        // OQ5-UI-09 blocker fix: every Runtime -> Overlay write goes through the ONE instance
        // _writeGate, including handshake and the post-Ready TabOrderState replies. A second
        // per-connection semaphore would let a tab-order reply and a SendNavigationAsync/
        // SendCommandAsync write interleave 4-byte prefixes and payloads on the same byte stream.
        var hello = await OverlayWireCodec.ReadAsync(pipe, connection.Token).ConfigureAwait(false);
        if (hello.Kind != OverlayWireMessageKind.Handshake || hello.ProtocolVersion != OverlayTransportProtocol.CurrentVersion ||
            OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(hello) || OverlayProductionControlsWireValidation.HasPayload(hello))
        {
            await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.ProtocolError, Error: "Overlay protocol version mismatch."), _writeGate, connection.Token).ConfigureAwait(false);
            return;
        }

        await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.HandshakeAccepted), _writeGate, connection.Token).ConfigureAwait(false);
        // OQ5-UI-09 section 6: the authoritative tab order goes out immediately after acceptance so
        // the client can apply it before it reports Ready -- the Runtime never Shows a Ready Overlay
        // that still has only the default local order.
        await SendTabOrderStateAsync(pipe, await CaptureTabOrderSafelyAsync(connection.Token).ConfigureAwait(false), connection.Token).ConfigureAwait(false);
        while (!connection.IsCancellationRequested)
        {
            var message = await OverlayWireCodec.ReadAsync(pipe, connection.Token).ConfigureAwait(false);
            if (message.ProtocolVersion != OverlayTransportProtocol.CurrentVersion)
                throw new FrontendProtocolException("Invalid Overlay state message.");
            if (OverlayProductionControlsWireValidation.HasPayload(message) && message.Kind is not (
                    OverlayWireMessageKind.ControllerLedMutationRequest or OverlayWireMessageKind.ControllerLedMutationResult
                    or OverlayWireMessageKind.CurrentPowerSourceMutationRequest or OverlayWireMessageKind.CurrentPowerSourceMutationResult
                    or OverlayWireMessageKind.ControllerVibrationMutationRequest or OverlayWireMessageKind.ControllerVibrationMutationResult
                    or OverlayWireMessageKind.FrontendSettingsState or OverlayWireMessageKind.ControllerVibrationState))
                throw new FrontendProtocolException("Unexpected Overlay production-controls payload.");

            if (message.Kind == OverlayWireMessageKind.DismissRequested && message.Command is null && message.Navigation is null && message.State is null && message.Error is null && message.TabOrderState is null && message.TabOrderMove is null && message.TabOrderMutationResult is null && message.QuickSettingsPage is null && message.QuickSettingsMutationRequest is null && message.QuickSettingsMutationResponse is null && message.ClawHudState is null && message.ClawHudMutationRequest is null && message.ClawHudMutationResponse is null && message.ProfileCatalogState is null && message.ProfilePageRequest is null && message.ProfilePageResult is null && !OverlayShortcutWireValidation.HasShortcutPayload(message) && !OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(message))
            {
                DismissRequested?.Invoke(this);
                continue;
            }

            if (message.Kind == OverlayWireMessageKind.TabOrderMoveRequest)
            {
                if (!OverlayQuickSettingsWireValidation.IsStructurallyValid(message.TabOrderMove) || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMutationResult is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationRequest is not null || message.QuickSettingsMutationResponse is not null || message.ClawHudState is not null || message.ClawHudMutationRequest is not null || message.ClawHudMutationResponse is not null || OverlayQuickSettingsWireValidation.HasProfilePayload(message) || OverlayShortcutWireValidation.HasShortcutPayload(message) || OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(message))
                    throw new FrontendProtocolException("Invalid Overlay tab-order move message.");
                _ = HandleTabOrderMoveRequestAsync(pipe, message.TabOrderMove!, connection.Token);
                continue;
            }

            if (message.Kind == OverlayWireMessageKind.QuickSettingsMutationRequest)
            {
                if (message.QuickSettingsMutationRequest is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationResponse is not null || message.ClawHudState is not null || message.ClawHudMutationRequest is not null || message.ClawHudMutationResponse is not null || OverlayQuickSettingsWireValidation.HasProfilePayload(message) || OverlayShortcutWireValidation.HasShortcutPayload(message) || OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(message))
                    throw new FrontendProtocolException("Invalid Overlay Quick Settings mutation request.");
                var request = message.QuickSettingsMutationRequest;
                if (!OverlayQuickSettingsWireValidation.IsStructurallyValid(request))
                    throw new FrontendProtocolException("Invalid Overlay Quick Settings mutation request shape.");
                // SF-V2-02/06 [CRITICAL]: a generic Device intent can still reach SetDeviceTdpAsync,
                // which may await real hardware apply completion. Handling it inline here would block
                // this sole read loop and delay/break a concurrent Hide/DismissRequested/tab-order move
                // frame while the Overlay is modal. Run it as one exception-contained fire-and-forget
                // operation and resume reading immediately; the response is written later through the
                // shared _writeGate.
                _ = HandleQuickSettingsMutationRequestAsync(pipe, request, connection.Token);
                continue;
            }

            if (message.Kind == OverlayWireMessageKind.ClawHudMutationRequest)
            {
                if (message.ClawHudMutationRequest is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationRequest is not null || message.QuickSettingsMutationResponse is not null || message.ClawHudState is not null || message.ClawHudMutationResponse is not null || OverlayQuickSettingsWireValidation.HasProfilePayload(message) || OverlayShortcutWireValidation.HasShortcutPayload(message) || OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(message))
                    throw new FrontendProtocolException("Invalid Overlay ClawHUD mutation request.");
                _ = HandleClawHudMutationRequestAsync(pipe, message.ClawHudMutationRequest, connection.Token);
                continue;
            }

            if (message.Kind == OverlayWireMessageKind.ProfileCatalogRequest)
            {
                if (message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationRequest is not null || message.QuickSettingsMutationResponse is not null || message.ClawHudState is not null || message.ClawHudMutationRequest is not null || message.ClawHudMutationResponse is not null || message.ProfileCatalogState is not null || message.ProfilePageRequest is not null || message.ProfilePageResult is not null || OverlayShortcutWireValidation.HasShortcutPayload(message) || OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(message))
                    throw new FrontendProtocolException("Invalid Overlay Profile catalog request.");
                _ = HandleProfileCatalogRequestAsync(pipe, connection.Token);
                continue;
            }

            if (message.Kind == OverlayWireMessageKind.ProfilePageRequest)
            {
                if (!OverlayQuickSettingsWireValidation.IsStructurallyValid(message.ProfilePageRequest) || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationRequest is not null || message.QuickSettingsMutationResponse is not null || message.ClawHudState is not null || message.ClawHudMutationRequest is not null || message.ClawHudMutationResponse is not null || message.ProfileCatalogState is not null || message.ProfilePageResult is not null || OverlayShortcutWireValidation.HasShortcutPayload(message) || OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(message))
                    throw new FrontendProtocolException("Invalid Overlay Profile page request.");
                _ = HandleProfilePageRequestAsync(pipe, message.ProfilePageRequest!, connection.Token);
                continue;
            }

            if (message.Kind == OverlayWireMessageKind.ShortcutExecuteRequest)
            {
                if (!OverlayShortcutWireValidation.IsValidExecuteRequestMessage(message))
                    throw new FrontendProtocolException("Invalid Overlay Shortcut execution request.");
                _ = HandleShortcutExecuteRequestAsync(pipe, message.ShortcutExecuteRequest!, connection.Token);
                continue;
            }

            if (message.Kind == OverlayWireMessageKind.BackButtonMappingMutationRequest)
            {
                if (!OverlayBackButtonMappingWireValidation.IsValidMutationRequestMessage(message))
                    throw new FrontendProtocolException("Invalid Overlay M1/M2 mapping mutation request.");
                _ = HandleBackButtonMappingMutationRequestAsync(pipe, message.BackButtonMappingMutationRequest!, connection.Token);
                continue;
            }

            if (message.Kind == OverlayWireMessageKind.ControllerLedMutationRequest || message.Kind == OverlayWireMessageKind.CurrentPowerSourceMutationRequest)
            {
                var led = message.Kind == OverlayWireMessageKind.ControllerLedMutationRequest;
                if (!OverlayProductionControlsWireValidation.IsValidSettingsMutationRequest(message, led))
                    throw new FrontendProtocolException("Invalid Overlay frontend settings mutation request.");
                _ = HandleFrontendSettingsMutationRequestAsync(pipe, message.FrontendSettingsMutationRequest!, led, connection.Token);
                continue;
            }

            if (message.Kind == OverlayWireMessageKind.ControllerVibrationMutationRequest)
            {
                if (!OverlayProductionControlsWireValidation.IsValidVibrationRequest(message))
                    throw new FrontendProtocolException("Invalid Overlay controller vibration mutation request.");
                _ = HandleControllerVibrationMutationRequestAsync(pipe, message.ControllerVibrationMutationRequest!, connection.Token);
                continue;
            }

            if (message.Kind != OverlayWireMessageKind.State || message.State is null || OverlayQuickSettingsWireValidation.HasProfilePayload(message) || OverlayShortcutWireValidation.HasShortcutPayload(message) || OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(message))
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

    private async Task HandleFrontendSettingsMutationRequestAsync(Stream pipe, OverlayFrontendSettingsMutationRequest request, bool led, CancellationToken token)
    {
        try
        {
            bool admitted;
            lock (_sync) admitted = _readyState && _state == OverlayState.Visible;
            OverlayFrontendSettingsMutationResponse response;
            if (admitted && _mutateFrontendSettings is not null)
            {
                try { response = await _mutateFrontendSettings(request, token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch { response = await CaptureFrontendSettingsSafelyAsync(token, request.RequestId, false, "Settings update failed.").ConfigureAwait(false); }
            }
            else response = await CaptureFrontendSettingsSafelyAsync(token, request.RequestId, false,
                admitted ? "Settings are unavailable." : "The Overlay is not visible.").ConfigureAwait(false);

            var kind = (led, response.Succeeded) switch
            {
                (true, _) => OverlayWireMessageKind.ControllerLedMutationResult,
                (false, _) => OverlayWireMessageKind.CurrentPowerSourceMutationResult,
            };
            await OverlayWireCodec.WriteAsync(pipe,
                new(OverlayTransportProtocol.CurrentVersion, kind, FrontendSettingsMutationResponse: response), _writeGate, token).ConfigureAwait(false);
        }
        catch { }
    }

    private async Task<OverlayFrontendSettingsMutationResponse> CaptureFrontendSettingsSafelyAsync(CancellationToken token, long requestId, bool succeeded, string? failure)
    {
        try
        {
            var captured = _captureFrontendSettings is null
                ? new OverlayFrontendSettingsMutationResponse(requestId, false, "Settings are unavailable.", UnavailableFrontendSettings(), false, false)
                : await _captureFrontendSettings(token).ConfigureAwait(false);
            return captured with { RequestId = requestId, Succeeded = succeeded, FailureMessage = failure };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return new(requestId, false, "Settings are unavailable.", UnavailableFrontendSettings(), false, false); }
    }

    private static FrontendSettingsSnapshot UnavailableFrontendSettings() =>
        new(FrontendLogLevel.Off, false, FrontButtonMappingSettings.Default) { BackButtonMapping = BackButtonMappingSettings.Default };

    private async Task HandleControllerVibrationMutationRequestAsync(Stream pipe, OverlayControllerVibrationMutationRequest request, CancellationToken token)
    {
        try
        {
            bool admitted;
            lock (_sync) admitted = _readyState && _state == OverlayState.Visible;
            FrontendControllerVibrationStrengthMutationResult result;
            if (admitted && _mutateControllerVibration is not null)
            {
                try { result = await _mutateControllerVibration(request, token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch { result = new(FrontendControllerVibrationStrengthMutationOutcome.Failed,
                    await CaptureControllerVibrationSafelyAsync(token).ConfigureAwait(false), "Vibration strength update failed."); }
            }
            else result = new(FrontendControllerVibrationStrengthMutationOutcome.Unavailable,
                await CaptureControllerVibrationSafelyAsync(token).ConfigureAwait(false), admitted ? "Vibration strength is unavailable." : "The Overlay is not visible.");

            if (!OverlayProductionControlsWireValidation.IsValidVibrationResult(new(OverlayTransportProtocol.CurrentVersion,
                    OverlayWireMessageKind.ControllerVibrationMutationResult, ControllerVibrationMutationResult: new(request.RequestId, result))))
                result = new(FrontendControllerVibrationStrengthMutationOutcome.Failed,
                    FrontendControllerVibrationStrengthSnapshot.Unavailable(), "Vibration strength update failed.");
            await OverlayWireCodec.WriteAsync(pipe,
                new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.ControllerVibrationMutationResult,
                    ControllerVibrationMutationResult: new(request.RequestId, result)), _writeGate, token).ConfigureAwait(false);
        }
        catch { }
    }

    private async Task<FrontendControllerVibrationStrengthSnapshot> CaptureControllerVibrationSafelyAsync(CancellationToken token)
    {
        try { return _captureControllerVibration is null ? FrontendControllerVibrationStrengthSnapshot.Unavailable() : await _captureControllerVibration(token).ConfigureAwait(false); }
        catch { return FrontendControllerVibrationStrengthSnapshot.Unavailable(); }
    }

    private async Task HandleTabOrderMoveRequestAsync(Stream pipe, AddonQuickSettingsTabOrderMoveIntent intent, CancellationToken token)
    {
        try
        {
            bool admitted;
            lock (_sync) admitted = _readyState && _state == OverlayState.Visible;
            AddonQuickSettingsTabOrderMutationResult result;
            if (!admitted)
            {
                result = new(false, "The Overlay is not visible.", await CaptureTabOrderSafelyAsync(token).ConfigureAwait(false));
            }
            else
            {
                try { result = await _moveTabOrder(intent, token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch { result = new(false, "Tab order update failed.", await CaptureTabOrderSafelyAsync(token).ConfigureAwait(false)); }
            }
            await OverlayWireCodec.WriteAsync(pipe,
                new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.TabOrderMoveResult, TabOrderMutationResult: result),
                _writeGate, token).ConfigureAwait(false);
        }
        catch { }
    }

    // SF-V2-02/06 section 12/15/17: runs OUTSIDE the ServeAsync read loop so a long TDP hardware-apply
    // wait never delays Hide/DismissRequested/tab-order processing. Admission (Ready + Visible) is
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

    private async Task HandleClawHudMutationRequestAsync(Stream pipe, OverlayClawHudMutationRequest request, CancellationToken token)
    {
        try
        {
            bool admitted;
            lock (_sync) admitted = _readyState && _state == OverlayState.Visible;
            var hasEnabled = request.Enabled is not null;
            var hasIntent = request.Intent is not null;
            FrontendClawHudMutationResult result;
            if (request.RequestId > 0 && hasEnabled ^ hasIntent && admitted)
            {
                try
                {
                    result = hasEnabled
                        ? _setClawHudEnabled is null
                            ? FrontendClawHudMutationResult.Failed(await CaptureClawHudSafelyAsync(token).ConfigureAwait(false), "ClawHUD enablement is unavailable.")
                            : new(true, null, await _setClawHudEnabled(request.Enabled!.Value, token).ConfigureAwait(false))
                        : _mutateClawHudSetting is null
                            ? FrontendClawHudMutationResult.Failed(await CaptureClawHudSafelyAsync(token).ConfigureAwait(false), "ClawHUD settings are unavailable.")
                            : await _mutateClawHudSetting(request.Intent!, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch { result = FrontendClawHudMutationResult.Failed(await CaptureClawHudSafelyAsync(token).ConfigureAwait(false), "ClawHUD update failed."); }
            }
            else
            {
                result = FrontendClawHudMutationResult.Failed(await CaptureClawHudSafelyAsync(token).ConfigureAwait(false), admitted ? "Invalid ClawHUD mutation request." : "The Overlay is not visible.");
            }

            await OverlayWireCodec.WriteAsync(pipe,
                new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.ClawHudMutationResult,
                    ClawHudMutationResponse: new OverlayClawHudMutationResponse(request.RequestId, Result: result)),
                _writeGate, token).ConfigureAwait(false);
        }
        catch { }
    }

    private async Task HandleShortcutExecuteRequestAsync(Stream pipe, OverlayShortcutExecuteRequest request, CancellationToken token)
    {
        try
        {
            bool admitted;
            lock (_sync) admitted = _readyState && _state == OverlayState.Visible;

            OverlayShortcutExecutionOutcome outcome;
            if (!admitted || _executeShortcut is null)
            {
                outcome = new(false, "The Overlay is not active.");
            }
            else
            {
                try { outcome = await _executeShortcut(request.TileId, token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch { outcome = new(false, "Shortcut could not be executed."); }
            }

            var snapshot = await CaptureShortcutSafelyAsync(token).ConfigureAwait(false);
            var response = new OverlayShortcutExecuteResponse(
                request.RequestId,
                request.TileId,
                outcome.Succeeded,
                outcome.Succeeded ? null : outcome.FailureMessage ?? "Shortcut could not be executed.",
                snapshot);
            var message = OverlayShortcutWireValidation.CreateBoundedResultMessage(response);
            await OverlayWireCodec.WriteAsync(pipe, message, _writeGate, token).ConfigureAwait(false);
        }
        catch { /* execution or connection teardown cannot fault the Overlay read loop */ }
    }

    private async Task HandleBackButtonMappingMutationRequestAsync(
        Stream pipe,
        OverlayBackButtonMappingMutationRequest request,
        CancellationToken token)
    {
        try
        {
            bool admitted;
            lock (_sync) admitted = _readyState && _state == OverlayState.Visible;

            OverlayBackButtonMappingMutationOutcome outcome;
            var mutate = _mutateBackButtonMapping;
            if (!admitted || mutate is null)
            {
                outcome = new(false,
                    admitted ? "M1 / M2 mapping is unavailable." : "The Overlay is not visible.",
                    await CaptureBackButtonMappingSafelyAsync(token).ConfigureAwait(false));
            }
            else
            {
                try
                {
                    outcome = await mutate(request.Mapping, token).ConfigureAwait(false);
                    if (!OverlayBackButtonMappingWireValidation.IsStructurallyValid(outcome.State))
                        outcome = new(false, "M1 / M2 mapping is unavailable.",
                            await CaptureBackButtonMappingSafelyAsync(token).ConfigureAwait(false));
                    else if (outcome.Succeeded && outcome.State.Mapping != request.Mapping)
                        outcome = outcome with { Succeeded = false, FailureMessage = "M1 / M2 mapping update was not applied." };
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch
                {
                    outcome = new(false, "M1 / M2 mapping update failed.",
                        await CaptureBackButtonMappingSafelyAsync(token).ConfigureAwait(false));
                }
            }

            var response = new OverlayBackButtonMappingMutationResponse(
                request.RequestId,
                outcome.Succeeded,
                outcome.FailureMessage,
                outcome.State);
            if (!OverlayBackButtonMappingWireValidation.IsStructurallyValid(response))
                response = new(request.RequestId, false, "M1 / M2 mapping update failed.", OverlayBackButtonMappingState.Unavailable());

            await OverlayWireCodec.WriteAsync(pipe,
                new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.BackButtonMappingMutationResult,
                    BackButtonMappingMutationResponse: response),
                _writeGate, token).ConfigureAwait(false);
        }
        catch { /* mutation or connection teardown cannot fault the Overlay read loop */ }
    }

    private async Task<OverlayBackButtonMappingState> CaptureBackButtonMappingSafelyAsync(CancellationToken token)
    {
        try
        {
            var state = _captureBackButtonMapping is null
                ? OverlayBackButtonMappingState.Unavailable()
                : await _captureBackButtonMapping(token).ConfigureAwait(false);
            return OverlayBackButtonMappingWireValidation.IsStructurallyValid(state)
                ? state
                : OverlayBackButtonMappingState.Unavailable();
        }
        catch { return OverlayBackButtonMappingState.Unavailable(); }
    }

    private async Task<FrontendShortcutDashboardSnapshot> CaptureShortcutSafelyAsync(CancellationToken token)
    {
        try
        {
            var snapshot = _captureShortcut is null
                ? FrontendShortcutDashboardSnapshot.Unavailable("Shortcut settings are unavailable.")
                : await _captureShortcut(token).ConfigureAwait(false);
            return OverlayShortcutWireValidation.IsStructurallyValid(snapshot)
                ? snapshot
                : FrontendShortcutDashboardSnapshot.Unavailable("Shortcut settings are unavailable.");
        }
        catch
        {
            return FrontendShortcutDashboardSnapshot.Unavailable("Shortcut settings are unavailable.");
        }
    }

    private async Task HandleProfileCatalogRequestAsync(Stream pipe, CancellationToken token)
    {
        try
        {
            bool admitted;
            lock (_sync) admitted = _readyState && _state == OverlayState.Visible;
            IReadOnlyList<FrontendProfileGameCatalogEntry> entries = [];
            string? error = null;
            if (admitted)
            {
                try { entries = await _scanProfileGames(token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch { error = "Profile game catalog is unavailable."; }
            }
            else error = "The Overlay is not visible.";

            var state = new OverlayProfileCatalogState(entries ?? [], error);
            if (!OverlayQuickSettingsWireValidation.IsStructurallyValid(state))
                state = new OverlayProfileCatalogState([], "Profile game catalog is unavailable.");
            await OverlayWireCodec.WriteAsync(pipe,
                new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.ProfileCatalogState, ProfileCatalogState: state),
                _writeGate, token).ConfigureAwait(false);
        }
        catch { }
    }

    private async Task HandleProfilePageRequestAsync(Stream pipe, OverlayProfilePageRequest request, CancellationToken token)
    {
        try
        {
            bool admitted;
            lock (_sync) admitted = _readyState && _state == OverlayState.Visible;
            QuickSettingsPageSnapshot page;
            string? error = null;
            if (admitted)
            {
                try { page = await _captureProfilePage(request.AppId, token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch { page = QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, request.AppId, "Profile settings are unavailable."); error = "Profile settings are unavailable."; }
            }
            else
            {
                page = QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, request.AppId, "The Overlay is not visible.");
                error = "The Overlay is not visible.";
            }

            var result = new OverlayProfilePageResponse(request.AppId, page, error);
            if (!OverlayQuickSettingsWireValidation.IsStructurallyValid(result))
            {
                page = QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, request.AppId, "Profile settings are unavailable.");
                result = new OverlayProfilePageResponse(request.AppId, page, "Profile settings are unavailable.");
            }
            await OverlayWireCodec.WriteAsync(pipe,
                new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.ProfilePageResult, ProfilePageResult: result),
                _writeGate, token).ConfigureAwait(false);
        }
        catch { }
    }

    private async Task<FrontendClawHudSnapshot> CaptureClawHudSafelyAsync(CancellationToken token)
    {
        try { return await _captureClawHud(token).ConfigureAwait(false); }
        catch { return FrontendClawHudSnapshot.Unavailable(false, "HUD settings are unavailable."); }
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
