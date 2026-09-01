# Work Order — PR8: Owned DirectInput Session Recovery

## Status

Implementation work order for the first Full PID1902 owned-state recovery PR after runtime Xbox360 ↔ SteamDeck presentation switching.

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
PR8   Owned DirectInput Session Recovery                         [this PR]
  ↓
PR9+  PID drift / PnP arrival / resume / Center M resurrection /
      crash keepalive / broader owned-state lifecycle hardening
```

PR7 merged as:

```text
f9ff4612d9db6d7f642281e3cff8ae549cb63cd1
Add runtime Xbox360 <-> SteamDeck presentation switching (PR7) (#444)
```

This work order was prepared against that `main`.

The older Full 1902 design documents predate insertion of PR2.5 and therefore describe this recovery phase as `PR9+`. The current implementation sequence above is authoritative: after merged PR7, this first recovery slice is current **PR8**.

Before implementation, read and treat these as design authorities:

- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/work-order/PR5_PID1902_DIRECTINPUT_PHYSICAL_OWNERSHIP_WORK_ORDER.md`
- `docs/work-order/PR6_FIRST_VIRTUAL_PRESENTATION_ATTACH_WORK_ORDER.md`
- `docs/work-order/PR7_RUNTIME_XBOX360_STEAMDECK_PRESENTATION_SWITCHING_WORK_ORDER.md`
- current `main` implementations of:
  - `Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs`
  - `Devices/MSI/Claw/MsiClawInputContracts.cs`
  - `Devices/MSI/Claw/MsiClawInputSource.cs`
  - `Devices/MSI/Claw/MsiClawAddonPresentation.cs`
  - `Devices/MSI/Claw/MsiClawModeContracts.cs`
  - `Hosting/AddonProcessHost.cs`
  - `HidHide/AddonControllerHidHideBaseline.cs`
  - old `Devices/MSI/Claw/MsiClawRoutingComposition.cs` only as historical evidence that `MsiClawInputSource.TestCompleted` is a real session-completion signal; do **not** restore old routing fault policy or `ExternalNativeTakeover` authority semantics.

The application remains pre-release. Do not preserve obsolete Steam-session routing behavior merely for compatibility.

---

## 1. Goal

PR5 currently establishes one verified process-owned DirectInput session at Disabled-mode startup and keeps the same `MsiClawInputSource` for the process lifetime.

PR6/PR7 then publish that same source through the currently selected VIIPER presentation.

The missing real lifecycle behavior is:

```text
Center M Disabled
+ Addon owns physical PID1902
+ HidHide baseline is active
+ X360 or SteamDeck presentation is live
    ↓
owned DirectInput polling session terminates unexpectedly
    ↓
MsiClawInputSource neutralizes LatestState and cleans up the dead session
    ↓
Addon currently has no mechanism to reacquire DirectInput
```

This leaves the virtual controller present but neutral until the Runtime is restarted.

PR8 adds the smallest production recovery path for that concrete failure:

> **If an already-owned DirectInput session dies unexpectedly and the same strongly identified MSI Claw settles back to the same PID1902 / exact hidden target, reacquire DirectInput on the same process-owned input source and resume normal presentation publishing without recreating VIIPER, changing physical PID, or releasing Addon authority.**

The primary expected real-world shape is a short DirectInput/device-session disruption where Windows/PnP settles back to the same PID1902 device inside the existing bounded settle windows.

---

## 2. Why PR8 is intentionally narrow

The Full 1902 design ultimately requires one reconciliation path for:

- DirectInput loss;
- PID1901 drift;
- physical PnP loss/arrival;
- suspend/resume;
- Center M runtime resurrection;
- crash recovery.

Do **not** implement all of those in this PR.

The project policy is to protect real handheld lifecycle failures without adding unnecessary managers/state machines. A small recovery slice is easier to validate on hardware and gives later lifecycle triggers one proven primitive to reuse.

PR8 therefore handles only:

```text
owned DirectInput session lost
→ same supported physical MSI Claw proven
→ current physical mode proven PID1902
→ exact current PID1902 primary collection unchanged
→ persistent HidHide baseline proven/repaired for that same target
→ same MsiClawInputSource reacquired
```

If recovery discovers PID1901, long-term missing hardware, a different/changed exact target, or ambiguous identity, PR8 fails closed and reports the condition. The later PR must add the corresponding real lifecycle reconciliation trigger/policy.

This is not a temporary compatibility hack. It is the first narrow slice of the conceptual future:

```text
ReconcileOwnedControllerAsync(trigger)
```

Do not create that as a generalized framework now.

---

## 3. Central invariants

### 3.1 Authority does not change

Throughout PR8 recovery:

```text
Center M desired authority = Disabled / Addon
Desired physical PID       = PID1902
Persistent HidHide owner   = Addon
VIIPER server/bus owner    = Addon
```

PR8 must never perform:

```text
PID1902 → PID1901
Center M startup-root mutation
HidHide baseline removal
legacy routing fallback
```

### 3.2 Recovery must prove the same physical device

PR5 already proves strong identity across native state and DirectInput/PnP collection.

PR8 must remember the strong physical identity committed by the successful PR5 acquisition and require the recovery capture to strongly match it.

Never reacquire or mutate a different/ambiguous MSI Claw merely because VID/PID match.

### 3.3 No stale controller input after loss

`MsiClawInputSource.PollAsync` already does this in its `finally` path before `TestCompleted` is raised:

```text
LatestState = neutral
→ cleanup dead DirectInput session
→ clear _currentSession
→ TestCompleted(summary)
```

Preserve that ordering.

The currently attached X360/SteamDeck publisher may remain alive during this PR8 recovery and will therefore publish neutral while the physical input session is unavailable.

Do not detach/recreate the VIIPER presentation merely because DirectInput was momentarily lost.

### 3.4 Never resume non-neutral publishing before physical isolation is proven

This is the critical recovery-specific ordering rule.

During normal startup, PR5 may start DirectInput before first establishing the exact HidHide target because no virtual presentation is attached yet.

During PR8 recovery, a virtual presentation is already attached and its publisher already points at the same `MsiClawInputSource`.

Therefore recovery must verify/reconcile the already-owned exact HidHide target **before restarting DirectInput**, otherwise the publisher could begin forwarding a newly valid physical state before isolation is proven.

Required recovery ordering:

```text
resolve exact same PID1902 collection
→ prove same physical identity
→ require exact target == previously-owned exact target
→ apply/verify persistent HidHide baseline for that exact target
→ only then restart DirectInput
→ require first valid state
```

Do not blindly reuse the startup ordering if doing so would create a physical + virtual double-input window during recovery.

---

## 4. Strict PR8 scope

### 4.1 In scope

PR8 may:

- detect unexpected completion of the already-owned `MsiClawInputSource` session;
- distinguish normal `Stopped` teardown from unexpected owned-session termination;
- refuse recovery if the dead session's cleanup was not proven;
- keep the current virtual presentation attached and neutral while DirectInput is unavailable;
- store the strong physical identity from the successful PR5 ownership commit for same-process recovery validation;
- serialize recovery through the existing `MsiClawAddonPhysicalOwnership._gate`;
- capture stable native state using the existing bounded native/PnP capture primitive;
- reacquire only when the same physical MSI Claw is proven still `MsiClawNativeMode.DirectInput` / PID1902;
- reuse the existing bounded DirectInput descriptor resolution logic;
- require the recovered descriptor to be the same exact primary PID1902 collection already owned by PR5;
- re-apply/read-back verify the persistent HidHide baseline for that same exact target before restarting DirectInput;
- restart the **same** `MsiClawInputSource` object;
- require a first valid DirectInput state before declaring recovery successful;
- request the existing PR7 presentation reconcile after successful physical recovery so current raw Steam/BPM state is re-evaluated;
- track/drain the one in-flight recovery during controlled process shutdown;
- add focused logs/tests/manual hardware validation instructions.

### 4.2 Strictly out of scope

Do **not** implement in PR8:

- PID1901 → PID1902 reclaim after owned-state drift;
- Center M runtime resurrection detection or process suppression;
- a new Center M process watcher/poller;
- physical-device arrival/removal watcher;
- indefinite waiting for a missing controller;
- HidHide target migration when the exact PnP collection instance changes;
- broad HidHide drift monitoring;
- suspend/hibernate/resume controller reacquisition wiring;
- automatic Runtime crash restart/service/watchdog;
- VIIPER runtime recreation/recovery;
- publisher-fault automatic recovery;
- presentation manager/state-machine changes;
- Steam/BPM policy changes;
- PID1901 restoration;
- old `ExternalNativeTakeover → yield current Steam session` behavior;
- old route retry/session policy;
- generalized `ControllerRecoveryManager`, `PhysicalRecoveryManager`, epochs, generations, barriers, or retry queues;
- periodic polling/timers.

These are later focused PRs after this recovery primitive is hardware-proven.

---

## 5. Current-code facts that constrain PR8

### 5.1 `MsiClawInputSource` already exposes the right failure boundary

Current source already exposes:

```csharp
event EventHandler<MsiClawInputTestSummary>? TestCompleted;
```

and the summary includes:

```text
StopReason
CleanupSucceeded
ReadFailures
```

The production polling path already sets `LatestState` to neutral and clears the finished session before raising this event.

Reuse this existing completion signal.

Do not add another DirectInput watcher merely to rediscover a failure the owner already receives directly from the physical-input session.

The `TestCompleted` name is historical/diagnostic, but changing/duplicating it is not required for PR8 correctness.

### 5.2 `AddonProcessHost` constructs the concrete source

Current production `CreatePhysicalOwnership(...)` creates:

```csharp
var directInputInputSource = new MsiClawInputSource(...);
```

before passing that exact source into `MsiClawAddonPhysicalOwnership`.

The host can therefore subscribe the one Full-1902 owned-input completion callback at this existing construction seam.

Do not introduce a second input-source instance or a global static event.

### 5.3 PR5 physical owner already has the required serialization boundary

`MsiClawAddonPhysicalOwnership` already owns one private `_gate` used by:

```text
AcquireAsync
ReleaseForCenterMEnableAsync
DisposeAsync
```

PR8 recovery must use that same gate.

This gives the required real lifecycle serialization for:

```text
DirectInput recovery vs Enable-and-Restart physical release
DirectInput recovery vs process teardown
```

Do not add another global lock/authority manager.

### 5.4 PR7 presentation already consumes the same source object

The active publisher reads:

```text
IControllerStateSnapshotSource.LatestState
```

from the same PR5 `MsiClawInputSource` object.

PR8 should restart a new DirectInput session inside that same source object. Once first valid state resumes, the existing publisher naturally sees the new snapshots.

No publisher replacement is required solely for DirectInput recovery.

### 5.5 Persistent HidHide target is already remembered

PR5 currently stores:

```text
_ownedHiddenTarget
```

and keeps it across ordinary process-owned DirectInput teardown/failure so the official Enable-and-Restart path can later clear exactly that target.

PR8 must preserve that target through a DirectInput loss.

For this PR, recovery succeeds only if the newly resolved exact PID1902 primary collection equals the already-owned target, case-insensitively after the same path normalization assumptions the current code uses.

Do not silently replace/remove the old target in PR8.

---

## 6. Remember the committed strong physical identity

Current PR5 validates a strong `MsiClawPhysicalIdentity` but does not retain it after acquisition.

PR8 needs that same-process identity evidence to prove a recovery candidate is the controller already owned by this Runtime.

Add the smallest required in-memory field to the existing physical owner, conceptually:

```csharp
private MsiClawPhysicalIdentity? _ownedPhysicalIdentity;
```

Set it only when PR5 ownership fully commits:

```text
PID1902 verified
DirectInput exact collection strongly matched
first valid state observed
HidHide baseline verified
→ _ownedPhysicalIdentity = final strong identity
→ _ownsInputSource = true
```

Do not persist this identity to disk for PR8.

Do not invent a new authority database/journal.

On recovery failure, retain the last proven owned identity and hidden target as ownership evidence. The explicit Enable-and-Restart path must still be able to release/clear the owned state.

---

## 7. Detect only unexpected completion of an already-owned session

Preferred production callback shape is narrow and event-driven.

Conceptually in `AddonProcessHost`:

```csharp
directInputInputSource.TestCompleted += OnOwnedControllerPhysicalInputCompleted;
```

The callback must ignore expected/non-owned completions.

### 7.1 Expected stop

```text
summary.StopReason == Stopped
→ normal explicit Stop/Dispose/Enable teardown
→ no recovery request
```

### 7.2 Startup acquisition failure before ownership commit

A source may terminate while PR5 is still waiting for its first valid state.

At that moment PR5 has not committed `_ownsInputSource` / `LiveInputSource` ownership yet.

```text
not yet an owned live source
→ no runtime recovery request
→ PR5 startup acquisition itself reports failure
```

Do not start a second acquisition behind the first failed startup attempt.

### 7.3 Unexpected owned-session termination

At callback time, after `MsiClawInputSource` finalization:

```text
summary.StopReason != Stopped
AND current Full-1902 physical owner still owns that source
AND source.IsRunning == false
AND process shutdown has not begun
```

→ request PR8 recovery.

### 7.4 Cleanup must be proven

If:

```text
summary.CleanupSucceeded == false
```

then the old DirectInput device/enumerator cleanup is not proven.

Do **not** immediately acquire another DirectInput session on top of possibly retained process-owned native resources.

Required behavior:

```text
virtual publisher remains neutral
no new DirectInput start
no PID mutation
no HidHide removal
log fail-closed recovery block
```

This is a realistic native-resource safety boundary, not a theoretical race.

---

## 8. One asynchronous recovery request; no polling or retry loop

The completion callback must not synchronously run stable native capture / PnP settle / DirectInput acquisition on the input polling worker.

Preferred flow:

```text
TestCompleted callback
→ validate it is an unexpected owned-session loss
→ schedule one async recovery operation
→ existing physical-owner _gate serializes it
```

Use one process-owned tracked task, conceptually:

```csharp
private Task _ownedControllerRecovery = Task.CompletedTask;
```

or an equally small equivalent.

Do not create:

- a background recovery thread;
- a retry queue;
- a channel;
- epochs/generations;
- periodic retries;
- a watchdog.

A later **new physical session that successfully recovered and then genuinely fails again** may naturally raise another real completion event and request another recovery. That is event-driven lifecycle handling, not a timer loop.

If a recovery attempt itself fails before ownership commits, do not recursively recover its own failed startup attempt.

---

## 9. Physical-owner recovery API

Extend the existing `IMsiClawAddonPhysicalOwnership` / `MsiClawAddonPhysicalOwnership` with one narrow recovery operation.

Conceptual shape:

```csharp
Task<MsiClawPhysicalOwnershipResult> RecoverLostInputAsync(
    CancellationToken cancellationToken);
```

Exact result/name may differ; reuse the existing physical ownership result where practical rather than inventing a large new result hierarchy.

The method must:

- use the same existing physical-owner `_gate`;
- refuse after owner disposal;
- refuse after `ReleaseForCenterMEnableAsync` has begun/committed;
- require prior committed strong identity + hidden target;
- require the previously-owned input source is no longer running;
- set process-live ownership false while recovery is in progress;
- never create a second `MsiClawInputSource`;
- never issue a PID mode write in PR8.

---

## 10. Required PR8 recovery sequence

### 10.1 Entry

```text
unexpected owned DirectInput session completed
→ LatestState already neutral
→ old session cleanup proven
→ process not shutting down
→ call RecoverLostInputAsync
→ acquire existing physical-owner _gate
```

### 10.2 Require prior ownership evidence

Require:

```text
_ownedPhysicalIdentity != null
_ownedPhysicalIdentity.Confidence == Strong
_ownedHiddenTarget != null
_ownedHiddenTarget is exact primary PID1902 collection shape
previous _ownsInputSource == true
_inputSource.IsRunning == false
```

If the source is somehow still healthy/running, treat recovery as unnecessary/no-op.

Once the dead owned session is accepted for recovery:

```text
_ownsInputSource = false
```

Do not clear:

```text
_ownedPhysicalIdentity
_ownedHiddenTarget
```

### 10.3 Stable native capture

Reuse the same production native capture seam already supplied to PR5:

```csharp
_captureStableNativeState(cancellationToken)
```

Production already calls:

```csharp
nativeState.CaptureStableCurrentSnapshotAsync(
    token,
    allowTransientDeviceNotFound: true)
```

This bounded settle window is sufficient for PR8.

Do not add another PnP polling loop.

### 10.4 Require exact same PID1902 state

The capture must positively prove:

```text
current mode = DirectInput / PID1902
current identity = Strong
_ownedPhysicalIdentity.StronglyMatches(currentIdentity) == true
```

If current mode is PID1901:

```text
classify/log OwnedPhysicalStateDriftPid1901
DO NOT issue SwitchModeAsync in PR8
DO NOT yield authority as ExternalNativeTakeover
leave presentation neutral/fail-closed
```

PID1901 reclaim + Center M resurrection handling is the next focused lifecycle PR.

If native state is missing/indeterminate after the existing bounded settle:

```text
no mutation
no repeated mode commands
leave presentation neutral
```

Long physical-device disappearance / PnP arrival recovery is a later PR.

If strong identity mismatches:

```text
no mutation
no DirectInput start
no HidHide mutation
```

Never recover onto another MSI Claw.

### 10.5 Re-resolve DirectInput descriptor

Reuse the existing bounded `ResolveDirectInputDescriptorAsync(...)` logic.

Require:

```text
exact MSI Claw PID1902 DirectInput candidate selected
primary gamepad collection shape
PnP node present
PnP-derived strong identity matches the stored owned identity
```

Do not create a second selector/enumerator policy.

### 10.6 Require the exact same persistent hidden target

Let:

```text
recoveredTarget = descriptor.PnpInstanceId
ownedTarget     = _ownedHiddenTarget
```

PR8 requires:

```text
recoveredTarget == ownedTarget
```

using the repository's normal case-insensitive path comparison expectations.

If the exact target changed:

```text
DO NOT remove old hidden target
DO NOT add a replacement target
DO NOT restart DirectInput
report TargetChanged / later PnP-recovery work required
```

This prevents PR8 from quietly turning into HidHide target migration / full PnP re-enumeration reconciliation.

### 10.7 Verify/reconcile persistent HidHide BEFORE DirectInput restart

Call the existing persistent owner for the same exact target:

```csharp
ApplyDisabledModeBaseline([ownedTarget])
```

Require `IsCompliant == true` by read-back.

Because the target is already Addon-owned and unchanged, this is an idempotent baseline verification/repair at a real recovery boundary.

If it reports:

```text
Conflict
Unavailable
MutationFailed
VerificationFailed
```

then:

```text
DO NOT restart DirectInput
publisher stays neutral
PID1902 stays desired/current
persistent ownership evidence retained
```

Do not remove foreign HidHide state.

### 10.8 Fresh Center M authority check before first recovery mutation

Stable native capture may take a bounded amount of time.

Immediately before the first PR8 recovery mutation (normally HidHide apply; if it is already compliant, before DirectInput acquire is also acceptable), require a fresh shared authority read:

```text
Center M startup roots == exactly Disabled
```

If not exactly Disabled:

```text
no forward recovery mutation
```

Do not add a second authority boolean.

The existing physical-owner gate still serializes this path with official physical release.

### 10.9 Restart the SAME input source

Only after same-target HidHide isolation is proven:

```text
_inputSource.StartPrepared(recoveredDescriptor)
→ require Started
→ require IsRunning
→ WaitForFirstValidStateAsync
→ require true + IsRunning
```

Do not construct a replacement `MsiClawInputSource`.

If start/first-valid fails:

```text
SafeStopAsync if needed
_ownsInputSource remains false
LatestState remains/returns neutral
no PID rollback
no HidHide clear
```

### 10.10 Commit recovered ownership

Only after first valid state:

```text
_ownsInputSource = true
_ownedPhysicalIdentity remains same
_ownedHiddenTarget remains same
```

Log recovery success.

The existing publisher is already reading the same source object and can resume receiving live snapshots.

---

## 11. Presentation behavior during recovery

PR8 should not unnecessarily rebuild the virtual presentation.

### While physical input is unavailable

Because `MsiClawInputSource` neutralizes `LatestState` before reporting completion:

```text
X360/Deck publisher remains running
→ reads neutral state
→ virtual controller remains attached but neutral
```

This is the preferred minimal behavior for a short DirectInput session disruption.

Do not:

- detach VIIPER merely because DI stopped;
- tear down server/bus;
- switch X360 ↔ Deck as part of physical recovery itself;
- create another publisher.

### After physical recovery succeeds

Raw Steam/BPM state may have changed while the input source was down. PR7 event delivery during that period correctly refuses forward presentation mutation because the source is not running.

Therefore successful PR8 recovery must request the existing PR7 reconcile once:

```text
PhysicalInputRecovered
→ RequestControllerPresentationReconcile("PhysicalInputRecovered")
```

That existing reconcile:

```text
captures fresh raw RunningAppID + BPM inside presentation gate
→ no-op if current kind still desired
→ switch if desired changed
```

Do not duplicate PR7 selection logic inside PR8.

---

## 12. Failure policy

### 12.1 Old DirectInput cleanup unproven

```text
CleanupSucceeded == false
→ no recovery acquire
→ remain neutral/fail closed
```

### 12.2 Center M authority no longer exactly Disabled

```text
no forward recovery mutation
```

Do not infer authority from process state or cached startup state.

### 12.3 Current physical mode PID1901

```text
report OwnedPhysicalStateDriftPid1901
no mode write in PR8
no ExternalNativeTakeover yield
no legacy routing retry
```

### 12.4 Physical device missing beyond bounded settle

```text
report PhysicalDeviceMissing
no repeated mode commands
no retry timer
```

### 12.5 Identity mismatch / ambiguity

```text
no mutation
fail closed
```

### 12.6 Exact target changed

```text
no hidden-target migration
no DI restart
fail closed
```

### 12.7 HidHide cannot be proven

```text
no DI restart
virtual publisher remains neutral
```

### 12.8 DI restart / first-valid failure

```text
stop partial session when safe
keep _ownsInputSource = false
no PID rollback
no HidHide removal
```

### 12.9 Recovery failure is terminal for this event

Do not add a retry timer/loop.

The Runtime remains alive. A later focused lifecycle PR may trigger the same recovery/reconcile primitive from PnP arrival/resume/etc.

A controlled Runtime restart remains capable of running the already-proven Disabled startup acquisition path.

---

## 13. Do not reuse old routing failure semantics

The legacy routing stack contains:

```text
MsiClawPhysicalInputFaultPolicy
ConfirmExternalNativeTakeover()
ExternalNativeTakeover
PhysicalInputSessionLost
retryCurrentSessionAfterSafeCleanup
```

Those semantics belong to the old route-scoped authority model where a Steam route borrowed the physical controller and could yield/retry a Steam session.

Full 1902 Disabled mode is different:

```text
Center M Disabled
→ Addon remains desired authority regardless of Steam session
```

PR8 must not call the old routing runtime fault handler or classify an unexpected DI loss as authority automatically won by another application.

A later PID-drift PR will explicitly classify same-device PID1902 → PID1901 as owned-state drift.

Reuse only low-level safe primitives, not old authority policy.

---

## 14. Concurrency / serialization — use existing owners

### 14.1 Recovery vs Enable Center M and Restart

Do not add another global lock.

`MsiClawAddonPhysicalOwnership._gate` already serializes:

```text
recovery
vs
ReleaseForCenterMEnableAsync
```

Whichever enters the physical-owner gate first completes its one bounded physical operation.

The official Enable path still has stronger overall ordering:

```text
presentation stop/join + detach
→ VIIPER teardown
→ physical release
→ HidHide clear
→ Center M roots enable
→ restart
```

If a recovery finishes immediately before physical release, the release simply stops the newly recovered source and continues.

Do not add an epoch/barrier merely to avoid that harmless redundant work.

### 14.2 Recovery vs PR7 presentation switch

PR8 physical recovery does not take the presentation gate while reacquiring DirectInput.

During input loss, PR7 presentation reconcile sees a non-running source and blocks/no-ops.

After successful recovery, PR8 requests one normal PR7 presentation reconcile.

No new cross-owner lock is required.

### 14.3 Publisher fault during physical recovery

Publisher fault remains PR6's terminal virtual fail-close path.

If VIIPER becomes Closed/Unsafe while physical recovery is running, physical recovery may still restore DirectInput ownership, but the post-recovery PR7 reconcile will refuse presentation mutation on a non-Ready VIIPER.

Do not turn PR8 into VIIPER recovery.

---

## 15. Process shutdown contract

`BeginProcessShutdown()` already:

```text
sets _processShutdownStarted
cancels _startupCancellationTokenSource
stops Runtime event delivery
```

PR8 callback must refuse new recovery scheduling once shutdown begins.

Track the one current recovery task.

In `DisposeAsync`, required order becomes conceptually:

```text
BeginProcessShutdown
→ drain deferred unrelated startup work as today
→ await _ownedControllerRecovery
→ await existing _presentationReconcile
→ presentation owner DisposeAsync
→ physical owner DisposeAsync
→ remaining process teardown
```

Drain physical recovery **before** the existing PR7 presentation-reconcile drain because successful physical recovery may request `PhysicalInputRecovered` presentation reconciliation as its final action.

Do not dispose the physical owner/source underneath an in-flight recovery.

Do not wait for arbitrary future events; only drain the currently owned task.

---

## 16. Sleep / hibernate / resume boundary

Sleep/hibernate is a real supported product lifecycle, but full Full-1902 resume recovery is intentionally not part of this PR.

PR8 must only avoid making it worse:

- no PID1901 restore on suspend;
- no second DirectInput source;
- no new resume manager;
- no legacy stock-XInput resume baseline in Disabled mode;
- if resume naturally causes the current DirectInput session to terminate and the same PID1902 settles quickly enough for the PR8 recovery attempt, recovery may succeed through this generic session-loss path;
- if the device handle/PID/topology does not settle inside this PR's existing bounded capture, fail closed and leave full resume reconciliation to the later lifecycle PR.

Do not add suspend-notification timing races/epochs to PR8.

---

## 17. Center M Enabled path must remain unchanged

PR8 Full-1902 physical owner exists only on exact Center M Disabled startup.

For Center M Enabled:

```text
legacyRoutingAllowed = true
new Full-1902 physical owner = absent
new PR8 completion callback has no owned Full-1902 source to recover
existing legacy behavior unchanged
```

Do not route Enabled-mode sessions through the new recovery path.

---

## 18. Logging / diagnostics

Add bounded logs useful for hardware validation.

Recommended events:

```text
OwnedPhysicalInputLost
OwnedPhysicalRecoveryRequested
OwnedPhysicalRecoveryStarted
OwnedPhysicalRecoveryBlocked
OwnedPhysicalRecoverySucceeded
OwnedPhysicalRecoveryFailed
```

Useful fields:

```text
StopReason
CleanupSucceeded
ReadFailures
CurrentNativeMode
IdentityMatched
OwnedHiddenTarget
RecoveredTarget
HidHideOutcome
DirectInputStartStatus
Reason
TotalMs
```

Specific failure reasons should distinguish at least:

```text
CleanupUnproven
OwnerNotCommitted
ProcessShutdown
AuthorityNotDisabled
PhysicalDeviceMissing
PhysicalIdentityMismatch
OwnedPhysicalStateDriftPid1901
DirectInputNotResolved
DirectInputPhysicalIdentityMismatch
RecoveredTargetChanged
HidHideReconcileFailed
DirectInputStartFailed
FirstValidStateNotObserved
```

Do not log controller state every publish/poll tick.

---

## 19. Preferred production changes

Expected primary files:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
```

Possibly very small/no change to:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputSource.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputContracts.cs
```

Prefer using the already-existing `TestCompleted` signal unchanged.

Expected focused tests:

```text
tests/SteamInputAddonforClaw.Tests/MsiClawAddonPhysicalOwnershipTests.cs
tests/SteamInputAddonforClaw.Tests/MsiClawInputSourceTests.cs
```

Host architecture/wiring assertions may be added to an existing suitable host/architecture test file rather than creating a new framework.

---

## 20. Implementation guidance — keep startup and recovery semantics distinct

Do not over-refactor PR5 merely to reduce duplicated lines.

The startup path and recovery path intentionally have one important ordering difference:

### Startup

```text
no virtual attached yet
→ DirectInput first-valid verification
→ HidHide target commit
→ later PR6 attaches virtual presentation
```

### PR8 recovery

```text
virtual presentation already attached
→ exact old HidHide isolation must be proven first
→ then restart DirectInput
```

A small shared helper for identity/descriptor validation is acceptable if it genuinely simplifies the code, but do not force both flows into one abstraction that obscures this safety distinction.

Do not add strategy objects/interfaces simply to share a few statements.

---

## 21. Required tests — physical owner

Add focused deterministic tests covering at minimum the following.

### 21.1 Successful same-PID1902 recovery

```text
initial AcquireAsync succeeds
→ source later becomes stopped
→ recovery capture = same strong identity + PID1902
→ same exact descriptor/target
→ HidHide baseline compliant
→ same input source StartPrepared again
→ first valid state
→ recovery succeeds
→ LiveInputSource is the same source object and running
→ SwitchModeAsync call count unchanged
```

### 21.2 HidHide is verified before restarting DirectInput

Use ordered fake events to prove recovery order:

```text
NativeCapture
→ DescriptorResolve
→ HidHideApply/Verify
→ InputStart
→ FirstValidState
```

This ordering is mandatory for PR8.

### 21.3 Same strong identity required

A different strong physical identity must result in:

```text
recovery failed
no HidHide mutation
no InputStart
no mode write
```

### 21.4 PID1901 is detected but not reclaimed in PR8

```text
recovery capture = same identity + XInput/PID1901
→ reason = OwnedPhysicalStateDriftPid1901 (or equivalent explicit classification)
→ zero SwitchModeAsync calls
→ no InputStart
```

Do not accidentally reuse startup's PID1901 → PID1902 mode-write branch.

### 21.5 Missing / indeterminate native state

No mutation.

### 21.6 Exact PnP target changed

```text
new primary target != _ownedHiddenTarget
→ no HidHide apply for new target
→ no old-target removal
→ no InputStart
```

### 21.7 HidHide recovery failure

For Conflict / Unavailable / MutationFailed / VerificationFailed:

```text
no InputStart
_ownsInputSource remains false
```

### 21.8 DirectInput restart failure

`StartPrepared` failure leaves recovery failed and does not touch PID/HidHide teardown.

### 21.9 First valid state failure

Partial restarted source is stopped, ownership remains not-live.

### 21.10 Recovery no-op when source is still running

No native/HidHide/DirectInput mutation.

### 21.11 Explicit release remains valid after recovery failure

Even after failed recovery:

```text
_ownedHiddenTarget retained
_ownedPhysicalIdentity evidence retained
ReleaseForCenterMEnableAsync can still restore/verify stock mode as applicable
```

Do not lose the official escape/release seam.

---

## 22. Required tests — input completion and host wiring

### 22.1 Completion event observes neutral / stopped source

Extend `MsiClawInputSourceTests` to prove an unexpected polling failure raises completion only after:

```text
LatestState == neutral
IsRunning == false
```

This is the production safety contract PR8 relies on.

### 22.2 Expected `Stopped` does not request recovery

Normal physical-owner release/process teardown must not trigger a recovery task.

### 22.3 Cleanup failure does not request recovery

An unexpected summary with:

```text
CleanupSucceeded == false
```

must be fail-closed and must not call the physical recovery method.

### 22.4 Owned unexpected completion requests exactly one asynchronous recovery

No synchronous native/DirectInput work on the completion callback.

### 22.5 Startup failure is not re-entered as runtime recovery

If PR5 has not yet committed the owned input session, an early source completion must not schedule PR8 recovery.

### 22.6 Successful recovery requests existing PR7 presentation reconcile

Assert the trigger/reason is equivalent to:

```text
PhysicalInputRecovered
```

and that no duplicate Steam/BPM policy exists in PR8.

### 22.7 Shutdown drains recovery before presentation teardown

Architecture/behavioral assertion:

```text
await owned physical recovery
→ await PR7 presentation reconcile
→ presentation DisposeAsync
→ physical DisposeAsync
```

### 22.8 Center M Enabled path has no Full-1902 recovery

No Full-1902 physical owner/source means callback cannot perform PR8 recovery.

---

## 23. Architecture regression guards

Add explicit tests or source assertions, where useful, that PR8 recovery code does **not** introduce/reference:

```text
ExternalNativeTakeover
ConfirmExternalNativeTakeover
retryCurrentSessionAfterSafeCleanup
AddonRoutingRuntime recovery authority
PID1901 restore
ApplyEnabledModeBaseline
Center M startup mutation
Timer
periodic polling
ControllerRecoveryManager
PhysicalRecoveryManager
epoch/generation/recovery barrier
```

Do not use brittle source-string tests when ordinary behavioral unit tests already prove the same contract, but a few architecture guards are appropriate for authority boundaries that are easy to regress.

---

## 24. Manual MSI Claw validation

Manual validation is required when supported hardware is available. If CI/Codex has no MSI Claw hardware, report it as blocked rather than inventing a pass.

### 24.1 Baseline X360 state

```text
Center M Disabled
Steam/BPM inactive
PID1902 owned
X360 presentation live
```

Verify normal buttons/axes/D-pad before fault injection.

### 24.2 Restart only the exact owned PID1902 HID collection

Use the exact Addon-owned PID1902 primary collection instance reported in the logs/HidHide baseline. A suitable manual Windows test may use exact-device `pnputil /restart-device` rather than a broad VID/PID operation.

Do not use a wildcard that could affect unrelated devices.

Expected:

```text
DirectInput read/session fails
→ virtual X360 immediately becomes/stays neutral
→ same PID1902 device settles
→ PR8 recovery starts
→ same physical identity verified
→ same HidHide target verified
→ same input source reacquired
→ X360 input resumes
```

Logs must show no PID1901 mode write and no VIIPER server/bus/device recreation.

### 24.3 Repeat while SteamDeck presentation is active

```text
Steam game or BPM active
→ SteamDeck presentation live
→ exact PID1902 collection restart
```

Expected:

```text
Deck stays attached but neutral during loss
→ DirectInput recovers
→ current raw Steam/BPM presentation reconcile runs
→ Deck remains/live again
```

### 24.4 Steam/BPM state changes during the outage

If practical:

```text
input unavailable
→ change Steam/BPM desired presentation
→ PR7 switch is blocked while source is stopped
→ PR8 recovers input
→ PhysicalInputRecovered reconcile selects current desired presentation
```

No stale permanently-wrong presentation after recovery.

### 24.5 Failure boundary: PID1901 drift

Optional hardware proof for this PR:

If the same controller becomes PID1901 during the owned session, PR8 should:

```text
report OwnedPhysicalStateDriftPid1901
remain fail-closed/neutral
not issue an automatic PID1902 reclaim yet
```

Restarting the Runtime may use the existing proven Disabled-startup acquisition path to restore service. The dedicated runtime drift/reclaim PR will add same-session PID1902 recovery.

### 24.6 No duplicate input

During and after recovery verify:

- no simultaneous physical + virtual gameplay input;
- exactly one virtual presentation remains attached normally;
- no duplicate VIIPER device recreation;
- no unnecessary PID1901↔PID1902 churn.

---

## 25. Non-goals / overengineering guard

Do not block this PR on theoretical instruction-level timing combinations.

Do not add complexity for cases such as:

- Steam/BPM callback landing on a particular line of the DI recovery method;
- shutdown cancellation flipping between two ordinary managed assignments when the existing owner gates still converge safely;
- an artificial test causing several impossible completion callbacks for one physical session;
- unsupported multi-user/RDP/Fast User Switching behavior.

Do protect the realistic supported lifecycle:

- actual DirectInput read/session failure;
- short device/PnP restart that settles back to PID1902;
- cleanup failure;
- explicit Enable-and-Restart while recovery is possible/in flight;
- process shutdown;
- wrong/ambiguous physical identity;
- HidHide operation failure.

The goal is not maximal synchronization. The goal is one clear physical owner, one recovery operation, one failure policy.

---

## 26. Acceptance criteria

PR8 is complete only when all of the following are true:

1. An unexpectedly terminated **already-owned** DirectInput session raises one Full-1902 recovery request.
2. Normal `Stopped` teardown does not request recovery.
3. Recovery is not scheduled for a startup input attempt that never reached owned state.
4. Unproven old-session cleanup blocks reacquisition.
5. `MsiClawInputSource` has already neutralized `LatestState` before the recovery callback observes completion.
6. The active X360/SteamDeck presentation may remain attached and publishes neutral while physical input is unavailable.
7. The physical owner retains the strong identity committed by successful PR5 ownership.
8. Recovery uses the existing physical-owner `_gate`; no second physical authority/manager is added.
9. Recovery uses the same process-owned `MsiClawInputSource` object.
10. Recovery stable capture must prove the same strong physical MSI Claw.
11. PR8 recovery only proceeds when current mode is still PID1902 / DirectInput.
12. PID1901 drift is explicitly detected and fails closed with **zero mode write** in this PR.
13. Missing/ambiguous physical state causes no mutation and no retry loop.
14. The DirectInput descriptor is re-resolved through the existing bounded selector path.
15. Recovered PnP identity must strongly match the stored physical identity.
16. Recovered exact primary PID1902 target must equal the previously-owned hidden target.
17. PR8 does not migrate/remove hidden targets.
18. The persistent HidHide baseline for the same target is read-back verified **before** DirectInput restart.
19. A fresh Center M startup-root read still proves exactly Disabled before the first forward recovery mutation.
20. HidHide conflict/unavailable/mutation/read-back failure blocks DirectInput restart.
21. `StartPrepared` + first-valid-state are required before recovery commits `_ownsInputSource = true`.
22. Failed/partial DI restart is stopped safely and leaves virtual output neutral.
23. Recovery never restores PID1901.
24. Recovery never calls old `ExternalNativeTakeover`/route-session retry policy.
25. Recovery never recreates VIIPER server/bus/typed devices.
26. Recovery never creates a second DirectInput source.
27. Successful physical recovery requests the existing PR7 fresh Steam/BPM presentation reconcile.
28. No duplicate PR8 Steam/BPM policy is introduced.
29. BeginProcessShutdown prevents new recovery scheduling.
30. Controlled shutdown drains the in-flight physical recovery before the PR7 presentation reconcile and presentation/physical teardown.
31. Enable-and-Restart remains presentation teardown → physical release → HidHide clear → Center M enable → restart.
32. Center M Enabled legacy behavior remains unchanged.
33. No polling/timer/watchdog/recovery manager/epoch framework is added.
34. Focused Debug + Release builds/tests pass.
35. Full test suite passes.
36. Manual MSI Claw validation is performed when hardware is available, or explicitly reported blocked when it is not.

---

## 27. Suggested PR description

```text
Add first Full-1902 owned-state recovery: reacquire an unexpectedly lost
DirectInput session when the same strongly-identified MSI Claw is still proven
PID1902 with the same exact persistent HidHide target.

The existing physical owner and its gate are reused. MsiClawInputSource already
neutralizes LatestState before its completion signal, so the current X360/Deck
presentation can stay attached and neutral during a short physical-input outage.
Recovery re-resolves the same PID1902 collection, verifies the persistent
HidHide baseline BEFORE restarting DirectInput, restarts the same source, waits
for first valid state, then requests the existing PR7 fresh Steam/BPM
presentation reconcile.

No PID mode write, PID1901 reclaim, PnP arrival watcher, resume manager, Center M
resurrection handling, watchdog, polling, VIIPER recreation, or old routing
ExternalNativeTakeover semantics are added. PID1901 drift / long PnP loss and
broader lifecycle recovery remain focused follow-up PRs.
```
