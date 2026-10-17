# Work Order — Post-Audit Five Targeted Fixes (One PR, Five Commits)

> Date: 2026-10-17
> Repository: `onehoon/SteamAddonforClaw`
> Source reviewed on `main`: `82ce935aa898b6bd16799eea61e24883630b7fdb` (refresh `main` before implementation; resolve genuine drift)
> Audit reference: `C:\GoogleDrive\Addon\Audit\AUDIT_SUMMARY.md` and owner reports `01`, `05`, `06`, `07`
> Product: **standalone Full1902**, one administrator Windows user / one interactive session
> Implementation owner: local Codex; hardware/interactive validation owner: user after merge
> Delivery: **exactly one implementation PR, five separately meaningful commits in the order below**
> **Excluded:** Audit findings `03-001` and `09-001` (Runtime death / Task Scheduler reliability) — separate PR-B

## 1. Locked outcome

Fix only the following audit findings in one PR, keeping each change and its focused tests in a separate commit:

| Commit | Audit finding | Scope | Proposed commit subject |
|---|---|---|---|
| 1 | `01-001` | Prove physical DirectInput cleanup before Center M stock restoration | `fix(controller): require proven DirectInput cleanup before stock release` |
| 2 | `05-001` | Distinguish unreadable favorite state from explicitly non-favorited games in Steam and XBOX catalogs | `fix(profiles): show unavailable favorites when profile storage is unreadable` |
| 3 | `06-004` | Reject ambiguous relative executable paths for configurable front-button launch actions | `fix(buttons): require absolute paths for application actions` |
| 4 | `07-001` | Make the three identified Main App dialogs follow the selected theme | `fix(ui): use current theme for remaining Main App dialogs` |
| 5 | `07-002` | Localize ordinary prerequisite setup progress/failure UI to en-US and ko-KR | `fix(ui): localize prerequisite setup progress and errors` |

One PR is intentional: these are bounded changes without a new shared manager or controller authority. Maintain the five commit boundaries even if the final PR will be squash-merged.

## 2. Mandatory architecture and scope

Read the current documents in the precedence order defined by:

1. `docs/Full 1902 Implementation/README.md`
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md` — HidHide/startup authority
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md` — stock-release and lifecycle
4. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md` — Full1902 controller model
5. `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md` — current privilege model

Historical work orders are background only. If a historical statement conflicts with active policy, use the latter. Read the recent `docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md` for the independent Steam/XBOX catalog model when doing commit 2.

Non-negotiable constraints:

- Center M **Disabled** means durable Addon PID1902 ownership; Steam/BPM changes the single attached virtual presentation, not the physical owner.
- The only ordinary stock-authority release is explicit **Enable Center M and Restart**, plus the supported uninstall stock-restoration path. The full ordered release must fail closed.
- Keep exact physical target identity, HidHide normalization, VIIPER ownership, and existing orderly teardown unchanged except for the explicit cleanup proof in commit 1.
- Main UI remains frontend-only; Runtime owns authoritative state and device actions.
- This is **not** a CTW integration or multi-session project. No RDP/Fast User Switching/cross-user behavior, supervisor/service, new generic state machine, broad refactor, or speculative race-defense work.
- Do not change `03-001`, `09-001`, `02-001`, `02-002`, `09-002`, WING suppression, optional A2VM boot-rumble defaults, VIIPER attach/ABI, or publisher cadence in this PR.
- Do not turn hardware validation into a coding, review, CI, or merge requirement. Local Codex owns code and deterministic tests; the user owns physical-device tests after merge.

## 3. Commit 1 — Audit 01-001: verified physical-input cleanup before stock release

### Confirmed code path

- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs` — `ReleaseForCenterMEnableAsync()` -> `ReleaseToXInputAsync()` currently calls `_inputSource.StopAsync()`, checks only `IsRunning`, clears the live session, and proceeds toward PID1901.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputSource.cs` — `StopAndConfirmCleanupAsync()` already checks the source's completed cleanup outcome, not merely worker exit. Failed Unacquire/dispose is a supported negative result.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputContracts.cs` — `IMsiClawPreparedInputSource` already exposes the cleanup-confirmation API.
- `src/SteamInputAddonforClaw/CenterMStartup/CenterMRebootAuthorityTransition.cs` — `RestoreStockAuthorityCoreAsync()` already stops subsequent stock-baseline/HidHide/Center M mutations on a failed physical release.

### Required behavior

In the **existing** release gate, replace the stop-only success decision with the existing cleanup-confirmation result, and require that the input source is no longer running. If cleanup throws or cannot be positively proven:

- return `PhysicalOwnershipReleaseResult` failure with a clear `DirectInputCleanupUnproven`-type reason and the **exact previously recorded HiddenTargets**;
- do not clear the live ownership evidence as if cleanup succeeded;
- do not issue the PID1902 -> PID1901 command;
- do not advance the outer stock-restoration flow to independent PID1901 proof, HidHide release, Center M task/service mutation, or restart.

Illustrative local change (adapt logging and state ordering to the actual implementation):

```csharp
if (_ownsInputSource)
{
    bool cleanupProven;
    try
    {
        cleanupProven = await _inputSource.StopAndConfirmCleanupAsync().ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        AppLog.Warn("ControllerOwnership", "DirectInput stock-release cleanup failed.", ex);
        return new(false, "DirectInputCleanupUnproven", targets);
    }

    if (!cleanupProven || _inputSource.IsRunning)
        return new(false, "DirectInputCleanupUnproven", targets);

    _ownsInputSource = false;
    ClearLivePhysicalSession();
}
```

This is an **illustration**, not permission to duplicate the cleanup mechanism or change the earlier virtual-presentation retirement order. Preserve existing `_releasedForEnable` lifecycle/fail-closed semantics; do not force reacquisition after a failed stock-release attempt.

### Tests

Extend:

- `tests/SteamInputAddonforClaw.Tests/MsiClawAddonPhysicalOwnershipTests.cs`: cleanup result false even though `IsRunning == false`; exception during stop/confirmation; no PID1901 write; returned exact target preserved; success case unchanged.
- `tests/SteamInputAddonforClaw.Tests/CenterMRebootAuthorityTransitionTests.cs`: physical release failure blocks subsequent stock proof, HidHide clear, Center M enable, and restart.
- Keep `MsiClawInputSourceTests` cleanup-failure producer coverage intact.

Avoid a new cleanup manager, retry loop, lock, or generation.

## 4. Commit 2 — Audit 05-001: favorite state is unknown, not false, on an unsafe profile load

### Confirmed code path

- `src/SteamInputAddonforClaw/Profiles/ProfileStore.cs`: `Load()` distinguishes safe `Loaded` / first-run `NotFound` from unsafe `Malformed`, `UnsupportedSchemaVersion`, and `ReadFailure`. The latter preserve the original file and have `CanSafelyReplace == false`.
- `Profiles/GameProfileMutations.cs:CaptureFavoriteAppIds()` and `Profiles/XboxGameProfileMutations.cs:CaptureFavoriteKeys()` ignore the unsafe status and enumerate an empty default document.
- `Frontend/InProcessAddonFrontendControl.cs:ScanProfileGamesAsync()/ScanXboxGamesAsync()` projects that empty set to `Favorite=false`.
- `src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs` has Boolean-only favorite entries.
- `src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml[.cs]` and `XboxPage.xaml[.cs]` render and sort the favorite star from that Boolean, with editable favorite buttons.

### Required behavior

Preserve successful Steam and XBOX catalog discovery, even when profile storage cannot be read. **Do not show every title as definitively not favorited.** Add the smallest explicit favorite-state availability signal to existing catalog projection, e.g. an optional `FavoriteStateKnown = true` property/constructor argument on each existing frontend catalog entry, fed from the result of the *existing* `ProfileStore.Load()` at the favorite capture seam. No universal profile-state service or new persistence schema.

Illustrative semantic shape:

```csharp
// For the existing favorite capture, absence of a trustworthy favorite set is not an empty set.
var loaded = _store.Load();
if (!loaded.CanSafelyReplace)
    return /* unavailable favorite state, not an empty authoritative favorite set */;
return /* known favorite set from loaded.Document */;
```

In both catalogs:

- Known favorite => existing `★`, favorite-first sort and mutation.
- Known not favorite => existing `☆`, normal sort and mutation.
- Unknown favorite => neutral/unknown UI (not `☆`), favorite change action disabled, and a concise localized informational/warning message when useful. Game-card browsing should remain available.
- Real first run (`NotFound`) is a **known empty** favorite set, not an error.
- Do not overwrite, regenerate, or normalize unsafe `profiles.json`; retain current fail-closed profile mutation semantics and intact source file.
- Scope only to favorite catalog projection and its frontend consumers. Do not change Steam/XBOX identity observation, game-profile apply precedence, per-game controller mapping, or general profile persistence.
- If a frontend contract gains one field, ensure transport serialization/readback and existing callers remain compatible; use existing versioning/patterns rather than a new protocol subsystem.

### Tests

Add focused tests for **both** Steam and XBOX catalogs:

- normal loaded favorites and first-run empty favorites;
- malformed/unsupported/unreadable profile store projects **unknown**, not false;
- original profile file survives unchanged; unsafe mutations remain blocked;
- favorite sort / glyph / button availability follows the known/unknown distinction;
- existing catalog search, selection and non-favorite functionality still works.

Prefer extending existing `ProfileStoreTests`, `XboxGameCatalogFrontendTests`, profile catalog tests, and UI layout/projection tests; do not introduce an unrelated test framework.

## 5. Commit 3 — Audit 06-004: fully qualified executable paths for front-button actions

### Confirmed code path

- `src/SteamInputAddonforClaw.Contracts/FrontButtons/FrontButtonMapping.cs`: `FrontButtonMappingValidation.ValidateBinding` checks action/Win+G rules, not `Launch.ExecutablePath`.
- `src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs`: the path text box is manually editable and emits mapping edits on `TextChanged`; picker returns a fully qualified `.exe` path.
- `src/SteamInputAddonforClaw/CenterM/Oem1ApplicationLauncher.cs`: action launch checks `.exe` extension only.
- `src/SteamInputAddonforClaw/Processes/UserProcessLauncher.cs`: launches in Medium or High mode; relative `.exe` resolution can depend on Runtime working directory.
- `Shortcuts/ShortcutRuntime.cs:IsValidExecutablePath` already provides the analogous fully-qualified `.exe` rule.

### Required behavior

The front-button **LaunchApplication** action must not execute an ambiguous relative `.exe` path. Use the existing `Path.IsPathFullyQualified` + case-insensitive `.exe` extension pattern, with the normal managed-path error handling.

- Enforce at the action's execution boundary for legacy persisted values; reject safely before `Process.Start` in both Medium and High privilege paths.
- Reject invalid **new** mapping candidates without silently resolving relative paths to the Runtime working directory.
- **Important compatibility/UX constraint:** `SettingsStore` validates an entire persisted mapping and falls back to feature defaults on invalid mappings. Do **not** let the mere presence of a historical relative-path value silently reset all four unrelated saved front-button mappings. Readback/migration/validation must preserve unrelated valid mappings and report invalid launch targets safely.
- The UI sends edits while typing every character. Preserve in-progress text entry/picker behavior: no destructive rollback of an unfinished text field, no save/revalidation loop, and no loss of the other mappings. Reject/indicate an invalid final target through the existing UI/runtime mutation result path.
- Empty/incomplete LaunchApplication configuration retains the existing no-op behavior. Hidden `Launch` payload carried while another action is selected must not invalidate that other action.
- No general process-launch rewrite, shell execution, arbitrary script launcher, security sandbox, path-owner enforcement, or privilege-policy change.

Illustrative executable-boundary guard:

```csharp
var fileName = application.ExecutablePath;
if (!Path.IsPathFullyQualified(fileName) ||
    !string.Equals(Path.GetExtension(fileName), ".exe", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("The application action requires a fully qualified .exe path.");
}
```

Choose the narrowest validation path that preserves the existing editor/save behavior and historical saved mappings; the snippet alone is **not** the full UX/persistence fix.

### Tests

Extend front-button mapping, settings persistence, UI mapping/editor, and action-launch tests:

- absolute `.exe` accepted (normal and administrator option; arguments preserved);
- relative `foo.exe`, drive-relative, non-`.exe` safely refused;
- manual entry and picker still work; unfinished text does not wipe settings;
- unrelated actions with hidden non-selected launch payload remain valid;
- existing persisted relative path cannot execute and does **not** erase other valid button mappings.

## 6. Commit 4 — Audit 07-001: selected theme on three dialogs

Current theme authority: `src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs:ApplyMainUiTheme()` sets `MainAppRoot.RequestedTheme`; `Views/ShortcutPage.xaml.cs` already uses `RequestedTheme = ActualTheme` with `XamlRoot`.

The missing app-owned dialogs are:

1. Developer Menu confirmation — `MainWindow.xaml.cs` (around `OpenDeveloperMenu` / developer warning).
2. Prerequisite setup unavailable/in-progress/failure dialog — `MainWindow.xaml.cs:HandlePrerequisiteSetupResultAsync`.
3. Center M authority confirmation — `Views/DevicePage.xaml.cs` near the `ContentDialog` before Enable/Disable and Restart.

Use each dialog's existing owning root/page **actual theme**, plus existing `XamlRoot`. For MainWindow dialogs use the actual themed content/root (`MainAppRoot.ActualTheme` or the equivalent existing owner); for page-owned dialogs use `ActualTheme`.

Illustrative existing WinUI pattern:

```csharp
var dialog = new ContentDialog
{
    // existing title / content / buttons unchanged
    RequestedTheme = ActualTheme,
    XamlRoot = XamlRoot
};
```

Do not change global `Application.Current.RequestedTheme`, create a theme manager, redesign dialogs, or localize intentional Developer Menu copy here.

Tests: extend `tests/SteamInputAddonforClaw.UiTests/MainUiThemeTests.cs` to cover these exact three dialogs and existing `XamlRoot` binding, preserving Light/Dark/System preference semantics.

## 7. Commit 5 — Audit 07-002: ordinary prerequisite UI localization

Existing localizer: `src/SteamInputAddonforClaw.UI/MainUiStrings.cs` and `Strings/en-US/Resources.resw` / `Strings/ko-KR/Resources.resw`.

Affected production strings:

- `src/SteamInputAddonforClaw.UI/MainWindow.xaml`: `Installing required components…` and `Please wait. This may take a moment.`
- `src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs:HandlePrerequisiteSetupResultAsync`: `Another setup operation is already in progress.`; `Setup couldn't be completed. Check Settings > Required Components or the application log for details.`; dialog title `Setup unavailable` and close button `OK`.

Required:

- Add matching, nonempty en-US and ko-KR keys under the existing naming conventions.
- Use `x:Uid` for the two static XAML TextBlocks and `MainUiStrings.Get` for runtime title/body/buttons, with English fallbacks.
- Keep result-type branching, busy-overlay visibility, request flow, Center M transition behavior, existing dialog theme (from commit 4), and diagnostics unchanged.
- Developer-only diagnostics/copy are **out of scope**; no wholesale localization sweep.
- Consider the exact current MainWindow code after PR #823 (prerequisite-ready Center M prompt); do not revert that feature while editing nearby lines.

Tests: `tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs` and relevant UI startup/prerequisite tests must verify key parity, `x:Uid` resolution, and removal of hardcoded user-visible strings from the affected paths. Keep technical identifiers unchanged.

## 8. Validation and final PR handoff

For each commit:

1. Implement only its finding and directly related tests/resources; review `git diff` before committing.
2. Run its targeted tests and `dotnet build` as appropriate for touched projects.
3. Report commit SHA, changed files, and test results in the PR description.

After commit 5:

- Build the solution in Release mode and run `dotnet test SteamInputAddonforClaw.slnx -c Release`.
- Separately run `dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release` (UI tests are not in the solution).
- Run focused mapping, catalog/transport, Center M transition and localization/theme tests, and `git diff --check`.
- Do not claim physical PID switch, HidHide cleanup, WinUI pixels, or end-to-end install tests were performed by Codex without evidence. Physical verification is **user-only after merge**, never an implementation/PR blocker.
- Open one PR with five commits and an explicit audit-ID checklist. No unrelated cleanup.

## 9. Explicit out-of-scope ledger

- **Separate PR-B:** `03-001` Runtime unexpected-death replacement/keepalive and `09-001` mandatory Task Scheduler repair/visibility/restart reliability. **No Task Scheduler policy or Runtime resurrection edits here.**
- `02-001`: latest `docs/VIIPER_INTEGRATION.md` and current code intentionally create/verify three detached logical VIIPER devices at initialization; do not retrofit lazy Edge creation in this PR.
- `02-002`: narrow rumble timer scheduling interleaving, no demonstrated routine-user impact — no speculative lock/epoch work.
- `09-002`: publish-size category reporting; excluded from this five-commit scope.
- `06-001`–`06-003`, `07-003`, and `01-H02`: no confirmed production defect to repair in this PR.

**Acceptance:** The five specified findings are corrected with bounded source-backed changes and focused automated regressions; no other ownership, policy, privilege, physical routing, VIIPER, power, profile-selection, or Overlay behavior is changed.
