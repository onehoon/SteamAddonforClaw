# Work Order — B1: Main App Quick Overlay Tab Order Editor + In-Overlay Move Button Glyph Polish

**Date:** 2026-10-24
**Status:** Ready for local Codex
**Repository:** `onehoon/SteamAddonforClaw`
**Reviewed baseline:** `main@9e18978a74b0712ab5848de7cf866de2b906e946` (PR #872 merged)
**Delivery:** ONE focused PR (B1), independent of Y-button implementation (B2)
**Product:** Standalone Full PID1902, single Windows user / one interactive session
**Responsibility:** Local Codex implements code and automated tests. Physical MSI Claw and visual/in-game verification belong to the user **after merge**, never a Codex, CI, or PR blocker.

## 0. Frozen product decision

Complete **Quick Overlay Tab Order** parity between the Main App and the in-game Overlay Setting tab.

1. **Main App → Quick Overlay:** add a native WinUI 3 `SettingsExpander` called **Quick Overlay Tab Order**, with `IsExpanded="False"` initially.
2. Expanding reveals **exactly five tab-order rows** (Shortcut, Device, Controller, Setting, Profile in the currently persisted order; these are identities, not hard-coded positions). Each row has an **Up** and **Down** button, and the first/last unavailable directions are disabled based on the authoritative snapshot.
3. **In-game Quick Settings Overlay → Setting tab:** rename the existing **Tab Order** card header to **Quick Overlay Tab Order** and replace only its existing move-button contents `◂` / `▸` with visible **Segoe Fluent Icons Up (U+E74A) / Down (U+E74B)**. Keep the card's existing expand/collapse and interaction behavior. Do **not** change the left tab rail, LB/RB hints, or tab navigation glyphs.
4. The Main App and in-game Overlay must display the **same persisted, Runtime-authoritative order** and apply the same one-position move semantics.
5. **Y Button Quick Jump is not implemented, not mocked and not a placeholder in B1.** Its future option card will be **first** on the Main App Quick Overlay page in B2. For now, preserve the existing Power Source card first and append the new Tab Order expander second. The eventual B2 order is `Y Button Quick Jump → Show only current power source → Quick Overlay Tab Order`.

### Visually intended Main App state for B1

```text
Quick Overlay

[ Show only current power source                     [toggle] ]

[ Quick Overlay Tab Order                                [v] ]
  Set the tab order and choose which tab opens first.
  (initially collapsed)

After user expands:
  Shortcut                                      [↑] [↓]
  Device                                        [↑] [↓]
  Controller                                    [↑] [↓]
  Setting                                       [↑] [↓]
  Profile                                       [↑] [↓]
```

The arrows represent move **earlier / later** in the persisted vertical tab order, not navigation between tabs. The tab now at position zero is the first tab displayed on the **next** Overlay Show, per PR #872.

## 1. Mandatory reading and code scope

Read the active document hierarchy first, respecting the standalone, non-CTW architecture:

- `docs/Full 1902 Implementation/README.md` and the precedence chain it declares, especially `FULL_1902_IMPLEMENTATION_ARCHITECTURE.md` and `FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`.
- `docs/overlayui/README.md`, `ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`, and `ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`. Existing Steam/QAM integration proposals are historical, **not** the current product.
- `docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR3_SETTING_TAB_ORDER_PARITY_WORK_ORDER.md` for typed tab-order owner, projections, move results and boundaries (QAM-specific historical sections are retired).
- `docs/work-order/2026-10-24_MAIN_UI_QUICK_OVERLAY_PAGE_POWER_SOURCE_CARD_MOVE_WORK_ORDER.md` (PR #871 page layout/authority).
- `docs/work-order/2026-10-24_OVERLAY_SHOW_FIRST_CONFIGURED_TAB_PR_A_WORK_ORDER.md` (PR #872: Show always selects order[0]).

Read current implementation:

- `src/SteamInputAddonforClaw.UI/Views/QuickOverlayPage.xaml` and `QuickOverlayPage.xaml.cs`.
- `src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs` (Quick Overlay lazy creation, Activate/RequestRefresh and invalidation).
- `src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw` and `ko-KR/Resources.resw`.
- `src/SteamInputAddonforClaw.Overlay/OverlayTabOrderRow.cs`, `OverlayWindow.ClawHud.cs`, `OverlayWindow.Shell.cs`, `Themes/QamOverlayResources.xaml`.
- `src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsShellContracts.cs`, `AddonQuickSettingsTabOrderContracts.cs` and `FrontendContracts.cs`.
- `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs` (the existing capture/move authority).
- `src/SteamInputAddonforClaw/Settings/AppSettings.cs` and `StartupSettingsCoordinator.cs` (READ ONLY: persistence already exists).

Review existing UI tests:
- `tests/SteamInputAddonforClaw.UiTests/QuickOverlayPageTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/OptionCardIconContractTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/OverlayTabOrderEditorTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/OverlayQamVisualResourcesTests.cs`
- `tests/SteamInputAddonforClaw.Tests/OverlayTabOrderContractTests.cs` and any existing frontend capture/mutation transport tests.

## 2. Verified implementation facts

### Existing shared Runtime contract

Already implemented:

```csharp
Task<AddonQuickSettingsTabOrderSnapshot> CaptureAddonQuickSettingsTabOrderAsync(
    CancellationToken cancellationToken = default);

Task<AddonQuickSettingsTabOrderMutationResult> MoveAddonQuickSettingsTabAsync(
    AddonQuickSettingsTabOrderMoveIntent intent,
    CancellationToken cancellationToken = default);
```

The shared types already encode exactly the data the Main App needs:

```csharp
public sealed record AddonQuickSettingsTabOrderRow(
    AddonQuickSettingsTabId TabId, string Label,
    bool CanMoveEarlier, bool CanMoveLater);

public sealed record AddonQuickSettingsTabOrderSnapshot(
    bool Available, IReadOnlyList<AddonQuickSettingsTabOrderRow> Rows);

public sealed record AddonQuickSettingsTabOrderMoveIntent(
    AddonQuickSettingsTabId TabId, int Delta);

public sealed record AddonQuickSettingsTabOrderMutationResult(
    bool Succeeded, string? FailureMessage,
    AddonQuickSettingsTabOrderSnapshot State);
```

`AddonQuickSettingsTabOrderProduct` already validates the fixed five-tab order and the adjacent-swap intent; the Runtime owns validation, persistence, and final move results. In `InProcessAddonFrontendControl`, moving returns `Succeeded` plus authoritative `State`, including on rejection/failure. A successful change triggers the existing `StateInvalidated` publication.

**Do not add another settings key, another editor-owned order persistence path, a whole-order replace mutation, another transport schema/RPC, or another owner.** Main UI should directly call these existing typed frontend methods.

### Main App current state

`QuickOverlayPage.xaml` currently consists of the page title and **one** `SettingsCard`: "Show only current power source", with native battery glyph E85E. `QuickOverlayPage.xaml.cs` has the existing startup-bootstrap initialization, on-activation `RequestRefresh()`, typed power-source write and fallback logic. The `MainWindow` creates this page lazily and invokes `Activate` when navigating to it; the already-installed invalidation callback calls `RequestRefresh` only while Quick Overlay is the active page.

**Extend this page, not SettingsPage and not the Main Window navigation topology.**

### In-game Overlay current state

`OverlayWindow.ClawHud.cs` has a Setting-page card expansion model with `SettingCardId.TabOrder` and current title "Tab Order"; it composes the existing `AddonQuickSettingsTabOrderRow` rows from `OverlayWindow.Shell.cs`. Their move buttons currently use the tiny literal strings `"◂"` and `"▸"`, passed into the shared `QamValueButtonStyle` (global min-height **22 DIP**). The shared style is also used by numerous Device/Profile/Controller numeric/value buttons.

**Only the existing Setting → Tab Order move buttons are being changed.** The game's left-side tab rail, LB/RB indicator arrows, row up/down selection, navigation, and all other numeric buttons must remain unchanged.

Microsoft's native Segoe Fluent Icons glyph catalog documents:
- Up: `E74A`
- Down: `E74B`
https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-fluent-icons-font

## 3. Main App XAML layout and native UX

Modify `QuickOverlayPage.xaml`, preserving its current title and power-source card exactly.

Append one standard Community Toolkit `SettingsExpander` with:

- `x:Name="QuickOverlayTabOrderExpander"`
- `x:Uid="QuickOverlay_TabOrderExpander"`
- `Header="Quick Overlay Tab Order"`, **exact title**, not "Tab Order" alone.
- `Description="Set the tab order and which tab opens first."` or similarly short approved copy.
- `IsExpanded="False"`.
- HeaderIcon: one native icon, recommend existing `<SymbolIcon Symbol="List" />` to represent an ordered group. Do not import external images or styles.
- Five rows **inside** the expander (not five free-floating cards on the page, and not a nested independent expander). Use native `SettingsCard` children, one per tab identity.

### Suggested row layout

```text
Quick Overlay Tab Order                      [v]
  ├─ Shortcut                               [↑] [↓]
  ├─ Device                                 [↑] [↓]
  ├─ Controller                             [↑] [↓]
  ├─ Setting                                [↑] [↓]
  └─ Profile                                [↑] [↓]
```

Rows must be **rendered/reordered according to the authoritative snapshot's `Rows`**, not initial enum order or hard-coded English labels. The first/last available directions must be derived from `CanMoveEarlier`/`CanMoveLater` provided by Runtime.

Render each row as a narrow standard `SettingsCard` with its `Header` set to `row.Label` and its content a small horizontal pair of move buttons. No need to add five decorative icons or five position labels: the vertical sequence itself expresses the order. If useful, the first row can have a subtle non-interactive "Opens first" sublabel; this is optional, not worth special presentation infrastructure.

Use the native `SettingsExpander.Items` collection, which is an `IList<object>`, and is already used elsewhere in the Main App. **A minimal code-behind approach is preferred:** one cached native row/card per fixed tab ID with its two button controls, reorder these existing cards in `QuickOverlayTabOrderExpander.Items` when an authoritative snapshot changes. Keep a small page-local mapping if needed; do not create a reusable tab-order editor framework or ViewModel.

Community Toolkit docs:
https://learn.microsoft.com/en-us/dotnet/api/communitytoolkit.winui.controls.settingsexpander.items
https://learn.microsoft.com/en-us/dotnet/communitytoolkit/windows/settingscontrols/settingsexpander

**Implementation escape hatch:** If the project's current SettingsControls version behaves better with the built-in `ItemsSource` and `ItemTemplate`, use one local collection bound to the existing `AddonQuickSettingsTabOrderRow` and a plain `SettingsCard` template instead. Either approach is supported by the control; choose the **simplest one that builds and passes tests**. Do not combine both approaches or introduce a separate model/manager solely for five rows.

### Button visual contract — shared design with the in-game editor

Each Main App move control:

- Native `FontIcon`, `FontFamily="Segoe Fluent Icons"`
- Earlier/up: `Glyph="&#xE74A;"` (or code equivalent `"\uE74A"`)
- Later/down: `Glyph="&#xE74B;"`
- `FontSize="20"` DIP
- Button **40 DIP width and 32 DIP height**, or equivalent minimum dimensions; preserve existing Main App Fluent button hover/disabled treatment.
- Two individual keyboard/pointer-operable buttons per row, not a single draggable item and no custom touch gesture.
- Meaningful `AutomationProperties.Name` and tooltips ("Move Shortcut up", "Move Shortcut down" etc.), preferably using existing resw localization format for screen-reader strings rather than showing extra labels.
- Top row's Up disabled; bottom row's Down disabled; never disable the opposite side.
- If state is unavailable/pending, disable actions rather than writing guesses.
- Button closure/Tag must be bound to **TabId** rather than numeric index, so moving and rerendering cannot execute against a stale index.
- Disable all move buttons during **one** Main App move request. No double-submission or optimistic local order; use authoritative result.

The header of the expanded Main App page remains native WinUI 3 with default theme/DPI; do not port the in-game `QamValueButtonStyle` into Main UI or create a shared visual control across processes.

## 4. Main App settings/async logic (one Runtime owner)

### 4.1 Initialization, capture, and refresh

Keep the existing `QuickOverlayPage.Initialize(FrontendBootstrapSnapshot, IAddonFrontendControl)` and power-source behavior unchanged.

The bootstrap does **not** carry the tab-order projection; **do not add it to bootstrap**. On initial page `Activate()` and each subsequent visible-page `RequestRefresh()`, run a best-effort independent capture:

```csharp
var snapshot = await _frontend.CaptureAddonQuickSettingsTabOrderAsync()
    .ConfigureAwait(true);
ApplyAuthoritativeTabOrder(snapshot);
```

It is acceptable to keep the existing power-source refresh method intact and add one focused `RefreshTabOrderAsync()` call inside the page's existing `RequestRefresh()`.

Requirements:
- Never capture tab order during the MainWindow constructor or warmup; the Quick Overlay page remains lazy (as in PR #871).
- Do not fetch/reinitialize the Main App page merely for tab-order updates.
- Preserve existing collapse state on refresh and after moves; `IsExpanded=False` means initial state only.
- On unavailable snapshot or thrown capture: leave the expander visible but its move rows disabled or present a compact unavailable state. Do not replace the failed read with a fabricated default order and allow a write.
- Do not re-query Runtime in a loop or poll. Navigation and existing invalidation are sufficient.
- Do not overwrite the just-completed authoritative mutation response with an older, already-started refresh. A small page-local pending-move / refresh gate is sufficient; **no additional synchronization, epoch, queue, manager, or generalized state machine**.

### 4.2 Single-position move

After clicking the Up/Down button for a named tab ID:

```csharp
var result = await _frontend.MoveAddonQuickSettingsTabAsync(
    new AddonQuickSettingsTabOrderMoveIntent(tabId, delta))
    .ConfigureAwait(true);
ApplyAuthoritativeTabOrder(result.State);
```

Required behavior:
1. Guard `_frontend != null`, currently available authoritative snapshot, `!_tabOrderMoveInProgress`, and the specific `row.CanMoveEarlier/CanMoveLater`.
2. Disable the move controls while the write is pending; do not optimistically rearrange on click.
3. Rebuild/reorder the visible five rows **only from the returned authoritative `State`**, whether `Succeeded=true` or a rejected operation returning a usable current state.
4. For rejection, log `FailureMessage` (if supplied), present the returned state and re-enable appropriately. Do not present the move as successful.
5. For an exception (RPC or persistence interruption), log and request one existing capture for current order, or retain the last confirmed state if the readback also fails. Never invent a success state.
6. Keep the native expander's open/closed state and avoid rebinding the expander itself on each update.
7. The underlying `MoveAddonQuickSettingsTabAsync` already fires `StateInvalidated` on success. If this callback requests a page refresh during the same mutation, the local in-flight guard should prevent an older refresh from overwriting the final move result; no new MainWindow subscription is needed.
8. A change made in-game becomes visible in Main App on next Quick Overlay activation/refresh. A Main App change becomes visible in-game through the current pre-Ready tab-order state and normal authoritative Overlay refresh.

The existing persisted settings remain the only source of truth for Overlay's next Show, including customized order[0].

### 4.3 No unrelated power-source regressions

Current `QuickOverlayPage` has a separate power-source mutation guard/rollback. Keep its success/failure/readback logic and existing `RequestRefresh()` contract working. Do not tie power-source writes to tab-order writes in a combined settings request or block one simply because the other has an unrelated refresh.

## 5. In-game Overlay Setting card: exact title and arrows

Change only:

- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs` — current call
  `CreateSettingCard(SettingCardId.TabOrder, "Tab Order", tabOrderDetails)`
  becomes `CreateSettingCard(SettingCardId.TabOrder, "Quick Overlay Tab Order", tabOrderDetails)`.
- `src/SteamInputAddonforClaw.Overlay/OverlayTabOrderRow.cs` — change current two character buttons to native glyph buttons.

Suggested minimal implementation shape:

```csharp
// Add Microsoft.UI.Xaml.Media for FontFamily.
_moveEarlier = CreateMoveButton("\uE74A", "Move earlier", -1);
_moveLater = CreateMoveButton("\uE74B", "Move later", +1);

private Button CreateMoveButton(string glyph, string accessibleName, int delta)
{
    var button = new Button
    {
        Content = new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            Glyph = glyph,
            FontSize = 20,
        },
        Style = OverlayQamResources.Style("QamValueButtonStyle"),
        MinWidth = OverlayQamResources.Get("QamValueButtonWidth", 40.0),
        MinHeight = 32,
        Padding = new Thickness(0),
    };
    AutomationProperties.SetName(button, accessibleName);
    ToolTipService.SetToolTip(button, accessibleName);
    button.Click += (_, _) => _requestMove(delta);
    return button;
}
```

The example specifies *shape and sizes*; adapt to the WinUI type references that actually compile. The only deliberate size override is **on these two buttons**. Do NOT alter `QamValueButtonStyle`, `QamValueButtonWidth`, `QamValueButtonHeight` (currently 40/22), or other shared themes/controls. In particular:
- Slider/numeric stepper/Controller/Device/Profile buttons keep their current sizes.
- Up/Down in the tab order editor still send `-1/+1` only.
- Boundary disablement, pending mutation behavior, `OverlayRowCapabilities.Adjust` on controller Left/Right, pointer click semantics and tab-order authoritative result handling stay exactly as they are.
- The Setting card still collapses/expands and retains row selection across Runtime order refresh.
- Do not modify actual Overlay vertical row navigation icons, LB/RB rail or bumper icons, tab labels, tab IDs, or selected-tab logic.

## 6. Localization, tests, and documentation

### 6.1 Main App en-US / ko-KR

Extend both `Resources.resw` files via the current `x:Uid` pattern. Suggested keys and values:

| Key | en-US | ko-KR |
| --- | --- | --- |
| `QuickOverlay_TabOrderExpander.Header` | Quick Overlay Tab Order | 퀵 오버레이 탭 순서 |
| `QuickOverlay_TabOrderExpander.Description` | Set the tab order and which tab opens first. | 퀵 오버레이 탭 순서와 처음 열리는 탭을 설정합니다. |

Other dynamic screen-reader texts may use appropriately named `MainUiStrings.Get` keys. The **in-game** Overlay Setting card name is exactly **Quick Overlay Tab Order** in English; in-game Overlay currently uses literal English text for its existing Setting controls, so do **not** start a full Overlay localization system just for this rename. Shared tab identity labels are the existing native `AddonQuickSettingsShellContract.LabelFor` (Device/Profile/Controller/Shortcut/Setting) and remain unchanged in this PR.

Header icon for the Main App expander may be `SymbolIcon Symbol="List"`; do not add a decorative icon to each fixed tab row.

### 6.2 UI tests

**Update the existing exact-count/UID source-contract tests rather than deleting their old assertions:**

- `QuickOverlayPageTests`: page stays lazy; on activation/revisit requests the authoritative tab-order snapshot; pending move uses the typed `AddonQuickSettingsTabOrderMoveIntent(tabId, delta)`, disables duplicate clicks, waits for authoritative response and uses its `State`; failed mutation recovers without optimistic order.
- `MainUiLocalizationResourceTests`: currently asserts the page has exactly one `SettingsCard`, one `ToggleSwitch` and exactly two x:Uid values. Update to assert the additional single `SettingsExpander` and new UIDs while retaining existing Power Source ownership/localization checks. Account for dynamic row construction vs XAML static rows according to chosen implementation.
- `OptionCardIconContractTests`: currently asserts exactly **40** cross-page icon-bearing cards and expects only the Power Source card on QuickOverlayPage. Add the new `Quick Overlay Tab Order` expander/icon entry and update the count to **41** while preserving the other 40 expected icons. Do not weaken the cross-page map.
- Add focused Main App tests that verify the displayed fixed set always follows authoritative snapshot order, boundary disabled states correspond to Runtime flags, and move intents address the correct **TabId** after reorder, not former indexes. Use the shared `AddonQuickSettingsTabOrderProduct` tests for concrete behavior; no giant UI automation harness needed.

**Overlay icon/card tests:**
- `OverlayDeviceRendererWiringTests`, `OverlayQamVisualResourcesTests`, `OverlayTabOrderEditorTests`: assert the tab-order Setting card header is exactly `Quick Overlay Tab Order`, the two glyphs are E74A/E74B and use `Segoe Fluent Icons` with 20 DIP size and tab-order-only 32 DIP height.
- Preserve existing end-of-order disablement, `-1/+1`, controller adjustment and selected-row identity tests.
- Assert shared `QamValueButtonStyle` resources remain 40 DIP wide/22 DIP min-height and unrelated `OverlayValueRow` buttons remain unchanged.

**Contract/transport tests:**
- Existing shared product tests and Main UI transport tests should confirm only one-position moves with authoritative mutation response and no new RPC/settings key.
- Do not require connected MSI Claw hardware for tests.

**README/help updates:**
- Update `README.md` and `docs/howtouse/README_KO.md` to describe Main App Quick Overlay's tab-order editor, its collapsed default, and its one-position Up/Down buttons. Document that **the first ordered tab opens next time** and that the in-game `Setting` page exposes the same order controls.
- Do not document any Y button Quick Jump feature yet.

### 6.3 Suggested commands (adapt to repo CI if different)

```powershell
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Debug
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Release
dotnet build SteamInputAddonforClaw.slnx -c Release
git diff --check
```

Real-device, on-screen icon scale and actual overlay UX validation is explicitly the **user's role after merge**, not required for local Codex/CI/PR approval.

## 7. PR boundaries / overengineering review

**Likely production file set:**
- `src/SteamInputAddonforClaw.UI/Views/QuickOverlayPage.xaml`
- `src/SteamInputAddonforClaw.UI/Views/QuickOverlayPage.xaml.cs`
- `src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw`
- `src/SteamInputAddonforClaw.UI/Strings/ko-KR/Resources.resw`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayTabOrderRow.cs`
- Focused tests and two help documents above.

**No changes to:** Runtime settings store/coordinator, contracts/DTOs, frontend named pipe, Overlay pipe/protocol, PID1901 ↔ PID1902 routing, HidHide/VIIPER ownership, physical controller capture and release, sleep/hibernate/resume, startup/shutdown/crash recovery, game/session detection, GPU/profile settings, overlay rail navigation, shortcut mapping, Y input, or QAM.

Do not add test-only synchronization, generic editor abstractions, duplicated client-side validation authorities, per-tab persistence, retry loops, or broad UI refactors. Respect practical lifecycle failures, but do not block this UI-only PR for theoretical interleavings.

## 8. Acceptance checklist

- [ ] Main App Quick Overlay has the existing power-source card followed by a **Quick Overlay Tab Order** `SettingsExpander`, initially collapsed.
- [ ] Expanding shows exactly the Runtime-authored 5 tab identities in persisted order, each with native Up/Down buttons.
- [ ] Main App Up/Down uses E74A/E74B, 20 DIP native FontIcon, 40x32 DIP controls; correct boundary disablement and accessible names.
- [ ] Moves go through existing typed `MoveAddonQuickSettingsTabAsync(new AddonQuickSettingsTabOrderMoveIntent(tabId, ±1))` only, and render authoritative readback on success/rejection.
- [ ] Pending failure/readback behaves safely without optimistic reorder, duplicate writes, unhandled exception or stale UI.
- [ ] Main App correctly reloads authoritative order on visit/revisit; the active expander does not unexpectedly collapse on refresh.
- [ ] In-game **Setting → Quick Overlay Tab Order** has the exact updated header and Up/Down glyphs, replacing `◂/▸`.
- [ ] In-game arrow visual scale is 20 DIP glyph / 32 DIP button height **only for tab-order move buttons**; other numeric/stepper buttons unaffected.
- [ ] In-game Overlay existing tab-order mutation, selection preservation, tab rail, LB/RB and input/capture semantics are unchanged.
- [ ] PR #872 start-tab policy remains unchanged: next Show selects the newly saved order[0].
- [ ] Both UI surfaces use the same single Runtime tab-order setting and typed move API; no new protocol/authority.
- [ ] Both locales, focused UI/contract tests, docs and Release build pass.
- [ ] Y Button Quick Jump is deferred to B2; no stub/placeholder/Y mapping in B1.
- [ ] Physical testing is user-owned post-merge, never a PR blocker.

**B2 handoff note (no implementation in B1):** The user requires **Y Button Quick Jump as the FIRST Main App Quick Overlay card**. B2 will add that new card above the existing Power Source card; the Quick Overlay Tab Order expander then becomes third. In-game, Y selection will be editable from Setting → Quick Settings with existing controller-oriented row conventions.
