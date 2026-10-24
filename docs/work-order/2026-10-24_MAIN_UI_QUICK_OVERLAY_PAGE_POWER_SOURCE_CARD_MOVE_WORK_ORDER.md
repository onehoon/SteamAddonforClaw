# Work Order — Main App Quick Overlay Page Foundation and Power-Source Option Migration

**Date:** 2026-10-24  
**Status:** Ready for local Codex implementation  
**Repository:** `onehoon/SteamAddonforClaw`  
**Target:** `main`; re-check the latest branch before editing  
**Code baseline reviewed:** `72a1a8f1af843844bbff0d63a4c0a2ea7b09d2dd` (PR #870 merged)  
**Delivery:** One focused, standalone implementation PR  
**Product:** Standalone Full PID1902 Steam Addon for Claw. No CTW/ClawTweaks/HHC integration.  
**Responsibility:** Local Codex implements and executes software tests. The user owns physical handheld/visual validation after merge, never as a merge gate.

## 0. Frozen scope and expected result

Create a new **top-level Main App sidebar page named "Quick Overlay"** and move **one existing Main App option card**, `Show only current power source`, from the Main App **Settings** page to the new page.

The feature is already implemented; this is a **Main App UI ownership/location change**, not a new power-source feature. Preserve its current Runtime-owned setting and existing game Overlay Setting-page editor.

**Navigation order:**

1. Device
2. MSI Update
3. Controller
4. Steam
5. XBOX
6. ClawHUD
7. **Quick Overlay** (new)
8. Shortcut
9. Tweaks
10. How to Use
11. Settings (existing NavigationView SettingsItem/footer; do not convert it to a normal sidebar MenuItem)

**New Quick Overlay page, initial contents:** a normal page title, optionally one concise introductory subtitle using existing Main App conventions, and **only one live SettingsCard** containing the relocated power-source toggle. Do not pre-create empty future-function cards.

**Power-source card icon is a required part of this PR.** Replace the old generic `SymbolIcon Symbol="View"` with the platform-native **Segoe Fluent Icons BatteryCharging4 glyph, U+E85E**:

    <ctcontrols:SettingsCard.HeaderIcon>
        <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE85E;" />
    </ctcontrols:SettingsCard.HeaderIcon>

It is a **static, category-level power/AC-DC icon**, not a live charger/charging status indicator. Do not bind glyph changes to current AC/DC state. No custom SVG, PNG, icon library, or assets.

For the **new top-level Quick Overlay navigation item**, use a distinct, platform-native **OpenPane glyph U+E8A0** (a quick side-panel metaphor):

    <NavigationViewItem x:Uid="MainNav_QuickOverlay" Content="Quick Overlay" Tag="QuickOverlay">
        <NavigationViewItem.Icon>
            <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE8A0;" />
        </NavigationViewItem.Icon>
    </NavigationViewItem>

Microsoft's official glyph catalog identifies `E85E = BatteryCharging4` and `E8A0 = OpenPane`:
https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-fluent-icons-font

## 1. Read authoritative documents and latest source first

Read these project documents before implementation:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md` (Main UI remains a frontend; Runtime remains controller/settings authority)
- `docs/overlayui/README.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md` (use current override notes in `docs/overlayui/README.md` for stale planning-era sections)
- `docs/work-order/ADDON_QUICK_SETTINGS_CURRENT_POWER_SOURCE_VISIBILITY_WORK_ORDER.md` (existing option's semantics)
- `docs/work-order/2026-10-14_MAIN_APP_SETTINGS_GENERAL_UI_RESW_PR9_WORK_ORDER.md` (current Settings resource conventions)
- `docs/work-order/2026-10-14_MAIN_APP_CLAWHUD_TAB_RESW_PR6_WORK_ORDER.md` (the old `Overlay` Main App identifier means **ClawHUD**; preserve it)
- `docs/work-order/2026-10-24_OVERLAY_ALL_SETTINGS_MUTATION_PREVIEW_AND_SELECTION_STABILITY_WORK_ORDER.md` (do not regress recently landed Overlay setting mutation presentation)

Read/reconcile the **current** implementations:

- `src/SteamInputAddonforClaw.UI/MainWindow.xaml`
- `src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs`
- `src/SteamInputAddonforClaw.UI/MainNavigationState.cs`
- `src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml` and `.xaml.cs`
- `src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml` and `.xaml.cs` (read only; it is the **ClawHUD Main App page**, not this new page)
- `src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw`
- `src/SteamInputAddonforClaw.UI/Strings/ko-KR/Resources.resw`
- `src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs`
- `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs` (read only; current in-game `Setting → Quick Settings` card stays)

Tests to review up front:

- `tests/SteamInputAddonforClaw.UiTests/MainNavigationStateTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/MainWindowTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/SettingsPageUpdateTests.cs`

## 2. Verified current code facts

1. Main App `SettingsPage.xaml` currently contains exactly the target `QuickSettingsPowerSourceCard` between the Gaming Home and Logging groups. It uses `SymbolIcon Symbol="View"`.
2. `SettingsPage.xaml.cs` initializes the toggle from `bootstrap.Settings.QuickSettingsCurrentPowerSourceOnly`, guards programmatic `Toggled` updates, mutates through `_frontend.SetQuickSettingsCurrentPowerSourceOnlyAsync(bool)`, and on failure calls `_frontend.GetBootstrapAsync()` to attempt authoritative rollback, falling back to its last known value if refresh fails. The associated `ResolveQuickSettingsPowerSourcePreference` helper is tested directly from `SettingsPage`.
3. The Runtime already persists exactly one `QuickSettingsCurrentPowerSourceOnly` setting, projects it via `FrontendSettingsSnapshot`, and implements the existing typed frontend mutation. **There is no need to add another settings key, owner, DTO, RPC, persistence mechanism, or service.**
4. The in-game Overlay's `Setting → Quick Settings` section already exposes the same option via `OverlayWindow.ClawHud.cs`. That control and its existing mutation/rollback path are **not** the card being removed.
5. `MainWindow.xaml` currently has `ClawHUD` with `Tag="Overlay"`, followed by `Shortcut`. `MainNavigationState` maps that tag to `MainNavigationPage.Overlay`. The existing `OverlayPageHost`, `GetOrCreateOverlayPage()`, and `_overlayContent` belong exclusively to ClawHUD. **Do not rename or repurpose any of these.**
6. `MainWindow.xaml.cs` uses per-page lazy factories (`GetOrCreate...Page`), `TryMaterializeMainPage`, `ShowPage` host visibility, `NavigationTagFor`, and `RestoreNavigationSelection`. Existing `DeferredWarmupPages` initializes several pages later after first rendering; the new page needs only **on-demand lazy creation** and should **not** join startup or deferred warm-up.
7. The Main UI project uses WinUI 3 + CommunityToolkit SettingsControls and SDK-default XAML compilation, with existing `Views/*.xaml` conventions. Do not create another UI project or source generator rule.
8. Localization uses `x:Uid` plus matching English/Korean `Resources.resw` keys. The current Settings card has `Settings_QuickSettingsPowerSourceCard.Header/Description`. Several tests have an explicit list of Settings-page UIDs/resources, so moving the card requires updating those tests.
9. `SettingsPageUpdateTests` explicitly calls `SettingsPage.ResolveQuickSettingsPowerSourcePreference` and asserts the card is present in Settings. These assertions must move to the new page/contract instead of being deleted.

## 3. Main App navigation and page shell

### 3.1 Separate page identity

In `MainNavigationState.cs` introduce `MainNavigationPage.QuickOverlay` and an exact navigation tag mapping:

    "QuickOverlay" => MainNavigationPage.QuickOverlay

Keep `MainNavigationPage.Overlay` untouched as the ClawHUD identity. Do not reuse it for the new page. Place the new navigation item in `MainWindow.xaml` **between ClawHUD and Shortcut**, with the OpenPane icon above, English/Korean `x:Uid` localization, and an independent hidden host:

    <ContentControl x:Name="QuickOverlayPageHost"
                    HorizontalContentAlignment="Stretch"
                    VerticalContentAlignment="Stretch"
                    Visibility="Collapsed" />

Wire the new page through the already-established mechanisms in `MainWindow.xaml.cs`:

- A single cached nullable `QuickOverlayPage` reference.
- A simple `GetOrCreateQuickOverlayPage()` factory using the existing `_frontend` and `_bootstrap`.
- `TryMaterializeMainPage` and `IsMainPageMaterialized`.
- `ShowPage` visibility selection and an on-selection `Activate()` refresh if needed.
- `NavigationTagFor` so failed page materialization can restore the prior selected sidebar item.
- Existing `MainNavigationView_SelectionChanged` routing via `MainNavigationState`; do not create a second navigation handler or route.
- Dispose/unsubscribe only page-owned callbacks actually introduced by the implementation; do not add generalized lifecycle infrastructure.

**Performance constraint:** Do not instantiate, query Runtime for, or initialize the new page in the MainWindow constructor, at app startup, or from `DeferredWarmupPages`. Only make it live when the user visits Quick Overlay. One extra static `NavigationViewItem` is acceptable.

### 3.2 New view layout

Add `src/SteamInputAddonforClaw.UI/Views/QuickOverlayPage.xaml` and companion `.xaml.cs`, following existing page naming, `ScrollViewer Padding="32"` and `StackPanel Spacing="16"` / `SettingsCard` visual language.

**Exactly one live option card for this PR**:

- Page title: `Quick Overlay`.
- Optional succinct description: `Configure the in-game Quick Overlay.`
- Card header: `Show only current power source`.
- Card description: `Show only the controls for the currently connected power source in Quick Settings Overlay.` (existing English copy; preserve its functional meaning and Korean translation).
- A native `ToggleSwitch` with blank `OffContent` / `OnContent`, matching the previous card.
- **BatteryCharging4** native `FontIcon` U+E85E in `SettingsCard.HeaderIcon`.
- The card is **not** a live AC/DC status indicator and does **not** modify power policy, TDP, battery charge limit, or any hardware state; it controls which AC/DC settings are shown in compact Quick Settings.

No reserved-placeholder cards, new expander stack, custom control template, per-option help popup, additional toolbar, or unrelated visual polish.

## 4. Move the one existing option without changing the contract

### 4.1 Main App Settings cleanup

Remove from `SettingsPage.xaml` only the `QuickSettingsPowerSourceCard`.

Remove from `SettingsPage.xaml.cs` only its feature-local:

- `_applyingQuickSettingsPowerSourcePreference` and `_lastKnownQuickSettingsCurrentPowerSourceOnly`,
- bootstrap initialization of the option,
- programmatic toggle setter,
- `QuickSettingsPowerSourceToggleSwitch_Toggled`,
- `ResolveQuickSettingsPowerSourcePreference` helper, **moving it to the new page if retaining the same tiny fallback seam**.

Preserve existing Settings update/theme/Gaming Home/logging/prerequisite/developer menu behavior, including related event subscriptions and error handling.

### 4.2 New page's existing Runtime mutation seam

The new page must use the **existing** `IAddonFrontendControl.SetQuickSettingsCurrentPowerSourceOnlyAsync(bool)` and `FrontendSettingsSnapshot.QuickSettingsCurrentPowerSourceOnly` exactly as the old Settings page did.

Required interaction:

1. Initialize with the provided bootstrap value (no new startup fetch).
2. On first visit/revisit, opportunistically refresh the authoritative value through the **existing** `GetBootstrapAsync()` method so a cached page does not display stale data after the option was changed through the in-game Overlay or another supported UI flow.
3. When the user toggles: submit **one existing typed mutation**, await the returned authoritative settings, then display exactly its returned value.
4. On mutation error: log the failure, try the existing bootstrap readback and restore that value; if readback fails, restore the last confirmed value. No unhandled `async void` exceptions.
5. Programmatic `IsOn` assignments must **not** re-trigger a mutation; preserve the existing applying-state guard.
6. Prevent **ordinary double-click/re-entrant edit while one mutation is pending** with one small page-local in-flight guard and, if appropriate, temporary toggle disablement. Keep this local to the new view; do not build a generic command/state machine.
7. A page activation/invalidation refresh must **not overwrite a newer in-flight user choice** with a stale bootstrap snapshot. A tiny existing-style guard is sufficient. Refresh failures are nonfatal: keep the last known selection and allow later retries. No extra polling, retries, epochs, locks, timers, or new save path.
8. Changes persist and take effect through the existing Runtime settings publication. Keep the in-game Overlay's already-implemented option/editor and value-preview handling untouched.

Do not request broad `CaptureStatusAsync` or controller acquisition just to read this preference. No blocking synchronous Runtime calls on the UI thread.

## 5. Localization contract (en-US / ko-KR)

Use the existing resw system; do not hardcode Korean in XAML code-behind and do not add a localization library.

**Suggested new keys/copy** (align x:Uid and names consistently):

| Resource key | en-US | ko-KR |
| --- | --- | --- |
| `MainNav_QuickOverlay.Content` | Quick Overlay | 퀵 오버레이 |
| `QuickOverlay_PageTitle.Text` | Quick Overlay | 퀵 오버레이 |
| `QuickOverlay_PageSubtitle.Text` (only if subtitle exists) | Configure the in-game Quick Overlay. | 게임 중 표시되는 퀵 오버레이의 설정을 변경합니다. |
| `QuickOverlay_PowerSourceCard.Header` | Show only current power source | 현재 전원 방식 옵션만 보기 |
| `QuickOverlay_PowerSourceCard.Description` | Show only the controls for the currently connected power source in Quick Settings Overlay. | 퀵 셋팅 오버레이에 현재 연결된 전원 방식 옵션만 표시합니다. |

Move rather than duplicate the old UI resource ownership: remove unused `Settings_QuickSettingsPowerSourceCard.Header/Description` resources from **both** locales after changing the XAML UID to `QuickOverlay_PowerSourceCard`. Retain the previously approved user-visible translation meaning. Avoid changing unrelated localization copy or key names.

Set the new navigation glyph and card glyph directly in XAML; no icon localization entry is required.

## 6. Tests and regression review (code-only)

Update tests that currently assert the option belongs to Settings; **do not simply remove assertions to obtain a green test run**.

**Main App shell/nav:**
- New `QuickOverlay` navigation tag selects `MainNavigationPage.QuickOverlay`. Existing `Overlay` remains ClawHUD; `Shortcut`, Settings footer, developer submenu and mouse-back destinations remain correct.
- Sidebar order: `ClawHUD` → `Quick Overlay` → `Shortcut`.
- `QuickOverlayPageHost` is distinct from `OverlayPageHost`. Lazy factory, materialization, host visibility, and nav selection restore are wired and do not cause eager startup materialization.
- New page is not inserted into `DeferredWarmupPages`; existing deferred pages are unchanged.
- Verify icon XML: Quick Overlay sidebar glyph `E8A0`; power-source card `E85E`, font `Segoe Fluent Icons`; card is present **only** on the new Main App Quick Overlay page, absent from Main App Settings.

**Settings migration:**
- Update `SettingsPageUpdateTests` to point the fallback test at `QuickOverlayPage` (or equivalently test the migrated behavior); assert the card is absent from Settings and present on Quick Overlay.
- Existing Settings update/theme/Gaming Home/logging/required-components/developer-menu tests still pass.
- Existing Runtime settings value, typed mutation and failure/readback path are reused, with no new setting duplication.
- Code/test checks cover initial bootstrap, user toggle, mutation success, typed/recoverable failure with rollback, and guarding programmatic `Toggled` updates. Use lightweight existing-style seams; do not instantiate an entire fake physical controller or large generic UI testing harness.

**Localization:**
- Update exact expected Settings UIDs and Korean copy maps in `MainUiLocalizationResourceTests`.
- Add tests verifying new Quick Overlay navigation/page/card UIDs, both locale resource values, unique IDs, and no stale renamed Settings card resource keys.
- Preserve the existing ClawHUD page (whose tag is `Overlay`) and existing Overlay Setting-page `Quick Settings` power-source toggle; verify both still use the shared Runtime setting.

**Suggested Codex commands:**

    dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Debug
    dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release
    dotnet build SteamInputAddonforClaw.sln -c Release
    git diff --check

Check the repository's actual solution filename/current CI commands before executing a full-solution build; use current repository build conventions. Run other CI suites impacted by navigation/localization/Frontend contracts if required.

The code/tests/build passing is the implementation/PR criterion. **Do not require the local Codex, CI, or PR reviewer to verify on a physical MSI Claw or visually inspect a live overlay before merge.** User-owned real-device validation occurs after merge.

## 7. Change boundaries / overengineering constraints

Expected files are confined to:
- Main App `MainWindow.xaml/.cs` and `MainNavigationState.cs`;
- new `Views/QuickOverlayPage.xaml/.cs`;
- existing `Views/SettingsPage.xaml/.cs` cleanup;
- both Main App `Resources.resw` files;
- corresponding focused Main UI tests.

Explicitly **do not** change:
- `AppSettings`, `SettingsStore`, `StartupSettingsCoordinator` or Runtime settings authority;
- `IAddonFrontendControl`, `FrontendWire`, named pipes, serialization, protocol version;
- in-game `SteamInputAddonforClaw.Overlay` UI, including its current `Setting → Quick Settings` card;
- existing ClawHUD Main App page or its `MainNavigationPage.Overlay`/`Tag="Overlay"`;
- power-source detection or AC/DC visibility filtering behavior;
- controller routing, PID1901/PID1902, HidHide, VIIPER, XInput/SteamDeck/DualSense attachment, capture/release gates, Sleep/Resume/Restart or cleanup paths;
- Y-button configurable jump, tab-order editor migration, X/LT/RT reserved controls, future Quick Overlay settings.

Do not introduce another preference, owner, coordinator, manager, service, state authority, polling loop, or general-purpose view model to relocate one native WinUI ToggleSwitch. One clear Runtime setting authority + one Main App Quick Overlay editor + the existing in-game Overlay editor is the intended design.

## 8. Final acceptance checklist

- [ ] Main App has an independent **Quick Overlay** sidebar item between ClawHUD and Shortcut, with OpenPane E8A0.
- [ ] New Quick Overlay page exists, is initialized lazily, and initially displays only the **Show only current power source** SettingsCard.
- [ ] Card header icon is **BatteryCharging4, Segoe Fluent Icons U+E85E**, replacing the old generic View icon.
- [ ] Same card and its local methods are removed from **Main App Settings** without affecting other Settings features.
- [ ] Option uses one existing Runtime-persisted `QuickSettingsCurrentPowerSourceOnly` value and existing setter; no new protocol or storage.
- [ ] On visit/revisit the value can refresh; mutation succeeds/readbacks safely; failures roll back without throwing; no duplicate event-triggered writes.
- [ ] The in-game Overlay `Setting → Quick Settings` control remains as-is.
- [ ] ClawHUD's existing `Overlay` navigation identity and behavior remain unchanged.
- [ ] en-US and ko-KR page/menu/card resources and focused tests are updated.
- [ ] Relevant tests/build and `git diff --check` pass. Physical handheld validation is **not** a merge blocker.

**Implementation handoff:** One PR; local Codex implements/tests, user performs post-merge hardware validation.
