using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Hosting;
using SteamInputAddonforClaw.Profiles;
using SteamInputAddonforClaw.Profiles.Performance;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class IntelGpuMinimumClockTests
{
    private static readonly double[] SelectableClocks = [1500, 1600, 1700, 1800, 1900, 2000, 2100];

    [Fact]
    public void Available_clocks_are_sanitized_and_bounds_use_actual_driver_entries()
    {
        var selection = IntelGpuMinimumClockPolicy.SelectAvailableClocks(
            [2300, 2000, double.NaN, 0, 2100, 2100.05, -1, 2250, 1500]);

        Assert.True(selection.Available);
        Assert.Equal(new[] { 1500d, 2000d, 2100d, 2250d, 2300d }, selection.AvailableClocksMhz);
        Assert.Equal(new[] { 1500d, 2000d, 2100d }, selection.SelectableClocksMhz);
        Assert.Equal(1500, selection.SelectableMinMhz);
        Assert.Equal(2100, selection.SelectableMaxMhz);
        Assert.Equal(selection.SelectableMaxMhz, selection.RecommendedDefaultMhz);
    }

    [Fact]
    public void Different_integrated_gpu_tables_produce_dynamic_bounds()
    {
        var b390 = IntelGpuMinimumClockPolicy.SelectAvailableClocks([900, 1500, 1600, 1700, 1800, 1900, 2000, 2100, 2200, 2250, 2300]);
        var arc140V = IntelGpuMinimumClockPolicy.SelectAvailableClocks([500, 800, 1300, 1525, 1625, 1725, 1825, 1925, 1975, 2025]);

        Assert.True(b390.Available);
        Assert.Equal(2200, b390.RecommendedDefaultMhz);
        Assert.True(arc140V.Available);
        Assert.Equal(1525, arc140V.SelectableMinMhz);
        Assert.Equal(1925, arc140V.RecommendedDefaultMhz);
    }

    [Fact]
    public void Invalid_or_insufficient_tables_fail_closed()
    {
        Assert.False(IntelGpuMinimumClockPolicy.SelectAvailableClocks([1500, 1600]).Available);
        Assert.False(IntelGpuMinimumClockPolicy.SelectAvailableClocks([1000, 1400, 1450, 1475]).Available);
        Assert.False(IntelGpuMinimumClockPolicy.SelectAvailableClocks([double.NaN, double.PositiveInfinity, 0, -10]).Available);
    }

    [Fact]
    public void Minimum_request_preserves_explicit_max_and_factory_release_always_uses_minus_one()
    {
        Assert.True(IntelGpuMinimumClockPolicy.TryCreateMinimumRequest(
            SelectableClocks, 1800, new(-1, 1950), out var target, out var applyRequest, out _));
        Assert.Equal(1800, target);
        Assert.Equal(new IntelGpuFrequencyRange(1800, 1950), applyRequest);

        Assert.False(IntelGpuMinimumClockPolicy.TryCreateMinimumRequest(
            SelectableClocks, 1800, new(-1, 1750), out _, out _, out _));

        Assert.True(IntelGpuMinimumClockPolicy.TryCreateFactoryMinimumReleaseRequest(new(1900, 1950), out var explicitMax));
        Assert.Equal(new IntelGpuFrequencyRange(-1, 1950), explicitMax);
        Assert.True(IntelGpuMinimumClockPolicy.TryCreateFactoryMinimumReleaseRequest(new(1900, -2), out var unspecifiedMax));
        Assert.Equal(new IntelGpuFrequencyRange(-1, -1), unspecifiedMax);
        Assert.False(IntelGpuMinimumClockPolicy.TryCreateFactoryMinimumReleaseRequest(new(double.NaN, 1950), out _));
    }

    [Fact]
    public void Readback_verifies_factory_minimum_and_preserved_max_semantics()
    {
        Assert.True(IntelGpuMinimumClockPolicy.MatchesReadback(new(-1, 1950), new(1900, 1950), new(-2, 1949.95)));
        Assert.False(IntelGpuMinimumClockPolicy.MatchesReadback(new(-1, 1950), new(1900, 1950), new(1500, 1950)));
        Assert.True(IntelGpuMinimumClockPolicy.MatchesReadback(new(-1, -1), new(1900, -2), new(-3, -4)));
        Assert.False(IntelGpuMinimumClockPolicy.MatchesReadback(new(-1, 1950), new(1900, 1950), new(-1, 1800)));
    }

    [Fact]
    public void Startup_without_an_enabled_active_game_releases_minimum_and_does_not_create_profile_file()
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeControl(new(1850, 2100));
        using var runtime = CreateRuntime(temp, fake, () => AcDcPowerSource.AC);

        runtime.StartupReconcile();

        Assert.Equal(1, fake.InitializeCalls);
        Assert.Equal(1, fake.SetCalls);
        Assert.Equal(new IntelGpuFrequencyRange(-1, 2100), fake.LastSetRange);
        Assert.False(File.Exists(temp.ProfilesPath));
    }

    [Fact]
    public void Legacy_device_gpu_json_does_not_become_a_runtime_floor()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.GetDirectoryName(temp.ProfilesPath)!);
        File.WriteAllText(temp.ProfilesPath,
            "{\"schemaVersion\":1,\"device\":{\"performance\":{\"gpuMinimumClock\":{\"enabled\":true,\"acMhz\":2200,\"dcMhz\":2100}}},\"games\":{}}");
        var fake = new FakeControl(new(2200, 2300));
        using var runtime = CreateRuntime(temp, fake, () => AcDcPowerSource.AC);

        runtime.StartupReconcile();

        Assert.Equal(new IntelGpuFrequencyRange(-1, 2300), fake.LastSetRange);
    }

    [Theory]
    [InlineData(true, 1700)]
    [InlineData(false, 1600)]
    public void Enabled_active_Steam_game_uses_its_current_power_rail(bool ac, double expectedMinimum)
    {
        using var temp = new TemporaryDirectory();
        var store = new ProfileStore(temp.ProfilesPath);
        store.Save(SteamDocument(enabled: true, gpuEnabled: true, acMhz: 1700, dcMhz: 1600));
        var fake = new FakeControl(new(900, 2100));
        using var runtime = CreateRuntime(temp, fake, () => ac ? AcDcPowerSource.AC : AcDcPowerSource.DC);
        runtime.SetActiveProfileResolver(document => ActiveProfileResolver.Resolve(ActiveProfileTarget.ForSteam(42), document));

        runtime.StartupReconcile();

        Assert.Equal(new IntelGpuFrequencyRange(expectedMinimum, 2100), fake.LastSetRange);
        Assert.Equal(1, fake.SetCalls);
    }

    [Fact]
    public void Enabled_active_Xbox_game_uses_its_saved_override()
    {
        using var temp = new TemporaryDirectory();
        var store = new ProfileStore(temp.ProfilesPath);
        store.Save(new ProfileDocument
        {
            XboxGames = new Dictionary<string, XboxGameProfile>
            {
                ["store:test"] = new()
                {
                    Enabled = true,
                    Performance = CreateGamePerformance(Gpu(enabled: true, acMhz: 1800, dcMhz: 1700))
                }
            }
        });
        var fake = new FakeControl(new(900, 2100));
        using var runtime = CreateRuntime(temp, fake, () => AcDcPowerSource.AC);
        runtime.SetActiveProfileResolver(document => ActiveProfileResolver.Resolve(ActiveProfileTarget.ForXbox("store:test"), document));

        runtime.StartupReconcile();

        Assert.Equal(new IntelGpuFrequencyRange(1800, 2100), fake.LastSetRange);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void Disabled_game_or_disabled_game_override_releases_to_factory(bool profileEnabled, bool gpuEnabled)
    {
        using var temp = new TemporaryDirectory();
        new ProfileStore(temp.ProfilesPath).Save(SteamDocument(profileEnabled, gpuEnabled, 1800, 1700));
        var fake = new FakeControl(new(1800, 2050));
        using var runtime = CreateRuntime(temp, fake, () => AcDcPowerSource.AC);
        runtime.SetActiveProfileResolver(document => ActiveProfileResolver.Resolve(ActiveProfileTarget.ForSteam(42), document));

        runtime.StartupReconcile();

        Assert.Equal(new IntelGpuFrequencyRange(-1, 2050), fake.LastSetRange);
        Assert.Equal(1, fake.SetCalls);
    }

    [Fact]
    public void Game_exit_releases_to_factory_instead_of_using_a_device_fallback()
    {
        using var temp = new TemporaryDirectory();
        new ProfileStore(temp.ProfilesPath).Save(SteamDocument(enabled: true, gpuEnabled: true, 1800, 1700));
        var fake = new FakeControl(new(900, 2000));
        ActiveProfileTarget? target = ActiveProfileTarget.ForSteam(42);
        using var runtime = CreateRuntime(temp, fake, () => AcDcPowerSource.AC);
        runtime.SetActiveProfileResolver(document => target is { } active ? ActiveProfileResolver.Resolve(active, document) : null);

        Assert.True(runtime.ReconcileEffective("GameStart").Succeeded);
        Assert.Equal(1800, fake.LastSetRange!.Value.Min);
        target = null;

        Assert.True(runtime.ReconcileEffective("GameExit").Succeeded);
        Assert.Equal(new IntelGpuFrequencyRange(-1, 2000), fake.LastSetRange);
        Assert.Equal(2, fake.SetCalls);
    }

    [Fact]
    public void Game_A_to_game_B_applies_B_then_B_off_releases_to_factory()
    {
        using var temp = new TemporaryDirectory();
        var store = new ProfileStore(temp.ProfilesPath);
        store.Save(new ProfileDocument
        {
            Games = new Dictionary<string, GameProfile>
            {
                ["42"] = SteamDocument(true, true, 1700, 1600).Games["42"],
                ["43"] = SteamDocument(true, true, 1800, 1750).Games["42"]
            }
        });
        ActiveProfileTarget? target = ActiveProfileTarget.ForSteam(42);
        var fake = new FakeControl(new(900, 2100));
        using var runtime = CreateRuntime(temp, fake, () => AcDcPowerSource.AC);
        runtime.SetActiveProfileResolver(document => target is { } active ? ActiveProfileResolver.Resolve(active, document) : null);

        Assert.True(runtime.ReconcileEffective("GameAStart").Succeeded);
        Assert.Equal(1700, fake.LastSetRange!.Value.Min);
        target = ActiveProfileTarget.ForSteam(43);
        Assert.True(runtime.ReconcileEffective("GameAtoB").Succeeded);
        Assert.Equal(1800, fake.LastSetRange!.Value.Min);

        var updated = store.Load().Document;
        var games = new Dictionary<string, GameProfile>(updated.Games)
        {
            ["43"] = updated.Games["43"] with { Enabled = false }
        };
        store.Save(updated with { Games = games });
        Assert.True(runtime.ReconcileEffective("GameBDisabled").Succeeded);
        Assert.Equal(new IntelGpuFrequencyRange(-1, 2100), fake.LastSetRange);
    }

    [Fact]
    public void Active_game_without_gpu_feature_releases_to_factory()
    {
        using var temp = new TemporaryDirectory();
        var document = SteamDocument(enabled: true, gpuEnabled: true, 1800, 1700);
        var game = document.Games["42"];
        var games = new Dictionary<string, GameProfile>(document.Games)
        {
            ["42"] = game with { Performance = game.Performance with { GpuMinimumClock = null } }
        };
        document = document with
        {
            Games = games
        };
        new ProfileStore(temp.ProfilesPath).Save(document);
        var fake = new FakeControl(new(1800, 2050));
        using var runtime = CreateRuntime(temp, fake, () => AcDcPowerSource.AC);
        runtime.SetActiveProfileResolver(value => ActiveProfileResolver.Resolve(ActiveProfileTarget.ForSteam(42), value));

        runtime.StartupReconcile();

        Assert.Equal(new IntelGpuFrequencyRange(-1, 2050), fake.LastSetRange);
    }

    [Fact]
    public void Unsupported_saved_game_target_is_not_clamped_and_factory_release_is_attempted()
    {
        using var temp = new TemporaryDirectory();
        var store = new ProfileStore(temp.ProfilesPath);
        store.Save(SteamDocument(enabled: true, gpuEnabled: true, acMhz: 1777, dcMhz: 1700));
        var fake = new FakeControl(new(1800, 2000));
        using var runtime = CreateRuntime(temp, fake, () => AcDcPowerSource.AC);
        runtime.SetActiveProfileResolver(document => ActiveProfileResolver.Resolve(ActiveProfileTarget.ForSteam(42), document));

        var result = runtime.ReconcileEffective("Startup");

        Assert.False(result.Succeeded);
        Assert.Equal("SavedTargetUnsupportedByCurrentDriver", result.FailureReason);
        Assert.Equal(new IntelGpuFrequencyRange(-1, 2000), fake.LastSetRange);
        Assert.Equal(1777, store.Load().Document.Games["42"].Performance.GpuMinimumClock!.AcMhz);
    }

    [Fact]
    public void Unknown_power_source_attempts_factory_release_and_reports_failure()
    {
        using var temp = new TemporaryDirectory();
        new ProfileStore(temp.ProfilesPath).Save(SteamDocument(enabled: true, gpuEnabled: true, 1800, 1700));
        var fake = new FakeControl(new(1800, 2050));
        using var runtime = CreateRuntime(temp, fake, () => null);
        runtime.SetActiveProfileResolver(document => ActiveProfileResolver.Resolve(ActiveProfileTarget.ForSteam(42), document));

        var result = runtime.ReconcileEffective("PowerSourceChanged");

        Assert.False(result.Succeeded);
        Assert.Contains("PowerSourceUnknown", result.FailureReason, StringComparison.Ordinal);
        Assert.Equal(new IntelGpuFrequencyRange(-1, 2050), fake.LastSetRange);
    }

    [Fact]
    public void Factory_release_readback_mismatch_is_a_failure()
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeControl(new(1800, 2050))
        {
            SetRangeReadback = requested => requested with { Min = 1500 }
        };
        using var runtime = CreateRuntime(temp, fake, () => AcDcPowerSource.AC);
        runtime.InitializeReadOnly();

        var result = runtime.ReleaseToFactoryMinimum("UnitTest");

        Assert.False(result.Succeeded);
        Assert.False(result.Verified);
        Assert.Equal(1500, result.ReadbackRange!.Value.Min);
        Assert.Equal(new IntelGpuFrequencyRange(-1, 2050), fake.LastSetRange);
    }

    [Fact]
    public void Uninstall_always_attempts_and_verifies_factory_release()
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeControl(new(1700, 2050));
        using var runtime = CreateRuntime(temp, fake, () => AcDcPowerSource.AC);
        runtime.InitializeReadOnly();

        var result = runtime.PrepareForUninstall();

        Assert.True(result.Succeeded);
        Assert.True(result.Verified);
        Assert.Equal(1, fake.SetCalls);
        Assert.Equal(new IntelGpuFrequencyRange(-1, 2050), fake.LastSetRange);
    }

    [Fact]
    public void Uninstall_fails_closed_when_factory_release_cannot_be_verified()
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeControl(new(1700, 2050)) { SetResult = 0x40000001 };
        using var runtime = CreateRuntime(temp, fake, () => AcDcPowerSource.AC);
        runtime.InitializeReadOnly();

        var result = runtime.PrepareForUninstall();

        Assert.False(result.Succeeded);
        Assert.Equal(0x40000001u, result.NativeSetResult);
        Assert.Equal(new IntelGpuFrequencyRange(-1, 2050), fake.LastSetRange);
    }

    [Fact]
    public void Uninstall_fails_closed_when_readback_keeps_an_external_minimum()
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeControl(new(1700, 2050))
        {
            SetRangeReadback = requested => requested with { Min = 0 }
        };
        using var runtime = CreateRuntime(temp, fake, () => AcDcPowerSource.AC);
        runtime.InitializeReadOnly();

        var result = runtime.PrepareForUninstall();

        Assert.False(result.Succeeded);
        Assert.False(result.Verified);
        Assert.Equal(0, result.ReadbackRange!.Value.Min);
        Assert.Equal(1, fake.SetCalls);
    }

    [Fact]
    public void Uninstall_fails_when_capability_stays_unavailable_after_one_reinitialize()
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeControl(new(1700, 2050))
        {
            Capability = new(false, "Unavailable", "", 0, 0, false, 0, 0, Array.Empty<double>())
        };
        using var runtime = CreateRuntime(temp, fake, () => AcDcPowerSource.AC);

        var result = runtime.PrepareForUninstall();

        Assert.False(result.Succeeded);
        Assert.Equal(1, fake.ReinitializeCalls);
        Assert.Equal(0, fake.SetCalls);
    }

    [Fact]
    public async Task Host_startup_reconciles_no_active_game_to_factory_minimum()
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeControl(new(1850, 2100));
        await using var host = new AddonProcessHost(
            testOnlyDataRoot: temp.Root,
            testIntelGpuMinimumClockControlFactory: () => fake,
            testGpuMinimumClockPowerSource: () => AcDcPowerSource.AC);

        host.ReconcileIntelGpuMinimumClockForStartup();

        Assert.Equal(1, fake.InitializeCalls);
        Assert.Equal(new IntelGpuFrequencyRange(-1, 2100), fake.LastSetRange);
    }

    [Fact]
    public async Task Host_uninstall_stops_before_Full1902_when_factory_release_fails()
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeControl(new(1850, 2100)) { SetResult = 0x40000001 };
        await using var host = new AddonProcessHost(
            testOnlyDataRoot: temp.Root,
            testIntelGpuMinimumClockControlFactory: () => fake,
            testGpuMinimumClockPowerSource: () => AcDcPowerSource.AC);

        var result = await host.PrepareForUninstallAsync();

        Assert.False(result.Succeeded);
        Assert.StartsWith("GpuMinimumClockReleaseFailed:", result.Reason, StringComparison.Ordinal);
        Assert.Equal(1, fake.SetCalls);
    }

    [Fact]
    public void Resume_reinitializes_once_for_device_unavailable_then_reconciles_game_override()
    {
        using var temp = new TemporaryDirectory();
        new ProfileStore(temp.ProfilesPath).Save(SteamDocument(enabled: true, gpuEnabled: true, 1800, 1700));
        var fake = new FakeControl(new(900, 2050));
        fake.SetResults.Enqueue(IntelGpuMinimumClockRuntime.DeviceUnavailableResult);
        fake.SetResults.Enqueue(0);
        using var runtime = CreateRuntime(temp, fake, () => AcDcPowerSource.AC);
        runtime.SetActiveProfileResolver(document => ActiveProfileResolver.Resolve(ActiveProfileTarget.ForSteam(42), document));
        runtime.InitializeReadOnly();

        var result = runtime.ReconcileAfterResume();

        Assert.True(result.Succeeded);
        Assert.Equal(1, fake.ReinitializeCalls);
        Assert.Equal(2, fake.SetCalls);
        Assert.Equal(new IntelGpuFrequencyRange(1800, 2050), fake.LastSetRange);
    }

    [Fact]
    public void Developer_frequency_mutation_is_blocked_only_by_enabled_game_profile_ownership()
    {
        using var temp = new TemporaryDirectory();
        var store = new ProfileStore(temp.ProfilesPath);
        store.Save(SteamDocument(enabled: true, gpuEnabled: true, 1800, 1700));
        using var runtime = CreateRuntime(temp, new FakeControl(new(-1, -1)), () => AcDcPowerSource.AC);

        Assert.True(runtime.BlocksDeveloperFrequencyMutation());

        store.Save(SteamDocument(enabled: true, gpuEnabled: false, 1800, 1700));
        Assert.False(runtime.BlocksDeveloperFrequencyMutation());
    }

    [Fact]
    public void Normal_runtime_dispose_does_not_issue_a_separate_factory_release()
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeControl(new(1800, 2050));
        var runtime = CreateRuntime(temp, fake, () => AcDcPowerSource.AC);
        runtime.InitializeReadOnly();

        runtime.Dispose();

        Assert.Equal(0, fake.SetCalls);
    }

    [Fact]
    public void Session_reinitialize_is_limited_to_device_loss_results()
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeControl(new(-1, -1));
        using var runtime = CreateRuntime(temp, fake, () => AcDcPowerSource.AC);
        runtime.InitializeReadOnly();

        Assert.False(runtime.TryReinitializeSession(0x40000001));
        Assert.Equal(0, fake.ReinitializeCalls);
        Assert.True(runtime.TryReinitializeSession(IntelGpuMinimumClockRuntime.DeviceLostResult));
        Assert.Equal(1, fake.ReinitializeCalls);
    }

    [Fact]
    public void Production_igcl_frequency_abi_matches_the_expected_x64_layout()
        => Assert.True(IntelGpuMinimumClockControl.NativeAbiIsExpectedForTests());

    private static IntelGpuMinimumClockRuntime CreateRuntime(
        TemporaryDirectory temp,
        FakeControl control,
        Func<AcDcPowerSource?> powerSource) =>
        new(new ProfileStore(temp.ProfilesPath), new ProfileMutationGate(), control, powerSource);

    private static ProfileDocument SteamDocument(bool enabled, bool gpuEnabled, double acMhz, double dcMhz) => new()
    {
        Games = new Dictionary<string, GameProfile>
        {
            ["42"] = new()
            {
                Enabled = enabled,
                Performance = CreateGamePerformance(Gpu(gpuEnabled, acMhz, dcMhz))
            }
        }
    };

    private static GamePerformanceOverrides CreateGamePerformance(GameGpuMinimumClockSettings gpu) => new()
    {
        CpuBoost = new() { Enabled = true, Ac = CpuBoostMode.Enabled, Dc = CpuBoostMode.Enabled },
        Tdp = new()
        {
            Enabled = true,
            Ac = Pair(20, 22),
            Dc = Pair(20, 22)
        },
        GpuMinimumClock = gpu
    };

    private static TdpPowerPair Pair(int pl1, int pl2) => new() { Pl1Watts = pl1, Pl2Watts = pl2 };

    private static GameGpuMinimumClockSettings Gpu(bool enabled, double acMhz, double dcMhz) => new()
    {
        Enabled = enabled,
        AcMhz = acMhz,
        DcMhz = dcMhz
    };

    private static IntelGpuMinimumClockNativeCapability CreateNativeCapability() => new(
        true, null, "Intel Integrated GPU", 0x8086, 0x1234, true, 300, 2300, SelectableClocks);

    private sealed class FakeControl(IntelGpuFrequencyRange currentRange) : IIntelGpuMinimumClockControl
    {
        private IntelGpuFrequencyRange _currentRange = currentRange;

        public IntelGpuMinimumClockNativeCapability Capability { get; set; } = CreateNativeCapability();
        public Queue<uint> SetResults { get; } = new();
        public int InitializeCalls { get; private set; }
        public int ReinitializeCalls { get; private set; }
        public int SetCalls { get; private set; }
        public uint SetResult { get; set; }
        public IntelGpuFrequencyRange? LastSetRange { get; private set; }
        public Func<IntelGpuFrequencyRange, IntelGpuFrequencyRange>? SetRangeReadback { get; set; }

        public IntelGpuMinimumClockNativeCapability Initialize()
        {
            InitializeCalls++;
            return Capability;
        }

        public IntelGpuMinimumClockNativeCapability Reinitialize()
        {
            ReinitializeCalls++;
            return Capability;
        }

        public IntelGpuFrequencyRange GetRange() => _currentRange;

        public uint SetRange(IntelGpuFrequencyRange range)
        {
            SetCalls++;
            LastSetRange = range;
            var result = SetResults.Count == 0 ? SetResult : SetResults.Dequeue();
            if (result == 0) _currentRange = SetRangeReadback?.Invoke(range) ?? range;
            return result;
        }

        public void Dispose() { }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"IntelGpuMinimumClock-{Guid.NewGuid():N}");
        internal string Root => _root;
        internal string ProfilesPath => Path.Combine(_root, "profiles.json");

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
