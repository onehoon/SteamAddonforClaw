# OQ5 UI Polish B — Remove Title / Balance Internal Content Spacing

> **Status:** Implementation work order  
> **Prepared:** 2026-09-04  
> **Target repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed main baseline:** `493e29870dee2905c90a7490dbddc146d567900e`  
> **Scope:** Overlay presentation/layout correction only. No Runtime/controller/lifecycle/geometry behavior change.

---

## 1. Goal

Correct the remaining internal-layout problem observed after OQ5 UI Polish A / PR #491.

The approved product direction is now:

```text
remove the "Quick Settings" title completely
make the Overlay INTERNAL top / left / right content spacing visually equal
make page content actually consume the usable Overlay body width
keep the clean single-surface design
keep the hidden scrollbar
keep the compact tab treatment from PR #491
```

Important terminology for this work order:

> **"margin / spacing" in this PR means spacing INSIDE the Overlay surface.**

This PR must **not** change the small outer HWND/work-area inset introduced by PR #491.

This is one small follow-up presentation PR.

---

## 2. Mandatory references before editing

Read these documents together before changing code:

### Full PID1902 authority

1. `docs/Full 1902 Implementation/README.md`
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
4. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`

This PR must remain presentation-only and must not touch any Full1902 authority.

### Overlay UI

5. `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
6. `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
7. `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
8. `docs/overlayui/OQ5_UI_POLISH_A_FLOATING_SURFACE_COMPACT_TABS_WORK_ORDER.md`
9. `docs/overlayui/OQ5_UI_04_LOGICAL_ROW_SELECTION_SCROLLING_WORK_ORDER.md`
10. `docs/overlayui/OQ5_UI_05_TOGGLE_ROW_PRIMITIVE_WORK_ORDER.md`
11. `docs/overlayui/OQ5_UI_06_SLIDER_ROW_PRIMITIVE_WORK_ORDER.md`

Also inspect the current implementation at the implementation-time `main` rather than mechanically applying this document to an old snapshot.

---

## 3. Current implementation facts at reviewed baseline

### 3.1 Current shell

`src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml` currently contains:

```xml
<Grid x:Name="OpaquePanel" Background="#FFF3F3F3" Padding="16,14,16,16">
    <Grid x:Name="AnimatedContent" RowSpacing="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <TextBlock
            Grid.Row="0"
            Text="Quick Settings"
            FontSize="24"
            FontWeight="SemiBold" />

        <Grid x:Name="TabStrip" Grid.Row="1" ... />

        <ScrollViewer
            x:Name="BodyScroll"
            Grid.Row="2"
            VerticalScrollBarVisibility="Hidden"
            HorizontalScrollBarVisibility="Disabled"
            HorizontalContentAlignment="Stretch">
            <Grid x:Name="TabBody" HorizontalAlignment="Stretch" />
        </ScrollViewer>
    </Grid>
</Grid>
```

PR #491 therefore reduced the title size but did not remove the title.

It also set shell padding to:

```text
Left   = 16 DIP
Top    = 14 DIP
Right  = 16 DIP
Bottom = 16 DIP
```

The new product decision supersedes that typography/layout choice.

### 3.2 Current Device page root

`OverlayWindow.BuildPage()` currently creates the Device page root as:

```csharp
var stack = new StackPanel { Spacing = 4 };
```

The Toggle/Slider/Navigation rows are then added below it.

### 3.3 Current reusable rows

`OverlayToggleRow`, `OverlaySliderRow`, and `OverlayTabOrderRow` use a `Border` as their top-level row container.

For example, Toggle/Slider rows currently contain internal padding similar to:

```csharp
Padding = new Thickness(12, 6, 12, 6)
```

The content-facing controls use the expected internal grid pattern:

```text
label / value area = star
right-side control/value = auto
```

Therefore the final visual result depends on the body/page/row width chain actually reaching the ScrollViewer viewport width.

### 3.4 Outer Overlay geometry is already separate

`OverlayWindowGeometry.PanelEdgeInsetDip` from PR #491 owns the small left/top/bottom separation between the HWND and the monitor WorkArea.

That is **not** the spacing problem being fixed here.

Do not change it in this PR.

---

## 4. Required implementation

## 4.1 Remove the `Quick Settings` title entirely

Delete the user-visible title:

```text
Quick Settings
```

Do not replace it with:

- another title;
- an icon/title row;
- breadcrumbs;
- a Settings gear;
- a compact caption.

The five tabs already provide sufficient top-level context for this transient surface.

The resulting shell should begin with the tab strip.

### Required XAML structure

Reduce the content grid from three rows to two rows:

```text
row 0 = tabs
row 1 = body
```

Conceptually:

```xml
<Grid.RowDefinitions>
    <RowDefinition Height="Auto" />
    <RowDefinition Height="*" />
</Grid.RowDefinitions>

<Grid x:Name="TabStrip" Grid.Row="0" ... />

<ScrollViewer x:Name="BodyScroll" Grid.Row="1" ...>
    ...
</ScrollViewer>
```

No empty title row or title-specific spacer should remain.

---

## 4.2 Make INTERNAL top / left / right shell spacing equal

The current `16,14,16,16` is no longer the target.

Use one explicit equal value for:

```text
Top
Left
Right
```

Recommended implementation baseline:

```xml
Padding="24,24,24,16"
```

Meaning:

```text
Left   24 DIP
Top    24 DIP
Right  24 DIP
Bottom 16 DIP
```

This intentionally keeps the bottom compact while making the three user-requested visible edges equal.

If implementation-time hardware review shows that 24 DIP is visibly excessive, a small single-value adjustment is acceptable, but **Top / Left / Right must remain the same value**.

Do not introduce independent top/left/right tuning after this point.

Do not solve the asymmetry by increasing the Overlay window width.

---

## 4.3 Preserve the clean one-surface layout

Keep `OpaquePanel` as the single visual surface.

Do not add:

- an inner Card;
- a body Border around the whole page;
- a nested background rectangle;
- an extra content panel merely to create margins;
- Mica/Acrylic/blur/shadow infrastructure.

The intended hierarchy remains:

```text
Overlay surface
  ├─ tabs
  └─ body content
```

---

## 4.4 Make the body/page/row width contract explicit and verify the first constraining element

The observed hardware symptom is:

```text
content appears too close to the left
large unused area remains on the right
```

Do **not** assume that changing shell `Padding` alone fixes this.

The body must satisfy this visual contract:

```text
usable width = Overlay client width - shell left padding - shell right padding

TabStrip width == usable width
BodyScroll viewport width == usable width
active page width == usable width
ordinary row container width == active page width
```

Then the row's own small internal padding may position its label/control inside that width.

### Minimum implementation expectations

Keep / explicitly preserve:

```xml
BodyScroll.HorizontalContentAlignment="Stretch"
TabBody.HorizontalAlignment="Stretch"
```

Make the active page roots explicit where useful, for example:

```csharp
var stack = new StackPanel
{
    Spacing = 4,
    HorizontalAlignment = HorizontalAlignment.Stretch,
};
```

Likewise for the Setting page root:

```csharp
var section = new StackPanel
{
    Spacing = 8,
    HorizontalAlignment = HorizontalAlignment.Stretch,
};
```

Ordinary reusable row containers may explicitly use:

```csharp
HorizontalAlignment = HorizontalAlignment.Stretch
```

when required to make the contract unambiguous.

### Important: do not cargo-cult `Stretch`

WinUI defaults already stretch many `FrameworkElement`s. Therefore merely adding `HorizontalAlignment=Stretch` everywhere and declaring the bug fixed is not sufficient.

During implementation, inspect the real layout chain using `ActualWidth` in the debugger or temporary diagnostic output:

```text
OpaquePanel
AnimatedContent
TabStrip
BodyScroll
TabBody
active page root
selected/visible row Container
```

Find the first element whose `ActualWidth` is materially narrower than its available parent width and fix that exact constraint.

Temporary diagnostics used only to identify the width break should be removed before commit unless an existing diagnostic seam already makes the value useful long-term.

Do not create a new layout manager, binding framework, converter, or width-synchronization service.

---

## 4.5 Keep row-local padding narrow and symmetric

Do not use row-local padding to compensate for a page-width bug.

For normal rows, left/right row padding must remain symmetric.

The current Toggle/Slider pattern:

```csharp
Padding = new Thickness(12, 6, 12, 6)
```

is acceptable **if**, after the page-width fix, the resulting hardware layout has balanced content.

If a small visual adjustment is needed, change left and right together.

Examples of acceptable follow-up tuning:

```csharp
new Thickness(8, 6, 8, 6)
```

or

```csharp
new Thickness(12, 6, 12, 6)
```

Do not use asymmetric left/right row padding.

Do not zero row padding purely to hide a parent width bug.

---

## 4.6 Preserve PR #491 tab and row-selection polish

Do not regress the parts of PR #491 that are already correct:

- compact five-tab Buttons;
- selected accent-filled tab;
- all five fixed tab identities;
- persisted tab order;
- LB/RB tab switching;
- hidden vertical scrollbar;
- no footer hint;
- subtle ordinary-row selected background;
- Shortcut 2x2 selection exception;
- 400-DIP Overlay width;
- small outer HWND inset.

This PR is not another full visual redesign.

---

## 5. Explicit non-goals

Do not change:

- `OverlayWindowGeometry.PanelEdgeInsetDip`;
- Overlay outer X/Y/Width/Height policy;
- monitor selection;
- HWND border/titlebar behavior;
- show/hide animation timing;
- outside-click dismissal;
- no-activate/topmost behavior;
- OQ4 controller capture/release behavior;
- controller input semantics;
- tab order persistence/transport;
- Runtime IPC;
- PID1901/PID1902;
- HidHide;
- VIIPER;
- routing policy;
- Device feature contracts;
- Shortcut action contracts;
- ToggleSwitch/Slider templates;
- 400-DIP panel width.

Do not add a generalized responsive-layout abstraction for one fixed handheld Overlay.

---

## 6. Expected files

Primary expected changes:

```text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
```

Possible only if actual width inspection proves the reusable row itself is the first constraining element:

```text
src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/OverlaySliderRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayTabOrderRow.cs
```

Do not touch `OverlayWindowGeometry.cs` for this task.

Avoid unrelated cleanup.

---

## 7. Implementation sequence

Use this order so the visual problem is diagnosed rather than masked:

1. Rebase/update from current `main`.
2. Delete `Quick Settings` and collapse the shell to `Tabs + Body`.
3. Set equal internal Top/Left/Right shell padding.
4. Run the Overlay and inspect the actual width chain.
5. Fix the first element that prevents the active page/row from consuming the usable body width.
6. Only then consider a small symmetric row-padding adjustment if the hardware result still needs polish.
7. Confirm all five tabs still fit and all controller interaction is unchanged.

Do not start by changing many unrelated margins at once.

---

## 8. Validation

Primary visual target:

```text
MSI Claw
1920 x 1200
150% Windows scaling
```

### 8.1 Required visual checks

Verify:

- `Quick Settings` is completely absent;
- tabs now occupy the top content row;
- top internal gap equals left internal gap equals right internal gap;
- tabs have equal visible left/right distance from the Overlay surface edge;
- Device rows use the available body width rather than clustering to the left;
- ToggleSwitch reaches the expected right-side content boundary;
- Slider header/value and Slider track use the expected body width;
- no large unexplained right-side dead area remains;
- no inner card/box was introduced;
- vertical scrollbar remains hidden;
- selected row remains obvious;
- Shortcut 2x2 page still lays out correctly;
- Setting tab-order page still lays out correctly.

### 8.2 Interaction regression checks

Verify existing behavior remains unchanged:

```text
LB / RB     tab navigation
DPad/sticks row navigation
A           activation where supported
Left/Right  adjustment where supported
B           Overlay close
pointer/touch tab and control interaction
BringSelectedRowIntoView scrolling
outside click dismissal
```

### 8.3 Width sanity check

For at least one Device row on the hardware build, confirm the layout relationship using debugger/runtime inspection if needed:

```text
row Container ActualWidth ~= active page ActualWidth
active page ActualWidth ~= BodyScroll viewport width
```

Minor expected differences caused only by the row's own symmetric padding are fine.

The goal is not exact pixel equality between child content and its parent; the goal is to remove the unintended large right-side unused region.

---

## 9. Tests / verification commands

This is primarily a visual-layout PR, so do not invent a pure-state abstraction only to unit-test XAML spacing.

Run at minimum:

```text
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test
git diff --check
```

Existing Overlay navigation/selection/geometry tests must remain green.

No geometry test changes are expected because outer geometry is explicitly out of scope.

---

## 10. Acceptance criteria

The PR is complete when:

- [ ] `Quick Settings` title and its dedicated row are removed.
- [ ] Shell contains only the top tab row and body row.
- [ ] Internal Top / Left / Right shell padding use one equal value.
- [ ] Outer HWND/work-area inset from PR #491 is unchanged.
- [ ] 400-DIP Overlay width is unchanged.
- [ ] Active page content consumes the usable body width.
- [ ] Device Toggle/Slider rows no longer leave a large unexplained right-side dead area.
- [ ] Row-local left/right padding is symmetric.
- [ ] Five compact tabs remain fully readable.
- [ ] Hidden scrollbar behavior is preserved.
- [ ] Existing controller/pointer/touch semantics are unchanged.
- [ ] No Runtime/Full1902/controller authority file is changed.
- [ ] Release build passes.
- [ ] Full test suite passes.
- [ ] `git diff --check` passes.

---

## 11. Review policy for this PR

Review this as a narrow production-reachable UI defect correction.

Block for:

- title still visible;
- Top/Left/Right internal padding visibly different;
- content still constrained to a narrower left-aligned width leaving a large right dead area;
- tab clipping;
- broken scrolling/navigation/activation;
- regression to Overlay lifecycle behavior;
- unrelated Runtime/controller authority changes.

Do **not** block for:

- theoretical instruction-level races unrelated to this presentation change;
- requests for generalized responsive layout infrastructure;
- a new theme/layout manager;
- future multi-monitor/multi-session abstractions outside current product scope;
- stylistic alternatives that do not affect the approved layout contract.

Keep the fix in the existing Overlay shell/page/row ownership seams.