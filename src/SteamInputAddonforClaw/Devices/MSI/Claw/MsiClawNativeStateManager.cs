using System.Diagnostics;
using System.Text.Json;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal enum MsiClawNativeMode { XInput, DirectInput, Other }

internal sealed class MsiClawNativeStateManager(IControllerDeviceEnumerator deviceEnumerator, IMsiClawModeController? modeController = null) : INativeControllerStateManager
{
    internal const int SnapshotFormatVersion = 1;
    public HandheldDeviceId DeviceId { get; } = new("msi.claw");
    internal Task<MsiClawModeTransitionResult> SwitchModeAsync(MsiClawNativeMode target, MsiClawPhysicalIdentity identity, CancellationToken cancellationToken) =>
        modeController is null
            ? Task.FromResult(new MsiClawModeTransitionResult(MsiClawModeTransitionStatus.UnsupportedDevice, MsiClawNativeMode.Other, target, null, null, false, false, false, false, 0, "ModeControllerUnavailable"))
            : modeController.SwitchModeAsync(target, identity, cancellationToken);

    public NativeStateCaptureResult CaptureSnapshot()
    {
        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<ControllerDeviceInfo> devices;
        try { devices = deviceEnumerator.EnumeratePresentDevices(); }
        catch (Exception exception)
        {
            AppLog.Error("NativeState", "MSI Claw PnP enumeration failed.", exception, ("Action", "Passive"));
            return new(NativeStateCaptureStatus.Failed, null, exception.Message);
        }

        var candidates = devices.Where(device => device.Present && MsiClawHardware.IsKnownController(device.VendorId, device.ProductId)).ToList();
        if (candidates.Count == 0)
            return new(NativeStateCaptureStatus.DeviceNotFound, null, "No known MSI Claw controller identity is present.");

        var logicalCandidates = candidates.GroupBy(LogicalIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(device => device.InstanceId, StringComparer.OrdinalIgnoreCase).ToList()).ToList();
        if (logicalCandidates.Count != 1)
        {
            AppLog.Warn("NativeState", "MSI Claw native-state capture indeterminate.", null,
                ("CaptureStatus", NativeStateCaptureStatus.Indeterminate),
                ("Reason", "MultipleLogicalMsiControllers"),
                ("CandidateCount", candidates.Count),
                ("LogicalGroupCount", logicalCandidates.Count),
                ("LogicalGroupKeys", string.Join(" | ", logicalCandidates.Select(group => LogicalIdentity(group[0])))),
                ("Action", "Passive"));
            return new(NativeStateCaptureStatus.Indeterminate, null, "Multiple logical MSI controller candidates are present.");
        }

        var selectedLogicalCandidate = logicalCandidates.Single();
        var products = selectedLogicalCandidate.Select(device => device.ProductId).Distinct().ToList();
        if (products.Count != 1 || ModeFor(products[0]) is not { } mode)
            return new(NativeStateCaptureStatus.Indeterminate, null, "MSI interfaces do not identify one exact restorable state.");

        var selected = selectedLogicalCandidate.First();
        var identity = MsiClawPhysicalIdentity.From(selected);
        var payload = new MsiClawNativeStatePayload(mode, selected.InstanceId, selected.ParentInstanceId, selected.ContainerId, selected.ProductId, identity.Confidence, identity.PhysicalDeviceKey);
        var snapshot = new DeviceNativeStateSnapshot(DeviceId, SnapshotFormatVersion, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(payload));
        AppLog.Info("NativeState", "MSI Claw native-state candidates grouped.",
            ("CandidateCount", candidates.Count),
            ("LogicalGroupCount", logicalCandidates.Count),
            ("Mode", mode),
            ("ResolvedPhysicalRoot", MsiClawPhysicalIdentity.ResolvePhysicalRoot(selected)?.RawRootInstanceId ?? "Unavailable"),
            ("IdentityConfidence", identity.Confidence),
            ("ElapsedMs", stopwatch.ElapsedMilliseconds));
        return new(NativeStateCaptureStatus.Success, snapshot, "Snapshot captured.");
    }

    public async Task<NativeStateRestoreResult> RestoreSnapshotAsync(DeviceNativeStateSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot is null || snapshot.DeviceId != DeviceId)
            return new NativeStateRestoreResult(NativeStateRestoreStatus.Failed, "SnapshotDeviceIdMismatch");
        if (snapshot.FormatVersion != SnapshotFormatVersion)
            return new NativeStateRestoreResult(NativeStateRestoreStatus.Unsupported, "UnsupportedSnapshotFormatVersion");
        MsiClawNativeStatePayload? original;
        try { original = snapshot.Payload.Deserialize<MsiClawNativeStatePayload>(); }
        catch (JsonException) { return new NativeStateRestoreResult(NativeStateRestoreStatus.Failed, "MalformedSnapshotPayload"); }
        if (original is null || original.ProductId is null)
            return new NativeStateRestoreResult(NativeStateRestoreStatus.Failed, "MalformedSnapshotPayload");

        var current = CaptureSnapshot();
        if (!current.AllowsMutation || current.Snapshot is null)
            return new NativeStateRestoreResult(current.Status == NativeStateCaptureStatus.Indeterminate ? NativeStateRestoreStatus.Indeterminate : NativeStateRestoreStatus.Failed, current.Reason);
        var currentPayload = current.Snapshot.Payload.Deserialize<MsiClawNativeStatePayload>();
        if (currentPayload is null) return new NativeStateRestoreResult(NativeStateRestoreStatus.Failed, "MalformedCurrentPayload");
        if (currentPayload == original || PayloadMatchesOriginalState(currentPayload, original))
            return new NativeStateRestoreResult(NativeStateRestoreStatus.Success, "AlreadyOriginalState");
        if (modeController is null) return new NativeStateRestoreResult(NativeStateRestoreStatus.Unsupported, "ModeControllerUnavailable");
        var expected = MsiClawPhysicalIdentity.FromPayload(original);
        if (!PayloadIdentitiesMatch(original, currentPayload)) return new NativeStateRestoreResult(NativeStateRestoreStatus.Indeterminate, "PhysicalIdentityMismatch");
        var result = await modeController.SwitchModeAsync(original.Mode, expected, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) return new NativeStateRestoreResult(NativeStateRestoreStatus.Failed, result.Reason);
        var restored = CaptureSnapshot();
        if (!restored.AllowsMutation || restored.Snapshot is null) return new NativeStateRestoreResult(NativeStateRestoreStatus.Indeterminate, "RestoredStateCouldNotBeVerified");
        var restoredPayload = restored.Snapshot.Payload.Deserialize<MsiClawNativeStatePayload>();
        return restoredPayload is not null && PayloadMatchesOriginalState(restoredPayload, original)
            ? new NativeStateRestoreResult(NativeStateRestoreStatus.Success, "NativeStateRestoredAndVerified")
            : new NativeStateRestoreResult(NativeStateRestoreStatus.Indeterminate, "RestoredStateMismatch");
    }

    private static MsiClawNativeMode? ModeFor(ushort? productId) => productId switch
    {
        MsiClawHardware.XInputProductId => MsiClawNativeMode.XInput,
        MsiClawHardware.DirectInputProductId => MsiClawNativeMode.DirectInput,
        MsiClawHardware.TestingProductId => MsiClawNativeMode.Other,
        _ => null
    };

    private static string LogicalIdentity(ControllerDeviceInfo device)
    {
        if (MsiClawPhysicalIdentity.ResolvePhysicalRoot(device) is { } physicalRoot) return $"root:{physicalRoot.RawRootInstanceId}";
        if (ControllerLogicalIdentity.IsUsableContainerId(device.ContainerId)) return $"container:{device.ContainerId:D}";
        if (!string.IsNullOrWhiteSpace(device.ParentInstanceId)) return $"parent:{device.ParentInstanceId}";
        return $"instance:{device.InstanceId}";
    }

    private static bool PayloadMatchesOriginalState(MsiClawNativeStatePayload current, MsiClawNativeStatePayload original) =>
        current.Mode == original.Mode && current.ProductId == original.ProductId && PayloadIdentitiesMatch(original, current);

    private static bool PayloadIdentitiesMatch(MsiClawNativeStatePayload original, MsiClawNativeStatePayload current)
    {
        var expected = MsiClawPhysicalIdentity.FromPayload(original);
        var actual = MsiClawPhysicalIdentity.FromPayload(current);
        if (expected.Confidence != MsiClawIdentityConfidence.Strong || actual.Confidence != MsiClawIdentityConfidence.Strong)
            return false;
        if (!string.IsNullOrWhiteSpace(expected.PhysicalDeviceKey))
            return expected.StronglyMatches(actual);
        return MsiClawPhysicalIdentity.IsUsableContainer(expected.ContainerId) &&
            MsiClawPhysicalIdentity.IsUsableContainer(actual.ContainerId) &&
            expected.ContainerId == actual.ContainerId;
    }
}
