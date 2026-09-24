# Work Order — Full1902 PID1902 GamepadMode 2 Invariant Normalization

> Date: 2026-09-22  
> Status: Active Mode2 reference; the app-owned Enter BIOS path is superseded and removed
> Baseline: `main` at `f1747dab271fe7bd021eba6121f3ff840de4b653`  
> Scope: Full1902 physical controller ownership only  
> Out of scope: Enter-BIOS mode selector UI, RB+RT BIOS-entry RE, CTW integration, new controller modes exposed to users

---

## 1. Read these design authorities first

Before implementation, read and preserve the current Full1902 authority/lifecycle contracts in:

- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/PR5_PID1902_DIRECTINPUT_PHYSICAL_OWNERSHIP_WORK_ORDER.md`
- `docs/work-order/PR9_OWNED_PID1901_DRIFT_RECLAIM_WORK_ORDER.md`
- `docs/work-order/PR10_PHYSICAL_DEVICE_LOSS_PNP_RETURN_RECOVERY_WORK_ORDER.md`

Also inspect current production/tests:

- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawGamepadModeClient.cs`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeController.cs`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeContracts.cs`
- `tests/SteamInputAddonforClaw.Tests/MsiClawAddonPhysicalOwnershipTests.cs`
- `tests/SteamInputAddonforClaw.Tests/MsiClawModeSwitchTests.cs`

This is a standalone Full1902 application. Do not add CTW/HHC integration.

---

## 2. Goal

Make the Full1902 physical-owner contract explicit and enforced:

```text
Center M == Disabled
+ Addon physical controller authority
+ physical controller admitted as PID1902
=
firmware GamepadMode must be positively proven as DirectInput / Mode 2
before DirectInput acquisition or recovery may continue.
```

The current code still treats Windows PID topology and firmware GamepadMode as partially interchangeable.

That assumption is now disproven by real hardware:

```text
PID1902 + GamepadMode 2 = normal DirectInput controller
PID1902 + GamepadMode 4 = firmware Desktop / hardware mouse behavior
```

Therefore:

> **PID1902 is necessary but not sufficient proof of the Full1902 physical-controller state.**

The Addon must converge the controller to both:

```text
PID1902
AND
GamepadMode == DirectInput (2)
```

before using it as the physical Full1902 input source.

This rule applies to normal startup and supported owned-input recovery. It is not a BIOS-only repair.

---

## 3. Concrete hardware evidence behind this change

### 3.1 Build 0.1.263.0 / BIOS return

Real hardware logs proved:

```text
Windows topology:
PID1902
NativeState classification = DirectInput

firmware readback:
GamepadMode = Desktop (4)
```

The DirectInput device could be opened, but its state remained invalid:

```text
ButtonCount=128
FirstValidStateNotObserved
```

so physical ownership failed and no X360 presentation could become usable.

PR #571 added the narrow observed repair:

```text
PID1902 + Mode4 → Mode2
PID1902 + Mode5 → Mode2
```

That solved the immediate Windows-return failure, but it still encodes specific historical causes rather than the actual Full1902 invariant.

### 3.2 CTW hardware-proven behavior

Reference only; do not integrate CTW.

Repository/branch reviewed:

```text
onehoon/ClawTweaks-Dev
release/v0.3.98.0
commit 001d8f333683bb2250f5de8e7d0247103b0d69d6
```

Relevant files:

```text
XboxGamingBarHelper/Devices/MSIClaw/MSIClawHidController.cs
XboxGamingBarHelper/Labs/ClawButtonMonitor.cs
```

CTW explicitly documents and implements:

```text
Mode2 / DirectInput  = PID1902
Mode4 / Desktop      = PID1902
```

Its hardware-mouse exit path performs:

```text
Desktop(4) / PID1902
→ SwitchMode(DirectInput=2)
→ ReadGamepadMode
→ require firmware mode to become 2
→ reacquire the already-enumerated DirectInput joystick
```

It explicitly notes that Desktop and DirectInput both remain PID1902, so device enumeration alone cannot distinguish them.

CTW's deterministic readback is the same protocol already implemented in this repository:

```text
TX: 0F 00 00 3C 26 ...
RX: 10 00 00 3C 27 <mode> ...
```

### 3.3 HHC behavior

Reference only; do not integrate HHC.

Repository/commit reviewed:

```text
Valkirie/HandheldCompanion
main = 1d85da30861f700868e48ae8f498a5c455896f7c
```

Relevant file:

```text
HandheldCompanion/Devices/MSI/ClawA1M.cs
```

HHC exposes the same enum:

```text
0 Offline
1 XInput
2 DirectInput
3 MSI
4 Desktop
5 BIOS
6 TESTING
```

and its current default setting is:

```text
MSIClawControllerIndex = 2
```

Mode switching is the same vendor command:

```text
0F 00 00 3C 24 <mode> 00
```

There is no separate hidden Desktop-mode command in HHC; `SwitchToDesktop()` is simply `SwitchMode(GamepadMode.Desktop)`.

This supports keeping the Addon design simple: use the existing GamepadMode protocol client and verify Mode2 directly.

---

## 4. Correct product invariant

While Center M is exactly Disabled:

```text
Desired authority        = Addon
Desired physical PID     = PID1902
Desired firmware mode    = GamepadMode.DirectInput / 2
Desired physical input   = valid DirectInput stream
Desired isolation        = verified Addon HidHide baseline
Desired presentation     = VIIPER X360 or SteamDeck according to existing policy
```

The physical owner must not accept:

```text
PID1902 + unknown/unverified GamepadMode
```

as equivalent to:

```text
PID1902 + verified GamepadMode 2
```.

Do not special-case only BIOS(5) or Desktop(4).

If a present PID1902 controller reports any known value other than Mode2, normalize it to Mode2.

If the initial mode query is unavailable, the owner may perform one bounded normalization attempt to Mode2 through the existing `SwitchAndVerifyAsync` path. Success still requires positive Mode2 readback.

If Mode2 cannot be positively verified, fail closed before DirectInput becomes active.

---

## 5. Keep the two different transition types separate

There are two materially different operations.

### 5.1 Cross-PID native transition

```text
PID1901 / XInput
→ Switch native mode to DirectInput
→ old PID1901 disappears
→ PID1902 target topology appears
```

This remains owned by:

```text
MsiClawModeController
_switchMode(MsiClawNativeMode.DirectInput, ...)
```

The existing bounded PnP/re-enumeration proof remains required.

### 5.2 Same-PID firmware personality normalization

```text
PID1902 + GamepadMode != 2
→ Switch GamepadMode to DirectInput(2)
→ PID stays PID1902
→ ReadGamepadMode must verify 2
```

This remains owned by:

```text
MsiClawGamepadModeClient
SwitchAndVerifyAsync(..., MsiClawGamepadMode.DirectInput, ...)
```

Do **not** route this through `MsiClawModeController`.

That class intentionally verifies cross-PID topology and old-PID disappearance. Those predicates are wrong for a Mode4→Mode2 transition because both firmware personalities can stay on PID1902.

Do not add a second native-mode manager.

---

## 6. Preferred implementation shape

Keep the change centered in `MsiClawAddonPhysicalOwnership`.

Add one small private owner-local helper, conceptually:

```csharp
EnsureDirectInputGamepadModeAsync(
    MsiClawPhysicalIdentity pid1902Identity,
    CancellationToken cancellationToken)
```

Do not create a new manager/interface/state machine for this.

A small private tuple/record local to this owner is sufficient if a return value is needed:

```text
Succeeded
WriteIssued
Reason
ObservedMode
```

Prefer reusing existing `MsiClawGamepadModeQueryResult` / `MsiClawGamepadModeWriteResult` where practical rather than creating a public contract.

---

## 7. Required helper behavior

The helper runs only after a **current strong PID1902 identity** has already been proven.

Conceptual behavior:

```text
require _gamepadModeClient available
→ QueryAsync(current PID1902 identity)

if query succeeds and Mode == DirectInput(2):
    success
    no firmware write

otherwise:
    fresh Center M authority read
    require exactly Disabled

    SwitchAndVerifyAsync(
        current PID1902 identity,
        DirectInput(2))

    require:
        write succeeded
        readback succeeded
        observed mode == DirectInput(2)

    success

any failure:
    fail closed
```

Important details:

1. **Query-unavailable is not permission to continue anymore.**
2. Query-unavailable may trigger one target-Mode2 normalization attempt.
3. There is no indefinite retry loop.
4. `SwitchAndVerifyAsync` already performs the required write + bounded readback.
5. If the write/readback still cannot prove Mode2, stop before DirectInput acquisition/restart.
6. Every real write must still be preceded by a fresh `Center M == Disabled` authority read.
7. Do not change Center M roots, HidHide ownership, or presentation policy here.

---

## 8. Startup acquisition ordering

Refactor `AcquireCoreAsync` so the current narrow early block:

```text
already PID1902
→ query
→ only Mode4/5 restore
```

is removed/replaced by the general invariant.

Preferred startup flow:

```text
capture stable native state

if PID1901:
    fresh authority check
    existing PID1901→PID1902 cross-PID transition
    require existing topology proof

capture final stable native state
require final native mode == DirectInput / PID1902
require strong final PID1902 identity

if startup was already PID1902:
    preserve the existing same-mode strong-identity comparison

NOW:
    EnsureDirectInputGamepadModeAsync(finalPid1902Identity)
    require verified GamepadMode 2

then:
    resolve DirectInput descriptor
    require same strong PID1902 identity
    acquire input
    require first valid state
    reconcile HidHide
    commit ownership
```

The GamepadMode check should therefore use the **final PID1902 identity**, not the pre-transition PID1901 identity.

This matters because the supported hardware can expose a different Windows root/container representation across PID1901→PID1902.

Do not attempt a post-transition Mode2 query using the old PID1901 identity.

---

## 9. Preserve same-mode identity safety

Current startup logic uses the native PID transition fact to decide whether strict same-mode identity equality applies:

```csharp
if (!modeWriteIssued && !initialIdentity.StronglyMatches(finalIdentity))
    fail;
```

Do not accidentally weaken this by reusing one boolean for all firmware writes.

Recommended local facts:

```text
pidTransitionWriteIssued
gamepadModeWriteIssued
anyModeWriteIssued = pidTransitionWriteIssued || gamepadModeWriteIssued
```

Use:

```text
pidTransitionWriteIssued
```

for cross-PID continuity / same-mode identity decisions.

Use:

```text
anyModeWriteIssued
```

for final `MsiClawPhysicalOwnershipResult.ModeWriteIssued` and diagnostics.

This avoids a real regression:

```text
already PID1902
+ identity changes between captures
+ GamepadMode normalization write happens
→ MUST NOT let that write masquerade as a valid cross-PID continuity bridge
```.

---

## 10. Authority-read ordering

Keep the existing first-mutation rule.

### Already PID1902 + Mode2

No firmware write occurs.

Therefore the existing fresh authority read immediately before DirectInput start remains the first-mutation boundary.

### Already PID1902 + non-Mode2 / query unavailable

Before the Mode2 normalization write:

```text
fresh Center M startup state
→ must be exactly Disabled
→ then SwitchAndVerifyAsync(Mode2)
```

That normalization write becomes the first physical mutation.

A second authority read immediately before DirectInput start is not required solely because another line of code follows, provided no existing lifecycle policy already requires it there.

### PID1901 → PID1902

Keep the existing fresh authority check before the cross-PID write.

After the new PID1902 identity is captured, query Mode2.

If the query unexpectedly cannot prove Mode2 and another same-PID Mode2 write is needed, perform another fresh authority read immediately before that additional write.

Do not cache startup authority from process launch.

---

## 11. Recovery must enforce the same invariant

`RecoverLostInputCoreAsync` currently distinguishes:

```text
current PID1902
current PID1901 → one reclaim to PID1902
```

but it does not currently prove GamepadMode 2 before restarting DirectInput.

Add the same helper to the recovery path after the recovery code has established a current strong PID1902 identity.

### Same-PID1902 recovery

Current sequence:

```text
capture PID1902
→ strong-match committed identity
→ descriptor
→ HidHide
→ DirectInput restart
```

Required:

```text
capture PID1902
→ strong-match committed identity
→ Ensure GamepadMode 2
→ descriptor
→ HidHide
→ DirectInput restart
```

If current firmware mode is Desktop(4), MSI(3), BIOS(5), or another known non-2 value:

```text
switch directly to Mode2
→ verify Mode2
→ continue recovery
```

Do not require PID1902 disappearance/reappearance.

### PID1901 drift reclaim

Current sequence:

```text
PID1901
→ existing one-shot native reclaim
→ fresh PID1902 capture
→ descriptor/HidHide/DI recovery tail
```

Required:

```text
PID1901
→ existing one-shot native reclaim
→ fresh strong PID1902 capture
→ Ensure GamepadMode 2 using THAT fresh PID1902 identity
→ descriptor/HidHide/DI recovery tail
```

Usually the query should already report Mode2 because the reclaim command targeted DirectInput. The extra proof is intentional: PID1902 topology is no longer considered sufficient proof of firmware personality.

---

## 12. Preserve recovery reason semantics

Do not let a same-PID GamepadMode normalization rewrite the existing PID-drift classification.

Keep a distinct local fact for:

```text
pidTransitionWriteIssued
```

so success reasons remain:

```text
same PID1902 recovery, even if Mode4→2 normalization occurred:
    Reason = OwnedPhysicalInputRecovered

actual PID1901→PID1902 reclaim:
    Reason = OwnedPhysicalStateDriftReclaimed
```

But `MsiClawPhysicalOwnershipResult.ModeWriteIssued` should truthfully report whether **any** mode write occurred:

```text
PID1902 Mode2 already verified:
    ModeWriteIssued = false

PID1902 Mode4→2:
    ModeWriteIssued = true

PID1901→PID1902:
    ModeWriteIssued = true
```

Do not use that broad result flag as the cross-PID identity bridge.

---

## 13. Query-unavailable behavior changes intentionally

Current test:

```text
Disabled_boot_query_unavailable_preserves_existing_pid1902_acquisition_path
```

encodes the old compromise:

```text
PID1902 + GamepadMode query unavailable
→ continue to DirectInput anyway
```

That behavior must be removed.

The new rule:

```text
PID1902
→ cannot positively prove Mode2
→ one bounded Mode2 normalization + verification attempt is allowed
→ if verified: continue
→ if not verified: fail closed
```

Reason:

Real hardware proved PID1902 may expose a non-gamepad firmware personality. Continuing after an unverified query can recreate the exact invalid-input failure already observed after BIOS return.

This is a real product correctness issue, not theoretical race hardening.

---

## 14. Do not normalize by enum special-case

Do not write logic like:

```csharp
if (mode is Bios or Desktop or MSI)
    switch to DirectInput;
```

Use the invariant:

```csharp
if (mode != MsiClawGamepadMode.DirectInput)
    normalize to DirectInput;
```

The owner does not need to understand why another supported firmware mode is currently active.

This also makes later diagnostic Enter-BIOS mode testing safe to recover from without another production-code PR for each mode value.

Do not expose Offline(0) or TESTING(6) in UI as part of this work order.

If such a known enum value is nevertheless observed while the controller is a strongly proven PID1902 under Addon authority, the same desired-state normalization rule applies.

---

## 15. Do not persist the observed/previous firmware mode

Do not add:

```text
PreviousGamepadMode
LastGamepadMode
DesiredGamepadMode setting
BIOS-return flag
WasDesktop flag
FirmwareModeJournal
```

The authority contract already defines the desired state:

```text
Center M Disabled → Mode2
```

Read current facts and converge them.

There is no need to remember how the controller reached another mode.

---

## 16. Enter BIOS is firmware-owned, not an Addon feature

The Main UI Enter BIOS card, named-pipe RPC, firmware-restart helper, and Addon-owned
Mode5 handoff have been removed. BIOS entry remains the physical firmware RB+RT path and
is outside this Mode2 normalization work order.

Do not add a frontend mode selector or reintroduce an Addon BIOS restart path. This document
continues to define only the normal Windows/Full1902 convergence to verified DirectInput / Mode2.

---

## 17. No change to MsiClawModeController semantics

Do not broaden `MsiClawModeController` to understand:

```text
MSI mode 3
Desktop mode 4
BIOS mode 5
```

Its job remains the XInput/PID1901 ↔ DirectInput/PID1902 native topology transition.

Same-PID firmware personality correction belongs to `MsiClawGamepadModeClient`.

This preserves one clear responsibility per existing primitive and avoids teaching cross-PID topology code about states that deliberately do not re-enumerate.

---

## 18. Logging

Make startup and recovery hardware validation obvious.

Prefer general ownership events rather than BIOS-specific event names.

Example:

```text
ControllerOwnership
Event=GamepadModeInvariantObserved
Context=Startup|Recovery
Succeeded=true|false
ObservedMode=DirectInput|Desktop|MSI|Bios|...
Reason=...

ControllerOwnership
Event=GamepadModeNormalizationStarted
Context=Startup|Recovery
TargetMode=DirectInput

ControllerOwnership
Event=GamepadModeNormalizationVerified
Context=Startup|Recovery
ObservedMode=DirectInput
WriteIssued=true
ReadbackVerified=true
```

Existing low-level logs from `MsiClawGamepadModeClient` remain valuable:

```text
GamepadModeQueryCompleted
GamepadModeWriteCompleted
GamepadModeVerified
```

Do not add polling/log timers.

The old boot-only names:

```text
DisabledBootGamepadModeObserved
DisabledBootGamepadModeRestoreStarted
DisabledBootGamepadModeRestoreVerified
```

may be renamed to general invariant/normalization names if that keeps startup and recovery on one code path.

Avoid keeping two separate logging vocabularies for the same operation.

---

## 19. Failure policy

If Mode2 cannot be proven:

```text
do not start/restart DirectInput
do not attach/change virtual presentation here
do not roll back to PID1901
do not clear persistent HidHide authority
do not enable Center M
do not retry forever
```

Return a stable failure reason such as:

```text
GamepadModeDirectInputNotVerified:<reason>
AuthorityChangedBeforeGamepadModeNormalization
GamepadModeClientUnavailable
```

Use one naming family for startup and recovery rather than BIOS-specific names.

Existing higher-level fail-close behavior then keeps virtual output neutral/unusable rather than forwarding an invalid physical stream.

---

## 20. Tests — startup

Update/add focused tests in:

```text
tests/SteamInputAddonforClaw.Tests/MsiClawAddonPhysicalOwnershipTests.cs
```

### 20.1 Already PID1902 + Mode2

Given:

```text
Center M Disabled
PID1902
GamepadMode query = DirectInput(2)
```

expect:

```text
zero GamepadMode write
zero cross-PID write
normal DirectInput acquire
ModeWriteIssued=false
```

Preserve the existing basic `Already_pid1902_acquires_without_a_mode_write` meaning, now with explicit GamepadMode proof.

### 20.2 PID1902 + Desktop(4)

Expect:

```text
query 4
→ one SwitchAndVerify(2)
→ DirectInput starts only after verification
→ ModeWriteIssued=true
```

This is the PR571 hardware regression test.

### 20.3 PID1902 + BIOS(5)

Preserve regression coverage:

```text
query 5
→ one SwitchAndVerify(2)
→ acquire
```

### 20.4 PID1902 + MSI(3)

Add this test specifically to prove the implementation is invariant-based rather than another `4 or 5` special case.

Expect:

```text
query 3
→ one SwitchAndVerify(2)
→ acquire
```

### 20.5 Query unavailable, normalization succeeds

Given:

```text
initial QueryAsync unavailable
SwitchAndVerifyAsync(2) succeeds with Mode2 readback
```

expect ownership succeeds.

This replaces the old query-unavailable passthrough behavior.

### 20.6 Query unavailable, normalization cannot verify

Expect:

```text
Failed
no DirectInput start
no HidHide target mutation
one bounded Mode2 normalization attempt only
```

### 20.7 Authority changes before same-PID normalization write

Given:

```text
PID1902
observed non-2 mode
fresh Center M state != Disabled
```

expect:

```text
zero GamepadMode write
zero DirectInput start
Failed
```

### 20.8 Same-PID identity mismatch is still rejected

Ensure an already-PID1902 identity mismatch between the initial and final native captures still fails as `SameModeIdentityMismatch`.

A GamepadMode normalization write must not bypass this check.

---

## 21. Tests — PID1901 startup transition

### 21.1 Normal PID1901→PID1902

After the existing successful cross-PID transition and final PID1902 capture:

```text
GamepadMode query must occur using the final PID1902 identity
Mode2 verified
then DirectInput resolve/acquire
```

No extra firmware write is expected when query returns Mode2.

### 21.2 Final PID1902 reports non-2 unexpectedly

Simulate:

```text
PID1901→PID1902 topology transition succeeds
final GamepadMode query = non-2
```

expect:

```text
fresh authority check
→ same-PID SwitchAndVerify(2)
→ verified
→ continue
```

This test proves the topology transition alone is not treated as firmware-personality proof.

### 21.3 Final identity is used for GamepadMode command resolution

Extend the fake if needed so the test can assert that the identity passed to the GamepadMode client is the fresh PID1902 identity, not the old PID1901 identity.

Do not weaken the existing allowed cross-PID root-change behavior.

---

## 22. Tests — owned recovery

### 22.1 Same PID1902 + Mode2

Preserve:

```text
Reason = OwnedPhysicalInputRecovered
ModeWriteIssued=false
same input source restarted
```

### 22.2 Same PID1902 + Mode4

After session loss:

```text
capture same PID1902 identity
→ query Mode4
→ normalize to Mode2
→ verify
→ resolve exact same target
→ HidHide
→ restart same input source
```

Expect:

```text
Reason = OwnedPhysicalInputRecovered
ModeWriteIssued=true
```

### 22.3 Same PID1902 + Mode3

Same expectations as Mode4.

This proves recovery is not BIOS/Desktop special-cased.

### 22.4 Same PID1902 normalization failure

Expect:

```text
Failed
no HidHide repair
no InputStart
existing owned identity/target retained for later explicit release
```

### 22.5 PID1901 drift reclaim followed by Mode2 proof

Existing PR9 reclaim still occurs once.

Then:

```text
fresh PID1902 identity
→ query firmware Mode
→ require Mode2
```

before descriptor/HidHide/restart.

### 22.6 PID1901 reclaim followed by unexpected non-2 mode

If the fresh PID1902 reports non-2:

```text
normalize same PID1902 to Mode2
→ verify
→ continue
```

Success reason remains:

```text
OwnedPhysicalStateDriftReclaimed
```

because an actual PID1901→PID1902 reclaim occurred.

### 22.7 Query/readback failure after reclaim

Fail closed before the recovery tail.

No rollback to PID1901.

---

## 23. Update test harness semantics carefully

The existing `Harness.GamepadMode` already defaults to Mode2 and is injected into the physical owner.

Extend it minimally to support:

- query call count;
- identities passed to Query/SwitchAndVerify if needed;
- separate startup/recovery observed mode when required;
- query-unavailable then successful verification;
- normalization write failure/readback mismatch.

Do not create a second fake framework.

Keep existing event-order assertions useful.

Where recovery tests currently expect:

```text
NativeCapture
DescriptorResolve
HidHideApply
InputStart
FirstValidState
```

include the GamepadMode invariant observation at the correct point only if the harness records that event. Do not make tests brittle around incidental log text.

---

## 24. Manual hardware validation

Run on supported MSI Claw after automated tests pass.

### 24.1 Normal steady-state boot

Starting state:

```text
Center M Disabled
PID1902
GamepadMode 2
```

verify:

```text
GamepadMode query = 2
no GamepadMode write
no PID re-enumeration caused by the Addon
DirectInput first valid state
X360/SteamDeck presentation behaves normally
```

### 24.2 Known Desktop recovery

Use the already-observed BIOS-return/Desktop state or another controlled existing method.

Starting state:

```text
PID1902
GamepadMode 4
mouse/keyboard-like physical behavior
```

verify:

```text
Addon observes Mode4
→ writes Mode2 directly
→ readback Mode2
→ PID remains PID1902
→ DirectInput first valid state succeeds
→ virtual X360 becomes usable
```

Do not require PID1902 disappear/appear during this repair.

### 24.3 Restart after repaired state

On the next normal boot verify:

```text
Mode2 is observed
no unnecessary mode write
```

### 24.4 Recovery smoke test

If practical, exercise one real supported DirectInput loss/PnP return or sleep/resume path.

Verify that a returned PID1902 is not accepted until Mode2 is proven.

Do not invent artificial scheduler-race tests.

---

## 25. Expected files

Primary production change:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs
```

Focused tests:

```text
tests/SteamInputAddonforClaw.Tests/MsiClawAddonPhysicalOwnershipTests.cs
```

Potential tiny supporting changes only if actually required:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeContracts.cs
tests/SteamInputAddonforClaw.Tests/MsiClawModeSwitchTests.cs
```

Prefer **no changes** to:

```text
MsiClawModeController.cs
frontend contracts
named-pipe protocol
Settings UI
VIIPER/presentation policy
HidHide policy
```

unless compilation proves a tiny mechanical update unavoidable.

---

## 26. Explicit non-goals

Do not include:

- Enter-BIOS mode dropdown;
- Mode3 BIOS hypothesis testing;
- RB+RT BIOS reverse engineering;
- CTW/HHC runtime integration;
- a generic firmware-mode manager;
- persistent desired-mode settings;
- firmware-mode journal/rollback;
- background GamepadMode polling;
- timer/watchdog;
- a new authority/state machine;
- mode-change retry queues;
- PID1902→PID1901 fallback on normalization failure;
- broad support for multiple interactive sessions/users;
- theoretical race hardening beyond existing owner gate/fresh authority reads.

---

## 27. Overengineering/race policy

Protect realistic supported lifecycle failures:

- boot already in PID1902 but wrong firmware personality;
- PID1901→PID1902 transition;
- DirectInput loss/recovery;
- PnP return;
- sleep/resume-related reappearance;
- authority changing before a real write;
- GamepadMode I/O/readback failure;
- exact physical identity mismatch;
- explicit Center M release overlapping recovery through the existing owner gate.

Do not add synchronization for arbitrary instruction-level interleavings.

The existing:

```text
MsiClawAddonPhysicalOwnership._gate
fresh authority reads
strong physical identity
bounded native capture
bounded GamepadMode readback
fail-close recovery
```

are the intended building blocks.

---

## 28. Acceptance criteria

This work is complete when all of the following are true:

1. Under exact Center M Disabled authority, a PID1902 controller is not accepted as Full1902 physical input until firmware GamepadMode 2 is positively proven.
2. Already PID1902 + Mode2 performs no GamepadMode write.
3. Already PID1902 + any observed known non-2 GamepadMode is normalized directly to Mode2 through `MsiClawGamepadModeClient`.
4. Query-unavailable no longer silently passes through to DirectInput.
5. Query-unavailable gets at most one bounded Mode2 normalization/verification attempt.
6. A Mode2 normalization write is preceded by a fresh Center M Disabled authority read.
7. Failure to verify Mode2 blocks DirectInput start/restart.
8. Same-PID Mode4→Mode2 does not require PID1902 PnP re-enumeration.
9. `MsiClawModeController` remains responsible only for native PID1901↔PID1902 topology transition.
10. PID1901→PID1902 startup captures a fresh final PID1902 identity before GamepadMode verification.
11. Cross-PID root-change behavior from PR11 remains valid.
12. A same-PID identity mismatch cannot be hidden by a GamepadMode write.
13. Owned same-PID1902 recovery verifies/normalizes Mode2 before descriptor/HidHide/DirectInput restart.
14. PID1901 drift reclaim verifies Mode2 on the fresh PID1902 state before continuing recovery.
15. Same-PID normalization success keeps recovery reason `OwnedPhysicalInputRecovered`.
16. Actual PID1901 reclaim keeps recovery reason `OwnedPhysicalStateDriftReclaimed`.
17. `ModeWriteIssued` truthfully reports same-PID GamepadMode writes as well as cross-PID mode writes, without being reused as the cross-PID identity proof.
18. Existing exact HidHide target/identity rules remain unchanged.
19. Failure never automatically restores PID1901 while Center M remains Disabled.
20. No new polling, watchdog, authority manager, persisted firmware-mode state, or generalized state machine is introduced.
21. Debug and Release builds pass.
22. Full automated test suite passes.

---

## 29. Final implementation principle

The previous implementation asked:

```text
Is this PID1902, and is it one of the two wrong modes we already happened to observe?
```

The corrected Full1902 owner asks only:

```text
Do I currently have the strongly identified physical MSI Claw on PID1902,
and can I positively prove its firmware GamepadMode is the one state
this authority owns: DirectInput / Mode2?
```

If yes, continue.

If not, converge once to Mode2 and verify.

If that cannot be proven, fail closed.

That is the complete scope of this PR.
