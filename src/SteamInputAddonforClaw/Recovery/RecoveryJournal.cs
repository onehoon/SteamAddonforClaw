using SteamInputAddonforClaw.Controllers;

namespace SteamInputAddonforClaw.Recovery;

internal sealed record RecoveryMutationState(
    bool ControllerModeChanged = false,
    IReadOnlyList<string>? HidHideDeviceAdditions = null,
    IReadOnlyList<string>? ExecutableWhitelistAdditions = null,
    IReadOnlyList<string>? AddonOwnedVirtualDevices = null,
    bool TemporaryXbox360OutputCreated = false)
{
    public bool HasRecordedMutations => ControllerModeChanged || TemporaryXbox360OutputCreated ||
        HidHideDeviceAdditions is { Count: > 0 } || ExecutableWhitelistAdditions is { Count: > 0 } || AddonOwnedVirtualDevices is { Count: > 0 };
}

internal sealed record RecoveryJournal(
    int SchemaVersion,
    Guid RecoverySessionId,
    DateTimeOffset CreatedAt,
    MsiControllerSnapshot OriginalControllerState,
    RecoveryMutationState Mutations);

internal enum RecoveryStatus { NoRecoveryNeeded, Success, Failure }
internal sealed record RecoveryResult(RecoveryStatus Status, string Reason, RecoveryJournal? Journal = null)
{
    public bool IsSafeToContinue => Status is RecoveryStatus.NoRecoveryNeeded or RecoveryStatus.Success;
}
