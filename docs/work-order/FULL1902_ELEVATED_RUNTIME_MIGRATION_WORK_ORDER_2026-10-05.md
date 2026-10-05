# Work Order — Full1902 Elevated Runtime Migration with Velopack-Safe Bootstrap

> **Status:** Ready for implementation  
> **Date:** 2026-10-05  
> **Scope:** Elevate the existing Full1902 Runtime owner without changing the packaged executable manifest, WING ownership, helper architecture, frontend IPC topology, or external-action privilege policy.

---

## 0. Required reading

Read these documents before changing code:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
- `docs/work-order/FULL1902_POLICY_B_BIND_WING_GAMEBAR_SUPPRESSION_TO_ADDON_AUTHORITY_WORK_ORDER.md`
- `docs/work-order/PR2_5_MANDATORY_CONTROLLER_RUNTIME_LIFETIME_WORK_ORDER.md`
- `docs/work-order/STARTUP_TASK_ABOVE_NORMAL_PRIORITY_WORK_ORDER.md`

This is a standalone Full1902 application. Do not add CTW integration.

---

## 1. Product decision

Administrator-elevated games are supported.

While Addon controller authority is active:

~~~text
physical WING press
=> native Xbox Game Bar must not surface
~~~

This must hold for both:

~~~text
normal-integrity foreground game
administrator-elevated foreground game
~~~

The 2026-10-05 007 First Light A/B logs established a realistic production failure:

~~~text
007 elevated
=> current medium Runtime did not observe the firmware Win+G sequence
=> WinGSuppressionGuard could not suppress it
=> Game Bar path remained active

007 non-elevated
=> current Runtime observed LWIN/G
=> existing WinGSuppressionGuard suppressed Win+G
=> Event88 still arrived
=> Game Bar did not surface
~~~

The fix is to elevate the existing Runtime owner, not to add another WING owner.

---

## 2. Final process model for this PR

Target:

~~~text
SteamInputAddonforClaw.exe normal Runtime instance   High integrity
    |
    +-- existing WinGSuppressionGuard               unchanged owner
    +-- Main UI child                               inherits High token
    +-- Overlay child                               inherits High token
    +-- PID1902 / HidHide / VIIPER                  unchanged
    +-- existing helpers                            unchanged
~~~

Important distinction:

~~~text
packaged SteamInputAddonforClaw.exe manifest = asInvoker
actual normal Runtime process                 = elevated before Runtime startup
~~~

Do not conflate the executable manifest with the Runtime process privilege.

---

## 3. Critical Velopack constraint

### 3.1 Keep the main manifest asInvoker

Do **not** change:

`src/SteamInputAddonforClaw/app.manifest`

away from:

~~~xml
<requestedExecutionLevel level="asInvoker" uiAccess="false" />
~~~

Do not set `requireAdministrator`.

Keep the UI and Overlay manifests unchanged as well.

### 3.2 Why requireAdministrator is prohibited in this migration

Current repository packaging:

- Velopack NuGet: 1.2.158
- `vpk`: 1.2.158
- main executable: `SteamInputAddonforClaw.exe`
- per-user install root under LocalAppData
- update apply path: `WaitExitThenApplyUpdates(... restart: true)`

Velopack invokes the main binary for fast hooks and restarts the main binary after apply.

The pre-migration installed Runtime is medium-integrity.

If the new package changed the main manifest directly to `requireAdministrator`:

~~~text
old medium Runtime
=> starts medium Update.exe
=> Update.exe applies new package
=> Update.exe tries to start new requireAdministrator main EXE / updated hook
=> elevation-required launch can fail
=> update may be applied but automatic continuation/restart is broken
~~~

The first migration release must not rely on the old installed binary knowing how to elevate the updater.

### 3.3 Preserve Velopack first-code contract

`VelopackApp.Build()...Run()` must remain before the new normal-Runtime elevation gate.

Fast hooks must be allowed to execute and exit according to Velopack's existing contract before any normal Runtime elevation logic.

Do not put `runas`, elevation validation, controller initialization, or single-instance Runtime ownership ahead of `VelopackApp.Run()`.

### 3.4 No Velopack version bump in this PR

Do not update:

- Velopack NuGet;
- `dnx vpk --version`;
- release workflow Velopack version.

The privilege migration must be independently correct with the repository's current 1.2.158 pin.

A Velopack dependency upgrade is a separate change.

---

## 4. Program startup change

Primary file:

`src/SteamInputAddonforClaw/Program.cs`

### 4.1 Keep existing special-entry behavior

The following existing special modes must keep their current behavior and must not be converted into normal Runtime elevation entrypoints:

- safe uninstall;
- Steam FSE elevated registration;
- prerequisite setup;
- owned prerequisite uninstall;
- Windows App Runtime setup;
- startup-task create/remove helper modes;
- Velopack fast hooks.

Do not refactor these helpers in this PR.

### 4.2 Preserve secondary-launch activation without UAC

A user clicking the app while the elevated Runtime is already running should continue to activate the existing Runtime/frontend without triggering a new elevation prompt.

Therefore do **not** elevate blindly at the top of `Main`.

Keep the current single-instance detection first.

Required normal-launch behavior:

~~~text
normal launch
=> existing SingleInstanceGate check

secondary
=> ActivatePrimaryInstance()
=> exit
=> no runas / no UAC

primary
=> evaluate Runtime elevation
~~~

### 4.3 A medium primary may not enter Runtime

Once the process has proven that it is the primary normal-runtime instance:

~~~text
if elevated
=> continue existing Program flow

if not elevated
=> release/dispose the primary SingleInstanceGate
=> launch the same current executable with ShellExecute verb "runas"
=> preserve the original command-line arguments
=> exit the medium process
~~~

The elevated replacement then reacquires the same current-user single-instance gate through the normal existing code.

Do not allow the medium primary to continue to:

- `TrySchedulePendingUpdateApply`;
- `RuntimeProcessApplication.Run()`;
- `AddonProcessHost.RunStartupAsync()`;
- controller authority;
- WinG suppression;
- UI/Overlay ownership.

### 4.4 Keep the implementation small

Do not introduce:

- a launcher project;
- a Windows service;
- an elevation manager;
- a token broker;
- an epoch/state machine;
- an elevated-runtime supervisor.

A couple of narrow `Program` helpers are sufficient.

Suggested shape:

~~~csharp
private static bool IsCurrentProcessElevated()
{
    using var identity = WindowsIdentity.GetCurrent();
    return new WindowsPrincipal(identity)
        .IsInRole(WindowsBuiltInRole.Administrator);
}

private static bool TryRelaunchElevated(string[] args)
{
    var executablePath = Environment.ProcessPath
        ?? throw new InvalidOperationException("The current executable path is unavailable.");

    var startInfo = new ProcessStartInfo(executablePath)
    {
        UseShellExecute = true,
        Verb = "runas",
        WorkingDirectory = AppContext.BaseDirectory,
    };

    foreach (var argument in args)
        startInfo.ArgumentList.Add(argument);

    try
    {
        return Process.Start(startInfo) is not null;
    }
    catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
    {
        // UAC cancelled. Log and fail closed.
        return false;
    }
}
~~~

Exact naming may follow repository conventions.

Do not add a retry loop.

### 4.5 UAC cancellation/failure policy

If elevation is cancelled or fails:

~~~text
medium primary
=> no Runtime initialization
=> no controller mutation
=> no presentation attach
=> no WinG authority
=> exit
~~~

Log one clear reason.

Do not fall back to a medium Runtime.

This is the required fail-close behavior.

---

## 5. Startup task migration

Primary file:

`src/SteamInputAddonforClaw/Install/StartupRegistration.cs`

Current contract:

~~~text
LogonType = InteractiveToken
RunLevel = 0 / least privilege
Priority = 2
--background
battery allowed
no execution time limit
~~~

New contract:

~~~text
LogonType = InteractiveToken
RunLevel = 1 / TASK_RUNLEVEL_HIGHEST
Priority = 2
--background
battery allowed
no execution time limit
~~~

Add one explicit constant rather than spreading magic `1` values, for example:

~~~csharp
internal const int TaskRunLevelHighest = 1;
~~~

Use it in both:

- task registration;
- compliance/readback verification.

A pre-migration owned task with `RunLevel == 0` is drift and must be repaired by the existing synchronization/elevated-repair path.

Do not create a second task.

Do not change:

- task name;
- action path;
- `--background`;
- interactive-token logon;
- priority;
- battery policy;
- execution-time-limit policy;
- current bounded readback settle.

---

## 6. First migration update contract

This is a required validation path, not an optional edge case.

The expected one-time transition is:

~~~text
installed old version: medium Runtime, asInvoker
=> old version downloads/applies elevated-Runtime migration release
=> new package still has asInvoker manifest
=> Velopack updated hook runs successfully
=> Velopack restarts new main process successfully at medium integrity
=> new Program normal-runtime bootstrap sees primary + medium
=> releases single-instance gate
=> requests runas
=> user approves UAC
=> elevated replacement becomes primary
=> existing startup synchronization repairs owned task to Highest
=> Full1902 Runtime starts
~~~

This permits a one-time UAC prompt during migration.

Do not require a two-release bridge.

Do not require the old installed version to understand the new startup-task contract before package apply.

---

## 7. Steady-state Velopack update contract

After the migration has completed:

~~~text
elevated Runtime
=> WaitExitThenApplyUpdates
=> Update.exe is started by the elevated Runtime and inherits that token
=> apply waits for Runtime shutdown
=> new package is applied
=> updater starts asInvoker main EXE with inherited elevated token
=> IsCurrentProcessElevated == true
=> no extra runas prompt
=> Runtime continues elevated
~~~

Keep the current:

~~~csharp
WaitExitThenApplyUpdates(update, silent: true, restart: true, restartArgs: restartArguments)
~~~

behavior unchanged.

Do not create a custom updater wrapper.

---

## 8. Fresh-install contract

Velopack per-user install remains unchanged.

Expected:

~~~text
Setup.exe installs package
=> Velopack fast install hook runs normally
=> first normal main launch starts asInvoker
=> normal-runtime bootstrap requests runas
=> user approves UAC
=> elevated Runtime continues
=> startup task is created/repaired with Highest when policy requires it
~~~

Do not elevate Velopack Setup itself merely to make the Runtime elevated.

---

## 9. Controlled Runtime restart

Current `RuntimeProcessApplication.RequestRestart()` uses:

~~~csharp
new ProcessStartInfo(executablePath) { UseShellExecute = false }
~~~

Do not rewrite this merely because the Runtime is elevated.

Once the Runtime is High, this replacement child inherits the elevated token.

The existing `--restart` single-instance wait/reacquire behavior remains valid.

Required outcome:

~~~text
High Runtime
=> controlled restart
=> High replacement process
=> no UAC
=> existing Full1902 restart teardown/reconcile unchanged
~~~

---

## 10. Main UI and Overlay

Do not modify:

- `FrontendProcessLauncher` privilege behavior;
- `OverlayProcessController` privilege behavior;
- frontend/overlay named-pipe ACL/options;
- UI manifest;
- Overlay manifest.

They are direct children of the elevated Runtime and may inherit its elevated token.

This PR does not intentionally de-elevate them.

The current `PipeOptions.CurrentUserOnly` topology remains unchanged.

---

## 11. WING / Game Bar code

Do not move or redesign `WinGSuppressionGuard`.

Keep:

- one Runtime-owned `WH_KEYBOARD_LL`;
- existing arm/disarm ownership;
- existing modifier cleanup;
- existing Full1902 Policy-B readiness gate;
- existing fail-close before presentation attach/re-attach;
- existing stock-authority release behavior.

The intended functional change comes from the Runtime process privilege, not from a new hook implementation.

Do not add:

- TDP-helper WING IPC;
- a second hook;
- GameInput production routing;
- new WING state;
- a watchdog.

---

## 12. Sleep / Hibernate / Resume

No power-lifecycle redesign is part of this PR.

Preserve the current sequence:

~~~text
Suspend
=> existing presentation quiesce/neutral path
=> Runtime/controller authority remains
=> WinG suppression remains owned by the same process

Resume
=> existing PowerResumeObserved
=> existing physical/presentation reconciliation
=> existing WinG IsArmed fail-close gate
~~~

Do not re-install the hook on every resume unless real hardware evidence from this PR demonstrates that Windows loses the hook in the supported lifecycle.

Hardware validation must include both Sleep and Hibernate.

---

## 13. Existing helpers are explicitly out of scope

Do not remove, inline, rename, or redesign:

- `SteamInputAddonforClaw.TdpHelper`;
- Center M startup helper;
- prerequisite helpers;
- uninstall helpers.

The TDP helper remains exactly as it is in this PR, including current WMI request/timeout behavior.

Any future helper consolidation belongs to the separate privileged-process cleanup phase.

---

## 14. External application / PowerShell inheritance is deferred

Do not modify in this PR:

- WING/OEM `LaunchApplication`;
- Shortcut executable launch;
- Shortcut PowerShell launch;
- URL launch semantics.

They may inherit the elevated Runtime token after this migration.

That is an explicitly accepted temporary behavior.

Do not add an unelevated launch broker here.

---

## 15. Tests

Primary existing test file:

`tests/SteamInputAddonforClaw.Tests/ElevationConfigurationTests.cs`

### 15.1 Manifest contract

Keep/assert:

~~~text
SteamInputAddonforClaw.app        = asInvoker
SteamInputAddonforClaw.UI.app     = asInvoker
SteamInputAddonforClaw.Overlay.app= asInvoker
TdpHelper                         = requireAdministrator
~~~

Add Overlay to the manifest test if it is not already covered.

This test is now a Velopack compatibility guard, not evidence that the normal Runtime is medium-integrity.

### 15.2 Startup task contract

Replace the old least-privilege assertion with Highest:

~~~text
TaskLogonInteractiveToken unchanged
TaskRunLevelHighest == 1
Principal.RunLevel = TaskRunLevelHighest
IsCompliant requires RunLevel == TaskRunLevelHighest
Priority == 2
--background unchanged
battery policy unchanged
execution limit unchanged
~~~

Add a test proving a task snapshot with old RunLevel 0 is non-compliant and therefore repairable.

Prefer behavior-level `IsCompliant` assertions where possible over only source-string checks.

### 15.3 Elevation launch construction

Add a small unit-testable seam for constructing the Runtime elevation `ProcessStartInfo`, or equivalent narrow test coverage, proving:

~~~text
FileName = current executable
UseShellExecute = true
Verb = runas
all original arguments preserved
~~~

Do not add an elevation service/interface solely for testing.

### 15.4 Program ordering guard

Add a focused architecture/source-order test if necessary to prove:

~~~text
VelopackApp.Build().Run()
    occurs before normal Runtime elevation

special command entrypoints
    remain outside normal Runtime elevation

secondary single-instance activation
    occurs before runas elevation

normal Runtime initialization
    cannot occur before elevation succeeds
~~~

The test should protect the real migration contract, not implementation trivia.

### 15.5 Existing lifecycle tests

All existing Full1902 tests must continue to pass unchanged unless an assertion explicitly encoded the old least-privilege startup-task contract.

Do not rewrite unrelated tests.

---

## 16. Manual / hardware validation

### A. Existing install -> migration release

This is mandatory.

1. Install/run the current pre-migration release.
2. Ensure it is running with the existing medium Runtime/startup task.
3. Update through the real Addon/Velopack update path to the migration build.
4. Verify package apply succeeds.
5. Verify updated hook completes.
6. Verify new main process restarts.
7. Approve the expected one-time Runtime UAC request.
8. Verify Runtime is elevated.
9. Verify startup task now reads Highest.
10. Restart Windows and verify background Runtime starts elevated without an interactive UAC prompt.

### B. Elevated version -> next elevated version

Create a second local/package test version.

Verify:

~~~text
High Runtime
=> download/apply
=> graceful Runtime shutdown
=> update applies
=> Runtime restarts High
=> no additional runas/UAC prompt
~~~

### C. Manual launch

With no Runtime running:

~~~text
launch stable Addon entry
=> one elevation prompt
=> Runtime High
=> Main UI works
~~~

With Runtime already running:

~~~text
launch stable Addon entry again
=> existing Runtime activates frontend
=> no additional UAC prompt
~~~

### D. WING

Center M Disabled / Addon authority:

- Windows desktop;
- ordinary game;
- administrator-elevated 007 First Light;
- non-elevated 007 First Light;
- Steam BPM.

For each relevant case:

~~~text
physical WING
=> native Game Bar does not appear
=> configured WING/Event88 action still arrives
=> no stuck Windows modifier
~~~

The elevated 007 case is the primary regression target.

### E. Sleep / Hibernate

With Center M Disabled:

~~~text
Sleep -> Resume
Hibernate -> Resume
~~~

Then verify:

- physical input healthy;
- correct virtual presentation;
- WING suppression still works;
- administrator-elevated game still suppresses Game Bar;
- no new PID1901/1902 churn beyond the existing recovery policy.

### F. Existing helper-backed device controls

Smoke test without code changes:

- TDP;
- currently exposed fan helper operations;
- battery charge limit.

No regression is acceptable from the privilege migration.

---

## 17. Expected files

Primary changes should be limited to approximately:

~~~text
src/SteamInputAddonforClaw/Program.cs
src/SteamInputAddonforClaw/Install/StartupRegistration.cs
tests/SteamInputAddonforClaw.Tests/ElevationConfigurationTests.cs
~~~

Documentation/tests may add small adjacent changes.

Expected **no production changes** to:

~~~text
src/SteamInputAddonforClaw/app.manifest
src/SteamInputAddonforClaw.UI/app.manifest
src/SteamInputAddonforClaw.Overlay/app.manifest
src/SteamInputAddonforClaw/Updates/VelopackUpdateClient.cs
src/SteamInputAddonforClaw/Lifecycle/FrontendProcessLauncher.cs
src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs
src/SteamInputAddonforClaw/GameBar/WinGSuppressionGuard.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/TdpHelperClient.cs
src/SteamInputAddonforClaw.TdpHelper/*
~~~

If implementation requires changes outside that boundary, re-check whether the PR is drifting into deferred cleanup.

---

## 18. Acceptance criteria

The PR is complete only when all of the following are true:

1. Normal Full1902 Runtime never proceeds at medium integrity.
2. Main executable manifest remains `asInvoker`.
3. Velopack bootstrap remains the first application lifecycle owner.
4. Existing install -> migration release update completes and can continue into the elevated Runtime.
5. Startup task uses `TASK_RUNLEVEL_HIGHEST`.
6. Old RunLevel 0 task is detected as drift and repaired.
7. Secondary manual launch can activate an existing Runtime without another UAC prompt.
8. Fresh/manual primary launch elevates before Runtime/controller ownership.
9. Existing Runtime restart stays elevated without a new UAC prompt.
10. UI/Overlay remain on their existing launch/IPC topology.
11. Existing WinGSuppressionGuard remains the only suppression owner.
12. WING suppresses Game Bar in an administrator-elevated 007 First Light process.
13. Sleep and Hibernate resume preserve Full1902 controller and WING behavior.
14. Existing TDP/Fan/Battery helper behavior is unchanged.
15. No helper consolidation or external-launch privilege cleanup is mixed into this PR.
16. All existing tests pass and focused elevation/startup-task tests are added.

---

## 19. Overengineering guard

Do not solve hypothetical future process models in this PR.

Supported product scope is:

~~~text
one Windows user
one interactive session
no Fast User Switching
no RDP/multi-session support
~~~

The implementation needs to protect real lifecycle paths:

- install/update/restart;
- UAC cancellation;
- Windows logon startup;
- Sleep/Hibernate/Resume;
- Runtime restart;
- administrator foreground games;
- physical-device/PnP recovery already handled by Full1902.

Do not add extra token authorities, launch brokers, synchronization layers, retries, watchdogs, or abstractions for theoretical interleavings.

The desired result is:

> one existing Runtime owner, elevated before it becomes the Runtime.
