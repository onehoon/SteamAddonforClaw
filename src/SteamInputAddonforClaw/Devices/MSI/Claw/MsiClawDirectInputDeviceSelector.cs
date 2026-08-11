using SteamInputAddonforClaw.Input.DirectInput;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal enum MsiClawDirectInputSelectionStatus
{
    Selected = 0,
    NotFound = 1,
    Indeterminate = 2
}

internal sealed record MsiClawDirectInputSelectionResult(
    MsiClawDirectInputSelectionStatus Status,
    DirectInputDeviceDescriptor? Descriptor,
    int CandidateCount,
    string Reason)
{
    internal bool IsSelected => Status == MsiClawDirectInputSelectionStatus.Selected && Descriptor is not null;
}

internal static class MsiClawDirectInputDeviceSelector
{
    internal static MsiClawDirectInputSelectionResult Select(IReadOnlyList<DirectInputDeviceDescriptor> devices)
    {
        var candidates = devices.Where(device => MsiClawHardware.IsDirectInputController(device.VendorId, device.ProductId)).ToArray();
        if (candidates.Length == 0)
            return new(MsiClawDirectInputSelectionStatus.NotFound, null, 0, "Pid1902NotFound");
        if (candidates.Any(candidate => !HasVerifiedIdentity(candidate)))
            return new(MsiClawDirectInputSelectionStatus.Indeterminate, null, candidates.Length, "PhysicalIdentityUnverified");
        if (candidates.Any(candidate => candidate.ButtonCount is null or < MsiClawHardware.RequiredDirectInputButtonCount))
            return new(MsiClawDirectInputSelectionStatus.Indeterminate, null, candidates.Length, "InsufficientButtonCount");
        if (candidates.Select(candidate => candidate.PhysicalIdentity!).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
            return new(MsiClawDirectInputSelectionStatus.Indeterminate, null, candidates.Length, "MultiplePhysicalIdentities");
        if (candidates.Select(candidate => candidate.PnpInstanceId!).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
            return new(MsiClawDirectInputSelectionStatus.Indeterminate, null, candidates.Length, "MultiplePnpInstanceIdentities");

        var descriptor = candidates.OrderBy(candidate => candidate.InstanceGuid).First();
        var reason = candidates.Length == 1 ? "VerifiedMsiPhysicalRoot" : "VerifiedMsiPhysicalRootAlias";
        return new(MsiClawDirectInputSelectionStatus.Selected, descriptor, candidates.Length, reason);
    }

    private static bool HasVerifiedIdentity(DirectInputDeviceDescriptor descriptor) =>
        !string.IsNullOrWhiteSpace(descriptor.DevicePath)
        && !string.IsNullOrWhiteSpace(descriptor.PnpInstanceId)
        && !string.IsNullOrWhiteSpace(descriptor.PhysicalIdentity)
        && descriptor.UsagePage == MsiClawHardware.DirectInputUsagePage
        && descriptor.Usage == MsiClawHardware.DirectInputUsage;
}
