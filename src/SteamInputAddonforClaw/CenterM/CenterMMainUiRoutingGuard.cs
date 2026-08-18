using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// Outcome of an arm attempt. <see cref="Armed"/> is the only state from which routing may proceed
/// to native-mode/PID1902 mutation; every other value means routing entry must not continue.
/// </summary>
internal enum CenterMMainUiRoutingGuardResult
{
    Armed,
    RealMainUiPresent,
    PrerequisiteFailure,
    HelperFailure,
    MutexFailure,
    InvariantFailure,
    Uncertain
}

/// <summary>
/// Arms/disarms transient, routing-time prevention of a NEW real MSI Center M MainUI becoming
/// operational, so PID1902/DirectInput routing can remain authoritative while Steam routing is
/// active. This is Phase 1 only: it never terminates an already-running real MainUI (arm simply
/// refuses when one is present) and it has no knowledge of OEM1 gestures, Quick Access, Game Bar,
/// VIIPER, or native controller-mode mutation -- routing calls <see cref="ArmAsync"/> before any of
/// that, and <see cref="DisarmAsync"/> only after native/output mutation has already been rolled
/// back or classified.
///
/// Arm sequence (safety-critical ordering, research: MSI_COMPLETE_RESEARCH_RESULT.md section 4 --
/// the real MainUI's own duplicate-instance check, keyed on the same
/// <see cref="CenterMMainUiMutexOwnership.MutexName"/> this class owns, runs before
/// <c>MainWindow</c>/controller-mode initialization):
/// 1. fresh same-name process snapshot -- any match means a real MainUI may already be present;
/// 2. stage + start the dedicated helper (process-name half of the guard);
/// 3. acquire the MainUI mutex (mutex half of the guard);
/// 4. fresh same-name snapshot again, verified via the existing
///    <see cref="CenterMHelperInvariant"/> -- the only same-name process must be the owned helper.
/// Any failure at any step unwinds only what this attempt itself acquired and never commits Armed.
/// </summary>
internal sealed class CenterMMainUiRoutingGuard : IAsyncDisposable
{
    /// <summary>Bounded number of additional exact-handle Stop attempts terminal disposal makes
    /// before handing unresolved ownership to <see cref="CenterMOrphanedHelperRegistry"/>, mirroring
    /// <see cref="CenterMOem1LifecycleCoordinator"/>'s own terminal cleanup policy for the same
    /// <see cref="CenterMHelperOwnership"/> primitive.</summary>
    private const int DisposeFinalCleanupAttempts = 3;

    private readonly Func<string> _publishRootProvider;
    private readonly IProcessSnapshotSource _processSnapshotSource;
    private readonly CenterMHelperOwnership _helperOwnership;
    private readonly CenterMMainUiMutexOwnership _mutexOwnership;
    private readonly Func<string, string?> _stager;
    private readonly TimeSpan _helperStopTimeout;
    /// <summary>Phase 2: retires an already-running real MainUI (tray-resident or visible) before
    /// the helper/mutex arm sequence below. Null is a valid, fully-Phase-1-compatible configuration
    /// -- an existing real MainUI then still simply refuses to arm, exactly as before.</summary>
    private readonly CenterMMainUiRoutingRetirement? _retirement;
    // Guards the ENTIRE Arm/Disarm transaction (not just the _armed flag) -- Arm does multiple
    // sequential native operations (stage, start helper, acquire mutex, re-verify), and a second
    // concurrent Arm/Disarm observing a stale _armed value mid-sequence could stop a helper the
    // first call just started, or publish Armed after a Disarm already became authoritative. A
    // SemaphoreSlim (not a plain lock) is used because the transaction awaits.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _armed;

    internal CenterMMainUiRoutingGuard(
        Func<string>? publishRootProvider = null,
        IProcessSnapshotSource? processSnapshotSource = null,
        CenterMHelperOwnership? helperOwnership = null,
        CenterMMainUiMutexOwnership? mutexOwnership = null,
        Func<string, string?>? stager = null,
        TimeSpan? helperStopTimeout = null,
        CenterMMainUiRoutingRetirement? retirement = null)
    {
        _publishRootProvider = publishRootProvider ?? (() => AppContext.BaseDirectory);
        _processSnapshotSource = processSnapshotSource ?? new Win32ProcessSnapshotSource();
        _helperOwnership = helperOwnership ?? new CenterMHelperOwnership();
        _mutexOwnership = mutexOwnership ?? new CenterMMainUiMutexOwnership();
        _stager = stager ?? CenterMHelperStaging.StageFromPublishRoot;
        _helperStopTimeout = helperStopTimeout ?? TimeSpan.FromSeconds(5);
        _retirement = retirement;
    }

    internal bool IsArmed => _armed;

    internal async Task<CenterMMainUiRoutingGuardResult> ArmAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Idempotent: a duplicate arm request while already armed is a confirmation, not a
            // fresh attempt -- it must never stage a second helper or re-acquire the mutex.
            if (_armed) return CenterMMainUiRoutingGuardResult.Armed;

            return await ArmCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task<CenterMMainUiRoutingGuardResult> ArmCoreAsync(CancellationToken cancellationToken)
    {
        AppLog.Debug("CenterM.RoutingGuard", "Routing guard arm started.");

        var beforeSnapshot = _processSnapshotSource.GetProcessesByName(CenterMProcessNames.MainUi);
        if (beforeSnapshot is null)
        {
            AppLog.Warn("CenterM.RoutingGuard", "Routing guard arm failed: same-name process enumeration was uncertain.", null);
            return CenterMMainUiRoutingGuardResult.Uncertain;
        }

        if (beforeSnapshot.Count > 0)
        {
            if (_retirement is null)
            {
                // Phase 1 behavior, preserved exactly when no retirement service is configured.
                AppLog.Info("CenterM.RoutingGuard", "Real MainUI present; routing guard will not arm.", ("Reason", "RealMainUiPresent"), ("MainUiProcessId", beforeSnapshot[0].ProcessId));
                return CenterMMainUiRoutingGuardResult.RealMainUiPresent;
            }

            var retirementResult = await _retirement.PrepareExistingMainUiForRoutingAsync(cancellationToken).ConfigureAwait(false);
            if (retirementResult is not (CenterMMainUiRoutingRetirementResult.NoMainUiPresent or CenterMMainUiRoutingRetirementResult.Retired))
            {
                return retirementResult switch
                {
                    CenterMMainUiRoutingRetirementResult.IdentityUncertain
                        or CenterMMainUiRoutingRetirementResult.WindowStateUncertain
                        or CenterMMainUiRoutingRetirementResult.AbsenceCheckFailed => CenterMMainUiRoutingGuardResult.Uncertain,
                    _ => CenterMMainUiRoutingGuardResult.RealMainUiPresent
                };
            }
        }

        // Do not trust beforeSnapshot for the arm-continuation decision below: retirement (if it
        // ran) already performed its own fresh absence verification, and if no real MainUI was ever
        // present this snapshot was already empty.
        var publishRoot = _publishRootProvider();
        var stagedPath = _stager(publishRoot);
        if (stagedPath is null)
        {
            AppLog.Warn("CenterM.RoutingGuard", "Routing guard arm failed: helper staging failed.", null);
            return CenterMMainUiRoutingGuardResult.HelperFailure;
        }

        // Safe pre-mutation cancellation point: nothing has been created yet, so there is nothing
        // to unwind -- a cancelled Enter should not still create a helper/mutex only to immediately
        // tear them down via the pipeline's subsequent rollback.
        if (cancellationToken.IsCancellationRequested)
        {
            AppLog.Info("CenterM.RoutingGuard", "Routing guard arm cancelled before any resource was created.", ("Action", "NoOp"));
            cancellationToken.ThrowIfCancellationRequested();
        }

        var startResult = _helperOwnership.Start(stagedPath);
        if (startResult != HelperStartResult.Started)
        {
            AppLog.Warn("CenterM.RoutingGuard", "Routing guard arm failed: helper did not start.", null, ("Result", startResult));
            await UnwindAsync().ConfigureAwait(false);
            return CenterMMainUiRoutingGuardResult.HelperFailure;
        }
        AppLog.Info("CenterM.RoutingGuard", "Helper started.", ("HelperProcessId", _helperOwnership.ProcessId));

        var mutexResult = _mutexOwnership.Acquire();
        if (mutexResult == CenterMMainUiMutexAcquireResult.Unavailable)
        {
            AppLog.Warn("CenterM.RoutingGuard", "Routing guard arm failed: MainUI mutex unavailable.", null);
            await UnwindAsync().ConfigureAwait(false);
            return CenterMMainUiRoutingGuardResult.MutexFailure;
        }
        AppLog.Info("CenterM.RoutingGuard", "MainUI mutex acquired.");

        // Do not trust the beforeSnapshot taken above: a real MainUI (or another same-name process)
        // could have appeared during helper start/mutex acquisition. This must be a fresh capture.
        var afterSnapshot = _processSnapshotSource.GetProcessesByName(CenterMProcessNames.MainUi);
        if (afterSnapshot is null)
        {
            AppLog.Warn("CenterM.RoutingGuard", "Routing guard arm failed: post-arm same-name enumeration was uncertain.", null);
            await UnwindAsync().ConfigureAwait(false);
            return CenterMMainUiRoutingGuardResult.Uncertain;
        }

        var invariant = CenterMHelperInvariant.Evaluate(afterSnapshot, _helperOwnership.ProcessId!.Value);
        if (invariant != CenterMHelperInvariantState.Valid)
        {
            AppLog.Warn("CenterM.RoutingGuard", "Routing guard arm failed: helper invariant check failed.", null, ("Invariant", invariant));
            await UnwindAsync().ConfigureAwait(false);

            // A foreign same-name process appearing during arm is only ever a reason to abandon
            // this arm attempt -- it is never terminated here (Phase 1 policy, section 13/19).
            return invariant switch
            {
                CenterMHelperInvariantState.Uncertain => CenterMMainUiRoutingGuardResult.Uncertain,
                CenterMHelperInvariantState.ForeignProcessDetected or CenterMHelperInvariantState.MultipleSameNameProcesses
                    => CenterMMainUiRoutingGuardResult.RealMainUiPresent,
                _ => CenterMMainUiRoutingGuardResult.InvariantFailure
            };
        }

        if (cancellationToken.IsCancellationRequested)
        {
            // Every check has passed, but the caller no longer wants this Enter -- unwind what was
            // just acquired rather than publishing Armed for a transition about to be rolled back
            // anyway.
            AppLog.Info("CenterM.RoutingGuard", "Routing guard arm cancelled just before Armed publication; unwinding.");
            await UnwindAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        _armed = true;
        AppLog.Info("CenterM.RoutingGuard", "Routing guard armed.", ("HelperProcessId", _helperOwnership.ProcessId));
        return CenterMMainUiRoutingGuardResult.Armed;
    }

    /// <summary>Idempotent and safe to call even when never armed (a failed/never-attempted arm
    /// leaves nothing to release). Returns false only when the owned helper's cleanup could not be
    /// confirmed -- callers must not treat that as a clean disarm (fail-closed, matching
    /// <see cref="CenterMHelperOwnership.Stop"/>'s own contract). Serialized against
    /// <see cref="ArmAsync"/> through the same gate -- an overlapping Disarm can never race a
    /// concurrent Arm's helper/mutex acquisition, and a Disarm always wins any race for the gate
    /// over a not-yet-started Arm (whichever acquires the gate first runs to completion before the
    /// other is admitted).</summary>
    internal async Task<bool> DisarmAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppLog.Debug("CenterM.RoutingGuard", "Routing guard disarm started.");
            _armed = false;
            var confirmed = await UnwindAsync().ConfigureAwait(false);
            AppLog.Info("CenterM.RoutingGuard", "Routing guard disarmed.", ("Confirmed", confirmed));
            return confirmed;
        }
        finally { _gate.Release(); }
    }

    private Task<bool> UnwindAsync()
    {
        var helperStopped = true;
        if (_helperOwnership.IsOwned)
        {
            helperStopped = _helperOwnership.Stop(_helperStopTimeout);
            AppLog.Info("CenterM.RoutingGuard", "Helper stop attempted.", ("Confirmed", helperStopped), ("HelperProcessId", _helperOwnership.ProcessId));
        }

        _mutexOwnership.Release();
        AppLog.Info("CenterM.RoutingGuard", "MainUI mutex released.");
        return Task.FromResult(helperStopped);
    }

    /// <summary>
    /// Terminal cleanup: a bounded number of additional exact-handle Stop attempts beyond whatever
    /// the normal Disarm path already tried, and -- if the exact helper handle is still unresolved
    /// after those -- hands the SAME <see cref="CenterMHelperOwnership"/> instance (never a copy,
    /// never discarded) to the process-level <see cref="CenterMOrphanedHelperRegistry"/>, mirroring
    /// <see cref="CenterMOem1LifecycleCoordinator.DisposeAsync"/>'s terminal policy for the same
    /// primitive. Never falls back to process-name/PID rediscovery. Idempotent.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            _armed = false;
            _mutexOwnership.Release();

            for (var attempt = 1; attempt <= DisposeFinalCleanupAttempts && _helperOwnership.IsOwned; attempt++)
            {
                if (_helperOwnership.Stop(_helperStopTimeout)) break;
                AppLog.Warn("CenterM.RoutingGuard", "Dispose final cleanup attempt could not confirm helper termination.", null, ("Attempt", attempt), ("ProcessId", _helperOwnership.ProcessId));
            }

            if (_helperOwnership.IsOwned)
            {
                CenterMOrphanedHelperRegistry.Register(_helperOwnership);
                AppLog.Warn("CenterM.RoutingGuard", "Terminal disposal completed with unresolved exact helper ownership; registered with the process-level orphan retry owner, not discarded.", null, ("ProcessId", _helperOwnership.ProcessId));
            }
        }
        finally { _gate.Release(); }
    }
}
