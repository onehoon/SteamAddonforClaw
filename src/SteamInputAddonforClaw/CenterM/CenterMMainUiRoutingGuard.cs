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
    Uncertain,
    /// <summary>The shared <see cref="CenterMHelperOwnership"/> retains ownership
    /// (<see cref="CenterMHelperOwnership.IsOwned"/>) but it is not operationally armed
    /// (<see cref="CenterMHelperOwnership.IsOperationallyOwned"/> is false) -- e.g. a job-less
    /// <see cref="HelperStartResult.PartialCleanupUnconfirmed"/> residue from a prior owner. The
    /// guard fails closed: it never starts a second helper, never discards the retained ownership,
    /// and never treats this as a clean native fallback.</summary>
    HelperOwnershipUnresolved
}

/// <summary>
/// Arms/disarms transient, routing-time prevention of a NEW real MSI Center M MainUI becoming
/// operational, so PID1902/DirectInput routing can remain authoritative while Steam routing is
/// active. Has no knowledge of OEM1 gestures, Quick Access, Game Bar, VIIPER, or native
/// controller-mode mutation of its own -- routing calls <see cref="ArmAsync"/> before any of that,
/// and <see cref="DisarmAsync"/> only after native/output mutation has already been rolled back or
/// classified.
///
/// When a <see cref="CenterMMainUiRoutingRetirement"/> service is configured (the production MSI
/// Claw composition), an existing exact real MainUI (tray-resident or visible) is first retired
/// under the Phase-2 tray/visible policy -- see that type's own remarks -- before the Phase-1
/// helper/mutex sequence below ever runs. When no retirement service is configured, Phase-1
/// behavior is preserved exactly: any existing same-name real MainUI causes arm to fail
/// (<see cref="CenterMMainUiRoutingGuardResult.RealMainUiPresent"/>) before helper/mutex/native-mode
/// mutation.
///
/// Arm sequence (safety-critical ordering, research: MSI_COMPLETE_RESEARCH_RESULT.md section 4 --
/// the real MainUI's own duplicate-instance check, keyed on the same
/// <see cref="CenterMMainUiMutexOwnership.MutexName"/> this class owns, runs before
/// <c>MainWindow</c>/controller-mode initialization):
/// 1. fresh same-name process snapshot -- any match means a real MainUI may already be present;
/// 2. if present and retirement is configured, retire it (tray XInput-verify-then-kill, or
///    visible minimize-then-hidden-then-XInput-verify-then-kill); otherwise fail immediately;
/// 3. stage + start the dedicated helper (process-name half of the guard);
/// 4. acquire the MainUI mutex (mutex half of the guard);
/// 5. fresh same-name snapshot again, verified via the existing
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
    private readonly Func<bool>? _persistentHelperOwnerReady;
    private readonly Func<CancellationToken, Task<bool>>? _releaseSharedHelper;
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
    private volatile bool _helperDemandActive;

    /// <summary>Per-arm "did I start it?" state (PR1 ownership convergence). True only when THIS
    /// arm attempt itself called <see cref="CenterMHelperOwnership.Start"/> and it succeeded --
    /// false when this arm instead borrowed an already-operational helper owned by an external
    /// authority (e.g. a future OEM1 lifecycle owner sharing the same <see cref="CenterMHelperOwnership"/>
    /// instance). Read only by <see cref="UnwindAsync"/> and terminal <see cref="DisposeAsync"/> to
    /// decide whether this guard may stop/register the helper -- a borrowed helper must never be
    /// stopped merely because Steam routing ended. Reset at the start of every arm attempt.</summary>
    private bool _helperStartedByCurrentArm;

    internal CenterMMainUiRoutingGuard(
        Func<string>? publishRootProvider = null,
        IProcessSnapshotSource? processSnapshotSource = null,
        CenterMHelperOwnership? helperOwnership = null,
        CenterMMainUiMutexOwnership? mutexOwnership = null,
        Func<string, string?>? stager = null,
        TimeSpan? helperStopTimeout = null,
        CenterMMainUiRoutingRetirement? retirement = null,
        Func<bool>? persistentHelperOwnerReady = null,
        Func<CancellationToken, Task<bool>>? releaseSharedHelper = null)
    {
        _publishRootProvider = publishRootProvider ?? (() => AppContext.BaseDirectory);
        _processSnapshotSource = processSnapshotSource ?? new Win32ProcessSnapshotSource();
        _helperOwnership = helperOwnership ?? new CenterMHelperOwnership();
        _mutexOwnership = mutexOwnership ?? new CenterMMainUiMutexOwnership();
        _stager = stager ?? CenterMHelperStaging.StageFromPublishRoot;
        _helperStopTimeout = helperStopTimeout ?? TimeSpan.FromSeconds(5);
        _retirement = retirement;
        _persistentHelperOwnerReady = persistentHelperOwnerReady;
        _releaseSharedHelper = releaseSharedHelper;
    }

    internal bool IsArmed => _armed;
    internal bool HasHelperDemand => _helperDemandActive;

    internal async Task<CenterMMainUiRoutingGuardResult> ReconcileOwnedStateAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_armed || !_mutexOwnership.IsOwned)
                return CenterMMainUiRoutingGuardResult.InvariantFailure;
            if (!_helperOwnership.IsOperationallyOwned)
                return CenterMMainUiRoutingGuardResult.HelperOwnershipUnresolved;

            var liveness = _helperOwnership.PollLiveness();
            if (liveness == HelperLivenessState.Exited)
                return CenterMMainUiRoutingGuardResult.HelperFailure;
            if (liveness != HelperLivenessState.Alive)
                return CenterMMainUiRoutingGuardResult.Uncertain;

            var sameName = _processSnapshotSource.GetProcessesByName(CenterMProcessNames.MainUi);
            var invariant = sameName is null
                ? CenterMHelperInvariantState.Uncertain
                : CenterMHelperInvariant.Evaluate(sameName, _helperOwnership.ProcessId!.Value);
            if (invariant == CenterMHelperInvariantState.Valid)
                return CenterMMainUiRoutingGuardResult.Armed;

            return invariant == CenterMHelperInvariantState.HelperMissing
                ? CenterMMainUiRoutingGuardResult.HelperFailure
                : invariant == CenterMHelperInvariantState.Uncertain
                    ? CenterMMainUiRoutingGuardResult.Uncertain
                    : CenterMMainUiRoutingGuardResult.InvariantFailure;
        }
        finally { _gate.Release(); }
    }

    internal async Task<CenterMMainUiRoutingGuardResult> ArmAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _helperDemandActive = true;
            // The production persistent-owner callback is serialized by the OEM1 coordinator gate.
            // Resolve it before entering the routing transaction so an OEM1 Stop already authorized
            // by that gate cannot race a later borrow decision.
            _ = PersistentHelperOwnerReady();
            // Idempotent: a duplicate arm request while already armed is a confirmation, not a
            // fresh attempt -- it must never stage a second helper or re-acquire the mutex.
            if (_armed) return CenterMMainUiRoutingGuardResult.Armed;

            var result = await ArmCoreAsync(cancellationToken).ConfigureAwait(false);
            if (result != CenterMMainUiRoutingGuardResult.Armed)
            {
                _helperDemandActive = false;
                await FinalizeFailedArmDemandAsync().ConfigureAwait(false);
            }
            return result;
        }
        catch
        {
            _helperDemandActive = false;
            await FinalizeFailedArmDemandAsync().ConfigureAwait(false);
            throw;
        }
        finally { _gate.Release(); }
    }

    private async Task<CenterMMainUiRoutingGuardResult> ArmCoreAsync(CancellationToken cancellationToken)
    {
        AppLog.Debug("CenterM.RoutingGuard", "Routing guard arm started.");

        // A previous arm from THIS guard still owns an unresolved exact handle (e.g. an unconfirmed
        // Stop during a prior DisarmAsync). Resetting the flag below before checking this would
        // erase the only local record that this guard -- not an external authority -- is
        // responsible for that retained ownership, so a later terminal DisposeAsync would wrongly
        // treat it as borrowed/unowned and skip its bounded retry/orphan-registration path. Fail
        // closed without touching the flag; a fresh arm may proceed once that responsibility is
        // actually resolved (Stop confirmed, or terminal Dispose has already taken it over).
        if (_helperStartedByCurrentArm && _helperOwnership.IsOwned)
        {
            AppLog.Warn("CenterM.RoutingGuard", "Routing guard cannot re-arm while cleanup from its previous helper is unresolved; failing closed.", null, ("ProcessId", _helperOwnership.ProcessId));
            return CenterMMainUiRoutingGuardResult.HelperOwnershipUnresolved;
        }

        // If the prior responsibility is already resolved, a fresh arm may classify ownership normally.
        _helperStartedByCurrentArm = false;

        // The shared CenterMHelperOwnership's own already-owned PID (if operationally armed) is
        // never itself a "real MainUI appeared" signal -- exclude it before deciding RealMainUiPresent,
        // matching CenterMOem1LifecycleCoordinator's own same-name/owned-PID distinction.
        var ownedPid = _helperOwnership.IsOperationallyOwned ? _helperOwnership.ProcessId : null;

        var beforeSnapshot = _processSnapshotSource.GetProcessesByName(CenterMProcessNames.MainUi);
        if (beforeSnapshot is null)
        {
            AppLog.Warn("CenterM.RoutingGuard", "Routing guard arm failed: same-name process enumeration was uncertain.", null);
            return CenterMMainUiRoutingGuardResult.Uncertain;
        }

        // Ownership resolution (PR1 ownership convergence) is checked BEFORE retirement ever runs:
        // IsOwned alone is not sufficient -- a job-less PartialCleanupUnconfirmed residue never
        // completed the operational arm sequence and must fail closed rather than being silently
        // replaced, borrowed, or (worse) used as the basis for retiring/terminating a real MainUI
        // while this guard's own helper ownership is itself unresolved.
        if (_helperOwnership.IsOwned && !_helperOwnership.IsOperationallyOwned)
        {
            AppLog.Warn("CenterM.RoutingGuard", "Routing guard arm failed: shared helper ownership is retained but not operational; failing closed.", null, ("ProcessId", _helperOwnership.ProcessId));
            return CenterMMainUiRoutingGuardResult.HelperOwnershipUnresolved;
        }

        var foreignBefore = beforeSnapshot.Where(p => p.ProcessId != ownedPid).ToList();
        if (foreignBefore.Count > 0)
        {
            if (_retirement is null)
            {
                // Phase 1 behavior, preserved exactly when no retirement service is configured.
                AppLog.Info("CenterM.RoutingGuard", "Real MainUI present; routing guard will not arm.", ("Reason", "RealMainUiPresent"), ("MainUiProcessId", foreignBefore[0].ProcessId));
                return CenterMMainUiRoutingGuardResult.RealMainUiPresent;
            }

            // The shared helper's own PID must never be treated as a routing-retirement candidate
            // -- retirement only ever acts on a same-name process that is NOT the already-owned
            // helper, matching the exact exclusion beforeSnapshot itself already applied above.
            var retirementResult = await _retirement.PrepareExistingMainUiForRoutingAsync(cancellationToken, ignoredSameNameProcessId: ownedPid).ConfigureAwait(false);
            if (retirementResult is not (CenterMMainUiRoutingRetirementResult.NoMainUiPresent or CenterMMainUiRoutingRetirementResult.Retired))
            {
                return retirementResult switch
                {
                    CenterMMainUiRoutingRetirementResult.IdentityUncertain
                        or CenterMMainUiRoutingRetirementResult.WindowStateUncertain
                        or CenterMMainUiRoutingRetirementResult.AbsenceCheckFailed => CenterMMainUiRoutingGuardResult.Uncertain,
                    // Not a Center M fact -- the canonical NativeMode route authority already
                    // rejected this route (routing-fault latch, recovery safety, power gate,
                    // active/recovery-boundary conflict) before the MainUI was ever touched, so
                    // diagnostics must not blame Center M for a prerequisite it had no part in.
                    CenterMMainUiRoutingRetirementResult.RoutingPreflightRejected => CenterMMainUiRoutingGuardResult.PrerequisiteFailure,
                    _ => CenterMMainUiRoutingGuardResult.RealMainUiPresent
                };
            }
        }

        // Do not trust beforeSnapshot for the arm-continuation decision below: retirement (if it
        // ran) already performed its own fresh absence verification, and if no real MainUI was ever
        // present this snapshot was already empty.
        //
        // Cancellation checkpoint BEFORE any staging/Start below: retirement/current-world
        // classification has fully completed by this point (it only ever returns while cancellation
        // is still authoritative via its own OperationCanceledException, never as a plain Retired
        // result), so no Addon-owned resource has been created yet -- but staging itself is not a
        // pure read (CenterMHelperStaging.StageFromPublishRoot creates a directory and copies/reads
        // a file), so a cancelled Enter must not still perform that filesystem mutation.
        cancellationToken.ThrowIfCancellationRequested();

        if (_helperOwnership.IsOperationallyOwned)
        {
            // Case B: an external authority (or a prior arm on this same guard) already has an
            // operational helper running -- borrow it instead of starting a second one.
            _helperStartedByCurrentArm = false;
            AppLog.Info("CenterM.RoutingGuard", "Borrowing already-operational shared helper.", ("HelperProcessId", _helperOwnership.ProcessId));
        }
        else
        {
            // Case A: nothing owned yet -- stage and start it ourselves.
            var publishRoot = _publishRootProvider();
            var stagedPath = _stager(publishRoot);
            if (stagedPath is null)
            {
                AppLog.Warn("CenterM.RoutingGuard", "Routing guard arm failed: helper staging failed.", null);
                return CenterMMainUiRoutingGuardResult.HelperFailure;
            }

            // Safe pre-mutation cancellation point: nothing has been created yet, so there is
            // nothing to unwind -- a cancelled Enter should not still create a helper/mutex only to
            // immediately tear them down via the pipeline's subsequent rollback.
            if (cancellationToken.IsCancellationRequested)
            {
                AppLog.Info("CenterM.RoutingGuard", "Routing guard arm cancelled before any resource was created.", ("Action", "NoOp"));
                cancellationToken.ThrowIfCancellationRequested();
            }

            var startResult = _helperOwnership.Start(stagedPath);
            // PartialCleanupUnconfirmed still retained the exact process handle (a suspended
            // helper that failed post-creation setup, cleaned up best-effort but unconfirmed) --
            // this arm is responsible for that retained ownership exactly as it would be for a
            // successful Start, so UnwindAsync/DisposeAsync must still be allowed to retry/register
            // it rather than treating it as borrowed/unowned.
            if (startResult == HelperStartResult.Started)
            {
                _helperStartedByCurrentArm = true;
            }
            else if (startResult == HelperStartResult.AlreadyOwned && _helperOwnership.IsOperationallyOwned)
            {
                // OEM1 won the serialized Start race. Join the same exact ownership as a borrower;
                // do not unwind the winner's helper merely because Routing observed it late.
                _helperStartedByCurrentArm = false;
                AppLog.Info("CenterM.RoutingGuard", "Helper became operationally owned during arm; borrowing shared helper.",
                    ("HelperProcessId", _helperOwnership.ProcessId));
            }
            else
            {
                _helperStartedByCurrentArm = startResult == HelperStartResult.PartialCleanupUnconfirmed;
                AppLog.Warn("CenterM.RoutingGuard", "Routing guard arm failed: helper did not start.", null, ("Result", startResult));
                await UnwindAsync(endingHelperDemand: true).ConfigureAwait(false);
                return CenterMMainUiRoutingGuardResult.HelperFailure;
            }

            if (startResult == HelperStartResult.Started)
                AppLog.Info("CenterM.RoutingGuard", "Helper started.", ("HelperProcessId", _helperOwnership.ProcessId));
        }

        var mutexResult = _mutexOwnership.Acquire();
        if (mutexResult == CenterMMainUiMutexAcquireResult.Unavailable)
        {
            AppLog.Warn("CenterM.RoutingGuard", "Routing guard arm failed: MainUI mutex unavailable.", null);
            await UnwindAsync(endingHelperDemand: true).ConfigureAwait(false);
            return CenterMMainUiRoutingGuardResult.MutexFailure;
        }
        AppLog.Info("CenterM.RoutingGuard", "MainUI mutex acquired.");

        // Do not trust the beforeSnapshot taken above: a real MainUI (or another same-name process)
        // could have appeared during helper start/mutex acquisition. This must be a fresh capture.
        var afterSnapshot = _processSnapshotSource.GetProcessesByName(CenterMProcessNames.MainUi);
        if (afterSnapshot is null)
        {
            AppLog.Warn("CenterM.RoutingGuard", "Routing guard arm failed: post-arm same-name enumeration was uncertain.", null);
            await UnwindAsync(endingHelperDemand: true).ConfigureAwait(false);
            return CenterMMainUiRoutingGuardResult.Uncertain;
        }

        var invariant = CenterMHelperInvariant.Evaluate(afterSnapshot, _helperOwnership.ProcessId!.Value);
        if (invariant != CenterMHelperInvariantState.Valid)
        {
            AppLog.Warn("CenterM.RoutingGuard", "Routing guard arm failed: helper invariant check failed.", null, ("Invariant", invariant));
            await UnwindAsync(endingHelperDemand: true).ConfigureAwait(false);

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
            await UnwindAsync(endingHelperDemand: true).ConfigureAwait(false);
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
            _helperDemandActive = false;
            var confirmed = await UnwindAsync(endingHelperDemand: true).ConfigureAwait(false);
            AppLog.Info("CenterM.RoutingGuard", "Routing guard disarmed.", ("Confirmed", confirmed));
            return confirmed;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Unwinds only what THIS arm attempt itself acquired. A borrowed helper
    /// (<see cref="_helperStartedByCurrentArm"/> false) is never stopped here -- it stays
    /// operational for its external owner regardless of whether this attempt failed partway through
    /// or a later <see cref="DisarmAsync"/> ends a successful arm. The mutex is always released
    /// since it is exclusively owned by this guard's own routing-time arm.
    ///
    /// <see cref="_helperStartedByCurrentArm"/> is cleared only once the exact helper ownership is
    /// actually resolved (stopped and confirmed, or already not owned) -- an unconfirmed
    /// <see cref="CenterMHelperOwnership.Stop"/> leaves it true so this arm remains the responsible
    /// party for terminal <see cref="DisposeAsync"/>'s bounded retry / orphan-registration path,
    /// exactly as it would be for a helper this arm is still mid-cleanup on.</summary>
    private async Task<bool> UnwindAsync(bool endingHelperDemand)
    {
        var helperStopped = true;
        if (_helperOwnership.IsOwned)
        {
            if (_releaseSharedHelper is not null && endingHelperDemand)
            {
                helperStopped = await _releaseSharedHelper(CancellationToken.None).ConfigureAwait(false);
                _helperStartedByCurrentArm = !helperStopped;
                AppLog.Info("CenterM.RoutingGuard", "Atomic shared-helper release completed.", ("Confirmed", helperStopped), ("HelperProcessId", _helperOwnership.ProcessId));
            }
            else if (PersistentHelperOwnerReady())
            {
                AppLog.Info("CenterM.RoutingGuard", "Routing relinquished helper lifetime to persistent OEM1 ownership.", ("HelperProcessId", _helperOwnership.ProcessId));
                _helperStartedByCurrentArm = false;
            }
            else if (_helperStartedByCurrentArm || (endingHelperDemand && _persistentHelperOwnerReady is not null))
            {
                // Routing is the last active demand. This also covers a helper originally started
                // by OEM1 and borrowed by Routing, after OEM1 has been disabled while Routing stays
                // active; the final Routing unwind then owns the shared helper teardown.
                helperStopped = _helperOwnership.Stop(_helperStopTimeout);
                AppLog.Info("CenterM.RoutingGuard", "Helper stop attempted.", ("Confirmed", helperStopped), ("HelperProcessId", _helperOwnership.ProcessId));
                _helperStartedByCurrentArm = !helperStopped;
            }
        }
        else
        {
            _helperStartedByCurrentArm = false;
        }

        _mutexOwnership.Release();
        AppLog.Info("CenterM.RoutingGuard", "MainUI mutex released.");
        return helperStopped;
    }

    private bool PersistentHelperOwnerReady()
    {
        try { return _persistentHelperOwnerReady?.Invoke() == true; }
        catch (Exception exception)
        {
            AppLog.Warn("CenterM.RoutingGuard", "Persistent helper ownership could not be confirmed; retaining Routing cleanup authority.", exception);
            return false;
        }
    }

    private async Task FinalizeFailedArmDemandAsync()
    {
        if (_persistentHelperOwnerReady is null || _helperStartedByCurrentArm || !_helperOwnership.IsOwned)
            return;

        if (_releaseSharedHelper is not null || !PersistentHelperOwnerReady())
        {
            var stopped = _releaseSharedHelper is not null
                ? await _releaseSharedHelper(CancellationToken.None).ConfigureAwait(false)
                : _helperOwnership.Stop(_helperStopTimeout);
            // Routing becomes the final cleanup authority once OEM1 has relinquished the helper.
            // Preserve that responsibility when exact-handle termination is unconfirmed so the
            // terminal bounded retry/orphan path cannot be skipped as if this were a borrow.
            _helperStartedByCurrentArm = !stopped;
            AppLog.Info("CenterM.RoutingGuard", "Failed Routing arm finalized borrowed helper cleanup.",
                ("Confirmed", stopped), ("HelperProcessId", _helperOwnership.ProcessId));
        }
    }

    /// <summary>
    /// Terminal cleanup: a bounded number of additional exact-handle Stop attempts beyond whatever
    /// the normal Disarm path already tried, and -- if the exact helper handle is still unresolved
    /// after those -- hands the SAME <see cref="CenterMHelperOwnership"/> instance (never a copy,
    /// never discarded) to the process-level <see cref="CenterMOrphanedHelperRegistry"/>, mirroring
    /// <see cref="CenterMOem1LifecycleCoordinator.DisposeAsync"/>'s terminal policy for the same
    /// primitive. Never falls back to process-name/PID rediscovery. Idempotent.
    ///
    /// This guard no longer assumes that disposing it always means it owns the helper lifetime: if
    /// the currently armed (or last attempted) arm only borrowed the helper, terminal cleanup is
    /// skipped entirely -- the shared <see cref="CenterMHelperOwnership"/> stays operational for its
    /// external owner, which is solely responsible for its own disposal.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            _armed = false;
            _mutexOwnership.Release();
            _helperDemandActive = false;

            if (!_helperStartedByCurrentArm)
            {
                // Borrowed (or never started) helper: this guard has no lifetime authority over it.
                return;
            }

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
