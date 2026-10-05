# Work Order — XBOX PR2: Event-Driven Active Game Session Diagnostic PoC

> **Date:** 2026-10-05
> **Repository:** onehoon/SteamAddonforClaw
> **Reviewed baseline:** main@b9d86d450958d6c1085e072dee1f907506f0366c
> **Current desktop frontend protocol at review:** FrontendTransportProtocol.CurrentVersion = 51
> **Architecture authority:** docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md
> **PR1 work order:** docs/XBOX_GAME_PROFILE_ARCHITECTURE/work-order/PR1_XBOX_INSTALLED_GAME_CATALOG_IDENTITY_DIAGNOSTIC_POC_WORK_ORDER_2026-10-05.md
> **Full1902 authority:** docs/Full 1902 Implementation/README.md and its precedence chain
> **Process model:** persistent Runtime + disposable Main App + independent warm Overlay
> **Scope:** read-only Developer Menu active-session diagnostic
> **Production XBOX profile application:** out of scope

---

## 1. Goal

Implement the second XBOX/Game Pass architecture validation as a Runtime-owned, event-driven active-game session diagnostic.

The PR must answer:

> Can the existing persistent Runtime identify a running XBOX/Game Pass/GDK game from normal Windows lifecycle events, using the actual process executable plus package identity plus MicrosoftGame.config exact-executable evidence, retain that game across normal Alt+Tab, and clear it when the matched process really exits — without polling and without disturbing Full1902, Steam, Main App, or Overlay architecture?

Target flow:

~~~text
WinEvent
   ↓
top-level HWND / PID
   ↓
OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)
   ↓
actual running executable path/name
   ↓
process package identity
   ↓
package MicrosoftGame.config
   ↓
exact executable-basename match to ExecutableList
   ↓
Active XBOX Game diagnostic fact
   ↓
matched process lifetime
   ↓
process exit clears fact
~~~

This PR is diagnostic evidence only.

It must not apply XBOX profiles or become a production profile selector.

---

## 2. PR1 real-machine evidence that PR2 must preserve

PR1 was validated on the target MSI Claw.

Observed:

~~~text
PackageManager.FindPackagesForUser("")
→ success

170 packages returned
170 package locations accessible
2 MicrosoftGame.config candidates
2 recognized/parseable configs
2 valid XBOX/GDK candidates
0 skipped/failed
~~~

Real games:

~~~text
Minecraft for Windows
StoreId: 9NBLGGH2JHXJ
PFN: Microsoft.MinecraftUWP_8wekyb3d8bbwe
Executable: Minecraft.Windows.exe

Aniimo Legend
StoreId: 9PK8PHLCQDF6
PFN: KingsgloryGames.AniimoLegend_9d08hqzdedf04
Executable: Aniimo.exe
~~~

Important real-machine finding:

> Both games are actually installed by the user on D:, while PR1 reported the package/config metadata root under C:\Program Files\WindowsApps\....

Therefore PR2 must explicitly preserve:

~~~text
package/config metadata location
!=
actual running process image location
~~~

Do not infer the running executable path by combining a package root with ExecutableList.

The live process is authoritative for its executable path.

---

## 3. Existing architecture boundary is frozen

Current process/lifetime separation is a hard requirement:

~~~text
SteamInputAddonforClaw.exe
→ persistent Runtime / tray / system authority
→ owns XBOX active-session diagnostic source

SteamInputAddonforClaw.UI.exe
→ disposable Main App
→ renders diagnostic state through existing .Frontend transport

SteamInputAddonforClaw.Overlay.exe
→ independent warm Overlay process
→ unchanged
~~~

Required ownership:

~~~text
Developer UI
→ Start / Stop / Capture over existing .Frontend RPC

Runtime
→ WinEvent hooks
→ candidate process inspection
→ package/config evidence
→ active diagnostic state
→ process lifetime
~~~

Do not:

- put WinEvent hooks in SteamInputAddonforClaw.UI.exe;
- put XBOX session detection in SteamInputAddonforClaw.Overlay.exe;
- modify OverlayProcessController;
- modify OverlayWire or the .Overlay protocol;
- add a helper executable;
- add a Windows service;
- add another named pipe;
- add a generic event bus;
- replace or wrap SteamSessionRuntime.ActualRunningAppId;
- introduce a universal IGameIdentityProvider, provider registry, platform hierarchy, or general game-detection framework.

This PR adds one narrow developer diagnostic Runtime owner.

---

## 4. Full1902 invariants — zero controller-authority changes

PR2 must not alter:

~~~text
Center M authority
PID1901 / PID1902 ownership
DirectInput
HidHide
VIIPER
Xbox360 / SteamDeck presentation
Steam/BPM presentation selection
M1/M2 mapping
rumble
LED
vibration
PnP recovery
routing rollback / fail-close
~~~

Required invariant:

~~~text
XBOX game detected
≠ force Xbox360
≠ detach SteamDeck
≠ change PID
≠ change HidHide
≠ touch VIIPER
≠ apply profile
~~~

The diagnostic must be safe whether Center M is Enabled or Disabled.

---

## 5. Microsoft / Windows API basis

Use documented Windows APIs and minimal access rights.

### 5.1 Event source

Microsoft documentation:

- SetWinEventHook:
  https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwineventhook

Use out-of-context hooks for:

~~~text
EVENT_SYSTEM_FOREGROUND
EVENT_OBJECT_CREATE
EVENT_OBJECT_SHOW
~~~

For object events require:

~~~text
idObject == OBJID_WINDOW
idChild == CHILDID_SELF
HWND != 0
GetAncestor(hwnd, GA_ROOT) == hwnd
PID != 0
~~~

Do not add NAMECHANGE/HIDE/DESTROY merely for completeness.

Window destruction is not session lifetime authority.

### 5.2 Actual executable path

Microsoft documentation:

- QueryFullProcessImageName:
  https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew
- Process security/access:
  https://learn.microsoft.com/windows/win32/procthread/process-security-and-access-rights

Open candidates with:

~~~text
PROCESS_QUERY_LIMITED_INFORMATION
~~~

The live process query is authoritative for:

~~~text
RunningProcessPath
RunningExecutableName
~~~

It may legitimately point to D: while package/config metadata points to C:.

### 5.3 Process package identity

Microsoft documentation:

- GetPackageFullName:
  https://learn.microsoft.com/windows/win32/api/appmodel/nf-appmodel-getpackagefullname
- GetPackageFamilyName:
  https://learn.microsoft.com/windows/win32/api/appmodel/nf-appmodel-getpackagefamilyname
- GetPackageId:
  https://learn.microsoft.com/windows/win32/api/appmodel/nf-appmodel-getpackageid
- GetApplicationUserModelId:
  https://learn.microsoft.com/windows/win32/api/appmodel/nf-appmodel-getapplicationusermodelid
- Package identity overview:
  https://learn.microsoft.com/windows/apps/desktop/modernize/package-identity-overview

Expected APPMODEL_ERROR_NO_PACKAGE / NO_APPLICATION results are normal negative evidence, not Runtime failures.

### 5.4 Package path evidence

Microsoft documentation:

- GetPackagePathByFullName2:
  https://learn.microsoft.com/windows/win32/api/appmodel/nf-appmodel-getpackagepathbyfullname2
- PackagePathType:
  https://learn.microsoft.com/windows/win32/api/appmodel/ne-appmodel-packagepathtype

Official path types include:

~~~text
Install
Mutable
Effective
MachineExternal
UserExternal
EffectiveExternal
~~~

PR2 should collect these only as diagnostic evidence for a positive package.

Do not make them a second identity authority.

Do not scan those directories.

### 5.5 Process lifetime

The matched process itself is the session-lifetime authority.

Acceptable narrow implementations:

~~~text
System.Diagnostics.Process.Exited
registered wait on the retained process handle
one dedicated wait owned by the diagnostic session
~~~

Do not poll HasExited.

Do not use WMI process polling.

---

## 6. ClawHUD is reference evidence only

Review:

~~~text
onehoon/ClawHUD
docs/GAME_DETECTION_PRODUCTION_DESIGN.md
docs/GAME_DETECTION_FIELD_ANALYSIS_2026-08-31.md

src/ClawHUD/GameDetection/
    WindowsGameIdentityProbe.cpp
    MicrosoftGameTrigger.cpp
    ProductionGameWindowSource.cpp
    GameSessionController.cpp
~~~

Carry over only the field-proven lessons:

1. GamingServicesUI.exe and PickerHost.exe appear during Xbox launches.
2. Window title/fullscreen geometry are not trustworthy game evidence.
3. Minecraft may CREATE/SHOW long before becoming foreground.
4. Exact MicrosoftGame.config executable match is strong XBOX identity evidence.
5. Alt+Tab must not end the session.
6. window HIDE/DESTROY is not process lifetime.
7. WinEvent callback work must stay thin.
8. bare PID is insufficient as process-generation identity.

Do not port:

- PresentMon;
- renderer verification;
- generic Win32 game detection;
- ClawHUD IPC;
- ClawHUD session architecture wholesale.

SteamAddonforClaw only needs XBOX/Game Pass identity.

---

## 7. One narrow Runtime diagnostic owner

Create one owner, conceptually:

~~~text
XboxGameSessionDiagnostic
~~~

Suggested location:

~~~text
src/SteamInputAddonforClaw/Diagnostics/XboxSession/
    XboxGameSessionDiagnostic.cs
    XboxGameProcessIdentityProbe.cs
    XboxGameWindowEventSource.cs
~~~

Exact filenames may vary.

Responsibilities:

- install/remove the three WinEvent hooks;
- serialize accepted observations;
- inspect candidate process identity;
- own current diagnostic ActiveXboxGame;
- own current matched-process lifetime wait/event;
- perform bounded initial reconcile;
- perform bounded resume reconcile if included;
- expose diagnostic snapshot/report.

It must not own:

- Steam;
- XBOX profile persistence;
- performance settings;
- display settings;
- M1/M2;
- controller presentation;
- Overlay;
- Game Bar;
- ClawHUD.

Do not split into manager/coordinator/controller abstractions unless real implementation necessity appears.

---

## 8. Developer-diagnostic lifetime

Do not make PR2 a permanent production observer.

Developer Menu explicitly starts/stops the diagnostic.

Recommended UI:

~~~text
XBOX Active Game Diagnostic

[Start] [Stop] [Capture Report]

Observer: Stopped / Running / Failed
Active XBOX game: <none or title>
~~~

Start:

~~~text
Runtime installs hooks
→ hooks armed successfully
→ bounded initial reconcile
→ Running
~~~

Stop:

~~~text
stop admitting callbacks
→ unhook WinEvent hooks
→ cancel queued probe work
→ retire process-exit registration
→ clear diagnostic active state
→ Stopped
~~~

If the Main UI disconnects/crashes while this developer diagnostic is running, stop it at the existing single-frontend disconnect boundary.

Use the same practical pattern already used for Runtime-owned developer sessions.

Do not create a generic diagnostic-session registry.

---

## 9. Thin WinEvent callback

The callback may only:

~~~text
validate event
→ capture HWND
→ capture PID
→ capture immediate GA_ROOT/top-level fact
→ enqueue a small immutable observation
→ return
~~~

Do not call from the callback:

~~~text
OpenProcess
QueryFullProcessImageName
GetPackageFullName
GetPackageFamilyName
GetPackageId
GetApplicationUserModelId
GetPackagePathByFullName2
File.Open
XML parser
report writer
frontend transport
profile code
~~~

---

## 10. Serialized Runtime worker

Accepted events must enter one serialized diagnostic execution context.

A small Channel<T>, one dedicated worker, or an equivalent existing project primitive is acceptable.

Normal launch can produce CREATE/SHOW/FOREGROUND close together. Serialize them so the same process generation is not probed concurrently.

Do not add:

- per-event Task fan-out;
- epoch framework;
- barrier framework;
- lock hierarchy;
- actor framework;
- multiple state owners.

A definitive positive or negative result may be cached only for that process generation.

Negative cache must not outlive the process generation.

---

## 11. Process-generation identity

Do not use PID alone.

Use the smallest concrete process-instance identity supported by the implementation, for example:

~~~text
PID
+ retained process handle
+ creation time when readily available
~~~

or an equivalent Process instance tied to that live OS process.

Required property:

> Work/results belonging to an exited old process must not activate or clear a later process that reused the same numeric PID.

Do not generalize this into a global generation/epoch authority.

---

## 12. Candidate process identity probe

For each candidate PID:

~~~text
OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)
        ↓
QueryFullProcessImageName
        ↓
GetPackageFullName
GetPackageFamilyName
GetPackageId
GetApplicationUserModelId
        ↓
resolve/read MicrosoftGame.config
        ↓
parse shared config model
        ↓
exact running executable basename match
~~~

Required positive evidence:

~~~text
process open succeeds
+ actual executable path resolved
+ package identity exists
+ PackageFullName resolved
+ PackageFamilyName resolved
+ MicrosoftGame.config readable
+ recognized game root
+ required Identity present
+ usable ExecutableList present
+ running executable basename exactly matches one Executable Name
~~~

Use case-insensitive Windows basename comparison.

Do not require a full-path match.

Optional evidence:

~~~text
AUMID
GetPackageId fields
StoreId
TitleId
ResourceId
official package path variants
~~~

Missing optional evidence must not reject an otherwise proven candidate unless new field evidence requires it.

Expected negatives:

~~~text
ordinary unpackaged process
→ no package identity

packaged non-game app
→ no MicrosoftGame.config

GamingServicesUI / PickerHost
→ no exact configured game executable match

same package / wrong helper executable
→ executable mismatch
~~~

Do not log every negative desktop event at Info.

---

## 13. Reuse the PR1 MicrosoftGame.config parser

PR1 already added:

~~~text
src/SteamInputAddonforClaw/Diagnostics/XboxCatalog/MicrosoftGameConfigReader.cs
~~~

Reuse it.

Do not add a second parser.

Compatibility requirement from the ClawHUD reference:

~~~xml
<Game ...>
<MicrosoftGame ...>
~~~

The shared reader must recognize both supported/research-proven root forms.

If current PR1 code still accepts only Game, extend the shared reader narrowly and add tests.

Keep the PR1 required identity fields:

~~~text
Identity Name
Identity Publisher
usable ExecutableList
~~~

---

## 14. Running path and config path are independent evidence

This is a required PR2 contract.

For a positive match capture both:

~~~text
RunningProcessPath
PackageConfigPath
~~~

Expected on the user's actual environment may look like:

~~~text
RunningProcessPath:
D:\...\Aniimo.exe

PackageConfigPath:
C:\Program Files\WindowsApps\KingsgloryGames.AniimoLegend_...\MicrosoftGame.config
~~~

This is valid.

Do not require:

~~~text
RunningProcessPath starts with PackageConfigRoot
~~~

Do not construct:

~~~text
RunningExePath = PackageConfigRoot + Executable Name
~~~

Identity relation is the exact executable basename match.

---

## 15. Official external package paths as diagnostic evidence

For each positive package, query once:

~~~text
Install
Effective
Mutable
MachineExternal
UserExternal
EffectiveExternal
~~~

using GetPackagePathByFullName2.

Record return code + path where available.

Purpose:

- explain the target machine's D:-installed payload layout;
- determine which official PackagePathType exposes that location, if any.

Do not:

- recursively search returned paths;
- use them as a second detector;
- require an external path for positive identity;
- change ACLs;
- take ownership of WindowsApps.

---

## 16. Active diagnostic-state semantics

Conceptual active record:

~~~text
CandidateKey
ProcessId
RunningProcessPath
RunningExecutableName
PackageFullName
PackageFamilyName
ApplicationUserModelId?
StoreId?
TitleId?
IdentityName
IdentityPublisher
IdentityResourceId?
MatchedExecutableName
ConfigPath
PackagePathEvidence
~~~

This record is:

- Runtime-owned;
- in-memory;
- diagnostic-only;
- not persisted;
- not a production profile target.

Activation:

~~~text
no active game
+ positive process/package/config/exe evidence
→ Active XBOX game = candidate
→ retain process-lifetime owner
~~~

Duplicate event for same process generation:

~~~text
already active
→ no second activation
~~~

Second positive game while current active process is still alive:

~~~text
log diagnostic conflict
→ keep current active game
~~~

Do not add multi-game arbitration in PR2.

If field evidence later proves a normal launcher handoff requires replacement, revise from that evidence.

---

## 17. Process exit owns retirement

When matched process exits:

~~~text
process exit notification
→ verify this is still the current active process generation
→ clear Active XBOX game
→ update snapshot/log/report state
~~~

Do not clear active state because:

- game lost foreground;
- HWND hid;
- HWND was destroyed;
- Xbox app became foreground;
- picker became foreground.

---

## 18. Alt+Tab contract

Required:

~~~text
XBOX game active
→ Alt+Tab to desktop / Explorer / Xbox app
→ matched process still alive
→ Active XBOX game remains unchanged
~~~

Returning to the game must not create a new diagnostic session.

Foreground is an event source, not lifetime authority.

---

## 19. Start/restart reconciliation

The diagnostic must identify a game that is already running before Start.

Required order:

~~~text
install hooks first
→ one current-foreground probe
→ if needed, one bounded EnumWindows pass for current top-level windows
→ feed candidate PIDs through SAME identity probe
→ stop enumeration
→ normal event-driven operation
~~~

Do not run recurring EnumWindows.

Hooks-first ordering avoids missing a launch transition between snapshot and subscription.

Controlled Runtime restart does not persist active state.

After restart, the diagnostic recomputes state from Windows reality.

---

## 20. Sleep / hibernate / resume

This is a real supported lifecycle.

Do not create a new power architecture.

Use existing Runtime power notification/lifecycle seams.

On suspend:

- stop trusting stale HWND observations;
- do not mutate Full1902;
- retire/revalidate process observation resources as needed by the narrow diagnostic.

On resume:

~~~text
invalidate stale window/process observations
→ ensure diagnostic hooks are valid/armed
→ one bounded reconcile
→ re-identify current XBOX game if still running
~~~

Do not assume old HWND/process handles survive suspend.

Do not add:

- new power manager;
- epoch/barrier system;
- retry loop;
- generalized participant framework.

If safe resume integration requires invasive Full1902 changes, keep the diagnostic-local implementation narrow and document the limitation rather than modifying controller ownership.

---

## 21. Developer UI

Add a Developer Menu card:

~~~text
XBOX Active Game Diagnostic
Event-driven XBOX/Game Pass running-game identity probe.

[Open]
~~~

Dedicated child page:

~~~text
Back to Developer Menu

XBOX Active Game Diagnostic

Read-only. Does not apply profiles or change controller presentation.

[Start] [Stop] [Capture Report]

Observer
  Stopped / Running / Failed

Hooks
  Foreground
  Create
  Show

Active game
  Display name / candidate key
  PID
  Running process path
  Running executable
  Package full name
  Package family name
  AUMID
  StoreId
  TitleId
  Identity
  Matched ExecutableList entry
  Config path

Package path evidence
  Install
  Effective
  Mutable
  MachineExternal
  UserExternal
  EffectiveExternal

Counters
  accepted WinEvents
  unique process generations inspected
  no-package candidates
  config-negative candidates
  executable mismatches
  positive matches
  process exits
~~~

The Main UI must not inspect processes/packages itself.

---

## 22. Frontend contract

Add diagnostic-specific methods to the existing desktop frontend interface.

Recommended shape:

~~~csharp
Task<FrontendXboxSessionDiagnosticSnapshot> CaptureXboxSessionDiagnosticAsync(
    CancellationToken cancellationToken = default);

Task<FrontendXboxSessionDiagnosticSnapshot> StartXboxSessionDiagnosticAsync(
    CancellationToken cancellationToken = default);

Task<FrontendXboxSessionDiagnosticSnapshot> StopXboxSessionDiagnosticAsync(
    CancellationToken cancellationToken = default);

Task<FrontendXboxSessionDiagnosticReportResult> GenerateXboxSessionDiagnosticReportAsync(
    CancellationToken cancellationToken = default);
~~~

A slightly smaller contract is acceptable if report generation fits cleanly into the snapshot, but preserve explicit Start/Stop/Capture semantics.

Suggested state enum:

~~~text
Stopped
Running
Failed
~~~

Do not:

- reuse Steam profile DTOs;
- use AppId=0 sentinels;
- add production ActiveProfileTarget in PR2;
- persist these snapshots.

---

## 23. Desktop frontend protocol only

Reviewed main:

~~~text
FrontendTransportProtocol.CurrentVersion = 51
~~~

This PR adds desktop RPCs, so bump the actual implementation baseline exactly once:

~~~text
51 → 52
~~~

If main advances first, increment the then-current value once.

Do not change:

~~~text
OverlayTransportProtocol
OverlayWire
NamedPipeOverlayServer
NamedPipeOverlayClient
OverlayProcessController
~~~

---

## 24. Frontend disconnect cleanup

The existing .Frontend server already performs narrow cleanup for Runtime-owned developer diagnostics.

Extend that concrete boundary:

~~~text
if XBOX session diagnostic may be running
→ StopXboxSessionDiagnosticAsync(CancellationToken.None)
~~~

Normal explicit Stop and frontend disconnect must converge on the same Runtime stop path.

Do not create a generalized session registry.

---

## 25. Logging

Use:

~~~text
XboxSessionDiagnostic
~~~

Info-level:

- observer started/stopped;
- hook failure;
- bounded reconcile completed;
- positive identity match;
- active game changed;
- matched process exited;
- resume reconcile;
- second-positive-live-game conflict.

Debug-level:

- no-package result;
- config absent/invalid;
- executable mismatch;
- duplicate process generation suppressed;
- raw counters.

Positive-match log must include:

~~~text
PID
RunningProcessPath
PackageFullName
PackageFamilyName
CandidateKey
StoreId
TitleId
MatchedExecutable
ConfigPath
~~~

This is required to prove D:-running executable versus C:-metadata behavior.

---

## 26. Durable report

Write a bounded report in the existing log/diagnostic directory.

Suggested:

~~~text
xbox-session-diagnostic-YYYYMMDD-HHMMSS.txt
~~~

Include:

~~~text
timestamp
OS/build
Addon version

observer state
hook status
event counters

current / last positive game
  PID
  running process path
  executable basename
  candidate key
  AUMID
  package full name
  PFN
  package identity fields
  StoreId
  TitleId
  config path
  matched ExecutableList entry

PackagePathType evidence
  Install
  Effective
  Mutable
  MachineExternal
  UserExternal
  EffectiveExternal
  result/error for each

lifecycle evidence
  activation
  foreground leave/return if observed
  process exit
  resume reconcile
~~~

Do not dump arbitrary window titles or whole XML files.

---

## 27. Strict no-polling policy

Forbidden:

~~~text
timer calling GetForegroundWindow
timer calling EnumWindows
timer calling Process.GetProcesses
timer checking HasExited
PeriodicTimer
Thread.Sleep discovery loop
WMI WITHIN
package-scan timer
filesystem watcher
~~~

Allowed:

~~~text
SetWinEventHook
one-shot current foreground read
one-shot Start/resume EnumWindows
process-exit notification/wait
explicit Developer capture
~~~

---

## 28. Tests — process identity

Required:

1. process open failure → negative, no crash;
2. process image path failure → negative;
3. APPMODEL_ERROR_NO_PACKAGE → expected negative;
4. package full name and PFN resolve;
5. config missing → negative;
6. malformed config → negative;
7. case-insensitive exact executable basename match → positive;
8. executable mismatch → negative;
9. helper process with package identity but no configured executable match → negative;
10. running path and config path may be on different drives → positive;
11. AUMID absent but required package/config/executable evidence succeeds → not rejected unless architecture explicitly requires it;
12. StoreId absent → PFN candidate key;
13. StoreId/PFN absent → identity tuple diagnostic fallback.

---

## 29. Tests — event/session lifecycle

Required:

1. non-window object event ignored;
2. child object ignored;
3. non-top-level window ignored;
4. CREATE accepted;
5. SHOW accepted;
6. FOREGROUND accepted;
7. callback only queues minimal event;
8. duplicate events do not repeatedly fully probe a definitively classified live generation;
9. positive candidate activates;
10. foreground leaves game → active remains;
11. active process exit → active clears;
12. stale old-generation exit cannot clear newer active process;
13. Stop unhooks and clears;
14. frontend disconnect retires session;
15. bounded initial reconcile detects an already-running game;
16. no recurring timer exists.

Do not add tests solely for pathological instruction-level interleavings.

---

## 30. Tests — shared config parser

Extend the existing PR1 MicrosoftGameConfigReader tests if needed.

Required root compatibility:

~~~xml
<Game ...>
<MicrosoftGame ...>
~~~

Both must use the same shared parser.

Still require:

~~~text
Identity Name
Identity Publisher
usable ExecutableList
~~~

Do not loosen validation into arbitrary XML acceptance.

---

## 31. Tests — frontend/UI architecture

Verify:

1. new diagnostic RPCs round-trip through .Frontend;
2. malformed payloads fail closed;
3. protocol increments once;
4. diagnostic is a Developer child, not top-level XBOX page;
5. Start cannot double-start;
6. Stop works while Running;
7. leaving the page invokes the chosen explicit diagnostic stop policy;
8. Main UI contains no SetWinEventHook/process/package/XML logic;
9. Overlay tree contains no XBOX-session diagnostic code.

---

## 32. Manual field validation

### A. Aniimo launch

Start diagnostic and launch Aniimo.

Expected evidence:

~~~text
RunningExecutableName = Aniimo.exe
RunningProcessPath = real D:-side executable path

PackageFamilyName =
KingsgloryGames.AniimoLegend_9d08hqzdedf04

StoreId = 9PK8PHLCQDF6
TitleId = 6B49108E
MatchedExecutable = Aniimo.exe

ConfigPath =
C:-side metadata path or another official package metadata path
~~~

D:/C: difference is expected.

### B. Minecraft launch

Expected:

~~~text
Minecraft.Windows.exe
Microsoft.MinecraftUWP_8wekyb3d8bbwe
StoreId = 9NBLGGH2JHXJ
TitleId = 35760C07
exact ExecutableList match
~~~

GamingServicesUI and PickerHost must not become active.

### C. Early CREATE/SHOW

Verify the real game can be identified before final foreground when strong process/package/config/executable evidence already exists.

### D. Alt+Tab

~~~text
game active
→ Alt+Tab away
→ Active XBOX game remains
→ return
→ no session churn
~~~

### E. Process exit

~~~text
close game
→ matched process exits
→ active state clears
~~~

### F. Start diagnostic after game already runs

~~~text
game already running
→ Start diagnostic
→ bounded reconcile
→ same game identified
~~~

### G. Controlled Runtime restart

~~~text
game running
→ Runtime restart
→ open Developer UI
→ Start diagnostic
→ bounded reconcile
→ same game identified
~~~

### H. Sleep / resume

~~~text
game exists
→ sleep/hibernate
→ resume
→ no stale-handle crash
→ bounded reconcile
→ game recovered if still alive
~~~

### I. Full1902 stability

Throughout all tests:

- no PID mutation from XBOX diagnostic;
- no HidHide mutation;
- no VIIPER attach/detach;
- no X360/SteamDeck presentation switch caused by XBOX diagnostic;
- no M1/M2 change;
- no profile apply.

---

## 33. Race / overengineering policy for this PR

Blocking real lifecycle issues include:

~~~text
PID reused after process exit
stale identity result activates wrong generation
resume trusts stale handle/HWND
frontend crash leaves developer hooks running
process exits while its identity probe is in flight
~~~

Handle with the smallest local checks:

~~~text
one serialized worker
+ process-generation identity
+ current-owner check
~~~

Do not add architecture for theoretical crossings between arbitrary instructions.

Do not introduce locks/epochs/barriers/managers unless real field evidence proves the simple owner cannot converge safely.

---

## 34. Expected files

Expected feature changes:

~~~text
src/SteamInputAddonforClaw/
    Diagnostics/XboxCatalog/MicrosoftGameConfigReader.cs
        // only if shared root compatibility needs extension
    Diagnostics/XboxSession/*
    Frontend/InProcessAddonFrontendControl.cs
    Hosting/AddonProcessHost.cs

src/SteamInputAddonforClaw.Contracts/Frontend/
    FrontendContracts.cs

src/SteamInputAddonforClaw.FrontendTransport/
    FrontendWire.cs
    NamedPipeAddonFrontendClient.cs
    NamedPipeAddonFrontendServer.cs

src/SteamInputAddonforClaw.UI/
    MainNavigationState.cs
    MainWindow.xaml
    MainWindow.xaml.cs
    Views/DeveloperPage.xaml
    Views/DeveloperPage.xaml.cs
    Views/XboxSessionDiagnosticPage.xaml
    Views/XboxSessionDiagnosticPage.xaml.cs

tests/SteamInputAddonforClaw.Tests/*
tests/SteamInputAddonforClaw.UiTests/*
~~~

Files that should not need feature changes:

~~~text
src/SteamInputAddonforClaw.Overlay/*
src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire*
src/SteamInputAddonforClaw/Profiles/ProfileDocument.cs
src/SteamInputAddonforClaw/Profiles/GameProfile.cs
src/SteamInputAddonforClaw/Profiles/Performance/*
src/SteamInputAddonforClaw/Profiles/Display/*
src/SteamInputAddonforClaw/Steam/*
src/SteamInputAddonforClaw/VirtualOutput/*
HidHide / controller-ownership code
~~~

Any feature change in these areas requires a concrete explanation and is presumptively out of PR2 scope.

---

## 35. Explicit non-goals

Do not implement:

- production XboxGameSessionRuntime used by profiles;
- ProfileDocument.XboxGames;
- top-level XBOX page;
- Profile → Steam rename;
- XBOX CPU/TDP/Power/FPS/Resolution application;
- shared active-profile target;
- per-game M1/M2;
- Overlay XBOX profile projection;
- WING Xbox app action;
- Game Bar integration;
- ClawHUD dependency;
- PresentMon;
- generic Epic/GOG/non-Steam detection;
- entitlement detection;
- Xbox Cloud Gaming detection;
- package install/uninstall listener;
- multi-game arbitration;
- multi-user/FUS/RDP support;
- universal game identity abstraction.

---

## 36. Acceptance criteria

PR2 code is complete when all are true:

1. XBOX Active Game Diagnostic is under Developer Menu.
2. Main UI has no game-detection authority.
3. Runtime owns hooks and candidate inspection.
4. hooks use FOREGROUND + CREATE + SHOW only.
5. object events require top-level OBJID_WINDOW / CHILDID_SELF observations.
6. callback is thin and queued.
7. no polling exists.
8. candidate process uses PROCESS_QUERY_LIMITED_INFORMATION.
9. running executable path comes from the live process.
10. PackageFullName/PFN come from the live process.
11. shared MicrosoftGame.config parser is reused.
12. Game and MicrosoftGame roots are handled consistently by that shared parser.
13. positive identity requires exact executable basename match.
14. running path and config path remain separate.
15. D:-running/C:-metadata is accepted.
16. official external package paths are diagnostic evidence only.
17. GamingServicesUI/PickerHost-type helpers cannot pass without exact executable evidence.
18. Alt+Tab does not clear active XBOX game.
19. matched process exit clears active state.
20. stale old process generation cannot mutate a newer PID reuse.
21. Start with already-running game performs one bounded reconcile.
22. resume uses bounded reconcile rather than polling if included.
23. frontend disconnect retires developer observer.
24. no profile/settings persistence is added.
25. no profile apply occurs.
26. Steam RunningAppID path is untouched.
27. Full1902 authority/presentation is untouched.
28. Overlay code/protocol is untouched.
29. Frontend protocol increments exactly once from implementation baseline.
30. focused tests pass.
31. full existing tests pass.
32. real target reports validate Minecraft and Aniimo before production XBOX-session/profile work starts.

---

## 37. Evidence gate before production integration

Do not proceed to production XBOX profile application from CI alone.

Collect real-machine evidence for:

### Minecraft

~~~text
Minecraft.Windows.exe
→ process package identity
→ Microsoft.MinecraftUWP PFN
→ MicrosoftGame.config
→ exact executable match
→ active
→ Alt+Tab retained
→ process exit cleared
~~~

### Aniimo

~~~text
actual D:-side Aniimo.exe process path
+
package/config identity metadata
+
exact executable match
→ active
→ process exit cleared
~~~

### Helper rejection

Capture launch helper traffic and verify no helper becomes active.

### Reconciliation

At least one must pass:

~~~text
Start diagnostic after game already running
or
Runtime restart while game remains running
~~~

Prefer sleep/resume validation before declaring PR2 field-proven.

---

## 38. Final implementation principle

PR2 validates one narrow chain:

~~~text
Windows lifecycle events
        ↓
persistent Runtime
        ↓
actual process identity
+ package identity
+ MicrosoftGame.config exact executable evidence
        ↓
diagnostic Active XBOX game
        ↓
real process lifetime
~~~

Existing architecture remains:

~~~text
Runtime
→ system / feature authority

Main App
→ disposable management/diagnostic UI through .Frontend

Overlay
→ independent Quick Settings process through .Overlay

Steam
→ existing RunningAppID authority unchanged

Full1902
→ controller ownership/presentation authority unchanged
~~~

Do not turn this PoC into a generic detector framework.

If field evidence contradicts the architecture, record it and revise the XBOX design before production profile behavior is added.
