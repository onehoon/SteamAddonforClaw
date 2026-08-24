using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Lifecycle;

namespace SteamInputAddonforClaw.Install;

internal static class UninstallBootstrap
{
    internal static void RunFastCallbackOnly()
    {
        AppLog.Info("Uninstall", "Velopack uninstall cleanup started.", ("FastCallback", true));
        _ = RequestRunningRuntimeShutdown();
        try { new WindowsTaskSchedulerStartupManager().Synchronize(false); } catch (Exception exception) { AppLog.Warn("Uninstall", "Startup registration cleanup failed.", exception); }
        AppLog.Info("Uninstall", "FastCallback completed without elevation or dependency teardown.", ("Action", "BoundedOnly"));
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
