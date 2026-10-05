# Work Order — XBOX PR4: Promote Installed Catalog / Identity to Production and Retire Catalog Diagnostic

> **Date:** 2026-10-06  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed baseline:** `main@571264a85db27b17156bb1b832784ed63a871391`  
> **Architecture authority:** `docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md`  
> **Full1902 authority:** `docs/Full 1902 Implementation/README.md` and the active Full1902 documents it references  
> **PoC A implementation history:** PR #678  
> **PoC B implementation history:** PR #682 + PR #684 + PR #686  
> **Front-button Xbox action:** PR #688 merged; field validation remains separate and does not gate this PR  
> **Scope:** production XBOX installed-game identity/catalog foundation + complete retirement of the installed-catalog diagnostic surface  
> **Out of scope:** XBOX Main App page, frontend production catalog transport, XBOX profile persistence, production active-session runtime, profile application, per-game M1/M2, Overlay XBOX projection

---

## 1. Goal

Promote the already field-validated XBOX installed-game catalog / identity logic out of the Developer diagnostic namespace into one production authority.

At the end of this PR:

~~~text
current-user package enumeration
→ Effective / Installed package locations
→ MicrosoftGame.config
→ required Identity
→ ExecutableList
→ canonical Xbox key
→ production XboxGameIdentity
→ production installed XBOX catalog result
~~~

must exist without depending on:

~~~text
XboxCatalogDiagnostic
FrontendXboxCatalogDiagnostic*
RunXboxCatalogDiagnostic
XboxCatalogDiagnosticPage
Developer Menu XBOX Catalog Diagnostic
diagnostic text report generation
~~~

The XBOX active-game **session diagnostic remains** in this PR.

Its validated WinEvent/process-lifetime behavior is still the only implementation of PoC B and must not be removed until the later production `XboxGameSessionRuntime` PR.

---

## 2. Why this PR exists now

PoC A has already completed its purpose on the real MSI Claw.

Field validation proved:

~~~text
PackageManager.FindPackagesForUser("")
→ current-user packages enumerable

Package.EffectiveLocation / InstalledLocation
→ MicrosoftGame.config readable

Aniimo Legend
→ store:9PK8PHLCQDF6

Minecraft for Windows
→ store:9NBLGGH2JHXJ

actual game payload on D:
+ package/config metadata on C:
→ valid and expected
~~~

The production architecture no longer needs a second Developer-only scanner that owns the same package/config/key logic.

Project policy is:

> When a PoC is promoted to production, remove the parallel diagnostic owner unless it still provides unique evidence needed by a later unresolved lifecycle question.

PoC A no longer provides unique unresolved evidence.

PoC B still does.

Therefore:

~~~text
PR4
PoC A → production
Catalog Diagnostic → deleted

later X3 production-session PR
PoC B → production XboxGameSessionRuntime
Active Session Diagnostic → deleted then
~~~

Do not keep both production catalog and diagnostic catalog implementations in parallel.

---

## 3. Locked architecture boundaries

This PR must preserve all existing domain boundaries.

### Steam remains untouched

Do not change:

~~~text
SteamSessionRuntime.ActualRunningAppId
ProfileDocument.Games
Steam numeric AppID contracts
ScanProfileGamesAsync
existing Steam profile UI/runtime
~~~

### Full1902 remains untouched

Do not change:

~~~text
PID1901 / PID1902 ownership
DirectInput ownership
HidHide
VIIPER
Xbox360 ↔ SteamDeck presentation policy
Sleep/Hibernate controller lifecycle
Restart/Shutdown controller lifecycle
WING / Center M ownership
WinGSuppressionGuard
~~~

### XBOX active-session diagnostic remains diagnostic

Do not convert in this PR:

~~~text
XboxGameSessionDiagnostic
WinEvent hooks
process-generation tracking
process-exit ownership
bounded startup/resume session reconcile
FrontendXboxSessionDiagnostic*
XboxSessionDiagnosticPage
~~~

Only replace its dependency on the **catalog diagnostic's parser/key helper** with the new production identity authority.

---

## 4. Current code problem to remove

Current production-independent code is coupled like this:

~~~text
Diagnostics/XboxCatalog/XboxCatalogDiagnostic.cs
    ├─ current-user package enumeration
    ├─ package location extraction
    ├─ MicrosoftGame.config scan
    ├─ canonical candidate-key derivation
    ├─ diagnostic counters
    ├─ diagnostic truncation
    ├─ diagnostic text report
    └─ diagnostic frontend DTO conversion

Diagnostics/XboxCatalog/MicrosoftGameConfigReader.cs
    └─ already reused by Active Session Diagnostic

Diagnostics/XboxSession/XboxGameProcessIdentityEvaluator.cs
    ├─ MicrosoftGameConfigReader
    └─ XboxCatalogDiagnostic.CreateCandidateKey(...)
~~~

The last dependency is especially important:

~~~csharp
XboxCatalogDiagnostic.CreateCandidateKey(...)
~~~

means PoC B currently depends on a PoC A diagnostic class for canonical identity.

PR4 must remove that dependency before deleting `XboxCatalogDiagnostic`.

---

## 5. Target production shape

Use one small production namespace/folder.

Recommended:

~~~text
src/SteamInputAddonforClaw/Xbox/
    MicrosoftGameConfigReader.cs
    XboxGameIdentity.cs
    XboxInstalledGameCatalog.cs
~~~

Do not create a large subsystem.

Do not add:

~~~text
XboxCatalogManager
XboxPlatformService
XboxPackageRepository
XboxGameIdentityFramework
IGamePlatform
IGameCatalogProvider
UniversalGameIdentity
provider/plugin registries
generic platform abstractions
~~~

Three narrow production files are sufficient.

If implementation can stay equally clear with two files, that is also acceptable.

The goal is not a specific file count.

The goal is one production identity/parser/catalog authority.

---

## 6. Promote MicrosoftGame.config parsing to production

Move the existing validated parser out of:

~~~text
SteamInputAddonforClaw.Diagnostics.XboxCatalog
~~~

into the production XBOX namespace.

Recommended namespace:

~~~csharp
namespace SteamInputAddonforClaw.Xbox;
~~~

Retain the proven parser behavior:

~~~text
DTD prohibited
XmlResolver = null
bounded XML size
recognized roots:
  Game
  MicrosoftGame

required:
  Identity.Name
  Identity.Publisher
  ExecutableList with >= 1 usable Executable.Name

optional:
  ShellVisuals.DefaultDisplayName
  Identity.ResourceId
  StoreId
  TitleId
  Executable attributes:
    Id
    TargetDeviceFamily
    Architecture
~~~

Do not loosen validation.

Do not add schema guessing or fuzzy XML handling.

Rename diagnostic-oriented model names where useful.

Recommended:

~~~csharp
internal sealed record XboxGameExecutable(
    string Name,
    string? Id,
    string? TargetDeviceFamily,
    string? Architecture);

internal sealed record MicrosoftGameConfig(
    string? DefaultDisplayName,
    string IdentityName,
    string IdentityPublisher,
    string? IdentityResourceId,
    string? StoreId,
    string? TitleId,
    IReadOnlyList<XboxGameExecutable> Executables);
~~~

The parser is production identity infrastructure now.

It must not retain a dependency on frontend diagnostic DTOs.

---

## 7. Add the production XboxGameIdentity authority

Add one production identity record.

Recommended shape:

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
    IReadOnlyList<XboxGameExecutable> Executables);
~~~

It may also carry another field only if a real next-phase consumer already requires it.

Do not put transient data into the stable identity:

~~~text
PID
HWND
versioned PackageFullName
installation path
config path
current process path
~~~

Those are observations, not canonical identity.

### Canonical key policy — one authority only

Move the current field-proven key derivation out of `XboxCatalogDiagnostic.CreateCandidateKey`.

Recommended as a static member/helper immediately adjacent to `XboxGameIdentity`:

~~~csharp
internal static string CreateKey(
    string? storeId,
    string? packageFamilyName,
    MicrosoftGameConfig config)
{
    if (!string.IsNullOrWhiteSpace(storeId))
        return $"store:{storeId.Trim()}";

    if (!string.IsNullOrWhiteSpace(packageFamilyName))
        return $"pfn:{packageFamilyName.Trim()}";

    static string Normalize(string value) =>
        Uri.EscapeDataString(value.Trim().ToUpperInvariant());

    return $"identity:{Normalize(config.IdentityName)}|{Normalize(config.IdentityPublisher)}|{Normalize(config.IdentityResourceId ?? string.Empty)}";
}
~~~

The exact method placement may differ.

The policy may not.

Required precedence remains:

~~~text
valid StoreId
→ store:<StoreId>

else PackageFamilyName
→ pfn:<PFN>

else required config Identity tuple
→ identity:<normalized Name|Publisher|ResourceId>
~~~

TitleId remains metadata only.

Do not promote TitleId to fallback authority.

---

## 8. Production installed-game catalog model

The catalog needs to distinguish stable identity from the current installed-package observation.

Recommended:

~~~csharp
internal sealed record XboxInstalledGameCatalogEntry(
    XboxGameIdentity Identity,
    string PackageName,
    string PackageFullName,
    string ConfigPath);
~~~

A package-location kind/root may be carried if genuinely useful for logging/debugging, but do not persist it into `XboxGameIdentity`.

Recommended top-level outcome:

~~~csharp
internal enum XboxInstalledGameCatalogOutcome
{
    Completed,
    Unavailable,
    Failed
}

internal sealed record XboxInstalledGameCatalogResult(
    XboxInstalledGameCatalogOutcome Outcome,
    IReadOnlyList<XboxInstalledGameCatalogEntry> Games,
    string? FailureReason);
~~~

A small internal skipped-count field is acceptable if it materially helps tests/logging.

Do not recreate the old diagnostic result shape with:

- UI truncation counts;
- report paths;
- diagnostic failure arrays;
- report-only package counters.

Production consumers need:

~~~text
Can the catalog source be used?
Which games were found?
If the source failed globally, why?
~~~

One bad package should not make the entire catalog unavailable.

---

## 9. Promote the validated package source, do not redesign it

Current PoC A already proved the supported Windows path:

~~~text
new PackageManager().FindPackagesForUser(string.Empty)
~~~

For every package:

~~~text
Package.Id
Package.DisplayName
Package.EffectiveLocation?.Path
Package.InstalledLocation?.Path
~~~

Use those semantics in production.

A narrow injectable source seam is justified because unit tests must not depend on locally installed Microsoft Store packages.

Recommended naming:

~~~text
IXboxInstalledPackageSource
WindowsXboxInstalledPackageSource
XboxInstalledPackage
XboxInstalledPackageLocation
XboxInstalledPackageEnumeration
~~~

Do not add dependency-injection infrastructure around it.

Constructor injection/default implementation, equivalent to the current diagnostic test seam, is enough.

### Preserve location behavior

Use:

~~~text
Effective
then Installed
~~~

with case-insensitive deduplication.

Do not require:

~~~text
running/game payload path
starts with
config/package metadata path
~~~

Real field evidence proved this false:

~~~text
game payload:
D:\xbox\...

config metadata:
C:\Program Files\WindowsApps\...
~~~

That cross-drive layout is normal.

---

## 10. Production scan behavior

Recommended class:

~~~csharp
internal sealed class XboxInstalledGameCatalog
{
    internal Task<XboxInstalledGameCatalogResult> ScanAsync(
        CancellationToken cancellationToken = default);
}
~~~

The implementation should be a direct promotion/simplification of the field-proven diagnostic scanner.

Required flow:

~~~text
ScanAsync
→ PackageManager.FindPackagesForUser(current user)
→ each package
→ Effective / Installed roots
→ look only for <root>\MicrosoftGame.config
→ bounded read
→ MicrosoftGameConfigReader
→ valid root + Identity + ExecutableList
→ XboxGameIdentity
→ deduplicate package/game observations
→ return installed games
~~~

### Ordinary Store app behavior

Most packages are not games and will not contain a valid `MicrosoftGame.config`.

That is not an error.

Expected:

~~~text
no MicrosoftGame.config
→ skip
~~~

Do not log every ordinary Store package at Info/Warn.

### Per-package failure

Examples:

~~~text
one package metadata read throws
one location inaccessible
config malformed
config required Identity missing
config has no usable executable
~~~

Policy:

~~~text
skip package
→ useful Debug/Warn only where appropriate
→ continue scan
~~~

Do not fail the whole catalog.

### Enumeration-source failure

If the top-level current-user enumeration itself fails:

~~~text
PlatformNotSupported / unavailable API
→ Unavailable

other enumeration failure
→ Failed
~~~

Return the global reason.

Do not invoke fallback sources.

---

## 11. Keep the field-proven safety limits

Retain the practical bounds from PoC A.

`MicrosoftGame.config` must remain bounded.

Current PoC A used approximately:

~~~text
2 MiB maximum config file
2,000,000 XML character limit
DTD prohibited
entities disabled
~~~

Keep equivalent limits.

Do not remove these limits just because the code is now production.

Do not introduce retries.

Package/config scanning occurs on explicit bounded requests in the later UI phase, not on a background timer.

---

## 12. Update Active Session Diagnostic to use production identity code

This is the only PoC B change allowed in PR4.

Current:

~~~csharp
using SteamInputAddonforClaw.Diagnostics.XboxCatalog;

...

XboxCatalogDiagnostic.CreateCandidateKey(
    read.Config.StoreId,
    evidence.PackageFamilyName,
    read.Config)
~~~

Target:

~~~text
production MicrosoftGameConfigReader
+ production XboxGameIdentity key authority
~~~

For example:

~~~csharp
using SteamInputAddonforClaw.Xbox;

...

XboxGameIdentity.CreateKey(
    read.Config.StoreId,
    evidence.PackageFamilyName,
    read.Config)
~~~

or the equivalent chosen production helper.

Do not otherwise modify:

~~~text
XboxGameProcessIdentityEvaluator matching policy
OpenProcess rights
QueryFullProcessImageName
package identity evidence
GetPackagePathByFullName2 diagnostic evidence
WinEvent source
process-generation cache
Alt+Tab behavior
process-exit behavior
startup reconcile
resume reconcile
session report
session UI
~~~

All of those have already passed target-device validation.

This is a dependency cleanup, not a PoC B redesign.

---

## 13. Retire the Runtime catalog diagnostic owner

Delete the production wiring that creates a new diagnostic scan on demand.

Current:

~~~csharp
runXboxCatalogDiagnostic:
    cancellationToken =>
        new XboxCatalogDiagnostic().RunAsync(cancellationToken)
~~~

Remove it from `AddonProcessHost`.

Remove from `InProcessAddonFrontendControl`:

~~~text
_runXboxCatalogDiagnostic
runXboxCatalogDiagnostic constructor parameter
RunXboxCatalogDiagnosticAsync(...)
~~~

Do not replace it with a hidden Developer production-scan command in this PR.

The production catalog will be exposed through the real XBOX page/frontend contract in the next focused PR.

---

## 14. Retire catalog diagnostic frontend contracts

Remove the catalog-diagnostic-only contracts from:

~~~text
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
~~~

Delete:

~~~text
FrontendXboxCatalogDiagnosticOutcome
FrontendXboxCatalogDiagnosticResult
FrontendXboxCatalogDiagnosticGame
FrontendXboxCatalogDiagnosticExecutable
FrontendXboxCatalogDiagnosticFailure
IAddonFrontendControl.RunXboxCatalogDiagnosticAsync(...)
~~~

Do **not** remove or alter:

~~~text
FrontendXboxSessionDiagnosticState
FrontendXboxSessionDiagnostic*
CaptureXboxSessionDiagnosticAsync
StartXboxSessionDiagnosticAsync
StopXboxSessionDiagnosticAsync
GenerateXboxSessionDiagnosticReportAsync
~~~

PoC B remains alive.

---

## 15. Retire the catalog diagnostic RPC cleanly

Remove:

~~~text
FrontendRpcMethod.RunXboxCatalogDiagnostic
NamedPipeAddonFrontendClient.RunXboxCatalogDiagnosticAsync
NamedPipeAddonFrontendServer dispatch branch
server no-payload validation reference
transport test fixture method/result
~~~

### Protocol version

`FrontendRpcMethod` is serialized by **exact string name**, not by numeric enum value.

Therefore no numeric reserved slot is required.

However, removing a supported method changes the wire contract.

Bump:

~~~text
FrontendTransportProtocol.CurrentVersion
52 → 53
~~~

and document:

~~~text
Version 53:
retire the completed XBOX installed-catalog diagnostic RPC/contracts.
The production XBOX catalog frontend contract will be introduced separately.
A v52 peer must fail the handshake rather than invoke a removed developer RPC.
~~~

Do not add a compatibility forwarding shim.

The product is pre-release and the existing transport policy already uses version bumps for removed RPCs.

---

## 16. Remove the Developer catalog diagnostic UI completely

Delete:

~~~text
src/SteamInputAddonforClaw.UI/Views/XboxCatalogDiagnosticPage.xaml
src/SteamInputAddonforClaw.UI/Views/XboxCatalogDiagnosticPage.xaml.cs
~~~

Remove from Developer page:

~~~text
XBOX Catalog Diagnostic SettingsCard
XboxCatalogDiagnosticRequested event
OpenXboxCatalogDiagnosticButton_Click
~~~

Keep:

~~~text
XBOX Active Game Diagnostic
~~~

Remove catalog diagnostic navigation state:

~~~text
MainNavigationPage.XboxCatalogDiagnostic
OpenXboxCatalogDiagnostic()
~~~

Remove from `MainWindow.xaml`:

~~~text
<views:XboxCatalogDiagnosticPage ... />
~~~

Remove from `MainWindow.xaml.cs`:

~~~text
DeveloperMenuContent.XboxCatalogDiagnosticRequested wiring
XboxCatalogDiagnosticContent.Initialize(...)
BackRequested wiring
wasXboxCatalogDiagnostic
visibility branch
Activate / Deactivate branch
~~~

No placeholder page/card should remain.

Do not rename the Active Session diagnostic to compensate.

---

## 17. Delete diagnostic-only text-report machinery

The production installed catalog must not write:

~~~text
xbox-catalog-diagnostic-*.txt
~~~

Remove:

~~~text
report builder
report path
report failure handling
UI game/failure truncation
512 KiB frontend diagnostic result shrinking
MaximumUiGames
MaximumUiFailures
MaximumUiExecutablesPerGame
MaximumReportFailures
diagnostic display-string truncation
diagnostic package/config summary counters that have no production consumer
~~~

Useful operational logging may remain, but it must describe production catalog behavior.

Recommended log category:

~~~text
XboxCatalog
~~~

not:

~~~text
XboxCatalogDiagnostic
~~~

Suggested Info boundary:

~~~text
scan requested/completed
Outcome
InstalledGameCount
SkippedPackageCount (optional)
~~~

Per-package negatives should normally be Debug unless they represent a notable access/parse failure.

Do not recreate a durable report elsewhere.

---

## 18. Tests — convert evidence, do not throw it away

The existing `XboxCatalogDiagnosticTests` contain valuable production behavior tests.

Do not simply delete all of them.

Rename/refactor the production-relevant coverage into, for example:

~~~text
XboxInstalledGameCatalogTests
MicrosoftGameConfigReaderTests
~~~

or one focused test file if clearer.

Required production tests:

### Parser

1. reads required Identity;
2. reads optional StoreId / TitleId / ResourceId;
3. reads all usable Executable entries;
4. accepts `Game` root;
5. accepts `MicrosoftGame` root;
6. accepts XML namespaces while comparing local names;
7. rejects missing Identity;
8. rejects missing usable executable;
9. rejects unrecognized root;
10. rejects malformed XML;
11. DTD/entity unsafe input remains blocked.

### Canonical identity/key

12. StoreId wins over PFN;
13. PFN wins over config Identity fallback;
14. identity fallback normalization remains stable;
15. package version change does not change a PFN-based key;
16. display-name change does not change a key;
17. install/config path change does not change a key.

### Catalog

18. zero packages → Completed + empty games;
19. valid game config → one production catalog entry;
20. ordinary package with no config → skipped without scan failure;
21. malformed config does not abort later valid packages;
22. duplicate package observations do not duplicate game rows;
23. Effective + Installed same path is probed once;
24. config only in Installed fallback is accepted;
25. D: payload assumptions are not introduced; config root may be any package metadata path;
26. top-level enumeration failure → Failed/Unavailable as appropriate;
27. cancellation before/during enumeration is honored;
28. one package metadata/location failure does not abort the rest.

### Session dependency

29. Active Session Diagnostic tests continue to pass using the production parser/key authority;
30. exact executable matching remains case-insensitive and exact-basename only;
31. helper rejection remains unchanged.

---

## 19. Delete diagnostic-only tests

Delete tests whose only purpose was the retired Developer surface:

~~~text
XboxCatalogDiagnosticFrontendTests.cs
XboxCatalogDiagnosticUiTests.cs
~~~

Remove catalog-diagnostic-only cases/fixtures from:

~~~text
FrontendNamedPipeTransportTests.cs
developer navigation/UI tests
MainWindow diagnostic navigation tests
~~~

Do not weaken shared transport tests for the remaining session diagnostic.

Add a source/contract assertion if useful that production source no longer contains:

~~~text
RunXboxCatalogDiagnostic
FrontendXboxCatalogDiagnostic
XboxCatalogDiagnosticPage
~~~

Do not require historical docs to lose those strings.

Historical work orders remain historical evidence.

---

## 20. Production code must have no dependency on Developer diagnostic contracts

After this PR, the dependency direction must be:

~~~text
SteamInputAddonforClaw.Xbox
    ↓
production MicrosoftGame.config parser
production XboxGameIdentity
production XboxInstalledGameCatalog

Diagnostics.XboxSession
    ↓
reuses production parser + identity key

Developer frontend/session diagnostic
    ↓
still only PoC B
~~~

Forbidden:

~~~text
production Xbox catalog
→ FrontendXboxCatalogDiagnostic*

production Xbox identity
→ Developer UI

XboxSessionDiagnostic
→ XboxCatalogDiagnostic
~~~

---

## 21. What PR4 intentionally does NOT wire yet

PR4 does not add the final Main App XBOX catalog transport.

Do not add yet:

~~~text
FrontendXboxGameCatalogEntry
ScanXboxGamesAsync
XBOX top-level page
Profile → Steam visible rename
favorites
XboxGames persistence
profile editor
~~~

Those belong to the next focused PR.

Reason:

This PR has one cleanup/authority objective:

> establish one production identity/catalog core and remove the completed PoC A surface.

The following PR can then expose that single production catalog through a clean XBOX-specific frontend contract without carrying any diagnostic baggage.

---

## 22. No background owner / polling

Do not instantiate a permanent catalog scanner in `AddonProcessHost` solely because the class now exists.

The catalog is request-driven.

Target lifecycle for the next UI PR remains:

~~~text
XBOX page activation / explicit Refresh
→ one bounded XboxInstalledGameCatalog.ScanAsync()
→ return result
→ stop
~~~

Do not add:

~~~text
PackageCatalog listener
timer
polling
watchdog
background cache owner
catalog singleton manager
periodic refresh
~~~

If later UX evidence requires live install/uninstall updates, solve that separately.

---

## 23. Failure policy

Production catalog failure must stay isolated.

### Source failure

~~~text
PackageManager enumeration fails
→ catalog result Failed/Unavailable
→ Steam unaffected
→ Full1902 unaffected
→ Active Session Diagnostic unaffected
~~~

### One package fails

~~~text
skip package
→ continue
~~~

### Config malformed

~~~text
skip package
→ continue
~~~

### No game packages installed

~~~text
Completed
Games = []
~~~

No destructive profile behavior exists yet in PR4.

Do not add fallback:

~~~text
PowerShell
Xbox private DB
registry PackageRepository scraping
recursive C:/D: scan
WindowsApps ACL/ownership change
~~~

---

## 24. Real-device evidence that must remain treated as authoritative

PR4 is not another catalog PoC.

Do not require another diagnostic report to merge the production promotion.

Existing field evidence already proved the platform path.

Locked observations:

### Aniimo

~~~text
key:
store:9PK8PHLCQDF6

PFN:
KingsgloryGames.AniimoLegend_9d08hqzdedf04

Executable:
Aniimo.exe

actual payload:
D:\xbox\Aniimo Legend\Content\Aniimo.exe

config:
C:\Program Files\WindowsApps\KingsgloryGames.AniimoLegend_1.0.18.0_x64__9d08hqzdedf04\MicrosoftGame.config
~~~

### Minecraft

~~~text
key:
store:9NBLGGH2JHXJ

PFN:
Microsoft.MinecraftUWP_8wekyb3d8bbwe

Executable:
Minecraft.Windows.exe

actual payload:
D:\xbox\Minecraft for Windows\Content\Minecraft.Windows.exe

config:
C:\Program Files\WindowsApps\Microsoft.MinecraftUWP_1.26.5203.0_x64__8wekyb3d8bbwe\MicrosoftGame.config
~~~

Do not reintroduce path-prefix assumptions.

---

## 25. Acceptance criteria

PR4 is complete only when all are true:

1. production `MicrosoftGameConfigReader` exists outside `Diagnostics`;
2. production `XboxGameIdentity` exists;
3. canonical key derivation exists in production identity code;
4. production installed-game catalog scan exists;
5. catalog uses current-user `PackageManager.FindPackagesForUser(string.Empty)`;
6. Effective/Installed config-location semantics are preserved;
7. `Game` and `MicrosoftGame` roots remain accepted;
8. Identity + usable ExecutableList remain required;
9. StoreId → PFN → Identity key precedence is unchanged;
10. TitleId remains metadata only;
11. ordinary non-game packages are skipped;
12. one bad package does not abort the scan;
13. no PowerShell/private DB/disk-recursive fallback exists;
14. no polling/background catalog owner exists;
15. `XboxGameSessionDiagnostic` now consumes production parser/key code;
16. `XboxGameSessionDiagnostic` lifecycle behavior is otherwise unchanged;
17. `XboxCatalogDiagnostic` production class is deleted;
18. catalog diagnostic report generation is deleted;
19. catalog diagnostic frontend DTOs are deleted;
20. `RunXboxCatalogDiagnosticAsync` is deleted;
21. `FrontendRpcMethod.RunXboxCatalogDiagnostic` is deleted;
22. frontend protocol is bumped to v53 with a removal note;
23. catalog diagnostic named-pipe dispatch is deleted;
24. Developer Menu catalog diagnostic card/event is deleted;
25. `XboxCatalogDiagnosticPage.xaml/.cs` are deleted;
26. catalog diagnostic navigation state/wiring is deleted;
27. XBOX Active Game Session Diagnostic remains available;
28. production-relevant PoC A tests are migrated, not discarded;
29. diagnostic-only frontend/UI tests are deleted;
30. all Active Session Diagnostic tests still pass;
31. full solution build/tests pass;
32. Steam code path is unchanged;
33. Full1902 ownership/presentation code is unchanged;
34. Overlay is unchanged;
35. XBOX profile persistence is not added in this PR.

---

## 26. Review policy

Review this PR for real product regressions:

- production catalog identity mismatch;
- key instability;
- parser regression;
- package-location regression;
- accidental deletion/breakage of Active Session Diagnostic;
- transport protocol inconsistency;
- Main UI navigation breakage after removing the catalog page;
- Steam or Full1902 coupling.

Do not block for theoretical timing races.

This PR does not add a long-lived concurrent catalog owner.

There is no justification for:

~~~text
epoch
barrier
new state machine
catalog manager hierarchy
retry scheduler
cache invalidation framework
~~~

---

## 27. Follow-up after PR4

After PR4 merges, the next product PR should consume the production catalog.

Recommended next step:

~~~text
PR5
→ XBOX-specific frontend catalog contract
→ Runtime ScanXboxGamesAsync using XboxInstalledGameCatalog
→ Main navigation visible Profile → Steam
→ separate top-level XBOX page
→ read-only installed XBOX catalog first
~~~

Still no XBOX profile persistence in that PR unless separately approved.

Later:

~~~text
X2
→ XboxGames persistence/editing

X3
→ production XboxGameSessionRuntime
→ then retire XBOX Active Game Session Diagnostic
~~~

---

## 28. Final implementation principle

PR4 should finish with this ownership:

~~~text
PRODUCTION XBOX IDENTITY/CATALOG
SteamInputAddonforClaw.Xbox
    |
    +-- MicrosoftGameConfigReader
    +-- XboxGameIdentity
    +-- XboxInstalledGameCatalog

ACTIVE SESSION POC — temporarily retained
Diagnostics.XboxSession
    |
    +-- uses production config parser
    +-- uses production canonical key
    +-- keeps validated WinEvent/process-lifetime diagnostic behavior

RETIRED
Diagnostics.XboxCatalog.XboxCatalogDiagnostic
Developer XBOX Catalog Diagnostic page
catalog diagnostic RPC/contracts/report
~~~

One parser.

One canonical-key authority.

One installed-catalog scanner.

No parallel catalog diagnostic implementation.
