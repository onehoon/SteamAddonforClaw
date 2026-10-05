# Work Order — XBOX PR2.1: Fix Live Session MicrosoftGame.config Location Resolution

> **Date:** 2026-10-05  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed baseline:** `main@4f93f11e5b076988d3a9294fc7b231f8ff196a0d`  
> **Parent implementation:** PR #682 / XBOX PR2 Event-Driven Active Game Session Diagnostic PoC  
> **Architecture authority:** `docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md`  
> **Parent work order:** `docs/XBOX_GAME_PROFILE_ARCHITECTURE/work-order/PR2_XBOX_ACTIVE_GAME_SESSION_DIAGNOSTIC_POC_WORK_ORDER_2026-10-05.md`  
> **Scope:** narrow PR2 field-failure fix only  
> **Architecture changes:** forbidden  
> **Production profile behavior:** out of scope

---

## 1. Goal

Fix the real-machine PR2 failure where the Runtime successfully reaches the live process and package identity, but cannot find `MicrosoftGame.config` because the session probe incorrectly treats `GetPackagePathByFullName2` package paths as the config-location authority.

The fix must preserve the existing PR2 architecture:

~~~text
WinEvent
→ top-level HWND / PID
→ live process generation
→ QueryFullProcessImageName
→ process package identity
→ MicrosoftGame.config
→ exact executable basename match
→ diagnostic Active XBOX game
→ process lifetime
~~~

Only this step changes:

~~~text
WRONG
GetPackagePathByFullName2(...)
→ assume returned path contains MicrosoftGame.config

RIGHT
live PackageFullName
→ PackageManager.FindPackageForUser("", packageFullName)
→ Package.EffectiveLocation / Package.InstalledLocation
→ locate MicrosoftGame.config using the same package-location semantics proven by PR1

GetPackagePathByFullName2(...)
→ diagnostic/report evidence only
~~~

Do not redesign the session detector.

---

## 2. Real target evidence — this is a concrete field failure

PR2 was tested on the target MSI Claw with Addon version `0.1.331.0`.

The Runtime is now elevated for unrelated product reasons. Treat elevated Runtime as the current product baseline, but **do not rely on elevation as the solution to this bug**.

Two PR2 session runs failed in the same place.

### Run 1

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

### Run 2

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

Runtime logs repeatedly report:

~~~text
[XboxSessionDiagnostic]
Candidate had no usable MicrosoftGame.config evidence.
Reason="MicrosoftGame.config was not found in the package paths returned by Windows."
~~~

followed by:

~~~text
Duplicate WinEvent for an already classified process generation was suppressed.
Disposition=ConfigNegative
~~~

Therefore the observed failure boundary is:

~~~text
WinEvent                       PASS
process open                   PASS
live image query               PASS
live process package identity  PASS
MicrosoftGame.config lookup    FAIL
executable exact match         not reached
~~~

This is not a theoretical edge case.

It is the current real target behavior.

---

## 3. PR1 proves the config is available through WinRT package metadata

The same `0.1.331.0` build successfully runs the installed-game catalog diagnostic.

PR1 field evidence:

~~~text
PackageManager.FindPackagesForUser("")
→ 170 packages
→ 170 accessible package locations
→ 2 MicrosoftGame.config candidates
→ 2 valid XBOX/GDK games
→ 0 failures
~~~

### Minecraft

~~~text
PackageFullName:
Microsoft.MinecraftUWP_1.26.5203.0_x64__8wekyb3d8bbwe

PackageFamilyName:
Microsoft.MinecraftUWP_8wekyb3d8bbwe

Effective package root:
C:\Program Files\WindowsApps\Microsoft.MinecraftUWP_1.26.5203.0_x64__8wekyb3d8bbwe

MicrosoftGame.config:
C:\Program Files\WindowsApps\Microsoft.MinecraftUWP_1.26.5203.0_x64__8wekyb3d8bbwe\MicrosoftGame.config

Executable:
Minecraft.Windows.exe
~~~

### Aniimo Legend

~~~text
PackageFullName:
KingsgloryGames.AniimoLegend_1.0.18.0_x64__9d08hqzdedf04

PackageFamilyName:
KingsgloryGames.AniimoLegend_9d08hqzdedf04

Effective package root:
C:\Program Files\WindowsApps\KingsgloryGames.AniimoLegend_1.0.18.0_x64__9d08hqzdedf04

MicrosoftGame.config:
C:\Program Files\WindowsApps\KingsgloryGames.AniimoLegend_1.0.18.0_x64__9d08hqzdedf04\MicrosoftGame.config

Executable:
Aniimo.exe
~~~

Both games are actually installed by the user on D:.

Therefore the real machine has already proven:

~~~text
actual game payload location
!=
package/config metadata location
~~~

and also proven that:

~~~text
PackageManager
→ Package.EffectiveLocation / InstalledLocation
→ MicrosoftGame.config
~~~

works.

PR2.1 must use that proven path.

---

## 4. Microsoft API basis

Use documented WinRT package APIs.

Relevant Microsoft documentation:

- `Windows.Management.Deployment.PackageManager`
- `PackageManager.FindPackageForUser(String, String)`
- `Package.EffectiveLocation`
- `Package.InstalledLocation`

Microsoft documents `FindPackageForUser(userSecurityId, packageFullName)` as retrieving information about a specific package installed for a specific user.

For the current user use:

~~~csharp
var package = new PackageManager().FindPackageForUser(
    string.Empty,
    packageFullName);
~~~

The exact API call is preferred over enumerating all installed packages again because the live process has already provided the exact `PackageFullName`.

Do not run another full:

~~~text
FindPackagesForUser("")
~~~

scan for every WinEvent candidate.

---

## 5. Current code defect

At the reviewed baseline, `WindowsXboxGameProcessIdentityProbe.InspectAsync` collects:

~~~csharp
QueryFullProcessImageNameW(...)
GetPackageFullName(...)
GetPackageFamilyName(...)
GetApplicationUserModelId(...)
GetPackageId(...)
GetPackagePathByFullName2(...)
~~~

and passes only `PackagePaths` into:

~~~text
XboxGameProcessIdentityEvaluator.InspectAsync
~~~

The evaluator then does:

~~~csharp
foreach (var packagePath in evidence.PackagePaths)
{
    var configPath = Path.Combine(packagePath.Path, "MicrosoftGame.config");
    ...
}
~~~

This is the bug.

The six `PackagePathType` results were intended to be diagnostic evidence explaining install/external layout.

They are not the config-location authority.

---

## 6. Required fix

### 6.1 Keep live process identity exactly as-is

Do not change:

~~~text
OpenProcess
GetProcessTimes
QueryFullProcessImageNameW
GetPackageFullName
GetPackageFamilyName
GetApplicationUserModelId
GetPackageId
process-generation ownership
process exit wait
WinEvent hooks
serialized worker
~~~

Those parts passed field validation.

### 6.2 Resolve the exact current-user Package from PackageFullName

After successful `GetPackageFullName`:

~~~text
packageFullName
→ PackageManager.FindPackageForUser("", packageFullName)
→ Package
~~~

Do not enumerate the whole package catalog.

### 6.3 Obtain config candidate roots from the Package object

Use the same semantics as PR1:

~~~text
Package.EffectiveLocation?.Path
Package.InstalledLocation?.Path
~~~

Deduplicate equal paths case-insensitively.

Probe:

~~~text
<root>\MicrosoftGame.config
~~~

in bounded order.

Recommended order:

~~~text
Effective
Installed
~~~

This matches the successful PR1 behavior.

### 6.4 Keep GetPackagePathByFullName2 results

Continue querying:

~~~text
Install
Effective
Mutable
MachineExternal
UserExternal
EffectiveExternal
~~~

because they are still valuable PR2 field evidence.

However:

> `GetPackagePathByFullName2` results must not decide where `MicrosoftGame.config` is read from.

They remain:

~~~text
FrontendXboxSessionDiagnosticGame.PackagePaths
→ report / UI evidence only
~~~

---

## 7. Keep config roots and diagnostic package paths as separate concepts

Do not overload one collection with two meanings.

Conceptually the process evidence should contain:

~~~text
PackageConfigLocations
  Effective
  Installed

PackagePaths
  Install
  Effective
  Mutable
  MachineExternal
  UserExternal
  EffectiveExternal
~~~

The first collection answers:

> Where should PR2 look for MicrosoftGame.config?

The second collection answers:

> What paths did the native AppModel PackagePathType API report?

They may contain different paths.

They may also point to different drives.

That is expected.

---

## 8. Minimal implementation shape

Prefer a narrow code change.

A reasonable shape is:

~~~csharp
internal sealed record XboxGamePackageConfigLocation(
    string Kind,
    string RootPath);
~~~

Extend process evidence:

~~~csharp
internal sealed record XboxGameProcessIdentityEvidence(
    ...
    IReadOnlyList<XboxGamePackageConfigLocation> ConfigLocations,
    IReadOnlyList<FrontendXboxSessionDiagnosticPackagePath> PackagePaths);
~~~

Then:

~~~text
WindowsXboxGameProcessIdentityProbe
→ resolves PackageFullName
→ exact PackageManager.FindPackageForUser(...)
→ captures Effective/Installed config roots
→ also captures existing six native PackagePathType values

XboxGameProcessIdentityEvaluator
→ searches ConfigLocations only
→ exact executable match
→ stores PackagePaths unchanged in frontend diagnostic result
~~~

Do not introduce:

- XboxPackageManager;
- XboxPackageRepository;
- package-location service layer;
- generic package abstraction;
- dependency-injection framework;
- provider hierarchy.

A tiny internal record/helper is enough.

If a few lines of PR1 location extraction must be duplicated to keep the fix local, that is preferable to creating a new subsystem.

---

## 9. PackageManager failure policy

Package lookup failure is a normal diagnostic negative, not a Runtime failure.

Examples:

~~~text
FindPackageForUser returns null
→ ConfigNegative

EffectiveLocation unavailable
+ InstalledLocation unavailable
→ ConfigNegative

one location throws
+ another succeeds
→ continue with the successful location
~~~

Capture a useful diagnostic reason.

Suggested reasons:

~~~text
PackageManager did not resolve the live PackageFullName.

Package metadata exposed no usable Effective/Installed location.

MicrosoftGame.config was not found in the Package object's Effective/Installed locations.

MicrosoftGame.config at '<path>' could not be read: <reason>.
~~~

Do not:

- elevate again;
- alter ACLs;
- take ownership of WindowsApps;
- scan D:;
- scan C:\Program Files\WindowsApps recursively;
- invoke PowerShell;
- use private Xbox databases.

---

## 10. Elevated Runtime baseline

The Runtime is now intentionally elevated due to another product requirement.

PR2.1 must assume:

~~~text
SteamInputAddonforClaw.exe Runtime
→ elevated
~~~

for current field testing.

However:

- do not request another elevation transition;
- do not add an elevation helper;
- do not alter Main App/Overlay elevation topology in this PR;
- do not make config location dependent on administrator-only filesystem traversal;
- do not use elevation as justification for WindowsApps ACL changes.

PR1 already proved the package/config API path before this session fix.

---

## 11. Do not change event deduplication yet

Current logs show many duplicate CREATE/SHOW/FOREGROUND events for the same process generation.

The existing process-generation negative cache successfully suppresses repeated work.

Keep it unchanged for PR2.1.

Specifically, do **not** yet add:

- timed retries;
- ConfigNegative TTL;
- delayed reinspection;
- FOREGROUND override;
- retry counters;
- epoch/state machinery.

Reason:

The currently observed `ConfigNegative` is caused by the wrong resolver, not proven metadata timing.

First fix the resolver and rerun the field probe.

Only if the corrected resolver shows a real normal-launch sequence like:

~~~text
CREATE
→ exact PackageFullName exists
→ exact Package lookup temporarily cannot expose config

later FOREGROUND
→ same generation would now succeed
~~~

should a narrow one-time reinspection policy be considered.

Do not solve an unproven second problem in this PR.

---

## 12. No architecture changes

PR2.1 must not modify:

~~~text
Runtime / Main UI / Overlay process ownership
Frontend pipe ownership
Overlay protocol
Steam RunningAppID
Full1902 PID/HidHide/VIIPER authority
X360 / SteamDeck presentation
M1/M2 mapping
profile persistence
performance profile application
WinEvent event set
process-lifetime monitoring
resume architecture
~~~

No new top-level UI is required.

No frontend protocol bump should be required unless the implementation changes a serialized DTO.

Prefer to keep the existing frontend contract exactly unchanged.

---

## 13. Tests — exact package config resolution

Add focused tests around the corrected boundary.

Required:

1. exact PackageFullName resolves a current-user package;
2. Effective location contains config → parsed;
3. Effective missing, Installed contains config → parsed;
4. Effective and Installed same path → probed once;
5. Effective throws but Installed works → continue and match;
6. Package lookup returns null → ConfigNegative;
7. package exposes neither location → ConfigNegative;
8. config absent in both → ConfigNegative;
9. config malformed → ConfigNegative;
10. running executable basename exact-match succeeds;
11. wrong executable basename → ExecutableMismatch;
12. running process path D: + config location C: → Matched;
13. native PackagePathType evidence may all point elsewhere or be unavailable → must not prevent a valid config match;
14. config location must not be synthesized from `PackagePaths`.

Use a narrow fake/package-location input seam as necessary.

Do not require real package installation in unit tests.

---

## 14. Preserve existing PR2 tests

Existing tests must continue to validate:

- FOREGROUND / CREATE / SHOW;
- thin WinEvent callback;
- one serialized worker;
- process generation identity;
- stale PID reuse protection;
- Alt+Tab retention;
- active process exit clear;
- bounded initial reconcile;
- resume reconcile;
- frontend disconnect cleanup;
- no polling;
- PackagePathType report evidence;
- PACKAGE_ID / PACKAGE_VERSION native layout.

Do not weaken these tests to make the new resolver fit.

---

## 15. Logging additions

Improve the one useful diagnostic boundary so the next field test can prove exactly what happened.

For a candidate with package identity, Debug log should be able to explain:

~~~text
PID
PackageFullName
RunningProcessPath
ConfigLocation.Effective
ConfigLocation.Installed
selected ConfigPath or failure
~~~

Do not log this at Info for every packaged process.

Positive match Info remains:

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

---

## 16. Diagnostic report

Keep the current report schema unless a tiny addition is needed.

For a positive game the report must still distinguish:

~~~text
Running process path
Config path
PackagePathType evidence
~~~

Example target result for Aniimo:

~~~text
Running process path:
D:\...\Aniimo.exe

Config path:
C:\Program Files\WindowsApps\KingsgloryGames.AniimoLegend_1.0.18.0_x64__9d08hqzdedf04\MicrosoftGame.config

Matched ExecutableList entry:
Aniimo.exe
~~~

The six native package paths should remain visible separately.

---

## 17. Manual field validation after PR2.1

Use the same MSI Claw.

### A. Aniimo

~~~text
Start diagnostic
→ launch Aniimo
→ wait for Active XBOX game
~~~

Must prove:

~~~text
PositiveMatches >= 1
CandidateKey = store:9PK8PHLCQDF6
PackageFamilyName = KingsgloryGames.AniimoLegend_9d08hqzdedf04
RunningExecutableName = Aniimo.exe
RunningProcessPath = actual D:-side path
ConfigPath = valid MicrosoftGame.config metadata path
MatchedExecutableName = Aniimo.exe
~~~

### B. Alt+Tab

~~~text
Aniimo active
→ Alt+Tab to desktop / Explorer / Xbox app
→ active game remains Aniimo
→ return to game
→ no new session churn
~~~

### C. Exit

~~~text
close Aniimo
→ retained process generation exits
→ Active XBOX game clears
~~~

### D. Minecraft

Must prove:

~~~text
Minecraft.Windows.exe
Microsoft.MinecraftUWP_8wekyb3d8bbwe
store:9NBLGGH2JHXJ
exact executable match
~~~

and verify launch helpers do not become positive.

### E. Start after game already runs

~~~text
game already running
→ Start diagnostic
→ bounded reconcile
→ positive match
~~~

Only after normal detection succeeds should Runtime restart and sleep/resume be repeated.

---

## 18. Acceptance criteria

PR2.1 is code-complete when all are true:

1. session config lookup no longer iterates `PackagePaths` as config roots;
2. live `PackageFullName` is used to resolve the exact current-user WinRT Package;
3. config roots come from `Package.EffectiveLocation` / `InstalledLocation`;
4. duplicated Effective/Installed paths are deduplicated;
5. existing `GetPackagePathByFullName2` six-path collection remains report-only evidence;
6. actual running executable path still comes from `QueryFullProcessImageNameW`;
7. running path and config path remain independent;
8. exact executable-basename matching remains required;
9. no recursive scanning is added;
10. no ACL/ownership changes are added;
11. no PowerShell/private DB fallback is added;
12. no polling is added;
13. no retry machinery is added without new field evidence;
14. WinEvent/process-generation/session lifetime logic is unchanged;
15. Full1902 is unchanged;
16. Steam is unchanged;
17. Overlay is unchanged;
18. frontend protocol remains unchanged unless a serialized contract truly changes;
19. focused tests pass;
20. full existing tests pass.

---

## 19. Field evidence gate

Do not proceed to production XBOX profile integration merely because PR2.1 CI passes.

The next field report must contain at least one real positive match.

Minimum gate:

~~~text
Aniimo or Minecraft
→ live process opened
→ package identity resolved
→ exact Package object resolved
→ MicrosoftGame.config read
→ exact executable match
→ Active XBOX game
→ Alt+Tab retained
→ process exit cleared
~~~

If this succeeds, continue PR2 lifecycle validation.

If it still fails, inspect the exact package lookup/config-location evidence before changing architecture.

---

## 20. Final implementation principle

This is a resolver correction, not a redesign.

~~~text
LIVE PROCESS
    |
    +-- QueryFullProcessImageName
    |       → real payload path, e.g. D:
    |
    +-- GetPackageFullName
            |
            v
      PackageManager.FindPackageForUser("", fullName)
            |
            +-- EffectiveLocation
            +-- InstalledLocation
                    |
                    v
             MicrosoftGame.config
                    |
                    v
            exact executable match

GetPackagePathByFullName2
→ diagnostic path evidence only
~~~

Preserve the existing owner, lifecycle, transport, and Full1902 architecture exactly.
