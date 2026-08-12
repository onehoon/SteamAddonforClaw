using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class StartupCoordinatorTests
{
    [Fact]
    public async Task StartupDoesNotInvokeExactPowerRecovery()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeEnvironmentDetector(events), new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator());
        var result = await coordinator.RunAsync(CancellationToken.None);
        Assert.True(result.RecoverySafe);
        Assert.Equal(["UpdateGate", "EnvironmentDetector", "EnvironmentWaiter"], events);
    }

    [Fact]
    public async Task CanStartRuntimeAsync_WhenNoUpdateExists_WaitsForEnvironmentAfterUpdateGate()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeEnvironmentDetector(events),
            new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator());

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.ShouldStartRuntime);
        Assert.Equal(ControllerEnvironmentMode.StockCenterM, result.EnvironmentMode);
        Assert.Equal(["UpdateGate", "EnvironmentDetector", "EnvironmentWaiter"], events);
    }

    [Fact]
    public async Task BaselineValidation_RunsOnlyAfterStableStockCenterMReadiness()
    {
        var events = new List<string>();
        var baseline = new FakeBaselineValidator(events, true);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeEnvironmentDetector(events), new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            startupBaselineValidator: baseline);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.RecoverySafe);
        Assert.Equal(["UpdateGate", "EnvironmentDetector", "EnvironmentWaiter", "Baseline"], events);
        Assert.Equal(1, baseline.Calls);
    }

    [Fact]
    public async Task BaselineValidation_DoesNotRunForUnsupportedEnvironment()
    {
        var events = new List<string>();
        var baseline = new FakeBaselineValidator(events, true);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeEnvironmentDetector(events, ClawTweaksState.InstalledInactive), new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            startupBaselineValidator: baseline);

        await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(0, baseline.Calls);
    }

    [Fact]
    public async Task CanStartRuntimeAsync_WhenUpdateIsScheduled_DoesNotInitializeEnvironment()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FakeUpdateGate(events, UpdateGateResult.RestartScheduled),
            new FakeEnvironmentDetector(events),
            new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator());

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.ShouldStartRuntime);
        Assert.Equal(ControllerEnvironmentMode.Indeterminate, result.EnvironmentMode);
        Assert.Equal(["UpdateGate"], events);
    }

    [Fact]
    public async Task RunAsync_WhenClawTweaksIsStarting_DoesNotPollOrWaitForStabilization()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeEnvironmentDetector(events, ClawTweaksState.Starting),
            new FakeEnvironmentWaiter(events),
            new FakeProbeFactory(), new FakeHardwareEvaluator());

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(ControllerEnvironmentReadiness.Indeterminate, result.EnvironmentReadiness);
        Assert.Equal(ControllerEnvironmentMode.Indeterminate, result.EnvironmentMode);
        Assert.Equal(["UpdateGate", "EnvironmentDetector"], events);
    }

    [Fact]
    public async Task RunAsync_WhenClawTweaksIsInstalledOrRunning_ReturnsPassiveWithoutReadinessWait()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeEnvironmentDetector(events, ClawTweaksState.InstalledInactive),
            new FakeEnvironmentWaiter(events),
            new FakeProbeFactory(), new FakeHardwareEvaluator());

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.ShouldStartRuntime);
        Assert.Equal(ControllerEnvironmentReadiness.NotApplicable, result.EnvironmentReadiness);
        Assert.Equal(ControllerEnvironmentMode.Unsupported, result.EnvironmentMode);
        Assert.DoesNotContain("EnvironmentWaiter", events);
    }

    [Fact]
    public async Task RunAsync_WhenClawTweaksIsInstalledInactive_IsUnsupportedAndPassive()
    {
        var events = new List<string>();
        var waiter = new FakeEnvironmentWaiter(events);
        var coordinator = new StartupCoordinator(
            new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeEnvironmentDetector(events, ClawTweaksState.InstalledInactive),
            waiter, new FakeProbeFactory(), new FakeHardwareEvaluator());

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(ControllerEnvironmentMode.Unsupported, result.EnvironmentMode);
        Assert.Empty(waiter.Modes);
    }

    [Fact]
    public async Task RunAsync_WhenClawTweaksStateIsIndeterminate_SkipsReadinessChecks()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeEnvironmentDetector(events, ClawTweaksState.Indeterminate),
            new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator());

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(ControllerEnvironmentMode.Indeterminate, result.EnvironmentMode);
        Assert.Equal(ControllerEnvironmentReadiness.Indeterminate, result.EnvironmentReadiness);
        Assert.DoesNotContain("EnvironmentWaiter", events);
    }

    private sealed class FakeUpdateGate(List<string> events, UpdateGateResult result) : IUpdateGate
    {
        public Task<UpdateGateResult> RunAsync(CancellationToken cancellationToken)
        {
            events.Add("UpdateGate");
            return Task.FromResult(result);
        }
    }

    private sealed class FakeBaselineValidator(List<string> events, bool safe) : IStartupNativeBaselineValidator
    {
        public int Calls { get; private set; }
        public Task<StartupNativeBaselineResult> ValidateAsync(CancellationToken cancellationToken)
        {
            Calls++; events.Add("Baseline");
            return Task.FromResult(new StartupNativeBaselineResult(safe, safe ? "ok" : "failed"));
        }
    }

    private sealed class FakeEnvironmentWaiter(List<string> events) : IControllerEnvironmentWaiter
    {
        public List<ControllerEnvironmentMode> Modes { get; } = [];

        public Task<ControllerEnvironmentReadiness> WaitUntilStableAsync(ControllerEnvironmentMode mode, CancellationToken cancellationToken)
        {
            events.Add("EnvironmentWaiter");
            Modes.Add(mode);
            return Task.FromResult(ControllerEnvironmentReadiness.Stable);
        }
    }

    private sealed class FakeEnvironmentDetector(List<string> events, params ClawTweaksState[] states) : IControllerEnvironmentDetector
    {
        private readonly Queue<ClawTweaksState> _states = new(states.Length == 0 ? [ClawTweaksState.NotInstalled] : states);

        public ControllerEnvironment Detect()
        {
            events.Add("EnvironmentDetector");
            var state = _states.Count > 1 ? _states.Dequeue() : _states.Peek();
            var mode = state switch
            {
                ClawTweaksState.Active or ClawTweaksState.InstalledInactive => ControllerEnvironmentMode.Unsupported,
                ClawTweaksState.NotInstalled => ControllerEnvironmentMode.StockCenterM,
                _ => ControllerEnvironmentMode.Indeterminate
            };
            return new ControllerEnvironment(mode, state);
        }
    }

    private sealed class FakeProbeFactory : IWindowsDeviceProbeContextFactory { public DeviceProbeContextCapture Capture() => new(DeviceProbeCaptureStatus.Success, new DeviceProbeContext(), "test"); }
    private sealed class FakeHardwareEvaluator : IHardwareCompatibilityEvaluator { public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _) => new(HardwareCompatibilityStatus.Supported, new("msi.claw"), new("msi.claw.cg3em"), "test"); }
    private sealed class ThrowingEnvironmentDetector : IControllerEnvironmentDetector { public ControllerEnvironment Detect() => throw new Xunit.Sdk.XunitException("Environment detection must not run after recovery failure."); }
    private sealed class ThrowingEnvironmentWaiter : IControllerEnvironmentWaiter { public Task<ControllerEnvironmentReadiness> WaitUntilStableAsync(ControllerEnvironmentMode _, CancellationToken __) => throw new Xunit.Sdk.XunitException("Environment wait must not run after recovery failure."); }
    private sealed class ThrowingProbeFactory : IWindowsDeviceProbeContextFactory { public DeviceProbeContextCapture Capture() => throw new Xunit.Sdk.XunitException("Hardware probe must not run after recovery failure."); }
    private sealed class ThrowingHardwareEvaluator : IHardwareCompatibilityEvaluator { public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _) => throw new Xunit.Sdk.XunitException("Hardware evaluator must not run after recovery failure."); }
}
