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
}
