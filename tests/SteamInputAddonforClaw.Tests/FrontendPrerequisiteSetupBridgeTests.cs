using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class FrontendPrerequisiteSetupBridgeTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory)) Directory.Delete(_testDirectory, recursive: true);
    }

    [Fact]
    public async Task Fresh_non_installable_revalidation_does_not_launch_and_returns_fresh_status()
    {
        var fresh = Snapshot("fresh");
        var executor = new FakeExecutor(new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.SteamActive, false));
        var control = CreateControl([fresh], executor);

        var result = await control.RunPrerequisiteSetupAsync();

        Assert.Equal(FrontendPrerequisiteSetupResultKind.NotInstallable, result.Result);
        Assert.Equal("fresh", result.Status!.Device.Model);
        Assert.Equal(1, executor.EvaluateCallCount);
        Assert.Equal(0, executor.RunCallCount);
    }

    [Theory]
    [InlineData(3, FrontendPrerequisiteSetupResultKind.Blocked)]
    [InlineData(3010, FrontendPrerequisiteSetupResultKind.RebootRequired)]
    public async Task Launched_helper_result_is_translated(int exitCode, FrontendPrerequisiteSetupResultKind expected)
    {
        var executor = new FakeExecutor(new(FirstTimeSetupStatus.Required, FirstTimeSetupReason.MissingComponents, true))
        {
            Result = new(ElevatedProcessResultKind.Completed, exitCode)
        };
        var control = CreateControl([Snapshot("pre"), Snapshot("post")], executor);

        var result = await control.RunPrerequisiteSetupAsync();

        Assert.Equal(expected, result.Result);
        Assert.Equal(1, executor.RunCallCount);
        Assert.Equal("test-runtime.exe", executor.ExecutablePath);
        Assert.Equal("post", result.Status!.Device.Model);
    }

    [Fact]
    public async Task Post_helper_status_is_not_the_stale_pre_execution_status()
    {
        var executor = new FakeExecutor(new(FirstTimeSetupStatus.Required, FirstTimeSetupReason.MissingComponents, true))
        {
            Result = new(ElevatedProcessResultKind.Completed, 0)
        };
        var control = CreateControl([Snapshot("pre"), Snapshot("post")], executor);

        var result = await control.RunPrerequisiteSetupAsync();

        Assert.Equal("post", result.Status!.Device.Model);
    }

    [Theory]
    [InlineData(3010, FrontendPrerequisiteSetupResultKind.RebootRequired)]
    [InlineData(3, FrontendPrerequisiteSetupResultKind.Blocked)]
    public async Task Helper_outcome_survives_post_status_refresh_failure(int exitCode, FrontendPrerequisiteSetupResultKind expected)
    {
        var executor = new FakeExecutor(new(FirstTimeSetupStatus.Required, FirstTimeSetupReason.MissingComponents, true))
        {
            Result = new(ElevatedProcessResultKind.Completed, exitCode)
        };
        var control = CreateControl(new ThrowingStatusProvider(Snapshot("pre")), executor);

        var result = await control.RunPrerequisiteSetupAsync();

        Assert.Equal(expected, result.Result);
        Assert.Null(result.Status);
        Assert.Equal(1, executor.RunCallCount);
    }

    [Fact]
    public async Task Process_shutdown_barrier_rejects_new_frontend_mutations()
    {
        var control = CreateControl([Snapshot("initial")], new FakeExecutor(new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.SteamActive, false)));

        control.BeginProcessShutdown();

        var exception = await Assert.ThrowsAsync<FrontendProtocolException>(() => control.SetLogLevelAsync(FrontendLogLevel.Debug));
        Assert.Contains("shutting down", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static InProcessAddonFrontendControl CreateControl(IReadOnlyList<SystemStatusSnapshot> snapshots, FakeExecutor executor)
    {
        return CreateControl(new QueueStatusProvider(snapshots), executor);
    }

    private static InProcessAddonFrontendControl CreateControl(ISystemStatusProvider status, FakeExecutor executor) =>
        new(null!, status, null, null!, "", executor, () => "test-runtime.exe");

    private sealed class ThrowingStatusProvider(SystemStatusSnapshot initial) : ISystemStatusProvider
    {
        private int _captureCount;
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
            Interlocked.Increment(ref _captureCount) == 1
                ? Task.FromResult(initial)
                : throw new InvalidOperationException("post-status probe failed");
    }

    private static SystemStatusSnapshot Snapshot(string model) => new(
        new("MSI", model, "BOARD", []),
        new(HardwareCompatibilityStatus.Supported, null, null, "Test"),
        [],
        new(ControllerEnvironmentCompatibilityStatus.Supported, ControllerEnvironmentCompatibilityReason.StockCenterMOnlySupported),
        new(new(PrerequisiteKind.HidHide, PrerequisiteStatus.Ready, "Test"), new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Ready, "Test"), new(PrerequisiteKind.Viiper, PrerequisiteStatus.Ready, "Test")),
        new(false, 0),
        new(AddonOperationalStatus.Ready, "Test"), true);

    private sealed class QueueStatusProvider(IEnumerable<SystemStatusSnapshot> snapshots) : ISystemStatusProvider
    {
        private readonly Queue<SystemStatusSnapshot> _snapshots = new(snapshots);
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult(_snapshots.Count > 1 ? _snapshots.Dequeue() : _snapshots.Peek());
    }

    private sealed class NoOpStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }

    private sealed class FakeExecutor(FirstTimeSetupAssessment assessment) : IFrontendPrerequisiteSetupExecutor
    {
        public ElevatedProcessResult? Result { get; init; }
        public int EvaluateCallCount { get; private set; }
        public int RunCallCount { get; private set; }
        public string? ExecutablePath { get; private set; }
        public FirstTimeSetupAssessment? SuppliedAssessment { get; private set; }

        public FirstTimeSetupAssessment Evaluate(SystemStatusSnapshot snapshot)
        {
            EvaluateCallCount++;
            return assessment;
        }

        public Task<ElevatedProcessResult?> RunAsync(FirstTimeSetupAssessment suppliedAssessment, string executablePath, CancellationToken cancellationToken)
        {
            RunCallCount++;
            SuppliedAssessment = suppliedAssessment;
            ExecutablePath = executablePath;
            return Task.FromResult(Result);
        }
    }
}
