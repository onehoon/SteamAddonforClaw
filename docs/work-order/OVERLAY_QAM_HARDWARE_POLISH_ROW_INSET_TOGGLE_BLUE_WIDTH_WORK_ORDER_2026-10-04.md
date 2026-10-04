# Work Order — Overlay QAM Hardware Polish: Row Inset, Steam-Blue Toggle, and Slightly Wider Content

**Date:** 2026-10-04  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**Target branch:** main  
**Reviewed main baseline:** 610b559a0e8ac6cec06cfb595f6e0768b3a45a77  
**Feature area:** Standalone WinUI 3 Overlay visual/layout polish  
**Implementation shape:** one focused visual-fix PR  
**Hardware evidence:** user-provided MSI Claw Overlay screenshot showing the selected TDP row on the current QAM-style build

---

# 0. Goal

Fix three concrete hardware-visible issues without changing Overlay behavior or Full1902 authority:

~~~text
1. Selected-row fill currently appears flush against row content,
   even though QamRowPadding = 16,10,16,10 exists.

2. Enabled toggles currently render neutral gray instead of Steam blue.

3. The Overlay should be slightly wider,
   but ONLY by increasing the right content area.
   The left vertical rail must remain exactly 52 DIP.
~~~

Target:

~~~text
Current total width     416 DIP
Current rail             52 DIP
Current content         364 DIP

New total width         432 DIP
Rail                     52 DIP   unchanged
New content             380 DIP

Net change
→ content only +16 DIP
~~~

At the reference 150% scale:

~~~text
total width 624 px → 648 px
rail width remains 78 px
content width 546 px → 570 px

net content increase = 24 physical px
~~~

This is intentionally a small widening, not a return to the historical wide Overlay.

---

# 1. Mandatory authority review

Read current versions before implementation:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/work-order/OVERLAY_QAM_SHELL_FOUNDATION_PR1_WORK_ORDER_2026-10-02.md
docs/work-order/OVERLAY_QAM_VISUAL_FOUNDATION_PR_A_WORK_ORDER_2026-10-02.md
docs/work-order/OVERLAY_QAM_CONTROLS_PR_B_WORK_ORDER_2026-10-02.md
docs/work-order/OVERLAY_QAM_PAGE_POLISH_PR_C_WORK_ORDER_2026-10-02.md

docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
~~~

This PR is visual/layout-only.

Do not modify:

- controller ownership;
- PID1901 / PID1902;
- HidHide;
- VIIPER;
- DirectInput;
- presentation routing;
- capture/release-to-resume;
- Overlay process lifecycle;
- transport/protocol;
- Quick Settings mutation behavior;
- M1/M2 behavior;
- ClawHUD behavior;
- Profile behavior;
- Shortcut behavior;
- controller navigation semantics.

---

# 2. Mandatory source review

Inspect current main:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindowGeometry.cs
src/SteamInputAddonforClaw.Overlay/OverlayRowChrome.cs
src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml

src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
src/SteamInputAddonforClaw.Overlay/OverlayNumericSliderRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayValueRow.cs

tests/SteamInputAddonforClaw.UiTests/OverlayWindowGeometryTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayQamVisualResourcesTests.cs
~~~

---

# 3. Root cause of the selected-row inset problem

Do NOT treat this as a missing QamRowPadding value.

The current code already correctly defines:

~~~text
QamRowPadding = 16,10,16,10
QamRowMargin  = -16,0,-16,0
~~~

and OverlayRowChrome applies both.

The problem is the location of QamContentPadding relative to the ScrollViewer.

Current XAML:

~~~xml
<Grid Grid.Column="1"
      Margin="{StaticResource QamContentPadding}">
    <ScrollViewer
        x:Name="BodyScroll"
        HorizontalContentAlignment="Stretch">
        <Grid
            x:Name="TabBody"
            HorizontalAlignment="Stretch"
            Width="{Binding ElementName=BodyScroll, Path=ViewportWidth}" />
    </ScrollViewer>
</Grid>
~~~

This means the 16-DIP horizontal content inset reduces the ScrollViewer viewport itself.

Then each selected row tries to reproduce the measured Steam CSS behavior with:

~~~text
row margin  = -16 horizontal
row padding = +16 horizontal
~~~

But the negative portion extends outside the already-inset ScrollViewer viewport and is clipped.

Conceptually:

~~~text
current visible viewport starts here
│
│  row wants to start 16 DIP before viewport
│  ← -16
│  ┌─────────────────────────────
│  │ +16 padding
│  │ text
│
outside part is clipped
~~~

Therefore after clipping:

~~~text
visible selected background start
≈ visible text start
~~~

which is exactly what the hardware screenshot shows.

This is a layout hierarchy problem, not a padding-token problem.

---

# 4. Correct layout model

The ScrollViewer must own the full right content-column width.

The 16-DIP content inset must exist INSIDE that viewport.

Then the selected row can safely extend by -16 into that internal padding region while keeping +16 content padding.

Target hierarchy:

~~~text
right content column
└─ ScrollViewer                 full content-column width
   └─ Border / inset host
      Padding = QamContentPadding
      └─ TabBody
         └─ row
            Margin  = -16 horizontal
            Padding = +16 horizontal
~~~

Result:

~~~text
right content pane edge
│
┌────────────────────────────────────── selected fill
│    TDP                         toggle
│    ↑
│    real 16-DIP content inset
└──────────────────────────────────────
~~~

This matches the semantic intent of the measured Steam QAM CSS:

~~~text
row padding: 10px vertical / 16px horizontal
row horizontal margin: -16px
~~~

---

# 5. Required OverlayWindow.xaml change

Do not increase QamRowPadding as a workaround.

Move QamContentPadding from the Grid OUTSIDE BodyScroll to an inset host INSIDE BodyScroll.

Preferred structure:

~~~xml
<Grid Grid.Column="1">
    <ScrollViewer
        x:Name="BodyScroll"
        VerticalScrollBarVisibility="Hidden"
        HorizontalScrollBarVisibility="Disabled"
        HorizontalContentAlignment="Stretch">

        <Border
            Padding="{StaticResource QamContentPadding}"
            HorizontalAlignment="Stretch">

            <Grid
                x:Name="TabBody"
                HorizontalAlignment="Stretch" />
        </Border>
    </ScrollViewer>
</Grid>
~~~

Exact formatting may vary.

Important requirements:

- BodyScroll occupies the full right content column.
- QamContentPadding is inside BodyScroll.
- TabBody remains the single page host.
- Remove the old TabBody Width binding to BodyScroll.ViewportWidth if the new stretch/inset host makes it redundant.
- Do not keep both the old outer Grid Margin and the new inner Padding.
- Do not create a second ScrollViewer.
- Do not add clipping workarounds.
- Do not change OverlayRowChrome padding/margin values.

The desired implementation should solve the root hierarchy issue rather than compensate numerically.

---

# 6. Preserve measured row tokens

Keep:

~~~xml
<Thickness x:Key="QamRowPadding">16,10,16,10</Thickness>
<Thickness x:Key="QamRowMargin">-16,0,-16,0</Thickness>
~~~

Do not change them in this PR.

Keep:

~~~text
QamRowMinHeight = 42
QamRowCornerRadius = 2
QamSelectedFillBrush = #26FFFFFF
~~~

The hardware bug is that the existing tokens were placed in a layout where their intended visual relationship was clipped.

---

# 7. Why the fix applies globally

OverlayRowChrome is shared by:

~~~text
OverlayToggleRow
OverlayNumericSliderRow
OverlayValueRow
OverlayTabOrderRow
Setting expandable header rows
~~~

Therefore fixing the viewport/inset hierarchy once should correct selection-fill/content inset behavior consistently.

Do not special-case the TDP row.

Do not add per-control margins.

Do not add a selected-only padding branch.

---

# 8. Toggle ON color

Current resources:

~~~text
QamAccentBrush   = #8B929A
QamToggleOnBrush = QamAccentBrush
~~~

This produces the gray ON toggle visible in the hardware screenshot.

Change the Toggle ON rail to Steam blue.

Add one explicit Steam blue brush:

~~~xml
<SolidColorBrush x:Key="QamSteamBlueBrush"
                 Color="#FF1A9FFF" />
~~~

Then:

~~~xml
<StaticResource x:Key="QamToggleOnBrush"
                ResourceKey="QamSteamBlueBrush" />
~~~

Keep OFF and thumb behavior unchanged.

Target:

~~~text
OFF
→ dark/neutral rail
→ light thumb

ON
→ Steam blue #1A9FFF rail
→ light thumb
~~~

---

# 9. Evidence boundary for Steam blue

The prior live QAM inspection did not have an active ToggleField available, so the old neutral toggle color was explicitly documented as provisional.

Additional reference evidence supports Steam blue:

~~~text
Steam common UI token:
--gpColor-Blue: #1A9FFF

Steam Deck theme sources that target gamepaddialog ToggleRail
identify #1A9FFF as the default toggle color / override the same Steam blue token.
~~~

Reference source:

~~~text
SteamTracking/SteamTracking
steamcommunity.com/public/shared/css/shared_global.css

Tormak9970/SteamDeckThemes
Desktop/ColoredTogglesDesktop/shared.css
~~~

The hardware screenshot now demonstrates that the provisional gray choice is visually wrong for the intended Steam-QAM appearance.

Update QAM_VISUAL_REFERENCE_2026-10-02.md to distinguish:

~~~text
#1A9FFF = evidence-backed Steam blue token / toggle direction

NOT
live 2026 computed ToggleField color measurement
~~~

Do not overclaim a live computed-style capture that was not performed.

---

# 10. Do not recolor Slider in this PR

Do not automatically change:

~~~text
QamSliderValueTrackBrush
QamSliderTrackBrush
QamSliderThumbBrush
~~~

to blue merely because Toggle ON is blue.

The current Slider resource values came from separate stylesheet evidence and still lack a live current QAM computed-style capture.

This PR changes only the control whose current hardware appearance is clearly wrong.

---

# 11. Slight width increase: 416 → 432 DIP

Change:

~~~text
OverlayWindowGeometry.MaxSurfaceWidthDip
416.0
→ 432.0
~~~

and:

~~~xml
OpaquePanel MaxWidth="416"
→
OpaquePanel MaxWidth="432"
~~~

Do not introduce a second width constant or responsive width policy.

The existing single geometry owner remains authoritative.

---

# 12. Left rail must remain exactly unchanged

Keep:

~~~xml
<ColumnDefinition Width="52" />
~~~

Keep:

~~~text
QamRailButtonSize = 52
QamRailItemHeight = 64
QamRailIconSize = 24
~~~

No rail geometry changes.

Therefore:

~~~text
old:
416 total - 52 rail = 364 content

new:
432 total - 52 rail = 380 content
~~~

All +16 DIP goes to the right content region.

This directly satisfies the product request.

---

# 13. New reference geometry at 150%

Reference device:

~~~text
1920 × 1200
150% / 144 DPI
bottom taskbar work bottom = 1128
floating gap = 4 DIP = 6 px
~~~

New expected Overlay rectangle:

~~~text
width = 432 DIP × 1.5 = 648 px
right edge = 1920 - 6 = 1914
x = 1914 - 648 = 1266

OverlayRect(
    X      = 1266,
    Y      = 6,
    Width  = 648,
    Height = 1116)
~~~

Old was:

~~~text
OverlayRect(1290, 6, 624, 1116)
~~~

The right edge remains unchanged.

The Overlay expands only toward the left by 24 physical px at 150%.

---

# 14. Expected geometry values by DPI

Update existing geometry tests.

For a 1920 × 1200 monitor with no reserved edge:

~~~text
96 DPI:
gap=4
width=432
x=1484
height=1192

120 DPI:
gap=5
width=540
x=1375
height=1190

144 DPI:
gap=6
width=648
x=1266
height=1188

168 DPI:
gap=7
width=756
x=1157
height=1186

192 DPI:
gap=8
width=864
x=1048
height=1184
~~~

For the existing bottom-taskbar reference at 144 DPI:

~~~text
OverlayRect(1266, 6, 648, 1116)
~~~

For the existing non-zero-origin test:

~~~text
monitor = 100..2020
right usable edge = 2014
width = 648
x = 1366

OverlayRect(1366, 46, 648, 1116)
~~~

For right taskbar reservation:

~~~text
workRight = 1848
right usable edge = 1842
x = 1194

OverlayRect(1194, 6, 648, 1188)
~~~

For left taskbar reservation:

~~~text
OverlayRect(1266, 6, 648, 1188)
~~~

For top taskbar reservation:

~~~text
OverlayRect(1266, 78, 648, 1116)
~~~

For zero/default DPI:

~~~text
OverlayRect(1484, 4, 432, 1192)
~~~

Update the adjacent-point width assertion from 624 to 648.

Do not weaken edge/work-area coverage.

---

# 15. QAM visual resource tests

Update OverlayQamVisualResourcesTests so it no longer asserts the frozen 416-DIP shell.

New assertions should prove:

~~~text
OpaquePanel MaxWidth = 432
rail column Width = 52
QamContentPadding still = 16,16,16,12

QamContentPadding is applied inside BodyScroll
old outer content Grid no longer owns QamContentPadding as Margin

single ScrollViewer remains
vertical scrollbar remains hidden
horizontal scrollbar remains disabled

QamRowPadding remains 16,10,16,10
QamRowMargin remains -16,0,-16,0

QamSteamBlueBrush exists once
QamSteamBlueBrush = #FF1A9FFF
QamToggleOnBrush points to QamSteamBlueBrush
~~~

Do not add screenshot/golden-image tests.

---

# 16. Add a focused source-shape regression for the root cause

The test should make the layout invariant explicit.

It should fail if a future refactor puts content padding outside the ScrollViewer again.

For example, structurally verify:

~~~text
BodyScroll is in the full Grid.Column=1 host
QamContentPadding is on a descendant INSIDE BodyScroll
TabBody is inside that padded descendant
~~~

and verify the old shape is absent:

~~~text
Grid.Column=1 + Margin=QamContentPadding directly wrapping BodyScroll
~~~

Do not rely only on string order if a small XDocument traversal can express the relationship robustly.

---

# 17. Preserve interaction behavior

The following must remain unchanged:

~~~text
LB/RB tab navigation
Up/Down logical row selection
Left/Right value adjustment
A activation/toggle
B collapse/back/dismiss

pointer row selection
pointer Toggle interaction
pointer Slider drag
BringIntoView
single BodyScroll
selection restoration after authoritative refresh
~~~

The layout host change must not create a second focus/navigation owner.

---

# 18. Verify nested negative margins after the fix

Device TDP is a feature-header section:

~~~text
TDP toggle row
detailStack Margin = 16 left
PL1 / PL2 rows
~~~

The header row should get full-pane selection fill with proper inner content inset.

The detail rows should retain their existing feature-child indentation.

Do not remove:

~~~text
QamDetailIndent = 16,0,0,0
~~~

merely because the root row inset was fixed.

Hardware acceptance should confirm:

~~~text
TDP header
→ full selected fill, 16-DIP content inset

PL1 / PL2
→ visibly nested relative to TDP header
~~~

---

# 19. Manual acceptance on MSI Claw

Reference:

~~~text
1920 × 1200
150%
~~~

Validate the exact screenshot scenario again.

Required:

### Selected TDP row

~~~text
selected fill extends toward content-pane edges
TDP text does NOT touch the selected fill edge
toggle does NOT touch the selected fill edge
clear inner horizontal inset is visible
~~~

### Other row types

~~~text
CPU Boost toggle row
Windows Power Mode toggle row
discrete Plugged in rows
numeric PL1 / PL2 slider rows
Setting rows
Controller M1/M2 rows
~~~

should keep coherent alignment.

### Toggle

~~~text
OFF → neutral/dark
ON  → Steam blue
thumb remains light/legible
disabled remains readable
~~~

### Width

~~~text
panel is only slightly wider
left rail appears exactly unchanged
right edge / floating gap unchanged
extra width is visibly available only to content
long values get slightly more breathing room
~~~

---

# 20. Expected files

Primary:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindowGeometry.cs
src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml

docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md

tests/SteamInputAddonforClaw.UiTests/OverlayWindowGeometryTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayQamVisualResourcesTests.cs
~~~

OverlayRowChrome.cs should normally require no production change.

OverlayToggleRow.cs should normally require no production change because it already consumes QamToggleOnBrush locally.

No Runtime/Contracts/FrontendTransport changes should be required.

---

# 21. Explicit non-goals

Not in this PR:

- changing the 52-DIP rail;
- changing rail icon size;
- changing tab behavior;
- changing Profile column count;
- changing Shortcut column count;
- changing row padding from 16/10;
- changing row negative margin from -16;
- changing Slider colors;
- redesigning Toggle geometry;
- adding a custom Toggle template;
- changing Device/Profile contracts;
- changing mutation timing;
- changing controller navigation;
- new ScrollViewer;
- responsive layout framework;
- new visual abstraction;
- Runtime/transport changes.

---

# 22. Overengineering guardrail

Desired implementation:

~~~text
OverlayWindow.xaml
→ move existing content inset inside existing ScrollViewer

OverlayWindowGeometry.cs
→ 416 → 432

QamOverlayResources.xaml
→ one Steam-blue brush
→ Toggle ON points to it

tests/docs
→ update exact intended contract
~~~

Nothing else.

Do not add:

- layout manager;
- width service;
- selected-row wrapper class;
- clipping workaround;
- custom Toggle state machine;
- new theme manager.

---

# 23. Definition of done

Complete when:

~~~text
selected rows visibly retain their inner 16-DIP content inset
negative row margin is no longer clipped by an externally inset ScrollViewer
QamRowPadding and QamRowMargin measured values remain unchanged

Toggle ON uses #1A9FFF Steam blue
Toggle OFF remains neutral

total Overlay width = 432 DIP
left rail = 52 DIP unchanged
right content = 380 DIP
right-side anchoring/floating gap unchanged

all existing Overlay behavior remains unchanged
all geometry/UI tests pass
normal Build and Test passes
~~~
