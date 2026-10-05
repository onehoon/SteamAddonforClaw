# Work Order - Full1902 MSI Quick Settings Runtime Quiesce

## Status

Focused Full1902 cleanup PR.

Baseline:

~~~text
repository: onehoon/SteamAddonforClaw
branch: main
commit: d55d8a5c27741c41fb7e54333ab5a6b7ec5ddd45
date: 2026-10-05
~~~

This PR fixes a real resource defect observed on supported hardware after MSI Center M is disabled for Full1902 Addon controller authority. Keep it narrow: no general AppX framework, no watchdog, no new authority state.

## 1. Read before implementation

Read and preserve:

- docs/Full 1902 Implementation/README.md
- docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
- docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
- docs/work-order/FULL1902_POLICY_B_BIND_WING_GAMEBAR_SUPPRESSION_TO_ADDON_AUTHORITY_WORK_ORDER.md
- docs/work-order/WING_WINDOWS_GAMEBAR_STATE_DIAGNOSTIC_WORK_ORDER_2026-10-05.md
- docs/WorkOrder_PR1_CenterM_Startup_Control.md

Inspect current main:

- src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
- src/SteamInputAddonforClaw/CenterMStartup/CenterMStartupControl.cs
- src/SteamInputAddonforClaw.CenterMStartupHelper/Program.cs
- src/SteamInputAddonforClaw/GameBar/WinGSuppressionGuard.cs
- tests/SteamInputAddonforClaw.Tests/Full1902WinGSuppressionAuthorityTests.cs

Historical MSI RE evidence is relevant, especially MSI_CENTER_M_ROUTING_LAUNCH_SUPPRESSION_RESEARCH_RESULT.md and MSI_COMPLETE_RESEARCH_RESULT.md.

The MSI Quick Settings MSIX evidence identifies:

~~~xml
<Application Id="App" Executable="Gamebar_Widget.exe" EntryPoint="Gamebar_Widget.App">
~~~

Do not restore CTW integration or retired Steam-session routing authority.

## 2. Problem

The original Center M startup-control PR intentionally owned only:

~~~text
MSI_Center_M_Server scheduled task
MSI_Center_M_Updater scheduled task
MSI Foundation Service startup type
~~~

Its helper intentionally changes startup configuration only; it does not stop running tasks, services, or processes.

Full1902 therefore has a real cleanup gap:

~~~text
Center M Disabled
-> Center M startup roots disabled
-> Addon owns controller authority
-> MSI Quick Settings Game Bar package can remain running
-> its backend is unavailable
-> Gamebar_Widget repeatedly sends osdready and throws Invalid handle
-> MSI Quick Settings Runtime Broker consumes CPU
~~~

Real 2026-10-05 DebugView capture repeatedly showed:

~~~text
Gamebar_Widget.Class_Gamebar.WriteNamedPipe("osdready")
System.ArgumentException: Invalid handle
FileStream(IntPtr handle, ...)
~~~

The same failure continued for roughly 99 seconds. Task Manager simultaneously showed MSI Quick Settings Runtime Broker consuming CPU.

This is a realistic production resource defect, not a theoretical race.

## 3. Exact target

Known MSI Quick Settings package name:

~~~text
9426MICRO-STARINTERNATION.MSIQuickSettings
~~~

Known packaged executable:

~~~text
Gamebar_Widget.exe
~~~

This PR targets only that MSI package runtime.

Do not target Microsoft Xbox Game Bar itself.

Do not target RuntimeBroker.exe generically.

## 4. Required product behavior

~~~text
Center M Enabled / stock authority
-> leave MSI Quick Settings runtime alone

Center M Disabled / Addon authority
-> if MSI Quick Settings is currently running
-> terminate the exact proven package once during Disabled-mode Runtime startup
-> continue Full1902 startup
~~~

The package must remain installed and registered.

Enable Center M and Restart therefore requires no Quick Settings reinstall or package-registration repair.

## 5. Implementation shape

Add one small concrete helper, preferably:

~~~text
src/SteamInputAddonforClaw/GameBar/MsiQuickSettingsRuntimeQuiescer.cs
~~~

Its only job:

~~~text
find Gamebar_Widget candidates
-> prove package identity
-> accept only exact MSI Quick Settings package
-> terminate all processes for that exact package
-> return a small result for logging/tests
~~~

Do not add PackageManagerService, AppxLifecycleManager, MsiRuntimeAuthorityManager, generic process/package abstractions, a watchdog, timer, service, or new authority state.

One narrow helper is enough.

## 6. Prove identity before termination

Gamebar_Widget is only a candidate process name.

Preferred sequence:

~~~text
Process.GetProcessesByName("Gamebar_Widget")
-> OpenProcess with minimum query access
-> GetPackageFullName
-> obtain package full name
-> require exact package-name component:
   9426MICRO-STARINTERNATION.MSIQuickSettings
-> only then terminate the package
~~~

A strict prefix check against the standard package-full-name format is acceptable:

~~~text
9426MICRO-STARINTERNATION.MSIQuickSettings_
~~~

Do not use a loose substring match.

If GetPackageFullName reports no package identity, skip the candidate.

If identity cannot be read, skip it, log bounded evidence, and do not kill it.

## 7. Terminate the package, not individual Runtime Broker processes

After exact package identity is proven, use:

~~~text
IPackageDebugSettings::TerminateAllProcesses(packageFullName)
~~~

Microsoft reference:

- https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ipackagedebugsettings-terminateallprocesses
- https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ipackagedebugsettings

This API terminates all processes for the specified package and is the preferred identity boundary for the observed MSI Quick Settings Runtime Broker.

Do not independently kill:

- RuntimeBroker.exe
- Gamebar_Widget.exe by name alone
- guessed child PIDs/process trees
- Xbox Game Bar
- broad MSI process-name matches

If multiple candidate PIDs resolve to one package full name, de-duplicate and terminate that package once.

## 8. Explicit RuntimeBroker prohibition

Never implement:

~~~csharp
Process.GetProcessesByName("RuntimeBroker")
~~~

followed by kill logic.

RuntimeBroker.exe is shared Windows infrastructure. Task Manager's "MSI Quick Settings Runtime Broker" label is evidence for diagnosis, not an implementation identity.

## 9. Integration point and ordering

Use the existing Full1902 owner:

~~~text
AddonProcessHost.TryStartDisabledModeControllerAsync(...)
~~~

Current main already starts with an exact authority check:

~~~csharp
if (startupResult.CenterMStartupState != FrontendCenterMStartupState.Disabled)
    return;
~~~

Immediately after that check, run one best-effort quiesce before any admission early-return:

~~~text
exact Center M Disabled proven
-> quiesce MSI Quick Settings runtime
-> continue existing Disabled controller startup
~~~

It must occur before:

- DisabledBootAdmission.IsReady early return
- VIIPER initialization
- PID1902 acquisition
- first presentation attach

Reason: even when Disabled-mode controller admission is temporarily blocked, an orphaned MSI Quick Settings package has no required role and should not burn CPU against an intentionally disabled backend.

Do not gate this cleanup on Steam, BPM, presentation type, Overlay, WING, VIIPER readiness, or PID1902 acquisition success.

The only product gate is exact Center M Disabled authority.

## 10. Failure policy

This is resource cleanup, not a controller safety prerequisite.

Required behavior:

~~~text
package absent -> no-op -> continue
termination succeeds -> continue
identity cannot be proven -> do not kill -> log -> continue
TerminateAllProcesses fails -> warn -> continue
~~~

Do not turn this failure into DisabledBootAdmission, PID1902, HidHide, VIIPER, WinG suppression, or presentation failure.

## 11. Center M Enabled / stock authority

When Center M startup state is exactly Enabled:

- do not enumerate Gamebar_Widget for this feature;
- do not terminate MSI Quick Settings;
- do not alter package registration or activation;
- do not add a restore operation.

Stock MSI behavior remains untouched.

## 12. Package state must remain unchanged

Out of scope and prohibited:

- Remove-AppxPackage
- PackageManager.RemovePackageAsync
- AppX unregister
- MSIX uninstall
- WindowsApps file deletion
- package ACL mutation
- widget registration deletion
- AppUserModelID changes
- Xbox Game Bar package changes

Also do not use persistent package state such as StartServicing, EnableDebugging, persistent suspension, activation redirection, or background-task blocking.

Current evidence needs only one stateless runtime termination.

## 13. No watchdog / polling

Do not add periodic process enumeration, WMI process-start watchers, ETW watchers, package callbacks, Game Bar callbacks for re-killing, resume timers, or retry managers.

One Disabled-startup termination is the first production fix.

A controlled Addon Runtime restart naturally executes the same startup path again. Policy B already separately owns native WING/Xbox Game Bar suppression.

If hardware testing later proves a normal supported lifecycle reproducibly reactivates MSI Quick Settings, capture the exact trigger and handle that specific lifecycle boundary in a separate focused PR. Do not pre-build speculative machinery.

## 14. Logging

Suggested one-shot events:

~~~text
MsiQuickSettingsQuiesceNotRunning
MsiQuickSettingsPackageIdentified
MsiQuickSettingsPackageTerminated
MsiQuickSettingsIdentityUnavailable
MsiQuickSettingsTerminationFailed
~~~

Useful fields: ProcessId, PackageFullName, HResult, Reason.

Do not spam every process-enumeration detail at Info level.

## 15. Native interop constraints

Keep interop local and minimal.

Expected APIs:

~~~text
OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)
GetPackageFullName
IPackageDebugSettings::TerminateAllProcesses
~~~

Use deterministic handle/COM cleanup.

Do not add elevation or a privileged helper unless supported-hardware implementation proves it is actually required.

## 16. Required automated tests

Add focused tests without a generic process/package framework.

Prove:

1. Gamebar_Widget plus exact MSI Quick Settings package full name is eligible.
2. Gamebar_Widget plus another package identity is skipped.
3. Gamebar_Widget with no/read-failed package identity is skipped.
4. Multiple candidate PIDs for one package full name produce one package termination.
5. This feature never enumerates/kills RuntimeBroker by process name.
6. Center M Disabled invokes quiesce.
7. Center M Enabled does not invoke quiesce.
8. Quiesce occurs before DisabledBootAdmission can return early.
9. Simulated quiesce failure does not block existing controller startup.

If host-level faking would require a large new abstraction, use a narrow source-contract test for the integration ordering. Do not add a general process manager only for tests.

## 17. Preserve Full1902 Policy B

Do not change the already merged WING/native Game Bar contract:

~~~text
Center M Disabled
-> WinGSuppressionGuard armed/proven before first live virtual presentation
-> WING native Xbox Game Bar must not surface

Center M Enabled
-> Full1902 suppression not active
~~~

MSI Quick Settings Game Bar widget and Microsoft Xbox Game Bar are different targets.

This PR removes the orphaned MSI package runtime only. It does not replace WinGSuppressionGuard.

## 18. Explicit non-goals

Do not change:

- PID1901/PID1902 authority
- HidHide policy
- DirectInput ownership
- VIIPER ownership
- Xbox360/SteamDeck presentation policy
- WING mapping
- OEM1 mapping
- Win+G suppression policy
- Xbox Game Bar settings/registry/GPO
- Center M three-root authority definition
- stock-safe uninstall
- Enable Center M and Restart ordering
- MSI package installation state

No user-facing toggle.

## 19. Manual hardware validation

### A. Reproduce before fix

Confirm, when present:

~~~text
Gamebar_Widget running
MSI Quick Settings Runtime Broker visible
DebugView repeatedly logs WritePipe:osdready / Invalid handle
recurring CPU use is visible
~~~

### B. Disabled startup after fix

1. Start the Addon with exact Center M Disabled authority.
2. Confirm one quiesce result.
3. Confirm MSI Gamebar_Widget exits.
4. Confirm associated MSI Quick Settings Runtime Broker disappears.
5. Confirm unrelated Runtime Broker processes remain.
6. Confirm Xbox Game Bar is not broadly killed.
7. Observe DebugView for roughly 100 seconds.
8. Confirm prior osdready / Invalid handle spam is gone.
9. Confirm recurring MSI Quick Settings CPU usage is gone.

### C. Full1902 regression

After quiesce verify Xbox360 desktop, SteamDeck game presentation, WING mapping, OEM1, Overlay, controller input, rumble, LED, and vibration remain normal. Native Xbox Game Bar must remain suppressed according to existing Policy B.

### D. Controlled Runtime restart

While Center M stays Disabled, restart/update-relaunch the Addon. If MSI Quick Settings is already absent, quiesce must be an idempotent no-op.

### E. Stock restoration

Use Enable Center M and Restart, reboot, and verify stock MSI behavior. MSI Quick Settings must still be installed/registered and able to activate normally if MSI/Windows needs it.

## 20. Respawn observation

During hardware testing, observe whether Gamebar_Widget returns after successful startup termination during:

- desktop idle
- Steam game start/exit
- BPM enter/exit
- Addon Overlay show/hide
- WING presses under Policy B
- one sleep/resume cycle if practical

If it does not respawn, one-shot cleanup is complete.

If it reproducibly respawns under normal supported lifecycle, capture the exact trigger/logs. Do not add polling/watchdog code in this PR.

## 21. Validation

Run:

~~~text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
git diff --check
~~~

Review must prove:

~~~text
1. Only exact MSI Quick Settings package identity can be terminated.
2. RuntimeBroker is never broadly targeted.
3. Package install/registration state is unchanged.
4. Quiesce runs only under exact Center M Disabled authority.
5. Quiesce runs before DisabledBootAdmission early return.
6. Quiesce failure does not alter controller authority/safety behavior.
7. Full1902 Policy B remains unchanged.
8. No polling/watchdog/new authority abstraction is introduced.
~~~

## 22. Acceptance criteria

- [ ] Center M Disabled attempts one MSI Quick Settings runtime quiesce.
- [ ] Center M Enabled never performs it.
- [ ] Gamebar_Widget is acted on only after exact package identity proof.
- [ ] Package-level termination removes the associated MSI Quick Settings runtime/Runtime Broker.
- [ ] Unrelated Runtime Broker processes are untouched.
- [ ] Xbox Game Bar itself is untouched.
- [ ] MSI Quick Settings remains installed and registered.
- [ ] No restore/reinstall is needed for Center M stock restoration.
- [ ] Identity/termination failure does not block Full1902 startup.
- [ ] Repeating MSI osdready / Invalid handle spam stops after successful quiesce.
- [ ] Recurring MSI Quick Settings CPU usage stops.
- [ ] No new background polling/watchdog cost is introduced.
- [ ] Existing three Center M startup roots remain the sole controller-authority truth.
- [ ] AddonProcessHost remains lifecycle orchestrator.
- [ ] WinGSuppressionGuard remains native Xbox Game Bar suppression owner.
- [ ] Full1902 PID/HidHide/VIIPER/presentation contracts are unchanged.

## 23. Final principle

~~~text
MSI authority
-> leave MSI Quick Settings alone

Addon authority
-> MSI Quick Settings has no required runtime role
-> if its packaged runtime is alive, terminate that exact package once
~~~

The Addon is not becoming a Windows package manager. This PR removes one proven orphaned MSI runtime that currently burns CPU and throws continuously after its backend has intentionally been disabled.
