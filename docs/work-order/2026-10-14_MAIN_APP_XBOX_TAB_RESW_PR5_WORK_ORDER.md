# Work Order — Main App RESW PR5: XBOX Tab (Catalog, Game Profiles, Per-Game M1/M2)

**Date:** 2026-10-14  
**Repository:** `onehoon/SteamAddonforClaw`  
**Owner:** Local Codex (implementation and automated code validation); user (physical handheld validation after merge)  
**Baseline:** Latest `main` **after PR #788 Steam localization was squash-merged**, as well as merged PRs #785 (RESW foundation), #786 (Device), #787 (Controller). Refresh HEAD before editing.  
**Delivery:** **One focused PR5** covering the entire XBOX Main App tab. Do not split the catalog and profile editor into separate implementation PRs.  
**Physical test rule:** **Do not require the local Codex or CI to perform MSI Claw physical-device validation; do not make it a PR blocker.** The user performs that testing after the code is merged.

## 0. Goal and non-negotiable decisions

Complete the existing Main App `en-US` / `ko-KR` **RESW + published PRI localization extraction** for the XBOX tab's locally authored user-visible text:

1. XBOX left-navigation label;
2. installed XBOX game catalog/search/favorites and selected-game editor;
3. all settings-card captions/descriptions and human-readable mode choices;
4. per-game **M1/M2 target labels and locally authored override/error messages**;
5. UI-authored normal informational text, availability warnings, fallback errors, saved-profile failures and Runtime-connection failure notices.

This is the **same resource-ownership policy already implemented in Device, Controller and Steam**: extract the entire Main-UI-owned display copy **including UI-authored errors**, but **do not alter or translate Runtime/Frontend-produced messages, game names, technical IDs or log literals**.

**Both language files must contain the exact existing English values at implementation time. Do not add Korean translations now.** The product owner translates `ko-KR/Resources.resw` separately later; a correct implementation must not need another C# change when that happens.

Stay within **standalone Full PID1902**, one Windows administrator user/one interactive session. CTW is **not** an integration target. This PR must not modify controller authority, background-runtime ownership, routing, VIIPER, HidHide, PnP behavior, Xbox Game Bar suppression or physical-device behavior.

Do not introduce another localization framework, new resource files, global UI base class, generic profile-editor abstraction, translation facade, automatic language selector or runtime language-switch state. The requested work is resource extraction and source/test adjustments, not UI redesign or cleanup.

## 1. Mandatory architecture and reference reading

Read the current architecture rather than relying on old pre-standalone CTW documents:

- `docs/Full 1902 Implementation/README.md` — document precedence.
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`.
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`.
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`.
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`.
- **`docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md`**, especially ``2.4` and ``14–15`: Steam and XBOX identities/catalogs/profile RPCs/pages are separate; XBOX profiles support offline/pre-launch edits; Main App is frontend only.
- `docs/work-order/2026-10-14_MAIN_APP_CONTROLLER_TAB_RESW_PR3_WORK_ORDER.md` — Controller M1/M2 label extraction without changing shared model authority.
- **`docs/work-order/2026-10-14_MAIN_APP_STEAM_TAB_RESW_PR4_WORK_ORDER.md`** — immediately preceding complete catalog/detail localization policy and runtime message precedence.

Read current code:

- `src/SteamInputAddonforClaw.UI/MainWindow.xaml` and `MainWindow.xaml.cs`.
- `src/SteamInputAddonforClaw.UI/Views/XboxPage.xaml` and `XboxPage.xaml.cs`.
- `src/SteamInputAddonforClaw.UI/Views/BackButtonMappingUiOptions.cs`.
- `src/SteamInputAddonforClaw.Contracts/BackButtons/BackButtonMapping.cs` (inspection only).
- `src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs` (M1/M2 local display solution).
- `src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml[.cs]` (already-localized Steam precedent; **do not modify** unless a concrete integration defect demands it).
- `src/SteamInputAddonforClaw.UI/MainUiStrings.cs` and `src/SteamInputAddonforClaw.UI/Strings/{en-US,ko-KR}/Resources.resw`.
- `scripts/verify-ui-localization-pri.ps1` and `scripts/publish-layout.ps1`.
- Existing automated tests identified in `8`.

## 2. Actual main-branch implementation inventory (verified)

`XboxPage.xaml` is approximately **12.4 KB across 36 physical source lines**; long XAML lines are not an indication of missing UI. `XboxPage.xaml.cs` contains approximately **567 physical lines / 42 KB**, many dense single-line handlers. Do **not** mass-reformat the whole file in a localization change.

### 2.1 Navigation and page boundaries

`MainWindow.xaml` currently has:

```xml
<NavigationViewItem Content="XBOX" Tag="Xbox">
```

XBOX is a **separate** top-level page, `XboxPage` / `XboxPageHost`. Its `Tag="Xbox"` is routing identity. **Never translate or rename `Tag` or `MainNavigationPage.Xbox`.** Add a dedicated `x:Uid` for its **Content only** (`MainNav_Xbox.Content`) while preserving the initial literal `Content="XBOX"` fallback.

MainWindow creates XboxPage lazily via `GetOrCreateXboxPage()` and activates/deactivates it on navigation. No change to warm-up, page lifetime, global navigation, AppID vs XBOX-key routing, or command handlers.

### 2.2 XBOX is two panels in one page

The XBOX Main App contains:

- `CatalogPanel`: XBOX heading; `↻ Refresh`; `GameSearchBox` with `Search games...`; items repeater bound to `DisplayName`, favorite glyph ★/☆, and whole-game `Tag="{Binding Game}"`.
- `DetailPanel`: `Back`; selected game title bound from `game.DisplayName`, enable toggle, and seven settings areas.

**Keep** the current two-panel structure. Do not split into two pages, combine Steam/XBOX, create a common ViewModel, rebuild card presentation, change icons or reflow XAML.

### 2.3 Current detailed section order

1. `M1 / M2 Button Mapping`: per-game enable/disable toggle, M1 and M2 target combo boxes. Disabled = use global mapping, enabled = per-game mapping.
2. `TDP Control`: Plugged in / On battery, PL1/PL2 and watts.
3. `CPU Boost`: AC/DC choice with seven `CpuBoostModeItem` labels.
4. `Windows Power Mode`: AC/DC choice with three `PowerModeItem` labels.
5. `Minimum GPU Clock`: AC/DC sliders, generated MHz text, runtime warning/description.
6. `Intel FPS Limit`: AC/DC sliders, rendered FPS values and runtime availability description.
7. `Resolution`: single card/dropdown with `Do not change` plus fixed resolution labels.

Do not add a Steam virtual-controller selector to the XBOX page; the XBOX page has the M1/M2 editor instead.

### 2.4 XBOX-specific state and RPC contracts

`XboxPage.xaml.cs` uses:

- `FrontendXboxGameCatalogEntry.Key` and `DisplayName`; XBOX profile key is a **string**, not a Steam AppID.
- `ScanXboxGamesAsync(scan.Token)` for cancellable catalog loading; `CaptureXboxGameProfileAsync(key, capture.Token)` for selected game.
- `IsCurrentScan`, `_active`, `IsCurrentProfileResponse` and `_captureCancellation` to reject stale/canceled/hidden-profile responses.
- A successful cached scan reused on later activation; explicit refresh is allowed.
- `FilterAndSort` searches `DisplayName` (not internal keys), favorites first, deterministic ordering.
- XBOX profile RPCs (`SetXboxGameProfile*Async`); no Steam `SetGameProfile*Async` substitutions.
- Per-game M1/M2 save queue with `_backButtonSaveChain`, `_backButtonEditVersion`, `RunAfterPreviousAsync`, `IsCurrentBackButtonMappingEdit`.
- Existing debounce/draft preservation for TDP, FPS and GPU.

These behaviors remain completely outside localization scope. Do not redesign cancellation/save sequencing, replace `Key` with `AppId`, create an alternate store, or add theoretical race defenses.

## 3. Fixed XAML text: use x:Uid + matching dotted RESW key

For each XBOX-owned, fixed, human-readable `Text`, `Content`, `Header`, `Description` and `PlaceholderText` property:

- Add stable unique `x:Uid="Xbox_..."` to the original element.
- Add `Xbox_<Uid>.<Property>` to **both** existing `Resources.resw` language files, using the exact English string from the current XAML.
- Preserve the original English property attribute as an XAML fallback.
- Do not change element names, binding expressions, handlers, styles, Grid positions, icons, accessibility properties or visibility.

Suggested examples (illustrative; do not replace full actual controls with simplified samples):

```xml
<NavigationViewItem x:Uid="MainNav_Xbox" Content="XBOX" Tag="Xbox">

<TextBox x:Name="GameSearchBox"
         x:Uid="Xbox_GameSearchBox"
         PlaceholderText="Search games..."
         TextChanged="GameSearchBox_TextChanged" />

<ctcontrols:SettingsExpander x:Name="ControllerMappingExpander"
                             x:Uid="Xbox_ControllerMappingExpander"
                             Header="M1 / M2 Button Mapping"
                             Description="Enable a per-game M1 / M2 override for Xbox 360 output. Off uses the global mapping." />
```

Corresponding RESW key examples:

```xml
<data name="MainNav_Xbox.Content" xml:space="preserve"><value>XBOX</value></data>
<data name="Xbox_GameSearchBox.PlaceholderText" xml:space="preserve"><value>Search games...</value></data>
<data name="Xbox_ControllerMappingExpander.Header" xml:space="preserve"><value>M1 / M2 Button Mapping</value></data>
<data name="Xbox_ControllerMappingExpander.Description" xml:space="preserve"><value>Enable a per-game M1 / M2 override for Xbox 360 output. Off uses the global mapping.</value></data>
```

Required coverage:

| Area | Fixed text |
| --- | --- |
| MainWindow | XBOX NavigationView item Content; `Tag="Xbox"` remains literal |
| Catalog | `XBOX` title, `↻ Refresh`, `Search games...` placeholder |
| Detail | `Back` |
| M1/M2 | `M1 / M2 Button Mapping`, full description, `M1`, `M2` card headers |
| TDP | `TDP Control`, full description, `Plugged in`, `On battery` |
| CPU | `CPU Boost`, description, AC/DC headers |
| Power | `Windows Power Mode`, description, AC/DC headers |
| GPU | `Minimum GPU Clock`, full thermal-limits description, AC/DC headers |
| FPS | `Intel FPS Limit`, full Intel API description, AC/DC headers |
| Resolution | `Resolution` card |

Use distinct UID names for each separate settings card even if its English header repeats `Plugged in` or `On battery`.

Do **not** localize or alter:

- `x:Name`, `x:Class`, `Tag` values, `Game` card bindings, `DisplayMemberPath="Label"`, XAML event methods, internal enums or RPC names.
- Game `DisplayName`, internal XBOX `Key`, search query, file/package identity, favorite glyph ★/☆.
- Empty toggle `OnContent=""` / `OffContent=""`, dynamic `SelectedGameNameText` (game name).
- `PL1` / `PL2`, `M1` / `M2` **button identities**, `FPS` / `MHz` / `W` units, `— W` / `— MHz` placeholders, numeric `60 FPS`, and exact resolution dimensions. M1/M2 Header values may be present as RESW keys for consistent XAML coverage but must remain physical button identifiers (not semantic translations).

**Important:** Existing `tests/SteamInputAddonforClaw.UiTests/XboxCatalogPageUiTests.cs` asserts `x:Name="PerGameBackButtonMappingEnabledToggle" IsOn="False"` and the actual M1/M2 save contract. Do not unnecessarily place a UID on this toggle with no human-readable content or modify its attribute order. Preserve explicit `IsOn="False"`, `IsExpanded="False"` and all event connections.

## 4. Dynamic option labels (XBOX-owned C#)

### 4.1 CPU Boost / Windows Power Mode

`XboxPage.xaml.cs` has the same typed mode arrays as Steam, but owns its own XBOX UI code:

- Seven CPU labels: `Disabled`, `Enabled`, `Aggressive`, `Efficient Enabled`, `Efficient Aggressive`, `Aggressive At Guaranteed`, `Efficient Aggressive At Guaranteed`.
- Three Power mode labels: `Best power efficiency`, `Balanced`, `Best performance`.

Extract their **display Label only** through `MainUiStrings.Get("Xbox_...", existingEnglish)`. Preserve exact `CpuBoostMode` and `WindowsPowerMode` enum associations, list ordering, source record types and `DisplayMemberPath="Label"`.

Prefer XBOX-scoped keys (`Xbox_CpuBoost_Mode_...` and `Xbox_PowerMode_Mode_...`) rather than coupling this independent page to Steam-specific keys. No shared translation manager/helper required.

### 4.2 Resolution

`ResolutionItems` currently has:

```text
Do not change
1920 × 1200
1920 × 1080
1680 × 1050
1440 × 900
```

Extract `Do not change` to `Xbox_Resolution_DoNotChange`. Keep numerical labels and order exact, and keep selection/mutation based on `ResolutionItem.Width` / `Height`, not localized `Label`. `null` still means no resolution override.

### 4.3 M1 / M2 target option labels — XBOX-specific extra requirement

`BackButtonMappingUiOptions.AddTargets(comboBox)` creates each `ComboBoxItem` from **all 18 members** of `Xbox360BackButtonTarget`:

```text
Disabled, A, B, X, Y,
DPadUp, DPadRight, DPadDown, DPadLeft,
LeftBumper, RightBumper, LeftTrigger, RightTrigger,
LeftStickClick, RightStickClick, View, Menu, XboxGuide
```

Each item stores the **typed enum in `ComboBoxItem.Tag`**, and downstream selection/persistence reads that typed tag. Only `Content` is translatable.

Follow PR3 Controller's proven local display-label pattern, with an **XBOX-only** helper or short method invoked after `AddTargets` for **both** M1 and M2:

```csharp
private static void LocalizeXboxBackTargets(ComboBox comboBox)
{
    foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
    {
        if (item.Tag is not Xbox360BackButtonTarget target)
            continue;

        item.Content = MainUiStrings.Get(
            "Xbox_BackTarget_" + target,
            BackButtonMappingLabels.GetDisplayName(target));
    }
}
```

In `XboxPage()` retain:

```csharp
BackButtonMappingUiOptions.AddTargets(M1BackButtonTargetComboBox);
BackButtonMappingUiOptions.AddTargets(M2BackButtonTargetComboBox);
LocalizeXboxBackTargets(M1BackButtonTargetComboBox);
LocalizeXboxBackTargets(M2BackButtonTargetComboBox);
```

If desired, consolidate those four invocations into a *single local method* for readability; do not create another shared class.

Add all 18 `Xbox_BackTarget_<EnumName>` source keys to both RESWs, with fallback text **exactly** from `BackButtonMappingLabels.GetDisplayName(target)` (e.g. `D-Pad Up`, `Left Bumper (LB)`, `Xbox Guide`). Do not guess technical captions; read the existing contract. Translating buttons represented as technical A/B/X/Y is not necessary now, but the option-resource keys must be present to make later RESW-only translations possible.

**No changes** to:

- `BackButtonMappingUiOptions.cs` (shared with Controller).
- `BackButtonMapping.cs` contract, enum, `BackButtonMappingLabels`.
- `ControllerPage.xaml.cs`, Controller's `Controller_BackTarget_*` keys or their use.
- `TryGetSelectedTarget` / `SelectTarget` / `BackButtonMappingValidation`.
- `BackButtonMappingSettings`, Xbox360 output and serialized mapping.
- Per-game toggle `UseGlobalMapping` inversion, `null`-mapping semantics, version/save-chain gates.

**Test compatibility:** Existing Controller localization test expects XBOX not to reference the Controller-local method name `LocalizeControllerBackTargets`. The new XBOX-local method name must stay distinct; the XBOX page must have its own mapping-label tests.

## 5. Runtime-driven descriptions and PRI property paths

### 5.1 Intel FPS description written by Render

`Render(FrontendXboxGameProfileSnapshot)` currently overwrites the XAML `IntelFpsExpander.Description` on every snapshot with either:

1. `Uses Intel's official API. Some games may not support FPS limiting.`, when FPS is available;
2. the authoritative `snapshot.FpsLimit?.UnavailableReason`, if supplied by the Runtime;
3. otherwise `Intel FPS Limit is unavailable.`, a locally authored fallback.

Convert only `1` and `3` to `MainUiStrings.Get`. **Do not change any availability logic, nullable precedence, actual Runtime reason or InfoBar state**.

### 5.2 GPU description written by RenderGpuMinimumClock

`RenderGpuMinimumClock(...)` sets `GpuMinimumClockInfoBar.Message = gpu?.UnavailableReason ?? string.Empty` and `IsOpen` from the presence of the Runtime reason. Leave both exactly as-is.

It also replaces `GpuMinimumClockExpander.Description` with:

1. the Runtime `gpu?.UnavailableReason` if it is a nonempty string;
2. otherwise the same static base description from XAML: `Overrides the Device minimum GPU clock for this game. Actual clock may still fall lower when required by power or thermal limits.`

Convert only this static base description, preserving the exact `gpu?.UnavailableReason is { Length: > 0 } warning` branch and the original precedence.

### 5.3 Critical x:Uid property PRI lookup format

**Do not repeat the PR #787 Controller error.** For XAML resource names like:

```text
Xbox_IntelFpsExpander.Description
Xbox_GpuMinimumClockExpander.Description
```

the published PRI uses:

```text
/Resources/Xbox_IntelFpsExpander/Description
/Resources/Xbox_GpuMinimumClockExpander/Description
```

`MainUiStrings.Get` appends its argument after `Resources/` and does **not** replace the dot itself. The code must therefore use the slash-separated relative path to refer to an XAML property resource:

```csharp
MainUiStrings.Get(
    "Xbox_IntelFpsExpander/Description",
    "Uses Intel's official API. Some games may not support FPS limiting.");

MainUiStrings.Get(
    "Xbox_GpuMinimumClockExpander/Description",
    "Overrides the Device minimum GPU clock for this game. Actual clock may still fall lower when required by power or thermal limits.");
```

Source RESW keys must remain **dotted**, and C# `MainUiStrings.Get` accesses use **slash**. Do not duplicate the same visible description into a redundant plain key. The existing `scripts/verify-ui-localization-pri.ps1` was already extended by Steam PR4 to validate `PlaceholderText` alongside `Text|Content|Header|Description`; simply reuse it.

## 6. All XBOX-tab-authored UI messages, warnings and errors

Extract every **locally authored** XBOX UI InfoBar message / fallback failure. Preserve the old English exactly, including distinctions between `TDP Control` and `TDP` or `Windows Power Mode` and `Power Mode`.

Suggested stable XBOX-scoped resource keys:

| Existing place | Suggested key | Current exact English fallback |
| --- | --- | --- |
| `RefreshGamesAsync` non-ready response | `Xbox_Catalog_LoadFailed` | XBOX game catalog could not be loaded. |
| `RefreshGamesAsync` ready/empty | `Xbox_Catalog_NoGames` | No installed XBOX games were found. |
| `RefreshGamesAsync` exception | `Xbox_Catalog_RefreshFailed` | XBOX game catalog could not be refreshed. |
| `FavoriteButton_Click` failure/catch | `Xbox_Favorite_SaveFailed` | Favorite could not be saved. |
| `CaptureSelectedAsync` catch | `Xbox_Profile_LoadFailed` | XBOX profile settings could not be loaded. |
| `RenderProfileStatus` unwritable | `Xbox_Profile_SettingsUnwritable` | Profile settings could not be loaded, so changes are disabled to avoid overwriting the existing profile. |
| `RenderProfileStatus` no limits | `Xbox_Tdp_Unavailable` | TDP Control is unavailable on this device. |
| `Render` FPS missing/absent reason | `Xbox_IntelFps_Unavailable` | Intel FPS Limit is unavailable. |
| `GpuMinimumClockEnabledToggle_Toggled` / GPU slider commit | `Xbox_GpuMinimumClock_UpdateFailed` | Minimum GPU Clock could not be updated. |
| `FpsEnabledToggle_Toggled` / FPS slider commit | `Xbox_IntelFps_UpdateFailed` | Intel FPS Limit could not be updated. |
| `CpuBoostEnabledToggle_Toggled` | `Xbox_CpuBoost_UpdateFailed` | CPU Boost could not be updated. |
| `TdpEnabledToggle_Toggled` | `Xbox_TdpControl_ToggleUpdateFailed` | TDP Control could not be updated. |
| `PowerModeEnabledToggle_Toggled` | `Xbox_PowerMode_ToggleUpdateFailed` | Windows Power Mode could not be updated. |
| `ProfileEnabledToggle_Toggled` failure | `Xbox_Profile_UpdateFailed` | Profile could not be updated. |
| `ProfileEnabledToggle_Toggled` exception | `Xbox_Profile_RuntimeInterrupted` | Profile could not be updated because the Runtime connection was interrupted. |
| `PerGameBackButtonMappingEnabledToggle_Toggled` invalid target | `Xbox_BackMapping_InvalidTargets` | Choose valid M1 and M2 targets before saving the per-game mapping. |
| `SaveBackButtonMappingAfterAsync` failure/catch | `Xbox_BackMapping_UpdateFailed` | M1 / M2 mapping could not be updated. |
| `MutatePowerModeAsync` | `Xbox_PowerMode_UpdateFailed` | Power Mode could not be updated. |
| `ResolutionComboBox_SelectionChanged` | `Xbox_Resolution_UpdateFailed` | Display resolution could not be updated. |
| `MutateCpuAsync` | `Xbox_CpuBoost_UpdateFailed` | CPU Boost could not be updated. |
| `SubmitTdpAfterDelayAsync` | `Xbox_Tdp_UpdateFailed` | TDP could not be updated. |

The repeated `Xbox_CpuBoost_UpdateFailed` row represents the same exact message and needs **one key**, not duplicate keys. Shared identical error text across separate XBOX handlers should reuse the same XBOX key. All actual call sites must be updated, not just one of them.

### 6.1 Dynamic feature error composition

`MutateFeatureEnabledAsync(string feature, ...)` currently builds `"{feature} could not be updated."`. Avoid constructing sentences from translated fragments. Use a **complete localized failure sentence** as an argument, as already done in `ProfilePage` PR4.

**Safe example preserving existing RPC and mutation structure:**

```csharp
private async void CpuBoostEnabledToggle_Toggled(object sender, RoutedEventArgs e) =>
    await MutateFeatureEnabledAsync(
        MainUiStrings.Get("Xbox_CpuBoost_UpdateFailed", "CPU Boost could not be updated."),
        CpuBoostEnabledToggle.IsOn,
        (id, value) => _frontend!.SetXboxGameProfileCpuBoostEnabledAsync(id, value));

private async Task MutateFeatureEnabledAsync(
    string failureMessage,
    bool enabled,
    Func<string, bool, Task<FrontendXboxGameProfileMutationResult>> mutation)
{
    // Keep existing body and guards.
    // Replace only locally composed failure-message expressions with failureMessage.
}
```

Pass the corresponding `Xbox_TdpControl_ToggleUpdateFailed` and `Xbox_PowerMode_ToggleUpdateFailed` complete sentence for their toggles. Keep exact existing failure precedence. Do not alter the active-page guard or `IsCurrentProfileResponse` check.

### 6.2 Authoritative result/capture strings vs local fallbacks

**Must remain verbatim:** `snapshot.FailureMessage` (catalog), `result.FailureMessage` (all mutations), `snapshot.FpsLimit?.UnavailableReason`, `gpu?.UnavailableReason`, exception objects/messages, and XBOX game `DisplayName`.

A typical result path should continue to be:

```csharp
if (!result.Succeeded)
    ShowError(
        result.FailureMessage
        ?? MainUiStrings.Get("Xbox_BackMapping_UpdateFailed", "M1 / M2 mapping could not be updated."),
        null);
```

Keep `??` precedence, even if a raw Runtime string is empty; do not introduce locale-based rewrites or new null/empty conditions. Keep original severity and recovery handling in `ShowError` / `ShowInfo` / `ClearError`.

**Do not localize:** `AppLog` English messages, `Key` strings, `Tag`, request identifiers, RPC names, Steam/XBOX platform identifiers, paths, package IDs, generated game titles, telemetry/event markers.

## 7. Critical XBOX lifecycle and M1/M2 invariants — no incidental changes

The following are realistic supported product behavior; localization must not disturb them.

### Catalog lifecycle

- `Activate()` reuses a completed catalog; first entry scans, explicit Refresh scans again.
- `RefreshGamesAsync()` cancels the previous scan via `_scanCancellation`, observes `IsCurrentScan` and `_active`, retains retryability after a failure, and handles a previously selected game disappearing from the catalog.
- `Deactivate()` hides UI, cancels selected-profile capture, and cancels GPU commit debounce, **but does not cancel the catalog scan or clear the selected profile**, and **does not cancel pending TDP/FPS edits simply because the page is hidden**.
- Catalog search only checks `DisplayName`, not internal XBOX `Key`, `StoreId`, `TitleId`, `PackageFullName`, or `ConfigPath`. Do not expose those in UI.
- The favorite mutation uses `SetXboxGameProfileFavoriteAsync` and only updates the visible catalog after the expected returned profile key matches.

### Profile/draft lifecycle

- `CaptureSelectedAsync` checks `IsCurrentProfileResponse(_active, _selectedGame?.Key, key, snapshot.Key)`; cancellation on game change/navigation must remain.
- `Render` and `BeginProfileLoad` suppress C# UI-change events so localization of ComboBox labels does not create unwanted writes.
- Keep enabled/writable checks, expander collapsed defaults, AC/DC configuration, device limits and appropriate InfoBar states.
- Preserve the **300 ms** existing TDP/FPS/GPU debounce cadence and generation-aware update guards; do not add speculative locks/state or change how queued drafts settle.
- Avoid changing XBOX profile offline edits/pre-launch apply, active-target resolution and stored profile identity. No changes to runtime apply services.

### Per-game M1/M2

- The toggle means **per-game mapping enabled**; `!configuration.UseGlobalMapping` remains exact.
- `ReadSelectedBackButtonMapping` must continue to read typed `Xbox360BackButtonTarget` values from Tag, validate both and create exactly one `BackButtonMappingSettings(m1,m2)`.
- Saving disabled override must still pass `mapping: null`, which means **use global mapping**.
- Preserve `QueueBackButtonMappingSave`, `RunAfterPreviousAsync`, `_backButtonEditVersion` and acceptance of only the latest edit for the currently selected XBOX key.
- Do not add a synchronization/state-management framework solely for rare theoretical scheduling combinations.
- Preserve Full1902 Xbox360-only rear-button output policy, Runtime ownership and teardown.

## 8. Automated validation and brittle-test updates

**Expected directly affected tests:**

- `tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs` — add XBOX navigation, XAML UID/property, dynamic option and UI-owned fallback message checks.
- `tests/SteamInputAddonforClaw.UiTests/XboxCatalogPageUiTests.cs` — existing dense set of navigation, catalog, search, mapping, mutation, cancellation and draft contracts.
- `tests/SteamInputAddonforClaw.UiTests/OptionCardIconContractTests.cs` — option headings and glyphs must remain mapped to their exact original English XAML header fallbacks.
- `tests/SteamInputAddonforClaw.UiTests/DeviceGpuMinimumClockUiTests.cs` — Steam/XBOX GPU slider and draft behavior.
- `tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs` — cross-domain navigation / runtime, existing published UI and page boundaries.
- `tests/SteamInputAddonforClaw.Tests/BackButtonUiLayoutTests.cs` — lower-level button contracts if indirectly affected.

Be especially careful with source-string tests:

- `XboxCatalogPageUiTests.Main_navigation_shows_Steam_then_XBOX_as_separate_top_level_pages` expects the actual `Content` labels and `Tag` values. Keep literal XAML `Content="XBOX"` even when adding `x:Uid`.
- `XboxCatalogPageUiTests.Xbox_controller_editor_uses_per_game_override_toggle_and_persists_one_complete_mapping` expects the M1/M2 toggle name and `IsOn="False"` adjacency; avoid moving or changing this element's attributes for an unrelated text requirement.
- `XboxCatalogPageUiTests` asserts the two `AddTargets` calls; retain them and localize visible option content *after* population.
- `MainUiLocalizationResourceTests.Controller_code_display_resources_cover_actions_dynamic_back_targets_and_fallbacks` currently asserts the Controller-only local M1/M2 translation helper does not appear in `XboxPage.xaml.cs`; use a different XBOX-local helper, not that Controller method name.
- Source tests that assert exact labels/enum declarations may need to check XBOX resource keys and typed item identity. Do not weaken actual acceptance of UI state changes, rollback, stale-game filtering or save order.

**Required focused tests:**

1. Both `en-US` and `ko-KR` RESW contain the **same nonempty key set**; XBOX additions are exact existing English in both languages at PR delivery but tests must **not** permanently assert en/ko equality.
2. `MainNav_Xbox.Content` maps to XBOX navigation and the `Tag="Xbox"` and order remain unchanged.
3. XBOX static XAML `x:Uid` values are unique; visible prose properties `Text|Content|Header|Description|PlaceholderText` have matching RESW entries. Exclude data-binding properties and units rather than trying to localize runtime dynamic game names.
4. The seven CPU / three Windows Power modes preserve enum identity and ordered labels; `Do not change` is RESW-backed while numerical sizes, ordering and null semantics remain unchanged.
5. For **every 18 `Xbox360BackButtonTarget` enum values**, assert the key `Xbox_BackTarget_<Name>` exists in both languages and its fallback comes from `BackButtonMappingLabels.GetDisplayName(target)`. Verify typed `ComboBoxItem.Tag` and `BackButtonMappingUiOptions` are unchanged and localization follows population for **both** combos.
6. XBOX source-owned errors are RESW-backed; the original `result.FailureMessage`/`snapshot.FailureMessage`/`UnavailableReason` precedence remains, including M1/M2 invalid-selection and save failures.
7. `Render` paths lookup the **slash-separated** key `Xbox_IntelFpsExpander/Description` / `Xbox_GpuMinimumClockExpander/Description`; the source RESW key is dotted and both published candidates are present.
8. Verify XBOX catalog scan gating, profile capture cancelation, active selected key, retry semantics, per-game M1/M2 ordered whole-mapping writes, and TDP/FPS/GPU debounce from the pre-existing tests.
9. Confirm PR4 Steam strings remain untouched; do not modify another tab to make the XBOX tests pass.

**Published PRI is mandatory**: run the repository's existing `scripts/publish-layout.ps1` which invokes `scripts/verify-ui-localization-pri.ps1`. PR4 already added XAML `PlaceholderText` URI handling. Each new XBOX key should resolve from the published `SteamInputAddonforClaw.UI.pri`, with `en-US` default and exactly one nonempty `en-US` and `ko-KR` candidate. Source RESW checks alone are not enough. Do not add a second PRI script or work around missing candidate failures.

Do not pin the total resource-key count, use placeholders for missing keys, or embed Korean translations in test assertions.

## 9. Acceptance criteria / expected diff

### Expected changed files

```text
src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/Views/XboxPage.xaml
src/SteamInputAddonforClaw.UI/Views/XboxPage.xaml.cs
src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw
src/SteamInputAddonforClaw.UI/Strings/ko-KR/Resources.resw
tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs
tests/SteamInputAddonforClaw.UiTests/XboxCatalogPageUiTests.cs       (only if necessary)
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs        (only if necessary)
other directly affected existing source-/PRI-contract tests        (only if necessary)
```

Normally **no change** needed in `scripts/verify-ui-localization-pri.ps1`, `MainUiStrings.cs`, `BackButtonMappingUiOptions.cs`, `ControllerPage.xaml.cs` or `ProfilePage.xaml.cs`.

### Automated validation (local Codex / CI)

- `dotnet restore SteamInputAddonforClaw.slnx` and release solution/UI test builds as supported by the repo.
- Existing core and UI automated test suites; particularly XBOX catalog/mutation and Main UI localization tests.
- Existing repository validation/check scripts and `git diff --check`.
- `scripts/publish-layout.ps1` and published PRI validation (including `PlaceholderText` and slash-separated Description property paths).
- `scripts/verify-publish-assets.ps1` and relevant publish/size smoke checks.
- Document meaningful test/build results in PR description. If a check needs actual MSI Claw hardware, omit it and leave physical validation to the user.

### Definition of done

- XBOX nav, catalog, profile detail, M1/M2 options, performance controls and all UI-originated warnings/errors support future translation by editing **only** `ko-KR/Resources.resw`.
- Both new source-language candidates are initially the exact existing English; no early Korean translation.
- The XBOX UI retains original source English fallbacks.
- All Runtime/Frontend-originated messages and XBOX game `DisplayName` remain untouched with identical priority.
- All XBOX game `Key`, typed M1/M2 `Tag`, save ordering, profile persistence, cancel/debounce and active-page guards are intact.
- No Steam/XBOX unification, no speculative race machinery, no new localization layer and no Runtime/controller authority changes.
- Automated tests and packaged-PRI verification pass. **Real-device tests are post-merge user responsibility and are not blockers.**

## 10. Local Codex submission instructions

Open **one** PR5 for all XBOX tab localization changes. In the PR summary:

1. Identify changed XBOX catalog/detail areas and total added XBOX/nav resources.
2. Explicitly state how M1/M2 typed tags, target order, global/per-game override and queue/version save policy remain unchanged.
3. Confirm UI-owned errors are RESW-backed and Runtime messages/game identity are unmodified.
4. Report tests, Release build, publish-PRI validation and source/PRI language candidate checks.
5. State that no additional physical-device testing was performed/required by Codex.

This work order is documentation only. Local Codex implements the code; the user handles physical verification after merge.
