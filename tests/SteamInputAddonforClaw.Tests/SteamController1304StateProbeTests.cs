using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics.SteamController1304;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SteamController1304StateProbeTests
{
    [Fact]
    public void Capture_FiltersTo1304AndSortsByInstanceId()
    {
        var other = Device("USB\\VID_28DE&PID_1102\\other", 0x28DE, 0x1102);
        var second = Device("USB\\VID_28DE&PID_1304\\b", 0x28DE, 0x1304);
        var first = Device("USB\\VID_28DE&PID_1304\\a", 0x28DE, 0x1304);

        var snapshot = new WindowsSteamController1304StateProbe(() => [second, other, first]).Capture();

        Assert.Null(snapshot.Failure);
        Assert.Equal([first.InstanceId, second.InstanceId], snapshot.Devices.Select(device => device.InstanceId));
    }

    [Fact]
    public void Capture_PreservesUnavailableMetadataWithoutChangingProductionClassification()
    {
        var device = Device("USB\\VID_28DE&PID_1304\\root", 0x28DE, 0x1304) with
        {
            ParentInstanceId = null,
            ClassName = null,
            Service = null,
            UsagePage = null,
            Usage = null
        };

        var snapshot = new WindowsSteamController1304StateProbe(() => [device]).Capture();

        var item = Assert.Single(snapshot.Devices);
        Assert.Null(item.ParentInstanceId);
        Assert.Null(item.ClassName);
        Assert.Null(item.UsagePage);
        Assert.Null(item.Usage);
    }

    private static ControllerDeviceInfo Device(string instanceId, ushort vendorId, ushort productId) => new(
        instanceId, Guid.NewGuid(), null, [], "USB", [$"USB\\VID_{vendorId:X4}&PID_{productId:X4}"], [], "USB", null, "usbccgp", vendorId, productId, true);
}
