using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Startup;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class HandheldCompanionEnvironmentTests
{
    [Fact]
    public void Detect_WhenHandheldCompanionIsNotRunning_UsesStockMode()
    {
        var environment = CreateDetector(running: false).Detect();

        Assert.Equal(ControllerEnvironmentMode.StockCenterM, environment.Mode);
    }

    [Fact]
    public void Detect_WhenHandheldCompanionIsRunning_YieldsOwnershipImmediately()
    {
        var environment = CreateDetector(running: true).Detect();

        Assert.Equal(ControllerEnvironmentMode.HHCManaged, environment.Mode);
    }

    [Fact]
    public void Detect_WhenHandheldCompanionAndClawTweaksEvidenceExist_PrioritizesHandheldCompanion()
    {
        var clawTweaksVirtualController = new ControllerDeviceInfo("HID\\VIRTUAL", Guid.NewGuid(), null, ["ROOT\\USBIP_WIN2\\UDE"], "HID", ["HID\\VID_045E&PID_028E"], ["HID_DEVICE_UP:0001_U:0005"], "HIDClass", null, null, 0x045E, 0x028E, true);
        var environment = new ClawTweaksEnvironmentDetector(new FakeEnumerator([clawTweaksVirtualController]), new FakeRuntimeDetector(true)).Detect();

        Assert.Equal(ControllerEnvironmentMode.HHCManaged, environment.Mode);
    }

    [Fact]
    public async Task RunAsync_WhenHandheldCompanionOwnsControllers_SkipsReadinessWait()
    {
        var waiter = new FakeWaiter();
        var coordinator = new StartupCoordinator(new ContinueUpdateGate(), new HhcEnvironmentDetector(), waiter);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.ShouldStartRuntime);
        Assert.Equal(ControllerEnvironmentMode.HHCManaged, result.EnvironmentMode);
        Assert.Equal(0, waiter.CallCount);
    }

    [Fact]
    public void Detect_WhenClawTweaksHasOnlyUsbIpNonController_DoesNotTreatItAsActive()
    {
        var usbIpDevice = new ControllerDeviceInfo("USB\\VID_1234&PID_5678", Guid.NewGuid(), null, ["ROOT\\USBIP_WIN2\\UDE"], "USB", ["USB\\VID_1234&PID_5678"], [], "USB", null, null, 0x1234, 0x5678, true);
        var detector = new ClawTweaksEnvironmentDetector(new FakeEnumerator([usbIpDevice]), new FakeRuntimeDetector(false), new FakeClawTweaksRuntimeDetector(true));

        var environment = detector.Detect();

        Assert.Equal(ClawTweaksState.Starting, environment.ClawTweaksState);
        Assert.Equal(ControllerEnvironmentMode.Indeterminate, environment.Mode);
    }

    [Theory]
    [InlineData("ROOT\\VIGEMBUS\\0001")]
    [InlineData("ROOT\\HANDHELDCOMPANION\\0001")]
    public void Detect_WhenClawTweaksHasAnotherVirtualController_DoesNotTreatItAsActive(string ancestor)
    {
        var virtualController = new ControllerDeviceInfo("HID\\VIRTUAL", Guid.NewGuid(), null, [ancestor], "HID", ["HID\\VID_045E&PID_028E"], ["HID_DEVICE_UP:0001_U:0005"], "HIDClass", null, null, 0x045E, 0x028E, true);
        var detector = new ClawTweaksEnvironmentDetector(new FakeEnumerator([virtualController]), new FakeRuntimeDetector(false), new FakeClawTweaksRuntimeDetector(true));

        var environment = detector.Detect();

        Assert.Equal(ClawTweaksState.Starting, environment.ClawTweaksState);
        Assert.Equal(ControllerEnvironmentMode.Indeterminate, environment.Mode);
    }

    [Fact]
    public void Detect_WhenClawTweaksHasViiperController_TreatsItAsActive()
    {
        var virtualController = new ControllerDeviceInfo("HID\\VIRTUAL", Guid.NewGuid(), null, ["ROOT\\USBIP_WIN2\\UDE"], "HID", ["HID\\VID_045E&PID_028E"], ["HID_DEVICE_UP:0001_U:0005"], "HIDClass", null, null, 0x045E, 0x028E, true);
        var detector = new ClawTweaksEnvironmentDetector(new FakeEnumerator([virtualController]), new FakeRuntimeDetector(false), new FakeClawTweaksRuntimeDetector(true));

        var environment = detector.Detect();

        Assert.Equal(ClawTweaksState.Active, environment.ClawTweaksState);
        Assert.Equal(ControllerEnvironmentMode.ClawTweaks, environment.Mode);
    }

    [Fact]
    public void Detect_WhenClawTweaksRuntimeInspectionFails_ReturnsIndeterminate()
    {
        var detector = new ClawTweaksEnvironmentDetector(new FakeEnumerator([]), new FakeRuntimeDetector(false), new ThrowingClawTweaksRuntimeDetector());

        var environment = detector.Detect();

        Assert.Equal(ControllerEnvironmentMode.Indeterminate, environment.Mode);
        Assert.Equal(ClawTweaksState.Indeterminate, environment.ClawTweaksState);
    }

    [Fact]
    public void Detect_WhenMsixPackageRuntimeAndUsbIpTopologyExist_UsesClawTweaksMode()
    {
        var root = new ControllerDeviceInfo("ROOT\\USB\\0000", Guid.Empty, null, [], "ROOT", ["ROOT\\USBIP_WIN2\\UDE"], [], "USB", null, "usbip2_ude", null, null, true);
        var controller = new ControllerDeviceInfo("HID\\VID_045E&PID_028E", Guid.NewGuid(), null, ["ROOT\\USB\\0000"], "HID", ["HID\\VID_045E&PID_028E"], ["HID_DEVICE_UP:0001_U:0005"], "HIDClass", null, null, 0x045E, 0x028E, true);
        var detector = new ClawTweaksEnvironmentDetector(new FakeEnumerator([controller, root]), new FakeRuntimeDetector(false), new FakeClawTweaksRuntimeDetector(true), new FakeInstallationProbe(true));

        var environment = detector.Detect();

        Assert.Equal(ControllerEnvironmentMode.ClawTweaks, environment.Mode);
        Assert.Equal(ClawTweaksState.Active, environment.ClawTweaksState);
    }

    [Fact]
    public void Detect_WhenPackageAndVirtualTopologyExistWithoutRuntime_ReturnsIndeterminate()
    {
        var virtualController = new ControllerDeviceInfo("HID\\VIRTUAL", Guid.NewGuid(), null, ["ROOT\\USBIP_WIN2\\UDE"], "HID", ["HID\\VID_045E&PID_028E"], ["HID_DEVICE_UP:0001_U:0005"], "HIDClass", null, null, 0x045E, 0x028E, true);
        var detector = new ClawTweaksEnvironmentDetector(new FakeEnumerator([virtualController]), new FakeRuntimeDetector(false), new FakeClawTweaksRuntimeDetector(false), new FakeInstallationProbe(true));

        var environment = detector.Detect();

        Assert.Equal(ControllerEnvironmentMode.Indeterminate, environment.Mode);
        Assert.Equal(ClawTweaksState.Indeterminate, environment.ClawTweaksState);
    }

    [Fact]
    public void Detect_WhenPackageIsInstalledWithoutRuntimeOrTopology_UsesStockCenterM()
    {
        var detector = new ClawTweaksEnvironmentDetector(new FakeEnumerator([]), new FakeRuntimeDetector(false), new FakeClawTweaksRuntimeDetector(false), new FakeInstallationProbe(true));

        var environment = detector.Detect();

        Assert.Equal(ControllerEnvironmentMode.StockCenterM, environment.Mode);
        Assert.Equal(ClawTweaksState.InstalledInactive, environment.ClawTweaksState);
    }

    private static ClawTweaksEnvironmentDetector CreateDetector(bool running) => new(new FakeEnumerator([]), new FakeRuntimeDetector(running));

    private sealed class FakeRuntimeDetector(bool running) : IHandheldCompanionRuntimeDetector
    {
        public bool IsRunning() => running;
    }

    private sealed class FakeClawTweaksRuntimeDetector(bool running) : IClawTweaksRuntimeDetector
    {
        public bool IsRunning() => running;
    }

    private sealed class ThrowingClawTweaksRuntimeDetector : IClawTweaksRuntimeDetector
    {
        public bool IsRunning() => throw new InvalidOperationException();
    }

    private sealed class FakeInstallationProbe(bool installed) : IClawTweaksInstallationProbe
    {
        public ClawTweaksInstallationInfo Detect() => installed
            ? new(true, "MSIClaw.ClawTweaks_0.2.0.13_x64__7eszav2039cvc", "C:\\Program Files\\WindowsApps\\MSIClaw.ClawTweaks_0.2.0.13_x64__7eszav2039cvc", "MsixPackage")
            : new(false, null, null, "None");
    }

    private sealed class FakeEnumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator
    {
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => devices;
    }

    private sealed class ContinueUpdateGate : IUpdateGate
    {
        public Task<UpdateGateResult> RunAsync(CancellationToken cancellationToken) => Task.FromResult(UpdateGateResult.Continue);
    }

    private sealed class HhcEnvironmentDetector : IControllerEnvironmentDetector
    {
        public ControllerEnvironment Detect() => new(ControllerEnvironmentMode.HHCManaged, ClawTweaksState.NotInstalled);
    }

    private sealed class FakeWaiter : IControllerEnvironmentWaiter
    {
        public int CallCount { get; private set; }
        public Task<ControllerEnvironmentReadiness> WaitUntilStableAsync(ControllerEnvironmentMode mode, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(ControllerEnvironmentReadiness.Stable);
        }
    }
}
