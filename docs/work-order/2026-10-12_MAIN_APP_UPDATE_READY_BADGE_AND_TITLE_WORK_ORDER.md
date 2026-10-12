# Work Order — Main App Update-Ready Indicator (Native WinUI 3 Settings Badge + Window Title)

> **Date:** 2026-10-12
> **Target repository:** `onehoon/SteamAddonforClaw`
> **Target branch:** `main` (reviewed near `4a67a21e863aab6cad3829336fd4ef79fd336423`; inspect the latest HEAD again before editing)
> **Implementation:** One small PR, Main App frontend only
> **Status:** Implementation work order for local Codex; hardware validation is a separate owner activity, not a merge gate

## 1. Goal

When the existing VeloPack update flow has **already downloaded** an update and Runtime reports `FrontendUpdateState.ReadyToInstall`, expose the state unobtrusively in two places in the **main WinUI 3 desktop app**:

1. A **native dot `InfoBadge`** on the existing built-in `NavigationView` **Settings** item.
2. A suffix on the existing Windows **window title**, exactly ` — Update ready`.

The existing `Settings > Application updates` card continues to show the authoritative description and `Install update` button. The badge is an indicator, **not** an extra download/apply action.

Do **not** implement the previously discussed Overlay/Rail indicator. No Overlay process, wire protocol, tab, settings card, or gamepad workflow is within scope.

## 2. Source-of-truth and boundaries

Read the **current code**, not historical design prose, before making changes. The relevant current sources are:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
- `docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md` (with current Steam/XBOX and standalone overrides)
- `docs/work-order/STARTUP_UPDATE_PR_A_DECOUPLE_CHECK_DOWNLOAD_FROM_CONTROLLER_STARTUP_WORK_ORDER.md`
- `docs/work-order/2026-10-12_MAIN_APP_UI_SYSTEM_LIGHT_DARK_THEME_WORK_ORDER.md`
- `src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs`
- `src/SteamInputAddonforClaw/Updates/FrontendUpdateCoordinator.cs`
- `src/SteamInputAddonforClaw/Updates/VelopackUpdateClient.cs`
- `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs`
- `src/SteamInputAddonforClaw.UI/MainWindow.xaml`
- `src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs`
- `src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml`
- `src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs`
- `tests/SteamInputAddonforClaw.UiTests/MainWindowTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/SettingsPageUpdateTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/MainUiThemeTests.cs`

**Current implementation observations, verified on 2026-10-12:**

- `FrontendUpdateState` already contains `ReadyToInstall`, and `FrontendUpdateSnapshot.CanInstall` already maps to it. No new update state is required.
- `FrontendUpdateCoordinator` owns background check/download and the `StateInvalidated` event; it confirms a pending downloaded update through VeloPack. Its background work follows controller startup and must not interrupt the active Runtime.
- `SettingsPage.Initialize(...)` invokes `RefreshAppUpdateAsync()`; `RenderAppUpdate(snapshot)` is the existing **single** Settings-card rendering entry point, setting `UpdateCard.Description`, `UpdateButton.Content`, and `UpdateButton.IsEnabled`.
- `MainWindow` is already subscribed to `_frontend.StateInvalidated` and invokes `SettingsContent.RequestAppUpdateRefresh()` in `RefreshInvalidatedFrontendStateOnUiThread()`.
- `MainWindow.xaml` has `MainNavigationView` as a normal left `NavigationView`, with its **built-in** Settings entry. It must not add a second custom Settings menu item.
- `MainWindow.xaml.cs` currently sets `Title = FormatWindowTitle(GetDisplayVersion())`. `FormatWindowTitle` has a direct unit test.
- The main app now has a System/Light/Dark theme feature (`MainAppRoot`, `AppThemeSelectionRequested`, `AppThemeCard`, system titlebar preferred theme). Preserve these changes; do not overwrite/revert them.
- Main UI and Runtime are separate processes; the UI is disposable and never owns Full1902 controller lifetime.
- `Program.cs` already attempts to apply pending VeloPack updates **on the next safe primary startup before controller admission**. This task is only a visual readiness indicator; do not alter this policy.

Official Microsoft references:

- [InfoBadge design guidance](https://learn.microsoft.com/windows/apps/develop/ui/controls/info-badge) — a property-free `InfoBadge` is a dot; `NavigationViewItem.InfoBadge` is the recommended native placement.
- [NavigationView.SettingsItem](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.navigationview.settingsitem) — built-in Settings item is exposed as an `object`, so cast it to `NavigationViewItem`.
- [NavigationViewItem.InfoBadge](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.navigationviewitem.infobadge) — native badge surface.

## 3. Exact UX contract

| Runtime-reported state | Settings navigation badge | Window title suffix | Existing Settings card |
| --- | --- | --- | --- |
| `Unavailable`, `Idle` | None | None | Keep existing behavior |
| `Checking` | None | None | Keep existing behavior |
| `UpToDate` | None | None | Keep existing behavior |
| **`ReadyToInstall`** | **Native informational dot** | **` — Update ready`** | Already displays `Install update` |
| `Installing` | None | None | Keep existing behavior |
| `Failed` | None | None | Existing failure/retry description |

Normal title: `Steam Addon for Claw v<version>`

Ready title: `Steam Addon for Claw v<version> — Update ready`

Additional requirements:

- The dot remains visible when Settings is selected/opened; merely reading the Settings page must **not** dismiss it.
- The dot and title stay in agreement and always reflect the **same last rendered authoritative snapshot**.
- Dismiss the indicator only when a later snapshot no longer reports `ReadyToInstall`; do not persist a separate dismissed/read flag.
- On app startup, show the normal title immediately and update it once the existing first `CaptureAppUpdateAsync` completes. Do not delay main-window creation.
- When the update downloads in the background while the UI is open, use existing frontend invalidation to refresh and show the indicator.
- UI styling should inherit existing Fluent/System/Light/Dark appearance. Do not hardcode colors or sizes.
- Do not add a new update-available/progress badge: **only fully downloaded and ready to install** is indicated.
- The Settings-card text and button remain the single place to understand/apply the update. No new banner, popup, auto-navigation, forced install, or toast.

## 4. Implementation design: one small page-to-window notification

Use the **existing Settings update capture/rendering path**. Do **not** add a second `CaptureAppUpdateAsync` request from `MainWindow`, duplicate subscriptions to Runtime update state, another coordinator, timers, polling, or persisted UI state.

### 4.1 SettingsPage: publish what it already rendered

In `Views/SettingsPage.xaml.cs`, add a narrowly scoped event such as:

```csharp
internal event Action<FrontendUpdateSnapshot>? AppUpdateSnapshotRendered;
```

At the end of the existing `RenderAppUpdate(FrontendUpdateSnapshot snapshot)` method, **after** updating the card and button, publish the same snapshot:

```csharp
private void RenderAppUpdate(FrontendUpdateSnapshot snapshot)
{
    _updateSnapshot = snapshot;
    UpdateCard.Description = snapshot.Message;
    UpdateButton.Content = snapshot.CanInstall ? "Install update" : "Check";
    UpdateButton.IsEnabled = snapshot.CanCheck || snapshot.CanInstall;

    AppUpdateSnapshotRendered?.Invoke(snapshot);
}
```

Adapt the exact code to the latest file if it has evolved. This event intentionally reports a **rendered UI projection**, not a new update owner. It covers first capture, `StateInvalidated` refresh, manual check/download, install response, and local failure rendering without adding new RPCs.

Preserve `RefreshAppUpdateAsync`'s existing exception classification and `UpdateButton_Click` operation guard/final refresh ordering. Do not introduce a new await/capture into the event handler.

### 4.2 MainWindow: subscribe before SettingsPage.Initialize

`SettingsPage.Initialize` starts the first async update refresh. In the `MainWindow` constructor, subscribe to `SettingsContent.AppUpdateSnapshotRendered` **before** calling `SettingsContent.Initialize(...)`, analogously to the existing App Theme event.

Example (illustrative; integrate with the actual constructor):

```csharp
SettingsContent.AppThemeSelectionRequested += OnAppThemeSelectionRequested;
SettingsContent.AppUpdateSnapshotRendered += OnAppUpdateSnapshotRendered;
SettingsContent.Initialize(_bootstrap, _frontend, _mainUiThemePreference);
```

Use an ordinary WinUI-thread handler:

```csharp
private void OnAppUpdateSnapshotRendered(FrontendUpdateSnapshot snapshot)
{
    _isUpdateReady = snapshot.State == FrontendUpdateState.ReadyToInstall;
    Title = FormatWindowTitle(GetDisplayVersion(), _isUpdateReady);
    ApplyUpdateReadySettingsBadge();
}
```

The actual `RenderAppUpdate` path resumes on the UI thread via `ConfigureAwait(true)`; do not add a generic dispatcher wrapper, synchronization manager, lock, or thread pool scheduling solely for this synchronous UI render notification.

Keep the local `_isUpdateReady` field **presentation-only**: it merely remembers which badge should be applied if the `NavigationView` Settings item has not yet been materialized. It does not make update policy decisions, persist anything, or drive VeloPack.

### 4.3 Native NavigationView Settings InfoBadge

The built-in item is accessed through `MainNavigationView.SettingsItem`, not via a new `NavigationViewItem` in `MenuItems`.

Create a reusable standard WinUI 3 `InfoBadge` (no `Value` and no `IconSource` = native dot). Prefer the built-in `InformationalDotInfoBadgeStyle` when it is available through the current WinUI 3 theme resource path. Do **not** introduce a custom template, overlayed `Ellipse`, restyled Settings entry, new icon image, or visual-tree crawling merely to change its color.

Illustrative minimal logic:

```csharp
private readonly InfoBadge _updateReadyDotBadge = new();
private bool _isUpdateReady;

private void ApplyUpdateReadySettingsBadge()
{
    if (MainNavigationView.SettingsItem is NavigationViewItem settingsItem)
        settingsItem.InfoBadge = _isUpdateReady ? _updateReadyDotBadge : null;
}
```

Call `ApplyUpdateReadySettingsBadge()` when an update snapshot is rendered, and once from the **existing** `MainNavigationView_Loaded` handler to handle the ordinary initial navigation-template lifecycle. Avoid creating a second Loaded handler, duplicated item, or polling. Reusing one badge avoids needless allocation on every frontend invalidation.

Native `NavigationViewItem.InfoBadge` handles compact/expanded item placement. The existing Settings navigation selection logic and device-page default must be unchanged.

If a verified WinUI 3 2.5.1 native limitation prevents attaching an `InfoBadge` to the built-in Settings item, report the concrete limitation rather than replacing the native Settings item with a custom menu/template. Do not silently expand scope.

### 4.4 Title text: preserve the existing format

Preserve `FormatWindowTitle("0.1.20") == "Steam Addon for Claw v0.1.20"` for existing callers/tests. A minimal optional parameter is sufficient:

```csharp
internal static string FormatWindowTitle(string version, bool updateReady = false)
{
    var baseTitle = $"Steam Addon for Claw v{version}";
    return updateReady ? baseTitle + " — Update ready" : baseTitle;
}
```

Do not use `AppWindow.TitleBar.ExtendsContentIntoTitleBar`, replace the system titlebar, append emoji, add an extra titlebar button, or change theme-dependent titlebar configuration.

### 4.5 Cleanup

Follow the existing `MainWindow.OnWindowClosed` cleanup convention and detach the new `SettingsContent.AppUpdateSnapshotRendered` handler if other page/window event handlers are detached there. No additional life-cycle manager is necessary.

## 5. Explicit non-goals and regression boundaries

**No changes whatsoever** to:

- `FrontendUpdateCoordinator` update state decisions or `SilentUpdateService` network scheduling.
- `VelopackUpdateClient` pending-update detection, download/apply, or restart arguments.
- `Program.cs` apply-before-Runtime-startup behavior.
- `FrontendUpdateState`, `FrontendUpdateSnapshot`, `IAddonFrontendControl`, frontend RPC/IPC schemas, `StateInvalidated` transport protocol.
- Existing `SettingsPage` `Application updates` card layout or button behavior.
- `AppTheme` preference, dynamic Windows app theme, `MainAppRoot`, or existing titlebar theme styling.
- `SteamInputAddonforClaw.Overlay`, Overlay pipe/wire contract, Rail, QAM, Win+G, controller capture.
- System tray, taskbar badge/overlay icon, Windows toast/app notifications.
- Full1902 Runtime, PID1901/PID1902, HidHide, VIIPER, DirectInput, recovery, controller ownership, sleep/hibernate/resume, shutdown, Center M policy.
- CTW integration: not applicable; this is a standalone Full1902 app.

Do not introduce general badge services, notifications frameworks, generic state authority, extra view models, state machines, retry loops, new locks, or timer-based checks for this work.

## 6. Tests (automated/code-level; local Codex responsibility)

Update/add focused tests in the **existing** `SteamInputAddonforClaw.UiTests` project.

1. Extend `MainWindowTests.FormatWindowTitle_IncludesTheVersionPrefix` coverage: default/false preserves the original title; true appends the exact suffix once.
2. Assert the indicator predicate maps **only** `FrontendUpdateState.ReadyToInstall` (or equivalently `snapshot.CanInstall`) to true; all other states false. Use a small pure testable expression/helper only if it genuinely simplifies the current code; do not create an abstraction solely for testability.
3. Verify the `SettingsPage.RenderAppUpdate` event is published after the card/button update, and that the `MainWindow` subscription exists **before** SettingsPage initialization. Prefer a compact behavior test when the current test harness allows it; a minimal source-contract test is acceptable for wiring that cannot be instantiated in normal unit tests.
4. Verify the existing `MainNavigationView_Loaded` hook reapplies the badge, and the built-in Settings item is used rather than adding a duplicated navigation entry.
5. Verify no additional update RPC/polling path or VeloPack update decision code was introduced.
6. Preserve the existing `SettingsPageUpdateTests`, `MainWindowTests`, `MainUiThemeTests`, and normal UI build/test behavior.

Build and run applicable automated tests when supported by the local developer environment. **Physical hardware validation is not a Codex acceptance criterion and must not block the PR/merge**; the project owner performs any post-merge device testing.

## 7. Acceptance

- Only the main app's existing **Settings** navigation entry displays a native informational dot when the downloaded update is `ReadyToInstall`.
- Main app title then reads `Steam Addon for Claw v<version> — Update ready`.
- Both indicators appear on the existing first capture and on background invalidation without navigating to Settings.
- Opening Settings does not dismiss the badge; the existing `Install update` button still works exactly as before.
- `Installing`, `UpToDate`, `Failed`, `Checking`, `Idle`, and `Unavailable` show the original title and no badge.
- Indicators respond consistently to install initiation or other authoritative state changes.
- Built-in System/Light/Dark theme and native NavigationView compact/expanded layout remain intact without bespoke styling.
- No behavioral changes to download/apply/restart or Full1902 controller lifecycle.
- No Overlay code, protocol, QAM, toast, taskbar, or custom titlebar changes.

## 8. Delivery

Implement as **one focused PR**. Keep the diff small (likely a few dozen production lines and focused tests) and provide an English PR description summarizing the source-reviewed decisions and automated validation. Do not write a hardware-validation requirement into the PR. This work order is documentation only; it does not itself authorize a separate unrelated refactor or expansion of the updater.
