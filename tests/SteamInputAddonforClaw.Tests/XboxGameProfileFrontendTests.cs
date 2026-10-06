using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Profiles;
using SteamInputAddonforClaw.Profiles.Performance;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class XboxGameProfileFrontendTests : IDisposable
{
    private const string Key = "store:9PK8PHLCQDF6";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"XboxProfileFrontend-{Guid.NewGuid():N}");
    private string ProfilePath => Path.Combine(_directory, "profiles.json");

    [Fact]
    public async Task XBOX_capture_projects_existing_TDP_limits_and_Intel_capability()
    {
        var store = new ProfileStore(ProfilePath);
        var gate = new ProfileMutationGate();
        var mutations = new XboxGameProfileMutations(store, gate, Model());
        var tdp = new TdpRuntime(store, gate, Model(), new MsiClawTdpHardware(new RecordingTdpTransport()));
        var limiter = new RecordingFrameLimiter();
        var fps = new IntelFrameLimiterRuntime(store, gate, limiter, marker: Path.Combine(_directory, "fps-marker.json"));
        var control = CreateControl(mutations, tdp, fps);

        var snapshot = await control.CaptureXboxGameProfileAsync(Key);

        Assert.Equal(Key, snapshot.Key);
        Assert.True(snapshot.PersistenceWritable);
        Assert.Equal(new FrontendTdpLimits(8, 30, 8, 37), snapshot.Limits);
        Assert.True(snapshot.FpsLimit!.Available);
        Assert.False(snapshot.Exists);
        await tdp.DisposeAsync();
        fps.Dispose();
    }

    [Fact]
    public async Task XBOX_profile_mutations_persist_without_live_apply_or_state_invalidation()
    {
        Directory.CreateDirectory(_directory);
        var store = new ProfileStore(ProfilePath);
        var gate = new ProfileMutationGate();
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings
            {
                Performance = new DevicePerformanceSettings
                {
                    Tdp = new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 24), Dc = Pair(18, 22) },
                    PowerMode = new DevicePowerModeSettings { Ac = WindowsPowerMode.Balanced, Dc = WindowsPowerMode.BestPowerEfficiency }
                }
            }
        });
        var mutations = new XboxGameProfileMutations(store, gate, Model());
        var transport = new RecordingTdpTransport();
        var tdp = new TdpRuntime(store, gate, Model(), new MsiClawTdpHardware(transport));
        var limiter = new RecordingFrameLimiter();
        var fps = new IntelFrameLimiterRuntime(store, gate, limiter, marker: Path.Combine(_directory, "fps-marker.json"));
        var control = CreateControl(mutations, tdp, fps, actualRunningAppIdSource: () => throw new InvalidOperationException("XBOX profile edits must not consult the Steam active AppID."));
        var invalidations = 0;
        control.StateInvalidated += (_, _) => invalidations++;

        Assert.True((await control.SetXboxGameProfileFavoriteAsync(Key, true, "Game")).Succeeded);
        Assert.True((await control.SetXboxGameProfileEnabledAsync(Key, true, "Game")).Succeeded);
        Assert.True((await control.SetXboxGameProfileCpuBoostAcAsync(Key, CpuBoostMode.Aggressive)).Succeeded);
        Assert.True((await control.SetXboxGameProfileCpuBoostDcAsync(Key, CpuBoostMode.Disabled)).Succeeded);
        Assert.True((await control.SetXboxGameProfileCpuBoostEnabledAsync(Key, true)).Succeeded);
        Assert.True((await control.SetXboxGameProfileTdpAsync(Key,
            new FrontendGameTdpConfiguration(true, new(25, 30), new(18, 24)))).Succeeded);
        Assert.True((await control.SetXboxGameProfileTdpEnabledAsync(Key, true)).Succeeded);
        Assert.True((await control.SetXboxGameProfilePowerModeAcAsync(Key, WindowsPowerMode.BestPerformance)).Succeeded);
        Assert.True((await control.SetXboxGameProfilePowerModeDcAsync(Key, WindowsPowerMode.Balanced)).Succeeded);
        Assert.True((await control.SetXboxGameProfilePowerModeEnabledAsync(Key, true)).Succeeded);
        Assert.True((await control.SetXboxGameProfileFpsLimitAcAsync(Key, 90)).Succeeded);
        Assert.True((await control.SetXboxGameProfileFpsLimitDcAsync(Key, 60)).Succeeded);
        Assert.True((await control.SetXboxGameProfileFpsLimitEnabledAsync(Key, true)).Succeeded);
        Assert.True((await control.SetXboxGameProfileResolutionAsync(Key, new(1920, 1200), "Game")).Succeeded);

        var capture = await control.CaptureXboxGameProfileAsync(Key);
        var saved = store.Load().Document.XboxGames[Key];
        Assert.True(capture.Exists);
        Assert.True(capture.Enabled);
        Assert.True(capture.CpuBoost.Enabled);
        Assert.Equal(CpuBoostMode.Aggressive, capture.CpuBoost.Ac);
        Assert.Equal(new FrontendTdpPowerPair(25, 30), capture.Tdp.Ac);
        Assert.Equal(WindowsPowerMode.BestPerformance, capture.PowerMode!.Ac);
        Assert.True(capture.FpsLimit!.Enabled);
        Assert.Equal(90, capture.FpsLimit.AcFps);
        Assert.Equal(new FrontendGameResolution(1920, 1200), capture.Resolution);
        Assert.True(saved.Favorite);
        Assert.Equal(0, invalidations);
        Assert.Equal(0, transport.OperationCount);
        Assert.Equal(0, limiter.ApplyCount);
        await tdp.DisposeAsync();
        fps.Dispose();
    }

    [Fact]
    public async Task Invalid_XBOX_TDP_and_FPS_values_do_not_write_the_profile()
    {
        var mutations = new XboxGameProfileMutations(new ProfileStore(ProfilePath), new ProfileMutationGate(), Model());
        mutations.SetEnabled(Key, true, "Game");
        var control = CreateControl(mutations);
        var before = File.ReadAllText(ProfilePath);

        var tdp = await control.SetXboxGameProfileTdpAsync(Key,
            new FrontendGameTdpConfiguration(true, new(7, 25), new(18, 22)));
        var fps = await control.SetXboxGameProfileFpsLimitAcAsync(Key, 39);

        Assert.Equal(FrontendGameProfileMutationOutcome.InvalidTarget, tdp.Outcome);
        Assert.Equal(FrontendGameProfileMutationOutcome.InvalidTarget, fps.Outcome);
        Assert.Equal(before, File.ReadAllText(ProfilePath));
    }

    private InProcessAddonFrontendControl CreateControl(
        XboxGameProfileMutations mutations,
        TdpRuntime? tdp = null,
        IntelFrameLimiterRuntime? fps = null,
        Func<uint>? actualRunningAppIdSource = null)
    {
        var settings = new StartupSettingsCoordinator(new AppSettings(),
            new SettingsStore(Path.Combine(_directory, "settings.json")), new NoOpStartupManager());
        return new InProcessAddonFrontendControl(settings, new ThrowingStatusProvider(), null,
            tdpRuntime: tdp, intelFpsRuntime: fps, xboxGameProfileMutations: mutations,
            actualRunningAppIdSource: actualRunningAppIdSource);
    }

    private static HandheldDeviceModelId Model() => new("msi.claw.a2vm.7");
    private static TdpPowerPair Pair(int pl1, int pl2) => new() { Pl1Watts = pl1, Pl2Watts = pl2 };

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class NoOpStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }

    private sealed class ThrowingStatusProvider : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Status is not used by these tests.");
    }

    private sealed class RecordingTdpTransport : IMsiClawTdpTransport
    {
        public int OperationCount { get; private set; }
        public bool TryGetAp(int index, out byte[] payload) { OperationCount++; payload = [0, 0, 0xC0]; return true; }
        public bool TrySetData(int block, byte value) { OperationCount++; return true; }
    }

    private sealed class RecordingFrameLimiter : IIntelFrameLimiter
    {
        public int ApplyCount { get; private set; }
        public void Initialize() { }
        public bool Available => true;
        public string? UnavailableReason => null;
        public IntelFpsCapability? Capability => null;
        public IntelFpsApplyOutcome Enable(int fps, AcDcPowerSource source, uint appId) { ApplyCount++; return IntelFpsApplyOutcome.Succeeded; }
        public bool Disable(AcDcPowerSource? source, uint appId) { ApplyCount++; return true; }
        public void Dispose() { }
    }
}
