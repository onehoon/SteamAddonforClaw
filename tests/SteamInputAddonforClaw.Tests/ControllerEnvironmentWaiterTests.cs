using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Startup;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ControllerEnvironmentWaiterTests
{
    [Fact]
    public async Task WaitUntilStableAsync_WhenInternalClawIsAbsent_ReturnsIndeterminate()
    {
        var waiter = CreateWaiter([], requiredStableSnapshots: 3, timeout: TimeSpan.FromMilliseconds(20));

        var readiness = await waiter.WaitUntilStableAsync(CancellationToken.None);

        Assert.Equal(ControllerEnvironmentReadiness.Indeterminate, readiness);
    }

    [Fact]
    public async Task WaitUntilStableAsync_WhenInternalClawTopologyIsStable_ReturnsStable()
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

        var readiness = await waiter.WaitUntilStableAsync(CancellationToken.None);

        Assert.Equal(ControllerEnvironmentReadiness.Stable, readiness);
    }

    private static ControllerEnvironmentWaiter CreateWaiter(
        IReadOnlyList<ControllerDeviceInfo> devices,
        int requiredStableSnapshots,
        TimeSpan timeout)
    {
        return new ControllerEnvironmentWaiter(
            new FakeEnumerator(devices),
            new ControllerDeviceClassifier(),
            requiredStableSnapshots,
            TimeSpan.Zero,
            timeout);
    }

    private sealed class FakeEnumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator
    {
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => devices;
    }
}
