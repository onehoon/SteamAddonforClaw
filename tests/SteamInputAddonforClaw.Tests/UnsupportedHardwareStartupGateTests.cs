using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Recovery;
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
        // The single hardware-support result every downstream feature gate reads (currently the OEM1
        // Center M mapping availability gate). Unsupported hardware must never report support.
        Assert.False(result.HardwareSupported);
    }

    [Fact]
    public async Task IndeterminateHardware_FailsClosedWithoutTopologyWork()
    {
        var coordinator = Create(new(HardwareCompatibilityStatus.Indeterminate, null, null, "Probe failed."));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.ShouldStartRuntime);
        Assert.Equal(HardwareCompatibilityStatus.Indeterminate, result.HardwareStatus);
        // Indeterminate is not support: a machine the probe could not identify must not unlock
        // hardware-gated features either.
        Assert.False(result.HardwareSupported);
    }

    [Fact]
    public async Task UpdateRestart_PrecedesHardwareGate()
    {
        var coordinator = new StartupCoordinator(new UpdateGate(UpdateGateResult.RestartScheduled), new ThrowingWaiter(), new ThrowingProbeFactory(), new ThrowingEvaluator(), recoveryJournalStore: new NoOpRecoveryJournalStore());

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.ShouldStartRuntime);
    }

    [Fact]
    public async Task SupportedHardware_ContinuesIntoTopologyStabilization()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(new RecordingUpdateGate(events), new RecordingWaiter(events), new RecordingProbeFactory(events), new RecordingEvaluator(), recoveryJournalStore: new NoOpRecoveryJournalStore());

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.HardwareSupported);
        Assert.Equal(HardwareCompatibilityStatus.Supported, result.HardwareStatus);
        Assert.Equal(FrontendCenterMStartupState.Enabled, result.CenterMStartupState);
        Assert.Equal(["UpdateGate", "HardwareCompatibility", "TopologyWaiter"], events);
    }

    private static StartupCoordinator Create(HardwareCompatibilityAssessment assessment) => new(new UpdateGate(UpdateGateResult.Continue), new ThrowingWaiter(), new ProbeFactory(), new Evaluator(assessment), recoveryJournalStore: new NoOpRecoveryJournalStore());

    private sealed class UpdateGate(UpdateGateResult result) : IUpdateGate { public Task<UpdateGateResult> RunAsync(CancellationToken _) => Task.FromResult(result); }
    private sealed class ProbeFactory : IWindowsDeviceProbeContextFactory { public DeviceProbeContextCapture Capture() => new(DeviceProbeCaptureStatus.Success, new DeviceProbeContext(), "test"); }
    private sealed class Evaluator(HardwareCompatibilityAssessment result) : IHardwareCompatibilityEvaluator { public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _) => result; }
    private sealed class ThrowingProbeFactory : IWindowsDeviceProbeContextFactory { public DeviceProbeContextCapture Capture() => throw new Xunit.Sdk.XunitException("Hardware probe must not be called."); }
    private sealed class ThrowingEvaluator : IHardwareCompatibilityEvaluator { public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _) => throw new Xunit.Sdk.XunitException("Hardware evaluator must not be called."); }
    private sealed class RecordingUpdateGate(List<string> events) : IUpdateGate { public Task<UpdateGateResult> RunAsync(CancellationToken _) { events.Add("UpdateGate"); return Task.FromResult(UpdateGateResult.Continue); } }
    private sealed class RecordingProbeFactory(List<string> events) : IWindowsDeviceProbeContextFactory { public DeviceProbeContextCapture Capture() { events.Add("HardwareCompatibility"); return new(DeviceProbeCaptureStatus.Success, new DeviceProbeContext(), "test"); } }
    private sealed class RecordingEvaluator : IHardwareCompatibilityEvaluator { public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _) => new(HardwareCompatibilityStatus.Supported, new("msi.claw"), new("msi.claw.cg3em"), "test"); }
    private sealed class RecordingWaiter(List<string> events) : IControllerTopologyWaiter { public Task<ControllerTopologyReadiness> WaitUntilStableAsync(CancellationToken _) { events.Add("TopologyWaiter"); return Task.FromResult(ControllerTopologyReadiness.Stable); } }
    private sealed class ThrowingWaiter : IControllerTopologyWaiter { public Task<ControllerTopologyReadiness> WaitUntilStableAsync(CancellationToken _) => throw new Xunit.Sdk.XunitException("Topology waiter must not be called."); }
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
