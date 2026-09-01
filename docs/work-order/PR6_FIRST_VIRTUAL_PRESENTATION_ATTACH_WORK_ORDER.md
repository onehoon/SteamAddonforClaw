# Work Order — PR6: First Virtual Presentation Attach

## Status

Implementation work order for the next Full PID1902 controller PR after physical ownership:

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
PR6   First Virtual Presentation Attach                          [this PR]
  ↓
PR7   Runtime Xbox360 ↔ SteamDeck Presentation Switching
  ↓
PR8+  Owned-state recovery / lifecycle hardening / obsolete-routing cleanup
```

PR5 merged as:

```text
aa0d7d5f83381c8b6032d656a430d4f1e6a842ef
Add first PID1902 + DirectInput physical ownership (PR5) (#439)
```

This work order was prepared against that `main`.

### Numbering note

The Full 1902 architecture documents were written before PR2.5 was inserted. Their old roadmap calls this slot:

```text
old PR7 = first presentation attach
```

The current implementation sequence is authoritative:

```text
PR5 = PID1902 + DirectInput physical ownership
PR6 = first virtual presentation attach
PR7 = runtime presentation switching
```

Do not create a numbering-only intermediate PR.

Before implementation, read and treat the following as design authorities:

- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/work-order/PR1_PERSISTENT_DUAL_VIIPER_DEVICES_WORK_ORDER.md`
- `docs/work-order/PR2_ADDON_OWNED_HIDHIDE_BASELINE_WORK_ORDER.md`
- `docs/work-order/PR2_5_MANDATORY_CONTROLLER_RUNTIME_LIFETIME_WORK_ORDER.md`
- `docs/work-order/PR3_REBOOT_BOUND_CONTROLLER_AUTHORITY_TRANSITION_WORK_ORDER.md`
- `docs/work-order/PR4_DISABLED_BOOT_ADMISSION_WORK_ORDER.md`
- `docs/work-order/PR5_PID1902_DIRECTINPUT_PHYSICAL_OWNERSHIP_WORK_ORDER.md`
- current `main` implementation of:
  - `Hosting/AddonProcessHost.cs`
  - `Runtime/AddonRuntimeComposition.cs`
  - `Runtime/AddonRuntimeHost.cs`
  - `Steam/SteamSessionRuntime.cs`
  - `Steam/SteamSessionWatcher.cs`
  - `Steam/SteamBigPictureWindowProbe.cs`
  - `Steam/EffectiveSteamSessionSource.cs`
  - `Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs`
  - `Devices/MSI/Claw/MsiClawInputContracts.cs`
  - `Devices/MSI/Claw/MsiClawInputSource.cs`
  - `VirtualOutput/Viiper/CanonicalViiperRuntime.cs`
  - `VirtualOutput/Viiper/CanonicalSteamDeckSession.cs`
  - `VirtualOutput/Viiper/CanonicalSteamDeckInputPublisher.cs`
  - `VirtualOutput/Viiper/CanonicalXbox360InputPublisher.cs`
  - `VirtualOutput/Viiper/SteamDeckDeviceStateMapper.cs`
  - `VirtualOutput/Viiper/Xbox360DeviceStateMapper.cs`
  - old `Routing/AddonRoutingRuntime.cs` only as a source of already-proven low-level VIIPER ordering; do **not** reuse its old routing authority/policy.

The project is pre-release. Existing Steam-session routing and temporary Game Bar/Xbox360 behavior are not compatibility contracts for the new persistent controller architecture.

---

## 1. Goal

PR5 established the first durable physical-controller ownership boundary:

```text
Center M Disabled
+ PR4 admission Ready
    ↓
PID1902 proven for the same strong physical MSI Claw
    ↓
DirectInput acquired
    ↓
first valid controller state observed
    ↓
exact PID1902 primary gamepad collection hidden
    ↓
persistent HidHide baseline verified
    ↓
LiveInputSource retained
```

PR6 makes that controller **usable** by attaching exactly one virtual presentation for the first time.

Required product flow:

```text
Center M roots == Disabled
+ DisabledBootAdmission == Ready
    ↓
create/verify one canonical VIIPER runtime
    ├─ SteamDeck logical device created / Detached
    └─ Xbox360 logical device created / Detached
    ↓
PR5 physical ownership == Owned
+ PR5 LiveInputSource is still running
    ↓
capture the freshest actual Steam-game + BPM facts
    ↓
Steam game active OR BPM active?
    ├─ yes → attach SteamDeck
    └─ no  → attach Xbox360
    ↓
send neutral state
    ↓
start exactly one matching publisher using the SAME PR5 input source
    ↓
first virtual presentation = active
```

PR6 stops there.

The central PR6 invariant is:

> **Exactly one virtual presentation may become live, and only after PR5 has positively proven physical PID1902 ownership, a live DirectInput source, and the exact persistent HidHide isolation target.**

Stable state after a successful PR6 startup:

```text
Center M roots = Disabled
physical MSI Claw = PID1902
PR5 DirectInput = running
persistent HidHide exact target = verified
canonical VIIPER runtime = Ready
Attached(Xbox360) XOR Attached(SteamDeck) = true
matching publisher = running
other publisher = absent
other virtual device = Detached
```

---

## 2. Product presentation policy

While Center M is Disabled and Addon physical ownership is healthy:

```text
actual Steam game inactive
AND BPM inactive
    → Xbox360

actual Steam game active
OR BPM active
    → SteamDeck
```

This is a **presentation** decision only.

It must not change:

- Center M authority;
- PID1902 ownership;
- DirectInput ownership;
- HidHide isolation;
- the selected physical MSI Claw;
- mandatory Runtime lifetime.

### 2.1 Do not use the old routing preference as presentation authority

Do **not** use `EffectiveSteamSessionSource.State` as PR6's first-presentation source of truth.

Current `EffectiveSteamSessionSource` is intentionally an old-routing abstraction. Its result is gated by:

```text
SteamInputRoutingEnabled
DeveloperTestModeState
SteamSessionWatcher state
```

and on a Center M Disabled boot PR4 currently calls only:

```text
SteamSessionRuntime.StartActualObservation()
```

not:

```text
StartRoutingObservation()
```

Therefore the effective old routing state is not the correct Full-1902 presentation authority.

The new Full-1902 presentation policy must use the underlying actual facts directly:

```text
raw RunningAppID
raw/event-driven BPM state
```

The user's old `SteamInputRoutingEnabled` setting must not turn a physically Addon-owned controller back into an Xbox presentation while an actual Steam game/BPM is active.

Developer Test Mode must not become a production presentation authority in PR6.

Do not delete those old concepts in this PR; simply do not use them for the new first-presentation decision.

---

## 3. Current-code findings that constrain the design

### 3.1 Disabled mode currently has no canonical VIIPER runtime

Current `AddonRuntimeCompositionFactory` creates:

```csharp
var routingRuntime = legacyRoutingAllowed
    ? AddonRoutingRuntime.Create(...)
    : null;
```

For exact Center M Disabled:

```text
legacyRoutingAllowed = false
→ AddonRoutingRuntime = null
```

Today `CanonicalViiperRuntime.TryInitialize(...)` is called from `AddonRoutingRuntime.Create(...)`.

Therefore after PR5:

```text
Disabled boot
→ physical PID1902 ownership can succeed
→ DirectInput can be live
→ HidHide can be active
→ BUT no Full-1902 canonical VIIPER runtime exists
```

PR6 must add the Full-1902 VIIPER ownership path explicitly.

Do **not** solve this by re-enabling `AddonRoutingRuntime` while Center M is Disabled.

That would reintroduce the old Steam-session physical-routing authority which PR4 intentionally gated off.

### 3.2 PR1 already built the native substrate we need

`CanonicalViiperRuntime.TryInitialize(...)` already creates one process-lifetime native graph:

```text
one server
one bus
SteamDeck typed logical device / Detached
Xbox360 typed logical device / Detached
```

Reuse it.

Do not create a second VIIPER wrapper, another native API surface, or separate X360/Deck buses.

### 3.3 PR5 already has the correct live physical source

`MsiClawAddonPhysicalOwnership.LiveInputSource` is the exact DirectInput source that produced the first valid state before HidHide was committed.

`MsiClawInputSource` already implements:

```text
IControllerStateSnapshotSource
```

but its current `IMsiClawPreparedInputSource` interface does not expose that capability.

PR6 should make the existing interface expose the already-existing snapshot contract rather than down-casting to `MsiClawInputSource` or constructing a second input source.

Preferred minimal change:

```csharp
internal interface IMsiClawPreparedInputSource : IAsyncDisposable, IControllerStateSnapshotSource
{
    ...
}
```

No second DirectInput acquisition is allowed.

### 3.4 Existing publishers are reusable

Reuse:

```text
CanonicalSteamDeckSession
CanonicalSteamDeckInputPublisher
CanonicalXbox360InputPublisher
SteamDeckDeviceStateMapper
Xbox360DeviceStateMapper
CanonicalViiperRuntime.Attach/Detach/SetState primitives
```

Do not route the new path through `CanonicalSteamDeckOutputStage` or the old `RoutingPipelineRuntimeCoordinator`.

Those classes contain old routing-session lifecycle and policy.

Reuse their proven low-level ordering, not their obsolete authority model.

---

## 4. Strict PR6 scope boundary

### 4.1 In scope

PR6 may:

- add one narrow Full-1902 process-lifetime virtual-presentation owner;
- initialize and own one `CanonicalViiperRuntime` for the Disabled-mode controller path;
- initialize VIIPER **before** PR5 performs a new physical takeover on a first ownership boot;
- consume PR5's exact `LiveInputSource`;
- expose that source through `IControllerStateSnapshotSource` without a cast;
- add one small read-only raw Steam/BPM presentation snapshot;
- perform a one-shot BPM refresh immediately before first attach;
- select Xbox360 or SteamDeck once at startup;
- attach exactly one selected typed device;
- send neutral before starting the selected publisher;
- start exactly one selected publisher;
- fail closed if native attachment/publisher startup is not positively successful;
- retire the selected publisher/presentation before Center M Enable-and-Restart releases physical ownership;
- tear down canonical VIIPER before physical ownership is released to MSI;
- retire virtual presentation before PR5 DirectInput on controlled process teardown;
- add narrow publisher-fault fail-close cleanup so stale output is not left attached;
- add focused logs and tests.

### 4.2 Strictly out of scope

Do **not** implement in PR6:

- runtime Xbox360 → SteamDeck switching after startup;
- runtime SteamDeck → Xbox360 switching after startup;
- subscribing the new presentation owner to RunningAppID/BPM state changes;
- debounce/epoch/versioning around presentation selection;
- Game Bar foreground presentation policy;
- OEM1/WING presentation switching;
- Steam/QAM button mapping changes;
- new rumble or haptic behavior;
- new gyro behavior;
- PID1902 → PID1901 drift recovery;
- physical PnP removal/re-arrival recovery;
- DirectInput reacquisition after runtime loss;
- HidHide drift repair loop;
- Center M runtime resurrection suppression;
- full suspend/resume controller reacquisition;
- Runtime crash supervisor/service/heartbeat;
- uninstall redesign;
- broad old-routing deletion/refactor;
- a generalized presentation state machine;
- a generic `ControllerAuthorityManager` / `PresentationManager` / recovery orchestration framework.

PR7 will add event-driven X360 ↔ SteamDeck switching.

PR8+ will harden real owned-state recovery/lifecycle paths.

---

## 5. VIIPER must be proven usable before a new first-boot physical takeover

The design documents intentionally place canonical VIIPER initialization before physical PID takeover:

```text
initialize VIIPER, both detached
→ reconcile physical PID1902
→ DirectInput
→ HidHide
→ first attach
```

PR5 was intentionally physical-only, so current code performs physical acquisition without creating VIIPER.

PR6 should now correct the combined Disabled-startup ordering.

### 5.1 Required production order

For exact Disabled + admission Ready:

```text
construct PR5 physical owner
    ↓
construct/initialize PR6 canonical VIIPER presentation owner
    ↓
require CanonicalViiperRuntime.State == Ready
    ↓
PR5 AcquireAsync
    ↓
require result == Owned
    ↓
require LiveInputSource != null && IsRunning
    ↓
fresh Steam/BPM capture
    ↓
attach first presentation
```

Important:

> If canonical VIIPER cannot reach `Ready`, do not issue a new PID1901 → PID1902 takeover for this boot.

This matters especially on the first Disabled boot with the zero-target HidHide foundation:

```text
VIIPER unusable
→ leave current physical state alone
→ do not newly hide the controller
→ keep Runtime/frontend repair path available
```

On a later Disabled boot where PID1902/HidHide already persisted from a previous successful session, a VIIPER failure may still mean the physical controller is hidden and unavailable. That is a real failure, but the existing PR5 Enable-and-Restart release seam remains available and must continue to work.

Do not attempt a speculative PID1901 rollback merely because VIIPER initialization failed while Center M remains Disabled.

### 5.2 Preserve native ownership evidence on VIIPER initialization failure

`CanonicalViiperRuntime.TryInitialize(...)` can return:

```text
null                  → nothing remains owned
Ready                 → safe to continue
CleanupPending         → owner must be retained for cleanup retry
Unsafe                 → owner must be retained; do not guess/destructively continue
```

Do not discard a non-null non-Ready runtime object.

The new presentation owner must retain it for controlled teardown, matching the existing canonical VIIPER ownership contract.

Do not call PR5 `AcquireAsync` unless VIIPER is positively `Ready`.

---

## 6. Add one narrow Full-1902 presentation owner

Suggested name:

```text
MsiClawAddonPresentation
```

or equivalent following current naming.

Keep it small.

Its responsibilities are only:

```text
own one CanonicalViiperRuntime
+ attach one initial typed device
+ own its publisher
+ fail-close that virtual presentation
+ release/teardown virtual resources
```

It does **not** own:

- Center M authority;
- PID1902;
- DirectInput acquisition;
- HidHide;
- Steam/BPM event observation;
- runtime presentation policy changes;
- full recovery.

A minimal in-memory shape is enough, for example:

```csharp
internal enum AddonPresentationKind
{
    Xbox360,
    SteamDeck,
}

internal sealed record InitialPresentationResult(
    bool Succeeded,
    AddonPresentationKind? Presentation,
    string Reason);
```

The owner may retain only the fields actually needed for cleanup, such as:

```text
CanonicalViiperRuntime
active kind? (nullable)
CanonicalXbox360InputPublisher?
CanonicalSteamDeckSession?
CanonicalSteamDeckInputPublisher?
one small serialization gate for attach/fault-cleanup/release
one tracked fault-cleanup task if needed
```

Do not create a durable presentation enum graph or persisted state.

### 6.1 Why one small serialization gate is acceptable

Once a real publisher exists, these realistic operations can overlap:

- publisher runtime fault cleanup;
- controlled process teardown;
- Enable Center M and Restart release.

They must not detach/remove the same native device concurrently.

One private `SemaphoreSlim` inside the presentation owner is sufficient.

It is **not** a second authority source.

Do not add epochs, barriers, transaction managers, or a generalized transition coordinator.

---

## 7. Reuse the exact PR5 input source

PR6 must publish from the same physical source PR5 already owns.

Required:

```text
PR5 LiveInputSource
    ↓
IControllerStateSnapshotSource.LatestState
    ↓
selected VIIPER publisher
```

Do not:

- create another `MsiClawInputSource`;
- enumerate/acquire DirectInput again;
- use the old routing composition's `ControllerStateSource`;
- copy states into a second cache/bridge merely to adapt types.

Before first attach, require:

```text
LiveInputSource != null
LiveInputSource.IsRunning == true
```

If the source has already stopped after PR5 returned success:

```text
no virtual attach
no publisher start
physical PID/HidHide remain as PR5 durable ownership
report failure
```

Later recovery PRs will reacquire the physical source.

---

## 8. Add a raw one-shot Steam/BPM presentation snapshot

PR6 needs a read-only fact equivalent to:

```csharp
internal readonly record struct SteamPresentationSnapshot(
    uint RunningAppId,
    bool BigPictureActive)
{
    internal bool WantsSteamDeck => RunningAppId != 0 || BigPictureActive;
}
```

Exact naming may follow current conventions.

### 8.1 Capture from existing underlying sources

Add a narrow method to `SteamSessionRuntime`, conceptually:

```csharp
internal SteamPresentationSnapshot CapturePresentationSnapshot()
{
    _bigPictureWatcher.Refresh(); // one-shot scan only when currently inactive
    return new(
        _runningAppIdSource.GetRunningAppId(),
        _bigPictureWatcher.IsActive);
}
```

Then expose that read-only capture through `AddonRuntimeHost` so `AddonProcessHost` does not reach into Steam internals.

Reuse the existing:

- `SteamRunningAppIdRegistrySource`;
- event-driven `SteamBigPictureWatcher`;
- one-shot `SteamBigPictureWatcher.Refresh()` behavior.

Do not instantiate another BPM watcher or another registry source.

### 8.2 No polling

This is a single startup decision.

Do not add a timer/poll loop.

`SteamBigPictureWatcher.Refresh()` already performs only a one-shot scan when inactive; when a live BPM session is tracked, it keeps the current event-driven authority.

### 8.3 Capture immediately before attach

Required ordering:

```text
physical ownership verified
+ LiveInputSource running
    ↓
CapturePresentationSnapshot()
    ↓
select kind
    ↓
attach selected typed device immediately
```

Do not capture the presentation at process startup and reuse it after PID/PnP/DirectInput/HidHide work.

Do not add a second read, generation counter, epoch, or barrier merely to defend against a Steam/BPM change occurring on a particular instruction between capture and attach.

One fresh read at the real decision boundary is enough under the product race/overengineering policy.

PR7 will react to later normal Steam/BPM changes.

---

## 9. First-presentation selection

Use only:

```text
RunningAppId != 0
BigPictureActive == true
```

Policy:

```csharp
var desired = snapshot.RunningAppId != 0 || snapshot.BigPictureActive
    ? AddonPresentationKind.SteamDeck
    : AddonPresentationKind.Xbox360;
```

Do not use:

- `SteamInputRoutingEnabled`;
- Developer Test Mode;
- current Game Bar foreground state;
- old `RoutingOperationalState`;
- old `SteamOutputActive`;
- profile state;
- frontend visibility;
- QAM visibility.

Log the decision once with bounded fields:

```text
RunningAppId
BigPictureActive
SelectedPresentation
```

---

## 10. Xbox360 first-attach sequence

When the fresh snapshot selects Xbox360:

```text
require VIIPER Ready
require PR5 input source running
    ↓
query exact owned Xbox360 attachment state
    ↓
require Detached
    ↓
AttachXbox360
    ↓
require Success
    ↓
SetXbox360State(default / neutral)
    ↓
require accepted
    ↓
create CanonicalXbox360InputPublisher using SAME PR5 snapshot source
    ↓
Start publisher
    ↓
commit active kind = Xbox360
```

The SteamDeck device remains detached.

### 10.1 Do not rely on Windows PnP to decide ownership

The canonical VIIPER runtime owns exact native handles and logical IDs.

Use its classified attachment primitives.

Do not search Windows for "some X360" and treat that as the owned device.

### 10.2 No fallback if Xbox360 attach fails

If Xbox360 is the selected policy and its attach/start fails:

```text
fail closed
attempt only the known-safe cleanup of that attempted presentation
DO NOT attach SteamDeck as fallback
```

A fallback would silently change product policy and could hide a native ownership defect.

---

## 11. SteamDeck first-attach sequence

When the fresh snapshot selects SteamDeck, reuse the low-level canonical session/publisher directly.

Conceptual sequence:

```text
require VIIPER Ready
require PR5 input source running
    ↓
create CanonicalSteamDeckSession(runtime)
    ↓
Start()
    ├─ require Deck currently Detached
    └─ attach exact Deck handle
    ↓
require session Active
    ↓
SetNeutral()
    ↓
require accepted
    ↓
create CanonicalSteamDeckInputPublisher using SAME PR5 snapshot source + session sink
    ↓
Start publisher
    ↓
commit active kind = SteamDeck
```

The Xbox360 device remains detached.

Do not route this through `CanonicalSteamDeckOutputStage`.

That stage owns old routing-stage prepare/execute/rollback semantics, route feedback wiring, and old routing status coupling.

Use `CanonicalSteamDeckSession` + `CanonicalSteamDeckInputPublisher` directly.

### 11.1 No fallback if SteamDeck attach fails

Do not attach Xbox360 merely because Deck failed.

Selected Deck failure is a presentation failure and must remain visible.

---

## 12. Neutral and publisher ordering

### 12.1 First attach

Required:

```text
attach selected device
→ send selected device neutral state
→ start selected publisher
```

Never start the publisher before neutral initialization is accepted.

### 12.2 Retirement / detach

Use the already-proven managed/native ordering:

```text
stop + JOIN publisher
→ neutralize selected typed device
→ detach selected typed device
```

The important invariant is:

> **No publisher may still be capable of calling SetState when native detach/removal begins.**

Existing `CanonicalViiperRuntime.DetachXbox360()` / `DetachDeck()` already perform a neutral state write before the native detach call.

Therefore a retirement path may safely use:

```text
publisher.StopAsync()
→ runtime/session Detach...
```

because the runtime detach primitive performs the neutral step after the publisher is proven stopped.

Do not write neutral first while the publisher is still running and assume the publisher will not overwrite it.

### 12.3 Publisher join failure is a hard cleanup barrier

Both existing canonical publishers intentionally fail closed if their worker cannot be joined.

If `StopAsync()` fails/throws:

```text
DO NOT detach/remove native device underneath a possibly-live publisher
retain ownership evidence
report failure
```

This is a real resource/lifecycle safety boundary, not a theoretical race.

---

## 13. Initial attach failure policy

PR6 failure semantics remain asymmetric because Center M is still Disabled.

### 13.1 Failure before PR5 physical acquisition

Example:

```text
VIIPER load/init not Ready
```

Required:

```text
no new physical PID mutation
no new DirectInput acquisition
no virtual attach
retain/cleanup any known VIIPER native ownership according to canonical rules
```

### 13.2 PR5 physical acquisition fails after VIIPER reached Ready

Required:

```text
no virtual attach
attempt clean VIIPER teardown
leave physical result according to PR5 durable failure policy
```

Do not issue a new PID1901 rollback from PR6.

### 13.3 Physical ownership succeeds but selected attach fails

Required:

```text
no fallback presentation
cleanup only known-safe virtual resources
physical PID1902 remains desired
persistent HidHide remains
DirectInput may remain owned until normal process teardown / explicit authority release
Runtime/frontend remain available
```

The user's escape path remains:

```text
Enable Center M and Restart
```

### 13.4 Neutral write fails after native attach

The selected device is known attached.

Attempt its normal classified detach path.

If detach succeeds:

```text
presentation failed cleanly
```

If detach is retryable/unsafe/unknown:

```text
retain the same presentation/VIIPER owner
no physical release
no alternate presentation
```

### 13.5 Publisher Start throws

Catch the startup failure and attempt known-safe presentation detach.

Do not leave an attached device just because publisher initialization failed.

Do not create a generic rollback transaction engine.

---

## 14. Runtime publisher fault: narrow virtual fail-close only

Once PR6 starts a real publisher, an actual output write/timer/worker failure is a realistic operation failure.

Do not leave the virtual device attached with stale last input indefinitely.

Required fault behavior:

```text
publisher reports fault
    ↓
schedule one async fail-close outside the publisher worker thread
    ↓
serialize with presentation release/teardown
    ↓
stop/join publisher resources if still owned
    ↓
neutral + detach selected virtual device when safe
    ↓
leave PID1902 / DirectInput / HidHide authority unchanged
    ↓
log presentation unhealthy
```

Do **not** automatically attach the other presentation.

Do **not** retry in a loop.

Do **not** restore PID1901.

PR8+ can add broader owned-state recovery after the baseline path is hardware-proven.

### 14.1 Do not self-join the publisher worker

The publisher fault callback may originate from its own worker thread.

Do not synchronously call a cleanup path that joins that same worker from inside the callback.

Schedule the cleanup asynchronously (matching the existing SteamDeck output fault pattern) and retain one task reference so controlled teardown can await/observe it.

No detached fire-and-forget cleanup that process teardown cannot account for.

---

## 15. Do not implement runtime presentation switching yet

PR6 selects once.

After successful first attach:

```text
RunningAppID/BPM later changes
→ observation may continue for existing product features
→ PR6 presentation does NOT switch
```

Examples intentionally deferred to PR7:

```text
boot inactive → X360 attached → Steam game launches
boot in BPM   → Deck attached → BPM exits
Steam game ends while Deck is attached
```

PR7 will consume actual RunningAppID/BPM change events and perform:

```text
current publisher stop/join
→ current detach
→ target attach
→ target neutral
→ target publisher start
```

without PID/HidHide/DirectInput churn.

Do not pre-build the PR7 event subscription, debounce, switch queue, or state logic in PR6.

---

## 16. Startup/frontend ordering: avoid a new cross-owner transition race by sequencing, not by architecture

Current PR5 starts the frontend named-pipe server before `TryAcquirePhysicalOwnershipAsync(...)`.

PR6 adds an attach operation after physical acquisition. The official `Enable Center M and Restart` action must retire virtual presentation before physical release.

A real user could otherwise open the UI immediately during startup and request Enable while the initial physical/presentation sequence is still committing.

Do **not** solve this with epochs, a new controller transaction manager, or nested authority states.

Preferred simple production ordering for PR6:

```text
construct frontend control / transition object
    ↓
for exact Disabled + Ready:
    initialize VIIPER
    → PR5 physical acquire
    → first presentation attach attempt
    ↓
only then start/mark the frontend transport ready for external requests
```

Important:

- controller startup attempt is bounded by existing native/PnP/DirectInput operations;
- failure must still continue to frontend startup so the user can use the repair/Enable path;
- do not abort Runtime/frontend startup merely because first presentation failed;
- Center M Enabled/Partial/Unavailable paths should not gain unnecessary startup delay.

This simple sequencing removes the realistic startup-vs-Enable overlap without inventing a second controller-authority coordinator.

If code layout changes are needed, keep them local to `AddonProcessHost.InitializeRuntimeAsync`.

Do not move unrelated frontend/overlay/TDP architecture.

---

## 17. Enable Center M and Restart must release virtual presentation first

PR5 extended the official release flow to:

```text
DirectInput stop
→ restore same MSI Claw PID1901
→ clear exact HidHide target
→ enable Center M roots
→ restart
```

After PR6, the required order becomes:

```text
selected virtual publisher stop/join
→ selected virtual device neutral + detach
→ canonical VIIPER runtime teardown
→ PR5 DirectInput stop
→ restore same strong MSI Claw PID1901
→ verify PID1901
→ clear exact PR5 HidHide target
→ enable Center M startup roots
→ restart
```

This ordering is mandatory.

### 17.1 Smallest integration into the existing PR5 release seam

Do not create a second public authority transition.

Keep `CenterMRebootAuthorityTransition` as the one ordered reboot-bound owner.

Its existing release callback may be composed in `AddonProcessHost` so that it performs:

```text
PR6 virtual release first
→ PR5 physical release second
→ return PR5 PhysicalOwnershipReleaseResult to the existing transition
```

If virtual release fails:

```text
return failure
DO NOT stop DirectInput
DO NOT restore PID1901
DO NOT clear HidHide
DO NOT enable Center M roots
```

This prevents a still-live/unknown virtual device from surviving across physical authority release.

### 17.2 VIIPER teardown must be proven before continuing

Successful explicit authority release requires canonical VIIPER teardown to reach its known-safe closed state.

If `TeardownAsync()` cannot prove cleanup:

```text
Enable-and-Restart fails before physical release
same owner object remains available for retry/diagnostics
```

Do not continue because "the process will reboot anyway."

The explicit authority-release path must be safe before it gives MSI the physical controller.

---

## 18. Controlled Runtime/process teardown while Center M remains Disabled

Normal Windows shutdown/restart or controlled Addon Runtime restart is **not** Center M authority release.

Required ordering:

```text
active PR6 presentation
→ stop/join publisher
→ neutral/detach virtual device
→ teardown process-owned canonical VIIPER runtime
→ release PR5 DirectInput process handle
→ KEEP PID1902
→ KEEP persistent HidHide target
→ process exits
```

Therefore `AddonProcessHost.DisposeAsync()` must dispose/retire the PR6 presentation owner **before** `_physicalOwnership.DisposeAsync()`.

Do not issue PID1902 → PID1901 on normal process teardown.

### 18.1 Process-shutdown cleanup failure

Follow the existing canonical VIIPER process-teardown philosophy:

- log a failed/unsafe native cleanup clearly;
- never detach/remove below a live publisher whose join did not complete;
- do not invent destructive cleanup past an unknown native outcome;
- do not turn normal process teardown into a generalized infinite retry loop.

Explicit `Enable Center M and Restart` has the stronger contract above and must stop before physical release if virtual cleanup cannot be proven.

---

## 19. Sleep / hibernate / resume boundary for PR6

Sleep/hibernate is a real supported product lifecycle and must be considered, but the Full-1902 roadmap explicitly places complete owned-state recovery in later focused PRs.

PR6 must **not** re-enable the old stock-XInput resume baseline.

Current PR4 rule remains:

```text
Center M Disabled
→ legacy Stock Center M resume baseline suppressed
→ no intentional PID1901 restore on suspend/resume
```

PR6 also must not build the full new PID1902/DirectInput/HidHide reacquisition state machine merely because a virtual presentation now exists.

For this PR:

- the publisher uses PR5 `LatestState`;
- if the PR5 DirectInput polling session dies, `MsiClawInputSource` already resets its latest state to neutral;
- do not publish from another/stale physical source;
- do not add a second resume authority;
- full suspend neutral/quiesce/reacquire/reattach validation remains a required later lifecycle PR before release-quality completion.

Manual PR6 hardware validation should record suspend/resume observations if convenient, but failures there should be classified against the explicitly deferred recovery scope rather than "fixed" by adding a second recovery manager to this PR.

The important PR6 safety invariant is that it never calls the old PID1901 stock baseline and never attaches a second presentation as a resume workaround.

---

## 20. Old routing / Game Bar boundaries

### Center M Enabled

Preserve existing legacy behavior in this PR.

PR6's new Full-1902 presentation owner must not be created.

### Center M Disabled

Keep:

```text
legacyRoutingAllowed = false
AddonRoutingRuntime = null
```

The new presentation owner is independent of the old routing pipeline.

Do not reconnect the old automatic Game Bar X360 entry path.

PR6 presentation selection is only:

```text
actual Steam game/BPM at first attach
```

Game Bar foreground is not a presentation authority.

---

## 21. No duplicate VIIPER ownership in one authority mode

Production invariant:

```text
Center M Enabled
→ legacy path may own its existing VIIPER runtime when old routing actually runs
→ PR6 Full-1902 owner absent

Center M Disabled
→ legacy AddonRoutingRuntime absent
→ PR6 Full-1902 owner owns the one canonical VIIPER runtime
```

Do not create both runtimes in the same Disabled controller path.

Do not create separate VIIPER runtimes for Xbox360 and SteamDeck.

---

## 22. Suggested production integration shape

Keep composition local and explicit.

Conceptually in `AddonProcessHost`:

```text
if CenterMStartupState != Disabled
    → no PR6 owner

construct PR5 physical owner for Disabled release seam

if DisabledBootAdmission != Ready
    → no PR6 VIIPER init/acquire/attach
    → frontend remains available

create PR6 presentation owner / canonical VIIPER runtime

if VIIPER not Ready
    → do not call PR5 AcquireAsync
    → frontend remains available

physicalResult = await PR5 AcquireAsync(...)

if !physicalResult.IsOwned
    → no attach
    → teardown unused VIIPER when safely possible
    → frontend remains available

source = PR5.LiveInputSource
if source == null || !source.IsRunning
    → no attach

presentationSnapshot = RuntimeHost.CapturePresentationSnapshot()
selected = snapshot.WantsSteamDeck ? SteamDeck : Xbox360

presentationResult = await PR6.AttachInitialAsync(source, selected, ...)
log result
continue Runtime/frontend startup regardless of success
```

The exact method names may differ.

Do not move this logic into `StartupCoordinator`; PR4 startup admission must remain read-only.

PR6 native mutation begins only after startup admission has already returned Ready.

---

## 23. Presentation owner lifetime

The process host should retain exactly one PR6 owner field while Center M is Disabled and the owner has acquired any VIIPER native resource.

Suggested conceptual field:

```csharp
private MsiClawAddonPresentation? _presentationOwnership;
```

Do not retain a second independent `CanonicalViiperRuntime` field elsewhere unless needed solely during construction.

The owner should remain reachable even after:

- initial attach failure with cleanup pending;
- runtime publisher fault;
- selected device detach retryable failure;
- VIIPER teardown failure.

Do not set the owner to null merely because the presentation is not usable if native ownership is still unresolved.

---

## 24. Logging / diagnostics

Keep logs transition-based; no new periodic logging beyond the existing publisher diagnostics.

Recommended category:

```text
ControllerPresentation
```

or existing `SteamOutput` where appropriate.

Log once for:

### VIIPER initialization

```text
Event=ViiperRuntimeInitialized
State=Ready/CleanupPending/Unsafe/Unavailable
DeckLogicalDeviceId
Xbox360LogicalDeviceId
```

### Presentation decision

```text
Event=InitialPresentationSelected
RunningAppId
BigPictureActive
Selected=Xbox360/SteamDeck
```

### First attach

```text
Event=InitialPresentationAttached
Presentation
PublisherStarted=true
```

### Failure

```text
Event=InitialPresentationFailed
Stage
Reason
Presentation
```

### Publisher fault fail-close

```text
Event=PresentationFaulted
Presentation
Reason
CleanupResult
```

### Release

```text
Event=PresentationReleased
Presentation
ViiperTeardownSucceeded
Reason
```

Do not log every 4 ms publish tick from PR6 itself.

---

## 25. Unit / architecture test requirements

Add focused tests for the new owner and integration.

### 25.1 Raw presentation snapshot

Verify:

1. `RunningAppID == 0`, BPM false → wants Xbox360.
2. `RunningAppID != 0`, BPM false → wants SteamDeck.
3. `RunningAppID == 0`, BPM true → wants SteamDeck.
4. both active → wants SteamDeck.
5. capture invokes the existing BPM one-shot refresh path rather than creating another watcher.
6. old `SteamInputRoutingEnabled=false` does **not** force the new snapshot inactive.
7. Developer Test Mode does not make the new production snapshot SteamDeck-active by itself.

Prefer a small direct test seam on `SteamSessionRuntime`; do not add a generic session-fact service.

### 25.2 Xbox360 first attach

Using the real `CanonicalViiperRuntime` contract over a fake native API and a deterministic publisher tick source, prove:

```text
both devices initially Detached
→ Xbox360 attach succeeds
→ neutral accepted before publish begins
→ Xbox360 publisher running
→ Deck never attached
→ source is the supplied PR5 snapshot source
```

### 25.3 SteamDeck first attach

Prove:

```text
both devices initially Detached
→ SteamDeck session attaches
→ neutral accepted
→ SteamDeck publisher running
→ Xbox360 never attached
→ source is the supplied PR5 snapshot source
```

### 25.4 Exactly-one invariant

For each successful initial path:

```text
only selected Attach call occurs
other Attach call count = 0
only selected publisher exists/runs
```

Do not require a second post-attach native state query merely for test symmetry if the canonical attach result is already the authoritative classified result.

### 25.5 Live input required

Verify no attach when:

- `LiveInputSource` is null;
- source reports `IsRunning == false` after PR5 acquisition.

### 25.6 VIIPER readiness before physical takeover

Host/integration test must prove:

```text
VIIPER Ready
→ physical Acquire may run

VIIPER null/CleanupPending/Unsafe
→ physical Acquire not called
→ no presentation attach
```

This is an important first-boot user-safety contract.

### 25.7 Physical ownership failure

If PR5 returns Failed:

```text
no attach
no publisher
unused VIIPER cleanup attempted/retained according to canonical outcome
```

### 25.8 Selected attach failure

Verify:

- selected attach failure does not attach the other type;
- neutral failure after successful attach attempts classified detach;
- publisher-start exception attempts classified detach;
- unsafe/unknown cleanup stops further destructive cleanup.

### 25.9 Publisher runtime fault

Prove fault handling:

```text
fault callback
→ async cleanup scheduled (not synchronous self-join)
→ publisher joined/stopped
→ selected device detached when safe
→ other presentation not attached
→ no PID/HidHide call
```

### 25.10 Release for Center M Enable

Extend the existing authority-transition integration tests to prove exact order:

```text
presentation-release
→ physical-release
→ hidhide:enable
→ centerm:true
→ restart
```

If presentation release fails:

```text
physical-release not called
HidHide untouched
Center M roots untouched
restart not requested
```

### 25.11 Process teardown

Prove owner disposal order in the process-host seam:

```text
presentation cleanup/VIIPER teardown
→ physical input teardown
```

and prove normal teardown does not issue PID1901 restoration.

### 25.12 No runtime switching yet

Architecture/source guard or focused integration test should prove PR6 does not subscribe its new owner to:

- `ActualRunningAppIdChanged` for switching;
- `BigPictureStateChanged` for switching;
- Game Bar foreground changes for switching.

Existing unrelated subscribers may remain.

### 25.13 No old routing owner dependency

The new presentation owner must not depend on:

```text
AddonRoutingRuntime
RoutingPipelineRuntimeCoordinator
RoutingPipelineSessionCoordinator
MsiClawNativeModeSessionCoordinator
MsiClawPhysicalIsolationStage
RecoveryManager
```

It may depend directly on the proven low-level VIIPER runtime/session/publisher primitives.

---

## 26. Manual hardware validation

Use a supported MSI Claw with Center M Disabled via the reboot-bound transition.

Collect normal Addon logs.

### Case A — first Disabled boot, no Steam/BPM

Expected:

```text
VIIPER initializes both typed devices Detached
PID1901 or PID1902 reconciles through PR5
DirectInput first valid state succeeds
exact HidHide target is verified
fresh RunningAppId = 0
fresh BPM = false
Xbox360 attaches
SteamDeck remains detached
Xbox360 input works
```

Confirm Windows/Steam sees only the intended virtual presentation, not a visible duplicate physical PID1902 gamepad.

### Case B — Runtime starts while BPM already active

Expected:

```text
PR5 physical ownership succeeds
fresh BPM capture = true
SteamDeck attaches FIRST
Xbox360 is never briefly attached
SteamDeck input works
```

This explicitly validates the architecture requirement:

> Do not hard-code X360 at boot and immediately switch to Deck.

### Case C — Runtime starts while a Steam game is already active

Expected:

```text
RunningAppID != 0
→ SteamDeck first attach
→ no initial X360 attach
```

### Case D — clean Runtime restart while Center M remains Disabled

Expected:

```text
old process retires publisher
→ detaches selected virtual
→ tears down VIIPER
→ releases DirectInput
→ DOES NOT restore PID1901 merely for restart

new Runtime
→ VIIPER Ready
→ PR5 keep/reclaim PID1902
→ exact HidHide baseline accepted
→ fresh first-presentation selection
→ selected virtual usable
```

### Case E — Enable Center M and Restart from a successful PR6 session

Expected order from logs/hardware:

```text
publisher stopped
virtual detached
VIIPER teardown complete
DirectInput released
same physical MSI Claw restored to PID1901
HidHide exact target cleared
Center M roots Enabled
Windows restart requested
```

After reboot, stock MSI controller behavior must work.

### Case F — initial policy changes after attach

For PR6 only, changing Steam/BPM state after the first attach is expected **not** to switch presentations yet.

Record the behavior, but do not expand this PR into PR7.

---

## 27. Sleep / resume observations during hardware testing

Because suspend/resume recovery is intentionally deferred, hardware testing may optionally record:

- whether the PR5 DirectInput handle survives;
- whether `LatestState` becomes neutral on DirectInput failure;
- whether the attached virtual device resumes publishing;
- whether any stuck input is observed.

Do not respond to one observation by adding a new PID/PnP recovery manager inside PR6.

If hardware proves an immediate safety blocker such as persistent stuck virtual input after resume, stop and address the smallest concrete safety mechanism required. Otherwise keep full reacquisition/reconciliation in the planned lifecycle hardening PR.

---

## 28. Overengineering guard

Do not add complexity solely for theoretical interleavings.

Do not add:

```text
PresentationEpoch
SteamStateGeneration
OwnershipVersion
AttachBarrier
ControllerAuthorityManager
PresentationStateMachine
VirtualDeviceRegistry
GenericDevicePresentationFactory
RetryScheduler
Watchdog
```

One fresh Steam/BPM read immediately before first attach is sufficient.

One private presentation cleanup gate is sufficient for real fault/release/teardown serialization.

Do not protect against arbitrary instruction-level overlap by adding more state.

Focus on real product failures:

- VIIPER cannot initialize;
- exact selected attach fails;
- neutral write fails;
- publisher fails to start;
- publisher faults while live;
- publisher cannot join;
- detach/teardown fails;
- explicit Center M release while virtual ownership exists;
- controlled process shutdown ordering.

---

## 29. Expected file impact

Likely production changes should remain concentrated around:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/
    MsiClawAddonPresentation.cs                 [new]
    MsiClawInputContracts.cs                    [small capability exposure]

src/SteamInputAddonforClaw/Steam/
    SteamSessionRuntime.cs                      [raw one-shot presentation snapshot]

src/SteamInputAddonforClaw/Runtime/
    AddonRuntimeHost.cs                         [read-only snapshot exposure]

src/SteamInputAddonforClaw/Hosting/
    AddonProcessHost.cs                         [Disabled startup/release/teardown composition]

possibly minimal wording/order adjustments in:
src/SteamInputAddonforClaw/CenterMStartup/
    CenterMRebootAuthorityTransition.cs
```

Tests should be added beside the existing VIIPER/PR5/Steam tests.

Do not spread PR6 into profile, overlay, QAM UI, TDP, OEM1, or old routing cleanup files unless a concrete compile/integration need requires a tiny change.

---

## 30. Acceptance criteria

PR6 is complete when all of the following are true.

1. New Full-1902 presentation code runs only for exact Center M Disabled + PR4 admission Ready.
2. A PR5 physical owner is still constructed on any exact Disabled boot so Enable-and-Restart retains its blocked-boot release seam.
3. Canonical VIIPER is initialized before a new PR5 physical takeover is allowed.
4. VIIPER must be positively `Ready`; null/CleanupPending/Unsafe blocks new physical acquisition.
5. One server/bus owns both existing typed logical devices; no second VIIPER runtime is created in Disabled mode.
6. PR5 physical ownership must be `Owned` before any virtual attach.
7. The exact PR5 `LiveInputSource` is reused; no second DirectInput acquisition exists.
8. `LiveInputSource` must still be running immediately before attach.
9. Initial presentation uses a fresh raw RunningAppID + BPM snapshot.
10. `SteamInputRoutingEnabled` does not decide the new presentation.
11. Developer Test Mode does not decide the new production presentation.
12. RunningAppID != 0 selects SteamDeck.
13. BPM active selects SteamDeck.
14. Otherwise Xbox360 is selected.
15. Xbox360 path attaches only Xbox360, sends neutral, then starts only Xbox360 publisher.
16. SteamDeck path attaches only SteamDeck, sends neutral, then starts only SteamDeck publisher.
17. Successful startup satisfies `Attached(Xbox360) XOR Attached(SteamDeck)`.
18. Selected attach failure never falls back to the other presentation.
19. Publisher-start failure attempts known-safe selected-device cleanup.
20. Runtime publisher fault asynchronously fails closed the selected virtual presentation and does not restore PID1901 or attach the other presentation.
21. No PR6 runtime X360 ↔ SteamDeck switching is implemented.
22. No new polling loop is added for Steam/BPM.
23. No old `AddonRoutingRuntime` is re-enabled in Disabled mode.
24. Enable-and-Restart retires publisher/presentation and tears down VIIPER before PR5 physical release.
25. Virtual release failure prevents physical release/HidHide clear/Center M root enable/restart.
26. Controlled process teardown retires PR6 presentation before PR5 DirectInput teardown.
27. Normal process teardown does not restore PID1901 while Center M remains Disabled.
28. PR4 stock-XInput resume baseline remains suppressed while Disabled.
29. No generalized manager/state-machine/epoch/watchdog abstraction is introduced.
30. Existing Center M Enabled legacy behavior remains unchanged by PR6.
31. Debug build passes.
32. Release build passes.
33. Full test suite passes.
34. Manual MSI Claw validation is performed when hardware is available, or explicitly reported as blocked if unavailable.

---

## 31. Completion report / PR description

The implementation PR should report at minimum:

```text
main/base SHA
files changed
production LOC / test LOC summary if useful
Debug result
Release result
full test-suite result
manual MSI Claw validation result
```

Also state explicitly:

```text
- which first presentation was tested on hardware;
- whether the other typed device remained detached;
- whether any physical PID1902 duplicate input was visible;
- whether Enable Center M and Restart successfully retired virtual output before restoring PID1901;
- that runtime X360 ↔ SteamDeck switching is intentionally deferred to PR7.
```

---

## 32. One-line invariant

> **PR6 may attach exactly one X360/SteamDeck presentation only after canonical VIIPER is ready and PR5 physical PID1902 + DirectInput + exact HidHide ownership is proven; the attach decision uses one fresh raw Steam/BPM snapshot, and any explicit authority release must retire/teardown that virtual presentation before physical ownership is returned to MSI.**
