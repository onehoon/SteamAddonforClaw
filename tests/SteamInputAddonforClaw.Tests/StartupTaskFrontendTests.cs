using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Processes;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Diagnostics;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class StartupTaskFrontendTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task Status_uses_the_cached_startup_result_without_reading_task_scheduler()
    {
        var manager = new FakeStartupManager();
        var (control, settings) = CreateControl(manager);

        var beforeEnsure = await control.CaptureStatusAsync();
        Assert.Equal(FrontendStartupTaskSnapshot.Unknown, beforeEnsure.StartupTask);
        Assert.Equal(0, manager.SynchronizeCount);

        settings.EnsureStartupRegistration();
        var afterEnsure = await control.CaptureStatusAsync();

        Assert.Equal(new(FrontendStartupTaskState.Verified, FrontendStartupTaskIssue.None), afterEnsure.StartupTask);
        Assert.Equal(1, manager.SynchronizeCount);
    }

    [Theory]
    [InlineData(StartupTaskFailureReason.ReadFailed, FrontendStartupTaskIssue.ReadFailed)]
    [InlineData(StartupTaskFailureReason.StableExecutableMissing, FrontendStartupTaskIssue.StableExecutableMissing)]
    [InlineData(StartupTaskFailureReason.RegistrationFailed, FrontendStartupTaskIssue.RegistrationFailed)]
    [InlineData(StartupTaskFailureReason.ReadbackUnverified, FrontendStartupTaskIssue.ReadbackUnverified)]
    public async Task Status_preserves_the_last_ensure_failure_reason(
        StartupTaskFailureReason failureReason,
        FrontendStartupTaskIssue expectedIssue)
    {
        var manager = new FakeStartupManager { Result = StartupRegistrationResult.Failed(failureReason) };
        var (control, settings) = CreateControl(manager);
        settings.EnsureStartupRegistration();

        var snapshot = await control.CaptureStatusAsync();

        Assert.Equal(new(FrontendStartupTaskState.NeedsAttention, expectedIssue), snapshot.StartupTask);
        Assert.Equal(1, manager.SynchronizeCount);
    }

    [Fact]
    public async Task Repair_runs_one_ensure_and_returns_the_latest_result()
    {
        var manager = new FakeStartupManager
        {
            Result = StartupRegistrationResult.Failed(StartupTaskFailureReason.ReadFailed)
        };
        var (control, _) = CreateControl(manager);
        var invalidations = 0;
        control.StateInvalidated += (_, _) => invalidations++;

        var failed = await control.RepairStartupTaskAsync();
        Assert.Equal(new(FrontendStartupTaskState.NeedsAttention, FrontendStartupTaskIssue.ReadFailed), failed);

        manager.Result = StartupRegistrationResult.Enabled();
        var repaired = await control.RepairStartupTaskAsync();

        Assert.Equal(new(FrontendStartupTaskState.Verified, FrontendStartupTaskIssue.None), repaired);
        Assert.Equal(2, manager.SynchronizeCount);
        Assert.Equal([true, true], manager.EnabledArguments);
        Assert.Equal(2, invalidations);
        Assert.Equal(repaired, (await control.CaptureStatusAsync()).StartupTask);
    }

    [Fact]
    public async Task Repair_after_runtime_shutdown_does_not_touch_task_scheduler()
    {
        var manager = new FakeStartupManager();
        var (control, _) = CreateControl(manager);
        control.BeginProcessShutdown();

        await Assert.ThrowsAsync<FrontendProtocolException>(() => control.RepairStartupTaskAsync());

        Assert.Equal(0, manager.SynchronizeCount);
    }

    private (InProcessAddonFrontendControl Control, StartupSettingsCoordinator Settings) CreateControl(FakeStartupManager manager)
    {
        Directory.CreateDirectory(_directory);
        AppLog.DirectoryOverride = _directory;
        var settings = new StartupSettingsCoordinator(
            new AppSettings(), new SettingsStore(Path.Combine(_directory, "settings.json")), manager);
        var control = new InProcessAddonFrontendControl(
            settings,
            new FixedSystemStatusProvider(Status),
            null,
            new FixedSetupExecutor());
        return (control, settings);
    }

    private static readonly SystemStatusSnapshot Status = new(
        new DeviceStatusSnapshot("MSI", "Claw", "BOARD", []),
        new HardwareCompatibilityAssessment(HardwareCompatibilityStatus.Supported,
            new HandheldDeviceId("msi.claw"), null, "Supported"),
        new RuntimePrerequisiteAssessment(
            new(PrerequisiteKind.HidHide, PrerequisiteStatus.Ready, "Ready"),
            new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Ready, "Ready"),
            new(PrerequisiteKind.Viiper, PrerequisiteStatus.Ready, "Ready")),
        new SteamStatusSnapshot(false, 0, SteamSessionSource.Actual),
        new AddonStatusSnapshot(AddonOperationalStatus.Ready, "Ready"),
        RecoverySafe: true);

    public void Dispose()
    {
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class FakeStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult? Result { get; set; }
        public int SynchronizeCount { get; private set; }
        public List<bool> EnabledArguments { get; } = [];

        public StartupRegistrationResult Synchronize(bool enabled)
        {
            SynchronizeCount++;
            EnabledArguments.Add(enabled);
            return Result ?? StartupRegistrationResult.Enabled();
        }
    }

    private sealed class FixedSystemStatusProvider(SystemStatusSnapshot snapshot) : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }

    private sealed class FixedSetupExecutor : IFrontendPrerequisiteSetupExecutor
    {
        public FirstTimeSetupAssessment Evaluate(SystemStatusSnapshot snapshot) =>
            new(FirstTimeSetupStatus.Complete, FirstTimeSetupReason.Complete, false);

        public Task<ChildProcessResult?> RunAsync(
            FirstTimeSetupAssessment assessment,
            string executablePath,
            CancellationToken cancellationToken) => Task.FromResult<ChildProcessResult?>(null);
    }
}
