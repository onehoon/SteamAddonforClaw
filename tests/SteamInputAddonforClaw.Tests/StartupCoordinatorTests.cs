using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.HidHide;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class StartupCoordinatorTests : IDisposable
{
    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), $"StartupCoordinatorTests.{Guid.NewGuid():N}");
    private readonly AppLogLevel _previousLogLevel = AppLog.MinimumLevelOverride;
    private readonly string? _previousLogDirectory = AppLog.DirectoryOverride;

    public StartupCoordinatorTests()
    {
        AppLog.DrainForTests();
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        AppLog.DirectoryOverride = _logDirectory;
    }

    public void Dispose()
    {
        AppLog.DrainForTests();
        AppLog.MinimumLevelOverride = _previousLogLevel;
        AppLog.DirectoryOverride = _previousLogDirectory;
        if (Directory.Exists(_logDirectory)) Directory.Delete(_logDirectory, recursive: true);
    }

    [Fact]
    public async Task VerifiedStockBaselineAfterStableTopology_IsRecoverySafe()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), stockCenterMBaseline: new FakeBaseline(events));
        var result = await coordinator.RunAsync(CancellationToken.None);
        Assert.True(result.RecoverySafe);
        Assert.Equal(["TopologyWaiter", "Baseline"], events);
    }

    [Fact]
    public async Task BaselineFailure_BlocksRoutingButStartsPassiveRuntime()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), stockCenterMBaseline: new FakeBaseline(events, false));
        var result = await coordinator.RunAsync(CancellationToken.None);
        Assert.False(result.RecoverySafe);
        Assert.Equal(FrontendCenterMStartupState.Enabled, result.CenterMStartupState);
        Assert.False(result.StockTopologyUnreadyBeforeBaseline);
        Assert.Equal(["TopologyWaiter", "Baseline"], events);
    }

    [Fact]
    public async Task SupportedStartup_DoesNotRunUpdateWorkBeforeHardwareEvaluation()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new ThrowingTopologyWaiter(), new FakeProbeFactory(), new FixedHardwareEvaluator(HardwareCompatibilityStatus.Unsupported), stockCenterMBaseline: new FakeBaseline(events),
            hardwareProbeTimeout: TimeSpan.FromMilliseconds(20), hardwareProbeDelay: (_, _) => Task.CompletedTask);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Empty(events);
        Assert.False(result.ShouldStartRuntime);
    }

    [Fact]
    public async Task CancellationDuringBaseline_Propagates()
    {
        var events = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var coordinator = new StartupCoordinator(
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            stockCenterMBaseline: new ThrowingBaseline(events, new OperationCanceledException(cancellation.Token)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.RunAsync(cancellation.Token));

        Assert.Equal(["TopologyWaiter", "Baseline"], events);
    }

    [Fact]
    public async Task CanStartRuntimeAsync_WaitsForTopologyWithoutAnUpdateGate()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator());

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.ShouldStartRuntime);
        Assert.Equal(FrontendCenterMStartupState.Enabled, result.CenterMStartupState);
        Assert.False(result.RecoverySafe);
        Assert.Equal(["TopologyWaiter"], events);
    }

    [Fact]
    public async Task SupportedStartup_ContinuesWithoutAnUpdateRestart()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator());

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.ShouldStartRuntime);
        Assert.DoesNotContain("UpdateGate", events);
    }

    [Theory]
    [InlineData((int)HardwareCompatibilityStatus.Unsupported)]
    [InlineData((int)HardwareCompatibilityStatus.Indeterminate)]
    public async Task NonSupportedHardware_DoesNotEstablishStartupBoundary(int statusValue)
    {
        var status = (HardwareCompatibilityStatus)statusValue;
        var events = new List<string>();
        // Indeterminate is retried by the hardware probe stabilization; this fake never
        // resolves, so use a non-waiting fake delay and a short timeout to avoid a real 5s wait.
        var coordinator = new StartupCoordinator(new ThrowingTopologyWaiter(),
            new FakeProbeFactory(), new FixedHardwareEvaluator(status), stockCenterMBaseline: new FakeBaseline(events),
            hardwareProbeTimeout: TimeSpan.FromMilliseconds(20), hardwareProbeDelay: (_, _) => Task.CompletedTask);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.RecoverySafe);
        Assert.DoesNotContain("Baseline", events);
    }

    [Fact]
    public async Task StockReadinessIndeterminate_DoesNotEstablishStartupBoundary()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(new FixedTopologyWaiter(events, ControllerTopologyReadiness.Indeterminate), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            stockCenterMBaseline: new FakeBaseline(events));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.RecoverySafe);
        Assert.DoesNotContain("Baseline", events);
        Assert.False(result.StockTopologyUnreadyBeforeBaseline); // Legacy construction did not prove exact startup roots.
    }

    [Fact]
    public async Task EnabledSupportedStockTopologyTimeout_ExposesOnlyTheNoBaselineRepairFact()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FixedTopologyWaiter(events, ControllerTopologyReadiness.Indeterminate), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            stockCenterMBaseline: new FakeBaseline(events),
            captureCenterMStartup: () => Roots(FrontendCenterMStartupState.Enabled));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.ShouldStartRuntime);
        Assert.False(result.RecoverySafe);
        Assert.True(result.StockTopologyUnreadyBeforeBaseline);
        Assert.Equal(["TopologyWaiter"], events);
    }

    [Fact]
    public async Task FreshStockVerification_WaitsForStableTopologyThenEstablishesBaseline()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), stockCenterMBaseline: new FakeBaseline(events));

        var verified = await coordinator.VerifyCurrentStockTopologyAndBaselineAsync(CancellationToken.None);

        Assert.True(verified);
        Assert.Equal(["TopologyWaiter", "Baseline"], events);
    }

    [Fact]
    public async Task FreshStockVerification_DoesNotAttemptBaselineWhenTopologyIsIndeterminate()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FixedTopologyWaiter(events, ControllerTopologyReadiness.Indeterminate), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            stockCenterMBaseline: new FakeBaseline(events));

        var verified = await coordinator.VerifyCurrentStockTopologyAndBaselineAsync(CancellationToken.None);

        Assert.False(verified);
        Assert.Equal(["TopologyWaiter"], events);
    }

    [Fact]
    public async Task FreshStockVerification_BaselineExceptionFailsClosed()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            stockCenterMBaseline: new ThrowingBaseline(events, new InvalidOperationException("test baseline failure")));

        var verified = await coordinator.VerifyCurrentStockTopologyAndBaselineAsync(CancellationToken.None);

        Assert.False(verified);
        Assert.Equal(["TopologyWaiter", "Baseline"], events);
    }

    [Fact]
    public async Task FreshStockVerification_WithoutBaselineOwnerFailsClosed()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator());

        var verified = await coordinator.VerifyCurrentStockTopologyAndBaselineAsync(CancellationToken.None);

        Assert.False(verified);
        Assert.Empty(events);
    }

    [Fact]
    public async Task EnabledRoots_StableTopology_ReachesBaseline()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            stockCenterMBaseline: new FakeBaseline(events));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupState.Enabled, result.CenterMStartupState);
        Assert.Contains("Baseline", events);
        Assert.True(result.RecoverySafe);
        Assert.False(result.StockTopologyUnreadyBeforeBaseline);
    }

    private sealed class FakeBaseline(List<string> events, bool succeeded = true) : IStockCenterMStartupBaseline
    {
        public Task<StockCenterMStartupBaselineResult> EstablishAsync(CancellationToken cancellationToken)
        {
            events.Add("Baseline");
            return Task.FromResult(new StockCenterMStartupBaselineResult(succeeded, false, succeeded ? "test" : "failed"));
        }
    }

    private sealed class ThrowingBaseline(List<string> events, Exception exception) : IStockCenterMStartupBaseline
    {
        public Task<StockCenterMStartupBaselineResult> EstablishAsync(CancellationToken cancellationToken)
        {
            events.Add("Baseline");
            return Task.FromException<StockCenterMStartupBaselineResult>(exception);
        }
    }

    private sealed class FakeTopologyWaiter(List<string> events) : IControllerTopologyWaiter
    {
        public int Calls { get; private set; }
        public int FastCalls { get; private set; }

        public Task<ControllerTopologyReadiness> WaitUntilStableAsync(CancellationToken cancellationToken)
        {
            events.Add("TopologyWaiter");
            Calls++;
            return Task.FromResult(ControllerTopologyReadiness.Stable);
        }

        public Task<ControllerTopologyReadiness> WaitForFirstUnambiguousControlHidAsync(CancellationToken cancellationToken)
        {
            events.Add("FirstControlHidWaiter");
            FastCalls++;
            return Task.FromResult(ControllerTopologyReadiness.Stable);
        }
    }

    private sealed class FixedTopologyWaiter(List<string> events, ControllerTopologyReadiness readiness) : IControllerTopologyWaiter
    {
        public Task<ControllerTopologyReadiness> WaitUntilStableAsync(CancellationToken cancellationToken)
        {
            events.Add("TopologyWaiter");
            return Task.FromResult(readiness);
        }
    }

    [Fact]
    public async Task HardwareIndeterminate_ThenSupported_RetriesAndContinues()
    {
        var events = new List<string>();
        var evaluator = new SequencedHardwareEvaluator([
            new(HardwareCompatibilityStatus.Indeterminate, null, null, "test"),
            new(HardwareCompatibilityStatus.Supported, new("msi.claw"), new("msi.claw.cg3em"), "test")
        ]);
        var delay = new RecordingHardwareProbeDelay();
        var coordinator = new StartupCoordinator(new FakeTopologyWaiter(events),
            new FakeProbeFactory(), evaluator, stockCenterMBaseline: new FakeBaseline(events), hardwareProbeDelay: delay.DelayAsync);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(2, evaluator.CallCount);
        Assert.Equal([TimeSpan.FromMilliseconds(500)], delay.Delays);
        Assert.True(result.RecoverySafe);
        AppLog.DrainForTests();
        Assert.DoesNotContain("[ERROR]", File.ReadAllText(AppLog.CurrentLogFilePath));
    }

    [Fact]
    public async Task StartupResult_RetainsIdentityFromFinalStabilizedHardwareQualification()
    {
        var events = new List<string>();
        var initialIdentity = new WindowsDeviceIdentity("MSI", "Claw 7", "InitialBoard", true);
        var finalIdentity = new WindowsDeviceIdentity("MSI", "Claw 8 AI+", "MS-1T91", true);
        var probeFactory = new SequencedProbeFactory([
            CaptureWithIdentity(initialIdentity),
            CaptureWithIdentity(finalIdentity),
        ]);
        var evaluator = new SequencedHardwareEvaluator([
            new(HardwareCompatibilityStatus.Indeterminate, null, null, "StartupProbeTransient"),
            new(HardwareCompatibilityStatus.Supported, new("msi.claw"), new("msi.claw.cg3em"), "FinalQualification"),
        ]);
        var coordinator = new StartupCoordinator(new FakeTopologyWaiter(events), probeFactory, evaluator,
            stockCenterMBaseline: new FakeBaseline(events), hardwareProbeDelay: (_, _) => Task.CompletedTask);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(2, probeFactory.CallCount);
        Assert.Equal(finalIdentity, result.HardwareIdentity);
        Assert.Equal(HardwareCompatibilityStatus.Supported, result.HardwareStatus);
        Assert.Equal(new HandheldDeviceId("msi.claw"), result.HardwareDeviceFamily);
        Assert.Equal(new HandheldDeviceModelId("msi.claw.cg3em"), result.HardwareDeviceModel);
        Assert.Equal("FinalQualification", result.HardwareReason);
    }

    [Fact]
    public async Task HardwarePnpReadFailure_ThenSupported_LogsRecoveryWithoutError()
    {
        var events = new List<string>();
        var pnpFailure = new PnpDeviceReadException("SetupDiGetDeviceRegistryPropertyW", 5, 1,
            "SPDRP_HARDWAREID", 32, @"HID\VID_0DB0&PID_1902\CLAW_A", 11);
        var evaluator = new SequencedHardwareEvaluator([
            new HardwareCompatibilityAssessment(HardwareCompatibilityStatus.Indeterminate, null, null, "PnpReadFailed")
            {
                PnpReadFailure = pnpFailure,
            },
            new(HardwareCompatibilityStatus.Supported, new("msi.claw"), new("msi.claw.cg3em"), "test"),
        ]);
        var coordinator = new StartupCoordinator(new FakeTopologyWaiter(events),
            new FakeProbeFactory(), evaluator, stockCenterMBaseline: new FakeBaseline(events),
            hardwareProbeDelay: (_, _) => Task.CompletedTask);

        var result = await coordinator.RunAsync(CancellationToken.None);
        AppLog.DrainForTests();
        var log = File.ReadAllText(AppLog.CurrentLogFilePath);

        Assert.True(result.RecoverySafe);
        Assert.Contains("Event=HardwareProbePnpReadRecovered", log);
        Assert.DoesNotContain("[ERROR]", log);
    }

    [Fact]
    public async Task PersistentHardwarePnpReadFailure_ExhaustingAdmissionWindowLogsOneTerminalError()
    {
        var events = new List<string>();
        var pnpFailure = new PnpDeviceReadException("SetupDiGetDeviceRegistryPropertyW", 5, 1,
            "SPDRP_HARDWAREID", 32, @"HID\VID_0DB0&PID_1902\CLAW_A", 11);
        var assessment = new HardwareCompatibilityAssessment(HardwareCompatibilityStatus.Indeterminate, null, null, "PnpReadFailed")
        {
            PnpReadFailure = pnpFailure,
        };
        var evaluator = new SequencedHardwareEvaluator([assessment], repeatLast: true);
        var coordinator = new StartupCoordinator(new ThrowingTopologyWaiter(),
            new FakeProbeFactory(), evaluator, stockCenterMBaseline: new FakeBaseline(events),
            hardwareProbeTimeout: TimeSpan.FromMilliseconds(20), hardwareProbeDelay: (_, _) => Task.CompletedTask);

        var result = await coordinator.RunAsync(CancellationToken.None);
        AppLog.DrainForTests();
        var log = File.ReadAllText(AppLog.CurrentLogFilePath);
        var errorLines = log.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("[ERROR]", StringComparison.Ordinal)).ToArray();

        Assert.False(result.ShouldStartRuntime);
        Assert.Equal(HardwareCompatibilityStatus.Indeterminate, result.HardwareStatus);
        Assert.True(evaluator.CallCount > 1);
        Assert.Single(errorLines);
        Assert.Contains("Event=HardwareProbeStabilizationTimedOut", errorLines[0]);
        Assert.Contains("Operation=SetupDiGetDeviceRegistryPropertyW", errorLines[0]);
        Assert.Contains("Win32Error=5", errorLines[0]);
        Assert.Contains("Action=Passive", errorLines[0]);
    }

    [Fact]
    public async Task HardwareProbeTimeoutAfterPnpReadRecovery_UsesWarnForFinalNonPnpUnknownAssessment()
    {
        var events = new List<string>();
        var pnpFailure = new PnpDeviceReadException("SetupDiGetDeviceRegistryPropertyW", 5, 1,
            "SPDRP_HARDWAREID", 32, @"HID\VID_0DB0&PID_1902\CLAW_A", 11);
        var evaluator = new SequencedHardwareEvaluator([
            new HardwareCompatibilityAssessment(HardwareCompatibilityStatus.Indeterminate, null, null, "PnpReadFailed")
            {
                PnpReadFailure = pnpFailure,
            },
            new(HardwareCompatibilityStatus.Indeterminate, null, null, "UnknownHardware"),
        ], repeatLast: true);
        var coordinator = new StartupCoordinator(new ThrowingTopologyWaiter(),
            new FakeProbeFactory(), evaluator, stockCenterMBaseline: new FakeBaseline(events),
            hardwareProbeTimeout: TimeSpan.FromMilliseconds(20), hardwareProbeDelay: (_, _) => Task.CompletedTask);

        var result = await coordinator.RunAsync(CancellationToken.None);
        AppLog.DrainForTests();
        var log = File.ReadAllText(AppLog.CurrentLogFilePath);
        var errorLines = log.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("[ERROR]", StringComparison.Ordinal)).ToArray();
        var timeoutLine = log.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.Contains("Startup hardware probe stabilization timed out.", StringComparison.Ordinal));

        Assert.False(result.ShouldStartRuntime);
        Assert.Equal(HardwareCompatibilityStatus.Indeterminate, result.HardwareStatus);
        Assert.True(evaluator.CallCount > 1);
        Assert.Empty(errorLines);
        Assert.Contains("[WARN]", timeoutLine);
        Assert.DoesNotContain("SetupDiGetDeviceRegistryPropertyW", timeoutLine);
    }

    [Fact]
    public async Task HardwareNoAdapterMatched_ThenSupported_RetriesAndContinues()
    {
        var events = new List<string>();
        var evaluator = new SequencedHardwareEvaluator([
            new(HardwareCompatibilityStatus.Unsupported, null, null, "No handheld-device adapter matched."),
            new(HardwareCompatibilityStatus.Supported, new("msi.claw"), new("msi.claw.cg3em"), "test")
        ]);
        var delay = new RecordingHardwareProbeDelay();
        var coordinator = new StartupCoordinator(new FakeTopologyWaiter(events),
            new FakeProbeFactory(), evaluator, stockCenterMBaseline: new FakeBaseline(events), hardwareProbeDelay: delay.DelayAsync);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(2, evaluator.CallCount);
        Assert.Equal([TimeSpan.FromMilliseconds(500)], delay.Delays);
        Assert.True(result.RecoverySafe);
    }

    [Fact]
    public async Task HardwareTerminalUnsupported_DoesNotRetry()
    {
        var events = new List<string>();
        var evaluator = new SequencedHardwareEvaluator([new(HardwareCompatibilityStatus.Unsupported, null, null, "MsiClawModelUnsupported")]);
        var delay = new RecordingHardwareProbeDelay();
        var coordinator = new StartupCoordinator(new ThrowingTopologyWaiter(),
            new FakeProbeFactory(), evaluator, stockCenterMBaseline: new FakeBaseline(events), hardwareProbeDelay: delay.DelayAsync);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(1, evaluator.CallCount);
        Assert.Empty(delay.Delays);
        Assert.Equal(HardwareCompatibilityStatus.Unsupported, result.HardwareStatus);
    }

    [Fact]
    public async Task HardwareSupportedOnFirstAttempt_NoDelay()
    {
        var events = new List<string>();
        var delay = new RecordingHardwareProbeDelay();
        var coordinator = new StartupCoordinator(new FakeTopologyWaiter(events),
            new FakeProbeFactory(), new FakeHardwareEvaluator(), stockCenterMBaseline: new FakeBaseline(events), hardwareProbeDelay: delay.DelayAsync);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Empty(delay.Delays);
        Assert.True(result.RecoverySafe);
    }

    [Theory]
    [InlineData((int)HardwareCompatibilityStatus.Indeterminate, "UnknownHardware")]
    [InlineData((int)HardwareCompatibilityStatus.Unsupported, "No handheld-device adapter matched.")]
    public async Task HardwareTransientNeverResolves_TimesOutPassive(int statusValue, string reason)
    {
        var events = new List<string>();
        var evaluator = new SequencedHardwareEvaluator([new((HardwareCompatibilityStatus)statusValue, null, null, reason)], repeatLast: true);
        var coordinator = new StartupCoordinator(new ThrowingTopologyWaiter(),
            new FakeProbeFactory(), evaluator, stockCenterMBaseline: new FakeBaseline(events),
            hardwareProbeTimeout: TimeSpan.FromMilliseconds(20), hardwareProbeDelay: (_, _) => Task.CompletedTask);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(evaluator.CallCount > 1);
        Assert.False(result.RecoverySafe);
        Assert.False(result.StockTopologyUnreadyBeforeBaseline);
        AppLog.DrainForTests();
        var log = File.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.DoesNotContain("[ERROR]", log);
        Assert.Contains("[WARN]", log);
    }

    [Fact]
    public async Task CancellationDuringHardwareProbeStabilization_Propagates()
    {
        var events = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var evaluator = new SequencedHardwareEvaluator([new(HardwareCompatibilityStatus.Indeterminate, null, null, "test")], repeatLast: true);
        var coordinator = new StartupCoordinator(new ThrowingTopologyWaiter(),
            new FakeProbeFactory(), evaluator, stockCenterMBaseline: new FakeBaseline(events),
            hardwareProbeDelay: (_, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.RunAsync(cancellation.Token));

        Assert.Equal(1, evaluator.CallCount);
    }

    // ---- PR4: authority-aware startup branch (work order sections 7-9/17/26) ----

    private static FrontendCenterMStartupSnapshot Roots(FrontendCenterMStartupState state) => new(state, false, false, false, null);

    [Fact]
    public async Task DisabledRoots_RunReadOnlyAdmission_NeverStockBaselineOrRecoveryMutation()
    {
        var events = new List<string>();
        // A stale journal is present: the Disabled path must not read, clean, or delete it.
        var waiter = new FakeTopologyWaiter(events);
        var admission = new FakeDisabledBootAdmission(events, DisabledBootAdmissionOutcome.Ready);
        var coordinator = new StartupCoordinator(
            waiter, new FakeProbeFactory(), new FakeHardwareEvaluator(),
            stockCenterMBaseline: new FakeBaseline(events),
            disabledBootAdmission: admission, captureCenterMStartup: () => Roots(FrontendCenterMStartupState.Disabled));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.ShouldStartRuntime);
        Assert.True(result.DisabledBootAdmission!.IsReady);
        Assert.False(result.StockTopologyUnreadyBeforeBaseline);
        Assert.Equal(FrontendCenterMStartupState.Disabled, result.CenterMStartupState);
        Assert.NotEqual(FrontendCenterMStartupState.Enabled, result.CenterMStartupState);
        Assert.Equal(["TopologyWaiter", "Admission"], events);
        Assert.DoesNotContain("Baseline", events);
        Assert.Equal(1, waiter.Calls);
    }

    [Fact]
    public async Task DisabledA2vmStartup_UsesFirstUsableControlHidInsteadOfStableSnapshots()
    {
        var events = new List<string>();
        var waiter = new FakeTopologyWaiter(events);
        var evaluator = new FixedHardwareAssessmentEvaluator(
            new(HardwareCompatibilityStatus.Supported, new("msi.claw"), new("msi.claw.a2vm.8"), "test"));
        var coordinator = new StartupCoordinator(
            waiter, new FakeProbeFactory(), evaluator,
            disabledBootAdmission: new FakeDisabledBootAdmission(events, DisabledBootAdmissionOutcome.Ready),
            captureCenterMStartup: () => Roots(FrontendCenterMStartupState.Disabled));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.DisabledBootAdmission!.IsReady);
        Assert.Equal(1, waiter.FastCalls);
        Assert.Equal(0, waiter.Calls);
        Assert.Equal(["FirstControlHidWaiter", "Admission"], events);
    }

    [Fact]
    public async Task DisabledRoots_TopologyNotStable_BlocksBeforeEvaluatingFacts()
    {
        var events = new List<string>();
        var admission = new FakeDisabledBootAdmission(events, DisabledBootAdmissionOutcome.Ready);
        var coordinator = new StartupCoordinator(
            new FixedTopologyWaiter(events, ControllerTopologyReadiness.Indeterminate),
            new FakeProbeFactory(), new FakeHardwareEvaluator(),
            stockCenterMBaseline: new FakeBaseline(events),
            disabledBootAdmission: admission, captureCenterMStartup: () => Roots(FrontendCenterMStartupState.Disabled));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(DisabledBootAdmissionOutcome.Blocked, result.DisabledBootAdmission!.Outcome);
        Assert.False(result.StockTopologyUnreadyBeforeBaseline);
        Assert.Equal(0, admission.EvaluateCount);
        Assert.True(result.ShouldStartRuntime); // mandatory Runtime stays alive
        Assert.NotEqual(FrontendCenterMStartupState.Enabled, result.CenterMStartupState);
        Assert.DoesNotContain("Baseline", events);
    }

    [Fact]
    public async Task DisabledRoots_AdmissionBlocked_KeepsRuntimeAliveWithNoMutation()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            stockCenterMBaseline: new FakeBaseline(events),
            disabledBootAdmission: new FakeDisabledBootAdmission(events, DisabledBootAdmissionOutcome.Blocked),
            captureCenterMStartup: () => Roots(FrontendCenterMStartupState.Disabled));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(DisabledBootAdmissionOutcome.Blocked, result.DisabledBootAdmission!.Outcome);
        Assert.True(result.ShouldStartRuntime);
        Assert.DoesNotContain("Baseline", events);
    }

    [Fact]
    public async Task DisabledRoots_PrerequisitesNotReady_KeepsRecoveryUnsafeAndRuntimeAvailable()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            stockCenterMBaseline: new FakeBaseline(events),
            disabledBootAdmission: new FakeDisabledBootAdmission(events, DisabledBootAdmissionOutcome.PrerequisitesNotReady),
            captureCenterMStartup: () => Roots(FrontendCenterMStartupState.Disabled));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.ShouldStartRuntime);
        Assert.False(result.RecoverySafe);
        Assert.Equal(DisabledBootAdmissionOutcome.PrerequisitesNotReady, result.DisabledBootAdmission!.Outcome);
        Assert.False(result.StockTopologyUnreadyBeforeBaseline);
        Assert.False(result.DisabledBootAdmission.IsReady);
        Assert.DoesNotContain("Baseline", events);
    }

    [Fact]
    public async Task Initial_prerequisite_block_logs_each_reason_and_available_usbip_version()
    {
        var events = new List<string>();
        var prerequisites = PrerequisitesWithUsbIp(PrerequisiteStatus.Indeterminate, "UsbIpWin2VersionUnavailable") with
        {
            UsbIpWin2 = new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Indeterminate, "UsbIpWin2VersionUnavailable", "0.9.8.0"),
        };
        var admission = new DisabledBootControllerAdmission(
            new SequencedPrerequisiteInspector(events, prerequisites),
            () => throw new Xunit.Sdk.XunitException("HidHide baseline must not run before USBIP readiness."));
        var coordinator = new StartupCoordinator(
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            disabledBootAdmission: admission,
            captureCenterMStartup: () => Roots(FrontendCenterMStartupState.Disabled));

        var result = await coordinator.RunAsync(CancellationToken.None);
        AppLog.DrainForTests();
        var logText = string.Join(Environment.NewLine,
            Directory.GetFiles(_logDirectory, "*.log", SearchOption.AllDirectories).Select(File.ReadAllText));

        Assert.Equal(DisabledBootAdmissionOutcome.PrerequisitesNotReady, result.DisabledBootAdmission?.Outcome);
        Assert.Contains("HidHideStatus", logText, StringComparison.Ordinal);
        Assert.Contains("HidHideReason", logText, StringComparison.Ordinal);
        Assert.Contains("UsbIpWin2Status", logText, StringComparison.Ordinal);
        Assert.Contains("UsbIpWin2Reason", logText, StringComparison.Ordinal);
        Assert.Contains("UsbIpWin2Version", logText, StringComparison.Ordinal);
        Assert.Contains("0.9.8.0", logText, StringComparison.Ordinal);
        Assert.Contains("ViiperStatus", logText, StringComparison.Ordinal);
        Assert.Contains("ViiperReason", logText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Indeterminate", "UsbIpWin2InspectionFailed")]
    [InlineData("Unusable", "UsbIpWin2DeviceUnavailable")]
    public async Task Deferred_disabled_admission_rechecks_transient_usbip_and_runs_full_hidhide_gate(string status, string reason)
    {
        var events = new List<string>();
        var inspector = new SequencedPrerequisiteInspector(events, ReadyPrerequisites());
        var admission = new DisabledBootControllerAdmission(inspector, () =>
        {
            events.Add("HidHideBaseline");
            return new(AddonHidHideBaselineOutcome.AlreadyCompliant, "verified", AddonHidHideBaselineSnapshot.Unknown);
        });
        var delay = new RecordingAdmissionRetryDelay();
        var coordinator = new StartupCoordinator(
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            disabledBootAdmission: admission,
            disabledAdmissionRetryWindow: TimeSpan.FromSeconds(10),
            disabledAdmissionRetryInterval: TimeSpan.FromSeconds(2),
            disabledAdmissionRetryMaxAttempts: 3,
            disabledAdmissionRetryDelay: delay.DelayAsync);
        var initial = DisabledBootControllerAdmissionResult.PrerequisitesNotReady(PrerequisitesWithUsbIp(
            Enum.Parse<PrerequisiteStatus>(status), reason));

        var result = await coordinator.RetryTransientDisabledBootAdmissionAsync(DisabledStartup(initial), CancellationToken.None);
        events.Add("PhysicalMutation");

        Assert.True(result.IsReady);
        Assert.Equal(1, inspector.Calls);
        Assert.Equal([TimeSpan.FromSeconds(2)], delay.Delays);
        Assert.True(events.IndexOf("HidHideBaseline") < events.IndexOf("PhysicalMutation"));
        Assert.Equal(1, events.Count(value => value == "TopologyWaiter"));
    }

    [Fact]
    public async Task Deferred_disabled_admission_stops_on_incompatible_prerequisite_without_another_retry()
    {
        var events = new List<string>();
        var incompatible = PrerequisitesWithUsbIp(PrerequisiteStatus.Incompatible, "UsbIpWin2VersionUnsupported");
        var inspector = new SequencedPrerequisiteInspector(events, incompatible);
        var admission = new DisabledBootControllerAdmission(inspector, () =>
        {
            events.Add("HidHideBaseline");
            return new(AddonHidHideBaselineOutcome.AlreadyCompliant, "verified", AddonHidHideBaselineSnapshot.Unknown);
        });
        var delay = new RecordingAdmissionRetryDelay();
        var coordinator = new StartupCoordinator(
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            disabledBootAdmission: admission,
            disabledAdmissionRetryWindow: TimeSpan.FromSeconds(10),
            disabledAdmissionRetryMaxAttempts: 4,
            disabledAdmissionRetryDelay: delay.DelayAsync);
        var initial = DisabledBootControllerAdmissionResult.PrerequisitesNotReady(PrerequisitesWithUsbIp(
            PrerequisiteStatus.Indeterminate, "UsbIpWin2InspectionFailed"));

        var result = await coordinator.RetryTransientDisabledBootAdmissionAsync(DisabledStartup(initial), CancellationToken.None);

        Assert.Equal(DisabledBootAdmissionOutcome.PrerequisitesNotReady, result.Outcome);
        Assert.Equal("UsbIpWin2VersionUnsupported", result.Prerequisites?.UsbIpWin2.Reason);
        Assert.Equal(1, inspector.Calls);
        Assert.Single(delay.Delays);
        Assert.DoesNotContain("HidHideBaseline", events);
    }

    [Fact]
    public async Task Deferred_disabled_admission_baseline_failure_returns_non_ready_and_blocks_takeover()
    {
        var events = new List<string>();
        var inspector = new SequencedPrerequisiteInspector(events, ReadyPrerequisites());
        var admission = new DisabledBootControllerAdmission(inspector, () =>
        {
            events.Add("HidHideBaseline");
            return new(AddonHidHideBaselineOutcome.VerificationFailed, "readback", AddonHidHideBaselineSnapshot.Unknown);
        });
        var coordinator = new StartupCoordinator(
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            disabledBootAdmission: admission, disabledAdmissionRetryInterval: TimeSpan.Zero,
            disabledAdmissionRetryMaxAttempts: 2, disabledAdmissionRetryDelay: (_, _) => Task.CompletedTask);
        var initial = DisabledBootControllerAdmissionResult.PrerequisitesNotReady(
            PrerequisitesWithUsbIp(PrerequisiteStatus.Indeterminate, "UsbIpWin2InspectionFailed"));

        var result = await coordinator.RetryTransientDisabledBootAdmissionAsync(DisabledStartup(initial), CancellationToken.None);

        Assert.Equal(DisabledBootAdmissionOutcome.Blocked, result.Outcome);
        Assert.Contains("HidHideBaseline=VerificationFailed:readback", result.Reason, StringComparison.Ordinal);
        Assert.Equal(["TopologyWaiter", "PrerequisiteInspection", "HidHideBaseline"], events);
    }

    [Fact]
    public async Task Deferred_disabled_admission_stops_after_bounded_attempts_when_usbip_never_recovers()
    {
        var events = new List<string>();
        var inspector = new SequencedPrerequisiteInspector(events,
            PrerequisitesWithUsbIp(PrerequisiteStatus.Indeterminate, "UsbIpWin2PackageInspectionFailed"));
        var admission = new DisabledBootControllerAdmission(inspector,
            () => throw new Xunit.Sdk.XunitException("HidHide must not normalize before all prerequisites are Ready."));
        var delay = new RecordingAdmissionRetryDelay();
        var coordinator = new StartupCoordinator(
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            disabledBootAdmission: admission,
            disabledAdmissionRetryWindow: TimeSpan.FromSeconds(10),
            disabledAdmissionRetryInterval: TimeSpan.Zero,
            disabledAdmissionRetryMaxAttempts: 3,
            disabledAdmissionRetryDelay: delay.DelayAsync);
        var initial = DisabledBootControllerAdmissionResult.PrerequisitesNotReady(
            PrerequisitesWithUsbIp(PrerequisiteStatus.Indeterminate, "UsbIpWin2PackageInspectionFailed"));

        var result = await coordinator.RetryTransientDisabledBootAdmissionAsync(DisabledStartup(initial), CancellationToken.None);

        Assert.Equal(DisabledBootAdmissionOutcome.Blocked, result.Outcome);
        Assert.Equal("DisabledBootAdmissionRetryBudgetExpired", result.Reason);
        Assert.Equal(3, inspector.Calls);
        Assert.Equal(3, events.Count(value => value == "TopologyWaiter"));
    }

    [Fact]
    public async Task Deferred_disabled_admission_waits_for_a_fresh_stable_topology_before_inspection()
    {
        var events = new List<string>();
        var inspector = new SequencedPrerequisiteInspector(events, ReadyPrerequisites());
        var admission = new DisabledBootControllerAdmission(inspector,
            () => new(AddonHidHideBaselineOutcome.AlreadyCompliant, "verified", AddonHidHideBaselineSnapshot.Unknown));
        var coordinator = new StartupCoordinator(
            new SequencedTopologyWaiter(events, ControllerTopologyReadiness.Indeterminate, ControllerTopologyReadiness.Stable),
            new FakeProbeFactory(), new FakeHardwareEvaluator(), disabledBootAdmission: admission,
            disabledAdmissionRetryWindow: TimeSpan.FromSeconds(10), disabledAdmissionRetryInterval: TimeSpan.Zero,
            disabledAdmissionRetryMaxAttempts: 2, disabledAdmissionRetryDelay: (_, _) => Task.CompletedTask);
        var initial = DisabledBootControllerAdmissionResult.PrerequisitesNotReady(
            PrerequisitesWithUsbIp(PrerequisiteStatus.Indeterminate, "UsbIpWin2InspectionFailed"));

        var result = await coordinator.RetryTransientDisabledBootAdmissionAsync(DisabledStartup(initial), CancellationToken.None);

        Assert.True(result.IsReady);
        Assert.Equal(1, inspector.Calls);
        Assert.Equal(["TopologyWaiter", "TopologyWaiter", "PrerequisiteInspection"], events);
    }

    [Fact]
    public async Task Deferred_disabled_admission_does_not_retry_an_initial_incompatible_result()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            disabledAdmissionRetryDelay: (_, _) => throw new Xunit.Sdk.XunitException("Incompatible USBIP must not be retried."));
        var incompatible = DisabledBootControllerAdmissionResult.PrerequisitesNotReady(
            PrerequisitesWithUsbIp(PrerequisiteStatus.Incompatible, "UsbIpWin2VersionUnsupported"));

        var result = await coordinator.RetryTransientDisabledBootAdmissionAsync(DisabledStartup(incompatible), CancellationToken.None);

        Assert.Same(incompatible, result);
        Assert.Empty(events);
    }

    [Fact]
    public async Task Deferred_disabled_admission_requires_supported_msi_hardware_before_retrying()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            disabledAdmissionRetryDelay: (_, _) => throw new Xunit.Sdk.XunitException("Unsupported hardware must not be retried."));
        var initial = DisabledBootControllerAdmissionResult.PrerequisitesNotReady(
            PrerequisitesWithUsbIp(PrerequisiteStatus.Indeterminate, "UsbIpWin2InspectionFailed"));

        var result = await coordinator.RetryTransientDisabledBootAdmissionAsync(
            DisabledStartup(initial) with { HardwareDeviceFamily = new("other") }, CancellationToken.None);

        Assert.Same(initial, result);
        Assert.Empty(events);
    }

    private static StartupResult DisabledStartup(DisabledBootControllerAdmissionResult admission) => new(
        ShouldStartRuntime: true,
        HardwareSupported: true,
        HardwareDeviceFamily: new("msi.claw"),
        HardwareDeviceModel: new("msi.claw.cg3em"),
        CenterMStartupState: FrontendCenterMStartupState.Disabled,
        DisabledBootAdmission: admission);

    private static RuntimePrerequisiteAssessment ReadyPrerequisites() => new(
        new(PrerequisiteKind.HidHide, PrerequisiteStatus.Ready, "HidHideReady"),
        new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Ready, "UsbIpWin2DeviceReady", "0.9.8.1"),
        new(PrerequisiteKind.Viiper, PrerequisiteStatus.Ready, "ViiperReady"));

    private static RuntimePrerequisiteAssessment PrerequisitesWithUsbIp(PrerequisiteStatus status, string reason) => new(
        new(PrerequisiteKind.HidHide, PrerequisiteStatus.Ready, "HidHideReady"),
        new(PrerequisiteKind.UsbIpWin2, status, reason, status == PrerequisiteStatus.Incompatible ? "0.9.8.0" : null),
        new(PrerequisiteKind.Viiper, PrerequisiteStatus.Ready, "ViiperReady"));

    [Theory]
    [InlineData("Partial")]
    [InlineData("Unavailable")]
    public async Task PartialOrUnavailableRoots_SelectNoControllerOwner(string state)
    {
        var events = new List<string>();
        var admission = new FakeDisabledBootAdmission(events, DisabledBootAdmissionOutcome.Ready);
        var coordinator = new StartupCoordinator(
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            stockCenterMBaseline: new FakeBaseline(events),
            disabledBootAdmission: admission, captureCenterMStartup: () => Roots(Enum.Parse<FrontendCenterMStartupState>(state)));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.ShouldStartRuntime);
        Assert.Null(result.DisabledBootAdmission);
        Assert.Equal(Enum.Parse<FrontendCenterMStartupState>(state), result.CenterMStartupState);
        Assert.False(result.StockTopologyUnreadyBeforeBaseline);
        Assert.NotEqual(FrontendCenterMStartupState.Enabled, result.CenterMStartupState);
        Assert.Equal(0, admission.EvaluateCount);
        Assert.DoesNotContain("Baseline", events);
    }

    [Fact]
    public async Task EnabledRoots_RunExistingStockPath_AdmissionNeverEvaluated()
    {
        var events = new List<string>();
        var admission = new FakeDisabledBootAdmission(events, DisabledBootAdmissionOutcome.Ready);
        var coordinator = new StartupCoordinator(
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            stockCenterMBaseline: new FakeBaseline(events),
            disabledBootAdmission: admission, captureCenterMStartup: () => Roots(FrontendCenterMStartupState.Enabled));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Contains("Baseline", events);
        Assert.Equal(0, admission.EvaluateCount);
        Assert.Equal(FrontendCenterMStartupState.Enabled, result.CenterMStartupState);
        Assert.Equal(FrontendCenterMStartupState.Enabled, result.CenterMStartupState);
        Assert.True(result.RecoverySafe);
        Assert.False(result.StockTopologyUnreadyBeforeBaseline);
    }

    [Fact]
    public async Task NoCaptureDelegate_PreservesLegacyStockPath()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            stockCenterMBaseline: new FakeBaseline(events));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Contains("Baseline", events);
        Assert.Equal(FrontendCenterMStartupState.Enabled, result.CenterMStartupState);
    }

    [Fact]
    public void DisabledBootAdmission_TakesNoPhysicalOwnershipDependency()
    {
        var parameterTypes = typeof(DisabledBootControllerAdmission)
            .GetConstructors().Single().GetParameters().Select(p => p.ParameterType.FullName ?? "");
        Assert.All(parameterTypes, name =>
        {
            Assert.DoesNotContain("DirectInput", name);
            Assert.DoesNotContain("Viiper", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("NativeState", name);
            Assert.DoesNotContain("Routing", name);
        });

        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src/SteamInputAddonforClaw/Startup/DisabledBootControllerAdmission.cs"));
        foreach (var forbidden in new[] { "SwitchModeAsync", "MsiClawInputSource", "DirectInput", "ApplyDisabledModeBaseline", "AddHiddenDevice", "AttachXbox360", "AttachSteamDeck", "CanonicalViiperRuntime" })
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeComposition_NeverComposesLegacyRouting_ButKeepsTheStockResumeBaselineGated()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src/SteamInputAddonforClaw/Runtime/AddonRuntimeComposition.cs"));
        // Full1902 Cleanup A: the legacy Steam-session routing authority graph is deleted -- the
        // runtime composition no longer references it at all.
        Assert.DoesNotContain("AddonRoutingRuntime", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartRoutingObservation(", source, StringComparison.Ordinal);
        // Section 11: the stock PID1901 resume baseline is still gated on the Center M Enabled
        // authority state -- independently, not via the removed legacy routing selection.
        Assert.Contains("!stockCenterMAuthority || stockCenterMBaseline is null", source, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }

    private sealed class FakeDisabledBootAdmission(List<string> events, DisabledBootAdmissionOutcome outcome) : IDisabledBootControllerAdmission
    {
        public int EvaluateCount { get; private set; }
        public DisabledBootControllerAdmissionResult Evaluate()
        {
            EvaluateCount++;
            events.Add("Admission");
            return outcome switch
            {
                DisabledBootAdmissionOutcome.Ready => DisabledBootControllerAdmissionResult.Ready,
                DisabledBootAdmissionOutcome.NotApplicable => DisabledBootControllerAdmissionResult.NotApplicable,
                DisabledBootAdmissionOutcome.PrerequisitesNotReady => new(DisabledBootAdmissionOutcome.PrerequisitesNotReady, "test"),
                _ => DisabledBootControllerAdmissionResult.Blocked("test"),
            };
        }
    }

    private sealed class SequencedPrerequisiteInspector(
        List<string> events,
        RuntimePrerequisiteAssessment assessment) : IRuntimePrerequisiteInspector
    {
        public int Calls { get; private set; }
        public RuntimePrerequisiteAssessment Inspect()
        {
            Calls++;
            events.Add("PrerequisiteInspection");
            return assessment;
        }
    }

    private sealed class SequencedTopologyWaiter : IControllerTopologyWaiter
    {
        private readonly List<string> _events;
        private readonly Queue<ControllerTopologyReadiness> _results;
        public SequencedTopologyWaiter(List<string> events, params ControllerTopologyReadiness[] results)
        {
            _events = events;
            _results = new(results);
        }
        public Task<ControllerTopologyReadiness> WaitUntilStableAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _events.Add("TopologyWaiter");
            return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : ControllerTopologyReadiness.Indeterminate);
        }
        public Task<ControllerTopologyReadiness> WaitForFirstUnambiguousControlHidAsync(CancellationToken cancellationToken) =>
            WaitUntilStableAsync(cancellationToken);
    }

    private sealed class RecordingAdmissionRetryDelay
    {
        public List<TimeSpan> Delays { get; } = [];
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class SequencedHardwareEvaluator(IEnumerable<HardwareCompatibilityAssessment> results, bool repeatLast = false) : IHardwareCompatibilityEvaluator
    {
        private readonly Queue<HardwareCompatibilityAssessment> _results = new(results);
        private HardwareCompatibilityAssessment? _last;
        public int CallCount { get; private set; }

        public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _)
        {
            CallCount++;
            if (_results.Count > 0) { _last = _results.Dequeue(); return _last; }
            if (repeatLast && _last is not null) return _last;
            throw new InvalidOperationException("SequencedHardwareEvaluator exhausted.");
        }
    }

    private sealed class RecordingHardwareProbeDelay
    {
        public List<TimeSpan> Delays { get; } = [];
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProbeFactory : IWindowsDeviceProbeContextFactory { public DeviceProbeContextCapture Capture() => new(DeviceProbeCaptureStatus.Success, new DeviceProbeContext(), "test"); }
    private static DeviceProbeContextCapture CaptureWithIdentity(WindowsDeviceIdentity identity) =>
        new(DeviceProbeCaptureStatus.Success, new DeviceProbeContext(), "test") { Identity = identity };
    private sealed class SequencedProbeFactory(IEnumerable<DeviceProbeContextCapture> captures) : IWindowsDeviceProbeContextFactory
    {
        private readonly Queue<DeviceProbeContextCapture> _captures = new(captures);
        public int CallCount { get; private set; }
        public DeviceProbeContextCapture Capture()
        {
            CallCount++;
            return _captures.Dequeue();
        }
    }
    private sealed class FakeHardwareEvaluator : IHardwareCompatibilityEvaluator { public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _) => new(HardwareCompatibilityStatus.Supported, new("msi.claw"), new("msi.claw.cg3em"), "test"); }
    private sealed class FixedHardwareAssessmentEvaluator(HardwareCompatibilityAssessment assessment) : IHardwareCompatibilityEvaluator
    {
        public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _) => assessment;
    }
    private sealed class FixedHardwareEvaluator(HardwareCompatibilityStatus status) : IHardwareCompatibilityEvaluator
    {
        public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _) => new(status, null, null, "test");
    }
    private sealed class ThrowingTopologyWaiter : IControllerTopologyWaiter { public Task<ControllerTopologyReadiness> WaitUntilStableAsync(CancellationToken _) => throw new Xunit.Sdk.XunitException("Topology wait must not run after recovery failure."); }
    private sealed class ThrowingProbeFactory : IWindowsDeviceProbeContextFactory { public DeviceProbeContextCapture Capture() => throw new Xunit.Sdk.XunitException("Hardware probe must not run after recovery failure."); }
    private sealed class ThrowingHardwareEvaluator : IHardwareCompatibilityEvaluator { public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _) => throw new Xunit.Sdk.XunitException("Hardware evaluator must not run after recovery failure."); }
}
