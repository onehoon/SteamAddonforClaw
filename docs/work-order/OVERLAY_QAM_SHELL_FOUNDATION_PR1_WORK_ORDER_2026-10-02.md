# Work Order — QAM-Like Overlay Shell Foundation PR1

**Date:** 2026-10-02  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**Target branch:** main  
**Reviewed main baseline:** 5d3c9f53f518c50ad7516e47554abe43e4058451  
**Feature area:** WinUI 3 Addon Overlay presentation only  
**Implementation shape:** one focused PR

---

## 0. Goal

Convert the current Addon Overlay shell from a centered, wide, horizontal-tab layout into the first structural version of a Steam Quick Access Menu-like shell.

This PR is intentionally limited to:

1. a compact right-side Overlay window;
2. a left-side vertical tab rail inside that window;
3. icon-first tab buttons;
4. preservation of all existing tab identities, Runtime-authoritative tab order, page lifetime, row navigation, and LB/RB tab navigation.

This PR does not yet copy the final Steam QAM color palette, Motiva Sans typography, Korean fallback font, or QAM control chrome. Those require the measured QuickAccess computed-style/font results and belong in the next focused visual PR.

The purpose of PR1 is to establish the correct physical shell first so subsequent QAM visual tuning is applied to the final layout rather than to the current obsolete horizontal-tab shell.

Required visual structure:

~~~text
screen
┌──────────────────────────────────────────────────────────────────────────┐
│                                                        ┌─────┬─────────┐ │
│                                                        │     │         │ │
│                                                        │ tab │ content │ │
│                                                        │rail │         │ │
│                                                        │     │         │ │
│                                                        └─────┴─────────┘ │
└──────────────────────────────────────────────────────────────────────────┘
                                                         ^
                                                         right-side Overlay

Overlay internal layout
┌──────┬──────────────────────────────────────┐
│ icon │                                      │
│ icon │           current page               │
│ icon │                                      │
│ icon │                                      │
│ icon │                                      │
└──────┴──────────────────────────────────────┘
  ^
  left vertical tab rail
~~~

---

# 1. Mandatory source/design review before coding

Read the latest versions of the following before editing.

## 1.1 Full1902 authority

At minimum:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
~~~

This PR is frontend presentation only.

Do not change:

- PID1901 ↔ PID1902 ownership;
- DirectInput ownership;
- HidHide normalization/ownership;
- VIIPER ownership/teardown;
- X360 ↔ SteamDeck presentation policy;
- Overlay capture/neutral ordering;
- Runtime-owned dismiss;
- Sleep / Hibernate / Resume behavior;
- Restart / Crash / Shutdown behavior;
- PnP recovery;
- fail-close behavior.

The Overlay remains a disposable presentation surface, not a controller authority.

## 1.2 Current Overlay architecture

Read:

~~~text
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md
~~~

Important override for this PR:

The old UI-design document still describes a horizontal tab strip and older placement assumptions. Those specific visual-shell sections are superseded by this work order.

The following existing behavior remains authoritative:

~~~text
LB  -> previous tab
RB  -> next tab
Up/Down -> page row navigation
Left/Right -> current row adjustment
A -> activate/toggle
B -> dismiss Overlay
~~~

Do not add a new controller-focus mode for the vertical rail.

## 1.3 Current source

Read at minimum:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindowGeometry.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Presentation.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
src/SteamInputAddonforClaw.Overlay/WindowInterop.cs

tests/SteamInputAddonforClaw.UiTests/OverlayWindowGeometryTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
~~~

Do not refactor unrelated Overlay rows/pages merely because the shell changes.

---

# 2. Reviewed current implementation

The reviewed main baseline currently has:

~~~text
OverlayWindowGeometry.MaxSurfaceWidthDip = 720
OverlayWindow X position                = horizontally centered
outer margin                            = one symmetric value on all four edges
XAML theme                              = Light
top navigation                          = 5-column horizontal strip
tab content                             = text labels
selected-tab indicator                  = horizontal bottom bar
BodyScroll                              = below the horizontal strip
LB/RB                                   = previous/next tab
Runtime tab order                       = authoritative and live-applied
~~~

The current shell is functionally correct but visually closer to a compact desktop settings panel than Steam Quick Access.

---

# 3. Reference QAM observation and PR1 target

Local Steam inspection established that the real Steam QAM is open as QuickAccess_uid17 and visually consists of:

~~~text
left vertical rail
+
right content panel
~~~

Observed approximate QAM width:

~~~text
~490 physical px in the captured environment
≈ 48 CSS px rail + 300 CSS px content
~~~

This is a visual measurement, not a final computed-style contract.

The Addon Overlay is intentionally allowed to be somewhat wider than Steam QAM.

For the MSI Claw reference display:

~~~text
1920 × 1200
150% scaling
~~~

PR1 target:

~~~text
MaxSurfaceWidthDip = 416 DIP
416 × 1.5 = 624 physical px
~~~

This is visibly wider than the observed QAM while remaining a compact right-side quick-settings surface.

Do not add a user-configurable width setting.

Hardware tuning can later compare a narrow bounded range such as 400 / 416 / 432 DIP, but PR1 should implement 416 DIP as the single baseline.

---

# 4. Window geometry — right-side compact placement

## 4.1 Replace centered positioning

The current geometry centers the capped surface.

Replace this with right-side placement.

The right edge should remain slightly inset from the usable monitor edge.

## 4.2 Stop using one maximum reserved edge as all four margins

The current calculation finds the maximum reserved edge and applies it to every side.

On the reference display this lets the bottom taskbar reservation inflate the right-side gap, which is undesirable for a QAM-like panel.

PR1 should instead calculate usable bounds per edge:

~~~text
left usable   = monitorLeft   + reservedLeft   + floatingGap
top usable    = monitorTop    + reservedTop    + floatingGap
right usable  = monitorRight  - reservedRight  - floatingGap
bottom usable = monitorBottom - reservedBottom - floatingGap
~~~

Then:

~~~text
availableWidth = max(0, right usable - left usable)
width          = min(availableWidth, MaxSurfaceWidth)
x              = max(left usable, right usable - width)

height         = max(0, bottom usable - top usable)
y              = top usable
~~~

This preserves real work-area safety without forcing the bottom taskbar reservation onto the right/top edges.

Do not add monitor-policy abstractions.

## 4.3 Floating gap

Use:

~~~csharp
internal const double FloatingGapDip = 4.0;
internal const double MaxSurfaceWidthDip = 416.0;
~~~

At 150% DPI:

~~~text
4 DIP -> 6 physical px
~~~

This matches the intended slight floating appearance without creating the large current side gap.

The work-area reservation still protects a visible taskbar independently.

## 4.4 Remove obsolete reference-taskbar geometry if no longer needed

If ReferenceTaskbarDip is used only to manufacture the old symmetric margin, remove it rather than preserving dead geometry policy.

Do not keep two competing margin models.

If diagnostics still need geometry metrics, change OverlayGeometryMetrics only as much as needed to represent the new per-edge facts.

A narrow acceptable shape is:

~~~csharp
internal readonly record struct OverlayGeometryMetrics(
    int MonitorWidth,
    int MonitorHeight,
    int WorkWidth,
    int WorkHeight,
    int ReservedLeftPx,
    int ReservedTopPx,
    int ReservedRightPx,
    int ReservedBottomPx,
    int FloatingGapPx);
~~~

Do not create a separate geometry service or monitor-layout manager.

## 4.5 Reference expected rectangle

For:

~~~text
monitor = 0,0 -> 1920,1200
work    = 0,0 -> 1920,1128
dpi     = 144 / 150%
~~~

Expected facts:

~~~text
reservedLeft   = 0
reservedTop    = 0
reservedRight  = 0
reservedBottom = 72
floatingGap    = 6
max width      = 416 DIP = 624 px

x      = 1920 - 6 - 624 = 1290
y      = 6
width  = 624
height = 1200 - 6 - (72 + 6) = 1116
~~~

Expected result:

~~~csharp
new OverlayRect(1290, 6, 624, 1116)
~~~

Update geometry tests from this exact contract.

Also preserve:

- non-zero monitor origins;
- DPI scaling;
- zero-sized monitor safety;
- unusually small monitor clamping;
- taskbar/work-area reservation;
- no negative dimensions.

---

# 5. XAML shell — left vertical rail + right content

Replace the horizontal top strip with a two-column shell.

Target conceptual XAML:

~~~xml
<Grid x:Name="QamShell">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="52" />
        <ColumnDefinition Width="*" />
    </Grid.ColumnDefinitions>

    <Border
        x:Name="TabRail"
        Grid.Column="0">
        <Grid x:Name="TabStrip" />
    </Border>

    <Grid
        Grid.Column="1"
        Margin="16,16,16,12">
        <ScrollViewer
            x:Name="BodyScroll"
            VerticalScrollBarVisibility="Hidden"
            HorizontalScrollBarVisibility="Disabled"
            HorizontalContentAlignment="Stretch">
            <Grid
                x:Name="TabBody"
                HorizontalAlignment="Stretch"
                Width="{Binding ElementName=BodyScroll, Path=ViewportWidth}" />
        </ScrollViewer>
    </Grid>
</Grid>
~~~

Exact nesting may follow the current source if a smaller edit is cleaner.

Required invariants:

- rail is physically on the left;
- body is physically on the right;
- rail does not scroll with page body;
- body keeps the existing shared BodyScroll;
- no new nested page ScrollViewer;
- no footer/hint bar;
- no generic Quick Settings title is reintroduced;
- current page builders remain unchanged in PR1.

Use:

~~~text
rail width = 52 DIP
~~~

This is close to the observed ~48 CSS px QAM rail while leaving comfortable WinUI hit targets.

Do not make the rail width user-configurable.

---

# 6. Vertical tab construction

## 6.1 Preserve the five fixed identities

Keep exactly:

~~~text
Device
Profile
Controller
Shortcut
Setting
~~~

Keep Runtime-authoritative ordering.

Do not:

- add/remove tabs;
- introduce nested tabs;
- create plugin tabs;
- create a new navigation model.

## 6.2 Change placement from columns to rows

Current BuildShell() builds five tab hosts into five columns.

Change the shell to five vertical positions.

Conceptually:

~~~text
for each tab position
    ensure TabStrip.RowDefinitions contains one fixed/auto row
    Grid.SetRow(tabHost, position)
~~~

Use a neutral variable name such as position, not column.

When authoritative tab order changes, ApplyTabOrderState() must reposition tab hosts with:

~~~csharp
Grid.SetRow(tabHost, position);
~~~

instead of Grid.SetColumn(tabHost, position).

The Setting page's own tab-order editor already has its independent row positioning. Do not conflate the two grids.

## 6.3 Icon-first rail buttons

Normal rail buttons should show an icon rather than the full text label.

Use built-in WinUI SymbolIcon / FontIcon primitives backed by the platform Fluent icon font.

Do not:

- add an icon package;
- bundle copied Steam icon assets;
- add SVG files for this PR;
- create an icon service.

Use one small mapping local to the shell.

The semantic intent should be:

~~~text
Device      -> device/system icon
Profile     -> profile/document/game-profile icon
Controller  -> controller/input icon
Shortcut    -> grid/quick-action icon
Setting     -> settings gear
~~~

If a suitable icon is not present in the Symbol enum, use one verified Segoe Fluent Icons glyph through FontIcon.

Do not invent raw glyph code points without verifying them against the Windows App SDK version used by the project.

The visible icon can evolve in a later visual polish PR; PR1 only needs a coherent semantic set.

Recommended button geometry:

~~~text
rail width       52 DIP
button width     44 DIP
button height    44 DIP
horizontal align Center
padding          0
~~~

Keep sufficiently large pointer/touch targets.

## 6.4 Preserve accessible text identity

Even though the visible button is icon-only, preserve the tab's text label for accessibility and pointer discoverability.

At minimum:

~~~text
ToolTipService.ToolTip = LabelFor(id)
AutomationProperties.Name = LabelFor(id)
~~~

Do not use the icon glyph as the accessible name.

## 6.5 Selected indicator becomes vertical

The existing 3-pixel horizontal indicator below each tab does not fit a vertical rail.

Change the existing indicator primitive rather than creating another selection framework.

Target:

~~~text
Width               = 3
HorizontalAlignment = Right
VerticalAlignment   = Stretch
selected visibility = existing selected-tab state
~~~

The selected indicator should sit at the rail/content boundary.

The existing selected button background/foreground behavior may remain temporarily.

Final Steam QAM selection colors/chrome belong in the next visual PR.

---

# 7. Preserve controller navigation exactly

This is a critical acceptance requirement.

The vertical rail is a visual shell change only.

Keep:

~~~text
LB -> SelectPreviousTab()
RB -> SelectNextTab()
~~~

Keep the existing Runtime semantic action flow in App.xaml.cs.

Do not reinterpret vertical rail orientation as a reason to change controller controls.

Specifically, do not add:

- DPad-left to enter rail;
- DPad-right to leave rail;
- a rail-focus boolean;
- a second logical selection model;
- a tab/body focus state machine;
- a focus epoch/barrier;
- duplicated tab navigation in Navigation.cs.

After a tab switch, retain current behavior:

~~~text
apply selected tab visuals
show selected page
reset body scroll to top
reset row selection to first selectable row
apply row selection visual
~~~

Pointer/touch can still directly click a rail tab.

---

# 8. Preserve Runtime-authoritative tab order

Current product behavior lets Runtime persist and publish the five-tab order.

That behavior remains.

Requirements:

- first configured tab remains the startup tab on every Show;
- live authoritative reorder preserves current selected tab/page semantics;
- the Setting tab editor still changes order through the existing typed intent;
- the vertical rail immediately reflects an authoritative reorder;
- page instances are not rebuilt merely because rail order changes.

Only the visual placement changes from column index to row index.

Do not add another local persisted order.

---

# 9. Presentation and window-lifetime behavior stays unchanged

Do not change:

~~~text
WS_EX_NOACTIVATE
tool-window behavior
always-on-top behavior
rounded-corner request
outside-click dismissal
ShowWithoutActivation
hide/show lifecycle
foreground monitor selection
composition animation durations
Overlay capture semantics
~~~

OverlayWindow.Presentation.cs should need no behavioral redesign.

If the narrower/right-side surface exposes a genuinely incorrect animation center, only make the smallest presentation correction required by the existing AnimatedContent bounds.

Do not replace the animation system in this PR.

---

# 10. Styling intentionally deferred to PR2

PR1 must not guess Steam QAM visual constants.

The next focused work order will apply measured QAM values for:

~~~text
surface background
rail background
primary text
secondary text
disabled text
selected/focus accent
row background states
separator colors
font size
font weight
line height
section spacing
toggle chrome
slider chrome
value/control chrome
~~~

It will also decide the final font path after validating:

~~~text
Steam clientui.uifont / Motiva Sans accessibility from WinUI 3
Korean rendered fallback font
safe fallback when Motiva is not directly consumable
~~~

PR1 must therefore not:

- copy or extract clientui.uifont;
- bundle Motiva Sans;
- add a custom font parser;
- install fonts into Windows;
- hard-code guessed Steam colors;
- add Mica/Acrylic/blur/shadow;
- add a theme manager.

Keep the existing theme/colors temporarily until the measured style PR.

---

# 11. Tests

## 11.1 Geometry tests

Update OverlayWindowGeometryTests to assert the new right-side/per-edge contract.

Required reference test:

~~~csharp
Assert.Equal(new OverlayRect(1290, 6, 624, 1116), result);
~~~

At minimum retain/add coverage for:

1. 1920×1200 at 150%;
2. DPI scaling;
3. non-zero monitor origin;
4. right-side taskbar reservation;
5. left-side taskbar reservation;
6. top-side taskbar reservation;
7. bottom-side taskbar reservation;
8. no reserved edge / fullscreen work area;
9. unusually small monitor;
10. zero-sized monitor.

Do not loosen assertions to ranges when the calculation is deterministic.

## 11.2 Shell/source-contract tests

Update/add focused tests that prove:

~~~text
horizontal 5-column rail is gone
vertical rail exists
tab hosts use row positioning
authoritative reorder uses Grid.SetRow
LB/RB SelectPreviousTab/SelectNextTab seams still exist
BodyScroll remains one shared body scroller
~~~

Do not build a new WinUI UI-test harness merely for this PR.

Existing source/composition regression style is acceptable because constructing OverlayWindow without a XAML host is already a known test limitation.

## 11.3 Existing tests

Run the normal solution/test suite used by current Overlay PRs.

Do not fix unrelated flaky or historical tests as part of this PR.

---

# 12. Files expected to change

Primary:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindowGeometry.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
tests/SteamInputAddonforClaw.UiTests/OverlayWindowGeometryTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
~~~

Only if genuinely required by the final compiled shape:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Presentation.cs
~~~

Do not touch Runtime/controller infrastructure.

---

# 13. Explicit non-goals

Not in PR1:

- Steam QAM final colors;
- Motiva Sans integration;
- Korean font fallback;
- final QAM typography;
- QAM toggle/slider templates;
- row-height/chrome redesign;
- Device/Profile/Controller content redesign;
- Shortcut behavior redesign;
- tab-order product changes;
- controller navigation changes;
- PID1902/controller work;
- HidHide work;
- VIIPER work;
- Steam GamepadUI/CEF patching;
- CTW integration;
- user-configurable Overlay width/rail width;
- generalized navigation/theme framework.

---

# 14. Overengineering guardrails

This is a visual-shell PR.

Prefer:

~~~text
one geometry calculation
one existing tab state authority
one existing page/row selection authority
one small tab-icon mapping
existing Runtime tab-order authority
existing LB/RB semantics
~~~

Do not add abstractions for unsupported or theoretical scenarios.

No new:

- shell manager;
- navigation authority;
- focus manager;
- geometry service;
- theme service;
- tab registry;
- layout state machine;
- synchronization epoch/barrier;
- custom icon asset system.

A small helper or local mapping is sufficient.

---

# 15. Hardware acceptance

Validate on the target MSI Claw reference environment:

~~~text
1920 × 1200
150% display scaling
~~~

Required visible result:

1. Overlay appears on the right side rather than centered.
2. Right edge has only a small floating gap.
3. Visible Windows taskbar/work-area reservation is still respected.
4. Overlay width is approximately 624 physical px at 150%.
5. Left rail is clearly separate from right content.
6. Five rail icons fit vertically without scrolling.
7. Pointer/touch can select each tab.
8. LB/RB changes previous/next tab exactly as before.
9. Up/Down stays page-row navigation.
10. Left/Right stays current-row adjustment.
11. A activation and B dismiss remain unchanged.
12. Runtime-authoritative tab reordering visibly reorders the vertical rail.
13. Closing/reopening starts on the first configured tab as before.
14. Scrollbar remains hidden.
15. No controller routing/presentation change occurs merely because the shell changed.

Also test:

- desktop/no-game foreground;
- Big Picture visible behind the Addon Overlay;
- at least one Steam game;
- bottom taskbar present;
- fullscreen/work-area == monitor case.

---

# 16. Definition of done

PR1 is complete when:

~~~text
centered 720-DIP horizontal-tab Overlay
    ->
right-side 416-DIP Overlay
+
left 52-DIP vertical icon rail
+
right content body
~~~

while preserving:

~~~text
LB/RB tab navigation
Runtime-authoritative tab order
startup-first-tab behavior
row selection/navigation
Overlay capture/dismiss behavior
Full1902 controller authority
~~~

and without prematurely implementing guessed Steam styling.

After this PR is hardware-validated, prepare PR2 from the measured Steam QuickAccess computed styles and font evidence:

~~~text
QAM palette
+ typography
+ selection/focus chrome
+ row/control chrome
~~~
