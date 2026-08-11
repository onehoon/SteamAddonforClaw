using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ViiperVirtualDeviceIdentityResolverTests
{
    [Fact]
    public void PreExistingDeviceIsNeverClaimed()
    {
        var device = Device("USB\\VID_28DE&PID_1102\\OLD", "USBIP");
        var result = new ViiperVirtualDeviceIdentityResolver(new ViiperVirtualDeviceIdentityPolicy()).Resolve([device], [device]);
        Assert.Equal(ViiperVirtualDeviceResolutionStatus.NoNewCandidate, result.Status);
    }

    [Fact]
    public void TwoLogicalNewDevicesAreAmbiguous()
    {
        var first = Device("USB\\VID_28DE&PID_1102\\ONE", "USBIP", Guid.NewGuid());
        var second = Device("USB\\VID_28DE&PID_1102\\TWO", "USBIP", Guid.NewGuid());
        var result = new ViiperVirtualDeviceIdentityResolver(new ViiperVirtualDeviceIdentityPolicy()).Resolve([], [first, second]);
        Assert.Equal(ViiperVirtualDeviceResolutionStatus.Ambiguous, result.Status);
    }

    [Fact]
    public void MultipleInterfacesInOneContainerAreResolvedTogether()
    {
        var container = Guid.NewGuid();
        var first = Device("USB\\VID_28DE&PID_1102\\ONE", "USBIP", container);
        var second = Device("USB\\VID_28DE&PID_1102\\TWO", "USBIP", container);
        var result = new ViiperVirtualDeviceIdentityResolver(new ViiperVirtualDeviceIdentityPolicy()).Resolve([], [first, second]);
        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Devices.Count);
    }

    private static ControllerDeviceInfo Device(string id, string topology, Guid? container = null) =>
        new(id, container, null, [topology], "USBIP", [], [], "HIDClass", null, "usbip", 0x28DE, 0x1102, true);
}
