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
        var pendingApply = source.IndexOf("TrySchedulePendingUpdateApply(runtimeArgs)", StringComparison.Ordinal);
        var runtimeStart = source.IndexOf("new RuntimeProcessApplication(runtimeArgs, singleInstanceGate)", StringComparison.Ordinal);

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
    public void Always_on_performance_setup_is_queued_after_existing_startup_reconciliation()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs");
        var deferredStart = source.IndexOf("internal void StartDeferredRuntimeStartup()", StringComparison.Ordinal);
        var deferredEnd = source.IndexOf("private void StartAlwaysOnPerformanceSetup()", deferredStart, StringComparison.Ordinal);
        var deferred = source[deferredStart..deferredEnd];
        var controllerStartup = deferred.IndexOf("TryStartDisabledModeControllerAsync(", StringComparison.Ordinal);
        var profileStartup = deferred.IndexOf("ReconcileDeviceProfileStartup();", StringComparison.Ordinal);
        var backgroundUpdate = deferred.IndexOf("StartBackgroundUpdate();", StringComparison.Ordinal);
        var setupQueue = deferred.IndexOf("StartAlwaysOnPerformanceSetup();", StringComparison.Ordinal);
        var setupEnd = source.IndexOf("private void StartDisabledBootAdmissionRetryIfEligible()", deferredEnd, StringComparison.Ordinal);
        var setupMethod = source[deferredEnd..setupEnd];

        Assert.True(controllerStartup >= 0);
        Assert.True(profileStartup > controllerStartup);
        Assert.True(backgroundUpdate > profileStartup);
        Assert.True(setupQueue > backgroundUpdate);
        Assert.Contains("Task.Run(", setupMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("await ", setupMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", setupMethod, StringComparison.Ordinal);
        Assert.Contains("_startupCancellationTokenSource.Token", setupMethod, StringComparison.Ordinal);
        Assert.Contains("Configuration: null", setupMethod, StringComparison.Ordinal);
        Assert.Contains("tdpRuntime.SetEnabled(true)", setupMethod, StringComparison.Ordinal);
        Assert.Contains("if (!tdpSeeded && _tdpRuntime is", setupMethod, StringComparison.Ordinal);
        Assert.Contains("catch (Exception exception)", setupMethod, StringComparison.Ordinal);
        Assert.Contains("_alwaysOnPerformanceSetupTask.ConfigureAwait(false)", source, StringComparison.Ordinal);
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
