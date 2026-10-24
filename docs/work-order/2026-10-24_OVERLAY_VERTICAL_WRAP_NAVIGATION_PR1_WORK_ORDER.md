# Work Order — PR1: Overlay Vertical Navigation Wrap

**Date:** 2026-10-24  
**Status:** Ready for local Codex implementation  
**Repository:** `onehoon/SteamAddonforClaw`  
**Target branch:** `main`  
**Reviewed code baseline:** `cb50225fea9b18c06781d95acb89555725189dfe` (post-PR #868 and #869)  
**Delivery:** One small, standalone code PR  
**Product:** Standalone Full PID1902 Steam Addon for Claw; no CTW/HHC integration

## 0. Goal and frozen user decision

Make **vertical logical selection wrap at the top/bottom** of the in-game Addon Quick Settings Overlay, matching the already-cyclic LB/RB tab navigation.

This is **PR1 only**, entirely separate from proposed future Y-button tab shortcuts, main-app Quick Overlay settings, or reserved X/LT/RT controls.

### Required behavior

| Input | Outcome |
| --- | --- |
| D-pad Up, left-stick Up, right-stick Up | Previous selectable Overlay row; from the first selectable row, jump to the last selectable row |
| D-pad Down, left-stick Down, right-stick Down | Next selectable Overlay row; from the last selectable row, jump to the first selectable row |
| Shortcut grid Up/Down | Keep current row/column behavior, adding first-row ↔ last-row vertical wrap; never select an empty cell |
| D-pad / either-stick Left/Right | **Unchanged**: row adjustment, or Shortcut horizontal tile movement; **no horizontal wrap** |
| LB/RB | **Unchanged**: existing tab-order wrap |
| A/B | **Unchanged**: activate/Back semantics |
| X/Y/LT/RT | **Unchanged**: reserved/unmapped for Overlay navigation |

**D-pad and both analog sticks intentionally share the same vertical wrap behavior. Do not add physical-input-source discrimination.** They already arrive as identical semantic `NavigateUp` / `NavigateDown` actions.

Do not add an opt-in toggle, new preference, configuration file field, extra button action, page scroll command, or persistent selection history. Wrapping is a fixed Overlay-local navigation behavior.

## 1. Mandatory reading and current-source gate

Before modifying code, review the **then-current main**; the baseline SHA above identifies the code used to prepare this document, not a license to overwrite later changes.

### Product / Overlay authority

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/overlayui/README.md`
- `docs/overlayui/OQ5_UI_03_DUAL_STICK_DIRECTIONAL_NAVIGATION_WORK_ORDER.md`
- `docs/overlayui/OQ5_UI_04_LOGICAL_ROW_SELECTION_SCROLLING_WORK_ORDER.md`
- `docs/work-order/2026-10-23_OVERLAY_DPAD_CARDINAL_ONLY_AND_NUMERIC_SLIDER_HOLD_REPEAT_WORK_ORDER.md`
- `docs/work-order/2026-10-24_OVERLAY_ALL_SETTINGS_MUTATION_PREVIEW_AND_SELECTION_STABILITY_WORK_ORDER.md`

The earlier OQ5-UI-04 document deliberately specified **bounded/no-wrap** vertical movement. **This work order supersedes only that vertical-boundary rule** for the current implementation; retain all its other architectural and selection constraints. Some older Overlay planning documents also describe outdated protocol/tab/Shortcut details: current `main` implementation is authoritative for those details.

### Implementation files to inspect

- `src/SteamInputAddonforClaw.Overlay/OverlayRowSelection.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayShortcutSelection.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shortcuts.cs`
- `src/SteamInputAddonforClaw/Lifecycle/OverlayControllerInputRouter.cs` (**read-only review; no intended edit**)
- `src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs` (**read-only review; no intended edit**)
- `src/SteamInputAddonforClaw.Overlay/App.xaml.cs` (**read-only review; no intended edit**)

### Existing tests

- `tests/SteamInputAddonforClaw.UiTests/OverlayRowSelectionTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/OverlayShortcutSelectionTests.cs`
- Review any additional currently existing tests that assert top/bottom boundaries, row-availability normalization, logical selection/scroll, or the Shortcut grid.

## 2. Verified current code facts

1. `OverlayControllerInputRouter` emits existing semantic `NavigateUp/Down/Left/Right` actions for D-pad and **both** sticks. D-pad Up/Down are single-shot per fresh cardinal gesture; a stick emits one action per neutral-to-deflected gesture. Only D-pad **Left/Right** have slider-specific hold-repeat actions. **Nothing in the router needs to change.**
2. `OverlayWindow.Navigation.NavigateUp/Down` already branches to the two existing selection models:
   - ordinary Device/Profile/Controller/Setting pages: `OverlayRowSelection.MovePrevious/MoveNext`;
   - Shortcut page: `OverlayShortcutSelection.MoveUp/MoveDown`.
3. Ordinary pages already refresh the selected-row visual and call `BringSelectedRowIntoView()` **only on changed selection**, via `RefreshRowSelectionAfterMove()`. Shortcut similarly calls `ApplyShortcutSelectionVisual(bringIntoView: true)` only on changed selection.
4. `OverlayRowSelection.Move` currently traverses `IsSelectable` rows linearly, calls `NormalizeSelection`, and **stops** when `FirstSelectableFrom` finds no candidate. `SetRows`, pointer selection, activation/adjustment, and dynamic availability are distinct behaviors that should be preserved.
5. Shortcut selection is a distinct `OverlayShortcutSelection` with currently **three visual columns** (`ShortcutVisualColumnCount = 3`) and a variable number of tiles. `TryMoveVertical` already computes the destination row and clamps a missing column in a partially filled final row to its last real tile. Horizontal movement remains bounded within the current row.
6. `OverlayTabState.SelectPrevious/SelectNext` already wraps via the persisted current tab order. There is no reason to touch that code.

These are frontend selection semantics, not PID1902/HidHide/VIIPER, capture/release, controller routing, or feature-mutation changes.

## 3. Implementation A — ordinary row selection

**Primary edit:** `src/SteamInputAddonforClaw.Overlay/OverlayRowSelection.cs`.

Change the implementation-local `MovePrevious()` / `MoveNext()` traversal so that, **only when a valid selected row has reached the end in the requested direction**, it searches from the opposite end for the next `IsSelectable()` row.

Conceptual minimal change in the existing `Move(int direction)`:

```csharp
// Conceptual shape only; preserve the existing NormalizeSelection behavior.
var next = FirstSelectableFrom(start, direction);
if (next is null)
{
    next = FirstSelectableFrom(
        direction < 0 ? _rows.Count - 1 : 0,
        direction);
}
// Move only if the destination is a different selectable row.
```

Follow actual source semantics rather than copying this fragment mechanically.

### Exact invariants

- **First selectable + Up** → last selectable row.
- **Last selectable + Down** → first selectable row.
- From the middle, retain existing one-step movement and skip rows whose `IsSelectable()` is false.
- Wrap to the **last/first currently selectable** row, not blindly the first/last stored array index. This includes collapsed Setting child controls, genuinely unavailable Controller controls, absent Profile controls, and transiently disabled rows according to their existing capabilities.
- A page with **zero selectable rows** retains `SelectedIndex = null` and returns no movement.
- A page with **exactly one selectable row** leaves it selected and returns `false` on Up/Down; do not request a redundant visual refresh or scroll.
- Do not alter `SetRows` preferred-index restoration, pointer selection, mutation-busy navigation policy, `NormalizeSelection`, `ActivateSelected`, or `AdjustSelected`. In particular, if a formerly selected row has become unavailable, preserve the existing normalization/fail-safe behavior rather than introducing wrapping of stale/unselectable targets.
- The method's return value must represent an observable selection change, so existing selection-visual/scroll wiring remains correct.
- Update the now-stale `OverlayRowSelection` comment saying `bounded / no-wrap`. Do not remove the bound/availability checks themselves.

Prefer a tiny edit of `Move` and reuse `FirstSelectableFrom`. Do **not** add new selection infrastructure, a second authority, stored per-tab positions, or wrap configuration.

## 4. Implementation B — Shortcut grid vertical wrap

**Primary edit:** `src/SteamInputAddonforClaw.Overlay/OverlayShortcutSelection.cs`.

Modify **only** the vertical movement policy (`MoveUp/MoveDown` / `TryMoveVertical`). Keep `Configure`, `Select`, `Reset`, `FindTileIndex`, and horizontal geometry intact.

- While a **real different row** exists in the requested direction, use the same destination-row and destination-column logic as today.
- **Up from first populated row**: choose the last populated row, preserving the selected column when that cell exists.
- **Down from last populated row**: choose the first row, preserving the selected column (the first row is full when more than one row exists).
- If the destination is a **short final row** and the desired column does not exist, use the existing last-tile-of-that-row clamp. Never select a nonexistent cell.
- **Zero tiles**: no selection/move.
- **Only one populated row** (1–3 tiles with current three-column layout): Up/Down are no-ops. Do not move sideways within that same row merely to manufacture wrap.
- **Horizontal Left/Right remain bounded within the row**. No left/right wrap, no cross-row horizontal movement.
- Retain existing runtime tile identity/reconciliation and the current `ApplyShortcutSelectionVisual(bringIntoView: true)` path. Do not add scroll calculations or re-render every tile on navigation.

### Concrete Shortcut cases for regression tests

With 3 columns, use the current dynamic tile count:

```text
6 tiles:
  0 1 2
  3 4 5
  Up from 1    -> 4
  Down from 4  -> 1
  Down from 5  -> 2

5 tiles (short final row):
  0 1 2
  3 4
  Up from 2    -> 4   (clamp missing last-row column 2)
  Down from 4  -> 1   (keep current column 1)
  Down from 3  -> 0

4 tiles:
  0 1 2
  3
  Up from 2    -> 3   (clamp)
  Down from 3  -> 0

1–3 tiles: one row only; Up/Down make no change
0 tiles: no selection
```

Do not add a more complex remembered-column/cursor system to make a wrap round trip restore an originally missing column; use the **currently selected** tile column each time, just like ordinary vertical movement already does.

## 5. Integration and unrelated behavior must stay unchanged

Expected existing path:

```text
physical D-pad Up/Down OR left/right stick Up/Down
  -> OverlayControllerInputRouter
  -> existing NavigateUp/Down
  -> existing Overlay named-pipe navigation message
  -> App.HandleNavigationAsync UI dispatcher
  -> OverlayWindow.NavigateUp/Down
  -> existing selected-row / Shortcut selection model (now wraps vertically)
  -> existing changed-only highlight + bring-into-view
```

**Expected changed production files are the two selection models only.** If implementation evidence shows that a tiny local UI wiring correction is necessary, justify it in the PR; otherwise leave these untouched:

- `OverlayControllerInputRouter` and physical input/readback.
- `OverlayWire`, protocol version, IPC DTOs, server/client.
- `App.HandleNavigationAsync`, tab selection, and horizontal `AdjustSelectedRow` / numeric slider repeat.
- OQ4 neutral game-facing presentation, consumed-control release, hide/dismiss, startup/resume/shutdown and fail-close.
- Full1902 PID1901/PID1902 ownership, HidHide, VIIPER, Steam/Deck/Edge attachment.
- Main UI, ClawHUD, Device/Profile/Controller/Shortcut feature mutations, Settings persistence, tab order.
- Y-button target-tab feature; reserved X/LT/RT; L3/R3; front buttons; M1/M2.

Only valid **vertical navigation gestures** gain wrap. Do not implement held Up/Down repeating, acceleration, alternate stick gestures, shortcuts, or a settings toggle.

## 6. Automated tests (hardware-free)

Update existing test files rather than creating a new test harness. Replace tests that explicitly assert **no vertical wrap**; do not simply delete them.

### `OverlayRowSelectionTests`

- First selectable Up wraps to last, and last selectable Down wraps to first; both return `true`.
- Middle Up/Down still step once in the intended direction.
- Disabled/unselectable rows at both ends and between rows are skipped correctly on wrap and normal movement.
- Zero selectable rows → null selection and false Up/Down; one selectable row among several unavailable rows → unchanged and false Up/Down.
- Dynamic `IsSelectable` changes, selected-row normalization, and `SetRows(preferredIndex)` retention still behave as before.
- `ActivateSelected` / `AdjustSelected` still dispatch only to the currently valid target and never implicitly activate/adjust due to vertical navigation.
- Existing controller/ClawHUD pending-mutation and row-identity regression tests remain passing.
- Replace the old `SelectionIsBoundedAndDoesNotWrap` test with explicit wrap tests.

### `OverlayShortcutSelectionTests`

- Full two-row (6-tile) up/down wrap preserving column.
- 4- and 5-tile layouts: short-last-row clamp on Up wrap, correct column on Down wrap.
- 0 tiles and a single row (1–3 tiles) stay put on vertical input.
- Ordinary within-grid vertical movement, horizontal bounded movement, `Configure` re-projection/selection, deleted-tile fallback, and invalid selection remain unchanged.
- Replace old `First_and_last_row_edges_are_bounded` expectations and any other now-obsolete `Assert.False(MoveUp/MoveDown)` edge assertions with the new contract.

### Narrow UI wiring check, if not already covered

Confirm in source/current tests that `OverlayWindow.NavigateUp/Down` continues to call the existing changed-only `RefreshRowSelectionAfterMove` / `ApplyShortcutSelectionVisual(bringIntoView: true)`, with no new unconditional scroll or renderer rebuilding. Prefer existing behavior tests and source checks; do not introduce a fake controller stack or new UI test abstraction just for wrap.

### Suggested local Codex commands

```powershell
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Debug
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release
git diff --check
```

Also run the repository's relevant existing build/CI suite. Fix production-relevant failures caused by this PR; do not broaden scope for unrelated cosmetic warnings.

**The user performs physical MSI Claw/game validation after merge. Do not make hardware access, physical verification, or subjective overlay visual confirmation a Codex completion requirement, CI gate, or PR-review blocker.**

## 7. Acceptance / scope checklist

- [ ] Ordinary Overlay rows wrap from first ↔ last selectable on Up/Down; empty/singleton stay unchanged.
- [ ] Shortcut grid wraps first ↔ last populated row while maintaining current column where possible and clamping a partial final row.
- [ ] D-pad and **both** analog sticks use the same existing vertical semantics.
- [ ] Left/Right value changes and numeric slider hold-repeat remain unchanged; Shortcut horizontal moves stay bounded.
- [ ] LB/RB tab cycling, A/B, tap/pointer selection, settings tab-order moves, row availability and in-flight selection stability remain intact.
- [ ] Changed-only row highlight / scroll-to-selection remains intact.
- [ ] Only two selection-model files and existing focused tests are normally changed; no new setting, IPC, input routing, or controller lifecycle work.
- [ ] Automated code tests and build pass; `git diff --check` passes.
- [ ] No physical-device test is required for PR merge.

## 8. Explicit non-goals

Do not implement PR2 (Y-button configurable jump target), PR3 (Main UI Quick Overlay configuration page), X/LT/RT assignment, faster paging, per-tab selection memory, horizontal wrap, stick-source separation, or future-feature abstractions in this PR. No CTW coupling.

**Implementation handoff:** Local Codex writes code and automated tests in one focused PR. The user owns real handheld verification after the code-correct merge.
