using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Startup;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UnsupportedHardwareStartupGateTests
{
    [Fact]
    public async Task UnsupportedHardware_StartsPassiveRuntimeWithoutEnvironmentWork()
    {
        var coordinator = Create(new(HardwareCompatibilityStatus.Unsupported, null, null, "No handheld-device adapter matched."));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.ShouldStartRuntime);
        Assert.Equal(ControllerEnvironmentMode.Unsupported, result.EnvironmentMode);
        Assert.Equal(ControllerEnvironmentReadiness.NotApplicable, result.EnvironmentReadiness);
    }

    [Fact]
    public async Task IndeterminateHardware_FailsClosedWithoutEnvironmentWork()
    {
        var coordinator = Create(new(HardwareCompatibilityStatus.Indeterminate, null, null, "Probe failed."));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.ShouldStartRuntime);
        Assert.Equal(ControllerEnvironmentMode.Indeterminate, result.EnvironmentMode);
        Assert.Equal(ControllerEnvironmentReadiness.Indeterminate, result.EnvironmentReadiness);
    }

    [Fact]
    public async Task UpdateRestart_PrecedesHardwareGate()
    {
        var coordinator = new StartupCoordinator(new UpdateGate(UpdateGateResult.RestartScheduled), new ThrowingEnvironmentDetector(), new ThrowingWaiter(), probeContextFactory: new ProbeFactory(), hardwareCompatibilityEvaluator: new Evaluator(new(HardwareCompatibilityStatus.Unsupported, null, null, "test")));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.ShouldStartRuntime);
    }

    [Fact]
    public void UnsupportedExternalControllerPolicy_PreservesRawAssessment()
    {
        var clear = new ExternalControllerAssessment(ExternalControllerAssessmentStatus.Clear, 0, []);
        var external = new ExternalControllerAssessment(ExternalControllerAssessmentStatus.ExternalPresent, 1, []);

        Assert.Same(clear, ExternalControllerAssessmentPolicy.ApplyEnvironmentSafety(clear, ControllerEnvironmentMode.Unsupported, ControllerEnvironmentReadiness.NotApplicable));
        Assert.Same(external, ExternalControllerAssessmentPolicy.ApplyEnvironmentSafety(external, ControllerEnvironmentMode.Unsupported, ControllerEnvironmentReadiness.NotApplicable));
    }

    [Fact]
    public async Task UnsupportedWaiter_ReturnsNotApplicableWithoutEnumerating()
    {
        var waiter = new ControllerEnvironmentWaiter(new ThrowingEnumerator(), new ControllerDeviceClassifier(new MsiClawInternalControllerMatcher()));

        var result = await waiter.WaitUntilStableAsync(ControllerEnvironmentMode.Unsupported, CancellationToken.None);

        Assert.Equal(ControllerEnvironmentReadiness.NotApplicable, result);
    }

    private static StartupCoordinator Create(HardwareCompatibilityAssessment assessment) => new(new UpdateGate(UpdateGateResult.Continue), new ThrowingEnvironmentDetector(), new ThrowingWaiter(), probeContextFactory: new ProbeFactory(), hardwareCompatibilityEvaluator: new Evaluator(assessment));

    private sealed class UpdateGate(UpdateGateResult result) : IUpdateGate { public Task<UpdateGateResult> RunAsync(CancellationToken _) => Task.FromResult(result); }
    private sealed class ProbeFactory : IWindowsDeviceProbeContextFactory { public DeviceProbeContextCapture Capture() => new(DeviceProbeCaptureStatus.Success, new DeviceProbeContext(), "test"); }
    private sealed class Evaluator(HardwareCompatibilityAssessment result) : IHardwareCompatibilityEvaluator { public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _) => result; }
    private sealed class ThrowingEnvironmentDetector : IControllerEnvironmentDetector { public ControllerEnvironment Detect() => throw new Xunit.Sdk.XunitException("Environment detector must not be called."); }
    private sealed class ThrowingWaiter : IControllerEnvironmentWaiter { public Task<ControllerEnvironmentReadiness> WaitUntilStableAsync(ControllerEnvironmentMode _, CancellationToken __) => throw new Xunit.Sdk.XunitException("Environment waiter must not be called."); }
    private sealed class ThrowingEnumerator : IControllerDeviceEnumerator { public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => throw new Xunit.Sdk.XunitException("Enumerator must not be called."); }
}
