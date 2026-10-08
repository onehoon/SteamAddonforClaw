# Work Order — v0.1.344 Elevated Main-App Shortcut Reorder and Card Shadow Fix

Date: 2026-10-08
Repository: onehoon/SteamAddonforClaw
Reviewed baseline: main at afb8c3d41d2369401d66daea05734e9754db9a01 (PR #725 squash, v0.1.344)
Execution: local Codex; ONE implementation PR, TWO focused commits
Real-device/visual verification: user's responsibility AFTER merge. It is **not** an implementation prerequisite, PR blocker, or required CI test.

**Purpose:** Close exactly two remaining Main App Shortcut regressions: (1) dragging cards never changes their order, and (2) a right/bottom shadow or raised-container artifact is visible around the cards. No other features.

---

## 1. Architectural authority / read before implementation

1. `docs/Full 1902 Implementation/README.md` — current policy precedence.
2. `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`, especially section 3.2. Full1902 is a standalone app: High Runtime launches High Main UI and High Overlay. Do not de-elevate the Main UI or create cross-integrity IPC or broker solely to permit drag/drop.
3. `docs/work-order/1008_V01343_PR724_POSTMERGE_THREE_REGRESSION_FIX_WORK_ORDER.md` — PR #725 predecessor; earlier hypotheses are superseded by the new Microsoft elevated-drag evidence.
4. `docs/work-order/SHORTCUT_EDITOR_3_COLUMN_QAM_TILE_VISUAL_POLISH_ONE_PR_TWO_COMMITS_WORK_ORDER_2026-10-07.md` — preserve three equal columns, row-major order, card dimensions, stacked Edit/Delete, and restrained no-shadow presentation.
5. Review current source and tests listed in sections 4 and 9 below.

Support scope: one administrator Windows user, one interactive session, handheld touchscreen and mouse/pointer input. No CTW integration. Do not touch TDP, CPU Boost, Device initial warnings, controller ownership/routing, PID1901/PID1902, HidHide, VIIPER, WING suppression, startup/elevation, Overlay presentation, other Shortcut actions, protocol, persistence schema, or Medium launch.

## 2. NEW root-cause evidence: native WinUI drag is unsupported by the elevated UI

Log folder: https://drive.google.com/drive/folders/1BHxU_-FJmh5bI4qzGQYnvSZ8f_6XN3Eg

Post-PR #725 v0.1.344:
- `ui-18068.log`: https://drive.google.com/file/d/1y-7LhOTRCq1zhy0cOnIoSuUfzzpipjTP/view
- `ui-15152.log`: https://drive.google.com/file/d/1IB88TKaVF4fULTGMSizxBQJaUKdYXenH/view
- `SteamInputAddonforClaw-2026-10-08-221245.769-P14680-Labc78ee694.log`:
  https://drive.google.com/file/d/14wPmA6GZ_JD7Lq2dkNaUojkIRPNQsdtF/view

Specific observations:
- Across the two UI logs, 25 `Native Shortcut drag started` with `NativeDragStartAccepted=True`.
- 22 `Recovering an abandoned drag` entries. Their local order stayed unchanged (`OrderChanged=False`).
- **Zero `DragItemsCompleted` and zero Runtime Shortcut Move requests**; one authoritative snapshot also superseded a pending drag.
- This is repeatable ordinary user interaction, not a fabricated race. PR #725 addressed *stale state*, not the underlying drag engine.
- The Full1902 privilege policy explicitly keeps Main UI at High integrity.
- Microsoft API docs explicitly state that `UIElement.StartDragAsync(PointerPoint)` is **not supported when the application runs elevated, as administrator**:
  https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.startdragasync
- Microsoft WinUI issue #7690 reports ListView reorder failing in an elevated WinUI 3 app:
  https://github.com/microsoft/microsoft-ui-xaml/issues/7690
- Microsoft also documents that `DragItemsCompleted` is supposed to fire when the native operation ends:
  https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.listviewbase.dragitemscompleted?view=windows-app-sdk-1.8

Conclusion with proper confidence: Elevation incompatibility is a **strong, evidence-backed root-cause match** for why native drag start is observed yet drag completion never arrives. The logs alone do not prove an exact internal WinUI call stack. Nevertheless, repeatedly tuning `DragItemsStarting` or adding stale-drag state recovery cannot establish a supported native drag path in High UI.

**Decision: Replace *only* the Main App Shortcut card reorder input with an in-window pointer gesture, not WinUI/OLE drag-and-drop. Keep the High UI.** It is an ordinary UI pointer gesture that computes source/target and sends the existing authoritative mutation, not a simulated cross-process drag.

Microsoft supported pointer APIs:
- `PointerRoutedEventArgs.GetCurrentPoint(UIElement)`: https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.input.pointerroutedeventargs.getcurrentpoint?view=windows-app-sdk-1.8
- `UIElement.CapturePointer(Pointer)`: https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.capturepointer?view=windows-app-sdk-1.8
- `UIElement.PointerCaptureLost`: https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.pointercapturelost?view=windows-app-sdk-1.8

## 3. Product acceptance

A. Main App Shortcut page (not Overlay editor): user can press/drag a card to another occupied card or an incomplete final row, release, and see the persisted row-major order reflected on Main App and Overlay after the existing Runtime response/next capture.

B. Works while Main UI remains elevated; no native drag/drop dependency. Mouse and actual handheld touch gestures must have appropriate input handling in the implementation. The user, not local Codex, performs hardware touch validation after merge.

C. Short click is not a move. Release over original card is a no-op. Release outside the Shortcut grid is cancel/no mutation. A deliberate single-card move produces exactly ONE existing mutation request. Edit and Delete buttons keep working and MUST NOT arm reorder.

D. Original 3-column grid, card widths, text, icon, margins, light/dark theme, and vertically stacked Edit/Delete remain as currently designed. The unintended right/bottom shadow/raised effect is absent for idle, hover, pressed, and after pointer release; keep the real card border and ordinary 12 DIP card gap.

E. Runtime/ShortcutStore continues to own the authoritative order; no optimistic store writes, duplicate local state authority, new RPC or document migration.

## 4. Exact current implementation defects / inventory

`src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml`:
- `ShortcutList` uses `ListView`, `ItemsWrapGrid Orientation=Horizontal MaximumRowsOrColumns=3`.
- Native path: `CanDragItems=True`, `CanReorderItems=True`, `AllowDrop=True` and `DragItemsStarting`/`DragItemsCompleted`.
- `ListViewItem` uses style setters but no custom template; default `ListViewItemPresenter` was restored in PR #725 specifically to try to repair native drag.
- `DataTemplate` has a card `Border Margin=0,0,12,12`, card fill/stroke, icon and Edit/Delete.
- The card has no explicit `ThemeShadow`. Thus the visible lower/right 'shadow' is not proven to be a `ThemeShadow`. It may be default container press/selection/pointer-over background/animation showing in the card's 12 DIP outer margin. Do not invent a cause as fact.

`src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs`:
- `_pendingDragOriginalTiles`, `_pendingDragTileId`, `_lastPointerDeviceType`;
- `ShortcutList_DragItemsStarting`, `ShortcutList_DragItemsCompleted`;
- `ShouldRecoverAbandonedDrag`, `TryRestoreAndClearPendingShortcutDrag`, `RejectShortcutDragStart`, `ClearPendingShortcutDrag`;
- `TryResolveShortcutMove` and `TryCreateShortcutMoveIntent` currently exist to interpret a native-mutated `_tiles` array.
- `Render` clears/refills `_tiles` from Runtime snapshots, including during a pending drag.
- `GetShortcutItemWidth` / `UpdateShortcutItemWidth` enforce the three-column width.
- `ApplyMutationAsync` already invokes `_frontend.MutateShortcutAsync(intent)` and renders the returned authoritative snapshot.
- `SetBusy` disables the editor when capture/mutation is underway.

`src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs`: `TryBuildMutation` handles `FrontendShortcutMutationKind.Move` using TileId and final zero-based TargetIndex. It already validates bounds, removes/inserts in the canonical order and persists once. **Keep unchanged.**

`src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`: `MutateShortcutAsync` invokes the existing Runtime and signals `StateInvalidated`. **Keep unchanged.**

## 5. Implementation commit 1 — Replace elevated-incompatible native drag with a simple pointer reorder

Proposed commit message: `fix(shortcut-ui): reorder cards with elevated-safe pointer gestures`

### 5.1 Disable and remove unused native drag hooks

In `ShortcutPage.xaml`:
- Remove the `DragItemsStarting` and `DragItemsCompleted` event subscriptions, not merely ignore their callbacks.
- Disable/remove `CanDragItems` / `CanReorderItems` / `AllowDrop` for native ListView DnD; do not call `StartDragAsync`, `DragStarting`, `DropCompleted` or `DataPackage` from the replacement.
- Set `SelectionMode=None` if needed so card clicks are not shadow/selection state authority.
- Preserve `ItemsWrapGrid` and `ItemsSource=_tiles` for 3-column layout. Do not replace layout with a broad custom control, page framework, manager, or drag/drop library.

### 5.2 Input routing: card surface, not Edit/Delete buttons

Attach pointer handlers to a concrete card-surface element in the `DataTemplate` (the existing root card `Border` or an equivalent named surface). Tag/bind the authoritative `TileId` to the surface so handlers can resolve the tile. Do **not** install a blanket handler that intercepts Edit/Delete buttons or other interactive controls.

Expected wiring (adjust names/types for compiling WinUI 3):
~~~xml
<Border
    Tag="{x:Bind TileId}"
    PointerPressed="ShortcutTile_PointerPressed"
    PointerMoved="ShortcutTile_PointerMoved"
    PointerReleased="ShortcutTile_PointerReleased"
    PointerCanceled="ShortcutTile_PointerCanceled"
    PointerCaptureLost="ShortcutTile_PointerCaptureLost"
    ...>
~~~

Protect `Edit` / `Delete`: bubbling PointerPressed originating inside a Button must be ignored, even if WinUI template routing changes; maintain Button click semantics and existing editability. A short click without movement never mutates.

Supported input:
- For mouse, start only with the primary/left button pressed.
- For touch and pen, use pointer-contact semantics; do not require a mouse left-button property for touch.
- Capture **the same pointer ID** from the card surface so movement/release can finish outside that card but inside the grid. Check `CapturePointer(...)` return; if capture cannot be acquired, cancel rather than assuming a gesture.
- Store only the minimal UI-local facts: pointer identifier, originating surface/card TileId, initial grid-relative position, whether moved past an intentional small DIP threshold, and current resolved target. Do not add locks/epochs/barriers/managers or asynchronous dispatch machinery.
- On `PointerCanceled`, `PointerCaptureLost`, deactivate, snapshot rebind, or a disabled/busy editor, clear the gesture and any visual cue without a Move. If releasing explicitly triggers `PointerCaptureLost`, avoid double cleanup/dispatch by clearing state before releasing capture.
- Do not call `_tiles.Move`, `Clear` or `Add` to preview an in-progress drag. Avoid mutating ItemsSource while its pointer/visual hierarchy is active.
- If a Runtime authoritative snapshot replaces cards during the gesture, cancel the local gesture cleanly; do not synthesize a stale move.

### 5.3 Resolve the target geometrically in one coordinate system

- Use `e.GetCurrentPoint(ShortcutList).Position` (DIP / XAML coordinates) and current realized containers, e.g. `ShortcutList.ContainerFromIndex(index) as ListViewItem` with `TransformToVisual(ShortcutList).TransformBounds(...)`.
- Calculate drop target from the visible 3-column card rectangles or their centers in the same coordinate system. **Do not hard-code screen physical pixels** or assume only one row.
- Support existing 3+1 and 3+2 final rows, and first↔last moves (including index 0 and Count-1).
- Ignore/cancel a release outside the valid ShortcutList content area; do not clamp distant outside coordinates to a valid card.
- A nearest-card-center policy is acceptable for ordinary inter-card gaps. If containers are not realized/positioned well enough to resolve a target, cancel (do not guess).
- If a card is pressed and released at its own index, no mutation. Avoid spurious moves if the pointer never passed the drag threshold.
- Maintain 150% DPI correctness naturally via XAML DIP transform, not a fixed 1.5 multiplier.

Recommended small pure-helper boundary:
~~~csharp
internal static int? ResolveShortcutDropIndex(
    Windows.Foundation.Point releasePosition,
    IReadOnlyList<(int Index, Windows.Foundation.Rect Bounds)> realizedCards,
    Windows.Foundation.Rect validDropBounds)
{
    // Reject out-of-bounds, choose a well-defined realized card
    // by intersection / nearest center, return null when unavailable.
}
~~~
Use this only if it materially simplifies tests; do not create a second gesture abstraction for testing.

### 5.4 Dispatch exactly one existing Runtime Move, without optimistic item mutation

- On a **valid release** after threshold, resolve originating TileId and targetIndex against the current `_tiles` order.
- Cancel/clear local pointer and visual state BEFORE awaiting IPC.
- If sourceIndex==targetIndex, do nothing.
- Build the unchanged contract:
~~~csharp
var intent = new FrontendShortcutMutationIntent(
    FrontendShortcutMutationKind.Move,
    TileId: draggedTileId,
    TargetIndex: targetIndex);
await ApplyMutationAsync(intent);
~~~
- Use `ApplyMutationAsync` as the single mutation and authoritative snapshot/feedback path. A failure leaves local state canonical and shows the existing error.
- The source-order interpretation helper `TryResolveShortcutMove` / `TryCreateShortcutMoveIntent` was designed for native WinUI collection mutation and is no longer the production decision maker. Remove obsolete native-specific methods/tests where safe; if tests use a small unrelated pure order helper, preserve only what the new implementation genuinely uses.
- No second `_tiles` authority, no shadow copy reordered locally, no duplicate Runtime requests.

### 5.5 Small, useful diagnostics only

Keep logs at **gesture boundaries**, not every pointer movement:

- `Shortcut pointer reorder armed` — TileId, PointerDeviceType, sourceIndex.
- `Shortcut pointer reorder released` — TileId, SourceIndex, TargetIndex, Outcome=(MoveRequested|SameCard|OutsideGrid|BelowThreshold|CaptureLost|Canceled|Unavailable).
- Existing `Runtime Shortcut move response received` — actual Runtime outcome.

Do not log entire card text/scripts/paths or high-frequency pointer streams. Do not leave a diagnostics-only unfinished PR. Implement the actual fix in this PR.

## 6. Implementation commit 2 — Remove right/bottom ListView card shadow/raised artifact

Proposed commit message: `fix(shortcut-ui): remove native item presentation shadow`

The user previously removed the visible effect, which reappeared after PR #725 restored the default native `ListViewItemPresenter`. The template association is plausible; source has no explicit `ThemeShadow`. Determine from current XAML/style which surface paints in the 12 DIP right/bottom margin. The goal is deterministic absence, not renaming the shadow.

- **Native DnD is now intentionally disabled**. It is acceptable again to use a minimal `ListViewItem` content-only `ControlTemplate` that has **no native drag/pressed/selected animation or raised visual**, while preserving the card's own Border, content bindings, and functional pointer/editor controls.
- Prefer the smallest working template/style change. A lightweight `ContentPresenter` in the ListViewItem template is reasonable after removing the native DnD dependency. Verify `Content`/`ContentTemplate` forwarding, stretch alignment, hit-test/pointer routing, and Edit/Delete keyboard/pointer behavior.
- Alternative: retain the default presenter and neutralize its **real** offending states/properties if this is simpler and reliably removes the artifact. Do not keep unexplained 12 DIP hover fills/shadows visible. Microsoft explains native ListViewItem container state visuals are layered over/under the ItemTemplate:
  https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/item-containers-templates
- **Do not delete the actual card Margin 0,0,12,12, the CardBackgroundFillColorDefaultBrush, the CardStrokeColorDefaultBrush, or the 1 px border solely to hide the native item effect**. These own actual spacing/fill/stroke. Do not change the Overlay tile visual system.
- Keep 3-column responsive width/`ItemsWrapGrid` and all existing card content and Edit/Delete.
- Do not introduce new ThemeShadow, animation, elevation/shadow style, or glows. A restrained in-card hover/drop-target cue is fine if necessary for discoverability, but must not appear as a lower/right elevation or heavy focus outline.

## 7. Critical implementation caveats — avoid a fourth ineffective drag patch

- `DragItemsStarting` success in logs is **not** proof of native drop support in High integrity.
- Do NOT reinstate `CanDragItems=true` / `CanReorderItems=true` as the fix, or try `StartDragAsync` from a custom grip.
- Do NOT add more stale-gesture recovery around `DragItemsCompleted`; the official elevated limitation makes that strategy unsupported.
- Do NOT lower Main UI process elevation as a workaround: explicitly violates Full1902 authority.
- Do NOT add a separate Medium frontend, COM drag broker, background helper, window hook or HWND subclass for card order.
- Do NOT make the card buttons nonfunctional by intercepting their pointer routing.
- No artificial broad race-proofing: only realistic pointer lost, canceled, page switch, capture/refresh, Runtime operation fail and shutdown/disable matter.
- The user wants these TWO bugs fixed now, not a new architecture document or multi-PR diagnostic campaign.

## 8. Source location / affected files

Expected scope (small):
1. `src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml`
2. `src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs`
3. `tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs` (adapt obsolete native drag assertions).
4. Add a tiny UI-focused test file **only if** needed for pure drop-target/gesture policy testing.

Do NOT edit ShortcutRuntime, ShortcutStore, schema models, frontend transport/protocol, Overlay, DevicePage, controller-lifecycle files, or app privilege settings.

## 9. Tests, assertions and quality

Update tests that currently **require** elevated-incompatible native behavior:
- `UiArchitectureTests.Main_app_shortcut_editor_uses_three_equal_native_wrap_slots_and_keeps...`: retain grid/card/content assertions; replace native DragItemsStarting/Completed assertions with no-native-drag and supported pointer event wiring assertions.
- `Main_app_shortcut_native_drag_sends_one_runtime_move_and_restores_local_order_first`: rewrite for pointer release → one authoritative Move call with no pre-mutation local reordering.
- `Main_app_shortcut_recovers_an_abandoned_drag_only_when_the_page_is_idle` and `Main_app_shortcut_stale_start_with_unchanged_order_does_not_reset_the_bound_collection`: remove obsolete native state coverage and replace with pointer cancel/loss/no-op behavior and no collection reset.
- `Main_app_shortcut_native_move_resolver_accepts_only_one_valid_item_move`: replace with current order/index/TileId resolver tests if needed; avoid testing old unused method as a contract.

Real automated tests where possible:
- 4-card [0,1,2] + [3]: 0→3, 3→0, 1→2, and same-index.
- 5-card incomplete row: 4→1; 0→4.
- Nearest-card/gap behavior, outside-grid cancellation, unrealized container handling.
- Threshold ignored click; capture lost and cancellation never send Move; a valid completion makes exactly one mutation and clears gesture; Edit/Delete do not arm drag.
- Source authority before/after failures remains Runtime-driven. No optimistic `ObservableCollection` reset/move while pointer held.
- Static XAML assertions: no native drag handlers/configuration, no unwanted ThemeShadow/raised controls, actual card spacing/stroke preserved, 3 columns, pointer handler wiring, Edit/Delete intact.
- Tests must compile for current `net10.0-windows10.0.26100.0` / Windows App SDK project without changing package versions.

Local Codex must run available **code-only** checks:
~~~powershell
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
git diff --check
~~~
If the local environment cannot execute one command (platform/tooling), report that limitation; do not invent pass status.

### User-owned post-merge reality check (never block implementation PR)

On the actual elevated MSI Claw:
- mouse/trackpad and touchscreen card moves in Main App;
- 3+1 layout; row-major overlay order;
- Edit/Delete clicks unchanged;
- no right/bottom shadow in idle, hover, pressed, after drag;
- cancel/drop outside and repeated drags;
- no effect on controller/Steam routing.

This device matrix is for the USER after code review/merge, **not** a requirement for local Codex and **not** a PR review blocker.

## 10. PR delivery and review checklist

ONE implementation PR, preferably TWO focused commits:
1. `fix(shortcut-ui): reorder cards with elevated-safe pointer gestures`
2. `fix(shortcut-ui): remove native item presentation shadow`

Local Codex:
- Read current main immediately before coding.
- Implement the usable reorder end-to-end, not diagnostics-only.
- Confirm no residual `DragItemsCompleted`-based production code or tests.
- Ensure no duplicate card order owner/temporary reordering or routing lifecycle changes.
- Include code-only test results in PR body and explain why native DnD was removed, citing Microsoft docs/issue.
- Keep real-device verification outside PR merge gate.
- Do not implement a third TDP/CPU Boost issue.

**Definition of done:** an elevated-safe Main UI Shortcut pointer reorder that issues the existing Runtime Move once on a valid gesture, plus a flat/non-elevated visual card surface without the unwanted right/bottom native container shadow.