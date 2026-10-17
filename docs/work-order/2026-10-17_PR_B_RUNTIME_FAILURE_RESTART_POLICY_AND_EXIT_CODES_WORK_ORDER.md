# Work Order — PR-B: Bounded Runtime Failure Restart via the Existing Windows Startup Task

> Date: 2026-10-17
> Repo: `onehoon/SteamAddonforClaw`
> Source reviewed: `main` @ `937825aa862cccb88ebd29551e9e36378f9f11d6` (PR #825 already squash-merged)
> Audit finding: **03-001** (unexpected Runtime process death leaves durable PID1902/HidHide authority without process-owned VIIPER)
> Prerequisite: **PR-A / #825 already implemented** task-read failure handling, status and manual repair
> Delivery: **one PR, two focused commits**; local Codex implements code/tests, user performs real handheld validation **after merge**
> Product scope: standalone Full PID1902; one supported Windows administrator user and one interactive session

## 1. Decision and scope

Implement the **smallest bounded Windows Task Scheduler failure-restart policy** on the **one existing Addon-owned mandatory logon task**, and ensure that a genuinely fatal Runtime failure does **not** silently return exit code zero.

No new Windows Service, watchdog executable, always-running monitor, heartbeat, extra Task Scheduler task, polling daemon, recovery manager, or new controller state authority.

The important distinction is:

- **Scheduled Runtime**: a process launched **directly as the scheduled task action** can be restarted by that task's configured restart-on-failure policy when Windows Task Scheduler observes task failure. This is the concrete supported recovery path implemented by this PR.
- **Out-of-band Runtime**: today's tray `Restart Addon` starts its successor with `Process.Start()`, and Velopack update handoff launches a replacement outside the original running task. **Those successor processes are not proved to be watched by the original task**. Do not assert that Task Scheduler watches any PID just because it is the same EXE.
- **Manual launch**: no automatic protection is promised for a Runtime launched from the desktop rather than as a scheduled task.
- An unexpected process death while the controller is hidden is a **real safety/availability gap** under Full1902. This PR provides **bounded protection for scheduled-task-owned lifetimes**, not an invented full-coverage guarantee for all possible launch topologies.

**Do not mark audit 03-001 as universally resolved for all start/restart/update origins merely by registering `RestartCount`/`RestartInterval`.** Document the coverage limit explicitly in the PR description. Preserving an honest, bounded improvement is preferable to speculative extra daemons or replacing the current validated restart/update architecture.

This PR contains **no change** to the automatic startup status UI, Repair button, frontend protocol v86, or the meaning of "Auto start · OK": it means the *owned task definition* was positively verified, including the new bounded-restart settings, **not** that a particular current Runtime PID is supervised.

### Two commits

| Commit | Focus | Suggested subject |
|---|---|---|
| 1 | Add Task Scheduler bounded failure restart fields and exact readback-compliance/repair tests | `feat(startup): verify bounded restart-on-failure policy for owned task` |
| 2 | Propagate true fatal Runtime outcomes as nonzero process exit codes while keeping intentional handoffs clean; test classifications | `fix(runtime): expose fatal startup and message-loop failures to task scheduler` |

No UI work, no new IPC method, no task-health monitoring or generic process restart refactor.

## 2. Architectural authority and external API documentation

Read **before editing**:

1. `docs/Full 1902 Implementation/README.md` — current document precedence.
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md` — exact mandatory startup task, deterministic persistent HidHide and controller authority.
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md` — reboot-bound authority and real lifecycle.
4. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md` — mandatory Runtime/VIIPER ownership and post-crash recovery gap.
5. `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md` — Runtime **Highest** run level and current same-user privilege model.
6. `docs/work-order/2026-10-17_PR_A_MANDATORY_STARTUP_TASK_REPAIR_AND_DEVICE_STATUS_WORK_ORDER.md` — PR-A/#825 integration contract.
7. `docs/work-order/TRAY_RESTART_AND_OVERLAY_MENU_CLEANUP_WORK_ORDER.md` — current controlled Runtime replacement.
8. `docs/work-order/STARTUP_UPDATE_PR_A_DECOUPLE_CHECK_DOWNLOAD_FROM_CONTROLLER_STARTUP_WORK_ORDER.md` — current pending-update handoff; older implementation snippets must be reconciled against today's code.

Microsoft Task Scheduler official references, not guesses:

- `https://learn.microsoft.com/en-us/windows/win32/taskschd/taskschedulerschema-restartonfailure-settingstype-element`: `RestartOnFailure` requires **Count** and **Interval**.
- `https://learn.microsoft.com/en-us/windows/win32/taskschd/tasksettings-restartinterval`: `TaskSettings.RestartInterval` is ISO 8601 duration, **minimum one minute**; changing interval requires restart count.
- `https://learn.microsoft.com/en-us/windows/win32/api/taskschd/nn-taskschd-itasksettings`: `ITaskSettings.RestartCount`/`RestartInterval` COM properties.

These sources document the restart **task settings**; they do not establish that independently created child/successor PIDs are tracked as the same running task. Do not infer that.

## 3. Verified source inventory — current main after PR #825

| File | Observed baseline / integration |
|---|---|
| `src/SteamInputAddonforClaw/Install/StartupRegistration.cs` | `OwnedStartupTaskState` currently records Enabled, action path/args, logon user/type, RunLevel, battery constraints and `ExecutionTimeLimit`; `WindowsOwnedStartupTaskStore.Read` obtains settings from COM; `Register` builds/updates exactly one named task, with `--background`, LogonTrigger, `Highest`, battery-friendly persistent task. `WindowsTaskSchedulerStartupManager.IsCompliant` checks these settings. There is **no** `RestartCount`/`RestartInterval`. |
| `src/SteamInputAddonforClaw/Settings/StartupSettingsCoordinator.cs` | PR-A stores `LatestStartupRegistration`; `EnsureStartupRegistration()` catches scheduler exceptions and reports failure. Continue using this exact owner and PR-A Device Repair path. |
| `src/SteamInputAddonforClaw/Runtime/AddonRuntimeComposition.cs` | Normal High Runtime initialization performs one mandatory startup-task ensure and logs result. Uninstall-preparation-only path skips the ensure. |
| `src/SteamInputAddonforClaw/Hosting/RuntimeProcessApplication.cs` | `Run()` currently returns `void`; unsupported/indeterminate hardware and other non-ready startup outcomes return normally; a caught startup or message-loop exception is **logged then swallowed** in `catch`, leading to successful process exit after cleanup. `RequestRestart()` launches a successor via `Process.Start` and `--restart`, then performs controlled teardown; safe uninstall likewise performs controlled teardown only after stock-safety proof. |
| `src/SteamInputAddonforClaw/Program.cs` | `Main()` invokes `new RuntimeProcessApplication(...).Run()` without setting an exit code from its result. Intentional successful paths return normally: Velopack callbacks; supported worker modes; same-user elevation bootstrap handoff; secondary-instance activation; pending Velopack apply. A thrown top-level exception already exits abnormally; do not double-wrap it. |
| `src/SteamInputAddonforClaw/Lifecycle/NativeMessageLoop.cs` | `Run()` throws on Win32 `GetMessageW == -1` or initial ready-post failure; normal `WM_QUIT` exits the loop. `RequestExit()` is the existing explicit quit mechanism. |
| `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs` | Owns startup outcomes (`RuntimeReady`, `UnsupportedHardware`, `IndeterminateHardware`, `Canceled`), `EvaluateUserRestart()`, `BeginProcessShutdown()`, deferred Full1902 startup, existing teardown, and persistent HidHide/presentation owners. No new lifecycle owner needed. |
| `src/SteamInputAddonforClaw/Updates/VelopackUpdateClient.cs` | `TrySchedulePendingUpdateApply()` calls `WaitExitThenApplyUpdates(..., restart: true, restartArgs: ...)`. Original scheduled process may intentionally return zero while updated process is launched via Velopack: this is **not** a scheduler-owned replacement process guarantee. |
| `src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs` and Device UI | PR-A auto-start status/Repair is driven by `StartupSettingsCoordinator.LatestStartupRegistration`. Do not add a second status/indicator. |
| `tests/SteamInputAddonforClaw.Tests/WindowsTaskSchedulerStartupManagerTests.cs` | Existing fake `IOwnedStartupTaskStore` already verifies zero rewrite for compliant tasks, exactly one write for drift and missing tasks, registration failures, no mistaken reads, readback settle and deletion. |
| `tests/SteamInputAddonforClaw.Tests/RuntimeProcessApplicationShutdownTests.cs`, `NativeMessageLoopTests.cs`, `StartupUpdateContractTests.cs`, `VelopackUpdateClientTests.cs`, `AddonProcessHostRestartTests.cs` | Existing shutdown, restart and update contracts; preserve all their original behavioral assertions. |

## 4. Commit 1 — One owned Task Scheduler failure restart policy

### 4.1 Exact policy constants

Use the **existing `WindowsTaskSchedulerStartupManager`** (not a new settings owner) to define:

```csharp
internal const int RestartOnFailureCount = 3;
internal const string RestartOnFailureInterval = "PT1M";
```

These values mean **at most three Windows Scheduler restart attempts using its one-minute restart interval**, subject to Task Scheduler's own semantics and ability to launch the task. Do not promise an exact controller recovery latency: firmware, Windows scheduler, post-crash device state, and physical reacquisition can vary.

Use the official COM API in `WindowsOwnedStartupTaskStore.Register`, alongside current battery/priority/time limit settings:

```csharp
dynamic settings = taskDefinition.Settings;
settings.Priority = WindowsTaskSchedulerStartupManager.StartupTaskPriority;
settings.DisallowStartIfOnBatteries = false;
settings.StopIfGoingOnBatteries = false;
settings.ExecutionTimeLimit = WindowsTaskSchedulerStartupManager.NoExecutionTimeLimit;
settings.RestartCount = WindowsTaskSchedulerStartupManager.RestartOnFailureCount;
settings.RestartInterval = WindowsTaskSchedulerStartupManager.RestartOnFailureInterval;
```

Keep the existing full task name, LogonTrigger, user identity, `Highest` run level, `--background` action, stable Velopack executable path, task priority, power settings and `PT0S` execution time limit.

Do not create a second task or invoke `schtasks.exe`/PowerShell for configuration.

### 4.2 Read exact installed settings, verify and repair drift

Extend the existing `OwnedStartupTaskState` with only:

```csharp
int RestartCount,
string RestartInterval
```

Read from the already opened `definition.Settings` in `WindowsOwnedStartupTaskStore.Read()`:

```csharp
int restartCount = (int)settings.RestartCount;
string restartInterval = (string)(settings.RestartInterval ?? string.Empty);
```

Include both in `IsCompliant` against the new constants (if Task Scheduler normalizes equivalent ISO 8601 durations, compare equivalently using a narrow built-in duration parser or stable canonical form instead of causing repeated false drift/re-registers; no custom duration framework).

The existing PR-A ensure behavior must remain:

- Read failed => `ReadFailed` and **no task rewrite**.
- Task absent => one `Register`, bounded verified readback.
- Task otherwise compliant including both new fields => zero writes/no UAC.
- Existing task missing either restart setting (the normal upgrade path) => one repair registration and verify **all** fields, including restart.
- Only one direct write, no retry loop or secondary elevation.
- Registration/access denied or unverified readback => `StartupRegistrationResult` failure; PR-A Device summary shows `Auto start · Error` with its existing Repair button.
- Uninstall still deletes **only this task** and positively verifies absence, not recreate it.
- The configured restart policy is a *task-definition requirement*, not proof of actual process survival.

**Compatibility note**: existing installed users have a compliant pre-PR-B task that lacks the new restart properties. They must receive **one ordinary automatic repair on next Runtime startup**, not require reinstall.

### 4.3 Tests — commit 1

Extend `WindowsTaskSchedulerStartupManagerTests.cs` and any narrowly affected original fake task states:

1. No-write compliant task includes `RestartCount=3`, `RestartInterval=PT1M`.
2. Previously-compliant task with no restart policy is recognized as drift and repaired exactly once.
3. Count-only / interval-only mismatch triggers exactly one write.
4. Readback missing either property prevents false success.
5. Genuine task-read error still blocks mutation (PR-A behavior).
6. Registration denied/unavailable returns failure; PR-A remains capable of manual retry, no extra UI.
7. Missing task writes only the original task with the full verified contract.
8. Uninstall remove verifies absence and never re-registers.
9. Battery operation, Highest run level, logon trigger/args and `PT0S` remain explicit regression assertions.
10. Running the verify step repeatedly on a compliant post-upgrade fake remains read-only.

Use the existing fake/task seam; **no real Task Scheduler registration by unit tests**.

## 5. Commit 2 — Fatal Runtime outcomes must return nonzero

### 5.1 Separate genuine runtime failure from intentional lifecycle

The currently swallowed exception:

```csharp
catch (Exception exception)
{
    AppLog.Error("Startup", "Runtime startup or message-loop execution failed; exiting cleanly.", exception);
}
// Run() returns void, Program.Main returns 0.
```

is a real failure path and must be corrected.

Implement the **smallest testable explicit result** from `RuntimeProcessApplication.Run()` to `Program.Main()` (e.g. return `int` with `0` / `1`, or a narrow `bool` success). Do **not** add a global process supervisor, new lifecycle authority, retry manager, state machine, or platform-wide result framework.

Illustrative shape:

```csharp
// RuntimeProcessApplication.Run() conceptual return policy:
//   UnsupportedHardware / IndeterminateHardware -> ExitSuccess (intentional refusal)
//   Canceled during accepted lifecycle          -> ExitSuccess
//   RuntimeReady + message loop exits via an accepted explicit shutdown
//                                                -> ExitSuccess
//   RunStartup/InitializeRuntime/GetMessage/ready-hook failure
//                                                -> ExitFailure
//   RuntimeReady + unrequested message-loop termination
//                                                -> ExitFailure

// In Program.Main at the existing primary-instance/runtime entry:
Environment.ExitCode =
    new RuntimeProcessApplication(runtimeArgs, singleInstanceGate).Run();
```

Minimal conceptual code around the existing `Run` body:

```csharp
internal int Run()
{
    // Keep the existing creation/wiring and cleanup; this is not a full replacement.
    var exitCode = 0;
    try
    {
        var outcome = _processHost!.RunStartupAsync().GetAwaiter().GetResult();
        if (outcome is AddonProcessStartupOutcome.UnsupportedHardware
            or AddonProcessStartupOutcome.IndeterminateHardware
            or AddonProcessStartupOutcome.Canceled)
            return 0; // accepted non-running/no-controller outcomes

        if (outcome != AddonProcessStartupOutcome.RuntimeReady)
            return 1;

        // Keep existing InitializeRuntimeAsync, tray and native loop.
        // ...
        _messageLoop!.Run(/* existing onReady */);
        if (Volatile.Read(ref _shutdownRequested) == 0)
        {
            AppLog.Error("Runtime", "Runtime message loop exited without an accepted shutdown.", null);
            exitCode = 1;
        }
    }
    catch (Exception exception)
    {
        AppLog.Error("Startup",
            "Runtime startup or message-loop execution failed.", exception);
        exitCode = 1;
    }
    finally
    {
        // Preserve BeginProcessShutdown and awaited DisposeAsync exactly as today.
    }
    return exitCode;
}
```

This snippet is **illustrative, not drop-in**: retain actual existing constructor/init ordering, nullability, pre-start activation, finally/teardown semantics and logging. Avoid `return` in `finally` or exiting before controller cleanup. If Cleanup throws, an existing top-level fatal path may still terminate abnormally; preserve actual controller teardown, do not silently suppress it to force a zero exit.

A tiny internal pure helper for choosing 0/1 is acceptable to make the branch classification unit-testable **only if needed**; it is not permission for another lifecycle manager.

### 5.2 Intentional exits must remain zero

Do **not** convert any of the following existing intentional product paths into Task Scheduler "task failed" signals:

- Normal `Restart Addon`: use `AddonProcessHost.EvaluateUserRestart()`, launch same EXE with `--restart`, controlled shutdown and message-loop quit **unchanged**. The old process must return zero on accepted handoff. Do not change it into an intentional `Environment.Exit(1)` to force restart.
- Successful **stock-safe uninstall**: existing positive restoration, mandatory task deletion, message-loop exit and cleanup. Must return zero. Failed stock preparation must keep current Runtime alive rather than return nonzero as a retry surrogate.
- Unsupported or indeterminate hardware: deliberate refusal to enter controller authority; never launch the scheduler on an unsupported Claw.
- Accepted cancellation from existing startup lifecycle: deliberate clean exit. Actual uncaught cancellation from other unexpected failing paths remains handled according to real exception provenance rather than globally treating any `OperationCanceledException` as success.
- Secondary instance invocation: activates the existing primary and exits zero; does not steal task ownership.
- Same-user UAC bootstrap handoff: medium process exits zero after successfully starting the elevated replacement; high Runtime remains the authority.
- Velopack **pending update**: current `WaitExitThenApplyUpdates` intentionally exits the pre-Runtime process with zero, permitting Velopack to start the updated replacement. Do not trigger a parallel Task Scheduler restart while an update is applying.
- Worker arguments: uninstall/bootstrap, Steam FSE registration, prerequisite setup, Windows App Runtime setup and their existing exit code contracts. Do not make helper successes or failures trigger normal Runtime crash recovery.
- Windows shutdown/logoff: no intentional re-launch loop on session end; do not add a daemon to defeat normal session teardown.
- Controller-only operation/acquisition/presentation failure already managed by existing **fail-close** (with persistent Runtime/tray/UI still available) is **not a fatal process failure**. Never escalate it to `ExitFailure` merely so Task Scheduler will restart.

### 5.3 Actual failures that must propagate

At least these currently reachable Runtime-level errors should no longer be silently successful:

- A thrown exception from `_processHost.RunStartupAsync()`, including genuinely fatal startup coordination failure.
- A thrown exception from `_processHost.InitializeRuntimeAsync()`, e.g. initialization failure that prevents creating the required Runtime.
- A thrown exception from the native `WM_RUNTIME_READY` startup callback / `StartRuntimeEventWatchers()` that aborts the Runtime loop.
- A `GetMessageW` failure.
- An unrequested `WM_QUIT`/message-loop termination while no accepted controlled shutdown was requested (where this can occur in actual Windows operation).
- An unhandled fatal exception that bubbles to the top-level Program catch already yields nonzero through standard .NET behavior; preserve it.

Do not promote deferred, deliberately caught feature-local controller failures into fatal process exits, or create artificial fatal-exit testing hooks in production.

### 5.4 Scheduled vs manual / replacement ownership coverage matrix

The PR description and release handoff must include:

| Current process origin | This PR's scheduler restart guarantee | Why |
|---|---|---|
| Task Scheduler's own original `--background` task process | **Bounded, eligible** after task failure observed by Scheduler | Task owns that launched instance |
| Manual app launch when no scheduled process is active | **Not guaranteed** | No task ownership of that PID |
| Tray `Restart Addon` successor via `Process.Start` | **Not guaranteed** | Replacement is launched outside Task Scheduler's original task |
| Velopack updated successor via `WaitExitThenApplyUpdates` | **Not guaranteed** | External updater handoff is not proven to remain Scheduler-owned |
| Secondary launch while Runtime is already primary | No new owner; must exit normally | SingleInstanceGate |
| Windows sign-out / shutdown | No user-session recovery expected | Session is ending |

Do not "fix" the uncovered rows in this PR by changing proven tray restart or update handoff, adding a background timer, adding a service, adding polling or scheduling a periodic second task. This PR is a **scheduler-managed failure restart hardening step**, and any decision to guarantee recovery across independent successor process lifetimes needs separate evidence and a dedicated explicit design decision.

**Critical honesty requirement**: failure-restart settings + correct exit codes are necessary and valuable, but not sufficient to guarantee continuous recovery after *every* possible Runtime death under today's process-launch topology.

## 6. Full1902 lifecycle invariants — preserve strictly

- `Center M Disabled` is durable **Addon controller authority**. A Runtime failure/restart does **not** authorize silently changing to stock `PID1901`, resetting persistent HidHide, claiming foreign ownership, or releasing the Addon task.
- In the crash window, PID1902 may remain hidden and process-owned VIIPER is gone. On a legitimate replacement Runtime, reuse the existing **Disabled-boot admission**, canonical VIIPER init, exact physical identity, HidHide reconciliation, `AcquireAsync`, controller presentation attachment, PnP recovery and suspend/resume owners. Do not create a shortcut takeover path or force a new firmware write just because startup followed a crash.
- `Center M Enabled` retains MSI stock `PID1901` and no Addon physical/virtual-controller takeover.
- `Enable Center M and Restart` and safe uninstall keep their existing verified teardown/stock restoration requirements.
- Uninstall task deletion must not be counteracted by a new scheduled recovery job; deletion/absence verification is already guarded.
- No process restart during working VIIPER/publisher or during normal feature-local failure. A crash is a **process death**, not `DirectInput`/USB/PnP loss in a still-running process.
- Sleep/hibernate/resume, battery operation, controller physical loss/re-enumeration, exact HidHide target ownership, VIIPER native teardown and rollback/fail-close remain supported as before.
- No extra privilege escalation: the owned task already runs `Highest`; the Runtime itself owns any direct COM task repair.
- Do not implement multi-user, RDP, Fast User Switching, cross-session watchdogs, or speculative epoch/barrier/lock systems.

## 7. Validation strategy (Codex/CI; no device blocker)

### Required automated tests

**Scheduler definition**

- Existing task compliant with restart count/interval => no rewrite.
- Older task missing restart policy => single rewrite, full compliance on bounded readback.
- Partial/broken restart definition => one rewrite or classified failure.
- Both count/interval proven by fake COM readback projection and existing store seam.
- Read failure, write denied and readback mismatch => no false Verified.
- Deleting task for stock-safe uninstall => still deletes only one task, verifies absence.
- Existing Center M admission, legacy Run cleanup and PR-A repair/status tests pass.

**Exit behavior**

- Startup-ready -> normal controlled restart request -> code 0, old process always tears down.
- Accepted stock-safe uninstall -> code 0; failed uninstall preparation remains running.
- Unsupported/indeterminate -> code 0 and no controller entry.
- Accepted startup cancellation -> code 0.
- Thrown Runtime initialization, WM_READY callback, and message-loop error -> code nonzero.
- Unexpected post-loop return without an accepted request -> code nonzero (if observed by the Run contract).
- Updating/restarting/secondary launch worker handoff remains code 0.
- Existing lifecycle tests for one native loop, one primary SingleInstanceGate, reenter with `--restart`, and pending Velopack update placement all pass.
- Feature-level controller failures stay fail-closed without global restart.

Prefer narrow extracted result-selection seam / available fake delegates in tests. Do not modify production architecture merely to create impossible race tests.

### Typical build/tests

```powershell
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release
git diff --check
```

Preserve the repository's CI publish/UI PRI gates. No Main UI source/resource changes are required.

### Hardware/operator verification — USER ROLE AFTER MERGE ONLY

For the user's later practical validation, **not** a local Codex completion criterion or a PR review blocker:

1. From an installed Windows session, inspect the existing named task's `RestartCount=3` and `RestartInterval=PT1M` plus unchanged Highest, trigger, path and battery settings.
2. Observe original scheduled-task-owned Runtime startup, controller acquire and VIIPER attach.
3. As the user, exercise a controlled terminate/crash of the actual scheduled Runtime and confirm whether the Scheduler restarts the task; capture task `LastTaskResult`/history and Runtime logs and verify actual PID1902 physical/HidHide/presentation restoration.
4. Verify normal tray Restart, successful uninstall, and Velopack update do not cause surprise parallel task activations.
5. Confirm real-world coverage for out-of-band successor lifetimes before considering 03-001 fully closed.

If actual Task Scheduler behavior contradicts assumptions, use **real evidence** to design the next narrow fix; hardware verification is **never required of Codex and never a pre-merge blocker**.

## 8. Non-goals and PR boundary

Do **not**:

- add Windows Service/scheduled polling supervisor/helper/watchdog or additional startup task;
- change single-instance, user/process elevation identity, tray menu, restart CLI, Velopack update protocol or frontend pipe protocol;
- add UI toggles for restart count, crash handling, background startup or a second Device card;
- change Center M controller authority or PnP/HidHide/VIIPER routing lifecycle;
- add unsafe direct PID1901 restore after a crash;
- change the safe-uninstall registration/removal contract;
- guarantee recovery of manual/child/update-replacement PIDs without evidence;
- add arbitrary timing race defenses or excessive retry algorithms.

The scope is strictly **one existing scheduled task's bounded restart settings + truthful Runtime exit codes**. The product may later need a separate explicit decision about full-coverage process supervision; that does **not** justify building such infrastructure in this PR.

## 9. Definition of done

- [ ] Existing named mandatory startup task uses `RestartCount=3`, `RestartInterval=PT1M`, with both values included in readback `IsCompliant`.
- [ ] Existing compliant task is read-only; old/drifted task gets at most one write and bounded verification.
- [ ] PR-A Device auto-start status/Repair remains the same UI and accurately reflects the now-extended task-definition contract.
- [ ] Fatal Runtime startup/native-loop exceptions no longer return process exit code zero.
- [ ] Explicit successful runtime restarts, successful uninstall, update handoffs, secondary launches and unsupported hardware exits remain intentional successes.
- [ ] Controller-local operation failures do not create restart loops.
- [ ] Crash recovery reuses existing Full1902 startup and fail-close owners, no new authority.
- [ ] Automated scheduler/exit policy and existing lifecycle/CI tests pass.
- [ ] PR clearly documents scheduler-owned lifetime coverage and out-of-band successor limitation.
- [ ] Real-device validation is assigned to the user **after merge** only.
