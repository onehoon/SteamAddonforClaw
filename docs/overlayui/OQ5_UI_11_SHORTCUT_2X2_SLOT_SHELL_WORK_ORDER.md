# Work Order — OQ5-UI-11: Shortcut 2×2 Slot Shell

## Status

Eleventh implementation PR from:

- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`

Track: Addon Quick Settings Overlay UI

Label/name: `OQ5-UI-11`

Phase: Phase D — Shortcut foundation

Implementation baseline:

```text
OQ5-UI-10 merged as PR #472
merge commit: 2000962cac9021fbab151d8a0c4ffe0913b923d4
```

This is **not** part of the numbered Full PID1902 implementation sequence.

---

## 1. Goal

Replace the current `Shortcut` placeholder page with the agreed fixed four-slot Quick Settings shell:

```text
[ Slot 1 ] [ Slot 2 ]
[ Slot 3 ] [ Slot 4 ]
```

The page must provide:

- exactly four fixed shortcut slot identities;
- a 2 × 2 visual tile layout;
- logical controller selection across that 2 × 2 grid;
- DPad, Left Stick, and Right Stick directional movement through the semantic navigation path that already exists;
- pointer/touch selection of a tile;
- a clear selected-tile visual independent of HWND/keyboard focus;
- a deliberate `Unassigned` state for every slot because the action catalog/assignment contract is not implemented yet.

This PR is the **visual and navigation shell only**.

It must not invent shortcut actions, persistence, execution, configuration, or a generalized dashboard/grid framework.

The intended result is:

```text
Shortcut tab selected
        ↓
Slot 1 selected by default
        ↓
Up / Down / Left / Right move geometrically in the fixed 2 × 2 grid
        ↓
A on an unassigned slot = no product action
        ↓
B / LB / RB keep their existing global meanings
```

---

## 2. Required reading before implementation

Read current `main`, not only the original planning examples.

Required project documents:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- current `docs/work-order/` set, especially OQ3/OQ4 and active Full1902 policy work
- `docs/overlayui/README.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/overlayui/OQ5_UI_10_SETTING_PAGE_TAB_ORDER_EDITOR_WORK_ORDER.md`

Required current source:

- `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayRowSelection.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayTabState.cs`
- `src/SteamInputAddonforClaw/Lifecycle/OverlayControllerInputRouter.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs`
- current OQ5 Overlay tests

Current Full1902 authority/lifecycle documents remain higher authority for controller ownership. PR11 is UI-only and must not alter that lifecycle.

---

## 3. Current code facts that define this PR boundary

### 3.1 Shortcut is still a placeholder

At the PR10 baseline, `OverlayWindow.BuildPage(...)` has real content only for:

```text
Device  → temporary Toggle/Slider/navigation fixtures
Setting → tab-order editor
```

All other tabs, including `Shortcut`, still use `CreatePlaceholderPage(...)`.

PR11 replaces only the `Shortcut` placeholder.

### 3.2 Semantic directional input already exists

The current Runtime/OQ4 path already converts:

```text
DPad
Left Stick
Right Stick
```

into the same semantic actions:

```text
NavigateUp
NavigateDown
NavigateLeft
NavigateRight
```

`App.HandleNavigationAsync(...)` already forwards those semantic actions to `OverlayWindow`.

Therefore PR11 requires:

```text
no new physical input mapping
no new DirectInput/XInput/GameInput reader
no .Overlay protocol bump
no new wire message kind
no new consumed-control ownership
```

DPad/both sticks are already part of the existing OQ4 capture/release design.

### 3.3 The existing `OverlayRowSelection` is intentionally vertical-list oriented

Current ordinary-page behavior is:

```text
Up / Down   → MovePrevious / MoveNext through a linear row list
Left/Right  → selected row Adjust(-1/+1)
A           → selected row Activate
```

That is correct for Device/Setting rows but it is **not** the correct geometry for a fixed 2 × 2 Shortcut grid.

For example, a naïve four-row reuse would make:

```text
Slot 1 + Down → Slot 2
```

when the intended spatial movement is:

```text
Slot 1 + Down → Slot 3
```

Do not distort `OverlayRowSelection` into a generic graph/grid navigation engine just for this page.

### 3.4 Existing global navigation meanings remain frozen

Current meanings remain:

```text
LB → previous top-level tab
RB → next top-level tab
B  → close Overlay through Runtime/OQ4 retirement
A  → activate current UI target
LT/RT/X/Y → reserved / no action
```

Shortcut must not redefine those global inputs.

---

## 4. Frozen Shortcut shell contract

### 4.1 Exactly four fixed slots

PR11 must expose exactly:

```text
Shortcut Slot 1
Shortcut Slot 2
Shortcut Slot 3
Shortcut Slot 4
```

They are fixed product identities, not dynamic user-created objects.

Do not add:

- slot 5+;
- add/remove buttons;
- arbitrary slot count;
- paging;
- categories;
- plugin slots;
- nested shortcut folders.

### 4.2 Fixed visual placement in PR11

Initial row-major placement is:

```text
Slot 1 → row 0, column 0
Slot 2 → row 0, column 1
Slot 3 → row 1, column 0
Slot 4 → row 1, column 1
```

PR11 does not reorder slots.

Slot assignment/order persistence is OQ5-UI-12 or later focused work.

### 4.3 Every slot is `Unassigned` in PR11

The current UI design deliberately does not freeze the shortcut action catalog or execution policy.

Therefore each tile should render a clear empty state, for example:

```text
Slot 1
Unassigned
```

Do not fill the page with invented actions such as:

- screenshot;
- keyboard;
- TDP;
- fan;
- Steam/QAM;
- Game Bar;
- launcher/process commands;
- WING/OEM1 actions.

The visible `Unassigned` state is intentional product behavior for this foundation PR.

### 4.4 A does nothing on an unassigned slot

The design baseline eventually allows:

```text
assigned Shortcut tile + A → execute assigned shortcut
```

But there is no assignment/execution contract yet.

Therefore in PR11:

```text
selected unassigned tile + A
→ no product action
→ no settings mutation
→ no process launch
→ no fake preview action
```

A debug-level diagnostic is acceptable if useful, but do not make the no-op noisy.

### 4.5 Pointer/touch selects; it does not execute a fake action

Pointer/touch must be usable on the four tiles.

For PR11:

```text
click/tap Slot N
→ logical selection becomes Slot N
→ selected visual updates
→ no action executes
```

Do not require pointer users to interact with invisible controller-only state.

---

## 5. Controller navigation geometry

Use bounded/no-wrap movement.

Required transitions:

```text
Slot 1:
  Right → Slot 2
  Down  → Slot 3
  Left  → no-op
  Up    → no-op

Slot 2:
  Left  → Slot 1
  Down  → Slot 4
  Right → no-op
  Up    → no-op

Slot 3:
  Right → Slot 4
  Up    → Slot 1
  Left  → no-op
  Down  → no-op

Slot 4:
  Left  → Slot 3
  Up    → Slot 2
  Right → no-op
  Down  → no-op
```

Do not wrap from one edge to the opposite edge.

DPad, Left Stick, and Right Stick must all produce the same behavior because they already converge to the same semantic navigation actions before reaching Overlay UI.

Do not distinguish input source inside the Shortcut page.

---

## 6. Add one narrow Shortcut selection model

Add the smallest pure state object needed for the fixed grid.

Recommended shape:

```csharp
internal enum OverlayShortcutSlotId
{
    Slot1,
    Slot2,
    Slot3,
    Slot4,
}

internal sealed class OverlayShortcutSelection
{
    OverlayShortcutSlotId SelectedSlot { get; }

    void Reset();
    bool Select(OverlayShortcutSlotId slot);
    bool MoveUp();
    bool MoveDown();
    bool MoveLeft();
    bool MoveRight();
}
```

Exact method names may differ.

Requirements:

- pure state only;
- no XAML references;
- no settings;
- no transport;
- no controller hardware knowledge;
- no generic `NavigationGraph`, `GridNavigationManager`, adjacency dictionary framework, or reusable dashboard abstraction.

The four-slot geometry is fixed enough to express directly and test directly.

Keep the slot identity local to the Overlay project in PR11 unless a current implementation requirement genuinely needs it shared.

Do **not** move it into the cross-process Contracts project merely because PR12 may need persistence later. If PR12 needs a shared persisted/wire identity, promote/refine the contract then.

---

## 7. OverlayWindow integration

### 7.1 Build a dedicated Shortcut page

Extend `BuildPage(...)` narrowly:

```text
Setting  → existing tab-order editor
Shortcut → new BuildShortcutPage(...)
Device   → existing preview fixture
others   → placeholder
```

Do not refactor the whole page factory.

### 7.2 Visual structure

Use a simple WinUI `Grid` with:

```text
2 columns
2 rows
```

Tiles should have equal visual weight and fit comfortably inside the existing 400-DIP panel/body inset.

Use normal WinUI typography and the existing page/surface style direction.

Each tile should visibly communicate:

- slot identity (`Slot 1`, etc.);
- current state (`Unassigned`);
- current logical selection via the same existing accent/highlight language where practical.

Do not change global Overlay width, WorkArea geometry, window chrome, animation, or DPI policy merely for these four tiles.

### 7.3 Keep tile visuals by slot identity

A simple narrow structure is enough, for example:

```csharp
Dictionary<OverlayShortcutSlotId, Border> _shortcutTiles
```

or an equivalently small local record.

Do not create a generic tile/card component library for four empty-state tiles.

If a `Button` is used for pointer/touch handling, selected logical state must remain independent of keyboard-focus state. Existing no-activate controller UX must not become dependent on WinUI focus.

### 7.4 Initial selection

When the Shortcut tab becomes active:

```text
selected slot = Slot 1
```

This matches the existing Overlay rule that entering a page selects its first usable item.

It is acceptable for Shortcut to reset to Slot 1 each time the user enters the Shortcut tab in PR11; retaining a per-page Shortcut selection is not required.

If Shortcut is the first configured top-level tab, every new Overlay Show therefore starts with:

```text
Shortcut tab selected
+ Slot 1 selected
```

### 7.5 Page-aware directional dispatch

Keep `App.HandleNavigationAsync(...)` semantic and generic.

Prefer to keep page-specific interpretation inside `OverlayWindow`.

The current public-to-App seam may remain:

```text
NavigateUp()
NavigateDown()
AdjustSelectedRow(-1/+1)
ActivateSelectedRow()
```

Inside those methods, branch narrowly when:

```text
_selected top-level tab == Shortcut
```

Conceptually:

```csharp
internal void NavigateUp()
{
    if (_tabState.SelectedTab == OverlayTabId.Shortcut)
    {
        MoveShortcutUp();
        return;
    }

    MoveRowSelection(up: true);
}
```

and similarly:

```text
NavigateDown → Shortcut MoveDown
Left         → Shortcut MoveLeft
Right        → Shortcut MoveRight
A            → Shortcut unassigned activation no-op
```

This avoids:

- a new protocol action set;
- a second input dispatcher;
- modifying `OverlayRowSelection` into 2D;
- a generic navigation engine.

### 7.6 Do not register Shortcut tiles as four ordinary linear rows

The Shortcut page is the one intended 2D exception.

Do not make the four tiles ordinary `_pageRows` solely to reuse the existing row-selection engine if doing so gives incorrect geometry.

It is fine for:

```text
CapabilitiesFor(Shortcut) → empty
OverlayRowSelection → no selected row while Shortcut is active
OverlayShortcutSelection → owns the one selected Shortcut tile
```

Keep one selection authority per active page surface.

### 7.7 Selected visual refresh

Add one narrow method such as:

```csharp
ApplyShortcutSelectionVisual()
```

It should:

- compare each fixed tile identity with `SelectedSlot`;
- apply selected accent/highlight to exactly one tile;
- clear it from the other three;
- not use `Focus()` as controller-selection authority.

Pointer/touch tile selection must call the same visual refresh path.

---

## 8. Interaction with existing top-level tab behavior

PR10 tab-order functionality remains unchanged.

Reordering top-level tabs may move the `Shortcut` tab header to a different position, but the contents of the Shortcut page remain the fixed Slot1–Slot4 grid.

Do not couple:

```text
top-level tab order
```

to:

```text
Shortcut slot order
```

They are separate product concepts.

When LB/RB moves away from Shortcut:

- existing top-level tab selection behavior remains authoritative;
- Shortcut does not consume LB/RB;
- no Shortcut modal/back stack remains active.

When B is pressed:

- existing Runtime-owned Overlay dismissal path remains authoritative;
- Shortcut does not intercept B.

---

## 9. No protocol/settings change in PR11

PR11 is frontend shell/navigation only.

Do not modify:

- `.Overlay` protocol version 5;
- `OverlayWireMessageKind`;
- `NamedPipeOverlayClient` preference messages;
- `StartupSettingsCoordinator`;
- `AppSettings`;
- `SettingsStore`;
- `settings.json` schema;
- Runtime composition.

OQ5-UI-12 owns the narrow Shortcut slot preference/assignment model.

Do not pre-build that persistence contract in PR11.

---

## 10. No controller/lifecycle change

PR11 must not change:

- PID1901 / PID1902;
- DirectInput ownership;
- HidHide;
- VIIPER ownership/teardown;
- Xbox360 / SteamDeck presentation selection;
- Steam/BPM state semantics;
- OQ4 neutral publication;
- release-to-resume gating;
- suspend/resume;
- physical-input recovery;
- WING/OEM1 mapping/policy;
- Game Bar suppression;
- Main UI ↔ Overlay coexistence;
- Steam QAM coexistence.

No new physical control is introduced by PR11. It only changes how already-consumed semantic Up/Down/Left/Right/A inputs are interpreted when the Shortcut page is active.

Therefore do not add another release gate, epoch, input state owner, controller session, or lock.

---

## 11. Expected implementation footprint

Likely production changes:

```text
src/SteamInputAddonforClaw.Overlay/OverlayShortcutSelection.cs   new, small pure 4-slot model
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs         Shortcut page + page-aware dispatch + visuals
```

Optional only if current style/readability clearly benefits:

```text
src/SteamInputAddonforClaw.Overlay/OverlayShortcutTile.cs
```

Do not add a separate UI class merely to wrap a `Border` and two `TextBlock`s if `BuildShortcutPage(...)` remains clear.

Expected tests:

```text
tests/SteamInputAddonforClaw.Tests/OverlayShortcutSelectionTests.cs
```

Potential focused updates to existing Overlay shell/navigation tests are acceptable if needed.

No Runtime/FrontendTransport/Settings test changes should be necessary unless a real regression is discovered.

---

## 12. Required tests

### 12.1 Pure 2 × 2 movement

Cover every geometric direction needed to prove the model:

```text
Slot1 Right → Slot2
Slot1 Down  → Slot3
Slot2 Left  → Slot1
Slot2 Down  → Slot4
Slot3 Up    → Slot1
Slot3 Right → Slot4
Slot4 Up    → Slot2
Slot4 Left  → Slot3
```

### 12.2 Bounded no-wrap edges

Verify no-op at every outer edge, including:

```text
Slot1 Left / Up
Slot2 Right / Up
Slot3 Left / Down
Slot4 Right / Down
```

A no-op should report no movement so the caller need not redraw/log a fake change.

### 12.3 Reset behavior

Verify:

```text
move selection away from Slot1
→ Reset()
→ Slot1 selected
```

### 12.4 Pointer-selection seam

Pure model test should verify direct selection of Slot1–Slot4 changes the selected identity deterministically.

Do not add UI automation solely to test WinUI button click plumbing if the pure state contract is already covered and the production wiring is trivial.

### 12.5 No action execution in PR11

If an activation method/seam exists, verify an unassigned slot has no action callback/side effect.

Do not invent a fake action merely to make this test non-empty.

### 12.6 Existing regression suite

All existing tests must remain green, especially:

- Overlay row-selection tests;
- tab navigation/state tests;
- tab-order editor/transport tests;
- OQ4 controller capture/release tests;
- Overlay transport/lifecycle tests.

---

## 13. Manual / hardware validation

Hardware validation on a supported MSI Claw should verify:

### Visual

At the reference device baseline:

```text
1920 × 1200
150% scaling
400-DIP Overlay panel
```

confirm:

- four tiles fit without clipping;
- 2 × 2 geometry is visually obvious;
- `Slot N` and `Unassigned` are readable;
- exactly one tile has the controller-selection highlight;
- no global panel-width/geometry change was needed.

Also spot-check another Windows scale if practical to ensure ordinary DPI-aware layout remains intact.

### Controller

With Shortcut active:

- DPad follows the required 2 × 2 geometry;
- Left Stick follows exactly the same geometry;
- Right Stick follows exactly the same geometry;
- boundaries do not wrap;
- A on every unassigned slot performs no action;
- B still closes the Overlay;
- LB/RB still change top-level tabs;
- LT/RT/X/Y remain no-op/reserved.

### Pointer/touch

- tapping/clicking each tile selects that tile;
- no fake shortcut executes;
- clicking outside the Overlay still uses the existing outside-dismiss path.

### OQ4 safety

During the above checks:

- game-facing controller output remains neutral while Overlay capture is active;
- closing the Overlay still waits for the existing consumed-control release policy where applicable;
- the previous X360/SteamDeck presentation resumes normally;
- no stuck direction/button leaks into the underlying game/Steam QAM.

Do not add PR11-specific synchronization if existing OQ4 behavior already satisfies this lifecycle.

---

## 14. Explicit non-goals / forbidden scope growth

Do **not** include in OQ5-UI-11:

- OQ5-UI-12 Shortcut persistence/assignment model;
- a shortcut action catalog;
- shortcut execution;
- process launching;
- shell commands;
- keyboard/macro injection;
- Steam/Game Bar/QAM shortcut actions;
- WING/OEM1 remapping;
- slot reorder UI;
- drag/drop;
- dynamic slot count;
- add/remove slot controls;
- nested Shortcut configuration page;
- modal/back-stack UI;
- generic tile/card framework;
- generic dashboard/layout manager;
- generic 2D navigation graph;
- new `.Overlay` wire kinds;
- protocol v6;
- new settings.json fields;
- another controller input session;
- raw controller state sent to Overlay;
- DPad-vs-stick-specific page behavior;
- hold-repeat framework;
- new OQ4 release-gate authority;
- PID/HidHide/VIIPER changes;
- Device/Profile/Controller feature implementation;
- unrelated visual polish/refactor.

---

## 15. Logging

Keep logging narrow.

Useful debug-level events may include:

```text
Shortcut selection moved: Slot1 → Slot2
Shortcut tile selected by pointer: Slot4
```

Do not log every no-op boundary press at Info level.

Do not log any invented shortcut execution because PR11 has none.

---

## 16. Completion criteria

PR11 is complete when all of the following are true:

1. `Shortcut` no longer shows the generic placeholder.
2. Exactly four fixed tiles render in a 2 × 2 layout.
3. All four tiles visibly show `Unassigned` or equivalent deliberate empty state.
4. Exactly one logical Shortcut tile is selected while the Shortcut page is active.
5. Entering Shortcut selects Slot 1.
6. DPad/both sticks use correct bounded 2 × 2 movement.
7. Pointer/touch can select each tile.
8. A on an unassigned tile performs no product action.
9. B/LB/RB retain their current global behavior.
10. Existing ordinary row selection behavior is unchanged on Device/Setting.
11. `OverlayRowSelection` has not been generalized into a 2D/grid framework.
12. No new protocol/settings/Runtime/controller authority was added.
13. Full Release build passes with zero warnings/errors expected by current baseline.
14. Full test suite passes.
15. `git diff --check` is clean.
16. Hardware checkpoint is documented; if physical hardware is unavailable, mark only the hardware portion blocked rather than weakening automated validation.

---

## 17. Review checklist

Review the PR specifically for:

- [ ] four slots only;
- [ ] 2 × 2, not linear-list navigation;
- [ ] bounded/no-wrap directional behavior;
- [ ] DPad/both sticks share the same semantic behavior;
- [ ] Slot 1 initial selection;
- [ ] pointer/touch selection works without fake execution;
- [ ] A is a no-op because every PR11 slot is unassigned;
- [ ] no action catalog/persistence was invented;
- [ ] no generic navigation/grid framework was added;
- [ ] existing row selection remains simple and vertical;
- [ ] no `.Overlay` protocol change;
- [ ] no settings schema change;
- [ ] no OQ4/Full1902 ownership/lifecycle change;
- [ ] no WING/OEM1/Game Bar policy work mixed into this PR;
- [ ] existing Overlay show/hide/outside-dismiss/tab-order behavior remains intact.
