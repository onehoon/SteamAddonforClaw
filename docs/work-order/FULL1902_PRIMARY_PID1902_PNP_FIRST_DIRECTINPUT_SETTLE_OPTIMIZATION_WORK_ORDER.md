# Work Order — Full1902 Primary PID1902 PnP-First DirectInput Settle Optimization

**Date:** 2026-09-16  
**Scope:** Narrow Full1902 physical-input settle optimization after PID1901 → PID1902 transitions and during owned physical-input recovery  
**Product:** Standalone Steam Addon for Claw / Full PID1902 architecture  
**Risk posture:** Preserve current lifecycle safety and authority semantics. This is an optimization of the bounded DirectInput settle tail, not a new controller authority or recovery design.

---

## 1. Read these authorities first

Before implementation, read and follow the current Full1902 authority order:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`

Also inspect the current implementation and tests, especially:

- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeController.cs`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawNativeStateManager.cs`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawHardware.cs`
- `src/SteamInputAddonforClaw/Input/DirectInput/VorticeDirectInputDeviceEnumerator.cs`
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`
- `tests/SteamInputAddonforClaw.Tests/MsiClawAddonPhysicalOwnershipTests.cs`
- `tests/SteamInputAddonforClaw.Tests/MsiClawModeSwitchTests.cs`

Historical PR5/PR8/PR9/PR10/PR11 work orders are useful implementation context, but current Full1902 policy documents and current code are authoritative where they differ.

CTW / Handheld Companion are reference evidence only. They are not product authorities and their controller lifecycle must not be copied wholesale.

---

## 2. Current-code finding that changes the original optimization idea

Do **not** blindly copy ClawTweaks' current "HID-first, then DirectInput" implementation.

The current Addon already performs an earlier, strong bounded PnP settle inside `MsiClawModeController.SwitchModeAsync(...)`.

For a PID1901 → PID1902 transition it already waits for all of the following before returning success:

```text
mode command write succeeded
old PID1901 disappeared
PID1902 appeared
a single PID1902 target control-HID logical group exists
target topology verified
source identity verified
```

That loop uses the existing PnP/controller enumerator with a ~75 ms poll interval and a bounded timeout.

`MsiClawAddonPhysicalOwnership` then performs another stable native-state capture and proves final PID1902 before it reaches DirectInput descriptor resolution.

Therefore this PR must **not** add a second generic "wait for PID1902" phase. Doing that would be redundant and could increase startup latency.

The remaining optimization target is narrower:

> The mode controller waits for the PID1902 **control HID** required to prove the native-mode transition. The DirectInput path later needs the exact primary PID1902 gamepad collection (`MI_00&COL01`). There can still be a short re-enumeration tail where the native PID1902 transition is proven but the primary gamepad collection / DirectInput registration is not ready.

That tail is currently handled by repeatedly calling `ResolveDirectInputDescriptorAsync(...)`.

---

## 3. Current DirectInput settle behavior

`MsiClawAddonPhysicalOwnership.ResolveDirectInputDescriptorAsync(...)` currently uses:

```text
settle window  = 3 seconds
settle interval = 150 ms
```

Within that bounded window it repeatedly runs:

```text
_enumerateDirectInputDevices()
→ MsiClawDirectInputDeviceSelector.Select(...)
```

Production composition in `AddonProcessHost` implements `_enumerateDirectInputDevices()` by creating and disposing a fresh:

```text
VorticeDirectInputDeviceEnumerator(IntPtr.Zero)
```

for each attempt.

Each Vortice enumerator constructs `IDirectInput8` via `DInput.DirectInput8Create()`. For an MSI PID1902 device it may also create an inspection DirectInput device, read InterfacePath/capabilities, and resolve PnP topology.

This is valid current behavior and no Addon crash/race has been demonstrated from it.

Do **not** claim that the Addon has the SharpDX heap-corruption defect observed in ClawTweaks. The Addon uses Vortice and has no equivalent production evidence.

The reason for this change is narrower:

```text
avoid repeatedly constructing the heavier DirectInput inspection path
while the exact primary PID1902 gamepad collection is not yet present in PnP
```

This is a simplification/performance/lifecycle-cleanliness improvement, not a bug fix for a proven Addon crash.

---

## 4. Goal

Keep all existing Full1902 ownership and fail-close guarantees while reducing unnecessary DirectInput-native enumeration during real PID/PnP settle windows.

Desired behavior on selected paths:

```text
PID1901 → PID1902 transition already proven by MsiClawModeController
    ↓
cheap exact primary PID1902 PnP collection readiness probe
    ↓
MI_00&COL01 absent
    → wait inside the EXISTING DirectInput settle budget
    → do not construct Vortice DirectInput repeatedly yet

MI_00&COL01 present
    ↓
run the EXISTING DirectInput descriptor selector
    ↓
retain all existing identity/topology/button-count validation
    ↓
if DirectInput itself is still not ready
    → keep the EXISTING bounded DirectInput retry behavior
```

The optimization is also useful during owned recovery because PnP loss/return or PID1901 drift can leave the native PID1902 topology visible slightly before the DirectInput gamepad surface is usable.

---

## 5. Non-negotiable safety rules

### 5.1 The PnP probe is only a readiness hint

The cheap primary-collection probe must never become authority or identity proof.

It must not replace:

- strong native physical identity verification;
- `MsiClawModeController` cross-mode transition proof;
- final PID1902 stable capture;
- `MsiClawDirectInputDeviceSelector`;
- exact primary collection verification;
- DirectInput PnP topology resolution;
- final `StronglyMatches(...)` checks;
- first valid input-state verification;
- HidHide exact-target verification.

A PnP probe returning `true` means only:

```text
it is now reasonable to spend a DirectInput enumeration attempt
```

Nothing more.

### 5.2 Do not add another timeout budget

Do not implement:

```text
3 seconds PnP wait
+ 3 seconds DirectInput wait
```

Use one existing `_directInputSettleWindow` deadline across both phases.

The maximum settle latency must not increase because of this PR.

### 5.3 Preserve the existing DirectInput retry after PnP readiness

PnP presence does not prove DirectInput registration is ready.

After the primary collection appears, the current selector may still legitimately return:

```text
NotFound
Indeterminate / PhysicalIdentityUnverified
```

Keep retrying those states inside the remaining existing deadline exactly as today.

Proven-invalid topology must still fail immediately.

### 5.4 Probe failure must not create a new product failure mode

This is an optimization probe, not required controller authority.

If the new PnP readiness probe throws or cannot be evaluated:

```text
log once
fall back to the current DirectInput settle behavior
```

Do not fail ownership solely because the optimization probe failed while the existing DirectInput path could still succeed.

### 5.5 Preserve one final DirectInput chance

If the PnP readiness hint remains false until the shared deadline, do not convert that hint into a new authoritative failure without checking the existing path.

At minimum, allow one final existing DirectInput selection attempt before returning the same existing `DirectInputNotResolved` failure.

This keeps the optimization non-authoritative and avoids a regression if Windows' PnP observation and DirectInput observation momentarily disagree.

---

## 6. Scope: where to use the PnP-first hint

### 6.1 Initial startup, already PID1902

Current contract:

```text
initial mode PID1902
→ ModeWriteIssued=false
→ no PID1901 appearance
→ DirectInput acquire
```

Do **not** add a new mandatory wait to this normal stable path.

The existing DirectInput resolver should remain the normal authority for an already-PID1902 boot.

This preserves the key Full1902 fast path and avoids turning an optimization for re-enumeration into a regression on every normal startup.

### 6.2 Initial startup, PID1901 → PID1902

Enable the PnP-first optimization after:

```text
MsiClawModeController transition succeeded
+ final stable native capture proves PID1902
```

and before the first expensive DirectInput enumeration attempt.

This is the primary target of the change.

### 6.3 Owned recovery, same-mode PID1902

Enable the optimization for `RecoverLostInputAsync(...)`.

If the exact primary PnP collection is already present, the probe returns immediately and current behavior continues with effectively no added wait.

If the physical device is still finishing PnP return, avoid repeated Vortice construction until the exact primary collection appears.

### 6.4 Owned recovery, PID1901 drift reclaim

After the existing single controlled PID1901 → PID1902 reclaim and post-reclaim stable PID1902 proof, use the same PnP-first optimization before DirectInput descriptor resolution.

Do not add another mode-write retry loop.

---

## 7. Suggested implementation shape

Keep the change local to the existing physical owner and production composition.

A narrow readiness delegate is sufficient, for example conceptually:

```csharp
Func<bool> isPrimaryPid1902CollectionPresent
```

Production composition may implement it with the existing `IControllerDeviceEnumerator`, using the already-defined MSI hardware contract:

```text
VID = 0x0DB0
PID = 0x1902
exact primary DirectInput collection = MI_00&COL01
```

Reuse:

```csharp
MsiClawHardware.IsDirectInputHidCollection(...)
MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId(...)
```

Do not introduce HidSharp into the Addon for this optimization.

Do not add a new controller-enumerator wrapper/interface solely for this feature if the existing production `controllerDevices` instance can provide the narrow delegate.

A possible owner API shape is:

```csharp
ResolveDirectInputDescriptorAsync(
    CancellationToken cancellationToken,
    bool preferPrimaryPnpFirst)
```

Exact naming may differ.

The implementation should remain one method / one deadline rather than a new manager or state machine.

---

## 8. Required settle algorithm

Conceptually:

```text
ResolveDirectInputDescriptorAsync(token, preferPrimaryPnpFirst)

create ONE absolute deadline from _directInputSettleWindow

if preferPrimaryPnpFirst:
    try cheap primary-PID1902 PnP probe

    while primary collection absent and deadline remains:
        delay _directInputSettleInterval
        probe again

    if probe throws:
        disable the optimization for this invocation
        continue with existing DirectInput selector loop

    if deadline expires before primary collection is observed:
        perform one final existing DirectInput selection attempt
        return success if it succeeds
        otherwise preserve existing failure behavior

run existing DirectInput selector loop using REMAINING time from the SAME deadline

Success:
    return selected descriptor

Retryable:
    NotFound
    Indeterminate + PhysicalIdentityUnverified

Non-retryable:
    preserve current immediate fail-close behavior
```

Do not sleep after the deadline merely to preserve an interval count.

Cancellation must remain responsive through the existing cancellation token.

---

## 9. Do not change these contracts

This PR must not change any of the following:

- Center M startup roots as controller authority;
- reboot-bound authority transitions;
- persistent PID1902 desired state;
- already-PID1902 startup behavior;
- no PID1902 → PID1901 → PID1902 normalization;
- `MsiClawModeController` transition rules;
- mode-write timeout/poll policy;
- strong physical identity semantics;
- PR11 cross-mode continuity rules;
- persistent HidHide baseline policy;
- exact owned hidden-target policy;
- changed-target PR10 fail-close behavior;
- VIIPER ownership or attach ordering;
- X360 / SteamDeck presentation policy;
- rumble behavior;
- M1/M2 behavior;
- gyro behavior;
- suspend/resume authority policy;
- device-arrival watcher architecture;
- runtime recovery authority.

Do not add:

```text
PnPReadyState
DirectInputEpoch
RecoveryEpoch
SettleManager
ControllerReadinessManager
new background polling thread
new long-lived timer
new PnP watcher
new retry state machine
```

The current owner/gate/recovery structure is sufficient.

---

## 10. Logging and timing evidence

Add focused timing evidence so hardware validation can prove whether this optimization actually buys anything.

Do not add a high-volume per-150-ms Info log.

Prefer a small set of structured Debug/Info events around the existing trace points, conceptually:

```text
PrimaryPid1902CollectionWaitStarted
PrimaryPid1902CollectionReady
PrimaryPid1902CollectionProbeFallback
DirectInputResolveCompleted
```

Useful fields:

```text
Context = StartupTransition | RecoverySameMode | RecoveryPidDrift
ElapsedMs
PnpWaitMs
DirectInputAttempts
DirectInputResolveMs
ModeWriteIssued
ProbeFallbackUsed
FinalReason
```

The current `MsiClawModeController` already logs native-mode command and PID1902 appearance timing. Do not duplicate those logs.

The useful new distinction is:

```text
PID1902 native/control topology ready
    → exact primary MI_00&COL01 ready
        → DirectInput descriptor ready
```

---

## 11. Automated tests

Extend the existing focused tests. Do not create a large new test framework.

At minimum cover:

### A. Stable already-PID1902 startup remains unchanged

```text
initial mode PID1902
ModeWriteIssued=false
PnP-first optimization is not required for normal startup
DirectInput succeeds
no extra mode write
no PID1901 transition
```

### B. PID1901 → PID1902 waits cheaply before DirectInput

Sequence:

```text
mode transition succeeds
primary PnP collection: false, false, true
DirectInput descriptor: success
```

Verify:

```text
one mode write only
DirectInput enumeration is not repeatedly called while primary PnP is absent
ownership succeeds
```

### C. Primary PnP appears, DirectInput is still late

Sequence:

```text
primary PnP = true
DirectInput = NotFound, NotFound, success
```

Verify the existing DirectInput retry behavior remains intact.

### D. Probe throws

The readiness delegate throws once.

Verify:

```text
no ownership failure solely from the optimization probe
fallback to current DirectInput settle logic
successful DirectInput still succeeds
```

### E. PnP hint never becomes ready

Verify:

```text
same shared deadline
one final existing DirectInput chance
no HidHide mutation on true failure
no virtual attach
no PID1901 rollback
```

### F. Proven-invalid DirectInput topology still fails immediately

Ambiguous/multiple physical identities or another current non-retryable selector result must not be converted into a wait/retry by the new PnP hint.

### G. Recovery, same-mode PID1902

If the primary collection is already present, recovery proceeds without an artificial delay.

If it is temporarily absent, the cheap wait happens before repeated Vortice enumeration.

### H. Recovery, PID1901 drift reclaim

Verify:

```text
exactly one PID1901 → PID1902 reclaim write
post-reclaim stable PID1902 proof
PnP-first DirectInput settle
same committed hidden target contract
successful recovery
```

### I. Cancellation

Cancellation during the PnP-first wait must exit promptly and must not continue into DirectInput/HidHide mutation.

---

## 12. Manual hardware validation

Hardware validation is required because the expected benefit is timing/lifecycle cleanliness, not a currently reproducible Addon correctness bug.

### 12.1 Normal Full1902 cold boots already in PID1902

Run multiple normal cold boots where the Claw starts/remains PID1902.

Confirm:

```text
ModeWriteIssued=false
no PID1901 appearance caused by Addon startup
no new PnP-first delay on the normal path
X360/SteamDeck presentation behavior unchanged
rumble unchanged
```

There must be no measurable startup regression attributable to this PR.

### 12.2 Real PID1901 → PID1902 startup/reclaim

Capture at least several real transition runs when available.

Compare:

```text
native command written
PID1902 first seen (existing trace)
primary MI_00&COL01 ready (new trace)
DirectInput descriptor ready
number of Vortice/DirectInput enumeration attempts
physical ownership ready
```

The expected result is fewer expensive DirectInput attempts during the PnP tail, not necessarily a large wall-clock reduction.

### 12.3 PnP loss/re-arrival / resume where practical

Validate real supported lifecycle cases:

- physical device loss and re-arrival;
- resume after sleep/hibernate when the device re-enumerates;
- owned PID1901 drift recovery if reproducible.

Confirm final convergence remains:

```text
Center M Disabled
→ same supported physical Claw
→ PID1902
→ DirectInput live
→ exact HidHide target verified
→ one current desired virtual presentation
```

---

## 13. Risk assessment

### Low risk if implemented exactly as this work order

Reasons:

- no authority policy changes;
- no new mode writes;
- no change to already-PID1902 normal startup;
- no new timeout budget;
- PnP readiness is non-authoritative;
- probe failure falls back to the current implementation;
- existing DirectInput selector and identity validation remain authoritative;
- existing recovery owner/gate remains unchanged;
- no new background thread/watcher/state machine.

### Real regression risks to avoid

#### Risk 1 — duplicated settle latency

If implementation adds a separate PnP timeout before the existing DirectInput timeout, startup/recovery can become slower by seconds.

**Mitigation:** one shared absolute deadline only.

#### Risk 2 — treating PnP presence as DirectInput readiness

The exact HID collection can be present before DirectInput is usable.

**Mitigation:** never remove the current DirectInput retry loop after the collection appears.

#### Risk 3 — treating the cheap probe as identity authority

A boolean presence probe cannot prove the owned physical device.

**Mitigation:** use it only to decide whether an expensive DirectInput attempt is worthwhile. Keep all current final identity/topology checks.

#### Risk 4 — introducing a new failure dependency

A new PnP enumeration call can itself fail.

**Mitigation:** log once and fall back to the existing DirectInput settle path.

#### Risk 5 — slowing the normal PID1902 boot

Applying a new wait to every startup would attack a problem that primarily exists during re-enumeration.

**Mitigation:** do not require the PnP-first phase for the already-PID1902 normal startup path.

---

## 14. Acceptance criteria

The PR is complete only when all of the following hold:

```text
[ ] current Full1902 authority semantics unchanged
[ ] already-PID1902 startup issues zero new mode writes and gains no mandatory wait
[ ] PID1901→PID1902 still uses exactly one controlled transition
[ ] existing MsiClawModeController PnP settle is not duplicated/reimplemented
[ ] exact primary MI_00&COL01 readiness can suppress wasted DirectInput attempts during re-enumeration
[ ] PnP readiness never replaces DirectInput/identity/topology validation
[ ] PnP probe failure falls back to existing behavior
[ ] one shared DirectInput settle deadline is preserved
[ ] DirectInput retry remains after PnP presence
[ ] non-retryable DirectInput selection still fails immediately
[ ] recovery same-mode and PID1901-drift paths remain safe
[ ] no new manager/state machine/watcher/thread is introduced
[ ] focused timing evidence is present
[ ] focused unit tests pass
[ ] full test suite passes
[ ] hardware validation shows no stable-PID1902 startup regression
```

---

## 15. Explicit non-goals

Do not use this PR to implement or investigate:

- CTW's PID1902 → PID1901 → PID1902 rumble-arm round-trip;
- CTW's PID1901 rumble endpoint fallback;
- VIIPER architecture changes;
- scheduled-task priority changes;
- firmware M1/M2/SyncToROM timing;
- new PnP event infrastructure;
- generalized controller lifecycle refactoring;
- speculative race hardening.

The product requirement remains simple:

> Preserve one clear Full1902 owner and the existing fail-close lifecycle, while avoiding unnecessary expensive DirectInput probing during a real, bounded PnP re-enumeration tail.
