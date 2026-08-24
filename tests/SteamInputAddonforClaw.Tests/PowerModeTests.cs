using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Profiles.Performance;
using SteamInputAddonforClaw.Profiles;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class PowerModeTests
{
    [Fact]
    public void Device_mutation_keeps_active_game_power_mode_authoritative()
    {
        var path = Path.Combine(Path.GetTempPath(), $"power-mode-{Guid.NewGuid():N}", "profiles.json");
        var store = new ProfileStore(path);
        var gamePower = new GamePowerModeSettings { Ac = WindowsPowerMode.BestPerformance, Dc = WindowsPowerMode.BestPowerEfficiency };
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings { Performance = new DevicePerformanceSettings { PowerMode = new DevicePowerModeSettings { Ac = WindowsPowerMode.Balanced, Dc = WindowsPowerMode.Balanced } } },
            Games = new() { ["42"] = new GameProfile { Enabled = true, Performance = new GamePerformanceOverrides { CpuBoost = new GameCpuBoostSettings { Ac = CpuBoostMode.Enabled, Dc = CpuBoostMode.Enabled }, Tdp = new GameTdpSettings { Ac = new() { Pl1Watts = 20, Pl2Watts = 22 }, Dc = new() { Pl1Watts = 20, Pl2Watts = 22 } }, PowerMode = gamePower } } }
        });
        var policy = new FakePowerModePolicy(); var runtime = new PowerModeRuntime(store, policy); runtime.SetActualAppIdSource(() => 42);
        Assert.Equal(PowerModeMutationOutcome.Succeeded, runtime.SetDeviceAc(WindowsPowerMode.BestPowerEfficiency).Outcome);
        Assert.Equal((WindowsPowerMode.BestPerformance, WindowsPowerMode.BestPowerEfficiency), policy.LastApplied);
    }

    [Fact]
    public void Unsafe_profile_load_is_not_reported_writable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"power-mode-{Guid.NewGuid():N}", "profiles.json"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "not-json");
        var runtime = new PowerModeRuntime(new ProfileStore(path), new FakePowerModePolicy()); runtime.StartupReconcile();
        Assert.False(runtime.Snapshot.PersistenceWritable);
    }

    [Fact]
    public void Windows_read_failure_leaves_power_mode_uninitialized_and_not_writable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"power-mode-{Guid.NewGuid():N}", "profiles.json");
        var policy = new FakePowerModePolicy { FailRead = true };
        var runtime = new PowerModeRuntime(new ProfileStore(path), policy);

        runtime.StartupReconcile();

        Assert.False(runtime.Snapshot.PersistenceWritable);
        Assert.Null(runtime.Snapshot.AcDesired);
        Assert.Null(runtime.Snapshot.DcDesired);
        Assert.Null(policy.LastApplied);
    }

    [Fact]
    public void Existing_enabled_profile_lazily_adopts_device_power_mode_before_first_edit()
    {
        var path = Path.Combine(Path.GetTempPath(), $"power-mode-{Guid.NewGuid():N}", "profiles.json");
        var store = new ProfileStore(path);
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings { Performance = new DevicePerformanceSettings { PowerMode = new DevicePowerModeSettings { Ac = WindowsPowerMode.Balanced, Dc = WindowsPowerMode.BestPowerEfficiency } } },
            Games = new() { ["42"] = new GameProfile { Enabled = true, Performance = new GamePerformanceOverrides { CpuBoost = new GameCpuBoostSettings { Ac = CpuBoostMode.Enabled, Dc = CpuBoostMode.Enabled }, Tdp = new GameTdpSettings { Ac = new() { Pl1Watts = 20, Pl2Watts = 22 }, Dc = new() { Pl1Watts = 20, Pl2Watts = 22 } } } } }
        });
        var mutations = new GameProfileMutations(store);
        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded, mutations.SetPowerModeAc(42, WindowsPowerMode.BestPerformance));
        var saved = store.Load().Document.Games["42"].Performance.PowerMode;
        Assert.Equal(WindowsPowerMode.BestPerformance, saved!.Ac);
        Assert.Equal(WindowsPowerMode.BestPowerEfficiency, saved.Dc);
    }

    [Fact]
    public void Active_profile_reconcile_returns_native_apply_failure()
    {
        var path = Path.Combine(Path.GetTempPath(), $"power-mode-{Guid.NewGuid():N}", "profiles.json");
        var store = new ProfileStore(path);
        store.Save(new ProfileDocument { Device = new DeviceSettings { Performance = new DevicePerformanceSettings { PowerMode = new DevicePowerModeSettings { Ac = WindowsPowerMode.Balanced, Dc = WindowsPowerMode.Balanced } } } });
        var policy = new FakePowerModePolicy { FailApply = true }; var runtime = new PowerModeRuntime(store, policy); runtime.SetActualAppIdSource(() => 0);
        var result = runtime.ReconcileWithResult(0);
        Assert.False(result.Succeeded);
        Assert.Contains("native failure", result.FailureMessage);
    }
    [Theory]
    [InlineData("961cc777-2547-4f9d-8174-7d86181b8a7a", PowerModeReadStatus.Known, WindowsPowerMode.BestPowerEfficiency)]
    [InlineData("00000000-0000-0000-0000-000000000000", PowerModeReadStatus.Known, WindowsPowerMode.Balanced)]
    [InlineData("ded574b5-45a0-4f42-8737-46345c09c238", PowerModeReadStatus.Known, WindowsPowerMode.BestPerformance)]
    [InlineData("11111111-1111-1111-1111-111111111111", PowerModeReadStatus.Unknown, null)]
    public void Maps_documented_guids_without_normalizing_unknown(string raw, PowerModeReadStatus status, WindowsPowerMode? mode)
    {
        var result = WindowsPowerModePolicy.Map(Guid.Parse(raw), "AC");
        Assert.Equal(status, result.Status);
        Assert.Equal(mode, result.Mode);
    }

    [Theory]
    [InlineData(WindowsPowerMode.BestPowerEfficiency, "961cc777-2547-4f9d-8174-7d86181b8a7a")]
    [InlineData(WindowsPowerMode.Balanced, "00000000-0000-0000-0000-000000000000")]
    [InlineData(WindowsPowerMode.BestPerformance, "ded574b5-45a0-4f42-8737-46345c09c238")]
    public void Maps_all_supported_modes_to_the_documented_guid(WindowsPowerMode mode, string raw) => Assert.Equal(Guid.Parse(raw), WindowsPowerModePolicy.ToGuid(mode));

    [Fact]
    public void Logs_native_read_and_write_failures_with_side_and_win32_error()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"power-mode-log-{Guid.NewGuid():N}");
        AppLog.DirectoryOverride = directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Info;

        WindowsPowerModePolicy.LogReadFailure("AC", 5);
        WindowsPowerModePolicy.LogWriteFailure("DC", 87);
        AppLog.DrainForTests();

        var log = File.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("Power Mode AC read failed", log);
        Assert.Contains("Side=", log); // side is part of the category-specific message
        Assert.Contains("Win32Error=5", log);
        Assert.Contains("Power Mode DC write failed", log);
        Assert.Contains("Win32Error=87", log);
    }
}

internal sealed class FakePowerModePolicy : IPowerModePolicy
{
    internal bool FailApply { get; init; }
    internal bool FailRead { get; init; }
    internal (WindowsPowerMode Ac, WindowsPowerMode Dc)? LastApplied { get; private set; }
    public PowerModeSystemState Read() => FailRead
        ? new(false, PowerModeSideReading.Unavailable, PowerModeSideReading.Unavailable, "read failure")
        : new(true, new(PowerModeReadStatus.Known, WindowsPowerMode.Balanced), new(PowerModeReadStatus.Known, WindowsPowerMode.Balanced), null);
    public PowerModeApplyResult Apply(WindowsPowerMode? ac, WindowsPowerMode? dc) { if (ac is { } || dc is { }) LastApplied = (ac ?? WindowsPowerMode.Balanced, dc ?? WindowsPowerMode.Balanced); return FailApply ? new(false, false, "native failure") : PowerModeApplyResult.NoOp; }
}
