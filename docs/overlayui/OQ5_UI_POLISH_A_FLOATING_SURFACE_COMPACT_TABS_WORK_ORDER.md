# OQ5 UI Polish A — Floating Surface / Compact Tabs / Clean Body

> **Status:** Implementation work order  
> **Prepared:** 2026-09-04  
> **Target repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed main baseline:** `99698d7b860796c9445937bdc586edb75fa46fcb`  
> **Scope:** Overlay presentation polish only. No Runtime/controller/lifecycle behavior change.

---

## 1. Goal

Polish the current `SteamInputAddonforClaw.Overlay` Quick Settings shell so it visually reads as a small floating handheld quick-settings surface instead of a desktop settings panel stuck to the work-area edge.

The approved direction is intentionally simple:

```text
~5 px visual separation from the work-area top / left / bottom
+ smaller Quick Settings title
+ compact modern horizontal tabs
+ balanced inner left/right spacing
+ no persistent footer hint
+ no visible scrollbar
+ no inner body card/box
+ subtle but clear controller row selection
```

This is a **single presentation-polish PR**.

Do not use this PR to redesign the Overlay architecture, add new feature pages, or change controller/capture ownership.

---

## 2. Mandatory references before editing

Read these documents together before changing code:

### Full PID1902 authority

1. `docs/Full 1902 Implementation/README.md`
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
4. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`

The authority order in the Full1902 README remains unchanged.

### Overlay UI contract

5. `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
6. `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
7. `docs/overlayui/OQ5_UI_04_LOGICAL_ROW_SELECTION_SCROLLING_WORK_ORDER.md`
8. `docs/overlayui/OQ5_UI_05_TOGGLE_ROW_PRIMITIVE_WORK_ORDER.md`
9. `docs/overlayui/OQ5_UI_06_SLIDER_ROW_PRIMITIVE_WORK_ORDER.md`
10. `docs/work-order/OQ_UX_A_OVERLAY_OUTSIDE_CLICK_DISMISS_AND_SURFACE_TONE_WORK_ORDER.md`

If `main` has moved from the reviewed SHA before implementation, re-read the affected current code and adapt this work order to the new implementation rather than restoring old structure mechanically.

---

## 3. Current code facts at the reviewed baseline

### 3.1 Window geometry

`src/SteamInputAddonforClaw.Overlay/OverlayWindowGeometry.cs` currently uses:

```text
PocPanelWidthDip = 400
X = workLeft
Y = workTop
Height = full workHeight
```

So the Overlay is intentionally flush with the work-area left/top and reaches the work-area bottom.

`WindowInterop.Configure()` uses `OverlayWindowGeometry.Calculate(...)` for the final native rectangle. Preserve this single geometry authority.

### 3.2 XAML shell

`OverlayWindow.xaml` currently has:

```text
OpaquePanel Padding = 28
AnimatedContent RowSpacing = 16
Quick Settings = TitleTextBlockStyle
five equal-width tab columns
BodyScroll VerticalScrollBarVisibility = Auto
FooterHint = "LB/RB Tabs  B Close"
```

The fourth row exists only for the footer hint.

### 3.3 Tabs

`OverlayWindow.BuildShell()` creates five ordinary WinUI `Button` instances with small padding, while `ApplySelectedHeaderVisual()` only changes `FontWeight`.

Keep the existing:

- fixed tab identities;
- persisted/tab-order authority;
- startup-tab behavior;
- page instances;
- LB/RB navigation;
- click handling.

Only change their visual treatment.

### 3.4 Body rows and selection

Toggle and Slider rows already use standard WinUI controls and reusable row primitives.

Current regular-row selection is rendered by changing the entire row `BorderBrush` to the accent brush. Because the containers use a 2-DIP border, this creates the large blue rectangular selection outline visible in the current build.

The logical selection model itself is correct and must remain intact.

### 3.5 Scrolling

`BringSelectedRowIntoView()` already programmatically scrolls the selected logical row into view.

The scrollbar can therefore be hidden visually without disabling scrolling.

---

## 4. Required implementation

## 4.1 Add only a very small outer work-area inset

Keep the existing panel width baseline:

```text
400 DIP
```

Do **not** make the Overlay noticeably narrower or wider in this PR.

Add one narrow geometry constant, for example:

```csharp
internal const double PanelEdgeInsetDip = 4.0;
```

The exact name may follow current naming conventions, but keep one authority for this value.

At 150% DPI, 4 DIP becomes approximately 6 physical pixels, which matches the intended "about 5 px" separation. The requested effect is only a slight floating separation, not a large desktop-window margin.

Conceptual final geometry:

```text
insetPx = round(PanelEdgeInsetDip * dpi / 96)

X      = workLeft + insetPx
Y      = workTop  + insetPx
Width  = existing 400-DIP width, clamped to remaining work area
Height = workHeight - (2 * insetPx)
```

Requirements:

- apply the inset on **left, top, and bottom**;
- keep the panel left-anchored;
- preserve DPI-aware geometry;
- safely clamp for unusually small work areas;
- do not add a general geometry/margin framework;
- do not add another native-window placement authority;
- do not change monitor-selection policy.

`WindowInterop.Configure()` may need only the minimum adjustment required so its provisional placement remains compatible with the final rectangle. Do not refactor native interop merely for symmetry.

---

## 4.2 Keep one clean surface — no inner body card

Keep `OpaquePanel` as the one main surface and retain the current neutral surface tone unless an existing ThemeResource can reproduce it without changing the approved appearance.

Do **not** add:

- a body `Card`;
- a second large rounded `Border` around the controls;
- a nested panel background;
- a dashboard/card framework.

Reduce the current oversized internal padding from 28 DIP to a compact balanced value.

Target starting point:

```xml
Padding="16,14,16,16"
```

Small hardware-polish adjustment is acceptable, but the final result must have visibly balanced left/right content margins.

`AnimatedContent.RowSpacing` may be reduced from 16 to approximately 12 if needed for the approved compact layout. Do not compress touch/controller targets to save space.

---

## 4.3 Reduce the `Quick Settings` title size locally

The current `TitleTextBlockStyle` is too large for this 400-DIP transient surface.

Use local Overlay-only typography rather than changing global WinUI typography resources.

Target:

```text
FontSize: about 24 DIP
FontWeight: SemiBold
```

A small adjustment around this value is acceptable after 150% hardware review.

Do not create a new application-wide typography scale for this PR.

---

## 4.4 Restyle the five existing tab Buttons into a compact selector

Do **not** migrate to `TabView` and do not create a new custom tab framework.

Continue using the five existing Buttons and existing `_tabButtons` / `OverlayTabState` flow.

Target visual behavior:

```text
Selected tab
- accent-filled background
- clearly contrasting foreground
- SemiBold text
- compact rounded shape

Inactive tab
- subtle neutral background or very low-chrome treatment
- normal foreground
- Normal font weight
```

Recommended starting metrics:

```text
height / min height : ~34-36 DIP
corner radius       : ~6-8 DIP
font size           : ~13-14 DIP
horizontal padding  : compact
MinWidth            : 0
```

Important constraints:

- all five English labels must fit at the 400-DIP panel baseline;
- `Controller` and `Shortcut` must not clip;
- do not add horizontal tab scrolling;
- do not globally scale the Overlay down;
- do not add tab icons in this polish PR — the five labels are already tight at 400 DIP;
- preserve pointer/touch tab selection;
- preserve LB/RB tab selection;
- preserve user-configurable tab order;
- preserve startup-tab = first configured tab.

Use existing WinUI theme/accent resources where practical. Avoid hardcoding a second product color system.

---

## 4.5 Make the body consume the available horizontal width

The body must visually use the same left/right content bounds.

At minimum:

- make the `ScrollViewer` content stretch horizontally;
- ensure `TabBody` and page roots stretch to the viewport;
- preserve the ToggleSwitch right alignment and Slider full-width behavior;
- avoid accidental right-side dead space caused by content sizing.

For example, use the equivalent of:

```xml
HorizontalContentAlignment="Stretch"
```

on the appropriate body container/ScrollViewer.

Do not solve this by increasing the whole Overlay width.

---

## 4.6 Hide the scrollbar, but keep scrolling fully functional

Change the body from a visible automatic scrollbar to an invisible scrollbar policy:

```xml
VerticalScrollBarVisibility="Hidden"
```

Do **not** use `Disabled`.

The following behavior must remain unchanged:

```text
controller Up/Down
→ logical row selection moves
→ off-screen selected row
→ BringSelectedRowIntoView()
→ body scrolls as needed
```

Pointer/touch scrolling should also continue to work through the existing `ScrollViewer` behavior.

Horizontal scrolling stays disabled.

---

## 4.7 Remove the footer hint completely

Delete the persistent footer:

```text
LB/RB  Tabs    B  Close
```

Remove the corresponding fourth XAML row as well.

Do not replace it with another permanent controller-hint strip in this PR.

LB/RB and B remain controller semantics; only their persistent on-screen legend is removed.

---

## 4.8 Replace the large blue row outline with a subtle selected-row treatment

Do **not** remove logical selection visibility.

The controller-first design still requires the selected actionable row to be obvious.

Change only the visual treatment of regular list rows:

Current:

```text
selected row
→ 2-DIP accent border around entire row
```

Target:

```text
selected row
→ subtle selected background/fill (preferred)
→ no large blue rectangular outline

unselected row
→ transparent/neutral
```

A narrow accent indicator may be used only if needed after hardware testing, but first prefer the simpler built-in subtle-fill approach.

Requirements:

- keep `OverlayRowSelection` unchanged unless a tiny visual-only seam is genuinely required;
- keep `OverlayRowCapabilities` unchanged;
- unavailable rows remain non-selectable mutation targets;
- selection stays clear at 150% scaling;
- row navigation/adjust/activate callbacks do not change;
- `BringSelectedRowIntoView()` does not change.

Do not generalize this into a selection-theme subsystem.

### Shortcut exception

The Shortcut page is intentionally a 2x2 tile surface with its own selection model. Do not restyle/re-architect those tiles merely to make ordinary list rows match this polish. Change Shortcut visuals only if a direct regression from the shared shell change requires a minimal correction.

---

## 5. Explicit non-goals

Do not include any of the following in this PR:

- Full1902 controller ownership changes;
- PID1901/PID1902 changes;
- HidHide changes;
- VIIPER changes;
- OQ4 capture/release changes;
- routing policy changes;
- controller semantic mapping changes;
- outside-click dismissal redesign;
- show/hide animation timing redesign;
- Runtime transport or persistence changes;
- new Device/Profile/Controller features;
- Shortcut action contracts;
- custom ToggleSwitch template;
- custom Slider template;
- Acrylic/Mica/blur framework;
- new shadow/composition subsystem;
- tab icons;
- horizontally scrollable tabs;
- panel-width redesign;
- global typography/resource refactor;
- generalized visual-theme manager.

This PR should stay small and presentation-focused.

---

## 6. Expected files

Primary expected changes:

```text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindowGeometry.cs
tests/SteamInputAddonforClaw.Tests/OverlayWindowGeometryTests.cs
```

Possible only if genuinely necessary:

```text
src/SteamInputAddonforClaw.Overlay/WindowInterop.cs
```

Avoid touching Runtime/controller projects.

Do not modify `OverlayToggleRow.cs` / `OverlaySliderRow.cs` unless a tiny visual adjustment cannot be expressed cleanly by the shell/selection visual path. Their current state/request contracts are not part of this work.

---

## 7. Tests

## 7.1 Geometry unit tests

Update `OverlayWindowGeometryTests` for the new inset contract.

At minimum cover:

1. 96 DPI:
   - 400-DIP width remains 400 px where space allows;
   - 4-DIP inset becomes 4 px;
   - X/Y are inset from the work-area origin;
   - height loses top + bottom inset.

2. 144 DPI / 150%:
   - 400-DIP width remains 600 px;
   - 4-DIP inset becomes approximately 6 px;
   - non-zero work-area origins remain correct.

3. Existing tested DPI values still preserve width conversion semantics.

4. Narrow/small work area:
   - width and height clamp safely;
   - no negative dimensions.

5. `dpi == 0` continues to use the existing 96-DPI fallback policy.

Do not introduce elaborate geometry abstractions solely to make tests easier.

## 7.2 Existing logical-navigation tests

Run and preserve at minimum:

```text
OverlayRowSelectionTests
OverlayTabState tests
OverlayTabOrderEditorTests
OverlayWindowGeometryTests
```

Do not rewrite selection tests merely because the selected-row color changed; logical behavior is not changing.

---

## 8. Hardware / UX validation

Primary reference target:

```text
1920 x 1200 display
150% Windows scaling
```

Validate on the real Overlay, not only XAML screenshots.

Required checks:

1. **Outer float**
   - top, left, and bottom are separated from the work area by only about 5-6 physical pixels at 150%;
   - it must not look like a large centered/floating desktop window.

2. **Title**
   - `Quick Settings` is visibly smaller than the current build;
   - still clearly reads as the page title.

3. **Tabs**
   - all five labels are fully readable;
   - no `Controller` / `Shortcut` clipping;
   - selected tab is immediately obvious;
   - inactive tabs are visually quieter;
   - LB/RB and touch/click behavior remains correct.

4. **Body**
   - balanced left/right margins;
   - ToggleSwitches align naturally toward the right edge;
   - Sliders use the available width;
   - no extra inner body card/box.

5. **Scrolling**
   - scrollbar is not visible;
   - DPad/stick navigation can still reach rows below the viewport;
   - selection automatically scrolls into view;
   - touch/pointer scrolling still works.

6. **Selection**
   - ordinary selected row is obvious without the current large blue outline;
   - disabled/unavailable rows do not look like active mutation targets.

7. **Footer**
   - no persistent LB/RB/B hint area remains;
   - the reclaimed vertical space belongs to the body.

8. **Lifecycle regression**
   - Show/Hide animation still completes without clipping;
   - outside click still dismisses correctly;
   - Overlay remains no-activate/topmost as before;
   - B close still follows the existing safe OQ4 retirement path;
   - controller navigation does not leak to the underlying Steam/QAM surface while capture is active.

Capture before/after screenshots at the 150% reference scaling for PR review.

---

## 9. Verification commands

Required before PR completion:

```text
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test
git diff --check
```

If the repository's current CI/build contract has become stricter by implementation time, run the current required superset as well.

---

## 10. Acceptance criteria

The PR is complete only when all of the following are true:

- [ ] Overlay still uses the 400-DIP width baseline.
- [ ] At 150% scaling, left/top/bottom separation is only about 5-6 physical pixels.
- [ ] No large outer margin was introduced.
- [ ] Internal left/right margins are balanced and compact.
- [ ] `Quick Settings` title is approximately 24 DIP and no longer dominates the panel.
- [ ] Five tabs fit without clipping at the reference configuration.
- [ ] Selected tab has a modern compact accent treatment.
- [ ] No tab icons or horizontally scrolling tab strip were introduced.
- [ ] Body has no large inner card/container.
- [ ] Vertical scrollbar is hidden, not disabled.
- [ ] Programmatic/controller scrolling still works.
- [ ] Footer hint is removed entirely.
- [ ] Regular selected rows no longer use the large 2-DIP blue outline.
- [ ] Controller selection remains clearly visible.
- [ ] Toggle/Slider behavior and Runtime-authoritative mutation seams are unchanged.
- [ ] LB/RB, B, DPad, left-stick, right-stick semantic behavior is unchanged.
- [ ] Outside-click dismissal and Show/Hide behavior are unchanged.
- [ ] No Full1902 / HidHide / VIIPER / routing authority code changed.
- [ ] Geometry tests cover the new DPI-aware inset.
- [ ] Full required build/tests pass.
- [ ] `git diff --check` passes.

---

## 11. Review focus

Review this PR primarily for:

- actual 1920x1200 @ 150% visual result;
- text clipping or uneven content width;
- whether the inset is genuinely small rather than desktop-window-like;
- retained controller-first selection clarity;
- retained scrolling despite the hidden scrollbar;
- accidental behavior/lifecycle scope creep.

Do **not** block this PR for speculative visual frameworks, generalized responsive-layout abstractions, custom-control systems, or theoretical future requirements. Keep one clear Overlay shell, one geometry authority, and the existing navigation/lifecycle owners.
