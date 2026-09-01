# Work Order — PR10: Physical Device Loss / PnP Return Recovery

## Status

Implementation work order for the next Full PID1902 owned-state lifecycle PR after PR9 owned PID1901 drift reclaim.

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
PR9   Owned PID1901 Drift Reclaim                                [merged as #447]
  ↓
PR10  Physical Device Loss / PnP Return Recovery                 [this PR]
  ↓
PR11+ Suspend/hibernate/resume, Center M resurrection,
      HidHide target migration if hardware proves required,
      crash keepalive / broader lifecycle hardening
```

PR9 merged as:

```text
d21cdf6b4667ae00c268d5fcdaa9d5049d6cfbbd
Add owned PID1901 drift reclaim to the recovery path (PR9) (#447)
```

This work order is prepared against that `main`.

Before implementation, read and treat these as design authorities:

- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/work-order/PR5_PID1902_DIRECTINPUT_PHYSICAL_OWNERSHIP_WORK_ORDER.md`
- `docs/work-order/PR8_OWNED_DIRECTINPUT_SESSION_RECOVERY_WORK_ORDER.md`
- `docs/work-order/PR9_OWNED_PID1901_DRIFT_RECLAIM_WORK_ORDER.md`
- current `main` implementations of:
  - `Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs`
  - `Devices/MSI/Claw/MsiClawInputSource.cs`
  - `Devices/MSI/Claw/MsiClawNativeStateManager.cs`
  - `Devices/MSI/Claw/MsiClawModeContracts.cs`
  - `Controllers/Detection/WindowsControllerDeviceEnumerator.cs`
  - `Hosting/AddonProcessHost.cs`
  - `HidHide/AddonControllerHidHideBaseline.cs`

The application remains pre-release. Do not preserve obsolete route-scoped controller ownership or legacy `ExternalNativeTakeover` semantics for compatibility.

---

## 1. Goal

PR8 and PR9 now recover two concrete owned-input failures:

```text
owned DirectInput session dies
→ same MSI Claw still PID1902
→ same exact PID1902 collection
→ reacquire same DirectInput source
```

and:

```text
owned DirectInput session dies
→ same MSI Claw now PID1901
→ Center M still Disabled
→ reclaim PID1902 once
→ same exact PID1902 collection
→ reacquire same DirectInput source
```

The remaining gap is a normal physical/PnP lifecycle shape:

```text
Center M Disabled
+ Addon owns PID1902
+ DirectInput / virtual presentation live
    ↓
physical MSI Claw disappears from PnP
    ↓
DirectInput session terminates
→ source becomes neutral
→ PR8/9 recovery runs once
→ stable native capture returns DeviceNotFound / temporarily unresolved
→ recovery fails closed
    ↓
physical MSI Claw later returns through PnP
    ↓
NO CURRENT EVENT retriggers owned recovery
```

The controller therefore stays neutral until the Runtime is restarted even though the same physical controller has safely returned.

PR10 must add the smallest event-driven continuation:

> **After an already-owned physical controller disappears and the existing PR8/PR9 recovery cannot find it, listen for a real Windows device-arrival signal and re-run the same physical-owner recovery entrypoint. If the same strongly identified MSI Claw returns as PID1902 or PID1901, reuse the existing PR8/PR9 logic to restore ownership.**

This PR is about **return-triggered re-entry into the existing recovery path**.

It is not a new controller recovery manager.

---

## 2. Product contract

The Full1902 architecture explicitly requires:

```text
Physical disappearance is recoverable;
neutralize output and rebind on safe PnP return.
```

While Center M startup roots remain exactly Disabled:

```text
Desired authority       = Addon
Desired physical mode   = PID1902
Persistent HidHide      = Addon-owned
VIIPER server/bus        = Addon-owned
Virtual presentation     = existing X360 or SteamDeck policy
```

Physical disappearance does **not** release authority to Center M.

Required user-visible behavior:

```text
physical disappears
→ stale physical input becomes neutral
→ virtual output remains attached but neutral
→ no PID1901 restoration
→ no HidHide clear
→ no VIIPER teardown
→ wait for an actual device-arrival signal

same physical MSI Claw safely returns
→ re-run existing owned recovery
→ PID1902 already: PR8 path
→ PID1901 drift: PR9 reclaim path
→ HidHide isolation proven
→ same input source becomes live
→ existing PR7 presentation reconcile
```

Ambiguous/different hardware continues to fail closed.

---

## 3. Keep PR10 narrow

### 3.1 In scope

PR10 may:

- retain the existing `MsiClawInputSource.TestCompleted` signal as the physical-input-loss detector;
- recognize when an owned recovery failed because the physical controller was temporarily absent / not yet resolvable;
- keep the current VIIPER presentation attached and neutral while the controller is absent;
- add one Runtime-owned, event-driven Windows device-arrival observer;
- on a real device-arrival event, request exactly one new attempt through the existing `MsiClawAddonPhysicalOwnership.RecoverLostInputAsync` path;
- make `RecoverLostInputAsync` re-enterable after a previous failed attempt when prior committed ownership evidence still exists;
- reuse `_ownedPhysicalIdentity`, `_ownedHiddenTarget`, the existing physical-owner `_gate`, PR9 PID1901 reclaim, PR8 PID1902 recovery, exact-target validation, HidHide-before-DirectInput ordering, and PR7 presentation reconcile;
- dispose the arrival observer during Runtime shutdown;
- add focused diagnostics and tests.

### 3.2 Strictly out of scope

Do **not** implement in PR10:

- periodic PnP polling;
- a continuous topology reconciliation loop;
- a general `ControllerRecoveryManager` / `PnPRecoveryManager`;
- retry queues/channels;
- epochs/generations/barriers;
- indefinite timer-based retries;
- suspend/hibernate/resume-specific behavior;
- Center M resurrection watcher/process suppression;
- Runtime crash watchdog/service/supervisor;
- VIIPER recreation;
- publisher-fault recovery;
- Steam/BPM policy changes;
- PID1902 → PID1901 rollback after a failure;
- broad HidHide drift monitoring;
- broad process-name killing;
- legacy `ExternalNativeTakeover` / route retry behavior;
- support for Fast User Switching, RDP, or multiple interactive sessions.

### 3.3 HidHide target migration remains out of scope for PR10

PR8/PR9 intentionally require:

```text
recoveredTarget == _ownedHiddenTarget
```

Preserve that contract in PR10.

If a real MSI Claw hardware test shows that supported physical disappearance/re-arrival gives the same physical controller a **different exact PID1902 primary collection instance ID**, PR10 must fail closed with `RecoveredTargetChanged` and capture that evidence in logs.

Do **not** silently migrate the HidHide target in this PR.

Target migration is a separate lifecycle change because a crash-safe migration must reason about persistent HidHide state and explicit Center M Enable cleanup. Do not broaden PR10 without hardware evidence.

---

## 4. Existing code gap — recovery cannot currently be retried after physical absence

Current PR9 recovery starts with conceptually:

```csharp
if (!_ownsInputSource
    || _ownedPhysicalIdentity is not Strong
    || _ownedHiddenTarget is null)
{
    return OwnerNotCommitted;
}
```

Then, after accepting a dead session:

```csharp
_ownsInputSource = false;
```

If the following native capture returns `DeviceNotFound`, recovery fails.

That means a later call to `RecoverLostInputAsync` currently sees:

```text
_ownsInputSource == false
→ OwnerNotCommitted
```

Even though the owner still retains the real durable evidence:

```text
_ownedPhysicalIdentity = strong committed device identity
_ownedHiddenTarget      = exact previously-owned PID1902 collection
_releasedForEnable      = false
```

PR10 must correct this conflation.

### 4.1 Separate "live input session" from "prior ownership was committed"

Do **not** add a second authority database or persistent state.

The smallest correct interpretation is:

```text
_committed ownership evidence
    = strong _ownedPhysicalIdentity
      + valid _ownedHiddenTarget
      + not released for Center M enable

_live DirectInput session
    = _ownsInputSource && _inputSource.IsRunning
```

`_ownsInputSource` should continue to mean whether the process currently has a committed/live DirectInput session.

It must **not** be required as proof that the controller was ever owned before a retry.

Preferred recovery precondition after PR10:

```text
if no strong _ownedPhysicalIdentity
or no valid _ownedHiddenTarget
→ OwnerNotCommitted

if _inputSource.IsRunning
→ RecoveryNotNeeded

otherwise
→ recovery may be attempted again
```

No new persisted boolean is required.

A separate in-memory `OwnershipWasCommitted` bool is acceptable only if implementation proves the existing strong identity + target evidence cannot express the invariant cleanly. Prefer the existing evidence.

---

## 5. Loss detection remains the existing DirectInput completion boundary

PR10 does not need another removal detector merely to discover that the current physical input path died.

`MsiClawInputSource` already guarantees before `TestCompleted`:

```text
LatestState = neutral
→ dead DirectInput session cleanup
→ IsRunning = false
→ completion event
```

PR8 already subscribes this in `AddonProcessHost`.

Keep it authoritative for loss.

### 5.1 Why removal notification is not required in this PR

A Windows PnP removal event could arrive before, after, or during the DirectInput failure.

Adding a second removal authority would duplicate the same lifecycle fact and create unnecessary ordering complexity.

For PR10:

```text
DirectInput completion = loss / neutralization boundary
Windows device arrival = future retry trigger only
```

This keeps one owner and one recovery path.

If hardware testing later proves that physical removal can leave DirectInput live/stale for materially too long, address that demonstrated behavior separately. Do not preemptively add another loss manager.

---

## 6. Add one event-driven Windows device-arrival source

PR10 needs a real future event because the controller may return seconds or minutes after the initial bounded PR8/PR9 attempt has already failed.

Use a simple event-driven Windows notification source.

### 6.1 Preferred implementation

The project already references `System.Management` and already uses `ManagementEventWatcher` elsewhere.

The preferred small implementation is a Runtime-owned watcher over:

```text
root\CIMV2
Win32_DeviceChangeEvent
EventType = 2   // Device Arrival
```

Microsoft documents `Win32_DeviceChangeEvent.EventType == 2` as **Device Arrival**.

A broad arrival notification is acceptable because the event itself is **not authority** and does not identify the controller. It merely wakes the existing recovery operation, which then re-enumerates and requires the same strong MSI Claw identity.

Do not trust the WMI event as proof that the arriving device is the MSI Claw.

### 6.2 Acceptable code shape

Conceptually:

```csharp
internal sealed class WindowsDeviceArrivalWatcher : IDisposable
{
    internal event Action? DeviceArrived;

    internal void Start();
    public void Dispose();
}
```

Names may differ.

Keep the class narrow:

- create/start one `ManagementEventWatcher`;
- raise one managed callback for Device Arrival;
- catch/log watcher start/event exceptions;
- `Stop`/unsubscribe/dispose deterministically;
- no polling;
- no device database;
- no PID/VID interpretation;
- no controller ownership logic.

If a small interface/adapter is necessary for focused tests, keep it local to this watcher. Do not generalize it into a system-wide PnP framework.

### 6.3 Do not bind this to the frontend window

The Full1902 controller Runtime is mandatory and may run without the UI open.

Do not make PnP recovery depend on a WinUI frontend HWND or frontend lifetime.

The watcher belongs to Runtime/process lifetime in `AddonProcessHost` (or the immediately adjacent Runtime host composition), not the disposable frontend.

---

## 7. Arrival notification is only a trigger, never proof

The arrival callback must do almost no work synchronously.

Conceptual flow:

```text
Windows device arrival callback
→ if shutdown started: ignore
→ if Full1902 physical owner absent: ignore
→ if current owned source is already live: ignore
→ request async owned recovery
```

Then the existing physical owner proves:

```text
current native state exists
→ strong identity matches _ownedPhysicalIdentity
→ PID1902: PR8 continuation
OR PID1901: PR9 one-shot reclaim
→ exact DirectInput/PnP identity matches
→ exact hidden target matches
→ Center M still Disabled
→ HidHide isolation verified
→ same DirectInput source restarted
```

An unrelated USB/Bluetooth/network device arrival therefore cannot gain controller authority.

---

## 8. Reuse one host recovery scheduling seam

PR8 currently directly schedules:

```csharp
_ownedControllerRecovery = RecoverOwnedControllerPhysicalInputAsync(...);
```

from `OnOwnedControllerPhysicalInputCompleted`.

PR10 should avoid adding a second parallel recovery scheduler.

Preferred shape:

```text
RequestOwnedControllerRecovery(trigger)
```

used by both:

```text
UnexpectedDirectInputCompletion
DeviceArrival
```

This is a small local helper, not a recovery manager.

Conceptually:

```csharp
private void RequestOwnedControllerRecovery(string trigger)
{
    if (shutdown) return;
    var physical = _physicalOwnership;
    if (physical is null) return;
    if (physical.LiveInputSource?.IsRunning == true) return;

    _ownedControllerRecovery =
        RecoverOwnedControllerPhysicalInputAsync(physical, trigger, shutdownToken);
}
```

The existing physical-owner `_gate` remains the serialization authority.

### 8.1 No artificial retry loop

If an arrival-triggered attempt still sees `DeviceNotFound`:

```text
fail closed
→ remain neutral
→ wait for another real future Device Arrival event
```

Do not sleep/retry internally after the existing bounded native/DirectInput settle windows expire.

### 8.2 Avoid unnecessary recovery storms

A device-arrival event is broad, but the normal system path should be cheap:

```text
owned source live
→ callback returns immediately
```

When the source is down, the existing owner gate and bounded recovery operation provide enough real lifecycle serialization.

Do not add epoch/generation/state-machine machinery solely because several PnP notifications can theoretically arrive close together.

A simple `if (!_ownedControllerRecovery.IsCompleted) return;` coalescing check is acceptable if needed to avoid obviously redundant concurrent recovery attempts, but do not let that check miss the only real arrival event after a completed `DeviceNotFound` result. Prefer correctness and the existing gate over elaborate task tracking.

---

## 9. Recovery-result classification for PnP absence

Do not create a large result hierarchy.

Current `MsiClawPhysicalOwnershipResult` already carries:

```text
Outcome
Reason
ModeWriteIssued
HiddenTarget
```

Continue using it.

The host may classify a failed recovery as "still waiting for PnP return" when the reason is one of the concrete topology-not-ready shapes, for diagnostics only, for example:

```text
PhysicalDeviceMissing:...
DirectInputNotResolved
DirectInputNotResolved:PnpNodeMissing
```

This classification must not become a second source of authority.

The next Device Arrival event may simply request recovery again regardless; the physical owner still performs all safety checks.

Avoid string parsing spread across multiple classes. If the implementation needs a reusable classification, prefer one small helper or one narrow property on the result rather than a new recovery state machine.

---

## 10. Same-target requirement on return

After the MSI Claw returns and PID1902 is proven, PR10 still requires:

```text
DirectInput descriptor is primary PID1902 gamepad collection
PnP node resolves
strong DirectInput identity matches _ownedPhysicalIdentity
recoveredTarget == _ownedHiddenTarget (case-insensitive)
```

If all are true:

```text
ApplyDisabledModeBaseline([ownedTarget])
→ HidHide verified
→ restart same DirectInput source
```

If the returned exact target changed:

```text
RecoveredTargetChanged
→ do not remove old target
→ do not hide new target
→ do not restart DirectInput
→ stay neutral
→ log old target + recovered target + strong identity result
```

This log is important hardware evidence for deciding whether the next PR needs crash-safe HidHide target migration.

---

## 11. PID1902/PID1901 return behavior is already implemented

Do not duplicate PR8/PR9 logic in the arrival watcher.

### Return as PID1902

```text
Device Arrival
→ RecoverLostInputAsync
→ same strong identity + PID1902
→ existing PR8 tail
```

Expected success result remains:

```text
Outcome         = Owned
Reason          = OwnedPhysicalInputRecovered
ModeWriteIssued = false
```

### Return as PID1901

```text
Device Arrival
→ RecoverLostInputAsync
→ same strong identity + PID1901
→ fresh Center M Disabled check
→ one PR9 PID1902 reclaim
→ post-reclaim identity verification
→ existing PR8 tail
```

Expected success remains:

```text
Outcome         = Owned
Reason          = OwnedPhysicalStateDriftReclaimed
ModeWriteIssued = true
```

Do not add `RecoverReturnedDeviceAsync` or another mode-reconciliation path.

---

## 12. HidHide-before-DirectInput remains mandatory

The physical device may return while a VIIPER X360/SteamDeck presentation is still attached.

The presentation publisher is reading the same stopped/neutral `MsiClawInputSource`.

Therefore return recovery must preserve:

```text
same exact target proven
→ HidHide baseline read-back verified
→ only then restart DirectInput
→ require first valid state
```

Never restart DirectInput first.

The concrete safety reason remains:

```text
physical device returns unisolated
+ DirectInput starts producing non-neutral state
+ existing virtual publisher forwards it
→ physical + virtual double-input risk
```

---

## 13. Center M authority remains fresh and authoritative

A controller may be absent for a long time.

Do not assume Center M startup authority is still Disabled merely because it was Disabled when the device disappeared.

The existing PR8/PR9 recovery already performs fresh authority reads at mutation boundaries.

Preserve those checks.

If the user changed authority while the device was absent:

```text
CenterMStartupState != Disabled
→ recovery fails closed
→ zero new PID reclaim if authority check precedes it
→ zero DirectInput restart
→ no HidHide migration/clear in PR10
```

The official `Enable Center M and Restart` flow remains the sole normal release boundary.

---

## 14. Presentation behavior

PR10 must not add presentation ownership.

While the physical device is absent:

```text
LatestState = neutral
existing X360/SteamDeck presentation may stay attached
publisher may stay alive and publish neutral
```

After successful return recovery, existing host behavior requests:

```text
RequestControllerPresentationReconcile("PhysicalInputRecovered")
```

Keep that path.

Optionally make the trigger/log more specific, e.g. `PhysicalInputRecoveredAfterPnPReturn`, only if it improves diagnostics without duplicating policy. The actual desired X360/SteamDeck state must still come from the existing raw RunningAppID/BPM snapshot.

Do not recreate VIIPER server/bus or publishers simply because the physical controller disappeared.

---

## 15. Runtime / shutdown lifecycle

The device-arrival watcher is process-owned.

Required shutdown ordering:

```text
BeginProcessShutdown
→ prevent new arrival-triggered recovery scheduling
→ stop/dispose device-arrival watcher
→ drain _ownedControllerRecovery
→ drain _presentationReconcile
→ retire presentation
→ dispose physical ownership
```

Exact ordering between stopping the watcher and draining the current recovery may be adjusted for the current host structure, but these invariants are mandatory:

- no new recovery after shutdown starts;
- no WMI callback after its owner is disposed;
- one in-flight physical recovery is drained before presentation/physical teardown;
- ordinary Runtime shutdown while Center M remains Disabled does not restore PID1901.

Do not add a separate shutdown manager.

---

## 16. Enable-and-Restart overlap

A physical return can occur while the user is initiating the official Center M Enable flow.

The existing physical-owner `_gate` plus `_releasedForEnable` is the authority boundary.

Required behavior:

```text
arrival recovery gets gate first
→ performs one safe recovery attempt or fails
→ release obtains gate afterward
→ official release wins next
```

or:

```text
release gets gate first
→ _releasedForEnable = true
→ later arrival recovery enters
→ ReleasedForCenterMEnable
→ no forward ownership mutation
```

Do not add a second lock/epoch to defend instruction-level callback timing.

---

## 17. Failure policy

### 17.1 Device still absent

```text
arrival callback occurs
→ stable capture still DeviceNotFound
→ fail closed
→ remain neutral
→ wait for another real arrival event
```

### 17.2 Different physical MSI Claw appears

```text
strong identity mismatch
→ no PID write
→ no HidHide mutation
→ no DirectInput restart
```

### 17.3 Returned as PID1901 but authority no longer Disabled

```text
AuthorityNotDisabled
→ no reclaim
→ fail closed
```

### 17.4 PID1901 reclaim fails

Existing PR9 behavior:

```text
one attempted mode write
→ no reverse write
→ remain neutral
```

### 17.5 Exact PID1902 collection changed

```text
RecoveredTargetChanged
→ no migration in PR10
→ no DirectInput restart
→ remain neutral
→ log evidence
```

### 17.6 HidHide verification fails

```text
no DirectInput restart
→ remain neutral
→ persistent ownership evidence retained
```

### 17.7 DirectInput restart / first valid state fails

Existing PR8 behavior:

```text
stop partial source
→ no PID1901 rollback
→ remain neutral
```

### 17.8 Device-arrival watcher itself fails to start

Do not crash the Runtime.

```text
log DeviceArrivalWatcherUnavailable
→ current Full1902 ownership remains as-is
→ no polling fallback
```

The controller can still recover from short losses caught inside existing bounded windows; a later Runtime restart can re-establish the watcher.

Do not silently introduce a timer fallback.

---

## 18. Logging

Add structured logs sufficient to distinguish:

```text
OwnedPhysicalInputLost
OwnedPhysicalRecoveryFailed / PhysicalDeviceMissing
ControllerDeviceArrivalObserved
OwnedPhysicalRecoveryRequested Trigger=DeviceArrival
OwnedPhysicalRecoveryStarted
OwnedPhysicalRecoveryNativeProven
OwnedPhysicalStateDriftDetected          // PR9 existing
OwnedPhysicalPid1902ReclaimCompleted     // PR9 existing
OwnedPhysicalRecoveryDirectInputResolved
OwnedPhysicalRecoveryIsolationVerified
OwnedPhysicalRecoverySucceeded
RecoveredTargetChanged
DeviceArrivalWatcherStarted
DeviceArrivalWatcherStopped
DeviceArrivalWatcherUnavailable
```

For target-change failure include both:

```text
OwnedHiddenTarget
RecoveredTarget
```

Never log a device arrival itself as proof of MSI Claw identity.

---

## 19. Expected production file changes

Prefer a small diff centered on existing ownership seams.

Likely production files:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
```

plus one small device-arrival watcher file, for example:

```text
src/SteamInputAddonforClaw/Controllers/Detection/WindowsDeviceArrivalWatcher.cs
```

or an equally appropriate nearby location.

Tests likely:

```text
tests/SteamInputAddonforClaw.Tests/MsiClawAddonPhysicalOwnershipTests.cs
tests/SteamInputAddonforClaw.Tests/<device-arrival-watcher-tests>.cs
tests/SteamInputAddonforClaw.Tests/<host recovery wiring tests>.cs
```

Do not create a new top-level recovery subsystem/folder unless current repository organization absolutely requires it.

---

## 20. Required tests

### 20.1 Recovery can re-enter after an earlier DeviceNotFound failure

```text
successful prior ownership
→ simulate session loss
→ recovery #1 stable capture = DeviceNotFound
→ Failed / PhysicalDeviceMissing
→ source remains neutral/not live
→ recovery #2 same strong device PID1902 + same target
→ OwnedPhysicalInputRecovered
```

This test is mandatory because it proves `_ownsInputSource` no longer incorrectly means "ownership was never committed".

### 20.2 Re-entry still refuses when ownership was never committed

```text
startup acquisition never succeeded
→ no _ownedPhysicalIdentity commit
→ recovery call
→ OwnerNotCommitted
→ zero PID/HidHide/DI mutation
```

### 20.3 Arrival while source is healthy is a no-op at host boundary

```text
owned source IsRunning = true
→ DeviceArrival
→ no recovery task/mutation
```

### 20.4 Arrival after physical-missing recovery requests the existing recovery path

```text
owned source lost
→ first recovery returns PhysicalDeviceMissing
→ DeviceArrival event
→ RecoverLostInputAsync called again
```

No new recovery implementation may be called.

### 20.5 Same device returns PID1902

```text
first attempt missing
→ arrival
→ same identity PID1902
→ same exact target
→ HidHide before DI start
→ recovery success
→ zero mode write
```

### 20.6 Same device returns PID1901

```text
first attempt missing
→ arrival
→ same identity PID1901
→ fresh Disabled authority
→ exactly one PID1902 write
→ final same identity PID1902
→ same target
→ HidHide before DI start
→ success
```

### 20.7 Different physical identity on arrival never receives a mode write

Especially when the different device is PID1901.

```text
arrival
→ strong identity mismatch
→ Failed
→ zero mode write
→ zero HidHide mutation
→ zero DI start
```

### 20.8 Changed exact target remains fail-closed

```text
same strong physical identity returns
→ exact primary PID1902 collection != _ownedHiddenTarget
→ RecoveredTargetChanged
→ no HidHide mutation
→ no DI start
```

Capture both target IDs in diagnostics if practical.

### 20.9 Repeated unrelated arrival while source live does nothing

Prove normal device arrivals do not cause topology/native work while controller operation is healthy.

### 20.10 Device-arrival watcher start/stop lifecycle

At minimum verify:

```text
Start subscribes/starts once
Device Arrival raises callback
Dispose stops/unsubscribes/disposes
post-dispose event does not escape
```

Do not create pathological start/dispose race tests unless the implementation naturally exposes a realistic lifecycle issue.

### 20.11 Watcher failure does not start polling

Architecture/source guard or focused test:

```text
watcher start throws/fails
→ Runtime continues
→ no Timer / PeriodicTimer / polling recovery path introduced
```

### 20.12 Shutdown blocks new arrival recovery

```text
BeginProcessShutdown
→ DeviceArrival callback
→ no new recovery request
```

### 20.13 Existing PR8/PR9 tests remain unchanged/passing

Especially:

- same PID1902 recovery;
- PID1901 drift reclaim;
- different identity rejection;
- Center M authority gate;
- post-mode-write identity verification;
- HidHide-before-DirectInput ordering;
- explicit Enable release after failed recovery.

---

## 21. Manual MSI Claw hardware validation — mandatory before considering PR10 complete for product use

Automated tests can prove orchestration, but this PR specifically targets real PnP lifecycle behavior.

Use a supported MSI Claw with Center M authority Disabled / Addon active.

### 21.1 Baseline

Confirm:

```text
physical = PID1902
HidHide = active/compliant
virtual = one X360 or SteamDeck presentation
controller input = working
```

### 21.2 Trigger a real physical/PnP disappearance

Use a real supported lifecycle mechanism available on the device/test environment that removes/re-enumerates the physical controller without intentionally enabling Center M.

Capture logs proving:

```text
DirectInput completion
LatestState neutral
PhysicalDeviceMissing / temporary unresolved recovery result
no PID1901 restore
no HidHide clear
no VIIPER teardown
```

### 21.3 Return as PID1902

When the physical device returns, prove:

```text
DeviceArrival observed
same strong physical identity
PID1902
same exact hidden target
HidHide verified before DI restart
same input source live again
presentation resumes
```

### 21.4 Return as PID1901 if the real device does so

Prove PR9 is reused:

```text
DeviceArrival
same strong identity PID1901
fresh Disabled authority
one PID1902 reclaim
post-reclaim same identity
same target
recovery succeeds
```

### 21.5 Record exact target stability

This is important for roadmap decisions.

Compare before/after:

```text
old _ownedHiddenTarget
new resolved PID1902 primary collection
```

If they are identical, PR10 scope is validated.

If they differ on a normal supported PnP return, do **not** patch PR10 ad hoc. Record the evidence and create the next focused work order for crash-safe HidHide target migration.

### 21.6 Long absence

Leave the physical device absent longer than the current bounded native/DI settle windows, then return it.

Success criterion:

```text
initial recovery already failed
→ later real arrival still retriggers recovery
→ controller recovers without Runtime restart
```

This is the principal PR10 acceptance scenario.

---

## 22. Build / validation requirements

Before opening the PR:

```text
dotnet build -c Debug
dotnet build -c Release
dotnet test -c Release
git diff --check
```

Requirements:

- 0 build errors;
- no new warnings attributable to PR10;
- full existing test suite passes except documented pre-existing hardware skips;
- focused PR10 tests pass;
- no new periodic polling/timer recovery code;
- no new authority/state manager;
- no regression to PR8/PR9 recovery;
- no PID1901 restoration while Center M remains Disabled.

If manual MSI hardware validation cannot be performed in the implementation environment, mark it explicitly as blocked in the PR description rather than claiming it passed.

---

## 23. Acceptance criteria

PR10 is complete when all of the following are true:

1. A long physical/PnP disappearance no longer permanently strands the process after the first `DeviceNotFound` recovery failure.
2. Previous committed ownership evidence remains sufficient to retry recovery after `_ownsInputSource` becomes false.
3. Recovery is still impossible when ownership was never successfully committed.
4. One Runtime-owned event-driven Windows Device Arrival observer exists.
5. There is no PnP polling/timer fallback.
6. The arrival event is only a trigger; strong physical identity remains the mutation authority.
7. Healthy owned input ignores unrelated device-arrival notifications without native/HidHide/DI work.
8. Same-device PID1902 return reuses PR8.
9. Same-device PID1901 return reuses PR9 and performs at most one reclaim write per attempt.
10. Different/ambiguous hardware never receives a mode write.
11. Exact returned target must still equal the previously-owned hidden target.
12. Changed exact target fails closed with useful diagnostics and no target migration.
13. HidHide is proven compliant before DirectInput restart.
14. The same `MsiClawInputSource` object is reused.
15. Existing virtual presentation remains the PR6/PR7 owner; no VIIPER recreation is introduced.
16. Successful recovery requests the existing PR7 presentation reconcile.
17. Center M startup authority is freshly checked at existing mutation boundaries.
18. Enable-and-Restart remains authoritative through the existing physical-owner gate.
19. Runtime shutdown stops the arrival watcher and prevents new recovery scheduling before teardown.
20. No new recovery manager/state machine/epoch/barrier is introduced.
21. Debug/Release builds and full tests pass.
22. Manual hardware testing records whether the exact PID1902 HidHide target remains stable across real PnP return.

---

## 24. Explicit non-goals / do-not-overengineer reminder

Do not solve theoretical callback interleavings merely because a test can force them.

Examples that do **not** justify new epochs/generations/barriers:

- a device arrival callback landing on the exact instruction where a recovery task is assigned;
- an unrelated USB arrival occurring between two recovery checks;
- shutdown flag changing between two adjacent managed statements;
- multiple WMI arrival callbacks queued in an artificial scheduler order.

The real production invariants are enough:

```text
DirectInput loss neutralizes state
+ one physical owner
+ one physical-owner gate
+ strong physical identity before mutation
+ fresh Center M authority before mutation
+ no internal retry loop
+ safe fail-closed result
+ future real arrival can trigger another attempt
```

Add complexity only if a realistic supported lifecycle produces a demonstrated user-impacting failure.

---

## 25. PR description checklist

The implementation PR should state:

- that it implements PR10 Physical Device Loss / PnP Return Recovery;
- the exact `main` base used;
- that loss detection still comes from `MsiClawInputSource.TestCompleted`;
- what Windows Device Arrival mechanism was used;
- that the arrival event is only a wake-up trigger, not identity authority;
- that `RecoverLostInputAsync` can now re-enter after a previous `DeviceNotFound` failure using retained committed identity/target evidence;
- that PR8 PID1902 and PR9 PID1901 paths are reused rather than duplicated;
- that HidHide target migration remains out of scope;
- Debug/Release build results;
- full test count;
- focused PR10 test results;
- `git diff --check` result;
- manual MSI Claw validation result or explicit `BLOCKED — no supported hardware`.
