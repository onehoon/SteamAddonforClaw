# Work Order — Main App RESW PR4: Steam Tab (Catalog + Per-Game Profile)

**Date:** 2026-10-14  
**Repository:** `onehoon/SteamAddonforClaw`  
**Implementation owner:** Local Codex  
**Baseline:** Latest `main`, including the merged localization PRs #785 (foundation), #786 (Device), and #787 (Controller). Read HEAD before editing.  
**Delivery:** **One focused PR4** for the entire Steam tab, including its catalog and selected-game detail view; do not split those views into separate PRs.  
**Physical verification:** The **user performs it after merge**. No MSI Claw hardware test may be required of local Codex or used as a PR/merge blocker.

## 1. Goal and fixed product decisions

Complete `en-US` / `ko-KR` resource extraction for **all Main App Steam-tab-authored user-facing text**, including normal UI, status/info messages, warnings, and **UI-authored error/failure fallback messages**. This is intentionally the **same policy already delivered for Device and Controller**.

- Both RESW language files initially contain the **exact current English text** for every new key. **Do not write Korean translations in PR4**. The user will translate `ko-KR` later without changing code.
- Preserve literal English fallbacks in XAML and `MainUiStrings.Get` calls. Do not change punctuation, capitalization, spaces, units or the exact existing error wording.
- Preserve **Runtime-/Frontend-authored** `FailureMessage`, `UnavailableReason` and game titles verbatim. They are data, not Steam-tab-owned copy.
- Include the Steam NavigationView entry and **both** existing `CatalogPanel` and `DetailPanel`. Keep them in the current single `ProfilePage` class.
- No new language selector, translation service, global string manager, centralized profile-UI refactor, storage format, localization refresh state, or locale override.
- **No behavior changes:** no catalog scan optimization, debounce changes, game/profile mutation changes, graphics/Steam driver or PID1902/HidHide/VIIPER authority changes.

**Do not edit** the XBOX page, the standalone Overlay, Steam QAM, Device summary, Developer screen, Runtime, or any product docs/user guides for localization in PR4. XBOX has its own later PR5.

## 2. Architecture/document authority — read first

Required references, in current precedence order:

1. `docs/Full 1902 Implementation/README.md` — Full1902 authority order.
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`.
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`.
4. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`.
5. `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`.
6. `docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md`, especially the Steam/XBOX **separate identity/catalog/page ownership**. The existing Steam `ProfilePage` and Steam AppID contract remain authoritative; renaming the class is not a localization requirement.
7. `docs/work-order/2026-10-14_MAIN_APP_DEVICE_TAB_RESW_PR2_WORK_ORDER.md`.
8. `docs/work-order/2026-10-14_MAIN_APP_CONTROLLER_TAB_RESW_PR3_WORK_ORDER.md` — source vs Runtime errors, publish-PRI checks and slash-separated PRI property lookup.

Required current code:

- `src/SteamInputAddonforClaw.UI/MainWindow.xaml`, `MainWindow.xaml.cs`.
- `src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml` (Steam Main App).
- `src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs`.
- `src/SteamInputAddonforClaw.UI/MainUiStrings.cs`.
- `src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw` and `ko-KR/Resources.resw`.
- `scripts/verify-ui-localization-pri.ps1`, `scripts/publish-layout.ps1`, `scripts/verify-publish-assets.ps1`.
- Relevant tests named in `8`.

The elevated Runtime owns Steam detection, controller presentation, persistent controller/device state, and profile apply. Main UI is a frontend; neither localized text nor UI process lifetime becomes controller/profile authority. **CTW integration is obsolete** and is not part of this work.

## 3. Current implementation inventory (verified on main)

`ProfilePage.xaml` is currently a compact ~12 KB XAML file (many elements share physical source lines). `ProfilePage.xaml.cs` is ~478 physical lines (~40 KB) containing several long one-line methods. **Do not treat physical LOC as feature size; do not mass-reformat the source for this localization PR.**

### 3.1 Navigation and page composition

`MainWindow.xaml` currently contains the **Steam-labeled navigation entry**:

```xml
<NavigationViewItem Content="Steam" Tag="Profile">
```

This is the **Steam page**, but internal navigation identity is `Profile`. Add only `x:Uid="MainNav_Steam"` with `MainNav_Steam.Content`. **Keep `Tag="Profile"`**, item order, MainNavigationState routing, lazy `ProfilePageHost` creation/warmup, and selection lifecycle unchanged.

`ProfilePage.xaml` contains exactly two major page states:

- `CatalogPanel` with `Steam` title, `↻ Refresh` button, `Search games...` placeholder, game cards and favorite star buttons.
- `DetailPanel` with `Back`, selected-game title and enable toggle, followed by performance/settings cards.

Do not split page classes, rename `ProfilePage`, change `CatalogPanel` / `DetailPanel` structure, reflow card layout, alter header icons, or change the search/card behaviors.

### 3.2 Detail card order and contents

Current order under selected-game detail:

1. `Virtual Controller` — per-Steam-game `Use default`, `Steam Deck`, `DualSense Edge` options.
2. `TDP Control` — AC/DC (Plugged in / On battery), PL1/PL2, watts.
3. `CPU Boost` — AC/DC and seven dynamically constructed `CpuBoostModeItem` labels.
4. `Windows Power Mode` — AC/DC and three dynamically constructed `PowerModeItem` labels.
5. `Minimum GPU Clock` — AC/DC MHz; availability warning and runtime-dependent description.
6. `Intel FPS Limit` — AC/DC FPS; runtime-dependent description.
7. `Resolution` — dynamic `ResolutionItem` list, starting with `Do not change` and four numbered sizes.

All card headings/descriptions and human-readable choices belong to Steam UI localization. Numeric measurements and device/game values do not require translation.

### 3.3 Existing dynamic display and error sources

`ProfilePage.xaml.cs` currently owns:

- `Modes`: Disabled, Enabled, Aggressive, Efficient Enabled, Efficient Aggressive, Aggressive At Guaranteed, Efficient Aggressive At Guaranteed.
- `PowerModes`: Best power efficiency, Balanced, Best performance.
- `ResolutionItems`: Do not change, then 1920 × 1200, 1920 × 1080, 1680 × 1050, 1440 × 900.
- `Render(...)`: overwrites `IntelFpsExpander.Description` based on `snapshot.FpsLimit`.
- `RenderGpuMinimumClock(...)`: overwrites `GpuMinimumClockExpander.Description` and `GpuMinimumClockInfoBar.Message` based on `gpu.UnavailableReason`.
- Catalog empty/refresh failures; favorite failure; profile load/writability errors; TDP unavailable; per-game virtual-controller mutation failure; GPU/FPS/TDP/CPU/Power/Resolution/Profile mutation errors; error recovery helpers.

`ShowError(message, exception)` currently logs the supplied message when there is an exception. Preserve its existing call semantics, logging category, severity and behavior. Do not invent a logging translation subsystem. `AppLog.Info/Warn` literal event messages, `ManualRefresh` / `FirstTabEntry` markers, exception type data and other machine diagnostic identifiers are **not** RESW resources.

## 4. XAML localization: fixed visible properties only

For each fixed **human-readable** `Text` / `Content` / `Header` / `Description` / `PlaceholderText`, add stable unique `x:Uid` and a matching source key `<Uid>.<Property>` in **both** existing RESW files.

Naming examples:

```xml
<NavigationViewItem x:Uid="MainNav_Steam" Content="Steam" Tag="Profile" />

<TextBox x:Name="GameSearchBox"
         x:Uid="Steam_GameSearchBox"
         PlaceholderText="Search games..."
         TextChanged="GameSearchBox_TextChanged"/>

<ctcontrols:SettingsExpander x:Name="GpuMinimumClockExpander"
                             x:Uid="Steam_GpuMinimumClockExpander"
                             Header="Minimum GPU Clock"
                             Description="Overrides the Device minimum GPU clock for this game. Actual clock may still fall lower when required by power or thermal limits." />
```

RESW entries:

```xml
<data name="MainNav_Steam.Content" xml:space="preserve"><value>Steam</value></data>
<data name="Steam_GameSearchBox.PlaceholderText" xml:space="preserve"><value>Search games...</value></data>
<data name="Steam_GpuMinimumClockExpander.Description" xml:space="preserve"><value>Overrides the Device minimum GPU clock for this game. Actual clock may still fall lower when required by power or thermal limits.</value></data>
```

The above is structural guidance: **do not replace actual elements with simplified examples or remove other attributes**.

### Mandatory visible inventory

| Area | Current user-facing values to extract |
| --- | --- |
| Navigation/header | `Steam` navigation Content; `Steam` page title |
| Catalog controls | `↻ Refresh`, `Search games...` (`PlaceholderText`) |
| Detail navigation | `Back` |
| Virtual Controller | Header, description `Overrides the Controller default for this Steam game.`, `Use default`, `Steam Deck`, `DualSense Edge` |
| TDP | `TDP Control`, description, `Plugged in` and `On battery` |
| CPU | `CPU Boost`, `Processor performance boost mode.`, AC/DC headers |
| Power | `Windows Power Mode`, `Configured AC/DC Windows power mode.`, AC/DC headers |
| GPU | `Minimum GPU Clock`, full thermal-limits description, AC/DC headers |
| Intel FPS | `Intel FPS Limit`, full official-API description, AC/DC headers |
| Resolution | `Resolution` |

Use independent UIDs for repeated `Plugged in` / `On battery` labels because distinct controls own those values. Preserve the original English values in each XAML property as fallback.

**Do not localize these identities or data bindings:**

- `Tag="Profile"` in MainWindow and the existing per-game controller option tags `Default` / `SteamDeck` / `DualSenseEdge`.
- `Tag="{Binding Game}"` (selected catalog item), `Text="{Binding Name}"` (game title) and `Content="{Binding FavoriteGlyph}"` (★/☆), selected game name, Steam AppID.
- `x:Name`, x:Class, event-handler names, XAML resource/style names, `DisplayMemberPath="Label"`, enumeration values.
- Technical units and identifiers `PL1`, `PL2`, `W`, `MHz`, `FPS`, `1920 × 1200` etc. Dynamic measurement strings and em-dash placeholders `— W` / `— MHz` remain as currently rendered, except `Do not change` is real prose and MUST be extracted.
- Empty toggle `OnContent=""` and `OffContent=""`: leave untouched.

`x:Uid` is for actual fixed text-bearing elements, not all layout/binding elements. Test against the real XAML structure, including the current long-line formatting.

## 5. C# localization — include UI-authored failures

### 5.1 Reuse existing helper

Use only `MainUiStrings.Get(key, englishFallback)`, and `MainUiStrings.Format(...)` if a true formatted message requires it. Do not instantiate a new `ResourceManager` or duplicate the common helper. Use `Steam_*` keys to keep this tab independently maintainable; do not make PR4 depend on changing Device/Controller resources.

For example:

```csharp
new CpuBoostModeItem(
    CpuBoostMode.EfficientAggressive,
    MainUiStrings.Get("Steam_CpuBoost_Mode_EfficientAggressive", "Efficient Aggressive"))
```

Keep enum/option identity and source ordering. C# `PowerModes` is currently declared near the end of the class; no architecture change is needed.

### 5.2 CPU, power and resolution mode lists

Extract all **seven** `CpuBoostModeItem.Label` texts and all **three** `PowerModeItem.Label` texts through corresponding keys. Keep the seven `CpuBoostMode` enum associations, three `WindowsPowerMode` enum associations, list ordering and `DisplayMemberPath="Label"`.

Extract the sole `Do not change` `ResolutionItem.Label`. Numerical resolution labels must remain exact and in current order:

```text
Do not change
1920 × 1200
1920 × 1080
1680 × 1050
1440 × 900
```

Store/select based on `Width` / `Height`, **never translated Label text**. Keep null resolution = do not change, including its profile mutation behavior.

### 5.3 C# overwrites of XAML descriptions — critical

`Render(...)` currently assigns:

```csharp
IntelFpsExpander.Description = snapshot.FpsLimit?.Available == true
    ? "Uses Intel's official API. Some games may not support FPS limiting."
    : snapshot.FpsLimit?.UnavailableReason ?? "Intel FPS Limit is unavailable.";
```

Keep this exact availability decision and precedence. Source-authored strings use RESW; `snapshot.FpsLimit?.UnavailableReason` remains **verbatim**, so a Runtime-originated explanation always outranks the local `Intel FPS Limit is unavailable.` fallback.

Likewise `RenderGpuMinimumClock(...)`:

- `gpu?.UnavailableReason` is authoritative and must be displayed verbatim in `GpuMinimumClockInfoBar` and `GpuMinimumClockExpander.Description`.
- Otherwise use the **same localized base description** as the XAML `Steam_GpuMinimumClockExpander.Description`.
- Do not modify availability flags, dirty draft, slider indices/clock formatting, warning visibility or error severity.

**Important actual PRI key pitfall (previous Controller PR review #787):** An RESW `x:Uid` property key written `Steam_GpuMinimumClockExpander.Description` is stored as `/Resources/Steam_GpuMinimumClockExpander/Description` in published PRI. `MainUiStrings.Get` takes the relative path appended after `Resources/`.

Correct for **C# reading an x:Uid property resource**:

```csharp
MainUiStrings.Get(
    "Steam_GpuMinimumClockExpander/Description",
    "Overrides the Device minimum GPU clock for this game. Actual clock may still fall lower when required by power or thermal limits.");
```

And analogously:

```csharp
MainUiStrings.Get(
    "Steam_IntelFpsExpander/Description",
    "Uses Intel's official API. Some games may not support FPS limiting.");
```

The **source RESW key stays dotted**; only direct C# runtime lookup uses the slash-separated property path. Never pass `Steam_GpuMinimumClockExpander.Description` directly to `MainUiStrings.Get`. Do not duplicate the description as a plain additional RESW key, and do not modify `MainUiStrings` to implement a new translation layer.

### 5.4 All UI-authored errors, warnings and empty-status messages

**Include** the original Steam tab's own errors and recoverable warnings in `en-US` / `ko-KR`. Suggested key inventory (derive final stable names from code; every branch must be covered):

| Existing source path | Exact existing English user text |
| --- | --- |
| `RefreshGamesAsync` empty catalog | `No installed Steam games were found.` |
| `RefreshGamesAsync` scan exception | `Game catalog could not be refreshed.` |
| `FavoriteButton_Click` failure and catch | `Favorite could not be saved.` |
| `CaptureSelectedAsync` catch | `Profile settings could not be loaded.` |
| `RenderProfileStatus` persistence unavailable | `Profile settings could not be loaded, so changes are disabled to avoid overwriting the existing profile.` |
| `RenderProfileStatus` missing TDP limits | `TDP Control is unavailable on this device.` |
| `SteamVirtualControllerComboBox_SelectionChanged` result and catch | `Virtual Controller could not be updated.` |
| `Render(...)` FPS local fallback | `Intel FPS Limit is unavailable.` |
| `GpuMinimumClockEnabledToggle_Toggled` and `SubmitGpuMinimumClockAfterDelayAsync` failures | `Minimum GPU Clock could not be updated.` |
| `FpsEnabledToggle_Toggled` and `SubmitFpsAfterDelayAsync` failures | `Intel FPS Limit could not be updated.` |
| `CpuBoostEnabledToggle_Toggled` + `MutateCpuAsync` failures | `CPU Boost could not be updated.` |
| `TdpEnabledToggle_Toggled` + `SubmitTdpAfterDelayAsync` failures | `TDP Control could not be updated.` / `TDP could not be updated.` (different existing strings — preserve exact branch wording) |
| `PowerModeEnabledToggle_Toggled` + `MutatePowerModeAsync` failures | `Windows Power Mode could not be updated.` / `Power Mode could not be updated.` |
| `ProfileEnabledToggle_Toggled` result/catch | `Profile could not be updated.` / `Profile could not be updated because the Runtime connection was interrupted.` |
| `ResolutionComboBox_SelectionChanged` failures | `Display resolution could not be updated.` |

`MutateFeatureEnabledAsync` currently receives a `string feature` (`CPU Boost`, `TDP Control`, `Windows Power Mode`) and constructs a failure sentence by interpolating the feature plus `could not be updated.`. **Do not translate only the suffix while leaving feature names English**, and do not require translators to compose sentences by concatenating fragments. Prefer a small **already complete, tab-owned translated error message** argument for this existing method, e.g.:

```csharp
private async void CpuBoostEnabledToggle_Toggled(object sender, RoutedEventArgs e) =>
    await MutateFeatureEnabledAsync(
        MainUiStrings.Get("Steam_CpuBoost_ToggleUpdateFailed", "CPU Boost could not be updated."),
        CpuBoostEnabledToggle.IsOn,
        (id, value) => _frontend!.SetGameProfileCpuBoostEnabledAsync(id, value));

private async Task MutateFeatureEnabledAsync(
    string failureMessage,
    bool enabled,
    Func<uint, bool, Task<FrontendGameProfileMutationResult>> mutation)
{
    // Preserve the exact existing method body, guards, RPC, Render and catch/recovery flow.
    // Only use failureMessage where the old code constructed the UI fallback message.
}
```

This is a narrow parameter meaning change, **not** permission to change its error handling. Use analogous complete-string keys for TDP and Windows Power Mode toggles. Keep the exact old text per branch and avoid gratuitous duplicate RESW entries when the identical UI copy is reused in several handlers. Existing Runtime-provided `result.FailureMessage` **must still take precedence** over the local fallback.

Specifically, preserve every `ShowError(result.FailureMessage ?? localFallback, ...)` and each `RestoreSelectedAfterMutationFailureAsync(appId, localFallback, exception, ...)` recovery sequence. Keep the same selected-game identity and `IsCurrentProfileResponse` checks; localization never affects whether a result is accepted.

The logging line in `ShowError` may receive localized display text through its existing `message` argument. Do not redesign `ShowError`/logging or add duplicate English logging state for this work; preserve original event sites, severity and diagnostic categories.

### 5.5 Steam virtual-controller semantics

Keep all three `ComboBoxItem.Tag` values `Default`, `SteamDeck`, `DualSenseEdge` exact. `Use default` is a visible caption; it must be localized, but the selected/tag lookup is unaffected. Preserve:

- `CanEditGameSteamVirtualController` requires known Steam-idle state, writable/enabled/existing profile, and no active mutation.
- Selection rollback and `SetSteamActivity(null)` / `SteamActivityRefreshRequested` after failure.
- `RenderControllerMutationSnapshot` preserves pending TDP/GPU/FPS drafts and the current profile identity.
- No changes to the global Controller-tab Steam virtual-controller selector.

### 5.6 UI messages vs Runtime data — exact boundary

**Localize:** Only fixed captions, locally constructed InfoBar warnings, locally constructed error strings (including `Runtime connection was interrupted` **when written by the UI**), and local error fallback strings.

**Do not localize or rewrite:** `result.FailureMessage`, `snapshot.FpsLimit.UnavailableReason`, `gpu.UnavailableReason`, Steam/game title, AppID, exception messages, log string literals/event tags, telemetry identifiers, file paths, internal enum names or profile-state machine inputs.

If a branch uses a Runtime message plus a UI fallback, preserve the existing precedence and null/empty semantics; do not introduce new coercion or filter conditions. In particular, preserve the branch that uses `gpu?.UnavailableReason ?? string.Empty` in the GPU InfoBar.

### 5.7 Numeric/technical display

Retain `FPS`, `W`, `MHz`, `PL1` and `PL2` as current technical units/identifiers. Retain `x FPS`, `x W`, `— W`, `— MHz` format and current GPU clock helper. Do not localize numeric source values, format conversions, Steam AppIDs or controller option tags.

## 6. PRI and build-system impact — required minimal verifier change

Current `scripts/verify-ui-localization-pri.ps1` checks **every** RESW key in the published `SteamInputAddonforClaw.UI.pri` and treats `.Text` / `.Content` / `.Header` / `.Description` as x:Uid property resources. It does **not** yet include `.PlaceholderText`, which PR4 needs for `GameSearchBox`.

Make one narrow edit to the existing matcher:

```powershell
if ($key -match '^(?<uid>.+)\.(?<property>Text|Content|Header|Description|PlaceholderText)$') {
    $uriSuffix = "/Resources/$($Matches.uid)/$($Matches.property)"
}
else {
    $uriSuffix = "/Resources/$key"
}
```

The actual published PRI may require a corresponding `/Resources/Steam_GameSearchBox/PlaceholderText` candidate. Validate **both** `en-US` and `ko-KR`; source-only key parity is insufficient.

Do not special-case a single Steam key, pin the total count of RESW keys, skip a candidate, generate a fresh PRI with `makepri new`, edit deployment topology, change language defaults or add a new pipeline. Preserve Windows App SDK / WinUI3 external publish layout.

If PRI inspection demonstrates the new `PlaceholderText` property behaves differently from the assumption above, correct the **smallest actual mismatch** based on the emitted PRI and document the evidence; do not bypass verification.

## 7. Protection against scope expansion / regression

The following pre-existing contracts must stay intact:

- MainWindow navigation: `Content="Steam"` stays English source fallback, `Tag="Profile"` remains the navigation identity; never rename `MainNavigationPage.Profile` or change the page host.
- Game catalog: first-entry/on-demand scan, successful cache, explicit refresh, refresh-button busy state, search by `Name` or AppID, favorite-first ordering, card `Tag` / favorites ★ and ☆, selected-game title and return navigation.
- Steam-specific profile identity: keep `uint AppId`, `FrontendProfileGameCatalogEntry` and Steam RPCs; **do not merge Steam and Xbox profiles** or introduce cross-domain Game Keys.
- Game profile enable/disable, Steam virtual-controller override, option enable toggles, expandable cards, actual AC/DC hardware/performance application.
- `Render` and `BeginProfileLoad` suppression flags, `SetEditorsEnabled`, `CaptureSelectedAsync`, mutation result AppID guard and rollback/error pathways.
- 300ms FPS/TDP/GPU draft settling, cancellation and generation guards, GPU pending-draft preservation, TDP pending draft, clock indices and selectability, no new concurrency machinery.
- GPU `UnavailableReason` / FPS `UnavailableReason` authority, whether a message is shown, and `InfoBarSeverity`.
- Page lifecycle `Activate`, `Deactivate`, `StateInvalidated` event subscriptions, lazy page construction/warmup, no unwanted catalog scanning during startup.
- Existing icons and card header hierarchy, profile toggle position, collapsed-by-default expanders, resolution single-card (not an expander), layout spacing and DPI behavior.
- No changes to Steam OS, Windows controller ownership, physical device lifecycle, external Overlay, XBOX page, Steam QAM, CTW, profile persistence schema or frontend transport.

Do not opportunistically refactor the long `ProfilePage.xaml.cs` methods to multiple new classes or generic services. The requested work is only localization extraction.

## 8. Automated tests — focus on source/resource and behavior contracts

Review and update, as narrowly as necessary:

- `tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs` — add Steam nav, XAML `x:Uid` and C# `Steam_*` key coverage; `PlaceholderText` mapping; key parity/nonblank, English fallback, **no en/ko value-equality requirement** for future translations.
- `tests/SteamInputAddonforClaw.UiTests/ProfilePagePolicyTests.cs` — preserve catalog load-once, manual refresh and TDP newer-draft behavior.
- `tests/SteamInputAddonforClaw.UiTests/SteamVirtualControllerUiTests.cs` — assertions for `Content="Use default" Tag="Default"` may need XDocument-based checks after adding `x:Uid`. Preserve actual tag/enum, disabled states, active Steam gating, RPC/rollback behavior.
- `tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs` — current `Profile_feature_order_collapsed_defaults_and_resolution_contract_are_explicit` contains exact source matches for `<ctcontrols:SettingsCard Header="Resolution">`, `new(null, null, "Do not change")` and Intel FPS description source assignments. **Update those brittle string checks to resource-aware/structural assertions**, but retain the actual feature order, collapsed defaults, resolution-only card, power-mode controls, GPU/FPS error precedence and profile ownership.
- `tests/SteamInputAddonforClaw.UiTests/XboxCatalogPageUiTests.cs` — verify existing Steam/XBOX separation, Xbox unaffected, and Steam page scan/name/tag identity.
- `tests/SteamInputAddonforClaw.UiTests/OptionCardIconContractTests.cs` — retain existing literal **English XAML Header fallbacks** and all icons, so the current icon map test should still pass.
- `tests/SteamInputAddonforClaw.UiTests/DeviceGpuMinimumClockUiTests.cs` — preserve prior GPU-profile/UI policy and draft tests.
- Existing publish layout, PRI verification, asset verifier regression tests.

**Focused new test expectations:**

1. `MainNav_Steam.Content` exists and `Tag="Profile"` remains exact.
2. Every intended Steam static XAML UID/property has a matching nonempty key in both languages. Exclude source binding expressions, empty toggle labels, changing game titles and non-prose units.
3. All seven CPU and three Power mode enum→Label mappings are preserved; only their display labels use RESW. `Do not change` is localized, while numerical resolutions and their order/width/height stay unchanged.
4. `SteamVirtualControllerComboBox` keeps `Default` / `SteamDeck` / `DualSenseEdge` exact tags and all runtime guards.
5. `IntelFpsExpander.Description` and `GpuMinimumClockExpander.Description` use the **slash-separated C# key** when reading the original `x:Uid` property resource, so later Korean translation persists after `Render`. Assert the corresponding **dotted RESW key** exists.
6. All local errors (including completed feature-name errors and catalog/virtual-controller failures) have keys; Runtime `FailureMessage` and `UnavailableReason` remain unchanged and precede local fallbacks.
7. Runtime UI state/update paths still preserve newer FPS/TDP/GPU user edits and respect stale-AppID responses; tests must not be weakened merely to pass after string extraction.
8. The extended publish-PRI verifier resolves `Steam_GameSearchBox.PlaceholderText` as `/Resources/Steam_GameSearchBox/PlaceholderText`, and published PRI contains exactly one nonempty candidate for both languages.

Do **not** hardcode number of languages beyond the project's current `en-US`/`ko-KR` contract, total number of keys, entire resource file text, or future Korean translation values. Avoid assertions that the Korean value must equal the English value after this PR.

## 9. Implementation / CI acceptance

Perform and report standard local code-only validation:

1. `dotnet restore SteamInputAddonforClaw.slnx` (where supported).
2. Release build for the solution and relevant UI tests.
3. Existing Core/UI automated tests including updated localization and Steam profile policy checks.
4. Existing repository PowerShell CI checks.
5. `scripts/publish-layout.ps1` including published **Main UI PRI** default `en-US` and candidates in **both** languages for every key; verify `PlaceholderText` specifically.
6. `scripts/verify-publish-assets.ps1` and other existing publish smoke/checks that do not require physical MSI Claw hardware.
7. `git diff --check`. Confirm no unrelated changes.

**Merge-ready code criteria:**

- Both RESW files hold exact source English for every newly extracted UI key; all keys valid and nonempty.
- Main App Steam navigation, catalog, detail, dynamic labels, UI-authored errors/warnings and overwritten descriptions use resources.
- UI `x:Uid` property key lookup from C# uses `/` between UID and property, preventing a post-render return to English when `ko-KR` is translated later.
- Raw Runtime messages and game names remain untouched, with identical precedence, severity, visibility and recovery.
- Steam and XBOX remain separate; no Controller/Device/Overlay/Runtime/transport behavior modified.
- Tests/published PRI/build pass; no unsupported theoretical race hardening or generic abstractions.
- **Physical hardware tests remain the user's post-merge role**. Never block PR4 on those tests.

## 10. Expected changed files

```text
src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml
src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs
src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw
src/SteamInputAddonforClaw.UI/Strings/ko-KR/Resources.resw
scripts/verify-ui-localization-pri.ps1
tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs
tests/SteamInputAddonforClaw.UiTests/SteamVirtualControllerUiTests.cs    (if needed)
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs             (if needed)
other directly impacted existing source/PRI tests                    (if needed)
```

No new resource helper or infrastructure project is expected. No need to change `XboxPage.xaml[.cs]`, `BackButtonMappingUiOptions.cs`, `SteamInputAddonforClaw.Contracts`, `FrontendTransport`, Steam profile Runtime implementation, or external Overlay.

## 11. Local Codex PR handoff

Submit **one** PR4 implementation, documenting:

- all Steam catalog/detail UI resource regions and local failure strings extracted;
- chosen stable `Steam_*` key naming for modes and UI fallback errors;
- explicit proof that Runtime-provided text/Steam AppIDs/tags are still untouched;
- source key parity, published `PlaceholderText` and `.Description` PRI lookup proof, and build/test/publish results;
- confirmation of no user-visible behavior change while both RESWs still contain English.

Do not require installation, physical controller testing, suspend/resume, firmware writes or on-device validation from Codex. Those checks are performed by the user **after merge**.

This commit creates the **work order only**. The implementation is the local Codex's responsibility.
