# Work Order — Full1902 Policy B: Bind WING / Game Bar Suppression to Addon Controller Authority

## Status

Focused implementation work order for moving native `Win+G` / Xbox Game Bar suppression from the old Steam-route lifetime to the Full PID1902 Addon controller-authority lifetime.

This work order follows the merged Full1902 Policy A removal of the `SteamInputRoutingEnabled` master switch.

This work order is intentionally **not numbered PR13**. PR13 remains reserved for the later Windows / Velopack uninstall-entry integration.

Code-review baseline used to prepare this work order:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     4a5b872f0cc8670144fad452d7fd662c2783b4c8
```

Before implementation, read and treat these documents as design authorities:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/FULL1902_POLICY_A_REMOVE_STEAM_INPUT_ROUTING_MASTER_SWITCH_WORK_ORDER.md`
- current PR5–PR10 Full1902 physical ownership / presentation / recovery work orders where relevant to startup, recovery, suspend/resume, and authority release.

The application is pre-release. Implement the current Full1902 product policy directly. Do not preserve the obsolete idea that native Game Bar suppression exists only while a Steam routing session is active.

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
```

The confirmed product policy is:

> While the Addon owns the controller, the WING button must never surface native Xbox Game Bar.

This PR is only about **native Game Bar suppression ownership/lifetime**.

It does **not** decide the final user-visible WING button action.

---

# 2. Why this is required now

The old architecture treated Win+G suppression as one routing pipeline stage:

```text
Steam routing session enters
→ WinGProtectionRoutingStage.ExecuteMutationAsync()
→ WinGSuppressionGuard.EnsureArmed()

Steam routing session leaves / rolls back
→ WinGProtectionRoutingStage.RollbackMutationAsync()
→ WinGSuppressionGuard.Disarm()
```

That lifecycle no longer matches Full1902.

Full1902 authority is:

```text
Center M Disabled
→ Addon owns PID1902 continuously
→ Steam/BPM inactive = Xbox360 presentation
→ Steam/BPM active   = SteamDeck presentation
```

There is no valid reason for native Game Bar to become available merely because the virtual presentation changed to Xbox360 or because Steam/BPM became inactive.

More importantly, the current Disabled-mode Full1902 path does not compose the legacy routing runtime at all:

```text
legacyRoutingAllowed = false
→ AddonRoutingRuntime = null
```

Therefore the existing route-stage suppression lifetime is not the correct Full1902 authority mechanism.

---

# 3. Current code-review findings

## 3.1 `WinGSuppressionGuard` is already the correct low-level primitive

Current file:

```text
src/SteamInputAddonforClaw/GameBar/WinGSuppressionGuard.cs
```

It already owns the process-local low-level keyboard-hook mechanics:

- `Start()` installs the hook;
- `EnsureArmed()` enables Win+G suppression;
- `Disarm()` disables suppression while leaving the hook installed;
- `Dispose()` removes the hook;
- current Win-key cleanup prevents a suppressed chord from leaving the modifier logically stuck.

Do **not** replace this implementation with:

- a new global keyboard service;
- registry policy disabling Game Bar system-wide;
- Group Policy mutation;
- a second hook implementation;
- a background watchdog.

Reuse this guard.

## 3.2 `WinGProtectionRoutingStage` currently binds suppression to the obsolete route lifetime

Current file:

```text
src/SteamInputAddonforClaw/GameBar/WinGProtectionRoutingStage.cs
```

It is an `IRoutingPipelineStage` whose mutation arm/rollback directly calls:

```csharp
_guard.EnsureArmed();
_guard.Disarm();
```

That remains a legacy route mechanism, not the Full1902 authority boundary.

It also exposes:

```csharp
CaptureAuthority()
```

with an active/epoch snapshot used by the existing WING gesture path.

Do not casually delete or repurpose that gesture-authority behavior in this PR.

## 3.3 `AddonRoutingRuntime` couples the same stage to legacy WING gesture delivery

Current file:

```text
src/SteamInputAddonforClaw/Routing/AddonRoutingRuntime.cs
```

It currently creates:

```csharp
var winGProtectionStage = new WinGProtectionRoutingStage(winGSuppressionGuard);
```

and feeds its authority snapshot into:

```text
ConfigureWingActionPath(...)
```

This means `WinGProtectionRoutingStage` currently serves two historical roles:

1. route-scoped Game Bar suppression;
2. route-scoped WING gesture stale-delivery authority.

The second role is tied to the not-yet-finalized WING mapping policy.

For this focused PR, **do not redesign the final WING gesture/mapping authority** merely to delete the old stage.

## 3.4 `AddonProcessHost` is the correct Full1902 orchestration boundary

Current file:

```text
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
```

It already owns the single process-level:

```text
WinGSuppressionGuard
```

and also owns the Full1902 process-lifetime controller objects:

```text
_physicalOwnership
_presentationOwnership
_presentationReconcile
_ownedControllerRecovery
_deviceArrivalWatcher
```

This is where the product already knows whether the current runtime successfully entered the Full1902 Disabled-mode Addon authority path.

Therefore Full1902 suppression should be orchestrated here, at the real authority / live-presentation boundary, rather than introducing a new `WingAuthorityManager` or another controller state authority.

## 3.5 Full1902 presentation owner should remain presentation-only

Current file:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs
```

Its contract is intentionally narrow:

```text
one canonical VIIPER runtime
one active typed presentation
one publisher
```

It explicitly does not own Center M authority, PID1902, HidHide, or Steam/BPM authority.

Do **not** move keyboard-hook/Game Bar policy into this VIIPER presentation owner merely because `AttachInitialAsync()` is the first live-output step.

The process host should enforce the suppression precondition before it calls the presentation owner.

---

# 4. Required Full1902 suppression lifecycle

## 4.1 Center M Enabled / stock authority

Full1902 authority contract:

```text
Center M Enabled
→ MSI / stock controller authority
→ desired PID1901
→ no Full1902 physical ownership
→ no Full1902 VIIPER presentation
```

In this state this PR must not independently arm a new Full1902 Game Bar suppression lifetime.

Do not globally disable Game Bar just because the Addon process is running.

The still-present legacy routing path may retain its existing route-local behavior until that legacy stack is removed in a later focused cleanup. Do not expand this PR into deleting `AddonRoutingRuntime` or finalizing legacy WING behavior.

## 4.2 Center M Disabled / Addon authority startup

The guard may be installed earlier in process startup if that is already the current lifecycle, but **suppression must be armed and proven before the first live virtual controller presentation is exposed**.

Required ordering conceptually:

```text
Center M exactly Disabled
→ Full1902 Disabled admission succeeds
→ physical ownership reaches safe PID1902 + DirectInput + HidHide state
→ Win+G suppression hook is installed
→ EnsureArmed() succeeds / IsArmed is true
→ only then AttachInitialAsync(...)
→ publisher becomes live
```

Do not reorder physical/HidHide ownership merely to satisfy this work order.

The new requirement is one extra gate immediately before the first live presentation boundary:

```text
Addon authority + ready physical owner
+ Game Bar suppression proven armed
→ live virtual presentation allowed
```

## 4.3 Arm failure is fail-closed

If Disabled-mode Addon authority is otherwise ready but the WING/Game Bar suppression guard cannot be proven armed:

```text
DO NOT attach a live Xbox360 presentation
DO NOT attach a live SteamDeck presentation
DO NOT start a publisher
DO NOT fall back to native Win+G / Game Bar behavior
```

Keep controller output neutral/detached according to the existing Full1902 fail-close boundary and log a clear reason such as:

```text
WinGSuppressionUnavailable
WinGSuppressionArmFailed
```

Exact enum/string naming is not mandated.

Do not create retry loops or a watchdog. A later normal Runtime restart may retry through the ordinary startup path.

## 4.4 Suppression remains armed across presentation switching

Once Addon authority is live:

```text
Xbox360 → SteamDeck
SteamDeck → Xbox360
Steam game start/exit
Big Picture enter/exit
```

must **not**:

- call `Disarm()`;
- call `EnsureArmed()` on every switch;
- reinstall the keyboard hook;
- create a new suppression epoch.

Presentation switching is not an authority change.

## 4.5 Overlay capture does not change suppression authority

The current Overlay capture path temporarily pauses/neutralizes game-facing publication while keeping the same controller authority.

Therefore:

```text
Overlay show / capture
Overlay hide / resume
```

must not disarm native Game Bar suppression.

Do not tie suppression to `_overlayCaptureActive`.

## 4.6 Suspend / hibernate / resume does not release suppression authority

Center M Disabled remains Addon authority across suspend/hibernate/resume.

Do not intentionally disarm the guard for suspend and re-arm it as a new authority session on resume.

If the existing Windows keyboard hook survives normally, leave it alone.

Do not add polling, periodic reinstallation, a resume epoch, or a new synchronization state solely for theoretical notification timing.

If real evidence later shows Windows destroys the hook across a normal supported resume path, handle that as a separate concrete lifecycle defect with evidence.

## 4.7 PID1902 loss / PnP recovery does not release suppression authority

Temporary physical controller loss, PID1902 session loss, or same-device PnP re-enumeration while Center M remains Disabled is still Addon authority.

During recovery:

```text
virtual output neutral / recovery in progress
→ Win+G suppression remains armed
→ physical ownership recovers
→ presentation resumes/reconciles
```

Do not disarm merely because DirectInput or the physical device is temporarily unavailable.

If a recovery path must recreate/reattach a presentation after it had been retired, it must not create a live presentation when `WinGSuppressionGuard.IsArmed` is false. Reuse one small process-host precondition/helper; do not build a second recovery manager.

---

# 5. Explicit authority release

Suppression belongs to Addon controller authority, so it must be released only when Addon controller authority is actually being returned to stock.

Supported release examples:

```text
Enable Center M and Restart
stock-safe uninstall preparation
process teardown after the controller has already been made safe for the intended lifecycle
```

For `Enable Center M and Restart`, do not disarm at the beginning of the button action.

Keep suppression active while live Addon-owned controller output is still present.

Conceptual ordering:

```text
neutral / stop publisher
→ detach / retire virtual presentation
→ release DirectInput / restore same MSI Claw to PID1901
→ complete the existing stock-safe authority-release safety boundary
→ Disarm Win+G suppression
→ continue Center M startup-root enable / restart flow as already designed
```

The exact call site should reuse the current centralized authority transition / stock-restoration orchestration. Do not introduce a second Center M transition manager.

If the current teardown path already disposes the guard shortly after proven stock restoration, a separate `Disarm()` call may be unnecessary there. Avoid duplicate teardown just to satisfy a checklist.

For uninstall, reuse the current PR12 stock-safe preparation path if a direct Runtime release seam is already available. **Do not implement PR13 installer interception in this work order.**

---

# 6. Controlled Runtime restart and unexpected Runtime death

A process-local keyboard hook naturally disappears when the Runtime process exits.

Do not try to make Win+G suppression persistent outside the Runtime process by adding:

- a Windows service;
- a supervisor;
- a second helper process;
- system-wide Game Bar registry policy;
- Task Scheduler activity whose only job is keeping the hook alive.

For a controlled Runtime restart while Center M remains Disabled:

```text
old Runtime exits
→ process hook disappears
→ mandatory Addon Runtime starts again
→ Full1902 startup reconciles controller authority
→ suppression is armed before the new live virtual presentation
```

Unexpected Runtime death remains the known broader Full1902 runtime-availability reliability problem. Do not solve that unrelated problem inside this focused suppression PR.

---

# 7. Legacy `WinGProtectionRoutingStage` boundary for this PR

Keep this PR narrow.

Because `WinGProtectionRoutingStage.CaptureAuthority()` currently participates in the existing legacy WING gesture path, **do not require deleting the stage in this work order**.

Target transitional structure:

```text
Full1902 Center M Disabled path
→ AddonProcessHost owns authority-bound suppression
→ does NOT depend on WinGProtectionRoutingStage

Legacy routing path, while still present
→ may retain WinGProtectionRoutingStage and its existing route-local WING gesture authority
```

The two supported controller-authority branches are mutually exclusive at startup, so this does not require a new shared arbitration layer.

Add comments only where necessary to make the transitional ownership clear.

A later focused WING/legacy-routing cleanup can decide whether to:

- delete `WinGProtectionRoutingStage` entirely;
- replace its route-authority snapshot with the final WING action authority;
- remove legacy routing-specific WING semantics.

Do not pull that future design into this PR.

---

# 8. WING mapping policy is explicitly out of scope

Do not change:

```text
WingMappingSettings
WingMapping
IWingMappingPreference
WingGestureRecognizer
WingActionDispatcher action catalog/defaults
Single/Double mappings
SteamButton action semantics
KeyboardHotkey / LaunchApplication actions
```

Do not decide in this work order whether WING Single should ultimately be:

- Steam Button;
- Addon Quick Settings Overlay;
- another user-selected action.

The only fixed policy here is:

```text
Addon controller authority active
→ native Xbox Game Bar must not appear from WING / Win+G
```

Custom WING action delivery is a separate policy layer.

---

# 9. Other explicit out-of-scope items

Do not change any of the following except strictly mechanical compile fixes:

- OEM1 / Center M button remapping policy;
- OEM1 dummy `MSI Center M.exe` helper suppression cleanup;
- Overlay button assignment;
- M1/M2 Xbox360 remapping;
- rumble / vibration strength;
- battery charge limit;
- `SteamInputRoutingEnabled` compatibility code (it is already removed);
- `legacyRoutingAllowed` removal;
- deletion of the old `AddonRoutingRuntime` / legacy route pipeline;
- Steam/BPM detection semantics;
- Xbox360 ↔ SteamDeck presentation selection;
- PID1901/PID1902 authority rules;
- HidHide baseline/policy;
- DirectInput acquisition/recovery;
- VIIPER server/bus/device ownership;
- Game Bar foreground presentation mechanics in the old legacy route;
- PR13 Velopack / Windows uninstall interception.

---

# 10. Preferred implementation shape

Do not add a new public subsystem.

A small private process-host helper is enough, conceptually:

```csharp
private bool EnsureAddonAuthorityWinGSuppression()
{
    _winGSuppressionGuard.Start(); // only if current lifecycle does not already install it earlier
    return _winGSuppressionGuard.EnsureArmed() && _winGSuppressionGuard.IsArmed;
}
```

Exact implementation is not mandated.

Important points:

- preserve current idempotent hook ownership;
- do not repeatedly `Start()`/arm per Steam event;
- do not add a second authority boolean;
- derive the decision from the already-selected Full1902 Disabled-mode startup path / real ownership objects;
- use the existing process-host teardown for disposal;
- use one narrow check before first/recreated live presentation.

If `Start()` is already called earlier in `AddonProcessHost`, keep that installation location and only add the authority-bound arm check where needed. Do not duplicate hook installation.

---

# 11. Required tests

## 11.1 Preserve low-level guard tests

Keep the existing `WinGSuppressionGuardTests` behavior covering:

- Win+G suppression when armed;
- unrelated keys passing through;
- modifier cleanup;
- disarm;
- hook disposal/idempotency.

Do not weaken those tests while changing ownership lifetime.

## 11.2 Full1902 Disabled startup ordering

Add focused process-host/lifecycle coverage proving:

```text
Center M Disabled / physical owner ready
→ suppression arm succeeds
→ only then first virtual presentation may attach/start
```

Use test seams/fakes rather than a real keyboard hook in unit tests.

The test must fail if the first presentation can become live before the arm result is known.

## 11.3 Arm failure fail-close

Prove:

```text
Center M Disabled
+ physical ownership ready
+ suppression arm failure
→ no live initial presentation
→ no publisher start
→ no native Game Bar fallback
```

Do not require a retry-state machine in the test.

## 11.4 Presentation changes do not change suppression

Prove that after successful authority admission:

```text
Xbox360 ↔ SteamDeck
Steam game start/exit
BPM enter/exit
```

cause zero `Disarm()` calls and do not reinstall the hook.

## 11.5 Overlay capture does not change suppression

Prove Overlay pause/resume leaves suppression armed.

## 11.6 Physical recovery does not change suppression

For the existing realistic owned-controller recovery paths, verify that temporary DirectInput loss / same-device PnP recovery does not intentionally disarm suppression and that a reattached live presentation still requires suppression to be armed.

Do not invent instruction-level race tests around callback ordering.

## 11.7 Authority release

Prove the real stock-release path does not disarm while a live Addon virtual presentation is still active.

After the current authority-release safety boundary is proven, suppression is disarmed/disposed according to the existing process lifecycle.

Also prove Center M Enabled startup does not arm the new Full1902 authority suppression path.

## 11.8 Preserve WING mapping contracts

Existing WING mapping persistence/action tests should continue to pass unchanged except for compile-required setup changes.

No test should assert a new final WING Single/Double default as part of this PR.

---

# 12. Logging

Add concise lifecycle logs only where they help diagnose a real device/user failure.

Useful events include conceptually:

```text
Full1902WinGSuppressionArmStarted
Full1902WinGSuppressionArmed
Full1902WinGSuppressionArmFailed
Full1902WinGSuppressionReleased
```

Include the authority context (`CenterMDisabled` / `AddonAuthority`) where useful.

Do not log every keyboard event beyond the existing Debug-level hook diagnostics.

Do not add telemetry/polling solely to prove the guard is still armed.

---

# 13. Documentation cleanup

Inspect current active user documentation after Policy A:

```text
README.md
docs/KOREAN_USER_GUIDE.md
```

Only update them if they still claim that native Game Bar availability changes with Steam routing state.

Do **not** invent final WING/Overlay mapping text.

Historical `docs/work-order/*` files remain historical records and must not be bulk-edited.

---

# 14. No overengineering

Do not add:

- `WingAuthorityManager`;
- `GameBarPolicyService`;
- a persistent suppression database;
- a new lifecycle state machine;
- an epoch/barrier solely for hook ownership;
- a second Center M authority source;
- background polling of `IsArmed`;
- automatic retry loops;
- a Windows service / watchdog;
- system-wide Game Bar registry mutation.

The existing Full1902 startup/physical/presentation authority and the existing `WinGSuppressionGuard` are sufficient.

Real supported lifecycle safety must remain intact, especially:

- suspend / hibernate / resume;
- controlled Runtime restart;
- physical device loss / PnP return;
- DirectInput owned-session recovery;
- explicit Center M authority release;
- uninstall stock-safe preparation;
- actual hook arm/install failure.

Do not add complexity for narrow instruction-level interleavings that have no realistic handheld lifecycle path.

---

# 15. Acceptance criteria

The PR is complete only when all of the following are true.

## Product behavior

- [ ] Center M Disabled / Addon authority cannot expose its first live virtual controller while native Win+G suppression is unarmed.
- [ ] Failed suppression arm causes a fail-closed controller-presentation result rather than native Game Bar fallback.
- [ ] Once Addon authority is live, suppression remains armed across Xbox360 ↔ SteamDeck transitions.
- [ ] Steam game / BPM state changes do not own suppression lifetime.
- [ ] Overlay capture pause/resume does not own suppression lifetime.
- [ ] temporary physical/DirectInput loss does not release suppression authority.
- [ ] suspend/hibernate/resume does not intentionally release suppression authority.
- [ ] explicit stock authority release retires live Addon controller output before suppression is released.
- [ ] Center M Enabled does not arm the new Full1902 suppression path merely because the Addon Runtime is running.

## Architecture

- [ ] `WinGSuppressionGuard` remains the one low-level keyboard-hook implementation.
- [ ] Full1902 Disabled-mode suppression no longer depends on `WinGProtectionRoutingStage` being composed.
- [ ] `MsiClawAddonPresentation` remains VIIPER/presentation-only and does not become a Game Bar policy owner.
- [ ] no new controller authority manager/state source is introduced.
- [ ] legacy `WinGProtectionRoutingStage` may remain for its current legacy route/WING gesture role; deleting/replacing it is not required here.
- [ ] no WING mapping/default/action policy is changed.
- [ ] no OEM1 policy is changed.
- [ ] no PR13 installer interception work is included.

## Lifecycle / failure

- [ ] controlled Runtime teardown remains idempotent.
- [ ] hook disposal remains process-owned and idempotent.
- [ ] arm/install failure is observable in logs and blocks live presentation.
- [ ] no polling/watchdog/retry service is added.

---

# 16. Validation

Run at minimum:

```text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
git diff --check
```

Also inspect repository references for:

```text
WinGSuppressionGuard
WinGProtectionRoutingStage
EnsureArmed
Disarm
WingRouteAuthoritySnapshot
ConfigureWingActionPath
```

The review should confirm that Full1902 Disabled-mode live presentation has a direct authority-bound suppression precondition and is not accidentally depending on the old routing stage.

---

# 17. Manual MSI Claw smoke validation

If supported hardware is available:

```text
1. Boot with MSI Center M Disabled.
2. Confirm normal non-Steam desktop state presents Xbox360.
3. Press WING repeatedly: native Xbox Game Bar must never appear.
4. Launch a Steam game and confirm SteamDeck presentation.
5. Press WING: native Xbox Game Bar must still never appear.
6. Exit the game and confirm Xbox360 presentation returns without making Game Bar available.
7. Enter/leave BPM and repeat the WING check.
8. Exercise Overlay show/hide; WING must not leak native Game Bar during capture/resume.
9. Sleep/resume and repeat.
10. If practical, exercise one real controller PnP/recovery path and repeat.
11. Use Enable Center M and Restart; after stock authority is restored, verify the Addon no longer applies its Full1902 suppression lifetime.
```

Do not use this smoke test to decide final WING custom mapping behavior.

---

# 18. Review guidance

Blocking findings for this PR include:

- Full1902 Disabled mode can attach/start a live virtual presentation before suppression is proven armed;
- native Game Bar becomes available when Steam/BPM becomes inactive but Addon authority remains active;
- X360 ↔ SteamDeck switching disarms/rearms suppression;
- PnP/DirectInput recovery releases suppression authority while Center M remains Disabled;
- suppression is released before the live Addon controller presentation is safely retired during explicit stock handoff;
- hook arm failure silently continues with live controller output;
- the implementation introduces global Game Bar registry/policy mutation or a watchdog/service without evidence;
- the PR changes final WING/OEM1/Overlay mapping policy.

Non-blocking / separate work:

- deleting the remaining legacy `WinGProtectionRoutingStage` after final WING policy is decided;
- deleting `AddonRoutingRuntime` / `legacyRoutingAllowed`;
- choosing WING Single/Double defaults;
- assigning WING or OEM1 to the Addon Quick Settings Overlay;
- OEM1 dummy-helper removal;
- PR13 uninstall interception.

---

# 19. Final invariant

After this PR:

```text
Controller authority decides native Game Bar suppression.

Center M Enabled / MSI authority
→ Full1902 suppression not active

Center M Disabled / Addon authority
→ native WING Win+G / Xbox Game Bar suppressed continuously
→ Steam/BPM only changes Xbox360 vs SteamDeck presentation
→ presentation changes do not change suppression authority
```

Native Game Bar suppression is therefore no longer conceptually owned by a Steam routing session.
