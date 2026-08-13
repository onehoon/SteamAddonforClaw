using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ViiperVirtualDeviceIdentityDiagnosticsTests
{
    private static readonly ViiperVirtualDeviceIdentityPolicy Policy = new();

    [Fact]
    public void NewGordonNodeFailingOnlyTopologyIsClassifiedButNotResolved()
    {
        var gordon = Device("USB\\VID_28DE&PID_1102\\NEW", vendorId: 0x28DE, productId: 0x1102, service: "unrelated");
        var resolver = new ViiperVirtualDeviceIdentityResolver(Policy);
        var resolution = resolver.Resolve([], [gordon]);

        Assert.Equal(ViiperVirtualDeviceResolutionStatus.NoNewCandidate, resolution.Status);
        Assert.Equal("VirtualDeviceDidNotAppear", resolution.Reason);

        var evidence = ViiperVirtualDeviceIdentityDiagnostics.Build([], [gordon], Policy, resolution);
        var device = Assert.Single(evidence.Devices);
        Assert.True(device.IsNewSinceBefore);
        Assert.True(device.VidMatch);
        Assert.True(device.PidMatch);
        Assert.True(device.VidPidMatchesGordon);
        Assert.False(device.HasCurrentViiperTopologyEvidence);
        Assert.False(device.CurrentPolicyMatches);
        Assert.Equal(ViiperVirtualDeviceIdentityDiagnostics.MissingViiperTopologyEvidenceRejection, device.Rejection);
    }

    [Fact]
    public void GordonLeafAndUsbipParentAreBothIncludedWithRelationshipVisible()
    {
        // The parent's InstanceId carries no USBIP/VIIPER token -- only its Service field does.
        // Current policy only inspects the leaf's own InstanceId/AncestorInstanceIds/Service, so
        // the parent's Service text is invisible to it: the leaf is still rejected even though a
        // related node clearly carries USBIP evidence. That split is exactly the scenario this
        // diagnostic exists to surface, without changing the policy's decision.
        var parent = Device("ROOT\\GENERIC\\PARENT001", vendorId: null, productId: null, service: "usbipw2co");
        var leaf = Device("USB\\VID_28DE&PID_1102\\LEAF", vendorId: 0x28DE, productId: 0x1102, service: "unrelated", parentInstanceId: parent.InstanceId, ancestorInstanceIds: [parent.InstanceId]);
        var resolver = new ViiperVirtualDeviceIdentityResolver(Policy);
        // The USBIP parent is already present before routing starts (it is the host-side
        // usbip-win2 device, not something VIIPER creates); only the leaf is new. The parent
        // must still show up in diagnostics purely because it is an ancestor of an interesting
        // node (category D), not because it independently matches A/B/C.
        var resolution = resolver.Resolve([parent], [leaf, parent]);

        Assert.Equal(ViiperVirtualDeviceResolutionStatus.NoNewCandidate, resolution.Status);

        var evidence = ViiperVirtualDeviceIdentityDiagnostics.Build([parent], [leaf, parent], Policy, resolution);
        Assert.Equal(2, evidence.Devices.Count);
        var leafEvidence = Assert.Single(evidence.Devices, d => d.Device.InstanceId == leaf.InstanceId);
        var parentEvidence = Assert.Single(evidence.Devices, d => d.Device.InstanceId == parent.InstanceId);
        Assert.Contains($"ChildOf:{parent.InstanceId}", leafEvidence.RelatedInterestingNodes);
        Assert.Contains($"ParentOf:{leaf.InstanceId}", parentEvidence.RelatedInterestingNodes);
        Assert.False(leafEvidence.CurrentPolicyMatches);
    }

    [Fact]
    public void FullyMatchingCandidateReportsNoRejectionAndResolverStillResolves()
    {
        var gordon = Device("USB\\VID_28DE&PID_1102\\OK", vendorId: 0x28DE, productId: 0x1102, service: "usbip");
        var resolver = new ViiperVirtualDeviceIdentityResolver(Policy);
        var resolution = resolver.Resolve([], [gordon]);

        Assert.True(resolution.Succeeded);

        var evidence = ViiperVirtualDeviceIdentityDiagnostics.Build([], [gordon], Policy, resolution);
        var device = Assert.Single(evidence.Devices);
        Assert.True(device.CurrentPolicyMatches);
        Assert.Equal(ViiperVirtualDeviceIdentityDiagnostics.NoneRejection, device.Rejection);
    }

    [Fact]
    public void PreExistingGordonIsNotClassifiedAsNew()
    {
        var gordon = Device("USB\\VID_28DE&PID_1102\\OLD", vendorId: 0x28DE, productId: 0x1102, service: "usbip");
        var resolver = new ViiperVirtualDeviceIdentityResolver(Policy);
        var resolution = resolver.Resolve([gordon], [gordon]);

        Assert.Equal(ViiperVirtualDeviceResolutionStatus.NoNewCandidate, resolution.Status);

        var evidence = ViiperVirtualDeviceIdentityDiagnostics.Build([gordon], [gordon], Policy, resolution);
        var device = Assert.Single(evidence.Devices);
        Assert.False(device.IsNewSinceBefore);
        Assert.Equal(ViiperVirtualDeviceIdentityDiagnostics.PreExistingInstanceRejection, device.Rejection);
    }

    [Fact]
    public void MultipleNewGordonNodesAreEachReportedOnceWithStableKeys()
    {
        var first = Device("USB\\VID_28DE&PID_1102\\ONE", vendorId: 0x28DE, productId: 0x1102, service: "usbip");
        var second = Device("USB\\VID_28DE&PID_1102\\TWO", vendorId: 0x28DE, productId: 0x1102, service: "usbip");
        var resolver = new ViiperVirtualDeviceIdentityResolver(Policy);
        var resolution = resolver.Resolve([], [first, second]);

        Assert.Equal(ViiperVirtualDeviceResolutionStatus.Ambiguous, resolution.Status);

        var evidence = ViiperVirtualDeviceIdentityDiagnostics.Build([], [first, second], Policy, resolution);
        Assert.Equal(2, evidence.Devices.Count);
        Assert.Equal(2, evidence.Devices.Select(d => d.Device.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(evidence.Devices, d => Assert.True(d.CurrentPolicyMatches));
    }

    [Fact]
    public void NullAndEmptyOptionalPropertiesDoNotThrowAndAreStablyRepresented()
    {
        var device = new ControllerDeviceInfo(
            InstanceId: "USB\\VID_28DE&PID_1102\\SPARSE",
            ContainerId: null,
            ParentInstanceId: null,
            AncestorInstanceIds: [],
            EnumeratorName: null,
            HardwareIds: [],
            CompatibleIds: [],
            ClassName: null,
            ClassGuid: null,
            Service: null,
            VendorId: 0x28DE,
            ProductId: 0x1102,
            Present: true);
        var resolver = new ViiperVirtualDeviceIdentityResolver(Policy);
        var resolution = resolver.Resolve([], [device]);

        var exception = Record.Exception(() => ViiperVirtualDeviceIdentityDiagnostics.Build([], [device], Policy, resolution));
        Assert.Null(exception);

        var evidence = ViiperVirtualDeviceIdentityDiagnostics.Build([], [device], Policy, resolution);
        var entry = Assert.Single(evidence.Devices);
        Assert.Empty(entry.BroaderTopologyEvidenceFields);
        Assert.Empty(entry.RelatedInterestingNodes);
    }

    [Fact]
    public void SameInstanceWithDifferentCasingIsNotClassifiedAsNew()
    {
        var before = Device("USB\\VID_28DE&PID_1102\\CASED", vendorId: 0x28DE, productId: 0x1102, service: "usbip");
        var after = Device("usb\\vid_28de&pid_1102\\cased", vendorId: 0x28DE, productId: 0x1102, service: "usbip");
        var resolver = new ViiperVirtualDeviceIdentityResolver(Policy);
        var resolution = resolver.Resolve([before], [after]);

        Assert.Equal(ViiperVirtualDeviceResolutionStatus.NoNewCandidate, resolution.Status);

        var evidence = ViiperVirtualDeviceIdentityDiagnostics.Build([before], [after], Policy, resolution);
        var entry = Assert.Single(evidence.Devices);
        Assert.False(entry.IsNewSinceBefore);
        Assert.Equal(ViiperVirtualDeviceIdentityDiagnostics.PreExistingInstanceRejection, entry.Rejection);
    }

    [Fact]
    public void LogOnFailureDoesNotThrowForFailureOrSuccessResolutions()
    {
        var gordon = Device("USB\\VID_28DE&PID_1102\\LOG", vendorId: 0x28DE, productId: 0x1102, service: "unrelated");
        var resolver = new ViiperVirtualDeviceIdentityResolver(Policy);
        var resolution = resolver.Resolve([], [gordon]);

        var exception = Record.Exception(() => ViiperVirtualDeviceIdentityDiagnostics.LogOnFailure([], [gordon], Policy, resolution, busId: 1, deviceId: 42));
        Assert.Null(exception);
    }

    private static ControllerDeviceInfo Device(
        string instanceId,
        ushort? vendorId,
        ushort? productId,
        string? service,
        string? parentInstanceId = null,
        IReadOnlyList<string>? ancestorInstanceIds = null) =>
        new(instanceId, null, parentInstanceId, ancestorInstanceIds ?? [], null, [], [], "HIDClass", null, service, vendorId, productId, true);
}
