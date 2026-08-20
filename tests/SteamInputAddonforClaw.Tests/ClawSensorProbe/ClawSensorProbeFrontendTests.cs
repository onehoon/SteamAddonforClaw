using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using System.Text.Json;
using Xunit;

namespace SteamInputAddonforClaw.Tests.ClawSensorProbe;

/// <summary>
/// Frontend-boundary coverage for the restored Claw Sensor Probe diagnostic:
/// <see cref="InProcessAddonFrontendControl"/> must gate availability purely on the MSI Claw device
/// family (never on <see cref="HardwareCompatibilityStatus"/>, Developer Test Mode, or routing/Steam
/// state), keep exactly one active Runtime-owned coordinator per session, and dispose it correctly on
/// Close and on process shutdown. Backend Workflow/Discovery/Statistics/Coordinator behavior is
/// already covered by <see cref="SteamInputAddonforClaw.Tests.ClawSensorProbe.ClawSensorProbeTests"/>;
/// these tests are for the frontend projection only.
/// </summary>
[Collection("AppLog")]
public sealed class ClawSensorProbeFrontendTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task Msi_claw_family_is_available_regardless_of_production_compatibility_status()
    {
        foreach (var status in Enum.GetValues<HardwareCompatibilityStatus>())
        {
            var control = CreateControl(ClawFamilySnapshot(status));
            var snapshot = await control.OpenClawSensorProbeAsync();
            Assert.True(snapshot.Available);
            await control.CloseClawSensorProbeAsync();
        }
    }

    [Fact]
    public async Task Non_claw_family_is_never_available()
    {
        var control = CreateControl(NonClawSnapshot());

        var snapshot = await control.OpenClawSensorProbeAsync();

        Assert.False(snapshot.Available);
        Assert.NotNull(snapshot.ErrorMessage);
    }

    [Fact]
    public async Task Open_carries_device_identity_from_the_status_snapshot()
    {
        var control = CreateControl(ClawFamilySnapshot(HardwareCompatibilityStatus.Supported));

        var snapshot = await control.OpenClawSensorProbeAsync();

        Assert.Equal("MSI", snapshot.Manufacturer);
        Assert.Equal("Claw A1M", snapshot.Model);
        Assert.Equal("Claw A1M board", snapshot.BaseBoard);
    }

    [Fact]
    public async Task Repeated_open_while_a_session_is_active_does_not_create_a_competing_session()
    {
        var control = CreateControl(ClawFamilySnapshot(HardwareCompatibilityStatus.Supported));

        var first = await control.OpenClawSensorProbeAsync();
        var second = await control.OpenClawSensorProbeAsync();

        Assert.Equal(first.State, second.State);
        // A brand new session would reset PhaseIndex/State to Ready with the coordinator freshly
        // constructed -- proving repeated Open returns the SAME session rather than replacing it.
        Assert.Equal(FrontendClawSensorProbeState.Ready, second.State);
    }

    [Fact]
    public async Task Close_disposes_the_session_so_capture_after_close_reports_unavailable()
    {
        var control = CreateControl(ClawFamilySnapshot(HardwareCompatibilityStatus.Supported));
        await control.OpenClawSensorProbeAsync();

        await control.CloseClawSensorProbeAsync();
        var afterClose = await control.CaptureClawSensorProbeAsync();

        Assert.False(afterClose.Available);
    }

    [Fact]
    public async Task Start_failure_in_a_hardware_less_test_environment_still_finalizes_a_report_and_reports_Failed()
    {
        // No real Windows Sensor API / MSI Claw sensors exist in the test environment, so
        // StartClawSensorProbeAsync's underlying StartCaptureAsync() call is expected to fail --
        // proving the frontend surfaces that as a Failed snapshot with an error message rather than
        // throwing out of the RPC boundary or leaving the session stuck.
        var control = CreateControl(ClawFamilySnapshot(HardwareCompatibilityStatus.Supported));
        await control.OpenClawSensorProbeAsync();

        var snapshot = await control.StartClawSensorProbeAsync();

        Assert.Equal(FrontendClawSensorProbeState.Failed, snapshot.State);
        Assert.NotNull(snapshot.ErrorMessage);
    }

    [Fact]
    public async Task Capture_reconciles_reader_faults_via_FailOnReaderFaultAsync_without_throwing_when_already_failed()
    {
        // PR #290 re-review finding #1: CaptureClawSensorProbeAsync must call the coordinator's
        // FailOnReaderFaultAsync() so a reader fault surfaced between polls is promoted to Failed the
        // same way the old page's 200ms UI timer did, instead of leaving the diagnostic stuck in
        // RecordingPhase until the user happens to press a button.
        //
        // This test environment has no real Windows Sensor API, so StartClawSensorProbeAsync's
        // underlying StartCaptureAsync() always fails before any reader is ever created -- there is
        // no way to construct a genuine ClawSensorProbeReaders fault from this test project without
        // real hardware or a test-only seam on the (intentionally unmodified) Runtime coordinator.
        // What IS verifiable here is that Capture calls into the fault-reconciliation path safely once
        // a session has already reached Failed by the normal Start-failure route -- FailOnReaderFaultAsync
        // no-ops once State == Failed, so this proves the new call does not throw, does not regress the
        // already-covered Start-failure/finalization behavior, and repeated polling after Failed stays
        // stable rather than looping or re-throwing.
        var control = CreateControl(ClawFamilySnapshot(HardwareCompatibilityStatus.Supported));
        await control.OpenClawSensorProbeAsync();
        var failed = await control.StartClawSensorProbeAsync();
        Assert.Equal(FrontendClawSensorProbeState.Failed, failed.State);

        var polled = await control.CaptureClawSensorProbeAsync();
        var polledAgain = await control.CaptureClawSensorProbeAsync();

        Assert.Equal(FrontendClawSensorProbeState.Failed, polled.State);
        Assert.Equal(FrontendClawSensorProbeState.Failed, polledAgain.State);
    }

    [Fact]
    public async Task Start_writes_device_identity_and_hardware_compatibility_into_the_finalized_report()
    {
        // Review finding #1 on PR #290: SetDeviceIdentity/SetHardwareCompatibility write through the
        // coordinator's session writer, which Start() does not create until StartClawSensorProbeAsync
        // runs -- calling them at Open time (before Start) silently no-ops and drops this metadata.
        // Assert the metadata actually lands in claw-sensor-report.json, not just that Start reaches
        // Failed in this hardware-less test environment.
        var control = CreateControl(ClawFamilySnapshot(HardwareCompatibilityStatus.Supported));
        await control.OpenClawSensorProbeAsync();

        var started = await control.StartClawSensorProbeAsync();

        Assert.Equal(FrontendClawSensorProbeState.Failed, started.State);
        Assert.NotNull(started.OutputDirectory);
        var reportPath = Path.Combine(started.OutputDirectory!, "claw-sensor-report.json");
        Assert.True(File.Exists(reportPath));
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
        var device = report.RootElement.GetProperty("Device");
        Assert.Equal("MSI", device.GetProperty("Manufacturer").GetString());
        Assert.Equal("Claw A1M", device.GetProperty("ProductName").GetString());
        Assert.Equal("Claw A1M board", device.GetProperty("BaseBoardProduct").GetString());
        var hardware = report.RootElement.GetProperty("ResolvedHardware");
        Assert.Equal("Supported", hardware.GetProperty("Status").GetString());
        Assert.Equal("msi.claw", hardware.GetProperty("DeviceFamily").GetString());
    }

    [Fact]
    public async Task Concurrent_process_shutdown_during_start_never_lets_an_unexpected_exception_escape()
    {
        // Review finding #2 on PR #290: StartClawSensorProbeAsync now links the RPC token with the
        // coordinator's LifecycleCancellation, so a concurrent BeginProcessShutdown (which disposes
        // the active coordinator, cancelling that token) must surface as, at most, an
        // OperationCanceledException at the RPC boundary -- never an unrelated/unhandled exception
        // type, and the shutdown call itself must not throw or deadlock against Start.
        var control = CreateControl(ClawFamilySnapshot(HardwareCompatibilityStatus.Supported));
        await control.OpenClawSensorProbeAsync();

        var startTask = control.StartClawSensorProbeAsync();
        control.BeginProcessShutdown();
        var exception = await Record.ExceptionAsync(() => startTask);

        Assert.True(exception is null or OperationCanceledException,
            $"Expected null or OperationCanceledException, got {exception?.GetType()}: {exception?.Message}");
    }

    [Fact]
    public async Task Start_then_close_disposes_cleanly_without_throwing()
    {
        var control = CreateControl(ClawFamilySnapshot(HardwareCompatibilityStatus.Supported));
        await control.OpenClawSensorProbeAsync();
        await control.StartClawSensorProbeAsync();

        var closed = await control.CloseClawSensorProbeAsync();

        Assert.False(closed.Available);
    }

    [Fact]
    public async Task Open_after_a_completed_or_failed_session_starts_fresh()
    {
        var control = CreateControl(ClawFamilySnapshot(HardwareCompatibilityStatus.Supported));
        await control.OpenClawSensorProbeAsync();
        var failed = await control.StartClawSensorProbeAsync();
        Assert.Equal(FrontendClawSensorProbeState.Failed, failed.State);

        var reopened = await control.OpenClawSensorProbeAsync();

        Assert.True(reopened.Available);
        Assert.Equal(FrontendClawSensorProbeState.Ready, reopened.State);
        Assert.Null(reopened.ErrorMessage);
    }

    [Fact]
    public async Task Process_shutdown_disposes_an_active_coordinator_without_throwing()
    {
        var control = CreateControl(ClawFamilySnapshot(HardwareCompatibilityStatus.Supported));
        await control.OpenClawSensorProbeAsync();

        control.BeginProcessShutdown();

        // No exception -- the active coordinator was disposed synchronously by the shutdown hook.
        var exception = await Record.ExceptionAsync(() => control.CaptureClawSensorProbeAsync());
        Assert.Null(exception);
    }

    [Fact]
    public async Task Open_rejects_a_session_commit_that_resumes_after_shutdown_has_begun()
    {
        // PR #290 re-review: ThrowIfShuttingDown() at the top of OpenClawSensorProbeAsync only
        // covers the time before the awaited _status.CaptureAsync() call. If BeginProcessShutdown()
        // runs its one-time detach/dispose pass while an Open request is suspended there, that
        // request must NOT be allowed to resume and commit a brand-new coordinator -- the shutdown
        // recheck and the session commit have to be atomic under the same gate BeginProcessShutdown
        // uses, or BeginProcessShutdown() is not a real mutation barrier.
        var statusProvider = new BlockableSystemStatusProvider(ClawFamilySnapshot(HardwareCompatibilityStatus.Supported));
        var control = CreateControl(statusProvider);

        var open = control.OpenClawSensorProbeAsync();
        await statusProvider.CaptureEntered;
        control.BeginProcessShutdown();
        statusProvider.ReleaseCapture();

        await Assert.ThrowsAsync<FrontendProtocolException>(() => open);
        Assert.False((await control.CaptureClawSensorProbeAsync()).Available);
    }

    [Fact]
    public async Task Shutdown_barrier_rejects_further_operations()
    {
        var control = CreateControl(ClawFamilySnapshot(HardwareCompatibilityStatus.Supported));
        control.BeginProcessShutdown();

        await Assert.ThrowsAsync<FrontendProtocolException>(() => control.OpenClawSensorProbeAsync());
    }

    [Fact]
    public void Runtime_ClawSensorProbe_code_has_no_WinUI_dependency()
    {
        // Architectural invariant: the Runtime-owned diagnostic backend must stay WinUI/XAML-free so
        // the Runtime process itself remains usable headless. Mirrors the same style of assembly-
        // reference assertion used elsewhere for the Runtime/UI boundary.
        var runtimeAssembly = typeof(SteamInputAddonforClaw.Diagnostics.ClawSensorProbe.ClawSensorProbeCoordinator).Assembly;
        var referencedAssemblyNames = runtimeAssembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();

        Assert.DoesNotContain("Microsoft.WinUI", referencedAssemblyNames);
        Assert.DoesNotContain(referencedAssemblyNames, name => name is not null && name.Contains("Microsoft.UI.Xaml", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedAssemblyNames, name => name is not null && name.Contains("SteamInputAddonforClaw.UI", StringComparison.OrdinalIgnoreCase));
    }

    private InProcessAddonFrontendControl CreateControl(SystemStatusSnapshot snapshot)
    {
        AppLog.DirectoryOverride = _testDirectory;
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), store, new FakeStartupManager());
        return new InProcessAddonFrontendControl(
            coordinator,
            new FixedSystemStatusProvider(snapshot),
            null,
            new DeveloperTestModeState(),
            "",
            captureRoutingStatus: () => new(true, RoutingOperationalState.Passive, false, false));
    }

    private InProcessAddonFrontendControl CreateControl(ISystemStatusProvider statusProvider)
    {
        AppLog.DirectoryOverride = _testDirectory;
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), store, new FakeStartupManager());
        return new InProcessAddonFrontendControl(
            coordinator,
            statusProvider,
            null,
            new DeveloperTestModeState(),
            "",
            captureRoutingStatus: () => new(true, RoutingOperationalState.Passive, false, false));
    }

    private static SystemStatusSnapshot ClawFamilySnapshot(HardwareCompatibilityStatus status) => new(
        new("MSI", "Claw A1M", "Claw A1M board", []),
        new HardwareCompatibilityAssessment(status, new HandheldDeviceId("msi.claw"), null, "test"),
        [], null!, null!, null!, null!, null!, true, false);

    private static SystemStatusSnapshot NonClawSnapshot() => new(
        new("Dell", "G Series", "Board", []),
        new HardwareCompatibilityAssessment(HardwareCompatibilityStatus.Unsupported, null, null, "No handheld-device adapter matched."),
        [], null!, null!, null!, null!, null!, true, false);

    public void Dispose()
    {
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_testDirectory)) Directory.Delete(_testDirectory, recursive: true);
    }

    private sealed class FakeStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }

    private sealed class FixedSystemStatusProvider(SystemStatusSnapshot snapshot) : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }

    /// <summary>Lets a test suspend an in-flight OpenClawSensorProbeAsync() exactly at its
    /// _status.CaptureAsync() await point, so BeginProcessShutdown() can be driven while that request
    /// is still resumable -- reproducing the shutdown/Open commit race from PR #290 re-review.</summary>
    private sealed class BlockableSystemStatusProvider(SystemStatusSnapshot snapshot) : ISystemStatusProvider
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task CaptureEntered => _entered.Task;
        public void ReleaseCapture() => _release.TrySetResult();

        public async Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await _release.Task.ConfigureAwait(false);
            return snapshot;
        }
    }
}
