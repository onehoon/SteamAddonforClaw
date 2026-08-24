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
        store.Save(new ProfileDocument { Games = new() { ["123"] = new() { Display = new() { Resolution = new GameDisplayResolution { Width = 1440, Height = 900 } } } } });
        var display = new FakeDisplay { Current = new(1440, 900, 120, 32), RestoreSucceeds = false };
        var runtime = new GameDisplayResolutionRuntime(store, new(), _root, display);
        runtime.StartupRecover(); runtime.Reconcile(123);
        Assert.Equal(0, display.ApplyCalls);
        Assert.True(File.Exists(Recovery));
    }
    [Fact]
    public void Apply_failure_restores_and_clears_recovery_when_restore_succeeds()
    {
        Directory.CreateDirectory(_root);
        var store = new ProfileStore(Profiles);
        store.Save(new ProfileDocument { Games = new() { ["123"] = new() { Display = new() { Resolution = new GameDisplayResolution { Width = 1440, Height = 900 } } } } });
        var display = new FakeDisplay { Current = new(1920, 1200, 120, 32), ApplySucceeds = false, RestoreSucceeds = true };
        new GameDisplayResolutionRuntime(store, new(), _root, display).Reconcile(123);
        Assert.Equal(1, display.RestoreCalls); Assert.False(File.Exists(Recovery));
    }
    [Fact]
    public void Capture_failure_after_persistence_restores_and_clears_recovery()
    {
        Directory.CreateDirectory(_root);
        var store = new ProfileStore(Profiles);
        store.Save(new ProfileDocument { Games = new() { ["123"] = new() { Display = new() { Resolution = new GameDisplayResolution { Width = 1440, Height = 900 } } } } });
        var display = new FakeDisplay { Current = new(1920, 1200, 120, 32), FailCaptureAfterFirst = true };
        new GameDisplayResolutionRuntime(store, new(), _root, display).Reconcile(123);
        Assert.Equal(1, display.RestoreCalls); Assert.False(File.Exists(Recovery));
    }
    [Fact]
    public void A_to_B_keeps_original_baseline_and_shutdown_restores_it()
    {
        Directory.CreateDirectory(_root);
        var store = new ProfileStore(Profiles);
        store.Save(new ProfileDocument { Games = new() { ["123"] = new() { Display = new() { Resolution = new GameDisplayResolution { Width = 1440, Height = 900 } } }, ["456"] = new() { Display = new() { Resolution = new GameDisplayResolution { Width = 1920, Height = 1080 } } } } });
        var display = new FakeDisplay { Current = new(1920, 1200, 120, 32) };
        var runtime = new GameDisplayResolutionRuntime(store, new(), _root, display);
        runtime.Reconcile(123); runtime.Reconcile(456); runtime.Shutdown();
        Assert.Equal(new DisplayModeSnapshot(1920, 1200, 120, 32), display.Current); Assert.False(File.Exists(Recovery));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class FakeDisplay : IDisplayResolutionService
    {
        public DisplayModeSnapshot Current; public bool ApplySucceeds = true, RestoreSucceeds = true, FailCaptureAfterFirst; public int ApplyCalls, RestoreCalls; private int _captures;
        public bool TryCapture(out DisplayModeSnapshot snapshot) { snapshot = Current; _captures++; return !FailCaptureAfterFirst || _captures == 1; }
        public bool TryApply(DisplayModeSnapshot current, int width, int height) { ApplyCalls++; if (ApplySucceeds) Current = current with { Width = width, Height = height }; return ApplySucceeds; }
        public bool TryRestore(DisplayModeSnapshot original) { RestoreCalls++; if (RestoreSucceeds) Current = original; return RestoreSucceeds; }
    }
}
