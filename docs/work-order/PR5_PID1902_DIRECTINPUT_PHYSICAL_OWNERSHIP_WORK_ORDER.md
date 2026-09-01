# Work Order — PR5: PID1902 + DirectInput Physical Ownership

## Status

Implementation work order for the next Full PID1902 controller PR after Disabled-boot admission:

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
PR5   PID1902 + DirectInput Physical Ownership                   [this PR]
  ↓
PR6   First Virtual Presentation Attach
  ↓
PR7   Runtime Xbox360 ↔ SteamDeck Presentation Switching
  ↓
PR8+  Owned-state recovery / lifecycle hardening / obsolete-routing cleanup
```

PR4 merged as:

```text
598dedf9254ef187938ec1fccc97f74142d91010
Add authority-aware startup and read-only Disabled-boot controller admission (PR4) (#437)
```

Current `main` when this work order was prepared also contains the unrelated Overlay PR #436:

```text
088cfcf796408a46882d55b94895251b0cb14acb
Add warm Overlay transport lifecycle (#436)
```

The Overlay changes are not part of this controller work order and must not be coupled to PR5.

### Numbering note

The Full 1902 design documents were written before PR2.5 was inserted. Their sequence therefore calls this slot:

```text
old PR6 = PID1902 + DirectInput ownership
```

The current implementation sequence is authoritative:

```text
PR4 = Disabled-boot admission
PR5 = PID1902 + DirectInput physical ownership
PR6 = first virtual presentation attach
```

Do not create an extra numbering-only intermediate PR.

Before implementation, read and treat the following as design authorities:

- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/work-order/PR1_PERSISTENT_DUAL_VIIPER_DEVICES_WORK_ORDER.md`
- `docs/work-order/PR2_ADDON_OWNED_HIDHIDE_BASELINE_WORK_ORDER.md`
- `docs/work-order/PR2_5_MANDATORY_CONTROLLER_RUNTIME_LIFETIME_WORK_ORDER.md`
- `docs/work-order/PR3_REBOOT_BOUND_CONTROLLER_AUTHORITY_TRANSITION_WORK_ORDER.md`
- `docs/work-order/PR4_DISABLED_BOOT_ADMISSION_WORK_ORDER.md`
- current `main` implementation of:
  - `Startup/StartupCoordinator.cs`
  - `Startup/DisabledBootControllerAdmission.cs`
  - `Startup/AddonStartupComposition.cs`
  - `Hosting/AddonProcessHost.cs`
  - `Runtime/AddonRuntimeComposition.cs`
  - `Devices/MSI/Claw/MsiClawDeviceAdapter.cs`
  - `Devices/MSI/Claw/MsiClawNativeStateManager.cs`
  - `Devices/MSI/Claw/MsiClawModeController.cs`
  - `Devices/MSI/Claw/MsiClawModeContracts.cs`
  - `Devices/MSI/Claw/MsiClawDirectInputDeviceSelector.cs`
  - `Devices/MSI/Claw/MsiClawInputSource.cs`
  - `Devices/MSI/Claw/MsiClawPhysicalInputStage.cs`
  - `Input/DirectInput/VorticeDirectInputDeviceEnumerator.cs`
  - `Input/DirectInput/DirectInputDeviceTopologyResolver.cs`
  - `HidHide/AddonControllerHidHideBaseline.cs`
  - old route-scoped `MsiClawNativeModeSessionCoordinator.cs`
  - old route-scoped `MsiClawPhysicalIsolationStage.cs`

The project is pre-release. The old Steam-session controller-routing lifecycle is not a compatibility contract for the new persistent ownership path.

---

## 1. Goal

PR4 answered only:

```text
Center M roots == Disabled
+ supported MSI Claw
+ stable controller topology
+ clean exclusive controller environment
+ prerequisites ready
+ recovery state safe for the new architecture
+ persistent HidHide foundation acceptable
    ↓
May Full PID1902 ownership proceed?
```

PR5 performs the **first real physical ownership operation** for the durable Addon controller architecture.

Required product flow:

```text
Center M roots == Disabled
+ PR4 DisabledBootAdmission == Ready
    ↓
re-check that Center M is still exactly Disabled at the mutation boundary
    ↓
inspect the current physical MSI Claw native mode
    ↓
current PID1902
    → keep PID1902

current PID1901
    → switch the SAME strong physical MSI Claw to PID1902
    ↓
bounded PID/PnP settle
    ↓
prove final state == PID1902
    ↓
prove final strong physical identity == the originally selected MSI Claw
    ↓
resolve the exact PID1902 DirectInput gamepad collection
    ↓
prove that DirectInput collection belongs to that same physical MSI Claw
    ↓
acquire DirectInput
    ↓
observe first valid controller state
    ↓
reconcile the persistent PR2 HidHide baseline to that exact PID1902 primary gamepad collection
    ↓
verify physical isolation
    ↓
physical ownership = Ready for PR6
```

PR5 stops there.

The virtual-controller invariant for this PR is:

```text
Xbox360 = detached
SteamDeck = detached
publisher = not started by the new Full PID1902 path
```

The central PR5 invariant is:

> **A virtual presentation must remain detached until the same physical MSI Claw is proven PID1902, a live DirectInput session has produced a valid state, and the exact primary PID1902 gamepad collection is persistently hidden and verified.**

---

## 2. Non-negotiable authority model

PR5 must not introduce a new authority source.

The existing persistent source of truth remains:

```text
Center M startup roots exactly Enabled
    → MSI / Stock authority
    → desired PID1901

Center M startup roots exactly Disabled
    → Addon authority
    → desired PID1902

Partial / Unavailable
    → no controller owner selected
```

PR4 already carries the boot-time authority/admission facts in `StartupResult`.

Do not add persisted fields such as:

```text
AddonOwnsController=true
DesiredPid=1902
Pid1902Acquired=true
PhysicalOwnershipEpoch
FirstPid1902Boot
PendingPhysicalTakeover
LastOwnedControllerId
```

The actual Windows configuration and current live hardware remain authoritative.

An in-memory PR5 acquisition result is acceptable and expected.

---

## 3. Explicit PR5 scope boundary

### 3.1 In scope

PR5 may:

- add one narrow process-lifetime MSI Claw physical-ownership component;
- consume PR4 `StartupResult.CenterMStartupState` and `DisabledBootAdmission`;
- reuse the one shared `CenterMStartupControl` for a fresh mutation-boundary read;
- reuse `MsiClawNativeStateManager` and the existing real MSI mode writer;
- keep an already-correct PID1902 device without issuing a mode write;
- switch PID1901 → PID1902 only for the verified same physical MSI Claw;
- reuse existing bounded native-mode/PnP settle behavior;
- perform one final typed strong-identity verification after the PID transition;
- reuse `VorticeDirectInputDeviceEnumerator`;
- reuse `MsiClawDirectInputDeviceSelector`;
- reuse `MsiClawInputSource.StartPrepared(...)` and first-valid-state verification;
- keep the successful DirectInput source alive for the Runtime lifetime so PR6 can consume it;
- resolve the exact PID1902 primary gamepad collection from the selected DirectInput descriptor/PnP topology;
- verify that DirectInput collection belongs to the same strong physical MSI Claw selected by the native-mode stage;
- call the PR2 persistent `AddonControllerHidHideBaseline.ApplyDisabledModeBaseline([exactTarget])`;
- verify the complete persistent HidHide baseline after target reconciliation;
- update PR4 HidHide admission so a **previously persisted, exact Addon-owned PID1902 primary target** does not incorrectly block the next Disabled boot;
- release process-owned DirectInput resources during controlled Runtime/process teardown while leaving PID1902 and persistent HidHide intact;
- add focused logs and unit/architecture tests.

### 3.2 Strictly out of scope

Do **not** implement any of the following in PR5:

- Xbox360 attach;
- SteamDeck attach;
- first presentation selection;
- publisher startup;
- runtime X360 ↔ SteamDeck switching;
- virtual neutral/switch policy;
- new rumble/gyro behavior;
- Center M runtime resurrection suppression;
- PID1902 → PID1901 drift recovery;
- physical disappearance/re-arrival recovery;
- long-lived PnP event subscription;
- full DirectInput-loss recovery loop;
- full suspend/resume PID1902 reacquisition;
- crash supervisor/service/heartbeat;
- automatic Runtime restart after unexpected death;
- new generalized controller reconciliation state machine;
- generic rollback transaction framework;
- uninstall redesign;
- final Enable-Center-M live physical release sequence;
- broad deletion/refactor of the old routing implementation.

The Full 1902 design explicitly leaves recovery/product hardening for later focused PRs.

Do not force every later lifecycle trigger into this first physical ownership PR.

---

## 4. Reuse low-level hardware primitives; do not reuse old route-scoped ownership semantics

PR5 should reuse proven low-level code rather than create a second MSI mode stack.

### 4.1 Reuse `MsiClawNativeStateManager`

The current MSI adapter already creates one real native state manager with:

```text
MsiClawNativeStateManager
    └─ MsiClawModeController
        ├─ WindowsControllerDeviceEnumerator
        ├─ MsiClawControlHidResolver
        └─ WindowsMsiClawModeWriter
```

Use the `MsiClawNativeStateManager` already owned by the existing `MsiClawDeviceAdapter`.

Do not construct another independent native-mode manager for PR5.

### 4.2 Reuse the existing DirectInput identity/reader path

Reuse:

```text
VorticeDirectInputDeviceEnumerator
→ DirectInputDeviceTopologyResolver
→ MsiClawDirectInputDeviceSelector
→ MsiClawInputSource.StartPrepared(...)
→ WaitForFirstValidStateAsync(...)
```

The existing path already verifies the MSI PID1902 gamepad usage, PnP instance identity, physical root, button layout, acquire result, and first valid state.

Do not create another DirectInput wrapper/library.

### 4.3 Reuse `AddonControllerHidHideBaseline`

Persistent Full PID1902 HidHide ownership is PR2's primitive:

```text
InspectDisabledModeBaseline(...)
ApplyDisabledModeBaseline(...)
ApplyEnabledModeBaseline(...)
```

PR5 extends its requested hidden-target set from:

```text
[]
```

to:

```text
[exact primary PID1902 gamepad collection]
```

Do not create a second HidHide writer.

### 4.4 Do NOT reuse the old route-scoped native ownership coordinator

Do not use:

```text
MsiClawNativeModeSessionCoordinator
```

as the new persistent physical authority owner.

It is built around old routing-session recovery/original-state semantics and can restore the pre-route native state when the route ends.

That is incompatible with the durable Disabled-mode contract:

```text
Center M Disabled
→ PID1902 remains desired after Runtime restart/shutdown
```

PR5 must call the low-level `MsiClawNativeStateManager` directly through one new persistent owner.

### 4.5 Do NOT reuse the old route-scoped physical isolation stage as the persistent baseline owner

Do not use:

```text
MsiClawPhysicalIsolationStage
```

for PR5's persistent HidHide mutation.

It is intentionally tied to:

- a routing recovery session;
- route-scoped HidHide journal additions;
- transient rollback semantics.

PR2's `AddonControllerHidHideBaseline` is the canonical persistent owner for Full PID1902.

Do not journal the new persistent target in the old routing recovery journal.

---

## 5. New narrow process-lifetime physical owner

Add one small MSI-specific component.

Suggested name:

```text
MsiClawAddonPhysicalOwnership
```

or:

```text
MsiClawPersistentPhysicalOwnership
```

Exact naming may follow current conventions.

Its responsibility is only:

```text
Disabled boot admitted
→ reconcile same physical MSI Claw to PID1902
→ acquire verified DirectInput
→ persist/verify exact HidHide target
→ retain process-owned DirectInput until teardown
```

Suggested conceptual dependencies:

```text
MsiClawAddonPhysicalOwnership
    ├─ MsiClawNativeStateManager
    ├─ IControllerDeviceEnumerator / WindowsControllerDeviceEnumerator
    ├─ DirectInput enumerator factory
    ├─ IMsiClawPreparedInputSource / MsiClawInputSource
    ├─ AddonControllerHidHideBaseline
    ├─ fresh Center M startup-state capture delegate
    └─ small bounded delay/clock seam only if required by tests
```

Do not add:

```text
ControllerAuthorityManager
PersistentControllerManager
PhysicalOwnershipStateMachine
Pid1902Manager
RecoveryOrchestrator
OwnershipEpoch
```

A simple one-shot startup acquisition plus owned-resource teardown is enough for PR5.

### 5.1 Result shape

A small in-memory result is sufficient, for example:

```csharp
internal enum MsiClawPhysicalOwnershipOutcome
{
    NotApplicable,
    Owned,
    Failed,
}

internal sealed record MsiClawPhysicalOwnershipResult(
    MsiClawPhysicalOwnershipOutcome Outcome,
    string Reason,
    bool ModeWriteIssued,
    string? HiddenTarget)
{
    internal bool IsOwned => Outcome == MsiClawPhysicalOwnershipOutcome.Owned;
}
```

Exact fields may differ.

Do not encode every internal step as a durable state enum.

PR6 only needs an unambiguous fact equivalent to:

```text
physical ownership verified == true
```

plus access to the live controller-state source.

---

## 6. Production wiring boundary

PR5 must run only when PR4 has positively admitted Addon authority.

Conceptually:

```csharp
if (startupResult.CenterMStartupState != FrontendCenterMStartupState.Disabled)
    return NotApplicable;

if (startupResult.DisabledBootAdmission?.IsReady != true)
    return FailedOrNotStarted;

return await physicalOwnership.AcquireAsync(...);
```

Do not attempt physical ownership when:

```text
Center M Enabled
Center M Partial
Center M Unavailable
DisabledBootAdmission Blocked
DisabledBootAdmission unavailable/null
```

The Runtime/frontend/tray should still come up where PR4 already says it may, but the new physical owner stays inactive.

### 6.1 Fresh authority re-check immediately before the first physical mutation

PR4's `StartupResult.CenterMStartupState` is the correct boot decision.

However, PR5 is the first code that will actually issue a physical native-mode mutation.

Immediately before the first possible `SwitchModeAsync(...)`, re-read the **same shared** `CenterMStartupControl` once.

Required:

```text
fresh state == Disabled
    → physical mutation may proceed

Enabled / Partial / Unavailable / read failure
    → fail closed
    → no mode write
    → no DirectInput acquire
    → no HidHide target mutation
```

This is a real authority-boundary check, not instruction-level race defense.

Do not re-run every PR4 admission fact again merely because time passed a few milliseconds.

One fresh Center M authority read at the actual mutation boundary is enough.

---

## 7. Native-state capture: identify one strong physical MSI Claw

Before any mode write, call the existing stable native-state capture.

Conceptually:

```csharp
var current = await nativeState.CaptureStableCurrentSnapshotAsync(token, allowTransientDeviceNotFound: true);
```

A usable PR5 starting state requires:

```text
capture allows mutation
snapshot exists
payload parses
mode == XInput or DirectInput
physical identity confidence == Strong
exactly one logical supported MSI Claw
```

Save the initial strong identity only in memory for this acquisition attempt.

Do not persist it.

### 7.1 Current PID1902

If:

```text
current mode == DirectInput / PID1902
```

then:

```text
ModeWriteIssued = false
keep PID1902
continue to final identity verification + DirectInput
```

Never implement:

```text
1902 → 1901 → 1902
```

as a startup normalization trick.

### 7.2 Current PID1901

If:

```text
current mode == XInput / PID1901
```

call the existing low-level transition once:

```csharp
await nativeState.SwitchModeAsync(
    MsiClawNativeMode.DirectInput,
    initialStrongIdentity,
    token);
```

The existing mode controller already resolves the exact control HID, performs the write, and waits a bounded period for the PID1902 control topology.

Do not add a second native-mode writer.

### 7.3 Missing / ambiguous / unsupported

For:

```text
DeviceNotFound after the bounded settle window
Indeterminate
multiple logical MSI controllers
identity not Strong
mode Other / PID1903 / unsupported
```

fail closed.

Do not issue a mode command against a guessed device.

---

## 8. Mandatory post-transition same-physical-identity verification

This is a critical PR5 safety requirement.

The current `MsiClawModeController.SwitchModeAsync(...)` verifies target PID/control topology, but the existing implementation currently records `CrossModeIdentityChanged` in diagnostics rather than making that log alone the final Full PID1902 authority decision.

PR5 therefore must perform a separate authoritative post-transition capture after PID1902 is present.

Required flow:

```text
initial strong physical identity
    ↓
(optional PID1901 → PID1902 mode write)
    ↓
CaptureStableCurrentSnapshotAsync(...)
    ↓
final mode must be DirectInput / PID1902
    ↓
final physical identity must be Strong
    ↓
initialIdentity.StronglyMatches(finalIdentity) == true
```

If the final PID1902 device is a different or ambiguous physical identity:

```text
fail closed
no DirectInput acquire
no HidHide target mutation
no virtual attach
```

Do not mutate a second/different MSI controller merely because one PID1902 target happened to enumerate.

### 8.1 Do not automatically roll PID1902 back to PID1901 on this failure

If the PID1901 → PID1902 command was already verified but a later PR5 stage fails, do **not** automatically issue PID1902 → PID1901 as a generic rollback.

Reason:

```text
Center M roots are still Disabled
→ desired authority is still Addon
→ PID1901 restoration would be an authority release the user did not request
```

Keep virtual output detached and report failure.

The official authority-release path remains:

```text
Enable Center M and Restart
```

---

## 9. Resolve the exact DirectInput gamepad collection

After final PID1902 identity verification, enumerate DirectInput using the existing path:

```text
VorticeDirectInputDeviceEnumerator
→ EnumerateGameControllers()
→ MsiClawDirectInputDeviceSelector.Select(...)
```

A selectable descriptor must already satisfy the existing MSI DirectInput requirements:

- VID `0x0DB0`;
- PID `0x1902`;
- verified PnP device path;
- verified physical root;
- Generic Desktop / Game Pad usage;
- sufficient button count;
- no ambiguous physical/PnP identity.

### 9.1 Bounded post-mode-switch DirectInput settle

The PID1902 control HID can become visible slightly before the DirectInput gamepad interface is fully usable.

Do not turn one normal PnP re-enumeration delay into a permanent ownership failure.

Use one small bounded DirectInput-selection window.

Recommended behavior:

```text
NotFound during immediate post-switch settle
    → retry for a short bounded window

Selected
    → continue

Indeterminate / multiple physical identities / malformed topology
    → fail immediately
```

Prefer an existing approximately 3-second controller-input settle convention if practical.

A simple `Task.Delay` loop is enough.

Do not add:

- a PnP event framework;
- a permanent poller;
- exponential backoff manager;
- acquisition epoch/generation framework.

Once acquisition succeeds or the bounded window expires, polling stops.

### 9.2 Current PID1902 boot also uses the same bounded DirectInput resolution

Even when no mode write was required, normal startup enumeration may still be settling.

Use the same bounded selection logic rather than creating separate "first boot" and "already 1902" code paths.

---

## 10. Prove the DirectInput collection belongs to the same native physical MSI Claw

`MsiClawDirectInputDeviceSelector` proves that the selected DirectInput descriptor is a verified MSI PID1902 gamepad collection.

PR5 additionally knows which strong native physical MSI Claw was selected before/after the native-mode stage.

Before acquiring DirectInput, resolve the descriptor's `PnpInstanceId` through the existing controller-device enumerator and build an `MsiClawPhysicalIdentity` from that PnP node.

Required:

```text
selected descriptor PnP node exists
MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId(PnpInstanceId) == true
PnP-derived physical identity confidence == Strong
finalNativeIdentity.StronglyMatches(directInputPnpIdentity) == true
```

If not:

```text
fail closed
DoNotAcquire
DoNotHide
```

This avoids relying on string coincidence between two different identity representations.

Do not invent a second physical-identity algorithm.

Use the current `MsiClawPhysicalIdentity.From(...)` / `StronglyMatches(...)` logic.

---

## 11. Acquire DirectInput and require a first valid state

After descriptor identity is proven, acquire it using the existing prepared-input path.

Conceptually:

```csharp
var start = inputSource.StartPrepared(descriptor);
if (!start.Started)
    fail;

var valid = await inputSource.WaitForFirstValidStateAsync(boundedToken);
if (!valid || !inputSource.IsRunning)
    fail;
```

Use a bounded first-valid-state wait.

Do not let Runtime startup hang indefinitely on a broken DirectInput device.

The existing `MsiClawInputSource` already:

- configures the DirectInput device;
- acquires it;
- polls at the existing interval;
- ignores the known invalid initial state for the existing bounded allowance;
- validates button layout;
- reports a first valid state;
- stops on real read/layout failure.

Reuse it.

### 11.1 Keep the successful source alive

On success, the PR5 physical owner retains this `MsiClawInputSource` for its lifetime.

PR6 must consume the **same live state source**.

Do not stop the input source immediately after using it merely as a health probe.

Do not create a second DirectInput reader for the first virtual presentation.

---

## 12. Exact persistent HidHide target reconciliation

Only after:

```text
PID1902 proven
same physical MSI Claw proven
exact DirectInput primary collection proven
DirectInput acquired
first valid state proven
```

may PR5 extend the persistent HidHide baseline.

Target:

```text
selectedDescriptor.PnpInstanceId
```

Validate again before mutation:

```csharp
MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId(target) == true
```

Then call the PR2 persistent owner:

```csharp
var result = addonControllerHidHideBaseline.ApplyDisabledModeBaseline([target]);
```

Success requires:

```text
result.IsCompliant == true
```

which is equivalent to:

```text
Success
or
AlreadyCompliant
```

The resulting baseline must be:

```text
Inverse = false
Active = true
Application whitelist = exactly Addon Runtime executable
Hidden targets = exactly one verified primary PID1902 gamepad collection
```

No broad VID/PID wildcard.

No entire composite-device tree hiding.

No route recovery journal entry.

---

## 13. Physical isolation verification

For PR5, successful PR2 persistent baseline read-back is the physical-isolation verification boundary.

After `ApplyDisabledModeBaseline([target])` returns compliant, optionally perform one final read-only `InspectDisabledModeBaseline([target])` if it simplifies tests/logging, but do not duplicate multiple equivalent validation layers.

The required proof is simply:

```text
exact target hidden
HidHide active
inverse false
only Addon whitelisted
no foreign hidden/whitelist state
```

PR5 does not need a second route-scoped `MsiClawPhysicalIsolationStage` on top of the persistent baseline.

The one persistent baseline owner is enough.

---

## 14. Important migration: PR4 admission must accept the PR5-persisted exact target on later boots

This is required for restart correctness.

PR4 was intentionally implemented before any exact PID1902 hidden target existed.

Its first-boot admission currently proves the PR3 foundation using the zero-target state:

```text
InspectDisabledModeBaseline([])
→ only AlreadyCompliant passes
```

After PR5 succeeds, the persistent state becomes:

```text
Hidden targets = [exact PID1902 primary collection]
```

If PR4 continues to inspect only `[]`, the next normal Windows/Runtime restart will classify the Addon's own persisted target as a foreign hidden entry and block Disabled boot.

PR5 must evolve this read-only admission contract.

### 14.1 Required admitted HidHide states

While Center M is Disabled, PR4/PR5 startup admission should accept exactly these persistent shapes:

#### First ownership boot

```text
Active = true
Inverse = false
Whitelist = exactly Addon
Hidden targets = 0
```

#### Later boot after PR5 successfully learned the target

```text
Active = true
Inverse = false
Whitelist = exactly Addon
Hidden targets = exactly 1
that entry passes MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId(...)
```

Everything else remains fail closed:

```text
foreign whitelist entry
unresolved whitelist entry
inverse configuration not safely supported
foreign hidden target
more than one persisted hidden target
non-primary/non-PID1902 hidden target
unreadable configuration
```

### 14.2 Keep this simple

Do not persist a separate list such as:

```text
AddonOwnedHidHideTargets.json
```

The persistent HidHide configuration itself already contains the exact target.

Do not create a generic ownership registry.

A small read-only helper/overload that lets `AddonControllerHidHideBaseline` classify **zero or one caller-validated existing owned target** is acceptable.

For example, conceptually:

```csharp
InspectDisabledModeBaselineAllowingExistingOwnedTarget(
    target => MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId(target));
```

Exact naming is flexible.

The helper must still enforce the baseline's existing exclusive rules; the validator must never turn arbitrary current hidden entries into "owned" entries.

Tests must prove that a random/foreign HidHide entry remains `Blocked`.

---

## 15. Acquisition ordering

Keep the successful path in this order:

```text
1. PR4 boot admission is Ready
2. fresh Center M state still Disabled
3. stable current native state captured
4. strong initial physical identity captured
5. if PID1901: switch to PID1902
6. stable final PID1902 state captured
7. same strong physical identity verified
8. bounded DirectInput descriptor resolution
9. descriptor/PnP identity verified against final native identity
10. DirectInput acquire
11. first valid controller state observed
12. apply persistent HidHide baseline with exact target
13. verify persistent physical isolation
14. retain DirectInput source as process-owned
15. return Owned
```

Do not hide the target before a usable DirectInput path is proven.

Do not attach a virtual device anywhere in this sequence.

---

## 16. Failure policy

PR5 must fail closed, but it must also respect durable authority.

### 16.1 Failure before a PID mode write

Examples:

- authority re-check not Disabled;
- native state missing/ambiguous;
- identity not Strong;
- unsupported native mode.

Required:

```text
no PID mutation
no DirectInput acquire
no HidHide target mutation
no virtual output
```

### 16.2 PID1901 → PID1902 transition fails

Required:

```text
stop acquisition
no DirectInput acquire
no HidHide target mutation
no virtual output
```

Do not guess what mode the device reached.

Log the transition result/reason.

### 16.3 Mode write succeeded, but final identity/PID verification fails

Required:

```text
no DirectInput acquire
no HidHide target mutation
no virtual output
```

Do not automatically force PID1901.

Center M remains Disabled, so PID1902 remains the desired state even though this acquisition attempt failed.

### 16.4 DirectInput selection/acquire/first-state fails

If no process-owned input was successfully acquired:

```text
leave physical PID as-is
leave existing persistent HidHide baseline as-is
no new hidden target
no virtual output
```

If DirectInput was partially started, stop/dispose that process-owned input source before returning failure.

Do not restore PID1901.

### 16.5 HidHide target reconciliation fails after DirectInput acquired

Required:

```text
stop/release the process-owned DirectInput session for this failed acquisition
keep Center M Disabled
keep physical PID as current reality
never attach virtual output
```

Do not run a generic rollback that clears the persistent PR3/PR5 HidHide foundation.

A HidHide mutation may have partially happened before an operation reported failure. Do not blindly remove the exact target and claim stock safety unless the existing PR2 primitive can prove that action is safe.

Fail closed and leave the user-visible repair path available:

```text
Enable Center M and Restart
```

A future focused reconcile/hardening PR may retry persistent target repair.

### 16.6 No generalized rollback transaction

Do not create a transaction engine that tries to unwind every completed step.

The correct durable semantics are asymmetric:

```text
process-owned handles may be released on failure
persistent Addon authority is NOT silently released
PID1902 is NOT converted back to PID1901 merely because a later stage failed
```

---

## 17. Runtime lifetime and teardown

A successful PR5 acquisition creates one long-lived process resource:

```text
DirectInput session
```

The exact HidHide target is **persistent configuration**, not a process lease.

The physical PID1902 desired state is also not a process lease.

### 17.1 Controlled Runtime restart / update relaunch

While Center M remains Disabled:

```text
Runtime teardown
→ stop/release DirectInput
→ leave exact persistent HidHide target
→ DO NOT switch PID1902 → PID1901
→ new Runtime starts
→ PR4 admission accepts existing exact owned target
→ PR5 reacquires current physical state
```

This is a required normal lifecycle.

### 17.2 Windows shutdown/restart

Same rule:

```text
release process-owned DirectInput
keep persistent HidHide baseline
no intentional PID1902 → PID1901 solely for shutdown/restart
```

Do not wire PR5 teardown through the old routing rollback path.

### 17.3 Teardown ownership

Whichever composition creates the new `MsiClawAddonPhysicalOwnership` must dispose it exactly once.

Prefer a simple process-host/runtime-composition ownership chain.

Do not create competing dispose owners.

The future PR6 virtual presentation owner must eventually tear down before/above physical input as required, but PR5 has no virtual attachment yet.

---

## 18. Sleep / hibernate / resume boundary for PR5

Sleep/hibernate remains a real product lifecycle requirement, but the Full 1902 design explicitly reserves full owned-state recovery for later hardening PRs.

PR5 must preserve these rules:

```text
suspend does NOT release Center M Disabled authority
suspend does NOT intentionally restore PID1901
legacy Stock Center M resume baseline remains suppressed by PR4
```

Do **not** add the full new PID1902 resume/reacquire architecture in PR5 merely because the first DirectInput source now exists.

For this PR:

- no virtual controller is attached, so there is no stale virtual input to neutral;
- a DirectInput loss during/after power transition may make PR5 ownership unhealthy;
- later PR8+ recovery work will reconcile PID/DirectInput/HidHide from fresh facts.

Do not assume the pre-suspend DirectInput handle is a permanent proof for future PR6.

PR6 must consume a currently healthy PR5 ownership fact/input source, and later lifecycle hardening will add the full resume path.

Do not re-enable the old XInput-restoration callback as a shortcut.

---

## 19. Unexpected Runtime death

Unexpected Runtime death after PR5 may leave:

```text
PID1902
persistent exact HidHide target
no live DirectInput process
no virtual presentation
```

This is a known real product recovery requirement.

Do not add a supervisor/service/watchdog in PR5.

The design already requires restart/reconcile to be idempotent so a later focused auto-restart mechanism can simply restart the Runtime.

PR5 must at least make the next normal Runtime/Windows startup capable of:

```text
PR4 admission accepts the exact persisted owned target
→ current PID1902 is kept
→ DirectInput reacquired
→ same target verified/reconciled
```

That is the required foundation for later crash recovery.

---

## 20. Do not initialize or attach a new presentation in PR5

PR1 already established the canonical dual-device VIIPER primitive.

PR5 does not need to change presentation policy.

The acceptance invariant is:

```text
physical ownership may become fully ready
AND
both virtual presentations remain detached
```

Do not call:

```text
AttachXbox360
AttachSteamDeck
EnterXbox360PresentationAsync
first-presentation mapper/publisher startup
```

from the new Full PID1902 path.

If construction of a detached canonical VIIPER substrate is already mechanically required by an existing composition boundary, it must remain fully detached and must not become part of PR5 success criteria beyond existing prerequisite readiness.

Prefer leaving first-presentation production wiring to PR6.

---

## 21. Interaction with Steam/BPM

PR4 keeps read-only Steam actual-game observation alive while legacy physical routing is disabled.

PR5 must not use Steam/BPM to decide physical ownership.

Required:

```text
Center M Disabled + PR4 admission Ready
→ PR5 physical ownership regardless of Steam/BPM state
```

Steam/BPM remains a future presentation selector only.

No Steam game/BPM event should:

- trigger PID switching;
- release DirectInput;
- clear/reapply the persistent HidHide target.

---

## 22. Interaction with old routing code

PR4 already prevents the legacy `AddonRoutingRuntime` from being selected while Center M is Disabled/Partial/Unavailable.

Keep that boundary.

PR5 should not spread a new `persistentMode` boolean through old route-scoped classes.

Reuse low-level primitives directly where useful, but do not reactivate the old routing owner.

No compatibility requirement exists to preserve the old Steam-session physical takeover semantics while Addon authority is Disabled.

---

## 23. Logging requirements

Make hardware validation possible from logs without adding new UI.

Recommended structured events/categories:

```text
ControllerOwnership
  Event=PhysicalOwnershipStarted
  CenterMState=Disabled
  Admission=Ready

ControllerOwnership
  Event=NativeStateCaptured
  Mode=XInput|DirectInput
  IdentityConfidence=Strong|...
  ModeWriteRequired=true|false

ControllerOwnership
  Event=Pid1902TransitionCompleted
  ModeWriteIssued=true|false
  FinalMode=DirectInput
  SamePhysicalIdentity=true|false

ControllerOwnership
  Event=DirectInputCandidateResolved
  PnpInstanceId=<exact target>
  SamePhysicalIdentity=true

ControllerOwnership
  Event=DirectInputReady
  FirstValidState=true

ControllerOwnership
  Event=PhysicalIsolationVerified
  HiddenTarget=<exact target>
  HidHideOutcome=Success|AlreadyCompliant

ControllerOwnership
  Result=Owned|Failed
  Reason=<stable reason>
```

Do not log on a permanent timer.

Do not dump unrelated process/environment data.

The exact PnP instance ID is already normal controller diagnostic data in this repository and may be logged consistently with existing input/routing diagnostics.

---

## 24. Frontend / protocol scope

No frontend protocol change is required solely for PR5.

Preferred foundation behavior:

- acquisition result is Runtime-internal;
- logs expose success/failure details;
- Device page continues to show Center M authority state;
- PR3 `Enable and Restart` remains the escape/release path.

Do not turn PR5 into a Device-page redesign.

Do not bump named-pipe protocol unless implementation truly needs a new frontend field.

PR6 may later surface virtual-presentation state if useful.

---

## 25. Tests — native physical ownership

Add focused tests for the new physical owner.

### 25.1 Already PID1902

Given:

```text
Center M Disabled
PR4 admission Ready
current native mode DirectInput
strong physical identity
```

expect:

```text
no mode write
same identity verified
DirectInput acquisition proceeds
```

### 25.2 PID1901 switches exactly once

Given:

```text
current native mode XInput
strong identity A
```

expect:

```text
SwitchModeAsync(DirectInput, identity A) exactly once
final mode DirectInput
final identity strongly matches A
```

### 25.3 Missing / ambiguous / Other blocks before mutation

Test at least:

- DeviceNotFound after settle;
- Indeterminate/multiple logical MSI controller;
- weak/indeterminate identity;
- unsupported native mode.

Expect zero DirectInput/HidHide mutation.

### 25.4 Mode transition failure blocks

`SwitchModeAsync` failure must prevent DirectInput and HidHide target mutation.

### 25.5 Final cross-mode identity mismatch blocks

Simulate:

```text
initial PID1901 strong identity A
mode write reports success
final PID1902 strong identity B
```

expect:

```text
Failed
DirectInput never acquired
HidHide target never applied
no PID1901 rollback command issued
```

This test is mandatory.

---

## 26. Tests — DirectInput resolution and acquisition

### 26.1 DirectInput appears after a normal short PnP delay

First enumeration returns `NotFound`, later enumeration selects the exact device inside the bounded window.

Expect acquisition succeeds.

### 26.2 DirectInput remains missing

Bounded window expires.

Expect:

```text
Failed
no HidHide target mutation
```

### 26.3 DirectInput ambiguous

Multiple physical identities or invalid topology must fail closed immediately.

Do not keep retrying an ambiguity as if it were normal PnP delay.

### 26.4 Descriptor is a different physical MSI Claw

Even though descriptor is valid PID1902, its PnP-derived strong physical identity does not match the native owner identity.

Expect:

```text
DoNotAcquire
DoNotHide
```

### 26.5 Acquire failure

`StartPrepared` returns failure.

Expect no HidHide target mutation.

### 26.6 First valid state never arrives / source stops

Expect:

```text
input source stopped/disposed
no HidHide target mutation
Failed
```

### 26.7 Successful DirectInput remains alive

After PR5 returns `Owned`:

```text
inputSource.IsRunning == true
```

until owner teardown.

PR6 must be able to reuse that same state source.

---

## 27. Tests — persistent HidHide target

### 27.1 Exact target application

After successful DirectInput acquisition, verify the baseline is called with exactly:

```text
[descriptor.PnpInstanceId]
```

and the target passes:

```text
MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId(...)
```

### 27.2 Compliant baseline required

`Success` / `AlreadyCompliant` → ownership may succeed.

`Conflict` / `Unavailable` / `MutationFailed` / `VerificationFailed` → ownership fails and virtual output remains detached.

### 27.3 HidHide failure releases process-owned DirectInput

If DirectInput succeeded but target reconcile fails:

```text
input source stopped
PID1902 not automatically restored to PID1901
persistent authority not cleared
```

### 27.4 No routing recovery journal ownership

Architecture/source test should prove the new physical owner does not call:

```text
BeginDeviceNativeStateMutation
RecordHidHideDeviceAddition
CompleteHidHideDeviceAddition
```

for the new persistent ownership path.

---

## 28. Tests — subsequent Disabled boot with persisted target

This is mandatory regression coverage.

### 28.1 Zero-target first boot remains admitted

PR4 behavior remains valid:

```text
Active=true
Inverse=false
Addon-only whitelist
hidden targets=0
→ admission may be Ready
```

### 28.2 One exact previously-owned target is admitted

Given:

```text
Active=true
Inverse=false
Addon-only whitelist
Hidden=[valid primary PID1902 target]
```

expect PR4 Disabled boot admission does **not** classify it as foreign conflict merely because PR5 persisted it.

### 28.3 Foreign hidden target remains blocked

A random HidHide entry must not become allowed simply because the new admission helper can accept one existing target.

### 28.4 Multiple hidden targets blocked

Under the current product scope of one supported internal MSI Claw physical controller, more than one persisted primary-controller target is ambiguous for this early architecture.

Fail closed rather than inventing multi-device ownership.

### 28.5 Restart/reacquire flow

Conceptual integration test:

```text
first Disabled boot PR5 success persists target
→ simulated controlled Runtime restart
→ PR4 admission accepts target
→ current PID1902 path performs no mode write
→ DirectInput reacquired
→ baseline already compliant
```

No 1902→1901 round trip.

---

## 29. Tests — teardown semantics

### 29.1 Controlled teardown

After successful ownership:

```text
DisposeAsync
→ DirectInput stopped/disposed exactly once
→ no PID1901 mode write
→ no ApplyEnabledModeBaseline
→ no hidden-target removal
```

### 29.2 Failed acquisition cleanup

If acquisition fails after starting DirectInput, the process-owned input resource is cleaned up.

No virtual resource exists to clean in PR5.

### 29.3 Idempotent teardown

Calling owner teardown twice must not crash or issue controller-authority mutations twice.

Do not build a generalized lifetime manager merely for this; ordinary idempotent `DisposeAsync` is enough.

---

## 30. Architecture guard tests

Add lightweight guards consistent with existing repository style.

The new Full PID1902 physical owner must not depend on or invoke:

```text
MsiClawNativeModeSessionCoordinator
MsiClawPhysicalIsolationStage
RoutingPipelineSessionCoordinator
RecoveryManager route mutation methods
AttachXbox360
AttachSteamDeck
EnterXbox360PresentationAsync
```

It may use:

```text
MsiClawNativeStateManager
MsiClawModeController through the existing manager
MsiClawDirectInputDeviceSelector
MsiClawInputSource
AddonControllerHidHideBaseline
```

Also prove:

```text
Center M Enabled/Partial/Unavailable never starts PR5 ownership
Disabled admission Blocked never starts PR5 ownership
```

Avoid brittle tests that freeze incidental line order unrelated to lifecycle safety.

---

## 31. Manual hardware validation

Run on a supported MSI Claw.

Capture logs for every scenario.

### 31.1 First Disabled boot from stock PID1901

Starting state:

```text
Center M Enabled
physical PID1901
```

Use:

```text
Disable and Restart
```

On next boot verify:

```text
PR4 admission Ready
fresh authority still Disabled
initial mode PID1901
one PID1901 → PID1902 command
PID1902 appears
same strong physical MSI Claw verified
DirectInput candidate resolved
first valid state read
exact primary PID1902 collection persisted in HidHide
physical isolation verified
no virtual controller attached
```

Verify Windows/game-controller behavior does not show a PR5-created virtual X360/SteamDeck yet.

### 31.2 Controlled Runtime/Windows restart while PID1902 + exact HidHide target already exist

Verify:

```text
PR4 admission accepts existing exact target
initial mode PID1902
ModeWriteIssued=false
no PID1901 appearance caused by Addon startup
DirectInput reacquired
baseline AlreadyCompliant or equivalent verified state
no virtual controller attached
```

This is one of the most important PR5 acceptance tests.

### 31.3 Current PID1902 start

Start with Center M roots Disabled and physical PID1902 already present.

Verify there is no forced 1902→1901→1902 round trip.

### 31.4 HidHide exact-target validation

After success inspect HidHide:

```text
Active = true
Inverse = false
Whitelist = Addon Runtime executable only
Hidden = exact PID1902 primary gamepad collection only
```

Do not accept a broad PID1902 root/wildcard.

### 31.5 Failure after PID1902 transition

If practical in a controlled development setup, make DirectInput unavailable after PID1902 is established.

Verify:

```text
no virtual attach
no automatic PID1901 rollback
Runtime/frontend/tray remain alive
Enable and Restart remains available
```

### 31.6 Controlled process restart

While Center M remains Disabled:

```text
restart/update relaunch the Runtime through an existing development-safe mechanism
```

Verify teardown/restart does not intentionally restore PID1901 or clear persistent HidHide.

### 31.7 Sleep/resume smoke check

PR5 does not implement full new resume recovery yet.

Still verify:

```text
Sleep → Resume
```

does **not** invoke the legacy stock XInput restoration baseline.

Record what happens to the PR5 DirectInput session for the next hardening PR; do not expand PR5 solely to cover every possible resume observation unless a realistic safety blocker is demonstrated.

---

## 32. Expected files

Exact diff may vary, but keep implementation concentrated.

Likely new file:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs
```

Likely modified production files:

```text
src/SteamInputAddonforClaw/Startup/DisabledBootControllerAdmission.cs
src/SteamInputAddonforClaw/Startup/AddonStartupComposition.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/HidHide/AddonControllerHidHideBaseline.cs
```

Potentially small changes if needed:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputContracts.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawNativeStateManager.cs
```

Prefer not to modify the old routing pipeline unless a reusable low-level primitive requires a truly small correction.

Likely tests:

```text
tests/SteamInputAddonforClaw.Tests/MsiClawAddonPhysicalOwnershipTests.cs
tests/SteamInputAddonforClaw.Tests/DisabledBootControllerAdmissionTests.cs
tests/SteamInputAddonforClaw.Tests/AddonControllerHidHideBaselineTests.cs
existing hosting/startup architecture tests as appropriate
```

Avoid unrelated UI, Overlay, QAM, TDP, profile, OEM1, rumble, gyro, or publisher work.

---

## 33. No-go design rules

Do not solve PR5 by adding:

```text
ControllerAuthorityManager
ControllerOwnershipStateMachine
Pid1902Watchdog
PersistentHidHideRegistry
ControllerEpoch
PnPGenerationBarrier
RecoveryTransaction
FirstBootAfterDisable flag
new supervisor/service
new generic DirectInput manager
new HidHide manager
```

Do not defend against arbitrary instruction-level timing combinations.

Protect realistic supported lifecycle facts directly:

- current PID may be 1901 or 1902;
- native-mode re-enumeration takes a bounded amount of time;
- the DirectInput gamepad interface can lag the control HID briefly;
- a mode operation can genuinely fail;
- physical identity can genuinely become ambiguous;
- DirectInput acquisition/readiness can genuinely fail;
- HidHide operations can genuinely fail;
- controlled Runtime/Windows restart must not release authority;
- an existing exact persistent target must survive restart without blocking the next boot.

Those are sufficient for PR5.

---

## 34. Acceptance criteria

PR5 is complete when all of the following are true:

1. Physical ownership starts only for exact Center M Disabled + PR4 admission Ready.
2. The shared Center M authority fact is read again immediately before first physical mutation.
3. Current PID1902 is kept without a mode write.
4. Current PID1901 is switched once to PID1902 using the same strong physical MSI identity.
5. PID/PnP settle is bounded.
6. Final native state is proven PID1902.
7. Final native strong identity is proven to match the initial physical MSI Claw.
8. DirectInput selection uses the existing verified MSI PID1902 path.
9. A short normal DirectInput/PnP appearance delay is tolerated with a bounded retry.
10. Ambiguous DirectInput identity fails closed.
11. The selected DirectInput PnP collection is proven to belong to the same native physical MSI Claw before acquire/hide.
12. DirectInput acquisition succeeds and a first valid state is observed before HidHide target mutation.
13. The successful DirectInput source remains alive for the physical owner's process lifetime.
14. The exact primary PID1902 gamepad collection is the only persisted hidden target.
15. Persistent HidHide reconciliation is performed by `AddonControllerHidHideBaseline`, not old route-scoped isolation/recovery.
16. Physical isolation is read-back verified before ownership reports success.
17. PR4 admission accepts both the zero-target first-boot baseline and one exact previously persisted PR5 target.
18. Foreign/multiple/unvalidated hidden targets still block.
19. Failure after PID1902 transition never automatically restores PID1901 while Center M remains Disabled.
20. Failed acquisition never attaches a virtual controller.
21. Controlled Runtime/process/Windows teardown releases DirectInput but leaves PID1902 desired state and persistent HidHide intact.
22. The new path does not use the old routing recovery journal for persistent native/HidHide ownership.
23. Both Xbox360 and SteamDeck remain detached by the new Full PID1902 path.
24. No new presentation publisher starts in PR5.
25. No new supervisor/watchdog/generalized authority state machine is added.
26. Debug and Release builds are clean.
27. Full automated test suite passes.

---

## 35. Expected next PR — do not implement here

After PR5 reports verified physical ownership:

```text
Center M roots == Disabled
+ PR4 admission Ready
+ PR5 physical ownership == Owned
+ PID1902 proven
+ DirectInput healthy
+ exact HidHide isolation proven
    ↓
PR6: first virtual presentation attach
```

PR6 should:

```text
initialize/reuse the canonical dual-device VIIPER Runtime as required
→ both logical devices begin detached
→ read the freshest Steam/BPM state immediately before first attach
→ Steam/BPM inactive: attach Xbox360
→ Steam game or BPM active: attach SteamDeck
→ send neutral target state
→ start only the selected presentation publisher
```

PR6 must use the same live PR5 physical input source.

It must not re-run PID1902 acquisition merely to choose a presentation.

Runtime Xbox360 ↔ SteamDeck switching remains the following PR.

---

## 36. Final implementation principle

PR4 established **facts before actions**.

PR5 adds the first action while keeping one clear owner:

```text
one Center M authority source
one existing MSI native-state manager
one process-owned DirectInput source
one persistent HidHide baseline owner
zero attached virtual controllers
```

Keep that shape.

The goal is not maximum abstraction or maximum race defense.

The goal is a small, hardware-verifiable transition from:

```text
Disabled boot admitted
```

to:

```text
same MSI Claw is PID1902
+ DirectInput is live
+ exact physical gamepad collection is isolated
+ virtual output is still detached
```

Only after that proof should PR6 expose a virtual controller.
