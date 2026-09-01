# Work Order — PR4: Disabled-Boot Controller Admission

## Status

Implementation work order for the next Full PID1902 PR after the reboot-bound authority transition:

```text
PR1   Persistent Dual VIIPER Devices Foundation                 [merged]
  ↓
PR2   Addon-Owned Persistent HidHide Baseline Foundation        [merged]
  ↓
PR2.5 Mandatory Controller Runtime Lifetime Foundation           [merged]
  ↓
PR3   Reboot-Bound Controller Authority Transition               [merged]
  ↓
PR4   Disabled-Boot Controller Admission                         [this PR]
  ↓
PR5   PID1902 + DirectInput Ownership                            [next]
  ↓
PR6   First Virtual Presentation Attach
  ↓
PR7   Runtime Xbox360 ↔ SteamDeck Presentation Switching
  ↓
PR8+  Owned-state recovery / lifecycle hardening / cleanup
```

Current `main` baseline when this work order was prepared:

```text
6640df3739bf01ebcfb26dbf5f95f3f86484cf07
Add reboot-bound MSI Center M controller-authority transition (PR3) (#434)
```

### Numbering note

The Full 1902 design documents were written before PR2.5 was inserted into the implementation sequence. They therefore describe:

```text
old PR5 = Disabled-boot admission
old PR6 = PID1902 + DirectInput ownership
```

The current sequence is authoritative:

```text
PR3 = reboot-bound authority transition
PR4 = Disabled-boot admission
PR5 = PID1902 + DirectInput ownership
```

Do not create another intermediate PR merely to preserve the older numbering.

Before implementation, read and treat the following as design authorities:

- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/work-order/PR2_ADDON_OWNED_HIDHIDE_BASELINE_WORK_ORDER.md`
- `docs/work-order/PR2_5_MANDATORY_CONTROLLER_RUNTIME_LIFETIME_WORK_ORDER.md`
- `docs/work-order/PR3_REBOOT_BOUND_CONTROLLER_AUTHORITY_TRANSITION_WORK_ORDER.md`
- current `main` implementation of:
  - `Startup/StartupCoordinator.cs`
  - `Startup/AddonStartupComposition.cs`
  - `Startup/StockCenterMStartupBaseline.cs`
  - `Startup/ControllerEnvironmentWaiter.cs`
  - `CenterMStartup/CenterMStartupControl.cs`
  - `CenterMStartup/CenterMRebootAuthorityTransition.cs`
  - `HidHide/AddonControllerHidHideBaseline.cs`
  - `HidHide/HidHideDriverClient.cs`
  - `Prerequisites/PrerequisiteContracts.cs`
  - `Prerequisites/RuntimePrerequisiteInspector.cs` and its current sub-inspectors
  - `Recovery/RecoveryManager.cs`
  - `Recovery/StartupHidHideRecoveryCleaner.cs`
  - `Runtime/AddonRuntimeComposition.cs`
  - `Runtime/AddonRuntimeHost.cs`
  - `Hosting/AddonProcessHost.cs`

The project is pre-release. Do not add compatibility wrappers for the old Steam-session physical-ownership model.

---

## 1. Goal

Make Windows startup **authority-aware** after PR3.

PR3 established the persistent next-boot authority intent:

```text
Center M startup roots exactly Enabled
    → MSI / stock authority intent

Center M startup roots exactly Disabled
    → Addon authority intent
```

PR4 must make startup respect that fact before any controller-mutating legacy startup logic can run.

The new Disabled-boot contract is:

```text
Windows logon
    ↓
Addon Runtime starts
    ↓
verify supported MSI Claw
    ↓
read actual Center M startup roots
    ↓
roots == Disabled
    ↓
DO NOT establish Stock Center M / PID1901 baseline
DO NOT run old route-scoped startup HidHide cleanup
DO NOT start old Steam-session physical routing
    ↓
run read-only Disabled-boot admission
    ↓
Ready or Blocked
```

PR4 is a **facts/gate PR only** for Addon-owned controller startup.

It must not perform the new physical controller takeover.

The one-line invariant is:

> **When Center M is Disabled, startup may inspect and classify the current controller environment, but it must not issue a physical mode command, mutate the persistent HidHide baseline, attach VIIPER output, or reuse the old Stock Center M recovery path.**

---

## 2. Why PR4 is required immediately after PR3

The current `StartupCoordinator` is still built around the old Stock Center M architecture.

After hardware compatibility succeeds, it currently proceeds conceptually as:

```text
Controller environment
→ wait for MSI Claw controller topology
→ StockCenterMStartupBaseline.EstablishAsync()
→ ResolveStaleRecoveryAsync()
```

`StockCenterMStartupBaseline.EstablishAsync()` is not read-only.

Its current behavior is:

```text
physical already XInput
    → keep XInput

physical DirectInput
    → SwitchModeAsync(XInput)
    → verify XInput
```

That was correct for the old architecture, but is no longer universally correct after PR3.

A valid new product lifecycle is now:

```text
PR3 Disable and Restart
→ Center M roots persist as Disabled
→ Windows restarts
→ mandatory Addon Runtime starts
```

At that point, if the physical MSI Claw happens to enumerate as PID1902 / DirectInput, the current old startup baseline may intentionally force:

```text
PID1902 → PID1901
```

before the new Addon-owned path even begins.

That directly violates the Full PID1902 authority contract:

> Windows restart while Center M remains Disabled is not an authority-release boundary.

PR4 must therefore introduce the authority branch **before** the legacy stock baseline and old recovery mutation path.

---

## 3. Explicit PR4 scope boundary

### 3.1 In scope

PR4 may:

- make startup read the real Center M startup-root state after hardware support is established;
- branch startup behavior by `Enabled / Disabled / Partial / Unavailable`;
- add one small read-only Disabled-boot admission component/result;
- reuse the existing MSI Claw topology stabilization waiter;
- reuse the existing controller-environment assessment;
- reuse the existing Runtime prerequisite inspector;
- reuse `RecoveryManager.LoadJournal()` as read-only stale-recovery evidence;
- reuse `AddonControllerHidHideBaseline.InspectDisabledModeBaseline([])`;
- require the PR3 zero-target persistent HidHide baseline to already be compliant;
- prevent the legacy Stock Center M baseline from running while Center M is Disabled;
- prevent the old startup HidHide recovery cleaner/journal retirement path from mutating state while Center M is Disabled;
- prevent the old Steam-session physical routing runtime from becoming the controller owner while Center M is Disabled;
- prevent the old Stock Center M resume baseline from running while Center M is Disabled;
- carry one in-memory admission result forward for PR5 to consume;
- add focused unit/architecture tests and startup logs.

### 3.2 Strictly out of scope

Do **not** implement any of the following in PR4:

- PID1901 → PID1902 mode switching;
- PID1902 → PID1901 restoration;
- any call to a native physical mode write for the new Disabled path;
- DirectInput acquisition;
- DirectInput retry/reacquisition;
- exact PID1902 primary-gamepad collection resolution;
- `ApplyDisabledModeBaseline(...)` on Disabled boot;
- new hidden-device target insertion;
- physical isolation verification;
- VIIPER server/bus startup for Addon-owned presentation;
- Xbox360 attach;
- SteamDeck attach;
- first presentation selection;
- publisher start/stop;
- runtime Xbox360 ↔ SteamDeck switching;
- rumble/gyro changes;
- PID drift recovery;
- physical PnP-loss recovery redesign;
- Center M runtime resurrection suppression;
- suspend/resume PID1902 reacquisition;
- crash supervisor/service/heartbeat;
- generalized controller-authority state machine;
- generalized transaction/rollback framework;
- persisted `FirstBootAfterDisable` / `PendingAuthorityTransition` / epoch state;
- broad removal of the old routing implementation.

PR4 establishes **permission to proceed later**.

PR5 will perform the first new physical ownership mutation.

---

## 4. Authority source of truth remains the Center M startup roots

Do not add a second persisted controller-mode setting.

Use the existing exact classification from `CenterMStartupControl`:

```text
Server task Enabled
Updater task Enabled
Foundation Service Automatic
    → FrontendCenterMStartupState.Enabled

Server task Disabled
Updater task Disabled
Foundation Service Disabled
    → FrontendCenterMStartupState.Disabled

mixed values
    → FrontendCenterMStartupState.Partial

read failure / feature unavailable
    → FrontendCenterMStartupState.Unavailable
```

PR4 derives an **in-memory startup decision** from this actual Windows configuration.

Do not persist another authority boolean.

Allowed conceptual output:

```text
CenterMStartupState = Disabled
DisabledBootAdmission = Ready / Blocked
```

This is a runtime fact, not a second source of authority.

---

## 5. Make `CenterMStartupControl` available to startup before the stock baseline

> **Later change (PR #442):** the Center M Enable/Disable card moved from the Device tab to the top
> of the **Controller** tab. Where this document says "Device page" / "Device-page capture" for the
> Center M startup control, read "Controller page". Backend behaviour is unchanged.

Today `AddonProcessHost` constructs the shared `CenterMStartupControl` only during Runtime initialization, after `StartupCoordinator.RunAsync()` has already completed.

PR4 needs the same real startup-root fact earlier.

Prefer the smallest composition change:

```text
AddonStartupCompositionFactory
    ↓
construct one CenterMStartupControl
    ↓
StartupCoordinator reads it only after supported hardware is proven
    ↓
AddonProcessHost reuses the SAME instance for:
        PR2.5 mandatory Runtime policy
        PR3 authority transition
        Device-page capture
```

Do not create a second Center M startup writer/manager.

A narrow read delegate is also acceptable inside `StartupCoordinator`, for example:

```csharp
Func<FrontendCenterMStartupSnapshot> captureCenterMStartup
```

but production should source it from the one shared `CenterMStartupControl`.

Important:

- unsupported/indeterminate hardware must still exit before the Center M authority fact is used to mutate anything;
- PR4 only calls `Capture()` during startup;
- PR4 does not call `SetEnabledAsync()` automatically.

---

## 6. New narrow Disabled-boot admission result

Add one small internal result that future PR5 can consume.

Suggested shape:

```csharp
internal enum DisabledBootAdmissionOutcome
{
    NotApplicable,
    Ready,
    Blocked,
}

internal sealed record DisabledBootAdmissionResult(
    DisabledBootAdmissionOutcome Outcome,
    string Reason)
{
    internal bool IsReady => Outcome == DisabledBootAdmissionOutcome.Ready;
}
```

Exact naming may follow repository conventions.

Do not add dozens of admission states.

The detailed facts already have their own existing types:

- `FrontendCenterMStartupSnapshot`;
- `ControllerEnvironmentAssessmentSnapshot`;
- `RuntimePrerequisiteAssessment`;
- `RecoveryLoadResult` / `RecoveryStatus`;
- `AddonHidHideBaselineResult`.

The admission result only answers:

```text
May the next Full PID1902 ownership stage run?
```

For PR4:

```text
Ready
    → PR5 may later attempt physical ownership

Blocked
    → Runtime stays alive
    → no controller mutation
    → user can inspect/repair or choose Enable and Restart

NotApplicable
    → Center M Enabled / stock path
```

Do not persist this result.

---

## 7. Required startup branch

After the existing update gate and supported-hardware gate succeed, capture Center M startup state **before** the legacy Stock Center M baseline.

Conceptual startup:

```text
Update gate
    ↓
Hardware stabilization / support decision
    ↓
CenterMStartupControl.Capture()
    ↓
┌─────────────────────────────────────────────────────────────┐
│ Enabled                                                     │
│   → existing Stock Center M startup path                    │
│                                                             │
│ Disabled                                                    │
│   → new read-only Disabled-boot admission                   │
│   → no StockCenterMStartupBaseline                          │
│   → no old startup recovery mutation                        │
│                                                             │
│ Partial / Unavailable                                       │
│   → no owner is silently selected                           │
│   → no StockCenterMStartupBaseline                          │
│   → no new Addon controller mutation                        │
│   → Runtime/UI may remain available for repair              │
└─────────────────────────────────────────────────────────────┘
```

This branch is the central PR4 behavior.

---

## 8. Enabled branch — preserve existing stock behavior

When startup roots are exactly Enabled:

```text
Center M authority intent = MSI / stock
```

Preserve the current startup behavior unless a tiny mechanical change is required by the new branch.

Expected flow remains conceptually:

```text
fresh environment assessment
→ existing topology stabilization
→ StockCenterMStartupBaseline.EstablishAsync()
→ XInput / PID1901 stock baseline verified
→ old stale route-scoped recovery cleanup if required
→ old routing Runtime may operate under its existing policy
```

PR4 must not regress the current Center M Enabled path.

Tests must prove the existing stock baseline is still called when and only when the roots are exactly Enabled.

Do not weaken the old recovery cleanup safety for this stock branch merely because Disabled mode now has a separate path.

---

## 9. Disabled branch — read-only admission only

When startup roots are exactly Disabled:

```text
Center M authority intent = Addon
```

The Runtime is mandatory by PR2.5.

PR4 must perform a read-only admission sequence and return either `Ready` or `Blocked`.

Recommended order:

```text
1. wait for a stable supported MSI Claw controller topology
2. fresh controller-manager assessment
3. Runtime prerequisite inspection
4. stale recovery-journal read-only inspection
5. PR2 zero-target HidHide baseline inspection
6. classify Ready / Blocked
```

All six stages are read-only in PR4.

If any stage cannot be positively verified:

```text
Blocked
→ do not run stock baseline
→ do not clean old recovery state
→ do not change PID
→ do not apply HidHide
→ do not attach VIIPER
→ keep Runtime/frontend/tray available
```

---

## 10. Reuse the existing topology stabilization waiter

The current `ControllerEnvironmentWaiter` is already read-only and stabilizes only the MSI Claw internal controller topology.

Its current Stock Center M readiness predicate accepts a resolvable control HID for either:

```text
XInput / PID1901
or
DirectInput / PID1902
```

That makes the existing waiter usable for PR4 admission without requiring a new PnP waiter.

Prefer reusing it rather than introducing a second topology-stabilization abstraction.

A pragmatic PR4 implementation may call the existing waiter using its current `StockCenterM` readiness mode if that is the smallest change, because the implementation is only checking:

```text
same MSI Claw internal topology exists
+ control HID is resolvable
+ topology is stable for the bounded window
```

It does not start Center M and it does not issue a mode command.

Do not rename/rewrite the entire environment model merely for this PR unless compilation forces a small mechanical change.

### 10.1 PID1901 and PID1902 are both valid PR4 observations

Critical rule:

```text
Center M Disabled + current PID1901
    → may still pass PR4 admission
    → PR5 will later switch to PID1902

Center M Disabled + current PID1902
    → may still pass PR4 admission
    → PR5 will later keep PID1902
```

PR4 must **not** require the current controller to already be PID1902.

PR4 also must **not** force PID1902 back to PID1901.

The desired-state policy belongs to PR5:

```text
current 1902 → keep
current 1901 → switch to 1902
```

---

## 11. Controller-manager admission while Disabled

Use one fresh existing controller-environment assessment.

For entering/continuing exclusive Addon controller authority at Disabled boot:

```text
assessment.Manager.Kind == ControllerManagerKind.None
    → manager gate passes

ClawTweaks / HandheldCompanion / Winhanced / Multiple / Indeterminate
    → Blocked
```

A throwing/unreadable assessment must also fail closed.

Do not use the existing stock compatibility result as the Disabled-mode authority gate.

The current stock compatibility policy intentionally expects MSI Center M to be running; while Center M is Disabled, `MsiCenterMNotOperational` is expected and does not mean the user chose stock authority.

For PR4, use the **manager classification fact**, not `Compatibility.AllowsMutation`, for the exclusive-controller-manager check.

Do not add generalized coexistence/arbitration.

Do not add new high-frequency process polling.

---

## 12. Runtime prerequisite admission

Reuse `IRuntimePrerequisiteInspector` / `RuntimePrerequisiteAssessment`.

Disabled admission requires:

```text
HidHide  == Ready
UsbIpWin2 == Ready
Viiper    == Ready
```

which is already represented by:

```csharp
RuntimePrerequisiteAssessment.IsRoutingReady
```

Although PR4 does not start VIIPER, a known missing/unusable/incompatible virtual-controller prerequisite must block admission before PR5 is allowed to own the physical controller.

This prevents the future failure shape:

```text
physical hidden / PID1902 owned
+ no usable virtual-controller backend
```

Do not install or repair prerequisites automatically in PR4.

`Missing`, `Unusable`, `Incompatible`, and `Indeterminate` all mean:

```text
DisabledBootAdmission = Blocked
```

The existing setup flow remains the repair path.

---

## 13. Stale recovery journal is read-only evidence on Disabled boot

This is a critical migration rule.

The current stock startup path calls `ResolveStaleRecoveryAsync()`, which can:

- remove route-scoped HidHide mutations proven by the old journal;
- delete the stale recovery journal after cleanup.

That mutation path is correct only for the old stock startup/recovery model.

After PR3, the Disabled path has a new persistent PR2 HidHide baseline that must survive restart.

Therefore PR4 Disabled boot must **not** run `StartupHidHideRecoveryCleaner.TryClean(...)` and must **not** retire/delete a recovery journal as part of admission.

Use only the existing read side:

```text
RecoveryManager.LoadJournal()
```

Recommended policy:

```text
NoRecoveryNeeded
    → recovery admission passes

RecoveryRequired
    → Blocked
    → do not clean/delete anything in PR4 Disabled mode

Failure / malformed / unverifiable
    → Blocked
```

Why:

A surviving old validated routing journal means the process cannot positively prove that the new persistent Addon baseline is the only HidHide ownership evidence.

Do not guess.

Do not auto-delete.

The safe user repair path is:

```text
Enable Center M and Restart
→ stock authority boot
→ existing stock stale-recovery cleanup may run under its existing contract
```

or another explicit supported repair path later.

This preserves one clear recovery authority instead of mixing the old route journal with the new persistent Disabled-mode baseline.

---

## 14. HidHide Disabled baseline must already be compliant

PR3 intentionally wrote this persistent zero-target baseline before disabling Center M:

```text
Inverse = false
Active = true
Addon Runtime executable whitelisted
Hidden targets = 0
```

PR4 only inspects it:

```csharp
var baseline = addonControllerHidHideBaseline.InspectDisabledModeBaseline([]);
```

Admission passes only when the baseline is positively proven compliant.

For the read-only inspection call, that means conceptually:

```text
Outcome == AlreadyCompliant
or equivalently
result.IsCompliant == true
```

Critical distinction:

```text
Applicable != compliant
```

`Applicable` means the baseline **could** be applied, but PR4 is not allowed to apply it.

Therefore:

```text
AlreadyCompliant → pass
Applicable       → Blocked
Conflict         → Blocked
Unavailable      → Blocked
```

Do not call:

```text
ApplyDisabledModeBaseline([])
```

from Disabled boot in PR4.

PR5 will first resolve the exact PID1902 target and extend the baseline only after physical ownership exists.

---

## 15. Partial / Unavailable Center M roots — fail closed without selecting an owner

Mixed startup roots are not MSI authority and are not Addon authority.

For:

```text
FrontendCenterMStartupState.Partial
FrontendCenterMStartupState.Unavailable
```

PR4 must not silently choose either controller owner.

Do not run:

```text
StockCenterMStartupBaseline.EstablishAsync()
```

and do not run any new Addon physical ownership.

Expected behavior:

```text
supported hardware
+ Center M roots Partial/Unavailable
    ↓
Runtime/frontend/tray may remain available
    ↓
controller mutation path stays passive
    ↓
Device page shows current real Center M startup state
    ↓
user repairs with Enable and Restart or Disable and Restart where available
```

Do not introduce an automatic authority repair at startup.

PR3 remains the explicit mutation owner for changing Center M startup roots.

---

## 16. Keep the Runtime alive when Disabled admission is blocked

A blocked Disabled admission is not a reason to terminate the mandatory Runtime.

When Center M roots are exactly Disabled:

```text
Runtime lifetime = mandatory
```

still applies even when:

- USBIP2 is missing;
- VIIPER is unusable;
- HidHide baseline drifted;
- a stale routing journal remains;
- controller topology is temporarily indeterminate;
- controller-manager admission cannot be verified.

Expected state:

```text
Center M Disabled
+ admission Blocked
+ Runtime alive
+ tray alive
+ frontend can open
+ no new controller mutation
```

This gives the user an escape/recovery path:

```text
Enable Center M and Restart
```

Do not convert a failed admission into ordinary Runtime Exit.

Do not add a watchdog in this PR.

---

## 17. Prevent the old Steam-session routing owner from running while Disabled

This is required for PR4 to remain a real no-mutation admission PR.

Even if the new Disabled admission reports `Ready`, PR4 must not let the existing old routing runtime become the physical controller owner before PR5 is implemented.

Introduce one narrow derived runtime gate, conceptually:

```csharp
legacyRoutingAllowed = centerMStartupState == FrontendCenterMStartupState.Enabled;
```

Use it when building the old routing runtime.

Expected behavior:

### Center M Enabled

```text
legacyRoutingAllowed = true
→ existing AddonRoutingRuntime behavior preserved
```

### Center M Disabled / Partial / Unavailable

```text
legacyRoutingAllowed = false
→ do not create/start the old Steam-session physical routing owner
```

Do not add a new generic routing manager.

The old routing implementation may remain in the repository; PR4 only stops selecting it in the new authority states.

This prevents a Steam/BPM event from re-entering the obsolete:

```text
Steam session
→ temporary physical takeover
```

model while the product is already in durable Addon authority mode.

---

## 18. Preserve read-only Steam/game observation when legacy routing is disabled

Disabling the old physical routing runtime must not unnecessarily disable independent game/profile observation.

`SteamSessionRuntime` already distinguishes actual Steam observation from routing observation.

When Center M is not exactly Enabled, prefer:

```text
StartActualObservation()
```

rather than:

```text
StartRoutingObservation()
```

if the current composition requires a choice.

This keeps current AppID/profile/device features alive without granting the legacy routing pipeline physical-controller authority.

Do not make PR4 a Steam detection rewrite.

---

## 19. Prevent Stock Center M baseline mutation on resume while Disabled

Startup is not the only place the existing stock baseline is wired.

`AddonRuntimeCompositionFactory` currently passes `IStockCenterMStartupBaseline.EstablishAsync()` into the Runtime's power/resume recovery path.

If that callback remains active during Center M Disabled mode, a real sleep/resume lifecycle could later issue:

```text
PID1902 → PID1901
```

even though startup correctly skipped the stock baseline.

PR4 must close this realistic lifecycle hole.

When Center M is not exactly Enabled:

```text
stock Center M resume baseline must not be invoked
```

Use the smallest composition-level gate.

Conceptually:

```csharp
Func<CancellationToken, Task<bool>> establishBaseline = legacyRoutingAllowed
    ? existingStockBaseline
    : _ => Task.FromResult(true);
```

or an equivalent narrow no-op path.

The important invariant is:

> **Sleep/resume while Center M is Disabled must not accidentally call the legacy XInput-restoration baseline.**

Do not implement new PID1902 resume ownership in PR4; that belongs to later recovery work after PR5/PR6.

---

## 20. Recommended `StartupResult` / composition data flow

Carry enough in-memory information forward so PR5 does not need to rediscover startup authority.

A small extension is acceptable, for example conceptually:

```csharp
internal sealed record StartupResult(
    ...existing fields...,
    FrontendCenterMStartupState CenterMStartupState = FrontendCenterMStartupState.Unavailable,
    DisabledBootAdmissionResult? DisabledBootAdmission = null);
```

Exact representation may differ.

Rules:

- `CenterMStartupState` is the startup snapshot classification, not persisted state;
- `DisabledBootAdmission` is `NotApplicable` for Enabled;
- `DisabledBootAdmission.IsReady` can be consumed by PR5;
- Partial/Unavailable must never be represented as Addon admission Ready;
- do not add `DesiredPid`, `FirstBoot`, `PendingTransition`, or generation counters.

If a smaller existing result shape can carry the same facts without ambiguity, use it.

---

## 21. Recommended startup flow in code

Conceptual pseudocode:

```csharp
var update = await _updateGate.RunAsync(token);
if (update == RestartScheduled)
    return UpdateRestart();

var hardware = await EvaluateHardwareCompatibilityWithStabilizationAsync(token);
if (!hardware.IsSupported)
    return ExistingUnsupportedOrIndeterminateResult(hardware);

var centerM = _centerMStartup.Capture();

switch (centerM.State)
{
    case FrontendCenterMStartupState.Enabled:
        return await RunExistingStockStartupAsync(hardware, token);

    case FrontendCenterMStartupState.Disabled:
    {
        var topology = await _environmentWaiter.WaitUntilStableAsync(
            ControllerEnvironmentMode.StockCenterM,
            token);

        if (topology != ControllerEnvironmentReadiness.Stable)
            return RuntimeReadyButAdmissionBlocked(
                hardware,
                centerM,
                "ControllerTopologyNotStable");

        var admission = _disabledBootAdmission.Evaluate();
        return RuntimeReady(
            hardware,
            centerM,
            admission,
            recoverySafe: admission.RecoverySafeFactIfCarriedSeparately);
    }

    case FrontendCenterMStartupState.Partial:
        return RuntimeReadyButNoControllerOwner(
            hardware,
            centerM,
            "CenterMStartupPartial");

    default:
        return RuntimeReadyButNoControllerOwner(
            hardware,
            centerM,
            "CenterMStartupUnavailable");
}
```

This pseudocode is directional, not a requirement to add helpers with these exact names.

Keep the implementation small.

---

## 22. Failure policy

PR4 has no generalized rollback because the new Disabled path performs no persistent mutation.

### Read failure

```text
cannot prove fact
→ Blocked
→ log reason
→ keep Runtime/UI available where hardware support is known
```

### Topology timeout

```text
Blocked
→ no PID command
→ no HidHide apply
→ no virtual attach
```

### Environment assessment throws / Indeterminate

```text
Blocked
```

### Prerequisite inspector reports not ready

```text
Blocked
```

### Recovery journal exists or cannot be validated

```text
Blocked
→ do not clean or delete it in Disabled mode
```

### HidHide inspection is Applicable / Conflict / Unavailable

```text
Blocked
→ do not repair it in PR4
```

No exception should cause the startup path to fall through into Stock Center M mutation while the roots are Disabled or unreadable.

That is the main fail-close rule.

---

## 23. Logging requirements

Make the authority branch and admission result obvious in hardware logs.

Suggested structured events:

```text
CenterM.StartupAuthority
  State=Enabled|Disabled|Partial|Unavailable
  Action=StockPath|DisabledAdmission|Passive

ControllerAdmission
  Result=Ready|Blocked
  Reason=<stable reason>

ControllerAdmission
  Topology=Stable|Indeterminate
  Manager=None|...
  PrerequisitesReady=true|false
  RecoveryJournal=Clear|Present|Invalid
  HidHideBaseline=AlreadyCompliant|Applicable|Conflict|Unavailable
```

Do not dump sensitive full process/environment data merely for this feature.

Do not add polling logs outside the existing bounded topology waiter.

The logs should make manual validation possible without adding new UI.

---

## 24. Frontend / UI scope

No frontend protocol change is required solely for PR4.

Do not bump the named-pipe protocol unless implementation truly needs to expose a new frontend field.

Preferred PR4 behavior:

- existing Device page continues to show actual Center M startup roots;
- PR2.5 continues to lock launch-at-startup while roots are Disabled;
- PR3 Enable and Restart remains the official release path;
- admission Ready/Blocked can remain Runtime-internal plus logs for this foundation PR.

If a tiny existing status field can surface the admission reason without adding a new protocol concept, that is optional, not required.

Do not turn PR4 into a status-page redesign.

---

## 25. Tests — Disabled admission component

Add focused tests for the read-only gate.

At minimum:

### 25.1 Ready path

Given:

```text
Center M roots = Disabled
stable supported MSI Claw topology
controller manager = None
all prerequisites = Ready
recovery journal = NoRecoveryNeeded
HidHide zero-target baseline = AlreadyCompliant
```

expect:

```text
DisabledBootAdmission = Ready
```

and zero mutations.

### 25.2 PID1901 is not rejected

A stable PID1901/XInput MSI Claw topology while Center M roots are Disabled can still reach:

```text
Admission = Ready
```

PR4 must not switch it.

### 25.3 PID1902 is not rejected or round-tripped

A stable PID1902/DirectInput MSI Claw topology while Center M roots are Disabled can still reach:

```text
Admission = Ready
```

and no XInput mode write is issued.

### 25.4 HidHide `Applicable` is blocked

Given a readable, conflict-free HidHide configuration that is not yet the PR3 baseline:

```text
InspectDisabledModeBaseline([]).Outcome = Applicable
```

expect:

```text
Admission = Blocked
ApplyDisabledModeBaseline() never called
```

### 25.5 HidHide conflict/unavailable blocked

Both must block with no mutation.

### 25.6 Prerequisite failure blocked

Test at least:

- USBIP2 Missing;
- VIIPER Unusable;
- HidHide Indeterminate.

All must block.

### 25.7 Controller manager blocked / fail closed

Test:

- `ClawTweaks`;
- `HandheldCompanion`;
- `Winhanced`;
- `Multiple`;
- `Indeterminate`;
- assessment throws.

All must block Disabled admission.

### 25.8 Recovery journal blocks without mutation

Test:

```text
LoadJournal = RecoveryRequired
```

and:

```text
LoadJournal = Failure
```

Expect:

- admission Blocked;
- no `StartupHidHideRecoveryCleaner.TryClean()`;
- no journal delete;
- no stock baseline.

### 25.9 Topology not stable

Existing bounded waiter returns `Indeterminate`:

```text
Admission = Blocked
```

No controller mutation follows.

---

## 26. Tests — authority-aware StartupCoordinator

Add/adjust `StartupCoordinatorTests` to prove the branch itself.

### Enabled

```text
roots Enabled
→ existing environment/topology path runs
→ StockCenterMStartupBaseline runs
→ existing stale recovery path remains allowed
```

### Disabled

```text
roots Disabled
→ topology admission runs
→ StockCenterMStartupBaseline NEVER runs
→ old stale recovery cleaner NEVER runs
→ old journal retirement NEVER runs
```

### Partial

```text
roots Partial
→ no stock baseline
→ no Disabled physical ownership
→ Runtime remains available for repair
```

### Unavailable

```text
roots Unavailable
→ no stock baseline
→ no Disabled physical ownership
→ fail closed
```

### Ordering

For Disabled, test the conceptual order:

```text
Update
→ Hardware
→ CenterMStartupCapture
→ Topology
→ Admission facts
```

and assert there is no mutation event afterward.

---

## 27. Tests — legacy routing / resume suppression

This is required because otherwise PR4 can pass startup tests and still mutate PID later in the same normal Windows lifecycle.

### Disabled Runtime composition

Given startup authority state Disabled:

```text
legacy AddonRoutingRuntime physical owner is not created/started
Steam actual observation may remain active
```

### Enabled Runtime composition

Given startup authority state Enabled:

```text
existing routing composition behavior is preserved
```

### Disabled resume

Inject/record the existing `IStockCenterMStartupBaseline` callback.

On a normal suspend/resume lifecycle while the startup authority is Disabled:

```text
stock baseline callback call count = 0
```

Do not require a new PID1902 resume owner yet.

The test only proves that the obsolete PID1901 restore path cannot run.

---

## 28. Architecture guard tests

Add lightweight source/constructor guards where useful.

PR4's new Disabled admission path must not reference or invoke:

```text
SwitchModeAsync
MsiClawInputSource
DirectInput
CanonicalViiperRuntime
AttachXbox360
AttachSteamDeck
ApplyDisabledModeBaseline
AddHiddenDevice
```

A source-level guard may be used if that matches existing repository test style.

Also verify:

```text
Center M Disabled branch does not call StockCenterMStartupBaseline.EstablishAsync
Center M Disabled branch does not call StartupHidHideRecoveryCleaner.TryClean
```

Do not create brittle tests for every line ordering unrelated to safety.

---

## 29. Manual hardware validation

Validate both authority branches on a supported MSI Claw.

### 29.1 Baseline — Center M Enabled regression

Start from:

```text
Center M roots = Enabled
physical controller = stock PID1901
```

Restart Windows.

Verify:

- Addon starts normally;
- stock startup baseline path still works;
- stock controller remains usable;
- existing non-Full1902 behavior is not regressed.

### 29.2 PR3 Disable → first PR4 Disabled boot

From the Enabled baseline:

```text
Controller page
→ Disable and Restart
```

After Windows restarts, verify logs show:

```text
Center M roots = Disabled
Startup authority = Addon / Disabled
DisabledBootAdmission = Ready
```

provided all admission facts are healthy.

Also verify:

- mandatory Runtime starts;
- tray remains available;
- Center M startup roots remain Disabled;
- PR3 zero-target HidHide baseline remains present:
  - Active=true;
  - Inverse=false;
  - Addon executable whitelisted;
  - hidden targets=0;
- no `StockCenterMStartupBaseline` mode write occurs;
- no old stale recovery cleanup mutation occurs;
- no new PID1902 mode write occurs in PR4;
- no DirectInput session is created by the new path;
- no virtual X360/SteamDeck presentation is attached by the new path.

### 29.3 Current physical PID is not a PR4 acceptance condition

Record whether the controller appears as PID1901 or PID1902 after reboot.

Both are acceptable for PR4.

The acceptance condition is:

```text
PR4 did not change it.
```

PR5 will own:

```text
1902 → keep
1901 → switch to 1902
```

### 29.4 Blocked prerequisite test

Temporarily create a known non-ready prerequisite state in a safe test environment.

Verify:

```text
DisabledBootAdmission = Blocked
Runtime remains alive
no stock baseline
no PID mutation
no HidHide apply
no virtual attach
```

The user must still be able to open the frontend and use `Enable and Restart`.

### 29.5 HidHide drift test

Starting from Center M Disabled, alter the PR3 zero-target baseline so inspection is readable but no longer compliant.

Expected:

```text
Applicable/other non-compliant result
→ admission Blocked
→ PR4 does not repair it automatically
```

### 29.6 Sleep/resume regression check

While Center M remains Disabled:

```text
Sleep
→ Resume
```

Verify there is no log indicating the legacy Stock Center M baseline attempted an XInput restore.

PR4 does not yet need to reacquire PID1902 after resume; it only must not release Addon authority back to the legacy stock path.

---

## 30. Expected files

The exact diff may vary, but keep the implementation concentrated.

Likely new file:

```text
src/SteamInputAddonforClaw/Startup/DisabledBootControllerAdmission.cs
```

Likely modified files:

```text
src/SteamInputAddonforClaw/Startup/StartupCoordinator.cs
src/SteamInputAddonforClaw/Startup/AddonStartupComposition.cs
src/SteamInputAddonforClaw/Runtime/AddonRuntimeComposition.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
```

Likely tests:

```text
tests/SteamInputAddonforClaw.Tests/DisabledBootControllerAdmissionTests.cs
tests/SteamInputAddonforClaw.Tests/StartupCoordinatorTests.cs
existing Runtime/hosting composition tests as appropriate
```

Avoid unrelated UI, QAM, overlay, TDP, profile, publisher, rumble, gyro, or OEM1 cleanup.

---

## 31. No-go design rules

Do not solve PR4 by adding:

```text
ControllerAuthorityManager
ControllerAuthorityStateMachine
BootAuthorityEpoch
PendingTransitionJournal
FirstBootAfterDisable flag
PID ownership manager
generic startup transaction engine
new watchdog/service
new PnP event framework
second HidHide authority
```

Do not defend against arbitrary instruction-level races.

The realistic lifecycle requirements for this PR are simpler:

- reboot after PR3 Disable;
- current controller may enumerate PID1901 or PID1902;
- startup topology may take a bounded time to settle;
- prerequisites may genuinely be missing;
- stale old recovery evidence may remain;
- HidHide configuration may genuinely drift;
- sleep/resume must not call the old PID1901 stock baseline while Disabled.

Protect those paths directly.

---

## 32. Acceptance criteria

PR4 is complete when all of the following are true:

1. Startup reads actual Center M startup roots before selecting the controller startup path.
2. Exact Enabled preserves the existing stock startup path.
3. Exact Disabled never runs `StockCenterMStartupBaseline.EstablishAsync()`.
4. Exact Disabled never runs old route-scoped startup HidHide cleanup/journal retirement.
5. Partial/Unavailable never silently selects MSI or Addon controller authority.
6. Disabled boot performs only read-only admission for the new Full PID1902 path.
7. Stable PID1901 and stable PID1902 are both acceptable PR4 observations.
8. Disabled admission requires a positively clean controller-manager environment.
9. Disabled admission requires all current Runtime prerequisites Ready.
10. Disabled admission requires no stale/unverifiable old recovery journal.
11. Disabled admission requires the PR3 zero-target HidHide baseline already compliant.
12. `Applicable` HidHide state is not mistaken for compliant state.
13. Blocked admission keeps the mandatory Runtime/frontend/tray alive and performs no controller mutation.
14. The old Steam-session physical routing owner is not selected while Center M is Disabled/Partial/Unavailable.
15. Steam actual-game observation can remain available without old physical routing ownership.
16. Sleep/resume while Disabled cannot invoke the legacy Stock Center M XInput baseline.
17. No PID switch, DirectInput acquisition, HidHide apply, or VIIPER attach is added by PR4.
18. Debug and Release builds are clean.
19. Full automated test suite passes.

---

## 33. Expected next PR — do not implement here

After PR4 proves that Disabled boot can safely enter an Addon-owned controller startup path, PR5 should perform the first new physical ownership operation.

Current PR5 target:

```text
Center M roots == Disabled
+ DisabledBootAdmission == Ready
    ↓
inspect current physical MSI Claw
    ↓
current PID1902
    → keep PID1902

current PID1901
    → switch same physical MSI Claw to PID1902
    ↓
bounded PnP settle
    ↓
verify same strong physical identity
    ↓
DirectInput acquire
    ↓
resolve exact PID1902 primary gamepad collection
    ↓
reconcile persistent HidHide baseline with that exact target
    ↓
verify physical isolation
```

Both persistent VIIPER logical devices remain detached through PR5.

First virtual presentation attach remains the following PR.

---

## 34. Final implementation principle

Keep PR4 deliberately boring.

The product is crossing an authority boundary, so the first boot under Addon authority should establish **facts before actions**.

```text
Center M Disabled
    ↓
Runtime mandatory
    ↓
read actual system state
    ↓
prove topology + prerequisites + recovery + HidHide admission
    ↓
Ready
```

or:

```text
cannot prove one required fact
    ↓
Blocked
    ↓
no physical mutation
```

The most important safety improvement is not a new state machine.

It is simply removing the obsolete assumption:

```text
"every supported startup should converge the MSI Claw to Stock XInput"
```

and replacing it with:

```text
Center M Enabled
    → stock startup path

Center M Disabled
    → read-only Addon admission
```

That is the entire purpose of PR4.