# Work Order — Full1902 Cleanup C: Remove Dead Legacy Routing Branches from Power Orchestration

## Status

Focused deletion/simplification work order for removing the remaining pre-Full1902 routing-specific resume branches from `PowerTransitionCoordinator` after Cleanup A / PR #476 removed the legacy Steam-session routing authority and Cleanup B / PR #477 removed the old Center M dummy/MainUI suppression subsystem.

This is a **behavior-preserving cleanup PR**, not a new power-management design and not a controller-recovery redesign.

Code-review baseline used to prepare this work order:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     0d3fd2e40f176ce8e4a6f23b7395a5757379db84
production-code baseline: aa77801c8dfcb621b16b854f928fc8aefd538e89
latest production cleanup: PR #477 — Full1902 Cleanup B
```

`0d3fd2e` only updates cleanup handoff documentation after PR #477; the reviewed production power code is unchanged from `aa77801c`.

Before implementation, read and treat these as authorities:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/FULL1902_LEGACY_CLEANUP_REVIEW_HANDOFF_2026-09-04.md`
- `docs/work-order/FULL1902_CLEANUP_A_REMOVE_LEGACY_STEAM_ROUTING_AUTHORITY_WORK_ORDER_V2.md`
- `docs/work-order/FULL1902_CLEANUP_B_REMOVE_LEGACY_CENTERM_DUMMY_HELPER_SUPPRESSION_WORK_ORDER.md`

The application is pre-release. Do not preserve dead routing callbacks or routing-only tests for source compatibility.

---

# 1. Goal

Remove the remaining resume-time branches whose model was tied to the deleted routing runtime:

```text
resume
  ↓
legacy preserved routing session?
  ├─ yes → reopen routing cleanup permission
  │         reconcile preserved routing session
  │         commit safe/unsafe
  │         run deferred routing reconciliation
  │
  └─ no
       ↓
legacy residual routing cleanup?
  ├─ yes → reopen routing cleanup permission
  │         retry route/pipeline teardown
  │         fail closed if retry fails
  │
  └─ no
       ↓
current recovery-journal / baseline flow
```

The first two branches no longer have a production owner after Cleanup A.

Current production constructs `PowerTransitionCoordinator` without any of the legacy routing callbacks, so those branches are permanently disabled by default values.

Target end state:

```text
resume
  ↓
validate current power epoch / barrier
  ↓
recovery enabled?
  ↓
mark Recovering + RecoverySafety.Indeterminate
  ↓
raise current resume observer
  ↓
incomplete recovery evidence?
  ├─ yes → fail closed
  └─ no
       ↓
establish current baseline
  ↓
revalidate epoch
  ↓
commit safe/unsafe
  ↓
run current afterRecovery callback
  ↓
final power-state log
```

This PR must not create a replacement routing recovery abstraction.

---

# 2. Current code review — proof that the routing branches are dead

## 2.1 Production `AddonRuntimeHost` does not supply the legacy callbacks

Current production creates the coordinator as:

```csharp
_powerCoordinator = new PowerTransitionCoordinator(
    powerGate,
    recoverySafetyState,
    Array.Empty<IPowerSuspendParticipant>(),
    token => ReconcileFreshAfterResumeAsync(token),
    recoveryEnabled: recoverySafe,
    hasIncompleteRecovery: hasIncompleteRecovery,
    establishBaseline: establishBaseline,
    resumeObserved: () => PowerResumeObserved?.Invoke());
```

It does **not** provide:

```text
hasResidualRoutingCleanup
retryResidualRoutingCleanup
hasPreservedRoutingSession
reconcilePreservedRoutingSession
afterPreservedRecoveryCommit
```

Therefore current production receives these constructor defaults:

```csharp
_hasResidualRoutingCleanup = () => false;
_retryResidualRoutingCleanup = _ => Task.FromResult(true);
_hasPreservedRoutingSession = () => false;
_reconcilePreservedRoutingSession = _ => Task.FromResult(false);
_afterPreservedRecoveryCommit = null;
```

The two routing branches cannot execute in normal production composition.

## 2.2 Current Full1902 ownership is elsewhere

Do not interpret removal of these branches as removal of real controller recovery.

Current Full1902 controller lifecycle remains owned by the existing current components, including where applicable:

```text
MsiClawAddonPhysicalOwnership
AddonControllerHidHideBaseline
MsiClawAddonPresentation
CanonicalViiperRuntime
current physical-device loss / PnP return recovery
current PID1902 reclaim/reacquire path
current stock PID1901 baseline callback when Center M authority is selected
```

This cleanup only removes obsolete resume hooks for the already-deleted `AddonRoutingRuntime` / routing pipeline.

## 2.3 Current power watcher is still live production infrastructure

`PowerTransitionWatcher` still owns actual suspend/resume notification handling and must remain.

Its current responsibilities include:

```text
Windows suspend/resume notification registration
immediate barrier application
power epoch advancement
stale/duplicate signal handling
queued notification dispatch
fail-closed handling if notification processing throws
shutdown drain of in-flight notification work
```

Do not redesign or remove it in Cleanup C.

## 2.4 `RecoverySafetyState` and recovery journal checks are still live

`PowerTransitionCoordinator` still uses:

```text
_hasIncompleteRecovery
RecoverySafetyState
_establishBaseline
_afterRecovery
_resumeObserved
```

These are not part of Cleanup C.

The larger `RecoveryJournal` architecture is a separate cleanup candidate and requires its own reference-closure/product decision.

Do **not** use this PR to remove or replace journal-based fail-close behavior.

---

# 3. In scope

Cleanup C includes only the following reference closure:

1. remove the five routing-specific callback fields from `PowerTransitionCoordinator`;
2. remove the five matching constructor parameters/default assignments;
3. remove the preserved-routing-session resume branch;
4. remove the residual-routing-cleanup resume branch;
5. remove routing-specific logs/comments that become unreachable with those branches;
6. remove `PowerMutationGate.TryOpenResumeCleanup(...)` and `TrySealResumeCleanup(...)` after their callers disappear;
7. remove tests whose sole purpose is the deleted preserved/residual routing resume model;
8. rewrite stale comments in surviving power tests where they still describe deleted routing/OEM1 production dependencies;
9. perform a fresh source/test reference search and close only references belonging to this dead power-routing graph.

This should remain a small negative-LOC PR.

---

# 4. Explicitly out of scope

Do not redesign or change:

- `PowerTransitionWatcher` notification registration or dispatch;
- `PowerMutationGate` barrier/epoch architecture;
- suspend notification barrier timing;
- duplicate suspend/resume handling;
- resume epoch validation;
- suspend participant quiesce deadline semantics;
- `IPowerSuspendParticipant` itself;
- `RecoverySafetyState`;
- current recovery-journal fail-close behavior;
- `hasIncompleteRecovery`;
- `establishBaseline`;
- `afterRecovery`;
- `resumeObserved` / `PowerResumeObserved`;
- current Steam/BPM refresh after resume;
- current stock PID1901 baseline logic;
- current Full1902 PID1902 physical recovery/reclaim;
- HidHide recovery/normalization;
- VIIPER attach/detach/teardown;
- physical device loss / PnP re-enumeration recovery;
- shutdown ordering;
- user-termination policy;
- Center M startup-root authority;
- RecoveryJournal cleanup;
- controller-software / ClawTweaks / HHC cleanup;
- developer feature redesign;
- rumble / gyro / M1/M2 behavior.

Do not create a new:

```text
PowerRecoveryManager
ResumeRoutingManager
PowerAuthorityService
ResumeEpochCoordinator
RecoveryFacade
```

or equivalent replacement abstraction.

The goal is deletion of dead routing logic, not moving it into a new wrapper.

---

# 5. Required change A — simplify `PowerTransitionCoordinator` fields

File:

```text
src/SteamInputAddonforClaw/Power/PowerTransitionCoordinator.cs
```

Delete these fields:

```csharp
private readonly Func<bool> _hasResidualRoutingCleanup;
private readonly Func<CancellationToken, Task<bool>> _retryResidualRoutingCleanup;
private readonly Func<bool> _hasPreservedRoutingSession;
private readonly Func<CancellationToken, Task<bool>> _reconcilePreservedRoutingSession;
private readonly Func<Task>? _afterPreservedRecoveryCommit;
```

Do not replace them with genericized names.

The remaining fields must continue to represent the current power contract only.

---

# 6. Required change B — shrink the constructor contract

Remove these constructor parameters:

```csharp
Func<bool>? hasResidualRoutingCleanup = null,
Func<CancellationToken, Task<bool>>? retryResidualRoutingCleanup = null,
Func<bool>? hasPreservedRoutingSession = null,
Func<CancellationToken, Task<bool>>? reconcilePreservedRoutingSession = null,
Func<Task>? afterPreservedRecoveryCommit = null,
```

and remove their default assignments.

Do not add an options object or callback bundle merely to shorten the signature.

The surviving constructor contract should continue to accept the current real dependencies:

```text
PowerMutationGate
RecoverySafetyState
IPowerSuspendParticipant collection
afterRecovery
recoveryEnabled
hasIncompleteRecovery
establishBaseline
suspendQuiesceBudget
resumeObserved
```

Exact parameter ordering may be formatted for readability, but avoid unrelated API churn.

Current `AddonRuntimeHost` construction should require little or no semantic change because it already does not pass the removed parameters.

---

# 7. Required change C — delete preserved-routing resume recovery

Delete the complete branch beginning from:

```csharp
if (_hasPreservedRoutingSession())
```

including its routing-specific behavior:

```text
TryOpenResumeCleanup
reconcilePreservedRoutingSession
TrySealResumeCleanup
routing-specific safe/unsafe commit
_afterPreservedRecoveryCommit
"Post-commit deferred routing reconciliation failed" log
immediate return from the preserved-routing branch
```

Do not convert this to a generalized "preserved Full1902 session" branch.

Current Full1902 physical/presentation owners already have their own lifecycle and recovery paths. There is no product requirement for `PowerTransitionCoordinator` to preserve/recreate a route session.

---

# 8. Required change D — delete residual-routing cleanup retry

Delete the complete branch beginning from:

```csharp
if (_hasResidualRoutingCleanup())
```

including:

```text
TryOpenResumeCleanup
_retryResidualRoutingCleanup
TrySealResumeCleanup
"Residual routing cleanup window could not be opened" log
"Residual routing cleanup retried on resume" log
routing-plan/stage-state comments
failure path that exists specifically for the deleted owner-stage rollback
```

After removal, resume must proceed directly to the still-current incomplete-recovery check.

The surviving sequence should conceptually be:

```csharp
if (_hasIncompleteRecovery())
{
    // current fail-closed behavior
    ...
    return;
}

safe = await _establishBaseline(cancellationToken);
...
```

Do not add a new generic residual-cleanup hook.

---

# 9. Required change E — remove resume-cleanup-only gate APIs

File:

```text
src/SteamInputAddonforClaw/Power/PowerMutationGate.cs
```

Fresh reference review at the baseline shows:

```text
TryOpenResumeCleanup
TrySealResumeCleanup
```

are used only by the two routing branches being deleted.

Delete both methods and the routing-specific XML comment describing:

```text
"the process's own residual routing/pipeline state"
```

Do **not** broaden Cleanup C into a general `PowerMutationGate` API purge.

In particular, preserve the current suspend barrier/cleanup primitives and their tests unless a fresh reference check proves a direct compile-only adjustment is necessary:

```text
Epoch
IsOpen
TryEnterBarrier
EnterNewCycleBarrier
TrySealSuspendCleanup
OpenAfterRecovery
TryCommitRecovery
Close
current token/cleanup helpers outside the removed resume-routing branch
```

`TryOpenAfterRecovery` and any other unrelated dead-looking method are not part of this focused PR unless required to compile after the exact reference closure above. Do not turn Cleanup C into a general power-class refactor.

---

# 10. Preserve the current resume ordering

The following ordering is a current lifecycle contract and must remain materially unchanged.

## 10.1 Before recovery work

Preserve:

```text
reject duplicate accepted resume for the same cycle
capture/validate observation epoch
ignore stale resume if a newer barrier owns the epoch
apply fallback barrier only when needed
fail closed if recovery was not safely established at startup
set PowerTransitionState.Recovering
set RecoverySafety.Indeterminate
compute current resume cycle
invoke resumeObserved
```

Do not move `resumeObserved` later as part of this cleanup.

## 10.2 Current recovery evidence

Preserve:

```text
_hasIncompleteRecovery()
→ fail closed
→ RecoverySafety.Unsafe
→ PowerTransitionState.Unsafe
→ gate remains closed
```

Cleanup C does not decide whether this journal-based contract should exist long term.

## 10.3 Baseline

Preserve:

```text
_establishBaseline(cancellationToken)
```

and its existing logging/elapsed-time accounting.

This callback is part of current production power behavior, including stock-authority resume baseline handling.

## 10.4 Epoch check before commit

Preserve the final current-epoch validation before committing recovery.

A newer suspend/barrier must still invalidate the older resume result.

## 10.5 Recovery commit

Preserve:

```text
_gate.TryCommitRecovery(...)
RecoverySafety.Safe / Unsafe
PowerTransitionState.Awake / Unsafe
```

with the same fail-closed meaning.

## 10.6 `afterRecovery`

Preserve the current post-commit callback and its failure behavior.

Current production supplies:

```text
ReconcileFreshAfterResumeAsync
```

which refreshes Steam/BPM state and requests status refresh after resume.

Do not merge this callback into baseline establishment and do not introduce a second resume observer.

---

# 11. Preserve suspend behavior exactly

Cleanup C is resume-routing cleanup only.

The suspend branch must retain:

```text
BarrierApplied check
duplicate suspend suppression
new power cycle increment
PowerTransitionState.Quiescing
captured suspend epoch
one shared quiesce deadline
participant invocation in order
per-participant bounded cancellation
exception → failure handling
TrySealSuspendCleanup(suspendEpoch)
stale/newer-epoch protection
final Suspended vs Unsafe state
```

Do not remove the participant mechanism merely because current production passes:

```csharp
Array.Empty<IPowerSuspendParticipant>()
```

The handoff explicitly preserves participant quiesce/deadline semantics as part of the generic power lifecycle.

Do not add timing defenses for theoretical instruction-level races beyond the already-established epoch/barrier model.

---

# 12. Tests — delete routing-only coverage

File:

```text
tests/SteamInputAddonforClaw.Tests/PowerTransitionTests.cs
```

Delete tests whose only purpose is the removed routing branches.

At the reviewed baseline this includes at least:

```text
Preserved_recovery_runs_post_commit_callback_only_after_safe_recovery_is_committed
ResidualRoutingCleanup_RetriedBeforeJournalCheckAndBaseline
ResidualRoutingCleanup_ClearsJournal_ThenNormalFreshResume
ResidualRoutingCleanup_Failure_SkipsJournalCheckAndBaselineAndRemainsUnsafe
NoResidualRoutingState_SkipsCleanupRetryAndGoesStraightToJournalCheck
StaleResidualCleanupCompletion_DoesNotSealOrCommitOverANewerSuspendEpoch
```

Perform a fresh grep before implementation for all of:

```text
hasResidualRoutingCleanup
retryResidualRoutingCleanup
hasPreservedRoutingSession
reconcilePreservedRoutingSession
afterPreservedRecoveryCommit
ResidualRoutingCleanup
Preserved_recovery
TryOpenResumeCleanup
TrySealResumeCleanup
```

If another test exists solely for those deleted contracts, remove it too.

Do not replace deleted routing tests with renamed versions of the same dead behavior.

---

# 13. Tests — preserve current power/lifecycle coverage

Keep/update tests that prove current supported behavior, including equivalent coverage for:

```text
resume observer fires only for first accepted resume cycle
clean resume establishes baseline and opens gate
baseline runs while normal mutation gate remains closed
afterRecovery runs only after successful recovery commit / gate open
incomplete recovery evidence fails closed without baseline
suspend immediately blocks normal mutation
suspend cleanup/barrier epoch is sealed correctly
stale suspend/resume completion cannot commit over newer epoch
duplicate automatic/resume-suspend signals reconcile once
participant quiesce uses one shared deadline
expired suspend deadline skips participant work
notification registration failure remains fail closed
notification-processing failure closes the gate
PowerTransitionWatcher drains in-flight work on shutdown
AddonRuntimeHost resume refresh/status behavior remains current
```

Do not weaken or delete a real lifecycle test merely because its original comment mentioned the old routing stack.

---

# 14. Rewrite stale comments in surviving tests

Some surviving power tests still contain historical comments referring to production dependencies removed by Cleanup A/B, for example old references to:

```text
OEM1 auxiliary resume reconcile
slow/timed-out routing quiesce
legacy routing re-entry
```

Where the test itself still protects a valid generic invariant, keep the test and rewrite only the comment to describe the current invariant.

Examples:

```text
BEFORE
"AddonRuntimeHost's OEM1 auxiliary resume reconcile therefore has to run ..."

AFTER
"The baseline callback must complete while normal mutation remains blocked; post-recovery work runs only after a successful recovery commit."
```

and:

```text
BEFORE
"invoked before a slow/timed-out routing quiesce could consume the whole budget"

AFTER
"participants share one absolute suspend deadline and are invoked in declared order while budget remains"
```

Do not invent a new current production consumer merely to justify the generic test.

---

# 15. Logging cleanup

Remove logs that exist only inside deleted branches, including current messages equivalent to:

```text
Residual routing cleanup window could not be opened.
Residual routing cleanup retried on resume.
Post-commit deferred routing reconciliation failed.
```

Also remove comments describing:

```text
current process owns exact stage state
a frozen routing plan
canonical pipeline teardown
journal fallback after route teardown
```

Keep current reachable power logs, including:

```text
Power notification observed
Suspend quiesce completed
Stale resume ignored because a newer power barrier is authoritative
Resume recovery disabled / remain passive
Recovery journal remains ... failing closed
Resume Stock baseline completed
Resume reconciliation invalidated by newer power barrier
Resume reconciliation failed
Post-recovery session reconciliation failed (or a purely wording-only equivalent)
Resume reconciliation completed
```

Do not expand this PR into a broad logging-style cleanup.

---

# 16. Expected code shape after Cleanup C

`PowerTransitionCoordinator.HandleAsync()` should become easier to read without adding helper classes.

The resume body should have one linear current path:

```text
validate signal / duplicate
→ validate epoch / barrier
→ recovery-enabled gate
→ mark Recovering / Indeterminate
→ raise resume observer
→ current incomplete-recovery fail-close check
→ establish baseline
→ epoch validation
→ commit recovery
→ afterRecovery
→ final status log
```

Do not extract each step into one-line wrapper methods solely to reduce method length.

This PR is successful if the method becomes simpler because dead branches are gone, not because complexity was moved elsewhere.

---

# 17. Product lifecycle invariants that must remain unchanged

## Center M Enabled

```text
MSI / stock controller authority
→ desired PID1901
→ resume power notification
→ current stock baseline verification/recovery callback remains authoritative
```

## Center M Disabled

```text
Addon controller authority
→ desired PID1902
→ Full1902 physical ownership / HidHide / presentation remain their existing owners
→ resume notification must not revive legacy Steam-session routing authority
```

## Physical loss / PnP re-enumeration

```text
physical session loss / re-enumeration
→ existing current Full1902 physical recovery path
```

Cleanup C must not move this into `PowerTransitionCoordinator`.

## HidHide / VIIPER failure

Actual operation failures continue to follow the current fail-close/cleanup contracts.

Do not add third-party-process attribution or new recovery authority here.

## New suspend while resume is in progress

The existing power epoch/barrier must remain the authority.

A stale older resume completion must not reopen the gate or overwrite the newer cycle.

No additional epoch/state manager is required.

---

# 18. Reference-closure rules

Before editing, search current `main` again because this area may move.

For every proposed deletion:

1. search production source;
2. search tests;
3. distinguish live current usage from historical docs/work orders;
4. delete current reference closures, not historical documentation merely because it mentions the old contract;
5. do not retain production APIs only because an old work order or deleted test names them.

Historical work orders may continue to mention the old routing callbacks as historical design context.

The required zero-residue check applies to current source/tests, not archival design history.

---

# 19. Validation

Minimum validation before completion:

```text
dotnet build -c Debug
dotnet build -c Release
dotnet test -c Release
git diff --check
```

Also run targeted power/runtime tests, for example using the repository's current test-project path:

```text
dotnet test -c Release --filter FullyQualifiedName~PowerTransitionTests
dotnet test -c Release --filter FullyQualifiedName~AddonRuntimeHostTests
```

If the test CLI requires the explicit test project/solution path in the current environment, use it.

Fresh source/test reference search must show no live references to:

```text
_hasResidualRoutingCleanup
_retryResidualRoutingCleanup
_hasPreservedRoutingSession
_reconcilePreservedRoutingSession
_afterPreservedRecoveryCommit
hasResidualRoutingCleanup
retryResidualRoutingCleanup
hasPreservedRoutingSession
reconcilePreservedRoutingSession
afterPreservedRecoveryCommit
TryOpenResumeCleanup
TrySealResumeCleanup
Residual routing cleanup
Post-commit deferred routing reconciliation
```

Historical docs/work orders are allowed to retain those names.

---

# 20. Manual hardware smoke if MSI Claw hardware is available

Because Sleep/Hibernate/Resume is a real product lifecycle, perform a bounded manual smoke if hardware is available.

## 20.1 Center M Enabled authority

Verify:

```text
start in stock authority / PID1901
→ sleep or hibernate
→ resume
→ app remains stable
→ expected stock PID1901 baseline remains/restores
→ no Addon PID1902 takeover occurs
```

## 20.2 Center M Disabled / Addon authority

Verify:

```text
start with current Full1902 PID1902 ownership
→ controller usable through current virtual presentation
→ sleep or hibernate
→ resume
→ current Full1902 recovery path converges to usable PID1902 ownership/presentation
→ no legacy routing-session recovery log appears
```

If supported hardware is not available in the implementation environment, report hardware smoke as blocked rather than fabricating a result.

Do not add production complexity solely because hardware smoke cannot be run in CI.

---

# 21. Review checklist

A reviewer should verify:

- [ ] the five routing callback fields are gone;
- [ ] the five matching constructor parameters/defaults are gone;
- [ ] the preserved-routing-session branch is gone;
- [ ] the residual-routing-cleanup branch is gone;
- [ ] routing-specific resume logs/comments are gone;
- [ ] `TryOpenResumeCleanup` is gone;
- [ ] `TrySealResumeCleanup` is gone;
- [ ] no replacement routing-recovery abstraction was introduced;
- [ ] `PowerTransitionWatcher` behavior is unchanged;
- [ ] suspend barrier/epoch behavior is unchanged;
- [ ] participant deadline semantics are unchanged;
- [ ] `hasIncompleteRecovery` still fails closed;
- [ ] `establishBaseline` still runs only on the current path;
- [ ] `resumeObserved` still runs at the same accepted-resume point;
- [ ] `afterRecovery` still runs after safe commit;
- [ ] stale newer-epoch protection remains;
- [ ] routing-only tests were removed instead of renamed;
- [ ] surviving generic lifecycle tests remain meaningful;
- [ ] stale test comments no longer claim deleted routing/OEM1 dependencies;
- [ ] Debug build passes;
- [ ] Release build passes;
- [ ] full Release tests pass;
- [ ] `git diff --check` is clean.

---

# 22. Completion criteria

Cleanup C is complete when the production power path no longer knows how to recover a "preserved routing session" or retry "residual routing cleanup", while all currently supported power/lifecycle safety remains intact.

Required end-state summary:

```text
PowerTransitionCoordinator
→ generic suspend/resume coordinator only
→ no deleted AddonRoutingRuntime recovery hooks
→ no route/pipeline teardown retry hooks

PowerMutationGate
→ no resume-routing cleanup window API
→ existing current barrier/epoch behavior retained

PowerTransitionWatcher
→ unchanged live notification owner

Current recovery
→ incomplete recovery evidence still fails closed
→ stock baseline callback still works
→ current Full1902 controller recovery remains outside legacy routing
→ afterRecovery + PowerResumeObserved remain current
```

Do not broaden the PR beyond this reference closure unless compilation proves a directly connected stale dependency.