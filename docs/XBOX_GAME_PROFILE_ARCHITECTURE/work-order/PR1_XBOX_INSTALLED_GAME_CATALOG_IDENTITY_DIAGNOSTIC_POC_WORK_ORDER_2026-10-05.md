# Work Order — XBOX PR1: Installed Game Catalog / Identity Diagnostic PoC

> **Date:** 2026-10-05  
> **Repository:** \`onehoon/SteamAddonforClaw\`  
> **Reviewed baseline:** \`main@34a895d9caf763e45eef367b0985ce5bc34a6a27\`  
> **Architecture authority:** \`docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md\`  
> **Full1902 authority:** \`docs/Full 1902 Implementation/README.md\` and its precedence chain  
> **UI/runtime boundary:** existing disposable Main UI + persistent Runtime + independent warm Overlay process  
> **Scope:** read-only Developer Menu diagnostic only  
> **Production behavior:** none  
> **XBOX active-game detection:** out of scope for this PR

---

## 1. Goal

Implement the first XBOX/Game Pass architecture validation as a **read-only Developer Menu diagnostic**.

The PR must answer one concrete question:

> Can the existing SteamAddonforClaw Runtime, in its real shipped Windows process/environment, enumerate the current user's installed packages and identify installed XBOX/Game Pass/GDK games from supported Windows package metadata plus \`MicrosoftGame.config\`, without elevation, polling, private Xbox databases, PowerShell, or WindowsApps ACL changes?

The diagnostic must produce enough evidence from real machines to decide whether the production XBOX catalog architecture is viable.

This PR is intentionally **not** the production XBOX catalog.

---

## 2. Architecture boundary — do not disturb the existing process model

The current process/lifetime separation is a hard requirement.

~~~text
SteamInputAddonforClaw.exe
→ persistent Runtime / tray / product authority
→ owns feature logic and system interrogation

SteamInputAddonforClaw.UI.exe
→ disposable Main App
→ renders Developer UI
→ calls Runtime only through existing .Frontend contract

SteamInputAddonforClaw.Overlay.exe
→ independent warm Overlay process
→ owns only Overlay rendering/lifecycle
→ uses .Overlay transport
~~~

PR1 must preserve this exact shape.

### Required ownership

~~~text
Developer UI button
        ↓
existing IAddonFrontendControl
        ↓
existing .Frontend named-pipe transport
        ↓
Runtime-owned XBOX catalog diagnostic
        ↓
Windows package APIs + MicrosoftGame.config
        ↓
typed diagnostic result
        ↓
Main UI renders result
~~~

### Forbidden architecture changes

Do **not**:

- run package enumeration directly inside \`SteamInputAddonforClaw.UI.exe\`;
- add any XBOX logic to \`SteamInputAddonforClaw.Overlay.exe\`;
- modify \`OverlayProcessController\`;
- modify \`OverlayWire\` / \`.Overlay\` protocol;
- introduce a new named pipe;
- introduce an XBOX helper process;
- create a Windows service;
- create a background watcher;
- create a package polling timer;
- add a new Runtime authority/manager abstraction unrelated to this one diagnostic operation.

The Main UI remains disposable.

The Overlay remains completely unaware that this diagnostic exists.

---

## 3. Full1902 safety boundary

This diagnostic must not participate in controller ownership.

Do not touch:

~~~text
Center M authority
PID1901 / PID1902
DirectInput
HidHide
VIIPER
Xbox360 publisher
SteamDeck publisher
Steam/BPM presentation selection
M1/M2 mapping
rumble
controller LEDs
controller vibration
suspend/resume controller recovery
physical-device PnP ownership
~~~

The scan must be safe regardless of whether Center M is Enabled or Disabled.

A package/API failure must never cause a controller reconcile or presentation change.

---

## 4. Product scope of PR1

### In scope

1. Add one Developer Menu entry:
   - **XBOX Catalog Diagnostic**
   - opens a dedicated child diagnostic page.

2. Add one Runtime-owned one-shot diagnostic operation.

3. Enumerate current-user installed packages using a supported Windows package API candidate.

4. For packages with accessible package locations:
   - locate/read \`MicrosoftGame.config\`;
   - parse the required/interesting identity fields;
   - classify credible GDK/XBOX game candidates.

5. Return a typed diagnostic snapshot to Main UI.

6. Render:
   - package enumeration summary;
   - valid game candidates;
   - identity evidence;
   - candidate canonical key;
   - per-package/config failure evidence where useful.

7. Emit concise structured logs.

8. Add focused unit/transport/UI architecture tests.

### Explicitly out of scope

Do not implement:

- top-level XBOX Main App tab;
- Profile → Steam navigation rename;
- \`ProfileDocument.XboxGames\`;
- XBOX profile persistence;
- XBOX profile mutations;
- CPU/TDP/Power/FPS/Resolution XBOX application;
- WinEvent hooks;
- active XBOX game detection;
- process lifetime tracking;
- sleep/resume XBOX session logic;
- XBOX Overlay projection;
- XBOX per-game M1/M2;
- WING/Center M \`XboxApp\` action;
- Game Bar integration;
- ClawHUD IPC/dependency;
- package install/update/uninstall monitoring;
- \`PackageCatalog\` background events;
- catalog caching;
- production XBOX game key migration policy.

---

## 5. Why this is a Runtime diagnostic, not a UI implementation

The PoC must validate the process/environment that will eventually own XBOX catalog discovery.

The production architecture requires Runtime authority and disposable frontends.

Therefore the package enumeration and config parsing must execute in the Runtime process even though the trigger originates from Developer Menu.

Wrong:

~~~text
DeveloperPage
→ new PackageManager()
→ scan package filesystem
→ render
~~~

Correct:

~~~text
DeveloperPage
→ RunXboxCatalogDiagnosticAsync()
→ .Frontend
→ Runtime diagnostic implementation
→ PackageManager / package metadata
→ MicrosoftGame.config parser
→ result
~~~

This also prevents PR1 from accidentally creating a second XBOX catalog implementation that would later need to be moved out of UI.

---

## 6. Diagnostic implementation shape

Keep the implementation narrow.

Suggested production-project location:

~~~text
src/SteamInputAddonforClaw/Diagnostics/XboxCatalog/
    XboxCatalogDiagnostic.cs
    MicrosoftGameConfigReader.cs
~~~

Exact filenames may vary.

Do not create:

~~~text
XboxCatalogManager
XboxPlatformService
XboxPackageRepository
XboxGameIdentityFramework
IGamePlatform
IGameCatalogProvider
UniversalGameIdentity
~~~

for this PoC.

A small class with one bounded scan method plus one parser is sufficient.

Conceptually:

~~~csharp
internal sealed class XboxCatalogDiagnostic
{
    public Task<FrontendXboxCatalogDiagnosticResult> RunAsync(
        CancellationToken cancellationToken);
}
~~~

If testability requires injection, inject only the narrow package-enumeration/config-read seam actually needed for tests.

Do not introduce a broad package-management abstraction.

---

## 7. Windows package enumeration — PoC contract

The architecture document identifies current-user package enumeration as the principal uncertainty.

Start with the supported API candidate:

~~~text
Windows.Management.Deployment.PackageManager.FindPackagesForUser("")
~~~

The diagnostic must record whether the API can be called successfully from the real Runtime process.

### Required evidence

Capture at least:

~~~text
Enumeration API succeeded?
Total packages returned
Packages with accessible installed/effective location
Packages with MicrosoftGame.config
Packages with parseable game config
Valid XBOX/GDK game candidates
Packages/configs skipped due access/read/parse failure
~~~

### No privilege escalation

The scan must not:

- request UAC;
- run elevated helper code;
- change package/file ACLs;
- take ownership of WindowsApps;
- grant the Addon extra filesystem permissions.

If supported APIs cannot expose enough information from the current process, report that result and end the PoC cleanly.

Do not work around it inside PR1.

### No fallback scraping in PR1

If \`PackageManager.FindPackagesForUser\` is blocked or incomplete, do **not** silently switch to:

- PowerShell;
- \`Get-AppxPackage\` subprocess;
- registry \`PackageRepository\` internals;
- Xbox app SQLite/private databases;
- GamingServices private files;
- recursive \`C:\\Program Files\\WindowsApps\` scans;
- broad fixed-drive scans.

The failure itself is valuable PoC evidence and should drive the next architecture decision.

---

## 8. Package location probing

Use only package paths exposed by supported package metadata/APIs.

The diagnostic may inspect the supported available package location concepts appropriate to the selected API.

The architecture research expects that install/effective locations may matter for games installed to different drives.

Do not assume:

~~~text
C:\Program Files\WindowsApps
~~~

is the only location.

Do not hard-code drive letters.

### Required field evidence

For every valid game candidate, record where the config was found conceptually:

~~~text
PackageFullName
PackageFamilyName
SelectedPackageLocationKind
SelectedPackageRoot
MicrosoftGameConfigPath
~~~

The UI may abbreviate the root path, but the diagnostic report/log should preserve the evidence needed to debug non-system-drive installs.

Do not expose or log unrelated user file contents.

---

## 9. MicrosoftGame.config parsing

The PoC must parse only the fields required to validate the planned identity model.

Capture:

~~~text
Identity
  Name
  Publisher
  ResourceId if present

StoreId if present
TitleId if present

ExecutableList
  Name
  Id if present
  TargetDeviceFamily if present
  Architecture if present
~~~

Recognize the expected Microsoft game config root forms already documented by the architecture/research.

Do not build a general-purpose GDK schema engine.

### Candidate validity for this diagnostic

A package may be reported as a valid game candidate when:

~~~text
MicrosoftGame.config is readable
+ recognized game config root
+ required Identity is present
+ at least one usable Executable Name is present
~~~

This is the **installed catalog PoC**.

Do not require a live-process executable exact match here; that belongs to the later active-game PR.

---

## 10. Candidate canonical key evidence

PR1 does not persist profiles.

It should still calculate and display the **candidate** key proposed by the architecture so real package data can validate it.

Use:

~~~text
StoreId present
→ store:<StoreId>

else PackageFamilyName present
→ pfn:<PackageFamilyName>

else
→ identity:<normalized Identity tuple>
~~~

Treat this key as diagnostic output only.

Do not:

- write it into \`profiles.json\`;
- create \`XboxGames\`;
- establish a migration contract;
- claim the fallback order is production-proven solely because the code compiles.

The field data from this PR decides whether the key policy should be simplified or revised before production persistence is implemented.

---

## 11. Frontend contract

Add one read-only diagnostic method to the existing desktop frontend contract.

Suggested shape:

~~~csharp
Task<FrontendXboxCatalogDiagnosticResult> RunXboxCatalogDiagnosticAsync(
    CancellationToken cancellationToken = default);
~~~

Suggested DTOs:

~~~csharp
public enum FrontendXboxCatalogDiagnosticOutcome
{
    Completed,
    Unavailable,
    Failed
}

public sealed record FrontendXboxCatalogDiagnosticResult(
    FrontendXboxCatalogDiagnosticOutcome Outcome,
    string Status,
    int EnumeratedPackageCount,
    int AccessiblePackageCount,
    int ConfigCandidateCount,
    int ValidGameCount,
    int SkippedOrFailedCount,
    IReadOnlyList<FrontendXboxCatalogDiagnosticGame> Games,
    IReadOnlyList<FrontendXboxCatalogDiagnosticFailure> Failures,
    string? ReportPath);

public sealed record FrontendXboxCatalogDiagnosticGame(
    string CandidateKey,
    string DisplayName,
    string PackageFullName,
    string PackageFamilyName,
    string? StoreId,
    string? TitleId,
    string IdentityName,
    string IdentityPublisher,
    string? IdentityResourceId,
    IReadOnlyList<string> Executables,
    string PackageLocationKind,
    string PackageRoot,
    string ConfigPath);

public sealed record FrontendXboxCatalogDiagnosticFailure(
    string Stage,
    string PackageIdentity,
    string Reason);
~~~

The exact shape may be trimmed if needed, but it must remain:

- typed;
- read-only;
- diagnostic-specific;
- independent of Steam profile DTOs.

Do not reuse \`FrontendProfileGameCatalogEntry\`.

Do not add fake numeric AppIDs.

### Failure list size

Do not return thousands of irrelevant non-game packages as UI rows.

The Runtime may collect aggregate rejection counts and return only actionable failures, such as:

- package enumeration failure;
- package location unavailable for a config candidate;
- config exists but cannot be read;
- config parse failure;
- config missing required Identity/ExecutableList.

Keep the response below the existing frontend frame-size limit.

---

## 12. Existing .Frontend transport only

Add one RPC to:

~~~text
IAddonFrontendControl
FrontendRpcMethod
NamedPipeAddonFrontendClient
NamedPipeAddonFrontendServer
InProcessAddonFrontendControl
~~~

Suggested method name:

~~~text
RunXboxCatalogDiagnostic
~~~

The reviewed baseline is:

~~~text
FrontendTransportProtocol.CurrentVersion = 49
~~~

Because this adds a new desktop frontend RPC contract, bump the frontend protocol exactly once:

~~~text
49 → 50
~~~

If \`main\` has advanced before implementation, increment the actual current version once instead of forcing 50.

### Overlay protocol

Do not change:

~~~text
OverlayTransportProtocol.CurrentVersion
OverlayWireMessageKind
NamedPipeOverlayClient
NamedPipeOverlayServer
OverlayProcessController
~~~

This diagnostic is Main UI / Runtime only.

---

## 13. Runtime wiring

The Runtime-side frontend control should own/invoke the diagnostic.

Prefer a narrow injected delegate or diagnostic instance following the current \`InProcessAddonFrontendControl\` composition style.

Conceptually:

~~~csharp
private readonly Func<CancellationToken, Task<FrontendXboxCatalogDiagnosticResult>>
    _runXboxCatalogDiagnostic;
~~~

with a default unavailable result in tests/compositions that do not provide the diagnostic.

Production \`AddonProcessHost\` composition should wire the real Runtime diagnostic.

Do not put package enumeration into:

~~~text
ProfileGameCatalogScanner
SteamSessionRuntime
AddonRuntimeHost
OverlayProcessController
~~~

PR1 is XBOX diagnostic evidence, not Steam profile behavior and not controller lifecycle.

---

## 14. Developer Menu UI

Add a Developer Menu card:

~~~text
XBOX Catalog Diagnostic
Read-only installed XBOX/Game Pass package and MicrosoftGame.config identity probe.

[Open]
~~~

Because the result can contain multiple games and long identity data, use a dedicated child page rather than expanding the main Developer Menu indefinitely.

Suggested files:

~~~text
src/SteamInputAddonforClaw.UI/Views/XboxCatalogDiagnosticPage.xaml
src/SteamInputAddonforClaw.UI/Views/XboxCatalogDiagnosticPage.xaml.cs
~~~

Add one child navigation state:

~~~text
MainNavigationPage.XboxCatalogDiagnostic
~~~

and follow the existing pattern used by:

- ClawSensorProbe;
- FanHardwareProbe;
- BatteryChargeLimitTest;
- VibrationTest.

### Child page behavior

Suggested page:

~~~text
Back to Developer Menu

XBOX Catalog Diagnostic
Read-only. No package or profile changes are performed.

[Scan Installed XBOX Games]

Status
  Completed / Failed / Unavailable
  Enumerated packages: N
  Config candidates: N
  Valid games: N
  Failures: N

Games
  <DisplayName>
    Candidate key
    StoreId
    TitleId
    PackageFamilyName
    Identity
    Executables
    Location kind
    Config path

[Open Report]    // only if ReportPath is produced
~~~

The Scan button performs exactly one scan.

Disable it while the request is in flight.

Do not auto-run repeatedly when the page remains visible.

It is acceptable to run once automatically on page activation **only if** the existing diagnostic-page UX consistently does so; explicit Scan is preferred because the goal is a deliberate field test.

---

## 15. Diagnostic report

A durable text report is useful because the field data will drive PR2/production design.

Prefer a Runtime-generated UTF-8 text report under the existing Addon log/diagnostic directory.

Suggested filename:

~~~text
xbox-catalog-diagnostic-YYYYMMDD-HHMMSS.txt
~~~

Report should contain:

~~~text
Timestamp
OS version/build
Addon version if readily available

Enumeration
  API path
  success/failure
  total packages
  accessible locations
  config candidates
  valid games

For each valid game
  display name
  candidate key
  package full name
  package family name
  package location type/path
  MicrosoftGame.config path
  Identity Name/Publisher/ResourceId
  StoreId
  TitleId
  executable list

Actionable failures
  package identity
  stage
  exception type / Win32/HRESULT where available
  concise message
~~~

Do not dump the full XML of every package config unless needed to diagnose a parser failure.

Do not log secrets/tokens.

---

## 16. Logging

Use a narrow category such as:

~~~text
XboxCatalogDiagnostic
~~~

Recommended events:

~~~text
scan requested
package enumeration completed
game candidate accepted
config candidate rejected
scan completed
scan failed
~~~

Examples:

~~~text
Xbox catalog diagnostic scan completed.
EnumeratedPackages=...
AccessiblePackages=...
ConfigCandidates=...
ValidGames=...
Failures=...
ReportPath=...
~~~

For accepted games, log identifiers needed for field comparison.

Do not emit one Info log for every ordinary non-game Store package.

Use Debug only for verbose rejection details if useful.

---

## 17. Cancellation and Main UI lifetime

The desktop frontend request already has a cancellation-aware transport.

Preserve that design.

If Main UI closes or request cancellation is signaled:

- stop the bounded scan where practical;
- do not leave a background scanning task intentionally running;
- do not create a persistent diagnostic session;
- do not require cleanup from Overlay or controller lifecycle.

No special disconnect manager is required for a one-shot read operation.

---

## 18. No polling / no watcher

PR1 must have no long-lived XBOX observer.

Forbidden:

~~~text
DispatcherTimer
System.Threading.Timer
PeriodicTimer
while + Delay
WMI WITHIN
PackageCatalog event subscription
filesystem watcher
process watcher
WinEvent hook
~~~

Those are not needed for installed catalog validation.

The only trigger is a user-requested one-shot scan through Developer Menu.

---

## 19. Tests

### 19.1 Config parser tests

Use synthetic XML fixtures.

Required cases:

1. valid config with StoreId + TitleId + one executable;
2. StoreId missing;
3. TitleId missing;
4. multiple executables;
5. optional executable attributes missing;
6. required Identity missing → rejected;
7. no usable executable name → rejected;
8. malformed XML → diagnostic parse failure, no crash;
9. supported alternate root form if current research/code recognizes both.

### 19.2 Candidate-key tests

1. StoreId wins.
2. no StoreId → PFN fallback.
3. no StoreId/PFN → Identity tuple fallback.
4. package version/full-name changes do not affect PFN-based candidate key.
5. display-name/path changes do not affect candidate key.

These tests validate the PoC output algorithm only; they do not make it a production persistence contract.

### 19.3 Scanner tests

Use an injectable fake package source / config file source only as narrowly required.

Verify:

1. zero packages → Completed with zero games.
2. non-game package with no config → ignored.
3. valid config package → one game.
4. one unreadable package does not abort remaining packages.
5. enumeration-source failure → Failed/Unavailable, no destructive behavior.
6. cancellation is observed.
7. duplicate observations do not emit duplicate game rows if the same package/canonical candidate is encountered.

### 19.4 Frontend tests

Verify:

1. \`IAddonFrontendControl.RunXboxCatalogDiagnosticAsync\` exists.
2. in-process control delegates exactly once.
3. unavailable fallback is deterministic when diagnostic is not wired.
4. named-pipe client/server round-trip the complete result.
5. malformed payload/request is rejected by existing protocol rules.
6. frontend protocol increments exactly once from the implementation baseline.

### 19.5 UI/navigation tests

Verify:

1. Developer Menu contains XBOX Catalog Diagnostic.
2. Open navigates to dedicated child page.
3. Back returns to Developer Menu.
4. mouse-back follows the same child-page return path.
5. Scan button disables while one scan is pending.
6. result rendering does not initiate another scan.
7. no XBOX top-level Main App tab is created in PR1.

### 19.6 Architecture/source guards

Add focused source tests if helpful to prove:

- no \`SteamInputAddonforClaw.Overlay\` project change is required;
- no \`OverlayWire\` XBOX diagnostic message exists;
- no timer/polling object is introduced into the new XBOX diagnostic;
- no \`ProfileDocument.XboxGames\` is introduced by PR1;
- no WinEvent active-game detection is introduced by PR1.

Do not add brittle whole-file snapshots merely to police style.

---

## 20. Manual validation matrix

Run on the target MSI Claw/Windows environment.

### Case A — normal scan

Developer Menu:

~~~text
XBOX Catalog Diagnostic
→ Scan Installed XBOX Games
~~~

Expected:

- no UAC;
- no Windows security prompt;
- Main UI remains responsive;
- Runtime/controller continues normally;
- scan completes once;
- report is generated;
- installed XBOX/Game Pass games appear with identity evidence.

### Case B — system-drive Game Pass title

Record:

- DisplayName;
- PFN;
- StoreId;
- TitleId;
- Identity;
- Executables;
- location kind/path;
- config readability;
- candidate key.

### Case C — non-system-drive Game Pass title

If available, install/use a game on another Xbox library drive.

Expected:

- the supported package API still exposes the usable package/config location;
- no hard-coded C: path assumption.

### Case D — Microsoft Store non-game packages

Expected:

- they do not appear as valid XBOX game candidates merely because they are packaged apps.

### Case E — repeat scan

Run the diagnostic multiple times manually.

Expected:

- no persistent watcher;
- no accumulating resources;
- same installed-state evidence is stable;
- each request is bounded and independent.

### Case F — Main UI close during/after scan

Expected:

- Runtime survives;
- Overlay survives;
- controller ownership unaffected;
- no leaked persistent diagnostic session.

### Case G — Center M Disabled / Full1902 active

Run while Addon owns PID1902.

Expected:

- controller input/presentation continues unchanged;
- no X360/SteamDeck switch caused by scan;
- no HidHide/PID/VIIPER log activity attributable to the diagnostic.

---

## 21. Evidence required before PR1 is considered successful

The PR code/CI passing is not enough.

Collect at least one real diagnostic report from the target machine and verify:

1. supported API call succeeds from the Runtime process;
2. actual Game Pass games are enumerated;
3. \`MicrosoftGame.config\` is readable;
4. Identity is populated;
5. executable list is populated;
6. StoreId presence/absence is known;
7. TitleId presence/absence is known;
8. PFN is available where expected;
9. non-system-drive behavior is known if such a game is available;
10. Microsoft Store non-game packages are not falsely classified;
11. no elevation/ACL workaround was required.

Prefer reports from multiple titles.

At least one first-party and one third-party Game Pass title should be tested before freezing the production catalog design, where available.

---

## 22. Stop conditions

Stop and report evidence instead of adding workarounds if any of these occur:

### Package enumeration blocked

~~~text
FindPackagesForUser unavailable/denied
~~~

Do not immediately add PowerShell or registry scraping.

### Package paths inaccessible

If supported package metadata cannot expose a readable game/config root without ACL changes, stop.

### MicrosoftGame.config absent for expected Game Pass titles

Record exact package/title evidence and stop before changing classification rules.

### Identity fields materially differ from architecture assumptions

Do not force data into the proposed key model.

The purpose of this PR is to learn the real shape.

Update the architecture after field evidence, then prepare the production catalog PR.

---

## 23. Files expected to change

Expected areas only:

~~~text
src/SteamInputAddonforClaw/
    Diagnostics/XboxCatalog/*

src/SteamInputAddonforClaw.Contracts/Frontend/
    FrontendContracts.cs

src/SteamInputAddonforClaw.FrontendTransport/
    FrontendWire.cs
    NamedPipeAddonFrontendClient.cs
    NamedPipeAddonFrontendServer.cs

src/SteamInputAddonforClaw/Frontend/
    InProcessAddonFrontendControl.cs

src/SteamInputAddonforClaw/Hosting/
    AddonProcessHost.cs

src/SteamInputAddonforClaw.UI/
    MainNavigationState.cs
    MainWindow.xaml
    MainWindow.xaml.cs
    Views/DeveloperPage.xaml
    Views/DeveloperPage.xaml.cs
    Views/XboxCatalogDiagnosticPage.xaml
    Views/XboxCatalogDiagnosticPage.xaml.cs

tests/SteamInputAddonforClaw.Tests/
tests/SteamInputAddonforClaw.UiTests/
~~~

Depending on the exact Windows Runtime API projection already referenced by the solution, a project/package reference may be required.

Keep any such dependency change minimal and explain why it is needed.

### Files that should not need changes

~~~text
src/SteamInputAddonforClaw.Overlay/*
src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire*
Profiles/ProfileDocument.cs
Profiles/GameProfile.cs
Profiles/Performance/*
Contracts/BackButtons/*
Devices/MSI/Claw/*
Steam/*
VIIPER/*
HidHide/*
~~~

A change in those areas requires a concrete explanation because it is outside PR1's diagnostic responsibility.

---

## 24. Acceptance criteria

PR1 is complete when all are true:

1. Developer Menu has an XBOX Catalog Diagnostic entry.
2. It opens a dedicated child diagnostic page.
3. The Main UI never enumerates packages directly.
4. One explicit Scan action crosses the existing \`.Frontend\` transport.
5. The Runtime performs the scan.
6. The Runtime uses a supported current-user package API candidate.
7. No UAC/elevation is requested.
8. No PowerShell/package DB/registry-private fallback is added.
9. No WindowsApps ACL change is performed.
10. \`MicrosoftGame.config\` is parsed for Identity, StoreId, TitleId, and ExecutableList.
11. Valid candidates require config + required Identity + usable executable data.
12. Candidate canonical key is displayed only as diagnostic evidence.
13. No XBOX profile data is persisted.
14. No active-game watcher or WinEvent hook exists.
15. No polling/timer exists.
16. Steam profile/catalog behavior is unchanged.
17. Full1902 controller authority is unchanged.
18. Overlay project/process/transport behavior is unchanged.
19. Frontend protocol is bumped once for the new desktop RPC.
20. Focused unit, transport, and UI tests pass.
21. Full existing test suite passes.
22. Real-machine diagnostic report is produced and reviewed before production XBOX catalog work begins.

---

## 25. Final implementation principle

This PR is an **evidence-gathering Runtime diagnostic**, not the beginning of a second frontend architecture.

Keep the existing ownership model:

~~~text
Runtime
→ performs Windows/package/GDK inspection

Main UI
→ requests and renders through .Frontend

Overlay
→ unchanged
~~~

If the supported package API works, later PRs can promote the proven scanner into the production XBOX catalog.

If it does not work, the failure is the result of PR1.

Do not hide that result behind architectural workarounds.
