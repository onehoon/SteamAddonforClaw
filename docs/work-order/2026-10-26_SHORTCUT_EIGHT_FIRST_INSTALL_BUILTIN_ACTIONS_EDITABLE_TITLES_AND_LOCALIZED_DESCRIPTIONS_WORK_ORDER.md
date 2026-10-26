# Work Order — Four New Shortcut Actions, Eight First-Install Defaults, Editable Titles, and Localized Action Descriptions

**Date:** 2026-10-26  
**Repository:** onehoon/SteamAddonforClaw  
**Implementation:** local Codex; one focused PR against latest main  
**Hardware validation:** user's role after merge, NOT a local-Codex or PR-review blocker  
**Scope:** standalone Full PID1902, single interactive Windows administrator user; no CTW integration

## 0. Final approved product policy

Implement **exactly four additional built-in Shortcut actions**: Keyboard, Show Desktop, Steam Input, and ClawHUD.

Keep **all Shortcut titles editable**, including existing and new built-ins. **On Add Shortcut, selecting a built-in Action must prefill the Title with that Action's current localized picker name (EN/KO); the prefilled title is only a suggestion, and the user can freely replace it before saving.** A saved title is user-owned data, not an immutable or automatically translated label. Do not introduce title locking, read-only Title inputs, automatic title normalization, or re-labeling of already persisted entries.

For an **existing built-in tile**, lock only its **Action type/identity** during editing. Users can still rename it, select/change its icon, edit applicable options (for example Screenshot's save folder), change the supported close-overlay preference, reorder it, and delete it. They can add another tile with that action using Add Shortcut (except the existing single-Screenshot constraint). Custom Application, PowerShell, and Website tiles retain their current editable titles and action-change behavior. If a custom tile is explicitly changed into a built-in type, its action kind is subsequently fixed in the same way. UI locking MUST have a matching Runtime mutation validation, not only a disabled ComboBox.

**Action descriptions are non-editable UI help**, keyed by selected Action kind and localized EN/KO. They are NOT fields on ShortcutTileDefinition or in shortcuts.json. The Overlay tile shows its current icon, user-authored title, and existing status when applicable; it does not show multi-line explanatory action descriptions.

**First-install only:** On ShortcutLoadStatus.NotFound, seed eight ordered built-in tiles. For ANY existing shortcuts.json, including an empty dashboard, preserve data and do not add/migrate/backfill defaults. No defaults revision, seed marker, extra file, persistence coordinator, periodic repair, or schema bump.

## 1. Mandatory current source and architecture reading

Before implementing, re-read current main rather than assuming these path contents remain identical:

- docs/Full 1902 Implementation/README.md
- docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
- docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
- docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md
- docs/work-order/2026-10-12_FIRST_INSTALL_SHORTCUT_AND_OVERLAY_TAB_DEFAULTS_WORK_ORDER.md
- docs/work-order/2026-10-13_OVERLAY_SHORTCUT_VERTICAL_ICON_TITLE_AND_XBOX_DEFAULT_LABEL_WORK_ORDER.md
- docs/work-order/2026-10-14_MAIN_APP_SHORTCUT_AND_HOW_TO_USE_RESW_PR7_WORK_ORDER.md
- docs/work-order/1007_SHORTCUT_3_COLUMN_BUILTIN_ACTIONS_WORK_ORDER.md
- docs/work-order/1007_SHORTCUT_CLOSE_OVERLAY_AFTER_LAUNCH_WORK_ORDER.md
- docs/work-order/CH_A3_CLAWHUD_MAIN_UI_AND_OVERLAY_SETTINGS_WORK_ORDER_2026-09-22.md

Primary code:

- src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs
- src/SteamInputAddonforClaw/Shortcuts/ShortcutStore.cs
- src/SteamInputAddonforClaw/Shortcuts/ShortcutDocument.cs
- src/SteamInputAddonforClaw/Shortcuts/ShortcutActionTypeIds.cs
- src/SteamInputAddonforClaw.Contracts/Shortcuts/ShortcutContracts.cs
- src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutEditorFrontendContracts.cs
- src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutFrontendContracts.cs
- src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutIconCatalog.cs
- src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
- src/SteamInputAddonforClaw/Steam/SteamSessionRuntime.cs
- src/SteamInputAddonforClaw/Steam/SteamRunningAppIdRegistrySource.cs
- src/SteamInputAddonforClaw/ClawHud/ClawHudFrontendMapping.cs
- src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
- src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml[.cs]
- src/SteamInputAddonforClaw.UI/MainUiStrings.cs
- src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw
- src/SteamInputAddonforClaw.UI/Strings/ko-KR/Resources.resw
- src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shortcuts.cs

## 2. Source-verified findings (2026-10-26)

- ShortcutRuntime currently creates four defaults only when ShortcutStore.Load() yields NotFound, saving through the existing validated document writer. Loaded files and intentionally empty dashboards are already preserved. Unsafe, unreadable, malformed, or unsupported-schema files remain unavailable and untouched.
- The current default order is Screenshot / Steam / Steam Big Picture / XBOX. Screenshot's existing default CloseOverlayAfterLaunch is false; the other three are true.
- ShortcutDocument.CurrentSchemaVersion = 2; supported ShortcutActionSpec.SchemaVersion = 1. ShortcutTileDefinition includes a stored editable Title and nullable icon override.
- ShortcutPage.ShowEditorAsync constructs the dialog in C#, currently suggests a title via GetDefaultTitle() but allows the user to change it. Runtime's Create and Update mutations currently allow edited Title. **Preserve this behavior.**
- Editor Action kind, ShortcutActionTypeIds, ProjectEditorAction, GetEditorActionKind, TryBuildAction, Resolve, ExecuteAsync, and parameterless-built-in validation require additive coverage for the four new kinds.
- OverlayWindow.Shortcuts currently displays FrontendShortcutTile.Title verbatim in a three-column grid. Do not replace saved titles with locale-dependent action names.
- The existing user Website URL action intentionally validates only HTTP/HTTPS. New Steam Input must be a dedicated built-in; do NOT generalize arbitrary user URI support.
- SteamSessionRuntime exposes actual Steam RunningAppID and a separate BPM fact. AddonProcessHost already consumes _runtimeHost?.ActualRunningAppId in frontend composition.
- ClawHUD's canonical desired-state owner is AddonProcessHost.CaptureClawHudState() / SetClawHudEnabledAsync(bool), with existing SetClawHudFrontendAsync and frontend refresh semantics.
- AddonProcessHost.HandleOverlayShortcutExecutionAsync currently executes the action first and retires the Overlay afterward on a successful requested close. **For focus-sensitive keyboard/desktop/Steam Input actions, review this ordering**; avoid injecting to the still-focused Overlay when focus must be handed back.
- The main WinUI 3 app already owns EN/KO RESW/PRI resources via MainUiStrings.Get(). The Overlay is a separate frontend without this same localization infrastructure. No overlay-wide localization redesign.
- The Runtime is elevated while Main UI/Overlay remain frontends. Keep UserProcessLauncher/privilege and existing shortcut transport ownership boundaries.

## 3. First-install defaults — exact ordered eight tiles

Extend only ShortcutRuntime.CreateInitialShortcutDocument() on NotFound. Persist eight tiles in this order:

| Index | Stored initial English title | Type | Parameters | Close Overlay default |
| ---: | --- | --- | --- | --- |
| 0 | Screenshot | existing ScreenshotFullscreen | empty object | false |
| 1 | Steam | existing SteamClient | empty object | true |
| 2 | Steam Big Picture | existing SteamBigPicture | empty object | true |
| 3 | XBOX | existing XboxApp | empty object | true |
| 4 | Keyboard | new Keyboard | empty object | true |
| 5 | Show Desktop | new ShowDesktop | empty object | true |
| 6 | Steam Input | new SteamInput | empty object | true |
| 7 | ClawHUD | new ClawHudToggle | empty object | false |

The new built-ins are parameterless at persisted ActionSpec level; use a unique non-empty TileId for every tile; do not persist icon overrides for seeded entries. Initial titles are intentionally canonical English data, consistent with the current seed. This does NOT preclude a user renaming them in Korean. Localized **picker labels/descriptions** are separate from persisted tile titles.

ClawHUD's default false close-overlay setting enables simple toggling while the Shortcut panel remains visible. The keyboard/desktop/Steam Input defaults request close-on-success for focus transfer; if the existing retirement semantics require an earlier handoff for correct activation, handle that narrowly in the existing Runtime/Overlay owner path.

Every loaded existing shortcuts.json is authoritative. NEVER:
- add the four new tiles to an existing dashboard;
- replace/remove/reorder existing tiles;
- reset customized names, screenshot folders, close flags, or glyphs;
- re-add user-deleted actions on restart.

Keep the current malformed/unreadable-file fail-safe and initial-seed write-failure handling.

## 4. New Action identity, execution and capability contracts

Choose stable descriptive TypeId constants in ShortcutActionTypeIds (e.g. system.touch-keyboard, system.show-desktop, steam.input-settings, addon.clawhud-toggle); keep ActionSpec schema version 1 and empty-object parameters. Extend only the existing typed editor enum/input, builder, projection, resolve and execution switches. Do not use stored titles or TileIds for determining action semantics.

### 4.1 Keyboard — Windows touch keyboard

- Intended feature is the **Windows 11 touch keyboard**, not a silently substituted classic accessibility OSK.
- The user presses the shortcut to show/hide the touch keyboard. Respect Windows-controlled keyboard activation/visibility and minimize process coupling.
- Review the chosen supported Windows 11 invocation approach for an elevated/background Runtime. IMPORTANT: Microsoft documents InputPane.TryShow as foreground-only and best effort; blindly invoking it in the persistent background Runtime is NOT a valid guarantee of opening the keyboard. If using a TabTip-based or other existing Shell route, confirm its foreground/toggle behavior from actual API/source evidence; do not kill Windows keyboard or TextInputHost processes as a hide strategy.
- Retire/focus-handoff the Overlay before invoking a foreground-sensitive OS keyboard operation. Reuse current owned-overlay retirement semantics and error reporting rather than adding a focus broker, polling state machine, or helper service. Ensure action dispatch errors do not report success.
- A Windows/platform denial or inability to show the touch keyboard should be a bounded feature failure only; it must not affect controller ownership or leave the Runtime unusable.

Reference: https://learn.microsoft.com/en-us/uwp/api/windows.ui.viewmanagement.inputpane.tryshow

### 4.2 Show Desktop

- Match the Windows **Win+D toggle** behavior: show desktop / restore prior windows, not launching explorer.exe or minimizing windows one by one.
- Windows' SendInput-based Win+D sequence is a valid reference; preserve key-down/key-up symmetry and avoid leaving Win logically pressed on failure. An equivalent well-supported Shell mechanism is fine.
- Prefer the existing Overlay close/focus-return path for the standard default close behavior. Do not add general input injection infrastructure for this one command.

Reference (official Windows sample): https://github.com/microsoft/Windows-classic-samples/blob/main/Samples/DesktopAutomationDismiss/cpp/ShowDesktop.cpp

### 4.3 Steam Input — currently running Steam game ONLY

- Read **actual Steam RunningAppID** from the existing Runtime source (not Profile ID, XBOX ID, BPM-only state, a guessed executable, or an app-specific persistent field).
- When RunningAppID == 0, publish a disabled/unavailable tile with an understandable short status, and decline execution without launching Steam controller settings.
- On execution, obtain a fresh actual AppID. For nonzero AppID, invoke the Steam protocol URI:

~~~text
steam://controllerconfig/{RunningAppID}
~~~

- Reuse the existing trusted UserProcessLauncher.LaunchUri and the built-in URI path. Never widen generic Website URI permissions.
- No general Steam controller settings fallback, no forced BPM launch, no synthetic/stale AppID caching, no Valve private process injection.
- URI **dispatch accepted** does not prove Steam rendered the target editor. Treat the actual editor result as a user-owned post-merge hardware/Steam-client check; do not claim it is guaranteed by the URI launch API.
- Recheck source before final code because Steam's protocol behavior can change.

### 4.4 ClawHUD Toggle

- Read current **DesiredEnabled**, not a proxy of whether an OS process window happens to be visible. Request its negation via the existing AddonProcessHost / InProcessAddonFrontendControl canonical mutation path.
- Reuse the existing ClawHUD persistence, managed Runtime acquisition/stop, gate, and overlay invalidation. Do NOT create a second ClawHUD process controller, separate setting, duplicated persistence, or a direct start/kill command.
- Reflect the newly committed desired state in subsequent snapshots. On an acquisition or operation failure, use existing result/status semantics; do not claim a successfully visible HUD merely because enabling was requested.
- Actual onscreen visibility may still follow the user's existing ClawHUD display mode (e.g. in-game only); the Shortcut changes Enabled state, not those display-mode settings.
- Leave Shortcut Overlay open by default for this toggle.

## 5. Editing rule — Action fixed, Title editable

This is a deliberate simplification of an earlier proposal to lock built-in titles. **Do NOT implement title locking.**

**Default Title on newly added tiles (required):** Preserve and extend the existing `ShortcutPage.GetDefaultTitle()` + `ShouldApplyDefaultTitle()` suggestion behavior. During **Add Shortcut**, when a user selects any of the eight built-in Action kinds, automatically populate the editable Title box with exactly the Action picker label in the **current UI language**. For example, Keyboard -> "Keyboard" (en-US) or "터치 키보드" (ko-KR); Show Desktop -> "Show Desktop" (en-US) or "바탕화면" (ko-KR); Steam Input and ClawHUD keep their names in both languages. Existing Steam / Steam Big Picture / XBOX / Screenshot should follow the same action-label rule without changing existing saved tiles. When the user cycles among built-in Action types **before manually editing Title**, update the still-auto-suggested Title to match the newly selected Action. Once the user types their own title, do not overwrite it on subsequent Action selection, except when they deliberately clear the field and select a built-in again. Titles for custom Application/PowerShell/Website actions remain freely editable, without forcing a synthesized action-name title. If editing an existing Shortcut, always initialize Title from its persisted value and **never** reset it merely because a built-in Action is selected or the UI language differs.

Keep the existing lightweight UI-local suggested-title tracking; no additional persisted `IsDefaultTitle` flag, migration, title localizer in Runtime or front-end transport field. The resulting user-confirmed Title is saved verbatim. **Do not auto-translate saved titles on language changes.** The eight first-install seed titles from Section 3 remain the canonical English values, as with today's seed; the localized suggestion requirement specifically concerns *manual new Shortcut registration* through the Main UI.

For any built-in tile (old four or new four):
- Title is freely editable and persisted exactly as user entered.
- Action type is immutable when editing that existing tile. Show the chosen action as a read-only/disabled selector, not a mysteriously clickable picker.
- The effective icon remains editable, including use-default; title changes must not change the default icon.
- Preserve action-specific non-TypeId parameters on edit: Screenshot still allows its folder setting; built-ins remain otherwise parameterless.
- Close Overlay option remains editable where existing product semantics support it. Screenshot retains its special invariant (always keep overlay for capture).
- Delete/reorder continue to work. Add Shortcut allows selecting any supported built-in type, subject to the established single-Screenshot restriction.

At the **Runtime mutation** boundary, verify that when the prior tile has a built-in TypeId, Update cannot replace it with a different TypeId. Avoid relying on the Main UI disable alone. Preserve the ability to submit a different Title, IconGlyphOverride, valid same-type settings and supported close preference. Do not make an additional persisted IsDefault/IsBuiltIn/IsTitleLocked flag; determine built-in identity from TypeId.

Custom Application/PowerShell/Website actions continue to support the existing editing and type-selection behavior; do not inadvertently lock all actions or break unsupported-future-action preservation. Existing user-customized titles on already persisted built-in tiles remain unchanged until the user edits them; do not normalize them on load.

Illustrative narrow guard (adjust to current builder and schema semantics):

~~~csharp
var existingType = tiles[index].Action.TypeId;
if (IsBuiltInType(existingType) &&
    !string.Equals(existingType, action!.TypeId, StringComparison.Ordinal))
{
    return Fail("Built-in Shortcut actions cannot be changed.", out failureMessage);
}
// Title remains intent.Title; icon and permitted configuration may still change.
~~~

Use a single small built-in TypeId classification (or equivalent existing switch) shared by validation and UI projection; do not build an action plugin registry or another authority manager.

## 6. Explanation text and EN/KO localization — no persisted description

**Action descriptions belong in the existing Add/Edit dialog**, dynamically selected from the Action picker (or shown under the read-only action choice during built-in edits). No per-tile Description field, no action-description persistence, no new Shortcut schema.

New Action catalog copy, exactly:

| Action | English picker title | Korean picker title | English description | Korean description |
| --- | --- | --- | --- | --- |
| Keyboard | Keyboard | 터치 키보드 | Show or hide the Windows touch keyboard. | Windows 터치 키보드를 표시하거나 숨깁니다. |
| ShowDesktop | Show Desktop | 바탕화면 | Show the desktop or restore open windows (Win+D). | 바탕화면을 표시하거나 열려 있던 창을 복원합니다. (Win+D) |
| SteamInput | Steam Input | Steam Input | Open the controller layout settings for the currently running Steam game. | 현재 실행 중인 Steam 게임의 컨트롤러 레이아웃 설정을 엽니다. |
| ClawHudToggle | ClawHUD | ClawHUD | Turn the ClawHUD performance overlay on or off. | ClawHUD 성능 오버레이를 켜거나 끕니다. |

Use existing MainUiStrings and Strings/en-US/Resources.resw + Strings/ko-KR/Resources.resw. Actual Korean values are required for these four action names and descriptions, **not copied English placeholders**. The Add Shortcut Title suggestion must use the **same localized Action picker label**; do not maintain a separate translation or a competing default-title string for the same Action. Preserve the current localized descriptions for Steam, Steam Big Picture, XBOX and Screenshot, updating only missing description resources if necessary; preserve user-entered titles and raw runtime errors.

The description should appear as subdued, wrapping help below the Action kind picker (or within the existing selected built-in action description area) and update when a user selects a different kind. Reuse existing CreateBuiltInActionPanel and local control patterns; avoid duplicate explanatory paragraphs or a new Description editor.

Existing Screenshot path/folder controls remain accessible. For Application/PowerShell/Website retain their form instructions; add no unnecessary new informational text. No separate description/tooltip in each small three-column Overlay tile. No new overlay localization system, schema revision, display-title resolver or automatic renaming tied to OS locale.

Optional localized Action target-summary labels in the Main App should use the existing GetDisplayTargetSummary pattern; do not rewrite Runtime-authored persisted titles.

## 7. Iconography

Reuse the shared Segoe Fluent Icons catalog and its current picker, not new image assets or an icon library. Suggested existing glyphs (choose existing named choices and keep one authoritative mapping):

| Action | Existing catalog glyph |
| --- | --- |
| Keyboard | Keyboard U+E765 |
| Show Desktop | Task view U+E7C4 |
| Steam Input | Settings U+E713 |
| ClawHUD | System U+E770 |

Update the single ShortcutIconCatalog.GetDefaultGlyph(kind) switch. Seed null overrides. Existing icon customizations are retained.

## 8. Focus handoff and lifecycle boundaries

The current host executes the Shortcut and *then* retires the Overlay when RetireOverlayAfterExecution is true. That order is valid for many launcher actions but may not be sufficient for these foreground-sensitive OS features. Explicitly review Keyboard, Win+D and Steam Input with the actual Overlay focus and retirement path; use the smallest change to ensure correct handoff.

When a focus-sensitive action cannot be admitted (notably Steam Input with AppID zero), leave Overlay available and return the expected disabled/failure result. If a qualifying action must close the Overlay *before* sending input or invoking the OS, reuse the existing Host-owned retirement method and introduce only a narrow action-kind condition, not a general workflow state machine. Preserve cancellation, teardown, overlay-capture safety and existing post-launch retirement behavior for all old actions.

Keep the controller owner independent: do not touch PID1901/1902, DirectInput, HidHide, VIIPER native ownership, physical device recovery, WING suppression, Steam virtual presentation, suspend/resume, stock restoration or Center M authority. No unsupported multi-session/RDP compatibility work.

## 9. Automated acceptance and regression coverage

Extend the existing relevant suites:

1. **Fresh install:** exactly eight ordered tiles, intended TypeIds, schema 2/1, empty parameter objects, unique non-empty TileIds, default glyphs, and expected close-overlay flags. Reload must preserve the same IDs.
2. **Existing user files:** empty, populated, renamed, reordered, customized glyph and folder, unsupported/future Action entries, malformed/unreadable files — none are seeded/backfilled/overwritten. Do not write a migration marker.
3. **Editor:** all four new actions selectable; valid parameterless built-ins; **Add Shortcut pre-fills a title exactly matching the selected Action label in EN/KO, updates the suggestion when switching kinds before manual editing, preserves manually customized input, and never overwrites persisted titles when editing existing tiles**; title and glyph remain freely editable; existing built-in type conversion rejected at Runtime and disabled in Main UI; custom title and type changes unchanged; screenshot special single-tile and folder rules unchanged.
4. **Descriptions:** EN/KO resources exist and contain intended translations; selected Action drives help text. Descriptions are absent from persisted shortcuts.json and Overlay tile contracts.
5. **Steam Input:** AppID=0 -> disabled/refused/no URI launch; nonzero -> exact steam://controllerconfig/{AppID} dispatch via injected fake; fresh AppID read at execution; BPM-only -> disabled; launcher failure is surfaced.
6. **ClawHUD:** uses captured DesiredEnabled to toggle, invokes exactly the existing supplied mutation delegate, does not touch separate process state, propagates operation failures, and leaves Overlay open by default.
7. **Keyboard/Show Desktop:** code-level tests with injected OS-command seams verify accepted/failed outcomes and no corrupted/held keyboard keys; verify overlay handoff order using host-level seam tests where practical. Do not require physical device emulation.
8. **Regression:** current Screenshot, Steam, Big Picture, XBOX, EXE, PowerShell, Website shortcuts, click/D-pad behavior, three-column grid, delete/reorder, frontend IPC/snapshot and security privilege behavior remain intact.

Execute applicable project builds and automated unit/UI tests, plus the project's existing localization resource checks. Fix reproducible code failures; avoid new test machinery for implausible scheduler interleavings.

**The local Codex must NOT be required to perform hands-on MSI Claw testing.** The owner will verify touch keyboard visibility, Steam client controller-config navigation, actual overlay focus behavior and ClawHUD toggle on real hardware **after merge**. Missing user-side hands-on verification is not a PR blocker when code and CI are sound.

## 10. Explicit non-goals

- No auto-adding new default tiles to existing users.
- No forced default-title/canonical name locking; titles remain editable.
- No editable or persisted Action description field.
- No new standalone CTW/HHC integration or imported helper process.
- No other proposed shortcuts (Alt+Tab, media controls, Steam QAM, TDP, FSE, game process suspension/kill).
- No general arbitrary URI handler for Website actions.
- No new localization runtime, state machine, authority abstraction, device lifecycle modifications, or theoretical-race protection.

## 11. Completion report expected from local Codex

Report the changed files, action TypeIds, default-seed result, description localization implementation, title-editability/type-lock behavior, test commands/results, and any known limitations of Windows touch keyboard or Steam URI dispatch. Create a focused PR. Real-device checks are explicitly deferred to the user's post-merge validation.
