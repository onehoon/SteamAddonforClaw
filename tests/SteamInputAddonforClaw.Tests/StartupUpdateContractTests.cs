using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class StartupUpdateContractTests
{
    [Fact]
    public void Velopack_early_auto_apply_is_disabled()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw", "Program.cs");

        Assert.Contains(".SetAutoApplyOnStartup(false)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Pending_apply_is_after_secondary_instance_activation_and_before_runtime_start()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw", "Program.cs");
        var secondaryActivation = source.IndexOf("singleInstanceGate.ActivatePrimaryInstance()", StringComparison.Ordinal);
        var primaryBoundary = source.IndexOf("using (singleInstanceGate)", StringComparison.Ordinal);
        var pendingApply = source.IndexOf("TrySchedulePendingUpdateApply(args)", StringComparison.Ordinal);
        var runtimeStart = source.IndexOf("new RuntimeProcessApplication(args, singleInstanceGate)", StringComparison.Ordinal);

        Assert.True(secondaryActivation >= 0);
        Assert.True(primaryBoundary > secondaryActivation);
        Assert.True(pendingApply > primaryBoundary);
        Assert.True(runtimeStart > pendingApply);
    }

    [Fact]
    public void Controller_startup_completes_before_background_update_check_starts()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs");
        var deferredStart = source.IndexOf("internal void StartDeferredRuntimeStartup()", StringComparison.Ordinal);
        var controllerAttempt = source.IndexOf("TryStartDisabledModeControllerAsync(", deferredStart, StringComparison.Ordinal);
        var pendingCleared = source.IndexOf("Volatile.Write(ref _disabledControllerStartupPending, 0)", controllerAttempt, StringComparison.Ordinal);
        var backgroundStart = source.IndexOf("StartBackgroundUpdate();", pendingCleared, StringComparison.Ordinal);

        Assert.True(deferredStart >= 0);
        Assert.True(controllerAttempt > deferredStart);
        Assert.True(pendingCleared > controllerAttempt);
        Assert.True(backgroundStart > pendingCleared);
    }

    [Fact]
    public void Startup_coordinator_has_no_network_update_dependency()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw", "Startup", "StartupCoordinator.cs");

        Assert.DoesNotContain("IUpdateGate", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SilentUpdateGate", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckForUpdatesAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DownloadUpdatesAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Background_update_service_contains_no_apply_or_restart_call()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw", "Updates", "SilentUpdateService.cs");

        Assert.DoesNotContain("WaitExitThenApplyUpdates", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyUpdatesAndRestart", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] pathParts)
    {
        var root = RepositoryRoot();
        return File.ReadAllText(Path.Combine([root, .. pathParts]));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
                return directory.FullName;

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
