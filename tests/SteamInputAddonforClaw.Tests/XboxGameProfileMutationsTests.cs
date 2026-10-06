using System.Text.Json;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Profiles;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class XboxGameProfileMutationsTests : IDisposable
{
    private const string StoreKey = "store:9PK8PHLCQDF6";
    private const string PfnKey = "pfn:Microsoft.Example_8wekyb3d8bbwe";
    private const string IdentityKey = "identity:SAMPLE.GAME|CN%3DSAMPLE|PC";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"XboxProfileMutations-{Guid.NewGuid():N}");
    private string PathName => Path.Combine(_directory, "profiles.json");

    [Theory]
    [InlineData(StoreKey)]
    [InlineData(PfnKey)]
    [InlineData(IdentityKey)]
    [InlineData("identity:not-a-canonical-key")]
    [InlineData("store:payload:with:colon")]
    public void Canonical_key_families_are_accepted(string key)
    {
        var store = new ProfileStore(PathName);
        var mutations = new XboxGameProfileMutations(store, new ProfileMutationGate());

        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, mutations.SetFavorite(key, true, "Game"));
        Assert.Contains(key, store.Load().Document.XboxGames.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("steam:123")]
    [InlineData("future:key")]
    [InlineData("store:")]
    [InlineData("identity:")]
    public void Invalid_key_families_are_rejected_without_creating_or_writing_a_document(string? key)
    {
        var mutations = new XboxGameProfileMutations(new ProfileStore(PathName), new ProfileMutationGate());

        Assert.Equal(XboxGameProfileMutations.MutationOutcome.InvalidTarget, mutations.SetFavorite(key!, true, "Game"));
        Assert.False(File.Exists(PathName));
    }

    [Fact]
    public void Enable_copies_saved_device_values_and_uses_fallbacks_without_device_defaults()
    {
        var store = new ProfileStore(PathName);
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings
            {
                Performance = new DevicePerformanceSettings
                {
                    CpuBoost = new DeviceCpuBoostSettings { Enabled = false, Ac = CpuBoostMode.Aggressive, Dc = CpuBoostMode.EfficientEnabled },
                    Tdp = new DeviceTdpSettings { Enabled = false, Ac = Pair(30, 35), Dc = Pair(18, 22) },
                    PowerMode = new DevicePowerModeSettings { Ac = WindowsPowerMode.BestPerformance, Dc = WindowsPowerMode.Balanced }
                }
            }
        });
        var mutations = new XboxGameProfileMutations(store, new ProfileMutationGate());

        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, mutations.SetEnabled(StoreKey, true, "Game"));
        var copied = store.Load().Document.XboxGames[StoreKey];
        Assert.True(copied.Enabled);
        Assert.True(copied.Performance.CpuBoost!.Enabled);
        Assert.Equal(CpuBoostMode.Aggressive, copied.Performance.CpuBoost.Ac);
        Assert.Equal(CpuBoostMode.EfficientEnabled, copied.Performance.CpuBoost.Dc);
        Assert.Equal(Pair(30, 35), copied.Performance.Tdp!.Ac);
        Assert.Equal(Pair(18, 22), copied.Performance.Tdp.Dc);
        Assert.Equal(WindowsPowerMode.BestPerformance, copied.Performance.PowerMode!.Ac);

        var fallbackStore = new ProfileStore(Path.Combine(_directory, "fallback", "profiles.json"));
        var fallback = new XboxGameProfileMutations(fallbackStore, new ProfileMutationGate());
        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, fallback.SetEnabled(PfnKey, true, null));
        var defaulted = fallbackStore.Load().Document.XboxGames[PfnKey];
        Assert.Equal(CpuBoostMode.Enabled, defaulted.Performance.CpuBoost!.Ac);
        Assert.Equal(CpuBoostMode.Enabled, defaulted.Performance.CpuBoost.Dc);
        Assert.True(defaulted.Performance.Tdp!.Enabled);
        Assert.Equal(Pair(20, 22), defaulted.Performance.Tdp.Ac);
        Assert.Equal(Pair(20, 22), defaulted.Performance.Tdp.Dc);
        Assert.Null(defaulted.Performance.PowerMode);
        Assert.Null(defaulted.Performance.FpsLimit);
    }

    [Fact]
    public void Capture_completes_defaults_in_memory_without_persisting_an_entry()
    {
        var store = new ProfileStore(PathName);
        var mutations = new XboxGameProfileMutations(store, new ProfileMutationGate());

        var capture = mutations.CaptureProfile(StoreKey);

        Assert.False(capture.Exists);
        Assert.True(capture.PersistenceWritable);
        Assert.Equal(CpuBoostMode.Enabled, capture.Profile.Performance.CpuBoost!.Ac);
        Assert.Equal(Pair(20, 22), capture.Profile.Performance.Tdp!.Ac);
        Assert.Null(capture.Profile.Performance.FpsLimit);
        Assert.False(File.Exists(PathName));
    }

    [Fact]
    public void Disable_and_reenable_preserve_values_and_display_name_never_changes_identity()
    {
        var store = new ProfileStore(PathName);
        var mutations = new XboxGameProfileMutations(store, new ProfileMutationGate(), Model());
        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, mutations.SetEnabled(StoreKey, true, "First name"));
        mutations.SetCpuBoostAc(StoreKey, CpuBoostMode.Aggressive);
        mutations.SetCpuBoostDc(StoreKey, CpuBoostMode.Disabled);
        mutations.SetTdp(StoreKey, Pair(25, 32), Pair(16, 25));
        mutations.SetResolution(StoreKey, new GameDisplayResolution { Width = 1440, Height = 900 }, "First name");

        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, mutations.SetEnabled(StoreKey, false, "Ignored while disabling"));
        var disabled = store.Load().Document.XboxGames[StoreKey];
        Assert.False(disabled.Enabled);
        Assert.Equal("First name", disabled.DisplayName);
        Assert.Equal(CpuBoostMode.Aggressive, disabled.Performance.CpuBoost!.Ac);
        Assert.Equal(Pair(25, 32), disabled.Performance.Tdp!.Ac);
        Assert.Equal(1440, disabled.Display.Resolution!.Width);

        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, mutations.SetEnabled(StoreKey, true, "Updated name"));
        var reenabled = store.Load().Document.XboxGames[StoreKey];
        Assert.True(reenabled.Enabled);
        Assert.Equal("Updated name", reenabled.DisplayName);
        Assert.Equal(CpuBoostMode.Aggressive, reenabled.Performance.CpuBoost!.Ac);
        Assert.Equal(CpuBoostMode.Disabled, reenabled.Performance.CpuBoost.Dc);
        Assert.Equal(Pair(25, 32), reenabled.Performance.Tdp!.Ac);
        Assert.Equal([StoreKey], store.Load().Document.XboxGames.Keys);
    }

    [Fact]
    public void Favorite_on_creates_disabled_entry_off_on_absent_is_noop_and_existing_values_survive()
    {
        var store = new ProfileStore(PathName);
        var mutations = new XboxGameProfileMutations(store, new ProfileMutationGate());

        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, mutations.SetFavorite(StoreKey, false, "Game"));
        Assert.False(File.Exists(PathName));
        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, mutations.SetFavorite(StoreKey, true, "Game"));
        var favoriteOnly = store.Load().Document.XboxGames[StoreKey];
        Assert.True(favoriteOnly.Favorite);
        Assert.False(favoriteOnly.Enabled);
        Assert.Null(favoriteOnly.Performance.CpuBoost);
        Assert.Null(favoriteOnly.Performance.Tdp);

        var extended = favoriteOnly with
        {
            Performance = new GamePerformanceOverrides { CpuBoost = new() { Ac = CpuBoostMode.Aggressive, Dc = CpuBoostMode.Enabled } },
            Display = new GameDisplayOverrides { Resolution = new() { Width = 1920, Height = 1200 } },
            ExtensionData = new() { ["futureProfile"] = JsonDocument.Parse("true").RootElement.Clone() }
        };
        store.Save(store.Load().Document with { XboxGames = new Dictionary<string, XboxGameProfile> { [StoreKey] = extended } });

        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, mutations.SetFavorite(StoreKey, false, "Renamed"));
        var updated = store.Load().Document.XboxGames[StoreKey];
        Assert.False(updated.Favorite);
        Assert.Equal("Renamed", updated.DisplayName);
        Assert.Equal(CpuBoostMode.Aggressive, updated.Performance.CpuBoost!.Ac);
        Assert.Equal(1920, updated.Display.Resolution!.Width);
        Assert.True(updated.ExtensionData!.ContainsKey("futureProfile"));
        Assert.Contains(StoreKey, store.Load().Document.XboxGames.Keys);
    }

    [Fact]
    public void Cpu_boost_side_and_enabled_changes_preserve_other_values()
    {
        var store = new ProfileStore(PathName);
        var mutations = new XboxGameProfileMutations(store, new ProfileMutationGate());
        mutations.SetEnabled(StoreKey, true, "Game");

        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, mutations.SetCpuBoostAc(StoreKey, CpuBoostMode.Aggressive));
        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, mutations.SetCpuBoostDc(StoreKey, CpuBoostMode.Disabled));
        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, mutations.SetCpuBoostEnabled(StoreKey, false));
        var saved = store.Load().Document.XboxGames[StoreKey].Performance.CpuBoost!;
        Assert.False(saved.Enabled);
        Assert.Equal(CpuBoostMode.Aggressive, saved.Ac);
        Assert.Equal(CpuBoostMode.Disabled, saved.Dc);
    }

    [Fact]
    public void Tdp_updates_validate_model_and_range_and_feature_toggle_preserves_pair()
    {
        var store = new ProfileStore(PathName);
        var noModel = new XboxGameProfileMutations(store, new ProfileMutationGate());
        Assert.Equal(XboxGameProfileMutations.MutationOutcome.InvalidTarget, noModel.SetTdp(StoreKey, Pair(25, 30), Pair(15, 20)));
        Assert.False(File.Exists(PathName));

        var mutations = new XboxGameProfileMutations(store, new ProfileMutationGate(), Model());
        mutations.SetEnabled(StoreKey, true, "Game");
        var beforeInvalid = File.ReadAllText(PathName);
        Assert.Equal(XboxGameProfileMutations.MutationOutcome.InvalidTarget, mutations.SetTdp(StoreKey, Pair(7, 30), Pair(15, 20)));
        Assert.Equal(beforeInvalid, File.ReadAllText(PathName));

        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, mutations.SetTdp(StoreKey, Pair(25, 30), Pair(15, 20)));
        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, mutations.SetTdpEnabled(StoreKey, false));
        var tdp = store.Load().Document.XboxGames[StoreKey].Performance.Tdp!;
        Assert.False(tdp.Enabled);
        Assert.Equal(Pair(25, 30), tdp.Ac);
        Assert.Equal(Pair(15, 20), tdp.Dc);
    }

    [Fact]
    public void Power_mode_copies_device_values_and_ac_dc_mutations_preserve_the_other_side()
    {
        var store = new ProfileStore(PathName);
        store.Save(new ProfileDocument { Device = new() { Performance = new() { PowerMode = new() { Ac = WindowsPowerMode.Balanced, Dc = WindowsPowerMode.BestPowerEfficiency } } } });
        var mutations = new XboxGameProfileMutations(store, new ProfileMutationGate());
        mutations.SetEnabled(StoreKey, true, "Game");

        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, mutations.SetPowerModeAc(StoreKey, WindowsPowerMode.BestPerformance));
        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, mutations.SetPowerModeEnabled(StoreKey, false));
        var saved = store.Load().Document.XboxGames[StoreKey].Performance.PowerMode!;
        Assert.False(saved.Enabled);
        Assert.Equal(WindowsPowerMode.BestPerformance, saved.Ac);
        Assert.Equal(WindowsPowerMode.BestPowerEfficiency, saved.Dc);

        var absentStore = new ProfileStore(Path.Combine(_directory, "no-power", "profiles.json"));
        var absent = new XboxGameProfileMutations(absentStore, new ProfileMutationGate());
        absent.SetEnabled(StoreKey, true, "Game");
        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Unavailable, absent.SetPowerModeAc(StoreKey, WindowsPowerMode.Balanced));
        Assert.Null(absentStore.Load().Document.XboxGames[StoreKey].Performance.PowerMode);
    }

    [Fact]
    public void Fps_mutations_create_sixty_sixty_defaults_and_reject_out_of_range_values()
    {
        var store = new ProfileStore(PathName);
        var mutations = new XboxGameProfileMutations(store, new ProfileMutationGate());
        mutations.SetEnabled(StoreKey, true, "Game");

        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, mutations.SetFpsLimitEnabled(StoreKey, true));
        var initial = store.Load().Document.XboxGames[StoreKey].Performance.FpsLimit!;
        Assert.True(initial.Enabled);
        Assert.Equal(60, initial.AcFps);
        Assert.Equal(60, initial.DcFps);
        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, mutations.SetFpsLimitAc(StoreKey, 90));
        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, mutations.SetFpsLimitDc(StoreKey, 45));
        var saved = store.Load().Document.XboxGames[StoreKey].Performance.FpsLimit!;
        Assert.Equal(90, saved.AcFps);
        Assert.Equal(45, saved.DcFps);
        var beforeInvalid = File.ReadAllText(PathName);
        Assert.Equal(XboxGameProfileMutations.MutationOutcome.InvalidTarget, mutations.SetFpsLimitAc(StoreKey, 39));
        Assert.Equal(XboxGameProfileMutations.MutationOutcome.InvalidTarget, mutations.SetFpsLimitDc(StoreKey, 121));
        Assert.Equal(beforeInvalid, File.ReadAllText(PathName));
    }

    [Fact]
    public void Resolution_create_clear_and_extension_data_follow_offline_semantics()
    {
        var store = new ProfileStore(PathName);
        var mutations = new XboxGameProfileMutations(store, new ProfileMutationGate());

        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, mutations.SetResolution(StoreKey, null, "Game"));
        Assert.False(File.Exists(PathName));
        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, mutations.SetResolution(StoreKey, new() { Width = 1440, Height = 900 }, "Game"));
        var created = store.Load().Document.XboxGames[StoreKey];
        Assert.False(created.Enabled);
        Assert.Equal(1440, created.Display.Resolution!.Width);

        var extended = created with { ExtensionData = new() { ["futureProfile"] = JsonDocument.Parse("42").RootElement.Clone() } };
        store.Save(store.Load().Document with { XboxGames = new Dictionary<string, XboxGameProfile> { [StoreKey] = extended } });
        Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, mutations.SetResolution(StoreKey, null, null));
        var cleared = store.Load().Document.XboxGames[StoreKey];
        Assert.Null(cleared.Display.Resolution);
        Assert.True(cleared.ExtensionData!.ContainsKey("futureProfile"));
        Assert.False(cleared.Enabled);
    }

    [Fact]
    public void Unsafe_profile_document_is_never_replaced_by_an_Xbox_mutation()
    {
        Directory.CreateDirectory(_directory);
        const string original = "{ invalid profile document";
        File.WriteAllText(PathName, original);
        var mutations = new XboxGameProfileMutations(new ProfileStore(PathName), new ProfileMutationGate());

        Assert.Equal(XboxGameProfileMutations.MutationOutcome.PersistenceFailed, mutations.SetFavorite(StoreKey, true, "Game"));
        Assert.Equal(original, File.ReadAllText(PathName));
        Assert.False(mutations.CaptureProfile(StoreKey).PersistenceWritable);
    }

    [Fact]
    public async Task Steam_and_Xbox_mutations_share_one_gate_and_preserve_both_collections()
    {
        var store = new ProfileStore(PathName);
        var gate = new ProfileMutationGate();
        var steam = new GameProfileMutations(store, gate);
        var xbox = new XboxGameProfileMutations(store, gate);
        var work = Enumerable.Range(0, 20).SelectMany(index => new Task[]
        {
            Task.Run(() => steam.SetFavorite((uint)(1000 + index), true, $"Steam {index}")),
            Task.Run(() => xbox.SetFavorite($"store:game-{index}", true, $"Xbox {index}"))
        });

        await Task.WhenAll(work);

        var saved = store.Load().Document;
        Assert.Equal(20, saved.Games.Count);
        Assert.Equal(20, saved.XboxGames.Count);
        Assert.True(saved.Games["1000"].Favorite);
        Assert.True(saved.XboxGames["store:game-0"].Favorite);
    }

    private static HandheldDeviceModelId Model() => new("msi.claw.a2vm.7");
    private static TdpPowerPair Pair(int pl1, int pl2) => new() { Pl1Watts = pl1, Pl2Watts = pl2 };

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
