using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Profiles;
using SteamInputAddonforClaw.Profiles.Performance;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class IntelGpuMinimumClockFrontendTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"MinimumGpuClockFrontend-{Guid.NewGuid():N}");

    [Fact]
    public async Task Developer_probe_ownership_refuses_production_mutations_before_persistence()
    {
        Directory.CreateDirectory(_directory);
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _directory;
        var profilesPath = Path.Combine(_directory, "profiles.json");
        var hardware = new FakeMinimumClockControl();
        using var runtime = new IntelGpuMinimumClockRuntime(
            new ProfileStore(profilesPath), new ProfileMutationGate(), hardware,
            () => AcDcPowerSource.AC, Path.Combine(_directory, "minimum-clock.json"));
        var control = CreateControl(runtime, developerModified: true);

        var capture = await control.CaptureGpuMinimumClockAsync();
        var enable = await control.SetDeviceGpuMinimumClockEnabledAsync(true);
        var rail = await control.SetDeviceGpuMinimumClockAcAsync(1625);

        Assert.True(capture.Available);
        Assert.Equal(FrontendGpuMinimumClockMutationOutcome.Unavailable, enable.Outcome);
        Assert.Contains("before enabling", enable.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FrontendGpuMinimumClockMutationOutcome.Unavailable, rail.Outcome);
        Assert.False(File.Exists(profilesPath));
        Assert.Equal(0, hardware.SetCalls);
    }

    [Fact]
    public async Task Game_profile_frontend_resolves_table_indexes_to_exact_driver_mhz_without_device_write()
    {
        Directory.CreateDirectory(_directory);
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _directory;
        var profilesPath = Path.Combine(_directory, "profiles.json");
        var store = new ProfileStore(profilesPath);
        var gate = new ProfileMutationGate();
        var gameMutations = new GameProfileMutations(store, gate);
        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded, gameMutations.SetEnabled(42, true, "Game"));
        var hardware = new FakeMinimumClockControl();
        using var runtime = new IntelGpuMinimumClockRuntime(
            store, gate, hardware, () => AcDcPowerSource.AC, Path.Combine(_directory, "minimum-clock.json"));
        var control = CreateControl(runtime, developerModified: false,
            gameProfileMutations: gameMutations, actualRunningAppIdSource: () => 0);

        var before = await control.CaptureGameProfileAsync(42);
        Assert.True(before.GpuMinimumClock!.Available);
        Assert.False(before.GpuMinimumClock.Initialized);
        Assert.False(before.GpuMinimumClock.Enabled);
        Assert.Null(before.GpuMinimumClock.AcMhz);
        Assert.Null(before.GpuMinimumClock.DcMhz);
        Assert.NotNull(before.GpuMinimumClock.RecommendedDefaultMhz);
        Assert.Equal([1525d, 1625d, 1725d, 1825d], before.GpuMinimumClock.SelectableClocksMhz);
        var enabled = await control.SetGameProfileGpuMinimumClockEnabledAsync(42, true);
        Assert.True(enabled.Succeeded, $"{enabled.Outcome}: {enabled.FailureMessage}");
        var firstEnable = store.Load().Document.Games["42"].Performance.GpuMinimumClock!;
        Assert.Equal(before.GpuMinimumClock.RecommendedDefaultMhz, firstEnable.AcMhz);
        Assert.Equal(before.GpuMinimumClock.RecommendedDefaultMhz, firstEnable.DcMhz);
        Assert.True((await control.SetGameProfileGpuMinimumClockAcAsync(42, 2)).Succeeded);
        Assert.True((await control.SetGameProfileGpuMinimumClockDcAsync(42, 3)).Succeeded);

        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded, gameMutations.SetGpuMinimumClockDc(42, 1777));
        Assert.True((await control.SetGameProfileGpuMinimumClockEnabledAsync(42, false)).Succeeded);
        Assert.True((await control.SetGameProfileGpuMinimumClockEnabledAsync(42, true)).Succeeded);

        var saved = store.Load().Document.Games["42"].Performance.GpuMinimumClock!;
        Assert.True(saved.Enabled);
        Assert.Equal(1725, saved.AcMhz);
        Assert.Equal(before.GpuMinimumClock.RecommendedDefaultMhz, saved.DcMhz);
        Assert.Equal(0, hardware.SetCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Active_game_gpu_rail_edit_applies_only_when_it_matches_current_power_source(bool acPower)
    {
        Directory.CreateDirectory(_directory);
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _directory;
        var profilesPath = Path.Combine(_directory, "profiles.json");
        var store = new ProfileStore(profilesPath);
        var gate = new ProfileMutationGate();
        var gameMutations = new GameProfileMutations(store, gate);
        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded, gameMutations.SetEnabled(42, true, "Game"));
        var hardware = new FakeMinimumClockControl();
        var target = ActiveProfileTarget.ForSteam(42);
        using var runtime = new IntelGpuMinimumClockRuntime(
            store, gate, hardware, () => acPower ? AcDcPowerSource.AC : AcDcPowerSource.DC,
            Path.Combine(_directory, "minimum-clock.json"));
        runtime.SetActiveProfileResolver(document => ActiveProfileResolver.Resolve(target, document));
        var control = CreateControl(runtime, developerModified: false,
            gameProfileMutations: gameMutations,
            activeProfileTargetSource: () => target);

        Assert.True((await control.SetGameProfileGpuMinimumClockEnabledAsync(42, true)).Succeeded);
        Assert.Equal(1, hardware.SetCalls);

        Assert.True((await control.SetGameProfileGpuMinimumClockAcAsync(42, 0)).Succeeded);
        Assert.Equal(acPower ? 2 : 1, hardware.SetCalls);
        Assert.True((await control.SetGameProfileGpuMinimumClockDcAsync(42, 1)).Succeeded);
        Assert.Equal(2, hardware.SetCalls);
        Assert.Equal(acPower ? 1525 : 1625, hardware.LastSetRange.Min);

        var saved = store.Load().Document.Games["42"].Performance.GpuMinimumClock!;
        Assert.Equal(1525, saved.AcMhz);
        Assert.Equal(1625, saved.DcMhz);
    }

    [Fact]
    public async Task Developer_probe_blocks_Steam_gpu_mutations_and_whole_profile_activation_before_persistence()
    {
        Directory.CreateDirectory(_directory);
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _directory;
        var profilesPath = Path.Combine(_directory, "profiles.json");
        var store = new ProfileStore(profilesPath);
        var gate = new ProfileMutationGate();
        var mutations = new GameProfileMutations(store, gate);
        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded, mutations.SetEnabled(42, true, "Game"));
        var hardware = new FakeMinimumClockControl();
        using var runtime = new IntelGpuMinimumClockRuntime(
            store, gate, hardware, () => AcDcPowerSource.AC, Path.Combine(_directory, "minimum-clock.json"));
        var control = CreateControl(runtime, developerModified: true, gameProfileMutations: mutations);

        Assert.Equal(FrontendGameProfileMutationOutcome.Unavailable,
            (await control.SetGameProfileGpuMinimumClockEnabledAsync(42, true)).Outcome);
        Assert.Null(store.Load().Document.Games["42"].Performance.GpuMinimumClock);

        var configured = new GameGpuMinimumClockSettings { Enabled = true, AcMhz = 1725.125, DcMhz = 1825.375 };
        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded,
            mutations.SetGpuMinimumClockEnabled(42, true, configured));
        Assert.Equal(FrontendGameProfileMutationOutcome.Unavailable,
            (await control.SetGameProfileGpuMinimumClockAcAsync(42, 0)).Outcome);
        Assert.Equal(configured, store.Load().Document.Games["42"].Performance.GpuMinimumClock);

        Assert.True(mutations.Disable(42));
        Assert.Equal(FrontendGameProfileMutationOutcome.Unavailable,
            (await control.SetGameProfileEnabledAsync(42, true, "Game")).Outcome);
        Assert.False(store.Load().Document.Games["42"].Enabled);
        Assert.Equal(configured, store.Load().Document.Games["42"].Performance.GpuMinimumClock);
        Assert.Equal(0, hardware.SetCalls);
    }

    [Fact]
    public async Task Whole_profile_toggle_reconciles_between_game_override_and_device_fallback()
    {
        Directory.CreateDirectory(_directory);
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _directory;
        var profilesPath = Path.Combine(_directory, "profiles.json");
        var store = new ProfileStore(profilesPath);
        var gate = new ProfileMutationGate();
        var document = new ProfileDocument
        {
            Device = new()
            {
                Performance = new()
                {
                    GpuMinimumClock = new() { Enabled = true, AcMhz = 1625, DcMhz = 1525 }
                }
            }
        };
        store.Save(document);
        var mutations = new GameProfileMutations(store, gate);
        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded, mutations.SetEnabled(42, true, "Game"));
        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded,
            mutations.SetGpuMinimumClockEnabled(42, true,
                new GameGpuMinimumClockSettings { Enabled = true, AcMhz = 1725, DcMhz = 1625 }));
        var hardware = new FakeMinimumClockControl();
        var target = ActiveProfileTarget.ForSteam(42);
        using var runtime = new IntelGpuMinimumClockRuntime(
            store, gate, hardware, () => AcDcPowerSource.AC, Path.Combine(_directory, "minimum-clock.json"));
        runtime.SetActiveProfileResolver(profileDocument => ActiveProfileResolver.Resolve(target, profileDocument));
        var control = CreateControl(runtime, developerModified: false,
            gameProfileMutations: mutations,
            activeProfileTargetSource: () => target);

        Assert.True((await control.SetGameProfileEnabledAsync(42, false, null)).Succeeded);
        Assert.Equal(1, hardware.SetCalls);
        Assert.Equal(1625, hardware.LastSetRange.Min);
        Assert.True((await control.SetGameProfileEnabledAsync(42, true, "Game")).Succeeded);
        Assert.Equal(2, hardware.SetCalls);
        Assert.Equal(1725, hardware.LastSetRange.Min);
    }

    [Fact]
    public void Production_ownership_blocks_only_developer_frequency_range_operations()
    {
        Assert.True(InProcessAddonFrontendControl.ShouldBlockDeveloperFrequencyMutation(
            FrontendIntelGpuFrequencyProbeOperation.SetMaxMax, productionOwnsFrequency: true));
        Assert.True(InProcessAddonFrontendControl.ShouldBlockDeveloperFrequencyMutation(
            FrontendIntelGpuFrequencyProbeOperation.RestoreOriginalFrequency, productionOwnsFrequency: true));
        Assert.False(InProcessAddonFrontendControl.ShouldBlockDeveloperFrequencyMutation(
            FrontendIntelGpuFrequencyProbeOperation.SetTestPl1, productionOwnsFrequency: true));
        Assert.False(InProcessAddonFrontendControl.ShouldBlockDeveloperFrequencyMutation(
            FrontendIntelGpuFrequencyProbeOperation.RestoreOriginalPower, productionOwnsFrequency: true));
        Assert.False(InProcessAddonFrontendControl.ShouldBlockDeveloperFrequencyMutation(
            FrontendIntelGpuFrequencyProbeOperation.SetMaxMax, productionOwnsFrequency: false));
    }

    private InProcessAddonFrontendControl CreateControl(
        IntelGpuMinimumClockRuntime runtime,
        bool developerModified,
        GameProfileMutations? gameProfileMutations = null,
        Func<uint>? actualRunningAppIdSource = null,
        Func<ActiveProfileTarget>? activeProfileTargetSource = null)
    {
        var settings = new StartupSettingsCoordinator(
            new AppSettings(), new SettingsStore(Path.Combine(_directory, "settings.json")), new FakeStartupManager());
        return new InProcessAddonFrontendControl(
            settings,
            new ThrowingSystemStatusProvider(),
            null,
            gameProfileMutations: gameProfileMutations,
            actualRunningAppIdSource: actualRunningAppIdSource,
            activeProfileTargetSource: activeProfileTargetSource,
            intelGpuMinimumClockRuntime: runtime,
            developerGpuFrequencyProbeModified: () => developerModified);
    }

    public void Dispose()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DrainForTests();
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = null;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
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

    private sealed class FakeMinimumClockControl : IIntelGpuMinimumClockControl
    {
        private IntelGpuFrequencyRange _currentRange = new(-1, -1);
        public int SetCalls { get; private set; }
        public IntelGpuFrequencyRange LastSetRange { get; private set; }
        public IntelGpuMinimumClockNativeCapability Initialize() => Capability;
        public IntelGpuMinimumClockNativeCapability Reinitialize() => Capability;
        public IntelGpuFrequencyRange GetRange() => _currentRange;
        public uint SetRange(IntelGpuFrequencyRange range) { SetCalls++; LastSetRange = range; _currentRange = range; return 0; }
        public void Dispose() { }

        private static IntelGpuMinimumClockNativeCapability Capability { get; } = new(
            true, null, "Intel Integrated GPU", 0x8086, 0x1234, true, 300, 2300,
            [1400, 1525, 1625, 1725, 1825, 1925, 2025]);
    }
}
