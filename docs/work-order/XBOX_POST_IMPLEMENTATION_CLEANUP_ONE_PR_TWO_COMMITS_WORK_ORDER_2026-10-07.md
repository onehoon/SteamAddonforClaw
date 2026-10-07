# Work Order — XBOX Post-Implementation Cleanup: One PR / Two Commits

> **Date:** 2026-10-07  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed main:** `main@adde656462b5f7bd2bf15e2a217891f93d939976`  
> **Primary architecture authority:** `docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md`, especially section 24A  
> **Full1902 authority:** `docs/Full 1902 Implementation/README.md` and the documents it orders  
> **PR shape:** one pull request containing **two reviewable implementation commits**  
> **Merge policy:** the PR branch keeps the two logical commits for review, but the repository's normal PR policy still uses **Squash Merge** after review/CI acceptance  
> **Scope:** close the two remaining XBOX post-implementation cleanup items identified by the 2026-10-07 audit: (1) Main App pending TDP/FPS slider edits must not be lost merely because the user leaves the XBOX top-level page, with the Main App FPS trailing delay normalized to 300 ms; (2) remove retired diagnostic-era fields from production-internal XBOX DTOs after identity/classification has already completed  
> **Not in scope:** detector redesign, polling, multi-game arbitration, same-key launcher handoff, ProfileStore/schema changes, Frontend/Overlay protocol changes, M1/M2 architecture changes, Developer-menu diagnostic removal, Full1902 controller ownership changes

---

## 1. Goal

Finish the XBOX feature family with one small cleanup PR while preserving the already-completed production architecture.

The PR has two independent responsibilities:

~~~text
Commit 1
Main App profile-editor behavior cleanup
→ pending XBOX TDP/FPS edit survives ordinary top-level navigation
→ true profile-context retirement still cancels the pending edit
→ Main App Steam/XBOX FPS debounce becomes 300 ms

Commit 2
Production-internal XBOX DTO cleanup
→ remove diagnostic-era fields that are no longer consumed after classification
→ preserve identity proof, useful logging, catalog/session behavior, frontend contracts
~~~

Do not combine the two concerns inside the same commit.

Do not use this cleanup PR to reopen XBOX game detection, profile ownership, Overlay architecture, M1/M2 authority, or unsupported multi-game behavior.

---

## 2. Mandatory product constraints

The implementation must continue to respect the supported product model:

- one Windows user;
- one interactive session;
- no Fast User Switching / RDP / multi-session support;
- event-driven XBOX game detection;
- no periodic process/window polling;
- Steam remains authoritative from numeric RunningAppID/AppID;
- XBOX remains authoritative from its canonical string identity;
- Steam wins over XBOX in `ActiveProfileTarget` while a Steam RunningAppID is active;
- CPU/TDP/Power/FPS/Resolution keep using the same shared runtime owners;
- non-Steam per-game M1/M2 keeps using the one existing Xbox360 publisher path;
- Steam per-game controller mapping remains Steam Input-owned;
- Full1902 PID1902 / HidHide / VIIPER / WING suppression lifecycle is unchanged.

This PR must not add a new manager, state machine, background worker, timer service, authority layer, profile store, process graph, or retry framework.

---

# Commit 1 — preserve pending Main App profile edits across ordinary navigation

## 3. Problem

The Main App XBOX page currently schedules slider mutations locally:

~~~text
TDP
→ 300 ms trailing delay

FPS
→ 275 ms trailing delay
~~~

but `XboxPage.Deactivate()` currently does all of the following:

~~~csharp
_active = false;
CancelScan();
CancelCapture();
CancelTdpDebounce();
CancelFpsDebounce();
~~~

`MainWindow.ShowPage(...)` calls `XboxContent.Deactivate()` whenever the user leaves the XBOX top-level page.

Therefore this normal user sequence is currently reachable:

~~~text
user changes XBOX TDP/FPS slider
→ UI shows the new value
→ user immediately selects another top-level page
→ XboxPage.Deactivate()
→ pending debounce is cancelled
→ visible edit never reaches persistence/runtime mutation
~~~

This is a real user-visible lost-edit path, not a theoretical timing race.

The completed Overlay has a different policy: normal user navigation/dismissal preserves valid pending edits, while actual context retirement cancels them.

Main App XBOX should follow the same semantic distinction without importing the Overlay binding architecture.

---

## 4. Required behavior

### 4.1 Ordinary top-level navigation must not retire the edit context

Leaving the XBOX top-level page for Device / Controller / Steam / Overlay / Shortcut / Settings must:

- mark the XBOX UI inactive;
- cancel in-flight catalog scan work;
- cancel in-flight profile capture work;
- **not cancel an already-scheduled TDP/FPS mutation** solely because the page became hidden.

Recommended minimal shape:

~~~csharp
internal void Deactivate()
{
    _active = false;
    CancelScan();
    CancelCapture();

    // Do not cancel TDP/FPS drafts here.
    // A top-level page visibility change is not a profile-context change.
}
~~~

Do not make MainWindow navigation asynchronous just to wait for a 300 ms debounce.

Do not add a global pending-edit coordinator.

### 4.2 A scheduled edit must be allowed to persist while the page is hidden

The current delayed submit methods reject work when `_active == false`.

That condition must no longer prevent the already-admitted mutation from reaching the existing typed frontend RPC.

For XBOX FPS, the effective logic should remain conceptually:

~~~csharp
await Task.Delay(300, token);

if (generation != currentGeneration
    || _frontend is null
    || _selectedGame is null)
{
    return;
}

var key = _selectedGame.Key;
var result = ac
    ? await _frontend.SetXboxGameProfileFpsLimitAcAsync(key, value)
    : await _frontend.SetXboxGameProfileFpsLimitDcAsync(key, value);

// Hidden page: persistence/apply already completed, but do not render hidden/stale UI.
if (!IsCurrentProfileResponse(_active, _selectedGame?.Key, key, result.Snapshot.Key))
    return;

Render(result.Snapshot);
~~~

For XBOX TDP, apply the same admission rule: page visibility must not cancel the already-scheduled persistence operation.

### 4.3 TDP draft-dirty state must settle even if the mutation completes while hidden

This point is important.

Today the TDP method clears `_tdpDraftDirty` only after the active-page response guard. If the mutation is allowed to complete while hidden but returns before clearing the dirty state, re-entering XBOX can preserve an obsolete local draft even though the mutation already succeeded.

Reorder the post-mutation logic so generation/draft settlement happens before the UI-render guard.

Conceptually:

~~~csharp
var result = await _frontend.SetXboxGameProfileTdpAsync(...);

var preserveDraft = ShouldPreserveDirtyTdpDraft(
    _tdpDraftDirty,
    generation,
    Volatile.Read(ref _tdpGeneration));

if (!preserveDraft)
    _tdpDraftDirty = false;

if (!IsCurrentProfileResponse(_active, _selectedGame?.Key, key, result.Snapshot.Key))
    return;

Render(result.Snapshot, preserveDraft);
~~~

Do not render the hidden page.

Do not let a completed current-generation mutation remain marked as an unsaved local draft.

### 4.4 Real profile-context retirement must still cancel pending drafts

Keep the existing cancellation boundaries for actual context changes.

At minimum, these paths must continue to cancel pending XBOX TDP/FPS work:

~~~text
SelectGameAsync(...)
→ selected XBOX game is changing

ClearSelection()
→ selected XBOX game is being retired

ReturnToCatalog()
→ calls ClearSelection()

catalog refresh proves the selected game is no longer installed/available
→ ReturnToCatalog()
→ ClearSelection()
~~~

This prevents an edit admitted for game A from being submitted later against game B.

Do not weaken those boundaries.

### 4.5 Profile-enabled state transitions keep their existing safety behavior

The existing XBOX profile-enabled mutation cancels a pending TDP draft before changing the profile's enabled state.

Keep that behavior unless the current implementation proves the equivalent cancellation happens through another existing context-retirement path.

Do not allow a delayed TDP edit captured under one profile-enabled state to resurrect settings after the user just disabled the profile.

### 4.6 Main App FPS trailing delay becomes 300 ms

Normalize the Main App profile FPS delay from 275 ms to 300 ms.

Apply this to both:

- `ProfilePage.xaml.cs` (Steam Main App);
- `XboxPage.xaml.cs` (XBOX Main App).

After this commit:

~~~text
Main App Steam
  TDP = 300 ms
  FPS = 300 ms

Main App XBOX
  TDP = 300 ms
  FPS = 300 ms

Overlay Quick Settings
  slider rows = existing TrailingDebounce300
~~~

Do not introduce a debounce service/manager only to share this number.

A tiny UI-only constant is acceptable if it genuinely makes the edited code clearer, but duplicated `300` literals are also acceptable. Prefer the simpler result.

### 4.7 M1/M2 behavior is unchanged

Do not alter the Main App XBOX rear-button mapping save chain.

It remains:

~~~text
Use global toggle / M1 / M2 edits
→ whole BackButtonMappingSettings record
→ ordered _backButtonSaveChain
→ SetXboxGameProfileBackButtonMappingAsync(...)
~~~

No debounce is required there.

Do not copy the Overlay's grouped 300 ms M1/M2 draft policy into the Main App ComboBox editor.

---

## 5. Commit 1 tests

Update/add tests in the existing UI test project rather than creating a new test assembly.

The existing `XboxCatalogPageUiTests` already locks much of this behavior and should be updated intentionally.

Required assertions:

1. `XboxPage.Deactivate()` still:
   - sets `_active = false`;
   - calls `CancelScan()`;
   - calls `CancelCapture()`.

2. `XboxPage.Deactivate()` no longer:
   - calls `CancelTdpDebounce()`;
   - calls `CancelFpsDebounce()`.

3. `SelectGameAsync(...)` still cancels both TDP and FPS pending edits before changing the selected game.

4. `ClearSelection()` still cancels both pending edit families.

5. XBOX delayed TDP/FPS submit paths do not use page inactivity by itself as a pre-submit rejection condition.

6. The returned XBOX mutation snapshot is rendered only when `IsCurrentProfileResponse(...)` still proves the page/selected-key/response-key context.

7. Current-generation XBOX TDP completion clears the dirty flag even if the page is inactive when the result returns.

8. Steam Main App and XBOX Main App FPS trailing delay are both 300 ms.

9. Existing XBOX M1/M2 ordered whole-record save-chain tests remain unchanged and passing.

10. Existing stale scan/capture response guards remain unchanged.

Do not add sleeps to tests when deterministic state/source-level assertions are available.

---

## 6. Commit 1 recommended commit message

~~~text
fix(xbox-ui): preserve pending profile edits across navigation
~~~

The commit may include the Steam Main App 275 → 300 ms normalization because it is part of the same debounce-policy cleanup.

Do not include DTO slimming in this commit.

---

# Commit 2 — remove retired diagnostic-era XBOX DTO payload

## 7. Goal

The old Developer-only XBOX installed-catalog/session diagnostics are already retired.

Production still carries a small amount of evidence in final internal records after that evidence has served its classification purpose.

Remove only fields that have no post-classification production consumer.

Do not remove evidence required to:

- prove XBOX/GDK identity;
- compute the canonical key;
- match the live executable;
- locate/read `MicrosoftGame.config`;
- produce useful bounded acceptance/failure logs.

The cleanup is about **not retaining dead data after classification**, not about weakening the identity proof.

---

## 8. `XboxGameIdentity` slimming

Current record:

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
    IReadOnlyList<XboxGameExecutable> Executables)
~~~

The production post-classification consumers require:

- `Key`;
- `DisplayName`;
- `StoreId` for existing catalog acceptance logging;
- `PackageFamilyName` for existing catalog acceptance logging.

The following retained fields are diagnostic-era dead payload after construction:

- `TitleId`;
- `IdentityName`;
- `IdentityPublisher`;
- `IdentityResourceId`;
- `Executables`.

Target shape:

~~~csharp
internal sealed record XboxGameIdentity(
    string Key,
    string DisplayName,
    string? StoreId,
    string? PackageFamilyName)
{
    internal static string CreateKey(
        string? storeId,
        string? packageFamilyName,
        MicrosoftGameConfig config)
    {
        ...
    }
}
~~~

Important:

`CreateKey(...)` still needs the parsed config fallback fields for the `identity:` key form.

Do **not** delete those fields from `MicrosoftGameConfig` or its parser.

Do **not** weaken key precedence:

~~~text
StoreId present
→ store:<StoreId>

else PackageFamilyName present
→ pfn:<PackageFamilyName>

else
→ identity:<IdentityName>|<Publisher>|<ResourceId>
~~~

Only the final retained DTO becomes smaller.

---

## 9. `XboxInstalledGameCatalogEntry` slimming

Current record:

~~~csharp
internal sealed record XboxInstalledGameCatalogEntry(
    XboxGameIdentity Identity,
    string PackageName,
    string PackageFullName,
    string ConfigPath);
~~~

The production frontend converts the accepted catalog entry to:

~~~text
Identity.Key
Identity.DisplayName
Favorite
~~~

and does not consume the accepted entry's retained:

- `PackageName`;
- `PackageFullName`;
- `ConfigPath`.

Target:

~~~csharp
internal sealed record XboxInstalledGameCatalogEntry(
    XboxGameIdentity Identity);
~~~

Keep local package/config variables long enough to preserve current logging.

The catalog acceptance log should still contain useful evidence, for example:

~~~text
Key
PackageFullName
PackageFamilyName
StoreId
~~~

Do not move that evidence into another long-lived DTO merely to preserve the old shape.

---

## 10. `XboxGameProcessMatch` slimming

Current record:

~~~csharp
internal sealed record XboxGameProcessMatch(
    XboxGameIdentity Identity,
    uint ProcessId,
    string RunningProcessPath,
    string RunningExecutableName,
    string PackageFullName);
~~~

The production session runtime consumes:

- `Identity`;
- `ProcessId`;
- `RunningExecutableName`.

It does not consume after classification:

- `RunningProcessPath`;
- `PackageFullName`.

Target:

~~~csharp
internal sealed record XboxGameProcessMatch(
    XboxGameIdentity Identity,
    uint ProcessId,
    string RunningExecutableName);
~~~

The evaluator still receives and uses the full evidence while proving identity.

Keep the existing config-resolution debug logging that records, as applicable:

- live process path;
- package full name;
- Effective/Installed package location;
- selected config path;
- failure reason.

The point is to log evidence at the proof boundary and then discard dead payload.

---

## 11. Construction-site updates

Update only the direct construction sites required by the slimmer records.

Expected production files include:

- `src/SteamInputAddonforClaw/Xbox/XboxGameIdentity.cs`;
- `src/SteamInputAddonforClaw/Xbox/XboxInstalledGameCatalog.cs`;
- `src/SteamInputAddonforClaw/Xbox/Session/ActiveXboxGame.cs`;
- `src/SteamInputAddonforClaw/Xbox/Session/XboxGameProcessIdentityEvaluator.cs`.

Do not refactor the whole XBOX namespace while touching these constructors.

Do not rename unrelated types.

Do not move files.

Do not create a new "evidence" or "diagnostic payload" hierarchy.

The existing transient evidence records used *during* classification are legitimate and should remain if they are still consumed:

- `XboxGameProcessIdentityEvidence`;
- package/config location records;
- parsed `MicrosoftGameConfig`;
- installed-package enumeration records.

These are not dead merely because they contain diagnostic-looking fields.

---

## 12. Commit 2 test updates

Update existing tests to prove behavior, not removed storage.

### 12.1 Catalog tests

Tests that currently inspect retained `PackageName`, `PackageFullName`, or `ConfigPath` on `XboxInstalledGameCatalogEntry` should instead assert the actual product contract:

- accepted candidate count;
- canonical key;
- display name;
- StoreId/PFN key precedence as relevant;
- D: payload / C: package metadata separation still does not reject the game;
- malformed/unreadable package/config behavior remains unchanged;
- duplicate canonical keys remain deduplicated.

Do not keep dead fields solely because a test currently reads them.

### 12.2 Session/evaluator tests

Tests that currently assert `XboxGameProcessMatch.RunningProcessPath` or `PackageFullName` should instead assert:

- disposition is `Matched`;
- expected canonical key;
- expected display name;
- expected PID where relevant;
- expected `RunningExecutableName`;
- exact executable mismatch remains rejected;
- D: live process + C: package metadata still matches;
- package/config lookup failure dispositions remain unchanged.

### 12.3 Canonical key tests

Retain or add explicit tests for all key forms:

~~~text
StoreId
→ store:...

no StoreId + PFN
→ pfn:...

no StoreId + no PFN
→ identity:...
~~~

The DTO cleanup must not change persisted XBOX profile keys for already-supported titles.

### 12.4 Frontend/profile/Overlay regressions

Existing tests must continue to prove:

- XBOX installed catalog frontend still projects key/display/favorite;
- XBOX profile capture/mutation remains unchanged;
- active XBOX profile resolution remains unchanged;
- Overlay XBOX profile/M1M2 contract remains unchanged;
- Frontend protocol stays at the existing version;
- Overlay protocol stays at the existing version.

No protocol bump is expected because the trimmed types are internal implementation records, not transport DTOs.

---

## 13. Commit 2 recommended commit message

~~~text
refactor(xbox): trim retired diagnostic DTO payload
~~~

Do not include debounce/navigation behavior changes in this commit.

---

# Documentation closure

## 14. Update the XBOX architecture audit as part of the two commits

Update:

`docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md`

without rewriting the architecture.

After Commit 1, section 24A.2/24A.3 should record that the Main App lost-edit cleanup and 300 ms policy normalization are implemented.

After Commit 2, section 24A.4/24A.7 should record that diagnostic-era final DTO payload was trimmed.

Keep the deferred items explicit:

- physical lifecycle validation remains a later user-run validation task;
- final physical Xbox app front-button validation remains pending;
- same-key launcher/bootstrap → main-executable handoff remains evidence-triggered only.

Do not change those deferred items into implementation requirements for this PR.

---

# Explicit non-goals / anti-overengineering guard

## 15. Do not change XBOX detection architecture

No changes to:

- `XboxGameSessionRuntime` ownership model;
- WinEvent event types;
- process-generation cache;
- bounded startup/resume reconcile;
- process exit ownership;
- PID reuse handling;
- ConfigNegative retry policy;
- polling policy;
- second-positive-process policy.

Especially do not add:

- multi-game arbitration;
- process priority scoring;
- process graph;
- retry epochs;
- TTL cache;
- periodic rescan;
- timer-based detector.

The existing field-proven detector is not being reopened.

## 16. Do not implement launcher handoff without field evidence

Current behavior for a second different positive XBOX process remains unchanged.

If future physical evidence proves a supported title has an overlapping launcher/bootstrap → main process transition for the **same canonical key**, address that concrete path later with a narrow same-key rule.

This PR must not anticipate it.

## 17. Do not change Developer-menu diagnostics

Do not remove:

- Xbox360 rumble/vibration diagnostics;
- PID1902 input cadence diagnostic;
- GameInput System Button Probe;
- other Full1902 controller diagnostics.

They are not XBOX/Game Pass game-detection leftovers.

Do not remove the Frontend protocol history comments for versions 50/52/53/58. They document protocol evolution and are not runtime dead code.

## 18. Do not change M1/M2 architecture

No changes to:

- `BackButtonMappingSettings`;
- global fallback semantics;
- `XboxGameProfile.Controller.BackButtonMapping`;
- `_activeNonSteamBackButtonMappingOverride`;
- `CanonicalXbox360InputPublisher`;
- rear-button suppression/release-gate behavior;
- Steam Input ownership of Steam per-game mapping;
- Overlay XBOX grouped mapping mutation.

## 19. Do not change persistence or transport schemas

Expected:

~~~text
ProfileDocument.CurrentSchemaVersion
→ unchanged

FrontendTransportProtocol.CurrentVersion
→ unchanged

OverlayTransportProtocol.CurrentVersion
→ unchanged
~~~

This PR does not add/remove persisted profile fields.

It does not change named-pipe payload contracts.

---

# Required validation

## 20. Automated validation

Run:

~~~powershell
dotnet build .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\tests\SteamInputAddonforClaw.UiTests\SteamInputAddonforClaw.UiTests.csproj -c Release --no-restore
git diff --check
~~~

If the solution-level test command already includes the UI test project in the current repository configuration, the explicit UI test command may duplicate execution; it is still acceptable as a focused regression check.

## 21. Focused manual/code-review scenarios

Commit 1 review should reason through these normal UI sequences:

~~~text
XBOX game A selected
→ change TDP
→ immediately click Controller top-level page
→ delayed TDP mutation still persists
→ hidden XBOX page does not render

XBOX game A selected
→ change FPS
→ immediately click Device top-level page
→ delayed FPS mutation still persists
→ hidden XBOX page does not render

XBOX game A selected
→ change TDP/FPS
→ choose a different XBOX game before delay expires
→ old game's pending draft is cancelled

XBOX game A selected
→ change TDP/FPS
→ Back to XBOX catalog before delay expires
→ pending draft is cancelled by ClearSelection

XBOX game A selected
→ change TDP
→ disable profile before delay expires
→ pending TDP edit cannot re-apply after disable
~~~

Commit 2 review should prove that the final product facts are unchanged:

~~~text
installed catalog
→ same canonical keys/display names

live session match
→ same exact executable proof
→ same active key/display/PID

profile persistence
→ same XboxGames keys

Overlay/Main App
→ same frontend DTOs
→ same behavior
~~~

---

# Commit discipline

## 22. Required PR branch history

Before review, the implementation branch should present exactly these two logical implementation commits (commit hashes will naturally differ):

~~~text
1. fix(xbox-ui): preserve pending profile edits across navigation
2. refactor(xbox): trim retired diagnostic DTO payload
~~~

Tests and the relevant architecture-document update belong in the same commit as the behavior they verify/document.

Do not make a third "tests" commit.

Do not make a third "docs" commit.

If implementation corrections are required before review, amend/rework the relevant logical commit rather than mixing the concerns.

The eventual repository merge still follows the project policy:

~~~text
review clean
+ CI PASS
→ Squash Merge
~~~

The two-commit branch shape exists to make review easier; it does not change the final squash-merge policy.

---

# Acceptance criteria

The PR is complete only when all of the following are true:

1. Ordinary top-level navigation no longer discards an admitted XBOX TDP/FPS slider edit.
2. Hidden XBOX UI is not rendered by the eventual delayed response.
3. Current-generation TDP dirty state settles correctly even when the mutation completes while hidden.
4. Actual profile-context retirement still cancels pending XBOX TDP/FPS edits.
5. Main App Steam/XBOX FPS delays are both 300 ms.
6. XBOX M1/M2 save semantics are unchanged.
7. `XboxGameIdentity` no longer retains the five identified unused post-classification fields.
8. `XboxInstalledGameCatalogEntry` no longer retains PackageName/PackageFullName/ConfigPath.
9. `XboxGameProcessMatch` no longer retains RunningProcessPath/PackageFullName.
10. Identity proof, exact executable match, key precedence, catalog dedupe, active-session lifecycle, and logs remain intact.
11. No Frontend/Overlay protocol bump occurs.
12. No profile schema bump occurs.
13. No Developer-menu diagnostic is removed as part of this cleanup.
14. No detector arbitration/retry/polling machinery is introduced.
15. The XBOX architecture document marks both audit cleanup items implemented while keeping physical validation and evidence-triggered launcher handoff deferred.
16. Build/tests/diff-check pass.
17. The PR branch presents the two required logical commits and no mixed cleanup commit.

---

## Final implementation principle

This PR is cleanup, not a new architecture phase.

Preserve the existing authorities and remove only the two concrete rough edges found by the post-implementation review:

~~~text
normal navigation should not silently lose an admitted edit

and

classification evidence should not remain in final DTOs after nobody consumes it
~~~

Do that with the smallest changes that fit the existing code.
