# Work Order — Shortcut Editor 3-Column Layout + Steam-QAM-Like Overlay Tile Visual Polish

> **Date:** 2026-10-07  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed main:** `main@e0711dc5575df71ac5e28fc31994ba456abd2fab`  
> **Observed app build:** `0.1.337`  
> **Product architecture:** standalone Full1902  
> **PR shape:** one PR, two self-contained commits  
> **Scope:** Main App Shortcut editor layout only + Overlay Shortcut tile visual differentiation only  
> **Out of scope:** Shortcut persistence, execution, Runtime contracts, controller ownership, Full1902 lifecycle, Overlay navigation semantics

---

# 1. Goal

Fix two visible Shortcut UX problems observed in the current 0.1.337 build.

Current Main App:

~~~text
Shortcut editor
[ Screenshot      ][ Steam           ]
[ Steam Big Pic...][ Xbox            ]
~~~

Even though the product contract is three columns, the current Main App renders only two columns.

Current Overlay:

~~~text
Shortcut

[ SELECTED TILE ][ Steam ][ Steam Big Picture ]
[ Xbox          ]
~~~

The selected tile has a visible fill, but unselected tiles use the exact same background color as the Overlay content surface, so they visually disappear and look like floating text.

Target:

~~~text
Main App
[ tile 0 ][ tile 1 ][ tile 2 ]
[ tile 3 ][        ][        ]

Each card:
[ icon ][ title / summary ][ Edit   ]
                           [ Delete ]

Overlay
[ idle ][ focused ][ idle ]
[ idle ]

idle
  subtle dark/lightened surface that is visible against the page

focused
  stronger Steam-QAM-like fill change

No bright accent outline.
No glow.
No scale animation.
~~~

The work is visual/layout-only.

Do not change what a Shortcut means or how it executes.

---

# 2. Mandatory source review before implementation

Read current main before coding.

## 2.1 Full1902 authority

Read at minimum:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md
~~~

This PR must remain independent from:

~~~text
PID1901 / PID1902 ownership
HidHide
VIIPER
DirectInput
SteamDeck / Xbox360 presentation switching
Sleep / Hibernate / Resume
Center M authority
routing rollback
WING suppression
~~~

Do not add a lifecycle owner, gate, state machine, timer, retry loop, or manager for this UI work.

## 2.2 Shortcut authority

Read:

~~~text
docs/work-order/1007_SHORTCUT_3_COLUMN_BUILTIN_ACTIONS_WORK_ORDER.md
docs/work-order/SHORTCUT_FOUNDATION_PR_D_MAIN_APP_SHORTCUT_EDITOR_WORK_ORDER_2026-09-24.md
docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR4_SHORTCUT_PARITY_WORK_ORDER.md
~~~

Inspect at minimum:

~~~text
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs

src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shortcuts.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml

tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayQamVisualResourcesTests.cs
~~~

---

# 3. External UI reference — Steam QAM / Decky

This PR should follow the visual interaction direction already used by Steam Gamepad UI / QAM and exposed/reused by Decky.

Reviewed reference sources:

~~~text
SteamDeckHomebrew/decky-frontend-lib
  src/utils/static-classes.ts
  src/components/Focusable.ts
  src/components/Field.ts
  src/components/FocusRing.ts

SteamDeckHomebrew/decky-loader
  frontend/src/components/settings/pages/plugin_list/index.tsx
~~~

Relevant evidence:

~~~text
quickAccessMenuClasses
  ItemFocusAnim-darkGrey
  ItemFocusAnim-darkerGrey
  ItemFocusAnim-grey
  ItemFocusAnim-translucent-white-10
  ItemFocusAnim-translucent-white-20
  ItemFocusAnimBorder-darkGrey
  focusAnimation
  hoverAnimation

gamepadDialogClasses
  HighlightOnFocus

Focusable
  focusClassName
  focusWithinClassName

Decky UI
  wraps Steam-native DialogButton controls in Focusable
~~~

The product direction inferred from this evidence is:

> focus is primarily communicated by a fill/surface change, not by a bright Windows-style accent outline.

Important:

The exact alpha values below are **our local product choice**, not a claim that they are exact Steam computed colors.

Do not attempt to inject Steam CSS or depend on Decky.

This is a visual reference only.

---

# 4. Reviewed current implementation

## 4.1 Main App currently does not actually guarantee three columns

Current `ShortcutPage.xaml`:

~~~xml
<ItemsWrapGrid
    Orientation="Horizontal"
    MaximumRowsOrColumns="3" />
~~~

The current item template has no explicit stable three-column item extent.

Field screenshot from 0.1.337 proves the actual result is:

~~~text
4 items
→ 2 columns × 2 rows
~~~

Therefore:

~~~text
MaximumRowsOrColumns="3"
!=
three equal-width columns
~~~

for the current editor surface.

The previously documented product contract remains:

~~~text
Main App position 0/1/2
→ Overlay row 0 column 0/1/2

Main App position 3/4/5
→ Overlay row 1 column 0/1/2
~~~

The fix must make the Main App visually honor that contract.

## 4.2 Horizontal Edit/Delete buttons consume excessive card width

Current card:

~~~xml
<StackPanel
    Grid.Column="2"
    Orientation="Horizontal"
    Spacing="8">

    <Button Content="Edit" />
    <Button Content="Delete" />
</StackPanel>
~~~

This causes the action area to consume the combined width of both buttons and reduces the title/summary column enough that normal names render as:

~~~text
Screensh...
Steam Bi...
~~~

This is unnecessary.

## 4.3 Overlay idle Shortcut brush is identical to the page background

Current resources:

~~~xml
QamContentBrush      = #FF0E141B
QamTileBrush         = #FF0E141B
QamTileSelectedBrush = #26FFFFFF
~~~

Therefore an unselected tile is literally painted the same color as the page.

The selected tile is visible, while normal tiles look like bare text.

The current selected fill itself is acceptable and should remain the stronger state.

---

# 5. PR structure — one PR, two commits

Implement this as **one PR with exactly two logical commits**.

Recommended commit order:

~~~text
Commit 1
fix(shortcut-ui): enforce three-column editor cards and stack actions

Commit 2
style(overlay): distinguish shortcut tiles with QAM-like idle and focus fills
~~~

Each commit should leave the tree buildable.

Do not combine these into one monolithic commit.

Do not add a third refactor/cleanup commit.

---

# 6. Commit 1 — Main App three-column editor + vertical action buttons

## 6.1 Expected files

Production:

~~~text
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs
    only if needed for the stable item-width calculation
~~~

Tests:

~~~text
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
~~~

Do not touch Runtime Shortcut code.

## 6.2 Preserve the existing editor control and reorder path

Keep:

~~~text
ListView
CanDragItems=True
CanReorderItems=True
AllowDrop=True
ShortcutList_DragItemsCompleted
ordered _tiles collection
Runtime Move mutation
~~~

Do not replace the editor with:

- ItemsRepeater;
- custom drag/drop;
- a new reorder controller;
- a new persisted Row/Column model;
- a layout manager abstraction.

The current ListView remains the owner.

## 6.3 Enforce an actual three-column visual extent

The final result must be:

~~~text
1 item
[ tile ][      ][      ]

2 items
[ tile ][ tile ][      ]

3 items
[ tile ][ tile ][ tile ]

4 items
[ tile ][ tile ][ tile ]
[ tile ][      ][      ]
~~~

The current `MaximumRowsOrColumns="3"` alone is insufficient and must not be treated as the fix.

Preferred minimal implementation:

1. keep the current `ItemsWrapGrid`;
2. retain row-major list order;
3. explicitly set the item extent so three equal slots fit the current `ShortcutList` viewport;
4. update that extent when the Shortcut list viewport width changes;
5. never switch to a 2-column or 1-column breakpoint.

A narrow implementation is acceptable, for example:

~~~text
ShortcutList.SizeChanged
→ calculate available editor width
→ reserve two inter-card gaps
→ divide remaining width by 3
→ set ItemsWrapGrid.ItemWidth
~~~

Use the existing card margin/gap in the calculation.

Do not add a general responsive layout service.

Do not calculate physical pixels manually.

Use WinUI DIPs / `ActualWidth`.

### Required invariant

~~~text
three columns are the product layout

window width changes
→ card width changes

window width changes
→ column count does NOT change
~~~

If the viewport is unusually narrow, horizontal clipping/normal app window constraints are preferable to silently changing the logical Shortcut arrangement to 2 columns.

Do not introduce responsive column-count breakpoints in this PR.

## 6.4 Keep card height content-driven

The Main App cards are editor cards, not Overlay square tiles.

Do not make them square.

Do not copy:

~~~text
QamShortcutTileSize
117.333333 DIP
~~~

into the Main App.

The Main App only needs the same **three-column ordering**, not identical tile geometry.

## 6.5 Stack Edit/Delete vertically

Replace:

~~~text
[ Edit ][ Delete ]
~~~

with:

~~~text
[ Edit   ]
[ Delete ]
~~~

Recommended XAML direction:

~~~xml
<StackPanel
    Grid.Column="2"
    Orientation="Vertical"
    Spacing="8"
    VerticalAlignment="Center">
    <Button
        Content="Edit"
        HorizontalAlignment="Stretch"
        ... />
    <Button
        Content="Delete"
        HorizontalAlignment="Stretch"
        ... />
</StackPanel>
~~~

Use equal button width through the stack/content sizing naturally.

Do not create custom button templates.

Do not move Delete into a context menu.

Do not remove either action.

## 6.6 Preserve useful text width

The card still has:

~~~text
drag icon
title
target summary
validation message
Edit/Delete
~~~

The title/summary column must remain the flexible `*` column.

The vertically stacked action column should consume only the width of the widest single button, not both buttons combined.

Expected improvement:

~~~text
Before
Screensh...
Steam Bi...

After
Screenshot
Steam Big Picture
~~~

Long user-defined titles may still ellipsize normally.

Do not remove `TextTrimming`.

## 6.7 Preserve editor behavior

Must remain unchanged:

- Add Shortcut;
- Edit;
- Delete;
- drag reorder;
- screenshot-folder controls;
- busy-state enable/disable;
- frontend refresh;
- action validation;
- default built-in titles;
- Runtime mutation contract.

No Shortcut schema or protocol change is required.

---

# 7. Commit 2 — Overlay Shortcut idle/focus surface polish

## 7.1 Expected files

Production:

~~~text
src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shortcuts.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
    only if the existing selected/unselected resource assignment needs a naming adjustment
~~~

Tests:

~~~text
tests/SteamInputAddonforClaw.UiTests/OverlayQamVisualResourcesTests.cs
~~~

No Runtime files.

## 7.2 Keep the existing selection architecture

Current behavior is already correct:

~~~text
_shortcutSelection
→ selected TileId / index

ApplyShortcutSelectionVisual()
→ selected ? QamTileSelectedBrush : QamTileBrush
~~~

Keep this exact ownership model.

Do not add:

- pointer-hover selection state;
- focus state object;
- animation manager;
- per-tile view model;
- another selection enum;
- another brush resolver abstraction.

This PR changes resources, not authority.

## 7.3 Give idle tiles a subtle visible surface

Current:

~~~xml
QamContentBrush = #FF0E141B
QamTileBrush    = #FF0E141B
~~~

Change the normal Shortcut tile fill so it is slightly visible against the page.

Recommended local target:

~~~xml
<SolidColorBrush
    x:Key="QamTileBrush"
    Color="#0DFFFFFF" />
~~~

`0x0D` is roughly a 5% white overlay.

This is intentionally subtle.

The normal tile should be visible as a tile, but should not compete with the focused tile.

## 7.4 Keep the stronger selected/focused fill

Keep the current stronger focus fill unless manual validation proves it too weak:

~~~xml
<SolidColorBrush
    x:Key="QamTileSelectedBrush"
    Color="#26FFFFFF" />
~~~

This is roughly a 15% white overlay and already reads well in the current screenshot.

Target contrast hierarchy:

~~~text
page
  #FF0E141B

idle Shortcut
  subtle translucent light surface

selected Shortcut
  clearly stronger translucent light surface
~~~

Do not replace the selected fill with Steam blue.

The Steam blue remains appropriate for semantic controls such as the current toggle-on resource, not generic Shortcut focus.

## 7.5 No bright focus border

Keep:

~~~text
QamSelectionBorderThickness = 0
QamFocusBorderBrush          = transparent
~~~

Do not add:

- blue border;
- white 2px focus rectangle;
- glow;
- shadow;
- scale-up animation.

The desired QAM direction is a surface/fill change.

This is consistent with the reviewed Steam/Decky focus model where `HighlightOnFocus` / `ItemFocusAnim-*` classes communicate controller focus primarily through item surface treatment.

## 7.6 Do not change tile geometry

Keep:

~~~text
ShortcutVisualColumnCount = 3
QamShortcutTileSize       = 117.33333333333333 DIP
QamTileSpacing            = 8 DIP
QamTileCornerRadius       = 2
QamTilePadding            = 12
~~~

Do not change:

- 3-column Overlay layout;
- square tile dimensions;
- page title spacing;
- rail width;
- content padding.

This commit is visual state differentiation only.

## 7.7 Preserve disabled semantics

Current disabled behavior:

~~~text
tile.Enabled
  true  → opacity 1.0
  false → QamDisabledOpacity
~~~

Keep it.

A disabled unselected tile should still use the idle tile surface and disabled opacity.

A disabled tile must remain non-executable.

Do not add another disabled fill.

---

# 8. Tests

Keep tests source-focused and small.

Do not create screenshot/golden-image infrastructure for this PR.

## 8.1 Main App layout tests

Update/add a narrow architecture assertion proving:

~~~text
Shortcut editor still uses ListView
reorder remains enabled
three-column item extent is explicitly owned
column count does not rely only on MaximumRowsOrColumns
Edit/Delete action stack is Vertical
~~~

If the implementation uses a `SizeChanged` handler, assert the XAML event wiring and the narrow code-behind method.

Do not test exact pixel/DIP width math through a full WinUI window host if no such infrastructure already exists.

## 8.2 Overlay visual resources tests

Assert:

~~~text
QamContentBrush != QamTileBrush
QamTileBrush == #0DFFFFFF
QamTileSelectedBrush == #26FFFFFF

selected/unselected still map through:
  selected ? QamTileSelectedBrush : QamTileBrush

QamSelectionBorderThickness remains 0
QamFocusBorderBrush remains transparent
~~~

Also preserve the existing assertion that Shortcut consumes the shared QAM resource authority.

Do not loosen unrelated QAM visual tests.

---

# 9. Manual validation

Use a current build at the existing MSI Claw reference environment.

## A. Main App — 4 built-ins

Create/retain:

~~~text
Screenshot
Steam
Steam Big Picture
Xbox
~~~

Expected:

~~~text
row 0
[ Screenshot ][ Steam ][ Steam Big Picture ]

row 1
[ Xbox       ][       ][                  ]
~~~

Not expected:

~~~text
[ Screenshot ][ Steam ]
[ Steam Big Picture ][ Xbox ]
~~~

## B. Main App — action buttons

Expected each card:

~~~text
                      [ Edit   ]
title / summary       [ Delete ]
~~~

Verify:

- the card becomes narrower;
- title/summary receives more horizontal space;
- `Steam Big Picture` is materially less likely to truncate;
- Edit/Delete still work;
- drag reorder still works.

## C. Main App — reorder parity

Move items.

Example:

~~~text
before
0 Screenshot
1 Steam
2 Steam Big Picture
3 Xbox

after
0 Xbox
1 Screenshot
2 Steam
3 Steam Big Picture
~~~

Verify Overlay shows the exact same row-major placement:

~~~text
[ Xbox ][ Screenshot ][ Steam ]
[ Steam Big Picture ]
~~~

## D. Overlay — idle state

With four shortcuts, verify all four square tile boundaries are perceptible even when not selected.

Expected:

~~~text
idle tile
→ subtle surface visible

page
→ darker than idle tile
~~~

The idle cards must not look like plain floating text.

## E. Overlay — controller focus

Navigate with D-pad.

Expected:

~~~text
selected tile
→ stronger fill

previous tile
→ returns to subtle idle fill
~~~

Not expected:

- blue outline;
- glow;
- scale pop;
- Windows accent rectangle.

## F. Overlay — execution

Verify:

~~~text
A
→ selected enabled Shortcut executes once

pointer/touch
→ selects + executes existing path

disabled
→ remains non-executable
~~~

No behavior change is expected.

---

# 10. Expected final diff

Likely production changes:

~~~text
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs

src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml
~~~

Possible narrow change if required by resource naming/wiring:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shortcuts.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
~~~

Tests:

~~~text
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayQamVisualResourcesTests.cs
~~~

Expected **no** changes to:

~~~text
src/SteamInputAddonforClaw/Shortcuts/*
src/SteamInputAddonforClaw.Contracts/Frontend/Shortcut*
src/SteamInputAddonforClaw.FrontendTransport/*

src/SteamInputAddonforClaw/Devices/*
src/SteamInputAddonforClaw/HidHide/*
src/SteamInputAddonforClaw/VirtualOutput/*
src/SteamInputAddonforClaw/Input/*
src/SteamInputAddonforClaw/Profiles/*
~~~

If the implementation starts modifying those areas, re-check scope.

---

# 11. Validation commands

Run at minimum:

~~~text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
git diff --check
~~~

Review the final diff for accidental Runtime/protocol behavior changes.

---

# 12. Acceptance criteria

## Commit 1 — Main App

- [ ] Shortcut editor visibly renders exactly three logical columns at the normal app window size.
- [ ] Four items render 3 + 1, not 2 + 2.
- [ ] One/two items do not expand to half/full-width preview semantics.
- [ ] Card widths are equal within the three-column editor.
- [ ] Window width changes adjust card extent without changing the product column count.
- [ ] Edit/Delete are vertically stacked.
- [ ] Edit/Delete remain functional.
- [ ] Title/summary width is increased relative to the current horizontal-button layout.
- [ ] Drag reorder remains functional.
- [ ] Main App order still maps row-major to Overlay order.
- [ ] No persistence/schema/protocol change.

## Commit 2 — Overlay

- [ ] Idle Shortcut tile fill is visibly different from `QamContentBrush`.
- [ ] Idle fill remains subtle.
- [ ] Selected tile uses the stronger existing selected fill.
- [ ] Selection is communicated by fill, not an accent border.
- [ ] No glow/scale animation is added.
- [ ] Three-column square geometry is unchanged.
- [ ] D-pad navigation is unchanged.
- [ ] Tile execution is unchanged.
- [ ] Disabled semantics are unchanged.
- [ ] No new selection/focus state authority is introduced.

## Whole PR

- [ ] Exactly two logical commits.
- [ ] Shortcut Runtime behavior is unchanged.
- [ ] Full1902 controller lifecycle is untouched.
- [ ] Overlay transport/protocol is untouched.
- [ ] All tests pass.

---

# 13. Overengineering guard

This PR has two concrete visual defects with direct local fixes.

Do not turn them into a general UI framework project.

Desired implementation:

~~~text
Main App
  existing ListView
  + explicit three-column item extent
  + vertical action stack

Overlay
  existing tile resources
  + subtle idle fill
  + existing stronger selected fill
~~~

Do not add:

- a responsive grid manager;
- a reusable card-layout service;
- a new view-model layer;
- another focus controller;
- animation infrastructure;
- a Steam CSS compatibility layer;
- Decky runtime dependency;
- duplicated Shortcut state.

The target is:

> **Make the Main App accurately preview the existing three-column Shortcut order, and make Overlay Shortcut tiles readable using the same restrained fill-based focus language as Steam QAM / Decky.**
