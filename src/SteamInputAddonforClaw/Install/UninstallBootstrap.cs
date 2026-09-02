using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Lifecycle;
using SteamInputAddonforClaw.Profiles.Performance;

namespace SteamInputAddonforClaw.Install;

internal static class UninstallBootstrap
{
    internal static void RunFastCallbackOnly()
    {
        AppLog.Info("Uninstall", "Velopack uninstall cleanup started.", ("FastCallback", true));
        // PR12 section 11/18: the running Runtime owns stock restoration AND the startup-task removal
        // (which must come only AFTER stock authority is proven). The fast callback no longer deletes
        // the task unconditionally -- if the Runtime did not release, the mandatory startup guarantee
        // must stay in place.
        var runtimeReleased = RequestRunningRuntimeShutdown();

        if (!runtimeReleased)
        {
            AppLog.Warn("Uninstall", "Runtime ownership was not released; preserving Addon-owned artifacts, startup registration, and recovery evidence.", null, ("Action", "PreserveDependencySafety"));
            return;
        }

        // Backstop only: the Runtime's PrepareForUninstallAsync already removed the task after proving
        // stock authority. This is an idempotent no-op on the common path (task already absent).
        try { WindowsTaskSchedulerStartupManager.WithElevatedRepair().Synchronize(false); } catch (Exception exception) { AppLog.Warn("Uninstall", "Startup registration cleanup backstop failed.", exception); }

        RunBoundedLocalCleanup(runtimeReleased);

        AppLog.Info("Uninstall", "FastCallback completed without elevation or dependency teardown.", ("Action", "BoundedOnly"));
    }

    internal static void RunBoundedLocalCleanup(bool runtimeReleased)
    {
        if (!runtimeReleased)
            return;
        var cefCleaned = Steam.SteamCefDebugBootstrap.RemoveOwnedMarker();
        var fpsCleaned = TryCleanupOwnedIntelFpsForUninstall();
        TryDeleteDirectory(CenterM.CenterMHelperStaging.RuntimeDirectory);
        TryDeleteFile(VelopackAppPaths.LegacyHidHideProvisioningReceiptPath);
        if (cefCleaned && fpsCleaned && !File.Exists(AddonDataPaths.RecoveryJournalPath))
            AddonDataPaths.DeleteFullResetRoot(VelopackAppPaths.RootAppDirectory);
    }

    // This is deliberately a feature-local cleanup path. The marker is the only evidence that
    // the Addon owns the global Intel limiter; without it uninstall must not touch Intel state.
    internal static bool TryCleanupOwnedIntelFpsForUninstall(
        string? ownershipPath = null,
        Func<string?, IIntelFrameLimiter>? limiterFactory = null)
    {
        ownershipPath ??= AddonDataPaths.IntelFpsLimitOwnershipPath;
        if (!File.Exists(ownershipPath)) return true;

        try
        {
            using var limiter = (limiterFactory ?? (path => new IntelFrameLimiter(path)))(ownershipPath);
            limiter.Initialize();
            // Cleanup is intentionally independent from the 40-120 user-facing capability
            // contract. A previously owned global limiter must still be retired after a driver
            // update narrows that contract, as long as FRAME_LIMIT remains reachable.
            if (!limiter.Disable(null, 0))
            {
                AppLog.Warn("Uninstall", "Owned Intel FPS limiter cleanup failed; preserving ownership evidence.");
                return false;
            }

            File.Delete(ownershipPath);
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Warn("Uninstall", "Intel FPS ownership cleanup failed; preserving ownership evidence.", exception,
                ("OwnershipPath", ownershipPath));
            return false;
        }
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
