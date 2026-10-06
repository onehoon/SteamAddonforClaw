using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Profiles;
using SteamInputAddonforClaw.Profiles.Display;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class GameDisplayResolutionRuntimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.DisplayTests", Guid.NewGuid().ToString("N"));
    private string Profiles => Path.Combine(_root, "profiles.json");
    private string Recovery => Path.Combine(_root, "display-resolution-recovery.json");
    [Fact]
    public void Pending_recovery_blocks_reconcile_when_restore_fails()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Recovery, "{\"Original\":{\"Width\":1920,\"Height\":1200,\"RefreshRate\":120,\"BitsPerPixel\":32},\"Target\":{\"Width\":1440,\"Height\":900}}");
        var store = new ProfileStore(Profiles);
        store.Save(new ProfileDocument { Games = new() { ["123"] = new() { Enabled = true, Performance = ValidPerformance(), Display = new() { Resolution = new GameDisplayResolution { Width = 1440, Height = 900 } } } } });
        var display = new FakeDisplay { Current = new(1440, 900, 120, 32), RestoreSucceeds = false };
        var runtime = new GameDisplayResolutionRuntime(store, new(), _root, display);
        runtime.SetActiveProfileResolver(ActiveProfileTestResolver.ForSteam(() => 123));
        runtime.StartupRecover(); runtime.Reconcile();
        Assert.Equal(0, display.ApplyCalls);
        Assert.True(File.Exists(Recovery));
    }
    [Fact]
    public void Apply_failure_restores_and_clears_recovery_when_restore_succeeds()
    {
        Directory.CreateDirectory(_root);
        var store = new ProfileStore(Profiles);
        store.Save(new ProfileDocument { Games = new() { ["123"] = new() { Enabled = true, Performance = ValidPerformance(), Display = new() { Resolution = new GameDisplayResolution { Width = 1440, Height = 900 } } } } });
        var display = new FakeDisplay { Current = new(1920, 1200, 120, 32), ApplySucceeds = false, RestoreSucceeds = true };
        var runtime = new GameDisplayResolutionRuntime(store, new(), _root, display);
        runtime.SetActiveProfileResolver(ActiveProfileTestResolver.ForSteam(() => 123));
        Assert.False(runtime.Reconcile());
        Assert.Equal(1, display.RestoreCalls); Assert.False(File.Exists(Recovery));
    }
    [Fact]
    public void Capture_failure_after_persistence_restores_and_clears_recovery()
    {
        Directory.CreateDirectory(_root);
        var store = new ProfileStore(Profiles);
        store.Save(new ProfileDocument { Games = new() { ["123"] = new() { Enabled = true, Performance = ValidPerformance(), Display = new() { Resolution = new GameDisplayResolution { Width = 1440, Height = 900 } } } } });
        var display = new FakeDisplay { Current = new(1920, 1200, 120, 32), FailCaptureAfterFirst = true };
        var runtime = new GameDisplayResolutionRuntime(store, new(), _root, display);
        runtime.SetActiveProfileResolver(ActiveProfileTestResolver.ForSteam(() => 123));
        Assert.False(runtime.Reconcile());
        Assert.Equal(1, display.RestoreCalls); Assert.False(File.Exists(Recovery));
    }
    [Fact]
    public void A_to_B_keeps_original_baseline_and_shutdown_restores_it()
    {
        Directory.CreateDirectory(_root);
        var store = new ProfileStore(Profiles);
        store.Save(new ProfileDocument { Games = new() { ["123"] = new() { Enabled = true, Performance = ValidPerformance(), Display = new() { Resolution = new GameDisplayResolution { Width = 1440, Height = 900 } } }, ["456"] = new() { Enabled = true, Performance = ValidPerformance(), Display = new() { Resolution = new GameDisplayResolution { Width = 1920, Height = 1080 } } } } });
        var display = new FakeDisplay { Current = new(1920, 1200, 120, 32) };
        var runtime = new GameDisplayResolutionRuntime(store, new(), _root, display);
        uint appId = 123;
        runtime.SetActiveProfileResolver(ActiveProfileTestResolver.ForSteam(() => appId));
        runtime.Reconcile(); appId = 456; runtime.Reconcile(); runtime.Shutdown();
        Assert.Equal(new DisplayModeSnapshot(1920, 1200, 120, 32), display.Current); Assert.False(File.Exists(Recovery));
    }

    [Fact]
    public void Reconcile_uses_the_same_resolution_runtime_for_XBOX_and_restores_after_exit()
    {
        Directory.CreateDirectory(_root);
        const string key = "store:display-game";
        var store = new ProfileStore(Profiles);
        store.Save(new ProfileDocument
        {
            XboxGames = new()
            {
                [key] = new XboxGameProfile
                {
                    Enabled = true,
                    Performance = new GamePerformanceOverrides
                    {
                        CpuBoost = new GameCpuBoostSettings { Enabled = true, Ac = CpuBoostMode.Aggressive, Dc = CpuBoostMode.EfficientEnabled },
                        Tdp = new GameTdpSettings { Enabled = true, Ac = new() { Pl1Watts = 20, Pl2Watts = 22 }, Dc = new() { Pl1Watts = 20, Pl2Watts = 22 } }
                    },
                    Display = new GameDisplayOverrides { Resolution = new GameDisplayResolution { Width = 1600, Height = 900 } }
                }
            }
        });
        var display = new FakeDisplay { Current = new(1920, 1200, 120, 32) };
        var runtime = new GameDisplayResolutionRuntime(store, new(), _root, display);
        string? activeKey = key;
        runtime.SetActiveProfileResolver(ActiveProfileTestResolver.ForXbox(() => activeKey));

        Assert.True(runtime.Reconcile());
        Assert.Equal((1600, 900), (display.Current.Width, display.Current.Height));

        activeKey = null;
        Assert.True(runtime.Reconcile());
        Assert.Equal(new DisplayModeSnapshot(1920, 1200, 120, 32), display.Current);
    }

    private static GamePerformanceOverrides ValidPerformance() => new()
    {
        CpuBoost = new GameCpuBoostSettings { Enabled = true, Ac = CpuBoostMode.Enabled, Dc = CpuBoostMode.Disabled },
        Tdp = new GameTdpSettings { Enabled = true, Ac = new() { Pl1Watts = 20, Pl2Watts = 22 }, Dc = new() { Pl1Watts = 20, Pl2Watts = 22 } }
    };

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class FakeDisplay : IDisplayResolutionService
    {
        public DisplayModeSnapshot Current; public bool ApplySucceeds = true, RestoreSucceeds = true, FailCaptureAfterFirst; public int ApplyCalls, RestoreCalls; private int _captures;
        public bool TryCapture(out DisplayModeSnapshot snapshot) { snapshot = Current; _captures++; return !FailCaptureAfterFirst || _captures == 1; }
        public bool TryApply(DisplayModeSnapshot current, int width, int height) { ApplyCalls++; if (ApplySucceeds) Current = current with { Width = width, Height = height }; return ApplySucceeds; }
        public bool TryRestore(DisplayModeSnapshot original) { RestoreCalls++; if (RestoreSucceeds) Current = original; return RestoreSucceeds; }
    }
}
