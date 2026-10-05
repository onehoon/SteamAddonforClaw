# Work Order — Full1902 Elevated Runtime

> **Status:** Ready for implementation  
> **Date:** 2026-10-05  
> **Scope:** Make the normal Full1902 application Runtime elevated from the beginning of its supported lifecycle, while keeping Velopack compatibility and preserving the existing controller/WING architecture.
> **Release assumption:** The product is pre-release. There is no requirement to migrate an already-deployed medium-integrity production install.

---

## 0. Required reading

Read these documents before changing code:

- \`docs/Full 1902 Implementation/README.md\`
- \`docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md\`
- \`docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md\`
- \`docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md\`
- \`docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md\`
- \`docs/work-order/FULL1902_POLICY_B_BIND_WING_GAMEBAR_SUPPRESSION_TO_ADDON_AUTHORITY_WORK_ORDER.md\`

This is a standalone Full1902 application. CTW integration is not part of this work.

---

## 1. Goal

Make the normal Steam Addon for Claw Runtime an administrator/elevated process for its entire supported Runtime lifecycle.

The primary regression target is the confirmed WING / Xbox Game Bar integrity-boundary failure:

~~~text
normal game
=> existing Runtime-owned WinGSuppressionGuard sees MSI firmware Win+G
=> suppresses native Game Bar

administrator-elevated game
=> current medium Runtime does not receive the relevant low-level keyboard sequence
=> native Game Bar can surface
~~~

Administrator-elevated games are supported.

The fix is:

> Elevate the existing Full1902 Runtime owner.

Do not add another WING owner.

---

## 2. Product process model

Target process model:

~~~text
Velopack / command bootstrap          may begin asInvoker
        |
        +-- Velopack fast hooks       existing behavior
        +-- explicit special modes    existing behavior
        |
        +-- normal application entry
                |
                +-- require High integrity
                        |
                        v
        SteamInputAddonforClaw Runtime [High]
                |
                +-- Main UI [inherits High]
                +-- Overlay [inherits High]
                +-- WinGSuppressionGuard
                +-- PID1902 / DirectInput
                +-- HidHide
                +-- VIIPER
                +-- Full1902 lifecycle
                +-- existing helper clients
~~~

There is still exactly one controller authority:

~~~text
Center M Disabled
=> Addon Runtime authority

Center M Enabled
=> MSI / stock authority
~~~

Elevation changes process privilege, not controller-authority policy.

The supported interactive Windows user must itself be a member of Administrators. Manual UAC is for consent under that same user identity; credentials for a different administrator account are unsupported.

---

## 3. Velopack constraint: keep the packaged main EXE asInvoker

### 3.1 Do not change the main manifest

Keep:

\`src/SteamInputAddonforClaw/app.manifest\`

as:

~~~xml
<requestedExecutionLevel level="asInvoker" uiAccess="false" />
~~~

Do not switch the packaged main EXE to \`requireAdministrator\`.

Keep the UI and Overlay manifests unchanged as well.

### 3.2 Reason

Current packaging uses Velopack 1.2.158 and the configured main executable is:

~~~text
SteamInputAddonforClaw.exe
~~~

Velopack executes the main executable for lifecycle hooks and launches it again after install/update operations.

The application therefore needs a small non-Runtime bootstrap region where Velopack can execute its contract before normal application ownership begins.

The target distinction is:

~~~text
packaged EXE manifest
= asInvoker for Velopack compatibility

normal application Runtime
= always elevated
~~~

Do not interpret the \`asInvoker\` manifest as permission for a medium-integrity Runtime.

### 3.3 Preserve Velopack ordering

\`VelopackApp.Build()...Run()\` must remain before normal Runtime elevation.

Do not move controller initialization, self-elevation, or Runtime ownership ahead of Velopack bootstrap.

Do not change Velopack version, packaging commands, or \`VelopackUpdateClient\` in this PR.

---

## 4. Normal application elevation gate

Primary file:

\`src/SteamInputAddonforClaw/Program.cs\`

### 4.1 Keep existing non-Runtime special modes before the elevation gate

Preserve the current special-entry handling, including:

- safe uninstall;
- Steam FSE elevated registration;
- prerequisite setup;
- owned prerequisite uninstall;
- Windows App Runtime setup;
- startup-task ensure/remove helper modes;
- Velopack fast hooks handled by \`VelopackApp.Run()\`.

Do not refactor those paths in this PR.

### 4.2 Elevate before entering the normal application lifecycle

After Velopack bootstrap and the existing special command exits have been handled, but **before** the normal single-instance/Runtime path:

~~~text
normal application entry
=> check current process elevation

already High
=> continue

not High
=> relaunch the same current executable with ShellExecute "runas"
=> preserve the original arguments and originating interactive user's SID
=> exit the medium bootstrap process

runas cancelled/failed
=> log
=> exit
=> do not enter Runtime
~~~

Then the elevated process starts from \`Main\` again:

~~~text
VelopackApp.Run()
=> special-mode checks
=> verify the current user SID matches the originating interactive user SID, when this is a self-elevation relaunch
=> elevation check passes
=> SingleInstanceGate
=> pending-update check
=> RuntimeProcessApplication
~~~

The elevated replacement must retain the originating interactive user's SID. If the SID differs, it logs and exits before `SingleInstanceGate` or controller ownership. Carry the expected SID through a minimal bootstrap handoff; do not add cross-user IPC, alternate-credential launch support, a service/broker, or multi-user ownership handling.

This means the entire normal application lifecycle, including single-instance ownership, runs at High integrity.

Do not create a mixed-integrity normal-runtime path merely to avoid a manual UAC prompt.

### 4.3 Keep the implementation narrow

A small helper in or adjacent to \`Program\` is sufficient.

Example shape:

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
        WorkingDirectory = AppContext.BaseDirectory
    };

    foreach (var argument in args)
        startInfo.ArgumentList.Add(argument);

    try
    {
        return Process.Start(startInfo) is not null;
    }
    catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
    {
        return false;
    }
}
~~~

Exact naming may follow repository conventions.

Do not introduce:

- another executable;
- an elevation manager;
- a launcher service;
- token duplication;
- Explorer/token broker tricks;
- retry loops;
- a supervisor;
- new Runtime state.

### 4.4 Fail closed on elevation failure

A medium normal-app bootstrap must never continue into Full1902 ownership.

If elevation is cancelled or fails:

~~~text
no SingleInstance ownership
no RuntimeProcessApplication
no AddonProcessHost
no PID mutation
no HidHide mutation
no VIIPER presentation
no WinG authority
~~~

Log the reason and exit.

---

## 5. Startup task: start High directly

Primary file:

\`src/SteamInputAddonforClaw/Install/StartupRegistration.cs\`

The Addon-owned scheduled task becomes:

~~~text
LogonType = TASK_LOGON_INTERACTIVE_TOKEN
RunLevel = TASK_RUNLEVEL_HIGHEST
Priority = 2
Action = SteamInputAddonforClaw.exe --background
DisallowStartIfOnBatteries = false
StopIfGoingOnBatteries = false
ExecutionTimeLimit = PT0S
~~~

Add an explicit run-level constant rather than magic values, for example:

~~~csharp
internal const int TaskRunLevelHighest = 1;
~~~

Use the same value in:

- registration;
- readback/compliance verification.

The startup task remains one task. Do not add a separate elevated launcher task.

At Windows logon:

~~~text
Task Scheduler Highest
=> SteamInputAddonforClaw.exe --background [High]
=> Program elevation gate sees High
=> no UAC
=> normal Full1902 Runtime starts
~~~

Because the product is pre-release, there is no special production migration requirement for an older least-privilege task.

The ordinary owned-task compliance path should still regard any wrong run level as non-compliant and reconcile it through the existing mechanism.

---

## 6. Manual launch

Supported behavior when the Runtime is not running:

~~~text
user launches Steam Addon for Claw
=> asInvoker bootstrap starts
=> Velopack/special handling completes
=> normal-app elevation gate
=> UAC prompt
=> elevated replacement
=> full application lifecycle runs High
~~~

This is expected.

Do not build additional machinery solely to hide this UAC prompt.

If the user manually launches the app again while an elevated Runtime is already running, another UAC prompt before secondary-instance detection is acceptable in this PR.

Avoiding that prompt would require relying on or designing a cross-integrity activation path and is not required for the current product goal.

---

## 7. Controlled Runtime restart

Current \`RuntimeProcessApplication.RequestRestart()\` starts the same executable with:

~~~csharp
UseShellExecute = false
~~~

Keep that behavior unless a concrete implementation failure is found.

A child of the already elevated Runtime inherits the elevated token:

~~~text
High Runtime
=> start replacement
=> replacement High
=> --restart flow
=> existing single-instance wait/reacquire
=> Runtime remains High
~~~

No UAC should be required for controlled restart.

Do not create a separate restart elevation path.

---

## 8. Velopack update lifecycle

No pre-release migration compatibility work is required.

Only the final steady-state architecture must work.

Expected normal update path:

~~~text
High Runtime
=> WaitExitThenApplyUpdates(...)
=> Update.exe starts as child of High Runtime
=> updater inherits High token
=> waits for Runtime exit
=> applies package
=> starts packaged asInvoker main EXE
=> child inherits updater's High token
=> Program elevation gate sees High
=> Runtime resumes High
~~~

Keep the existing:

~~~csharp
WaitExitThenApplyUpdates(update, silent: true, restart: true, restartArgs: restartArguments)
~~~

Do not:

- wrap Update.exe;
- create a custom updater;
- change Velopack version;
- modify package layout;
- introduce a bridge release;
- test old Medium release -> new High release migration.

Fresh-install + steady-state update are the only required product lifecycle.

---

## 9. Fresh install

Expected lifecycle:

~~~text
Velopack Setup
=> package installed
=> Velopack install hooks run under their existing contract
=> first normal application launch
=> Runtime elevation gate
=> UAC
=> Full1902 Runtime High
~~~

Once the mandatory startup task exists:

~~~text
next Windows logon
=> Task Scheduler Highest
=> Runtime High directly
=> no login-time UAC
~~~

Do not elevate Setup.exe solely for this feature.

---

## 10. Main UI and Overlay

Do not add de-elevation.

Existing direct child launch remains:

~~~text
Runtime [High]
    +-- Main UI [High]
    +-- Overlay [High]
~~~

Do not modify:

- \`FrontendProcessLauncher\` privilege behavior;
- \`OverlayProcessController\` privilege behavior;
- frontend/overlay named-pipe options;
- UI manifest;
- Overlay manifest.

The existing same-user IPC topology remains.

UI and Overlay are still frontends only; High integrity does not make them controller authorities.

---

## 11. WING / Game Bar suppression

Do not change ownership or mechanism.

\`WinGSuppressionGuard\` remains:

~~~text
Runtime-owned
WH_KEYBOARD_LL
one suppression hook
existing LWIN/RWIN/G tracking
existing modifier cleanup
existing Policy-B arm/disarm
existing fail-close readiness gate
~~~

Do not add:

- a TDP-helper WING path;
- another hook;
- a helper-to-Runtime suppression protocol;
- GameInput production routing;
- a new WING state authority;
- a watchdog.

The functional fix is that the existing owner now runs High.

---

## 12. Sleep / Hibernate / Resume

No power-lifecycle redesign.

Preserve the existing Full1902 contract:

~~~text
Suspend
=> existing presentation neutral/quiesce
=> Addon authority remains
=> same Runtime remains the WinG suppression owner

Resume
=> existing PowerResumeObserved path
=> physical/PnP reconciliation
=> presentation reconciliation
=> existing WinG suppression readiness gate
~~~

Do not add hook reinstall/retry/state machinery unless hardware testing demonstrates a real failure after Sleep/Hibernate.

Mandatory hardware validation includes both Sleep and Hibernate.

---

## 13. Existing helpers remain unchanged

Do not remove, inline, rename, or redesign:

- \`SteamInputAddonforClaw.TdpHelper\`;
- Center M startup helper;
- prerequisite helpers;
- uninstall helpers.

The TDP helper remains a Runtime-owned helper. It does **not** become the Runtime launcher.

Do not invert ownership into:

~~~text
TdpHelper
=> launches/owns Runtime
~~~

The intended owner direction remains:

~~~text
Runtime
=> may launch/use TdpHelper
~~~

Future helper consolidation is a separate privileged-process cleanup task.

---

## 14. Deferred external-process privilege cleanup

Do not modify:

- WING/OEM \`LaunchApplication\`;
- Shortcut executable actions;
- Shortcut PowerShell actions;
- URL actions;
- other user-selected external process launch behavior.

After this PR those launches may inherit the elevated Runtime token.

That is an explicitly accepted temporary behavior.

The later privileged-process cleanup should evaluate external-process de-elevation and helper consolidation together.

Do not solve it here.

---

## 15. Tests

Primary existing test file:

\`tests/SteamInputAddonforClaw.Tests/ElevationConfigurationTests.cs\`

### 15.1 Manifest contract

Keep/assert:

~~~text
SteamInputAddonforClaw.app         = asInvoker
SteamInputAddonforClaw.UI.app      = asInvoker
SteamInputAddonforClaw.Overlay.app = asInvoker
TdpHelper                          = requireAdministrator
~~~

Add Overlay coverage if missing.

The main \`asInvoker\` assertion protects Velopack compatibility.

### 15.2 Startup task contract

Replace the current least-privilege expectation.

Assert:

~~~text
TaskLogonInteractiveToken
TaskRunLevelHighest == 1
Principal.RunLevel = TaskRunLevelHighest
Priority == 2
--background unchanged
battery policy unchanged
execution time limit unchanged
~~~

Existing compliance/readback logic must use the same Highest requirement.

No test specifically for migration from RunLevel 0 is required.

### 15.3 Runtime elevation gate

Add narrow tests around pure/testable pieces only.

Verify that the elevation relaunch start information has:

~~~text
FileName = current executable
UseShellExecute = true
Verb = runas
original args preserved
~~~

Also verify the fail-closed outcome: if `runas` is cancelled (including Win32 error 1223) or process creation otherwise fails, the medium bootstrap logs the outcome and exits before acquiring `SingleInstanceGate` or entering `RuntimeProcessApplication` / controller ownership. Use a focused source/architecture guard if the native process-start result is not directly testable; do not add an interface/service solely for this test.

Verify that the elevated replacement receives and matches the originating interactive user's SID; a different SID must exit before `SingleInstanceGate`.

Do not add an interface/service solely for unit testing.

### 15.4 Startup ordering

Protect the meaningful ordering:

~~~text
VelopackApp.Build().Run()
=> existing special command dispatch
=> normal Runtime elevation gate
=> SingleInstanceGate
=> pending-update handling
=> RuntimeProcessApplication
~~~

The test may be a focused source/architecture guard if that is the smallest repository-consistent option.

Do not overfit tests to local helper names.

### 15.5 Existing lifecycle tests

Keep existing Full1902 controller lifecycle tests unchanged except assertions that explicitly encode the old startup-task RunLevel.

---

## 16. Required manual / hardware validation

### A. Fresh install

From a clean install:

~~~text
install
=> first normal launch
=> one UAC consent prompt for the same administrator user
=> Runtime High
=> Main UI works
=> Overlay works
~~~

Create/verify the owned startup task and confirm RunLevel Highest.

Reboot Windows:

~~~text
logon
=> Runtime starts High from Task Scheduler
=> no interactive UAC
~~~

### B. Controlled restart

~~~text
High Runtime
=> Restart
=> replacement Runtime High
=> no UAC
=> controller recovers through existing Full1902 path
~~~

### C. Steady-state Velopack update

Use two builds based on the new elevated-runtime architecture.

Verify:

~~~text
High Runtime
=> download/apply update
=> Runtime exits safely
=> Velopack applies package
=> Runtime restarts High
=> no UAC
~~~

Do not test a historical medium build as a required acceptance case.

### D. WING suppression

With Center M Disabled / Addon authority:

- Windows desktop;
- normal game;
- administrator-elevated 007 First Light;
- non-elevated 007 First Light;
- Steam BPM.

Verify:

~~~text
physical WING
=> native Game Bar does not surface
=> configured WING/Event88 action still works
=> no stuck Windows modifier
~~~

The administrator-elevated 007 case is the primary functional proof.

### E. Sleep / Hibernate

While Center M is Disabled:

~~~text
Sleep -> Resume
Hibernate -> Resume
~~~

After resume verify:

- physical input healthy;
- correct virtual presentation;
- WING suppression works;
- elevated 007 still does not surface Game Bar;
- no regression in existing PID1902/PnP recovery.

### F. Existing helper-backed controls

Smoke-test existing behavior:

- TDP;
- fan helper operations currently exposed;
- battery charge limit.

No helper refactor is part of this PR.

### G. Manual launch with UAC cancellation

With no Runtime already active, launch the Addon manually and cancel the UAC prompt. Verify:

- the medium bootstrap exits;
- no Runtime, AddonProcessHost, or controller-authority startup occurs;
- PID, HidHide, VIIPER, and WinG ownership state is unchanged;
- the log records the cancelled/failed elevation outcome.

Also verify that a replacement launched under a different administrator SID (for example, over-the-shoulder credentials from a standard account) is rejected before normal Runtime/controller ownership. The supported same-user administrator consent path must continue normally.

---

## 17. Expected production changes

Primary expected files:

~~~text
src/SteamInputAddonforClaw/Program.cs
src/SteamInputAddonforClaw/Install/StartupRegistration.cs
tests/SteamInputAddonforClaw.Tests/ElevationConfigurationTests.cs
~~~

Small adjacent test changes are acceptable.

Expected no production changes to:

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

If substantial production changes appear outside the expected files, re-check scope before continuing.

---

## 18. Acceptance criteria

The PR is complete when:

1. The packaged main EXE remains \`asInvoker\` for Velopack compatibility.
2. Every normal application Runtime reaches High integrity under the originating interactive administrator user before single-instance/Runtime ownership begins.
3. Elevation cancellation/failure or a different elevated user SID exits without controller mutation.
4. The owned startup task uses \`TASK_RUNLEVEL_HIGHEST\`.
5. Windows logon starts the Runtime High without UAC.
6. Controlled Runtime restart remains High without UAC.
7. Fresh install -> first normal launch reaches High through one explicit UAC.
8. Steady-state Velopack update restarts into High without custom updater code.
9. Main UI and Overlay remain on their current launch/IPC topology and inherit High.
10. \`WinGSuppressionGuard\` remains the only WING suppression owner.
11. Administrator-elevated 007 First Light no longer opens native Game Bar from physical WING.
12. Sleep/Hibernate/Resume preserve current Full1902 behavior and WING suppression.
13. Existing TDP/Fan/Battery helper behavior remains unchanged.
14. TDP helper does not become a Runtime bootstrap/launcher.
15. External EXE/PowerShell privilege cleanup is not mixed into this PR.
16. All relevant existing and new tests pass.

---

## 19. Overengineering guard

Supported product scope remains:

~~~text
one Windows user
one interactive session
no Fast User Switching
no RDP/multi-session
~~~

Protect real lifecycle behavior:

- fresh install;
- manual launch/UAC cancellation;
- Windows logon startup;
- controlled Runtime restart;
- Velopack steady-state update;
- Sleep/Hibernate/Resume;
- physical device/PnP recovery;
- elevated foreground games;
- real operation failure.

Do not add infrastructure for theoretical interleavings.

The target remains:

> **One existing Runtime owner, High before it becomes the Runtime.**
