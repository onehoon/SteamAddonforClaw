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

    // A realistic Stock Center M topology requires both the gamepad-usage interface AND the
    // mode-switch-critical control HID interface (see MsiClawModeTopology). A single generic
    // MSI VID/PID device is not sufficient for readiness — see the dedicated
    // GamepadOnlyWithoutControlHid regression test below for why.
    [Fact]
    public async Task WaitUntilStableAsync_WhenInternalHandheldTopologyIsStable_ReturnsStable()
    {
        var gamepadInterface = GamepadInterface();
        var directInputControlHid = DirectInputControlHid();
        var waiter = CreateWaiter([gamepadInterface, directInputControlHid], requiredStableSnapshots: 3, timeout: TimeSpan.FromSeconds(1));

        var readiness = await waiter.WaitUntilStableAsync(ControllerEnvironmentMode.StockCenterM, CancellationToken.None);

        Assert.Equal(ControllerEnvironmentReadiness.Stable, readiness);
    }

    // Same good path as above, but exercises the XInput control HID topology (PID 1901, UsagePage
    // 0xFFA0, Usage 0x0001) instead of DirectInput, proving readiness recognizes either mode.
    [Fact]
    public async Task WaitUntilStableAsync_GamepadAndXInputControlHidBothStable_ReturnsStable()
    {
        var gamepadInterface = GamepadInterface();
        var xInputControlHid = new ControllerDeviceInfo(
            "HID\\VID_0DB0&PID_1901&MI_02&COL01",
            Guid.NewGuid(),
            null,
            [],
            "HID",
            ["HID\\VID_0DB0&PID_1901&MI_02&COL01"],
            ["HID_DEVICE_UP:FFA0_U:0001"],
            "HIDClass",
            null,
            null,
            0x0DB0,
            0x1901,
            true,
            null,
            0xFFA0,
            0x0001);
        var waiter = CreateWaiter([gamepadInterface, xInputControlHid], requiredStableSnapshots: 3, timeout: TimeSpan.FromSeconds(1));

        var readiness = await waiter.WaitUntilStableAsync(ControllerEnvironmentMode.StockCenterM, CancellationToken.None);

        Assert.Equal(ControllerEnvironmentReadiness.Stable, readiness);
    }

    // Regression: the MSI gamepad-usage interface enumerating (and staying constant) is not enough on
    // its own. Without the mode-switch-critical control HID (XInput or DirectInput topology) ever
    // appearing, readiness must never settle on Stable, no matter how many stable polls the gamepad
    // interface alone accumulates.
    [Fact]
    public async Task WaitUntilStableAsync_GamepadOnlyWithoutControlHid_DoesNotReportStable()
    {
        var gamepadInterface = GamepadInterface();
        var waiter = CreateWaiter([gamepadInterface], requiredStableSnapshots: 3, timeout: TimeSpan.FromMilliseconds(30));

        var readiness = await waiter.WaitUntilStableAsync(ControllerEnvironmentMode.StockCenterM, CancellationToken.None);

        Assert.Equal(ControllerEnvironmentReadiness.Indeterminate, readiness);
    }

    private static ControllerDeviceInfo GamepadInterface() => new(
        "HID\\VID_0DB0&PID_1902&MI_00&COL01",
        Guid.NewGuid(),
        null,
        [],
        "HID",
        ["HID\\VID_0DB0&PID_1902&MI_00&COL01"],
        ["HID_DEVICE_UP:0001_U:0005"],
        "HIDClass",
        null,
        null,
        0x0DB0,
        0x1902,
        true);

    private static ControllerDeviceInfo DirectInputControlHid() => new(
        "HID\\VID_0DB0&PID_1902&MI_02&COL01",
        Guid.NewGuid(),
        null,
        [],
        "HID",
        ["HID\\VID_0DB0&PID_1902&MI_02&COL01"],
        ["HID_DEVICE_UP:FFF0_U:0040"],
        "HIDClass",
        null,
        null,
        0x0DB0,
        0x1902,
        true,
        null,
        0xFFF0,
        0x0040);

    [Fact]
    public async Task WaitUntilStableAsync_ExternalControllerHotplugNoise_DoesNotResetOrBlockStockCenterMStabilization()
    {
        var stableDevices = new[] { GamepadInterface(), DirectInputControlHid() };
        var xboxController = new ControllerDeviceInfo(
            "HID\\VID_045E&PID_0B13",
            Guid.NewGuid(),
            null,
            [],
            "HID",
            ["HID\\VID_045E&PID_0B13"],
            ["HID_DEVICE_UP:0001_U:0005"],
            "HIDClass",
            null,
            null,
            0x045E,
            0x0B13,
            true);
        var steamControllerReceiver = new ControllerDeviceInfo(
            "HID\\VID_28DE&PID_1304&MI_02&COL01",
            Guid.NewGuid(),
            null,
            [],
            "HID",
            ["HID\\VID_28DE&PID_1304&MI_02&COL01"],
            ["HID_DEVICE_UP:FF00_U:0001"],
            "HIDClass",
            null,
            null,
            0x28DE,
            0x1304,
            true,
            null,
            0xFF00,
            0x0001);
        var enumerator = new HotplugNoiseEnumerator(stableDevices, [xboxController, steamControllerReceiver]);
        var waiter = new ControllerEnvironmentWaiter(
            enumerator,
            new ControllerDeviceClassifier(new MsiClawInternalControllerMatcher()),
            requiredStableSnapshots: 3,
            sampleInterval: TimeSpan.Zero,
            timeout: TimeSpan.FromSeconds(2));

        var readiness = await waiter.WaitUntilStableAsync(ControllerEnvironmentMode.StockCenterM, CancellationToken.None);

        Assert.Equal(ControllerEnvironmentReadiness.Stable, readiness);
    }

    // Regression for the startup race: the MSI Claw's gamepad-usage HID interface (the one that
    // satisfies a generic "is this a game controller" filter) can enumerate before the vendor/control
    // HID interface (vendor-defined usage page, e.g. XInput PID 1901 UsagePage 0xFFA0/Usage 0x0001, or
    // DirectInput PID 1902 UsagePage 0xFFF0/Usage 0x0040) that the mode-switch logic actually depends
    // on. IsInternalHandheld must track both, not just the gamepad-usage interface, so readiness cannot
    // be declared Stable while the control interface is still settling.
    [Fact]
    public async Task WaitUntilStableAsync_MsiControlInterfaceStillSettling_DoesNotReportStable()
    {
        var gamepadInterface = new ControllerDeviceInfo(
            "HID\\VID_0DB0&PID_1902&MI_00&COL01",
            Guid.NewGuid(),
            null,
            [],
            "HID",
            ["HID\\VID_0DB0&PID_1902&MI_00&COL01"],
            ["HID_DEVICE_UP:0001_U:0005"],
            "HIDClass",
            null,
            null,
            0x0DB0,
            0x1902,
            true);
        // The control interface never settles: its InstanceId changes on every poll, simulating PnP
        // enumeration still in progress.
        var enumerator = new SettlingControlInterfaceEnumerator(gamepadInterface, settleAfterTick: int.MaxValue);
        var waiter = new ControllerEnvironmentWaiter(
            enumerator,
            new ControllerDeviceClassifier(new MsiClawInternalControllerMatcher()),
            requiredStableSnapshots: 3,
            sampleInterval: TimeSpan.Zero,
            timeout: TimeSpan.FromMilliseconds(30));

        var readiness = await waiter.WaitUntilStableAsync(ControllerEnvironmentMode.StockCenterM, CancellationToken.None);

        // Prior to the fix, IsInternalHandheld required IsGameControllerCandidate, which the vendor
        // control interface never satisfies, so it was silently excluded from the stability snapshot
        // entirely and the gamepad interface alone would report Stable almost immediately within this
        // same short timeout. With the fix, the still-changing control interface keeps resetting
        // stability, so readiness times out to Indeterminate instead.
        Assert.Equal(ControllerEnvironmentReadiness.Indeterminate, readiness);
    }

    [Fact]
    public async Task WaitUntilStableAsync_MsiControlInterfaceSettlesAfterFewPolls_ReturnsStableOnceBothSettle()
    {
        var gamepadInterface = new ControllerDeviceInfo(
            "HID\\VID_0DB0&PID_1902&MI_00&COL01",
            Guid.NewGuid(),
            null,
            [],
            "HID",
            ["HID\\VID_0DB0&PID_1902&MI_00&COL01"],
            ["HID_DEVICE_UP:0001_U:0005"],
            "HIDClass",
            null,
            null,
            0x0DB0,
            0x1902,
            true);
        var enumerator = new SettlingControlInterfaceEnumerator(gamepadInterface, settleAfterTick: 2);
        var waiter = new ControllerEnvironmentWaiter(
            enumerator,
            new ControllerDeviceClassifier(new MsiClawInternalControllerMatcher()),
            requiredStableSnapshots: 3,
            sampleInterval: TimeSpan.Zero,
            timeout: TimeSpan.FromSeconds(2));

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

    /// <summary>
    /// Simulates external-controller hotplug noise: on every poll tick, a different (or no) external
    /// candidate device is present alongside the always-present stable device. Used to prove that
    /// external-controller connect/disconnect noise cannot reset or block startup stabilization.
    /// </summary>
    private sealed class HotplugNoiseEnumerator(IReadOnlyList<ControllerDeviceInfo> stableDevices, IReadOnlyList<ControllerDeviceInfo> noiseCandidates) : IControllerDeviceEnumerator
    {
        private int _tick;

        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices()
        {
            var index = _tick++ % (noiseCandidates.Count + 1);
            var devices = new List<ControllerDeviceInfo>(stableDevices);
            if (index > 0) devices.Add(noiseCandidates[index - 1]);
            return devices;
        }
    }

    /// <summary>
    /// Simulates the MSI Claw's vendor/control HID interface (same VID/PID family as the gamepad
    /// interface, different collection) still being enumerated by PnP: before <paramref name="settleAfterTick"/>
    /// its InstanceId changes on every poll; from that tick onward it is fixed. The always-present
    /// gamepad interface never changes.
    /// </summary>
    private sealed class SettlingControlInterfaceEnumerator(ControllerDeviceInfo gamepadInterface, int settleAfterTick) : IControllerDeviceEnumerator
    {
        private int _tick;

        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices()
        {
            var tick = _tick++;
            var instanceId = tick < settleAfterTick
                ? $"HID\\VID_0DB0&PID_1902&MI_02&COL01_Settling_{tick}"
                : "HID\\VID_0DB0&PID_1902&MI_02&COL01";
            var controlInterface = new ControllerDeviceInfo(
                instanceId,
                Guid.NewGuid(),
                null,
                [],
                "HID",
                [instanceId],
                ["HID_DEVICE_UP:FFF0_U:0040"],
                "HIDClass",
                null,
                null,
                0x0DB0,
                0x1902,
                true,
                null,
                0xFFF0,
                0x0040);
            return [gamepadInterface, controlInterface];
        }
    }
}
