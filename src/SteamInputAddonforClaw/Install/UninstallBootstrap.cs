using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Lifecycle;
using SteamInputAddonforClaw.Profiles.Performance;
using SteamInputAddonforClaw.WindowsGaming;

namespace SteamInputAddonforClaw.Install;

internal static class UninstallBootstrap
{
    internal const string SafeUninstallApprovedEnvironmentVariable = "STEAMADDON_SAFE_UNINSTALL_APPROVED";

    internal static bool IsSafeUninstallApproved => string.Equals(
        Environment.GetEnvironmentVariable(SafeUninstallApprovedEnvironmentVariable), "1", StringComparison.Ordinal);

    internal static void RunFastCallbackOnly()
    {
        if (IsSafeUninstallApproved)
            return;

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

        // PR12 section 18 / review [P1]: the fast callback does NOT own startup-task removal. A gone
        // Runtime process only proves the mutex disappeared -- NOT that PR12 stock preparation
        // succeeded -- so removing the mandatory startup task here could strip the guarantee while
        // Center M is still Disabled, and it must never launch a UAC flow from this callback. That
        // mutation belongs exclusively to a successful Runtime PrepareForUninstallAsync.
        RunBoundedLocalCleanup(runtimeReleased);

        AppLog.Info("Uninstall", "FastCallback completed without elevation or dependency teardown.", ("Action", "BoundedOnly"));
    }

    internal static bool RunBoundedLocalCleanup(bool runtimeReleased)
    {
        if (!runtimeReleased)
            return false;
        var cefCleaned = Steam.SteamCefLegacyMarkerCleanup.RemoveOwnedMarker();
        var fpsCleaned = TryCleanupOwnedIntelFpsForUninstall();
        var steamFseCleaned = new WindowsGamingHomeConfiguration().TryCleanupForUninstall();
        var legacyReceiptCleaned = TryDeleteFile(VelopackAppPaths.LegacyHidHideProvisioningReceiptPath);
        var succeeded = cefCleaned && fpsCleaned && steamFseCleaned && legacyReceiptCleaned;
        AppLog.Info("Uninstall", "Bounded Addon-owned local cleanup completed.",
            ("Succeeded", succeeded), ("CefMarkerCleaned", cefCleaned), ("IntelFpsCleaned", fpsCleaned),
            ("SteamFseCleaned", steamFseCleaned), ("LegacyReceiptCleaned", legacyReceiptCleaned),
            ("UserDataRootPreserved", true));
        return succeeded;
    }

    internal static SingleInstanceGate? AcquireRuntimeGateForSafeUninstall(
        TimeSpan waitBudget,
        TimeSpan probeInterval,
        Func<SingleInstanceGate>? createGate = null,
        Func<bool>? requestPrimaryUninstall = null,
        Func<DateTimeOffset>? utcNow = null,
        Action<TimeSpan>? delay = null)
    {
        if (waitBudget <= TimeSpan.Zero || probeInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(waitBudget));

        createGate ??= SingleInstanceGate.CreateForCurrentUser;
        requestPrimaryUninstall ??= SingleInstanceGate.RequestPrimaryUninstall;
        utcNow ??= static () => DateTimeOffset.UtcNow;
        delay ??= static duration => Thread.Sleep(duration);

        try
        {
            var initial = createGate();
            if (initial.IsPrimaryInstance) return initial;
            initial.Dispose();

            if (!requestPrimaryUninstall())
            {
                AppLog.Warn("Uninstall", "Running Runtime uninstall request could not be signaled.");
                return null;
            }

            var deadline = utcNow() + waitBudget;
            while (utcNow() < deadline)
            {
                var probe = createGate();
                if (probe.IsPrimaryInstance) return probe;
                probe.Dispose();
                var remaining = deadline - utcNow();
                if (remaining > TimeSpan.Zero)
                    delay(remaining < probeInterval ? remaining : probeInterval);
            }

            AppLog.Warn("Uninstall", "Running Runtime did not release its mutex within the safe-uninstall wait budget.", null,
                ("WaitBudgetMs", waitBudget.TotalMilliseconds));
            return null;
        }
        catch (Exception exception)
        {
            AppLog.Error("Uninstall", "Runtime mutex acquisition for safe uninstall failed.", exception);
            return null;
        }
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
            if (!limiter.Disable(null))
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

    private static bool TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); return !File.Exists(path); }
        catch (Exception exception)
        {
            AppLog.Warn("Uninstall", "Bounded Addon-owned file cleanup failed.", exception, ("Path", path));
            return false;
        }
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
