# Work Order — PR2.5: Mandatory Controller Runtime Lifetime Foundation

## Status

Implementation work order for the small intermediate PR between:

```text
PR2   Addon-Owned HidHide Baseline Foundation
  ↓
PR2.5 Mandatory Controller Runtime Lifetime Foundation
  ↓
PR3   Reboot-Bound Controller Authority Transition
```

This PR exists to establish one product/lifecycle contract before Center M Disable is wired to the new controller authority flow:

> **When MSI Center M is Disabled, the background Addon Runtime and its system tray are mandatory for the Windows interactive session. Only the frontend/UI window is disposable.**

This is still a foundation PR.

It does **not** acquire PID1902, does **not** apply the new persistent HidHide baseline, does **not** perform the reboot-bound Center M transition, and does **not** add crash-supervisor machinery.

Before implementation, read and treat the following as current design authorities:

- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/work-order/PR2_ADDON_OWNED_HIDHIDE_BASELINE_WORK_ORDER.md`
- current `main` implementation of:
  - `Hosting/AddonProcessHost.cs`
  - `Hosting/RuntimeProcessApplication.cs`
  - `Lifecycle/UserTerminationGuard.cs`
  - `Lifecycle/SystemTrayIcon.cs`
  - `Install/StartupRegistration.cs`
  - `Settings/StartupSettingsCoordinator.cs`
  - `CenterMStartup/CenterMStartupControl.cs`
  - `Frontend/InProcessAddonFrontendControl.cs`
  - `SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs`

The project is pre-release. Do not add backward-compatibility layers for obsolete runtime-exit semantics.

---

## 1. Goal

Make the existing background Runtime + tray lifecycle obey the future Addon-owned controller contract **before** the product actually performs Center M Disable → PID1902 ownership.

The desired product rule is:

```text
Center M Enabled
    → MSI / stock controller authority
    → Addon background startup may remain a normal user preference
    → ordinary Addon Exit/Restart remains available when existing safety guards allow it

Center M Disabled
    → Addon is the selected controller authority
    → background Runtime is mandatory
    → system tray remains alive with the Runtime
    → Launch at Windows startup is mandatory ON
    → ordinary user Runtime Exit is not supported
    → ordinary tray Restart is also disabled in this initial simple contract
    → frontend/settings window may open/close freely
```

The official way to leave this future mandatory Runtime mode is:

```text
Enable MSI Center M and Restart
```

PR3 will implement that reboot-bound authority transition.

PR2.5 only establishes the lifetime/startup foundation that PR3 can rely on.

---

## 2. Product semantics

### 2.1 Runtime and tray are the persistent platform process

The existing architecture already separates the Runtime process from the frontend window.

Preserve and strengthen that separation:

```text
Runtime process
    ├─ controller/device runtime ownership
    ├─ named-pipe frontend server
    ├─ system tray
    └─ background lifecycle

Frontend/UI process or window
    └─ disposable presentation surface
```

When Center M is Disabled:

```text
close frontend window
→ Runtime remains alive
→ tray remains alive

reopen from tray
→ frontend opens
→ Runtime ownership never changed
```

Do not move tray ownership into the frontend.

Do not make frontend visibility part of controller-runtime health.

### 2.2 Center M Disabled means mandatory Runtime

The actual MSI Center M startup configuration is the authority fact.

Use the current exact state contract:

```text
Server task Disabled
Updater task Disabled
MSI Foundation Service Disabled
    → Center M startup state = Disabled
    → Addon Runtime mandatory
```

Do **not** introduce another persisted boolean such as:

```text
AddonRuntimeMandatory=true
AddonControllerMode=true
Full1902Enabled=true
```

The existing Center M startup configuration remains the persistent source of truth.

### 2.3 Partial / unavailable state is not Addon-owned authority

For this PR:

```text
Center M startup state == Disabled
    → mandatory Runtime policy

Enabled
    → normal existing Runtime policy

Partial / Unavailable
    → do not silently classify as Addon-owned
```

PR3 will own the transition/repair policy for Partial and mutation failures.

Do not create a second authority decision here.

---

## 3. Current implementation facts to reuse

PR2.5 should use the seams that already exist rather than adding another manager.

### 3.1 Existing user-termination gate

`AddonProcessHost.EvaluateUserTermination()` already delegates to the Runtime's existing lifecycle safety gate.

`RuntimeProcessApplication.RequestExit()` and `RequestRestart()` already consult that decision.

`SystemTrayIcon` already uses the same decision to gray `Restart` and `Exit`.

Therefore the new mandatory-authority rule should extend this existing boundary instead of creating a parallel exit manager.

### 3.2 Existing background startup owner

`StartupSettingsCoordinator` is already the single settings-level write path for:

```text
LaunchAtWindowsStartup
```

and delegates to the existing `WindowsTaskSchedulerStartupManager`.

`AddonRuntimeCompositionFactory` already calls:

```text
startupSettings.Repair()
```

at Runtime startup.

Extend this existing path instead of adding a second scheduled-task owner.

### 3.3 Existing Center M startup truth

`CenterMStartupControl.Capture()` already reads the three actual Center M startup roots and classifies them as:

```text
Enabled
Disabled
Partial
Unavailable
```

Use that read side.

Do not duplicate the service/task probing logic.

---

## 4. Required Runtime termination policy

### 4.1 Extend the existing process-level decision

The process-level termination decision should become conceptually:

```text
existing Runtime safety decision
    ↓
if already blocked
    → preserve existing block reason

otherwise inspect current Center M startup state
    ↓
Disabled
    → block ordinary user termination

Enabled / Partial / Unavailable
    → preserve existing behavior for this PR
```

Add one narrow reason such as:

```csharp
UserTerminationBlockReason.ControllerAuthorityMandatory
```

Exact naming may follow repository conventions.

Do not add another termination state machine.

### 4.2 Keep existing safety reasons authoritative

If the existing Runtime safety guard already reports:

```text
RoutingTransition
PendingRoutingCleanup
NativeModeActive
NativeRecoveryOwned
RecoveryMutationOwned
RuntimeShuttingDown
```

preserve that result.

The new mandatory-authority reason is only needed when ordinary termination would otherwise be permitted.

This avoids changing established cleanup/recovery safety semantics.

### 4.3 Tray behavior while Center M is Disabled

The current tray uses the termination decision for both:

```text
Restart
Exit
```

For the initial simple implementation, keep that shape.

Therefore while Center M is exactly Disabled:

```text
Open    = enabled
Restart = grayed/disabled
Exit    = grayed/disabled
```

This is intentionally stricter than a future seamless Runtime restart mechanism.

Do not split Restart and Exit policy merely to preserve tray Restart in PR2.5.

A controlled replacement-Runtime restart can be added later if the product actually needs it.

### 4.4 Frontend close remains allowed

Do not reinterpret closing the settings window as a Runtime exit request.

Expected behavior:

```text
frontend X / close
→ frontend retires only
→ Runtime message loop stays alive
→ tray stays registered
```

No Center M authority check is required for ordinary frontend close because it does not release Runtime ownership.

---

## 5. Explicit lifecycle exceptions

The mandatory-user-termination gate is not the Windows process police.

It must not try to prevent every possible process exit.

### 5.1 Windows shutdown / restart

Windows shutdown/restart is a valid process-lifetime boundary.

Do not attempt to veto system shutdown because Center M is Disabled.

The Full PID1902 architecture already defines:

```text
Windows shutdown/restart while Center M Disabled
→ authority intent remains Addon
→ do not intentionally release authority merely because Windows is ending
→ next boot/logon Runtime starts and reconciles current physical state
```

PR2.5 does not implement PID behavior; it only must not treat OS shutdown as an ordinary tray Exit command.

### 5.2 Uninstall

`RequestExitForUninstall()` is an explicit lifecycle path and currently bypasses normal user termination evaluation.

Do not route uninstall through the ordinary tray/user Exit guard in this PR.

Future uninstall work must restore a supported stock environment before the Addon is removed.

That controller/HidHide/PID cleanup is **not** PR2.5 scope.

### 5.3 Unexpected crash / Task Manager kill

User-mode code cannot make the Runtime literally unkillable.

PR2.5 must not introduce:

- a Windows service;
- a second watchdog process;
- a supervisor process;
- heartbeat IPC;
- process resurrection polling;
- restart epochs;
- a kernel/service protection scheme.

Unexpected Runtime death is a real later recovery requirement, but not part of this foundation PR.

Initial product-hardening order is intentionally:

```text
first:
    startup mandatory
    ordinary intentional Runtime exit blocked
    tray remains Runtime-owned

later:
    focused automatic crash restart / keepalive
```

---

## 6. Launch-at-startup becomes mandatory in Disabled mode

### 6.1 Required state

When current Center M startup state is exactly Disabled:

```text
Settings.LaunchAtWindowsStartup = true
owned Task Scheduler startup task = enabled
arguments = --background
```

The user must not be able to persist `false` while the mandatory policy is active.

### 6.2 Reuse `StartupSettingsCoordinator`

Do not add another startup-task coordinator.

Extend `StartupSettingsCoordinator` with the narrow policy input it needs, for example conceptually:

```csharp
Func<bool> isLaunchAtStartupRequired
```

or an equivalent small read-only dependency.

Do not pass a general Center M manager into Settings merely to answer one boolean policy fact.

The implementation should remain directly unit-testable.

### 6.3 Startup `Repair()` behavior

At Runtime startup:

```text
if startup is not mandatory
    → preserve current Repair behavior
    → synchronize the user's saved preference

if startup is mandatory
    → effective desired startup = true
    → persist LaunchAtWindowsStartup=true if the saved value is false
    → synchronize the owned Task Scheduler task as enabled
    → return the actual registration result/message
```

This ensures a machine that already has Center M startup roots Disabled cannot remain in the unsupported state:

```text
Center M Disabled
+
Addon startup setting false
```

merely because that preference was saved before the new authority architecture existed.

### 6.4 User attempts to turn startup OFF

While startup is mandatory:

```text
SetLaunchAtWindowsStartup(false)
→ do not persist false
→ do not delete the startup task
→ keep/repair true as needed
→ return current settings with LaunchAtWindowsStartup=true
→ return a clear message such as:
   "Required while MSI Center M is disabled."
```

It is acceptable to call `Synchronize(true)` to prove/repair the required task instead of merely ignoring the request.

What is not acceptable:

```text
persist false first
→ then discover Disabled mode
→ leave settings/task contradictory
```

### 6.5 Startup registration failure

If Center M is already Disabled and the startup task cannot be repaired:

```text
Runtime is already alive
→ do not intentionally exit the Runtime
→ keep the in-memory/persisted desired preference true
→ surface/log registration failure clearly
```

PR2.5 must not invent a rollback that re-enables Center M.

PR3 will require successful startup registration **before** committing a new Center M Disable transition.

This distinction is important:

```text
PR2.5
already-Disabled machine + repair failure
→ keep current Runtime alive and report failure

PR3
user attempting Enabled → Disabled transition
→ startup guarantee fails
→ do not disable Center M / do not reboot as successful
```

---

## 7. Settings UI behavior

The backend policy is authoritative.

At minimum, a user attempt to turn startup OFF while Center M is Disabled must immediately converge back to ON using the existing returned settings snapshot.

Preferred UI if it can be implemented without expanding the PR materially:

```text
Launch at Windows startup: ON
control disabled/grayed
Description: Required while MSI Center M is disabled.
```

A small frontend snapshot property such as:

```csharp
LaunchAtWindowsStartupRequired
```

is acceptable if needed to render the locked state directly.

Keep it a simple derived fact.

Do not persist it separately.

Do not build a new settings policy model.

If adding the property requires a frontend wire-contract update, update the existing contract/tests directly. The project is pre-release; do not add old/new protocol compatibility wrappers solely for this field.

If locking the control materially expands this PR, the minimum acceptable behavior is still:

```text
attempt OFF
→ backend rejects
→ returned Settings says true
→ existing UI snaps back to ON
→ message explains why
```

Correct backend enforcement matters more than UI polish in PR2.5.

---

## 8. Suggested narrow composition change

Avoid constructing multiple independent Center M startup readers/owners.

A practical small wiring direction is:

```text
AddonProcessHost
    owns/retains one CenterMStartupControl
        ├─ existing frontend Center M startup UI
        ├─ mandatory Runtime termination policy read
        └─ launch-at-startup-required read/predicate
```

Conceptually:

```csharp
bool IsControllerRuntimeMandatory() =>
    centerMStartup.Capture().State == FrontendCenterMStartupState.Disabled;
```

Then pass only the narrow fact/predicate where required.

The exact implementation does not have to use this function name.

Do **not** introduce:

- `ControllerAuthorityManager`;
- `RuntimeLifetimeManager`;
- `MandatoryRuntimeService`;
- another settings store;
- another Center M task/service reader;
- another process-lifetime state machine.

The point of PR2.5 is to use the seams that already exist.

---

## 9. Center M Enabled behavior must remain unchanged

When Center M is exactly Enabled:

```text
Launch at Windows startup
→ ordinary user preference

Tray Restart / Exit
→ available when existing Runtime safety guard permits

Frontend close
→ unchanged
```

Do not make the Addon permanently unexitable on machines where MSI still owns the controller.

Do not force startup ON merely because the hardware is a supported MSI Claw.

The mandatory policy depends on **actual Center M startup state == Disabled**, not hardware identity alone.

---

## 10. No controller mutation in PR2.5

This PR must not make Center M Disabled activate the Full PID1902 controller path yet.

Even if the current actual Center M startup state is Disabled, PR2.5 does **not** add:

```text
PID1901 → PID1902
DirectInput acquire
HidHide baseline apply
VIIPER attach
X360 publisher
Steam Deck publisher
Center M process kill
MainUI suppression lifetime changes
Steam/BPM presentation selection
```

The result after PR2.5 is only:

```text
Center M Disabled
→ Runtime/tray lifetime policy is mandatory
→ startup task is mandatory
```

Controller ownership activation remains assigned to later PRs.

---

## 11. No reboot-bound Center M transition in PR2.5

Do not change the current Center M Enable/Disable mutation flow into the final reboot-bound workflow here.

PR3 owns:

```text
Disable and Restart
→ preflight
→ mandatory startup proven
→ persistent HidHide baseline prepared
→ Center M roots disabled and verified
→ immediate reboot
```

and:

```text
Enable and Restart
→ release Addon controller authority as implemented by that stage
→ restore stock baseline
→ Center M roots enabled and verified
→ immediate reboot
```

PR2.5 must provide the Runtime/startup guarantee PR3 needs, but must not implement PR3 early.

---

## 12. In scope

Implement only the smallest set needed for the mandatory Runtime lifetime contract:

- derive `Runtime mandatory` from the existing actual Center M startup state;
- extend the existing process-level user termination decision so ordinary Runtime termination is blocked when Center M is exactly Disabled;
- add one narrow block reason for mandatory controller authority if useful;
- reuse the existing tray termination decision so `Restart` and `Exit` are disabled/grayed while mandatory;
- keep tray owned by the Runtime process;
- preserve free frontend/UI close/reopen behavior;
- make `LaunchAtWindowsStartup` effectively mandatory true while Center M is exactly Disabled;
- ensure startup `Repair()` forces/repairs the Task Scheduler registration ON in mandatory mode;
- prevent a user startup-OFF request from persisting false or deleting the task while mandatory;
- return a clear registration/settings message for the required state;
- optional small UI lock/description if reviewable without broad transport refactor;
- focused unit/integration tests for the above.

---

## 13. Explicitly out of scope

Do **not** implement any of the following in PR2.5:

### Controller ownership

- PID1901 → PID1902;
- PID1902 → PID1901;
- DirectInput acquire/read;
- physical PnP stabilization;
- exact PID1902 collection discovery;
- physical isolation verification.

### HidHide production wiring

- applying the PR2 persistent Disabled-mode baseline from startup;
- clearing the PR2 baseline during Enable;
- startup recovery semantic migration;
- changing old route-scoped HidHide stages.

### Center M authority transition

- `Disable and Restart` implementation;
- `Enable and Restart` implementation;
- forced reboot;
- current-session Center M kill/quiesce;
- MainUI guard lifetime migration;
- transition rollback/transaction framework.

### VIIPER / presentation

- X360 attach;
- Steam Deck attach;
- publisher start/stop;
- first-presentation selection;
- runtime X360 ↔ Deck switching.

### Keepalive hardening

- Windows service;
- supervisor process;
- child-process watchdog;
- heartbeat;
- automatic crash resurrection;
- Task Manager kill prevention;
- restart epoch;
- new background polling loop.

### Other features

- rumble;
- gyro;
- WING/OEM1 behavior changes;
- TDP/fan/device-setting changes;
- unrelated UI polish;
- broad routing refactor.

---

## 14. Failure policy

Keep failure behavior simple and deterministic.

### Center M state read failure

If current Center M startup state cannot be proven Disabled:

```text
PR2.5 does not claim mandatory Addon authority
```

Do not guess.

Do not block Exit solely because a read failed.

Do not force startup ON solely from an unavailable read.

PR3 will own transition admission/repair for unreadable/Partial configurations.

### Startup-task repair failure while already Disabled

```text
keep current Runtime alive
keep desired startup preference true
report/log failure
```

Do not terminate the only future controller Runtime because its next-logon registration could not be repaired.

### Termination decision read

The termination gate should use a fresh enough actual Center M startup fact for a user action.

Do not cache a forever-stale authority decision merely because Center M state was captured once at process startup.

No polling is needed: evaluate on the existing user action / settings paths.

---

## 15. Logging

Use bounded lifecycle/settings logs only.

Useful events/fields include conceptually:

```text
ControllerRuntimeMandatory evaluated
User termination blocked
Launch-at-startup forced ON
Launch-at-startup OFF request rejected
Mandatory startup repair succeeded
Mandatory startup repair failed
```

Useful fields:

```text
CenterMStartupState
TerminationReason
RequestedStartupEnabled
EffectiveStartupEnabled
StartupRegistrationSuccess
```

Do not log on a timer.

Do not add a runtime heartbeat log.

---

## 16. Test requirements

Add focused tests around real product paths.

### 16.1 User termination

At minimum:

```text
Center M Enabled + existing Runtime guard allows
→ CanTerminate = true

Center M Disabled + existing Runtime guard allows
→ CanTerminate = false
→ reason = ControllerAuthorityMandatory

Center M Disabled + existing Runtime guard already blocks RoutingTransition
→ preserve RoutingTransition

Center M Partial + existing Runtime guard allows
→ PR2.5 does not classify mandatory

Center M Unavailable + existing Runtime guard allows
→ PR2.5 does not classify mandatory
```

### 16.2 Tray

Verify the existing tray policy receives the combined decision:

```text
mandatory false
→ Restart/Exit normal when otherwise safe

mandatory true
→ Restart/Exit grayed
→ Open remains available
```

Do not add fragile UI timing tests.

### 16.3 Startup setting

At minimum:

```text
Enabled mode + saved startup false
→ Repair synchronizes false
→ user may keep false

Disabled mode + saved startup false
→ Repair persists/effectively converges to true
→ Synchronize(true)

Disabled mode + saved startup true
→ Repair keeps true
→ Synchronize(true)

Disabled mode + user requests false
→ false is not persisted
→ owned task is not deleted
→ returned settings remain true
→ message explains required state

Disabled mode + mandatory task repair failure
→ returned registration result reports failure
→ desired setting remains true
```

### 16.4 UI/frontend behavior

If `LaunchAtWindowsStartupRequired` or equivalent is added to the frontend snapshot:

- Disabled mode maps it true;
- Enabled/Partial/Unavailable map it false;
- Settings page disables the toggle only when true;
- current settings are still rendered from actual Runtime-side state;
- named-pipe transport round-trips the field if the wire contract changes.

If no explicit UI property is added, test that an OFF attempt receives `LaunchAtWindowsStartup=true` back and the existing page snapback path remains valid.

### 16.5 Frontend close isolation

Preserve or add a focused structural test proving frontend close/disposal does not call Runtime process shutdown while the Runtime/tray host remains alive.

Do not invent a test-only race merely to prove instruction ordering.

---

## 17. Expected code size / reviewability

Keep this PR small.

Target roughly:

```text
100–350 LOC production changes where practical
+ focused tests
```

If the PR starts adding PID/HidHide/reboot/VIIPER/supervisor architecture, stop and split it.

The review should be understandable as:

> **“Center M Disabled makes the existing background Runtime + tray and startup task mandatory; nothing yet changes the physical controller.”**

---

## 18. Validation

Run repository-standard validation.

At minimum:

```text
dotnet build -c Debug
dotnet build -c Release
dotnet test -c Debug
dotnet test -c Release
git diff --check
```

Also run focused tests for:

- `UserTerminationGuard` / process-level termination composition;
- `SystemTrayIcon` termination flags if touched;
- `StartupSettingsCoordinator`;
- frontend launch-at-startup behavior;
- named-pipe frontend contract if changed.

No MSI Claw hardware validation is required to merge PR2.5 because this PR must not switch PID, hide a physical controller, or attach a virtual controller.

A small manual Windows smoke test is useful if convenient:

```text
actual Center M startup state Enabled
→ startup toggle remains optional
→ tray Exit available when otherwise safe

actual Center M startup state Disabled
→ Runtime startup setting forced ON
→ tray Restart/Exit disabled
→ close UI
→ tray remains
→ reopen UI from tray
```

Do not block merge solely because hardware controller validation is unavailable for this foundation-only change.

---

## 19. Acceptance criteria

PR2.5 is complete only when all of the following are true:

1. Exact current Center M startup state is the only fact that decides whether the Runtime is mandatory.
2. No new persisted controller-authority/runtime-mandatory boolean is introduced.
3. Center M Disabled blocks ordinary user Runtime termination.
4. Existing lower-level termination safety reasons remain intact and are not replaced by a new parallel guard.
5. Tray `Open` remains available while mandatory.
6. Tray `Restart` and `Exit` are disabled/grayed while mandatory in the initial simple implementation.
7. Closing the frontend/UI does not stop the Runtime or remove the tray.
8. `LaunchAtWindowsStartup` cannot be persisted false while Center M is Disabled.
9. Startup `Repair()` forces/repairs the existing owned `--background` Task Scheduler task ON while mandatory.
10. A failed mandatory startup repair is surfaced without intentionally shutting down the currently running Runtime.
11. Center M Enabled preserves the existing optional-startup and ordinary-exit behavior.
12. Partial/Unavailable Center M startup state is not silently treated as Addon-owned authority.
13. No PID, DirectInput, HidHide production wiring, VIIPER presentation, Steam/BPM, or Center M reboot-transition behavior is added.
14. No Windows service/supervisor/watchdog/heartbeat is added.
15. Focused tests and the full suite pass cleanly.

---

## 20. PR description requirements

The PR description should state clearly:

- this is **PR2.5: Mandatory Controller Runtime Lifetime Foundation**;
- Center M Disabled now makes the existing background Runtime + tray lifetime mandatory;
- ordinary tray Runtime Restart/Exit is blocked in that mode;
- frontend/UI close remains allowed;
- Launch at Windows startup is forced/locked ON while Disabled;
- actual Center M startup configuration remains the authority source of truth;
- no new controller authority boolean is persisted;
- no PID1902 mutation occurs;
- no DirectInput acquisition occurs;
- no HidHide baseline is production-wired;
- no virtual controller is attached;
- no reboot-bound Center M transition is implemented yet;
- no crash supervisor/service/watchdog is added;
- build/test results.

---

## 21. Final implementation rule

Keep the dependency chain simple:

```text
PR1
Persistent dual VIIPER typed devices
        ↓
PR2
Persistent Addon-owned HidHide baseline primitive
        ↓
PR2.5
Mandatory Runtime + Tray + Startup contract
        ↓
PR3
Reboot-bound Center M authority transition
        ↓
PR4+
Disabled boot admission / PID1902 / DirectInput / presentation
```

The final PR2.5 invariant is:

> **If actual Center M startup configuration is Disabled, the existing Addon background Runtime and tray are treated as mandatory and the existing startup task is kept enabled; only the frontend UI remains disposable. PR2.5 still performs no physical controller ownership mutation.**
