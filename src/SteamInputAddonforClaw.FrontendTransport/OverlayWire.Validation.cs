using System.Linq;
using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.FrontendTransport;

internal static class OverlayBackButtonMappingWireValidation
{
    internal static bool IsStructurallyValid(OverlayBackButtonMappingState? state) =>
        state is { Mapping: not null } && BackButtonMappingValidation.IsValid(state.Mapping);

    internal static bool IsStructurallyValid(OverlayBackButtonMappingMutationRequest? request) =>
        request is { RequestId: > 0, Mapping: not null } && BackButtonMappingValidation.IsValid(request.Mapping);

    internal static bool IsStructurallyValid(OverlayBackButtonMappingMutationResponse? response) =>
        response is { RequestId: > 0, State: not null } && IsStructurallyValid(response.State);

    internal static bool IsValidStateMessage(OverlayWireMessage message) =>
        message.ProtocolVersion == OverlayTransportProtocol.CurrentVersion
        && message.Kind == OverlayWireMessageKind.BackButtonMappingState
        && IsStructurallyValid(message.BackButtonMappingState)
        && message.BackButtonMappingMutationRequest is null
        && message.BackButtonMappingMutationResponse is null
        && !HasNonBackButtonMappingPayload(message);

    internal static bool IsValidMutationRequestMessage(OverlayWireMessage message) =>
        message.ProtocolVersion == OverlayTransportProtocol.CurrentVersion
        && message.Kind == OverlayWireMessageKind.BackButtonMappingMutationRequest
        && IsStructurallyValid(message.BackButtonMappingMutationRequest)
        && message.BackButtonMappingState is null
        && message.BackButtonMappingMutationResponse is null
        && !HasNonBackButtonMappingPayload(message);

    internal static bool IsValidMutationResultMessage(OverlayWireMessage message) =>
        message.ProtocolVersion == OverlayTransportProtocol.CurrentVersion
        && message.Kind == OverlayWireMessageKind.BackButtonMappingMutationResult
        && IsStructurallyValid(message.BackButtonMappingMutationResponse)
        && message.BackButtonMappingState is null
        && message.BackButtonMappingMutationRequest is null
        && !HasNonBackButtonMappingPayload(message);

    internal static bool HasBackButtonMappingPayload(OverlayWireMessage message) =>
        message.BackButtonMappingState is not null
        || message.BackButtonMappingMutationRequest is not null
        || message.BackButtonMappingMutationResponse is not null;

    private static bool HasNonBackButtonMappingPayload(OverlayWireMessage message) =>
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
        || message.ProfilePageResult is not null
        || message.ShortcutState is not null
        || message.ShortcutExecuteRequest is not null
        || message.ShortcutExecuteResult is not null;
}

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
        || message.ProfilePageResult is not null
        || OverlayBackButtonMappingWireValidation.HasBackButtonMappingPayload(message);
}
