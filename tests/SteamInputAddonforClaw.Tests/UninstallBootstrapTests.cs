using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Lifecycle;
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

        try
        {
            UninstallBootstrap.RunBoundedLocalCleanup(runtimeReleased: false);
            Assert.True(File.Exists(evidencePath));
        }
        finally
        {
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
}
