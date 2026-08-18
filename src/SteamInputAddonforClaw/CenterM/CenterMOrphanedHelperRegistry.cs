using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// Review 4957630432 finding #3 (MAJOR): the concrete, testable terminal ownership policy for a
/// <see cref="CenterMHelperOwnership"/> whose exact retained helper handle still could not be
/// confirmed terminated by the time its owning <see cref="CenterMOem1LifecycleCoordinator"/> reached
/// its terminal disposed state. <see cref="CenterMOem1LifecycleCoordinator"/> constructs its
/// <see cref="CenterMHelperOwnership"/> internally as a private field by default -- there is no
/// caller-visible or process-level owner that could otherwise perform the promised later exact-handle
/// retry once <see cref="CenterMOem1LifecycleCoordinator.DisposeAsync"/> has made the coordinator
/// itself unusable. This process-wide static registry is that explicit, discoverable
/// process-shutdown/recovery owner: <see cref="CenterMOem1LifecycleCoordinator.DisposeAsync"/>
/// registers the SAME <see cref="CenterMHelperOwnership"/> instance here (never a copy, never
/// discarded) whenever it reaches terminal disposal with unresolved exact ownership still remaining,
/// regardless of whether that instance was caller-injected or created internally by the coordinator's
/// default construction path.
///
/// Deliberately dormant: registration never itself terminates anything, and nothing in this PR calls
/// <see cref="TryRetryAll"/> -- it exists so a future PR's process-shutdown/recovery composition seam
/// has a real, testable place to attempt the promised exact-handle retry (e.g. from a process-exit
/// handler), matching every other production-dormant surface in this PR.
/// </summary>
internal static class CenterMOrphanedHelperRegistry
{
    private static readonly object _sync = new();
    private static readonly List<CenterMHelperOwnership> _orphans = [];

    /// <summary>Registers <paramref name="ownership"/> as an unresolved terminal orphan. A no-op if
    /// it is not (or is no longer) owned, or if it is already registered -- idempotent so a
    /// coordinator's own bounded retry attempts can call this defensively.</summary>
    internal static void Register(CenterMHelperOwnership ownership)
    {
        lock (_sync)
        {
            if (!ownership.IsOwned) return;
            if (!_orphans.Contains(ownership)) _orphans.Add(ownership);
        }
        AppLog.Warn("CenterM.Helper", "Unresolved exact helper ownership registered with the process-level orphan retry owner.", null, ("ProcessId", ownership.ProcessId));
    }

    /// <summary>True while <paramref name="ownership"/> is currently registered as an unresolved
    /// orphan.</summary>
    internal static bool Contains(CenterMHelperOwnership ownership)
    {
        lock (_sync) return _orphans.Contains(ownership);
    }

    internal static int RegisteredCount
    {
        get { lock (_sync) return _orphans.Count; }
    }

    /// <summary>Attempts <see cref="CenterMHelperOwnership.Stop"/> against every still-owned
    /// registered instance (via its own exact retained handle -- never name/PID rediscovery) and
    /// removes any that resolve or are no longer owned. Returns the number still unresolved
    /// afterward. Safe to call repeatedly; never invoked by this PR's own code paths.</summary>
    internal static int TryRetryAll(TimeSpan waitTimeout)
    {
        lock (_sync)
        {
            _orphans.RemoveAll(o => !o.IsOwned || o.Stop(waitTimeout));
            return _orphans.Count;
        }
    }

    /// <summary>Test-only: clears all registered orphans. This registry is process-wide static
    /// state, so tests that exercise it must reset between runs. Never called by production code.
    /// </summary>
    internal static void TestOnly_Clear()
    {
        lock (_sync) _orphans.Clear();
    }
}
