using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Lifecycle;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Profiles.Performance;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UninstallBootstrapTests
{
    [Fact]
    public void Data_root_is_the_full_reset_root()
    {
        var root = AddonDataPaths.ResolveDataRoot("C:\\Users\\Test\\App");
        Assert.EndsWith("SteamInputAddonforClaw-Data", root, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bounded_local_cleanup_preserves_artifacts_when_runtime_release_failed()
    {
        var root = Path.Combine(Path.GetTempPath(), $"uninstall-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var evidencePath = Path.Combine(root, "steam-cef-marker.json");
        File.WriteAllText(evidencePath, "owned-evidence");
        var previousOwnershipPathProvider = SteamCefDebugBootstrap.OwnershipPathProvider;

        try
        {
            SteamCefDebugBootstrap.OwnershipPathProvider = () => evidencePath;
            UninstallBootstrap.RunBoundedLocalCleanup(runtimeReleased: false);
            Assert.True(File.Exists(evidencePath));
        }
        finally
        {
            SteamCefDebugBootstrap.OwnershipPathProvider = previousOwnershipPathProvider;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Single_instance_gate_can_dispose_after_uninstall_handler_registration()
    {
        using var gate = new SingleInstanceGate(
            $"Local\\SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}",
            $"Local\\SteamInputAddonforClaw.Tests.Activate.{Guid.NewGuid():N}");

        if (gate.IsPrimaryInstance)
            gate.RegisterUninstallRequest(static () => { });
    }

    [Fact]
    public void Single_instance_gate_rejects_duplicate_uninstall_handler_registration()
    {
        using var gate = new SingleInstanceGate(
            $"Local\\SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}",
            $"Local\\SteamInputAddonforClaw.Tests.Activate.{Guid.NewGuid():N}");

        if (gate.IsPrimaryInstance)
        {
            gate.RegisterUninstallRequest(static () => { });
            Assert.Throws<InvalidOperationException>(() => gate.RegisterUninstallRequest(static () => { }));
        }
    }

    [Fact]
    public void Stale_fps_marker_failed_cleanup_preserves_ownership_evidence()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"intel-fps-{Guid.NewGuid():N}.json");
        File.WriteAllText(marker, "{\"fps\":60}");
        try
        {
            var limiter = new FakeIntelFrameLimiter { DisableResult = false };
            Assert.False(UninstallBootstrap.TryCleanupOwnedIntelFpsForUninstall(marker, _ => limiter));
            Assert.True(File.Exists(marker));
            Assert.Equal(1, limiter.DisableCalls);
        }
        finally { if (File.Exists(marker)) File.Delete(marker); }
    }

    [Fact]
    public void Stale_fps_marker_successful_cleanup_removes_ownership_evidence()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"intel-fps-{Guid.NewGuid():N}.json");
        File.WriteAllText(marker, "{\"fps\":60}");
        var limiter = new FakeIntelFrameLimiter();
        Assert.True(UninstallBootstrap.TryCleanupOwnedIntelFpsForUninstall(marker, _ => limiter));
        Assert.False(File.Exists(marker));
        Assert.Equal(1, limiter.DisableCalls);
    }

    [Fact]
    public void Stale_fps_marker_cleanup_does_not_require_user_facing_availability()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"intel-fps-{Guid.NewGuid():N}.json");
        File.WriteAllText(marker, "{\"fps\":60}");
        var limiter = new FakeIntelFrameLimiter { AvailableValue = false };
        Assert.True(UninstallBootstrap.TryCleanupOwnedIntelFpsForUninstall(marker, _ => limiter));
        Assert.False(File.Exists(marker));
        Assert.Equal(1, limiter.DisableCalls);
    }

    private sealed class FakeIntelFrameLimiter : IIntelFrameLimiter
    {
        public bool DisableResult { get; init; } = true;
        public int DisableCalls { get; private set; }
        public void Initialize() { }
        public bool Available => AvailableValue;
        public bool AvailableValue { get; init; } = true;
        public string? UnavailableReason => null;
        public IntelFpsCapability? Capability => null;
        public IntelFpsApplyOutcome Enable(int fps, FpsPowerSource source, uint appId) => IntelFpsApplyOutcome.Verified;
        public bool Disable(FpsPowerSource? source, uint appId) { DisableCalls++; return DisableResult; }
        public void Dispose() { }
    }
}
