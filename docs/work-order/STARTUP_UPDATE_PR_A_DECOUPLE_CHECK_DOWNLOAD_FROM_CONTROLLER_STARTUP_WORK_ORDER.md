# Work Order — Startup Update PR-A: Decouple Network Update Check/Download From Controller Startup

> Date: 2026-09-15  
> Status: Ready for implementation  
> Baseline: `main` at `eb303e14061bb10b53747c7093e33c06c81b7d05`  
> Scope: Update startup ordering only. **Do not include Task Scheduler priority changes or VIIPER phantom cleanup in this PR.**

## 1. Goal

Remove VeloPack/GitHub network update work from the controller startup critical path.

Today the Runtime does this:

```text
process start
→ VelopackApp.Build().Run()
→ StartupCoordinator
→ SilentUpdateGate
   → network update check
   → transient retry/backoff when applicable
   → update download when available
   → WaitExitThenApplyUpdates when available
→ hardware compatibility
→ controller topology / Full1902 admission
→ Runtime initialization
→ deferred Full1902 controller acquisition
→ VIIPER presentation
```

The update gate runs before the MSI Claw controller can become usable. A normal no-update network request therefore delays every startup, and boot-time network instability can delay it much longer.

Target behavior:

```text
process start
→ VeloPack hook/bootstrap handling only
→ single-instance ownership established
→ if a previously downloaded update is pending:
     apply it locally and restart before Runtime/controller startup
→ otherwise:
     hardware/controller startup proceeds immediately
→ Full1902 controller startup / first presentation completes
→ background update check starts
→ background update download completes if a newer version exists
→ DO NOT interrupt the current Runtime to apply it
→ downloaded update is applied on the next safe primary-process startup
```

The product rule is:

> **Network update availability must never gate controller availability. Downloading an update must never tear down a working Full1902 controller session.**

---

## 2. Read Before Implementation

Read the current authority documents before editing startup/lifecycle code:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md

docs/work-order/PR2_5_MANDATORY_CONTROLLER_RUNTIME_LIFETIME_WORK_ORDER.md
docs/work-order/PR4_DISABLED_BOOT_ADMISSION_WORK_ORDER.md
docs/work-order/PR5_PID1902_DIRECTINPUT_PHYSICAL_OWNERSHIP_WORK_ORDER.md
docs/work-order/PR6_FIRST_VIRTUAL_PRESENTATION_ATTACH_WORK_ORDER.md
docs/work-order/TRAY_RESTART_AND_OVERLAY_MENU_CLEANUP_WORK_ORDER.md
```

This work order supersedes the old update-order assumption in `TRAY_RESTART_AND_OVERLAY_MENU_CLEANUP_WORK_ORDER.md` section 9, where `Restart Addon` was documented as intentionally re-entering a blocking `SilentUpdateGate` before controller startup.

Do not otherwise reopen the tray-restart design in this PR.

---

## 3. Current Source State

### 3.1 `Program.Main()`

Current startup begins with:

```csharp
VelopackApp.Build()
    .OnBeforeUninstallFastCallback(_ => UninstallBootstrap.RunFastCallbackOnly())
    .Run();
```

Then logging and the current single-instance logic run.

VeloPack package version is currently:

```xml
<PackageReference Include="Velopack" Version="1.2.0" />
```

### 3.2 Blocking update gate

`StartupCoordinator.RunAsync()` currently starts with:

```text
Startup update gate entered
→ await IUpdateGate.RunAsync(...)
→ if RestartScheduled: abort Runtime startup
→ only then evaluate hardware/controller state
```

`AddonStartupCompositionFactory` injects:

```csharp
new SilentUpdateGate(updateRestartArguments)
```

### 3.3 Current update service

`SilentUpdateService.CheckDownloadAndScheduleAsync(...)` currently does:

```text
CheckForUpdatesAsync
→ if available: DownloadUpdatesAsync
→ WaitExitThenApplyUpdates
→ return restartScheduled = true
```

Transient network failures are retried with the existing bounded backoff:

```text
attempt 1
→ 2s delay
attempt 2
→ 5s delay
attempt 3
```

`SilentUpdateGate` also applies a two-minute overall timeout.

### 3.4 Full1902 controller startup is already deferred off the message loop

`AddonProcessHost.StartDeferredRuntimeStartup()` currently runs the real Disabled-mode controller critical path:

```text
TryStartDisabledModeControllerAsync(...)
→ physical PID1902 ownership
→ HidHide verification
→ DirectInput source
→ Win+G suppression arm/prove
→ first VIIPER presentation attach
```

Only after that attempt finishes does it clear:

```csharp
_disabledControllerStartupPending
```

and continue deferred feature startup.

This is the correct boundary for beginning non-critical network update work.

---

## 4. Product Decision

### 4.1 Network check/download moves after controller startup

Remove update network work from `StartupCoordinator` entirely.

The new startup critical path must not call:

```text
GithubSource
CheckForUpdatesAsync
DownloadUpdatesAsync
SilentUpdateGate
```

before controller startup/admission.

For Center M Disabled, background update work must not begin until the deferred Full1902 controller startup attempt has finished.

That means at minimum:

```text
TryStartDisabledModeControllerAsync(...) finished
→ _disabledControllerStartupPending cleared
→ then start background update check/download
```

It is acceptable for the controller attempt to have failed closed. The updater may still run afterward so a future release can be downloaded, but update activity must not compete with the controller startup attempt itself.

For Center M Enabled, use the same deferred-startup tail rather than creating a separate update-start path.

Do not add a timer, scheduler, update manager state machine, or a second process solely for this.

### 4.2 Download now, apply later

When an update exists:

```text
check
→ download
→ log that update is ready for next safe startup
→ keep current Runtime alive
```

Do **not** call an apply/restart method from the background update path.

Forbidden in the background path:

```text
WaitExitThenApplyUpdates
ApplyUpdatesAndRestart
Process restart
Runtime shutdown
controller teardown
```

The currently working PID1902 / DirectInput / HidHide / VIIPER session must remain untouched.

### 4.3 Apply a downloaded update on the next safe primary-process startup

A downloaded update still needs to install automatically.

However, do **not** simply rely on VeloPack's default startup auto-apply at the very beginning of `Main()`.

This application has a persistent background Runtime and normal secondary launches. A second manual launch or the `Restart Addon` replacement process can start while the old Runtime still owns live controller resources. VeloPack auto-apply runs before the Addon's single-instance gate, so allowing it to apply a pending package there can compete with an already-running Runtime and its controlled Full1902 teardown.

That is a realistic supported lifecycle, not a theoretical race.

Required safe ordering:

```text
VelopackApp hook/bootstrap handling
→ single-instance check

normal secondary launch while Runtime already exists
→ activate existing Runtime
→ exit secondary instance
→ DO NOT apply pending update

--restart replacement
→ wait until old Runtime fully releases the single-instance gate
→ become primary
→ now it is safe to apply pending update

cold/logon startup with no existing Runtime
→ become primary immediately
→ now it is safe to apply pending update
```

Then:

```text
safe primary + pending downloaded update
→ schedule local VeloPack apply
→ exit this pre-Runtime process
→ VeloPack applies package
→ VeloPack restarts with the same arguments
→ normal startup proceeds on updated version
```

The update apply is therefore paid only when an actual downloaded update exists, and only before controller resources for this process are created.

---

## 5. VeloPack 1.2.0 Contract To Use

The repository currently references VeloPack `1.2.0`.

Its `VelopackApp` implementation confirms:

```text
auto-apply is ON by default
pending local package newer than current version
→ UpdateExe.Apply(... restart: true, args)
```

The implementation passes the current launch arguments to the restarted application.

Its `UpdateManager` also exposes:

```csharp
VelopackAsset? UpdatePendingRestart
```

and:

```csharp
WaitExitThenApplyUpdates(
    VelopackAsset? toApply,
    bool silent = false,
    bool restart = true,
    string[]? restartArgs = null)
```

Use those existing capabilities. Do not invent a separate `pending-update.json` or version marker.

### Required bootstrap setting

Because safe apply must occur only after the Addon's single-instance ownership decision, disable VeloPack's automatic early apply:

```csharp
VelopackApp.Build()
    .SetAutoApplyOnStartup(false)
    .OnBeforeUninstallFastCallback(...)
    .Run();
```

Keep `VelopackApp.Build().Run()` at the beginning of `Main()` for VeloPack hook handling. Do not move it behind ordinary application initialization.

---

## 6. Recommended Minimal Implementation Shape

Exact naming may differ, but keep the ownership simple.

### 6.1 `Program.Main()` owns the safe pending-apply boundary

After the process has become the primary single instance, but **before** entering Runtime lifetime/controller initialization:

```csharp
using (singleInstanceGate)
{
    if (TrySchedulePendingUpdateApply(args))
        return;

    runtimeLifetimeEntered = true;
    ... normal Runtime startup ...
}
```

Important ordering:

```text
secondary-instance activation decision
< pending update apply
< RuntimeProcessApplication.Run()
```

For `--restart`, preserve the current behavior where the replacement waits for the old instance lock to be released first. Only after that wait succeeds may pending update apply run.

### 6.2 Reuse `VelopackUpdateClient` rather than create another updater stack

Prefer extending the current VeloPack wrapper with the minimum local-pending capability instead of creating a second unrelated update abstraction.

Conceptually:

```csharp
internal bool TrySchedulePendingUpdateApply(string[]? restartArguments)
{
    var pending = _operations.UpdatePendingRestart;
    if (pending is null)
        return false;

    _operations.WaitExitThenApplyUpdates(
        pending,
        silent: true,
        restart: true,
        restartArguments);
    return true;
}
```

The exact fakeable operations interface may be adjusted to expose the current local pending asset.

This method performs **no network access**.

### 6.3 Background service becomes check + download only

Rename/refine the current update method so its semantics are explicit, for example:

```csharp
CheckAndDownloadAsync(...)
```

Desired behavior:

```csharp
if (!IsInstalled)
    return NoUpdate;

if (!await CheckForUpdatesAsync(...))
    return NoUpdate;

await DownloadUpdatesAsync(...);
return Downloaded;
```

Do not preserve a misleading `Schedule` name once it no longer schedules apply.

A small result enum/record is acceptable only if it materially improves tests/logging. A simple bool such as `downloaded` is also sufficient. Do not add a generalized update state model.

### 6.4 Keep the existing bounded retry policy

The existing transient retry policy can remain because it no longer blocks controller startup:

```text
immediate
→ 2s
→ 5s
```

Retain a bounded overall timeout (currently two minutes) for the background operation so broken network state cannot leave an update operation alive indefinitely.

Move that timeout out of the deleted startup gate into the narrow background update runner.

Do not retry non-network programming/filesystem/state failures merely because the operation is now background.

### 6.5 Start background update from the deferred-startup tail

Add one process-lifetime tracked background update task, for example:

```csharp
private Task? _backgroundUpdateTask;
```

At the tail of `StartDeferredRuntimeStartup()` after the controller critical attempt is finished:

```text
TryStartDisabledModeControllerAsync(...) finishes
→ clear _disabledControllerStartupPending
→ existing deferred profile/power startup as appropriate
→ start background update operation once
```

The exact placement after other already-deferred local feature startup is flexible. The hard invariant is:

> **No update network work may start before the Full1902 controller startup attempt has completed.**

Do not place the update call inside `TryStartDisabledModeControllerAsync()` or any controller owner.

### 6.6 Shutdown behavior

Use the existing process cancellation token/lifetime. When Runtime shutdown begins:

```text
cancel background update check/download
→ do not schedule apply
→ do not delay controller teardown waiting for network retries
```

It is fine to track/observe the background task so exceptions are not lost. Do not create a shutdown barrier/state machine solely for it.

If the current VeloPack check wrapper returns promptly on cancellation while observing its late underlying task, preserve that behavior.

---

## 7. Remove Obsolete Blocking-Gate Code

After moving the feature, remove dead startup-gate structure rather than retaining two update policies.

Expected cleanup includes, if no other live callers remain:

```text
src/SteamInputAddonforClaw/Startup/SilentUpdateGate.cs
src/SteamInputAddonforClaw/Startup/IUpdateGate.cs
UpdateGateResult
StartupCoordinator._updateGate
StartupCoordinator constructor update-gate parameter
StartupCoordinator update-gate logging / RestartScheduled early return
AddonStartupCompositionFactory updateRestartArguments parameter
AddonProcessHost._updateRestartArguments
AddonProcessHost constructor updateRestartArguments parameter
AddonProcessStartupOutcome.UpdateRestartScheduled
RuntimeProcessApplication UpdateRestartScheduled branch
```

Search before deletion and keep anything still used by a real production path.

Do not retain `RestartScheduled` compatibility solely for historical tests.

---

## 8. Full1902 Safety Requirements

This PR must not change controller authority semantics.

### Center M Disabled

Still required:

```text
Desired PID = PID1902
persistent HidHide baseline remains authoritative
DirectInput is process-owned
exactly one VIIPER presentation is attached when healthy
controlled Runtime restart does not restore PID1901
```

A background update download must not touch any of these.

### Pending-update apply

The safe primary-process pending-apply step occurs **before** controller Runtime initialization, so there is nothing from the new process to tear down.

For `Restart Addon`, the old Runtime must already have completed its normal controlled teardown before the replacement process passes the single-instance gate and schedules update apply.

Do not add special PID1901 restoration for updates.

### Update failures

All of these remain update-local failures:

```text
GitHub unavailable
DNS/network failure
timeout
download failure
checksum failure
VeloPack update lock failure
```

They must never cause:

```text
PID mode change
HidHide mutation
VIIPER detach
Runtime exit
controller neutralization
Center M authority change
```

Log and continue the current Runtime session.

---

## 9. Logging

Keep logs sufficient to validate ordering without adding a benchmark subsystem.

Recommended events/messages:

```text
Update.BackgroundCheckStarted
Update.NoUpdate
Update.DownloadCompleted
Update.BackgroundCheckFailed
Update.PendingApplyDetected
Update.PendingApplyScheduled
```

Include useful fields where already cheap:

```text
ElapsedMs
ExceptionType
Action
```

The important log-order acceptance condition on Center M Disabled boot is:

```text
controller startup / first presentation completion
< Update.BackgroundCheckStarted
```

Do not add high-frequency polling logs.

---

## 10. Tests

Update/add focused tests for the real product contracts.

### 10.1 StartupCoordinator no longer owns updates

Remove `FakeUpdateGate` dependencies from `StartupCoordinatorTests` and unsupported-hardware tests.

Verify supported startup proceeds directly into hardware/controller authority evaluation without invoking update APIs.

### 10.2 Background no-update path

```text
installed app
+ no update
→ check once
→ no download
→ no apply/restart
```

### 10.3 Background update path

```text
update available
→ check
→ download
→ current Runtime remains alive
→ no WaitExitThenApplyUpdates / ApplyUpdatesAndRestart from background path
```

### 10.4 Retry behavior remains bounded

Preserve coverage for:

```text
transient check failure → retry
transient download failure → retry
2s + 5s maximum backoff
non-transient failure → no retry
cancellation → no retry
```

Update test names from `CheckDownloadAndScheduleAsync` to the new check/download-only semantics.

### 10.5 Update begins after controller critical startup

Add a focused host/source-contract test proving the background update start call is ordered after the deferred controller startup attempt.

Do not construct a synthetic instruction-level race test.

### 10.6 Pending update does not apply from a secondary launch

Verify the ordering in `Program.Main()`:

```text
normal secondary launch
→ detects non-primary instance
→ activates primary
→ returns
```

before any pending-update apply call.

This prevents a user double-click from terminating/updating the live controller Runtime.

### 10.7 `Restart Addon` waits for old Runtime before pending apply

Preserve the current `--restart` single-instance wait contract and verify pending apply is checked only after the replacement becomes primary.

Do not bypass the old Runtime's controlled shutdown.

### 10.8 Launch arguments survive update apply

When pending apply is scheduled, pass the current application arguments as VeloPack restart arguments.

Important cases:

```text
--background
--restart
manual launch with no background flag
```

The updated process must preserve the intended launch mode.

If `--restart` is preserved through update apply, the existing single-instance retry logic must still converge normally because the previous Runtime is already gone. Do not special-case it unless a concrete test demonstrates a problem.

### 10.9 VeloPack early auto-apply is explicitly disabled

Add a source/contract assertion that `Program.Main()` uses:

```csharp
.SetAutoApplyOnStartup(false)
```

so future cleanup does not accidentally move pending apply back ahead of the single-instance safety boundary.

### 10.10 Background update cancellation

Verify Runtime shutdown cancellation prevents a background check/download from scheduling any apply/restart action and does not materially delay shutdown.

---

## 11. Likely Files

Expected primary files:

```text
src/SteamInputAddonforClaw/Program.cs
src/SteamInputAddonforClaw/Hosting/RuntimeProcessApplication.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Startup/StartupCoordinator.cs
src/SteamInputAddonforClaw/Startup/AddonStartupComposition.cs
src/SteamInputAddonforClaw/Startup/IUpdateGate.cs                 [likely delete]
src/SteamInputAddonforClaw/Startup/SilentUpdateGate.cs            [likely delete]
src/SteamInputAddonforClaw/Updates/IUpdateClient.cs
src/SteamInputAddonforClaw/Updates/VelopackUpdateClient.cs
src/SteamInputAddonforClaw/Updates/SilentUpdateService.cs

tests/SteamInputAddonforClaw.Tests/StartupCoordinatorTests.cs
tests/SteamInputAddonforClaw.Tests/UnsupportedHardwareStartupGateTests.cs
tests/SteamInputAddonforClaw.Tests/SilentUpdateServiceTests.cs
tests/SteamInputAddonforClaw.Tests/VelopackUpdateClientTests.cs
tests/SteamInputAddonforClaw.Tests/RuntimeProcessApplicationShutdownTests.cs
[focused AddonProcessHost/update-order contract test as appropriate]
```

Search the repository for these before finalizing:

```text
IUpdateGate
UpdateGateResult
SilentUpdateGate
RestartScheduled
CheckDownloadAndScheduleAsync
WaitExitThenApplyUpdates
updateRestartArguments
```

---

## 12. Non-Goals

Do **not** include any of the following in this PR:

```text
Task Scheduler Priority 7 → Normal / Above Normal
Task Scheduler task re-registration policy
startup timing benchmark framework
VIIPER phantom device inventory/cleanup
PID1902 polling interval changes
DirectInput settle changes
new update UI / toast / tray command
manual Check for Updates command
update channel switching
VeloPack package-version upgrade
Windows service / supervisor / watchdog
new controller authority state
new lifecycle state machine
```

Task priority benchmarking is intentionally a separate follow-up after this larger deterministic startup delay is removed.

---

## 13. Acceptance Criteria

Implementation is acceptable when all of the following are true:

1. A normal boot with no pending downloaded update performs **zero update network I/O before Full1902 controller startup completes**.
2. `StartupCoordinator` no longer waits for GitHub/VeloPack update availability.
3. Center M Disabled controller startup / first presentation attempt completes before background update check begins.
4. An available update downloads without interrupting the current controller session.
5. Background update completion does not call immediate apply/restart.
6. A downloaded update is applied automatically on the next **safe primary-process startup**.
7. A normal secondary launch while Runtime is alive does not apply the pending update.
8. `Restart Addon` allows the old Runtime to finish controlled teardown and release the single-instance gate before pending update apply begins.
9. Pending apply preserves launch arguments, including `--background`.
10. VeloPack early auto-apply is disabled so apply cannot occur before the Addon's single-instance decision.
11. Update failure is fail-open for application/controller operation and never mutates Full1902 controller authority.
12. Existing Full1902 sleep/resume, restart, HidHide, DirectInput, VIIPER, and PID1902 authority contracts remain unchanged.
13. Existing update retry/backoff remains bounded and cancellation-aware.
14. All affected tests pass.

---

## 14. Validation

### 14.1 No-update cold boot

With Center M Disabled and normal network access:

```text
cold boot / logon
→ Addon starts
→ controller becomes usable
→ only afterward background update check appears in log
```

Confirm no early `SilentUpdateGate`/GitHub check remains.

### 14.2 Slow/unavailable network

Start with network unavailable or temporarily unavailable:

```text
controller startup must proceed normally
background update retries may occur afterward
```

Controller-ready timing must not wait for the 2s/5s retry backoff.

### 14.3 Update available

Publish/use a newer valid test release:

```text
current Runtime controller remains usable
→ update downloads in background
→ no Runtime restart occurs when download completes
```

### 14.4 Secondary manual launch with update pending

While the background Runtime is still running and a downloaded update is pending:

```text
launch app manually
→ existing Runtime activates/opens UI
→ running Runtime is not killed/restarted
→ pending package remains pending
```

### 14.5 Controlled Restart Addon with update pending

```text
Restart Addon
→ old Runtime performs ordinary Full1902 controlled teardown
→ replacement waits for single-instance release
→ replacement becomes primary
→ pending update apply is scheduled
→ updated process restarts
→ Full1902 controller reconciles normally
```

### 14.6 Reboot/logon with update pending

```text
Windows restart
→ mandatory Addon startup task launches process
→ process becomes primary
→ pending update applies locally
→ updated Addon restarts with --background preserved
→ Full1902 controller startup runs
```

This is the only startup where update application is intentionally allowed to precede controller readiness, because a real already-downloaded update exists and no old Runtime is alive.

---

## 15. Implementation Principle

Keep the design small:

```text
one existing VeloPack wrapper
one background check/download task
one safe primary-process pending-apply boundary
```

Do not create an `UpdateManagerService`, `UpdateLifecycleManager`, persisted update state, or another authority layer.

The desired ownership is straightforward:

```text
Program / single-instance boundary
→ decides when pending local package may safely apply

AddonProcessHost deferred startup tail
→ decides when non-critical background network update work may begin

SilentUpdateService / VelopackUpdateClient
→ perform check/download mechanics only
```

This removes a guaranteed startup dependency while preserving the Full1902 rule that controller authority and teardown remain owned by the Runtime lifecycle, not by the updater.
