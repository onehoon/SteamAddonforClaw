using SteamInputAddonforClaw.Devices.Abstractions;

namespace SteamInputAddonforClaw.Recovery;

internal sealed record RecoveryMutationState(
    bool DeviceNativeStateChanged = false,
    IReadOnlyList<string>? HidHideDeviceAdditions = null,
    IReadOnlyList<string>? ExecutableWhitelistAdditions = null,
    IReadOnlyList<string>? AddonOwnedVirtualDevices = null,
    bool TemporaryXbox360OutputCreated = false,
    IReadOnlyList<AddonOwnedVirtualDeviceRecoveryEntry>? AddonOwnedVirtualDeviceEntries = null,
    bool? OriginalHidHideActiveState = null)
{
    public bool HasRecordedMutations => DeviceNativeStateChanged || TemporaryXbox360OutputCreated ||
        HidHideDeviceAdditions is { Count: > 0 } || ExecutableWhitelistAdditions is { Count: > 0 } || AddonOwnedVirtualDevices is { Count: > 0 } || AddonOwnedVirtualDeviceEntries is { Count: > 0 } || OriginalHidHideActiveState is not null;
}

internal sealed record AddonOwnedVirtualDeviceRecoveryEntry(
    Guid MutationId,
    string DeviceType,
    ushort VendorId,
    ushort ProductId,
    IReadOnlyList<string> PreExistingMatchingInstanceIds,
    IReadOnlyList<string> ResolvedInstanceIds);

internal sealed record RecoveryJournal(
    int SchemaVersion,
    Guid RecoverySessionId,
    DateTimeOffset CreatedAt,
    DeviceNativeStateSnapshot? OriginalDeviceState,
    RecoveryMutationState Mutations);

internal enum RecoveryStatus { NoRecoveryNeeded, Success, Failure }
internal sealed record RecoveryResult(RecoveryStatus Status, string Reason, RecoveryJournal? Journal = null)
{
    public bool IsSafeToContinue => Status is RecoveryStatus.NoRecoveryNeeded or RecoveryStatus.Success;
}

internal sealed record StartupResidueCleanupResult(bool IsJournalValid, bool IsNonNativeResidueResolved, string Reason);
