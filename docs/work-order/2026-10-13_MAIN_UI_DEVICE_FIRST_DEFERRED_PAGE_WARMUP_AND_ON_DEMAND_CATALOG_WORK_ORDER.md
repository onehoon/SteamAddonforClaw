# Work Order — Main UI Device-First Startup, Deferred Page Warm-up, and On-Demand Steam/XBOX Catalogs

> Date: 2026-10-13  
> Target: `onehoon/SteamAddonforClaw`, current `main` (re-read HEAD before coding)  
> Product: standalone Full1902, single Windows user and interactive session  
> Implementation: local Codex  
> Scope: main WinUI 3 frontend only, plus UI-focused tests  
> Hardware verification: user after merge; **not** an implementation/CI/review blocker

## 1. Goal and locked user decisions

Make the main desktop UI appear promptly on its default **Device** page without constructing all unrelated WinUI page trees before `App.xaml.cs` calls `Activate()`. Once the Device surface is visible and responsive, construct other normal page controls **one at a time on the UI dispatcher** so their first navigation is warm. If the user navigates earlier than the warm-up, create that destination immediately. Page creation and Steam/XBOX installed-game catalog scanning are different concerns.

The required user-visible policy is:

| Surface | Page/control creation | Data or catalog loading | Re-entry |
| --- | --- | --- | --- |
| Device | At initial MainWindow construction | Existing Device/status refresh on first activation; preserve real authority checks | Existing refresh behavior |
| Controller | Deferred sequential warm-up after Device is displayed, or immediate on user navigation | Preserve existing `Activate()`-only behavior for vibration/Steam controller state | Reuse page |
| Steam (Profile) | Deferred sequential warm-up, or immediate on demand | **No catalog scan until first Steam tab entry** | Reuse successful catalog; only explicit Refresh rescans |
| XBOX | Deferred sequential warm-up, or immediate on demand | **No catalog scan until first XBOX tab entry** | Reuse successful catalog; only explicit Refresh rescans |
| ClawHUD, Shortcut, Tweaks, Settings | Deferred sequential warm-up, or immediate on demand | Keep existing page-specific refresh/invalidation behavior; do not scan game catalogs | Reuse page |
| How to Use | **Only when its tab is first entered** | Preserve existing lazy WebView2/document-loading path | Reuse page and loaded document |
| Developer Menu and diagnostics | Existing first-use materialization only | Unchanged | Existing behavior |

**Explicitly do not** preload Steam or XBOX installed-game catalogs after the other pages become ready. The earlier optional background catalog-prefetch idea is rejected. A page may be warmed without invoking its `Activate()` or `Scan*GamesAsync()`.

Do not add automatic catalog invalidation when a game is installed/removed. Manual **Refresh** is authoritative for installed-game listing during the current Main UI process. A new Main UI process has a new first-scan lifetime.

## 2. Architectural authority and non-goals

Read before implementation:

- `docs/Full 1902 Implementation/README.md` and its current precedence chain.
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`.
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`.
- `docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md`.
- `docs/work-order/DEVELOPER_DIAGNOSTIC_LAZY_MATERIALIZATION_WORK_ORDER_2026-10-06.md`.
- `docs/work-order/1008_HOW_TO_USE_WEBVIEW2_LAZY_INITIALIZATION_AND_RENDER_COMPLETION_FIX_WORK_ORDER.md`.
- `docs/work-order/2026-10-12_MAIN_APP_UPDATE_READY_BADGE_AND_TITLE_WORK_ORDER.md`.

Do not change the headless Runtime or its Full1902 PID1902/HidHide/VIIPER controller ownership, Center M policy, device cleanup, physical recovery, sleep/resume, rendering/publisher ownership, or lifecycle. Do not change main-UI/Overlay exclusive visible-surface coordination or make the Main UI a permanently warm background process. No CTW integration.

Do not modify Overlay profile detection, Overlay named-pipe protocol, active game tracking, profiles persistence, installed-game catalog scanner implementations, or Windows event hooks. Do not add a global installed-game catalog cache, a new Runtime service, timer-based polling, package-install watcher, new authority, synchronization state machine, or generalized page framework.

## 3. Verified current source and performance evidence

### 3.1 Cold start

`src/SteamInputAddonforClaw/Lifecycle/SystemTrayIcon.cs` dispatches Tray activation to `RequestFrontendOpen(FrontendOpenReason.Tray)`. `Hosting/AddonProcessHost.cs` coordinates surface ownership and starts the disposable external UI through `Lifecycle/FrontendProcessLauncher.cs`.

`src/SteamInputAddonforClaw.UI/App.xaml.cs::StartFrontendAsync()` waits for these before showing the window:

~~~text
NamedPipeAddonFrontendClient.ConnectAsync()
  -> GetBootstrapAsync()
  -> new MainWindow(frontendClient, bootstrap)
  -> ActivateOrDeferOnUiThread()
~~~

`MainWindow.xaml` eagerly places Device, Profile, Xbox, HowToUse, Controller, Overlay, Shortcut, Tweaks and Settings in the main Grid even when `Visibility="Collapsed"`. `MainWindow.xaml.cs` then initializes all of them before first activation. `SettingsPage.Initialize()` also starts update and Gaming Home snapshot requests at construction. The How to Use WebView2 was previously made lazy, but the containing page still exists eagerly.

From the user-supplied 2026-10-13 logs (`Addon/TouchpadPoc/1013`), two representative already-running Runtime tray opens were ~1.38s and ~1.41s; `MainWindow` initialization took ~0.675s and ~0.693s respectively. One overlapping Runtime startup was ~6.0s total, so it is **not** evidence that page construction alone costs six seconds. These are observations, not guaranteed budgets.

### 3.2 Current page lifecycle and cross-page dependencies

Files:

- `src/SteamInputAddonforClaw.UI/MainWindow.xaml`
- `src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs`
- `src/SteamInputAddonforClaw.UI/MainNavigationState.cs`
- `src/SteamInputAddonforClaw.UI/Views/{DevicePage,ControllerPage,ProfilePage,XboxPage,OverlayPage,ShortcutPage,WindowsTweaksPage,SettingsPage,HowToUsePage}.xaml.cs`

`ShowPage(MainNavigationPage)` currently switches every page's `Visibility` and invokes existing `Activate()/Deactivate()` hooks. The existing `DeveloperContentHost` and `GetOrCreateDeveloperMenu()/GetOrCreateClawSensorProbePage()/GetOrCreateFanHardwareProbePage()` already demonstrate targeted lazy materialization.

Several MainWindow callbacks assume that **all pages exist**, beyond `ShowPage`:

- `RenderSystemStatus` writes to Device, Controller, Steam Profile, **and Settings required-components card**, and starts prerequisite setup when necessary.
- `RefreshInvalidatedFrontendStateOnUiThread` calls Settings update/Gaming Home refresh, ClawHUD refresh, Shortcut refresh, and conditional Tweaks refresh.
- `OnSteamActivityRefreshRequested` updates both Controller and Steam Profile before requesting a new status.
- `OnWindowClosed` unsubscribes page events.
- Window-owned front-button, back-button, and LED mutation handlers call `ControllerContent.Apply*`, and `DrainPendingControllerMappingSavesAsync` must still drain on close.
- `SettingsPage.AppThemeSelectionRequested` / `AppUpdateSnapshotRendered`, `SettingsPage.DeveloperMenuRequested`, and Controller/Profile `SteamActivityRefreshRequested` subscriptions must be made exactly once when the pages are created.

Do not change the ownership or ordering of cross-page controller edits; make page references nullable only where they are genuinely unmaterialized. Preserve a last-known `_latestSystemStatus`, `_bootstrap` and existing window-owned persisted/UI mapping values as the state to seed newly materialized pages. Do **not** invent a second authoritative UI state store.

### 3.3 Installed-game catalog differences to unify

`Views/ProfilePage.xaml.cs::Activate()` **always** calls `RefreshGamesAsync()`. This repeatedly scans Steam manifests/`shortcuts.vdf` on each tab return. `ProfileGameCatalogScanner.ScanAsync()` runs filesystem scans off-thread and is called through the existing frontend transport.

`Views/XboxPage.xaml.cs` already has `_catalogLoaded`, `_scanCancellation`, `ShouldStartCatalogScan(...)`, and explicit Refresh. On successful first scan it stores the result; returning to the tab reuses it; leaving the tab does not cancel the in-flight catalog scan. Keep these semantics.

The XBOX installed catalog reader (`XboxInstalledGameCatalog`) uses `PackageManager.FindPackagesForUser(string.Empty)` plus `MicrosoftGame.config`, NOT PackageCatalog installation-change subscriptions. The separate `XboxGameSessionRuntime` uses Window Foreground/Create/Show events for **active game identity**, not for installed-catalog refresh.

The main frontend RPC server (`NamedPipeAddonFrontendServer.ExecuteRequestAsync`) has a serial `operationGate`; unnecessary catalog prefetch could delay other UI requests. Do not introduce speculative background installed-catalog scans.

### 3.4 Overlay is separate and must remain working without Main UI catalog refresh

`AddonProcessHost.CaptureOverlayProfileQuickSettingsPageAsync()` resolves live target from `CaptureActiveQuickSettingsProfileTarget()`:

- Steam: live `ActualRunningAppId`, including non-Steam games whose AppID Steam publishes;
- XBOX: live `XboxGameSessionRuntime.ActiveGame`.

It then captures that target's profile through `InProcessAddonFrontendControl.CaptureQuickSettingsPageAsync`. The profile persistence layer can return writable defaults for a valid not-yet-saved target. **There is no requirement that the running game already appear in a Main UI catalog cache.**

For Steam only, `EnrichProfileDisplayNameAsync` can scan the Steam game catalog **if the stored profile display name is empty**. This is display-name enrichment, not an installed-catalog admission check. It is not part of this work order. Preserve the conditional Overlay no-running-game one-shot recheck added on 2026-10-10, and the Runtime's existing Steam/XBOX active-target change refresh.

## 4. Implementation approach

### 4.1 Make only Device eager; keep simple MainWindow ownership

Replace non-Device eager `<views:*Page .../>` children inside `MainWindow.xaml` with lightweight XAML hosts (e.g. one named `ContentControl` per page with initial `Visibility="Collapsed"`). Keep `<views:DevicePage x:Name="DeviceContent" />` eager. Keep existing `DeveloperContentHost`.

Example structural direction (names are illustrative; implement consistently with source/tests):

~~~xml
<Grid>
    <views:DevicePage x:Name="DeviceContent" />
    <ContentControl x:Name="ControllerPageHost" Visibility="Collapsed" />
    <ContentControl x:Name="ProfilePageHost" Visibility="Collapsed" />
    <ContentControl x:Name="XboxPageHost" Visibility="Collapsed" />
    <ContentControl x:Name="OverlayPageHost" Visibility="Collapsed" />
    <ContentControl x:Name="ShortcutPageHost" Visibility="Collapsed" />
    <ContentControl x:Name="TweaksPageHost" Visibility="Collapsed" />
    <ContentControl x:Name="SettingsPageHost" Visibility="Collapsed" />
    <ContentControl x:Name="HowToUsePageHost" Visibility="Collapsed" />
    <ContentControl x:Name="DeveloperContentHost" Visibility="Collapsed" />
</Grid>
~~~

Use straightforward nullable MainWindow-owned page fields (e.g. `ControllerPage? _controllerPage`) and narrowly scoped `GetOrCreate*` methods, consistent with existing Developer pages. Each method must:

1. Return the same page instance if it already exists.
2. Construct on the WinUI dispatcher, initialize once with existing `_frontend` / `_bootstrap` / HWND provider, and connect existing page event callbacks once.
3. Attach the page to its existing host and leave the host collapsed unless currently selected.
4. Seed only relevant known state: last status/Steam active flag, latest prerequisite status, or current window-owned mapping settings. Preserve `Settings` update badge/title event chain and existing current theme.
5. **Never** call page `Activate()` just because its control was warmed. For Steam/XBOX that would trigger forbidden catalog scans.
6. If page construction fails, log it, do not pretend it exists, and allow a subsequent explicit navigation attempt to retry via normal UI handling. Do not crash a successfully displayed Device screen solely because a nonessential background page failed.

The main constructor retains bootstrap/window shell setup, Device initialization, default nav selection, status refresh, and existing prerequisite-gated startup behavior. Do not move WinUI XAML creation onto `Task.Run` or any MTA/background thread.

### 4.2 Warm other ordinary controls after the visible Device surface

Start a **one-shot**, UI-dispatcher **low-priority**, sequential warm-up after first real activation / first initial visible rendering opportunity. Do not run it synchronously from `MainWindow` constructor or delay `App.StartFrontendAsync()` activation. Use the already owned `DispatcherQueue` and ordinary page fields: one materialization **per dispatched callback**, then post the next at low priority, yielding to render/input/navigation between pages.

Suggested order (adjust only based on measured page construction cost):

~~~text
Device [eager]
  -> [window visible and responsive]
  -> Controller
  -> Settings
  -> ClawHUD
  -> Shortcut
  -> Tweaks
  -> Steam page CONTROL ONLY
  -> XBOX page CONTROL ONLY
  -> [done]

How to Use: never in warm-up; on first user entry only
Developer pages: existing on-demand only
~~~

A newly selected tab **must take priority** over queued optional work. If it is already prepared, use it. Otherwise materialize it immediately on the dispatcher and activate. The queued sequence must skip pages already built. Do not create the same page twice. Do not turn a user click into a wait for the remaining warm-up queue. Cancel/stop pending warm-up on window close; do not schedule work after dispose, disconnect, or shutdown. Keep this a small MainWindow-local one-shot routine; no background worker manager, global navigation framework, polling or arbitrary timeout.

Importantly, `OnWindowActivated` fires around `Show()` and is **not itself proof of a fully painted frame**. Ensure warm-up is actually deferred to a later dispatcher/render opportunity. Check the built-in `Loaded` / dispatcher ordering; if needed, use a narrowly scoped one-shot first-render callback, not a timer, delay loop or new state machine. Record timestamps so the first-window activation is visibly earlier than page warm-up work.

### 4.3 Keep established navigation behavior

`ShowPage` should (a) ensure destination page is present when needed, (b) control host visibility, (c) preserve old-page `Deactivate` and new-page `Activate` hooks, and (d) leave navigation state and natural page reuse intact. Preserve:

- Device first selection/refresh; Center M startup re-read on Device entry.
- Controller activation refreshes, vibration lifetime flags, Front/OEM/M1/M2 mappings, LED debounce and one ordered persistence chain.
- Steam activity guards when Controller or Profile becomes active, including a fresh `RefreshSystemStatusAsync`.
- XBOX selected game/profile edit view and cancellation on leaving, without losing its cached catalog.
- Shortcut drag/capture cleanup and page refresh only while active.
- ClawHUD state refresh and Settings/Tweaks invalidation behavior.
- Settings native NavigationView item, system Light/Dark theme, pending update-ready InfoBadge/window title and existing Settings-required-components/prerequisite notifications.
- DeveloperMenu/diagnostic destinations and nested return/back flow.
- How to Use existing lazy WebView2 creation and `NavigationCompleted` success contract; create the **page** on first entry too.

Do not force initial Device status to await all page construction. When `RenderSystemStatus` receives a snapshot before other pages are built, render Device immediately and keep `_latestSystemStatus`. On later Controller/Profile/Settings creation apply this last known snapshot where needed. `StateInvalidated` must not create an absent page merely to refresh it; the page captures state at creation/activation instead.

Preserve mutation save on window close even if Controller has never been materialized. Programmatic callbacks that can only originate from an existing page may safely assume that page exists, but general snapshot/closure callbacks must not.

### 4.4 Steam: successful first scan only, then explicit Refresh

In `Views/ProfilePage.xaml.cs` change the unconditional `Activate() -> RefreshGamesAsync()`. Follow existing XBOX page policy without copying its unnecessary complexity:

- Add a minimal successful `_catalogLoaded` flag, plus an in-progress guard for a first scan still running during a tab switch.
- On **first tab entry**, run `ScanProfileGamesAsync()` once. Mark loaded only after successful response; successful **empty** catalog still counts as loaded.
- When leaving and returning, preserve `_catalog`, selected game, search/filter and editor drafts as existing lifecycle permits; rebind cached list if necessary; **do not rescan**.
- The existing Refresh button **always** requests a fresh scan when allowed, even if loaded. Favor existing XBOX-like cancellation/replacement semantics or a simple disable-while-in-progress behavior so rapid manual Refresh cannot allow an older result to overwrite newer data. No repeated automatic retries.
- If initial scan fails, show the existing error and allow an explicit Refresh or a subsequent tab visit to try again.
- Preserve favorite toggles, mutation responses and selected-profile capture; do not reset a valid catalog because a later Refresh fails.

Reference `Views/XboxPage.xaml.cs` `ShouldStartCatalogScan`, `_catalogLoaded` and `RefreshGamesAsync` behavior, but implement only the state necessary for Steam.

### 4.5 XBOX: keep successful-catalog reuse; do not add events

No XBOX scanner, profile, or session changes are needed. Its current `_catalogLoaded` and Refresh behavior already meets the decision. Ensure the XBOX page warm-up does not call `Activate`, `ScanXboxGamesAsync` or create an implicit auto-scan. Retain the existing Windows WinEvent-based active-game detector; it is not a PackageCatalog install-notification feature.

### 4.6 Diagnostics for actual latency

Use the existing UI logging path and a few precise `Stopwatch`-based measurements; no telemetry service or routine spam:

- App frontend connection completed / Bootstrap acquired / MainWindow initialized / activated (existing logs).
- MainWindow constructor total duration and, if useful, `InitializeComponent` portion.
- Deferred-warm-up started **after** initial visibility scheduling; page name, elapsed construction/initialize duration; warm-up complete.
- Steam/XBOX initial catalog scan requested/completed, with outcome/duration (use existing Runtime catalog logs where adequate).
- UI window close while warm-up pending: verify no new page is constructed.

Compare against observed ~1.38–1.41s tray activation and ~0.68–0.69s MainWindow construction. Do not prescribe a fixed threshold or claim the entire process cold-start segment disappears; WinUI process and Runtime named-pipe connection are unchanged.

## 5. Tests and verification

Update existing source-contract tests to represent the new hosts and nullable page lifetime, rather than preserving assertions that depend on old eager `ControllerContent/ProfileContent/XboxContent/SettingsContent` strings. In particular inspect:

- `tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs`: developer lazy materialization; Device Center M; status, prerequisites, Settings update and invalidation; ClawHUD page ownership; source-level navigation ordering.
- `tests/SteamInputAddonforClaw.UiTests/XboxCatalogPageUiTests.cs`: current XBOX cache and manual refresh assertions, plus now-deferred XBOX page creation.
- `tests/SteamInputAddonforClaw.UiTests/SettingsPageUpdateTests.cs` and any current update badge/theme tests.
- Steam Profile-page tests and any window-specific source assertions referencing eager page names.

Include focused automated coverage or source-contract assertions for:

1. `MainWindow.xaml` eagerly constructs only Device among normal destinations.
2. MainWindow initialization does **not** materialize other normal pages or run catalog scans before initial display.
3. Only one warm-up page is constructed per low-priority dispatcher turn; user early navigation uses the same instance and is not blocked by the remaining sequence.
4. How to Use is not warmed, initializes only on first entry; Developer diagnostics remain lazy.
5. No `Activate` / Steam or XBOX `Scan*GamesAsync` call during background page materialization.
6. First Steam tab entry scans; a successful empty list caches; tab leave/re-entry uses the cache; Refresh rescans; scan failure remains retryable; stale overlapping result cannot overwrite newer Refresh.
7. Existing XBOX first-entry/re-entry/Refresh contract remains unchanged.
8. `CaptureStatus`/`StateInvalidated` before Settings/Controller/Profile construction do not throw, do not force creation, and are reflected on first display. Preserve update-ready badge/title, theme selection and prerequisite UI.
9. Controller/front-button/LED save chain and window close semantics remain correct; no dangling events/extra page construction after close.
10. Opening a new game without Main UI catalog Refresh still yields Overlay profile through the **existing independent live target path**; contract tests can establish separation. No user hardware test belongs in CI or PR acceptance.

Build and run the relevant existing .NET UI/Runtime automated test projects in CI. Source-only unit tests should not pretend to prove true paint timing; use logs/measurements for latency.

**Physical MSI Claw testing and subjective first-paint/first-navigation speed are owned by the user after merge, never required of local Codex or a merge blocker.**

## 6. Scope, PR sizing, and acceptance

One focused Main UI PR is reasonable (XAML host conversion, MainWindow page ownership/warm-up, Steam Profile cache policy, associated tests). Avoid unrelated refactor/cleanup and Runtime code changes. If test migration or one-PR diff becomes unwieldy, split into a page lifecycle warm-up PR and a small Steam cache-policy PR without requiring a staged framework or extra architecture.

Acceptance checklist:

- [ ] Device is the only eager user-page tree and is shown before optional page warm-up.
- [ ] Other normal pages are subsequently constructed sequentially and reusable; early clicks are handled.
- [ ] How to Use and developer-only pages remain first-use lazy.
- [ ] Steam/XBOX installed catalogs **never** scan during warm-up or as a side effect of unrelated Main UI status invalidation.
- [ ] Steam/XBOX first tab entry loads the installed catalog; re-entry reuses successful state; Refresh is manual and effective.
- [ ] No package-install watcher or auto-update listener for either installed catalog.
- [ ] Overlay active-target detection / profile availability remains independent and unchanged.
- [ ] Existing Full1902 controller, overlay surface, Settings badge/theme, profile editing, and shutdown contracts remain intact.
- [ ] Relevant build/tests pass and the code change contains no practical regression blocker.
- [ ] Post-merge device verification is left to the user.

## 7. Do not do

- Do not keep the Main UI process alive solely to make reopen instantaneous.
- Do not preload installed Steam/XBOX game lists after Device becomes visible.
- Do not initialize XAML controls using `Task.Run`.
- Do not run all deferred constructors in one blocking dispatcher callback.
- Do not add locks/epochs/manager interfaces or speculative race defenses.
- Do not lose already-rendered Device state when other pages are created.
- Do not modify Overlay Profile first-show logic, conditional no-game recheck, `CaptureActiveQuickSettingsProfileTarget`, Steam RunningAppID, XBOX `ActiveGame` or PID1902 lifecycle.
- Do not make real-hardware checks mandatory before merge.

---
End of work order.
