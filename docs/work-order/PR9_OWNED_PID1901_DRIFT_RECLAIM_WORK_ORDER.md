# Work Order — PR9: Owned PID1901 Drift Reclaim

## Status

Implementation work order for the next Full PID1902 owned-state recovery PR after PR8 owned DirectInput session recovery.

Current implementation sequence:

```text
PR1   Persistent Dual VIIPER Devices Foundation                 [merged]
  ↓
PR2   Addon-Owned Persistent HidHide Baseline Foundation        [merged]
  ↓
PR2.5 Mandatory Controller Runtime Lifetime Foundation           [merged]
  ↓
PR3   Reboot-Bound Controller Authority Transition               [merged]
  ↓
PR4   Disabled-Boot Controller Admission                         [merged as #437]
  ↓
PR5   PID1902 + DirectInput Physical Ownership                   [merged as #439]
  ↓
PR6   First Virtual Presentation Attach                          [merged as #441]
  ↓
PR7   Runtime Xbox360 ↔ SteamDeck Presentation Switching         [merged as #444]
  ↓
PR8   Owned DirectInput Session Recovery                         [merged as #445]
  ↓
PR9   Owned PID1901 Drift Reclaim                                [this PR]
  ↓
PR10+ Physical loss/PnP return, suspend/resume, explicit
      Center M resurrection triggers, crash keepalive, broader hardening
```

PR8 merged as:

```text
07d24cde3ac3d96e799f682b90a8649acde2b418
Add first Full-1902 owned DirectInput session recovery (PR8) (#445)
```

This work order is prepared against that `main`.

Before implementation, read and treat these as design authorities:

- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/work-order/PR5_PID1902_DIRECTINPUT_PHYSICAL_OWNERSHIP_WORK_ORDER.md`
- `docs/work-order/PR8_OWNED_DIRECTINPUT_SESSION_RECOVERY_WORK_ORDER.md`
- current `main` implementations of:
  - `Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs`
  - `Devices/MSI/Claw/MsiClawModeContracts.cs`
  - `Devices/MSI/Claw/MsiClawInputSource.cs`
  - `Hosting/AddonProcessHost.cs`
  - `HidHide/AddonControllerHidHideBaseline.cs`

The application is pre-release. Do not preserve obsolete route-scoped ownership or `ExternalNativeTakeover` behavior for compatibility.

---

## 1. Goal

PR8 now handles this real failure successfully:

```text
Center M Disabled
+ previously-owned PID1902
+ owned DirectInput session dies unexpectedly
    ↓
PR8 recovery captures same strong physical MSI Claw
+ current mode still PID1902
+ exact PID1902 collection unchanged
    ↓
verify HidHide
→ restart same DirectInput source
→ first valid state
→ existing presentation continues/reconciles
```

PR8 intentionally fails closed when the same recovery capture reports PID1901:

```text
OwnedPhysicalStateDriftPid1901
```

That is the next concrete lifecycle gap.

PR9 must implement the smallest safe continuation:

> **When an already-owned DirectInput session dies and the same strongly identified MSI Claw is now PID1901 while Center M authority is still exactly Disabled, treat this as owned physical-state drift, reclaim that same device to PID1902 once, re-prove the same physical identity, then continue the existing PR8 exact-target/HidHide/DirectInput recovery path.**

This is desired-state reconciliation, not a new authority transfer.

---

## 2. Product meaning of PID1901 drift

While Center M startup roots remain exactly Disabled:

```text
DesiredAuthority    = Addon
DesiredPhysicalPID  = PID1902
```

Therefore this transition:

```text
same owned MSI Claw
PID1902 → PID1901
```

must be classified as:

```text
OwnedPhysicalStateDrift
```

not:

```text
ExternalNativeTakeover
ExternalAuthorityWon
YieldCurrentSteamSession
RestoreStockController
```

The Addon remains controller authority until the user explicitly performs:

```text
Enable Center M and Restart
```

Do not restore the old Steam-session policy where an external/native takeover permanently wins the current session.

---

## 3. Keep PR9 narrow

PR9 is not the complete future `ReconcileOwnedControllerAsync(trigger)` implementation.

It extends the already-proven PR8 DirectInput-loss recovery operation for one additional observed physical state:

```text
current same device = PID1901
→ reclaim PID1902
→ continue PR8 recovery
```

### In scope

PR9 may:

- reuse the PR8 `MsiClawInputSource.TestCompleted` trigger;
- reuse the one tracked `_ownedControllerRecovery` task in `AddonProcessHost`;
- reuse `MsiClawAddonPhysicalOwnership.RecoverLostInputAsync`;
- reuse the same physical-owner `_gate`;
- detect PID1901 from the existing stable native-state capture;
- prove PID1901 belongs to `_ownedPhysicalIdentity` before any mode write;
- perform one fresh Center M startup-root authority read before the reclaim mutation;
- issue exactly one `PID1901 → PID1902` mode transition through the existing `_switchMode` / `MsiClawNativeStateManager.SwitchModeAsync` primitive;
- perform bounded post-transition native/PnP stabilization through the existing capture primitive;
- require final PID1902 + same strong physical identity;
- then reuse the existing PR8 DirectInput descriptor resolution, exact-target validation, HidHide-before-DirectInput ordering, same-source restart, first-valid-state requirement, and PR7 presentation reconcile.

### Strictly out of scope

Do **not** implement in PR9:

- a PnP removal/arrival watcher;
- indefinite waiting for a physically absent controller;
- suspend/hibernate/resume wiring;
- a new Center M process watcher or poller;
- a generalized Center M resurrection manager;
- a watchdog/service/supervisor for Runtime crash recovery;
- VIIPER runtime recreation;
- publisher-fault recovery;
- HidHide target migration when the exact PID1902 collection changes;
- broad HidHide drift monitoring;
- a new controller authority manager;
- a recovery state machine;
- epoch/generation/barrier machinery;
- retry queues/channels;
- periodic timers/polling;
- PID1902 → PID1901 rollback on reclaim failure;
- legacy `ExternalNativeTakeover` / route retry semantics.

A future focused PR may add an explicit lifecycle trigger for a resurrected Center M runtime and, if hardware evidence requires it, targeted quiesce of exact known Center M controller processes. PR9 does not add a new background observer for that condition.

---

## 4. Important safety interpretation for Center M resurrection

The Full 1902 design describes the eventual recovery sequence as:

```text
verify Center M remains Disabled
→ quiesce conflicting Center M runtime if it resurrected
→ reclaim PID1902
```

For this small PR, do not create a new Center M runtime detector/watcher merely to complete that future sequence.

The mandatory PR9 rule is:

```text
fresh Center M startup configuration == exactly Disabled
```

immediately before the first PID1902 reclaim write.

If current code already exposes a **narrow, positively-identified, side-effect-free read** that proves a conflicting Center M controller runtime is active, PR9 may fail closed on that evidence rather than fighting it.

Do not wire the Full1902 owner through old route-scoped `ExternalNativeTakeover`, routing guard, or generalized process-kill policy.

Do not add broad process-name killing.

If a persistent Center M runtime fights the reclaim, the existing post-transition verification must fail closed. Because DirectInput remains stopped after a failed recovery, PR9 must not introduce an internal retry loop that repeatedly flips the PID.

---

## 5. Existing PR8 recovery is the one implementation seam

Current `MsiClawAddonPhysicalOwnership.RecoverLostInputAsync` already:

```text
acquires existing _gate
→ requires prior committed ownership evidence
→ requires stopped same source
→ retains _ownedPhysicalIdentity + _ownedHiddenTarget
→ captures stable native state
→ PID1902: continues recovery
→ PID1901: currently returns OwnedPhysicalStateDriftPid1901
→ resolves DirectInput
→ requires exact same target
→ fresh Center M Disabled check
→ verifies/repairs HidHide BEFORE DirectInput restart
→ restarts SAME source
→ first valid state
→ commits _ownsInputSource = true
```

PR9 should modify this existing flow rather than creating:

```text
RecoverPidDriftAsync
Pid1902RecoveryManager
NativeTakeoverRecoveryManager
ControllerAuthorityManager
```

Preferred code shape:

```text
RecoverLostInputCoreAsync
  capture current native state
  verify strong owned identity
  if PID1901:
      reclaim same device to PID1902
      post-switch stable verify
  else require PID1902
  continue one shared PR8 recovery tail
```

Keep one owner, one gate, one recovery entrypoint.

---

## 6. Identity must be proven before the reclaim write

PR8 retained:

```csharp
_ownedPhysicalIdentity
```

from the successful PR5 ownership commit.

For PR9, a PID1901 capture is not sufficient by itself.

Required before mode mutation:

```text
current native capture allows mutation
current mode == XInput / PID1901
current identity confidence == Strong
_ownedPhysicalIdentity confidence == Strong
_ownedPhysicalIdentity.StronglyMatches(currentIdentity) == true
```

Only then may the reclaim path consider a mode write.

A different MSI Claw, weak identity, ambiguous topology, or missing device must never be switched merely because VID/PID look compatible.

### Mandatory ordering

```text
capture PID1901 + identity
→ strong-match against committed owned identity
→ fresh Center M Disabled check
→ mode write
```

Do not perform the authority read/mode write before same-device proof.

---

## 7. Reclaim uses exactly one existing PID transition primitive

When all preconditions are satisfied:

```text
_switchMode(MsiClawNativeMode.DirectInput, currentIdentity, cancellationToken)
```

or the exact existing equivalent must be used.

Rules:

- one reclaim attempt per one PR8 recovery invocation;
- no manual HID command duplication;
- no second mode writer;
- no retry loop in PR9;
- no fallback to PID1901;
- no `PID1902 → PID1901 → PID1902` round-trip;
- no mutation of a different physical identity.

If the mode transition reports failure:

```text
recovery = Failed
DirectInput remains stopped
virtual output remains neutral
persistent HidHide ownership remains
no reverse mode write
```

The result/log must preserve that a mode write was attempted for diagnostics.

---

## 8. Mandatory post-reclaim verification

A successful mode-write return is not enough.

After the one PID1902 transition attempt:

```text
bounded stable native capture
→ capture must allow mutation/inspection
→ final mode == DirectInput / PID1902
→ final identity confidence == Strong
→ final identity strongly matches committed _ownedPhysicalIdentity
→ final identity strongly matches the PID1901 identity used for the transition
```

Any failure here must fail closed.

Examples:

```text
mode write reports success but device disappears
mode write reports success but final state is still PID1901
final topology is ambiguous
another MSI Claw appears
strong identity changes across re-enumeration
```

→ no DirectInput restart, no target migration, no PID1901 rollback.

---

## 9. Reuse the PR8 recovery tail after PID1902 is proven

Once the reclaim path proves:

```text
same owned MSI Claw
+ final PID1902
```

do not duplicate the rest of PR8.

Continue through the same shared logic:

```text
ResolveDirectInputDescriptorAsync
→ require primary PID1902 gamepad collection
→ resolve PnP node
→ require strong match to _ownedPhysicalIdentity
→ require recovered exact target == _ownedHiddenTarget
→ fresh authority check as appropriate
→ ApplyDisabledModeBaseline([same exact target])
→ require IsCompliant
→ StartPrepared(same source)
→ require IsRunning
→ WaitForFirstValidStateAsync
→ commit _ownsInputSource = true
```

Refactor only enough to avoid two copies of this tail.

Do not introduce a strategy/interface hierarchy to share it.

A small local helper is acceptable only if it makes the single owner/recovery path clearer.

---

## 10. Exact HidHide target remains unchanged in PR9

PR9 adds PID drift reclaim, **not HidHide target migration**.

After PID1901 → PID1902 re-enumeration, resolve the exact current PID1902 primary gamepad collection as usual.

For this PR:

```text
recoveredTarget == _ownedHiddenTarget
```

must still be required.

If the exact collection changed:

```text
RecoveredTargetChanged
→ fail closed
→ do not remove old target
→ do not add a replacement target
→ do not restart DirectInput
```

This intentionally preserves the PR8 safety contract.

If hardware testing proves the same physical controller legitimately receives a new exact PID1902 collection identity during a supported lifecycle transition, implement that migration as a separate focused PR with explicit ownership/read-back rules rather than silently broadening PR9.

---

## 11. HidHide-before-DirectInput remains mandatory

During recovery the VIIPER presentation may still be attached and its publisher is reading the same `MsiClawInputSource`, whose state is neutral while stopped.

Therefore, after PID1902 reclaim:

```text
prove exact target
→ verify/repair HidHide baseline
→ only then restart DirectInput
```

must remain unchanged from PR8.

Do not copy PR5 startup ordering here.

The reason is concrete:

```text
restart DirectInput first
→ source can become non-neutral
→ existing virtual publisher can forward it
→ physical isolation may not yet be proven
→ possible physical + virtual double input
```

PR9 must not introduce that window.

---

## 12. Center M authority checks

At minimum perform a fresh authority read immediately before the PID1902 mode write:

```text
CenterMStartupState == Disabled
```

Otherwise:

```text
AuthorityNotDisabled
→ zero mode write
→ zero DirectInput restart
→ no HidHide mutation
```

Because the reclaim may include bounded PnP stabilization before DirectInput/HidHide recovery, retaining the existing PR8 fresh authority check before its recovery mutation boundary is acceptable and preferred if it keeps the flow simple.

Do not cache the startup value from process launch.

Do not add another persisted authority boolean.

---

## 13. Result / diagnostic semantics

The current `MsiClawPhysicalOwnershipResult` already contains:

```text
Outcome
Reason
ModeWriteIssued
HiddenTarget
```

Use it rather than inventing a second recovery-result hierarchy unless implementation proves it impossible.

For a successful PID1901 reclaim, use a specific reason such as:

```text
OwnedPhysicalStateDriftReclaimed
```

and:

```text
Outcome         = Owned
ModeWriteIssued = true
HiddenTarget    = existing exact owned target
```

For a successful same-PID1902 PR8 recovery, preserve existing semantics:

```text
OwnedPhysicalInputRecovered
ModeWriteIssued = false
```

For failures after the reclaim write, `ModeWriteIssued` should remain true so logs/tests can prove PR9 did not silently pretend no native mutation occurred.

A minimal implementation may extend the existing `RecoveryFail(...)` helper with an optional `modeWriteIssued` argument rather than creating more result types.

---

## 14. Presentation behavior

PR9 must not own presentation policy.

While the physical source is down:

```text
MsiClawInputSource.LatestState = neutral
existing X360/SteamDeck publisher may remain attached
```

After successful recovery, existing host behavior already does:

```text
result.IsOwned
→ RequestControllerPresentationReconcile("PhysicalInputRecovered")
```

Reuse that path.

Do not:

- detach/recreate VIIPER merely because PID drift occurred;
- create another publisher;
- hard-code X360 or SteamDeck;
- duplicate RunningAppID/BPM policy;
- recreate VIIPER server/bus.

The current raw Steam/BPM fact decides presentation through PR7.

---

## 15. Enable-and-Restart must remain authoritative

PR9 recovery and explicit Center M Enable may overlap in real usage.

The existing physical-owner `_gate` is the required serialization boundary.

Do not add a new global lock.

Expected behavior:

```text
PR9 recovery owns _gate first
→ completes one known-safe reclaim/recovery attempt or fails closed
→ Enable release runs afterward
→ restores/keeps same strong device PID1901 as required

OR

Enable release owns _gate first
→ _releasedForEnable becomes true
→ later recovery is refused
```

Do not weaken the existing official release ordering:

```text
presentation stop/join
→ virtual detach
→ VIIPER teardown Closed
→ physical owner ReleaseForCenterMEnableAsync
→ PID1901 stock verified
→ HidHide clear
→ Center M roots enable
→ reboot
```

A failed PR9 recovery must retain `_ownedPhysicalIdentity` and `_ownedHiddenTarget` evidence so explicit release can still clear/restore the owned environment.

---

## 16. Process shutdown

Keep PR8 shutdown behavior unchanged:

```text
BeginProcessShutdown
→ prevent new recovery scheduling
→ drain _ownedControllerRecovery
→ drain _presentationReconcile
→ presentation Dispose
→ physical owner Dispose
```

PR9 should not add another tracked task.

If shutdown cancellation occurs before the reclaim mutation, honor it.

Once a native mode write has begun, do not invent rollback/transaction machinery. Let the existing mode transition primitive and post-state verification establish the known result; on failure remain fail closed.

Do not restore PID1901 merely because the Addon process is shutting down while Center M remains Disabled.

---

## 17. Missing/ambiguous device remains out of scope

If PR9 recovery capture cannot prove a present strong MSI Claw:

```text
DeviceNotFound
Indeterminate
weak identity
ambiguous identity
unsupported mode
```

PR9 does not wait indefinitely and does not create a PnP watcher.

Required behavior:

```text
virtual publisher remains neutral
DirectInput remains stopped
no mode write
no HidHide removal
recovery returns Failed
```

A later physical-loss/PnP-return PR will add the correct arrival-triggered continuation.

---

## 18. No theoretical race hardening

Follow the project review/overengineering policy.

Protect realistic lifecycle interactions:

- DirectInput session dies;
- same physical MSI Claw re-enumerates as PID1901;
- explicit Enable-and-Restart overlaps recovery;
- process shutdown overlaps recovery;
- real mode write fails;
- PnP settle fails;
- identity cannot be proven;
- HidHide or DirectInput operation fails.

Do not add complexity solely for instruction-level interleavings such as:

- a Steam callback landing between two local statements;
- an exact scheduler interleave around a bool assignment;
- a suspend callback theoretically firing between arbitrary instructions;
- artificial tests that require a new epoch/barrier to distinguish otherwise convergent work.

The existing owner `_gate`, PR8 tracked task, fresh fact reads, and fail-closed operation ordering are sufficient for PR9's scope.

---

## 19. Suggested implementation shape

Keep the diff centered on `MsiClawAddonPhysicalOwnership` plus focused tests.

Conceptually:

```csharp
private async Task<MsiClawPhysicalOwnershipResult> RecoverLostInputCoreAsync(...)
{
    require committed ownership;
    require source stopped;

    var capture = await _captureStableNativeState(...);
    require strong current identity;
    require current identity matches _ownedPhysicalIdentity;

    var modeWriteIssued = false;

    if (mode == MsiClawNativeMode.XInput)
    {
        require fresh CenterM == Disabled;

        var transition = await _switchMode(
            MsiClawNativeMode.DirectInput,
            currentIdentity,
            cancellationToken);
        modeWriteIssued = true;
        require transition.Succeeded;

        var verified = await _captureStableNativeState(...);
        require verified mode == DirectInput;
        require verified strong identity matches owned identity;
    }
    else
    {
        require mode == DirectInput;
    }

    // one existing/shared PR8 recovery tail
    resolve exact DI descriptor;
    require same owned target;
    require CenterM still Disabled;
    verify/repair HidHide;
    restart same source;
    require first valid state;
    commit ownership;

    return Owned(..., modeWriteIssued);
}
```

This is illustrative, not a demand for exact local method names.

The important design is one linear recovery operation, not another framework.

---

## 20. Production logging

Add concise lifecycle logs sufficient to diagnose hardware behavior without per-poll noise.

Recommended events:

```text
OwnedPhysicalStateDriftDetected
  CurrentMode=PID1901
  IdentityMatched=true

OwnedPhysicalPid1902ReclaimStarted
  HiddenTarget=<existing target>

OwnedPhysicalPid1902ReclaimCompleted
  FinalMode=PID1902
  SamePhysicalIdentity=true

OwnedPhysicalPid1902ReclaimFailed
  Reason=<classified reason>
  ModeWriteIssued=true/false
```

Existing PR8 logs should continue for:

```text
DirectInput resolution
HidHide verification
DirectInput restart
first valid state
overall recovery success/failure
```

Do not add high-frequency topology polling logs.

---

## 21. Mandatory automated tests

Extend `MsiClawAddonPhysicalOwnershipTests` and only add host tests if host code actually changes.

At minimum cover all of the following.

### 21.1 Same owned PID1901 successfully reclaims PID1902

Given:

```text
prior successful ownership
source unexpectedly stopped
current native mode = PID1901
current identity strongly matches committed owner
Center M = Disabled
mode transition succeeds
post-transition capture = same identity + PID1902
exact target unchanged
HidHide compliant
DirectInput restart + first valid state succeeds
```

Assert:

```text
Outcome = Owned
Reason = OwnedPhysicalStateDriftReclaimed (or final chosen specific equivalent)
ModeWriteIssued = true
exactly one switch call
switch target = DirectInput / PID1902
same input source restarted
```

### 21.2 Different strong PID1901 identity never receives a mode write

```text
current PID1901 strong identity != _ownedPhysicalIdentity
→ Failed
→ SwitchCalls == 0
→ no HidHide mutation
→ no DirectInput restart
```

This is mandatory.

### 21.3 Center M no longer exactly Disabled blocks reclaim

```text
same PID1901 identity
CenterM = Enabled / Partial / Unavailable
→ zero mode write
→ zero HidHide mutation
→ zero DirectInput restart
```

### 21.4 PID1902 transition failure fails closed

```text
SwitchModeAsync fails
→ exactly one attempted PID1902 write
→ ModeWriteIssued = true
→ no reverse PID1901 write
→ no HidHide mutation
→ no DirectInput restart
```

### 21.5 Post-switch final mode must be PID1902

Mode writer reports success but stable capture remains PID1901/Other:

```text
→ Failed
→ no DirectInput restart
→ no PID1901 rollback
```

### 21.6 Post-switch identity mismatch fails closed

```text
PID1901 identity A
mode write succeeds
final PID1902 identity B
→ Failed
→ no HidHide mutation
→ no DirectInput restart
```

### 21.7 Exact target changed after reclaim

```text
same physical strong identity
PID1902 restored
resolved primary collection != _ownedHiddenTarget
→ RecoveredTargetChanged
→ no target migration
→ no DirectInput restart
```

### 21.8 HidHide failure after successful PID reclaim

Assert:

```text
mode write happened exactly once
ModeWriteIssued = true
PID1901 rollback never happens
DirectInput restart does not happen
owned identity/target evidence remains available for explicit release
```

### 21.9 DirectInput restart failure after successful reclaim

Assert fail closed with:

```text
no PID1901 rollback
no VIIPER operation
persistent HidHide not cleared
ModeWriteIssued = true
```

### 21.10 First-valid-state failure after successful reclaim

Partial DirectInput session is stopped/cleaned and recovery remains not live.

### 21.11 Existing PR8 same-PID1902 recovery remains unchanged

A PID1902 loss/recovery must still:

```text
ModeWriteIssued = false
no SwitchModeAsync call
HidHide before DirectInput restart
```

### 21.12 Explicit release still works after failed reclaim

Test at least one post-write failure and one pre-write failure.

The official release must still return/use the exact persisted hidden target and safely restore/accept PID1901 as appropriate.

### 21.13 Architecture guard

Verify PR9 does not introduce references/patterns such as:

```text
ExternalNativeTakeover
ConfirmExternalNativeTakeover
retryCurrentSessionAfterSafeCleanup
ControllerRecoveryManager
PhysicalRecoveryManager
PeriodicTimer
RecoveryTimer
epoch
generation
ApplyEnabledModeBaseline from recovery
```

Do not write brittle source-string tests for arbitrary formatting unless they protect an explicit architecture contract; behavior tests are preferred.

---

## 22. Full regression expectations

Run at minimum:

```text
dotnet build -c Debug
dotnet build -c Release
dotnet test -c Release
git diff --check
```

Required:

```text
0 build errors
0 new warnings
all existing tests pass
new PR9 tests pass
```

Do not delete/weaken existing PR5–PR8 tests to make the new flow pass.

Preserve tests covering:

- PID1902 startup ownership;
- strong identity boundaries;
- exact HidHide ownership;
- first presentation attach;
- X360 ↔ SteamDeck switching;
- PR8 same-PID1902 recovery;
- Enable-and-Restart release ordering;
- ordinary process teardown keeping durable PID1902/HidHide authority.

---

## 23. Manual MSI Claw validation

Hardware validation is mandatory before considering PID drift recovery proven in the product, even if CI passes.

Use a supported MSI Claw under exact Center M Disabled authority.

### 23.1 Baseline

Confirm:

```text
physical mode = PID1902
one live DirectInput source
HidHide exact PID1902 primary collection hidden
one VIIPER presentation live
controller input responsive
```

Test both presentation policies where practical:

```text
desktop / no BPM → X360
Steam game or BPM → SteamDeck
```

### 23.2 Induce same-device PID1901 drift

Using only a known safe development/diagnostic method, cause the same physical MSI Claw to enumerate as PID1901 while Addon authority remains Disabled.

Observe:

```text
owned DirectInput session terminates
virtual output becomes neutral
PR8 recovery begins
same PID1901 strong identity is recognized as owned drift
one PID1902 reclaim is issued
same device returns PID1902
same exact target is resolved
HidHide verified before DirectInput resumes
same input source reacquires
first valid state observed
controller becomes responsive again
```

### 23.3 Verify no forbidden churn

During a successful PR9 recovery confirm there is no:

```text
VIIPER server recreation
VIIPER bus recreation
X360/Deck typed-device recreation
HidHide baseline removal
PID1901 rollback after successful reclaim
second DirectInput source object
legacy routing session start
```

### 23.4 Repeat

Perform several real drift/reclaim cycles, including at least one while X360 is active and one while SteamDeck/BPM is active.

The purpose is to prove the actual handheld re-enumeration shape, not to manufacture instruction-level races.

### 23.5 Target identity observation

Record whether the exact PID1902 primary collection remains stable across the real PID1901→PID1902 reclaim.

If hardware produces a different exact collection instance:

```text
PR9 should fail closed by design
```

Do not silently expand PR9 during implementation. Use that evidence to design the next focused HidHide/PnP target-migration PR.

---

## 24. Acceptance criteria

PR9 is complete only when all of the following are true:

1. PID1901 on the same strongly identified previously-owned MSI Claw is treated as owned-state drift.
2. A different/ambiguous device never receives a mode write.
3. Center M startup configuration is read fresh and must be exactly Disabled before reclaim.
4. Reclaim issues at most one PID1901→PID1902 transition per recovery invocation.
5. Successful transition is followed by bounded stable PID1902 + strong same-identity verification.
6. No failure path performs PID1902→PID1901 rollback while Center M remains Disabled.
7. Existing PR8 exact-target rule remains: no HidHide target migration.
8. HidHide is verified/repaired before DirectInput restart.
9. The same process-owned `MsiClawInputSource` is restarted; no second source is created.
10. First valid DirectInput state is required before recovery commits.
11. Existing PR7 presentation reconcile is reused after successful recovery.
12. VIIPER server/bus/typed devices are untouched by physical recovery.
13. Explicit Enable-and-Restart remains the only normal authority-release path.
14. Existing physical-owner `_gate` serializes recovery/release/teardown; no new global authority lock is added.
15. No polling/timer/recovery manager/state machine/epoch framework is introduced.
16. Existing same-PID1902 PR8 recovery behavior remains intact.
17. Build/test suite is clean.
18. Manual hardware validation result is documented; if blocked, state that explicitly rather than claiming hardware success.

---

## 25. Expected PR size / review focus

This should remain a focused PR.

Most production change should be inside:

```text
MsiClawAddonPhysicalOwnership.RecoverLostInputCoreAsync
```

plus focused tests and logs.

`AddonProcessHost` should require little or no production change because PR8 already owns the completion event, tracked recovery task, and successful presentation-reconcile call.

Review should focus on realistic production safety:

- same-device proof before mutation;
- fresh Disabled authority before mutation;
- one mode write only;
- post-write PID/identity verification;
- HidHide-before-DirectInput ordering;
- fail-close behavior;
- explicit release still safe.

Do not block the PR for theoretical callback/instruction interleavings that the existing owner gate and eventual fail-close behavior already cover.

---

## 26. Final implementation principle

Keep the controller authority model simple:

```text
Center M Disabled
→ Addon remains authority
→ desired physical PID = PID1902

owned DirectInput loss
→ current same device PID1902
   → PR8 reacquire

owned DirectInput loss
→ current same device PID1901
   → PR9 one targeted reclaim to PID1902
   → prove same device / same exact target
   → reuse PR8 HidHide + DirectInput recovery tail

anything missing / ambiguous / different / unproven
→ fail closed
→ wait for a later real lifecycle trigger/PR
```

Do not create a second owner to recover the first owner.
