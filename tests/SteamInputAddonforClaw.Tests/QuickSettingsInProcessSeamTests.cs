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

/// <summary>Shared Frontend V2, SF-V2-03 section 22/23: the <see
/// cref="IAddonFrontendControl.CaptureQuickSettingsPageAsync"/>/<see
/// cref="IAddonFrontendControl.MutateQuickSettingAsync"/> seam on <see
/// cref="InProcessAddonFrontendControl"/> must stay read-only for capture, fail closed (with zero
/// side effects) for the not-yet-implemented Profile page, and preserve the existing shutdown/
/// cancellation conventions.</summary>
[Collection("AppLog")]
public sealed class QuickSettingsInProcessSeamTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task Capture_device_page_reflects_the_aggregate_and_persists_nothing()
    {
        var cpuPolicy = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Aggressive), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var cpuBoostRuntime = CreateReconciledCpuBoostRuntime(cpuPolicy);
        var control = CreateControl(cpuBoostRuntime);
        var profilesPath = Path.Combine(_testDirectory, "profiles.json");
        var contentsBefore = File.ReadAllText(profilesPath);

        var page = await control.CaptureQuickSettingsPageAsync(QuickSettingsPageId.Device);

        Assert.True(page.Available);
        var acRow = page.Sections.SelectMany(s => s.Rows).Single(r => r.RowId == QuickSettingsRowId.DeviceCpuBoostAc);
        Assert.Equal((int)CpuBoostMode.Aggressive, acRow.Value!.IntegerValue);
        Assert.Equal(0, cpuPolicy.AcWriteCount);
        Assert.Equal(0, cpuPolicy.DcWriteCount);
        Assert.Equal(contentsBefore, File.ReadAllText(profilesPath));
    }

    [Fact]
    public async Task Capture_profile_page_is_explicitly_unavailable_with_zero_side_effects()
    {
        var control = CreateControl(cpuBoostRuntime: null);

        var page = await control.CaptureQuickSettingsPageAsync(QuickSettingsPageId.Profile, appId: 4000u);

        Assert.False(page.Available);
        Assert.Equal(QuickSettingsPageId.Profile, page.PageId);
        Assert.Equal(4000u, page.AppId);
        Assert.Empty(page.Sections);
    }

    [Fact]
    public async Task Capture_device_page_with_app_id_is_unavailable()
    {
        var control = CreateControl(cpuBoostRuntime: null);

        var page = await control.CaptureQuickSettingsPageAsync(QuickSettingsPageId.Device, appId: 1u);

        Assert.False(page.Available);
    }

    [Fact]
    public async Task Shutdown_barrier_rejects_page_capture_and_mutation()
    {
        var control = CreateControl(cpuBoostRuntime: null);
        control.BeginProcessShutdown();

        await Assert.ThrowsAsync<FrontendProtocolException>(() => control.CaptureQuickSettingsPageAsync(QuickSettingsPageId.Device));

        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Device, null, QuickSettingsRowId.DeviceCpuBoostEnabled,
            [new(QuickSettingsRowId.DeviceCpuBoostEnabled, QuickSettingsValue.Boolean(true))]);
        await Assert.ThrowsAsync<FrontendProtocolException>(() => control.MutateQuickSettingAsync(intent));
    }

    [Fact]
    public async Task Valid_mutation_reaches_the_underlying_typed_method_and_reprojects()
    {
        var cpuPolicy = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Disabled), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var cpuBoostRuntime = CreateReconciledCpuBoostRuntime(cpuPolicy);
        var control = CreateControl(cpuBoostRuntime);
        var invalidationCount = 0;
        control.StateInvalidated += (_, _) => invalidationCount++;
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Device, null, QuickSettingsRowId.DeviceCpuBoostAc,
            [new(QuickSettingsRowId.DeviceCpuBoostAc, QuickSettingsValue.Integer((int)CpuBoostMode.Aggressive))]);

        var result = await control.MutateQuickSettingAsync(intent);

        Assert.True(result.Succeeded);
        Assert.Equal(1, cpuPolicy.AcWriteCount);
        // Section 30: the adapter must not add a second invalidation beyond the one the underlying
        // typed SetDeviceCpuBoostAcAsync mutation already raises.
        Assert.Equal(1, invalidationCount);
        var acRow = result.Page.Sections.SelectMany(s => s.Rows).Single(r => r.RowId == QuickSettingsRowId.DeviceCpuBoostAc);
        Assert.Equal((int)CpuBoostMode.Aggressive, acRow.Value!.IntegerValue);
    }

    private CpuBoostRuntime CreateReconciledCpuBoostRuntime(ICpuBoostPowerPolicy policy)
    {
        var runtime = new CpuBoostRuntime(new ProfileStore(Path.Combine(_testDirectory, "profiles.json")), policy);
        runtime.StartupReconcile();
        return runtime;
    }

    private InProcessAddonFrontendControl CreateControl(CpuBoostRuntime? cpuBoostRuntime)
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
            powerModeRuntime: null);
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
