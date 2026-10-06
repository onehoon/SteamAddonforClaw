# Steam Addon for Claw — XBOX Game Detection, Catalog, and Profile Architecture

> **Date:** 2026-10-05  
> **Status:** Architecture authority for the XBOX game/profile feature family  
> **Field-validation status (2026-10-05):** PoC A installed catalog = PASS; PoC B event-driven active-game identity/lifecycle = PASS; PoC C Xbox app activation = pending  
> **Product baseline:** Standalone Full1902 SteamAddonforClaw  
> **Scope:** XBOX/Game Pass installed-game catalog, event-driven active-game detection, XBOX-specific per-game profiles, per-game M1/M2 mapping, Main App navigation, Overlay projection, and front-button Xbox app launch  
> **Out of scope:** Xbox Game Bar integration, ClawHUD IPC/dependency, Steam profile redesign, Full1902 physical controller ownership redesign

---

## 1. Purpose

SteamAddonforClaw already has a clear Steam game identity:

~~~text
Steam RunningAppID
→ Steam AppID
→ existing Steam game catalog/profile
~~~

The XBOX/Game Pass side needs a separate product path with the same user-facing outcome:

~~~text
installed XBOX game
→ preconfigure a game profile before launch

running XBOX game
→ identify the exact game
→ apply that game's profile
→ use the same existing device-performance apply implementations
→ apply per-game M1/M2 mapping while Xbox360 presentation is active
~~~

This architecture deliberately does **not** try to invent one universal Steam/XBOX game-ID system.

The final product model is:

~~~text
Steam domain
    Steam RunningAppID
    Steam catalog
    Steam profile persistence
    Steam Main App page

XBOX domain
    Windows package / GDK identity
    XBOX catalog
    XBOX profile persistence
    XBOX Main App page

shared lower-level apply mechanisms
    CPU Boost
    TDP
    Windows Power Mode
    Intel FPS Limit
    Display Resolution
    Xbox360 M1/M2 software mapping
~~~

The domains are separate at identity, catalog, persistence, frontend-contract, and Main App UI level.

They converge only where one physical machine-wide setting must actually be applied.

---

## 2. Locked product decisions

The following are architecture decisions, not implementation options.

### 2.1 Xbox Game Bar remains disabled

SteamAddonforClaw's existing Full1902 direction suppresses Xbox Game Bar / Win+G behavior while Addon controller authority is active.

Do not add Game Bar back as an XBOX game-detection dependency.

Do not use:

- XboxGameBarAppTargetTracker;
- Game Bar widgets;
- Win+G activation;
- a hidden Game Bar helper solely for title identity.

The XBOX feature must work with Game Bar absent/disabled.

### 2.2 ClawHUD is reference material only

ClawHUD contains useful field-proven Windows/XBOX identity work:

~~~text
WindowsGameIdentityProbe
MicrosoftGameTrigger
ProductionGameWindowSource
~~~

That implementation demonstrates that Windows process package identity plus readable MicrosoftGame.config and exact executable matching can identify Microsoft/Xbox titles in real use.

SteamAddonforClaw may reuse the **technical findings and Windows API pattern**.

It must not add:

- ClawHUD IPC for game identity;
- a ClawHUD runtime dependency;
- duplicated ClawHUD generic game-verification architecture;
- PresentMon-based game detection.

The Addon has a narrower problem because it only needs XBOX/Game Pass catalog members, not universal game classification.

### 2.3 Steam remains untouched as a separate domain

Existing Steam authority remains:

~~~text
SteamSessionRuntime.ActualRunningAppId
→ existing Steam profile/catalog/frontend paths
~~~

Do not replace Steam AppID with a generalized string identity merely to support XBOX.

Do not rewrite the stable Steam detector.

### 2.4 Main App pages are fully separate

Current user-facing top-level Main App navigation changes from:

~~~text
Device
Controller
Profile
Overlay
Shortcut
How to Use
Settings
~~~

to:

~~~text
Device
Controller
Steam
XBOX
Overlay
Shortcut
How to Use
Settings
~~~

The current Profile page becomes the user-facing **Steam** page.

The new **XBOX** page is a separate top-level page.

Do not implement:

~~~text
Profile
  [Steam] [XBOX]
~~~

or another combined page/sub-tab model.

### 2.5 No production polling

XBOX active-game detection must be event-driven.

Do not introduce:

- periodic GetForegroundWindow checks;
- repeated EnumWindows;
- process-list polling;
- WMI WITHIN polling;
- timer-based package scans;
- a background "is Xbox game running?" loop.

Bounded one-shot reconciliation at real lifecycle boundaries is allowed and required where necessary:

- Runtime startup;
- controlled Runtime restart;
- resume;
- explicit XBOX catalog refresh.

A one-shot reconciliation is not a polling architecture.

---

## 3. Relationship to Full1902

This document does not change Full1902 controller authority.

Current controller contract remains:

~~~text
Center M Enabled
→ MSI / stock controller authority

Center M Disabled
→ Addon Runtime authority
→ desired physical PID1902
→ DirectInput Addon-owned
→ HidHide Addon baseline
→ VIIPER Addon-owned
~~~

Current presentation authority also remains:

~~~text
Steam/BPM inactive
→ Xbox360 presentation

Steam/BPM active
→ SteamDeck presentation
~~~

XBOX game detection must **not** become another presentation authority.

Specifically:

~~~text
XBOX game detected
≠ force Xbox360
≠ detach SteamDeck
≠ change PID1902 ownership
≠ change HidHide
~~~

In normal supported use, an XBOX game is launched while Steam/BPM is inactive, so Xbox360 is already the presentation.

If a Steam/BPM session is simultaneously active, existing presentation policy remains authoritative. Do not add Steam-vs-XBOX presentation arbitration merely for an unsupported simultaneous-game scenario.

---

## 4. Current Addon baseline that must be preserved

The existing profile implementation is explicitly Steam-oriented.

Current persistence:

~~~text
ProfileDocument.Games
Dictionary<string, GameProfile>
key = numeric Steam AppID serialized as string
~~~

Current frontend contracts also use numeric Steam AppID:

~~~text
FrontendGameProfileSnapshot(uint AppId, ...)
FrontendProfileGameCatalogEntry(uint AppId, ...)
ScanProfileGamesAsync()
CaptureActiveGameProfileAsync()
~~~

Current performance runtimes consume actual Steam AppID and directly resolve ProfileDocument.Games.

Examples include:

- CpuBoostRuntime;
- Tdp runtime;
- PowerModeRuntime;
- IntelFrameLimiterRuntime;
- GameDisplayResolutionRuntime.

Current M1/M2 implementation is one global Xbox360 mapping:

~~~text
BackButtonMappingSettings
→ CanonicalXbox360InputPublisher
~~~

The XBOX architecture should extend this baseline without destabilizing the existing Steam path.

---

## 5. High-level target architecture

~~~text
                         MAIN APP
              ┌─────────────────────────┐
              │ Steam        XBOX       │
              │ page         page       │
              └──────┬─────────┬────────┘
                     │         │
          Steam catalog         XBOX catalog
          Steam profiles        XBOX profiles
                     │         │
                     └────┬────┘
                          │
                 Runtime profile selector
                 (derived fact only)
                          │
          ┌───────────────┼────────────────┐
          │               │                │
          v               v                v
      CPU/TDP/         Display          Xbox360
      Power/FPS        Resolution       M1/M2 mapping
          │               │                │
          └───────────────┴────────────────┘
                          │
                     hardware/OS


XBOX active-game identity:

WinEvent
   │
   v
top-level HWND / PID
   │
   v
process package identity
   │
   v
MicrosoftGame.config
   │
   ├─ Identity
   ├─ StoreId (optional)
   ├─ TitleId (optional)
   └─ ExecutableList
   │
   v
exact current executable match
   │
   v
ActiveXboxGame
~~~

---

## 6. XBOX identity evidence

### 6.1 Strong runtime evidence

A process is eligible to become an Addon XBOX game only when all required evidence succeeds.

Required runtime evidence:

~~~text
process can be opened for limited query
+ executable path/name resolved
+ package identity resolved
+ MicrosoftGame.config found and readable
+ current executable exactly matches one ExecutableList entry
~~~

This deliberately rejects helpers such as:

- GamingServicesUI;
- PickerHost;
- unrelated Xbox app UI processes;
- shell processes;
- arbitrary full-screen processes.

Do not use:

- window title similarity;
- full-screen geometry;
- GPU usage;
- PresentMon;
- executable-name fuzzy matching;
- "looks like a game" scoring.

The Addon is not a universal game detector.

### 6.2 Runtime Windows APIs

The process-side probe may use the same Windows API family already validated by ClawHUD research:

~~~text
OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)
QueryFullProcessImageName
GetApplicationUserModelId
GetPackageFullName
GetPackageFamilyName
GetPackageId
OpenPackageInfoByFullName / package metadata APIs as needed
~~~

The implementation should be C# / CsWin32 / P/Invoke / WinRT according to the current Addon conventions.

Do not copy the ClawHUD C++ source wholesale.

### 6.3 MicrosoftGame.config facts

The GDK config is valuable because:

- Identity is required;
- ExecutableList is required;
- every Executable requires Name;
- StoreId is a Partner Center store identity when present;
- TitleId is an Xbox Live title identity when present.

StoreId and TitleId are both optional in the schema, so neither may be the only fallback-free identity key.

---

## 7. Canonical XBOX profile key

The persisted XBOX profile key must survive:

- normal game updates;
- package version changes;
- executable process-ID changes;
- installation path changes.

It must not depend on:

- PID;
- package full name including version;
- install path;
- display name;
- current HWND.

Recommended canonical key selection:

~~~text
1. valid StoreId
   → "store:<StoreId>"

else

2. PackageFamilyName
   → "pfn:<PackageFamilyName>"

else

3. MicrosoftGame.config required Identity
   Name + Publisher + ResourceId
   → "identity:<normalized identity tuple>"
~~~

TitleId should be retained as metadata and evidence but should not be the primary fallback by itself because:

- it is optional;
- development/default values may exist;
- Store/package registration identity is better suited to installed PC title persistence.

The profile entry should persist descriptive metadata separately so later diagnostics can explain how the key was derived.

Conceptual model:

~~~csharp
internal sealed record XboxGameIdentity(
    string Key,
    string DisplayName,
    string? StoreId,
    string? TitleId,
    string? PackageFamilyName,
    string IdentityName,
    string IdentityPublisher,
    string? IdentityResourceId,
    IReadOnlyList<string> Executables);
~~~

The exact public/frontend DTO may be narrower.

---

## 8. Installed XBOX game catalog

### 8.1 Product behavior

The Main App XBOX page must show installed XBOX/Game Pass games so the user can configure profiles before launch.

Required flow:

~~~text
open / refresh XBOX page
→ one bounded installed-package scan
→ identify packages with readable MicrosoftGame.config
→ require at least one valid title executable
→ build XBOX game catalog
→ merge Favorite/profile metadata
→ render
~~~

This is an on-demand scan, not periodic monitoring.

### 8.2 Production filtering

The scanner should include a package only when the package is a credible Microsoft/GDK game.

Minimum candidate rule:

~~~text
package installation/effective location available
+ MicrosoftGame.config readable
+ recognized Game root
+ required Identity available
+ ExecutableList contains at least one usable executable
~~~

Do not classify every Microsoft Store package as an XBOX game.

### 8.3 Enumeration API PoC gate

Installed-package enumeration was the main API uncertainty and has now been field-validated on the target MSI Claw / Windows environment before production implementation is frozen.

Microsoft exposes:

~~~text
Windows.Management.Deployment.PackageManager.FindPackagesForUser("")
~~~

for current-user package enumeration.

Microsoft also exposes:

~~~text
PackageCatalog.OpenForCurrentUser()
~~~

for current-user package catalog events.

However, the current SteamAddonforClaw main UI/runtime distribution is unpackaged/framework-dependent in parts of the product, and PackageManager documentation lists packageQuery capability requirements.

Therefore architecture policy is:

> **Do not design around undocumented registry scraping or PowerShell just to avoid validating the supported Windows API.**

The first XBOX catalog PoC must answer:

1. Can the shipped Addon runtime call the selected current-user package enumeration API without elevation?
2. Are installed Game Pass/GDK packages returned?
3. Are the effective/install paths readable enough to locate MicrosoftGame.config?
4. Does the approach work for the actual supported Windows FSE build and normal desktop session?
5. Does it work without taking ownership of WindowsApps or changing ACLs?

If the answer is no, stop and redesign catalog enumeration using another documented Windows/package API path.

Do not silently fall back to:

- PowerShell child processes;
- scraping undocumented Xbox app databases;
- recursive whole-disk scans;
- WindowsApps ACL takeover;
- registry PackageRepository internals.

### 8.4 Catalog refresh policy

Initial production policy should remain simple:

~~~text
XBOX page activation / explicit Refresh
→ bounded rescan
~~~

Do not add a permanent PackageCatalog listener merely because it exists.

A PackageCatalog install/update/uninstall event source may be added later if real UX testing shows stale catalog state while the Main App remains open is a meaningful problem.

This keeps the initial feature event-driven where it matters — active game detection — without adding another persistent authority for a non-critical list.

### 8.5 Uninstalled profiles

Removing a game must not automatically delete its saved XBOX profile.

Reason:

- reinstall should preserve user settings;
- catalog state and user profile state are different domains.

Initial XBOX page may show only currently installed titles.

A future "saved but not installed" section is optional and not required for first production support.

---

## 9. Event-driven active XBOX game detection

### 9.1 Owner

Create one narrow runtime owner, conceptually:

~~~text
XboxGameSessionRuntime
~~~

Responsibilities:

- own the WinEvent hooks;
- inspect only candidate PIDs raised by those events;
- publish the current ActiveXboxGame fact;
- monitor the matched process lifetime;
- perform bounded startup/resume reconciliation.

It must not own:

- Steam;
- profile persistence;
- performance setting application;
- controller presentation;
- UI;
- Game Bar;
- ClawHUD.

Do not split this into separate watcher/manager/coordinator abstractions unless implementation evidence requires it.

### 9.2 Event sources

Recommended minimal WinEvent inputs:

~~~text
EVENT_SYSTEM_FOREGROUND
EVENT_OBJECT_CREATE
EVENT_OBJECT_SHOW
~~~

For object events require:

~~~text
idObject == OBJID_WINDOW
idChild == CHILDID_SELF
top-level root == HWND
PID != 0
~~~

The callback must remain thin:

~~~text
WinEvent callback
→ capture HWND/PID/event
→ post/queue to Runtime-owned serialized execution context
→ return
~~~

Do not parse package metadata in the WinEvent callback.

### 9.3 Candidate evaluation

For each relevant candidate PID:

~~~text
resolve process generation / process handle
→ ignore already-negative/positive same generation when safely cached
→ inspect package identity
→ inspect MicrosoftGame.config
→ exact executable match?
    NO  → ignore
    YES → derive XboxGameIdentity
          publish/activate XBOX game session
~~~

Negative caching should be bounded to the process generation only.

Do not create a global executable blacklist as the primary classifier.

### 9.4 Process lifetime

Once a matching XBOX game process becomes active:

~~~text
open/retain process handle
→ register wait / Process.Exited equivalent
→ no polling
~~~

On process exit:

~~~text
matching active process exits
→ clear ActiveXboxGame
→ reconcile effective profile
→ restore Device/global policy through existing runtimes
~~~

If a title legitimately exposes multiple matched executables under the same canonical XBOX key, the implementation may retain a small set of matching live process generations for that same key and clear the session when the last one exits.

Do not turn this into a general process graph.

### 9.5 Startup / controlled restart reconciliation

A WinEvent source only sees future events.

A Runtime may start while an XBOX game is already running.

Therefore, after hooks are installed:

~~~text
one bounded reconciliation
→ inspect current foreground PID
→ optionally enumerate current top-level windows once if required to recover a background live XBOX session
→ apply the same exact identity probe
~~~

A one-shot EnumWindows call at this lifecycle boundary is permitted.

Do not repeat it on a timer.

### 9.6 Resume

Before suspend:

- do not alter XBOX profile persistence;
- do not invent a session-release state;
- normal Full1902 output neutralization remains owned by controller lifecycle.

After resume:

~~~text
invalidate stale process/window observations
→ one bounded XBOX session reconcile
→ exact identity evidence only
→ re-publish current XBOX target or None
~~~

Do not assume pre-suspend HWND/process handles remain valid.

### 9.7 Detection failure policy

If identity cannot be proven:

~~~text
ActiveXboxGame = None
~~~

Then:

- do not guess a profile;
- keep/apply Device baseline;
- use global M1/M2 mapping;
- log the reason at an appropriate level.

Failing to detect one XBOX title must not affect:

- Steam detection;
- Full1902 controller ownership;
- X360/SteamDeck presentation;
- Main App startup.

---

## 10. XBOX profile persistence

### 10.1 Separate persisted domain

Keep current Steam storage unchanged:

~~~text
ProfileDocument.Games
→ Steam AppID profiles
~~~

Add a sibling XBOX collection:

~~~csharp
public Dictionary<string, XboxGameProfile> XboxGames { get; init; } = [];
~~~

Conceptual JSON:

~~~json
{
  "schemaVersion": 1,
  "device": { },
  "games": {
    "553850": { }
  },
  "xboxGames": {
    "store:9XXXXXXXXXXX": { }
  }
}
~~~

The exact schema-version decision must follow current ProfileStore rules.

Current ProfileDocument explicitly states that purely additive fields do not automatically require a schema bump.

If XboxGames is additive and old builds preserve unknown extension data safely, do not bump schema merely for aesthetics.

### 10.2 Separate profile type

Do not reuse GameProfile directly if that keeps Steam-specific semantics/comments attached to the type.

Recommended:

~~~csharp
public sealed record XboxGameProfile
{
    public bool Enabled { get; init; }
    public bool Favorite { get; init; }
    public string? DisplayName { get; init; }

    public XboxGameProfileIdentityMetadata Identity { get; init; } = new();

    public GamePerformanceOverrides Performance { get; init; } = new();
    public GameDisplayOverrides Display { get; init; } = new();
    public XboxGameControllerOverrides Controller { get; init; } = new();
}
~~~

Reuse existing nested performance/display models where their semantics are genuinely identical.

Do not duplicate:

- GameTdpSettings;
- GameCpuBoostSettings;
- GamePowerModeSettings;
- GameFpsLimitSettings;
- GameDisplayResolution.

### 10.3 Controller override

XBOX profiles may add:

~~~csharp
public sealed record XboxGameControllerOverrides
{
    public BackButtonMappingSettings? BackButtonMapping { get; init; }
}
~~~

Recommended semantics:

~~~text
Xbox profile disabled
→ global BackButtonMappingSettings

Xbox profile enabled
+ Controller.BackButtonMapping == null
→ global BackButtonMappingSettings

Xbox profile enabled
+ Controller.BackButtonMapping != null
→ per-game XBOX M1/M2 mapping
~~~

This prevents enabling a TDP-only profile from accidentally freezing a copy of the user's current global M1/M2 mapping.

The Main App may expose a simple "Use global mapping" state for M1/M2 rather than inventing a separate general override framework.

---

## 11. Runtime profile selection

### 11.1 Why one derived selector is needed

Machine-wide settings such as TDP and CPU Boost can have only one effective target at a time.

Today those runtimes directly accept Steam AppID.

Creating separate:

~~~text
XboxCpuBoostRuntime
XboxTdpRuntime
XboxPowerModeRuntime
XboxFpsRuntime
~~~

would duplicate authority and failure handling.

Instead, keep separate platform detection and derive one narrow runtime target for shared apply code.

Conceptual value:

~~~csharp
internal enum ActiveProfileTargetKind
{
    None,
    Steam,
    Xbox
}

internal readonly record struct ActiveProfileTarget(
    ActiveProfileTargetKind Kind,
    uint SteamAppId,
    string? XboxGameKey);
~~~

This is **not** a persisted game-identity framework.

It is only the final selector passed to machine-wide profile apply code.

### 11.2 Selection policy

Normal supported product flow:

~~~text
Steam RunningAppID != 0
→ Steam target

else if ActiveXboxGame != null
→ XBOX target

else
→ None / Device baseline
~~~

Do not add complex multi-game arbitration.

Simultaneous live Steam game + live XBOX game is not a supported gameplay scenario in the initial architecture.

If field testing proves this occurs in normal use and causes a real user-visible failure, define one explicit policy then.

Do not build an epoch/priority state machine preemptively.

### 11.3 Shared lookup helper

Current apply runtimes should not each reimplement:

~~~text
if Steam → document.Games
if XBOX → document.XboxGames
~~~

Create one small profile lookup/policy helper returning the relevant enabled profile sections for the derived ActiveProfileTarget.

The helper should remain narrow.

Do not introduce:

- IGameIdentity;
- IGameProfileProvider hierarchies;
- plugin-style profile backends;
- generic platform registries.

Two known domains do not justify a framework.

---

## 12. Existing performance-runtime reuse

The final apply sequence should be:

~~~text
Steam event / XBOX event / power-source event / profile mutation
→ compute current ActiveProfileTarget
→ existing ProfileStore + mutation gate
→ resolve enabled profile
→ existing apply implementation
~~~

Reuse current ownership/recovery behavior in:

- CPU Boost;
- TDP;
- Windows Power Mode;
- Intel FPS Limit;
- Display Resolution.

The required refactor is to remove the assumption that every game target is uint Steam AppID.

Do not rewrite the underlying Windows/EC/IGCL implementation.

### Logging

Current logs often use RunningAppID.

As runtimes become platform-neutral at the apply boundary, log a stable target label instead:

~~~text
ProfileTarget=Steam:553850
ProfileTarget=Xbox:store:9XXXXXXXXXXX
ProfileTarget=None
~~~

Do not remove useful native failure details.

---

## 13. Per-game M1/M2 behavior

Current Full1902 mapping path:

~~~text
global BackButtonMappingSettings
→ Xbox360 publisher
~~~

Target behavior:

~~~text
Active presentation != Xbox360
→ XBOX per-game mapping has no Xbox360 output effect

Active presentation == Xbox360
+ active XBOX profile has explicit mapping
→ use XBOX profile M1/M2 mapping

otherwise
→ use global BackButtonMappingSettings
~~~

The provider consumed by CanonicalXbox360InputPublisher should resolve the effective mapping at publish time or at the existing safe mapping-refresh boundary.

Do not:

- create a second Xbox360 publisher;
- modify physical PID1902 ownership;
- write MSI firmware M1/M2 slots;
- map XBOX profiles into SteamDeck rear buttons;
- bypass the existing rear-button suppression/transition safety path.

Per-game XBOX M1/M2 is software output policy only.

---

## 14. Main App XBOX page

### 14.1 Navigation

User-facing Main App navigation becomes:

~~~text
Device
Controller
Steam
XBOX
Overlay
Shortcut
How to Use
Settings
~~~

The existing NavigationView item's visible Content changes from Profile to Steam.

A separate XBOX NavigationViewItem/Page is added.

### 14.2 Steam page

The existing Steam profile page retains:

- current installed Steam catalog;
- favorites;
- Steam AppID identity;
- existing performance/profile mutations.

Do not add XBOX entries to ScanProfileGamesAsync.

Do not change FrontendProfileGameCatalogEntry into a multi-platform record simply for the new page.

Internal class/file renaming from ProfilePage to SteamProfilePage is optional cleanup, not a prerequisite.

Avoid a large rename-only diff if it adds risk without product value.

### 14.3 XBOX page

The XBOX page owns:

- installed XBOX game catalog;
- XBOX Favorites;
- XBOX profile Enabled;
- CPU Boost;
- TDP;
- Windows Power Mode;
- Intel FPS Limit;
- Resolution;
- M1;
- M2.

Suggested layout should mirror the mature Steam page where controls are semantically identical.

Do not create a novel XBOX visual system.

Where XBOX has additional controls, add a Controller section for M1/M2.

### 14.4 Offline/pre-launch editing

Selecting an installed game from the XBOX catalog must allow profile edits when the game is not running.

This is a core requirement.

The catalog key and persisted profile key are therefore independent of PID/process lifetime.

---

## 15. Separate frontend contracts

Keep Steam contracts stable.

Add XBOX-specific contracts.

Conceptual examples:

For the first read-only production catalog surface, keep the catalog DTO intentionally narrow:

~~~csharp
public sealed record FrontendXboxGameCatalogEntry(
    string Key,
    string DisplayName);
~~~

`Key` is internal transport/application identity only. The normal Main App catalog renders and searches `DisplayName` only.

Do not expose normal-user catalog fields for:

- StoreId;
- TitleId;
- PackageFamilyName / PFN;
- PackageFullName;
- MicrosoftGame.config path;
- ExecutableList;
- AUMID or process/path evidence.

Those remain Runtime identity/persistence metadata.

Later profile persistence may extend the XBOX frontend surface with user-facing profile state such as Favorite, but technical platform identity still remains hidden from the normal UI.

Conceptual future profile example:

~~~csharp
public sealed record FrontendXboxGameProfileSnapshot(
    string Key,
    string? DisplayName,
    bool Exists,
    bool Enabled,
    FrontendGameCpuBoostConfiguration CpuBoost,
    FrontendGameTdpConfiguration Tdp,
    FrontendGamePowerModeConfiguration PowerMode,
    FrontendGameFpsLimitConfiguration FpsLimit,
    FrontendGameResolution? Resolution,
    FrontendXboxBackButtonConfiguration BackButtons,
    bool PersistenceWritable);
~~~

Methods should likewise remain separate:

~~~text
ScanXboxGamesAsync
CaptureXboxGameProfileAsync
CaptureActiveXboxGameProfileAsync
SetXboxGameProfile...
~~~

Do not overload Steam methods with "AppId=0 means Xbox" or encode a string key into uint.

Do not introduce one giant universal profile DTO unless a later measured maintenance problem justifies it.

---

## 16. Overlay behavior

### 16.1 One Profile surface, not two platform tabs

The Main App has separate Steam and XBOX pages.

The Overlay should **not** gain separate Steam and XBOX tabs.

Overlay remains context-driven.

When a game is active:

~~~text
active Steam game
→ Overlay Profile shows only that Steam game's profile

active XBOX game
→ Overlay Profile shows only that XBOX game's profile

no active recognized game
→ no active-game profile detail
~~~

The Overlay should not ask the user which platform is active.

Runtime already owns that fact.

### 16.2 Current overlay catalog behavior

The XBOX feature does not require adding an XBOX installed-game catalog to Overlay.

Pre-launch/offline profile editing belongs in Main App Steam/XBOX pages.

If the existing out-of-game Steam catalog remains temporarily in Overlay, it must not interfere with active XBOX projection.

A later UX cleanup may simplify Overlay to active-game-only, but that is not required to implement XBOX detection.

### 16.3 Transport

Current Overlay profile transport uses uint AppId.

Do not encode XBOX key into uint.

Prefer an explicit active-profile projection contract for Overlay, for example:

~~~text
Platform = Steam | Xbox
TargetId = string representation
QuickSettingsPageSnapshot = rendered active page
~~~

or keep platform-specific capture inside Runtime and send only the already-rendered generic Quick Settings page.

The latter is preferable when existing generic Quick Settings projection can carry all XBOX fields except M1/M2.

Do not make Overlay own profile-store lookup.

---

## 17. WING / Center M Xbox app action

Current FrontButtonAction includes Steam and generic launch actions.

Add one explicit user-facing action:

~~~text
Xbox
~~~

Internal enum name may be:

~~~text
XboxApp
~~~

Recommended capability:

~~~text
Normal domain → allowed
Steam domain  → not required initially
~~~

Reason:

- Normal corresponds to Xbox360/non-Steam presentation;
- launching Xbox from Steam Game / Big Picture domain is not a primary product workflow;
- avoid creating odd simultaneous Steam/XBOX presentation expectations.

### 17.1 Launch mechanism

Do not implement Xbox action as:

- Win+G;
- Game Bar;
- a hard-coded mutable WindowsApps executable path.

Use packaged-app activation through the Xbox app's verified AUMID / application activation mechanism.

The exact current Xbox app package/AUMID must be validated in PoC and then kept in one feature-local authority.

Do not scatter package strings through dispatcher/UI code.

### 17.2 Game Bar suppression remains

Adding XboxApp does not weaken existing WING Game Bar suppression.

~~~text
Gamebar Button
→ Xbox action
~~~

means "open/activate Xbox app", not "open Xbox Game Bar".

---

## 18. Failure and fail-close policy

### XBOX catalog scan fails

~~~text
Steam page unaffected
XBOX page shows unavailable/error
saved XBOX profiles remain untouched
no destructive rewrite
~~~

### One package metadata read fails

Skip that package and log the reason.

Do not fail the entire catalog unless the enumeration source itself failed.

### Runtime candidate identity probe fails

Ignore the candidate.

Do not guess.

### Active XBOX process exits

Clear XBOX active fact and reapply current effective profile target.

### Profile document malformed/unreadable

Preserve current ProfileStore fail-safe behavior:

- do not overwrite unreadable/malformed data;
- do not fabricate a writable empty document over it.

### Performance apply fails

Preserve each existing runtime's fail-close/recovery behavior.

XBOX support must not weaken current marker/recovery semantics.

### Per-game M1/M2 invalid

Reject mutation at validation boundary and use the last valid/global mapping.

Do not send invalid virtual-controller states.

---

## 19. Lifecycle matrix

| Lifecycle event | XBOX catalog | XBOX session | Profile apply | M1/M2 |
| --- | --- | --- | --- | --- |
| Runtime startup | no background scan required | install hooks + bounded reconcile | apply detected target or Device baseline | effective Xbox360 mapping |
| Main App opens XBOX page | bounded catalog scan | unchanged | unchanged unless user mutates | unchanged unless user mutates |
| XBOX game window appears | unchanged | one-shot PID/package/config probe | XBOX profile becomes effective | per-game override if Xbox360 |
| Alt+Tab away | unchanged | session retained by process lifetime | XBOX profile retained | retained |
| Return to game | unchanged | no new session required | unchanged | unchanged |
| Game exits | unchanged | process wait clears session | Device/other active target restored | global mapping |
| Sleep | unchanged | old observations invalidated as needed | existing subsystem suspend behavior | Full1902 neutral path owns output |
| Resume | unchanged | bounded reconcile | reapply actual target | current effective mapping |
| Controlled Runtime restart | unchanged | hooks + bounded reconcile | reapply actual target | current effective mapping |
| Game update | next XBOX page scan reflects package | runtime identity probe uses current metadata | persisted stable key reused when derivation remains same | persisted override reused |
| Game uninstall | next scan omits title | no live session | saved profile retained | no active override |

---

## 20. Concurrency / race policy

Follow the project's production-race policy.

Protect real lifecycle behavior:

- startup;
- game launch;
- process exit;
- sleep/resume;
- controlled Runtime restart;
- operation failure.

Do not add complexity solely for instruction-level event interleavings.

Recommended practical rules:

- WinEvent callbacks post to one Runtime-owned serialized execution context;
- active process identity uses process generation/handle, not an unqualified stale PID;
- profile mutation continues using the existing ProfileMutationGate;
- after an active-game change, one reconcile applies the current target;
- stale async result must not overwrite a newer clearly different active target when the existing code already has current-target revalidation.

Do not add:

- epochs;
- barriers;
- generalized session state machines;
- multiple authority managers;
- retry loops for every event ordering.

---

## 21. Testing strategy

### 21.1 XBOX identity tests

Test parsing and key derivation for:

1. StoreId present.
2. StoreId absent, PFN present.
3. StoreId/PFN unavailable, required config Identity fallback.
4. TitleId absent.
5. multiple Executable entries.
6. exact executable match success.
7. helper executable mismatch.
8. malformed config fails closed.
9. package identity query failure fails closed.

### 21.2 Session tests

1. foreground/create/show candidate triggers exactly one probe per process generation where caching applies.
2. non-XBOX process never becomes active.
3. exact matched XBOX process becomes active.
4. Alt+Tab does not clear the process-lifetime session.
5. active process exit clears XBOX fact.
6. startup bounded reconciliation detects an already-running game.
7. resume reconciliation does not trust stale HWND/PID state.
8. no timer/process polling exists.

### 21.3 Catalog tests

1. current-user package enumeration result converts only valid game packages.
2. framework/resource/non-game packages are rejected.
3. one unreadable package does not fail the rest.
4. saved profile metadata does not define installed state.
5. uninstalled game profile is preserved.
6. duplicate package/config observations collapse to one canonical key where appropriate.

### 21.4 Persistence tests

1. existing Steam Games JSON remains unchanged.
2. XboxGames round-trips.
3. unknown extension data remains preserved.
4. explicit null structural XboxGames is handled safely.
5. malformed/unsupported profile document is never overwritten.

### 21.5 Shared runtime tests

For CPU Boost, TDP, Power Mode, FPS, Resolution:

~~~text
Steam target
→ existing behavior unchanged

XBOX target
→ same lower-level apply implementation receives XBOX profile values

None
→ Device/global baseline
~~~

### 21.6 M1/M2 tests

1. no XBOX override → global mapping.
2. XBOX override + Xbox360 → per-game mapping.
3. XBOX override + SteamDeck → no Xbox360 remap path.
4. game exit → global mapping.
5. invalid XBOX mapping rejected.

### 21.7 UI tests

Main App navigation:

~~~text
Device
Controller
Steam
XBOX
Overlay
Shortcut
How to Use
Settings
~~~

Verify:

- no user-facing Profile top-level item remains;
- Steam page still uses existing Steam catalog;
- XBOX page uses only XBOX catalog/contracts;
- no nested Steam/XBOX tabs under one Profile page.

### 21.8 Overlay tests

1. active Steam → Steam page projection.
2. active XBOX → XBOX page projection.
3. active target changes → old target cannot overwrite new projection.
4. no XBOX catalog ownership in Overlay.

### 21.9 Front-button tests

1. XboxApp allowed only in intended domain.
2. executor uses packaged-app activation seam.
3. no Win+G path.
4. existing Game Bar suppression remains unchanged.
5. activation failure is logged and does not alter controller authority.

---

## 22. Required hardware/field PoCs before production implementation

### PoC A — installed XBOX catalog

Run on the actual supported MSI Claw / Windows environment.

Capture:

- package enumeration result;
- package name/family/full name;
- install/effective path access;
- MicrosoftGame.config path/readability;
- Identity;
- StoreId;
- TitleId;
- ExecutableList;
- display name.

Test multiple games, including at least:

- one Microsoft first-party Game Pass title;
- one third-party Game Pass title;
- one title installed to a non-system game drive if available.

Pass condition:

> Installed games can be enumerated without elevation, undocumented DB scraping, ACL modification, or periodic scanning.

**Field status: PASS.** PR1 field validation was performed before the Runtime elevation change. The current product Runtime is elevated for an unrelated requirement, but XBOX catalog enumeration must not depend on elevation, ACL takeover, or private package databases.

### PoC B — event-driven active game identity

With Xbox Game Bar disabled:

~~~text
install WinEvent hooks
→ launch Xbox app / FSE
→ launch Game Pass game
→ capture CREATE/SHOW/FOREGROUND events
→ exact PID package/config match
→ keep active session through Alt+Tab
→ observe process exit
~~~

Test:

- launch;
- game active;
- Alt+Tab;
- return;
- game exit;
- second game;
- sleep/resume;
- controlled Addon Runtime restart while game is running.

Pass condition:

> The correct XBOX game identity is established and retired without periodic polling.

**Field status: PASS.** Aniimo Legend and Minecraft for Windows were positively identified on the target device, launch helpers were rejected by exact executable matching, Alt+Tab retained the active identity, process exit cleared it, and bounded reconciliation recovered an already-running title after resume and after a controlled Runtime restart.

### PoC C — Xbox app activation

Validate the current Xbox app package/AUMID and activation API.

Pass condition:

> WING/Center M action can activate the Xbox app without Win+G, Game Bar, or a mutable WindowsApps executable path.

---

## 22A. Field validation record — MSI Claw, 2026-10-05

This section is the canonical handoff record for the XBOX PoC work completed on the real target device.

Do not re-open the already-resolved questions below unless later field evidence contradicts them.

### 22A.1 Environment and implementation history

Field validation used the supported MSI Claw / Windows environment:

~~~text
OS:
Microsoft Windows NT 10.0.26200.0
build 26200

Observed Addon builds:
0.1.331.0
0.1.332.0
0.1.333.0
~~~

Relevant implementation history:

~~~text
PR #678
→ installed XBOX catalog diagnostic PoC
→ PoC A

PR #682
→ event-driven XBOX active-game session diagnostic PoC
→ initial PoC B implementation

PR #684
→ PR2.1 MicrosoftGame.config location correction
→ live PackageFullName
→ PackageManager.FindPackageForUser("", packageFullName)
→ Package.EffectiveLocation / InstalledLocation

PR #686
→ GetPackagePathByFullName2 import correction
→ kernel32.dll → kernelbase.dll
→ diagnostic PackagePathType evidence only
~~~

Runtime privilege history must remain explicit:

- PR1 catalog field validation succeeded **before** the later Runtime elevation change.
- The current product Runtime is elevated for an unrelated product requirement.
- PR2/PR2.1 later field runs may therefore be elevated.
- XBOX package/config architecture must still not depend on ACL takeover, WindowsApps ownership changes, PowerShell fallback, private package databases, or broad filesystem scans.

### 22A.2 PoC A — installed catalog: PASS

PR1 field validation proved that the shipped Runtime can enumerate current-user packages and locate valid GDK/XBOX metadata through documented WinRT package APIs.

Initial field result:

~~~text
PackageManager.FindPackagesForUser("")
Total packages: 170
Accessible package locations: 170
MicrosoftGame.config candidates: 2
Recognized/parseable roots: 2
Valid XBOX/GDK candidates: 2
Failures: 0
~~~

A later 0.1.332.0 catalog capture returned 171 packages, with the same two valid game candidates and zero package/config failures.

Validated titles:

#### Aniimo Legend

~~~text
CandidateKey:
store:9PK8PHLCQDF6

PackageFullName:
KingsgloryGames.AniimoLegend_1.0.18.0_x64__9d08hqzdedf04

PackageFamilyName:
KingsgloryGames.AniimoLegend_9d08hqzdedf04

StoreId:
9PK8PHLCQDF6

TitleId:
6B49108E

ExecutableList:
Aniimo.exe

Effective config root:
C:\Program Files\WindowsApps\KingsgloryGames.AniimoLegend_1.0.18.0_x64__9d08hqzdedf04
~~~

#### Minecraft for Windows

~~~text
CandidateKey:
store:9NBLGGH2JHXJ

PackageFullName:
Microsoft.MinecraftUWP_1.26.5203.0_x64__8wekyb3d8bbwe

PackageFamilyName:
Microsoft.MinecraftUWP_8wekyb3d8bbwe

StoreId:
9NBLGGH2JHXJ

TitleId:
35760C07

ExecutableList:
Minecraft.Windows.exe

Effective config root:
C:\Program Files\WindowsApps\Microsoft.MinecraftUWP_1.26.5203.0_x64__8wekyb3d8bbwe
~~~

The games themselves were installed to the user's D: game drive.

Therefore field evidence established an important architecture fact:

~~~text
actual game payload path
!=
package metadata / MicrosoftGame.config path
~~~

Do not require path-prefix equality between the live executable and the config metadata location.

### 22A.3 Initial PoC B field failure — config lookup only

The first PR2 field runs on Addon 0.1.331.0 did **not** fail at WinEvent, process opening, process image lookup, or package identity.

Run 1:

~~~text
Accepted WinEvents: 331
Unique process generations inspected: 47
Process open failures: 0
Process image failures: 0
No-package candidates: 33
Package identity failures: 0
Config-negative candidates: 14
Executable mismatches: 0
Positive matches: 0
Process exits: 9
~~~

Run 2:

~~~text
Accepted WinEvents: 107
Unique process generations inspected: 44
Process open failures: 0
Process image failures: 0
No-package candidates: 34
Package identity failures: 0
Config-negative candidates: 10
Executable mismatches: 0
Positive matches: 0
Process exits: 7
~~~

The failure boundary was:

~~~text
WinEvent                       PASS
process open                   PASS
live process image             PASS
process package identity       PASS
MicrosoftGame.config lookup    FAIL
exact executable match         not reached
~~~

The defect was that PR2 treated `GetPackagePathByFullName2` output as the config-location authority.

PR2.1 corrected this to:

~~~text
live PackageFullName
→ PackageManager.FindPackageForUser("", packageFullName)
→ Package.EffectiveLocation / InstalledLocation
→ MicrosoftGame.config
→ exact current executable basename match
~~~

The six `GetPackagePathByFullName2` path types remain diagnostic evidence only.

### 22A.4 PR2.1 normal launch validation — PASS

Addon 0.1.332.0 field validation after PR #684 produced positive matches for both test games.

#### Aniimo Legend

Counters:

~~~text
Accepted WinEvents: 135
Unique process generations inspected: 42
Process open failures: 0
Process image failures: 0
No-package candidates: 32
Package identity failures: 0
Config-negative candidates: 8
Executable mismatches: 1
Positive matches: 1
Process exits: 6
~~~

The launch helper was rejected first:

~~~text
D:\xbox\Aniimo Legend\Content\gamelaunchhelper.exe
→ package identity resolved
→ MicrosoftGame.config resolved
→ not present in ExecutableList
→ ExecutableMismatch
~~~

The real game then matched:

~~~text
PID:
10752

RunningProcessPath:
D:\xbox\Aniimo Legend\Content\Aniimo.exe

PackageFullName:
KingsgloryGames.AniimoLegend_1.0.18.0_x64__9d08hqzdedf04

ConfigPath:
C:\Program Files\WindowsApps\KingsgloryGames.AniimoLegend_1.0.18.0_x64__9d08hqzdedf04\MicrosoftGame.config

MatchedExecutable:
Aniimo.exe

CandidateKey:
store:9PK8PHLCQDF6
~~~

#### Minecraft for Windows

Counters:

~~~text
Accepted WinEvents: 104
Unique process generations inspected: 42
Process open failures: 0
Process image failures: 0
No-package candidates: 32
Package identity failures: 0
Config-negative candidates: 8
Executable mismatches: 1
Positive matches: 1
Process exits: 7
~~~

Again, `gamelaunchhelper.exe` was rejected and the real game matched:

~~~text
PID:
10928

RunningProcessPath:
D:\xbox\Minecraft for Windows\Content\Minecraft.Windows.exe

PackageFullName:
Microsoft.MinecraftUWP_1.26.5203.0_x64__8wekyb3d8bbwe

ConfigPath:
C:\Program Files\WindowsApps\Microsoft.MinecraftUWP_1.26.5203.0_x64__8wekyb3d8bbwe\MicrosoftGame.config

MatchedExecutable:
Minecraft.Windows.exe

CandidateKey:
store:9NBLGGH2JHXJ
~~~

This validates the intended strong classifier:

~~~text
live process
+ package identity
+ PackageManager config location
+ exact ExecutableList basename
→ XBOX game
~~~

No path equality, title heuristic, fullscreen test, GPU activity, PresentMon, or fuzzy executable match is required.

### 22A.5 Alt+Tab retention and process exit — PASS

Aniimo lifecycle evidence:

~~~text
ActiveGameDetected
→ ForegroundLeft
→ ForegroundReturned
→ ForegroundLeft
→ ForegroundReturned
→ active CandidateKey remains store:9PK8PHLCQDF6
→ matched process exits
→ active diagnostic state cleared
~~~

Minecraft showed the same behavior.

Therefore:

- foreground loss is not session loss;
- window hide/destroy is not the retirement authority;
- the retained matched process generation remains authoritative;
- process exit is the normal retirement authority.

This is field confirmation of the process-lifetime design in section 9.

### 22A.6 PackagePathType diagnostic correction — PASS, non-authoritative

Before PR #686, all six `GetPackagePathByFullName2` diagnostics returned result 127 because the P/Invoke imported the API from `kernel32.dll`.

PR #686 changed only the import module to `kernelbase.dll`.

Addon 0.1.333.0 field evidence then reported for Aniimo:

~~~text
Install:
result=0
path=C:\Program Files\WindowsApps\KingsgloryGames.AniimoLegend_1.0.18.0_x64__9d08hqzdedf04

Effective:
result=0
path=C:\Program Files\WindowsApps\KingsgloryGames.AniimoLegend_1.0.18.0_x64__9d08hqzdedf04

Mutable:
result=15707
path=<unavailable>

MachineExternal:
result=1168
path=<unavailable>

UserExternal:
result=1168
path=<unavailable>

EffectiveExternal:
result=1168
path=<unavailable>
~~~

This closes the result-127 diagnostic defect.

Architecture policy remains unchanged:

> `GetPackagePathByFullName2` is diagnostic evidence only. It is not the MicrosoftGame.config authority and is not required for a positive XBOX game match.

Do not add fallback machinery merely because optional path types return unavailable.

### 22A.7 Suspend / resume recovery — PASS

Addon 0.1.333.0 was tested with Aniimo already running across suspend/resume.

Observed Runtime lifecycle:

~~~text
23:20:16
Suspend
→ Full1902 presentation paused safely

23:21:01
Resume
→ same Xbox360 publisher resumed
→ normal Runtime resume reconciliation completed
~~~

The XBOX diagnostic observer was intentionally started only **after** resume:

~~~text
23:21:22.543
ObserverStarted

23:21:22.710
ActiveGameDetected
CandidateKey=store:9PK8PHLCQDF6
PID=17092
Executable=Aniimo.exe
~~~

The startup reconcile result was:

~~~text
ForegroundProcessId=17396
EnumeratedProcessCount=34
ActiveCandidateKey=store:9PK8PHLCQDF6
~~~

The foreground process was not the game.

Therefore the positive match did not depend on receiving the original launch event or on the game being foreground at observer start.

The bounded one-shot reconciliation found the already-running game after resume and reconstructed exact package/config/executable identity.

Counters:

~~~text
Accepted WinEvents: 1
Unique process generations inspected: 9
Process open failures: 0
Process image failures: 0
No-package candidates: 7
Package identity failures: 0
Config-negative candidates: 1
Executable mismatches: 0
Positive matches: 1
Process exits: 0
~~~

This is sufficient field evidence for the architecture requirement:

~~~text
resume
→ bounded current-state reconcile
→ already-running XBOX game can be recovered
~~~

An observer-kept-running-across-the-entire-sleep variant was not separately required as a production blocker after this bounded-reconciliation path succeeded.

### 22A.8 Controlled Runtime restart recovery — PASS

Aniimo was left running while the Addon Runtime was restarted.

After the new Runtime was available, the XBOX diagnostic observer was started.

Field report:

~~~text
ObserverStarted:
2026-10-05T14:28:27.4828838+00:00

ActiveGameDetected:
2026-10-05T14:28:27.5666335+00:00

CandidateKey:
store:9PK8PHLCQDF6

PID:
17136

RunningProcessPath:
D:\xbox\Aniimo Legend\Content\Aniimo.exe

ConfigPath:
C:\Program Files\WindowsApps\KingsgloryGames.AniimoLegend_1.0.18.0_x64__9d08hqzdedf04\MicrosoftGame.config

MatchedExecutable:
Aniimo.exe
~~~

The already-running game was re-established about 84 ms after observer start.

Counters:

~~~text
Accepted WinEvents: 1
Unique process generations inspected: 8
Process open failures: 0
Process image failures: 0
No-package candidates: 7
Package identity failures: 0
Config-negative candidates: 0
Executable mismatches: 0
Positive matches: 1
Process exits: 0
~~~

This confirms the controlled-restart requirement from section 9.5:

~~~text
new Runtime
→ no historical game-launch event available
→ install hooks
→ bounded reconcile
→ find existing game process
→ exact identity
→ ActiveXboxGame
~~~

No periodic process/window polling is needed.

### 22A.9 PoC B final pass matrix

The event-driven active-game identity PoC is considered complete for production architecture purposes.

~~~text
normal launch detection                 PASS
process open / generation identity      PASS
live executable path                    PASS
package full/family identity            PASS
MicrosoftGame.config resolution         PASS
D: payload + C: metadata separation     PASS
exact ExecutableList match              PASS
gamelaunchhelper rejection              PASS
unrelated packaged-process rejection    PASS
Alt+Tab retention                       PASS
matched process exit clear              PASS
post-resume already-running recovery    PASS
controlled Runtime restart recovery     PASS
bounded startup reconciliation          PASS
no production polling                   PASS
~~~

No field evidence justified adding:

- timer polling;
- ConfigNegative retry loops;
- TTL/epoch retry policy;
- path-prefix matching;
- generic game scoring;
- another detector manager/state machine.

Keep the simpler process-generation cache + event-driven wake-up + bounded lifecycle reconcile architecture.

### 22A.10 Current X0 gate

As of the end of the 2026-10-05 field work:

~~~text
PoC A — installed XBOX catalog
PASS

PoC B — event-driven active XBOX identity/lifecycle
PASS

PoC C — Xbox app packaged activation
PENDING
~~~

Production X1 catalog/identity work should not re-litigate PoC A or PoC B without contradictory new device evidence.

The remaining X0 task is only PoC C:

~~~text
Xbox app package/AUMID
→ packaged-app activation API
→ no Win+G
→ no Game Bar
→ no hardcoded mutable WindowsApps executable path
~~~

---

## 23. Suggested implementation sequence

This is architecture sequencing, not a set of already-approved work orders.

### Phase X0 — diagnostic PoCs

Current field status:

~~~text
catalog enumeration / config parser   PASS
WinEvent active-game detector         PASS
resume/restart bounded recovery       PASS
Xbox app activation                   PENDING
~~~

Remaining X0 scope is only the packaged Xbox app activation PoC.

No production profile mutation yet.

### Phase X1 — XBOX identity/catalog foundation

Implementation sequence is intentionally split so the validated PoC does not remain as a parallel owner:

~~~text
PR4
→ promote PoC A parser / canonical identity / installed catalog into production code
→ retire XBOX Catalog Diagnostic Runtime owner, report, frontend RPC/contracts, Developer page, and diagnostic-only tests
→ keep the XBOX Active Game Session Diagnostic temporarily
→ make that session diagnostic consume the production parser/key authority

PR5
→ expose the production installed catalog through XBOX-specific frontend contracts
→ Main App top-level navigation rename Profile → Steam
→ add separate XBOX page with read-only installed catalog
~~~

The XBOX Active Game Session Diagnostic is retired only when the later production `XboxGameSessionRuntime` is implemented in X3. Do not keep a production catalog and a separate catalog diagnostic scanner in parallel.

- production XboxGameIdentity model;
- production installed-XBOX catalog scan;
- separate frontend catalog contracts in the following focused PR;
- Main App top-level navigation rename Profile → Steam in that frontend/UI PR;
- add XBOX page with read-only catalog first.

### Phase X2 — XBOX profile persistence/editing

- ProfileDocument.XboxGames;
- XboxGameProfile;
- XboxGameProfileMutations;
- XBOX page profile detail;
- reuse existing performance/display nested settings.

### Phase X3 — active XBOX session + shared apply target

- production XboxGameSessionRuntime;
- ActiveProfileTarget;
- refactor existing performance runtimes to resolve Steam/XBOX target without duplicating hardware implementations;
- startup/resume/exit reconciliation.

### Phase X4 — per-game M1/M2

- XboxGameControllerOverrides;
- effective BackButtonMapping provider;
- XBOX page M1/M2 editing;
- preserve global fallback.

### Phase X5 — Overlay active XBOX profile

- Runtime-authoritative active platform projection;
- Overlay shows one current profile;
- no XBOX catalog added to Overlay.

### Phase X6 — front-button Xbox action

- FrontButtonAction.XboxApp;
- capability validation;
- packaged activation executor;
- UI label/action.

Keep each phase small enough for evidence-based review.

Do not combine PoC uncertainty with a large persistence/UI/runtime refactor.

---

## 24. Explicit non-goals

Do not implement as part of this architecture:

- Xbox Game Bar integration;
- Game Bar widget/helper process;
- ClawHUD IPC;
- PresentMon game classification;
- universal Win32/non-Steam game profiles;
- Epic/GOG detection;
- generic "all launchers" game identity framework;
- entitlement/Game Pass subscription-state detection;
- Xbox cloud gaming profile detection unless later explicitly designed;
- Xbox app private database parsing;
- Xbox Gaming Services private protocol reverse engineering;
- polling;
- WindowsApps ACL ownership changes;
- firmware M1/M2 profile writes;
- Steam profile key migration;
- Steam RunningAppID replacement;
- XBOX-driven VIIPER presentation switching;
- multi-user/RDP/Fast User Switching support;
- simultaneous multi-game arbitration framework.

---

## 25. Architecture invariants

The implementation is correct only if all of the following remain true.

1. Main App exposes separate **Steam** and **XBOX** top-level pages.
2. Existing Steam identity remains numeric RunningAppID/AppID.
3. XBOX uses its own string canonical identity.
4. XBOX detection works with Xbox Game Bar disabled.
5. ClawHUD is not required.
6. Active XBOX detection uses events plus bounded lifecycle reconciliation, never periodic polling.
7. A process becomes an XBOX game only from strong package/GDK evidence with exact executable match.
8. Installed XBOX catalog can be edited before game launch.
9. Steam and XBOX profile persistence remain distinct collections.
10. Existing CPU/TDP/Power/FPS/Resolution lower-level implementations are reused.
11. One derived active-profile selector is the only convergence point for machine-wide game overrides.
12. XBOX per-game M1/M2 overrides only the Xbox360 software mapping path.
13. Global M1/M2 remains the fallback.
14. XBOX detection never changes PID1902 ownership, HidHide, VIIPER ownership, or Steam/BPM presentation policy.
15. Overlay does not gain separate Steam and XBOX profile tabs.
16. Overlay active-game profile is selected by Runtime, not by UI guessing.
17. Front-button Xbox action activates the Xbox app and never routes through Win+G/Game Bar.
18. Detection/catalog failure fails closed to Device/global settings without affecting Steam or controller ownership.
19. Sleep/resume and controlled Runtime restart perform bounded reconciliation.
20. No speculative manager/state-machine/abstraction is added for unsupported multi-session or pathological timing cases.

---

## 26. Reference evidence

### SteamAddonforClaw

Primary authority:

- docs/Full 1902 Implementation/README.md
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
- docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
- docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md
- current ProfileDocument / GameProfile / ProfileStore
- current Profile performance runtimes
- current BackButtonMappingSettings / Xbox360 publisher
- current FrontButtonMappingSettings / dispatcher
- current Overlay active-profile projection

### ClawHUD research reference only

Reviewed current onehoon/ClawHUD main:

- src/ClawHUD/GameDetection/WindowsGameIdentityProbe.cpp
- src/ClawHUD/GameDetection/MicrosoftGameTrigger.cpp
- src/ClawHUD/GameDetection/ProductionGameWindowSource.cpp
- docs/GAME_DETECTION_PRODUCTION_DESIGN.md
- docs/GAME_DETECTION_FIELD_ANALYSIS_2026-08-31.md

Relevant validated finding:

> Microsoft/Xbox title identity can be established from process package identity plus readable MicrosoftGame.config and exact current-executable matching without Game Bar.

SteamAddonforClaw does not need ClawHUD's generic renderer/fullscreen game verifier because this architecture intentionally supports only the XBOX/Microsoft packaged/GDK domain.

### Microsoft platform references

- PackageManager.FindPackagesForUser:
  https://learn.microsoft.com/en-us/uwp/api/windows.management.deployment.packagemanager.findpackagesforuser
- PackageCatalog.OpenForCurrentUser:
  https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.packagecatalog.openforcurrentuser
- GetPackageFullName:
  https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getpackagefullname
- GetPackageFamilyName:
  https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getpackagefamilyname
- MicrosoftGame.config schema:
  https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/system/microsoftgameconfig/microsoftgameconfig-schema
- MicrosoftGame.config Identity:
  https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/system/microsoftgameconfig/elements/microsoftgameconfig-element-identity
- MicrosoftGame.config Executable:
  https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/system/microsoftgameconfig/elements/microsoftgameconfig-element-executable
- MicrosoftGame.config StoreId:
  https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/system/microsoftgameconfig/elements/microsoftgameconfig-element-storeid
- MicrosoftGame.config TitleId:
  https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/system/microsoftgameconfig/elements/microsoftgameconfig-element-titleid

---

## 27. Final design principle

Do not make XBOX support a reason to generalize the entire product.

The desired architecture is deliberately asymmetric:

~~~text
Steam
→ use Steam's excellent RunningAppID authority

XBOX
→ use Windows package/GDK identity and event-driven process lifetime

Main App
→ separate Steam and XBOX pages

Hardware apply
→ reuse the same existing implementations

Overlay
→ show only the Runtime-selected active game's profile

Controller
→ keep one Full1902 owner and one Xbox360 publisher
~~~

One clear owner per responsibility is more important than forcing two different platforms into the same identity model.
