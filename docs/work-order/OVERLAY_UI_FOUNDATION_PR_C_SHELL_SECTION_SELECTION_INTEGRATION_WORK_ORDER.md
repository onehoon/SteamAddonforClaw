# Work Order — Overlay UI Foundation PR-C: Shell, Section, Selection, and Pointer Integration

> **Status:** Ready for implementation  
> **Prepared:** 2026-09-21  
> **Target repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed main baseline:** `8f24d8470aa1d0f2f36fead79a18587c6074257f` (`refactor: replace overlay slider rows with value rows (#561)`)  
> **Architecture authority:** standalone Full PID1902  
> **Scope:** final Overlay UI Foundation integration pass before user-facing feature migration. Establish the single-column section hierarchy, consistent editable-row chrome, stronger logical-selection visuals, pointer/touch-to-logical-selection synchronization, and low-chrome top-tab presentation. Do not add or migrate product features in this PR.

---

## 1. Goal

Finish the reusable UI foundation of the new large Addon Overlay so subsequent work can focus on connecting real user features rather than repeatedly changing shell/navigation/control behavior.

PR-A established source responsibility boundaries:

```text
OverlayWindow
├─ Presentation
├─ Shell
├─ Navigation
└─ QuickSettings
```

PR-B established the two currently proven editable row interactions:

```text
boolean
→ OverlayToggleRow
→ A / native ToggleSwitch

numeric or discrete ordered value
→ OverlayValueRow
→ Left/Right / previous-next buttons
```

The remaining foundation gaps in current `main` are presentation/integration gaps:

1. Device/Profile section content is still built as a flat sequence of TextBlocks and row containers;
2. setting rows do not synchronize pointer/touch interaction back into the logical controller selection;
3. logical selection is currently shown mainly through a temporary background fill;
4. the top tabs still use the old full Button selected-fill presentation;
5. row chrome metrics are repeated across Toggle, Value, and tab-order rows;
6. future feature work does not yet have one stable single-column Section → Row visual baseline to build on.

This PR must close those gaps **without connecting new user features**.

After this PR:

> **Overlay UI Foundation is considered complete enough for feature migration.**

Future feature PRs may add a narrowly required new primitive such as ActionRow or StatusRow when the first real consumer needs it, but they should not need to redesign the shell, selection model, common editable-row chrome, or numeric/discrete interaction.

---

## 2. This is the final planned Foundation PR

The intended sequence is:

```text
PR-A
OverlayWindow structural refactor
        ↓
PR-B
ToggleRow + controller-first ValueRow
        ↓
PR-C
Shell + sections + selection + pointer integration
        ↓
Overlay UI Foundation complete
        ↓
Desktop user-feature inventory / tab mapping
        ↓
feature connection PRs
```

Do not create a PR-D merely for speculative Foundation abstractions.

If later hardware testing reveals a concrete visual defect, fix that defect in a focused UI polish PR.

---

## 3. Surface scope — Overlay only

This PR changes only the Addon-owned Overlay:

```text
src/SteamInputAddonforClaw.Overlay/
```

Do **not** redesign or replace desktop UI controls under:

```text
src/SteamInputAddonforClaw.UI/
```

Desktop UI remains a mouse/keyboard-oriented configuration surface and may continue to use:

- Slider;
- ComboBox/dropdowns;
- ToggleSwitch;
- SettingsCard/form layouts;
- desktop-specific dialogs/navigation.

The Overlay intentionally has a separate controller-first presentation layer.

Do not create shared Desktop/Overlay visual controls.

Shared Runtime/product contracts remain shared; presentation does not.

---

## 4. Mandatory references before editing

Read these documents together.

### Full PID1902 authority

1. `docs/Full 1902 Implementation/README.md`
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
4. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`

The Overlay remains a frontend only.

It never becomes controller authority, HidHide authority, VIIPER authority, physical-mode authority, or feature persistence authority.

### Current Overlay Foundation

5. `docs/work-order/OVERLAY_UI_FOUNDATION_PR_A_STRUCTURAL_REFACTOR_WORK_ORDER.md`
6. `docs/work-order/OVERLAY_UI_FOUNDATION_PR_B_CONTROLLER_FIRST_VALUE_ROW_WORK_ORDER.md`
7. `docs/work-order/OVERLAY_LARGE_FLOATING_SURFACE_GEOMETRY_THEME_POC_WORK_ORDER.md`
8. `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`

### Current source

Inspect current `main` before editing:

```text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
src/SteamInputAddonforClaw.Overlay/OverlayValueRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayTabOrderRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayRowSelection.cs
src/SteamInputAddonforClaw.Overlay/OverlayShortcutSelection.cs
```

Inspect relevant tests before implementation.

If `main` has advanced beyond the reviewed SHA, adapt this work order to current source rather than restoring the reviewed snapshot mechanically.

---

## 5. Current product decisions that override older Overlay POC wording

Some older Overlay documents describe the original narrow 400-DIP side panel, persistent controller hints, or visible Slider controls.

Those are historical implementation references where they conflict with the current direction.

For PR-C, the current product decisions are:

```text
Window
→ current PR #559 large floating surface
→ geometry/width unchanged in PR-C

Content
→ settings interaction is single-column by default
→ avoid two-column interactive settings layouts
→ Shortcut 2x2 grid remains the intentional exception

Value editing
→ no visible Slider in Overlay
→ no ComboBox/dropdown for current ordered values
→ ValueRow uses Left/Right

Footer
→ no persistent controller-hint footer

Input
→ Runtime semantic controller navigation remains primary
→ pointer/touch remains supported through normal WinUI controls

Desktop UI
→ unchanged
```

Do not reintroduce the old narrow-panel assumptions while implementing this PR.

---

# 6. Preserve the current large-window shell geometry

PR-C is **not** a geometry PR.

Keep the existing large floating window behavior from PR #559:

- monitor/DPI selection;
- physical window rectangle;
- topmost;
- no-activate;
- rounded DWM corners;
- show/hide animation;
- outside-click lifecycle;
- current outer XAML margins.

Do not add a fixed content `MaxWidth`.

Do not shrink the Overlay width in this PR.

The user will evaluate the overall horizontal size later on hardware.

The Foundation contract is only:

> **interactive settings content is one-dimensional/single-column unless a concrete UI such as Shortcut genuinely requires 2D layout.**

---

# 7. Target shell hierarchy

Keep the current XAML shell small.

Conceptually:

```text
Overlay large floating surface
│
├─ Top Tab Strip
│    Device / Profile / Controller / Shortcut / Setting
│
└─ ScrollViewer
     └─ Active Page
          └─ vertical Section stack
               ├─ Section
               │    ├─ optional Header
               │    ├─ optional Message
               │    └─ Rows
               │
               └─ Section
                    └─ Rows
```

Do not put the TabStrip inside the scrolling region.

Do not add a footer.

Do not add a right-side status pane.

Do not add nested ScrollViewers for ordinary sections.

Shortcut remains a special fixed 2x2 page.

---

# 8. Introduce one narrow reusable editable-row chrome helper

Current code repeats essentially the same row container construction in:

```text
OverlayToggleRow
OverlayValueRow
AddonQuickSettingsTabOrderRow
```

The repeated facts include:

- `Border` container;
- row padding;
- corner radius;
- transparent unselected border;
- row-level selection background target.

PR-C may introduce **one small stateless helper** for this proven shared visual chrome.

Recommended shape:

```text
OverlayRowChrome
└─ Create(UIElement child) → Border
```

It may centralize only concrete shared row metrics such as:

```text
Padding
MinHeight
CornerRadius
base BorderThickness
transparent unselected BorderBrush
```

Recommended initial baseline:

```text
Padding    = 12,6
MinHeight  = 52 DIP
CornerRadius = current compact row radius
selection accent width = 3 DIP
```

The exact helper name is flexible.

### Important constraints

This is **not** permission to create:

- `IOverlayRow`;
- `OverlayRowBase`;
- generic row inheritance;
- a control factory;
- a style manager;
- a design-token framework;
- a row registry;
- a renderer abstraction.

One small stateless helper with multiple current consumers is sufficient.

Shortcut tiles do not need to use this row chrome because they are intentionally a different 2D visual.

---

# 9. Section hierarchy

## 9.1 Quick Settings Device/Profile

Current `RebuildQuickSettingsContent()` appends:

```text
section heading TextBlock
section message TextBlock
row
row
next section heading
row
...
```

into one flat StackPanel.

Change the visual tree so each shared `QuickSettingsSection` produces one explicit visual section:

```text
Section StackPanel
├─ optional section header
├─ optional section message
└─ row stack
     ├─ row
     ├─ row
     └─ row
```

Then the page content is:

```text
Page section stack
├─ Section
├─ Section
└─ Section
```

Use the shared snapshot's existing:

- `section.Label`;
- `section.Message`;
- visible rows.

Do not duplicate product labels or section policy into Overlay code.

### Initial visual rhythm

Use a small consistent hierarchy rather than arbitrary margins.

Recommended first baseline:

```text
between sections: about 16–20 DIP
header → message/rows: about 4–6 DIP
between rows: about 4 DIP
```

Keep the values simple and locally defined.

Hardware polish may adjust these later.

Do not create responsive breakpoints.

## 9.2 Setting tab

The existing Tab Order UI should use the same Section → Rows hierarchy:

```text
TAB ORDER
row
row
row
row
row
```

Do not change tab-order product behavior.

Do not add new Setting features in this PR.

## 9.3 Controller tab

Controller remains a placeholder until feature migration begins.

Do not populate it merely to demonstrate the new section system.

## 9.4 Shortcut tab

Keep the current fixed 2x2 Shortcut structure.

Do not force it into the one-column row layout.

This is the intentional 2D exception.

---

# 10. Row logical-selection visual

The current row selection uses only a subtle background fill.

Finalize a clearer but quiet controller-selection state.

Recommended selected row treatment:

```text
selected row
→ subtle selected background fill
→ 3-DIP accent strip on the left

unselected row
→ transparent fill
→ transparent left strip of the same thickness
```

Using the same left-strip thickness in both states avoids layout movement.

Do **not** use a large full-row accent outline.

Do **not** turn the whole selected row into an accent-colored button.

The selected state should be clearly distinguishable from:

- pointer hover;
- pressed child button;
- disabled control.

Reuse current WinUI/theme resources where practical:

- existing accent brush;
- existing subtle-fill brush;
- normal text foreground.

Do not hard-code a different custom selection color into each row type.

### Shortcut selection

Shortcut tiles may keep the current clearer border-based selected treatment because they are large tiles rather than ordinary setting rows.

Do not force Shortcut to use the row left-strip treatment.

---

# 11. Top tab visual

The current selected tab uses a full accent-filled Button with white foreground.

Replace that POC look with a lower-chrome tab presentation.

Target concept:

```text
Device   Profile   Controller   Shortcut   Setting
──────
```

Selected tab:

- normal/light surface;
- semibold label;
- small accent underline/indicator at the bottom.

Inactive tabs:

- normal label;
- no indicator;
- no permanent filled pill/background.

Keep normal native pointer hover/pressed feedback where practical.

### Implementation constraints

Keep:

- five fixed tab identities;
- current tab ordering;
- current pointer click behavior;
- LB/RB bounded navigation;
- Runtime-authoritative tab-order state;
- existing top-level `Button` semantics if they remain the simplest implementation.

Do not migrate to:

- `NavigationView`;
- `TabView`;
- WinUI gamepad focus;
- another tab navigation authority.

Do not add icons in this PR merely to decorate the tabs.

Text labels are sufficient for the Foundation.

---

# 12. Pointer/touch must synchronize logical row selection

This is the main missing interaction seam after PR-B.

Today:

```text
controller Up/Down
→ logical selected row changes

touch ValueRow arrow / ToggleSwitch
→ feature mutates
→ logical selected row may still point at an older row
```

That creates an avoidable mode mismatch when the user alternates touch and controller.

Target:

```text
user touches/clicks an actionable setting row
→ that row becomes the logical selected row
→ its native child action still happens normally
→ next DPad/stick input continues from that same row
```

Do not steal or replace the child control's normal pointer/touch action.

---

## 12.1 Extend `OverlayRowSelection` narrowly

Add one explicit selection operation, for example:

```csharp
internal bool TrySelect(int index)
```

Required behavior:

- reject index < 0;
- reject index >= row count;
- reject an unselectable row;
- return false if the requested row is already selected;
- otherwise update `SelectedIndex` and return true;
- do not invoke `Activate`;
- do not invoke `Adjust`.

This is a pure selection operation only.

Do not add pointer concepts to `OverlayRowSelection`.

---

## 12.2 Window-level pointer selection

Add one narrow OverlayWindow helper that maps a rendered row/container back to the active page's logical row index.

Conceptually:

```text
row pointer/tap
→ identify current page row
→ _rowSelection.TrySelect(index)
→ ApplyRowSelectionVisual()
```

Use the current `_pageRows` list rather than introducing a second row-index dictionary unless code evidence proves one is necessary.

This matters for the Setting tab because authoritative tab reordering can change row order while preserving row instances.

Resolve against current page order at interaction time.

### Quick Settings rebuild

Quick Settings rows are rebuilt when their structural shape changes.

Wire pointer selection to the newly created row containers as part of that existing rebuild path.

Do not maintain a separate lifetime registry for old controls.

### Setting rows

Wire the existing Tab Order row containers into the same logical pointer-selection behavior.

### Disabled/unselectable row

Touching an unavailable row must not make it the controller mutation target.

`TrySelect` should fail closed.

### Shortcut

Shortcut already has pointer selection through:

```text
tile.Tapped
→ SelectShortcutSlot(...)
```

Keep that separate fixed-grid path.

---

# 13. Do not add pointer mutation to the row background

Pointer/touch selection and pointer/touch mutation are different actions.

Required behavior remains:

```text
tap ToggleSwitch
→ select row + native toggle action

tap ValueRow previous/next button
→ select row + one-step adjustment

tap empty row background
→ logical selection may move to that row
→ no setting mutation by itself
```

Do not make tapping the whole Toggle row implicitly toggle.

Do not make tapping the whole Value row implicitly increment.

This avoids accidental mutations on touch.

---

# 14. Keep controller semantics unchanged

PR-C must preserve:

```text
LB / RB
→ previous / next top tab

Up / Down
→ previous / next selectable row

Left / Right
→ selected ValueRow adjustment
→ selected Tab Order movement where currently supported
→ Shortcut horizontal selection on Shortcut page

A
→ selected ToggleRow activation
→ existing tab-order row behavior unchanged
→ current unassigned Shortcut remains no-op

B
→ close Overlay through existing Runtime/capture path
```

Do not add an edit mode.

Do not add a nested Back stack.

Do not reinterpret B as page-local Back.

---

# 15. Scrolling remains one shared page viewport

Keep the current `BodyScroll` architecture.

Required behavior:

- TabStrip remains fixed;
- active page body scrolls;
- vertical scrollbar remains hidden;
- horizontal scrolling remains disabled;
- controller row movement calls `BringSelectedRowIntoView`;
- tab change resets the shared body scroll to top;
- a same-shape Quick Settings value update does not rebuild the visual tree;
- a structural Quick Settings rebuild preserves the selected RowId where possible.

Pointer selection of an already visible row does not require a special scroll action.

Do not add per-section ScrollViewers.

---

# 16. Preserve Quick Settings binder / mutation authority

PR-C is presentation and selection integration only.

Do not change:

```text
OverlayQuickSettingsPageBinding
OverlayDelayedSliderCommit
ScheduleQuickSettingsSlider
QuickSettingsCommitPolicy
QuickSettingsCommitGroupId
QuickSettingsLinkedSliderConstraint
authoritative readback
stale-generation guard
hide-time pending-draft cancellation
```

The Device/Profile surface remains one generic renderer.

Do not split Device and Profile into duplicated UI implementations.

---

# 17. Do not prebuild unused ActionRow / StatusRow

PR-C is the last **Foundation** PR, but Foundation completion does not mean every hypothetical future control must already exist.

Do not add:

```text
OverlayActionRow
OverlayStatusRow
OverlayChoiceRow
OverlayComboBoxRow
OverlayModalSelector
```

unless current `main` has acquired a real consumer before implementation starts.

When feature migration begins:

- first real command/action feature may add one narrow ActionRow;
- first real passive information block may add one narrow StatusRow;
- unusually large option sets may justify a separate selection surface.

Those should be driven by actual product needs rather than speculative Foundation work.

---

# 18. Do not create a general UI framework

Explicitly avoid:

```text
OverlayUiManager
OverlayPageManager
OverlayNavigationService
OverlaySectionManager
IOverlayRow
OverlayRowBase
generic descriptor-driven UI framework
dynamic page schema
dependency injection for visual primitives
focus graph
navigation graph
breakpoint/responsive-layout engine
generic input behavior framework
```

The desired architecture after PR-C is still simple:

```text
OverlayWindow
├─ Shell
├─ Navigation
├─ QuickSettings
└─ small concrete visual primitives
     ├─ OverlayToggleRow
     ├─ OverlayValueRow
     └─ narrow shared row chrome helper
```

One clear owner remains more important than minimum LOC.

---

# 19. Expected files

Likely production changes:

```text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
src/SteamInputAddonforClaw.Overlay/OverlayRowSelection.cs
src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayValueRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayTabOrderRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayRowChrome.cs   # optional narrow helper
```

Tests likely changed/added:

```text
tests/SteamInputAddonforClaw.Tests/OverlayRowSelectionTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayDeviceRendererWiringTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayShortcutSelectionTests.cs
tests/SteamInputAddonforClaw.Tests/*Overlay shell/source contract tests as appropriate
```

Expected unchanged:

```text
src/SteamInputAddonforClaw.UI/*
src/SteamInputAddonforClaw.Contracts/*
src/SteamInputAddonforClaw.FrontendTransport/*
src/SteamInputAddonforClaw/Lifecycle/*
src/SteamInputAddonforClaw.QamHost/*
src/SteamInputAddonforClaw.Overlay/OverlayQuickSettingsPageBinding.cs
src/SteamInputAddonforClaw.Overlay/OverlayDelayedSliderCommit.cs
src/SteamInputAddonforClaw.Overlay/WindowInterop.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindowGeometry.cs
```

Do not touch Runtime/controller lifecycle code to implement visual selection.

---

# 20. Unit tests — `OverlayRowSelection.TrySelect`

Add pure tests for the new explicit-selection seam.

Required coverage:

### Select valid row

```text
rows = selectable rows
TrySelect(2)
→ SelectedIndex = 2
→ returns true
```

### Already selected

```text
TrySelect(current)
→ returns false
→ selection unchanged
```

### Invalid index

Cover:

- negative;
- count/out of range.

Expected:

```text
returns false
selection unchanged
```

### Unselectable row

```text
TrySelect(index of unavailable row)
→ false
→ previous valid selection remains
```

### Selection only

Prove `TrySelect` does not call row:

- `Activate`;
- `Adjust`.

Do not add pointer/XAML dependencies to the pure selection tests.

---

# 21. Source/composition tests

Because WinUI window construction is not available in the ordinary unit-test host, add narrow source/composition guards for the integration facts that matter.

At minimum protect:

### Single-column settings baseline

Assert Device/Profile Quick Settings continue using a vertical section/row hierarchy and do not introduce a two-column settings grid.

Do not ban all `Grid` usage because row internals legitimately use Grid.

Keep the assertion specific to the page/section composition path.

### Section grouping

Assert the renderer creates one section container per shared `QuickSettingsSection` rather than appending all rows flatly.

### Pointer selection seam

Assert generic Quick Settings rows and Setting tab-order rows are wired to the one logical row-selection helper.

### Top tab low-chrome state

Assert selected-tab rendering uses the dedicated accent indicator/underline rather than setting the entire selected Button background to the accent brush.

Do not write brittle pixel-perfect source tests.

### No product-scope leak

Assert this PR does not add:

- Desktop UI edits;
- new controller reader;
- Runtime feature mutation policy;
- shared contract changes.

Use existing architecture/source-contract test patterns; do not build a new source-linter framework.

---

# 22. Existing tests that must remain green

Do not weaken tests protecting:

- OverlayTabState and authoritative tab order;
- OverlayRowSelection navigation/normalization;
- OverlayShortcutSelection fixed 2x2 behavior;
- OverlayValueModel;
- OverlayToggleModel;
- generic Device/Profile Quick Settings renderer;
- Quick Settings binder;
- delayed/grouped commit;
- QAM/Overlay product parity;
- outside-click behavior;
- large-window geometry;
- no-activate/topmost;
- semantic controller capture/release;
- Full1902 lifecycle.

A visual-tree refactor must not be used as a reason to remove authority/behavior tests.

---

# 23. Build / automated validation

Run:

```text
dotnet restore SteamInputAddonforClaw.slnx
dotnet build SteamInputAddonforClaw.slnx --no-restore -v:minimal
dotnet build SteamInputAddonforClaw.slnx --configuration Release --no-restore -v:minimal
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj
git diff --check
```

No new warnings.

---

# 24. Hardware/manual validation

PR-C changes visible shell hierarchy and pointer/controller handoff, so hardware validation is important.

Use the supported MSI Claw at the reference 1920×1200 / 150% scale first.

## 24.1 Tabs

Verify:

- selected tab is obvious without a full accent-filled pill;
- all five labels remain readable;
- LB/RB still moves tabs correctly;
- touch/click tab selection still works;
- tab reorder still updates the authoritative visual order.

## 24.2 Section layout

Verify Device/Profile:

- sections are visually separated;
- header/message hierarchy is clear;
- rows remain one column;
- no excessive horizontal split is introduced;
- long pages scroll naturally;
- hidden scrollbar is acceptable.

## 24.3 Controller selection

Verify:

```text
Up/Down
→ one row at a time
→ selected background + left accent moves together

Left/Right on ValueRow
→ value changes
→ selection stays on row

A on ToggleRow
→ toggle changes
→ selection stays on row
```

## 24.4 Touch/controller handoff

Critical scenario:

```text
controller selects row A
→ touch ValueRow arrow on row D
→ row D becomes logical selected row and changes one value step
→ press DPad Down
→ navigation continues from row D
```

Also verify:

```text
touch ToggleSwitch on row C
→ row C becomes selected
→ switch still toggles normally
```

Touching empty row background:

```text
→ selects row
→ does not mutate the setting
```

## 24.5 Disabled row

Touch an unavailable/nonselectable row if a real current state can expose one:

```text
→ must not become active controller mutation target
```

## 24.6 Shortcut

Verify current behavior remains:

- touch tile selects tile;
- DPad/sticks continue from selected tile;
- selected border is visible;
- A on current Unassigned slot remains no-op;
- 2x2 layout remains the only normal 2D content exception.

## 24.7 Lifecycle regression

Verify:

- Overlay show/hide animation unchanged;
- B close unchanged;
- outside-click behavior unchanged;
- no focus theft;
- topmost behavior unchanged;
- no controller input leak after close.

---

# 25. Explicit non-goals

Do not implement any actual new product feature in PR-C.

Specifically do not add:

- fan control;
- battery limit;
- LED controls;
- vibration strength;
- M1/M2 mapping;
- front-button mapping;
- new controller settings;
- new Device settings;
- new Profile settings;
- shortcut actions;
- new diagnostics;
- ActionRow;
- StatusRow;
- dropdown/flyout selector;
- nested page stack;
- modal navigation;
- persistent bottom controller hints;
- right-side status dashboard;
- content max-width policy;
- Overlay width change;
- two-column setting forms;
- desktop UI changes;
- QAM redesign/removal;
- controller transport changes;
- Full1902 lifecycle changes.

---

# 26. Review criteria

Treat these as blocking.

### Blocking

- settings content becomes a general two-column interactive layout;
- window geometry/width changes as part of this Foundation PR;
- desktop UI is changed;
- full selected-tab accent fill remains despite this PR claiming low-chrome tab integration;
- pointer/touch mutation works but logical row selection remains stale;
- pointer-selection logic can select an unavailable row;
- tapping the empty row background mutates a setting;
- child Toggle/Value buttons stop receiving their normal pointer action;
- controller selection visual is ambiguous or shifts row layout;
- Quick Settings section grouping duplicates product policy/labels;
- Device/Profile generic renderer is split/duplicated;
- mutation/debounce/readback authority moves into the visual layer;
- Runtime/controller authority is touched without concrete necessity;
- a general UI framework/manager/interface hierarchy is introduced.

### Non-blocking

- small spacing/padding adjustments discovered during hardware testing;
- exact underline thickness within a compact 2–3 DIP range;
- exact subtle selection-fill resource if it remains clearly distinguishable;
- helper naming/placement.

### Theoretical / do not block

Do not add state machines, locks, epochs, focus arbitration, or event-order machinery solely for hypothetical touch/controller instruction-level races.

The supported behavior only needs to converge correctly through the existing UI-thread semantic navigation and authoritative Runtime readback paths.

---

# 27. Acceptance checklist

### Shell

- [ ] Current large floating geometry remains unchanged.
- [ ] Five top tabs remain fixed identities with Runtime-authoritative ordering.
- [ ] Selected tab uses a low-chrome accent indicator/underline.
- [ ] No persistent footer/hint bar exists.
- [ ] Ordinary settings content remains single-column.
- [ ] Shortcut remains the intentional fixed 2x2 exception.

### Sections

- [ ] Device/Profile shared Quick Settings render explicit visual sections.
- [ ] Setting Tab Order uses the same basic Section → Rows hierarchy.
- [ ] Shared section labels/messages remain the source of product text.
- [ ] Section/row spacing is consistent.

### Rows

- [ ] Toggle/Value/TabOrder rows share narrow common chrome where practical.
- [ ] Selected row uses subtle fill + left accent strip.
- [ ] Unselected row reserves equivalent accent thickness to avoid visual movement.
- [ ] ValueRow remains Left/Right with no edit mode.
- [ ] ToggleRow remains A/native ToggleSwitch.

### Pointer/controller handoff

- [ ] `OverlayRowSelection` has one narrow explicit-select operation.
- [ ] Touching/clicking an actionable row synchronizes logical selection.
- [ ] Touching a disabled row does not select it.
- [ ] Empty-row tap selects only; it does not mutate.
- [ ] Native child controls still perform their own action.
- [ ] Shortcut pointer selection remains correct.

### Architecture/regression

- [ ] Overlay remains a frontend only.
- [ ] Desktop UI is unchanged.
- [ ] Shared Quick Settings contracts are unchanged.
- [ ] QAM is unchanged.
- [ ] Runtime/controller lifecycle is unchanged.
- [ ] Quick Settings commit/readback semantics are unchanged.
- [ ] Debug/Release builds pass.
- [ ] Full tests pass.
- [ ] `git diff --check` passes.

---

# 28. Follow-up after PR-C

After PR-C is merged:

> **Stop adding generic Foundation pieces and begin user-feature migration.**

The next planning task should inspect the user-facing Desktop UI (excluding developer/diagnostic-only surfaces) and map each real feature to:

```text
Device
Profile
Controller
Shortcut
Setting
```

For each feature, choose the smallest existing Overlay interaction:

```text
boolean
→ ToggleRow

ordered numeric/discrete value
→ ValueRow

read-only information
→ add a narrow StatusRow only when first needed

command/action
→ add a narrow ActionRow only when first needed
```

Feature migration should reuse existing Runtime/product authorities and must not turn the Overlay into another feature-state owner.
