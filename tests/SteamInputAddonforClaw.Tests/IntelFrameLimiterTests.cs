using SteamInputAddonforClaw.Profiles;
using SteamInputAddonforClaw.Profiles.Performance;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class IntelFrameLimiterTests
{
    [Theory]
    [InlineData(41, 300, 1, false)]
    [InlineData(30, 119, 1, false)]
    [InlineData(30, 300, 2, false)]
    [InlineData(30, 300, 1, true)]
    public void Capability_requires_the_complete_addon_range(int min, int max, int step, bool expected) =>
        Assert.Equal(expected, new IntelFpsCapability(min, max, step, 2, 0, true).SupportsAddonRange);

    [Fact]
    public void Missing_profile_projects_off_and_first_enable_creates_60_pair()
    {
        using var fixture = new FpsFixture();
        fixture.Store.Save(new ProfileDocument { Games = new() { ["42"] = EnabledProfile() } });
        var mutations = new GameProfileMutations(fixture.Store);
        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded, mutations.SetFpsLimitEnabled(42, true));
        var fps = fixture.Store.Load().Document.Games["42"].Performance.FpsLimit;
        Assert.Equal(new GameFpsLimitSettings { Enabled = true, AcFps = 60, DcFps = 60 }, fps);
    }

    [Fact]
    public void Disable_and_reenable_preserve_custom_values()
    {
        using var fixture = new FpsFixture();
        fixture.Store.Save(new ProfileDocument { Games = new() { ["42"] = EnabledProfile(new GameFpsLimitSettings { Enabled = true, AcFps = 73, DcFps = 47 }) } });
        var mutations = new GameProfileMutations(fixture.Store);
        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded, mutations.SetFpsLimitEnabled(42, false));
        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded, mutations.SetFpsLimitEnabled(42, true));
        Assert.Equal((73, 47), (fixture.Store.Load().Document.Games["42"].Performance.FpsLimit!.AcFps, fixture.Store.Load().Document.Games["42"].Performance.FpsLimit!.DcFps));
    }

    [Theory]
    [InlineData(39)]
    [InlineData(121)]
    public void Values_outside_40_to_120_are_rejected(int fps)
    {
        using var fixture = new FpsFixture(); fixture.Store.Save(new ProfileDocument { Games = new() { ["42"] = EnabledProfile() } });
        Assert.Equal(GameProfileMutations.MutationOutcome.InvalidTarget, new GameProfileMutations(fixture.Store).SetFpsLimitAc(42, fps));
    }

    [Fact]
    public void Active_reconcile_selects_current_rail_and_ownership_cleanup_is_marker_bounded()
    {
        using var fixture = new FpsFixture(); fixture.Store.Save(new ProfileDocument { Games = new() { ["42"] = EnabledProfile(new GameFpsLimitSettings { Enabled = true, AcFps = 73, DcFps = 47 }) } });
        var fake = new FakeLimiter(); var runtime = new IntelFrameLimiterRuntime(fixture.Store, new ProfileMutationGate(), fake, () => FpsPowerSource.DC, fixture.Marker); runtime.Reconcile(42);
        Assert.Equal((true, 47), (fake.LastEnable, fake.LastFps)); Assert.True(File.Exists(fixture.Marker));
        runtime.Reconcile(0); Assert.True(fake.LastDisable); Assert.False(File.Exists(fixture.Marker));
    }

    [Fact]
    public void Startup_without_marker_does_not_touch_external_global_state()
    {
        using var fixture = new FpsFixture();
        var fake = new FakeLimiter();
        using var runtime = new IntelFrameLimiterRuntime(fixture.Store, new ProfileMutationGate(), fake, () => FpsPowerSource.AC, fixture.Marker);
        runtime.StartupRecover();
        Assert.Equal(0, fake.DisableCalls);
    }

    [Fact]
    public void Failed_disable_keeps_ownership_marker_for_retry()
    {
        using var fixture = new FpsFixture();
        Directory.CreateDirectory(fixture.DirectoryPath);
        File.WriteAllText(fixture.Marker, "{\"fps\":60}");
        var fake = new FakeLimiter { DisableResult = false };
        using var runtime = new IntelFrameLimiterRuntime(fixture.Store, new ProfileMutationGate(), fake, () => FpsPowerSource.DC, fixture.Marker);
        runtime.StartupRecover();
        Assert.Equal(1, fake.DisableCalls);
        Assert.True(File.Exists(fixture.Marker));
    }

    [Fact]
    public void Non_active_profile_reconcile_does_not_call_igcl()
    {
        using var fixture = new FpsFixture();
        fixture.Store.Save(new ProfileDocument { Games = new() { ["42"] = EnabledProfile(new GameFpsLimitSettings { Enabled = true, AcFps = 73, DcFps = 47 }) } });
        var fake = new FakeLimiter();
        using var runtime = new IntelFrameLimiterRuntime(fixture.Store, new ProfileMutationGate(), fake, () => FpsPowerSource.AC, fixture.Marker);
        runtime.Reconcile(43);
        Assert.Equal(0, fake.EnableCalls);
        Assert.Equal(0, fake.DisableCalls);
    }

    [Fact]
    public void Marker_persistence_failure_disables_successfully_enabled_limit()
    {
        using var fixture = new FpsFixture();
        fixture.Store.Save(new ProfileDocument { Games = new() { ["42"] = EnabledProfile(new GameFpsLimitSettings { Enabled = true, AcFps = 73, DcFps = 47 }) } });
        Directory.CreateDirectory(fixture.Marker);
        var fake = new FakeLimiter();
        using var runtime = new IntelFrameLimiterRuntime(fixture.Store, new ProfileMutationGate(), fake, () => FpsPowerSource.AC, fixture.Marker);
        Assert.False(runtime.ReconcileWithResult(42));
        Assert.Equal(1, fake.EnableCalls);
        Assert.Equal(1, fake.DisableCalls);
    }

    private static GameProfile EnabledProfile(GameFpsLimitSettings? fps = null) => new() { Enabled = true, Performance = new GamePerformanceOverrides { CpuBoost = new() { Ac = SteamInputAddonforClaw.Contracts.DeviceProfiles.CpuBoostMode.Enabled, Dc = SteamInputAddonforClaw.Contracts.DeviceProfiles.CpuBoostMode.Enabled }, Tdp = new() { Ac = new() { Pl1Watts = 20, Pl2Watts = 22 }, Dc = new() { Pl1Watts = 20, Pl2Watts = 22 } }, FpsLimit = fps } };

    private sealed class FpsFixture : IDisposable
    {
        internal readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "intel-fps-" + Guid.NewGuid().ToString("N")); internal string ProfilePath => Path.Combine(DirectoryPath, "profiles.json"); internal string Marker => Path.Combine(DirectoryPath, "intel-fps-limit-ownership.json"); private ProfileStore? _store; internal ProfileStore Store => _store ??= new(ProfilePath);
        public void Dispose() { if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true); }
    }

    private sealed class FakeLimiter : IIntelFrameLimiter
    {
        public bool Available => true; public string? UnavailableReason => null; public IntelFpsCapability? Capability => new(30, 300, 1, 2, 0, true); public bool LastEnable; public bool LastDisable; public int LastFps; public int EnableCalls; public int DisableCalls; public bool DisableResult = true;
        public bool Enable(int fps, FpsPowerSource source, uint appId) { EnableCalls++; LastEnable = true; LastDisable = false; LastFps = fps; return true; }
        public bool Disable(FpsPowerSource? source, uint appId) { DisableCalls++; LastDisable = true; LastEnable = false; return DisableResult; }
        public void Dispose() { }
    }
}
