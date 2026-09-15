using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Contracts.Frontend;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UnsupportedHardwareStartupGateTests
{
    [Fact]
    public async Task UnsupportedHardware_StartsPassiveRuntimeWithoutTopologyWork()
    {
        var coordinator = Create(new(HardwareCompatibilityStatus.Unsupported, null, null, "No handheld-device adapter matched."));
        var result = await coordinator.RunAsync(CancellationToken.None);
        Assert.False(result.ShouldStartRuntime);
        Assert.Equal(HardwareCompatibilityStatus.Unsupported, result.HardwareStatus);
        Assert.False(result.HardwareSupported);
    }

    [Fact]
    public async Task IndeterminateHardware_FailsClosedWithoutTopologyWork()
    {
        var coordinator = Create(new(HardwareCompatibilityStatus.Indeterminate, null, null, "Probe failed."));
        var result = await coordinator.RunAsync(CancellationToken.None);
        Assert.False(result.ShouldStartRuntime);
        Assert.Equal(HardwareCompatibilityStatus.Indeterminate, result.HardwareStatus);
        Assert.False(result.HardwareSupported);
    }

    [Fact]
    public async Task SupportedHardware_ContinuesIntoTopologyStabilizationWithoutUpdateWork()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(new RecordingWaiter(events), new RecordingProbeFactory(events), new RecordingEvaluator());
        var result = await coordinator.RunAsync(CancellationToken.None);
        Assert.True(result.HardwareSupported);
        Assert.Equal(HardwareCompatibilityStatus.Supported, result.HardwareStatus);
        Assert.Equal(FrontendCenterMStartupState.Enabled, result.CenterMStartupState);
        Assert.Equal(["HardwareCompatibility", "TopologyWaiter"], events);
    }

    private static StartupCoordinator Create(HardwareCompatibilityAssessment assessment) =>
        new(new ThrowingWaiter(), new ProbeFactory(), new Evaluator(assessment));

    private sealed class ProbeFactory : IWindowsDeviceProbeContextFactory
    {
        public DeviceProbeContextCapture Capture() => new(DeviceProbeCaptureStatus.Success, new DeviceProbeContext(), "test");
    }

    private sealed class Evaluator(HardwareCompatibilityAssessment result) : IHardwareCompatibilityEvaluator
    {
        public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _) => result;
    }

    private sealed class RecordingProbeFactory(List<string> events) : IWindowsDeviceProbeContextFactory
    {
        public DeviceProbeContextCapture Capture()
        {
            events.Add("HardwareCompatibility");
            return new(DeviceProbeCaptureStatus.Success, new DeviceProbeContext(), "test");
        }
    }

    private sealed class RecordingEvaluator : IHardwareCompatibilityEvaluator
    {
        public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _) =>
            new(HardwareCompatibilityStatus.Supported, new("msi.claw"), new("msi.claw.cg3em"), "test");
    }

    private sealed class RecordingWaiter(List<string> events) : IControllerTopologyWaiter
    {
        public Task<ControllerTopologyReadiness> WaitUntilStableAsync(CancellationToken _)
        {
            events.Add("TopologyWaiter");
            return Task.FromResult(ControllerTopologyReadiness.Stable);
        }
    }

    private sealed class ThrowingWaiter : IControllerTopologyWaiter
    {
        public Task<ControllerTopologyReadiness> WaitUntilStableAsync(CancellationToken _) =>
            throw new Xunit.Sdk.XunitException("Topology waiter must not be called.");
    }
}
