using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Recovery;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class HandheldCompanionEnvironmentTests
{
    [Fact]
    public void Detect_WhenHandheldCompanionIsNotInstalledAndNotRunning_UsesStockMode()
    {
        var environment = CreateDetector(running: false, installed: false).Detect();

        Assert.Equal(ControllerEnvironmentMode.StockCenterM, environment.Mode);
    }

    [Fact]
    public void Detect_WhenHandheldCompanionIsInstalledButNotRunning_IsUnsupported()
    {
        var environment = CreateDetector(running: false, installed: true).Detect();

        Assert.Equal(ControllerEnvironmentMode.HHCManaged, environment.Mode);
    }

    [Fact]
    public void Detect_WhenHandheldCompanionIsRunning_YieldsOwnershipImmediately()
    {
        var environment = CreateDetector(running: true, installed: false).Detect();

        Assert.Equal(ControllerEnvironmentMode.HHCManaged, environment.Mode);
    }

    [Fact]
    public void Detect_WhenHandheldCompanionAndClawTweaksEvidenceExist_PrioritizesHandheldCompanion()
    {
        var clawTweaksVirtualController = new ControllerDeviceInfo("HID\\VIRTUAL", Guid.NewGuid(), null, ["ROOT\\USBIP_WIN2\\UDE"], "HID", ["HID\\VID_045E&PID_028E"], ["HID_DEVICE_UP:0001_U:0005"], "HIDClass", null, null, 0x045E, 0x028E, true);
        var environment = new ClawTweaksEnvironmentDetector(new FakeRuntimeDetector(true), handheldCompanionInstallationProbe: new FakeApplicationInstallationProbe(false)).Detect();

        Assert.Equal(ControllerEnvironmentMode.HHCManaged, environment.Mode);
    }

    [Fact]
    public async Task RunAsync_WhenHandheldCompanionOwnsControllers_SkipsReadinessWait()
    {
        var waiter = new FakeWaiter();
        var coordinator = new StartupCoordinator(new ContinueUpdateGate(), new HhcEnvironmentDetector(), waiter, new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: new NoOpRecoveryJournalStore());

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.ShouldStartRuntime);
        Assert.Equal(ControllerEnvironmentMode.HHCManaged, result.EnvironmentMode);
        Assert.Equal(0, waiter.CallCount);
    }

    [Fact]
    public void Detect_WhenClawTweaksIsRunningWithoutTopology_IsStillUnsupported()
    {
        var usbIpDevice = new ControllerDeviceInfo("USB\\VID_1234&PID_5678", Guid.NewGuid(), null, ["ROOT\\USBIP_WIN2\\UDE"], "USB", ["USB\\VID_1234&PID_5678"], [], "USB", null, null, 0x1234, 0x5678, true);
        var detector = new ClawTweaksEnvironmentDetector(new FakeRuntimeDetector(false), new FakeClawTweaksRuntimeDetector(true));

        var environment = detector.Detect();

        Assert.Equal(ClawTweaksState.Active, environment.ClawTweaksState);
        Assert.Equal(ControllerEnvironmentMode.Unsupported, environment.Mode);
    }

    [Theory]
    [InlineData("ROOT\\VIGEMBUS\\0001")]
    [InlineData("ROOT\\HANDHELDCOMPANION\\0001")]
    public void Detect_WhenClawTweaksIsRunning_IsUnsupported(string ancestor)
    {
        var virtualController = new ControllerDeviceInfo("HID\\VIRTUAL", Guid.NewGuid(), null, [ancestor], "HID", ["HID\\VID_045E&PID_028E"], ["HID_DEVICE_UP:0001_U:0005"], "HIDClass", null, null, 0x045E, 0x028E, true);
        var detector = new ClawTweaksEnvironmentDetector(new FakeRuntimeDetector(false), new FakeClawTweaksRuntimeDetector(true));

        var environment = detector.Detect();

        Assert.Equal(ClawTweaksState.Active, environment.ClawTweaksState);
        Assert.Equal(ControllerEnvironmentMode.Unsupported, environment.Mode);
    }

    [Fact]
    public void Detect_WhenClawTweaksIsRunningWithVirtualDevice_IsUnsupported()
    {
        var virtualController = new ControllerDeviceInfo("HID\\VIRTUAL", Guid.NewGuid(), null, ["ROOT\\USBIP_WIN2\\UDE"], "HID", ["HID\\VID_045E&PID_028E"], ["HID_DEVICE_UP:0001_U:0005"], "HIDClass", null, null, 0x045E, 0x028E, true);
        var detector = new ClawTweaksEnvironmentDetector(new FakeRuntimeDetector(false), new FakeClawTweaksRuntimeDetector(true));

        var environment = detector.Detect();

        Assert.Equal(ClawTweaksState.Active, environment.ClawTweaksState);
        Assert.Equal(ControllerEnvironmentMode.Unsupported, environment.Mode);
    }

    [Fact]
    public void Detect_WhenClawTweaksRuntimeInspectionFails_ReturnsIndeterminate()
    {
        var detector = new ClawTweaksEnvironmentDetector(new FakeRuntimeDetector(false), new ThrowingClawTweaksRuntimeDetector());

        var environment = detector.Detect();

        Assert.Equal(ControllerEnvironmentMode.Indeterminate, environment.Mode);
        Assert.Equal(ClawTweaksState.Indeterminate, environment.ClawTweaksState);
    }

    [Fact]
    public void Detect_WhenClawTweaksIsInstalledAndRunning_IsUnsupported()
    {
        var root = new ControllerDeviceInfo("ROOT\\USB\\0000", Guid.Empty, null, [], "ROOT", ["ROOT\\USBIP_WIN2\\UDE"], [], "USB", null, "usbip2_ude", null, null, true);
        var controller = new ControllerDeviceInfo("HID\\VID_045E&PID_028E", Guid.NewGuid(), null, ["ROOT\\USB\\0000"], "HID", ["HID\\VID_045E&PID_028E"], ["HID_DEVICE_UP:0001_U:0005"], "HIDClass", null, null, 0x045E, 0x028E, true);
        var detector = new ClawTweaksEnvironmentDetector(new FakeRuntimeDetector(false), new FakeClawTweaksRuntimeDetector(true), new FakeInstallationProbe(true));

        var environment = detector.Detect();

        Assert.Equal(ControllerEnvironmentMode.Unsupported, environment.Mode);
        Assert.Equal(ClawTweaksState.Active, environment.ClawTweaksState);
    }

    [Fact]
    public void Detect_WhenClawTweaksIsInstalledButNotRunning_IsUnsupported()
    {
        var virtualController = new ControllerDeviceInfo("HID\\VIRTUAL", Guid.NewGuid(), null, ["ROOT\\USBIP_WIN2\\UDE"], "HID", ["HID\\VID_045E&PID_028E"], ["HID_DEVICE_UP:0001_U:0005"], "HIDClass", null, null, 0x045E, 0x028E, true);
        var detector = new ClawTweaksEnvironmentDetector(new FakeRuntimeDetector(false), new FakeClawTweaksRuntimeDetector(false), new FakeInstallationProbe(true));

        var environment = detector.Detect();

        Assert.Equal(ControllerEnvironmentMode.Unsupported, environment.Mode);
        Assert.Equal(ClawTweaksState.InstalledInactive, environment.ClawTweaksState);
    }

    [Fact]
    public void Detect_WhenClawTweaksIsInstalledWithoutRuntime_IsUnsupported()
    {
        var detector = new ClawTweaksEnvironmentDetector(new FakeRuntimeDetector(false), new FakeClawTweaksRuntimeDetector(false), new FakeInstallationProbe(true));

        var environment = detector.Detect();

        Assert.Equal(ControllerEnvironmentMode.Unsupported, environment.Mode);
        Assert.Equal(ClawTweaksState.InstalledInactive, environment.ClawTweaksState);
    }

    private static ClawTweaksEnvironmentDetector CreateDetector(bool running, bool installed) => new(new FakeRuntimeDetector(running), installationProbe: new FakeInstallationProbe(false), handheldCompanionInstallationProbe: new FakeApplicationInstallationProbe(installed));

    private sealed class FakeRuntimeDetector(bool running) : IHandheldCompanionRuntimeDetector
    {
        public bool IsRunning() => running;
    }

    private sealed class FakeClawTweaksRuntimeDetector(bool running) : IClawTweaksRuntimeDetector
    {
        public bool IsRunning(ClawTweaksInstallationInfo _) => running;
    }

    private sealed class ThrowingClawTweaksRuntimeDetector : IClawTweaksRuntimeDetector
    {
        public bool IsRunning(ClawTweaksInstallationInfo _) => throw new InvalidOperationException();
    }

    private sealed class FakeInstallationProbe(bool installed) : IClawTweaksInstallationProbe
    {
        public ClawTweaksInstallationInfo Detect() => installed
            ? new(true, "MSIClaw.ClawTweaks_0.2.0.13_x64__7eszav2039cvc", "C:\\Program Files\\WindowsApps\\MSIClaw.ClawTweaks_0.2.0.13_x64__7eszav2039cvc", "MsixPackage")
            : new(false, null, null, "None");
    }

    private sealed class FakeApplicationInstallationProbe(bool installed) : IApplicationInstallationProbe
    {
        public ApplicationInstallationInfo Detect() => new(installed, installed ? "TestInstalled" : "TestNotInstalled");
    }

    private sealed class ContinueUpdateGate : IUpdateGate
    {
        public Task<UpdateGateResult> RunAsync(CancellationToken cancellationToken) => Task.FromResult(UpdateGateResult.Continue);
    }

    private sealed class HhcEnvironmentDetector : IControllerEnvironmentAssessmentProvider
    {
        public ControllerEnvironmentAssessmentSnapshot Capture() => new([], new(ControllerManagerKind.HandheldCompanion, ControllerManagerClassificationReason.HandheldCompanionDetected), new(ControllerEnvironmentCompatibilityStatus.Unsupported, ControllerEnvironmentCompatibilityReason.HandheldCompanionNotSupportedByCurrentVersion));
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

    private sealed class FakeProbeFactory : IWindowsDeviceProbeContextFactory { public DeviceProbeContextCapture Capture() => new(DeviceProbeCaptureStatus.Success, new DeviceProbeContext(), "test"); }
    private sealed class FakeHardwareEvaluator : IHardwareCompatibilityEvaluator { public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _) => new(HardwareCompatibilityStatus.Supported, new("msi.claw"), new("msi.claw.cg3em"), "test"); }
    private sealed class NoOpRecoveryJournalStore : IRecoveryJournalStore
    {
        public string JournalPath => "noop-recovery-journal.json";
        public bool Exists() => false;
        public string ReadText() => throw new NotSupportedException();
        public void WriteNew(RecoveryJournal journal) => throw new NotSupportedException();
        public void ReplaceExisting(RecoveryJournal journal) => throw new NotSupportedException();
        public void Delete() => throw new NotSupportedException();
    }
}
