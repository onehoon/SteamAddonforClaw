using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Profiles;
using SteamInputAddonforClaw.Profiles.Performance;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Shared Frontend V2, SF-V2-01: <see cref="FrontendDeviceQuickSettingsSnapshot"/> and
/// <see cref="InProcessAddonFrontendControl.CaptureDeviceQuickSettingsAsync"/> must reuse the
/// existing Runtime authorities/mappers exactly, stay read-only, and isolate one child's failure
/// from healthy siblings (work order sections 6/8/13.1/13.2). TDP is left null (unconfigured) in
/// every aggregate test here rather than standing up a full hardware-backed <c>TdpRuntime</c> --
/// that already exercises the "missing authority -> only that child Unavailable" path, which is
/// the same shape a real TDP hardware failure would take.</summary>
[Collection("AppLog")]
public sealed class DeviceQuickSettingsAggregateTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void Unavailable_aggregate_contains_all_three_unavailable_children()
    {
        var snapshot = FrontendDeviceQuickSettingsSnapshot.Unavailable;

        Assert.Equal(FrontendCpuBoostSnapshot.Unavailable, snapshot.CpuBoost);
        Assert.Equal(FrontendTdpSnapshot.Unavailable, snapshot.Tdp);
        Assert.Equal(FrontendPowerModeSnapshot.Unavailable, snapshot.PowerMode);
    }

    [Fact]
    public async Task Aggregate_maps_the_same_values_as_the_individual_captures()
    {
        var cpuPolicy = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Aggressive), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var cpuBoostRuntime = CreateReconciledCpuBoostRuntime(cpuPolicy);
        var powerModeRuntime = CreateReconciledPowerModeRuntime(new FakePowerModePolicy());
        var control = CreateControl(cpuBoostRuntime, powerModeRuntime);

        var aggregate = await control.CaptureDeviceQuickSettingsAsync();

        Assert.Equal(await control.CaptureCpuBoostAsync(), aggregate.CpuBoost);
        Assert.Equal(await control.CapturePowerModeAsync(), aggregate.PowerMode);
        // No TdpRuntime was configured -- this is the "missing TDP authority" case (section 13.2).
        Assert.Equal(FrontendTdpSnapshot.Unavailable, aggregate.Tdp);
    }

    [Fact]
    public async Task Missing_cpu_authority_produces_only_cpu_unavailable()
    {
        var powerModeRuntime = CreateReconciledPowerModeRuntime(new FakePowerModePolicy());
        var control = CreateControl(cpuBoostRuntime: null, powerModeRuntime: powerModeRuntime);

        var aggregate = await control.CaptureDeviceQuickSettingsAsync();

        Assert.Equal(FrontendCpuBoostSnapshot.Unavailable, aggregate.CpuBoost);
        Assert.NotEqual(FrontendPowerModeSnapshot.Unavailable, aggregate.PowerMode);
        Assert.Equal(WindowsPowerMode.Balanced, aggregate.PowerMode.Ac.Current);
    }

    [Fact]
    public async Task Missing_power_mode_authority_produces_only_power_mode_unavailable()
    {
        var cpuPolicy = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Aggressive), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var cpuBoostRuntime = CreateReconciledCpuBoostRuntime(cpuPolicy);
        var control = CreateControl(cpuBoostRuntime, powerModeRuntime: null);

        var aggregate = await control.CaptureDeviceQuickSettingsAsync();

        Assert.Equal(FrontendPowerModeSnapshot.Unavailable, aggregate.PowerMode);
        Assert.NotEqual(FrontendCpuBoostSnapshot.Unavailable, aggregate.CpuBoost);
        Assert.Equal(CpuBoostMode.Aggressive, aggregate.CpuBoost.Ac.Current);
    }

    [Fact]
    public async Task Aggregate_capture_never_mutates_or_persists_anything()
    {
        var cpuPolicy = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Aggressive), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var cpuBoostRuntime = CreateReconciledCpuBoostRuntime(cpuPolicy);
        var powerModeRuntime = CreateReconciledPowerModeRuntime(new FakePowerModePolicy());
        var control = CreateControl(cpuBoostRuntime, powerModeRuntime);
        var profilesPath = Path.Combine(_testDirectory, "profiles.json");
        var contentsAfterBootstrap = File.ReadAllText(profilesPath);

        await control.CaptureDeviceQuickSettingsAsync();
        await control.CaptureDeviceQuickSettingsAsync();

        Assert.Equal(0, cpuPolicy.AcWriteCount);
        Assert.Equal(0, cpuPolicy.DcWriteCount);
        Assert.Equal(contentsAfterBootstrap, File.ReadAllText(profilesPath));
    }

    [Fact]
    public async Task Aggregate_capture_does_not_raise_state_invalidated()
    {
        var cpuBoostRuntime = CreateReconciledCpuBoostRuntime(new FakeCpuBoostPowerPolicy());
        var control = CreateControl(cpuBoostRuntime, powerModeRuntime: null);
        var invalidated = false;
        control.StateInvalidated += (_, _) => invalidated = true;

        await control.CaptureDeviceQuickSettingsAsync();

        Assert.False(invalidated);
    }

    [Fact]
    public async Task Shutdown_barrier_rejects_aggregate_capture()
    {
        var cpuBoostRuntime = CreateReconciledCpuBoostRuntime(new FakeCpuBoostPowerPolicy());
        var control = CreateControl(cpuBoostRuntime, powerModeRuntime: null);
        control.BeginProcessShutdown();

        var exception = await Assert.ThrowsAsync<FrontendProtocolException>(() => control.CaptureDeviceQuickSettingsAsync());
        Assert.Contains("shutting down", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancellation_is_not_converted_into_a_healthy_aggregate()
    {
        var cpuBoostRuntime = CreateReconciledCpuBoostRuntime(new FakeCpuBoostPowerPolicy());
        var control = CreateControl(cpuBoostRuntime, powerModeRuntime: null);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => control.CaptureDeviceQuickSettingsAsync(cts.Token));
    }

    private CpuBoostRuntime CreateReconciledCpuBoostRuntime(ICpuBoostPowerPolicy policy)
    {
        var runtime = new CpuBoostRuntime(new ProfileStore(Path.Combine(_testDirectory, "profiles.json")), policy);
        runtime.StartupReconcile();
        return runtime;
    }

    private PowerModeRuntime CreateReconciledPowerModeRuntime(IPowerModePolicy policy)
    {
        var runtime = new PowerModeRuntime(new ProfileStore(Path.Combine(_testDirectory, "profiles.json")), policy);
        runtime.StartupReconcile();
        return runtime;
    }

    private InProcessAddonFrontendControl CreateControl(CpuBoostRuntime? cpuBoostRuntime, PowerModeRuntime? powerModeRuntime)
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _testDirectory;
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), store, new FakeStartupManager());
        return new InProcessAddonFrontendControl(
            coordinator,
            new ThrowingSystemStatusProvider(),
            null,
            new DeveloperTestModeState(),
            cpuBoostRuntime: cpuBoostRuntime,
            powerModeRuntime: powerModeRuntime);
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
