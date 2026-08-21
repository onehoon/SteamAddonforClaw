using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Feedback;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// Covers the Vibration Test dedicated diagnostic session's page-entry lifecycle: PR #269 review
/// required that opening the detail page creates the session file immediately -- even if the user
/// never presses a command button -- rather than lazily on the first command.
/// </summary>
[Collection("AppLog")]
public sealed class VibrationTestSessionLifecycleTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task Opening_the_session_creates_exactly_one_file_before_any_command_is_run()
    {
        var control = CreateControl();

        var result = await control.OpenVibrationTestSessionAsync();

        Assert.True(result.Succeeded);
        Assert.NotNull(result.LogFilePath);
        Assert.True(File.Exists(result.LogFilePath));
        var vibrationTestDirectory = Path.Combine(_testDirectory, "VibrationTest");
        Assert.Single(Directory.GetFiles(vibrationTestDirectory));

        await control.CloseVibrationTestSessionAsync();
    }

    [Fact]
    public async Task Opening_the_session_twice_reuses_the_same_file_instead_of_starting_a_second_one()
    {
        var control = CreateControl();

        var first = await control.OpenVibrationTestSessionAsync();
        var second = await control.OpenVibrationTestSessionAsync();

        Assert.Equal(first.LogFilePath, second.LogFilePath);
        var vibrationTestDirectory = Path.Combine(_testDirectory, "VibrationTest");
        Assert.Single(Directory.GetFiles(vibrationTestDirectory));

        await control.CloseVibrationTestSessionAsync();
    }

    [Fact]
    public async Task Closing_an_open_session_flushes_and_reports_the_same_file_path()
    {
        var control = CreateControl();
        var opened = await control.OpenVibrationTestSessionAsync();

        var closed = await control.CloseVibrationTestSessionAsync();

        Assert.True(closed.Succeeded);
        Assert.Equal(opened.LogFilePath, closed.LogFilePath);
        var contents = File.ReadAllText(closed.LogFilePath!);
        Assert.Contains("SessionStarted", contents);
        Assert.Contains("SessionClosed", contents);
    }

    [Fact]
    public async Task A_write_after_the_session_closes_is_a_silent_no_op_not_an_exception_on_the_disposed_writer()
    {
        // Regression for PR #269 review: RunVibrationTestAsync captures the current session, writes
        // a pre-command line, awaits the runtime test (up to the developer 250ms window), then writes
        // a result line. If CloseVibrationTestSessionAsync() detaches+disposes that same session while
        // the await is still in flight, the resumed post-await write used to hit a disposed
        // StreamWriter. WriteVibrationSessionIfCurrent must instead observe that the session is no
        // longer current and silently skip the write.
        var control = CreateControl();
        var opened = await control.OpenVibrationTestSessionAsync();
        var staleSession = control.TestOnly_CurrentVibrationSession!;

        var closed = await control.CloseVibrationTestSessionAsync();
        Assert.True(closed.Succeeded);

        var exception = Record.Exception(() => control.WriteVibrationSessionIfCurrent(staleSession, "post-close result"));

        Assert.Null(exception);
        var contents = File.ReadAllText(opened.LogFilePath!);
        Assert.DoesNotContain("post-close result", contents);
    }

    [Fact]
    public void MapVibrationTestOutcome_reports_full_success_when_accepted_and_physically_confirmed()
    {
        var outcome = new DeveloperVibrationTestOutcome(true, new PhysicalRumbleWriteResult(PhysicalRumbleWriteStatus.Succeeded, "OK"), null);

        var (succeeded, reason) = InProcessAddonFrontendControl.MapVibrationTestOutcome(outcome);

        Assert.True(succeeded);
        Assert.Equal("Succeeded", reason);
    }

    [Fact]
    public void MapVibrationTestOutcome_reports_rejection_when_not_accepted()
    {
        var outcome = new DeveloperVibrationTestOutcome(false, null, null);

        var (succeeded, reason) = InProcessAddonFrontendControl.MapVibrationTestOutcome(outcome);

        Assert.False(succeeded);
        Assert.Equal("Feedback bridge is unavailable, superseded, or the test was cancelled.", reason);
    }

    [Fact]
    public void MapVibrationTestOutcome_reports_a_physical_command_write_failure_as_not_successful()
    {
        // Regression for PR #269 review: accepted (authority/sequence) is not the same question as
        // "did the real MSI HID write succeed" -- a FailingSink result must never be reported to the
        // Vibration Test page as Succeeded.
        var outcome = new DeveloperVibrationTestOutcome(true, new PhysicalRumbleWriteResult(PhysicalRumbleWriteStatus.Failed, "WriteFailed"), null);

        var (succeeded, reason) = InProcessAddonFrontendControl.MapVibrationTestOutcome(outcome);

        Assert.False(succeeded);
        Assert.Contains("WriteFailed", reason);
    }

    [Fact]
    public void MapVibrationTestOutcome_reports_a_physical_STOP_write_failure_as_not_successful()
    {
        var outcome = new DeveloperVibrationTestOutcome(
            true,
            new PhysicalRumbleWriteResult(PhysicalRumbleWriteStatus.Succeeded, "OK"),
            new PhysicalRumbleWriteResult(PhysicalRumbleWriteStatus.Unavailable, "NoVerifiedEndpoint"));

        var (succeeded, reason) = InProcessAddonFrontendControl.MapVibrationTestOutcome(outcome);

        Assert.False(succeeded);
        Assert.Contains("NoVerifiedEndpoint", reason);
    }

    private InProcessAddonFrontendControl CreateControl()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _testDirectory;
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), store, new FakeStartupManager());
        return new InProcessAddonFrontendControl(
            coordinator,
            new ThrowingSystemStatusProvider(),
            null,
            new DeveloperTestModeState(),
            "",
            captureRoutingStatus: () => new(true, RoutingOperationalState.Passive, true, false));
    }

    public void Dispose()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = null;
        if (Directory.Exists(_testDirectory)) Directory.Delete(_testDirectory, recursive: true);
    }

    private sealed class FakeStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }

    private sealed class ThrowingSystemStatusProvider : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Status capture is not part of these tests.");
    }
}
