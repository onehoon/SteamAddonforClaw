using SteamInputAddonforClaw.Controllers.Detection;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ControllerTopologySnapshotTests
{
    [Fact]
    public void ResolveAncestors_IsCaseInsensitiveAndIgnoresMissingAncestors()
    {
        var root = new ControllerDeviceInfo("ROOT\\USB\\0000", Guid.Empty, null, [], "ROOT", [], [], "USB", null, null, null, null, true);
        var controller = new ControllerDeviceInfo("HID\\CONTROLLER", Guid.NewGuid(), null, ["root\\usb\\0000", "ROOT\\MISSING"], "HID", [], ["HID_DEVICE_UP:0001_U:0005"], "HIDClass", null, null, null, null, true);

        var resolved = new ControllerTopologySnapshot([root, controller]).ResolveAncestors(controller);

        var ancestor = Assert.Single(resolved);
        Assert.Equal(root.InstanceId, ancestor.InstanceId);
    }
}
