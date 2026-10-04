using System.IO.Pipes;
using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Contracts.ControllerLed;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.FrontendTransport;

internal sealed class NamedPipeOverlayClient : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    // SF-V2-02/06 section 11/19.3: Quick Settings mutations are correlation-specific to this client
    // only -- Show/Hide/Navigation/State/tab-order state never gain a request id. Serializing sends through
    // one gate is an accepted foundation-level simplification; the id/pending-TCS pair is what
    // actually prevents a late, retired result from completing a newer request (section 23.9), not
    // the gate itself.
    private readonly SemaphoreSlim _quickSettingsMutationGate = new(1, 1);
    private readonly object _quickSettingsMutationSync = new();
    private long _quickSettingsRequestSequence;
    private long _pendingQuickSettingsRequestId;
    private TaskCompletionSource<OverlayQuickSettingsMutationResponse>? _pendingQuickSettingsMutation;
    private readonly SemaphoreSlim _clawHudMutationGate = new(1, 1);
    private readonly object _clawHudMutationSync = new();
    private long _clawHudRequestSequence;
    private long _pendingClawHudRequestId;
    private TaskCompletionSource<OverlayClawHudMutationResponse>? _pendingClawHudMutation;
    private readonly SemaphoreSlim _tabOrderMoveGate = new(1, 1);
    private readonly object _tabOrderMoveSync = new();
    private TaskCompletionSource<AddonQuickSettingsTabOrderMutationResult>? _pendingTabOrderMove;
    private readonly SemaphoreSlim _shortcutExecutionGate = new(1, 1);
    private readonly object _shortcutExecutionSync = new();
    private long _shortcutExecutionRequestSequence;
    private long _pendingShortcutExecutionRequestId;
    private Guid _pendingShortcutExecutionTileId;
    private TaskCompletionSource<OverlayShortcutExecuteResponse>? _pendingShortcutExecution;
    private readonly SemaphoreSlim _backButtonMappingMutationGate = new(1, 1);
    private readonly object _backButtonMappingMutationSync = new();
    private long _backButtonMappingRequestSequence;
    private long _pendingBackButtonMappingRequestId;
    private BackButtonMappingSettings? _pendingBackButtonMapping;
    private TaskCompletionSource<OverlayBackButtonMappingMutationResponse>? _pendingBackButtonMappingMutation;
    private readonly SemaphoreSlim _productionSettingsMutationGate = new(1, 1);
    private readonly object _productionSettingsMutationSync = new();
    private long _productionSettingsRequestSequence;
    private long _pendingProductionSettingsRequestId;
    private bool _pendingProductionSettingsIsLed;
    private TaskCompletionSource<OverlayFrontendSettingsMutationResponse>? _pendingProductionSettingsMutation;
    private readonly SemaphoreSlim _controllerVibrationMutationGate = new(1, 1);
    private readonly object _controllerVibrationMutationSync = new();
    private long _controllerVibrationRequestSequence;
    private long _pendingControllerVibrationRequestId;
    private TaskCompletionSource<OverlayControllerVibrationMutationResponse>? _pendingControllerVibrationMutation;
    private NamedPipeClientStream? _pipe;
    private int _disposed;

    internal event Action<FrontendSettingsSnapshot, bool, bool>? FrontendSettingsStateReceived;
    internal event Action<FrontendControllerVibrationStrengthSnapshot>? ControllerVibrationStateReceived;

    internal NamedPipeOverlayClient(string pipeName) => _pipeName = pipeName;

    internal async Task RunAsync(Func<OverlayCommand, Task> commandHandler, CancellationToken token = default)
        => await RunAsync(commandHandler, null, null, null, token).ConfigureAwait(false);

    internal async Task RunAsync(Func<OverlayCommand, Task> commandHandler, Func<OverlayNavigationAction, Task>? navigationHandler, CancellationToken token = default)
        => await RunAsync(commandHandler, navigationHandler, null, null, token).ConfigureAwait(false);

    internal async Task RunAsync(
        Func<OverlayCommand, Task> commandHandler,
        Func<OverlayNavigationAction, Task>? navigationHandler,
        Func<AddonQuickSettingsTabOrderSnapshot, Task>? tabOrderHandler,
        CancellationToken token = default)
        => await RunAsync(commandHandler, navigationHandler, tabOrderHandler, null, null, token).ConfigureAwait(false);

    internal async Task RunAsync(
        Func<OverlayCommand, Task> commandHandler,
        Func<OverlayNavigationAction, Task>? navigationHandler,
        Func<AddonQuickSettingsTabOrderSnapshot, Task>? tabOrderHandler,
        Func<QuickSettingsPageSnapshot, Task>? quickSettingsPageHandler,
        CancellationToken token = default)
        => await RunAsync(commandHandler, navigationHandler, tabOrderHandler, quickSettingsPageHandler, null, token).ConfigureAwait(false);

    internal async Task RunAsync(
        Func<OverlayCommand, Task> commandHandler,
        Func<OverlayNavigationAction, Task>? navigationHandler,
        Func<AddonQuickSettingsTabOrderSnapshot, Task>? tabOrderHandler,
        Func<QuickSettingsPageSnapshot, Task>? quickSettingsPageHandler,
        Func<FrontendClawHudSnapshot, Task>? clawHudHandler,
        CancellationToken token = default)
        => await RunAsync(commandHandler, navigationHandler, tabOrderHandler, quickSettingsPageHandler, clawHudHandler, null, null, token).ConfigureAwait(false);

    internal async Task RunAsync(
        Func<OverlayCommand, Task> commandHandler,
        Func<OverlayNavigationAction, Task>? navigationHandler,
        Func<AddonQuickSettingsTabOrderSnapshot, Task>? tabOrderHandler,
        Func<QuickSettingsPageSnapshot, Task>? quickSettingsPageHandler,
        Func<FrontendClawHudSnapshot, Task>? clawHudHandler,
        Func<OverlayProfileCatalogState, Task>? profileCatalogHandler,
        Func<OverlayProfilePageResponse, Task>? profilePageHandler,
        CancellationToken token = default)
        => await RunAsync(commandHandler, navigationHandler, tabOrderHandler, quickSettingsPageHandler,
            clawHudHandler, profileCatalogHandler, profilePageHandler, null, token).ConfigureAwait(false);

    internal async Task RunAsync(
        Func<OverlayCommand, Task> commandHandler,
        Func<OverlayNavigationAction, Task>? navigationHandler,
        Func<AddonQuickSettingsTabOrderSnapshot, Task>? tabOrderHandler,
        Func<QuickSettingsPageSnapshot, Task>? quickSettingsPageHandler,
        Func<FrontendClawHudSnapshot, Task>? clawHudHandler,
        Func<OverlayProfileCatalogState, Task>? profileCatalogHandler,
        Func<OverlayProfilePageResponse, Task>? profilePageHandler,
        Func<FrontendShortcutDashboardSnapshot, Task>? shortcutStateHandler,
        CancellationToken token = default)
        => await RunAsync(commandHandler, navigationHandler, tabOrderHandler, quickSettingsPageHandler,
            clawHudHandler, profileCatalogHandler, profilePageHandler, shortcutStateHandler, null, token).ConfigureAwait(false);

    internal async Task RunAsync(
        Func<OverlayCommand, Task> commandHandler,
        Func<OverlayNavigationAction, Task>? navigationHandler,
        Func<AddonQuickSettingsTabOrderSnapshot, Task>? tabOrderHandler,
        Func<QuickSettingsPageSnapshot, Task>? quickSettingsPageHandler,
        Func<FrontendClawHudSnapshot, Task>? clawHudHandler,
        Func<OverlayProfileCatalogState, Task>? profileCatalogHandler,
        Func<OverlayProfilePageResponse, Task>? profilePageHandler,
        Func<FrontendShortcutDashboardSnapshot, Task>? shortcutStateHandler,
        Func<OverlayBackButtonMappingState, Task>? backButtonMappingHandler,
        CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        _pipe = pipe;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await pipe.ConnectAsync(5000, linked.Token).ConfigureAwait(false);
        await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.Handshake), _writeGate, linked.Token).ConfigureAwait(false);
        var accepted = await OverlayWireCodec.ReadAsync(pipe, linked.Token).ConfigureAwait(false);
        if (accepted.Kind != OverlayWireMessageKind.HandshakeAccepted || accepted.ProtocolVersion != OverlayTransportProtocol.CurrentVersion ||
            OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(accepted) || OverlayProductionControlsWireValidation.HasPayload(accepted))
            throw new FrontendProtocolException("Overlay handshake was rejected.");

        // OQ5-UI-09 section 6: apply the mandatory initial authoritative order BEFORE reporting Ready.
        var initial = await OverlayWireCodec.ReadAsync(pipe, linked.Token).ConfigureAwait(false);
        if (initial.ProtocolVersion != OverlayTransportProtocol.CurrentVersion || initial.Kind != OverlayWireMessageKind.TabOrderState ||
            OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(initial) || OverlayProductionControlsWireValidation.HasPayload(initial))
            throw new FrontendProtocolException("Overlay did not receive an initial tab-order state.");
        var initialOrder = ValidateTabOrderMessage(initial);
        if (tabOrderHandler is not null)
            await tabOrderHandler(initialOrder).ConfigureAwait(false);

        await SendStateAsync(pipe, OverlayState.Ready, linked.Token).ConfigureAwait(false);
        try
        {
            while (!linked.IsCancellationRequested)
            {
                var message = await OverlayWireCodec.ReadAsync(pipe, linked.Token).ConfigureAwait(false);
                if (message.ProtocolVersion != OverlayTransportProtocol.CurrentVersion)
                    throw new FrontendProtocolException("Invalid Overlay message.");
                if (OverlayProductionControlsWireValidation.HasPayload(message) && message.Kind is not (
                        OverlayWireMessageKind.FrontendSettingsState or OverlayWireMessageKind.ControllerLedMutationResult
                        or OverlayWireMessageKind.CurrentPowerSourceMutationResult or OverlayWireMessageKind.ControllerVibrationState
                        or OverlayWireMessageKind.ControllerVibrationMutationResult))
                    throw new FrontendProtocolException("Unexpected Overlay production-controls payload.");
                if (message.Kind == OverlayWireMessageKind.TabOrderState)
                {
                    var order = ValidateTabOrderMessage(message);
                    if (tabOrderHandler is not null)
                        await tabOrderHandler(order).ConfigureAwait(false);
                    continue;
                }
                if (message.Kind == OverlayWireMessageKind.TabOrderMoveResult)
                {
                    if (!ValidateTabOrderMutationResult(message) || OverlayShortcutWireValidation.HasShortcutPayload(message) || OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(message))
                        throw new FrontendProtocolException("Invalid Overlay tab-order mutation result.");
                    TaskCompletionSource<AddonQuickSettingsTabOrderMutationResult>? pending;
                    lock (_tabOrderMoveSync) pending = _pendingTabOrderMove;
                    pending?.TrySetResult(message.TabOrderMutationResult!);
                    continue;
                }
                if (message.Kind == OverlayWireMessageKind.Navigation)
                {
                    if (message.Navigation is null || message.Command is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || OverlayQuickSettingsWireValidation.HasProfilePayload(message) || OverlayShortcutWireValidation.HasShortcutPayload(message) || OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(message))
                        throw new FrontendProtocolException("Invalid Overlay navigation message.");
                    if (navigationHandler is not null)
                        await navigationHandler(message.Navigation.Value).ConfigureAwait(false);
                    continue;
                }
                if (message.Kind == OverlayWireMessageKind.QuickSettingsPageState)
                {
                    if (message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || message.QuickSettingsMutationRequest is not null || message.QuickSettingsMutationResponse is not null || message.ClawHudState is not null || message.ClawHudMutationRequest is not null || message.ClawHudMutationResponse is not null || OverlayQuickSettingsWireValidation.HasProfilePayload(message) || OverlayShortcutWireValidation.HasShortcutPayload(message) || OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(message))
                        throw new FrontendProtocolException("Invalid Overlay Quick Settings page message.");
                    // Section 11: fail closed on a malformed outbound page frame rather than pass null
                    // collections into the future SF-V2-07 renderer.
                    if (!OverlayQuickSettingsWireValidation.IsStructurallyValid(message.QuickSettingsPage))
                        throw new FrontendProtocolException("Invalid Overlay Quick Settings page shape.");
                    if (quickSettingsPageHandler is not null)
                        await quickSettingsPageHandler(message.QuickSettingsPage!).ConfigureAwait(false);
                    continue;
                }
                if (message.Kind == OverlayWireMessageKind.ClawHudState)
                {
                    if (message.ClawHudState is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationRequest is not null || message.QuickSettingsMutationResponse is not null || message.ClawHudMutationRequest is not null || message.ClawHudMutationResponse is not null || OverlayQuickSettingsWireValidation.HasProfilePayload(message) || OverlayShortcutWireValidation.HasShortcutPayload(message) || OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(message) || !IsStructurallyValid(message.ClawHudState))
                        throw new FrontendProtocolException("Invalid Overlay ClawHUD state message.");
                    if (clawHudHandler is not null)
                        await clawHudHandler(message.ClawHudState).ConfigureAwait(false);
                    continue;
                }
                if (message.Kind == OverlayWireMessageKind.ShortcutState)
                {
                    if (!OverlayShortcutWireValidation.IsValidStateMessage(message))
                        throw new FrontendProtocolException("Invalid Overlay Shortcut state message.");
                    if (shortcutStateHandler is not null)
                        await shortcutStateHandler(message.ShortcutState!).ConfigureAwait(false);
                    continue;
                }
                if (message.Kind == OverlayWireMessageKind.ShortcutExecuteResult)
                {
                    if (!OverlayShortcutWireValidation.IsValidExecuteResultMessage(message))
                        throw new FrontendProtocolException("Invalid Overlay Shortcut execution result.");
                    var response = message.ShortcutExecuteResult!;
                    TaskCompletionSource<OverlayShortcutExecuteResponse>? pending;
                    lock (_shortcutExecutionSync)
                    {
                        if (response.RequestId == _pendingShortcutExecutionRequestId
                            && response.TileId != _pendingShortcutExecutionTileId)
                            throw new FrontendProtocolException("Overlay Shortcut execution correlation mismatch.");
                        pending = response.RequestId == _pendingShortcutExecutionRequestId ? _pendingShortcutExecution : null;
                    }
                    pending?.TrySetResult(response);
                    continue;
                }
                if (message.Kind == OverlayWireMessageKind.BackButtonMappingState)
                {
                    if (!OverlayBackButtonMappingWireValidation.IsValidStateMessage(message))
                        throw new FrontendProtocolException("Invalid Overlay M1/M2 mapping state message.");
                    if (backButtonMappingHandler is not null)
                        await backButtonMappingHandler(message.BackButtonMappingState!).ConfigureAwait(false);
                    continue;
                }
                if (message.Kind == OverlayWireMessageKind.BackButtonMappingMutationResult)
                {
                    if (!OverlayBackButtonMappingWireValidation.IsValidMutationResultMessage(message))
                        throw new FrontendProtocolException("Invalid Overlay M1/M2 mapping mutation result.");
                    var response = message.BackButtonMappingMutationResponse!;
                    TaskCompletionSource<OverlayBackButtonMappingMutationResponse>? pending;
                    lock (_backButtonMappingMutationSync)
                    {
                        pending = response.RequestId == _pendingBackButtonMappingRequestId
                            ? _pendingBackButtonMappingMutation
                            : null;
                        if (pending is not null && response.Succeeded && response.State.Mapping != _pendingBackButtonMapping)
                            throw new FrontendProtocolException("Overlay M1/M2 mapping result does not match the requested mapping.");
                    }
                    pending?.TrySetResult(response);
                    continue;
                }
                if (message.Kind == OverlayWireMessageKind.FrontendSettingsState)
                {
                    if (!OverlayProductionControlsWireValidation.IsValidSettingsState(message))
                        throw new FrontendProtocolException("Invalid Overlay frontend settings state.");
                    FrontendSettingsStateReceived?.Invoke(message.FrontendSettingsState!, message.ControllerLedAvailable!.Value, message.FrontendSettingsAvailable!.Value);
                    continue;
                }
                if (message.Kind == OverlayWireMessageKind.ControllerVibrationState)
                {
                    if (!OverlayProductionControlsWireValidation.IsValidVibrationState(message))
                        throw new FrontendProtocolException("Invalid Overlay controller vibration state.");
                    ControllerVibrationStateReceived?.Invoke(message.ControllerVibrationState!);
                    continue;
                }
                if (message.Kind is OverlayWireMessageKind.ControllerLedMutationResult or OverlayWireMessageKind.CurrentPowerSourceMutationResult)
                {
                    var led = message.Kind == OverlayWireMessageKind.ControllerLedMutationResult;
                    if (!OverlayProductionControlsWireValidation.IsValidSettingsMutationResult(message, led))
                        throw new FrontendProtocolException("Invalid Overlay frontend settings mutation result.");
                    var response = message.FrontendSettingsMutationResponse!;
                    TaskCompletionSource<OverlayFrontendSettingsMutationResponse>? pending;
                    lock (_productionSettingsMutationSync)
                    {
                        if (response.RequestId == _pendingProductionSettingsRequestId && led != _pendingProductionSettingsIsLed)
                            throw new FrontendProtocolException("Overlay frontend settings mutation correlation mismatch.");
                        pending = response.RequestId == _pendingProductionSettingsRequestId ? _pendingProductionSettingsMutation : null;
                    }
                    pending?.TrySetResult(response);
                    continue;
                }
                if (message.Kind == OverlayWireMessageKind.ControllerVibrationMutationResult)
                {
                    if (!OverlayProductionControlsWireValidation.IsValidVibrationResult(message))
                        throw new FrontendProtocolException("Invalid Overlay controller vibration mutation result.");
                    var response = message.ControllerVibrationMutationResult!;
                    TaskCompletionSource<OverlayControllerVibrationMutationResponse>? pending;
                    lock (_controllerVibrationMutationSync)
                        pending = response.RequestId == _pendingControllerVibrationRequestId ? _pendingControllerVibrationMutation : null;
                    pending?.TrySetResult(response);
                    continue;
                }
                if (message.Kind == OverlayWireMessageKind.ProfileCatalogState)
                {
                    if (message.ProfileCatalogState is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationRequest is not null || message.QuickSettingsMutationResponse is not null || message.ClawHudState is not null || message.ClawHudMutationRequest is not null || message.ClawHudMutationResponse is not null || message.ProfilePageRequest is not null || message.ProfilePageResult is not null || OverlayShortcutWireValidation.HasShortcutPayload(message) || OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(message) || !OverlayQuickSettingsWireValidation.IsStructurallyValid(message.ProfileCatalogState))
                        throw new FrontendProtocolException("Invalid Overlay Profile catalog state message.");
                    if (profileCatalogHandler is not null)
                        await profileCatalogHandler(message.ProfileCatalogState).ConfigureAwait(false);
                    continue;
                }
                if (message.Kind == OverlayWireMessageKind.ProfilePageResult)
                {
                    if (!OverlayQuickSettingsWireValidation.IsStructurallyValid(message.ProfilePageResult) || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationRequest is not null || message.QuickSettingsMutationResponse is not null || message.ClawHudState is not null || message.ClawHudMutationRequest is not null || message.ClawHudMutationResponse is not null || message.ProfileCatalogState is not null || message.ProfilePageRequest is not null || OverlayShortcutWireValidation.HasShortcutPayload(message) || OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(message))
                        throw new FrontendProtocolException("Invalid Overlay Profile page result message.");
                    if (profilePageHandler is not null)
                        await profilePageHandler(message.ProfilePageResult!).ConfigureAwait(false);
                    continue;
                }
                if (message.Kind == OverlayWireMessageKind.QuickSettingsMutationResult)
                {
                    if (message.QuickSettingsMutationResponse is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationRequest is not null || message.ClawHudState is not null || message.ClawHudMutationRequest is not null || message.ClawHudMutationResponse is not null || OverlayQuickSettingsWireValidation.HasProfilePayload(message) || OverlayShortcutWireValidation.HasShortcutPayload(message) || OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(message))
                        throw new FrontendProtocolException("Invalid Overlay Quick Settings mutation result.");
                    // Section 23.9: only complete the CURRENT pending request. A late result whose id was
                    // superseded by a newer send must never complete that newer request.
                    TaskCompletionSource<OverlayQuickSettingsMutationResponse>? pending;
                    lock (_quickSettingsMutationSync)
                        pending = message.QuickSettingsMutationResponse.RequestId == _pendingQuickSettingsRequestId ? _pendingQuickSettingsMutation : null;
                    pending?.TrySetResult(message.QuickSettingsMutationResponse);
                    continue;
                }
                if (message.Kind == OverlayWireMessageKind.ClawHudMutationResult)
                {
                    if (!ValidateClawHudMutationResult(message) || OverlayShortcutWireValidation.HasShortcutPayload(message) || OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(message))
                        throw new FrontendProtocolException("Invalid Overlay ClawHUD mutation result.");
                    TaskCompletionSource<OverlayClawHudMutationResponse>? pending;
                    lock (_clawHudMutationSync)
                        pending = message.ClawHudMutationResponse!.RequestId == _pendingClawHudRequestId ? _pendingClawHudMutation : null;
                    pending?.TrySetResult(message.ClawHudMutationResponse);
                    continue;
                }
                if (message.Kind != OverlayWireMessageKind.Command || message.Command is null || message.Navigation is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || message.ProfileCatalogState is not null || message.ProfilePageRequest is not null || message.ProfilePageResult is not null || OverlayShortcutWireValidation.HasShortcutPayload(message) || OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(message))
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
        finally
        {
            CancelPendingShortcutExecution();
            CancelPendingBackButtonMappingMutation();
            CancelPendingProductionControlMutations();
        }
    }

    private async Task SendStateAsync(Stream pipe, OverlayState state, CancellationToken token) =>
        await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.State, State: state), _writeGate, token).ConfigureAwait(false);

    private static AddonQuickSettingsTabOrderSnapshot ValidateTabOrderMessage(OverlayWireMessage message)
    {
        if (message.TabOrderState is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || OverlayQuickSettingsWireValidation.HasProfilePayload(message) || OverlayShortcutWireValidation.HasShortcutPayload(message) || OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(message))
            throw new FrontendProtocolException("Invalid Overlay tab-order message.");
        if (!OverlayQuickSettingsWireValidation.IsStructurallyValid(message.TabOrderState))
            throw new FrontendProtocolException("Overlay tab-order state was malformed.");
        return message.TabOrderState;
    }

    private static bool ValidateTabOrderMutationResult(OverlayWireMessage message)
    {
        if (message.TabOrderMutationResult is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationRequest is not null || message.QuickSettingsMutationResponse is not null || OverlayQuickSettingsWireValidation.HasProfilePayload(message) || OverlayShortcutWireValidation.HasShortcutPayload(message) || OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(message))
            return false;
        return OverlayQuickSettingsWireValidation.IsStructurallyValid(message.TabOrderMutationResult.State);
    }

    internal async Task SendProfileCatalogRequestAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var pipe = _pipe ?? throw new IOException("Overlay pipe is not connected.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.ProfileCatalogRequest),
            _writeGate, linked.Token).ConfigureAwait(false);
    }

    internal async Task SendProfilePageRequestAsync(uint appId, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (appId == 0) throw new FrontendProtocolException("Invalid Overlay Profile AppId.");
        var pipe = _pipe ?? throw new IOException("Overlay pipe is not connected.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.ProfilePageRequest, ProfilePageRequest: new OverlayProfilePageRequest(appId)),
            _writeGate, linked.Token).ConfigureAwait(false);
    }

    internal async Task<OverlayShortcutExecuteResponse> SendShortcutExecuteAsync(Guid tileId, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (tileId == Guid.Empty)
            throw new FrontendProtocolException("Invalid Overlay Shortcut TileId.");
        if (!_shortcutExecutionGate.Wait(0))
            throw new InvalidOperationException("A Shortcut execution is already pending.");

        var requestId = Interlocked.Increment(ref _shortcutExecutionRequestSequence);
        try
        {
            if (requestId <= 0)
                throw new FrontendProtocolException("Overlay Shortcut request id is invalid.");

            var pipe = _pipe ?? throw new IOException("Overlay pipe is not connected.");

            var completion = new TaskCompletionSource<OverlayShortcutExecuteResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_shortcutExecutionSync)
            {
                _pendingShortcutExecutionRequestId = requestId;
                _pendingShortcutExecutionTileId = tileId;
                _pendingShortcutExecution = completion;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            var request = new OverlayShortcutExecuteRequest(requestId, tileId);
            await OverlayWireCodec.WriteAsync(pipe,
                new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.ShortcutExecuteRequest,
                    ShortcutExecuteRequest: request), _writeGate, linked.Token).ConfigureAwait(false);
            return await completion.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_shortcutExecutionSync)
            {
                if (_pendingShortcutExecutionRequestId == requestId)
                {
                    _pendingShortcutExecution = null;
                    _pendingShortcutExecutionTileId = Guid.Empty;
                }
            }
            _shortcutExecutionGate.Release();
        }
    }

    private void CancelPendingShortcutExecution()
    {
        TaskCompletionSource<OverlayShortcutExecuteResponse>? pending;
        lock (_shortcutExecutionSync)
        {
            pending = _pendingShortcutExecution;
            _pendingShortcutExecution = null;
            _pendingShortcutExecutionRequestId = 0;
            _pendingShortcutExecutionTileId = Guid.Empty;
        }
        pending?.TrySetCanceled();
    }

    private static bool IsStructurallyValid(FrontendClawHudSnapshot snapshot)
    {
        if (!Enum.IsDefined(snapshot.RuntimeState)) return false;
        var settings = snapshot.Settings;
        if (settings is null) return true;
        return Enum.IsDefined(settings.DisplayMode)
            && settings.HudSizeOffset is >= -2 and <= 2
            && Enum.IsDefined(settings.Font)
            && Enum.IsDefined(settings.Alignment)
            && Enum.IsDefined(settings.BackgroundMode)
            && settings.BackgroundOpacityPercent is >= 50 and <= 100
            && settings.BackgroundOpacityPercent % 5 == 0
            && (!settings.IntelVrrRangeFixEnabled || settings.IntelVrrLastResult is null || Enum.IsDefined(settings.IntelVrrLastResult.Status));
    }

    private static bool ValidateClawHudMutationResult(OverlayWireMessage message)
    {
        var response = message.ClawHudMutationResponse;
        return response is { RequestId: > 0, Result: { Snapshot: { } snapshot } }
            && message.Command is null
            && message.Navigation is null
            && message.State is null
            && message.Error is null
            && message.TabOrderState is null
            && message.TabOrderMove is null
            && message.TabOrderMutationResult is null
            && message.QuickSettingsPage is null
            && message.QuickSettingsMutationRequest is null
            && message.QuickSettingsMutationResponse is null
            && message.ClawHudState is null
            && message.ClawHudMutationRequest is null
            && !OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(message)
            && IsStructurallyValid(snapshot);
    }

    // PR3: one typed move at a time; the Runtime result contains the authoritative readback.
    internal async Task<AddonQuickSettingsTabOrderMutationResult> SendTabOrderMoveAsync(AddonQuickSettingsTabOrderMoveIntent intent, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var pipe = _pipe ?? throw new IOException("Overlay pipe is not connected.");
        if (!OverlayQuickSettingsWireValidation.IsStructurallyValid(intent))
            throw new FrontendProtocolException("Invalid Overlay tab-order move intent.");
        await _tabOrderMoveGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            var completion = new TaskCompletionSource<AddonQuickSettingsTabOrderMutationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_tabOrderMoveSync) _pendingTabOrderMove = completion;
            await OverlayWireCodec.WriteAsync(pipe,
                new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.TabOrderMoveRequest, TabOrderMove: intent),
                _writeGate, linked.Token).ConfigureAwait(false);
            return await completion.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_tabOrderMoveSync) _pendingTabOrderMove = null;
            _tabOrderMoveGate.Release();
        }
    }

    // SF-V2-06: one narrow shared-product method carrying the exact closed QuickSettingsMutationIntent
    // shared Quick Settings contract. A typed feature failure (Succeeded=false) is a normal
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

    internal async Task<OverlayBackButtonMappingMutationResponse> SendBackButtonMappingMutationAsync(
        BackButtonMappingSettings mapping,
        CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (!BackButtonMappingValidation.IsValid(mapping))
            throw new FrontendProtocolException("Invalid Overlay M1/M2 mapping.");

        var pipe = _pipe ?? throw new IOException("Overlay pipe is not connected.");
        await _backButtonMappingMutationGate.WaitAsync(token).ConfigureAwait(false);
        var requestId = Interlocked.Increment(ref _backButtonMappingRequestSequence);
        try
        {
            if (requestId <= 0)
                throw new FrontendProtocolException("Overlay M1/M2 mapping request id is invalid.");

            var completion = new TaskCompletionSource<OverlayBackButtonMappingMutationResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_backButtonMappingMutationSync)
            {
                _pendingBackButtonMappingRequestId = requestId;
                _pendingBackButtonMapping = mapping;
                _pendingBackButtonMappingMutation = completion;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            var request = new OverlayBackButtonMappingMutationRequest(requestId, mapping);
            await OverlayWireCodec.WriteAsync(pipe,
                new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.BackButtonMappingMutationRequest,
                    BackButtonMappingMutationRequest: request),
                _writeGate, linked.Token).ConfigureAwait(false);
            return await completion.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_backButtonMappingMutationSync)
            {
                if (_pendingBackButtonMappingRequestId == requestId)
                {
                    _pendingBackButtonMappingRequestId = 0;
                    _pendingBackButtonMapping = null;
                    _pendingBackButtonMappingMutation = null;
                }
            }
            _backButtonMappingMutationGate.Release();
        }
    }

    internal Task<OverlayFrontendSettingsMutationResponse> SendControllerLedMutationAsync(ControllerLedSettings settings, CancellationToken token = default) =>
        SendFrontendSettingsMutationAsync(new OverlayFrontendSettingsMutationRequest(0, ControllerLed: settings), led: true, token);

    internal Task<OverlayFrontendSettingsMutationResponse> SendCurrentPowerSourceMutationAsync(bool enabled, CancellationToken token = default) =>
        SendFrontendSettingsMutationAsync(new OverlayFrontendSettingsMutationRequest(0, CurrentPowerSourceOnly: enabled), led: false, token);

    private async Task<OverlayFrontendSettingsMutationResponse> SendFrontendSettingsMutationAsync(OverlayFrontendSettingsMutationRequest request, bool led, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (led && ControllerLedSettingsValidation.Validate(request.ControllerLed) is not null)
            throw new FrontendProtocolException("Invalid Overlay controller LED settings.");
        var pipe = _pipe ?? throw new IOException("Overlay pipe is not connected.");
        await _productionSettingsMutationGate.WaitAsync(token).ConfigureAwait(false);
        var requestId = Interlocked.Increment(ref _productionSettingsRequestSequence);
        try
        {
            var completion = new TaskCompletionSource<OverlayFrontendSettingsMutationResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_productionSettingsMutationSync)
            {
                _pendingProductionSettingsRequestId = requestId;
                _pendingProductionSettingsIsLed = led;
                _pendingProductionSettingsMutation = completion;
            }
            var kind = led ? OverlayWireMessageKind.ControllerLedMutationRequest : OverlayWireMessageKind.CurrentPowerSourceMutationRequest;
            var correlated = request with { RequestId = requestId };
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, kind, FrontendSettingsMutationRequest: correlated), _writeGate, linked.Token).ConfigureAwait(false);
            return await completion.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_productionSettingsMutationSync)
            {
                if (_pendingProductionSettingsRequestId == requestId)
                {
                    _pendingProductionSettingsRequestId = 0;
                    _pendingProductionSettingsMutation = null;
                }
            }
            _productionSettingsMutationGate.Release();
        }
    }

    internal async Task<FrontendControllerVibrationStrengthMutationResult> SendControllerVibrationMutationAsync(int leftPercent, int rightPercent, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (leftPercent is < 0 or > 100 || rightPercent is < 0 or > 100)
            throw new FrontendProtocolException("Invalid Overlay controller vibration values.");
        var pipe = _pipe ?? throw new IOException("Overlay pipe is not connected.");
        await _controllerVibrationMutationGate.WaitAsync(token).ConfigureAwait(false);
        var requestId = Interlocked.Increment(ref _controllerVibrationRequestSequence);
        try
        {
            var completion = new TaskCompletionSource<OverlayControllerVibrationMutationResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_controllerVibrationMutationSync)
            {
                _pendingControllerVibrationRequestId = requestId;
                _pendingControllerVibrationMutation = completion;
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            var request = new OverlayControllerVibrationMutationRequest(requestId, leftPercent, rightPercent);
            await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.ControllerVibrationMutationRequest,
                ControllerVibrationMutationRequest: request), _writeGate, linked.Token).ConfigureAwait(false);
            var response = await completion.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            return response.Result;
        }
        finally
        {
            lock (_controllerVibrationMutationSync)
            {
                if (_pendingControllerVibrationRequestId == requestId)
                {
                    _pendingControllerVibrationRequestId = 0;
                    _pendingControllerVibrationMutation = null;
                }
            }
            _controllerVibrationMutationGate.Release();
        }
    }

    private void CancelPendingProductionControlMutations()
    {
        lock (_productionSettingsMutationSync)
        {
            _pendingProductionSettingsMutation?.TrySetCanceled();
            _pendingProductionSettingsMutation = null;
            _pendingProductionSettingsRequestId = 0;
        }
        lock (_controllerVibrationMutationSync)
        {
            _pendingControllerVibrationMutation?.TrySetCanceled();
            _pendingControllerVibrationMutation = null;
            _pendingControllerVibrationRequestId = 0;
        }
    }

    private void CancelPendingBackButtonMappingMutation()
    {
        TaskCompletionSource<OverlayBackButtonMappingMutationResponse>? pending;
        lock (_backButtonMappingMutationSync)
        {
            pending = _pendingBackButtonMappingMutation;
            _pendingBackButtonMappingMutation = null;
            _pendingBackButtonMapping = null;
            _pendingBackButtonMappingRequestId = 0;
        }
        pending?.TrySetCanceled();
    }

    internal Task<FrontendClawHudMutationResult> SendClawHudEnabledAsync(bool enabled, CancellationToken token = default) =>
        SendClawHudMutationCoreAsync(new OverlayClawHudMutationRequest(0, Enabled: enabled), token);

    internal Task<FrontendClawHudMutationResult> SendClawHudMutationAsync(FrontendClawHudMutationIntent intent, CancellationToken token = default)
    {
        if (!intent.TryValidate(out var failureMessage))
            throw new FrontendProtocolException(failureMessage ?? "Invalid ClawHUD setting mutation.");
        return SendClawHudMutationCoreAsync(new OverlayClawHudMutationRequest(0, Intent: intent), token);
    }

    private async Task<FrontendClawHudMutationResult> SendClawHudMutationCoreAsync(OverlayClawHudMutationRequest request, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var pipe = _pipe ?? throw new IOException("Overlay pipe is not connected.");
        await _clawHudMutationGate.WaitAsync(token).ConfigureAwait(false);
        var requestId = Interlocked.Increment(ref _clawHudRequestSequence);
        try
        {
            var correlated = request with { RequestId = requestId };
            var tcs = new TaskCompletionSource<OverlayClawHudMutationResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_clawHudMutationSync) { _pendingClawHudRequestId = requestId; _pendingClawHudMutation = tcs; }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            await OverlayWireCodec.WriteAsync(pipe,
                new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.ClawHudMutationRequest, ClawHudMutationRequest: correlated),
                _writeGate, linked.Token).ConfigureAwait(false);
            var response = await tcs.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            return response.Result ?? throw new FrontendProtocolException(response.Error ?? "Overlay ClawHUD mutation failed.");
        }
        finally
        {
            lock (_clawHudMutationSync) { if (_pendingClawHudRequestId == requestId) _pendingClawHudMutation = null; }
            _clawHudMutationGate.Release();
        }
    }

    internal async Task SendDismissRequestedAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var pipe = _pipe ?? throw new IOException("Overlay pipe is not connected.");
        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.DismissRequested), _writeGate, token).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _lifetime.Cancel();
            _pipe?.Dispose();
            CancelPendingBackButtonMappingMutation();

            // Let the one pending Shortcut request observe cancellation and release its gate before
            // disposing that gate.
            await _shortcutExecutionGate.WaitAsync().ConfigureAwait(false);
            _shortcutExecutionGate.Release();
            await _backButtonMappingMutationGate.WaitAsync().ConfigureAwait(false);
            _backButtonMappingMutationGate.Release();
            await _productionSettingsMutationGate.WaitAsync().ConfigureAwait(false);
            _productionSettingsMutationGate.Release();
            await _controllerVibrationMutationGate.WaitAsync().ConfigureAwait(false);
            _controllerVibrationMutationGate.Release();

            _writeGate.Dispose();
            _quickSettingsMutationGate.Dispose();
            _clawHudMutationGate.Dispose();
            _tabOrderMoveGate.Dispose();
            _shortcutExecutionGate.Dispose();
            _backButtonMappingMutationGate.Dispose();
            _productionSettingsMutationGate.Dispose();
            _controllerVibrationMutationGate.Dispose();
            _lifetime.Dispose();
        }
    }
}
