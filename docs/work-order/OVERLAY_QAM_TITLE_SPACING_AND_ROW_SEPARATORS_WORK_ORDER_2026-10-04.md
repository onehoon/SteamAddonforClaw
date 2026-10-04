# Work Order — Overlay QAM Title Breathing Room and Row Separators

**Date:** 2026-10-04  
**Status:** Ready for implementation  
**Target repository:** `onehoon/SteamAddonforClaw`  
**Target branch:** `main`  
**Reviewed main baseline:** `8e155e9effa7c7a60cc4373e30b5ebef41379949`  
**Scope:** Overlay presentation polish only  
**Implementation shape:** one focused PR

---

## 0. Goal

Polish the current standalone Addon Overlay so its vertical hierarchy reads closer to Steam QAM without making the Overlay taller or loosening the compact option layout.

The user-visible changes are intentionally narrow:

1. give the canonical page title more breathing room above and, especially, below;
2. add a thin, low-contrast horizontal separator between ordinary option rows;
3. keep the current row padding, row spacing, section spacing, controller navigation, pointer behavior, and page architecture unchanged.

Reference direction:

~~~text
Page title

    slightly more top breathing room
    clearly more space below the title

Option A
────────────────

Option B
────────────────

Option C
────────────────

SECTION HEADER
...
~~~

The separator is a visual boundary, not extra vertical whitespace.

---

## 1. Mandatory project review

Before editing, read the current versions of:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md
docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md
docs/work-order/OVERLAY_QAM_VISUAL_FOUNDATION_PR_A_WORK_ORDER_2026-10-02.md
docs/work-order/OVERLAY_QAM_CONTROLS_PR_B_WORK_ORDER_2026-10-02.md
docs/work-order/OVERLAY_QAM_PAGE_POLISH_PR_C_WORK_ORDER_2026-10-02.md
~~~

This PR is presentation-only.

Do not modify:

- Full1902 authority/lifecycle;
- PID1901/PID1902 handling;
- HidHide;
- VIIPER;
- DirectInput;
- Xbox360/SteamDeck presentation;
- Overlay capture/release;
- transport contracts;
- Quick Settings semantic contracts;
- M1/M2 behavior;
- profile/runtime mutation policy;
- tab-order behavior;
- show/hide/no-activate behavior.

---

## 2. Mandatory source review

Inspect current main before changing code:

~~~text
src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
src/SteamInputAddonforClaw.Overlay/OverlayRowChrome.cs

src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayValueRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayNumericSliderRow.cs

src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs

tests/SteamInputAddonforClaw.UiTests/OverlayQamVisualResourcesTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
~~~

If main has moved, adapt this work order to the current implementation. Do not mechanically restore an older structure.

---

## 3. Current code facts

At the reviewed baseline:

### 3.1 Page-title spacing

`OverlayWindow.xaml` renders one fixed page title above the shared body `ScrollViewer`.

Current theme values:

~~~xml
<Thickness x:Key="QamPageTitleMargin">16,16,16,8</Thickness>
<Thickness x:Key="QamBodyContentPadding">16,0,16,12</Thickness>
~~~

Because the body has no top padding, the effective title-to-first-content gap is driven primarily by the title's current 8-DIP bottom margin.

This is why the first option visually sits too close to the page title.

### 3.2 Ordinary rows are already compact

Current ordinary-row metrics are:

~~~xml
<x:Double x:Key="QamRowSpacing">0</x:Double>
<Thickness x:Key="QamRowPadding">16,10,16,10</Thickness>
<Thickness x:Key="QamRowMargin">-16,0,-16,0</Thickness>
<x:Double x:Key="QamRowMinHeight">42</x:Double>
~~~

Do not increase these values in this PR.

### 3.3 A separator resource already exists but is transparent

Current:

~~~xml
<SolidColorBrush x:Key="QamSeparatorBrush" Color="#00000000" />
~~~

Reuse this semantic resource instead of inventing another separator color system.

### 3.4 Row selection is fill-based

`OverlayRowChrome.Create(...)` currently initializes the row border from the selection-border resources, while `ApplyRowSelectionVisual()` mutates both row background and row border brush.

However, the current selection border thickness is zero, so the actual visible row selection is the existing selected fill.

The separator must not become selected/accent-colored when controller selection moves.

---

## 4. Required title-spacing change

Change only the canonical page-title margin:

~~~xml
<Thickness x:Key="QamPageTitleMargin">16,20,16,16</Thickness>
~~~

This means:

~~~text
left   16 DIP   unchanged
top    20 DIP   +4
right  16 DIP   unchanged
bottom 16 DIP   +8
~~~

Keep:

~~~xml
<Thickness x:Key="QamBodyContentPadding">16,0,16,12</Thickness>
~~~

unchanged.

Do not add a second title wrapper, extra spacer element, page-specific title margin, or per-tab title metric.

The single `PageTitle` in `OverlayWindow.xaml` remains the only page-title authority.

---

## 5. Required option-row separator

### 5.1 Separator appearance

Change the existing separator brush to a subtle white rule:

~~~xml
<SolidColorBrush x:Key="QamSeparatorBrush" Color="#1AFFFFFF" />
~~~

This is intentionally lower contrast than the selected-row fill.

Add one theme metric:

~~~xml
<Thickness x:Key="QamRowSeparatorThickness">0,0,0,1</Thickness>
~~~

The separator must:

- be 1 DIP;
- use `QamSeparatorBrush`;
- not add any explicit margin;
- not add a new layout row;
- not increase `QamRowSpacing`;
- not increase `QamRowPadding`;
- scale naturally with DPI.

### 5.2 Use the existing row chrome owner

Implement the separator in `OverlayRowChrome.Create(...)`.

Conceptually:

~~~csharp
internal static Border Create(UIElement child) => new()
{
    Child = child,
    Padding = OverlayQamResources.Get(
        "QamRowPadding",
        new Thickness(16, 10, 16, 10)),
    Margin = OverlayQamResources.Get(
        "QamRowMargin",
        new Thickness(-16, 0, -16, 0)),
    MinHeight = OverlayQamResources.Get("QamRowMinHeight", 42.0),
    CornerRadius = OverlayQamResources.Get(
        "QamRowCornerRadius",
        new CornerRadius(2)),

    BorderThickness = OverlayQamResources.Get(
        "QamRowSeparatorThickness",
        new Thickness(0, 0, 0, 1)),
    BorderBrush = OverlayQamResources.Brush("QamSeparatorBrush"),

    Background = OverlayQamResources.Brush("QamContentBrush"),
};
~~~

Exact formatting may follow current code style.

Do not add a separator `Rectangle`/child element between rows. The Border edge is enough and avoids changing row count, navigation indices, or vertical spacing.

---

## 6. Preserve selection independently from the separator

The ordinary row separator must remain the same color whether a row is selected or not.

Update `ApplyRowSelectionVisual()` so ordinary rows change only their background:

~~~csharp
private void ApplyRowSelectionVisual()
{
    if (!_pageRows.TryGetValue(_tabState.SelectedTab, out var rows))
        return;

    var selectedIndex = _rowSelection.SelectedIndex;
    for (var i = 0; i < rows.Count; i++)
    {
        var selected = i == selectedIndex;
        rows[i].Container.Background =
            selected ? _rowSelectedFillBrush : RowUnselectedFillBrush;
    }
}
~~~

Do not overwrite `rows[i].Container.BorderBrush` from the selection path.

This keeps:

~~~text
selection authority  = selected background fill
separator authority  = OverlayRowChrome border
~~~

separate without adding a new state object or visual wrapper.

Important:

- `_rowSelectedBrush` is still used by Profile catalog / Shortcut tile presentation; do not remove it merely because ordinary rows stop using it.
- remove `RowUnselectedBrush` only if it becomes truly unused after the change.
- do not change `OverlayRowSelection`, `OverlayRowCapabilities`, or controller navigation.

---

## 7. Separator scope

The separator applies to ordinary linear option rows created through `OverlayRowChrome`.

This includes the current shared row primitives used by:

- Device Quick Settings rows;
- Profile detail Quick Settings rows;
- Controller option rows;
- Setting detail rows;
- Setting expandable headers that already use ordinary row chrome.

Do not apply this PR's row separator to:

- left rail items;
- Shortcut tiles;
- Profile catalog tiles/cards;
- the overall page title;
- section heading text by itself;
- the outer Overlay surface.

Do not add special "hide the separator on the final row" bookkeeping in this PR.

A final row retaining the subtle bottom rule is acceptable and also provides a clean boundary before the next section. Avoid introducing last-row flags, per-section separator ownership, or dynamic edge-state tracking solely to remove that final rule.

---

## 8. Preserve compact vertical density

The purpose of the separator is specifically to improve option separation **without increasing option-to-option vertical spacing**.

Therefore keep unchanged:

~~~text
QamRowSpacing            = 0
QamRowPadding            = 16,10,16,10
QamRowMinHeight          = 42
QamSectionSpacing        = 24
QamSectionHeaderSpacing  = 4
QamBodyContentPadding    = 16,0,16,12
~~~

Do not compensate for the line by adding top/bottom padding.

Do not increase the height of Toggle, Slider, value-stepper, or discrete-choice controls.

---

## 9. Tests

Update the existing UI source-contract tests rather than adding a new test framework.

At minimum:

### `OverlayQamVisualResourcesTests`

Update the expected title margin:

~~~csharp
AssertResourceValue(resources, "QamPageTitleMargin", "16,20,16,16");
~~~

Add explicit assertions for:

~~~text
QamSeparatorBrush == #1AFFFFFF
QamRowSeparatorThickness == 0,0,0,1
~~~

Verify `OverlayRowChrome.cs` consumes both:

~~~text
QamSeparatorBrush
QamRowSeparatorThickness
~~~

and still consumes:

~~~text
QamRowPadding
QamRowMargin
QamRowMinHeight
QamRowCornerRadius
~~~

### Selection visual contract

Add or update a source-contract assertion that ordinary-row selection no longer assigns:

~~~text
rows[i].Container.BorderBrush
~~~

inside `ApplyRowSelectionVisual()`.

Keep assertions proving selected fill remains:

~~~text
QamSelectedFillBrush
_rowSelectedFillBrush
rows[i].Container.Background
~~~

Do not weaken unrelated Profile/Shortcut tile tests that still use `_rowSelectedBrush`.

---

## 10. Hardware/manual validation

Validate on the normal Claw target display:

~~~text
1920 x 1200
150% Windows scaling
~~~

Check all five Overlay tabs.

Required visual result:

~~~text
1. Page title no longer feels pinned to the top edge.
2. The gap below the page title is visibly larger than before.
3. The first option no longer appears attached to the title.
4. Ordinary options have a subtle 1-DIP horizontal rule between them.
5. The line is visible but clearly lower contrast than selected-row fill.
6. Moving controller selection does not recolor/disappear the separator.
7. Row height and option density feel unchanged.
8. No extra scrollbar or layout overflow is introduced.
9. Device/Profile/Controller/Setting rows remain aligned.
10. Shortcut/Profile catalog tiles are unchanged.
~~~

Also verify:

~~~text
controller Up/Down
controller Left/Right
A/Accept
pointer/touch row selection
slider drag
toggle click
Setting expand/collapse
tab switching
scroll-to-selected-row
~~~

remain behaviorally unchanged.

---

## 11. Non-goals

Do not use this PR to:

- change fonts;
- resize the page title;
- change rail geometry;
- change Overlay width/position;
- change section spacing;
- redesign cards/tiles;
- restyle Toggle/Slider geometry;
- change selected-row opacity;
- add gradients/shadows;
- add animation;
- add a generalized separator component;
- add a new page/row abstraction;
- refactor Quick Settings rendering;
- alter runtime or transport state.

---

## 12. Acceptance criteria

The PR is complete when:

1. `QamPageTitleMargin` is `16,20,16,16`.
2. The body padding remains unchanged.
3. Ordinary row padding/spacing/min-height remain unchanged.
4. `QamSeparatorBrush` is a subtle visible low-alpha white.
5. Ordinary `OverlayRowChrome` rows render a 1-DIP bottom separator.
6. Controller selection changes row background only and does not overwrite the separator brush.
7. Tile/catalog visuals are unchanged.
8. Existing interaction/navigation semantics are unchanged.
9. UI tests are updated and pass.
10. Hardware review at 1920x1200 / 150% confirms the title breathes better and rows are easier to distinguish without making the page feel more vertically spread out.

---

## 13. Implementation principle

Keep this as a small visual polish patch.

Preferred ownership remains:

~~~text
QamOverlayResources.xaml
    -> visual values

OverlayRowChrome
    -> ordinary row chrome

OverlayWindow.Navigation
    -> selected-row fill only

OverlayWindow.xaml
    -> one canonical page title + one body ScrollViewer
~~~

Do not introduce another wrapper, manager, state object, or generalized styling layer for this change.
