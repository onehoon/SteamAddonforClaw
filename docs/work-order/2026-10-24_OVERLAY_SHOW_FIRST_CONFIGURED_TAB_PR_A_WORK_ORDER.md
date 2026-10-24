# Work Order — PR-A: Always Open Quick Settings Overlay on the First Configured Tab

**Date:** 2026-10-24  
**Status:** Ready for local Codex implementation  
**Repository:** `onehoon/SteamAddonforClaw`  
**Target:** Latest `main` (reviewed baseline `8251ffa1660f560fc8fb042af2a9fa9172807198`, after PR #871)  
**Delivery:** One narrow, independent PR-A, prior to Y-button Quick Jump  
**Product:** Standalone Full PID1902 Addon, one supported interactive Windows user/session

## 0. Goal and frozen user decision

When the user opens the in-game Quick Settings Overlay, **always select the first tab from the authoritative persisted Overlay tab order** (`AddonQuickSettingsTabOrder[0]`) — regardless of whether a Steam game, an XBOX game, or no game is currently active.

The existing "open Profile automatically when a Steam/XBOX game is running" shortcut is being retired because the Profile tab is not a per-open action. A separate future PR will provide optional **Y-button Quick Jump**, initially targeted to Profile; **Y-button handling/configuration is explicitly NOT part of PR-A**.

| Context at Overlay Show | Existing behavior | PR-A behavior |
| --- | --- | --- |
| Active Steam game | Profile forcibly selected | Current authoritative tab-order first item |
| Active XBOX game | Profile forcibly selected | Current authoritative tab-order first item |
| No active game | Current tab-order first item | Same |
| Tab order has been customized | Runtime order persists but active-game Show bypasses first tab | Every Show selects current order[0] |
| User switches tabs while visible | Existing LB/RB/pointer handling | Unchanged; never force a tab while visible |
| Overlay hidden then shown again | Existing reset with optional Profile override | Reset to current order[0] |

The existing default order (only if the user has not changed it) is **Shortcut, Device, Controller, Setting, Profile**. **Do not reorder or reset a user's saved order.**

## 1. Mandatory architecture and code reading

Read the following before writing code, using the current latest `main` as the code authority:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
- `docs/overlayui/README.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
- `docs/work-order/OVERLAY_CONTROLLER_DEVICE_POLISH_AND_ACTIVE_GAME_PROFILE_FIRST_WORK_ORDER_2026-10-04.md` (historical, superseded **only** for Show-selected tab)
- `docs/work-order/1007_XBOX_M1_M2_UI_SEMANTICS_AND_ACTIVE_PROFILE_OVERLAY_FIX_WORK_ORDER.md` (historical, superseded **only** for Show-selected tab)
- `docs/work-order/2026-10-10_OVERLAY_PROFILE_NO_RUNNING_GAME_CONDITIONAL_RECHECK_WORK_ORDER.md` (preserve active Profile refresh, loading and no-game state)
- `docs/work-order/2026-10-24_OVERLAY_VERTICAL_WRAP_NAVIGATION_PR1_WORK_ORDER.md` (preserve existing Up/Down and LB/RB wrap)

Inspect these current source locations:

- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Presentation.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayTabState.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs`
- `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
- `src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs`
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs`
- `src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsShellContracts.cs`

Relevant tests to inspect:

- `tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/OverlayTabStateTests.cs`
- `tests/SteamInputAddonforClaw.Tests/AddonProcessHostOverlayQuickSettingsContractTests.cs`
- `tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs`
- any current tests of Overlay command dispatch, configured tab order, Profile loading placeholder and no-game recheck.

## 2. Verified code facts and the important separation

The currently implemented Show path is:

```text
AddonProcessHost.CoordinateOverlayToggleAsync()
  -> var preferActiveProfile =
         CaptureActiveQuickSettingsProfileTarget() is not null
  -> OverlayProcessController.ShowAsync(preferActiveProfile)
  -> existing OverlayCommand.Show or ShowActiveProfile
  -> Overlay App.HandleCommandAsync
  -> OverlayWindow.ShowForPocAsync(preferActiveProfile: ...)
  -> OverlayWindow.ResetUiForShow(preferActiveProfile)
  -> _tabState.ResetForShow()         // correctly selects configured order[0]
  -> if (preferActiveProfile) _tabState.Select(Profile)  // behavior to REMOVE
```

`OverlayTabState.ResetForShow()` is already correct and derives the first tab from the current, validated `Order`. `AddonQuickSettingsTabOrderContract.DefaultOrder` already defines the default; Runtime settings preserve user changes and the Overlay already receives authoritative tab order before Ready.

**Separate two responsibilities that currently share the local variable `preferActiveProfile`:**

1. **Which tab is initially displayed?** The Overlay's `_tabState.ResetForShow()` now has the final say, always selecting current order[0].
2. **Which Quick Settings page gets priority during the first asynchronous data refresh?** Runtime currently passes the active-target fact to `_overlayController.RefreshQuickSettingsAsync(preferActiveProfile)` after capture is committed. Retain the existing capture/publish priority and target authority. Removing forced Profile selection must NOT remove or delay Profile data publication.

The Profile page's existing loading placeholder (`PrepareActiveProfileFirstShow()`), authoritative game-context projection, no-running-game conditional recheck and live game-target refresh remain valid even if the first visible tab is Shortcut/Device/Controller/Setting.

## 3. Production implementation — minimum coherent edit

### A. Simplify Overlay-local Show selection

In `OverlayWindow.Shell.cs`, change `ResetUiForShow` to take no active-Profile selection argument, and **remove only the Profile override**:

```csharp
// The first visible tab always follows the current authoritative tab order.
private void ResetUiForShow()
{
    PrepareActiveProfileFirstShow();  // preserve existing loading placeholder
    _tabState.ResetForShow();         // one selected-tab authority: Order[0]
    ResetShortcutForShow();
    ResetSettingCardsForShow();
    ApplySelectedTabVisualState();
}
```

Retain the existing order/selection visual handling, especially:
- reset before any window reveal/animation, so a prior tab is never briefly shown;
- `ApplySelectedTabVisualState()` and its row selection/scroll path;
- `_tabState.TryApplyOrder` semantics (live order changes keep the current tab selected; the new first tab applies on the **next** Show);
- `PrepareActiveProfileFirstShow()` and the existing Profile rendering/recheck guard.

Do not add a `StartupTab` preference, extra per-game branch, extra state authority or default Profile fallback.

### B. Remove now-unused Overlay-window parameter

In `OverlayWindow.Presentation.cs`, the window is no longer responsible for deciding Profile-first. Simplify its local method:

```csharp
internal async Task ShowForPocAsync()
{
    ResetUiForShow();
    ConfigureWindow();
    // keep all existing visual Show lifecycle/animation/capture-adjacent behavior
}
```

Do not change the rest of Show/Hide implementation or its safety contracts.

In `SteamInputAddonforClaw.Overlay/App.xaml.cs`, handle both **existing** commands as the same Overlay visual Show:

```csharp
case OverlayCommand.Show:
case OverlayCommand.ShowActiveProfile:
    await _window.ShowForPocAsync();
    break;
```

**Keep `OverlayCommand.ShowActiveProfile` in the existing wire enum for this PR.** Although it no longer changes visual selection, Runtime currently chooses this command based on active profile and separately uses the same active-target fact for prioritized data publication. Retaining the command preserves protocol compatibility and minimizes risk/changed files; transport cleanup can be addressed separately if ever useful, but is not necessary for this feature. No protocol version bump.

### C. Keep Runtime and transport admission untouched

Do NOT edit `AddonProcessHost.CoordinateOverlayToggleAsync()`, `OverlayProcessController.ShowAsync(...)`, `OverlayProcessController.RefreshQuickSettingsAsync(profileFirst)`, `OverlayWire.cs` or the server/client command validation just to remove visual Profile auto-selection.

Specifically retain:

```csharp
var preferActiveProfile = CaptureActiveQuickSettingsProfileTarget() is not null;
...
await _overlayController.ShowAsync(preferActiveProfile);
...
// After presentation capture commits:
_ = _overlayController.RefreshQuickSettingsAsync(preferActiveProfile);
```

This is still useful for refreshing Profile data first when a game is active. The command variant remains understood by both peers, but **neither variant overrides the saved initial tab order**.

**No change** to pause/resume game-facing presentation, neutral output, physical controls, overlay capture/release, lifecycle/teardown or per-game settings mutation authority.

## 4. Tests — update obsolete old-policy assertions, preserve safety coverage

### Overlay UI and tab order

`tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs` currently has a test explicitly asserting:

```csharp
Assert.Contains("if (preferActiveProfile) _tabState.Select(AddonQuickSettingsTabId.Profile);", shell);
Assert.Contains("ShowForPocAsync(preferActiveProfile: true)", app);
```

Replace those outdated expectations with checks that:
- `ResetUiForShow()` always invokes `_tabState.ResetForShow()`;
- `ResetUiForShow()` does **not** select Profile based on any active-game flag;
- both `OverlayCommand.Show` and `OverlayCommand.ShowActiveProfile` dispatch to the **same** `ShowForPocAsync()` visual path;
- `PrepareActiveProfileFirstShow()` still occurs before the reveal and preserves its existing no-game loading/presentation behavior.

Update source-method-extraction tests that hard-code the old signature `ResetUiForShow(bool preferActiveProfile = false)`. Do not weaken or delete the existing Profile loading/recheck assertions.

`tests/SteamInputAddonforClaw.UiTests/OverlayTabStateTests.cs` already tests the real `ResetForShow()` and reordered tabs. Adapt the obsolete `ActiveProfileStartupSelectionPreservesConfiguredTabOrderAndTraversal` test: it currently manufactures the old startup override by manually calling `Select(Profile)` immediately after `ResetForShow()`. Either convert it to an **explicit user-initiated Profile selection** test with a truthful name, or use it to test that a Show reset after prior manual Profile selection restores configured order[0]. Keep traversal assertions where useful.

Add/keep focused tests verifying:
1. Default order Show picks Shortcut first.
2. A custom order such as `Controller, Device, Profile, Shortcut, Setting` picks Controller first, even when Profile is available.
3. Closing/reopening resets a manually selected Profile to the configured first tab.
4. Live tab reorder preserves the current page until next Show, and next Show picks the new order[0].
5. LB/RB tab cycling/wrap and D-pad/stick vertical navigation remain unchanged.
6. The Profile loading placeholder, empty-state and no-running-game conditional recheck still work when Profile is opened later.

### Runtime/transport contract

`AddonProcessHostOverlayQuickSettingsContractTests` already verifies that `preferActiveProfile` comes from the unified Steam/XBOX target and is passed into the post-capture `RefreshQuickSettingsAsync(preferActiveProfile)`. Retain that test, even if the older test name mentions "first show".

`OverlayTransportTests` covers the legacy `ShowActiveProfile` command validity and normal visibility acknowledgement. Preserve those tests and the protocol version.

Do **not** require new controller mocks, a second state coordinator, UI automation, test-only synchronization layers, or simulated impossible instruction-level races. Reuse the existing pure `OverlayTabState` tests plus focused source/wiring assertions.

### Local Codex validation commands

```powershell
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Debug
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Release
dotnet build SteamInputAddonforClaw.slnx -c Release
git diff --check
```

Run relevant repository CI. Do not expand the PR for unrelated warnings or source-cleanup opportunities.

**User-owned physical MSI Claw / in-game overlay validation happens after code-correct merge. Never make physical-device verification a Codex acceptance requirement, CI gate or PR review blocker.**

## 5. File scope and non-goals

Expected production changes:
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Presentation.cs`
- `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`

Expected test changes:
- `tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/OverlayTabStateTests.cs`
- Other related current tests only where directly necessary.

**Explicitly deferred:**
- Y-button mapping and its Main App Quick Overlay + in-game Setting editors.
- New user preference for a startup tab (the **existing tab order's first entry** already owns this).
- Reordering the default/persisted tab order.
- Changing Runtime's data-refresh priority, game detection or Profile capture/refresh behavior.
- Changing Overlay IPC/wire DTOs/protocol enum/version.
- Horizontal/vertical tab-order editor glyph polish.
- Input router, physical button bindings, PID1901/PID1902, HidHide, VIIPER, suspend/resume, shutdown, rollback, fail-close or Steam QAM.

Do not introduce new managers, state machines, epochs, gates, general-purpose navigation interfaces or duplicated selection authorities.

## 6. Acceptance checklist

- [ ] Steam game, XBOX game or no game: Overlay always opens at currently configured `Order[0]`.
- [ ] Both existing wire Show variants open with identical selected-tab behavior.
- [ ] Never flash the previously selected tab during reveal.
- [ ] Users can move away from Order[0] normally; reopening resets to Order[0].
- [ ] Runtime Profile state still loads and updates, including Steam/XBOX targets and no-game empty state.
- [ ] No data-refresh priority regression or newly blocked/delayed controller capture.
- [ ] Current tab-order editor, Settings data, LB/RB/D-pad/stick, A/B and feature mutation remain unchanged.
- [ ] Existing tests/build pass; obsolete forced-Profile selection assertions are replaced by new first-tab expectations.
- [ ] No real-device verification is required for PR merge.

**Handoff:** Implement the small PR-A in local Codex. The user performs physical/visual validation after merge.

---

## Separate design note — tab-order button glyphs (NOT part of PR-A)

The current in-game Setting tab-order editor uses literal string characters `◂` / `▸` as Button content in `OverlayTabOrderRow.cs` (`CreateMoveButton`), even though the actions change **vertical order**. It reuses `QamValueButtonStyle` with a 40-DIP minimum width and only 22-DIP minimum height.

Preferred future UI-only polish:

- Replace the literal characters with native `FontIcon` content using `FontFamily = new FontFamily("Segoe Fluent Icons")`.
- **Move Earlier / Up: glyph `"\uE74A"` (`Up`)**.
- **Move Later / Down: glyph `"\uE74B"` (`Down`)**.
- Suggested icon `FontSize = 20` DIP and tab-order-only minimum button height about 30–32 DIP, retaining `QamValueButtonStyle` for color/theme/disabled semantics; do **not** enlarge all `QamValueButtonStyle` users.
- Alternative lighter chevron pair: `"\uE70E"` / `"\uE70D"` (`ChevronUp` / `ChevronDown`).
- Keep the existing `-1` / `+1` move meaning, disabled-at-ends behavior, `AutomationProperties.Name` and ToolTip strings. No extra reordering state.
- This note is **not** authorization to modify icon code in PR-A; the user asked for recommendations, and polish may be a small separate PR after deciding the glyph.

Glyphs documented in Microsoft's Segoe Fluent Icons catalog:
https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-fluent-icons-font
