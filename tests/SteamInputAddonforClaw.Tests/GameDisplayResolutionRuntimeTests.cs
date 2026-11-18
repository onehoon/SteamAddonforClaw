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
    private static readonly DisplayModeSnapshot OriginalMode = new(1920, 1200, 120, 32);
    private static readonly DisplayScaleSnapshot OriginalScale = new("\\.\\DISPLAY1", "monitor-path-1", 2, 150);

    [Fact]
    public void Pending_legacy_recovery_blocks_reconcile_when_resolution_restore_fails()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Recovery, "{\"Original\":{\"Width\":1920,\"Height\":1200,\"RefreshRate\":120,\"BitsPerPixel\":32},\"Target\":{\"Width\":1440,\"Height\":900}}");
        var store = new ProfileStore(Profiles);
        SaveSteamProfile(store, 123, new() { Width = 1440, Height = 900 });
        var display = new FakeDisplay { Current = new(1440, 900, 120, 32), RestoreSucceeds = false };
        var scaling = new FakeScaling { CaptureSucceeds = false };
        var runtime = CreateRuntime(store, display, scaling);
        runtime.SetActiveProfileResolver(ActiveProfileTestResolver.ForSteam(() => 123));

        runtime.StartupRecover();
        runtime.Reconcile();

        Assert.Equal(0, display.ApplyCalls);
        Assert.Equal(0, scaling.CaptureCalls);
        Assert.True(File.Exists(Recovery));
    }

    [Fact]
    public void Resolution_only_does_not_query_or_require_DPI_scaling()
    {
        Directory.CreateDirectory(_root);
        var store = new ProfileStore(Profiles);
        SaveSteamProfile(store, 123, new() { Width = 1440, Height = 900 });
        var display = new FakeDisplay { Current = OriginalMode };
        var scaling = new FakeScaling { CaptureSucceeds = false };
        var runtime = CreateRuntime(store, display, scaling);
        uint active = 123;
        runtime.SetActiveProfileResolver(ActiveProfileTestResolver.ForSteam(() => active));

        Assert.True(runtime.Reconcile());
        Assert.Equal((1440, 900), (display.Current.Width, display.Current.Height));
        Assert.Equal(0, scaling.CaptureCalls);
        Assert.Equal(0, scaling.SetCalls);

        active = 0;
        Assert.True(runtime.Reconcile());
        Assert.Equal(OriginalMode, display.Current);
        Assert.Equal(0, scaling.RestoreCalls);
    }

    [Fact]
    public void Scaling_only_changes_100_percent_and_restores_without_changing_resolution()
    {
        Directory.CreateDirectory(_root);
        var store = new ProfileStore(Profiles);
        SaveSteamProfile(store, 123, resolution: null, scaling: true);
        var display = new FakeDisplay { Current = OriginalMode };
        var scaling = new FakeScaling { Current = OriginalScale };
        var runtime = CreateRuntime(store, display, scaling);
        uint active = 123;
        runtime.SetActiveProfileResolver(ActiveProfileTestResolver.ForSteam(() => active));

        Assert.True(runtime.Reconcile());
        Assert.Equal(OriginalMode, display.Current);
        Assert.Equal(0, display.ApplyCalls);
        Assert.Equal(0, display.RestoreCalls);
        Assert.Equal(100, scaling.Current.OriginalScaleFactor);
        Assert.Equal(1, scaling.SetCalls);

        active = 0;
        Assert.True(runtime.Reconcile());
        Assert.Equal(OriginalMode, display.Current);
        Assert.Equal(0, display.ApplyCalls);
        Assert.Equal(0, display.RestoreCalls);
        Assert.Equal(OriginalScale, scaling.Current);
        Assert.False(File.Exists(Recovery));
    }

    [Fact]
    public void Scaling_packet_structures_keep_the_reverse_engineered_native_sizes()
    {
        Assert.Equal(0x20, DisplayScalingService.DpiScaleGetInfoSize);
        Assert.Equal(0x18, DisplayScalingService.DpiScaleSetInfoSize);
    }

    [Fact]
    public void Display_capture_failure_after_recovery_persistence_restores_and_clears_marker()
    {
        Directory.CreateDirectory(_root);
        var store = new ProfileStore(Profiles);
        SaveSteamProfile(store, 123, new() { Width = 1440, Height = 900 });
        var display = new FakeDisplay { Current = OriginalMode, FailCaptureAfterFirst = true };
        var runtime = CreateRuntime(store, display, new FakeScaling());
        runtime.SetActiveProfileResolver(ActiveProfileTestResolver.ForSteam(() => 123));

        Assert.False(runtime.Reconcile());
        Assert.Equal(OriginalMode, display.Current);
        Assert.Equal(1, display.RestoreCalls);
        Assert.False(File.Exists(Recovery));
    }

    [Fact]
    public void Combined_changes_apply_resolution_then_scaling_and_restore_resolution_then_scaling()
    {
        Directory.CreateDirectory(_root);
        var store = new ProfileStore(Profiles);
        SaveSteamProfile(store, 123, new() { Width = 1440, Height = 900 }, scaling: true);
        var operations = new List<string>();
        var display = new FakeDisplay { Current = OriginalMode, Operations = operations };
        var scaling = new FakeScaling { Current = OriginalScale, Operations = operations };
        var runtime = CreateRuntime(store, display, scaling);
        uint active = 123;
        runtime.SetActiveProfileResolver(ActiveProfileTestResolver.ForSteam(() => active));

        Assert.True(runtime.Reconcile());
        Assert.True(operations.IndexOf("display.apply") < operations.IndexOf("scale.set100"));
        operations.Clear();

        active = 0;
        Assert.True(runtime.Reconcile());

        Assert.True(operations.IndexOf("display.restore") < operations.IndexOf("scale.restore"));
        Assert.Equal(OriginalMode, display.Current);
        Assert.Equal(OriginalScale, scaling.Current);
        Assert.False(File.Exists(Recovery));
    }

    [Fact]
    public void Already_100_percent_does_not_issue_a_native_scale_change()
    {
        Directory.CreateDirectory(_root);
        var store = new ProfileStore(Profiles);
        SaveSteamProfile(store, 123, resolution: null, scaling: true);
        var display = new FakeDisplay { Current = OriginalMode };
        var scaling = new FakeScaling { Current = OriginalScale with { OriginalRelativeScale = 0, OriginalScaleFactor = 100 } };
        var runtime = CreateRuntime(store, display, scaling);
        runtime.SetActiveProfileResolver(ActiveProfileTestResolver.ForSteam(() => 123));

        Assert.True(runtime.Reconcile());
        Assert.Equal(0, scaling.SetCalls);
    }

    [Fact]
    public void Switching_from_scaling_on_to_off_restores_scale_before_the_new_resolution()
    {
        Directory.CreateDirectory(_root);
        var store = new ProfileStore(Profiles);
        SaveSteamProfile(store, 123, new() { Width = 1440, Height = 900 }, scaling: true);
        SaveSteamProfile(store, 456, new() { Width = 1920, Height = 1080 }, scaling: false);
        var operations = new List<string>();
        var display = new FakeDisplay { Current = OriginalMode, Operations = operations };
        var scaling = new FakeScaling { Current = OriginalScale, Operations = operations };
        var runtime = CreateRuntime(store, display, scaling);
        uint active = 123;
        runtime.SetActiveProfileResolver(ActiveProfileTestResolver.ForSteam(() => active));

        Assert.True(runtime.Reconcile());
        operations.Clear();
        active = 456;
        Assert.True(runtime.Reconcile());

        Assert.True(operations.IndexOf("scale.restore") < operations.IndexOf("display.apply"));
        Assert.Equal(OriginalScale, scaling.Current);
        Assert.Equal((1920, 1080), (display.Current.Width, display.Current.Height));

        active = 0;
        Assert.True(runtime.Reconcile());
        Assert.Equal(OriginalMode, display.Current);
    }

    [Fact]
    public void A_to_B_keeps_the_original_resolution_baseline_and_shutdown_restores_it()
    {
        Directory.CreateDirectory(_root);
        var store = new ProfileStore(Profiles);
        SaveSteamProfile(store, 123, new() { Width = 1440, Height = 900 });
        SaveSteamProfile(store, 456, new() { Width = 1920, Height = 1080 });
        var display = new FakeDisplay { Current = OriginalMode };
        var runtime = CreateRuntime(store, display, new FakeScaling());
        uint active = 123;
        runtime.SetActiveProfileResolver(ActiveProfileTestResolver.ForSteam(() => active));

        Assert.True(runtime.Reconcile());
        active = 456;
        Assert.True(runtime.Reconcile());
        runtime.Shutdown();

        Assert.Equal(OriginalMode, display.Current);
        Assert.False(File.Exists(Recovery));
    }

    [Fact]
    public void A_to_B_with_scaling_keeps_the_original_scale_baseline_without_recapturing()
    {
        Directory.CreateDirectory(_root);
        var store = new ProfileStore(Profiles);
        SaveSteamProfile(store, 123, new() { Width = 1440, Height = 900 }, scaling: true);
        var document = store.Load().Document;
        store.Save(document with
        {
            Games = new Dictionary<string, GameProfile>(document.Games)
            {
                ["456"] = document.Games["123"] with
                {
                    DisplayName = "Game B",
                    Display = new GameDisplayOverrides { Resolution = new() { Width = 1600, Height = 900 }, ScalingTo100Percent = true },
                },
            },
        });
        var display = new FakeDisplay { Current = OriginalMode };
        var scaling = new FakeScaling { Current = OriginalScale };
        var runtime = CreateRuntime(store, display, scaling);
        uint active = 123;
        runtime.SetActiveProfileResolver(ActiveProfileTestResolver.ForSteam(() => active));

        Assert.True(runtime.Reconcile());
        active = 456;
        Assert.True(runtime.Reconcile());
        active = 0;
        Assert.True(runtime.Reconcile());

        Assert.Equal(1, scaling.CaptureCalls);
        Assert.Equal(OriginalScale, scaling.Current);
        Assert.Equal(OriginalMode, display.Current);
    }

    [Fact]
    public void Enabling_scaling_during_a_resolution_owned_session_captures_and_restores_its_context()
    {
        Directory.CreateDirectory(_root);
        var store = new ProfileStore(Profiles);
        SaveSteamProfile(store, 123, new() { Width = 1440, Height = 900 });
        var display = new FakeDisplay { Current = OriginalMode };
        var scaling = new FakeScaling { Current = OriginalScale };
        var runtime = CreateRuntime(store, display, scaling);
        uint active = 123;
        runtime.SetActiveProfileResolver(ActiveProfileTestResolver.ForSteam(() => active));

        Assert.True(runtime.Reconcile());
        Assert.Equal((1440, 900), (display.Current.Width, display.Current.Height));
        var document = store.Load().Document;
        var profile = document.Games["123"];
        store.Save(document with
        {
            Games = new Dictionary<string, GameProfile>(document.Games)
            {
                ["123"] = profile with { Display = profile.Display with { ScalingTo100Percent = true } },
            },
        });

        Assert.True(runtime.Reconcile());
        Assert.Equal(1, scaling.CaptureCalls);
        Assert.Equal((1440, 900), (display.Current.Width, display.Current.Height));
        Assert.Equal(100, scaling.Current.OriginalScaleFactor);

        document = store.Load().Document;
        profile = document.Games["123"];
        store.Save(document with
        {
            Games = new Dictionary<string, GameProfile>(document.Games)
            {
                ["123"] = profile with { Display = profile.Display with { ScalingTo100Percent = false } },
            },
        });
        Assert.True(runtime.Reconcile());
        Assert.Equal(OriginalScale, scaling.Current);
        Assert.Equal((1440, 900), (display.Current.Width, display.Current.Height));

        active = 0;
        Assert.True(runtime.Reconcile());
        Assert.Equal(OriginalMode, display.Current);
        Assert.False(File.Exists(Recovery));
    }

    [Fact]
    public void Xbox_profile_can_enable_and_restore_scaling_through_the_shared_runtime()
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
                    Performance = ValidPerformance(),
                    Display = new GameDisplayOverrides { ScalingTo100Percent = true },
                },
            },
        });
        var display = new FakeDisplay { Current = OriginalMode };
        var scaling = new FakeScaling { Current = OriginalScale };
        var runtime = CreateRuntime(store, display, scaling);
        string? activeKey = key;
        runtime.SetActiveProfileResolver(ActiveProfileTestResolver.ForXbox(() => activeKey));

        Assert.True(runtime.Reconcile());
        Assert.Equal(OriginalMode, display.Current);
        Assert.Equal(100, scaling.Current.OriginalScaleFactor);

        activeKey = null;
        Assert.True(runtime.Reconcile());
        Assert.Equal(OriginalScale, scaling.Current);
        Assert.Equal(OriginalMode, display.Current);
    }

    [Fact]
    public void Startup_recovery_restores_both_components_and_legacy_marker_restores_resolution_only()
    {
        Directory.CreateDirectory(_root);
        var store = new ProfileStore(Profiles);
        SaveSteamProfile(store, 123, new() { Width = 1440, Height = 900 }, scaling: true);
        var display = new FakeDisplay { Current = OriginalMode };
        var scaling = new FakeScaling { Current = OriginalScale };
        var firstRuntime = CreateRuntime(store, display, scaling);
        firstRuntime.SetActiveProfileResolver(ActiveProfileTestResolver.ForSteam(() => 123));
        Assert.True(firstRuntime.Reconcile());

        var afterCrash = new GameDisplayResolutionRuntime(store, new(), _root, display, scaling);
        afterCrash.StartupRecover();
        Assert.Equal(OriginalMode, display.Current);
        Assert.Equal(OriginalScale, scaling.Current);
        Assert.False(File.Exists(Recovery));

        File.WriteAllText(Recovery, "{\"Original\":{\"Width\":1920,\"Height\":1200,\"RefreshRate\":120,\"BitsPerPixel\":32},\"Target\":{\"Width\":1440,\"Height\":900}}");
        display.Current = new(1440, 900, 120, 32);
        scaling.CaptureCalls = scaling.RestoreCalls = 0;
        scaling.CaptureSucceeds = false;
        var legacyRecovery = CreateRuntime(store, display, scaling);
        legacyRecovery.StartupRecover();

        Assert.Equal(OriginalMode, display.Current);
        Assert.Equal(0, scaling.CaptureCalls);
        Assert.Equal(0, scaling.RestoreCalls);
        Assert.False(File.Exists(Recovery));
    }

    [Fact]
    public void Failed_second_apply_rolls_back_the_first_and_failed_scale_restore_keeps_marker()
    {
        Directory.CreateDirectory(_root);
        var store = new ProfileStore(Profiles);
        SaveSteamProfile(store, 123, new() { Width = 1440, Height = 900 }, scaling: true);
        var display = new FakeDisplay { Current = OriginalMode };
        var scaling = new FakeScaling { Current = OriginalScale, SetSucceeds = false };
        var runtime = CreateRuntime(store, display, scaling);
        uint active = 123;
        runtime.SetActiveProfileResolver(ActiveProfileTestResolver.ForSteam(() => active));

        Assert.False(runtime.Reconcile());
        Assert.Equal(OriginalMode, display.Current);
        Assert.Equal(OriginalScale, scaling.Current);
        Assert.False(File.Exists(Recovery));

        scaling.SetSucceeds = true;
        Assert.True(runtime.Reconcile());
        active = 0;
        scaling.RestoreSucceeds = false;
        Assert.False(runtime.Reconcile());
        Assert.True(File.Exists(Recovery));
        Assert.Equal(100, scaling.Current.OriginalScaleFactor);

        scaling.RestoreSucceeds = true;
        Assert.True(runtime.Reconcile());
        Assert.Equal(OriginalScale, scaling.Current);
        Assert.False(File.Exists(Recovery));
    }

    [Fact]
    public void Scale_restore_never_applies_to_a_different_primary_display()
    {
        Directory.CreateDirectory(_root);
        var store = new ProfileStore(Profiles);
        SaveSteamProfile(store, 123, resolution: null, scaling: true);
        var display = new FakeDisplay { Current = OriginalMode };
        var scaling = new FakeScaling { Current = OriginalScale };
        var runtime = CreateRuntime(store, display, scaling);
        uint active = 123;
        runtime.SetActiveProfileResolver(ActiveProfileTestResolver.ForSteam(() => active));
        Assert.True(runtime.Reconcile());
        var otherDisplay = scaling.Current with { PrimaryGdiDeviceName = "\\.\\DISPLAY2", MonitorDevicePath = "monitor-path-2" };
        scaling.Current = otherDisplay;
        active = 0;

        Assert.False(runtime.Reconcile());
        Assert.Equal(100, scaling.Current.OriginalScaleFactor);
        Assert.True(File.Exists(Recovery));
        Assert.Equal(0, display.ApplyCalls);
        Assert.Equal(0, display.RestoreCalls);
    }

    [Fact]
    public void Failed_scale_capture_does_not_apply_resolution_or_leave_a_recovery_marker()
    {
        Directory.CreateDirectory(_root);
        var store = new ProfileStore(Profiles);
        SaveSteamProfile(store, 123, new() { Width = 1440, Height = 900 }, scaling: true);
        var display = new FakeDisplay { Current = OriginalMode };
        var scaling = new FakeScaling { CaptureSucceeds = false };
        var runtime = CreateRuntime(store, display, scaling);
        runtime.SetActiveProfileResolver(ActiveProfileTestResolver.ForSteam(() => 123));

        Assert.False(runtime.Reconcile());
        Assert.Equal(OriginalMode, display.Current);
        Assert.Equal(0, display.ApplyCalls);
        Assert.False(File.Exists(Recovery));
    }

    private GameDisplayResolutionRuntime CreateRuntime(ProfileStore store, FakeDisplay display, FakeScaling scaling)
        => new(store, new(), _root, display, scaling);

    private static void SaveSteamProfile(ProfileStore store, uint appId, GameDisplayResolution? resolution, bool scaling = false)
    {
        store.Save(new ProfileDocument
        {
            Games = new()
            {
                [appId.ToString()] = new()
                {
                    Enabled = true,
                    Performance = ValidPerformance(),
                    Display = new GameDisplayOverrides { Resolution = resolution, ScalingTo100Percent = scaling },
                },
            },
        });
    }

    private static GamePerformanceOverrides ValidPerformance() => new()
    {
        CpuBoost = new GameCpuBoostSettings { Enabled = true, Ac = CpuBoostMode.Enabled, Dc = CpuBoostMode.Disabled },
        Tdp = new GameTdpSettings { Enabled = true, Ac = new() { Pl1Watts = 20, Pl2Watts = 22 }, Dc = new() { Pl1Watts = 20, Pl2Watts = 22 } },
    };

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class FakeDisplay : IDisplayResolutionService
    {
        public DisplayModeSnapshot Current;
        public bool ApplySucceeds = true, RestoreSucceeds = true, FailCaptureAfterFirst;
        public int ApplyCalls, RestoreCalls, CaptureCalls;
        public List<string>? Operations;

        public bool TryCapture(out DisplayModeSnapshot snapshot)
        {
            Operations?.Add("display.capture");
            snapshot = Current;
            CaptureCalls++;
            return !FailCaptureAfterFirst || CaptureCalls == 1;
        }

        public bool TryApply(DisplayModeSnapshot current, int width, int height)
        {
            Operations?.Add("display.apply");
            ApplyCalls++;
            if (ApplySucceeds) Current = current with { Width = width, Height = height };
            return ApplySucceeds;
        }

        public bool TryRestore(DisplayModeSnapshot original)
        {
            Operations?.Add("display.restore");
            RestoreCalls++;
            if (RestoreSucceeds) Current = original;
            return RestoreSucceeds;
        }
    }

    private sealed class FakeScaling : IDisplayScalingService
    {
        public DisplayScaleSnapshot Current = OriginalScale;
        public bool CaptureSucceeds = true, SetSucceeds = true, RestoreSucceeds = true;
        public int MinimumRelativeScale = 0, CaptureCalls, SetCalls, RestoreCalls;
        public List<string>? Operations;

        public bool TryCapture(out DisplayScaleSnapshot snapshot)
        {
            Operations?.Add("scale.capture");
            CaptureCalls++;
            snapshot = Current;
            return CaptureSucceeds;
        }

        public bool TrySetTo100(DisplayScaleSnapshot original)
        {
            Operations?.Add("scale.set100");
            if (Current.OriginalRelativeScale == MinimumRelativeScale && Current.OriginalScaleFactor == 100) return true;
            if (!SetSucceeds || !SameDisplay(Current, original)) return false;
            SetCalls++;
            Current = Current with { OriginalRelativeScale = MinimumRelativeScale, OriginalScaleFactor = 100 };
            return true;
        }

        public bool TryRestore(DisplayScaleSnapshot original)
        {
            Operations?.Add("scale.restore");
            RestoreCalls++;
            if (!RestoreSucceeds || !SameDisplay(Current, original)) return false;
            Current = original;
            return true;
        }

        private static bool SameDisplay(DisplayScaleSnapshot left, DisplayScaleSnapshot right)
            => string.Equals(left.PrimaryGdiDeviceName, right.PrimaryGdiDeviceName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.MonitorDevicePath, right.MonitorDevicePath, StringComparison.OrdinalIgnoreCase);
    }
}
