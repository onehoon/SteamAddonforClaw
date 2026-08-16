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
        var owned = Device("USB\\VID_28DE&PID_1205\\OWNED");
        tracker.Publish(owned);
        Assert.True(tracker.IsExcluded(owned));
        Assert.False(tracker.IsExcluded(Device("USB\\VID_28DE&PID_1205\\PHYSICAL")));
    }

    [Fact]
    public void Uncertain_ownership_removes_the_exclusion_and_latches_failure()
    {
        var tracker = new AddonOwnedVirtualDeviceTracker();
        var device = Device("USB\\VID_28DE&PID_1205\\OWNED");
        tracker.Publish(device);
        tracker.MarkOwnershipUncertain();
        Assert.False(tracker.IsExcluded(device));
        Assert.True(tracker.HasUncertainOwnership);
    }

    [Fact]
    public void ResolveOwnership_publishes_all_exact_ids_before_clearing_uncertainty()
    {
        var tracker = new AddonOwnedVirtualDeviceTracker();
        var first = Device("USB\\VID_28DE&PID_1205\\FIRST");
        var second = Device("USB\\VID_28DE&PID_1205\\SECOND");
        tracker.MarkOwnershipUncertain();
        tracker.ResolveOwnership([first, second]);
        Assert.False(tracker.HasUncertainOwnership);
        Assert.True(tracker.IsExcluded(first));
        Assert.True(tracker.IsExcluded(second));
    }

    private static ControllerDeviceInfo Device(string instanceId) => new(instanceId, null, null, [], "HID", ["HID_DEVICE_UP:0001_U:0005"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);

    private const string UsbIpHostInstanceId = "ROOT\\USB\\0000";
    private static ControllerDeviceInfo UsbIpHost() => new(UsbIpHostInstanceId, null, null, [], "ROOT", ["ROOT\\USBIP_WIN2\\UDE"], [], "System", null, "usbip2_ude", null, null, true);

    // Regression for the teardown TOCTOU: WaitForAbsenceAsync verifies the exact previously-owned
    // InstanceId is absent at one point in time; a SEPARATE, later snapshot re-appearance of that
    // exact InstanceId must not be excused from the "still present" check just because it used to be
    // legitimately owned. Excusing it (the old `!ownedIds.Contains(...)` clause) would silently clear
    // ownership uncertainty and let recovery complete while the owned device is, per the later
    // evidence, actually still present -- this must fail closed instead.
    [Fact]
    public void SteamDeck_reappearance_of_the_exact_previously_owned_instance_after_absence_blocks_the_clear()
    {
        var tracker = new AddonOwnedVirtualDeviceTracker();
        var ownedDevice = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205\\X", null, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["HID\\VID_28DE&PID_1205"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);
        tracker.MarkOwnershipUncertain();
        tracker.ResolveOwnership([ownedDevice]);

        var uncertaintyClearSnapshot = new[] { UsbIpHost(), ownedDevice };

        var cleared = tracker.ClearUncertaintyAfterVerifiedAbsence(uncertaintyClearSnapshot, new SteamDeckVirtualDeviceIdentityPolicy(), before: [], owned: [ownedDevice]);

        Assert.False(cleared);
        Assert.True(tracker.IsExcluded(ownedDevice));
        Assert.False(tracker.HasUncertainOwnership);
    }

    // Sanity counterpart: a truly-absent owned device (not in the uncertainty-clear snapshot at all)
    // still clears normally -- the fix only tightens the exact-reappearance case above, it does not
    // regress the ordinary successful-teardown path.
    [Fact]
    public void SteamDeck_genuine_absence_of_the_owned_instance_still_clears_uncertainty()
    {
        var tracker = new AddonOwnedVirtualDeviceTracker();
        var ownedDevice = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205\\X", null, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["HID\\VID_28DE&PID_1205"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);
        tracker.MarkOwnershipUncertain();
        tracker.ResolveOwnership([ownedDevice]);

        var cleared = tracker.ClearUncertaintyAfterVerifiedAbsence([UsbIpHost()], new SteamDeckVirtualDeviceIdentityPolicy(), before: [], owned: [ownedDevice]);

        Assert.True(cleared);
        Assert.False(tracker.HasUncertainOwnership);
    }
}
