# Work Order — Startup Task Above-Normal Priority

**Date:** 2026-09-16  
**Baseline:** `main` at `655087b202bd66f3bd323cca2d178d5d4a29028d`  
**Scope:** Narrow Task Scheduler startup-priority change only  
**Product:** Standalone Steam Addon for Claw / Full PID1902 architecture  
**Risk posture:** Improve logon-start scheduling opportunity without changing controller authority, Runtime elevation, startup sequencing, or lifecycle ownership.

---

## 1. Read these authorities first

Before implementation, read and follow the current Full1902 authority documents:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`

Also inspect the current implementation and focused tests:

- `src/SteamInputAddonforClaw/Install/StartupRegistration.cs`
- `src/SteamInputAddonforClaw/Install/ElevatedStartupTaskSetup.cs`
- `src/SteamInputAddonforClaw/Settings/StartupSettingsCoordinator.cs`
- `src/SteamInputAddonforClaw/Runtime/AddonRuntimeComposition.cs`
- `tests/SteamInputAddonforClaw.Tests/WindowsTaskSchedulerStartupManagerTests.cs`

Relevant historical work-order context:

- `docs/work-order/PR2_5_MANDATORY_CONTROLLER_RUNTIME_LIFETIME_WORK_ORDER.md`
- `docs/work-order/PR11_FULL1902_HARDWARE_VALIDATION_ROUTING_AND_STARTUP_FIXES_WORK_ORDER.md`
- `docs/work-order/STARTUP_UPDATE_PR_A_DECOUPLE_CHECK_DOWNLOAD_FROM_CONTROLLER_STARTUP_WORK_ORDER.md`

The current Full1902 product contract remains authoritative: when Center M is Disabled, the background Runtime is mandatory and is the controller authority for the interactive Windows session.

---

## 2. Goal

Change the Addon-owned Windows Task Scheduler startup task so newly created or otherwise legitimately recreated tasks launch the Runtime at **Above Normal process priority** instead of Task Scheduler's default Below Normal priority.

Desired Task Scheduler setting:

```text
Priority = 2
```

Microsoft Task Scheduler maps:

```text
Priority 2 / 3 -> ABOVE_NORMAL_PRIORITY_CLASS
Priority 4 / 5 / 6 -> NORMAL_PRIORITY_CLASS
Priority 7 / 8 -> BELOW_NORMAL_PRIORITY_CLASS
```

Task Scheduler's default is `7`, so the current Addon task is implicitly launched Below Normal because `StartupRegistration.cs` does not set `Settings.Priority`.

Reference:

- Microsoft Learn: `TaskSettings.Priority property`
  - https://learn.microsoft.com/en-us/windows/win32/taskschd/tasksettings-priority

This PR intentionally chooses:

```text
Priority = 2
```

Do not use:

```text
0 = Realtime
1 = High
```

The intent is only to give the mandatory controller Runtime better scheduling opportunity during Windows logon contention, not to aggressively preempt the rest of the system.

---

## 3. Current implementation fact

`WindowsOwnedStartupTaskStore.Register(...)` currently creates the owned task with:

```csharp
dynamic taskDefinition = service.NewTask(0);
taskDefinition.RegistrationInfo.Description = "Starts Steam Input Addon for Claw after Windows logon.";
taskDefinition.Principal.UserId = configuration.UserId;
taskDefinition.Principal.LogonType = WindowsTaskSchedulerStartupManager.TaskLogonInteractiveToken;
taskDefinition.Principal.RunLevel = 0;

dynamic settings = taskDefinition.Settings;
settings.DisallowStartIfOnBatteries = false;
settings.StopIfGoingOnBatteries = false;
settings.ExecutionTimeLimit = WindowsTaskSchedulerStartupManager.NoExecutionTimeLimit;
```

There is no explicit:

```csharp
settings.Priority = ...;
```

Therefore Task Scheduler uses its default priority value `7`.

The startup task already correctly uses:

```text
LogonTrigger
InteractiveToken
RunLevel = 0 / least privilege
--background
battery start allowed
battery continuation allowed
no execution time limit
```

Preserve all of those contracts.

---

## 4. Required production change

Keep this PR local to the existing Task Scheduler owner.

Add one explicit constant near the existing scheduler constants, for example:

```csharp
internal const int StartupTaskPriority = 2;
```

Then set the Task Scheduler priority when constructing the owned task:

```csharp
dynamic settings = taskDefinition.Settings;
settings.Priority = WindowsTaskSchedulerStartupManager.StartupTaskPriority;
settings.DisallowStartIfOnBatteries = false;
settings.StopIfGoingOnBatteries = false;
settings.ExecutionTimeLimit = WindowsTaskSchedulerStartupManager.NoExecutionTimeLimit;
```

Exact constant naming may follow current repository conventions, but the production value must be exactly:

```text
2
```

Do not set the value through a new manager, wrapper, configuration object, settings file, registry value, or user preference.

This is a product startup policy, not a user-configurable feature.

---

## 5. Do NOT change Runtime elevation

Task Scheduler process priority and Windows elevation are separate concerns.

Preserve:

```csharp
taskDefinition.Principal.RunLevel = 0;
```

Do not change it to Highest.

Do not copy ClawTweaks' elevated-helper task policy.

The Addon Runtime must remain least-privilege in normal operation. Existing bounded elevated children remain limited to the operations that already require elevation.

Required invariant:

```text
Task priority = Above Normal
Runtime privilege = unchanged / least privilege
```

---

## 6. Do NOT add process-wide priority mutation code

Do not add any Runtime call such as:

```csharp
Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;
```

or Win32:

```text
SetPriorityClass(...)
```

This PR is specifically about the Windows-logon startup task.

The normal product path starts the persistent Runtime through that scheduled task. A manual/debug launch that does not originate from the task does not need to be force-promoted by this PR.

Do not create a second priority authority inside `Program.Main()`, `RuntimeProcessApplication`, or `AddonProcessHost`.

---

## 7. Existing-task migration policy — do not force UAC for this tuning only

This PR must **not** make scheduler priority part of the current `IsCompliant(...)` repair contract.

In particular, do not extend `OwnedStartupTaskState` solely to read/store `Settings.Priority`, and do not add logic such as:

```csharp
&& state.Priority == StartupTaskPriority
```

to `WindowsTaskSchedulerStartupManager.IsCompliant(...)`.

Reason:

The current production repair path is intentionally:

```text
read-only verify
→ if materially compliant: no rewrite / no UAC
→ if missing or materially drifted: one bounded elevated repair
```

Priority is a performance tuning property, not a controller-authority or persistence-safety invariant.

Treating an existing default-priority task as materially non-compliant would cause otherwise healthy existing installations to request an elevated task rewrite solely for a scheduling optimization.

That is not justified.

Required migration behavior:

```text
new task
→ Priority 2 automatically

existing task that is already materially compliant
→ leave it untouched
→ no UAC solely for priority

existing task later recreated because it is genuinely missing/materially drifted
→ new definition gets Priority 2
```

For development/hardware validation, explicitly recreate the Addon-owned task once so the new priority can be tested immediately.

Do not add a one-time migration marker, version flag, registry value, or persisted "priority migrated" state.

---

## 8. Full1902 lifecycle contracts that must remain unchanged

This PR must not modify any controller lifecycle or authority behavior.

Preserve:

```text
Center M Enabled
→ MSI / stock authority
→ PID1901 desired

Center M Disabled
→ Addon Runtime authority
→ mandatory background Runtime
→ PID1902 desired
→ persistent HidHide baseline
→ Addon-owned DirectInput
→ canonical VIIPER Runtime
→ exactly one active virtual presentation when healthy
```

Do not change:

- PID1901 ↔ PID1902 transition logic;
- PnP settle timing;
- DirectInput settle timing;
- HidHide ownership;
- VIIPER creation/attach/teardown;
- X360 / SteamDeck presentation selection;
- suspend/hibernate/resume behavior;
- restart/crash/shutdown policy;
- Center M authority transitions;
- startup update behavior implemented by PR #523;
- Task Scheduler trigger timing;
- Task Scheduler RunLevel;
- task battery settings;
- task execution-time limit;
- startup task repair/elevation policy.

Do not add any new lifecycle state or synchronization mechanism.

---

## 9. Tests

Keep tests narrow. Do not create a new scheduler abstraction or integration framework solely to inspect one COM property.

### 9.1 Production source contract

Add a focused test proving the production task definition explicitly sets Above Normal priority.

A small source-contract test is acceptable because the production COM `dynamic` object is not represented by the existing fake store.

For example, verify `StartupRegistration.cs` contains the production assignment:

```csharp
settings.Priority = WindowsTaskSchedulerStartupManager.StartupTaskPriority;
```

and verify:

```csharp
WindowsTaskSchedulerStartupManager.StartupTaskPriority == 2
```

Do not refactor the production task store merely to make this property easier to mock.

### 9.2 Existing compliant task remains read-only

Preserve the existing tests proving:

```text
Existing compliant task
→ no RegisterTaskDefinition
→ no elevated helper
→ no UAC
```

Add or adjust a focused assertion only if needed to make clear that priority is intentionally **not** part of `IsCompliant(...)`.

A useful regression contract is:

```text
priority tuning does not make an otherwise compliant task drifted
```

Do not add `Priority` to the fake `OwnedStartupTaskState` just to test that statement.

### 9.3 Existing scheduler safety settings remain unchanged

Preserve coverage for:

```text
RunLevel = 0
InteractiveToken
--background
DisallowStartIfOnBatteries = false
StopIfGoingOnBatteries = false
ExecutionTimeLimit = PT0S
```

### 9.4 Full suite

Run at minimum:

```text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Release --no-build
git diff --check
```

Use the repository's normal restore/no-restore form as appropriate for the working environment.

---

## 10. Hardware / Windows validation

After the code is built, validate with an actually recreated Addon startup task.

Do not infer success from source alone.

### 10.1 Recreate the owned startup task once

On the validation machine, remove/recreate the existing Addon-owned scheduled task through the existing supported setup/repair flow so the new definition is written.

Do not modify unrelated tasks.

### 10.2 Verify the registered task

Verify the owned task:

```text
Task Name = Steam Input Addon for Claw
Trigger = current-user logon
Action = stable Addon executable --background
RunLevel = least privilege
Priority = 2
Battery restrictions = disabled
ExecutionTimeLimit = PT0S
```

Exported task XML should contain conceptually:

```xml
<Priority>2</Priority>
```

### 10.3 Verify launched Runtime priority

After a cold/logon start through the scheduled task, verify the Addon Runtime process is launched as:

```text
Above Normal
```

Do not require the UI/frontend process to inherit or match this priority.

### 10.4 Lifecycle smoke checks

At minimum validate:

```text
cold boot / logon
→ background Runtime starts
→ Full1902 Disabled mode reaches controller ownership normally
→ virtual controller becomes usable

Restart Addon
→ controlled teardown/restart still works

Sleep / Resume
→ existing reacquire/reconcile path still works
```

This PR should not introduce any special test race or synthetic scheduler stress scenario.

---

## 11. Logging

No new per-start benchmark subsystem is required.

Do not add high-frequency logs.

If the existing task-registration log is touched, an optional low-cost field is acceptable when the task is actually created/recreated:

```text
Priority=2
```

Do not add priority logging to every steady-state `Synchronize(true)` call merely for this PR.

---

## 12. Explicit non-goals

Do not include any of the following in this PR:

- startup update changes;
- VeloPack changes;
- task trigger-delay changes;
- process priority mutation outside Task Scheduler;
- `High` or `Realtime` priority;
- Runtime elevation changes;
- Task Scheduler `RunLevel=Highest`;
- automatic rewrite of an existing healthy task solely for priority;
- one-time migration state;
- PID/PnP settle optimization;
- DirectInput changes;
- VIIPER changes;
- HidHide changes;
- phantom-device cleanup;
- controller recovery changes;
- new startup manager/state machine;
- user-facing priority setting;
- generalized scheduler configuration abstraction.

Keep this PR intentionally small.

---

## 13. Expected change shape

Production change should be approximately:

```diff
 internal const int TaskLogonInteractiveToken = 3;
+internal const int StartupTaskPriority = 2;
 ...
 dynamic settings = taskDefinition.Settings;
+settings.Priority = WindowsTaskSchedulerStartupManager.StartupTaskPriority;
 settings.DisallowStartIfOnBatteries = false;
 settings.StopIfGoingOnBatteries = false;
 settings.ExecutionTimeLimit = WindowsTaskSchedulerStartupManager.NoExecutionTimeLimit;
```

Plus focused tests only.

If implementation starts spreading into controller ownership, startup coordination, Runtime hosting, or UI code, stop and reduce scope.

---

## 14. Acceptance criteria

This work is complete when all of the following are true:

1. Newly created/recreated `Steam Input Addon for Claw` scheduled tasks explicitly use `Priority = 2`.
2. Task Scheduler therefore launches the background Runtime as `ABOVE_NORMAL_PRIORITY_CLASS`.
3. `RunLevel` remains `0` / least privilege.
4. Existing materially compliant tasks are **not** rewritten solely because their old priority is 7/default.
5. The existing startup task repair path does not gain another UAC prompt or migration mechanism.
6. There is no `SetPriorityClass`/`ProcessPriorityClass` mutation in Runtime code for this feature.
7. Full1902 controller authority/lifecycle code is unchanged.
8. Existing startup-task tests still pass and focused priority coverage is added.
9. Debug/Release build, full tests, and `git diff --check` pass.
10. Windows validation of a recreated task confirms `<Priority>2</Priority>` and an Above Normal launched Runtime.

---

## 15. PR boundary

One PR only:

```text
Startup Task Above-Normal Priority
```

Do not bundle the upcoming/independent work for:

```text
PID1902 PnP/DirectInput settle optimization
VIIPER phantom cleanup
other boot-time tuning
```

The purpose of this PR is intentionally narrow:

> **Give the mandatory Full1902 Runtime better scheduling opportunity at Windows logon, using the existing Task Scheduler owner and no new lifecycle authority.**
