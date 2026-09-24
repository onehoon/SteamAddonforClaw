using System.Buffers.Binary;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.FrontendTransport;

internal static class OverlayTransportProtocol
{
    // Version 3 (OQ4): adds the Runtime -> Overlay Navigation message and OverlayNavigationAction.
    // Semantic, edge-driven navigation only -- no ControllerState / buttons / sticks / raw reports
    // ever cross this wire. A v2 peer must fail the handshake rather than silently ignore Navigation.
    // Version 4 (OQ5-UI-02): adds PreviousTab / NextTab semantic actions for LB/RB tab navigation.
    // Version 5 (OQ5-UI-09): adds the pre-Ready tab-order frame invariant. The Runtime sends the
    // authoritative order right after HandshakeAccepted and the Overlay must apply it before it reports Ready. No fallback --
    // a v4 peer must fail the handshake.
    // Version 6 (SF-V2-02): adds typed Device Quick Settings state delivery
    // (DeviceQuickSettingsState) and the explicit CPU Boost/TDP/Power Mode mutation
    // request/result messages (DeviceMutationRequest / DeviceMutationResult). A v5 peer must fail
    // the handshake rather than silently miss the new frames.
    // Version 7 (SF-V2-06): replaces the pre-release v6 Device-specific Quick Settings state/mutation
    // wire (DeviceQuickSettingsState / DeviceMutationRequest / DeviceMutationResult,
    // OverlayDeviceMutationKind/Request/Response, OverlayDeviceMutationDispatch) with the shared
    // QuickSettingsPageSnapshot / QuickSettingsMutationIntent / QuickSettingsMutationResult contract
    // already consumed by the Main UI / Overlay (SF-V2-04/05), inside narrow transport correlation wrappers.
    // A v6 peer must fail the handshake rather than silently misinterpret the replaced frames.
    // Pre-release: no compatibility shim.
    // Version 8 (shared-surface PR3): replaces raw whole-order Setting state/mutation with the typed
    // AddonQuickSettingsTabOrderSnapshot and one-position move intent/result contract. A v7 peer must
    // fail the handshake rather than send complete-order requests to the new Runtime seam.
    // Version 9: shared Quick Settings rows carry renderer-only Visible metadata for current
    // AC/DC projection. Hidden rows remain in the authoritative page and grouped drafts.
    // Version 10 (CH-A3): adds Runtime-owned ClawHUD snapshot publication and correlated
    // top-level/nested ClawHUD mutations. A v9 peer must fail the handshake; no compatibility shim.
    // Version 11: adds the narrow Profile catalog and selected Profile page request flows. A v10
    // peer must fail the handshake; no compatibility shim.
    // Version 12: adds Runtime-owned sanitized Shortcut state, TileId-only execution requests, and
    // correlated execution results. A v11 peer must fail the handshake; no compatibility shim.
    internal const int CurrentVersion = 12;
    internal const int MaxFrameBytes = 512 * 1024;
}

internal enum OverlayWireMessageKind { Handshake, HandshakeAccepted, Command, Navigation, State, DismissRequested, ProtocolError, TabOrderState, TabOrderMoveRequest, TabOrderMoveResult, QuickSettingsPageState, QuickSettingsMutationRequest, QuickSettingsMutationResult, ClawHudState, ClawHudMutationRequest, ClawHudMutationResult, ProfileCatalogRequest, ProfileCatalogState, ProfilePageRequest, ProfilePageResult, ShortcutState, ShortcutExecuteRequest, ShortcutExecuteResult }
internal enum OverlayCommand { Show, Hide, Shutdown }
internal enum OverlayNavigationAction { NavigateUp, NavigateDown, NavigateLeft, NavigateRight, Accept, Back, PreviousTab, NextTab }
internal enum OverlayState { Ready, Visible, Hidden }

/// <summary>Overlay -> Runtime shared Quick Settings mutation request (SF-V2-06 section 9.2).
/// Correlation is transport-specific and deliberately not part of the shared product contract --
/// <see cref="Intent"/> is the exact same closed <see cref="QuickSettingsMutationIntent"/> shared by
/// the Main UI and Overlay transports.</summary>
internal sealed record OverlayQuickSettingsMutationRequest(long RequestId, QuickSettingsMutationIntent Intent);

/// <summary>Runtime -> Overlay reply to one <see cref="OverlayQuickSettingsMutationRequest"/>. A
/// valid Runtime result -- including a normal typed feature failure -- arrives as <see cref="Result"/>;
/// <see cref="Error"/> is reserved for a thrown operation/transport-side failure, never a second copy
/// of <see cref="QuickSettingsMutationResult"/>'s own Succeeded/FailureMessage shape.</summary>
internal sealed record OverlayQuickSettingsMutationResponse(long RequestId, QuickSettingsMutationResult? Result = null, string? Error = null);

internal sealed record OverlayClawHudMutationRequest(long RequestId, bool? Enabled = null, FrontendClawHudMutationIntent? Intent = null);
internal sealed record OverlayClawHudMutationResponse(long RequestId, FrontendClawHudMutationResult? Result = null, string? Error = null);
internal sealed record OverlayProfileCatalogState(IReadOnlyList<FrontendProfileGameCatalogEntry> Entries, string? Error = null);
internal sealed record OverlayProfilePageRequest(uint AppId);
internal sealed record OverlayProfilePageResponse(uint AppId, QuickSettingsPageSnapshot Page, string? Error = null);
internal sealed record OverlayShortcutExecuteRequest(long RequestId, Guid TileId);
internal sealed record OverlayShortcutExecutionOutcome(bool Succeeded, string? FailureMessage = null);
internal sealed record OverlayShortcutExecuteResponse(
    long RequestId,
    Guid TileId,
    bool Succeeded,
    string? FailureMessage,
    FrontendShortcutDashboardSnapshot Snapshot);

internal sealed record OverlayWireMessage(
    int ProtocolVersion,
    OverlayWireMessageKind Kind,
    OverlayCommand? Command = null,
    OverlayNavigationAction? Navigation = null,
    OverlayState? State = null,
    string? Error = null,
    AddonQuickSettingsTabOrderSnapshot? TabOrderState = null,
    AddonQuickSettingsTabOrderMoveIntent? TabOrderMove = null,
    AddonQuickSettingsTabOrderMutationResult? TabOrderMutationResult = null,
    QuickSettingsPageSnapshot? QuickSettingsPage = null,
    OverlayQuickSettingsMutationRequest? QuickSettingsMutationRequest = null,
    OverlayQuickSettingsMutationResponse? QuickSettingsMutationResponse = null,
    FrontendClawHudSnapshot? ClawHudState = null,
    OverlayClawHudMutationRequest? ClawHudMutationRequest = null,
    OverlayClawHudMutationResponse? ClawHudMutationResponse = null,
    OverlayProfileCatalogState? ProfileCatalogState = null,
    OverlayProfilePageRequest? ProfilePageRequest = null,
    OverlayProfilePageResponse? ProfilePageResult = null,
    FrontendShortcutDashboardSnapshot? ShortcutState = null,
    OverlayShortcutExecuteRequest? ShortcutExecuteRequest = null,
    OverlayShortcutExecuteResponse? ShortcutExecuteResult = null);

/// <summary>SF-V2-06 section 10/11: the Overlay transport's own job is only wire-structural safety --
/// "is this a closed Quick Settings message that can be handed to the shared adapter without a
/// null-reference/enum-default hazard?" -- never product validation (row/value/TDP-group shape,
/// which/whether a row is mutable). That remains <c>QuickSettingsMutationAdapter</c>'s job.</summary>
internal static class OverlayQuickSettingsWireValidation
{
    internal static bool IsStructurallyValid(AddonQuickSettingsTabOrderSnapshot? state)
    {
        if (state is null) return false;
        if (!state.Available) return state.Rows is { Count: 0 };
        if (state.Rows is not { Count: 5 } rows) return false;
        var order = rows.Select(row => row.TabId).ToArray();
        if (!AddonQuickSettingsTabOrderContract.TryNormalize(order, out _)) return false;
        var expected = AddonQuickSettingsTabOrderProduct.Create(order).Rows;
        return rows.SequenceEqual(expected);
    }

    internal static bool IsStructurallyValid(AddonQuickSettingsTabOrderMoveIntent? intent) =>
        intent is not null && Enum.IsDefined(intent.TabId) && intent.Delta is -1 or 1;

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

    internal static bool IsStructurallyValid(OverlayProfileCatalogState? state) =>
        state is { Entries: not null } && state.Entries.All(entry =>
            entry is { AppId: > 0 } && !string.IsNullOrWhiteSpace(entry.Name) && Enum.IsDefined(entry.Source));

    internal static bool IsStructurallyValid(OverlayProfilePageRequest? request) => request is { AppId: > 0 };

    internal static bool IsStructurallyValid(OverlayProfilePageResponse? response) =>
        response is { AppId: > 0, Page: { } page } &&
        page.PageId == QuickSettingsPageId.Profile && page.AppId == response.AppId && IsStructurallyValid(page);

    internal static bool HasProfilePayload(OverlayWireMessage message) =>
        message.ProfileCatalogState is not null || message.ProfilePageRequest is not null || message.ProfilePageResult is not null;

    /// <summary>Section 11: the Overlay client must fail closed on a malformed outbound page frame
    /// rather than pass null collections into the future SF-V2-07 renderer. Narrow structural safety
    /// only -- not a re-run of the shared product projection's semantic validation.</summary>
    internal static bool IsStructurallyValid(QuickSettingsPageSnapshot? page) =>
        page is { Sections: { } sections, LinkedSliderConstraints: not null } &&
        sections.All(section => section is { Rows: not null } && section.Rows.All(row => row is not null));
}

internal static class OverlayShortcutWireValidation
{
    private const string OversizedSnapshotMessage = "Shortcut configuration is too large to display.";
    private const string UnavailableMessage = "Shortcut settings are unavailable.";
    private const string ExecutionFailedMessage = "Shortcut could not be executed.";

    internal static bool IsStructurallyValid(FrontendShortcutDashboardSnapshot? snapshot) =>
        snapshot is { Tiles: { } tiles }
        && tiles.All(tile => tile is not null
            && tile.TileId != Guid.Empty
            && tile.Title is not null
            && Enum.IsDefined(tile.State))
        && tiles.Select(tile => tile.TileId).Distinct().Count() == tiles.Count;

    internal static bool IsStructurallyValid(OverlayShortcutExecuteRequest? request) =>
        request is { RequestId: > 0 } && request.TileId != Guid.Empty;

    internal static bool IsStructurallyValid(OverlayShortcutExecuteResponse? response) =>
        response is { RequestId: > 0, Snapshot: { } snapshot }
        && response.TileId != Guid.Empty
        && IsStructurallyValid(snapshot);

    internal static bool IsValidStateMessage(OverlayWireMessage message) =>
        message.ProtocolVersion == OverlayTransportProtocol.CurrentVersion
        && message.Kind == OverlayWireMessageKind.ShortcutState
        && message.ShortcutState is { } snapshot
        && IsStructurallyValid(snapshot)
        && !HasNonShortcutPayload(message)
        && message.ShortcutExecuteRequest is null
        && message.ShortcutExecuteResult is null;

    internal static bool IsValidExecuteRequestMessage(OverlayWireMessage message) =>
        message.ProtocolVersion == OverlayTransportProtocol.CurrentVersion
        && message.Kind == OverlayWireMessageKind.ShortcutExecuteRequest
        && IsStructurallyValid(message.ShortcutExecuteRequest)
        && !HasNonShortcutPayload(message)
        && message.ShortcutState is null
        && message.ShortcutExecuteResult is null;

    internal static bool IsValidExecuteResultMessage(OverlayWireMessage message) =>
        message.ProtocolVersion == OverlayTransportProtocol.CurrentVersion
        && message.Kind == OverlayWireMessageKind.ShortcutExecuteResult
        && IsStructurallyValid(message.ShortcutExecuteResult)
        && !HasNonShortcutPayload(message)
        && message.ShortcutState is null
        && message.ShortcutExecuteRequest is null;

    internal static bool HasShortcutPayload(OverlayWireMessage message) =>
        message.ShortcutState is not null
        || message.ShortcutExecuteRequest is not null
        || message.ShortcutExecuteResult is not null;

    internal static OverlayWireMessage CreateBoundedStateMessage(FrontendShortcutDashboardSnapshot? snapshot)
    {
        var safeSnapshot = IsStructurallyValid(snapshot) ? snapshot! : FrontendShortcutDashboardSnapshot.Unavailable(UnavailableMessage);
        var message = new OverlayWireMessage(OverlayTransportProtocol.CurrentVersion,
            OverlayWireMessageKind.ShortcutState, ShortcutState: safeSnapshot);
        if (OverlayWireCodec.GetSerializedLength(message) <= OverlayTransportProtocol.MaxFrameBytes)
            return message;

        return new OverlayWireMessage(OverlayTransportProtocol.CurrentVersion,
            OverlayWireMessageKind.ShortcutState,
            ShortcutState: FrontendShortcutDashboardSnapshot.Unavailable(OversizedSnapshotMessage));
    }

    internal static OverlayWireMessage CreateBoundedResultMessage(OverlayShortcutExecuteResponse response)
    {
        if (!IsStructurallyValid(response))
            throw new FrontendProtocolException("Invalid Overlay Shortcut execution result.");

        var message = new OverlayWireMessage(OverlayTransportProtocol.CurrentVersion,
            OverlayWireMessageKind.ShortcutExecuteResult, ShortcutExecuteResult: response);
        if (OverlayWireCodec.GetSerializedLength(message) <= OverlayTransportProtocol.MaxFrameBytes)
            return message;

        var bounded = response with
        {
            Snapshot = FrontendShortcutDashboardSnapshot.Unavailable(OversizedSnapshotMessage)
        };
        message = new OverlayWireMessage(OverlayTransportProtocol.CurrentVersion,
            OverlayWireMessageKind.ShortcutExecuteResult, ShortcutExecuteResult: bounded);
        if (OverlayWireCodec.GetSerializedLength(message) <= OverlayTransportProtocol.MaxFrameBytes)
            return message;

        bounded = bounded with { FailureMessage = bounded.Succeeded ? null : ExecutionFailedMessage };
        message = new OverlayWireMessage(OverlayTransportProtocol.CurrentVersion,
            OverlayWireMessageKind.ShortcutExecuteResult, ShortcutExecuteResult: bounded);
        if (OverlayWireCodec.GetSerializedLength(message) <= OverlayTransportProtocol.MaxFrameBytes)
            return message;

        throw new FrontendProtocolException("Overlay Shortcut execution result exceeds the frame limit.");
    }

    private static bool HasNonShortcutPayload(OverlayWireMessage message) =>
        message.Command is not null
        || message.Navigation is not null
        || message.State is not null
        || message.Error is not null
        || message.TabOrderState is not null
        || message.TabOrderMove is not null
        || message.TabOrderMutationResult is not null
        || message.QuickSettingsPage is not null
        || message.QuickSettingsMutationRequest is not null
        || message.QuickSettingsMutationResponse is not null
        || message.ClawHudState is not null
        || message.ClawHudMutationRequest is not null
        || message.ClawHudMutationResponse is not null
        || message.ProfileCatalogState is not null
        || message.ProfilePageRequest is not null
        || message.ProfilePageResult is not null;
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
        var payload = Serialize(message);
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

    internal static int GetSerializedLength(OverlayWireMessage message) => Serialize(message).Length;

    private static byte[] Serialize(OverlayWireMessage message) => JsonSerializer.SerializeToUtf8Bytes(message, Json);

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
        Func<Guid, CancellationToken, Task<OverlayShortcutExecutionOutcome>>? executeShortcut = null)
        : this(pipeName, () => new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly), captureTabOrder, moveTabOrder, mutateQuickSettings,
            captureClawHud, setClawHudEnabled, mutateClawHudSetting, scanProfileGames, captureProfilePage, captureShortcut, executeShortcut)
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
        Func<Guid, CancellationToken, Task<OverlayShortcutExecutionOutcome>>? executeShortcut = null)
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
        if (hello.Kind != OverlayWireMessageKind.Handshake || hello.ProtocolVersion != OverlayTransportProtocol.CurrentVersion)
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

            if (message.Kind == OverlayWireMessageKind.DismissRequested && message.Command is null && message.Navigation is null && message.State is null && message.Error is null && message.TabOrderState is null && message.TabOrderMove is null && message.TabOrderMutationResult is null && message.QuickSettingsPage is null && message.QuickSettingsMutationRequest is null && message.QuickSettingsMutationResponse is null && message.ClawHudState is null && message.ClawHudMutationRequest is null && message.ClawHudMutationResponse is null && message.ProfileCatalogState is null && message.ProfilePageRequest is null && message.ProfilePageResult is null && !OverlayShortcutWireValidation.HasShortcutPayload(message))
            {
                DismissRequested?.Invoke(this);
                continue;
            }

            if (message.Kind == OverlayWireMessageKind.TabOrderMoveRequest)
            {
                if (!OverlayQuickSettingsWireValidation.IsStructurallyValid(message.TabOrderMove) || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMutationResult is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationRequest is not null || message.QuickSettingsMutationResponse is not null || message.ClawHudState is not null || message.ClawHudMutationRequest is not null || message.ClawHudMutationResponse is not null || OverlayQuickSettingsWireValidation.HasProfilePayload(message) || OverlayShortcutWireValidation.HasShortcutPayload(message))
                    throw new FrontendProtocolException("Invalid Overlay tab-order move message.");
                _ = HandleTabOrderMoveRequestAsync(pipe, message.TabOrderMove!, connection.Token);
                continue;
            }

            if (message.Kind == OverlayWireMessageKind.QuickSettingsMutationRequest)
            {
                if (message.QuickSettingsMutationRequest is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationResponse is not null || message.ClawHudState is not null || message.ClawHudMutationRequest is not null || message.ClawHudMutationResponse is not null || OverlayQuickSettingsWireValidation.HasProfilePayload(message) || OverlayShortcutWireValidation.HasShortcutPayload(message))
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
                if (message.ClawHudMutationRequest is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationRequest is not null || message.QuickSettingsMutationResponse is not null || message.ClawHudState is not null || message.ClawHudMutationResponse is not null || OverlayQuickSettingsWireValidation.HasProfilePayload(message) || OverlayShortcutWireValidation.HasShortcutPayload(message))
                    throw new FrontendProtocolException("Invalid Overlay ClawHUD mutation request.");
                _ = HandleClawHudMutationRequestAsync(pipe, message.ClawHudMutationRequest, connection.Token);
                continue;
            }

            if (message.Kind == OverlayWireMessageKind.ProfileCatalogRequest)
            {
                if (message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationRequest is not null || message.QuickSettingsMutationResponse is not null || message.ClawHudState is not null || message.ClawHudMutationRequest is not null || message.ClawHudMutationResponse is not null || message.ProfileCatalogState is not null || message.ProfilePageRequest is not null || message.ProfilePageResult is not null || OverlayShortcutWireValidation.HasShortcutPayload(message))
                    throw new FrontendProtocolException("Invalid Overlay Profile catalog request.");
                _ = HandleProfileCatalogRequestAsync(pipe, connection.Token);
                continue;
            }

            if (message.Kind == OverlayWireMessageKind.ProfilePageRequest)
            {
                if (!OverlayQuickSettingsWireValidation.IsStructurallyValid(message.ProfilePageRequest) || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationRequest is not null || message.QuickSettingsMutationResponse is not null || message.ClawHudState is not null || message.ClawHudMutationRequest is not null || message.ClawHudMutationResponse is not null || message.ProfileCatalogState is not null || message.ProfilePageResult is not null || OverlayShortcutWireValidation.HasShortcutPayload(message))
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

            if (message.Kind != OverlayWireMessageKind.State || message.State is null || OverlayQuickSettingsWireValidation.HasProfilePayload(message) || OverlayShortcutWireValidation.HasShortcutPayload(message))
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
        try
        {
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
                if (message.Kind == OverlayWireMessageKind.TabOrderMoveResult)
                {
                    if (!ValidateTabOrderMutationResult(message) || OverlayShortcutWireValidation.HasShortcutPayload(message))
                        throw new FrontendProtocolException("Invalid Overlay tab-order mutation result.");
                    TaskCompletionSource<AddonQuickSettingsTabOrderMutationResult>? pending;
                    lock (_tabOrderMoveSync) pending = _pendingTabOrderMove;
                    pending?.TrySetResult(message.TabOrderMutationResult!);
                    continue;
                }
                if (message.Kind == OverlayWireMessageKind.Navigation)
                {
                    if (message.Navigation is null || message.Command is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || OverlayQuickSettingsWireValidation.HasProfilePayload(message) || OverlayShortcutWireValidation.HasShortcutPayload(message))
                        throw new FrontendProtocolException("Invalid Overlay navigation message.");
                    if (navigationHandler is not null)
                        await navigationHandler(message.Navigation.Value).ConfigureAwait(false);
                    continue;
                }
                if (message.Kind == OverlayWireMessageKind.QuickSettingsPageState)
                {
                    if (message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || message.QuickSettingsMutationRequest is not null || message.QuickSettingsMutationResponse is not null || message.ClawHudState is not null || message.ClawHudMutationRequest is not null || message.ClawHudMutationResponse is not null || OverlayQuickSettingsWireValidation.HasProfilePayload(message) || OverlayShortcutWireValidation.HasShortcutPayload(message))
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
                    if (message.ClawHudState is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationRequest is not null || message.QuickSettingsMutationResponse is not null || message.ClawHudMutationRequest is not null || message.ClawHudMutationResponse is not null || OverlayQuickSettingsWireValidation.HasProfilePayload(message) || OverlayShortcutWireValidation.HasShortcutPayload(message) || !IsStructurallyValid(message.ClawHudState))
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
                if (message.Kind == OverlayWireMessageKind.ProfileCatalogState)
                {
                    if (message.ProfileCatalogState is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationRequest is not null || message.QuickSettingsMutationResponse is not null || message.ClawHudState is not null || message.ClawHudMutationRequest is not null || message.ClawHudMutationResponse is not null || message.ProfilePageRequest is not null || message.ProfilePageResult is not null || OverlayShortcutWireValidation.HasShortcutPayload(message) || !OverlayQuickSettingsWireValidation.IsStructurallyValid(message.ProfileCatalogState))
                        throw new FrontendProtocolException("Invalid Overlay Profile catalog state message.");
                    if (profileCatalogHandler is not null)
                        await profileCatalogHandler(message.ProfileCatalogState).ConfigureAwait(false);
                    continue;
                }
                if (message.Kind == OverlayWireMessageKind.ProfilePageResult)
                {
                    if (!OverlayQuickSettingsWireValidation.IsStructurallyValid(message.ProfilePageResult) || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationRequest is not null || message.QuickSettingsMutationResponse is not null || message.ClawHudState is not null || message.ClawHudMutationRequest is not null || message.ClawHudMutationResponse is not null || message.ProfileCatalogState is not null || message.ProfilePageRequest is not null || OverlayShortcutWireValidation.HasShortcutPayload(message))
                        throw new FrontendProtocolException("Invalid Overlay Profile page result message.");
                    if (profilePageHandler is not null)
                        await profilePageHandler(message.ProfilePageResult!).ConfigureAwait(false);
                    continue;
                }
                if (message.Kind == OverlayWireMessageKind.QuickSettingsMutationResult)
                {
                    if (message.QuickSettingsMutationResponse is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationRequest is not null || message.ClawHudState is not null || message.ClawHudMutationRequest is not null || message.ClawHudMutationResponse is not null || OverlayQuickSettingsWireValidation.HasProfilePayload(message) || OverlayShortcutWireValidation.HasShortcutPayload(message))
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
                    if (!ValidateClawHudMutationResult(message) || OverlayShortcutWireValidation.HasShortcutPayload(message))
                        throw new FrontendProtocolException("Invalid Overlay ClawHUD mutation result.");
                    TaskCompletionSource<OverlayClawHudMutationResponse>? pending;
                    lock (_clawHudMutationSync)
                        pending = message.ClawHudMutationResponse!.RequestId == _pendingClawHudRequestId ? _pendingClawHudMutation : null;
                    pending?.TrySetResult(message.ClawHudMutationResponse);
                    continue;
                }
                if (message.Kind != OverlayWireMessageKind.Command || message.Command is null || message.Navigation is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || message.ProfileCatalogState is not null || message.ProfilePageRequest is not null || message.ProfilePageResult is not null || OverlayShortcutWireValidation.HasShortcutPayload(message))
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
        }
    }

    private async Task SendStateAsync(Stream pipe, OverlayState state, CancellationToken token) =>
        await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.State, State: state), _writeGate, token).ConfigureAwait(false);

    private static AddonQuickSettingsTabOrderSnapshot ValidateTabOrderMessage(OverlayWireMessage message)
    {
        if (message.TabOrderState is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderMove is not null || message.TabOrderMutationResult is not null || OverlayQuickSettingsWireValidation.HasProfilePayload(message) || OverlayShortcutWireValidation.HasShortcutPayload(message))
            throw new FrontendProtocolException("Invalid Overlay tab-order message.");
        if (!OverlayQuickSettingsWireValidation.IsStructurallyValid(message.TabOrderState))
            throw new FrontendProtocolException("Overlay tab-order state was malformed.");
        return message.TabOrderState;
    }

    private static bool ValidateTabOrderMutationResult(OverlayWireMessage message)
    {
        if (message.TabOrderMutationResult is null || message.Command is not null || message.Navigation is not null || message.State is not null || message.Error is not null || message.TabOrderState is not null || message.TabOrderMove is not null || message.QuickSettingsPage is not null || message.QuickSettingsMutationRequest is not null || message.QuickSettingsMutationResponse is not null || OverlayQuickSettingsWireValidation.HasProfilePayload(message) || OverlayShortcutWireValidation.HasShortcutPayload(message))
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

            // Let the one pending Shortcut request observe cancellation and release its gate before
            // disposing that gate.
            await _shortcutExecutionGate.WaitAsync().ConfigureAwait(false);
            _shortcutExecutionGate.Release();

            _writeGate.Dispose();
            _quickSettingsMutationGate.Dispose();
            _clawHudMutationGate.Dispose();
            _tabOrderMoveGate.Dispose();
            _shortcutExecutionGate.Dispose();
            _lifetime.Dispose();
        }
    }
}
