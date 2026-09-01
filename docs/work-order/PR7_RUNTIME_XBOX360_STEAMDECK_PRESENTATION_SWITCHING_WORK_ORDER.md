# Work Order — PR7: Runtime Xbox360 ↔ SteamDeck Presentation Switching

## Status

Implementation work order for the next Full PID1902 controller PR after the first virtual presentation attach.

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
PR7   Runtime Xbox360 ↔ SteamDeck Presentation Switching         [this PR]
  ↓
PR8+  Owned-state recovery / lifecycle hardening / obsolete-routing cleanup
```

PR6 merged as:

```text
a150527cc09d2dd8bc42c1d5af9a0c4048adbc20
Add first virtual presentation attach (PR6) (#441)
```

This work order was prepared against that `main`.

The older Full 1902 architecture documents were written before PR2.5 was inserted, so their roadmap numbering is shifted. Their old `PR8` presentation-switching slot corresponds to the current **PR7**.

Before implementation, read and treat these as the design authorities:

- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/work-order/PR5_PID1902_DIRECTINPUT_PHYSICAL_OWNERSHIP_WORK_ORDER.md`
- `docs/work-order/PR6_FIRST_VIRTUAL_PRESENTATION_ATTACH_WORK_ORDER.md`
- current `main` implementation of:
  - `Devices/MSI/Claw/MsiClawAddonPresentation.cs`
  - `Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs`
  - `Devices/MSI/Claw/MsiClawInputContracts.cs`
  - `Devices/MSI/Claw/MsiClawInputSource.cs`
  - `Hosting/AddonProcessHost.cs`
  - `Runtime/AddonRuntimeComposition.cs`
  - `Runtime/AddonRuntimeHost.cs`
  - `Steam/SteamSessionRuntime.cs`
  - `Steam/SteamRunningAppIdRegistrySource.cs`
  - `Steam/SteamBigPictureWindowProbe.cs`
  - `VirtualOutput/Viiper/CanonicalViiperRuntime.cs`
  - `VirtualOutput/Viiper/CanonicalSteamDeckSession.cs`
  - `VirtualOutput/Viiper/CanonicalSteamDeckInputPublisher.cs`
  - `VirtualOutput/Viiper/CanonicalXbox360InputPublisher.cs`
  - old `Routing/AddonRoutingRuntime.cs` only as historical/proven low-level attach-detach ordering; do **not** restore its old routing authority.

The project remains pre-release. Existing Steam-session routing and the temporary Game Bar/Xbox360 presentation path are not compatibility contracts for the new Full-1902 controller authority architecture.

---

## 1. Goal

PR6 established a valid process-lifetime controller state after startup:

```text
Center M roots = Disabled
physical MSI Claw = PID1902
PR5 DirectInput source = live
persistent HidHide exact target = verified
canonical VIIPER runtime = Ready
exactly one typed virtual device = attached/live
matching publisher = running
other typed device = Detached
```

PR7 makes the selected virtual presentation follow the actual runtime Steam state without disturbing physical controller ownership.

Required runtime policy:

```text
actual Steam game inactive
AND BPM inactive
    → Xbox360 presentation

actual Steam game active
OR BPM active
    → SteamDeck presentation
```

Runtime transition examples:

```text
Xbox360 live
→ Steam game starts
→ SteamDeck live

SteamDeck live
→ Steam game ends and BPM is inactive
→ Xbox360 live

Xbox360 live
→ BPM opens
→ SteamDeck live

SteamDeck live
→ BPM closes while a Steam game is still active
→ SteamDeck remains live
```

The central PR7 invariant is:

> **Runtime presentation changes must change only the attached VIIPER typed device and its publisher. PID1902, the PR5 DirectInput source, the persistent HidHide baseline, the physical MSI Claw identity, and the canonical VIIPER server/bus must remain unchanged.**

---

## 2. Product authority contract — unchanged

PR7 must not alter the Full-1902 ownership model.

While Center M is Disabled:

```text
Physical authority            = Addon Runtime
Desired physical PID          = PID1902
Physical input                = PR5 DirectInput owner
Physical isolation            = persistent Addon HidHide baseline
Virtual native owner          = one canonical VIIPER runtime
Presentation selector         = actual Steam game OR BPM
```

Steam/BPM decides only:

```text
Xbox360 vs SteamDeck presentation
```

Steam/BPM must never decide:

```text
whether Addon owns the physical controller
whether PID1902 should exist
whether DirectInput should be acquired
whether HidHide should be active
whether Center M authority should be released
```

Do not reintroduce the old policy:

```text
Steam starts → take physical controller
Steam ends   → restore PID1901
```

That policy is obsolete for Center M Disabled mode.

---

## 3. Strict PR7 scope boundary

### 3.1 In scope

PR7 may:

- extend the existing PR6 `MsiClawAddonPresentation` owner with runtime reconciliation/switching;
- consume the existing raw RunningAppID change event;
- consume the existing event-driven Big Picture state change callback;
- capture the freshest raw `SteamPresentationSnapshot` at the actual presentation mutation boundary;
- switch Xbox360 → SteamDeck;
- switch SteamDeck → Xbox360;
- attach a desired presentation when the owner currently has no active presentation but VIIPER + PR5 input are still healthy;
- no-op when the already-active presentation matches current policy and its publisher is healthy;
- reuse the existing PR6 serialization gate;
- preserve the existing PR6 publisher-fault fail-close behavior;
- preserve the existing PR6 explicit Center M release and process-teardown ordering;
- add bounded runtime switching diagnostics;
- add focused unit/architecture tests and hardware validation instructions.

### 3.2 Strictly out of scope

Do **not** implement in PR7:

- PID1902 drift recovery;
- PID1901 reclaim after external mode drift;
- physical PnP removal/re-arrival recovery;
- DirectInput reacquisition after physical-input loss;
- HidHide drift repair;
- Center M process resurrection suppression/recovery;
- automatic Runtime crash restart/service/watchdog;
- full suspend/hibernate/resume controller reacquisition;
- a new power lifecycle state machine;
- Game Bar foreground presentation policy for the new Full-1902 owner;
- OEM1/WING presentation policy changes;
- Steam/QAM button remapping changes;
- Overlay policy changes;
- rumble/haptic redesign;
- gyro changes;
- a generic `PresentationManager`;
- a generic `ControllerAuthorityManager`;
- epochs/generations/version barriers for ordinary RunningAppID/BPM events;
- periodic polling;
- broad deletion/refactor of the legacy routing stack.

PR8+ will handle real owned-state recovery/lifecycle hardening after the basic persistent-controller path is proven on hardware.

---

## 4. Current-code findings that constrain PR7

### 4.1 PR6 already owns the correct presentation state

Current `MsiClawAddonPresentation` owns:

```text
CanonicalViiperRuntime
_activeKind
_publisher
_deckSession
one _gate
one publisher-fault cleanup task
```

That is already the correct in-memory authority for presentation state.

Do not create another owner just for runtime switching.

The existing `_gate` is specifically documented to serialize:

```text
attach
publisher-fault cleanup
Center M release
process teardown
```

PR7 should extend that same gate to serialize runtime presentation reconcile/switch operations.

### 4.2 PR6 already has the reusable target attach primitives

Current PR6 code already has narrow methods equivalent to:

```text
AttachXbox360Async(source)
AttachSteamDeckAsync(source)
```

They already enforce:

```text
target is detached
→ attach target
→ neutral target
→ create matching publisher using the SAME PR5 source
→ start publisher
→ commit active kind
```

Reuse/refactor these helpers rather than implementing a second attach path.

### 4.3 PR6 `RetireAsync` currently tears down VIIPER

Current PR6 retirement does:

```text
stop/join active publisher
→ detach selected typed device
→ clear active presentation
→ CanonicalViiperRuntime.TeardownAsync()
```

That is correct for:

- `Enable Center M and Restart`;
- controlled process teardown;
- the current terminal publisher-fault path.

It is **not** correct for normal PR7 X360 ↔ Deck switching, because presentation switching must keep:

```text
same VIIPER server
same bus
same typed X360 device object
same typed SteamDeck device object
```

PR7 therefore needs a small internal distinction between:

```text
retire current active presentation only
```

and:

```text
retire presentation + final VIIPER teardown
```

Do not introduce another public lifecycle owner for this distinction.

### 4.4 Raw Steam/BPM facts already exist

PR6 added:

```csharp
SteamPresentationSnapshot(
    uint RunningAppId,
    bool BigPictureActive)
```

with:

```text
WantsSteamDeck = RunningAppId != 0 || BigPictureActive
```

`SteamSessionRuntime.CapturePresentationSnapshot()` already reads:

```text
raw RunningAppID
+ current event-driven BPM state
```

and is already exposed through `AddonRuntimeHost.CapturePresentationSnapshot()`.

Reuse it unchanged unless a very small signature adjustment is required.

Do not use `EffectiveSteamSessionSource.State` for PR7.

### 4.5 Runtime RunningAppID event already reaches `AddonProcessHost`

Current production wiring already subscribes:

```csharp
_runtimeHost.ActualRunningAppIdChanged += OnActualRunningAppIdChanged;
```

`OnActualRunningAppIdChanged` currently fans out to:

- QAM host state;
- CPU Boost profile;
- Power Mode profile;
- display resolution profile;
- TDP profile;
- Intel FPS profile.

PR7 should reuse this existing callback.

Do not instantiate another registry watcher.

### 4.6 BPM state already has an existing process-host callback seam

Current `AddonRuntimeCompositionFactory.Create(...)` receives:

```csharp
bigPictureStateChanged: _qamHostController.OnBigPictureStateChanged
```

`SteamSessionRuntime` already raises raw:

```csharp
BigPictureStateChanged(bool active)
```

PR7 can replace that direct QAM callback with one local `AddonProcessHost` handler which:

```text
1. forwards the same bool to QamHostProcessController unchanged
2. requests Full-1902 presentation reconcile
```

Preferred conceptual shape:

```csharp
bigPictureStateChanged: OnBigPictureStateChanged
```

where:

```csharp
private void OnBigPictureStateChanged(bool active)
{
    _qamHostController.OnBigPictureStateChanged(active);
    RequestControllerPresentationReconcile("BigPictureChanged");
}
```

Do not create another BPM watcher or a second raw-state event graph.

---

## 5. Presentation selection remains raw actual state

PR7 policy remains exactly the PR6 selection policy:

```text
RunningAppId == 0
AND BigPictureActive == false
    → Xbox360

RunningAppId != 0
OR BigPictureActive == true
    → SteamDeck
```

Do not use:

- `SteamInputRoutingEnabled`;
- Developer Test Mode;
- `EffectiveSteamSessionSource`;
- old `RoutingOperationalState`;
- old `SteamOutputActive`;
- Game Bar foreground state;
- QAM visibility;
- frontend visibility;
- profile state.

The old routing preference must not prevent SteamDeck presentation when Addon owns the controller and a real Steam game/BPM is active.

Developer Test Mode must not create a production SteamDeck switch.

---

## 6. Event-driven only — no polling

Runtime switching must be driven only by the already-existing raw state changes:

```text
SteamRunningAppIdRegistrySource.Changed
→ SteamSessionRuntime.ActualRunningAppIdChanged
→ AddonRuntimeHost.ActualRunningAppIdChanged
→ AddonProcessHost

SteamBigPictureWatcher.StateChanged
→ SteamSessionRuntime.BigPictureStateChanged
→ existing AddonRuntimeCompositionFactory callback
→ AddonProcessHost
```

Do not add:

- timers;
- periodic registry reads;
- periodic BPM scans;
- fixed-delay reconcile loops;
- polling threads.

`SteamBigPictureWatcher` remains event-driven with its existing bounded one-shot startup/replacement behavior.

---

## 7. Runtime event delivery — keep it simple

RunningAppID/BPM changes are low-frequency real product events.

Do not build a generalized delivery queue/dispatcher/state machine for them.

A small asynchronous request from the process-host callback is sufficient.

Preferred principle:

```text
raw event arrives
→ schedule one async presentation reconcile
→ existing MsiClawAddonPresentation._gate serializes it
→ after entering the gate, capture current raw Steam/BPM facts again
→ reconcile to that current desired presentation
```

This has two important properties:

1. event callbacks do not block the registry watcher / WinEvent delivery thread while VIIPER publisher stop/join and native attach/detach run;
2. queued duplicate/overlapping events do not apply stale desired state because the actual snapshot is captured only after the existing presentation gate is acquired.

### 7.1 Do not capture a stale snapshot before waiting for the gate

Avoid this shape for runtime switching:

```csharp
var snapshot = runtimeHost.CapturePresentationSnapshot();
await presentationGate;
apply(snapshot);
```

A previous switch may still be running while another Steam/BPM event changes the desired state.

Instead prefer a reconcile API which can capture inside the existing owner gate, conceptually:

```csharp
Task<...> ReconcileDesiredPresentationAsync(
    IMsiClawPreparedInputSource source,
    Func<SteamPresentationSnapshot> captureSnapshot,
    CancellationToken token)
```

Inside `_gate`:

```text
verify owner usable
verify source running
captureSnapshot()
compute desired kind
compare with current kind
switch only if needed
```

The delegate is a read-only fact capture; it does not make the presentation owner a Steam watcher.

Exact signature may vary, but preserve this boundary.

### 7.2 No epoch/generation required

Do not add a presentation epoch merely because two source callbacks can arrive close together.

The existing gate + fresh capture at the actual mutation boundary is enough for the supported single-user/single-session product lifecycle.

---

## 8. Request runtime reconcile early in the existing RunningAppID callback

`OnActualRunningAppIdChanged` currently performs several profile reconciles that may involve hardware/local-I/O work.

PR7 controller switching should not wait behind those unrelated profile mutations.

Preferred order:

```text
OnActualRunningAppIdChanged(appId)
    ↓
forward appId to QAM host as today
    ↓
request async controller-presentation reconcile immediately
    ↓
continue existing CPU Boost / Power Mode / Resolution / TDP / FPS profile work unchanged
```

Do not synchronously perform the entire presentation switch inside the event callback.

Do not reorder or remove the existing profile behavior beyond inserting the asynchronous presentation request near the start.

---

## 9. Reconcile preconditions

Before a runtime switch or fresh attach, require the already-owned Full-1902 stack to still be usable.

At minimum:

```text
presentation owner exists
physical owner exists
CanonicalViiperRuntime.State == Ready
PR5 LiveInputSource != null
PR5 LiveInputSource.IsRunning == true
process shutdown has not begun
```

If any precondition is not met:

```text
no new attach
no PID mutation
no HidHide mutation
no DirectInput reacquisition
no fallback to old routing
log bounded failure/no-op
```

Do not turn PR7 into physical-input recovery.

### 9.1 Source not running

If `LiveInputSource.IsRunning == false` at a presentation-change boundary:

```text
DO NOT retire the current presentation merely to attempt a target that cannot be driven
DO NOT create another DirectInput source
DO NOT restore PID1901
```

Return/report a reconcile failure and leave physical ownership to later recovery work.

`MsiClawInputSource` already neutralizes its latest state when its polling session fails; PR8+ will own proper physical-input reacquisition.

---

## 10. Same-presentation events are no-ops

If current desired policy equals the already-active presentation and the publisher is healthy:

```text
current = Xbox360
snapshot wants Xbox360
→ no native call
→ no publisher restart

current = SteamDeck
snapshot wants SteamDeck
→ no native call
→ no publisher restart
```

This matters for combinations such as:

```text
Steam game active
BPM opens/closes
→ desired remains SteamDeck
```

and:

```text
BPM active
RunningAppID changes 0 ↔ non-zero
→ desired remains SteamDeck
```

Do not detach/reattach merely because a source fact changed when the effective presentation did not.

Do not re-query Windows PnP for no-op transitions.

A simple current-kind + publisher-running check is enough.

---

## 11. Xbox360 → SteamDeck transition

Required runtime sequence:

```text
current Xbox360 publisher
    ↓
stop + JOIN publisher
    ↓
require publisher no longer running
    ↓
CanonicalViiperRuntime.DetachXbox360()
    ├─ neutral write is performed by the canonical detach primitive
    └─ require classified Success
    ↓
clear current publisher / active kind
    ↓
create CanonicalSteamDeckSession over SAME canonical VIIPER runtime
    ↓
session.Start()
    ├─ require Deck currently Detached
    └─ attach exact existing Deck typed device
    ↓
require session Active
    ↓
session.SetNeutral()
    ↓
require accepted
    ↓
create CanonicalSteamDeckInputPublisher using SAME PR5 source
    ↓
Start publisher
    ↓
commit active kind = SteamDeck
```

Throughout the transition:

```text
PID1902 unchanged
DirectInput source unchanged
HidHide target unchanged
VIIPER server unchanged
VIIPER bus unchanged
Xbox360 typed device object not removed/recreated
SteamDeck typed device object not removed/recreated
```

Short interval with both typed devices detached is valid.

Both attached at once is never valid.

---

## 12. SteamDeck → Xbox360 transition

Required inverse sequence:

```text
current SteamDeck publisher
    ↓
stop + JOIN publisher
    ↓
require publisher no longer running
    ↓
CanonicalSteamDeckSession.DetachDevice()
    ├─ canonical runtime neutralizes Deck before native detach
    └─ require known-safe success
    ↓
dispose managed Deck session wrapper
clear current publisher / active kind
    ↓
query exact owned Xbox360 attachment state
    ↓
require Detached
    ↓
CanonicalViiperRuntime.AttachXbox360()
    ↓
require Success
    ↓
CanonicalViiperRuntime.SetXbox360State(default)
    ↓
require accepted
    ↓
create CanonicalXbox360InputPublisher using SAME PR5 source
    ↓
Start publisher
    ↓
commit active kind = Xbox360
```

Again, do not rebuild the native runtime or physical ownership chain.

---

## 13. Retirement ordering — follow the proven PR6 publisher contract

The high-level Full-1902 architecture document contains an older conceptual diagram showing neutral before publisher stop.

The current PR6 work order and publisher implementation establish the more precise managed/native safety rule:

> **No publisher may still be capable of calling `SetState` when native detach/removal begins.**

Therefore PR7 must use the already-proven ordering:

```text
stop + JOIN publisher
→ canonical runtime/session detach primitive
    → neutral write
    → native detach
```

Do **not** manually write neutral while the publisher is still running, because the live publisher can overwrite that neutral state before detach.

Do not rewrite the canonical publishers for this PR.

---

## 14. Refactor PR6 retirement only as much as required

Current PR6 `RetireAsync(...)` combines:

```text
active presentation retirement
+ final VIIPER teardown
```

PR7 should split the internal implementation conceptually into:

```text
RetireActivePresentationCoreAsync(...)
    stop/join current publisher
    detach current typed device
    clear managed active-presentation fields
    KEEP CanonicalViiperRuntime alive/Ready

RetireAsync(...)
    call RetireActivePresentationCoreAsync(...)
    then perform final CanonicalViiperRuntime.TeardownAsync()
```

Exact naming is not mandatory.

Important:

- the core helper should assume the existing `_gate` is already held;
- do not let it reacquire the same `_gate` and deadlock;
- `ReleaseForCenterMEnableAsync` and `DisposeAsync` continue to use final teardown;
- runtime switch uses active-presentation-only retirement.

Do not introduce a generic teardown strategy object or lifecycle framework.

---

## 15. Switch failure policy

Runtime presentation switching is a real native/publisher operation and must fail closed when a step cannot be proven safe.

### 15.1 Current publisher stop/join fails

If the current publisher cannot be proven stopped:

```text
DO NOT detach current typed device
DO NOT attach target typed device
retain ownership evidence
report switch failure
```

This is a hard cleanup barrier.

### 15.2 Current detach fails

If current typed-device detach returns retryable/unsafe/unknown or throws:

```text
DO NOT attach target
DO NOT touch physical ownership
retain the same canonical owner
report failure
```

Never intentionally attach both.

### 15.3 Current retirement succeeds but target attach fails

If the old presentation is safely retired but the target cannot be attached:

```text
leave both typed devices detached when cleanup is known-safe
DO NOT reattach the previous presentation as fallback
DO NOT attach some alternate presentation
DO NOT restore PID1901
keep PID1902 / DirectInput / HidHide / VIIPER ownership
report presentation failure
```

This is a visible fail-closed condition.

A later real RunningAppID/BPM event may request another reconcile if the canonical runtime remains Ready; do not add an automatic retry timer/loop.

### 15.4 Target neutral fails

Attempt only the canonical detach/cleanup for that selected target.

If cleanup succeeds:

```text
both presentations detached
runtime may remain Ready
```

If cleanup cannot be proven:

```text
retain canonical ownership evidence
stop further switching
```

### 15.5 Target publisher Start throws

Attempt only known-safe cleanup of that target.

Do not restore the old presentation as a hidden rollback.

The selected policy failed and should remain observable.

---

## 16. Publisher runtime fault remains a narrow terminal fail-close for PR7

PR6 already handles a live publisher fault by scheduling asynchronous fail-close outside the publisher worker thread.

Preserve that core behavior.

A publisher runtime fault is different from a normal desired-presentation change.

For PR7:

```text
publisher runtime fault
→ async fail-close
→ stop/join if still owned
→ neutral/detach current virtual presentation when safe
→ do not attach the other presentation
→ do not issue PID command
→ do not touch HidHide
→ do not reacquire DirectInput
```

It is acceptable for the current PR6 fault path to remain terminal for the process-owned VIIPER presentation runtime (including final VIIPER teardown) until PR8+ introduces broader recovery.

Do not use PR7 normal switching as an automatic publisher-fault recovery system.

Do not clear/reset the fault task merely to allow silent repeated recovery loops.

---

## 17. Active presentation = null is a valid fail-closed runtime state

After a known-safe failed switch, the owner may have:

```text
Canonical VIIPER = Ready
Xbox360 = Detached
SteamDeck = Detached
active kind = null
PR5 DirectInput = still running
PID1902/HidHide = unchanged
```

PR7 runtime reconcile may treat a later genuine Steam/BPM event as an opportunity to attach the then-current desired presentation:

```text
active kind == null
+ VIIPER Ready
+ PR5 source running
→ capture freshest snapshot
→ attach desired presentation
```

This is event-driven reconciliation, not a retry loop.

Do not add a timer merely because no presentation is active.

---

## 18. Fresh desired state must be read inside the existing gate

Every runtime reconcile must use the current raw fact at the real switch decision boundary.

Conceptual flow:

```text
await existing presentation _gate
    ↓
verify owner/source/runtime usable
    ↓
capture current RunningAppID + BPM
    ↓
compute desired kind
    ↓
if desired == current and publisher healthy
    → no-op
else
    → perform one transition
```

This directly protects normal practical event overlap such as:

```text
Steam game starts
→ Deck switch begins
→ game closes before switch finishes
→ second event queues
→ after first switch, second reconcile enters gate
→ captures current inactive state
→ converges back to X360
```

No instruction-level epoch or generation is required.

---

## 19. Cancellation / shutdown boundary

Raw Steam/BPM event reconcile is a forward presentation mutation, not an authority-release action.

Use the process shutdown cancellation token to prevent new forward work once shutdown begins.

Preferred simple rule:

```text
before acquiring / before first presentation mutation
→ honor cancellation

once current publisher stop/join / detach mutation has begun
→ complete that one known-safe switch attempt or fail closed
→ do not strand an attached native device because the caller token flipped mid-step
```

Do not add a transaction manager.

### 19.1 Shutdown subscription cleanup

`PrepareRuntimeForShutdown()` must stop future PR7 event delivery before `_presentationOwnership.DisposeAsync()` tears down VIIPER.

Ensure any new callback/subscription added for PR7 is removed along with the existing Runtime-host subscriptions.

If a previously scheduled reconcile reaches the owner after shutdown has already made it unusable, it must fail/no-op without issuing new native attach calls.

Do not block process shutdown indefinitely waiting for arbitrary future events.

---

## 20. Explicit Enable Center M and Restart — preserve PR6 ordering

PR7 must not weaken the already-merged explicit authority release path.

Required order remains:

```text
stop/join active presentation publisher
→ neutral/detach active typed device
→ canonical VIIPER teardown proven Closed
→ PR5 DirectInput stop
→ restore same strong MSI Claw PID1901
→ verify PID1901
→ clear exact Addon HidHide target
→ enable Center M startup roots
→ restart
```

Because PR7 runtime reconcile uses the same presentation `_gate`:

```text
switch in progress vs Enable-and-Restart
→ whichever enters the gate first completes its presentation mutation
→ release then retires whatever presentation is actually current
```

Do not add another global transition lock.

Once VIIPER is torn down/Closed for Center M release, any queued presentation reconcile must fail/no-op and must not reattach anything.

---

## 21. Controlled process teardown — preserve durable authority

Current `AddonProcessHost.DisposeAsync()` order is correct and must remain:

```text
presentation owner DisposeAsync
→ PR5 physical owner DisposeAsync
```

Meaning:

```text
stop/join active publisher
→ detach virtual device
→ teardown canonical VIIPER
→ release DirectInput process handle
→ KEEP PID1902
→ KEEP persistent HidHide target
```

Do not issue PID1902 → PID1901 during ordinary Runtime restart / Windows shutdown merely because PR7 added live switching.

---

## 22. Sleep / hibernate / resume — do not expand PR7 into recovery

Sleep/hibernate is a real supported lifecycle, but full Full-1902 resume reacquisition belongs to PR8+.

PR7 requirements are limited to not making the existing boundary worse:

- do not re-enable the legacy stock-XInput resume baseline for Center M Disabled;
- do not restore PID1901 on suspend;
- do not create a second DirectInput source on resume;
- do not introduce a presentation-only resume manager;
- if a raw Steam/BPM event occurs after resume and the PR5 source is no longer running, runtime presentation reconcile must refuse a forward switch rather than fabricating recovery;
- existing active publisher may publish PR5's neutralized latest state if the input source has failed; PR8+ owns physical reacquisition and full presentation restore.

Do not add speculative synchronization around suspend notifications and exact event timing.

---

## 23. Preserve legacy Center M Enabled behavior

PR7 Full-1902 presentation switching applies only when the new presentation owner exists on an exact Center M Disabled path.

For Center M Enabled:

```text
legacyRoutingAllowed = true
existing AddonRoutingRuntime behavior unchanged
new Full-1902 presentation owner = absent
PR7 raw event callback = no-op for Full-1902 switching
```

Do not route Center M Enabled sessions through `MsiClawAddonPresentation` in this PR.

Do not remove old routing merely because the new path now supports switching.

Legacy cleanup is a later focused PR.

---

## 24. Do not reuse the old Game Bar/X360 policy as Full-1902 switching authority

Current repository still contains temporary old-routing Game Bar presentation code in `AddonRoutingRuntime`.

It may be useful only as historical proof of low-level ordering:

```text
publisher stop/join
→ detach current
→ attach target
→ start target publisher
```

Do not wire Full-1902 runtime presentation changes through:

- `GameBarForegroundWatcher`;
- `GameBarForegroundPresentationDelivery`;
- `EnterXbox360PresentationAsync`;
- `ExitXbox360PresentationAsync`;
- `RoutingPipelineRuntimeCoordinator`;
- `CanonicalSteamDeckOutputStage`.

The Full-1902 policy source is only:

```text
raw actual RunningAppID
OR raw BPM active
```

---

## 25. Logging / diagnostics

Add bounded logs useful for the hardware POC.

Recommended events:

```text
PresentationReconcileRequested
PresentationReconcileNoChange
PresentationSwitchStarted
PresentationSwitchCompleted
PresentationSwitchFailed
```

Useful fields:

```text
Reason / Trigger
RunningAppId
BigPictureActive
PreviousPresentation
DesiredPresentation
CurrentPresentation
Stage
FailureReason
TotalMs
```

Optional debug timing decomposition is acceptable for:

```text
PublisherStopMs
DetachMs
AttachMs
NeutralMs
PublisherStartMs
```

Do not log controller state every publish tick.

Do not add a high-frequency telemetry stream in this PR.

---

## 26. Implementation shape — preferred minimal changes

Expected primary production files:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
```

Possibly small/no changes to:

```text
src/SteamInputAddonforClaw/Steam/SteamSessionRuntime.cs
src/SteamInputAddonforClaw/Runtime/AddonRuntimeHost.cs
```

but only if the existing callbacks/capture seam cannot be reused directly.

Preferred direction is to reuse the current PR6 `CapturePresentationSnapshot()` and the current `bigPictureStateChanged` callback parameter rather than adding another public event abstraction.

Expected focused tests:

```text
tests/SteamInputAddonforClaw.Tests/MsiClawAddonPresentationTests.cs
```

plus narrow process-host/event-policy tests only where required.

Do not move unrelated Overlay/QAM/Profile code.

---

## 27. Required automated tests

Add deterministic tests for at least the following.

### 27.1 Selection matrix

```text
RunningAppID = 0, BPM false → Xbox360
RunningAppID != 0, BPM false → SteamDeck
RunningAppID = 0, BPM true → SteamDeck
RunningAppID != 0, BPM true → SteamDeck
```

PR6 already covers the basic snapshot matrix; PR7 tests should cover runtime reconcile behavior for the same matrix.

### 27.2 Xbox360 → SteamDeck

Prove:

```text
X360 publisher StopAsync called
X360 native detach occurs after publisher stop
Deck attaches only after X360 detach success
Deck neutral accepted before Deck publisher start
Deck publisher starts
final ActivePresentation == SteamDeck
exactly one typed device attached
```

### 27.3 SteamDeck → Xbox360

Prove inverse order and final state.

### 27.4 Same desired presentation no-op

Prove no native attach/detach and no publisher restart when:

```text
current == desired
publisher healthy
```

### 27.5 Game + BPM OR semantics

Prove:

```text
Deck active due game
BPM true/false while game still active
→ no switch

Deck active due BPM
RunningAppID changes while BPM active
→ no switch

only when both become inactive
→ switch to X360
```

### 27.6 Fresh snapshot after gate acquisition

Deterministically hold one reconcile inside the presentation gate, change the fake raw snapshot before a queued reconcile enters, then prove the queued reconcile chooses the **new** desired presentation rather than the snapshot that existed when the event was originally requested.

Do this with a narrow test seam; do not add a production epoch/generation.

### 27.7 Current publisher stop/join failure

Prove:

```text
switch fails
current native device not detached
other device not attached
```

### 27.8 Current detach failure

Prove target is never attached.

### 27.9 Target attach failure

Prove:

```text
old presentation was safely retired
other/fallback presentation is NOT reattached
both detached if cleanup was proven
PID/DirectInput/HidHide dependencies absent from presentation owner
```

### 27.10 Target neutral failure

Prove canonical target cleanup is attempted and no fallback occurs.

### 27.11 Target publisher Start failure

Prove target detach cleanup is attempted and old presentation is not secretly restored.

### 27.12 Active presentation null

After a known-safe failed switch leaves both detached, a later event-driven reconcile with VIIPER Ready + live source can attach the then-current desired presentation.

No timer/retry loop exists.

### 27.13 LiveInputSource stopped

Prove:

```text
no current retirement
no target attach
no second DirectInput source
```

### 27.14 VIIPER non-Ready

Prove no forward presentation mutation.

### 27.15 Publisher runtime fault contract unchanged

Prove a real publisher fault still fail-closes asynchronously and does not automatically switch to the other presentation.

### 27.16 Enable-and-Restart during/after switching

Using deterministic gate/seams, prove explicit release ultimately retires whichever presentation is actually current before physical release callback is allowed to proceed.

Do not create pathological instruction-level race tests beyond realistic serialized overlap.

### 27.17 Process teardown ordering unchanged

Prove presentation owner is still disposed before PR5 physical owner.

### 27.18 Center M Enabled legacy path unchanged

Architecture/source guard or focused composition test:

```text
legacyRoutingAllowed path still uses AddonRoutingRuntime
Full-1902 presentation owner remains Disabled-path-only
```

### 27.19 No old-policy dependencies

Source/constructor guards should continue proving the new owner does not depend on:

```text
AddonRoutingRuntime
RoutingPipelineRuntimeCoordinator
CanonicalSteamDeckOutputStage
GameBarForegroundWatcher
MsiClawNativeModeSessionCoordinator
MsiClawPhysicalIsolationStage
RecoveryManager
```

### 27.20 No physical churn in switch code

Architecture guard: the runtime-switch implementation must not call/reference physical mode switching or persistent HidHide mutation primitives.

---

## 28. Build / CI requirements

Before opening the PR:

1. build Debug;
2. build Release;
3. run focused `MsiClawAddonPresentationTests`;
4. run any new process-host/event-policy tests;
5. run the complete test suite;
6. confirm no unrelated snapshots/generated files changed;
7. inspect the final diff against this work order.

PR6 baseline reported:

```text
2963 passed / 1 skipped
```

Do not hard-code that count into assertions; report the new real full-suite result in the PR description.

Manual MSI Claw validation may remain `BLOCKED — no supported hardware` during implementation if hardware is unavailable, but the code PR still needs deterministic automated coverage.

---

## 29. Manual MSI Claw validation plan

When supported hardware is available, validate with Center M Disabled after PR6/PR7 startup succeeds.

### 29.1 Desktop / no Steam game

Expected:

```text
PID1902 present
Xbox360 virtual presentation live
SteamDeck detached
controller input works
```

### 29.2 Start a normal Steam game

Expected:

```text
RunningAppID becomes non-zero
X360 publisher stops
X360 detaches
Deck attaches
Deck publisher starts
PID1902 never changes
DirectInput does not reacquire
HidHide target does not change
```

### 29.3 Exit the Steam game

With BPM inactive:

```text
Deck → X360
same PID1902
same DirectInput ownership
same HidHide baseline
same VIIPER server/bus
```

### 29.4 Enter BPM from desktop

Expected:

```text
X360 → Deck
```

Exit BPM with no game running:

```text
Deck → X360
```

### 29.5 Game + BPM overlap

Validate OR semantics:

```text
Steam game active + BPM active
→ Deck

close BPM while game remains active
→ stay Deck / no native switch

exit game afterward
→ X360
```

### 29.6 Repeated switching

Repeat at least 10–20 normal cycles:

```text
X360 ↔ Deck
```

Verify:

- no stuck virtual controller;
- no duplicate live controllers;
- no PID1901 appearance caused by presentation changes;
- no repeated VIIPER server/bus creation;
- no DirectInput reacquisition;
- no HidHide mutation;
- no publisher worker leak;
- buttons/D-pad/sticks/triggers remain responsive after each switch.

### 29.7 Timing/log review

Capture logs around each transition and confirm the blackout interval is bounded and caused only by:

```text
publisher stop/join
native detach
native attach
neutral
publisher start
```

Do not optimize transition timing with additional concurrency until actual hardware measurements show a user-visible problem.

---

## 30. Overengineering guard

Do not block PR7 on theoretical races that require pathological instruction-level interleavings.

Protect realistic product behavior:

- game launch/exit events during normal use;
- BPM open/close events;
- two ordinary state changes arriving while a presentation switch is still running;
- publisher native/write failure;
- current detach failure;
- target attach failure;
- user selecting Enable Center M and Restart while a switch is in progress;
- process shutdown while presentation work is active;
- PR5 input source already being dead when a new switch is requested.

Do not add complexity solely for scenarios such as:

- a BPM callback landing between two specific field assignments;
- an arbitrary pair of async tasks crossing at one exact source line;
- a suspend notification arriving on one exact native instruction boundary;
- scheduler interleavings only reproducible with artificial hooks and no realistic handheld lifecycle impact.

The implementation target is:

> **One existing presentation owner, one existing serialization gate, one raw Steam/BPM policy, one clear switch path, one clear final teardown path.**

---

## 31. Completion criteria

PR7 is complete only when all of the following are true:

1. Exact Center M Disabled + successful PR6 ownership can switch presentation at runtime.
2. Steam game start causes X360 → SteamDeck when BPM/game policy changes to active.
3. Steam game exit causes SteamDeck → X360 only when BPM is also inactive.
4. BPM open causes X360 → SteamDeck.
5. BPM close returns to X360 only when RunningAppID is also zero.
6. Runtime policy uses raw RunningAppID + BPM only.
7. `SteamInputRoutingEnabled` does not control Full-1902 presentation selection.
8. Developer Test Mode does not control Full-1902 presentation selection.
9. Current PR6 `MsiClawAddonPresentation` remains the single presentation owner.
10. The same existing presentation `_gate` serializes runtime switching, publisher-fault cleanup, explicit release, and teardown.
11. Fresh desired state is captured after entering that gate for runtime reconciles.
12. No presentation epoch/generation/state manager is added.
13. No polling is added.
14. Same-desired-state events are no-ops.
15. X360 → Deck stops/joins X360 publisher before native detach.
16. Deck → X360 stops/joins Deck publisher before native detach.
17. Canonical detach primitives own neutral-before-native-detach behavior.
18. Target neutral is accepted before target publisher starts.
19. Exactly one typed device is live after a successful switch.
20. Short both-detached transition state is allowed.
21. Both typed devices are never intentionally attached simultaneously.
22. No fallback/rollback to the previous or alternate presentation occurs after target failure.
23. PID1902 is unchanged by presentation switching.
24. PR5 DirectInput source is unchanged and is never reacquired by PR7.
25. Persistent HidHide state is unchanged by presentation switching.
26. Canonical VIIPER server/bus are unchanged by normal switches.
27. Typed X360/Deck native devices are not removed/recreated during normal switches.
28. Source-not-running blocks forward switching without physical recovery.
29. Publisher runtime fault remains narrow fail-close and does not automatically attach the other presentation.
30. Explicit Enable-and-Restart still tears down virtual presentation/VIIPER before physical release.
31. Controlled process teardown still retires virtual presentation before DirectInput while keeping PID1902/HidHide durable.
32. Center M Enabled legacy routing remains unchanged.
33. No new Game Bar/OEM1/WING/Overlay presentation policy is introduced.
34. Debug build passes.
35. Release build passes.
36. Focused tests pass.
37. Full suite passes.
38. PR description reports the actual verification result and any blocked manual hardware validation.

---

## 32. PR description checklist

The implementation PR should clearly state:

- PR7 extends the merged PR6 `MsiClawAddonPresentation`; it does not add another authority owner;
- raw RunningAppID/BPM events now request runtime presentation reconcile;
- desired state is captured fresh inside the existing presentation gate;
- X360 ↔ Deck switching changes only virtual attachment/publisher state;
- PID1902, DirectInput, HidHide, VIIPER server/bus remain unchanged;
- same-desired events are no-ops;
- failure never falls back to the other/previous presentation;
- publisher fault behavior remains fail-closed;
- no polling/epoch/state-machine was added;
- Enable-and-Restart and process teardown ordering remain intact;
- Debug/Release/full-suite results;
- manual MSI Claw validation status.

---

## 33. Final implementation principle

Keep the code aligned with the actual product architecture:

```text
Center M Disabled
    ↓
one physical Addon owner
    ↓
PID1902 + one DirectInput source + persistent HidHide
    ↓
one canonical VIIPER server/bus
    ├─ persistent Xbox360 typed device
    └─ persistent SteamDeck typed device
    ↓
one MsiClawAddonPresentation owner
    ↓
raw RunningAppID/BPM policy
    ↓
exactly one live presentation
```

Normal Steam/BPM presentation changes must remain a small virtual-output operation:

```text
stop current publisher
→ detach current typed device
→ attach target typed device
→ neutral target
→ start target publisher
```

Nothing below the virtual-presentation boundary should churn merely because a Steam game or Big Picture state changed.
