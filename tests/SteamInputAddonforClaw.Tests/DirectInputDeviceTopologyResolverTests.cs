using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Input.DirectInput;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class DirectInputDeviceTopologyResolverTests
{
    private const string Path = "\\\\?\\hid#vid_0db0&pid_1902&mi_00&col01#7&test&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private const string HidInstance = "HID\\VID_0DB0&PID_1902&MI_00&COL01\\7&TEST&0&0000";
    private const string Root = "USB\\VID_0DB0&PID_1902\\5&ROOT&0&4";

    [Fact]
    public void Resolve_ValidGamePadPath_ReturnsVerifiedMsiPhysicalRoot()
    {
        var result = new DirectInputDeviceTopologyResolver(new FakeEnumerator([Device(HidInstance, 0x0DB0, 0x1902, [Root])])).Resolve(Path);

        Assert.True(result.IsVerified);
        Assert.Equal(HidInstance, result.PnpInstanceId);
        Assert.Equal(Root, result.PhysicalIdentity);
        Assert.Equal("VerifiedMsiPhysicalRoot", result.SelectionReason);
    }

    [Fact]
    public void Resolve_PnpInstanceMissing_IsUnresolved() => Assert.Equal("PnpInstanceNotFound", new DirectInputDeviceTopologyResolver(new FakeEnumerator([])).Resolve(Path).SelectionReason);

    [Fact]
    public void Resolve_NonMsiPnpIdentity_IsUnresolved() => Assert.Equal("PnpIdentityIsNotMsiPid1902", new DirectInputDeviceTopologyResolver(new FakeEnumerator([Device(HidInstance, 0x045E, 0x028E, [Root])])).Resolve(Path).SelectionReason);

    [Fact]
    public void Resolve_NonGamePadUsage_IsUnresolved() => Assert.Equal("PnpInterfaceIsNotGamePad", new DirectInputDeviceTopologyResolver(new FakeEnumerator([Device(HidInstance, 0x0DB0, 0x1902, [Root], "HID\\VID_0DB0&PID_1902&MI_00&COL01 | HID\\VID_0DB0&PID_1902&MI_00&COL01 | HID\\VID_0DB0&UP:0001_U:0002")])).Resolve(Path).SelectionReason);

    [Theory]
    [InlineData("\\\\?\\hid#vid_0db0&pid_1902&mi_00&col02#7&test&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}", "HID\\VID_0DB0&PID_1902&MI_00&COL02\\7&TEST&0&0001", "HID\\VID_0DB0&PID_1902&MI_00&COL02 | HID\\VID_0DB0&UP:FFF0_U:0040")]
    [InlineData("\\\\?\\hid#vid_0db0&pid_1902&mi_01&col03#7&test&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}", "HID\\VID_0DB0&PID_1902&MI_01&COL03\\7&TEST&0&0002", "HID\\VID_0DB0&PID_1902&MI_01&COL03 | HID\\VID_0DB0&UP:000C_U:0001")]
    public void Resolve_NonGamePadCollectionUnderMsiPhysicalRoot_IsUnresolved(string path, string hidInstance, string hardwareIds)
    {
        var result = new DirectInputDeviceTopologyResolver(new FakeEnumerator([Device(hidInstance, 0x0DB0, 0x1902, [Root], hardwareIds)])).Resolve(path);

        Assert.False(result.IsVerified);
        Assert.Equal("PnpInterfaceIsNotGamePad", result.SelectionReason);
    }

    [Fact]
    public void Resolve_MsiUsbAncestorMissing_IsUnresolved() => Assert.Equal("MsiPhysicalRootNotFound", new DirectInputDeviceTopologyResolver(new FakeEnumerator([Device(HidInstance, 0x0DB0, 0x1902, ["USB\\OTHER"])] )).Resolve(Path).SelectionReason);

    [Fact]
    public void Resolve_PnpEnumerationFailure_IsUnresolved() => Assert.Equal("PnPEnumerationFailed:InvalidOperationException", new DirectInputDeviceTopologyResolver(new FakeEnumerator([]) { Exception = new InvalidOperationException() }).Resolve(Path).SelectionReason);

    private static ControllerDeviceInfo Device(string instanceId, ushort vendorId, ushort productId, IReadOnlyList<string> ancestors, string? hardwareIds = null) => new(instanceId, null, ancestors.FirstOrDefault(), ancestors, "HID", (hardwareIds ?? "HID\\VID_0DB0&PID_1902&MI_00&COL01 | HID\\VID_0DB0&UP:0001_U:0005").Split(" | "), [], "HIDClass", null, null, vendorId, productId, true);
    private sealed class FakeEnumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator
    {
        public Exception? Exception { get; init; }
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => throw new InvalidOperationException("Broad enumeration must not be used by exact DirectInput lookup.");
        public ControllerDeviceInfo? FindPresentDevice(string instanceId)
        {
            if (Exception is not null) throw Exception;
            return devices.SingleOrDefault(device => string.Equals(device.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
        }
    }
}
