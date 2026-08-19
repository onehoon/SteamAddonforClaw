using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Recovery;
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
        // The single hardware-support result every downstream feature gate reads (currently the OEM1
        // Center M mapping availability gate). Unsupported hardware must never report support.
        Assert.False(result.HardwareSupported);
    }

    [Fact]
    public async Task IndeterminateHardware_FailsClosedWithoutEnvironmentWork()
    {
        var coordinator = Create(new(HardwareCompatibilityStatus.Indeterminate, null, null, "Probe failed."));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.ShouldStartRuntime);
        Assert.Equal(ControllerEnvironmentMode.Indeterminate, result.EnvironmentMode);
        Assert.Equal(ControllerEnvironmentReadiness.Indeterminate, result.EnvironmentReadiness);
        // Indeterminate is not support: a machine the probe could not identify must not unlock
        // hardware-gated features either.
        Assert.False(result.HardwareSupported);
    }

    [Fact]
    public async Task UpdateRestart_PrecedesHardwareGate()
    {
        var coordinator = new StartupCoordinator(new UpdateGate(UpdateGateResult.RestartScheduled), new ThrowingEnvironmentDetector(), new ThrowingWaiter(), new ThrowingProbeFactory(), new ThrowingEvaluator(), recoveryJournalStore: new NoOpRecoveryJournalStore());

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.ShouldStartRuntime);
    }

    [Fact]
    public async Task SupportedHardware_ContinuesIntoEnvironmentDetection()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(new RecordingUpdateGate(events), new RecordingEnvironmentDetector(events), new RecordingWaiter(events), new RecordingProbeFactory(events), new RecordingEvaluator(), recoveryJournalStore: new NoOpRecoveryJournalStore());

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(ControllerEnvironmentMode.StockCenterM, result.EnvironmentMode);
        Assert.Equal(ControllerEnvironmentReadiness.Stable, result.EnvironmentReadiness);
        Assert.True(result.HardwareSupported);
        Assert.Equal(["UpdateGate", "HardwareCompatibility", "EnvironmentDetector", "EnvironmentWaiter"], events);
    }

    [Fact]
    public async Task UnsupportedWaiter_ReturnsNotApplicableWithoutEnumerating()
    {
        var waiter = new ControllerEnvironmentWaiter(new ThrowingEnumerator(), new ControllerDeviceClassifier(new MsiClawInternalControllerMatcher()));

        var result = await waiter.WaitUntilStableAsync(ControllerEnvironmentMode.Unsupported, CancellationToken.None);

        Assert.Equal(ControllerEnvironmentReadiness.NotApplicable, result);
    }

    private static StartupCoordinator Create(HardwareCompatibilityAssessment assessment) => new(new UpdateGate(UpdateGateResult.Continue), new ThrowingEnvironmentDetector(), new ThrowingWaiter(), new ProbeFactory(), new Evaluator(assessment), recoveryJournalStore: new NoOpRecoveryJournalStore());

    private sealed class UpdateGate(UpdateGateResult result) : IUpdateGate { public Task<UpdateGateResult> RunAsync(CancellationToken _) => Task.FromResult(result); }
    private sealed class ProbeFactory : IWindowsDeviceProbeContextFactory { public DeviceProbeContextCapture Capture() => new(DeviceProbeCaptureStatus.Success, new DeviceProbeContext(), "test"); }
    private sealed class Evaluator(HardwareCompatibilityAssessment result) : IHardwareCompatibilityEvaluator { public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _) => result; }
    private sealed class ThrowingProbeFactory : IWindowsDeviceProbeContextFactory { public DeviceProbeContextCapture Capture() => throw new Xunit.Sdk.XunitException("Hardware probe must not be called."); }
    private sealed class ThrowingEvaluator : IHardwareCompatibilityEvaluator { public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _) => throw new Xunit.Sdk.XunitException("Hardware evaluator must not be called."); }
    private sealed class RecordingUpdateGate(List<string> events) : IUpdateGate { public Task<UpdateGateResult> RunAsync(CancellationToken _) { events.Add("UpdateGate"); return Task.FromResult(UpdateGateResult.Continue); } }
    private sealed class RecordingProbeFactory(List<string> events) : IWindowsDeviceProbeContextFactory { public DeviceProbeContextCapture Capture() { events.Add("HardwareCompatibility"); return new(DeviceProbeCaptureStatus.Success, new DeviceProbeContext(), "test"); } }
    private sealed class RecordingEvaluator : IHardwareCompatibilityEvaluator { public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _) => new(HardwareCompatibilityStatus.Supported, new("msi.claw"), new("msi.claw.cg3em"), "test"); }
    private sealed class RecordingEnvironmentDetector(List<string> events) : IControllerEnvironmentAssessmentProvider { public ControllerEnvironmentAssessmentSnapshot Capture() { events.Add("EnvironmentDetector"); return Stock(); } }
    private sealed class RecordingWaiter(List<string> events) : IControllerEnvironmentWaiter { public Task<ControllerEnvironmentReadiness> WaitUntilStableAsync(ControllerEnvironmentMode _, CancellationToken __) { events.Add("EnvironmentWaiter"); return Task.FromResult(ControllerEnvironmentReadiness.Stable); } }
    private sealed class ThrowingEnvironmentDetector : IControllerEnvironmentAssessmentProvider { public ControllerEnvironmentAssessmentSnapshot Capture() => throw new Xunit.Sdk.XunitException("Environment assessment must not be called."); }
    private static ControllerEnvironmentAssessmentSnapshot Stock() => new([], new(ControllerManagerKind.None, ControllerManagerClassificationReason.NoThirdPartyControllerManager), new(ControllerEnvironmentCompatibilityStatus.Supported, ControllerEnvironmentCompatibilityReason.StockCenterMOnlySupported));
    private sealed class ThrowingWaiter : IControllerEnvironmentWaiter { public Task<ControllerEnvironmentReadiness> WaitUntilStableAsync(ControllerEnvironmentMode _, CancellationToken __) => throw new Xunit.Sdk.XunitException("Environment waiter must not be called."); }
    private sealed class ThrowingEnumerator : IControllerDeviceEnumerator { public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => throw new Xunit.Sdk.XunitException("Enumerator must not be called."); }
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
