# Work Order — PR11: Full1902 Hardware-Validation Routing and Startup Fixes

## Status

Implementation work order for the first corrective PR after PR10 / PR #448 hardware validation.

This PR is driven by **real supported MSI Claw hardware evidence from Runtime v0.1.207.0**, not by a theoretical race review.

Before implementation, read and treat these documents as design authorities:

- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/work-order/PR5_PID1902_DIRECTINPUT_PHYSICAL_OWNERSHIP_WORK_ORDER.md`
- `docs/work-order/PR8_OWNED_DIRECTINPUT_SESSION_RECOVERY_WORK_ORDER.md`
- `docs/work-order/PR9_OWNED_PID1901_DRIFT_RECLAIM_WORK_ORDER.md`
- `docs/work-order/PR10_PHYSICAL_DEVICE_LOSS_PNP_RETURN_RECOVERY_WORK_ORDER.md`
- `docs/work-order/PR10_HIDHIDE_STARTUP_AUTHORITY_ADDENDUM.md`

Also inspect the current `main` implementations of:

- `Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs`
- `Devices/MSI/Claw/MsiClawModeController.cs`
- `Devices/MSI/Claw/MsiClawModeContracts.cs`
- `Devices/MSI/Claw/MsiClawNativeStateManager.cs`
- `Input/DirectInput/MsiClawDirectInputDeviceSelector.cs` and adjacent selector/enumerator code
- `Install/StartupRegistration.cs`
- `Install/ElevatedStartupTaskSetup.cs`
- `Settings/StartupSettingsCoordinator.cs`
- the focused tests for the above components.

The application is pre-release. Correct the Full1902 product contract directly; do not preserve an invalid cross-PID identity assumption merely because older PR5/PR9 tests encoded it.

---

# 1. Goal

Fix the concrete blockers and false failures found during the first successful Full1902 Disabled-mode boot on MSI Claw hardware.

The tested system successfully reached all of these steps:

```text
Center M Enabled
→ Disable and Restart
→ mandatory Addon startup task created
→ Center M startup roots Disabled
→ reboot
→ Addon Runtime starts in background
→ Disabled admission Ready
→ HidHide baseline compliant
→ canonical VIIPER Runtime initialized
→ current controller detected as PID1901 / XInput
→ Addon writes DirectInput mode command
→ PID1901 disappears
→ PID1902 appears
→ final native capture reports DirectInput / Strong
```

but physical ownership then failed with:

```text
CrossModeIdentityMismatch
```

As a result:

```text
PR5 physical ownership not committed
→ no live DirectInput source
→ Xbox360 cannot attach
→ BPM/Steam presentation reconcile fires
→ SteamDeck cannot attach
→ "no live PR5 input source"
```

The same invalid cross-mode identity assumption also caused the first Center M Enable release to report:

```text
Pid1901RestoreUnverified:Ok
```

even though the hardware had actually completed PID1902 → PID1901 successfully.

Separately, the first Addon startup-task repair still performs a known-failing normal-user `RegisterTaskDefinition` attempt before invoking the elevated repair path:

```text
Startup task repair required
→ non-elevated RegisterTaskDefinition
→ E_ACCESSDENIED / 0x80070005
→ elevated repair requested
→ elevated repair succeeds
```

That parent-process write attempt is unnecessary in the supported production path and should be removed.

PR11 must fix these issues without weakening same-mode device validation, without PID-only blind attachment, and without adding a new authority/recovery framework.

---

# 2. Real hardware evidence

## 2.1 PID1901 → PID1902 transition succeeds physically

v0.1.207 observed the live controller before the Addon mode write as:

```text
Mode=XInput
ResolvedPhysicalRoot=USB\VID_0DB0&PID_1901\00006F64096B22E7
IdentityConfidence=Strong
```

The Addon then successfully wrote the DirectInput mode command.

The transition observer saw:

```text
OldPidPresent=False
TargetPidPresent=True
TargetControlCandidateCount=1
LogicalCandidateCount=1
TargetIdentityConfidence=Strong
NativeModeTransitionSucceeded
```

The authoritative post-transition capture then reported:

```text
Mode=DirectInput
ResolvedPhysicalRoot=USB\VID_0DB0&PID_1902\5&3AF6909B&0&4
IdentityConfidence=Strong
```

The current PR5 owner subsequently performs:

```csharp
initialIdentity.StronglyMatches(finalIdentity)
```

and fails because the Windows physical-root/container representation is not stable across this real MSI native-mode transition.

The hardware evidence therefore proves:

> `MsiClawPhysicalIdentity.StronglyMatches` is useful for same-mode/current-topology identity checks, but it is **not a valid cross-PID continuity predicate for PID1901 ↔ PID1902 on the supported MSI Claw hardware**.

Do not attempt to force the two Windows instance strings to become equal.

## 2.2 Center M Enable physically restored PID1901 but was reported failed

The same v0.1.207 run later performed PID1902 → PID1901 during `Enable Center M and Restart`.

The native transition reported success and a fresh native capture reported:

```text
Mode=XInput
PID1901
IdentityConfidence=Strong
```

but `ReleaseForCenterMEnableAsync` rejected it because it compared the pre-write PID1902 identity to the post-write PID1901 identity with `StronglyMatches`.

The resulting reason was:

```text
Pid1901RestoreUnverified:Ok
```

A second Enable attempt succeeded immediately because the controller was already PID1901.

This is a real false-negative in the official authority-release lifecycle.

## 2.3 Startup-task first repair unnecessarily probes a known-denied write

v0.1.207 observed:

```text
Startup task synchronization started
Startup task repair required
Startup task registration was denied. 0x80070005
elevated repair requested
elevated repair completed. Created
```

The elevated bounded child is the actual supported creation/repair path on this hardware.

The normal Runtime should keep read-only task verification, but should not deliberately issue a write that the supported machine has proven requires elevation merely to discover `E_ACCESSDENIED` first.

## 2.4 Immediate parent readback was transiently false

The same first creation showed another concrete diagnostic issue:

```text
elevated child: task created / helper readback verified
parent Runtime immediately after helper exit: ReadbackVerified=False
later Disable transition: TaskFound=True / TaskCompliant=True
next reboot: --background Runtime actually starts
```

So the task was genuinely created, but the immediate normal-process readback did not yet prove it.

Keep the requirement for independent parent readback, but allow a **small bounded read-only settle window** after a successful elevated create/repair. Do not add repeated writes or indefinite retries.

---

# 3. Product safety rule for cross-mode identity

Do not replace the current safety checks with:

```text
"VID_0DB0 + PID_1902 exists"
→ trust first matching node
```

Windows can retain stale/non-present device instances. The Addon must continue to operate only on **current present/live topology**.

The corrected rule is:

> **Same-mode ownership uses strong physical identity matching. Cross-mode continuity is proven by the Addon's own bounded mode transition plus unique present target topology and fresh same-mode validation after re-enumeration.**

For a PID1901 → PID1902 transition, the useful proof is already available:

```text
1. fresh native capture has exactly one logical supported MSI Claw candidate;
2. current source mode is PID1901 / XInput;
3. source identity is Strong;
4. source control HID is uniquely resolved against that current identity;
5. Addon itself writes the mode command to that source;
6. old PID1901 disappears within the bounded transition window;
7. exactly one present target PID1902 control logical group appears;
8. target topology is valid and Strong;
9. fresh authoritative native capture reports PID1902 / DirectInput;
10. current DirectInput enumeration resolves exactly one usable MSI Claw gamepad descriptor;
11. descriptor is the exact primary PID1902 collection;
12. descriptor PnP identity StronglyMatches the fresh **PID1902** native identity;
13. the real DirectInput endpoint opens and produces a first valid input state.
```

Steps 10-13 are important: they prevent attaching to a stale/non-present historical PID1902 node.

What is **not** required is:

```text
PID1901 physical-root string == PID1902 physical-root string
```

because hardware validation proved that invariant false.

---

# 4. Preserve `StronglyMatches`; stop using it across PID boundaries

Do **not** globally weaken or redefine `MsiClawPhysicalIdentity.StronglyMatches` just to make this test pass.

It is still useful for operations where both identities belong to the same current native mode/topology, including:

- finding the exact source control HID before a mode write;
- proving an already-PID1902 boot is still the expected current PID1902 device;
- matching a fresh PID1902 native capture to the resolved PID1902 DirectInput PnP collection;
- same-PID1902 DirectInput-session recovery;
- Windows writer-side verification immediately before writing the current source control endpoint.

The bug is the **call site semantics**, not necessarily the predicate itself.

Audit every `StronglyMatches` use in the Full1902 owner and classify it as:

```text
same-mode/current-topology comparison
→ keep

cross-mode PID1901 ↔ PID1902 comparison
→ replace with transition/current-topology proof
```

Do not add a global `CrossModeIdentityManager`, persisted serial map, topology database, or generalized identity framework.

---

# 5. Fix `MsiClawModeController` transition completion semantics

The existing mode controller already does most of the correct work:

- strongly verifies the source in its current mode;
- writes to the exact current control HID;
- polls only present target-PID devices;
- filters the expected target control usage;
- groups target candidates by logical identity;
- fails when more than one target logical group is present;
- records `OldPidDisappeared`, `TargetPidAppeared`, `SourceIdentityVerified`, and `TargetTopologyVerified`.

However, current success can be returned as soon as one target group appears even if `oldGone` is still false.

For cross-mode continuity, require both:

```text
exactly one target logical control group present
AND
old PID no longer present
```

before returning `Succeeded`.

Preferred bounded behavior:

```text
targetGroups == 0
→ continue bounded settle

targetGroups > 1
→ AmbiguousDevice / fail closed

targetGroups == 1 && oldGone == false
→ continue bounded settle

targetGroups == 1 && oldGone == true
→ Succeeded
```

If the target appears but the old PID remains through the bounded deadline, use the existing `OldDeviceDidNotDisappear` status if it cleanly fits the implementation.

Do not add another polling loop; reuse the existing bounded transition loop.

`CrossModeIdentityChanged` may remain a diagnostic field, but it must not itself imply failure.

---

# 6. Fix initial Full1902 acquisition

Current PR5 acquisition conceptually does:

```csharp
initial PID1901 identity
→ SwitchMode(DirectInput)
→ fresh PID1902 capture
→ initialIdentity.StronglyMatches(finalIdentity) // invalid on real hardware
```

Change this so the two paths are explicit.

## 6.1 Already PID1902 boot — same-mode proof remains strict

When no mode write is required:

```text
initial mode = DirectInput / PID1902
→ fresh current PID1902 validation
→ same-mode strong identity proof may remain
→ exact DirectInput descriptor
→ exact primary collection
→ descriptor PID1902 PnP identity matches fresh PID1902 native identity
→ real DirectInput first-valid-state
→ HidHide exact target
→ ownership commit
```

Do not weaken this path merely because cross-mode continuity is changing.

## 6.2 PID1901 boot — use the successful transition as the bridge

When a mode write is required:

```text
initial PID1901 Strong + unique current logical controller
→ fresh Center M == Disabled
→ one PID1901→PID1902 switch
→ require successful transition evidence
→ require old PID1901 disappeared
→ require target PID1902 appeared / unique topology
→ fresh final native capture must be PID1902 / Strong
→ DO NOT compare initial PID1901 root/container to final PID1902 using StronglyMatches
→ resolve live DirectInput descriptor
→ exact primary collection
→ final PID1902 identity StronglyMatches descriptor PID1902 identity
→ first valid DirectInput state
→ HidHide verify
→ ownership commit
```

A simple implementation shape is preferred. For example, retain the `MsiClawModeTransitionResult` from the mode write and use its already-existing evidence instead of introducing new state.

Conceptually:

```csharp
MsiClawModeTransitionResult? transition = null;

if (initialMode == MsiClawNativeMode.XInput)
{
    ...
    transition = await _switchMode(...);
    if (!transition.Succeeded
        || !transition.WriteSucceeded
        || !transition.OldPidDisappeared
        || !transition.TargetPidAppeared
        || !transition.SourceIdentityVerified
        || !transition.TargetTopologyVerified)
    {
        return Fail(...);
    }
}

var finalCapture = await _captureStableNativeState(...);
...
if (finalMode != MsiClawNativeMode.DirectInput)
    return Fail(...);

if (initialMode == MsiClawNativeMode.DirectInput
    && !initialIdentity.StronglyMatches(finalIdentity))
{
    return Fail(...); // same-mode only
}
```

Exact code may differ, but keep the ownership proof readable and local.

---

# 7. Keep stale PID1902 protection through live DirectInput proof

Do not delete the existing DirectInput verification stages.

After the corrected cross-mode transition, the selected device must still satisfy the current product contract:

```text
current DirectInput enumeration
→ one selected MSI Claw descriptor
→ descriptor PnpInstanceId is PID1902 primary collection
→ corresponding PnP node exists/current
→ its PID1902 identity is Strong
→ it StronglyMatches the fresh final PID1902 native identity
→ input source successfully StartPrepared(descriptor)
→ first valid state observed
```

This is the primary reason a stale historical PnP PID1902 node cannot become a live controller merely because its VID/PID string remains in Windows.

Do not change `MsiClawDirectInputDeviceSelector` into a first-match VID/PID selector.

---

# 8. Fix PR9 PID1901 drift reclaim for the same real cross-mode behavior

The existing recovery path currently performs this before a PID1901 reclaim:

```csharp
ownedPid1902Identity.StronglyMatches(currentPid1901Identity)
```

That is the same invalid cross-PID assumption and will prevent PR9 from working on the hardware shape now observed.

Correct recovery branching by **current mode**.

## 8.1 Current mode remains PID1902

This is same-mode PR8 recovery.

Keep the strong match:

```text
committed PID1902 identity
StronglyMatches
fresh current PID1902 identity
```

Then continue the existing exact-target recovery tail.

## 8.2 Current mode is PID1901

For owned-state drift:

```text
prior ownership is committed
+ exact owned hidden target retained
+ input source is down
+ fresh native capture succeeds
+ capture has one logical supported MSI Claw current candidate
+ current mode = PID1901
+ current identity = Strong
+ Center M startup roots are freshly exactly Disabled
```

This is sufficient to treat the **single current supported internal Claw controller** as the reclaim candidate under the current supported product scope.

Do not attempt to compare its PID1901 root string to the previous PID1902 root string.

Immediately before mutation:

```text
use the fresh current PID1901 identity as the source expected identity
→ source control HID resolution remains same-mode StronglyMatches
→ issue exactly one PID1901→PID1902 write
→ require successful bounded transition evidence
→ fresh final PID1902 capture / Strong
→ current live DirectInput descriptor
→ descriptor identity matches fresh final PID1902 identity
→ exact recovered target MUST still equal committed _ownedHiddenTarget
→ HidHide normalize/verify
→ restart SAME input source
→ first valid state
→ commit recovery
```

No wildcard device selection and no second authority owner.

### 8.3 Ambiguity remains fail-closed

`MsiClawNativeStateManager.CaptureSnapshot()` already fails when it sees multiple logical MSI controller candidates.

Preserve this.

A PID1901 reclaim must perform **no mode write** when current topology is:

- missing;
- multiple logical MSI controllers;
- unsupported/indeterminate mode;
- weak/indeterminate current identity;
- Center M not exactly Disabled.

This is the supported safety boundary. Do not create a cross-PID serial resolver solely to retain the old impossible root comparison.

### 8.4 Refresh the in-memory PID1902 identity after successful reclaim

After a full successful reclaim has proven:

- final PID1902 native state;
- live exact DirectInput collection;
- unchanged committed HidHide target;
- first valid DirectInput state;

refresh `_ownedPhysicalIdentity` to the fresh final PID1902 identity.

This is in-memory current ownership evidence only.

Do not change `_ownedHiddenTarget` migration policy. `RecoveredTargetChanged` still fails closed exactly as PR10 requires.

---

# 9. Fix Center M Enable PID1902 → PID1901 false-negative

`ReleaseForCenterMEnableAsync` currently:

```text
capture PID1902 Strong identity
→ switch to PID1901
→ fresh PID1901 capture
→ pre-write PID1902 StronglyMatches(post-write PID1901)
→ false
→ Pid1901RestoreUnverified
```

Correct it using the inverse transition proof.

Required path:

```text
stop/join/release DirectInput as existing code requires
→ fresh current native capture
→ if already PID1901 / XInput: no mode write, success
→ if PID1902 / DirectInput:
     current identity Strong
     switch exact current source to PID1901
     require write success
     require old PID1902 disappeared
     require one valid present PID1901 target logical group
     fresh final capture must be XInput / PID1901 / Strong
     DO NOT compare PID1902 and PID1901 physical-root strings
→ return physical release success
→ caller continues HidHide cleanup / Center M startup-root enable / reboot
```

Keep failures fail-closed. If PID1901 cannot actually be observed after the switch, do not clear HidHide/enable Center M as though release succeeded.

---

# 10. Fix misleading release failure reasons

Do not produce reasons such as:

```text
Pid1901RestoreUnverified:Ok
```

`verifyReason == "Ok"` only means `TryReadIdentity` succeeded; it must not be concatenated when a later independent predicate fails.

Split verification failures explicitly.

Conceptually:

```csharp
if (!TryReadIdentity(..., out var verifyReason))
    return Failure("Pid1901RestoreUnverified:" + verifyReason);

if (finalMode != MsiClawNativeMode.XInput)
    return Failure("Pid1901RestoreFinalModeNotXInput:" + finalMode);
```

There should no longer be a cross-mode `StronglyMatches` failure at this point.

Use similarly precise reasons for initial acquisition/reclaim if helpful, but do not create a large result hierarchy solely for diagnostics.

---

# 11. Simplify normal Runtime startup-task repair

The current production manager is created with an elevated repair invoker.

After read-only inspection determines that the fixed Addon task is missing or materially drifted, the parent Runtime currently attempts `_taskStore.Register(configuration)` first and only invokes elevation after `AccessDenied`.

On supported hardware the first registration has already proven to require elevation.

Change the production flow to:

```text
Synchronize(true)
→ read exact current task

compliant
→ Success
→ no write
→ no UAC

missing / materially drifted
→ if this manager has the production elevated repair invoker:
     invoke elevated EnsureOwnedTask directly
     do NOT call parent-process RegisterTaskDefinition first
     after child completes, independently read back from normal Runtime
     require exact task contract

manager has no elevated invoker
→ this is the elevated child/direct-write path
→ RegisterTaskDefinition directly
→ readback verify
```

This reuses the existing constructor/composition distinction cleanly:

```text
normal production Runtime = WithElevatedRepair()
elevated --ensure-startup-task child = manager without elevated fallback
```

Do not add another startup manager.

Do not remove `IOwnedStartupTaskStore.Register`; the elevated child still needs the actual Task Scheduler write primitive and unit tests need the seam.

Do not run the entire Runtime elevated.

---

# 12. Add a bounded post-elevation readback settle

The v0.1.207 first creation produced:

```text
helper completed successfully
→ immediate parent readback false
→ later readback compliant
→ next-logon startup worked
```

Independent parent verification remains mandatory, but an immediate single read is too strict for the observed Task Scheduler behavior.

After `ElevatedStartupTaskOutcome.Created`, perform a **small bounded read-only verification window**:

```text
for a short bounded duration:
    Read()
    if exact compliant task → Success
    otherwise wait briefly and re-read

window expires
→ Failed
```

Requirements:

- no repeated elevation;
- no repeated `RegisterTaskDefinition` writes from the parent;
- no unbounded retry;
- no background repair worker;
- no new state machine;
- no sleeps on the normal already-compliant path.

Keep the total window short because this is only a one-time/repair transition, not a Runtime hot path.

Use injectable delay/time only if necessary for deterministic focused tests; do not generalize a scheduler framework.

---

# 13. Preserve exact startup-task contract

Do not weaken the task verification introduced by PR10.

The task still must verify at minimum:

```text
TaskName             = Steam Input Addon for Claw
Enabled              = true
ActionPath           = stable Addon executable
ActionArguments      = --background
LogonTriggerUserId   = current interactive user
LogonType             = InteractiveToken
RunLevel              = least privilege / non-elevated
DisallowOnBatteries   = false
StopGoingOnBatteries  = false
ExecutionTimeLimit    = PT0S
```

Normal steady state remains:

```text
compliant task
→ read-only Success
→ no rewrite
→ no UAC
```

Only missing/materially drifted task state invokes the one bounded elevated child.

---

# 14. Tests — cross-mode transition controller

Update/add focused `MsiClawModeController` tests.

At minimum prove:

### Successful real-hardware-shaped transition

```text
source PID1901 present / strong / unique
→ write succeeds
→ source PID disappears
→ exactly one present PID1902 target control logical group appears
→ Succeeded
```

The target may have a different physical root/container representation from the source.

### Old PID still present

```text
PID1902 target appears
but PID1901 remains present through settle deadline
→ NOT Succeeded
→ OldDeviceDidNotDisappear (or equivalent concrete failure)
```

### Ambiguous target

```text
multiple current PID1902 logical groups
→ AmbiguousDevice
```

### Target absent

```text
old PID disappears
but no valid target appears
→ TargetDeviceDidNotAppear / bounded failure
```

Do not add pathological instruction-interleaving tests.

---

# 15. Tests — initial physical acquisition

Update `MsiClawAddonPhysicalOwnershipTests` to encode the real hardware contract.

The existing test that mandates cross-mode `StronglyMatches` failure must be replaced.

Add a real-hardware-shaped fixture where:

```text
PID1901 root:
USB\VID_0DB0&PID_1901\00006F64096B22E7

PID1902 root:
USB\VID_0DB0&PID_1902\5&3AF6909B&0&4
```

and prove:

```text
PID1901 initial capture Strong
→ one successful transition with old PID gone + one target group
→ final PID1902 capture Strong with a DIFFERENT root identity
→ current primary PID1902 DirectInput descriptor matches FINAL PID1902 identity
→ first valid input state
→ HidHide exact target
→ ownership succeeds
```

Also prove:

- already-PID1902 same-mode identity mismatch still fails;
- final mode not PID1902 fails;
- weak final PID1902 identity fails;
- DirectInput descriptor belonging to a different current PID1902 physical identity still fails;
- non-primary collection fails;
- DirectInput missing/ambiguous remains fail-closed;
- transition failure never proceeds to DirectInput/HidHide.

Do not merely delete the old safety test without replacing it with the correct current-topology safety tests.

---

# 16. Tests — PR9 owned PID1901 drift reclaim

Update the recovery tests so they no longer assume the old committed PID1902 physical root must equal the new PID1901 root.

Required cases:

### Real cross-mode drift

```text
owned PID1902 identity/root A
→ DirectInput lost
→ fresh current native capture = PID1901 identity/root B, Strong, one logical supported controller
→ Center M exactly Disabled
→ one PID1901→PID1902 transition succeeds
→ final PID1902 Strong
→ final live DirectInput descriptor matches final PID1902 identity
→ exact target unchanged
→ recovery succeeds
```

### Ambiguous current PID1901

```text
native capture reports multiple logical MSI controllers / Indeterminate
→ no mode write
→ fail closed
```

### Missing/weak/unsupported current state

```text
→ no mode write
```

### Center M no longer Disabled

```text
→ no mode write
```

### Transition failure

```text
→ exactly one attempted reclaim
→ no reverse PID1901 rollback
→ no DirectInput restart
```

### Post-reclaim target changed

Preserve PR10:

```text
RecoveredTargetChanged
→ fail closed
→ no HidHide target migration
```

### Same PID1902 recovery

Keep the existing strong same-mode owned-identity check unchanged.

---

# 17. Tests — Center M Enable release

Add/update tests for the real inverse transition.

Required:

```text
current PID1902 Strong root A
→ transition to PID1901 succeeds
→ PID1902 disappears
→ one PID1901 target appears
→ final capture XInput Strong root B
→ root A != root B is allowed
→ release succeeds
```

Also retain/cover:

```text
already PID1901
→ no mode write
→ success
```

and:

```text
transition write fails
→ release fails

old PID does not disappear
→ release fails

PID1901 target never appears
→ release fails

fresh final capture not XInput / weak / unavailable
→ release fails
```

Assert that no failure reason can become the misleading literal:

```text
Pid1901RestoreUnverified:Ok
```

---

# 18. Tests — startup registration simplification

Update `WindowsTaskSchedulerStartupManagerTests` to match the new product flow.

### Existing compliant task

```text
Read compliant
→ Success
→ RegisterCalls=0
→ ElevatedCalls=0
```

### Missing task in normal production manager

```text
Read missing
→ ElevatedCalls=1
→ parent RegisterCalls=0
→ elevated side leaves compliant task
→ parent bounded readback verifies
→ Success
```

### Drifted task in normal production manager

```text
Read drifted
→ ElevatedCalls=1
→ parent RegisterCalls=0
→ exact repair
→ parent verifies
```

Remove/replace tests whose product expectation is:

```text
missing task registers non-elevated first when allowed
```

for a manager configured with the production elevated invoker.

### Elevated child/direct-write manager

A manager with **no** elevated invoker must still be able to:

```text
Read missing/drifted
→ RegisterCalls=1
→ exact readback
→ Success
```

This represents the already-elevated `--ensure-startup-task` child.

### Bounded readback settle

Test:

```text
helper returns Created
read #1 = missing/drifted
read #2 or later inside bounded window = compliant
→ Success
```

and:

```text
helper returns Created
all reads remain missing/drifted until deadline
→ Failed
```

### Failure cases

Keep:

- UAC cancelled → failure;
- helper failed → failure;
- stable executable missing → failure;
- exact contract mismatch after bounded readback → failure.

No repeated elevation in any one `Synchronize(true)` call.

---

# 19. Integration-level presentation regression

Add or update a focused host/integration test that proves the actual user-visible chain which v0.1.207 failed.

Conceptually:

```text
Center M Disabled
→ physical owner starts from PID1901
→ cross-mode roots differ exactly as real hardware
→ physical ownership succeeds
→ LiveInputSource != null
→ Steam/BPM inactive
→ Xbox360 presentation attaches

then BPM becomes active
→ presentation reconcile runs
→ Xbox360 detaches
→ SteamDeck attaches
→ no physical mode write during presentation change
```

The test does not need to emulate every PnP timing detail already covered by lower-level tests.

Its purpose is to prevent a future regression where physical acquisition failure silently leaves both virtual presentations unavailable.

---

# 20. Logging

Keep diagnostics useful for the next hardware run.

For a successful cross-mode acquisition/reclaim/release, log enough evidence to make the proof visible:

```text
SourceMode
TargetMode
SourceIdentityConfidence
TargetIdentityConfidence
CrossModeIdentityChanged   // diagnostic only
OldPidDisappeared
TargetPidAppeared
TargetLogicalGroupCount
FinalMode
FinalIdentityConfidence
DirectInputPnpInstanceId when applicable
ModeWriteIssued
Result
Reason
```

Change success terminology away from implying root equality, e.g. avoid logging:

```text
SamePhysicalIdentity=true
```

when what was actually proven is a controlled cross-mode transition plus current target topology.

Prefer names such as:

```text
CrossModeTransitionVerified=true
CurrentTargetTopologyVerified=true
```

for the cross-mode path.

Keep `SamePhysicalIdentity=true` only for actual same-mode `StronglyMatches` checks if useful.

For startup repair, the expected first-create log should become approximately:

```text
Startup task synchronization started
Startup task repair required
Startup task elevated repair requested
Startup task elevated repair completed
Startup task readback verified
```

There should be no normal-parent:

```text
Startup task registration was denied. 0x80070005
```

because the parent should no longer make that write attempt.

---

# 21. Explicit non-goals

Do **not** add in this PR:

- a new cross-PID serial-number database;
- a persistent physical-identity map;
- a generalized PnP identity manager;
- a second controller authority owner;
- a new recovery manager/state machine;
- periodic device polling beyond existing bounded transition/settle loops;
- retry queues/channels;
- epochs/generations/barriers;
- support for multiple MSI Claw physical controllers;
- Fast User Switching / RDP / multi-session support;
- HidHide exact-target migration;
- Center M resurrection watcher/process suppression;
- suspend/hibernate/resume redesign;
- Runtime crash watchdog/service;
- broad changes to `ControllerEnvironmentCompatibility`.

In particular, do **not** modify the intentional PR4 behavior where stock compatibility may report `MsiCenterMNotOperational` while Center M is Disabled. Disabled-mode admission deliberately does not use that legacy stock-compatibility result as its authority gate.

Also do not mix the unrelated WinUI shutdown fallback log cleanup into this controller/startup corrective PR. The v0.1.207 `UI dispatcher was unavailable` fallback is not the cause of controller routing failure and can be handled separately if it proves user-visible.

---

# 22. Hardware validation after implementation

Repeat the supported MSI Claw Full1902 path using the next build.

## A. First Disabled boot from stock PID1901

Expected:

```text
Center M Enabled
→ Disable and Restart
→ startup task verified
→ Center M Disabled boot
→ PID1901 detected
→ one PID1901→PID1902 write
→ PID1901 disappears
→ PID1902 appears
→ different cross-mode physical-root representation is accepted
→ live PID1902 DirectInput primary collection acquired
→ first valid input
→ HidHide exact target verified
→ PhysicalOwnership = Owned
```

There must be no:

```text
CrossModeIdentityMismatch
```

for the observed real cross-mode root change.

## B. Default Xbox360 presentation

With Steam/BPM inactive immediately after successful ownership:

```text
Xbox360 attached/live
SteamDeck detached
controller input works
```

## C. Steam/BPM presentation switch

Start BPM or a qualifying Steam session.

Expected:

```text
presentation reconcile requested
→ Xbox360 retire/detach
→ SteamDeck attach/live
→ controller input works
```

There must be no:

```text
no live PR5 input source
```

unless an actual physical input failure occurred.

## D. Return from Steam/BPM

Expected:

```text
SteamDeck → Xbox360
```

with no PID1902 ownership churn.

## E. Enable Center M and Restart

Expected first attempt:

```text
DirectInput retired
→ PID1902→PID1901
→ PID1902 disappears
→ PID1901 appears
→ fresh XInput Strong
→ release succeeds immediately
→ HidHide Addon cleanup
→ Center M roots Enabled
→ reboot
```

There must be no false:

```text
Pid1901RestoreUnverified:Ok
```

and no need for a second Enable attempt.

## F. Startup task first creation/repair

Delete the Addon startup task, then exercise the supported transition that requires it.

Expected:

```text
read-only detects missing task
→ directly request bounded elevation
→ no parent E_ACCESSDENIED write attempt
→ elevated child creates exact task
→ parent bounded readback proves task
→ transition continues
```

## G. Existing task steady state

On the next normal Runtime start:

```text
TaskFound=True
TaskCompliant=True
→ no rewrite
→ no UAC
```

---

# 23. Validation

Run at minimum:

```text
dotnet build -c Debug
dotnet build -c Release
dotnet test -c Release
git diff --check
```

Also run focused tests for:

```text
MsiClawModeController
MsiClawAddonPhysicalOwnership
PR8/PR9/PR10 owned recovery
CenterMRebootAuthorityTransition
WindowsTaskSchedulerStartupManager
startup/elevation configuration
controller presentation host/reconcile
```

The PR is not considered complete solely because synthetic tests pass. The primary bugs were discovered only on real MSI Claw hardware, so the above hardware path must be revalidated before treating Full1902 routing as proven.

---

# 24. Acceptance criteria

PR11 is complete when all of the following are true:

1. A supported MSI Claw may legitimately expose different Windows physical-root/container representations in PID1901 and PID1902 without failing ownership solely for that reason.
2. `MsiClawPhysicalIdentity.StronglyMatches` remains strict for same-mode/current-topology validation.
3. No cross-mode PR5/PR9/Enable path requires PID1901 and PID1902 root-string equality.
4. PID1901→PID1902 success requires a verified current source, successful Addon-issued write, old PID disappearance, one present target logical group, fresh PID1902 state, and live exact DirectInput proof.
5. Stale/non-present PID1902 nodes cannot be selected merely by VID/PID.
6. Initial Disabled boot from PID1901 reaches `PhysicalOwnership=Owned` on the real hardware shape observed in v0.1.207.
7. A live PR5 input source exists after ownership success.
8. Xbox360 becomes the live default presentation when Steam/BPM is inactive.
9. Steam/BPM presentation reconcile can switch to SteamDeck without physical ownership churn.
10. PR9 PID1901 drift reclaim no longer depends on comparing prior PID1902 root identity to current PID1901 root identity.
11. Ambiguous/missing/weak current PID1901 topology still receives no reclaim write.
12. Successful reclaim refreshes the in-memory current PID1902 identity only after the existing exact-target/HidHide/input proof succeeds.
13. `RecoveredTargetChanged` remains fail-closed; this PR does not implement HidHide target migration.
14. Center M Enable succeeds on the first real PID1902→PID1901 transition when current target topology verifies, even if the Windows root representation changes across modes.
15. `Pid1901RestoreUnverified:Ok` can no longer be emitted.
16. A normal production Runtime with a missing/drifted Addon startup task does not first call non-elevated `RegisterTaskDefinition`.
17. Missing/drifted startup task repair directly uses the existing bounded elevated path, then independent normal-process readback verification.
18. A short bounded read-only post-elevation settle handles the observed immediate readback lag without repeated writes/elevation.
19. An already-compliant startup task remains read-only/no-UAC.
20. No new authority manager, recovery manager, cross-PID identity database, polling service, watchdog, epoch, barrier, or unsupported multi-device abstraction is introduced.
21. Full build/test validation passes.
22. Real MSI Claw hardware validates:

```text
Disable + reboot
→ PID1901→PID1902
→ Xbox360 works
→ Steam/BPM → SteamDeck works
→ leave Steam/BPM → Xbox360 works
→ Enable + reboot
→ PID1902→PID1901 succeeds first attempt
```

This is the required correction before moving on to broader Full1902 lifecycle hardening.