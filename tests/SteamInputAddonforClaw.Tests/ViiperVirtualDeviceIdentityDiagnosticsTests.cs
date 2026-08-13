using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ViiperVirtualDeviceIdentityDiagnosticsTests
{
    private static readonly ViiperVirtualDeviceIdentityPolicy Policy = new();
    private const string UsbIpHostInstanceId = "ROOT\\USB\\0000";
    private const string UsbIpHostService = "usbip2_ude";
    private const string UsbIpHostHardwareId = "ROOT\\USBIP_WIN2\\UDE";
    private const string RootHubInstanceId = "USB\\ROOT_HUB30\\1&2B53A856&2&0";

    [Fact]
    public void RealObservedTopologyReportsHasUsbIpWin2AncestorAndNoRejection()
    {
        var host = UsbIpHost();
        var gordon = Gordon("USB\\VID_28DE&PID_1102\\NEW", Guid.NewGuid());
        var resolver = new ViiperVirtualDeviceIdentityResolver(Policy);
        var resolution = resolver.Resolve([host], [host, gordon]);

        Assert.True(resolution.Succeeded);

        var evidence = ViiperVirtualDeviceIdentityDiagnostics.Build([host], [host, gordon], Policy, resolution);
        var gordonEvidence = Assert.Single(evidence.Devices, d => d.Device.InstanceId == gordon.InstanceId);
        Assert.True(gordonEvidence.HasUsbIpWin2Ancestor);
        Assert.Contains(UsbIpHostInstanceId, gordonEvidence.MatchingUsbIpWin2AncestorInstanceIds);
        Assert.True(gordonEvidence.CurrentPolicyMatches);
        Assert.Equal(ViiperVirtualDeviceIdentityDiagnostics.NoneRejection, gordonEvidence.Rejection);
    }

    [Fact]
    public void NewGordonWithoutRecognizedUsbIpWin2AncestorReportsMissingAncestorRejection()
    {
        var gordon = Gordon("USB\\VID_28DE&PID_1102\\PHYSICAL", Guid.NewGuid(), ancestorInstanceIds: [RootHubInstanceId]);
        var resolver = new ViiperVirtualDeviceIdentityResolver(Policy);
        var resolution = resolver.Resolve([], [gordon]);

        Assert.Equal(ViiperVirtualDeviceResolutionStatus.NoNewCandidate, resolution.Status);

        var evidence = ViiperVirtualDeviceIdentityDiagnostics.Build([], [gordon], Policy, resolution);
        var gordonEvidence = Assert.Single(evidence.Devices, d => d.Device.InstanceId == gordon.InstanceId);
        Assert.True(gordonEvidence.IsNewSinceBefore);
        Assert.True(gordonEvidence.VidPidMatchesGordon);
        Assert.False(gordonEvidence.HasUsbIpWin2Ancestor);
        Assert.False(gordonEvidence.CurrentPolicyMatches);
        Assert.Equal(ViiperVirtualDeviceIdentityDiagnostics.MissingUsbIpWin2AncestorRejection, gordonEvidence.Rejection);
    }

    [Fact]
    public void UsbIpHostAncestorRecordItselfIsVisibleInDiagnostics()
    {
        var host = UsbIpHost();
        var gordon = Gordon("USB\\VID_28DE&PID_1102\\NEW", Guid.NewGuid());
        var resolver = new ViiperVirtualDeviceIdentityResolver(Policy);
        var resolution = resolver.Resolve([host], [host, gordon]);

        var evidence = ViiperVirtualDeviceIdentityDiagnostics.Build([host], [host, gordon], Policy, resolution);
        var hostEvidence = Assert.Single(evidence.Devices, d => d.Device.InstanceId == UsbIpHostInstanceId);
        Assert.True(hostEvidence.IsUsbIpWin2HostNode);
        Assert.Contains($"DescendantOf:{UsbIpHostInstanceId}", Assert.Single(evidence.Devices, d => d.Device.InstanceId == gordon.InstanceId).RelatedInterestingNodes);
    }

    [Fact]
    public void PreExistingGordonIsNotClassifiedAsNew()
    {
        var host = UsbIpHost();
        var gordon = Gordon("USB\\VID_28DE&PID_1102\\OLD", Guid.NewGuid());
        var resolver = new ViiperVirtualDeviceIdentityResolver(Policy);
        var resolution = resolver.Resolve([host, gordon], [host, gordon]);

        Assert.Equal(ViiperVirtualDeviceResolutionStatus.NoNewCandidate, resolution.Status);

        var evidence = ViiperVirtualDeviceIdentityDiagnostics.Build([host, gordon], [host, gordon], Policy, resolution);
        var gordonEvidence = Assert.Single(evidence.Devices, d => d.Device.InstanceId == gordon.InstanceId);
        Assert.False(gordonEvidence.IsNewSinceBefore);
        Assert.Equal(ViiperVirtualDeviceIdentityDiagnostics.PreExistingInstanceRejection, gordonEvidence.Rejection);
    }

    [Fact]
    public void AmbiguousValidGroupsAreBothRepresentedAndPolicyMatching()
    {
        var host = UsbIpHost();
        var first = Gordon("USB\\VID_28DE&PID_1102\\ONE", Guid.NewGuid());
        var second = Gordon("USB\\VID_28DE&PID_1102\\TWO", Guid.NewGuid());
        var resolver = new ViiperVirtualDeviceIdentityResolver(Policy);
        var resolution = resolver.Resolve([host], [host, first, second]);

        Assert.Equal(ViiperVirtualDeviceResolutionStatus.Ambiguous, resolution.Status);

        var evidence = ViiperVirtualDeviceIdentityDiagnostics.Build([host], [host, first, second], Policy, resolution);
        var firstEvidence = Assert.Single(evidence.Devices, d => d.Device.InstanceId == first.InstanceId);
        var secondEvidence = Assert.Single(evidence.Devices, d => d.Device.InstanceId == second.InstanceId);
        Assert.True(firstEvidence.CurrentPolicyMatches);
        Assert.True(secondEvidence.CurrentPolicyMatches);
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
        Assert.Empty(entry.MatchingUsbIpWin2AncestorInstanceIds);
        Assert.Empty(entry.RelatedInterestingNodes);
    }

    [Fact]
    public void SameInstanceWithDifferentCasingIsNotClassifiedAsNew()
    {
        var host = UsbIpHost();
        var before = Gordon("USB\\VID_28DE&PID_1102\\CASED", Guid.NewGuid());
        var after = before with { InstanceId = before.InstanceId.ToLowerInvariant() };
        var resolver = new ViiperVirtualDeviceIdentityResolver(Policy);
        var resolution = resolver.Resolve([host, before], [host, after]);

        Assert.Equal(ViiperVirtualDeviceResolutionStatus.NoNewCandidate, resolution.Status);

        var evidence = ViiperVirtualDeviceIdentityDiagnostics.Build([host, before], [host, after], Policy, resolution);
        var entry = Assert.Single(evidence.Devices, d => d.Device.InstanceId == after.InstanceId);
        Assert.False(entry.IsNewSinceBefore);
        Assert.Equal(ViiperVirtualDeviceIdentityDiagnostics.PreExistingInstanceRejection, entry.Rejection);
    }

    [Fact]
    public void PreExistingUsbipNodeSharingContainerIdWithNewGordonIsIncludedViaSameContainer()
    {
        // The USBIP node is pre-existing and has no parent/ancestor relationship to the new
        // Gordon leaf -- the only link is a shared ContainerId. It carries the exact usbip-win2
        // host signature, so it must be pulled into the interesting set independently, not merely
        // alongside the Gordon leaf.
        var container = Guid.NewGuid();
        var usbipNode = UsbIpHost() with { ContainerId = container };
        var gordon = Gordon("USB\\VID_28DE&PID_1102\\NEW", container, ancestorInstanceIds: [RootHubInstanceId]);

        var resolver = new ViiperVirtualDeviceIdentityResolver(Policy);
        var resolution = resolver.Resolve([usbipNode], [usbipNode, gordon]);

        var evidence = ViiperVirtualDeviceIdentityDiagnostics.Build([usbipNode], [usbipNode, gordon], Policy, resolution);
        Assert.Equal(2, evidence.Devices.Count);
        var usbipEvidence = Assert.Single(evidence.Devices, d => d.Device.InstanceId == usbipNode.InstanceId);
        var gordonEvidence = Assert.Single(evidence.Devices, d => d.Device.InstanceId == gordon.InstanceId);
        Assert.True(usbipEvidence.IsUsbIpWin2HostNode);
        Assert.False(usbipEvidence.IsNewSinceBefore);
        Assert.Contains($"SameContainerAs:{gordon.InstanceId}", usbipEvidence.RelatedInterestingNodes);
        Assert.Contains($"SameContainerAs:{usbipNode.InstanceId}", gordonEvidence.RelatedInterestingNodes);
        // Ancestor points at the root hub, not directly at the usbip node, so ownership is still
        // rejected here -- SameContainerAs is diagnostic evidence only, never a production rule.
        Assert.False(gordonEvidence.CurrentPolicyMatches);
    }

    [Fact]
    public void LogOnFailureDoesNotThrowForFailureOrSuccessResolutions()
    {
        var gordon = Gordon("USB\\VID_28DE&PID_1102\\LOG", Guid.NewGuid(), ancestorInstanceIds: [RootHubInstanceId]);
        var resolver = new ViiperVirtualDeviceIdentityResolver(Policy);
        var resolution = resolver.Resolve([], [gordon]);

        var exception = Record.Exception(() => ViiperVirtualDeviceIdentityDiagnostics.LogOnFailure([], [gordon], Policy, resolution, busId: 1, deviceId: 42));
        Assert.Null(exception);
    }

    private static ControllerDeviceInfo UsbIpHost() =>
        new(UsbIpHostInstanceId, null, null, [], "ROOT", [UsbIpHostHardwareId], [], "System", null, UsbIpHostService, null, null, true);

    private static ControllerDeviceInfo Gordon(string instanceId, Guid container, IReadOnlyList<string>? ancestorInstanceIds = null) =>
        new(instanceId, container, RootHubInstanceId, ancestorInstanceIds ?? [RootHubInstanceId, UsbIpHostInstanceId], "USB", [], [], "HIDClass", null, null, 0x28DE, 0x1102, true);
}
