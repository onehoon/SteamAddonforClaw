namespace SteamInputAddonforClaw.Recovery;

// DTO for schema-v1 files only. It is intentionally kept outside the v2 recovery model.
internal sealed record LegacyRecoveryJournalV1(
    int SchemaVersion,
    Guid RecoverySessionId,
    DateTimeOffset CreatedAt,
    object? OriginalControllerState,
    LegacyRecoveryMutationStateV1? Mutations);

internal sealed record LegacyRecoveryMutationStateV1(
    bool ControllerModeChanged = false,
    IReadOnlyList<string>? HidHideDeviceAdditions = null,
    IReadOnlyList<string>? ExecutableWhitelistAdditions = null,
    IReadOnlyList<string>? AddonOwnedVirtualDevices = null,
    bool TemporaryXbox360OutputCreated = false);
