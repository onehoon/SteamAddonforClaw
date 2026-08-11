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
    public void Uncertain_ownership_removes_the_exclusion_and_latches_failure()
    {
        var tracker = new AddonOwnedVirtualDeviceTracker();
        var device = Device("USB\\VID_28DE&PID_1102\\OWNED");
        tracker.Publish(device);
        tracker.MarkOwnershipUncertain();
        Assert.False(tracker.IsExcluded(device));
        Assert.True(tracker.HasUncertainOwnership);
    }

    [Fact]
    public void ResolveOwnership_publishes_all_exact_ids_before_clearing_uncertainty()
    {
        var tracker = new AddonOwnedVirtualDeviceTracker();
        var first = Device("USB\\VID_28DE&PID_1102\\FIRST");
        var second = Device("USB\\VID_28DE&PID_1102\\SECOND");
        tracker.MarkOwnershipUncertain();
        tracker.ResolveOwnership([first, second]);
        Assert.False(tracker.HasUncertainOwnership);
        Assert.True(tracker.IsExcluded(first));
        Assert.True(tracker.IsExcluded(second));
    }

    private static ControllerDeviceInfo Device(string instanceId) => new(instanceId, null, null, [], "HID", ["HID_DEVICE_UP:0001_U:0005"], [], "HIDClass", null, null, 0x28DE, 0x1102, true);
}
