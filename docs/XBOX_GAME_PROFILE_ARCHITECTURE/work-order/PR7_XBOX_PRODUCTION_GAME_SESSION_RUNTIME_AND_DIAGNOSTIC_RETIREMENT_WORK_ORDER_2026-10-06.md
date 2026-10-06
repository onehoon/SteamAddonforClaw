# Work Order — XBOX PR7: Promote Active Game Session PoC to Production Runtime and Retire Diagnostic Surface

> **Date:** 2026-10-06  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed main:** `main@5e21ca301f1045279dbeed5c19e77d470fabb46c`  
> **Architecture authority:** `docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md`  
> **Full1902 authority:** `docs/Full 1902 Implementation/README.md` and its active precedence chain  
> **Previous XBOX phase:** PR #692 / squash `72e69449ba5f259217109b89706517490428432d` added persisted offline XBOX profiles  
> **Validated precursor:** PoC B / PR #682 + PR #684 + PR #686 field validation is PASS on the real MSI Claw  
> **Current frontend protocol:** `FrontendTransportProtocol.CurrentVersion = 57`  
> **Scope:** replace the developer-started XBOX session diagnostic owner with one process-lifetime production `XboxGameSessionRuntime`, publish a minimal `ActiveXboxGame` fact, preserve the field-proven event/process/config/executable identity path, and remove the obsolete diagnostic frontend/UI/report surface  
> **Out of scope:** ActiveProfileTarget, Steam/XBOX arbitration, CPU/TDP/Power/FPS/Resolution XBOX live apply, per-game M1/M2, Overlay XBOX profile projection

---

## 1. Goal

Promote the already field-proven PoC B active XBOX game detector into the production Runtime.

Current:

~~~text
Developer Menu
→ user opens XBOX Active Game Diagnostic
→ Start
→ XboxGameSessionDiagnostic
→ WinEvent hooks
→ exact process/package/config/exe identity
→ diagnostic ActiveGame
→ Stop when page/session closes
~~~

Target:

~~~text
normal Addon Runtime startup
→ XboxGameSessionRuntime starts automatically
→ WinEvent hooks remain active for Runtime lifetime
→ exact process/package/config/exe identity
→ ActiveXboxGame? Current
→ process exit clears Current
→ resume re-arms/reconciles
→ Runtime shutdown disposes owner
~~~

There is no user Start/Stop control after PR7.

This PR establishes **production active XBOX identity only**.

It does not apply the XBOX profile yet.

---

## 2. Why this is a promotion, not a new detector

PoC B has already passed real-device validation.

Validated on the target MSI Claw:

- Aniimo Legend exact identity positive;
- Minecraft for Windows exact identity positive;
- launch helper rejected by exact executable matching;
- unrelated Xbox/Gaming UI processes rejected;
- Alt+Tab retained the active game by process lifetime;
- process exit cleared the active game;
- Sleep/Resume bounded reconciliation recovered an already-running game;
- controlled Runtime restart recovered an already-running game;
- no periodic polling.

Do not replace this with a new classifier.

Do not add:

- PresentMon;
- Game Bar;
- title/window heuristics;
- full-screen geometry;
- GPU-use scoring;
- executable fuzzy matching;
- WMI polling;
- process-list timer;
- generic game-detection framework.

The production task is to **remove diagnostic-only baggage and make the proven owner process-lifetime**.

---

## 3. Locked PR7 boundary

PR7 owns:

~~~text
Windows WinEvent + process lifetime
→ exact XBOX identity
→ ActiveXboxGame
~~~

PR7 stops there.

Do **not** implement:

~~~text
ActiveProfileTarget
Steam/XBOX priority
ProfileDocument lookup from the session runtime
CPU Boost apply
TDP apply
Windows Power Mode apply
Intel FPS apply
Resolution apply
Device baseline restore
per-game M1/M2
Overlay XBOX profile
~~~

Those are PR8+.

Required invariant:

> A positive or negative XBOX detection in PR7 changes no controller presentation and writes no machine-wide profile setting.

---

## 4. Full1902 invariants — zero controller-authority changes

PR7 must not change:

~~~text
Center M authority
PID1901 / PID1902 ownership
DirectInput
HidHide
VIIPER
Xbox360 / SteamDeck presentation selection
Steam/BPM presentation policy
WING / Center M mapping
M1/M2 publisher
rumble
LED
vibration
PnP recovery
routing rollback/fail-close
~~~

Specifically:

~~~text
ActiveXboxGame != null
≠ force Xbox360
≠ detach SteamDeck
≠ change PID1902 ownership
≠ change HidHide
≠ touch VIIPER
~~~

The XBOX session runtime is independent of Center M Enabled/Disabled state.

It should exist in a normal supported Runtime in either controller-authority state.

Do not start it during headless uninstall preparation.

---

## 5. Current code facts that must drive implementation

The reviewed main currently contains:

~~~text
src/SteamInputAddonforClaw/Diagnostics/XboxSession/
    XboxGameSessionDiagnostic.cs
    XboxGameProcessIdentityProbe.cs
    XboxGameProcessIdentityEvaluator.cs
    XboxGameWindowEventSource.cs
~~~

The current diagnostic already provides:

- dedicated WinEvent message-pump thread;
- EVENT_SYSTEM_FOREGROUND;
- EVENT_OBJECT_CREATE;
- EVENT_OBJECT_SHOW;
- top-level window filtering;
- thin unmanaged callback;
- one Channel single-reader serialized worker;
- process-generation identity using PID + GetProcessTimes creation time;
- `PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE`;
- process-handle exit notification via `ThreadPool.RegisterWaitForSingleObject`;
- same-generation positive/negative caching;
- `PackageManager.FindPackageForUser("", packageFullName)`;
- Effective/Installed package config locations;
- production `MicrosoftGameConfigReader`;
- exact running executable ↔ ExecutableList match;
- canonical `XboxGameIdentity.CreateKey(...)`;
- bounded startup/window reconciliation;
- resume hook re-arm/reconciliation;
- Alt+Tab retention;
- matched process-exit clear.

Preserve these proven mechanics unless this work order explicitly removes diagnostic-only behavior.

---

## 6. Production source location

Move/promote the production session code out of `Diagnostics`.

Recommended:

~~~text
src/SteamInputAddonforClaw/Xbox/Session/
    XboxGameSessionRuntime.cs
    XboxGameWindowEventSource.cs
    XboxGameProcessIdentityProbe.cs
    XboxGameProcessIdentityEvaluator.cs
~~~

Equivalent placement directly under `Xbox/` is acceptable if it is clearer.

Do not retain duplicate copies under both:

~~~text
Diagnostics/XboxSession
Xbox/Session
~~~

There must be one production implementation.

The narrow existing interfaces are useful real OS test seams and may remain:

~~~text
IXboxGameWindowEventSource
IXboxGameProcessIdentityProbe
IXboxGameProcessGeneration
~~~

Do not replace them with a generic provider/plugin hierarchy.

---

## 7. Add the minimal production ActiveXboxGame fact

Add one small production model.

Recommended:

~~~csharp
internal sealed record ActiveXboxGame(
    string Key,
    string DisplayName);
~~~

The production fact exposed to the rest of the Runtime should contain only what future profile selection needs.

Do not expose as production active-state fields:

- PID;
- HWND;
- PackageFullName;
- PFN;
- AUMID;
- config path;
- StoreId / TitleId as separate authority;
- process path;
- package version.

Those may remain local logging/evidence while classifying a process, but are not the application-level active identity.

The canonical key remains:

~~~text
StoreId
→ else PFN
→ else config Identity tuple
~~~

through the existing `XboxGameIdentity.CreateKey`.

### DisplayName

Derive DisplayName from the parsed `MicrosoftGame.config` using the same production identity semantics as the catalog where practical.

Recommended fallback order:

~~~text
config DefaultDisplayName
→ package display/name metadata if already available without a new scan
→ running executable basename
~~~

Do not perform an installed-catalog scan merely to obtain a display name.

The Key, not DisplayName, is authoritative.

---

## 8. XboxGameSessionRuntime public shape

Recommended narrow API:

~~~csharp
internal sealed class XboxGameSessionRuntime : IAsyncDisposable
{
    internal ActiveXboxGame? ActiveGame { get; }

    internal event Action<ActiveXboxGame?>? ActiveGameChanged;

    internal Task StartAsync(CancellationToken cancellationToken = default);

    internal Task ReconcileAfterResumeAsync(
        CancellationToken cancellationToken = default);
}
~~~

Equivalent callback-based publication is acceptable.

Requirements:

- `ActiveGame` is the current production fact;
- `ActiveGameChanged` fires only when the semantic active identity changes;
- duplicate WinEvents for the same active key/generation do not produce duplicate active-change notifications;
- process exit publishes null exactly once for the active generation;
- startup/reconcile discovering the already-published same key does not spam change notifications.

Do not expose Start/Stop to the Main UI.

Do not add a frontend capture RPC in PR7.

PR8 will consume this Runtime fact directly inside the Runtime.

---

## 9. Runtime lifetime — always on

Current diagnostic is explicitly user-started.

Production behavior:

~~~text
AddonProcessHost normal startup
→ construct XboxGameSessionRuntime
→ StartAsync
→ hooks installed
→ bounded startup reconcile
→ continue for Runtime lifetime
~~~

It must not depend on:

- Main UI launch;
- XBOX page activation;
- Developer Menu;
- Overlay;
- Center M Disabled;
- Steam running.

### Start failure

XBOX detection is not required for Runtime/controller ownership startup.

If hooks cannot start:

~~~text
log warning
ActiveGame = null
Runtime continues
Steam unaffected
Full1902 unaffected
~~~

Do not fail Addon Runtime startup because the XBOX observer failed.

Do not add an automatic retry loop.

A later real lifecycle boundary such as Resume may make one bounded re-arm attempt.

---

## 10. Preserve the WinEvent design

Keep the existing event set exactly:

~~~text
EVENT_SYSTEM_FOREGROUND
EVENT_OBJECT_CREATE
EVENT_OBJECT_SHOW
~~~

Keep:

~~~text
WINEVENT_OUTOFCONTEXT
dedicated message-loop thread
~~~

Microsoft documents that the thread calling `SetWinEventHook` must have a message loop, and out-of-context events are queued/delivered asynchronously.

Official reference:

- https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwineventhook

Do not add extra events merely for coverage:

- NAMECHANGE;
- HIDE;
- DESTROY;
- LOCATIONCHANGE.

Window destruction is not session lifetime authority.

### Relevant object event filter

Keep:

~~~text
EVENT_SYSTEM_FOREGROUND
→ top-level HWND
→ PID != 0

EVENT_OBJECT_CREATE / EVENT_OBJECT_SHOW
→ idObject == OBJID_WINDOW
→ idChild == CHILDID_SELF
→ top-level HWND
→ PID != 0
~~~

`GetWindowThreadProcessId` remains the direct HWND → PID mapping.

Official reference:

- https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid

---

## 11. WinEvent callback stays thin

The unmanaged callback must continue to do only:

~~~text
classify event kind
validate HWND/object/child/top-level
obtain PID
queue immutable observation
return
~~~

Do not call from the callback:

~~~text
OpenProcess
QueryFullProcessImageName
PackageManager
GetPackageFullName
GetPackageFamilyName
File.Open
MicrosoftGame.config parser
profile store
frontend
hardware runtime
~~~

No managed exception may cross the unmanaged callback boundary.

Keep the existing catch-at-boundary behavior.

---

## 12. Keep one serialized execution context

The existing Channel design is a good production fit:

~~~text
multiple WinEvent / process-exit producers
→ Channel
→ single reader
→ process cache + active fact mutation
~~~

Retain a single serialized owner.

Rename diagnostic message types as appropriate, e.g.:

~~~text
SessionMessage
WindowObservationMessage
ProcessExitedMessage
ReconcileMessage
~~~

Remove messages that only exist for diagnostic UI/report state.

Do not add:

- application-wide event bus;
- actor framework;
- generalized state machine;
- epochs;
- barriers;
- multiple session managers.

---

## 13. Simplify process identity evidence for production

The diagnostic process probe currently gathers more evidence than production needs.

Current diagnostic-only native evidence includes:

~~~text
GetApplicationUserModelId
GetPackageId
GetPackagePathByFullName2 (all PackagePathType values)
package architecture/version/resource fields
diagnostic package-path list
~~~

These exist to populate the diagnostic report.

They are not used to decide the production canonical key/match.

PR7 should remove this diagnostic-only collection.

### Production required process evidence

Keep only:

~~~text
OpenProcess(
    PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE)

GetProcessTimes
→ process generation creation time

QueryFullProcessImageNameW
→ running path / executable basename

GetPackageFullName
→ live package full name

GetPackageFamilyName
→ PFN fallback identity

PackageManager.FindPackageForUser("", packageFullName)
→ EffectiveLocation / InstalledLocation

MicrosoftGame.config
→ Identity
→ StoreId optional
→ TitleId optional metadata
→ ExecutableList

exact current executable basename match
~~~

No installed catalog scan is needed.

No PowerShell.

No WindowsApps ACL work.

No private Xbox DB.

### Remove diagnostic-only P/Invoke when unused

If no other production consumer exists after diagnostic retirement, delete from this session path:

~~~text
GetApplicationUserModelId
GetPackageId
GetPackagePathByFullName2
PackagePathTypes
FrontendXboxSessionDiagnosticPackagePath
PackageIdNative projection
~~~

Do not leave dead evidence collection “for future diagnostics.”

---

## 14. Production process inspection result

Replace the diagnostic frontend DTO dependency.

Current:

~~~csharp
XboxGameProcessInspection(
    Disposition,
    FailureReason,
    FrontendXboxSessionDiagnosticGame? Game)
~~~

Target concept:

~~~csharp
internal sealed record XboxGameProcessMatch(
    XboxGameIdentity Identity,
    uint ProcessId,
    string RunningProcessPath,
    string RunningExecutableName,
    string PackageFullName);

internal sealed record XboxGameProcessInspection(
    XboxGameProcessInspectionDisposition Disposition,
    string? FailureReason,
    XboxGameProcessMatch? Match);
~~~

The exact match record may be smaller.

Requirements:

- no dependency from production XBOX code to `SteamInputAddonforClaw.Contracts.Frontend`;
- canonical identity is the existing production `XboxGameIdentity`;
- the active publication is projected from match → `ActiveXboxGame`.

Do not create a second identity type that reimplements key derivation.

---

## 15. MicrosoftGame.config matching remains exact

Production match remains:

~~~text
running executable basename
== case-insensitive exact basename of one ExecutableList entry
~~~

No fuzzy matching.

No prefix matching.

No helper inheritance.

No “same package therefore game” shortcut.

A packaged launcher/helper such as `gamelaunchhelper.exe` must remain negative when it is not in the config ExecutableList.

Retain the existing 2 MiB config bound and production XML safety policy in `MicrosoftGameConfigReader`.

---

## 16. Process generation remains PID + creation time

Do not cache by bare PID.

Keep:

~~~text
OpenProcess
+ GetProcessTimes creation time
→ XboxGameProcessGenerationKey(ProcessId, CreationTime)
~~~

This handles normal Windows PID reuse without a new global epoch.

The process handle also owns exit observation.

`PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE` is intentional:

- limited query for path/package/process metadata;
- SYNCHRONIZE for process lifetime wait.

Do not widen to PROCESS_ALL_ACCESS.

The current product Runtime being elevated does not justify broader process rights.

---

## 17. Process lifetime remains handle-based, never polling

Keep the existing:

~~~text
ThreadPool.RegisterWaitForSingleObject
→ process handle signaled
→ enqueue ProcessExitedMessage
~~~

No timer.

No `Process.HasExited` polling loop.

No WMI process watcher.

Microsoft documents `RegisterWaitForSingleObject` as a thread-pool registration over a WaitHandle and recommends unregistering the returned `RegisteredWaitHandle` when finished.

Reference:

- https://learn.microsoft.com/dotnet/api/system.threading.threadpool.registerwaitforsingleobject

Retain explicit unregister/disposal in process-generation teardown.

---

## 18. Same-generation cache policy

Keep the useful current behavior:

~~~text
same PID + same creation time already inspected
→ suppress duplicate probe
~~~

A cached positive generation may re-activate only if there is currently no active game and its process handle is still live.

A cached negative generation remains negative for that exact process generation.

Do not create:

- global executable blacklist;
- package blacklist persisted across launches;
- time-based negative-cache expiry;
- retry loop for the same generation.

New process generation = new evidence opportunity.

---

## 19. Active-game ownership policy

Supported product scope remains one user / one interactive session / normal one-game usage.

Keep the current practical policy:

~~~text
no active XBOX game
+ exact positive process
→ activate it

active process remains live
+ unrelated foreground change
→ retain active XBOX game

active process exits
→ clear ActiveXboxGame
~~~

Do not clear on Alt+Tab.

Do not clear on window hide/destroy.

Do not implement a multi-game arbitration framework.

### Second positive process

If a second different positive XBOX process appears while the current active process is still live:

~~~text
keep current active process
log bounded informational/debug evidence
do not arbitrate
~~~

This is consistent with the supported single-game product scope.

Do not add priority scoring or a process graph.

If future real field evidence shows one supported title requires multiple executable generations for one logical session, handle that concrete title/lifecycle then.

---

## 20. Startup bounded reconcile

WinEvent sees future events only.

After hooks are successfully armed:

~~~text
GetForegroundWindow
→ foreground PID candidate

if no active game:
→ EnumWindows once
→ unique top-level PIDs
→ inspect at most existing bounded maximum
→ stop once an exact positive match is found
~~~

Retain the current bounded maximum:

~~~text
MaximumReconcileWindows = 512
~~~

unless tests prove another value is necessary.

No timer repeats this scan.

A normal Runtime restart while a game is already running must recover it through this one bounded startup reconcile.

This behavior already passed field validation.

---

## 21. Resume lifecycle

Real Sleep/Hibernate/Resume is mandatory product scope.

Production resume behavior should preserve the field-proven PoC while discarding stale non-authoritative observations.

Recommended:

~~~text
PowerResumeObserved
→ ReconcileAfterResumeAsync

inside XboxGameSessionRuntime:
    stop admitting WinEvent callback work
    cancel in-flight candidate inspection
    stop/unhook WinEvent source

    retire non-active cached process generations

    if active generation handle is already signaled:
        clear active

    if active generation is still live:
        it may remain the proven active process-generation owner

    re-create/re-arm WinEvent source
    resume admission
    one bounded foreground/top-level reconcile
~~~

The important rule is:

> Do not trust stale HWND observations or negative process caches across resume.

A retained **live process handle + creation-time generation** is stronger evidence than a stale HWND and may remain authoritative while it is still unsignaled.

This avoids unnecessary identity churn while respecting the canonical resume policy.

### Resume failure

If hook re-arm fails:

~~~text
log warning
do not crash Runtime
do not affect Full1902 resume
do not affect Steam
~~~

If a previously proven active process generation is still live, retain it until its handle exits.

If no proven active process remains, ActiveGame is null.

No periodic retry.

A later lifecycle resume/restart may make another bounded attempt.

---

## 22. Event-source failure during normal runtime

The existing diagnostic correctly treats the WinEvent source separately from a currently retained matched process.

Production policy:

~~~text
WinEvent message loop/hook fails
→ stop admitting new window events
→ log warning
→ retire non-active cached generations
→ keep a currently proven live active generation until its process exits
→ no new game detection while source is failed
~~~

Do not clear a still-live, already-proven game merely because future event discovery is unavailable.

Do not restart hooks in a loop.

A real lifecycle boundary such as resume may attempt one re-arm.

---

## 23. ActiveGameChanged semantics

Publish only semantic changes.

Examples:

~~~text
null → store:ABC
    fire

store:ABC → same store:ABC due duplicate FOREGROUND/SHOW
    do not fire

store:ABC → null because active process exits
    fire

null → null after negative candidate
    do not fire
~~~

For PR7 there is no consumer that applies profiles.

Still implement this cleanly so PR8 can consume one production event rather than re-reading diagnostic state.

The event callback must not run inside the unmanaged WinEvent callback.

It should be emitted from the serialized session worker after the owner state is committed.

---

## 24. Production logging

Rename log category from diagnostic terminology.

Recommended:

~~~text
XboxSession
~~~

or:

~~~text
Xbox.GameSession
~~~

Do not continue emitting:

~~~text
XboxSessionDiagnostic
~~~

for the production owner.

### Info

Use Info for lifecycle-significant events:

~~~text
session runtime started
startup reconcile completed
positive XBOX active identity accepted
active game process exited / cleared
resume reconcile completed
event source failed/recovered at lifecycle boundary
runtime stopped
~~~

### Debug

Use Debug for candidate negatives/duplicates:

~~~text
process open failure
no package
config negative
executable mismatch
same-generation duplicate
config location evidence
second-positive-live-game conflict
~~~

Do not create or write diagnostic report files.

Do not keep a 256-entry lifecycle evidence ring solely for logging.

Normal AppLog is the production evidence channel.

---

## 25. Remove diagnostic counters/report machinery

Delete from the production runtime:

~~~text
FrontendXboxSessionDiagnosticState
FrontendXboxSessionDiagnosticCounters
FrontendXboxSessionDiagnosticLifecycleEvent
FrontendXboxSessionDiagnosticSnapshot
FrontendXboxSessionDiagnosticReportResult
diagnostic report builder/writer
MaximumLifecycleEvents
MaximumReportNameAttempts
OmittedLifecycleEventCount
LastPositiveGame
Observer Start/Stop status strings
hook-installed UI booleans
~~~

Counters that are not used by production control flow should not survive merely because PoC had them.

If one small counter materially helps a production log, compute/log it locally rather than preserving the diagnostic snapshot model.

---

## 26. AddonProcessHost wiring

Replace:

~~~text
_xboxSessionDiagnostic
~~~

with:

~~~text
_xboxGameSessionRuntime
~~~

Recommended field:

~~~csharp
private XboxGameSessionRuntime? _xboxGameSessionRuntime;
~~~

### Construction

Normal Runtime only:

~~~text
!_headlessUninstallPreparation
→ construct production XboxGameSessionRuntime
~~~

Do not gate on:

- Main UI;
- Center M Disabled;
- Steam state;
- XBOX page.

### Start

Start automatically as part of normal Runtime initialization.

Do not make XBOX hook failure fail the whole Addon startup.

Recommended host pattern:

~~~csharp
try
{
    await _xboxGameSessionRuntime.StartAsync(cancellationToken)
        .ConfigureAwait(false);
}
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    throw;
}
catch (Exception exception)
{
    AppLog.Warn(
        "XboxSession",
        "Production XBOX game-session observer could not start; Runtime will continue without active XBOX detection.",
        exception);
}
~~~

Equivalent bounded failure handling is acceptable.

### Future PR8 seam

Do not wire `ActiveGameChanged` into performance runtimes yet.

The host may keep the event unused in PR7.

Do not add dummy profile apply callbacks.

---

## 27. PowerResumeObserved wiring

Current host does:

~~~text
OnPowerResumeObserved
→ _xboxSessionDiagnostic.ReconcileAfterResumeAsync()
~~~

Change only the owner:

~~~text
OnPowerResumeObserved
→ _xboxGameSessionRuntime.ReconcileAfterResumeAsync(...)
~~~

Keep this independent from the existing:

- Full1902 presentation resume;
- controller LED/vibration resume;
- 2.5 s performance resume reconcile.

Do not wait for the 2.5 s Steam profile settle before re-establishing XBOX identity.

Do not make XBOX resume failure block controller resume.

---

## 28. Shutdown ordering

The production XBOX session owner is Runtime-owned and must stop before process resources disappear.

During normal Runtime shutdown:

~~~text
stop/Dispose XboxGameSessionRuntime
→ unhook WinEvent hooks
→ stop message pump
→ cancel candidate work
→ unregister process waits
→ dispose retained process handles
→ complete worker
~~~

Do this before `_runtimeHost` is disposed.

It does not need to be ordered against VIIPER/HidHide as an authority owner because it does not own controller state.

Keep teardown bounded and idempotent.

Do not keep a frontend-disconnect stop path: Main UI disconnect must no longer stop XBOX production detection.

---

## 29. Retire the Developer diagnostic UI

Delete:

~~~text
src/SteamInputAddonforClaw.UI/Views/XboxSessionDiagnosticPage.xaml
src/SteamInputAddonforClaw.UI/Views/XboxSessionDiagnosticPage.xaml.cs
~~~

Remove from Developer page:

~~~text
XBOX Active Game Diagnostic card
XboxSessionDiagnosticRequested event
OpenXboxSessionDiagnosticButton_Click
~~~

Remove from MainWindow:

~~~text
XboxSessionDiagnosticContent
Initialize/BackRequested wiring
visibility handling
Activate/Deactivate handling
wasXboxSessionDiagnostic
~~~

Remove from MainNavigationState:

~~~text
MainNavigationPage.XboxSessionDiagnostic
OpenXboxSessionDiagnostic()
mouse-back destination entry
~~~

Remove Main UI shutdown logic that exists only to stop that diagnostic page/session.

Production detection must continue when Main UI is closed.

---

## 30. Retire diagnostic frontend contracts

Delete from:

~~~text
SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
~~~

the complete diagnostic family:

~~~text
FrontendXboxSessionDiagnosticState
FrontendXboxSessionDiagnosticReportOutcome
FrontendXboxSessionDiagnosticCounters
FrontendXboxSessionDiagnosticPackagePath
FrontendXboxSessionDiagnosticGame
FrontendXboxSessionDiagnosticLifecycleEvent
FrontendXboxSessionDiagnosticSnapshot
FrontendXboxSessionDiagnosticReportResult
~~~

Remove from `IAddonFrontendControl`:

~~~text
CaptureXboxSessionDiagnosticAsync
StartXboxSessionDiagnosticAsync
StopXboxSessionDiagnosticAsync
GenerateXboxSessionDiagnosticReportAsync
~~~

Do not replace them with a production ActiveXboxGame frontend RPC in PR7.

Production active identity is Runtime-internal until PR8/Overlay phases require a user surface.

---

## 31. Retire diagnostic frontend implementation

Remove from `InProcessAddonFrontendControl`:

- diagnostic delegates;
- capture/start/stop/report methods;
- `NotifyXboxSessionDiagnosticStateChanged`;
- diagnostic-specific state invalidation.

Remove diagnostic delegate injection from `AddonProcessHost`.

Do not have `XboxGameSessionRuntime` know about:

~~~text
IAddonFrontendControl
StateInvalidated
named pipe
Main UI
~~~

---

## 32. Retire diagnostic RPCs

Current frontend protocol at reviewed baseline:

~~~text
FrontendTransportProtocol.CurrentVersion = 57
~~~

Remove these RPC methods:

~~~text
CaptureXboxSessionDiagnostic
StartXboxSessionDiagnostic
StopXboxSessionDiagnostic
GenerateXboxSessionDiagnosticReport
~~~

Remove their client/server dispatch and request/response test fixture plumbing.

Because the wire contract removes methods/types, bump:

~~~text
57 → 58
~~~

Add a version comment:

~~~text
Version 58:
retire the completed XBOX active-game session diagnostic frontend/RPC contract
as the field-proven detector is promoted to an always-on Runtime-owned
XboxGameSessionRuntime. No production active-XBOX frontend RPC is added yet.
~~~

Overlay protocol remains unchanged.

The enum is string-serialized by exact method name; no numeric slot reservation is required.

---

## 33. Remove frontend-disconnect diagnostic cleanup

The diagnostic currently has session teardown behavior tied to frontend connection/session lifetime.

After PR7:

~~~text
Main UI connects/disconnects
→ XboxGameSessionRuntime unchanged
~~~

Remove tests and server hooks whose only purpose is:

~~~text
frontend disconnect
→ StopXboxSessionDiagnostic
~~~

Production XBOX detection belongs to Runtime lifetime, not frontend lifetime.

---

## 34. Tests — migrate the valuable PoC evidence

Do not delete the PoC B behavior tests wholesale.

Rename/migrate:

~~~text
XboxGameSessionDiagnosticTests
→ XboxGameSessionRuntimeTests
~~~

Retain tests for actual production behavior.

Delete report/UI/counter-specific assertions.

### Required runtime tests

1. `StartAsync` arms the three WinEvent hooks.
2. Start performs one bounded reconcile.
3. foreground positive exact match activates `ActiveXboxGame`.
4. CREATE/SHOW can identify a game before it becomes foreground.
5. helper executable mismatch never activates.
6. no-package candidate never activates.
7. config-negative candidate never activates.
8. malformed config never activates.
9. same process generation is inspected once.
10. PID reuse with a different creation time is inspected as a new generation.
11. duplicate positive events do not duplicate `ActiveGameChanged`.
12. Alt+Tab away does not clear ActiveGame.
13. return to foreground does not re-create the session.
14. active process exit clears ActiveGame exactly once.
15. non-active process exit does not clear ActiveGame.
16. a second live positive process does not replace the current live active game.
17. Runtime startup reconcile recovers an already-running background game.
18. Runtime shutdown unregisters process waits and disposes handles.
19. no timer/process polling exists.

---

## 35. Tests — resume lifecycle

Required:

1. resume stops/re-arms the WinEvent source;
2. stale non-active process-generation cache is retired;
3. a retained active generation whose process handle is still live remains active;
4. a retained active generation that signaled exit is cleared;
5. bounded reconcile finds an already-running game after re-arm;
6. hook re-arm failure does not throw through the host/runtime lifecycle;
7. failed re-arm with no proven live active game leaves ActiveGame null;
8. failed re-arm with a still-live proven active generation retains it until its handle exits;
9. resume does not touch profile/hardware apply runtimes.

Do not add pathological instruction-interleaving tests.

---

## 36. Tests — identity evaluator simplification

Migrate existing evaluator tests to production types.

Required:

1. image query failure → negative;
2. no package → negative;
3. package query failure → negative;
4. PackageManager config-location failure → negative;
5. config absent → negative;
6. malformed/oversized config → negative;
7. exact executable mismatch → negative;
8. exact executable match → production `XboxGameIdentity`;
9. StoreId key priority;
10. PFN fallback;
11. config Identity tuple fallback;
12. D: running process path with C: config metadata still matches;
13. no path-prefix equality requirement.

Add source/contract assertion or equivalent proving the production session path no longer contains:

~~~text
GetApplicationUserModelId
GetPackageId
GetPackagePathByFullName2
FrontendXboxSessionDiagnostic
~~~

Do not add a new abstraction solely for this assertion.

---

## 37. Tests — event-source contract

Retain/update current Windows event-source tests:

- exact three event constants;
- out-of-context hook;
- top-level filter;
- object/child filter;
- callback queues observation only;
- one-time `EnumWindows` reconciliation seam;
- no timer;
- no package/config work in callback.

The dedicated message-pump thread is intentional because Microsoft documents the `SetWinEventHook` caller-thread message-loop requirement.

---

## 38. Tests — AddonProcessHost production lifetime

Add focused host/source-contract tests proving:

1. production `XboxGameSessionRuntime` is composed in normal Runtime;
2. headless uninstall does not start it;
3. it is not gated by Center M Disabled;
4. it is not gated by Main UI;
5. hook-start failure is feature-local and does not fail Runtime startup;
6. `PowerResumeObserved` calls production resume reconcile;
7. Runtime shutdown disposes the production session owner;
8. Main UI/frontend disconnect does not dispose/stop production XBOX detection;
9. no active-game event invokes CPU/TDP/Power/FPS/Resolution apply in PR7.

Use existing test seams where practical.

Do not introduce a host-wide dependency injection framework for this.

A narrow optional factory/delegate for `XboxGameSessionRuntime` is acceptable only if needed by the existing host tests.

---

## 39. Delete obsolete diagnostic tests

Delete or rewrite:

~~~text
tests/SteamInputAddonforClaw.UiTests/XboxSessionDiagnosticUiTests.cs
~~~

Remove diagnostic-specific portions of:

~~~text
FrontendNamedPipeTransportTests
MainNavigationStateTests
MainWindow UI source-contract tests
App shutdown tests
Developer menu tests
frontend fake/control fixtures
~~~

Do not retain dead tests asserting that the removed Developer diagnostic exists.

Replace them with production-runtime tests where the behavior still matters.

---

## 40. Failure policy

### Candidate identity cannot be proven

~~~text
ignore candidate
ActiveGame unchanged
~~~

### Initial observer start fails

~~~text
ActiveGame = null
log warning
Runtime continues
Steam continues
Full1902 continues
~~~

### Event source fails while active game is proven/live

~~~text
retain active generation
stop detecting new candidates
clear when retained process handle exits
~~~

### Active process exits

~~~text
ActiveGame = null
publish ActiveGameChanged(null)
~~~

PR7 stops there.

Do not apply Device baseline yet.

PR8 owns target/apply convergence.

### Package/config operation failure

Fail the candidate closed.

Do not:

- retry in a loop;
- guess;
- use title/window heuristics;
- scan disk.

---

## 41. Race / overengineering policy

Follow the project review policy.

Real lifecycle to protect:

- Runtime startup;
- game launch;
- Alt+Tab;
- process exit;
- Sleep/Hibernate/Resume;
- controlled Runtime restart;
- hook/message-pump failure;
- process query/config operation failure.

Do not add machinery for theoretical instruction-level races.

The existing architecture already provides:

~~~text
one serialized Channel worker
process generation key
retained process handle
registered process-exit wait
bounded lifecycle reconcile
~~~

That is sufficient.

Do not add:

- epoch counters;
- barriers;
- generalized session state machine;
- manager/coordinator hierarchy;
- retry scheduler;
- multiple active authorities.

---

## 42. No production profile lookup in PR7

Even though PR #692 now persists:

~~~text
ProfileDocument.XboxGames
~~~

`XboxGameSessionRuntime` must not read it.

No dependency on:

~~~text
ProfileStore
XboxGameProfileMutations
CpuBoostRuntime
TdpRuntime
PowerModeRuntime
IntelFrameLimiterRuntime
GameDisplayResolutionRuntime
BackButtonMappingSettings
~~~

The production session runtime answers only:

> Which exact XBOX game process is currently the supported active XBOX session?

PR8 answers:

> Which Steam/XBOX profile target is effective, and what settings should be applied?

Keep these separate.

---

## 43. No presentation changes

Do not subscribe `ActiveGameChanged` to:

~~~text
RequestControllerPresentationReconcile
~~~

XBOX detection is not presentation policy.

Existing presentation remains:

~~~text
Steam/BPM inactive → Xbox360
Steam/BPM active   → SteamDeck
~~~

Even if ActiveXboxGame is present.

This is a hard architecture invariant.

---

## 44. No frontend user indicator yet

Do not add an “Active XBOX game” badge/status to:

- XBOX page;
- Device page;
- Settings;
- Overlay;
- tray.

The user requested the XBOX page as a normal game-name/profile surface, not a technical identity console.

PR7 has no frontend active-game contract.

A later product requirement can expose context through the existing profile surface.

---

## 45. Logging privacy / usefulness

Production logs may contain technical evidence useful for support.

Recommended positive-match Info fields:

~~~text
Key
DisplayName
PID
RunningExecutableName
~~~

Package/config technical paths may remain Debug when needed to diagnose a failed identity match.

Do not reproduce the old giant diagnostic report in routine logs.

Do not log every duplicate WinEvent at Info.

The log should remain useful for future `Addon/Log` analysis without becoming noisy.

---

## 46. Field validation after code merge

The user is currently external and cannot run the MSI Claw field test.

PR7 may be implemented/reviewed/merged based on:

- already-passed PoC B field evidence;
- preservation of the proven identity/lifecycle algorithm;
- focused unit/source-contract tests;
- full CI.

Do **not** mark the following new production-lifetime validation as completed until hardware is available.

Later field checklist:

~~~text
Runtime launch with no game
→ production observer starts automatically

launch Aniimo
→ ActiveGame accepted

Alt+Tab
→ retained

exit
→ cleared

launch Minecraft
→ accepted

game already running + Runtime restart
→ bounded startup reconcile recovers

Sleep/Hibernate/Resume with game alive
→ active identity recovers/retains correctly

Main UI close/reopen
→ production XBOX detection never stops
~~~

No need to preserve the old diagnostic page just to perform this validation.

Use AppLog evidence.

---

## 47. Microsoft / platform references reviewed

### WinEvent

- `SetWinEventHook`  
  https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwineventhook

Relevant documented facts:

- zero process/thread IDs can observe the current desktop broadly;
- `WINEVENT_OUTOFCONTEXT` queues cross-process events;
- the registering thread must have a message loop;
- returned hook handles are explicitly unhooked.

### HWND → PID

- `GetWindowThreadProcessId`  
  https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid

### Live process image / minimal access

- `QueryFullProcessImageNameW`  
  https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew
- Process security/access rights  
  https://learn.microsoft.com/windows/win32/procthread/process-security-and-access-rights

### Packaged-process identity

- `GetPackageFullName`  
  https://learn.microsoft.com/windows/win32/api/appmodel/nf-appmodel-getpackagefullname
- `GetPackageFamilyName`  
  https://learn.microsoft.com/windows/win32/api/appmodel/nf-appmodel-getpackagefamilyname
- Package identity overview  
  https://learn.microsoft.com/windows/apps/desktop/modernize/package-identity-overview

### Package/config location

- `PackageManager.FindPackageForUser` / current package metadata path used by the proven implementation  
  https://learn.microsoft.com/uwp/api/windows.management.deployment.packagemanager.findpackageforuser

### Process lifetime

- `ThreadPool.RegisterWaitForSingleObject`  
  https://learn.microsoft.com/dotnet/api/system.threading.threadpool.registerwaitforsingleobject

The existing implementation already conforms to the core event-driven/process-lifetime pattern.

PR7 should simplify around that implementation, not replace it.

---

## 48. Required acceptance criteria

PR7 is complete only when all are true:

1. `XboxGameSessionRuntime` exists outside the Diagnostics namespace.
2. There is exactly one production XBOX session owner.
3. It starts automatically during normal Runtime startup.
4. It does not depend on Main UI/Developer Menu.
5. It does not start in headless uninstall preparation.
6. It works regardless of Center M Enabled/Disabled state.
7. `ActiveXboxGame` is a minimal production fact keyed by canonical XBOX key.
8. `ActiveGameChanged` or equivalent publishes semantic changes only.
9. EVENT_SYSTEM_FOREGROUND / OBJECT_CREATE / OBJECT_SHOW remain the only event inputs.
10. WinEvent callback remains thin.
11. one serialized worker remains the state authority.
12. process generation remains PID + creation time.
13. process handles use limited query + synchronize, not broad rights.
14. process lifetime remains registered-wait/event driven.
15. no periodic polling exists.
16. startup performs one bounded reconcile.
17. resume re-arms hooks and performs one bounded reconcile.
18. stale non-active generation cache is not trusted across resume.
19. a proven still-live active generation may survive resume.
20. Alt+Tab does not clear ActiveGame.
21. active process exit clears ActiveGame.
22. helper executable mismatch remains negative.
23. exact MicrosoftGame.config executable matching remains mandatory.
24. production session path uses production `MicrosoftGameConfigReader` and `XboxGameIdentity`.
25. D: process / C: config metadata remains valid.
26. diagnostic-only AUMID/GetPackageId/GetPackagePathByFullName2 evidence collection is removed from the production session path.
27. production XBOX session code has no dependency on frontend diagnostic DTOs.
28. diagnostic report generation is removed.
29. diagnostic counters/lifecycle-ring state is removed unless a concrete production control-flow use remains.
30. Developer XBOX Active Game Diagnostic card/page is removed.
31. MainNavigation diagnostic route is removed.
32. frontend diagnostic contracts are removed.
33. diagnostic capture/start/stop/report RPCs are removed.
34. frontend protocol is bumped 57 → 58.
35. Main UI disconnect no longer stops XBOX detection.
36. AddonProcessHost disposes the production owner at Runtime shutdown.
37. event-source/start/resume failures are feature-local and do not fail Full1902/Steam.
38. PR7 does not read `ProfileDocument.XboxGames`.
39. PR7 does not add `ActiveProfileTarget`.
40. PR7 invokes zero XBOX profile hardware/OS apply.
41. PR7 changes zero Full1902 presentation/ownership behavior.
42. existing Steam session behavior is unchanged.
43. migrated production session tests pass.
44. identity evaluator tests pass.
45. frontend transport tests reflect the removed diagnostic contract.
46. UI tests reflect removal of the diagnostic surface.
47. full solution build/tests pass.

---

## 49. Review policy

Block for realistic defects such as:

- production observer accidentally remains Developer-UI-started;
- Main UI disconnect stops production detection;
- process exit does not clear ActiveGame;
- Alt+Tab incorrectly clears ActiveGame;
- stale bare PID can become authority after PID reuse;
- resume never re-arms/reconciles;
- hook failure crashes Runtime;
- helper executable becomes a false positive;
- exact executable/config requirement is weakened;
- diagnostic-only evidence collection/DTOs remain as a parallel authority;
- active XBOX change accidentally applies settings in PR7;
- active XBOX changes presentation/HidHide/VIIPER;
- removed frontend contract leaves protocol mismatch or dead UI navigation.

Do not block for:

- theoretical two-game arbitration;
- exact instruction-level callback interleavings with no realistic lifecycle consequence;
- absence of a generalized platform/session abstraction;
- absence of retry machinery after a rare hook failure.

---

## 50. Follow-up — PR8

After PR7:

~~~text
Steam active identity:
SteamSessionRuntime.ActualRunningAppId

XBOX active identity:
XboxGameSessionRuntime.ActiveGame

XBOX persisted profiles:
ProfileDocument.XboxGames
~~~

But they still do not converge.

PR8 then adds exactly one derived selector:

~~~text
Steam RunningAppID != 0
→ ActiveProfileTarget.Steam

else XboxGameSessionRuntime.ActiveGame != null
→ ActiveProfileTarget.Xbox

else
→ ActiveProfileTarget.None
~~~

and teaches the existing CPU/TDP/Power/FPS/Resolution runtimes to resolve that target without duplicating hardware implementations.

Do not pull PR8 into PR7.

---

## 51. Final architecture after PR7

~~~text
                       Runtime lifetime

WinEvent hooks
 FOREGROUND / CREATE / SHOW
            │
            v
XboxGameSessionRuntime
            │
            ├─ process-generation cache
            ├─ exact package/config/exe identity
            ├─ retained process handle
            ├─ process-exit wait
            ├─ bounded startup reconcile
            └─ bounded resume reconcile
            │
            v
ActiveXboxGame?
  Key
  DisplayName

            X
            │
            │  PR7 intentionally stops here
            │
            v
Profile apply / ActiveProfileTarget
          (PR8)
~~~

The main design goal is not more abstraction.

It is:

> one production XBOX session owner, one active XBOX fact, one proven process-lifetime path, and no diagnostic duplicate.
