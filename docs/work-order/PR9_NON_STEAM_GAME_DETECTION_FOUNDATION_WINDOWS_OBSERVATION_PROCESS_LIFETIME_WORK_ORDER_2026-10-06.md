# Work Order — PR9: Non-Steam Game Detection Foundation — Windows Observation and Process Lifetime

> **Date:** 2026-10-06  
> **Repository:** \`onehoon/SteamAddonforClaw\`  
> **Reviewed main:** \`main@f1c476676ffbb2460226f487c5bb456b71bab9e8\`  
> **Architecture authorities:**  
> - \`docs/Full 1902 Implementation/README.md\` and its active precedence chain  
> - \`docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md\`  
> - \`docs/XBOX_GAME_PROFILE_ARCHITECTURE/work-order/PR8_XBOX_SHARED_ACTIVE_PROFILE_TARGET_AND_LIVE_APPLY_WORK_ORDER_2026-10-06.md\`  
> **Previous phase:** PR #697 / squash merge \`f1c476676ffbb2460226f487c5bb456b71bab9e8\` completed platform-neutral profile resolution/apply for Steam + XBOX  
> **Scope:** extract the already field-proven Windows window-observation and process-generation/lifetime/image-path primitives out of the XBOX namespace so they can become the reusable foundation for future non-Steam platforms and user-registered custom EXEs, while preserving XBOX behavior exactly  
> **Out of scope:** Custom EXE feature, Epic/GOG catalogs, registered-executable matcher, shared non-Steam active-game model, universal game detector, Steam detection changes, profile/apply changes, controller-presentation changes

---

## 1. Goal

Create the first reusable **non-Steam game-detection foundation** without changing product behavior.

PR8 already established the lower boundary:

~~~text
platform-specific game identity
        ↓
ActiveProfileTarget / active-profile resolver
        ↓
ResolvedActiveProfile
        ↓
shared CPU / TDP / Power / FPS / Resolution runtimes
~~~

PR9 establishes the upper Windows-observation boundary for non-Steam platforms:

~~~text
                    NON-STEAM WINDOWS FOUNDATION
              ┌────────────────────────────────────┐
              │ WinEvent / top-level HWND / PID    │
              │ process generation / creation time │
              │ process handle / exit wait         │
              │ executable full path query         │
              └──────────────────┬─────────────────┘
                                 │
                    platform-specific identity proof
                                 │
                ┌────────────────┴────────────────┐
                │                                 │
              XBOX                           future consumers
      package + config proof            Custom / Epic / GOG
                │
                ↓
         ActiveXboxGame
~~~

This PR must **not** add a second platform yet.

Its only production consumer remains \`XboxGameSessionRuntime\`.

The purpose is to make the already-proven Windows mechanics reusable before Custom/Epic/GOG are added.

---

## 2. Locked architecture boundary

### 2.1 Steam is explicitly outside this foundation

Steam remains:

~~~text
SteamSessionRuntime.ActualRunningAppId
→ Steam AppID
→ existing Steam profile domain
~~~

Do not route Steam through:

- the new window-observation source;
- process-generation tracking;
- executable matching;
- a common game detector;
- a common session manager.

The existence of another \`SetWinEventHook\` user such as \`SteamBigPictureWindowProbe\` does **not** make it part of this PR.

Steam BPM/window observation is presentation-specific and remains where it is.

Do not consolidate Win32 hooks merely to deduplicate P/Invoke declarations.

### 2.2 Platform-specific identity remains above the shared Windows primitives

The shared foundation may answer only facts such as:

~~~text
top-level window event happened
PID = 1234
process generation = (1234, creationTime)
process is still alive
full image path = D:\Games\Game.exe
process exited
~~~

It must not answer:

~~~text
this is an XBOX game
this is an Epic game
this is a GOG game
this is Custom game X
this profile key should be active
~~~

Those remain platform/session responsibilities.

### 2.3 XBOX identity proof remains unchanged

The following remain XBOX-specific:

- \`GetPackageFullName\`;
- \`GetPackageFamilyName\`;
- current-user package lookup;
- \`MicrosoftGame.config\`;
- XBOX executable-list exact match;
- \`XboxGameIdentity\`;
- canonical XBOX key selection;
- \`XboxGameProcessIdentityEvaluator\`;
- \`ActiveXboxGame\`;
- \`XboxGameSessionRuntime\` active-XBOX semantics.

PR9 is not an XBOX detector redesign.

---

## 3. Current code facts

Reviewed \`main@f1c476676ffbb2460226f487c5bb456b71bab9e8\`.

### 3.1 XboxGameWindowEventSource is already platform-neutral in behavior

Current file:

~~~text
src/SteamInputAddonforClaw/Xbox/Session/XboxGameWindowEventSource.cs
~~~

It currently owns:

- \`EVENT_SYSTEM_FOREGROUND\`;
- \`EVENT_OBJECT_CREATE\`;
- \`EVENT_OBJECT_SHOW\`;
- top-level HWND filtering;
- \`HWND → PID\`;
- foreground PID lookup;
- bounded \`EnumWindows\`;
- WinEvent thread/message pump;
- callback safety;
- hook cleanup.

None of those operations require XBOX package identity.

The XBOX naming is now the primary barrier to reuse.

### 3.2 XboxGameProcessIdentityProbe mixes common process mechanics with XBOX proof

Current file:

~~~text
src/SteamInputAddonforClaw/Xbox/Session/XboxGameProcessIdentityProbe.cs
~~~

The first half is reusable:

~~~text
OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE)
GetProcessTimes
PID + creation time generation key
retained process handle
ThreadPool.RegisterWaitForSingleObject
IsSignaled
Exited
QueryFullProcessImageNameW
Dispose
~~~

The XBOX-specific half starts at package identity:

~~~text
GetPackageFullName
GetPackageFamilyName
PackageManager.FindPackageForUser
MicrosoftGame.config location resolution
XboxGameProcessIdentityEvaluator
~~~

PR9 must split at that boundary.

### 3.3 XboxGameSessionRuntime contains real XBOX session semantics

Current \`XboxGameSessionRuntime\` owns:

- one active XBOX fact;
- candidate-generation cache;
- duplicate-event suppression;
- PID reuse handling;
- XBOX inspection result cache;
- startup bounded reconcile;
- resume rearm/reconcile;
- live-active retention after WinEvent source failure;
- process-exit clearing;
- "second positive XBOX process does not arbitrate while current active remains live."

Do not extract this whole class into a universal session runtime in PR9.

There is no second session consumer yet.

---

## 4. Create a dedicated foundation namespace

Add a narrow namespace/folder for reusable Windows game-detection primitives.

Recommended:

~~~text
src/SteamInputAddonforClaw/GameDetection/Windows/
~~~

Namespace:

~~~csharp
namespace SteamInputAddonforClaw.GameDetection.Windows;
~~~

Do not place the extracted primitives under:

- \`Xbox\`;
- \`Steam\`;
- \`Profiles\`;
- \`WindowsGaming\` simply because that namespace already exists.

The current \`WindowsGaming\` domain contains FSE/package/launch configuration and is not the non-Steam process-observation authority.

Do not create a multi-layer project/package for this foundation.

Keep it internal to the existing Runtime assembly.

---

## 5. Extract the generic WinEvent window source

Move/rename the current XBOX-specific observation types into the new foundation.

Recommended resulting types:

~~~csharp
internal enum GameWindowEventKind
{
    Foreground,
    Create,
    Show,
}

internal sealed record GameWindowObservation(
    GameWindowEventKind Kind,
    nint WindowHandle,
    uint ProcessId);

internal interface IGameWindowEventSource : IAsyncDisposable
{
    Task StartAsync(
        Action<GameWindowObservation> observation,
        Action<Exception> failure,
        CancellationToken cancellationToken);

    Task StopAsync();

    uint GetForegroundProcessId();

    IReadOnlyList<uint> EnumerateTopLevelProcessIds(int maximumProcessCount);
}

internal sealed class WindowsGameWindowEventSource : IGameWindowEventSource
{
    // extracted implementation
}
~~~

Equivalent concise naming is acceptable, but do not keep an XBOX prefix on the reusable types.

### Preserve behavior exactly

Keep:

~~~text
EVENT_SYSTEM_FOREGROUND
EVENT_OBJECT_CREATE
EVENT_OBJECT_SHOW
WINEVENT_OUTOFCONTEXT
top-level root check
OBJID_WINDOW / CHILDID_SELF requirement for object events
foreground event acceptance
zero HWND/PID rejection
deduplicated bounded EnumWindows PID result
dedicated background thread
message queue creation before readiness
WM_QUIT shutdown
UnhookWinEvent cleanup
no managed exception escaping native callback
~~~

Do not add:

- HIDE;
- DESTROY;
- LOCATIONCHANGE;
- UIAutomation;
- periodic foreground polling;
- WMI;
- process-list polling.

Future consumers can prove a need before expanding the event set.

### Logging

The generic source should not emit XBOX-specific log category/text.

Use a neutral category such as:

~~~text
GameDetection.Windows
~~~

Only the XBOX session owner should emit XBOX-specific lifecycle logs.

---

## 6. Extract generic process generation/lifetime ownership

Create one shared process primitive file, for example:

~~~text
GameDetection/Windows/WindowsGameProcess.cs
~~~

Recommended common contracts:

~~~csharp
internal readonly record struct GameProcessGenerationKey(
    uint ProcessId,
    long CreationTime);

internal readonly record struct GameProcessImageQueryResult(
    bool Succeeded,
    string? ImagePath,
    int ErrorCode);

internal interface IGameProcessGeneration : IDisposable
{
    uint ProcessId { get; }
    GameProcessGenerationKey Key { get; }
    bool IsSignaled { get; }

    GameProcessImageQueryResult QueryImagePath();

    event Action<IGameProcessGeneration>? Exited;
}

internal sealed record GameProcessOpenResult(
    IGameProcessGeneration? Generation,
    int ErrorCode);

internal interface IWindowsGameProcessSource
{
    GameProcessOpenResult Open(uint processId);
}

internal sealed class WindowsGameProcessSource : IWindowsGameProcessSource
{
    // OpenProcess / GetProcessTimes / registered wait implementation
}
~~~

Equivalent record/class names are acceptable if the ownership remains this small.

### Why image path belongs here

Custom/Epic/GOG exact executable matching will require the same native path query.

Therefore this is a proven reusable primitive, not speculative abstraction.

### Preserve lazy/duplicate-event cost

Do not query the image path merely because \`Open(processId)\` succeeded.

Current XBOX runtime opens a candidate generation before duplicate-generation suppression.

A duplicate WinEvent for an already-classified generation should not trigger unnecessary image-path/package work.

Therefore:

~~~text
Open
→ PID + creation time + lifetime handle only

new/unclassified generation
→ QueryImagePath
→ platform-specific proof
~~~

\`QueryImagePath()\` may cache its result per generation if that simplifies reuse, but no global path cache is needed.

### Native handle

The shared interface must not expose \`SafeProcessHandle\` to future platform code.

The concrete \`WindowsGameProcessGeneration\` may retain an internal native handle accessor for the XBOX-specific Windows package probe inside the same assembly.

This is implementation plumbing, not a public/shared identity contract.

---

## 7. Refactor WindowsXboxGameProcessIdentityProbe to compose the foundation

Keep:

~~~text
IXboxGameProcessIdentityProbe
WindowsXboxGameProcessIdentityProbe
~~~

as XBOX-specific types.

Change their common process contracts to the new foundation types.

Conceptually:

~~~csharp
internal interface IXboxGameProcessIdentityProbe
{
    GameProcessOpenResult Open(uint processId);

    Task<XboxGameProcessInspection> InspectAsync(
        IGameProcessGeneration generation,
        CancellationToken cancellationToken);
}
~~~

\`WindowsXboxGameProcessIdentityProbe\` should compose/use \`WindowsGameProcessSource\`.

Recommended flow:

~~~text
Open(processId)
→ WindowsGameProcessSource.Open
→ generic generation

InspectAsync(generation)
→ generation.QueryImagePath()
→ XBOX package full name/family name
→ current-user package config locations
→ XboxGameProcessIdentityEvaluator
~~~

Do not duplicate \`OpenProcess\`, \`GetProcessTimes\`, registered wait, or image-path P/Invoke in the XBOX probe after extraction.

### XBOX package query access

For the production Windows implementation only:

~~~text
IGameProcessGeneration
→ require concrete WindowsGameProcessGeneration
→ use its internal retained SafeProcessHandle
→ GetPackageFullName/GetPackageFamilyName
~~~

Do not add package APIs to \`IGameProcessGeneration\`.

Future Custom/Epic/GOG code must not need package APIs.

---

## 8. Refactor XboxGameSessionRuntime to use common observation/generation types

Update \`XboxGameSessionRuntime\` to depend on:

~~~text
IGameWindowEventSource
IXboxGameProcessIdentityProbe
GameWindowObservation
GameProcessGenerationKey
IGameProcessGeneration
~~~

Default construction:

~~~csharp
_windowSource = windowSource ?? new WindowsGameWindowEventSource();
_processProbe = processProbe ?? new WindowsXboxGameProcessIdentityProbe();
~~~

The XBOX session owner remains XBOX-specific.

Do not rename it to:

- \`GameSessionRuntime\`;
- \`NonSteamGameSessionRuntime\`;
- \`UniversalGameSessionRuntime\`.

### Preserve all current lifecycle behavior

Required unchanged behaviors:

1. first start installs hooks and performs one bounded reconcile;
2. foreground PID is inspected first;
3. \`EnumWindows\` is used only when needed and bounded to the existing maximum;
4. duplicate events for the same PID+creation-time generation do not re-inspect;
5. PID reuse retires the old generation and evaluates the new generation;
6. Alt+Tab to a non-XBOX process does not clear the active XBOX fact;
7. matched process exit clears ActiveXboxGame exactly once;
8. stale exit notification from an old generation cannot clear the replacement generation;
9. a second positive XBOX process does not replace a still-live current active process;
10. failed initial hook start remains feature-local;
11. resume re-arms hooks and performs bounded reconcile;
12. resume retires non-active cached generations;
13. a signaled active generation is cleared;
14. WinEvent-source failure retains a proven live active XBOX game until its process exits;
15. shutdown unregisters waits and disposes retained process handles;
16. no polling is introduced.

---

## 9. Rename/remove XBOX-prefixed shared types completely

Because the product is still in development and there is no deployed compatibility contract, do not keep compatibility wrappers/aliases merely to preserve old internal names.

Expected removals/renames include:

~~~text
XboxGameWindowEventKind
→ GameWindowEventKind

XboxGameWindowObservation
→ GameWindowObservation

IXboxGameWindowEventSource
→ IGameWindowEventSource

WindowsXboxGameWindowEventSource
→ WindowsGameWindowEventSource

XboxGameProcessGenerationKey
→ GameProcessGenerationKey

IXboxGameProcessGeneration
→ IGameProcessGeneration
~~~

Do not leave obsolete forwarding types in the XBOX namespace.

The source tree should make the ownership boundary obvious.

---

## 10. Do not extract the session coordinator yet

This PR deliberately stops before generic session orchestration.

Keep inside \`XboxGameSessionRuntime\`:

~~~text
Channel<SessionMessage>
candidate dictionary
inspection cache
active generation
active XBOX publication
reconcile algorithm
resume policy
event-source failure policy
XBOX second-positive-process policy
~~~

Reason:

> One existing consumer is not enough evidence to freeze a generic session abstraction.

The next Custom EXE phase will provide the second real consumer.

Only after comparing XBOX and registered-executable session code should another extraction be considered.

---

## 11. Do not add registered-executable matching yet

PR9 prepares the mechanics but does not introduce:

~~~text
RegisteredGameExecutable
RegisteredExecutableMatcher
custom:<GUID>
Epic manifest parsing
GOG .info parsing
ActiveNonSteamGame
GamePlatform enum
~~~

These belong to the next feature PR when there is a real second consumer.

This keeps the foundation evidence-based.

---

## 12. Profile/apply layer must remain untouched

PR8 established the required architecture.

PR9 must not modify semantics in:

~~~text
ActiveProfileTarget
ActiveProfileResolver
ResolvedActiveProfile

CpuBoostRuntime
TdpRuntime
PowerModeRuntime
IntelFrameLimiterRuntime
GameDisplayResolutionRuntime
~~~

No profile schema change.

No frontend protocol change.

No Main App/Overlay behavior change.

No Steam profile mutation change.

The expected effective XBOX path after PR9 is still:

~~~text
WindowsGameWindowEventSource
→ PID
→ WindowsGameProcessSource
→ generic process generation/image path
→ XBOX package/config identity proof
→ XboxGameSessionRuntime.ActiveGame
→ ActiveProfileResolver
→ existing shared apply runtimes
~~~

---

## 13. Full1902/controller behavior must remain untouched

Do not modify:

- Center M authority;
- PID1901/PID1902 transition;
- DirectInput;
- HidHide;
- VIIPER;
- Xbox360/SteamDeck presentation;
- WING / Win+G suppression;
- physical-input lifecycle;
- rumble;
- LED/vibration;
- M1/M2;
- sleep controller teardown/recovery.

Non-Steam detection is not controller authority.

---

## 14. Suggested production file layout

Recommended final layout:

~~~text
src/SteamInputAddonforClaw/
    GameDetection/
        Windows/
            WindowsGameWindowEventSource.cs
            WindowsGameProcess.cs

    Xbox/
        Session/
            XboxGameProcessIdentityProbe.cs
            XboxGameProcessIdentityEvaluator.cs
            XboxGameSessionRuntime.cs
            ActiveXboxGame.cs
~~~

Delete the old XBOX-owned window source file after the implementation is moved.

If \`XboxGameProcessIdentityProbe.cs\` becomes clearer after splitting package-query helpers into a small XBOX-only private/static section, that is acceptable.

Do not create extra projects or assemblies.

---

## 15. Tests — move primitive tests to foundation ownership

Current \`XboxGameSessionRuntimeTests\` includes both:

- XBOX session behavior tests;
- generic WinEvent filter tests.

Split responsibility.

Recommended new test file:

~~~text
tests/SteamInputAddonforClaw.Tests/WindowsGameDetectionFoundationTests.cs
~~~

### 15.1 Window-source tests

Move/retain coverage for:

- allowed FOREGROUND;
- allowed CREATE;
- allowed SHOW;
- object event requires OBJID_WINDOW;
- object event requires CHILDID_SELF;
- zero HWND rejected;
- non-top-level HWND rejected;
- unrelated event rejected;
- bounded top-level PID enumeration behavior where testable.

The test should reference \`WindowsGameWindowEventSource.IsRelevantObservation\`, not XBOX.

### 15.2 Process-generation tests

Add focused tests for the new common primitive where deterministic seams allow it.

Required behavior to prove:

~~~text
PID + creation time forms generation key
same PID with different creation time is a different generation
process exit notification is one-shot for production registration
Dispose is idempotent
image-path query result is generation-local
image-path failure retains an error code
~~~

Do not create brittle tests that depend on arbitrary scheduler timing.

If native process-handle behavior is difficult to unit-test directly, keep the native implementation small and validate the semantic behavior through the existing XBOX fake-generation tests.

---

## 16. XBOX regression tests

Update \`XboxGameSessionRuntimeTests\` to use the new shared contracts.

Fake types should become conceptually:

~~~text
FakeGameWindowEventSource : IGameWindowEventSource
FakeGameProcessGeneration : IGameProcessGeneration
FakeXboxGameProcessIdentityProbe : IXboxGameProcessIdentityProbe
~~~

Keep all current production lifecycle tests.

Required XBOX regression list:

- already-running foreground XBOX game recovered once;
- bounded window reconcile when foreground is not the game;
- Create/Show/Foreground event activation;
- duplicate generation suppression;
- Alt+Tab retention;
- active process exit clear;
- PID reuse;
- stale exit protection;
- second positive process non-arbitration;
- initial hook failure and later resume rearm;
- resume cache retirement;
- signaled active generation handling;
- event-source failure live-active retention;
- shutdown disposal;
- package/config/image fail-closed evidence;
- exact executable match;
- package/config location resolution;
- optional package display metadata failure behavior.

The expected assertions should not change except for renamed shared types/namespaces.

---

## 17. Source audits

Before opening the PR, verify all of the following.

### Shared foundation contains no platform identity

Search the new \`GameDetection/Windows\` sources and confirm they contain no:

~~~text
Xbox
MicrosoftGame.config
GetPackageFullName
GetPackageFamilyName
PackageManager
Steam
Epic
GOG
Custom
Profile
~~~

A neutral diagnostic namespace/category string is acceptable; platform names are not.

### XBOX-specific package logic remains under XBOX

Confirm:

~~~text
GetPackageFullName
GetPackageFamilyName
MicrosoftGame.config
XboxGameIdentity
XboxGameProcessIdentityEvaluator
~~~

remain XBOX-owned.

### Old shared XBOX names are gone

No production definitions/usages of:

~~~text
IXboxGameWindowEventSource
WindowsXboxGameWindowEventSource
XboxGameWindowObservation
XboxGameWindowEventKind
IXboxGameProcessGeneration
XboxGameProcessGenerationKey
~~~

### No product feature expansion

Confirm no:

~~~text
custom:<GUID>
Epic
GOG
RegisteredExecutableMatcher
ActiveNonSteamGame
GamePlatform
universal detector/provider registry
new polling timer
new WMI watcher
~~~

---

## 18. Logging requirements

Keep logs useful while moving ownership.

Foundation logs should describe Windows mechanics:

~~~text
GameDetection.Windows
WinEvent source
process generation
process image query
~~~

XBOX logs should continue to describe product identity/session semantics:

~~~text
XboxSession
candidate
package/config proof
positive XBOX identity
ActiveXboxGame
~~~

Do not rename all XBOX lifecycle logs to generic game logs simply because low-level primitives moved.

This PR should improve ownership clarity, not erase domain context.

---

## 19. Failure policy

Preserve current practical failure behavior.

### WinEvent source cannot start

~~~text
XBOX active detection unavailable
→ Runtime continues
→ controller authority unaffected
~~~

### Candidate process cannot be opened

~~~text
log/debug
→ ignore candidate
~~~

### Image path cannot be queried

~~~text
XBOX inspection fails closed
→ no active XBOX identity
~~~

### Package/config proof fails

~~~text
XBOX-specific negative result
→ no active XBOX identity
~~~

### Event source fails after a proven active XBOX game exists

Keep current behavior:

~~~text
stop accepting new observations
→ retain proven live active generation
→ process-handle exit still clears it
~~~

Do not weaken this lifecycle behavior during extraction.

---

## 20. Overengineering constraints

Do not create:

- \`IGameDetector\`;
- \`IGamePlatform\`;
- \`IGameIdentityProvider\`;
- \`GameDetectionManager\`;
- \`GameSessionManager\`;
- provider/strategy registries;
- shared persisted game ID;
- generic catalog abstraction;
- generic active-game arbitration;
- polling fallback.

This PR needs only:

~~~text
one Windows window-event source
one Windows process-generation/lifetime source
one generic generation contract
existing XBOX session + XBOX identity proof
~~~

A new abstraction is justified only if it represents a concrete Windows primitive already needed by XBOX and known to be required by future exact-EXE detection.

---

## 21. Real lifecycle acceptance

The refactor must preserve realistic lifecycle behavior:

- normal game launch;
- already-running game during Runtime startup;
- Alt+Tab;
- matched game process exit;
- PID reuse;
- controlled Runtime restart;
- Sleep/Hibernate/Resume;
- WinEvent-source operation failure;
- shutdown/dispose.

Do not add locks/epochs/barriers for theoretical callback interleavings that current serialized \`XboxGameSessionRuntime\` already handles safely.

---

## 22. Required validation

Before opening the PR:

~~~powershell
dotnet build .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\tests\SteamInputAddonforClaw.UiTests\SteamInputAddonforClaw.UiTests.csproj -c Release --no-restore
git diff --check
~~~

Also run source searches for the audit rules in section 17.

No frontend transport version bump is expected.

---

## 23. Manual validation

Because this PR changes the low-level observer/lifetime implementation used by production XBOX detection, perform one MSI Claw regression pass even though there is no new feature.

Required:

### A. Launch XBOX game

~~~text
launch field-proven XBOX title
→ active XBOX identity accepted
→ PR8 profile applies
~~~

### B. Alt+Tab

~~~text
Alt+Tab away
→ active XBOX identity retained
→ game profile remains effective
~~~

### C. Exit

~~~text
exit matched game process
→ ActiveXboxGame clears
→ Device/global profile convergence
~~~

### D. Runtime restart with game already running

~~~text
restart Runtime
→ bounded startup reconcile
→ existing game recovered
~~~

### E. Sleep/Resume

~~~text
game active
→ Sleep/Hibernate
→ Resume
→ hooks rearm
→ bounded session reconcile
→ active identity/profile remains correct
~~~

Steam and controller-presentation behavior must be unchanged throughout.

---

## 24. PR description requirements

The implementation PR must explicitly state:

- baseline commit reviewed;
- this is a behavior-preserving extraction;
- Steam is excluded from the new foundation;
- WinEvent/HWND/PID observation is now platform-neutral;
- PID+creation-time/process-handle/exit/image-path mechanics are now platform-neutral;
- XBOX package/config identity remains XBOX-specific;
- \`XboxGameSessionRuntime\` remains the session owner;
- no generic session manager/detector framework was introduced;
- no Custom/Epic/GOG feature was added;
- profile/apply and Full1902 controller paths were untouched;
- automated validation results;
- MSI Claw XBOX regression status.

---

## 25. Completion condition

PR9 is complete only when the production XBOX path is functionally unchanged but the source ownership becomes:

~~~text
GameDetection.Windows
    WindowsGameWindowEventSource
    WindowsGameProcessSource
    GameProcessGeneration
    image-path query
             │
             ↓
Xbox.Session
    WindowsXboxGameProcessIdentityProbe
    package/config identity proof
    XboxGameSessionRuntime
             │
             ↓
ActiveXboxGame
             │
             ↓
existing PR8 profile/apply path
~~~

The foundation must be immediately reusable by the next Custom EXE phase without copying:

- WinEvent hooks;
- top-level window enumeration;
- process open;
- creation-time generation logic;
- exit wait;
- image-path query.

The next PR should then add the first real second consumer: **user-registered Custom EXE detection**, using exact executable-path matching on top of this foundation.
