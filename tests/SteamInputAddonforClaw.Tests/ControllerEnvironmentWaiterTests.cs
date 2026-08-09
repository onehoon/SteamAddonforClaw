using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ControllerEnvironmentWaiterTests
{
    [Fact]
    public async Task WaitUntilStableAsync_WhenInternalHandheldIsAbsent_ReturnsIndeterminate()
    {
        var waiter = CreateWaiter([], requiredStableSnapshots: 3, timeout: TimeSpan.FromMilliseconds(20));

        var readiness = await waiter.WaitUntilStableAsync(ControllerEnvironmentMode.StockCenterM, CancellationToken.None);

        Assert.Equal(ControllerEnvironmentReadiness.Indeterminate, readiness);
    }

    [Fact]
    public async Task WaitUntilStableAsync_WhenInternalHandheldTopologyIsStable_ReturnsStable()
    {
        var claw = new ControllerDeviceInfo(
            "HID\\VID_0DB0&PID_1902",
            Guid.NewGuid(),
            null,
            [],
            "HID",
            ["HID\\VID_0DB0&PID_1902"],
            ["HID_DEVICE_UP:0001_U:0005"],
            "HIDClass",
            null,
            null,
            0x0DB0,
            0x1902,
            true);
        var waiter = CreateWaiter([claw], requiredStableSnapshots: 3, timeout: TimeSpan.FromSeconds(1));

        var readiness = await waiter.WaitUntilStableAsync(ControllerEnvironmentMode.StockCenterM, CancellationToken.None);

        Assert.Equal(ControllerEnvironmentReadiness.Stable, readiness);
    }

    [Fact]
    public async Task WaitUntilStableAsync_WhenClawTweaksVirtualTopologyIsStable_DoesNotRequireInternalHandheld()
    {
        var virtualController = new ControllerDeviceInfo("HID\\VIRTUAL", Guid.NewGuid(), null, ["ROOT\\USBIP_WIN2\\UDE"], "HID", ["HID\\VID_045E&PID_028E"], ["HID_DEVICE_UP:0001_U:0005"], "HIDClass", null, null, 0x045E, 0x028E, true);
        var waiter = CreateWaiter([virtualController], 3, TimeSpan.FromSeconds(1));

        var readiness = await waiter.WaitUntilStableAsync(ControllerEnvironmentMode.ClawTweaks, CancellationToken.None);

        Assert.Equal(ControllerEnvironmentReadiness.Stable, readiness);
    }

    [Fact]
    public async Task WaitUntilStableAsync_WhenClawTweaksUsbIpEvidenceIsAncestorMetadata_ReturnsStable()
    {
        var virtualController = new ControllerDeviceInfo("HID\\VID_045E&PID_028E", Guid.NewGuid(), null, ["ROOT\\USB\\0000"], "HID", ["HID\\VID_045E&PID_028E"], ["HID_DEVICE_UP:0001_U:0005"], "HIDClass", null, null, 0x045E, 0x028E, true);
        var usbIpRoot = new ControllerDeviceInfo("ROOT\\USB\\0000", Guid.Empty, null, [], "ROOT", ["ROOT\\USBIP_WIN2\\UDE"], [], "USB", null, "usbip2_ude", null, null, true);
        var waiter = CreateWaiter([virtualController, usbIpRoot], 3, TimeSpan.FromSeconds(1));

        var readiness = await waiter.WaitUntilStableAsync(ControllerEnvironmentMode.ClawTweaks, CancellationToken.None);

        Assert.Equal(ControllerEnvironmentReadiness.Stable, readiness);
    }

    [Fact]
    public async Task WaitUntilStableAsync_WhenClawTweaksHasOnlyUsbIpNonController_ReturnsIndeterminate()
    {
        var usbIpDevice = new ControllerDeviceInfo("USB\\VID_1234&PID_5678", Guid.NewGuid(), null, ["ROOT\\USBIP_WIN2\\UDE"], "USB", ["USB\\VID_1234&PID_5678"], [], "USB", null, null, 0x1234, 0x5678, true);
        var waiter = CreateWaiter([usbIpDevice], 3, TimeSpan.FromMilliseconds(20));

        var readiness = await waiter.WaitUntilStableAsync(ControllerEnvironmentMode.ClawTweaks, CancellationToken.None);

        Assert.Equal(ControllerEnvironmentReadiness.Indeterminate, readiness);
    }

    private static ControllerEnvironmentWaiter CreateWaiter(
        IReadOnlyList<ControllerDeviceInfo> devices,
        int requiredStableSnapshots,
        TimeSpan timeout)
    {
        return new ControllerEnvironmentWaiter(
            new FakeEnumerator(devices),
            new ControllerDeviceClassifier(new MsiClawInternalControllerMatcher()),
            requiredStableSnapshots,
            TimeSpan.Zero,
            timeout);
    }

    private sealed class FakeEnumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator
    {
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => devices;
    }
}
