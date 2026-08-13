using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ViiperVirtualDeviceIdentityResolverTests
{
    private const string UsbIpHostInstanceId = "ROOT\\USB\\0000";
    private const string UsbIpHostService = "usbip2_ude";
    private const string UsbIpHostHardwareId = "ROOT\\USBIP_WIN2\\UDE";
    private const string RootHubInstanceId = "USB\\ROOT_HUB30\\1&2B53A856&2&0";

    [Fact]
    public void RealHardwareTopologyResolves()
    {
        var container = Guid.NewGuid();
        var host = UsbIpHost();
        var mi00 = Gordon("USB\\VID_28DE&PID_1102&MI_00\\1", container);
        var mi01 = Gordon("USB\\VID_28DE&PID_1102&MI_01\\1", container);
        var mi02 = Gordon("USB\\VID_28DE&PID_1102&MI_02\\1", container);

        var result = new ViiperVirtualDeviceIdentityResolver(new ViiperVirtualDeviceIdentityPolicy()).Resolve([host], [host, mi00, mi01, mi02]);

        Assert.True(result.Succeeded);
        Assert.Equal("VirtualDeviceIdentityResolved", result.Reason);
        Assert.Equal(3, result.Devices.Count);
    }

    [Fact]
    public void PhysicalSteamControllerWithoutUsbIpAncestorIsRejected()
    {
        var device = Gordon("USB\\VID_28DE&PID_1102\\PHYSICAL", Guid.NewGuid(), ancestorInstanceIds: [RootHubInstanceId]);
        var result = new ViiperVirtualDeviceIdentityResolver(new ViiperVirtualDeviceIdentityPolicy()).Resolve([], [device]);

        Assert.Equal(ViiperVirtualDeviceResolutionStatus.NoNewCandidate, result.Status);
        Assert.Equal("VirtualDeviceDidNotAppear", result.Reason);
    }

    [Fact]
    public void PreExistingVirtualGordonIsNeverClaimed()
    {
        var host = UsbIpHost();
        var gordon = Gordon("USB\\VID_28DE&PID_1102\\OLD", Guid.NewGuid());
        var result = new ViiperVirtualDeviceIdentityResolver(new ViiperVirtualDeviceIdentityPolicy()).Resolve([host, gordon], [host, gordon]);

        Assert.Equal(ViiperVirtualDeviceResolutionStatus.NoNewCandidate, result.Status);
    }

    [Fact]
    public void AncestorInstanceIdTextAloneIsInsufficientWithoutTheExactHostSignature()
    {
        // The ancestor's InstanceId looks USBIP-related, but the resolved record does not carry
        // the exact usbip-win2 driver identity -- production ownership must not be granted on
        // textual resemblance alone.
        var lookalikeHost = new ControllerDeviceInfo("ROOT\\SOMETHING_USBIP_LIKE\\123", null, null, [], "ROOT", [], [], "System", null, "unrelated_service", null, null, true);
        var gordon = Gordon("USB\\VID_28DE&PID_1102\\LOOKALIKE", Guid.NewGuid(), ancestorInstanceIds: [lookalikeHost.InstanceId]);
        var result = new ViiperVirtualDeviceIdentityResolver(new ViiperVirtualDeviceIdentityPolicy()).Resolve([], [lookalikeHost, gordon]);

        Assert.Equal(ViiperVirtualDeviceResolutionStatus.NoNewCandidate, result.Status);
    }

    [Fact]
    public void UnresolvedAncestorRecordFailsClosed()
    {
        // The candidate references ROOT\USB\0000 as an ancestor, but no such record exists in
        // the current snapshot -- ownership must not be inferred from the ancestor string alone.
        var gordon = Gordon("USB\\VID_28DE&PID_1102\\ORPHAN", Guid.NewGuid());
        var result = new ViiperVirtualDeviceIdentityResolver(new ViiperVirtualDeviceIdentityPolicy()).Resolve([], [gordon]);

        Assert.Equal(ViiperVirtualDeviceResolutionStatus.NoNewCandidate, result.Status);
    }

    [Fact]
    public void ServiceOnlyPartialUsbIpSignatureIsRejected()
    {
        var partialHost = new ControllerDeviceInfo(UsbIpHostInstanceId, null, null, [], "ROOT", [], [], "System", null, UsbIpHostService, null, null, true);
        var gordon = Gordon("USB\\VID_28DE&PID_1102\\PARTIAL1", Guid.NewGuid());
        var result = new ViiperVirtualDeviceIdentityResolver(new ViiperVirtualDeviceIdentityPolicy()).Resolve([], [partialHost, gordon]);

        Assert.Equal(ViiperVirtualDeviceResolutionStatus.NoNewCandidate, result.Status);
    }

    [Fact]
    public void HardwareIdOnlyPartialUsbIpSignatureIsRejected()
    {
        var partialHost = new ControllerDeviceInfo(UsbIpHostInstanceId, null, null, [], "ROOT", [UsbIpHostHardwareId], [], "System", null, "wrong_service", null, null, true);
        var gordon = Gordon("USB\\VID_28DE&PID_1102\\PARTIAL2", Guid.NewGuid());
        var result = new ViiperVirtualDeviceIdentityResolver(new ViiperVirtualDeviceIdentityPolicy()).Resolve([], [partialHost, gordon]);

        Assert.Equal(ViiperVirtualDeviceResolutionStatus.NoNewCandidate, result.Status);
    }

    [Fact]
    public void MatchingIsCaseInsensitiveAcrossAncestorInstanceIdServiceAndHardwareId()
    {
        var host = new ControllerDeviceInfo(UsbIpHostInstanceId.ToUpperInvariant(), null, null, [], "ROOT", [UsbIpHostHardwareId.ToLowerInvariant()], [], "System", null, UsbIpHostService.ToUpperInvariant(), null, null, true);
        var gordon = Gordon("USB\\VID_28DE&PID_1102\\CASED", Guid.NewGuid(), ancestorInstanceIds: [UsbIpHostInstanceId.ToLowerInvariant()]);
        var result = new ViiperVirtualDeviceIdentityResolver(new ViiperVirtualDeviceIdentityPolicy()).Resolve([], [host, gordon]);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void MultipleInterfacesInOneContainerRemainOneLogicalDevice()
    {
        var host = UsbIpHost();
        var container = Guid.NewGuid();
        var first = Gordon("USB\\VID_28DE&PID_1102\\ONE", container);
        var second = Gordon("USB\\VID_28DE&PID_1102\\TWO", container);
        var result = new ViiperVirtualDeviceIdentityResolver(new ViiperVirtualDeviceIdentityPolicy()).Resolve([host], [host, first, second]);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Devices.Count);
    }

    [Fact]
    public void TwoLogicalNewDevicesAreAmbiguous()
    {
        var host = UsbIpHost();
        var first = Gordon("USB\\VID_28DE&PID_1102\\ONE", Guid.NewGuid());
        var second = Gordon("USB\\VID_28DE&PID_1102\\TWO", Guid.NewGuid());
        var result = new ViiperVirtualDeviceIdentityResolver(new ViiperVirtualDeviceIdentityPolicy()).Resolve([host], [host, first, second]);

        Assert.Equal(ViiperVirtualDeviceResolutionStatus.Ambiguous, result.Status);
        Assert.Equal("AmbiguousVirtualDeviceIdentity", result.Reason);
    }

    [Fact]
    public void EmptyOrSentinelContainersFallBackToParentAndRemainAmbiguousWhenParentsDiffer()
    {
        var host = UsbIpHost();
        var sentinel = new Guid("00000000-0000-0000-ffff-ffffffffffff");
        var first = GordonWithParent("USB\\VID_28DE&PID_1102\\ONE", "PARENT_ONE", Guid.Empty);
        var second = GordonWithParent("USB\\VID_28DE&PID_1102\\TWO", "PARENT_TWO", sentinel);
        var result = new ViiperVirtualDeviceIdentityResolver(new ViiperVirtualDeviceIdentityPolicy()).Resolve([host], [host, first, second]);

        Assert.Equal(ViiperVirtualDeviceResolutionStatus.Ambiguous, result.Status);
    }

    [Fact]
    public void AnotherPreExistingViiperGordonGroupIsPreservedAndOnlyTheNewGroupResolves()
    {
        var host = UsbIpHost();
        var existingGroup = Gordon("USB\\VID_28DE&PID_1102\\EXISTING", Guid.NewGuid());
        var newGroup = Gordon("USB\\VID_28DE&PID_1102\\NEW", Guid.NewGuid());
        var result = new ViiperVirtualDeviceIdentityResolver(new ViiperVirtualDeviceIdentityPolicy())
            .Resolve([host, existingGroup], [host, existingGroup, newGroup]);

        Assert.True(result.Succeeded);
        var resolved = Assert.Single(result.Devices);
        Assert.Equal(newGroup.InstanceId, resolved.InstanceId);
    }

    private static ControllerDeviceInfo UsbIpHost() =>
        new(UsbIpHostInstanceId, null, null, [], "ROOT", [UsbIpHostHardwareId], [], "System", null, UsbIpHostService, null, null, true);

    private static ControllerDeviceInfo Gordon(string instanceId, Guid container, IReadOnlyList<string>? ancestorInstanceIds = null) =>
        new(instanceId, container, RootHubInstanceId, ancestorInstanceIds ?? [RootHubInstanceId, UsbIpHostInstanceId], "USB", [], [], "HIDClass", null, null, 0x28DE, 0x1102, true);

    private static ControllerDeviceInfo GordonWithParent(string instanceId, string parent, Guid container) =>
        new(instanceId, container, parent, [RootHubInstanceId, UsbIpHostInstanceId], "USB", [], [], "HIDClass", null, null, 0x28DE, 0x1102, true);
}
