# Work Order — Full1902 Policy B: Bind WING / Game Bar Suppression to Addon Controller Authority

## Status

Focused implementation work order for moving native `Win+G` / Xbox Game Bar suppression to the Full1902 Addon controller-authority lifetime.

This document has been **revised after merged PR #470** (`Full1902 Policy A2: decouple OEM1/WING front-button actions and disable legacy routing`).

PR #470 is now a hard prerequisite and changed the production composition materially:

- production no longer composes `AddonRoutingRuntime`;
- production no longer starts legacy Steam-session physical routing observation;
- OEM1/WING action wiring is owned by the feature-local `MsiClawFrontButtonRuntime`;
- WING Steam-button delivery already targets the existing Full1902 `MsiClawAddonPresentation` pulse seam;
- `MsiClawFrontButtonRuntime` already exposes the exact Policy B readiness seam through `nativeWinGSuppressionReady`;
- production currently leaves that readiness false, so WING custom delivery is intentionally inactive until this work lands.

This work order therefore no longer treats `WinGProtectionRoutingStage.CaptureAuthority()` or `AddonRoutingRuntime` as production WING authority.

This work order is intentionally **not numbered PR13**. PR13 remains reserved for the later Windows / Velopack uninstall-entry integration.

Code-review baseline for this revision:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     3c727cf194cfa9f2678468294f372dcc6791cdca
merged PR:  #470
```

Before implementation, read and treat these as design authorities:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/FULL1902_POLICY_A2_DECOUPLE_FRONT_BUTTON_ACTIONS_AND_DISABLE_LEGACY_ROUTING_WORK_ORDER.md`
- current PR5–PR12 Full1902 physical ownership / presentation / recovery / stock-safe-release work orders where relevant.

The application is pre-release. Implement the current Full1902 product policy directly. Do not preserve obsolete route-scoped suppression semantics merely because legacy classes remain in the tree.

---

# 1. Goal

Make this product invariant true:

```text
MSI Center M Enabled / MSI stock controller authority
→ Full1902 WING Game Bar suppression is not active

MSI Center M Disabled / Addon controller authority
→ native WING Win+G / Xbox Game Bar is suppressed for the entire Addon-authority lifetime
→ independent of Steam game state
→ independent of Big Picture state
→ independent of Xbox360 vs SteamDeck virtual presentation
→ independent of Overlay capture
→ retained across supported physical-input recovery and suspend/resume
```

Confirmed product policy:

> While the Addon owns the controller, the WING button must never surface native Xbox Game Bar.

This PR is only about **native Game Bar suppression ownership/lifetime and activating the already-extracted WING delivery seam safely**.

It does **not** decide the final user-visible WING Single/Double mappings.

---

# 2. Current production architecture after PR #470

## 2.1 Legacy physical routing is no longer production-composed

Current production target is already:

```csharp
AddonRoutingRuntime? routingRuntime = null;
```

`AddonRuntimeCompositionFactory` calls only:

```csharp
steamRuntime.StartActualObservation();
```

for the old Runtime composition path.

Therefore Policy B must not reintroduce any dependency on:

```text
AddonRoutingRuntime
RoutingPipeline
WinGProtectionRoutingStage
CanonicalSteamDeckOutputStage
legacy route activation
```

These classes may still exist in source for historical/tests/cleanup reasons, but they are not production authority.

## 2.2 `MsiClawFrontButtonRuntime` is now the production front-button owner

Current production file:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawFrontButtonRuntime.cs
```

It owns only feature-local behavior:

- OEM1 Event41 WMI observation;
- WING Event88 WMI observation;
- OEM1/WING gesture recognition;
- OEM1/WING action dispatch;
- a small WING lifetime epoch used to reject stale delayed gesture delivery.

It intentionally does **not** own:

- PID1901/PID1902;
- DirectInput;
- HidHide;
- VIIPER;
- Steam/BPM observation;
- physical recovery;
- Center M startup authority.

This separation must remain.

## 2.3 PR #470 already created the Policy B WING readiness seam

`MsiClawFrontButtonRuntime.Create(...)` accepts:

```csharp
Func<bool>? nativeWinGSuppressionReady
```

and `CaptureWingAuthority()` derives active delivery from:

```text
front-button runtime lifetime valid
AND nativeWinGSuppressionReady() == true
```

Production currently does not pass a readiness callback, so the default is false.

That is intentional interim safety: WING custom actions remain inactive until native Win+G suppression is proven ready.

Policy B should activate this existing seam directly; do not replace it with a new manager or state object.

## 2.4 Steam/QAM pulses already reuse the Full1902 presentation owner

PR #470 added to `IMsiClawAddonPresentation` / `MsiClawAddonPresentation`:

```csharp
AddonPresentationKind? ActivePresentation { get; }
bool TryRequestSteamPulse();
bool TryRequestQuickAccessPulse();
```

The SteamDeck pulse path uses one shared `SteamDeckSystemButtonOverlay` inside the existing continuous SteamDeck publisher.

Policy B must not change this ownership model and must not create another virtual device or pulse publisher.

---

# 3. `WinGSuppressionGuard` remains the one low-level primitive

Current file:

```text
src/SteamInputAddonforClaw/GameBar/WinGSuppressionGuard.cs
```

It already owns the process-local low-level keyboard-hook mechanics:

- `Start()` installs the low-level hook;
- `EnsureArmed()` enables Win+G suppression;
- `IsArmed` verifies both armed state and installed hook;
- `Disarm()` releases suppression while leaving the hook installed;
- `Dispose()` removes the hook;
- existing Win-key cleanup prevents a suppressed chord from leaving the Windows modifier logically stuck.

Reuse this guard.

Do **not** add:

- another keyboard-hook implementation;
- a `WingAuthorityManager`;
- a `GameBarPolicyService`;
- registry/GPO Game Bar disabling;
- a service/helper/watchdog;
- polling of `IsArmed`;
- a retry state machine.

---

# 4. Critical current ordering problem after PR #470

The production Runtime currently installs the keyboard hook through:

```csharp
AddonProcessHost.StartRuntimeEventWatchers()
{
    _winGSuppressionGuard.Start();
}
```

But `RuntimeProcessApplication` calls this only **after**:

```text
RunStartupAsync()
→ InitializeRuntimeAsync()
→ TryInitializeTray(...)
→ enter NativeMessageLoop
→ StartRuntimeEventWatchers()
```

Meanwhile the Full1902 Disabled-mode controller path runs during `InitializeRuntimeAsync()` / deferred startup setup and currently reaches:

```csharp
presentation.AttachInitialAsync(...)
```

before the old `StartRuntimeEventWatchers()` hook-install point is guaranteed to have run.

That ordering is incompatible with Policy B.

Required invariant:

```text
Center M Disabled
→ physical PID1902 ownership + DirectInput + exact HidHide proven
→ Win+G hook installed
→ EnsureArmed() succeeds
→ IsArmed == true
→ only then first live virtual presentation may attach/start publisher
```

Therefore this PR must move or otherwise guarantee the **single hook installation** early enough that the arm check can be performed before the first `AttachInitialAsync()`.

Do not call `Start()` twice merely to preserve the old call site.

Preferred simplification:

- move the one process-owned `Start()` to the authority-aware startup path before first live presentation, or to an earlier single process startup point that is guaranteed to run before that boundary;
- make the old `StartRuntimeEventWatchers()` call no longer responsible for first installation if that ordering is too late;
- keep exactly one low-level guard instance and one installation lifecycle.

Do not create a second hook just for Disabled mode.

---

# 5. Required Full1902 suppression lifecycle

## 5.1 Center M Enabled / stock authority

Full1902 authority contract:

```text
Center M Enabled
→ MSI / stock controller authority
→ desired PID1901
→ no Full1902 physical ownership
→ no Full1902 VIIPER presentation
→ no Full1902 front-button action owner
```

In this state:

```text
Full1902 Win+G suppression must not be armed.
```

The Addon process may still install the hook earlier if process architecture requires it, but `IsArmed` must remain false under stock authority.

Do not globally suppress Game Bar merely because the Addon Runtime is running.

Do not create a hidden VIIPER/presentation just to service WING.

## 5.2 Center M Disabled / Addon authority startup

After Disabled admission and physical ownership are safe, but **before** first live virtual presentation:

```text
1. Ensure the one WinGSuppressionGuard hook is installed.
2. Call EnsureArmed().
3. Require EnsureArmed() == true.
4. Require IsArmed == true.
5. Only then call presentation.AttachInitialAsync(...).
6. Only after the presentation boundary succeeds, compose/start MsiClawFrontButtonRuntime.
7. Pass nativeWinGSuppressionReady: () => _winGSuppressionGuard.IsArmed.
```

Conceptual production wiring:

```csharp
_frontButtonRuntime = MsiClawFrontButtonRuntime.Create(
    hardwareSupported: startupResult.HardwareSupported,
    oem1MappingPreference: startupSettings,
    wingMappingPreference: startupSettings,
    isSteamDeckPresentationActive: () =>
        _presentationOwnership?.ActivePresentation == AddonPresentationKind.SteamDeck,
    tryRequestQuickAccessPulse: () =>
        _presentationOwnership?.TryRequestQuickAccessPulse() ?? false,
    tryRequestSteamPulse: () =>
        _presentationOwnership?.TryRequestSteamPulse() ?? false,
    nativeWinGSuppressionReady: () => _winGSuppressionGuard.IsArmed);
```

Exact formatting/naming may differ, but do not add another authority boolean.

## 5.3 Arm failure is fail-closed

If Disabled-mode Addon authority is otherwise ready but the guard cannot be installed/armed/proven:

```text
DO NOT attach a live Xbox360 presentation
DO NOT attach a live SteamDeck presentation
DO NOT start a controller publisher
DO NOT create WING custom delivery
DO NOT fall back to native Game Bar behavior while Addon owns the controller
```

Keep virtual output detached/neutral according to the existing Full1902 fail-close boundary.

Log a clear reason, conceptually:

```text
WinGSuppressionUnavailable
WinGSuppressionArmFailed
```

No retry loop. A later ordinary Runtime restart may retry through the normal startup path.

## 5.4 Suppression remains armed across presentation switching

Once Addon authority is live:

```text
Xbox360 → SteamDeck
SteamDeck → Xbox360
Steam game start/exit
Big Picture enter/exit
```

must cause:

```text
0 Disarm() calls
0 additional Start() hook installs
0 new hook ownership state
```

Presentation switching is not controller-authority release.

## 5.5 Overlay capture does not change suppression authority

Overlay capture temporarily pauses/neutralizes game-facing publication while keeping Addon controller authority.

Therefore:

```text
Overlay show/capture
Overlay hide/resume
```

must not disarm native Game Bar suppression.

Do not tie suppression to `_overlayCaptureActive`.

## 5.6 Suspend / hibernate / resume retains suppression authority

Center M Disabled remains Addon authority across suspend/hibernate/resume.

Do not intentionally disarm on suspend and create a new suppression session on resume.

If the existing Windows hook survives supported resume normally, leave it alone.

Do not add resume polling/reinstall loops or new epochs for theoretical timing interleavings.

If future hardware evidence proves the hook is destroyed across real resume, handle that concrete defect separately.

## 5.7 Physical device loss / PnP / DirectInput recovery retains suppression authority

While Center M remains Disabled:

```text
physical PID1902 loss
DirectInput session loss
same-device PnP re-enumeration
owned recovery
```

are recovery events, not controller-authority release.

Suppression remains armed.

If an existing recovery path recreates or reattaches live presentation, it must require:

```text
_winGSuppressionGuard.IsArmed == true
```

before live presentation/publisher resumes.

Use one small process-host precondition/helper if needed. Do not build a second recovery manager.

---

# 6. WING production authority after Policy B

PR #470 already provides the correct feature-local authority boundary:

```text
MsiClawFrontButtonRuntime lifetime
+ nativeWinGSuppressionReady()
→ WingRouteAuthoritySnapshot.Active
```

Policy B should simply bind:

```csharp
nativeWinGSuppressionReady: () => _winGSuppressionGuard.IsArmed
```

This gives the intended behavior:

```text
Center M Disabled + guard armed
→ WING custom delivery allowed

Center M Disabled + guard unavailable/unarmed
→ WING custom delivery blocked

Center M Enabled
→ no Full1902 front-button runtime
→ no Policy-B WING delivery
```

Do not introduce a second WING authority owner.

The existing small lifetime epoch inside `MsiClawFrontButtonRuntime` remains sufficient for stale delayed gesture rejection.

Do not add another hook epoch/barrier.

---

# 7. Legacy `WinGProtectionRoutingStage` after PR #470

After PR #470, the legacy stage is no longer a production prerequisite for WING delivery.

Therefore this Policy B PR must **not** depend on:

```text
WinGProtectionRoutingStage.CaptureAuthority()
WinGProtectionRoutingStage.ExecuteMutationAsync()
WinGProtectionRoutingStage.RollbackMutationAsync()
AddonRoutingRuntime.Create(...)
```

The old class may remain unreferenced if deleting it would widen the PR unnecessarily.

Do not spend this PR deleting the entire old route stack unless a strictly mechanical dead-code deletion is clearly isolated and very small.

A later legacy cleanup may delete:

- `WinGProtectionRoutingStage`;
- old route-owned WING composition paths;
- `StartRoutingObservation()` if fully dead;
- other unused legacy routing classes.

Policy B does not need them.

---

# 8. Explicit stock authority release

Suppression belongs to Addon controller authority, so it must be released only when controller authority has actually become stock-safe.

Relevant supported flows:

- Enable Center M and Restart;
- stock-safe uninstall preparation;
- process teardown after an already-completed stock restoration.

PR #470 established the front-button teardown dependency:

```text
front-button runtime
→ presentation owner
```

Policy B must extend the stock release ordering conceptually to:

```text
stop/dispose MsiClawFrontButtonRuntime
→ neutral/stop publisher
→ detach/retire virtual presentation
→ release DirectInput
→ restore and verify same physical MSI Claw PID1901
→ complete existing stock-safe authority-release boundary
→ Disarm Win+G suppression
→ continue Center M root enable / restart or uninstall completion
```

Do not disarm at the beginning of Enable-and-Restart.

Do not disarm while a live Addon virtual controller remains exposed.

Reuse the existing `CenterMRebootAuthorityTransition`, presentation release seam, physical ownership release seam, stock baseline, and PR12 uninstall preparation.

Do not introduce another Center M transition coordinator.

If immediate process disposal follows a proven stock-safe release and naturally removes the hook, avoid redundant teardown purely for checklist symmetry. The key invariant is that suppression is not released before stock authority is safe.

---

# 9. Controlled Runtime restart and unexpected Runtime death

A process-local keyboard hook disappears with the Runtime process.

Do not make suppression persistent outside the Runtime process using:

- a service;
- supervisor;
- helper process;
- registry/GPO system-wide Game Bar policy;
- Task Scheduler activity whose only job is maintaining the hook.

For a controlled Runtime restart while Center M remains Disabled:

```text
old Runtime exits
→ process hook disappears
→ mandatory Addon Runtime starts again
→ Full1902 startup reconciles PID1902 authority
→ hook installs/arms before new live presentation
```

Unexpected Runtime death remains the broader known runtime-availability issue. Do not solve that unrelated reliability problem here.

---

# 10. WING mapping policy remains out of scope

Do not change:

```text
WingMappingSettings
WingMapping
IWingMappingPreference
WingGestureRecognizer
WingActionDispatcher action catalog/defaults
Single/Double mapping values
KeyboardHotkey action semantics
LaunchApplication action semantics
```

The current `SteamButton` action should simply become safely deliverable when Policy-B suppression readiness is true.

Do not decide whether WING Single should ultimately be:

- Steam Button;
- Addon Quick Settings Overlay;
- another user-selected action.

Only this policy is fixed:

```text
Addon controller authority active
→ native Xbox Game Bar must not surface from WING / Win+G
```

---

# 11. OEM1 scope

PR #470 already separated OEM1 from legacy physical routing.

Do not redesign OEM1 in this PR.

Do not change:

- `Oem1MappingSettings` contract;
- Normal/Routing slot semantics;
- OEM1 gesture behavior;
- Quick Access pulse behavior;
- Big Picture behavior;
- legacy dummy Center M helper cleanup beyond strictly dead mechanical code.

Policy B may mechanically touch `MsiClawFrontButtonRuntime.Create(...)` composition only to pass the new suppression-readiness callback for WING.

---

# 12. Other explicit out-of-scope items

Do not change except for strictly required mechanical compile/test fixes:

- final OEM1/WING button assignment UX;
- Overlay button assignment;
- M1/M2 Xbox360 remapping;
- rumble / vibration strength;
- battery charge limit;
- deletion of the old entire `AddonRoutingRuntime` stack;
- Steam/BPM detection semantics;
- Xbox360 ↔ SteamDeck selection policy;
- PID1901/PID1902 authority rules;
- HidHide baseline/policy;
- DirectInput acquisition/recovery semantics;
- VIIPER server/bus/device ownership;
- PR13 Velopack / Windows uninstall interception.

Do not add a new general abstraction merely because old routing code remains in source.

---

# 13. Preferred implementation shape

Use `AddonProcessHost`, which already owns:

```text
_winGSuppressionGuard
_physicalOwnership
_presentationOwnership
_frontButtonRuntime
_presentationReconcile
_ownedControllerRecovery
_deviceArrivalWatcher
```

A small private helper is sufficient, conceptually:

```csharp
private bool EnsureAddonAuthorityWinGSuppression()
{
    _winGSuppressionGuard.Start();
    return _winGSuppressionGuard.EnsureArmed()
        && _winGSuppressionGuard.IsArmed;
}
```

Exact implementation is not mandated.

Important constraints:

- call `Start()` from exactly one process-owned installation path;
- make that installation happen before first live Disabled-mode presentation;
- do not reinstall per Steam/BPM event;
- do not add a persisted suppression flag;
- do not add an `AddonAuthoritySuppressionActive` boolean duplicating `IsArmed`;
- derive policy from the existing Center M Disabled / Full1902 startup path;
- keep `MsiClawAddonPresentation` presentation-only;
- keep `MsiClawFrontButtonRuntime` feature-local;
- let `WinGSuppressionGuard` remain the low-level hook owner.

---

# 14. Required tests

## 14.1 Preserve low-level guard tests

Keep `WinGSuppressionGuardTests` covering:

- Win+G suppressed when armed;
- unrelated keys pass through;
- Win modifier cleanup;
- disarm;
- hook install failure;
- disposal/idempotency.

Do not weaken these tests to fit the new ownership lifecycle.

## 14.2 Disabled startup ordering

Add focused process-host/lifecycle coverage proving:

```text
Center M Disabled
→ physical owner ready
→ hook installed
→ arm succeeds
→ IsArmed true
→ first AttachInitialAsync allowed
```

The test must fail if a live first presentation can be attached before the arm result is known.

Use fakes/seams; do not require a real keyboard hook in unit tests.

## 14.3 Arm failure fail-close

Prove:

```text
Center M Disabled
+ physical ownership ready
+ suppression install/arm failure
→ no initial live presentation
→ no publisher start
→ no front-button WING custom delivery
```

Do not require retry machinery.

## 14.4 WING readiness seam becomes live only when armed

Add/adjust `MsiClawFrontButtonRuntimeTests` proving:

```text
nativeWinGSuppressionReady == false
→ CaptureWingAuthority().Active == false
→ WING SteamButton action not delivered

nativeWinGSuppressionReady == true
→ WING authority active during valid runtime lifetime
→ WING SteamButton delegates to presentation TryRequestSteamPulse()
```

Production composition test/source guard must prove the callback is based on:

```csharp
_winGSuppressionGuard.IsArmed
```

not a new duplicated boolean.

## 14.5 Presentation changes do not own suppression

Prove after successful admission:

```text
Xbox360 ↔ SteamDeck
Steam game start/exit
BPM enter/exit
```

cause:

```text
zero Disarm()
zero hook reinstall
```

## 14.6 Overlay capture does not own suppression

Prove Overlay pause/resume leaves suppression armed.

Do not require WING pulse delivery while the SteamDeck publisher is Overlay-paused; PR #470 already correctly makes the pulse seam unavailable there.

The suppression itself must remain armed.

## 14.7 Physical recovery does not own suppression

For realistic PR8–PR10 owned recovery paths:

- temporary DirectInput loss does not intentionally disarm;
- same-device PnP recovery does not intentionally disarm;
- any recreated/reconciled live presentation requires `IsArmed` true.

Do not invent instruction-level race tests.

## 14.8 Authority release ordering

Prove the real Enable Center M / stock-safe release path does not disarm while a live Addon virtual presentation remains active.

After the existing stock-safe boundary is proven, suppression may be disarmed/disposed.

Also prove Center M Enabled startup does not arm Full1902 suppression merely because Runtime starts.

## 14.9 Regression for PR #470

Preserve all #470 invariants:

- no production `AddonRoutingRuntime.Create(...)`;
- no production `StartRoutingObservation()`;
- OEM1/WING action path remains independent of legacy routing;
- no second VIIPER/publisher created for front-button pulses;
- OEM1 Normal/Routing mapping continues to follow actual `ActivePresentation`;
- WING action still uses `MsiClawAddonPresentation.TryRequestSteamPulse()`.

---

# 15. Logging

Add concise lifecycle logs for real failures/transitions only.

Useful conceptual events:

```text
Full1902WinGSuppressionArmStarted
Full1902WinGSuppressionArmed
Full1902WinGSuppressionArmFailed
Full1902WinGSuppressionReleased
```

Include useful authority context such as `CenterMDisabled` / `AddonAuthority`.

Do not log every keyboard event beyond existing Debug hook diagnostics.

Do not add telemetry/polling to prove the guard stays armed.

---

# 16. Documentation cleanup

Inspect active user documentation only if needed:

```text
README.md
docs/KOREAN_USER_GUIDE.md
```

Update only if they still imply native Game Bar availability changes with Steam routing state.

Do not invent final WING/Overlay mapping text.

Historical work orders remain historical records. Do not bulk-edit older work orders.

This Policy B document itself is the authoritative post-#470 implementation contract.

---

# 17. No overengineering

Do not add:

- `WingAuthorityManager`;
- `GameBarPolicyService`;
- `SuppressionState` persistence;
- a new controller lifecycle state machine;
- a hook epoch/barrier;
- another Center M authority source;
- background polling of `IsArmed`;
- retry loops;
- Windows service/watchdog;
- system-wide Game Bar registry mutation.

The existing owners are sufficient:

```text
AddonProcessHost              → orchestration / authority lifetime
WinGSuppressionGuard          → low-level Win+G hook
MsiClawAddonPresentation      → one live VIIPER presentation/publisher
MsiClawFrontButtonRuntime     → OEM1/WING gesture/action delivery
```

Protect real supported lifecycle failures only:

- sleep / hibernate / resume;
- controlled Runtime restart;
- physical device loss / PnP return;
- DirectInput owned-session recovery;
- explicit Center M authority release;
- stock-safe uninstall preparation;
- actual hook install/arm failure.

Do not add complexity for narrow scheduler interleavings that have no realistic handheld lifecycle path.

---

# 18. Acceptance criteria

## Product behavior

- [ ] Center M Disabled / Addon authority cannot expose its first live virtual controller while native Win+G suppression is unarmed.
- [ ] Failed hook install/arm causes fail-closed presentation behavior rather than native Game Bar fallback.
- [ ] Once Addon authority is live, suppression stays armed across Xbox360 ↔ SteamDeck transitions.
- [ ] Steam game / BPM state changes do not own suppression lifetime.
- [ ] Overlay capture pause/resume does not own suppression lifetime.
- [ ] temporary physical/DirectInput loss does not release suppression authority.
- [ ] suspend/hibernate/resume does not intentionally release suppression authority.
- [ ] WING custom delivery becomes active only when the existing suppression guard reports `IsArmed`.
- [ ] explicit stock authority release retires Addon output before suppression is released.
- [ ] Center M Enabled does not arm Full1902 suppression merely because Runtime is running.

## Architecture

- [ ] `WinGSuppressionGuard` remains the only low-level keyboard-hook implementation.
- [ ] hook installation occurs early enough to be proven before first Disabled-mode `AttachInitialAsync()`.
- [ ] production WING authority is `MsiClawFrontButtonRuntime` + suppression readiness, not `WinGProtectionRoutingStage`.
- [ ] production does not compose `AddonRoutingRuntime`.
- [ ] `MsiClawAddonPresentation` remains VIIPER/presentation-only.
- [ ] `MsiClawFrontButtonRuntime` remains feature-local and does not become controller authority.
- [ ] no new authority manager/state source is introduced.
- [ ] no final WING mapping/default policy is changed.
- [ ] no OEM1 mapping policy is changed.
- [ ] no PR13 installer interception work is included.

## Lifecycle / failure

- [ ] controlled Runtime teardown remains idempotent.
- [ ] hook disposal remains process-owned and idempotent.
- [ ] arm/install failure is observable and blocks live presentation.
- [ ] recovery does not silently expose live presentation with suppression unarmed.
- [ ] no polling/watchdog/retry service is added.

---

# 19. Validation

Run at minimum:

```text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
git diff --check
```

Also inspect production references for:

```text
WinGSuppressionGuard
EnsureArmed
IsArmed
Disarm
MsiClawFrontButtonRuntime.Create
CaptureWingAuthority
TryRequestSteamPulse
AttachInitialAsync
AddonRoutingRuntime.Create
StartRoutingObservation
```

Required review conclusions:

```text
1. Full1902 Disabled first live presentation has a direct suppression precondition.
2. WING production readiness is bound to the existing WinGSuppressionGuard.IsArmed seam.
3. #470 legacy-routing removal is not regressed.
4. No second VIIPER/publisher/authority is introduced.
```

---

# 20. Manual MSI Claw smoke validation

If supported hardware is available:

```text
1. Boot with MSI Center M Disabled.
2. Confirm normal desktop state presents Xbox360.
3. Press WING: native Xbox Game Bar must not appear.
4. Start a Steam game and confirm presentation switches to SteamDeck.
5. Press WING: native Xbox Game Bar must not appear; configured WING action may execute according to current mapping.
6. Exit the Steam game and return to Xbox360; native Game Bar must remain blocked.
7. Enter/exit BPM; suppression must remain continuous.
8. Open/close Addon Overlay; native Game Bar must remain blocked while Addon authority remains active.
9. Sleep/resume; verify Addon authority and Game Bar suppression remain correct.
10. If practical, exercise real same-device PnP/DirectInput recovery and verify no native Game Bar exposure.
11. Use Enable Center M and Restart.
12. After stock authority is restored, confirm the Addon Full1902 suppression lifetime is no longer active.
```

Do not substitute synthetic instruction-level race testing for these real lifecycle checks.
