# Work Order — v0.1.345 Shortcut Pointer Drag Visual Feedback (105% Lift, Follow, Drop Target)

Date: 2026-10-08
Repository: onehoon/SteamAddonforClaw
Reviewed main: cc1dbbbdb844ce3bee4b8ec3b5270d206a534788 (PR #726 squash merge)
Execution: local Codex
Delivery: **ONE implementation PR**, preferably one focused commit
Scope: **Main App Shortcut editor presentation only**; no new reorder engine
Physical-device/visual acceptance: **user-owned AFTER merge; NOT required for local Codex and NOT a PR merge blocker**

## 1. Product decision

PR #726 fixed the actual elevated-window Shortcut reorder: mouse pointer gestures send one existing frontend Runtime Move, the persisted Shortcut JSON order changes, and the UI subsequently uses the authoritative snapshot. **Do not reopen or replace that logic.**

The remaining problem is concrete UX: while the user drags, the source card stands perfectly still and gives no indication that a drag is happening. Users only see the reorder after release, so the UI feels nonresponsive.

Implement the previously agreed visual behavior:

| Gesture stage | Visual response |
| --- | --- |
| Idle / short click / Edit / Delete | **Unchanged card**: same normal size (1.00), spacing, fill/stroke, and no floating effect |
| Pointer press, still below the existing **8 DIP** threshold | No drag preview or scale-up |
| Cross threshold with an accepted/captured gesture | Show the **same card** floating at **1.05x (105%)**; start a short **~120 ms** ease-out scale-in; slightly reduce opacity to ~0.95, no permanent shadow |
| Pointer moved while active | Floating card follows the pointer immediately without easing/lag; visibly preserve the original pointer-to-card contact offset |
| Valid target | Visually indicate the **currently resolved destination** with a restrained neutral outline or subtle fill; never a bright Steam-blue focus ring |
| Canceled, same-card, outside, capture lost, page change, Runtime snapshot rebind, or released | Clear preview/target and restore original card appearance reliably; preserve exactly the existing cancel/no-op/one-Move semantics |
| Successful drop | Clear transient visuals, send the existing Move exactly once, let the existing Runtime response snapshot render the final order |

**Never resize real ListView item Width/Height**. The grid remains fixed 3 columns, row-major, and 12 DIP margins/gaps.

The user requested a recognizable lift/zoom during dragging, **not** new persistence or controller functionality.

## 2. Source/authority instructions

Read these before coding:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md` (especially `3.2`: High Runtime, High Main UI and High Overlay by inheritance)
- `docs/work-order/1008_V01344_ELEVATED_SHORTCUT_POINTER_REORDER_AND_CARD_SHADOW_FIX_WORK_ORDER.md` — PR #726 input/persistence foundation
- `docs/work-order/SHORTCUT_EDITOR_3_COLUMN_QAM_TILE_VISUAL_POLISH_ONE_PR_TWO_COMMITS_WORK_ORDER_2026-10-07.md` — original three-column/card design
- Current source: `src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml` and `ShortcutPage.xaml.cs`, and `tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs`

This is a standalone Full1902 app for one Windows administrator user / one interactive session. CTW integration is irrelevant. Do not change the High Main UI elevation model or add cross-integrity IPC.

### Verified baseline

Latest post-PR #726 log test, **v0.1.345.0, October 8 ~23:41–23:42 KST**:

- Main UI log: https://drive.google.com/file/d/1tUWAVrDDmymkz2LafenFgrwIyZpC3C0O/view
- Runtime log: https://drive.google.com/file/d/1d1SKN5bfUkVIxz3oP4YRs6wPlbRdp8R-/view

From the UI log: 27 pointer gestures armed and released, 12 MoveRequested, 12 Runtime Move succeeded, 10 SameCard, 5 Unavailable. Runtime confirmed 12 saved Shortcut documents. The user then verified that cards really do reorder; what is missing is **visible feedback during movement**. Do not claim the Move path is still broken.

Current source baseline:

- `ShortcutPage.xaml`: `ListView` / `ItemsWrapGrid Orientation=Horizontal MaximumRowsOrColumns=3`, three-column computed `ItemWidth`; card root `Border Margin="0,0,12,12" Padding="16" ...` with font icon, title/summary/validation text, vertically stacked Edit/Delete; simple content-only `ListViewItem` template prevents native raised-container shadow.
- `ShortcutPage.xaml.cs`: `_reorderPointer`, `_reorderSurface`, `_reorderTileId`, `_reorderSourceIndex`, `_reorderStartPosition`, `_reorderThresholdPassed`, `_reorderTargetIndex`; `ShortcutTile_PointerPressed/Moved/Released/Canceled/CaptureLost`; `ClearShortcutReorder`.
- Existing elevated-safe touch behavior: `ManipulationMode="None"` on the card; accepted touch/pen press calls `surface.CancelDirectManipulations()`, then `surface.CapturePointer(e.Pointer)`; the card excludes Edit/Delete through `IsPointerSourceInsideButton`.
- Existing drop resolver: `ResolveCurrentShortcutDropIndex` → `ResolveShortcutDropIndex`, including occupied-row bounds and incomplete last row, used for `TryCreatePointerMoveIntent`.
- Existing sole authoritative mutation: `await ApplyMutationAsync(intent)` → `_frontend.MutateShortcutAsync(intent)` → Runtime/ShortcutStore; after response `Render(result.Snapshot)`.
- `Render`, `Deactivate`, `ShortcutPage_Unloaded`, `SetBusy`, `PointerCanceled`, and `PointerCaptureLost` already call `ClearShortcutReorder`. Reuse this cleanup owner.

## 3. Microsoft WinUI / Windows App SDK docs VERIFIED for this work order

**Use Microsoft.UI.Xaml / Microsoft.UI.Xaml.Controls APIs matching WinUI 3, not old Windows.UI.Xaml-only code.** Microsoft documents:

1. `UIElement.StartDragAsync(PointerPoint)` — unsupported in elevated administrator apps:
   https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.startdragasync?view=windows-app-sdk-1.8
   This is why **native DragItemsStarting/Completed, OLE drag/drop, StartDragAsync, or a Medium UI broker must stay out**.
2. `PointerRoutedEventArgs.GetCurrentPoint(UIElement)` — returns coordinates relative to the supplied XAML element:
   https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.input.pointerroutedeventargs.getcurrentpoint?view=windows-app-sdk-1.8
   Keep point/bounds in **one XAML DIP coordinate space**, never physical-screen pixels.
3. `CompositeTransform` — supports `ScaleX/ScaleY`, `TranslateX/TranslateY` in one render transform; applies scale then translation:
   https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.compositetransform?view=windows-app-sdk-1.8
4. `UIElement.RenderTransformOrigin` — `0.5,0.5` is the element center for scale, distinct from top-left placement:
   https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.rendertransformorigin?view=windows-app-sdk-1.8
5. `UIElement.Scale` / `ScaleTransition` / `Vector3Transition.Duration` — native render-only scale and duration-based transitions are supported. These are a suitable alternative to animating a `CompositeTransform` with a `Storyboard`:
   https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.scale?view=windows-app-sdk-1.8
   https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.scaletransition?view=windows-app-sdk-1.8
   https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.vector3transition.duration?view=windows-app-sdk-1.8
6. `Canvas.Left`, `Canvas.Top`, `Canvas.ZIndex` — positioning and correct overlap order for transient visual copies:
   https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.canvas?view=windows-app-sdk-1.8
7. `ItemsWrapGrid Orientation=Horizontal` arranges items in row-major order; do not replace it:
   https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.itemswrapgrid?view=windows-app-sdk-1.8

**Design distinction:** These Microsoft docs establish availability/semantics of the UI APIs. They do **not** supply a Microsoft-endorsed exact Shortcut-drag visual implementation or guarantee that animating the original item can cross virtualized ListView container boundaries without clipping. The overlay/ghost arrangement below is **our design decision** to avoid that practical risk, not a documented Microsoft mandate.

## 4. Recommended implementation: one hit-test-inert preview layer above the existing ListView

**Prefer a transient "drag preview/ghost" instead of translating the real live card inside `ItemsWrapGrid`.**

Why this choice:
- A real `ListViewItem` can remain within its items-host clip/z-order or be recycled; render-transforming it over other row/column items may not produce a consistently floating card.
- An overlay sibling to `ShortcutList` can paint on top without moving/reparenting the source view or changing `_tiles`.
- The original card remains in its stable layout slot as a **dimmed placeholder**; a ghost based on the same card template follows the pointer.
- The ghost is presentation-only; it is **not a second item in the collection or a second ordering authority**.

### 4.1 Layout: reuse the card template; no functional duplicate controls

In `ShortcutPage.xaml`:

1. Extract the **current card DataTemplate** to a single local `UserControl.Resources` keyed `DataTemplate` (suggested key: `ShortcutCardTemplate`, keep `x:DataType="frontend:FrontendShortcutEditorTile"`). Do not change the original Border geometry/bindings/button actions.
2. Set `ShortcutList.ItemTemplate="{StaticResource ShortcutCardTemplate}"` and remove its duplicated inline `ItemTemplate` body.
3. In the existing `Grid Grid.Row="2"` add ONE topmost, hit-test-transparent transient `Canvas` sibling **after** the ListView and empty state; it hosts:
   - A small `Border` target indicator, `Visibility=Collapsed` by default; 8-DIP radius, subtle neutral theme stroke or understated neutral fill, no glow.
   - A `ContentPresenter` preview, `ContentTemplate="{StaticResource ShortcutCardTemplate}"`, `Visibility=Collapsed` by default. Assign `Content` to the *same tile object* only while previewing. `IsHitTestVisible=False` for the entire layer/preview; cloned Edit/Delete are **visuals only**, never clickable.
4. Make the preview the last/highest `Canvas.ZIndex` child. Set size from the realized source item, not from physical pixels or hard-coded 1920x1200.
5. Keep `ShortcutEmptyState` layering intact when no items, and make the transient canvas empty/invisible without affecting navigation or the normal card's pointer hit tests.

Illustrative XAML **shape** (NOT a blind replacement: Codex must integrate with the existing element names, XAML resource syntax and compiled bindings):

~~~xml
<UserControl.Resources>
    <DataTemplate x:Key="ShortcutCardTemplate"
                  x:DataType="frontend:FrontendShortcutEditorTile">
        <!-- Existing complete Border + icon/title/summary/validation/Edit/Delete,
             all bindings and pointer handlers preserved verbatim. -->
    </DataTemplate>
</UserControl.Resources>

<!-- In existing Grid Grid.Row="2" -->
<ListView x:Name="ShortcutList"
          ItemTemplate="{StaticResource ShortcutCardTemplate}"
          ... />

<Canvas x:Name="ShortcutDragLayer" IsHitTestVisible="False">
    <Border x:Name="ShortcutDropIndicator"
            Canvas.ZIndex="1"
            IsHitTestVisible="False"
            Visibility="Collapsed"
            BorderThickness="2"
            CornerRadius="8"
            BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" />
    <ContentPresenter x:Name="ShortcutDragPreview"
                      Canvas.ZIndex="2"
                      IsHitTestVisible="False"
                      ContentTemplate="{StaticResource ShortcutCardTemplate}"
                      Visibility="Collapsed"
                      Opacity="0.95" />
</Canvas>
~~~

A keyed template is preferred to two separately maintained copies. If the compiled `x:Bind` resource needs small placement/context adjustments, make those only locally. **No new reusable control framework** or new view-model.

### 4.2 Preserve pointer contact offset; avoid snapping to card center

Existing pointer logic uses `e.GetCurrentPoint(ShortcutList).Position` for drop resolution. **Leave this intact.**

For the drag *visual*, use a **single shared space**: `e.GetCurrentPoint(ShortcutDragLayer).Position` and `sourceItem.TransformToVisual(ShortcutDragLayer).TransformBounds(...)`.

At successful pointer arm, capture the press point in drag-layer coordinates (one additional `Point` field is enough). When threshold passes, measure the source `ListViewItem` bounding rectangle in that same layer space. The preview should represent the item container's layout size, including the existing 12-DIP right/bottom card margin. Calculate:

~~~csharp
// Illustrative, use actual existing fields/WinUI types.
Point current = e.GetCurrentPoint(ShortcutDragLayer).Position;
double dx = current.X - _dragPressPointInLayer.X;
double dy = current.Y - _dragPressPointInLayer.Y;

Canvas.SetLeft(ShortcutDragPreview, sourceItemBounds.X + dx);
Canvas.SetTop(ShortcutDragPreview, sourceItemBounds.Y + dy);
~~~

This lets the preview follow from the actual grab point rather than teleporting its center to the cursor. Cache the source bounds for this short gesture only; ensure the `x:Bind` data context and preview layout size are valid before rendering.

**Important:** Do not call `e.GetCurrentPoint(preview)` and do not use transformed/animated ghost coordinates to resolve the authoritative target. Keep `ResolveCurrentShortcutDropIndex(positionInShortcutList)` and the same `ListViewItem` bounds as PR #726.

### 4.3 105% zoom: render only, center origin, immediate movement

At the existing 8-DIP threshold:
- Show the ghost in front of all cards, dim the original source card moderately (suggest ~0.3–0.4 opacity).
- Scale the preview uniformly **1.00 → 1.05** around its center in **~120 ms ease-out**.
- During `PointerMoved`, update only preview `Canvas.Left/Top` (or an equivalently supported plain translation) **synchronously** from pointer movement. **Do not apply a 120ms translation transition to every pointer event**: that causes lag behind the cursor.
- Optional preview opacity ~0.95. **No `ThemeShadow` or permanent default-card shadow**, because PR #726 removed a visible unwanted right/bottom raised artifact.
- Keep full textual content and card dimensions; the ghost's Edit/Delete is decorative due to hit-test opt-out.

**Pick ONE WinUI animation family**; don't stack `RenderTransform` + `UIElement.Scale` + Composition APIs:

Option A, direct `Scale` transition:
~~~csharp
using System.Numerics;

// On setup/when preview gets a valid measured size:
ShortcutDragPreview.ScaleTransition = new Vector3Transition
{
    Duration = TimeSpan.FromMilliseconds(120)
};
ShortcutDragPreview.CenterPoint = new Vector3(
    (float)(ShortcutDragPreview.Width / 2),
    (float)(ShortcutDragPreview.Height / 2), 0);
ShortcutDragPreview.Scale = new Vector3(1.05f, 1.05f, 1.0f);
~~~

**Verify** a transition genuinely runs on entering visible state; a `Collapsed` element's initial scale change may snap rather than animate. If needed, use a simple WinUI `Storyboard` with a 120ms scale animation on the same ghost. On quick release/cancel it is safer to remove the preview immediately than let a stale preview remain. Optional short return-to-normal animation is fine only if guaranteed not to outlive a new gesture or the authoritative rebind.

Option B, single `CompositeTransform`:
~~~xml
<ContentPresenter x:Name="ShortcutDragPreview"
                  RenderTransformOrigin="0.5,0.5"
                  ...>
    <ContentPresenter.RenderTransform>
        <CompositeTransform ScaleX="1" ScaleY="1" />
    </ContentPresenter.RenderTransform>
</ContentPresenter>
~~~

Animate `ScaleX`/`ScaleY` from 1 to 1.05 over ~120ms via supported WinUI animation, but update position directly with `Canvas.Left/Top`. Do not animate Width/Height or `ItemsWrapGrid.ItemWidth`. Either option is valid; local Codex should use the smallest **verified compiling** solution for the current SDK.

### 4.4 Drop target indicator must match the *existing* resolver, not invent a second one

On pointer movement after threshold:
- Keep `_reorderTargetIndex = ResolveCurrentShortcutDropIndex(positionInShortcutList)` from PR #726.
- When target changes to a valid index **different from source**, locate its realized `ListViewItem`, map its card or item geometry into `ShortcutDragLayer` DIP coordinates, and update `ShortcutDropIndicator` location/size. A subtle neutral stroke/contrast is enough.
- For invalid/unavailable/outside, below threshold, or same card: hide the indicator.
- The current last-row (3+1/3+2) resolver legitimately maps horizontal blank space within the *occupied last row* to its nearest real item; show the indicator around that **real target card**, not a nonexistent slot.
- No drag-reordering of the `_tiles` collection, no “temporary preview insert”, and no new target-index resolver just for visuals. Avoid layout mutation loops on every pointer movement.
- Update target-indicator bounds only if resolved target changes or layout changes; skip work if the container is missing/unrealized.
- Do not change `ResolveShortcutDropIndex` behavior merely to make the indicator look prettier; the existing logic is exercised in PR #726.

### 4.5 Cleanup: extend the existing ONE teardown path

Extend **`ClearShortcutReorder(string outcome, bool releaseCapture)`** instead of inventing a second gesture manager/owner.

Cleanup must restore **all transient visuals**, regardless of exit path:
- Source `Border.Opacity` back to 1.0 (and no other visual mutation persists).
- Hide/reset preview, clear `ShortcutDragPreview.Content`, reset its scale/position for reuse. Dispose/stop any ongoing scale animation if necessary.
- Hide/reset target indicator.
- Clear the extra press-point/source-bounds fields.
- Preserve existing pointer state reset and optional `ReleasePointerCapture` behavior. Continue clearing state before calling ReleasePointerCapture to avoid reentrant double-release.
- `PointerReleased` constructs intent exactly as today, calls existing cleanup, then (if valid) **one** `await ApplyMutationAsync(intent)`; a preview must not block the Runtime response.
- `PointerCanceled`, `PointerCaptureLost`, `Deactivate`, `ShortcutPage_Unloaded`, `Render` authoritative rebind, `SetBusy(true)` and an unavailable/editor-disabled state use the same clear path.
- Do not leave a ghost hovering after outside release, a failed Move, window close, nav away, or rapid repeated drag.
- On short click there is no preview to clean except pointer capture bookkeeping.

**Do not delay mutation, hold a pointer capture for 120ms, or use a new timer/epoch/lock/state machine to complete a cosmetic animation.** Prefer a deterministic immediate reset to a visually fancy but unsafe stale preview.

### 4.6 Keep Edit/Delete and input accessibility unchanged

- Keep the original `IsPointerSourceInsideButton(...)` gate. Pressing Edit/Delete must continue invoking their existing actions; no drag starts.
- The ghost layer must **never** receive hit tests, capture a pointer, or invoke buttons. It is only a transient image of the existing tile content via the shared card template.
- Keep mouse-left and touch/pen behavior from PR #726 (`CancelDirectManipulations` and `ManipulationMode=None`).
- No permanent scale on hover/selection/keyboard focus. No added shadow, blue outline, pulsing animation, haptics, or auto-scroll in this PR.
- No global “reduced motion settings” abstraction. If the current app already has a relevant built-in platform motion preference, respect it with a minimal local check rather than creating a new settings/authority layer. No extra settings tab needed.

## 5. Allowed scope / prohibited changes

Expected changed files:
1. `src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml` — shared keyed card template + one overlay layer.
2. `src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs` — small transient presentation helpers integrated with existing pointer and clear path.
3. `tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs` — update structural assertions from inline to keyed template; assert preview layout, behavior ownership, cleanup and no native drag.

Do NOT change:
- `src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs`, `ShortcutStore` or `shortcuts.json` schema.
- Any frontend RPC, mutation contract, message format, Overlay QAM renderer, user launch privilege logic or installer.
- The High Runtime/Main UI process elevation policy, controller ownership, routing, HidHide, VIIPER, PID1901↔1902, sleep/resume handling.
- TDP / CPU Boost warning behavior, unrelated UX or helper processes.
- Windows App SDK package version, compiler configuration or whole-app styles just to implement this card effect.

Avoid extra NuGet packages, composition manager, multi-window drag helper, duplication of canonical tile-order state, background loops, per-pointer logging, synchronization wrappers, or theoretical race machinery.

## 6. Focused tests and review requirements

Run code-only tests. Update old tests that currently assume `ListView.ItemTemplate` contains an inline `DataTemplate`; they must find the keyed resource instead.

Required code-level assertions:
- The same keyed `ShortcutCardTemplate` is referenced by ListView and ghost; no diverging edit/delete click wiring.
- Pointer press **below** 8 DIP has no preview or scale.
- After threshold, preview is 105%, centered and pointer-following; movement updates position in the same layer coordinate system without per-move animation lag.
- Drag preview and target indicator remain `IsHitTestVisible=False`; actual card pointer handlers and Button exclusion are unchanged.
- The target indicator follows the **same** `ResolveCurrentShortcutDropIndex` result; skip same source or invalid targets.
- A release/cancel/deactivation/unload/snapshot rebind/busy transition invokes the shared clear path; preview, source opacity and indicator always reset.
- One valid gesture still invokes exactly one `ApplyMutationAsync` and no optimistic `_tiles.Move/Clear/Add` during movement.
- 3-column `ItemsWrapGrid`, widths, card 12 DIP margin and border, stack Edit/Delete, theme brushes and clipping/overlap behavior stay intact.
- 4 tiles 3+1 layout and incomplete last-row targets; unchanged drop target resolver and no new screenshot/golden-image framework.
- No `StartDragAsync`, `DragItemsStarting`, `DragItemsCompleted`, or native drag/drop event reintroduced.
- Prefer direct code assertions plus small pure helpers where genuinely necessary; no new UI integration test harness or fake window/compositor.

Commands (adapt only for local environment restrictions):

~~~powershell
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release
git diff --check
~~~

If the build tooling cannot execute a check, record the limitation honestly. **Real hardware touch/mouse/scale smoothness and screenshot checks are reserved for the user AFTER merge; they are not required tests, not implementation prerequisites, and not PR review blockers.**

## 7. PR delivery

ONE PR, one topical commit preferred:

`feat(shortcut-ui): show lifted 105-percent pointer drag preview`

Include in PR description:
- PR #726 was already saving Move successfully; this PR only adds **visual affordance**.
- Chosen approach: in-grid transparent preview layer, shared tile template, center-scale and pointer follow, highlight the existing resolved target.
- Why ghost instead of transforming actual ListViewItem: stable layout/paint hierarchy, no virtualization/reorder interference.
- Microsoft reference links from `3`; explicitly distinguish API documentation from our design choices.
- Code-only build/test results; **device validation owned by user after merge**.

**Definition of done:** The elevated Main App visibly lifts the selected card to 105%, follows the user's pointer during a deliberate gesture, marks its drop target, and restores normal appearance immediately on any completion/cancel. The pre-existing PR #726 authoritative Move/cleanup semantics are unchanged, with no regression of the no-shadow idle card presentation.