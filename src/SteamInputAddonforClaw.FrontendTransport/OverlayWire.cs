using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Contracts.ControllerLed;
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
    // Version 13: adds Runtime-owned global Xbox360 M1/M2 mapping state and one correlated
    // whole-record mutation. A v12 peer must fail the handshake; no compatibility shim.
    // Version 14 adds shared settings and vibration state/mutation frames.
    // Version 15 adds the Runtime-authorized active-game Profile-first Show command.
    internal const int CurrentVersion = 15;
    internal const int MaxFrameBytes = 512 * 1024;
}

internal enum OverlayWireMessageKind { Handshake, HandshakeAccepted, Command, Navigation, State, DismissRequested, ProtocolError, TabOrderState, TabOrderMoveRequest, TabOrderMoveResult, QuickSettingsPageState, QuickSettingsMutationRequest, QuickSettingsMutationResult, ClawHudState, ClawHudMutationRequest, ClawHudMutationResult, ProfileCatalogRequest, ProfileCatalogState, ProfilePageRequest, ProfilePageResult, ShortcutState, ShortcutExecuteRequest, ShortcutExecuteResult, BackButtonMappingState, BackButtonMappingMutationRequest, BackButtonMappingMutationResult, FrontendSettingsState, ControllerLedMutationRequest, ControllerLedMutationResult, CurrentPowerSourceMutationRequest, CurrentPowerSourceMutationResult, ControllerVibrationState, ControllerVibrationMutationRequest, ControllerVibrationMutationResult }
internal enum OverlayCommand { Show, Hide, Shutdown, ShowActiveProfile }
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

internal sealed record OverlayBackButtonMappingState(
    bool Available,
    BackButtonMappingSettings Mapping,
    string? FailureMessage = null)
{
    internal static OverlayBackButtonMappingState Unavailable() =>
        new(false, BackButtonMappingSettings.Default, "M1 / M2 mapping is unavailable.");
}

internal sealed record OverlayBackButtonMappingMutationRequest(long RequestId, BackButtonMappingSettings Mapping);
internal sealed record OverlayBackButtonMappingMutationOutcome(
    bool Succeeded,
    string? FailureMessage,
    OverlayBackButtonMappingState State);
internal sealed record OverlayBackButtonMappingMutationResponse(
    long RequestId,
    bool Succeeded,
    string? FailureMessage,
    OverlayBackButtonMappingState State);

internal sealed record OverlayFrontendSettingsMutationRequest(long RequestId, ControllerLedSettings? ControllerLed = null, bool? CurrentPowerSourceOnly = null);
internal sealed record OverlayFrontendSettingsMutationResponse(long RequestId, bool Succeeded, string? FailureMessage, FrontendSettingsSnapshot Settings, bool ControllerLedAvailable, bool SettingsAvailable = true);
internal sealed record OverlayControllerVibrationMutationRequest(long RequestId, int LeftPercent, int RightPercent);
internal sealed record OverlayControllerVibrationMutationResponse(long RequestId, FrontendControllerVibrationStrengthMutationResult Result);

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
    OverlayShortcutExecuteResponse? ShortcutExecuteResult = null,
    OverlayBackButtonMappingState? BackButtonMappingState = null,
    OverlayBackButtonMappingMutationRequest? BackButtonMappingMutationRequest = null,
    OverlayBackButtonMappingMutationResponse? BackButtonMappingMutationResponse = null,
    FrontendSettingsSnapshot? FrontendSettingsState = null,
    bool? ControllerLedAvailable = null,
    bool? FrontendSettingsAvailable = null,
    OverlayFrontendSettingsMutationRequest? FrontendSettingsMutationRequest = null,
    OverlayFrontendSettingsMutationResponse? FrontendSettingsMutationResponse = null,
    FrontendControllerVibrationStrengthSnapshot? ControllerVibrationState = null,
    OverlayControllerVibrationMutationRequest? ControllerVibrationMutationRequest = null,
    OverlayControllerVibrationMutationResponse? ControllerVibrationMutationResult = null);
