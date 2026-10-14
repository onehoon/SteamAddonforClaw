# Work Order — Main App RESW PR9: Settings (General UI Only; Exclude Logging Levels and All Developer Menus)

**Date:** 2026-10-14  
**Repository:** `onehoon/SteamAddonforClaw`  
**Owner:** Local Codex (implementation and automated verification); user (physical MSI Claw verification **after merge**)  
**Baseline:** Latest `main` after Main App localization PRs #785–#793, particularly merged **PR #793** (Tweaks). Re-read HEAD before implementation.  
**Deliverable:** One focused **PR9**; **Settings general user interface only**. No Developer Menu localization, no Logging level option localization.

## 0. Explicit user decisions — these override a generic “translate every string” policy

The user explicitly finalized these exceptions:

1. **Logging Off / Info / Debug selection options are excluded.** Keep their literal English `ComboBoxItem.Content` attributes and the existing `SettingsPage.LogLevelComboBox_SelectionChanged` implementation untouched. The logging card's **Header “Logging,” Description “Choose log detail.”, and “Open folder” button remain normal user-facing Settings UI and ARE included**.
2. **Developer Menu is excluded completely**: the `DeveloperMenuCard` Settings option card (both `Header="Developer Menu"` and its Description), its navigation/click handler, the developer entry warning `ContentDialog`, its checkbox, every Developer Menu page and child/submenu (including probes/diagnostics) remain English and unchanged. **Do not attach x:Uid to the DeveloperMenuCard or add RESW keys for those developer surfaces.**
3. Localize **only the remaining General Settings UI**, using the existing `en-US` / `ko-KR` resource files. **Both language values remain the original English in this PR**; the Korean translation comes later.
4. No change to the actual Settings functions, updater, Windows Gaming Full Screen Experience, logging, quick-settings power-source policy, required component authority, update badge, or developer gating.

**Interpretation:** “Exclude Logging selection” does **not** exclude the whole Logging card. “Exclude Developer Menu” includes its card **and everything behind it**, not just the developer page.

Do **not** use this PR to localize the MainWindow prerequisite-installation modal, the global window title/update-ready suffix, or other non-Settings Main App shell strings. Those may be addressed in a separate final missing-string audit only if the user asks. Do not touch the in-game Addon Overlay/QAM or real ClawHUD. CTW is retired; no integration work.

No new localization framework, culture selector, alternate SettingsItem owner, runtime abstraction, generic resource wrappers, controller authority, or speculative race protection. Keep existing `MainUiStrings.Get` and `MainUiStrings.Format`.

## 1. Mandatory source and architecture review

Follow the current Full1902 architecture authority:

- `docs/Full 1902 Implementation/README.md` (document precedence).
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`.
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`.
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`.
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`.
- `docs/work-order/2026-10-14_MAIN_UI_RESW_LOCALIZATION_PR1_DEPLOYMENT_SAFE_FOUNDATION_WORK_ORDER.md`.
- `docs/work-order/2026-10-14_MAIN_APP_XBOX_TAB_RESW_PR5_WORK_ORDER.md`.
- `docs/work-order/2026-10-14_MAIN_APP_SHORTCUT_AND_HOW_TO_USE_RESW_PR7_WORK_ORDER.md`.
- `docs/work-order/2026-10-14_MAIN_APP_TWEAKS_TAB_RESW_PR8_WORK_ORDER.md`.

Read latest code on `main`:

```text
src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
src/SteamInputAddonforClaw.UI/MainNavigationState.cs
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs
src/SteamInputAddonforClaw.UI/MainUiStrings.cs
src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw
src/SteamInputAddonforClaw.UI/Strings/ko-KR/Resources.resw
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs       (read-only contract reference)
src/SteamInputAddonforClaw.Contracts/Frontend/WindowsTweaksFrontendContracts.cs (read-only if needed)
tests/SteamInputAddonforClaw.UiTests/MainWindowTests.cs
tests/SteamInputAddonforClaw.UiTests/SettingsPageUpdateTests.cs
tests/SteamInputAddonforClaw.UiTests/SettingsPageLoggingTests.cs
tests/SteamInputAddonforClaw.UiTests/SettingsExpanderUiContractTests.cs
tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
scripts/verify-ui-localization-pri.ps1
scripts/publish-layout.ps1
scripts/verify-publish-assets.ps1
```

Read-only for exclusion confirmation:

```text
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml.cs
```

The product is **standalone Full1902**, one Windows user and one interactive session. Local Codex performs code-only validation; physical testing on the real handheld is the user's responsibility and must **not** be a CI/PR merge blocker.

## 2. Verified code facts — different settings surfaces have different ownership

Current `Views/SettingsPage.xaml` has approximately **104 lines**; `SettingsPage.xaml.cs` has approximately **449 lines**.

Visible Settings general UI sections, in current XAML order:

1. Page heading `Settings`.
2. `UpdateCard`, `Header="Application updates"`; `UpdateButton Content="Check"`, dynamically becomes `Install update`.
3. `AppThemeCard`, `Header="App theme"` and `Description="Choose the main app appearance."`; choice labels `System`, `Light`, `Dark` with stable enum-compatible string `Tag`.
4. `GamingHomeExpander`, `Header="Windows Gaming Full Screen Experience"`, original Description, and four choices `Off` (Tag=None), `Xbox` (Tag=Xbox), `Steam Big Picture` (Tag=SteamBigPicture), `Other Windows app` (Tag=Other, read-only current state). Nested `GamingHomeStartupCard`, `Header="Start on Windows startup"`, original Description.
5. `QuickSettingsPowerSourceCard`, `Header="Show only current power source"`, original Description and ToggleSwitch.
6. Logging `SettingsCard`, `Header="Logging"`, `Description="Choose log detail."`; `Off / Info / Debug` selection **excluded**; `Open folder` button **included**.
7. `RequiredComponentsExpander`, `Header="Required Components"` and dynamic read-only component cards.
8. `DeveloperMenuCard` — **excluded in full**; keep this original card as the terminal element.

**Settings navigation is special:** `MainWindow.xaml` uses the **built-in** `MainNavigationView.SettingsItem`, NOT a custom `NavigationViewItem Content="Settings" Tag="Settings"` in `NavigationView.MenuItems`. `MainWindow.xaml.cs` currently calls `SetEnglishSettingsItemContent()` inside `MainNavigationView_Loaded` before measuring pane width, and that method forces:

```csharp
if (MainNavigationView.SettingsItem is NavigationViewItem settingsItem)
    settingsItem.Content = "Settings";
```

Localize **that existing built-in SettingsItem only**; do **not** add a duplicate NavigationViewItem, change the tag routing, remove the built-in update-ready InfoBadge, change menu order or replace built-in settings navigation.

The app's existing `MainUiStrings.Get("Settings_AppTheme_Description", DefaultAppThemeDescription)` is a PR1 foundation resource. **Reuse it unchanged**; do not add an unnecessary parallel resource for the exact same App theme Description sentence.

## 3. Built-in Settings navigation and layout measurement

Replace the misleading `SetEnglishSettingsItemContent` method with a small `SetLocalizedSettingsItemContent` and call it from the **same point** in `MainNavigationView_Loaded`, before `ApplyUpdateReadySettingsBadge()` and before `MeasureDesiredWidth`:

```csharp
private void MainNavigationView_Loaded(object sender, RoutedEventArgs args)
{
    SetLocalizedSettingsItemContent();
    ApplyUpdateReadySettingsBadge();
    // Everything else unchanged, including prerequisites and pane-width measurement.
}

private void SetLocalizedSettingsItemContent()
{
    if (MainNavigationView.SettingsItem is NavigationViewItem settingsItem)
        settingsItem.Content = MainUiStrings.Get("MainNav_Settings", "Settings");
}
```

Use **direct plain resource key** `MainNav_Settings` (no `.Content` suffix, because there is **no x:Uid** on a built-in SettingsItem in `MainWindow.xaml`). The already-shipped C# `MainUiStrings` prepends `Resources/`; `MainNav_Settings` therefore resolves to `/Resources/MainNav_Settings`.

Leave `MainNavigationView.SettingsItem` itself and `settingsItem.InfoBadge` unchanged. Do not touch `MainNavigationView.SettingsItem` icon/style, `MainNavigationPage.Settings` navigation, `MainNavigationView.MenuItems` ordering or startup pane logic.

The Settings item becomes language dependent without triggering a new layout/re-measure system. The existing pane measurement already occurs **after** this label assignment.

## 4. Fixed Settings XAML: x:Uid and both RESW candidates

Use **existing, unique XAML x:Uid** on each in-scope static card/control, with the current English attribute still present for fallback and source/structural test compatibility. Example:

```xml
<TextBlock x:Uid="Settings_PageTitle" FontSize="24" FontWeight="SemiBold" Text="Settings" />

<ctcontrols:SettingsCard x:Name="UpdateCard"
                         x:Uid="Settings_UpdateCard"
                         Header="Application updates">
    ...
    <Button x:Name="UpdateButton"
            x:Uid="Settings_UpdateButton"
            Content="Check"
            Click="UpdateButton_Click" />
</ctcontrols:SettingsCard>
```

Suggested `Resources.resw` keys and initial values (names may be adjusted consistently, but preserve stability and use both languages):

| Resource | Initial English value |
| --- | --- |
| `MainNav_Settings` | Settings |
| `Settings_PageTitle.Text` | Settings |
| `Settings_UpdateCard.Header` | Application updates |
| `Settings_UpdateButton.Content` | Check |
| `Settings_AppThemeCard.Header` | App theme |
| `Settings_AppTheme_System.Content` | System |
| `Settings_AppTheme_Light.Content` | Light |
| `Settings_AppTheme_Dark.Content` | Dark |
| `Settings_GamingHomeExpander.Header` | Windows Gaming Full Screen Experience |
| `Settings_GamingHomeExpander.Description` | Choose the home app used by Windows gaming full screen experience. |
| `Settings_GamingHome_Off.Content` | Off |
| `Settings_GamingHome_Xbox.Content` | Xbox |
| `Settings_GamingHome_SteamBigPicture.Content` | Steam Big Picture |
| `Settings_GamingHome_Other.Content` | Other Windows app |
| `Settings_GamingHomeStartupCard.Header` | Start on Windows startup |
| `Settings_GamingHomeStartupCard.Description` | Start Windows directly in the selected gaming home. |
| `Settings_QuickSettingsPowerSourceCard.Header` | Show only current power source |
| `Settings_QuickSettingsPowerSourceCard.Description` | Show only the controls for the currently connected power source in Quick Settings Overlay. |
| `Settings_LoggingCard.Header` | Logging |
| `Settings_LoggingCard.Description` | Choose log detail. |
| `Settings_OpenLogFolderButton.Content` | Open folder |
| `Settings_RequiredComponentsExpander.Header` | Required Components |

**Previously existing:** `Settings_AppTheme_Description` = `Choose the main app appearance.` (maintain same resource name and C# call, including original `DefaultAppThemeDescription` fallback). For `AppThemeCard`, x:Uid need only localize its `Header`, because the existing `SetAppThemeSelection` assigns the already RESW-backed Description during `SettingsPage.Initialize`; retain the original English XAML Description fallback.

**Developer exclusions (hard):**

```xml
<ctcontrols:SettingsCard
    x:Name="DeveloperMenuCard"
    Click="DeveloperMenuButton_Click"
    Description="Developer-only diagnostics. General users should not use this menu."
    Header="Developer Menu"
    IsClickEnabled="True">
```

Keep the DeveloperMenuCard exactly in its original layout/markup, **without x:Uid**, RESW keys or translation logic. Do not include it in any tests that assert every Settings card is localized; exclude by `x:Name`. Do not change Developer Menu child pages or developer-entry confirmation dialog in MainWindow code.

**Logging selection exclusions (hard):**

```xml
<ComboBox x:Name="LogLevelComboBox" ...>
    <ComboBoxItem Content="Off" />
    <ComboBoxItem Content="Info" />
    <ComboBoxItem Content="Debug" />
</ComboBox>
```

Keep all three unchanged, without x:Uid. Do not add RESW keys `Settings_Logging_Off/Info/Debug`. Keep `LogLevelComboBox_SelectionChanged` switching on `item.Content` exactly as today. **Do not refactor the selection to Tag or index** only for this localization PR. These are deliberately untranslated log levels.

### Dynamic card descriptions and property keys

For `GamingHomeExpander` and `GamingHomeStartupCard`, define both `.Header` and `.Description` property resources via their UIDs because their XAML default descriptions are in scope.

The code also **replaces these two Description values at runtime**, so direct C# retrieval of the XAML property resources must use the **slash-separated PRI property URI**:

```csharp
MainUiStrings.Get(
    "Settings_GamingHomeExpander/Description",
    "Choose the home app used by Windows gaming full screen experience.");

MainUiStrings.Get(
    "Settings_GamingHomeStartupCard/Description",
    "Start Windows directly in the selected gaming home.");
```

That maps to `/Resources/Settings_GamingHomeExpander/Description` and `/Resources/Settings_GamingHomeStartupCard/Description` in the generated PRI, while the source RESW keys remain `Settings_GamingHomeExpander.Description` and `Settings_GamingHomeStartupCard.Description`.

**Do not** call `Get("Settings_GamingHomeExpander.Description", ...)` from C#: this dot path is a different PRI resource URI. Keep the original English XAML attributes and do not override dynamic Runtime failures by setting an unconditional static description.

## 5. C# dynamic Settings messages — localize only UI-authored text

The following strings are **created by `SettingsPage.xaml.cs`**, not by the backend. Assign RESW and use `MainUiStrings.Get`, preserving prior branch predicates and source-origin message priority.

### 5.1 Update card

`RenderAppUpdate(FrontendUpdateSnapshot snapshot)` currently does:

```csharp
_updateSnapshot = snapshot;
UpdateCard.Description = snapshot.Message;
UpdateButton.Content = snapshot.CanInstall ? "Install update" : "Check";
UpdateButton.IsEnabled = snapshot.CanCheck || snapshot.CanInstall;
AppUpdateSnapshotRendered?.Invoke(snapshot);
```

Keep `UpdateCard.Description = snapshot.Message` **exactly** — this is producer-owned update state/error text. Keep the order of those statements and `AppUpdateSnapshotRendered` publication; existing update-badge tests depend on it.

Change only the button labels:

```csharp
UpdateButton.Content = snapshot.CanInstall
    ? MainUiStrings.Get("Settings_Update_InstallAction", "Install update")
    : MainUiStrings.Get("Settings_UpdateButton/Content", "Check");
```

The `Check` value comes from the same existing XAML `Settings_UpdateButton.Content` property resource, referenced by its **slash** URI in C#. No new duplicate `Check` key needed. The selection still depends **only on `snapshot.CanInstall`**, not a localized string.

In `UpdateButton_Click` the locally constructed status messages must be resourced:

```csharp
RenderAppUpdate(new(
    FrontendUpdateState.Checking,
    MainUiStrings.Get("Settings_Update_Checking", "Checking for updates…")));

RenderAppUpdate(new(
    FrontendUpdateState.Failed,
    MainUiStrings.Get("Settings_Update_OperationFailed", "The update operation failed. Try again.")));
```

Leave `result.FailureMessage` (if any), received `FrontendUpdateSnapshot.Message`, update check/download/install, `_updateOperationInProgress` guard, `finally` refresh and update-ready badge unchanged. Do not change `FormatWindowTitle` or its global `Update Available` suffix in this PR.

### 5.2 App theme

`SetAppThemeSelection` must continue to choose enum `MainUiTheme` using **existing stable `ComboBoxItem.Tag`** values `System` / `Light` / `Dark`, not localized Content.

Keep `DefaultAppThemeDescription` and:

```csharp
private static string GetDefaultAppThemeDescription()
    => MainUiStrings.Get("Settings_AppTheme_Description", DefaultAppThemeDescription);
```

unchanged in purpose and resource key. A caller-provided `failureDescription` takes precedence exactly as before; do not reinterpret or translate it. Preserve the existing `_isApplyingAppThemeSelection` guard and `AppThemeSelectionRequested` callback semantics.

### 5.3 Windows Gaming Full Screen Experience (Gaming Home)

`RenderGamingHome` assigns the UI-visible expander/startup descriptions dynamically. Localize the **UI-owned whole strings**:

| Source context | Key and exact English fallback |
| --- | --- |
| Existing initial field snapshot (`FrontendGamingHomeSnapshot.Unavailable` constructed in UI) | `Settings_GamingHome_InitiallyUnavailable` = Windows Gaming Full Screen Experience is unavailable. |
| Local capture/mutation recovery fallback snapshot | `Settings_GamingHome_VerifyFailed` = Windows Gaming Full Screen Experience could not be verified. |
| Selection is `Other` | `Settings_GamingHome_OtherDescription` = The current Windows app is outside Addon control. Choose Off, Xbox, or Steam Big Picture to replace it. |
| Available + normal selection | **Reuse** `Settings_GamingHomeExpander/Description` property resource |
| Unavailable and no producer reason | `Settings_GamingHome_UnavailableDescription` = The current Windows setting could not be verified. |
| Startup card when `Selection == Other` | `Settings_GamingHome_OtherStartupDescription` = The current value is shown but startup behavior for this app is not managed by the Addon. |
| Startup card normal | **Reuse** `Settings_GamingHomeStartupCard/Description` property resource |
| Failed selection mutation local fallback | `Settings_GamingHome_SelectionChangeFailed` = The Gaming Home selection could not be changed. |
| Failed startup mutation local fallback | `Settings_GamingHome_StartupChangeFailed` = The Gaming Home startup setting could not be changed. |

Keep the exact original nested conditional logic and `snapshot.UnavailableReason ?? localFallback`:

```csharp
GamingHomeExpander.Description = snapshot.Available
    ? snapshot.Selection == FrontendGamingHomeSelection.Other
        ? MainUiStrings.Get("Settings_GamingHome_OtherDescription",
            "The current Windows app is outside Addon control. Choose Off, Xbox, or Steam Big Picture to replace it.")
        : MainUiStrings.Get("Settings_GamingHomeExpander/Description",
            "Choose the home app used by Windows gaming full screen experience.")
    : snapshot.UnavailableReason
        ?? MainUiStrings.Get("Settings_GamingHome_UnavailableDescription",
            "The current Windows setting could not be verified.");
```

The two local fallback arguments passed to `RunGamingHomeMutationAsync` should be localized **at the existing call sites**; `result.FailureMessage ?? fallbackFailureMessage` must retain its original producer-first semantics.

**Do not** change:

- `FrontendGamingHomeSelection` enum parsing from the **typed Tag** values (`None`, `Xbox`, `SteamBigPicture`, `Other`).
- `IsStartupWritable`, `Selection == Other` read-only behavior, startup gating or checked state.
- `Interlocked.Exchange` mutation gate, `_applyingGamingHomeState`, rollback via `CaptureGamingHomeAsync`, current-state refresh, or no extra restart/prompt.
- `FrontendGamingHomeSnapshot.UnavailableReason` or any Runtime-provided string, even if currently English.

### 5.4 Quick Settings preference

The `QuickSettingsPowerSourceCard` is fixed XAML and requires its Header and Description x:Uid keys (`4`), **but there are no locally constructed visible success/failure sentences in the current toggle handler**. Its failure path logs and attempts authoritative rollback. Keep this intact: no new toast, no changes to readback or preference behavior.

### 5.5 Logging card

Localize the card's `Header`, `Description` and `Open folder` button with `x:Uid`, preserving all English XAML fallback attributes.

**Exclude Off / Info / Debug choices and all logging operation code**. No changes to `SetLogLevel`, `GetLogLevelSelectionIndex`, `LogLevelComboBox_SelectionChanged`, bootstrap/readback/rollback, or log folder launch. Technical log categories/levels remain English.

### 5.6 Required Components (read-only diagnostic list, general Settings UI)

`RequiredComponentsExpander.Header` is translated via `x:Uid` as normal UI.

`RenderRequiredComponents(FrontendStatusSnapshot snapshot)` builds exactly three read-only rows in order `HidHide`, `usbip-win2`, `VIIPER`. Keep their identifiers as-is, and keep `Status = snapshot.Prerequisites...Status.ToString()` and `Reason` producer data raw.

Only the locally formatted aggregate summary:

```csharp
Text = $"{readyCount} of {components.Length} ready",
```

must be localized with a **complete parameterized template**, e.g.:

```csharp
Text = MainUiStrings.Format(
    "Settings_RequiredComponents_ReadyCount",
    "{0} of {1} ready",
    readyCount,
    components.Length),
```

Do **not** translate or textual-match `Ready` enum values for display: `readyCount` intentionally counts authoritative status codes with `StringComparison.OrdinalIgnoreCase` as today. Do not change `SettingsCard` row ownership, `Header=name`, `Description=reason`, `Text=status`, order, icons, no repair/install actions, or any HidHide/usbip/VIIPER state/teardown code. Preserve `RequiredComponentsExpander.Items.Clear()` and rebuild semantics.

## 6. Explicit Developer Menu exclusion (strong regression boundary)

**None of the following may receive a new RESW key, translated label or production code edit:**

- `SettingsPage.xaml`'s `DeveloperMenuCard`, including `Header="Developer Menu"` and `Description="Developer-only diagnostics. General users should not use this menu."`.
- `DeveloperMenuCard.Visibility = GetDeveloperMenuCardVisibility(bootstrap.Settings.DeveloperMenuEnabled)`.
- `DeveloperMenuRequested` event, `DeveloperMenuButton_Click` and Developer menu navigation.
- `MainWindow.OnDeveloperMenuRequested`, `ContentDialog` title `Developer Menu`, long diagnostic warning, `"Don't show this warning again"` checkbox, `Continue`, `Cancel`, suppress-persistent-warning logic.
- `DeveloperPage.xaml/.xaml.cs` and all children: `ClawSensorProbePage`, `FanHardwareProbePage`, `CenterMButton` page and other diagnostic descendants.
- Any developer-only modal, diagnostic status or log folder controls, probes or extra XAML elements.

The fact that `DeveloperMenuCard` lives inside `SettingsPage.xaml` is **not** permission to localize it. For tests that inventory "all Settings UIDs" or "all Settings cards", explicitly exempt the one original `DeveloperMenuCard` and its downstream pages.

**Do not remove or disable the Developer Menu**. Only leave its English text and all behavior unchanged. It must retain its original hidden/visible bootstrap policy.

## 7. Source/PRI testing, including known brittle contracts

### 7.1 Necessary tests

In `tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs` add focused coverage for the new `Settings_*` key surface:

1. `MainNav_Settings` (plain C# key; not `MainNav_Settings.Content`) exists and is used by the built-in `MainNavigationView.SettingsItem` setter **before** pane measurement and alongside unchanged update badge.
2. Each in-scope XAML `x:Uid` is unique and has the correct dotted `.Text`/`.Header`/`.Description`/`.Content` key in `en-US` and `ko-KR`, including AppTheme/GamingHome options and logging **card+button only**. Existing XAML attribute fallback text remains English.
3. `Settings_AppTheme_Description` continues to work via `GetDefaultAppThemeDescription`, and `failureDescription ?? default` ordering remains intact. No duplicate resource for its same static text is needed.
4. Runtime-overwritten `GamingHomeExpander/Description` and `GamingHomeStartupCard/Description` are read via **slash** C# PRI paths, while the source property keys remain dotted in RESW.
5. `UpdateButton.Content` uses the correct `CanInstall` decision, localized `Install update` and existing `Check` UID content path. `UpdateCard.Description = snapshot.Message` and publication order remain unchanged.
6. The two locally synthetic update messages, the two Gaming Home mutation fallbacks, both local unavailable snapshot messages and two `Selection == Other` descriptions have resources; **producer`UnavailableReason` / `FailureMessage` remains verbatim**.
7. Required Components aggregate `{0} of {1} ready` formats correctly, with correct placeholders and without changing the readyCount typed-status calculation or component IDs/status/reasons.
8. **No UID** on `DeveloperMenuCard`, **no RESW keys** for developer card, developer warning or descendant screens, and **no UID/RESW** for Logging `Off/Info/Debug` options.
9. Existing logging handler still maps English `ComboBoxItem.Content` to Off/Info/Debug exactly and bootstrap rollback remains intact; no requirement to translate those options.
10. Both languages have exactly the same **key sets** with nonempty candidates. Initial new values equal original English, but **do not** write tests enforcing permanent en-US/ko-KR **value equality**, nor freeze global resource count.
11. **Published `SteamInputAddonforClaw.UI.pri`** contains exactly one nonempty `en-US` and `ko-KR` candidate for all new keys (including `MainNav_Settings` and slash-resolved property keys).

### 7.2 Existing tests must remain meaningful

Review and preserve these known contracts, modifying only brittle source-string assumptions when needed:

**`tests/SteamInputAddonforClaw.UiTests/MainWindowTests.cs`**

The `Update_indicators_use_the_existing_settings_snapshot_and_built_in_navigation_item` test explicitly locates method markers **`"private void SetEnglishSettingsItemContent"`** to slice `MainNavigationView_Loaded` and `ApplyUpdateReadySettingsBadge`. After renaming to `SetLocalizedSettingsItemContent`, update **those marker strings only** and add an assertion for the `MainUiStrings.Get("MainNav_Settings", "Settings")` call. Keep the existing checks for `ApplyUpdateReadySettingsBadge`, built-in SettingsItem, no manually added Settings nav item and no unwanted extra update capture.

**`tests/SteamInputAddonforClaw.UiTests/SettingsPageUpdateTests.cs`**

- `Rendered_update_snapshot_is_published_after_the_existing_card_is_updated` depends on the exact source ordering of `UpdateCard.Description = snapshot.Message`, `UpdateButton.Content = snapshot.CanInstall`, `UpdateButton.IsEnabled = snapshot.CanCheck || snapshot.CanInstall` and `AppUpdateSnapshotRendered?.Invoke(snapshot)`; retain this ordering, even after inserting `MainUiStrings.Get` inside the Content assignment.
- `Gaming_home_expander_uses_separate_guarded_selection_and_startup_mutations` verifies ComboBox typed Tags, popup avoidance, no new `Process.Start`/`Restart` logic and current busy/rollback. Don't accidentally add new SettingsPage local methods with words that upset overly broad source assertions; keep original method shapes.
- The enum selector and startup toggle remain functional and cannot use translated Content for enum parsing.

**`tests/SteamInputAddonforClaw.UiTests/SettingsPageLoggingTests.cs`**

Checks `Header="Logging"`, `Description="Choose log detail."`, the exact `Off/Info/Debug` option order, the `Open folder` button, original card ordering and `GetLogLevelSelectionIndex`. These should pass with English XAML fallback literals still present. **Do not change the logging-choice tests to accept translated labels**; they are explicitly excluded by the user. Existing `Developer_menu_keeps_folder_access_and_diagnostics_but_no_longer_owns_log_level` remains unchanged.

**`tests/SteamInputAddonforClaw.UiTests/SettingsExpanderUiContractTests.cs`**

Expanded/closed state and header icons, absence of child icons, dynamic Required Components card structure. All stay unchanged.

**`tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs`**

- `Settings_page_shows_read_only_required_components_and_no_launch_at_startup_toggle` and `Settings_header_icons_are_unique` must pass.
- `Settings_developer_menu_card_visibility_follows_bootstrap_flag` must pass.
- Developer lazy-page materialization and navigation tests must remain intact.
- Do not remove existing developer card or its diagnostic links merely to make a localization assertion simpler.

Any other directly affected UI tests should be adjusted **minimally**, preserving the originally protected behavior.

### 7.3 Published PRI, not just source RESW

Reuse, unchanged, the existing:

- `scripts/publish-layout.ps1`
- `scripts/verify-ui-localization-pri.ps1`
- `scripts/verify-publish-assets.ps1`

It maps dotted XAML keys (`Settings_UpdateCard.Header`) to URI `/Resources/Settings_UpdateCard/Header` and plain C# keys (`MainNav_Settings`, `Settings_Update_InstallAction`) to `/Resources/<key>`. Confirm the generated unpackaged Main UI PRI is shipped to the correct `ui/` output and candidates are nonempty for both languages.

No extra PRI verifier/synchronization layer and no MSIX migration.

## 8. Expected diff, CI and merge-readiness acceptance

**Expected files:**

```text
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs
src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw
src/SteamInputAddonforClaw.UI/Strings/ko-KR/Resources.resw
tests/SteamInputAddonforClaw.UiTests/MainWindowTests.cs
tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs
other directly impacted UI tests (only if necessary)
```

**Files intentionally unchanged:**

```text
src/SteamInputAddonforClaw.UI/MainUiStrings.cs
src/SteamInputAddonforClaw.UI/MainWindow.xaml          (built-in SettingsItem already exists)
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.*
src/SteamInputAddonforClaw.UI/Views/ClawSensorProbePage.*
src/SteamInputAddonforClaw.UI/Views/FanHardwareProbePage.*
src/SteamInputAddonforClaw.Overlay/**
src/SteamInputAddonforClaw.Contracts/**
src/SteamInputAddonforClaw/WindowsTweaks/**
src/SteamInputAddonforClaw.FrontendTransport/**
scripts/verify-ui-localization-pri.ps1
scripts/publish-layout.ps1
```

Run code/CI validations:

1. `dotnet restore SteamInputAddonforClaw.slnx` plus UI test project restore.
2. Release build of the solution and UI tests.
3. Core/UI test suites, especially Main Ui localization, built-in settings nav/update-badge, SettingsPageUpdate, SettingsPageLogging, SettingsExpander and UiArchitecture contracts.
4. All existing CI PowerShell verification scripts as feasible; `git diff --check`.
5. `scripts/publish-layout.ps1` and existing published-PRI validator: en-US default with both nonempty candidates for every added key.
6. `scripts/verify-publish-assets.ps1`; existing startup smoke test in CI where applicable. Do not perform physical MSI Claw validation as a Codex requirement.

**Definition of done:**

- General Settings UI strings, dynamic UI-authored messages and built-in Settings nav are RESW-backed.
- `Settings_AppTheme_Description` is reused and existing failure precedence preserved.
- Off/Info/Debug logging **choices and handler** remain **English, unchanged, and unlocalized**.
- Developer Menu **card, warning, submenus, pages and tools remain English, unchanged, and unlocalized**.
- Update, Gaming Home, Quick Settings preferences, Required Components, badge and navigation behavior remain identical and Runtime messages remain raw.
- Both initial locale values are current English, and later Korean translation needs only `ko-KR` RESW edits.
- Release build, automated regressions and published PRI verification pass; **physical device testing is not a PR/merge blocker**.

## 9. Local Codex delivery

Create **one PR9** with summary of:

- General Settings cards and built-in SettingsItem caption localized;
- logging level option exclusion and intact raw string-based mapping;
- Developer Menu card/dialog/all descendants exclusion;
- publisher-owned update/Gaming Home failure text untouched, C#-generated UI fallbacks resourced;
- Required Components formatted summary and technical names unchanged;
- test/CI and published PRI validation results;
- no Runtime/contract/Overlay changes and no physical hardware test requirement.

No user-facing translations other than initial English string extraction belong to this PR.
