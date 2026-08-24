using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Lifecycle;

namespace SteamInputAddonforClaw.Install;

internal static class UninstallBootstrap
{
    internal static void RunFastCallbackOnly()
    {
        AppLog.Info("Uninstall", "Velopack uninstall cleanup started.", ("FastCallback", true));
        var runtimeReleased = RequestRunningRuntimeShutdown();
        try { new WindowsTaskSchedulerStartupManager().Synchronize(false); } catch (Exception exception) { AppLog.Warn("Uninstall", "Startup registration cleanup failed.", exception); }

        if (!runtimeReleased)
        {
            AppLog.Warn("Uninstall", "Runtime ownership was not released; preserving Addon-owned artifacts and recovery evidence.", null, ("Action", "PreserveDependencySafety"));
            return;
        }

        RunBoundedLocalCleanup(runtimeReleased);

        AppLog.Info("Uninstall", "FastCallback completed without elevation or dependency teardown.", ("Action", "BoundedOnly"));
    }

    internal static void RunBoundedLocalCleanup(bool runtimeReleased)
    {
        if (!runtimeReleased)
            return;
        var cefCleaned = Steam.SteamCefDebugBootstrap.RemoveOwnedMarker();
        TryDeleteDirectory(CenterM.CenterMHelperStaging.RuntimeDirectory);
        TryDeleteFile(VelopackAppPaths.LegacyHidHideProvisioningReceiptPath);
        if (cefCleaned && !File.Exists(AddonDataPaths.RecoveryJournalPath))
            AddonDataPaths.DeleteFullResetRoot(VelopackAppPaths.RootAppDirectory);
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception exception) { AppLog.Warn("Uninstall", "Bounded Addon-owned directory cleanup failed.", exception, ("Path", path)); }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) { AppLog.Warn("Uninstall", "Bounded Addon-owned file cleanup failed.", exception, ("Path", path)); }
    }

    private static bool RequestRunningRuntimeShutdown()
    {
        try
        {
            using (var initialProbe = SingleInstanceGate.CreateForCurrentUser())
            {
                if (initialProbe.IsPrimaryInstance) return true;
            }

            if (!SingleInstanceGate.RequestPrimaryUninstall()) return false;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < deadline)
            {
                try
                {
                    using var probe = SingleInstanceGate.CreateForCurrentUser();
                    if (probe.IsPrimaryInstance) return true;
                }
                catch { return false; }
                Thread.Sleep(100);
            }
            AppLog.Warn("Uninstall", "Running Addon did not release its single-instance ownership before the bounded shutdown wait expired.", null, ("Action", "PreserveDependencySafety"));
            return false;
        }
        catch (Exception exception) { AppLog.Warn("Uninstall", "Running Addon shutdown request failed.", exception); return false; }
    }

}
