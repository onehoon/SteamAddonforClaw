# Work Order — OQ5-UI-04: Logical Row Selection and Scrolling

## Status

Fourth implementation PR from:

- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`

Track: Addon Quick Settings Overlay UI

Label/name: `OQ5-UI-04`

Baseline: `main` after PR #462 / commit `2d44068c64030ef96a403ca2b58f67b800019328`.

This is **not** part of the numbered Full PID1902 PR sequence.

---

## 1. Goal

Turn the semantic directional input foundation already completed by OQ4 / OQ5-UI-02 / OQ5-UI-03 into one predictable controller-first **logical row selection** model inside the Overlay frontend.

Required interaction model after this PR:

```text
NavigateUp / NavigateDown
→ move the selected row within the active tab

NavigateLeft / NavigateRight
→ dispatch an adjustment only when the selected row explicitly supports adjustment

Accept
→ dispatch activation only when the selected row explicitly supports activation

PreviousTab / NextTab
→ existing LB/RB tab switch
→ reset the newly selected page to its first selectable row

Back
→ existing Runtime-owned Overlay dismissal
```

The selected row must be visually obvious and must be brought into the existing `BodyScroll` viewport when controller navigation moves it off-screen.

This remains a **no-activate Overlay window**. Logical controller selection must not depend on WinUI keyboard focus or HWND activation.

---

## 2. Required reading before implementation

Read current `main` after #462.

Required documents:

- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/overlayui/OQ5_UI_01_FIVE_TAB_OVERLAY_SHELL_WORK_ORDER.md`
- `docs/overlayui/OQ5_UI_02_LB_RB_TAB_NAVIGATION_WORK_ORDER.md`
- `docs/overlayui/OQ5_UI_03_DUAL_STICK_DIRECTIONAL_NAVIGATION_WORK_ORDER.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/OQ4_CONTROLLER_CAPTURE_NEUTRAL_PUBLICATION_WORK_ORDER.md`

Required current source:

- `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayTabState.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs`
- `src/SteamInputAddonforClaw/Lifecycle/OverlayControllerInputRouter.cs`
- relevant existing Overlay tests under `tests/SteamInputAddonforClaw.Tests/`

---

## 3. Current code facts that define the seam

### 3.1 The semantic input path is already complete

Current `.Overlay` protocol is v4.

Current navigation actions already include:

```text
NavigateUp
NavigateDown
NavigateLeft
NavigateRight
Accept
Back
PreviousTab
NextTab
```

DPad and both sticks already arrive as the same `Navigate*` semantic actions.

LB/RB already arrive as `PreviousTab` / `NextTab`.

Do not add another protocol version for this PR.

Do not add raw controller state or physical-source information to Overlay UI.

### 3.2 Current `App.HandleNavigationAsync()` is the correct UI-thread dispatch seam

Current behavior already marshals navigation onto the existing `DispatcherQueue`.

Today only:

- `Back`;
- `PreviousTab`;
- `NextTab`;

perform visible actions.

This PR should extend that same switch for:

- `NavigateUp`;
- `NavigateDown`;
- `NavigateLeft`;
- `NavigateRight`;
- `Accept`.

Do not add another queue, event bus, or navigation worker.

### 3.3 Current page bodies are placeholders

OQ5-UI-01 created five page containers but no production control rows yet.

The next PRs will add:

- toggle rows;
- slider rows;
- delayed slider mutation behavior;
- real Runtime-backed Device controls.

Therefore this PR should establish only the smallest row-selection/capability seam those real controls need.

Do not invent feature settings merely to make this PR interactive.

### 3.4 Existing scroll owner

`OverlayWindow.xaml` already owns one `ScrollViewer`:

```xml
<ScrollViewer x:Name="BodyScroll" ...>
    <Grid x:Name="TabBody" />
</ScrollViewer>
```

Reuse it.

Do not add per-page ScrollViewers, a nested scrolling abstraction, or a generalized virtualized list in this PR.

### 3.5 Existing tab behavior

Every Overlay Show resets the top-level tab to `Order[0]`.

LB/RB tab changes are bounded/no-wrap.

This PR must additionally make a newly selected tab choose its **first selectable row**.

For now, it is acceptable and preferred to reset row selection to the first selectable row every time the user changes tabs rather than persisting per-page row position.

Do not add per-page selection persistence.

---

## 4. Frozen logical-selection rules

### 4.1 One logical row selection only

At any time:

```text
active tab = exactly one tab
selected row = zero or one row within that active tab
```

Zero selected rows is valid when the page has no selectable rows.

Do not create multiple simultaneous controller selections.

Do not mirror desktop keyboard focus state.

### 4.2 Up / Down

```text
NavigateUp
→ previous selectable row

NavigateDown
→ next selectable row
```

Behavior is bounded/no-wrap.

Example:

```text
first row + Up   → stays first row
last row + Down  → stays last row
```

Skip rows that are not selectable.

Do not add page wrap or section wrap preferences.

### 4.3 Left / Right

```text
NavigateLeft
→ selected row adjustment delta = -1

NavigateRight
→ selected row adjustment delta = +1
```

But only when the selected row explicitly registers an adjustment capability.

If the row does not support adjustment:

```text
Left / Right → no-op
```

Do not reinterpret Left/Right as tab navigation. LB/RB already own tabs.

### 4.4 A / Accept

```text
Accept
→ selected row activation
```

But only when the selected row explicitly registers an activation capability.

If the selected row has no activation capability:

```text
A → no-op
```

Do not create a generic dialog/navigation stack for A.

### 4.5 B / Back

B remains global Overlay close.

Do not route B into row selection state.

Do not add a local Back stack.

### 4.6 Tab switch

For:

- LB/RB;
- pointer/touch tab click;
- every fresh Overlay Show;

the active page must select its first selectable row.

If no selectable row exists, clear row selection.

Current top-level Show behavior remains:

```text
Overlay Show
→ reset to first configured tab
→ select first selectable row in that tab
→ reveal Overlay
```

Do not restore the previously selected row from the previous Overlay session.

---

## 5. Keep the row contract narrow

Do not build a generic UI framework.

The only capabilities needed by later OQ5 controls are:

```text
selectable?
visual element / row container
activate?  optional
adjust?    optional, receives -1 / +1
```

A narrow local registration shape is appropriate, conceptually:

```csharp
internal sealed record OverlayRowRegistration(
    FrameworkElement Element,
    Func<bool> IsSelectable,
    Action? Activate,
    Action<int>? Adjust);
```

Exact naming/type shape may differ if a simpler implementation is clearer.

Requirements:

- ownership stays inside `OverlayWindow` / Overlay frontend;
- no Runtime-visible row model;
- no persistence;
- no global `NavigationManager`;
- no service locator;
- no interface hierarchy solely for future controls;
- no generalized command framework.

The contract should be sufficient for OQ5-UI-05 Toggle and OQ5-UI-06 Slider without pre-building those controls now.

---

## 6. Selection state implementation

Use one small local selection state for the currently active page.

Recommended facts:

```text
current active-tab row registrations
selected row index (nullable)
```

Optional pure state helper is acceptable if it materially improves unit testing, for example a tiny `OverlayRowSelectionState` that only knows:

- current selected index;
- select first available;
- move previous/next while skipping unavailable rows;
- clear when no row is available.

Do not give this helper knowledge of:

- controller hardware;
- tabs;
- XAML transport;
- settings;
- feature mutation;
- scrolling;
- pointer capture.

### 6.1 Dynamic availability

Later Runtime-backed rows may become disabled/unavailable while the Overlay is visible.

This PR does not need a reactive availability framework.

However, every selection move should evaluate the row's current `IsSelectable` fact rather than assuming the initial page build state remains valid forever.

If the currently selected row is no longer selectable when an action arrives, normalize selection to the nearest/first selectable row before mutation.

Keep this bounded and local; do not add observation/subscription machinery before a real feature requires it.

---

## 7. Selected-row visual

Logical controller selection must be visible independently of WinUI keyboard focus.

### Required visual behavior

```text
selected row   → clear restrained highlight
unselected row → normal page surface
```

Use existing WinUI/XamlControlsResources theme resources where possible.

Do not introduce a new color system in this PR.

Do not resize standard WinUI controls.

Do not depend on:

- `Focus()`;
- keyboard focus rectangles;
- window activation;
- `SendInput`;
- synthetic keyboard events.

A simple row wrapper such as `Border` is acceptable for selection visual state.

Keep row geometry compatible with the current 400 DIP panel and 28 DIP content inset.

### Visual non-goal

This is not the final visual-polish PR.

Do not spend this PR on:

- final card shadows;
- complex hover animations;
- controller glyph art;
- custom ToggleSwitch templates;
- custom Slider templates;
- new typography scale.

---

## 8. Bring selected row into view

Whenever controller navigation changes row selection:

```text
selection changes
→ update selected-row visual
→ ensure selected element is visible within BodyScroll
```

Prefer the existing WinUI bring-into-view path, e.g. `FrameworkElement.StartBringIntoView(...)`, rather than manually calculating scroll offsets.

Use non-animated/minimal movement for controller navigation if needed so fast row movement remains predictable.

Do not:

- replace `BodyScroll`;
- calculate monitor/window physical coordinates for row scrolling;
- create a scrolling manager;
- run polling timers.

At first/last boundary where selection does not change, do not force redundant scroll movement.

---

## 9. Temporary navigation-validation rows

Because no production Quick Settings controls exist yet, add a small **temporary, explicitly non-feature navigation preview** only to make this PR hardware-testable.

Recommended scope:

- Device page only;
- enough simple rows to exceed the visible body height and prove scrolling;
- each row is selectable for navigation/highlight only;
- no Runtime mutation;
- no fake TDP/FPS/CPU values;
- A is a no-op;
- Left/Right are no-ops.

Visible labels should make the temporary nature obvious, e.g.:

```text
Navigation Preview 01
Navigation Preview 02
...
```

Use a simple vertical stack with normal spacing.

The rows exist only as development scaffolding and can be replaced/removed as OQ5-UI-05/06 and real Device controls arrive.

Do not create placeholder rows that pretend to be real product settings.

Other tabs may remain their existing placeholder/empty state with zero selectable rows.

---

## 10. OverlayWindow responsibilities

Keep UI state private to `OverlayWindow`.

Add narrow operations conceptually like:

```csharp
internal void NavigateUp();
internal void NavigateDown();
internal void AdjustSelectedRow(int delta);
internal void ActivateSelectedRow();
```

Or one narrow semantic dispatcher if it is simpler.

Responsibilities:

1. resolve active page row registrations;
2. normalize/advance selection;
3. apply visual selection state;
4. bring the new row into view;
5. invoke optional row activation/adjustment capability.

Do not expose private row collections to `App`.

Do not move tab state out of `OverlayWindow`.

---

## 11. App navigation dispatch

Extend the existing `App.HandleNavigationAsync()` switch only.

Conceptually:

```csharp
case OverlayNavigationAction.NavigateUp:
    _window?.NavigateUp();
    break;
case OverlayNavigationAction.NavigateDown:
    _window?.NavigateDown();
    break;
case OverlayNavigationAction.NavigateLeft:
    _window?.AdjustSelectedRow(-1);
    break;
case OverlayNavigationAction.NavigateRight:
    _window?.AdjustSelectedRow(+1);
    break;
case OverlayNavigationAction.Accept:
    _window?.ActivateSelectedRow();
    break;
```

Preserve:

```text
Back        → existing SendBackDismissAsync()
PreviousTab → existing SelectPreviousTab()
NextTab     → existing SelectNextTab()
```

Do not add another transport message or queue.

---

## 12. Tab-selection integration

Current `ApplySelectedTabVisualState()` also resets `BodyScroll` to top.

Refactor only as much as necessary so tab changes have deterministic ordering:

```text
select tab
→ show matching page
→ reset BodyScroll to top
→ choose first selectable row for new page
→ apply selected-row visual
```

Every fresh Show must do the same after `OverlayTabState.ResetForShow()` and before visual reveal.

Do not accidentally reset row selection merely because the selected-row highlight itself is refreshed.

Prefer separating:

- tab visual application;
- active-page row-selection reset;
- row visual application;

if the current method becomes ambiguous.

---

## 13. Protocol / Runtime / lifecycle non-changes

This PR should not change:

- `.Overlay` protocol version v4;
- `OverlayNavigationAction` enum;
- `OverlayControllerInputRouter`;
- stick thresholds/hysteresis;
- OQ4 consumed-control release gate;
- physical PID ownership;
- DirectInput ownership;
- HidHide;
- VIIPER;
- presentation pause/resume;
- Main UI ↔ Overlay retirement ordering;
- Steam QAM coexistence policy;
- outside-click dismissal;
- Overlay window geometry / animation.

If the implementation appears to require any of those, stop and re-evaluate the approach before expanding scope.

---

## 14. Tests

### 14.1 Selection-state tests

If a pure selection helper is introduced, add direct unit tests proving:

```text
no rows                 → no selection
first selectable exists → reset selects it
Down                     → next selectable
Up                       → previous selectable
disabled rows            → skipped
first + Up               → no-op / no wrap
last + Down              → no-op / no wrap
all rows unavailable     → selection clears
```

Also cover a row becoming unselectable between actions and selection normalizing to a valid row.

Do not test XAML implementation details such as dictionary enumeration order.

### 14.2 Capability dispatch tests

Add narrow non-XAML tests where practical proving:

- Left passes `-1` only to an adjustable selected row;
- Right passes `+1` only to an adjustable selected row;
- A invokes activation only for an activatable selected row;
- unsupported capability is a no-op;
- no selection means no mutation callback.

If the capability seam is kept entirely inside `OverlayWindow` and cannot be tested without standing up WinUI, keep the logic minimal and do not create a large UI-test framework solely for this PR.

### 14.3 Regression tests

Existing tests must remain green for:

- OQ5-UI-01 first-tab reset/default order;
- OQ5-UI-02 LB/RB bounded tab traversal;
- OQ5-UI-03 stick semantic generation/release gate;
- Overlay transport v4;
- OQ4 source-loss / capture retirement;
- presentation pause/resume.

No transport test changes should be needed unless an existing test asserts UI dispatch source text, which should be avoided.

---

## 15. Manual / hardware validation

Use the real Runtime → Overlay capture path on a supported MSI Claw.

Validate:

1. Open Overlay → first configured tab is selected.
2. Device page's first navigation-preview row is selected immediately.
3. DPad Down moves exactly one row per press.
4. DPad Up moves exactly one row per press.
5. Left Stick Up/Down moves rows using the same selection behavior.
6. Right Stick Up/Down moves rows using the same selection behavior.
7. At first row, Up does not wrap.
8. At last row, Down does not wrap.
9. Selected row has a clear visible highlight while the window remains no-activate.
10. Move far enough down that content scrolls → selected row is automatically brought into view.
11. Move back up → ScrollViewer follows selection predictably.
12. Left/Right on navigation-preview rows does nothing.
13. A on navigation-preview rows does nothing.
14. RB switches to the next tab and clears row selection if that page has no selectable rows.
15. LB returns to Device and selects its first preview row rather than restoring the old row position.
16. Pointer/touch tab selection has the same first-row reset behavior.
17. B still closes Overlay through the existing Runtime-owned path.
18. Outside click/touch still closes Overlay.
19. Steam QAM may remain visible behind the Overlay and must not receive controller navigation while Overlay capture is active.
20. Close while a DPad/button/bumper/stick is held → existing OQ4 release gate still prevents input leak before publisher resume.
21. Re-open Overlay after navigating deep down the list → first configured tab + first row again.

No hardware-only result should be claimed unless actually tested on the device.

---

## 16. Expected files

Primary expected changes:

```text
src/SteamInputAddonforClaw.Overlay/App.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
```

Likely small XAML change for row-preview container/style if useful:

```text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
```

Optional narrow pure-state file if used:

```text
src/SteamInputAddonforClaw.Overlay/OverlayRowSelectionState.cs
```

Tests under:

```text
tests/SteamInputAddonforClaw.Tests/
```

No Runtime lifecycle/router file is expected to change.

---

## 17. Build / verification

Required before PR completion:

```text
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test
git diff --check
```

If the full suite exposes a known unrelated flaky failure, reproduce it on unchanged current `main` before treating it as unrelated.

---

## 18. Acceptance criteria

The PR is complete when all of the following are true:

- [ ] `NavigateUp` / `NavigateDown` move one logical selected row.
- [ ] selection is bounded/no-wrap.
- [ ] unavailable rows are skipped.
- [ ] zero selectable rows is a valid state.
- [ ] selected-row state is visually obvious without HWND/keyboard focus.
- [ ] selected row is brought into `BodyScroll` view when navigation moves it off-screen.
- [ ] `NavigateLeft` dispatches adjustment `-1` only when supported.
- [ ] `NavigateRight` dispatches adjustment `+1` only when supported.
- [ ] `Accept` activates only when supported.
- [ ] unsupported row capabilities are no-ops.
- [ ] every tab switch selects that page's first selectable row.
- [ ] every fresh Overlay Show still resets to first configured tab and then first selectable row.
- [ ] Device page has temporary clearly-labeled navigation-preview rows sufficient to validate scrolling, with no fake feature mutation.
- [ ] other placeholder pages may remain without selectable rows.
- [ ] B remains global Runtime-owned Overlay close.
- [ ] LB/RB remain tab navigation.
- [ ] DPad and both sticks continue sharing the same semantic `Navigate*` path.
- [ ] `.Overlay` protocol remains v4.
- [ ] no Runtime controller/presentation/DirectInput/HidHide/VIIPER architecture is changed.
- [ ] no second navigation/focus manager is introduced.
- [ ] Release build passes.
- [ ] full test suite passes.
- [ ] `git diff --check` is clean.

---

## 19. Explicit non-goals

Do not implement in this PR:

- ToggleSwitch behavior;
- Slider behavior/value rendering;
- slider hold-repeat;
- QAM-style delayed slider commits;
- Runtime feature snapshot transport;
- TDP / CPU Boost / Power Mode / FPS mutation;
- tab-order persistence;
- tab-order editor;
- Shortcut tiles/actions;
- Profile real controls;
- Controller real controls;
- pointer-driven generalized row-selection framework;
- keyboard navigation framework;
- custom WinUI control templates/sizing;
- a generic `NavigationManager` / focus graph / view-model framework;
- protocol v5;
- any PID1902 / DirectInput / HidHide / VIIPER lifecycle change.

---

## 20. Overengineering guard

Protect the real UX requirements:

- selection must be deterministic;
- selected row must remain visible while navigating;
- no focus-steal dependency;
- tab changes must reset to a valid row;
- unsupported A/Left/Right actions must not mutate anything;
- existing OQ4 input isolation must remain intact.

Do **not** add complexity for hypothetical future layouts.

Target:

> one Overlay window, one active tab, zero-or-one logical selected row, one existing semantic input path, one existing ScrollViewer.
