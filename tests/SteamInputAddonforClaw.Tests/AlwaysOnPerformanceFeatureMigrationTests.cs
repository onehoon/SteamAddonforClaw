using System.Text.Json;
using System.Text.Json.Nodes;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Profiles;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AlwaysOnPerformanceFeatureMigrationTests
{
    [Fact]
    public void Migrates_only_existing_feature_flags_and_preserves_values_profiles_and_extension_data()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileStore(Path.Combine(directory.Path, "profiles.json"));
        var document = BuildDocument();
        store.Save(document);

        var result = AlwaysOnPerformanceFeatureMigration.Run(store, new ProfileMutationGate());

        Assert.Equal(AlwaysOnPerformanceFeatureMigrationStatus.Changed, result.Status);
        var migrated = store.Load().Document;
        var migratedDevicePerformance = migrated.Device.Performance;
        var migratedDeviceTdp = migratedDevicePerformance.Tdp!;
        Assert.True(migratedDeviceTdp.Enabled);
        AssertPairEqual(document.Device.Performance.Tdp!.Ac, migratedDeviceTdp.Ac);
        AssertPairEqual(document.Device.Performance.Tdp.Dc, migratedDeviceTdp.Dc);
        Assert.True(migratedDevicePerformance.CpuBoost!.Enabled);
        Assert.Equal(CpuBoostMode.Disabled, migratedDevicePerformance.CpuBoost.Ac);
        Assert.Equal(CpuBoostMode.EfficientAggressiveAtGuaranteed, migratedDevicePerformance.CpuBoost.Dc);
        Assert.True(migratedDevicePerformance.PowerMode!.Enabled);
        Assert.Equal(WindowsPowerMode.BestPowerEfficiency, migratedDevicePerformance.PowerMode.Ac);
        Assert.Equal(WindowsPowerMode.BestPerformance, migratedDevicePerformance.PowerMode.Dc);
        Assert.Equal(document.Device.Performance.MaximumProcessorState, migrated.Device.Performance.MaximumProcessorState);
        Assert.Equal(document.Device.Battery, migrated.Device.Battery);

        Assert.Equal(document.Games.Count, migrated.Games.Count);
        foreach (var (key, profile) in migrated.Games)
        {
            Assert.Equal(document.Games[key].Enabled, profile.Enabled);
            Assert.Equal(document.Games[key].Favorite, profile.Favorite);
            var sourcePerformance = document.Games[key].Performance;
            var sourceTdp = sourcePerformance.Tdp!;
            var migratedTdp = profile.Performance.Tdp!;
            Assert.True(migratedTdp.Enabled);
            AssertPairEqual(sourceTdp.Ac, migratedTdp.Ac);
            AssertPairEqual(sourceTdp.Dc, migratedTdp.Dc);
            var migratedCpuBoost = profile.Performance.CpuBoost!;
            var sourceCpuBoost = sourcePerformance.CpuBoost!;
            Assert.True(migratedCpuBoost.Enabled);
            Assert.Equal(sourceCpuBoost.Ac, migratedCpuBoost.Ac);
            Assert.Equal(sourceCpuBoost.Dc, migratedCpuBoost.Dc);
            var migratedPowerMode = profile.Performance.PowerMode!;
            var sourcePowerMode = sourcePerformance.PowerMode!;
            Assert.True(migratedPowerMode.Enabled);
            Assert.Equal(sourcePowerMode.Ac, migratedPowerMode.Ac);
            Assert.Equal(sourcePowerMode.Dc, migratedPowerMode.Dc);
            Assert.Equal(sourcePerformance.FpsLimit, profile.Performance.FpsLimit);
            Assert.Equal(sourcePerformance.GpuMinimumClock, profile.Performance.GpuMinimumClock);
        }

        Assert.Equal(document.XboxGames.Count, migrated.XboxGames.Count);
        foreach (var (key, profile) in migrated.XboxGames)
        {
            Assert.Equal(document.XboxGames[key].Enabled, profile.Enabled);
            Assert.Equal(document.XboxGames[key].Favorite, profile.Favorite);
            var sourcePerformance = document.XboxGames[key].Performance;
            var sourceTdp = sourcePerformance.Tdp!;
            var migratedTdp = profile.Performance.Tdp!;
            Assert.True(migratedTdp.Enabled);
            AssertPairEqual(sourceTdp.Ac, migratedTdp.Ac);
            AssertPairEqual(sourceTdp.Dc, migratedTdp.Dc);
            var migratedCpuBoost = profile.Performance.CpuBoost!;
            var sourceCpuBoost = sourcePerformance.CpuBoost!;
            Assert.True(migratedCpuBoost.Enabled);
            Assert.Equal(sourceCpuBoost.Ac, migratedCpuBoost.Ac);
            Assert.Equal(sourceCpuBoost.Dc, migratedCpuBoost.Dc);
            var migratedPowerMode = profile.Performance.PowerMode!;
            var sourcePowerMode = sourcePerformance.PowerMode!;
            Assert.True(migratedPowerMode.Enabled);
            Assert.Equal(sourcePowerMode.Ac, migratedPowerMode.Ac);
            Assert.Equal(sourcePowerMode.Dc, migratedPowerMode.Dc);
            Assert.Equal(sourcePerformance.FpsLimit, profile.Performance.FpsLimit);
            Assert.Equal(sourcePerformance.GpuMinimumClock, profile.Performance.GpuMinimumClock);
        }

        AssertJsonEqual(document.ExtensionData!["futureRoot"], migrated.ExtensionData!["futureRoot"]);
        AssertJsonEqual(document.Device.ExtensionData!["futureDevice"], migrated.Device.ExtensionData!["futureDevice"]);
        AssertJsonEqual(document.Device.Performance.ExtensionData!["futurePerformance"], migrated.Device.Performance.ExtensionData!["futurePerformance"]);
        AssertJsonEqual(document.Device.Performance.Tdp!.ExtensionData!["futureTdp"], migratedDeviceTdp.ExtensionData!["futureTdp"]);
        AssertJsonEqual(document.Games["101"].ExtensionData!["futureSteam"], migrated.Games["101"].ExtensionData!["futureSteam"]);
        AssertJsonEqual(document.XboxGames["Xbox!Game!Identity"].ExtensionData!["futureXbox"], migrated.XboxGames["Xbox!Game!Identity"].ExtensionData!["futureXbox"]);
    }

    [Fact]
    public void Migration_is_idempotent_and_does_not_create_a_missing_profile_document()
    {
        using var directory = new TemporaryDirectory();
        var missingPath = Path.Combine(directory.Path, "missing", "profiles.json");
        var missingStore = new ProfileStore(missingPath);

        Assert.Equal(AlwaysOnPerformanceFeatureMigrationStatus.Unchanged,
            AlwaysOnPerformanceFeatureMigration.Run(missingStore, new ProfileMutationGate()).Status);
        Assert.False(File.Exists(missingPath));

        var path = Path.Combine(directory.Path, "profiles.json");
        var store = new ProfileStore(path);
        store.Save(BuildDocument());
        Assert.Equal(AlwaysOnPerformanceFeatureMigrationStatus.Changed,
            AlwaysOnPerformanceFeatureMigration.Run(store, new ProfileMutationGate()).Status);
        var secondRunTimestamp = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, secondRunTimestamp);

        Assert.Equal(AlwaysOnPerformanceFeatureMigrationStatus.Unchanged,
            AlwaysOnPerformanceFeatureMigration.Run(store, new ProfileMutationGate()).Status);
        Assert.Equal(secondRunTimestamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Migration_does_not_synthesize_missing_performance_records()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "profiles.json");
        var document = BuildDocument();
        var original = document with
        {
            Device = document.Device with
            {
                Performance = document.Device.Performance with { Tdp = null, CpuBoost = null, PowerMode = null }
            },
            Games = new Dictionary<string, GameProfile>
            {
                ["101"] = new() { Enabled = false, Favorite = true, Performance = new GamePerformanceOverrides() }
            },
            XboxGames = new Dictionary<string, XboxGameProfile>
            {
                ["Xbox!Game!Identity"] = new() { Enabled = false, Favorite = true, Performance = new GamePerformanceOverrides() }
            }
        };
        var store = new ProfileStore(path);
        store.Save(original);
        var timestamp = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, timestamp);

        var result = AlwaysOnPerformanceFeatureMigration.Run(store, new ProfileMutationGate());

        Assert.Equal(AlwaysOnPerformanceFeatureMigrationStatus.Unchanged, result.Status);
        var loaded = store.Load().Document;
        Assert.Null(loaded.Device.Performance.Tdp);
        Assert.Null(loaded.Device.Performance.CpuBoost);
        Assert.Null(loaded.Device.Performance.PowerMode);
        Assert.Null(loaded.Games["101"].Performance.Tdp);
        Assert.Null(loaded.Games["101"].Performance.CpuBoost);
        Assert.Null(loaded.Games["101"].Performance.PowerMode);
        Assert.Null(loaded.XboxGames["Xbox!Game!Identity"].Performance.Tdp);
        Assert.Null(loaded.XboxGames["Xbox!Game!Identity"].Performance.CpuBoost);
        Assert.Null(loaded.XboxGames["Xbox!Game!Identity"].Performance.PowerMode);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Unsafe_profile_load_is_not_replaced()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "profiles.json");
        const string malformed = "{ this is not valid JSON";
        File.WriteAllText(path, malformed);

        var result = AlwaysOnPerformanceFeatureMigration.Run(new ProfileStore(path), new ProfileMutationGate());

        Assert.Equal(AlwaysOnPerformanceFeatureMigrationStatus.UnsafeProfile, result.Status);
        Assert.Equal(ProfileLoadStatus.Malformed, result.ProfileStatus);
        Assert.Equal(malformed, File.ReadAllText(path));
    }

    [Fact]
    public void Failed_atomic_save_can_be_retried_on_the_next_run()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "profiles.json");
        var store = new ProfileStore(path);
        store.Save(BuildDocument());
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, "reserved");

        using (new FileStream(temporaryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.ThrowsAny<IOException>(() => AlwaysOnPerformanceFeatureMigration.Run(store, new ProfileMutationGate()));

        Assert.False(store.Load().Document.Device.Performance.Tdp!.Enabled);
        Assert.Equal(AlwaysOnPerformanceFeatureMigrationStatus.Changed,
            AlwaysOnPerformanceFeatureMigration.Run(store, new ProfileMutationGate()).Status);
        Assert.True(store.Load().Document.Device.Performance.Tdp!.Enabled);
    }

    [Fact]
    public void Cancellation_before_persistence_leaves_the_document_unchanged()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "profiles.json");
        var store = new ProfileStore(path);
        store.Save(BuildDocument());
        var before = File.ReadAllBytes(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            AlwaysOnPerformanceFeatureMigration.Run(store, new ProfileMutationGate(), cancellation.Token));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    private static ProfileDocument BuildDocument()
    {
        var tdp = new GameTdpSettings
        {
            Enabled = false,
            Ac = new TdpPowerPair { Pl1Watts = 24, Pl2Watts = 31, ExtensionData = Data("{\"future\":\"pair\"}") },
            Dc = new TdpPowerPair { Pl1Watts = 16, Pl2Watts = 23 },
            ExtensionData = Data("{\"future\":\"tdp\"}")
        };
        var cpuBoost = new GameCpuBoostSettings
        {
            Enabled = false,
            Ac = CpuBoostMode.Disabled,
            Dc = CpuBoostMode.EfficientAggressiveAtGuaranteed
        };
        var powerMode = new GamePowerModeSettings
        {
            Enabled = false,
            Ac = WindowsPowerMode.BestPowerEfficiency,
            Dc = WindowsPowerMode.BestPerformance
        };
        GamePerformanceOverrides Performance() => new()
        {
            Tdp = tdp,
            CpuBoost = cpuBoost,
            PowerMode = powerMode,
            FpsLimit = new GameFpsLimitSettings { Enabled = false, AcFps = 73, DcFps = 59 },
            GpuMinimumClock = new GameGpuMinimumClockSettings { Enabled = false, AcMhz = 1500, DcMhz = 1300 },
            ExtensionData = Data("{\"future\":\"gamePerformance\"}")
        };

        return new ProfileDocument
        {
            ExtensionData = Data("{\"future\":\"root\"}"),
            Device = new DeviceSettings
            {
                ExtensionData = Data("{\"future\":\"device\"}"),
                Performance = new DevicePerformanceSettings
                {
                    ExtensionData = Data("{\"future\":\"performance\"}"),
                    Tdp = new DeviceTdpSettings
                    {
                        Enabled = false,
                        Ac = new TdpPowerPair { Pl1Watts = 30, Pl2Watts = 37 },
                        Dc = new TdpPowerPair { Pl1Watts = 22, Pl2Watts = 29 },
                        ExtensionData = Data("{\"future\":\"tdp\"}")
                    },
                    CpuBoost = new DeviceCpuBoostSettings
                    {
                        Enabled = false,
                        Ac = CpuBoostMode.Disabled,
                        Dc = CpuBoostMode.EfficientAggressiveAtGuaranteed,
                        ExtensionData = Data("{\"future\":\"cpu\"}")
                    },
                    PowerMode = new DevicePowerModeSettings
                    {
                        Enabled = false,
                        Ac = WindowsPowerMode.BestPowerEfficiency,
                        Dc = WindowsPowerMode.BestPerformance,
                        ExtensionData = Data("{\"future\":\"power\"}")
                    },
                    MaximumProcessorState = new DeviceMaximumProcessorStateSettings { Enabled = false, Ac = 99, Dc = 100 }
                },
                Battery = new DeviceBatterySettings { ChargeLimit = new DeviceBatteryChargeLimitSettings { Enabled = false, LimitPercent = 80 } }
            },
            Games = new Dictionary<string, GameProfile>
            {
                ["101"] = new() { Enabled = true, Favorite = true, DisplayName = "Steam active", Performance = Performance(), ExtensionData = Data("{\"future\":\"steam\"}") },
                ["202"] = new() { Enabled = false, Favorite = false, DisplayName = "Steam prepared", Performance = Performance() }
            },
            XboxGames = new Dictionary<string, XboxGameProfile>
            {
                ["Xbox!Game!Identity"] = new() { Enabled = true, Favorite = true, DisplayName = "Xbox active", Performance = Performance(), ExtensionData = Data("{\"future\":\"xbox\"}") },
                ["Xbox!Second!Identity"] = new() { Enabled = false, Favorite = false, DisplayName = "Xbox prepared", Performance = Performance() }
            }
        };
    }

    private static Dictionary<string, JsonElement> Data(string json)
    {
        using var document = JsonDocument.Parse(json);
        var value = document.RootElement.GetProperty("future").GetString()!;
        var key = "future" + char.ToUpperInvariant(value[0]) + value[1..];
        return new Dictionary<string, JsonElement> { [key] = document.RootElement.Clone() };
    }

    private static void AssertPairEqual(TdpPowerPair expected, TdpPowerPair actual)
    {
        Assert.Equal(expected.Pl1Watts, actual.Pl1Watts);
        Assert.Equal(expected.Pl2Watts, actual.Pl2Watts);
        Assert.Equal(expected.ExtensionData?.Count ?? 0, actual.ExtensionData?.Count ?? 0);
        foreach (var (key, value) in expected.ExtensionData ?? [])
            AssertJsonEqual(value, actual.ExtensionData![key]);
    }

    private static void AssertJsonEqual(JsonElement expected, JsonElement actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected.GetRawText()), JsonNode.Parse(actual.GetRawText())));

    private sealed class TemporaryDirectory : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SteamInputAddonforClaw-AlwaysOnMigration", Guid.NewGuid().ToString("N"));
        internal TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
