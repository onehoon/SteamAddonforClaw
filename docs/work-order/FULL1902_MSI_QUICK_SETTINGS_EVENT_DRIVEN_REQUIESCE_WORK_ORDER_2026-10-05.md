# Work Order - Full1902 MSI Quick Settings Event-Driven Re-Quiesce

## Status

Focused follow-up to merged PR #677.

Baseline:

~~~text
repository: onehoon/SteamAddonforClaw
branch: main
baseline commit: c6a4b0eddd5e101cc095db6abfa9aec96865fa41
PR #677 merge: 3f42b717043942b26a068e573f367da0e78cb7e0
date: 2026-10-05
~~~

This work order supersedes only the old PR #677 assumption that a single Disabled-startup quiesce is sufficient.

Do not reinterpret the historical PR #677 work order as current policy for process-start monitoring. Real supported-hardware evidence now proves that MSI Quick Settings can be reactivated later during the same Addon-authority Runtime lifetime.

The required follow-up is event-driven. Do not add polling.

---

# 1. Read before implementation

Read and preserve the current contracts in:

- docs/Full 1902 Implementation/README.md
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
- docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
- docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
- docs/work-order/FULL1902_POLICY_B_BIND_WING_GAMEBAR_SUPPRESSION_TO_ADDON_AUTHORITY_WORK_ORDER.md
- docs/work-order/FULL1902_MSI_QUICK_SETTINGS_RUNTIME_QUIESCE_WORK_ORDER_2026-10-05.md

Inspect current main implementations of at least:

- src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
- src/SteamInputAddonforClaw/GameBar/MsiQuickSettingsRuntimeQuiescer.cs
- src/SteamInputAddonforClaw/Controllers/Detection/WindowsDeviceArrivalWatcher.cs
- src/SteamInputAddonforClaw/CenterM/WmiMsiEventSource.cs
- src/SteamInputAddonforClaw/CenterMStartup/CenterMRebootAuthorityTransition.cs
- tests/SteamInputAddonforClaw.Tests/MsiQuickSettingsRuntimeQuiescerTests.cs
- tests/SteamInputAddonforClaw.Tests/Full1902WinGSuppressionAuthorityTests.cs

Do not restore CTW integration or retired Steam-session routing ownership.

---

# 2. New real-hardware evidence

Google Drive evidence:

~~~text
Addon/Log/1005/04
~~~

PR #677 itself works as implemented. The problem is not failed termination.

Observed sequence:

~~~text
18:29:23
Addon 0.1.327.0 starts after update/relaunch.

18:29:25
MsiQuickSettingsQuiesceNotRunning
CandidateCount=0
→ no Gamebar_Widget exists at startup.

18:32:15
another Addon Runtime starts.

18:32:18
MsiQuickSettingsQuiesceNotRunning
CandidateCount=0
→ again no Gamebar_Widget exists at startup.

18:33 onward
normal supported Runtime activity continues.
GameBar IsInputRedirected state transitions and WING/Event88 activity are observed.

18:36:30
user requests Addon Runtime Restart.

18:36:31
new Runtime starts.

18:36:32
the new Runtime discovers:
Gamebar_Widget PID=17052
PackageFullName=
9426MICRO-STARINTERNATION.MSIQuickSettings_2.0.71.0_x64__kzh8wxbdkxb8p

The exact-package scan also discovers PID=6876 with the same package identity.

Both are terminated successfully:
TerminatedProcessCount=2
FailureCount=0
~~~

Therefore:

~~~text
PR #677 startup termination works.
MSI Quick Settings was absent at prior startup.
MSI Quick Settings later reappeared during the same Runtime lifetime.
A later Runtime restart found and successfully killed it.
~~~

This is now a demonstrated normal-lifecycle reactivation, not a hypothetical race.

The current startup-only cleanup is insufficient.

---

# 3. Product decision

While exact Center M Disabled / Addon authority is active:

~~~text
MSI Quick Settings has no supported runtime role.
If Gamebar_Widget starts,
the Addon should immediately verify its package identity
and quiesce the exact MSI Quick Settings package runtime.
~~~

Use a Windows process-start event.

Do not poll the process table.

Microsoft documents Win32_ProcessStartTrace as the WMI event emitted when a new process starts. It supplies:

- ProcessID
- ParentProcessID
- ProcessName
- SessionID

References:

- https://learn.microsoft.com/en-us/previous-versions/windows/desktop/krnlprov/win32-processstarttrace
- https://learn.microsoft.com/en-us/previous-versions/windows/desktop/krnlprov/win32-processtrace
- https://learn.microsoft.com/en-us/dotnet/api/system.management.managementeventwatcher.start

ManagementEventWatcher.Start subscribes asynchronously and delivers matching events through EventArrived.

This is not a timer/watchdog loop.

---

# 4. Target architecture

Required lifetime:

~~~text
exact Center M Disabled proven
    ↓
start Gamebar_Widget process-start watcher
    ↓
run one startup reconcile for already-existing MSI Quick Settings
    ↓
continue ordinary Full1902 startup

Runtime lifetime
    ↓
Windows emits Gamebar_Widget process-start event
    ↓
receive ProcessID / ParentProcessID / SessionID
    ↓
prove the event PID has exact MSI Quick Settings package identity
    ↓
terminate only processes with that exact package full name

stock authority restored
    ↓
stop/dispose watcher
    ↓
never terminate future stock MSI Quick Settings

ordinary Runtime shutdown/restart
    ↓
stop/dispose watcher
    ↓
new Runtime starts
    ↓
watcher starts first
    ↓
startup reconcile catches anything that appeared during the restart gap
~~~

---

# 5. Add one narrow event-driven watcher

Preferred file:

~~~text
src/SteamInputAddonforClaw/GameBar/MsiQuickSettingsProcessStartWatcher.cs
~~~

Suggested responsibility only:

~~~text
subscribe to one WMI query
→ receive Gamebar_Widget process starts
→ expose ProcessID / ParentProcessID / SessionID
→ deterministic Stop/Dispose
~~~

Production query:

~~~sql
SELECT *
FROM Win32_ProcessStartTrace
WHERE ProcessName = 'Gamebar_Widget.exe'
~~~

Namespace:

~~~text
\\.\root\CIMV2
~~~

Do not subscribe to every process and filter later in managed code.

Do not use:

- polling;
- Process.GetProcesses on a timer;
- WMI WITHIN polling queries;
- ETW infrastructure;
- a Windows service;
- a supervisor process;
- a generic process-monitor manager;
- a generalized package-lifecycle framework.

This is one narrow Runtime-owned observer for one known executable activation.

---

# 6. Event payload

Use a small immutable event payload, for example:

~~~csharp
internal readonly record struct MsiQuickSettingsProcessStart(
    uint ProcessId,
    uint ParentProcessId,
    uint SessionId);
~~~

ProcessName is already constrained by the WQL query and does not need to become authority.

The event itself is only a wake-up.

Never trust the event process name as proof that the process is MSI software.

The exact package identity remains the authority for termination.

---

# 7. Exact package proof remains mandatory

PR #677 correctly established the safety boundary:

~~~text
process candidate
→ OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)
→ GetPackageFullName
→ exact MSI Quick Settings package identity
→ only then terminate
~~~

Keep:

~~~text
9426MICRO-STARINTERNATION.MSIQuickSettings_
~~~

as the allowed package-name prefix for discovering a concrete package full name.

For actual process termination, continue requiring equality with the exact concrete package full name, case-insensitive.

Example concrete package observed on hardware:

~~~text
9426MICRO-STARINTERNATION.MSIQuickSettings_2.0.71.0_x64__kzh8wxbdkxb8p
~~~

Do not weaken this to:

- Gamebar_Widget filename only;
- publisher only;
- MSI substring;
- RuntimeBroker name;
- package-family substring;
- parent process identity.

---

# 8. Refactor PR #677 quiescer instead of duplicating it

Current main has:

~~~text
MsiQuickSettingsRuntimeQuiescer.Quiesce()
~~~

Refactor its API so the two legitimate entry paths are explicit.

Recommended shape:

~~~csharp
internal static MsiQuickSettingsRuntimeQuiesceResult QuiesceExisting()
internal static MsiQuickSettingsRuntimeQuiesceResult QuiesceStartedProcess(uint processId)
~~~

Exact names may differ, but preserve the distinction:

### Existing-process reconcile

~~~text
QuiesceExisting()
→ used once at Disabled Runtime startup
→ enumerate Gamebar_Widget candidates
→ prove exact package full name
→ terminate all currently running processes carrying that exact package full name
~~~

### Process-start event path

~~~text
QuiesceStartedProcess(eventProcessId)
→ use the event PID directly
→ prove exact MSI Quick Settings package full name
→ terminate all currently running processes carrying that exact package full name
~~~

Both paths must share the same:

- TryGetPackageFullName implementation;
- exact package-name validation;
- exact concrete package-full-name comparison;
- TerminateProcess implementation;
- package-wide process enumeration needed to include the package Runtime Broker.

Do not copy native interop into the watcher.

The watcher observes process creation.

The quiescer owns identity proof and termination.

---

# 9. Keep the PR #677 startup reconcile

Do NOT delete the one-shot startup reconcile.

A process-start subscription only observes starts after subscription is active.

Real lifecycle:

~~~text
old Runtime shuts down
→ its watcher is gone
→ Gamebar_Widget may already exist or may start during the restart/crash gap
→ new Runtime starts
~~~

If the new Runtime relied only on future process-start events, an already-running Gamebar_Widget would never generate another start event and would remain alive.

Required ordering is therefore:

~~~text
1. exact Disabled authority gate
2. start watcher
3. QuiesceExisting()
4. continue controller startup
~~~

Starting the watcher before the existing-process reconcile closes the practical observation gap:

~~~text
already running before watcher
→ QuiesceExisting catches it

starts after watcher registration
→ process-start callback catches it
~~~

Do not introduce an epoch/barrier/state machine around the tiny overlap between watcher registration and initial reconcile.

If both paths observe the same short-lived process, the exact package revalidation and normal process disappearance behavior are sufficient. Do not build synchronization solely for that theoretical interleaving.

---

# 10. PR #677 cleanup decisions

Review the merged code while implementing this PR.

## Keep

Keep these PR #677 concepts:

- exact package identity proof via GetPackageFullName;
- PROCESS_QUERY_LIMITED_INFORMATION;
- PROCESS_TERMINATE only after package proof;
- revalidation on the final termination handle;
- full-process enumeration only when an exact target package has been proven;
- exact package-full-name comparison;
- RuntimeBroker name must never be used as a kill target;
- best-effort failure policy;
- startup quiesce before DisabledBootAdmission early return.

These remain necessary.

## Rename / simplify

Prefer renaming:

~~~text
Quiesce()
→ QuiesceExisting()
~~~

because the Runtime now has two distinct event sources.

The normal startup no-op log:

~~~text
MsiQuickSettingsQuiesceNotRunning
~~~

may be downgraded from INFO to DEBUG.

Once the event watcher owns the Runtime lifetime, "not running at startup" is a normal state and does not need production INFO noise.

If small helper methods or collections from PR #677 become redundant after extracting the shared direct-PID path, delete them.

Do not retain two separate implementations of package identity or termination just to preserve the original PR layout.

## Do not remove

Do not remove the full exact-package process scan after package proof.

The 1005/04 hardware log proves one activation can involve at least:

~~~text
Gamebar_Widget PID 17052
another process PID 6876
same exact MSI Quick Settings package full name
~~~

The package-associated Runtime Broker must also be retired without targeting RuntimeBroker by name.

---

# 11. Runtime integration point

Current main already has:

~~~csharp
private async Task TryStartDisabledModeControllerAsync(...)
{
    if (startupResult.CenterMStartupState != FrontendCenterMStartupState.Disabled)
        return;

    ...
}
~~~

Integrate immediately after the exact Disabled check.

Required conceptual order:

~~~csharp
if (startupResult.CenterMStartupState != FrontendCenterMStartupState.Disabled)
    return;

StartMsiQuickSettingsProcessStartWatcher();

MsiQuickSettingsRuntimeQuiescer.QuiesceExisting();

var owner = CreatePhysicalOwnership(startupComposition);
...
~~~

This remains before:

- CreatePhysicalOwnership;
- DisabledBootAdmission early return;
- VIIPER initialization;
- PID1902 acquisition;
- presentation attach.

Reason:

MSI Quick Settings cleanup is a consequence of exact Addon authority, not controller admission success.

A temporarily blocked controller startup still must not leave the orphaned MSI widget running against a disabled backend.

---

# 12. Process-start callback behavior

The callback should do minimal deterministic work.

Conceptually:

~~~csharp
private void OnMsiQuickSettingsProcessStarted(MsiQuickSettingsProcessStart started)
{
    if (Volatile.Read(ref _processShutdownStarted) != 0)
        return;

    var result = MsiQuickSettingsRuntimeQuiescer.QuiesceStartedProcess(started.ProcessId);

    AppLog.Debug(
        "MsiQuickSettings",
        "MSI Quick Settings process-start re-quiesce completed.",
        ("ProcessId", started.ProcessId),
        ("ParentProcessId", started.ParentProcessId),
        ("SessionId", started.SessionId),
        ("TerminatedProcessCount", result.TerminatedProcessCount),
        ("FailureCount", result.FailureCount));
}
~~~

Do not schedule a timer or retry loop.

Do not trigger controller reconcile.

Do not touch:

- PID1902;
- HidHide;
- VIIPER;
- Steam presentation;
- Overlay;
- WING mapping.

This callback is package cleanup only.

---

# 13. Parent PID diagnostics

Log ParentProcessID from Win32_ProcessStartTrace.

This is useful because the current 1005/04 logs prove reactivation but do not identify the launcher.

Required diagnostic fields on an observed Gamebar_Widget start:

~~~text
ProcessId
ParentProcessId
SessionId
~~~

Optionally capture ParentProcessName immediately as best-effort diagnostic data.

If parent lookup fails because the parent has already exited, do not retry and do not treat that as a cleanup failure.

Never use parent identity as termination authority.

The package identity of the started process remains the only authority.

---

# 14. Watcher failure policy

If the WMI subscription cannot start:

~~~text
log warning
→ still run QuiesceExisting()
→ continue Full1902 startup
~~~

Do not fail controller startup.

Do not fall back to polling.

Suggested event:

~~~text
MsiQuickSettingsProcessWatcherUnavailable
~~~

Include exception type / HRESULT or Management status where available.

A later Runtime restart naturally retries watcher creation.

---

# 15. Watcher callback / Dispose boundary is real lifecycle safety

This watcher is different from a diagnostic observer because its callback can terminate processes.

Therefore teardown must guarantee:

~~~text
Dispose returns
→ no admitted callback can later terminate MSI Quick Settings
~~~

This matters at the real supported boundary:

~~~text
Enable Center M and Restart
→ stock authority is proven
→ MSI Quick Settings becomes legitimate again
~~~

Use the same narrow lifecycle pattern already proven in:

~~~text
WindowsDeviceArrivalWatcher
WmiMsiEventSource
~~~

At minimum:

~~~text
Dispose:
1. close callback admission
2. unsubscribe managed handler
3. wait for any already-admitted callback to finish
4. Stop/Dispose ManagementEventWatcher best-effort
5. return
~~~

This is not speculative race hardening.

Without the drain boundary, a callback admitted immediately before stock restoration could execute after stock authority is restored and kill legitimate MSI Quick Settings.

Do not create a generalized callback-drain framework.

Keep the synchronization private to this one watcher.

---

# 16. Stock authority restoration

Current production seam:

~~~text
onStockAuthorityRestored: () =>
{
    _winGSuppressionGuard.Disarm();
    ...
}
~~~

Extend this existing verified stock-authority boundary.

Required order:

~~~text
verified stock authority restored
→ dispose/stop MSI Quick Settings process-start watcher
→ disarm Full1902 Win+G suppression
→ continue existing authority transition
~~~

Conceptually:

~~~csharp
onStockAuthorityRestored: () =>
{
    StopMsiQuickSettingsProcessStartWatcher();

    _winGSuppressionGuard.Disarm();
    ...
}
~~~

The watcher stop helper must be idempotent.

Do not start MSI Quick Settings manually.

Do not restore package registration.

The package was never removed or disabled.

After this boundary, Windows/MSI may activate it normally.

---

# 17. Process shutdown / controlled Runtime restart

Current BeginProcessShutdown already closes process-owned event sources early.

Add the new watcher near that boundary.

Required:

~~~text
BeginProcessShutdown
→ _processShutdownStarted = 1
→ dispose MSI Quick Settings process-start watcher
→ continue existing shutdown
~~~

Do this before long controller/presentation teardown.

Why:

- no new package termination should begin after Runtime shutdown starts;
- a controlled Runtime restart may have a short watcher-free gap;
- the next Runtime's QuiesceExisting handles anything that exists when it comes back.

Do not persist watcher state across process restart.

---

# 18. Sleep / Hibernate / Resume

Do not add special resume polling.

ManagementEventWatcher is intended to remain an event subscription for the Runtime lifetime.

Required policy:

~~~text
sleep/hibernate
→ watcher remains Runtime-owned

resume
→ if Windows/MSI starts Gamebar_Widget
→ normal ProcessStartTrace event path handles it
~~~

If supported-hardware testing later proves the WMI subscription itself becomes unusable after resume, capture that evidence and fix that specific lifecycle failure separately.

Do not preemptively add resume re-registration or periodic health probes.

---

# 19. Runtime crash

No service or supervisor is added by this PR.

If Runtime crashes:

~~~text
watcher disappears with the process
→ mandatory Runtime recovery/restart path eventually starts a new Runtime
→ new watcher starts
→ QuiesceExisting reconciles any MSI Quick Settings process that appeared while Runtime was absent
~~~

This preserves the existing Full1902 Runtime ownership architecture.

---

# 20. Performance contract

Idle behavior must be event-driven.

Forbidden:

~~~text
while running:
    Process.GetProcesses()
    Sleep(...)
~~~

Required:

~~~text
ManagementEventWatcher waiting
→ no matching Gamebar_Widget start
→ no quiescer work
~~~

The full process enumeration used to terminate all members of the exact package is acceptable only:

- once during Disabled startup reconcile when Gamebar_Widget already exists; or
- after a matching Gamebar_Widget process-start event whose PID proves exact MSI package identity.

Do not enumerate all processes on unrelated process starts.

---

# 21. Logging

Suggested watcher lifecycle events:

~~~text
MsiQuickSettingsProcessWatcherStarted
MsiQuickSettingsProcessWatcherUnavailable
MsiQuickSettingsProcessStartObserved
MsiQuickSettingsProcessWatcherStopped
~~~

For process start:

~~~text
ProcessId
ParentProcessId
SessionId
ParentProcessName (optional/best-effort only)
~~~

For quiesce result reuse existing fields:

~~~text
PackageFullName
TerminatedProcessCount
FailureCount
IdentityUnavailableCount
~~~

Do not log every unrelated process because the WQL query should prevent unrelated callbacks.

---

# 22. Required tests

Do not create a generic process-monitor framework for tests.

Use the smallest test seam consistent with existing repository style.

A narrow adapter seam around ManagementEventWatcher is acceptable if needed for deterministic Start/Dispose/callback tests.

Do not generalize WmiMsiEventSource or WindowsDeviceArrivalWatcher into a shared WMI abstraction solely for this PR.

Required coverage:

## 22.1 WQL scope

Prove production watcher subscribes only to:

~~~text
Win32_ProcessStartTrace
ProcessName = Gamebar_Widget.exe
root\CIMV2
~~~

## 22.2 Event payload

A valid process-start event forwards:

~~~text
ProcessID
ParentProcessID
SessionID
~~~

Malformed/missing fields must not default into a usable PID that could terminate another process.

## 22.3 Exact package proof on event PID

Prove:

~~~text
Gamebar_Widget start event
+ event PID has exact MSI Quick Settings package
→ eligible for quiesce
~~~

and:

~~~text
same process name
+ different/no package identity
→ ignored
→ no termination
~~~

## 22.4 Startup ordering

Prove:

~~~text
exact Disabled authority gate
→ watcher Start
→ QuiesceExisting
→ CreatePhysicalOwnership
→ DisabledBootAdmission check
~~~

Enabled boot must not start the watcher and must not quiesce MSI Quick Settings.

## 22.5 Stock restoration

Prove the existing onStockAuthorityRestored callback stops/disposes the MSI Quick Settings watcher.

The callback must stop package termination authority before returning to ordinary stock behavior.

## 22.6 Shutdown

Prove BeginProcessShutdown disposes the watcher early and idempotently.

A callback arriving after shutdown admission closes must not call the quiescer.

## 22.7 Callback drain

Prove Dispose does not return while an already-admitted process-start callback is still executing.

This is required because callback side effects include process termination.

## 22.8 No polling

Source/behavior guard:

- no Timer;
- no periodic Task.Delay loop;
- no repeated Process.GetProcesses loop outside an actual startup/event quiesce;
- no Win32_Process __InstanceCreationEvent WITHIN query;
- no generic RuntimeBroker watcher.

## 22.9 Preserve PR #677 safety guards

Continue proving:

- no RuntimeBroker process-name targeting;
- no Process.Kill by filename;
- package identity is revalidated on termination handle;
- no package uninstall/unregister;
- no IPackageDebugSettings;
- no EnableDebugging;
- no StartServicing.

---

# 23. Manual hardware validation

Use Center M Disabled on supported MSI Claw hardware.

## Test A - idle cost

1. Start Addon.
2. Confirm MsiQuickSettingsProcessWatcherStarted.
3. Leave system idle for several minutes.
4. Confirm no periodic MsiQuickSettings process enumeration/log loop.
5. Confirm Addon CPU behavior remains unchanged within normal measurement noise.

## Test B - existing process at startup

Arrange for MSI Quick Settings to already be running before Addon Runtime starts.

Expected:

~~~text
watcher starts
→ QuiesceExisting finds Gamebar_Widget
→ exact package identity proven
→ package processes terminated
~~~

## Test C - live reactivation

Start with no Gamebar_Widget.

Reproduce any normal path that causes MSI Quick Settings to reappear.

Expected log:

~~~text
MsiQuickSettingsProcessStartObserved
ProcessId=...
ParentProcessId=...
SessionId=...

exact package identity proven
→ exact package processes terminated
~~~

Verify Task Manager no longer leaves MSI Quick Settings Runtime Broker alive.

## Test D - repeated reactivation

If the platform starts Gamebar_Widget again later:

~~~text
new process-start event
→ same exact proof
→ terminate again
~~~

No polling is involved.

## Test E - unrelated process

Start another packaged app and arbitrary processes.

Expected:

~~~text
no callback reaches Addon for unrelated process names
no package termination
~~~

## Test F - fake same filename

If practical, run a non-packaged executable named Gamebar_Widget.exe.

Expected:

~~~text
process-start event may arrive
→ GetPackageFullName fails / no package identity
→ process is NOT terminated
~~~

## Test G - controlled Addon restart

While Center M remains Disabled:

1. restart Addon Runtime;
2. old watcher stops;
3. new watcher starts;
4. QuiesceExisting reconciles anything already alive;
5. no persistent watcher state is required.

## Test H - Enable Center M and Restart

1. request Enable Center M and Restart;
2. allow stock restoration to complete;
3. confirm watcher is stopped at onStockAuthorityRestored;
4. confirm Addon no longer terminates MSI Quick Settings;
5. after reboot, confirm stock MSI behavior can activate Quick Settings normally.

## Test I - sleep/resume

1. Center M Disabled / Addon authority active;
2. sleep or hibernate;
3. resume;
4. if MSI/Windows activates Gamebar_Widget, confirm the existing event subscription receives it and quiesces it;
5. if no activation occurs, no extra work should run.

Do not add special resume code unless this test proves the watcher itself fails across resume.

---

# 24. Existing PR #677 code that should not survive if redundant

During implementation, actively remove accidental duplication created by the new event path.

Delete/refactor code if all of the following are true:

~~~text
it only exists because PR #677 had one entry path
AND
the new shared direct-PID/existing-process helpers replace it
AND
removing it does not weaken package identity or termination safety
~~~

Examples that may be simplified:

- duplicated package-full-name filtering;
- duplicated exact-match helpers;
- duplicate termination result formatting;
- representative PID bookkeeping used only for an old log shape.

Do not delete a piece solely to reduce LOC.

Keep anything that protects:

- exact package identity;
- termination-handle revalidation;
- already-running process reconcile;
- package-associated Runtime Broker cleanup;
- clear production logging.

Goal:

~~~text
one process-start observer
one package identity implementation
one exact-package termination implementation
two explicit entry paths:
    existing-at-startup
    newly-started-process
~~~

---

# 25. Non-goals

Do not change:

- Center M three-root authority definition;
- PID1901/PID1902 transitions;
- DirectInput ownership;
- HidHide;
- VIIPER;
- X360/SteamDeck presentation;
- WING/OEM1 mappings;
- WinGSuppressionGuard behavior;
- Game Bar diagnostic observers;
- MSI package registration;
- Xbox Game Bar registration/settings;
- Addon update/restart architecture.

Do not uninstall or unregister MSI Quick Settings.

Do not permanently disable package activation.

Do not kill RuntimeBroker by name.

Do not add a user-facing setting.

---

# 26. Validation commands

Run at minimum:

~~~text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
git diff --check
~~~

Review production references for:

~~~text
MsiQuickSettingsRuntimeQuiescer
MsiQuickSettingsProcessStartWatcher
Win32_ProcessStartTrace
Gamebar_Widget.exe
GetPackageFullName
TerminateProcess
RuntimeBroker
onStockAuthorityRestored
BeginProcessShutdown
~~~

---

# 27. Acceptance criteria

- [ ] Exact Center M Disabled starts one event-driven Gamebar_Widget process-start watcher.
- [ ] Exact Center M Enabled starts no watcher and performs no Quick Settings quiesce.
- [ ] Watcher is registered before startup QuiesceExisting.
- [ ] Startup QuiesceExisting remains present for already-running processes and restart/crash gaps.
- [ ] No polling/timer/process-table heartbeat exists.
- [ ] Process-start event PID is never trusted without GetPackageFullName proof.
- [ ] Only exact MSI Quick Settings concrete package full name can be terminated.
- [ ] All currently running processes carrying that exact package full name may be terminated, including the package Runtime Broker.
- [ ] RuntimeBroker is never targeted by executable name.
- [ ] ParentProcessID and SessionID are logged for future activation-root diagnosis.
- [ ] Watcher failure is best-effort and does not block controller startup.
- [ ] BeginProcessShutdown stops/disposes watcher before long teardown.
- [ ] onStockAuthorityRestored stops/disposes watcher before returning to stock behavior.
- [ ] Dispose drains admitted callbacks so no package termination can escape after stock authority restoration.
- [ ] Sleep/resume adds no speculative watcher restart/polling logic.
- [ ] Existing Full1902 controller authority, HidHide, VIIPER, and presentation contracts are unchanged.
- [ ] Redundant PR #677 helper duplication is removed where the new shared entry paths make it unnecessary.
- [ ] Normal idle CPU cost remains event-driven with no recurring process scan.

---

# 28. Final implementation principle

The final design should be:

~~~text
Center M Disabled
→ subscribe once to Gamebar_Widget process-start events
→ reconcile already-existing MSI Quick Settings once
→ otherwise sleep

Gamebar_Widget starts
→ Windows/WMI wakes the Addon
→ event PID proves exact MSI Quick Settings package
→ terminate that exact package runtime
→ return to idle

Center M stock authority restored
→ stop watcher
→ never interfere with stock MSI Quick Settings
~~~

Do not solve this with a watchdog.

The new evidence justifies one exact event subscription, and nothing broader.
