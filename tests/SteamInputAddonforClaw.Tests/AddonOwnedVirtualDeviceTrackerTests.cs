using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonOwnedVirtualDeviceTrackerTests
{
    [Fact]
    public void Only_the_exact_published_PnP_instance_is_excluded()
    {
        var tracker = new AddonOwnedVirtualDeviceTracker();
        var owned = Device("USB\\VID_28DE&PID_1102\\OWNED");
        tracker.Publish(owned);
        Assert.True(tracker.IsExcluded(owned));
        Assert.False(tracker.IsExcluded(Device("USB\\VID_28DE&PID_1102\\PHYSICAL")));
    }

    [Fact]
    public void Invalidation_removes_stale_ownership()
    {
        var tracker = new AddonOwnedVirtualDeviceTracker();
        var device = Device("USB\\VID_28DE&PID_1102\\OWNED");
        tracker.Publish(device);
        tracker.InvalidateAll();
        Assert.False(tracker.IsExcluded(device));
    }

    private static ControllerDeviceInfo Device(string instanceId) => new(instanceId, null, null, [], "HID", ["HID_DEVICE_UP:0001_U:0005"], [], "HIDClass", null, null, 0x28DE, 0x1102, true);
}
