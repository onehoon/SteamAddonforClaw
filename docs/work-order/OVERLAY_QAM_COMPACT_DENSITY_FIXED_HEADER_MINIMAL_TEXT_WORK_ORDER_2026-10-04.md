# Work Order — Overlay QAM Compact Density, Fixed Page Header, and Minimal-Text Policy

**Date:** 2026-10-04  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**Target branch:** main  
**Reviewed main baseline:** 88e4e378b0b54a6d2e0b065c13802d9e01381aa6  
**Feature area:** Standalone WinUI 3 Overlay visual density / fixed header / information hierarchy  
**Implementation shape:** one focused UI-only PR  
**Hardware evidence:** user-provided MSI Claw screenshots compared directly with a real Steam QAM / Deck-style quick-settings surface

---

# 0. Goal

The current Overlay now has the correct overall shell, width, rail, dark palette, Steam-blue Toggle ON state, and selected-row horizontal inset.

The remaining major visual mismatch is vertical density.

Current hardware screenshots show:

~~~text
Addon Overlay
→ ordinary rows are visually very tall
→ selected row blocks are roughly 60 DIP high
→ large apparent gaps exist between controls
→ Setting page becomes unnecessarily long
→ page title scrolls away when selection moves downward
→ Main-App-style status/explanation text consumes additional vertical space
~~~

The target is:

~~~text
Steam-QAM-like compact rows
+
fixed page title
+
scroll only the page body
+
option label + current value/control as the default information model
+
no supplemental status/help/explanation text by default
~~~

This PR is presentation-only.

It must not change Runtime authority, controller semantics, mutation semantics, transport, persistence, or feature behavior.

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
docs/work-order/OVERLAY_QAM_HARDWARE_POLISH_ROW_INSET_TOGGLE_BLUE_WIDTH_WORK_ORDER_2026-10-04.md

docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
~~~

Do not modify:

- PID1901 / PID1902 ownership;
- HidHide;
- VIIPER;
- DirectInput;
- Xbox360 / SteamDeck presentation;
- capture / release-to-resume;
- Overlay process lifecycle;
- named-pipe protocol;
- Quick Settings contracts;
- ClawHUD contracts;
- Profile contracts;
- Shortcut contracts;
- M1/M2 contracts;
- tab-order persistence;
- controller navigation semantics.

---

# 2. Mandatory source review

Inspect current main before editing:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml
src/SteamInputAddonforClaw.Overlay/OverlayRowChrome.cs
src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayValueRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayNumericSliderRow.cs

src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shortcuts.cs
src/SteamInputAddonforClaw.Overlay/OverlayTabOrderRow.cs

src/SteamInputAddonforClaw.Overlay/OverlayQuickSettingsPageBinding.cs
~~~

Inspect all current UI tests that assert these source shapes before changing them.

---

# 3. Root cause of the excessive ordinary-row height

The current shared QAM row tokens are already:

~~~text
QamRowPadding   = 16,10,16,10
QamRowMinHeight = 42
QamRowSpacing   = 0
~~~

These values are not the main problem.

The real problem is that the child controls are much taller than the 20-22 DIP content that the measured Steam row geometry expects.

## Toggle row

Current WinUI ToggleSwitch path:

~~~text
OverlayRowChrome top padding       10
native ToggleSwitch effective      40
OverlayRowChrome bottom padding    10
-------------------------------------
actual ordinary row                60 DIP
~~~

Microsoft's current WinUI ToggleSwitch template uses:

~~~text
ToggleSwitchPreContentMargin   = 10
track                          = 20
ToggleSwitchPostContentMargin  = 10
----------------------------------
native control height          = 40
~~~

The Addon QAM style also currently sets:

~~~text
QamToggleStyle MinHeight = 40
~~~

Therefore the measured Steam row padding is being added around an already-padded 40-DIP native control.

## Value/discrete row

Current QamValueButtonSize is 40 and is used for both width and minimum height.

Therefore:

~~~text
10 top padding
40 button height
10 bottom padding
----------------
60 DIP row
~~~

This matches the approximately 90-physical-pixel selected row visible at 150% scaling in the hardware screenshot.

The root cause is therefore:

> WinUI's default interaction/control height is being nested inside Steam's measured row padding.

Do not reduce QamRowPadding to compensate.

---

# 4. Ordinary row target

Preserve the measured Steam row geometry:

~~~text
row horizontal padding = 16
row vertical padding   = 10
row minimum height     = 42
row spacing            = 0
~~~

Target ordinary row:

~~~text
10 top padding
~20-22 control/content
10 bottom padding
----------------------
~40-42 DIP total
~~~

At 150%:

~~~text
42 DIP ≈ 63 physical px
~~~

This should visually resemble the compact real QAM rows much more closely than the current ~90 px rows.

---

# 5. Compact native ToggleSwitch without replacing it

Keep the native WinUI ToggleSwitch.

Do not add a custom Toggle control or state machine.

Change the Overlay-local QAM style/resource overrides so the existing native template no longer contributes its own 10+10 vertical content margins.

Add explicit QAM resources, for example:

~~~text
QamToggleMinHeight = 22
QamTogglePreContentMargin = 1
QamTogglePostContentMargin = 1
~~~

Change QamToggleStyle:

~~~text
MinHeight 40
→
MinHeight 22
~~~

In OverlayToggleRow's existing control-local resource setup, also set:

~~~text
ToggleSwitchPreContentMargin
ToggleSwitchPostContentMargin
~~~

from the new QAM resources.

Target native Toggle content height:

~~~text
1
+ 20 DIP native rail
+ 1
=
22 DIP
~~~

Combined with OverlayRowChrome:

~~~text
10 + 22 + 10 = 42 DIP
~~~

Keep:

- native drag/click behavior;
- IsOn / Toggled semantics;
- authoritative suppression;
- Steam-blue ON rail;
- OFF colors;
- thumb colors;
- disabled behavior.

Do not replace the WinUI ToggleSwitch template in this PR.

---

# 6. Split value-button width from height

Current:

~~~text
QamValueButtonSize = 40
→ MinWidth = 40
→ MinHeight = 40
~~~

Replace this single square-size assumption with explicit geometry:

~~~text
QamValueButtonWidth  = 40
QamValueButtonHeight = 22
~~~

Keep the current 16-DIP icon size.

Update:

~~~text
QamValueButtonStyle
OverlayValueRow.CreateIconButton
OverlayTabOrderRow via shared style
~~~

so the horizontal click target stays wide enough for the compact arrows while the vertical control no longer forces a 60-DIP row.

Do not change RepeatButton timing.

Do not change Left/Right controller semantics.

---

# 7. Numeric slider rows remain two-line, but should also be less tall

Numeric slider rows are structurally different from ordinary one-line rows.

Current approximate internal geometry:

~~~text
20 label/value line
2 internal spacing
32 Slider height
----------------
54 content

+ 10 top / 10 bottom row padding
=
~74 DIP total
~~~

Do not force numeric slider rows to 42 DIP.

However the current 32-DIP Slider control height is a WinUI interaction default, while the current Steam-reference stylesheet evidence uses:

~~~text
track = 4 px
handle = 12 px
~~~

Reduce:

~~~text
QamSliderHeight
32
→
22
~~~

Keep:

~~~text
QamSliderTrackHeight = 4
QamSliderThumbWidth  = 12
QamSliderThumbHeight = 12
~~~

Resulting approximate numeric row:

~~~text
20 header
2 spacing
22 Slider
20 row vertical padding
=
~64 DIP
~~~

This keeps sliders visibly taller than ordinary rows while reducing unnecessary vertical expansion.

Do not change slider mutation/debounce behavior.

---

# 8. Remove ClawHUD detail-row spacing

Current BuildClawHudPage uses:

~~~text
QamSectionHeaderSpacing = 4
~~~

as the StackPanel spacing between every ClawHUD row.

That resource is for section-heading rhythm, not a plain row list.

Change the ClawHUD detail row stack to:

~~~text
QamRowSpacing = 0
~~~

Do not add per-row margins.

Rows should directly follow each other, with selected fill occupying the row itself rather than floating between large vertical gaps.

---

# 9. Controller M1/M2 row spacing

Current Controller page uses one StackPanel with QamSectionHeaderSpacing for:

~~~text
M1 / M2 heading
M1 row
M2 row
~~~

This means the heading spacing also becomes M1-to-M2 spacing.

Restructure only the presentation:

~~~text
section
  heading
  rowStack
    M1
    M2
~~~

Use:

~~~text
section heading → rowStack gap = QamSectionHeaderSpacing
M1 → M2 gap                 = QamRowSpacing = 0
~~~

Do not change M1/M2 state or mutation behavior.

---

# 10. Fixed page title

Current structure:

~~~text
BodyScroll
  TabBody
    page StackPanel
      page title
      page content
~~~

Therefore BringSelectedRowIntoView / normal body scrolling moves the page title away with the content.

This is why the Setting title disappears upward when navigating far down the page.

The title must become a fixed element outside BodyScroll.

Target right-column structure:

~~~text
Grid.Column = 1
├─ Row 0 Auto
│  └─ PageTitle
│
└─ Row 1 *
   └─ BodyScroll
      └─ internal padded host
         └─ TabBody
            └─ current page body
~~~

Use exactly one shared PageTitle TextBlock for all five tabs.

Do not create one title per page.

---

# 11. Preserve the PR661 row-inset fix while moving the title

PR661 fixed selected-row horizontal inset by moving horizontal content padding INSIDE the full-width BodyScroll viewport.

Do not regress that structure.

Because the title is now outside BodyScroll, split the old combined content padding into two clear presentation resources.

Recommended:

~~~text
QamPageTitleMargin
= 16,16,16,8

QamBodyContentPadding
= 16,0,16,12
~~~

This preserves the current initial visual rhythm:

~~~text
16 top inset
28 title line height
8 title-to-body gap
body begins
~~~

while keeping BodyScroll full width and retaining 16 DIP of internal horizontal padding for the negative row margin.

Remove the old QamContentPadding resource if it has no remaining consumer after the split.

Do not leave both old and new padding active.

---

# 12. PageTitle XAML

Add one:

~~~text
x:Name = PageTitle
~~~

TextBlock in the fixed header row.

Apply:

~~~text
QamPageTitleTextStyle
QamPageTitleMargin
~~~

The title text remains sourced from:

~~~text
AddonQuickSettingsShellContract.LabelFor(selectedTab)
~~~

No duplicate title strings.

---

# 13. Remove CreateQamPage wrapper

Current BuildPage creates page content and then wraps it through CreateQamPage, which inserts the title inside the scrollable body.

After the fixed PageTitle exists:

~~~text
BuildPage
→ return the page-specific content directly
~~~

Remove CreateQamPage.

Update ApplySelectedTabVisualState so the existing selected-tab authority also updates:

~~~text
PageTitle.Text = LabelFor(_tabState.SelectedTab)
~~~

Do not add another page-title state field.

The selected tab remains the only authority.

---

# 14. Fixed-title behavior

The following must hold:

~~~text
LB/RB tab change
→ selected page changes
→ fixed PageTitle text changes

row Up/Down
→ BodyScroll may move
→ PageTitle does not move

BringSelectedRowIntoView
→ affects only BodyScroll content
→ PageTitle remains visible

Show/reset
→ first configured tab selected
→ matching fixed title visible
~~~

No sticky-header framework is needed.

---

# 15. Minimal-text product policy

For the Overlay, the default visible information model is now:

~~~text
page title

optional structural section/card title

option label                  control/current value
option label                  control/current value
option label                  control/current value
~~~

Keep only text required to identify:

- current page;
- structural section/card;
- option/control;
- current value;
- shortcut tile action;
- Profile game tile.

Remove all supplemental status/help/explanation prose from the Overlay for now.

If a specific message proves necessary later, add it deliberately in a separate focused change.

---

# 16. Text that remains

Keep:

~~~text
Device / Profile / Controller / Shortcut / Setting page title

ClawHUD card title
Tab Order card title
M1 / M2 section title

Quick Settings section labels that identify a real feature group
TDP
CPU Boost
Windows Power Mode
etc.

option labels
Enable HUD
Display Mode
HUD Size
Font
Alignment
Background
Opacity
Intel VRR Range Fix
M1
M2
etc.

current values
Always
-2
Segoe UI Variable
Center
Content Width
50%
Right Bumper
etc.

Profile game titles
Shortcut tile titles
~~~

This is not a "remove all words" change.

It is a "remove all supplemental prose/status captions" change.

---

# 17. ClawHUD: remove all supplemental status/summary text

Remove visible presentation for:

~~~text
Enabled · Ready
Disabled · ...
Loading ClawHUD status...
Ready
Waiting for ClawHUD state.
VRR: AlreadyCorrect — Already using the native VRR range.
VRR result status/message
ClawHUD failure/status message
~~~

The Setting card header becomes conceptually:

~~~text
ClawHUD                              chevron
~~~

When expanded:

~~~text
Enable HUD                           toggle
Display Mode                         selector
HUD Size                             stepper
Font                                 selector
Alignment                            selector
Background                           selector
Opacity                              stepper
Intel VRR Range Fix                  toggle
~~~

Remove from the presentation class where no longer needed:

~~~text
SettingCardView.Summary
_clawHudStatusText
_clawHudVrrStatusText
UpdateClawHudCardSummary
UpdateTabOrderCardSummary
CreateStatusText if it has no remaining consumer
~~~

Keep FrontendClawHudSnapshot.StatusMessage and IntelVrrLastResult in contracts/state.

Do not change transport or Runtime data merely because Overlay no longer renders it.

ApplyClawHudFailure must still settle mutation-in-flight state and restore correct control enablement.

---

# 18. Tab Order: remove summary and instruction text

Remove visible:

~~~text
Device › Profile › Controller › Shortcut › Setting
Use Left and Right to move the selected tab.
~~~

Collapsed card:

~~~text
Tab Order                            chevron
~~~

Expanded:

~~~text
Device                               arrows
Profile                              arrows
Controller                           arrows
Shortcut                             arrows
Setting                              arrows
~~~

Controller Left/Right behavior remains unchanged.

Do not remove accessibility names/tooltips from the move buttons.

---

# 19. Controller: remove explanatory/status caption

Remove visible:

~~~text
Xbox 360 mode only. Steam Game / Big Picture keeps M1 as R4 and M2 as L4.
Updating M1 / M2 mapping...
M1 / M2 mapping is unavailable.
M1 / M2 mapping update failed.
~~~

Keep:

~~~text
Controller page title
M1 / M2 section heading
M1 row
M2 row
~~~

Remove presentation-only status TextBlock/state if no longer needed.

Keep:

- availability state;
- mutation-in-flight gating;
- authoritative readback;
- failure result processing;
- selection preservation.

Do not change target options.

---

# 20. Generic Device/Profile Quick Settings: do not render messages

Remove visible rendering for:

~~~text
page.Message
section.Message
LastLocalFailureMessage banner
"Quick Settings are unavailable."
loading/unavailable message rows
~~~

Keep real section labels and control rows.

Update QuickSettingsSurface so it no longer needs presentation-only:

~~~text
FailureText
UnavailableText
~~~

BuildQuickSettingsPage should return the content host directly without a failure-banner TextBlock.

When a page is unavailable:

~~~text
clear its rendered controls
RenderedAvailable = false
page body may be empty under the fixed page title
~~~

Do not change the binding's authoritative state or LastLocalFailureMessage storage.

The binder can retain that diagnostic/product fact even though this Overlay version does not render it.

---

# 21. Quick Settings shape comparison must ignore section.Message

Current HasSameShape compares:

~~~text
previous.Message
current.Message
~~~

After section.Message is no longer rendered, a message-only change is no longer a visual shape change.

Remove section.Message from the renderer shape comparison.

Required behavior:

~~~text
same SectionId
same Label
same row shapes
different Message only
→ reuse existing controls
→ no section rebuild
~~~

This reduces unnecessary UI churn and matches the new presentation contract.

Do not remove section.Message from shared contracts.

---

# 22. Profile catalog: remove status line

Remove the Profile catalog status TextBlock and visible text for:

~~~text
Loading games...
No games found.
catalog error text
~~~

Keep:

- Profile fixed page title;
- game catalog cards;
- favorite-first ordering;
- 3-column layout;
- selection;
- A activation;
- pointer click;
- active/selected detail transitions.

ShowProfileCatalog should no longer need a display-status string parameter.

Profile errors may continue through existing logs/diagnostics.

Do not modify Profile transport contracts.

---

# 23. Shortcut: remove status/banner/subtitle text

Remove visible:

~~~text
Loading shortcuts...
Shortcut settings are unavailable.
No shortcuts configured. Add shortcuts in the Main App.
Shortcut could not be executed.
tile.StatusText subtitle
~~~

Keep:

- fixed Shortcut page title;
- tile title;
- tile Enabled opacity;
- 2-column layout;
- selection;
- A/pointer execution;
- execution-in-flight gating.

The page may contain only tile titles, or be empty when no tiles are available.

Do not modify FrontendShortcutTile.StatusText or failure fields in shared contracts.

They may be reused later by another surface or a future selectively reintroduced Overlay message.

---

# 24. Preserve diagnostics/state even when UI text is removed

This PR removes visual presentation, not underlying state.

Do not remove:

~~~text
FrontendClawHudSnapshot.StatusMessage
IntelVrrLastResult
QuickSettingsPageSnapshot.Message
QuickSettingsSection.Message
OverlayQuickSettingsPageBinding.LastLocalFailureMessage
OverlayBackButtonMappingState.FailureMessage
FrontendShortcutDashboardSnapshot.FailureMessage
FrontendShortcutTile.StatusText
OverlayProfileCatalogState.Error
~~~

Do not bump protocol versions.

Do not remove serializer fields.

Do not alter Runtime projection.

Keep existing OverlayLog calls.

Where a presentation-only field can be removed safely, remove only that WinUI field.

Do not build a new notification/toast framework in this PR.

---

# 25. QamCaptionTextStyle

Do not delete QamCaptionTextStyle merely because this PR removes current supplemental captions.

Keep the resource available for a future deliberately selected message.

The policy is:

~~~text
caption style exists
but no generic "always render every status string" rule exists
~~~

Do not add an alternative generic message component.

---

# 26. Section spacing remains distinct from row spacing

Keep:

~~~text
QamRowSpacing = 0
QamSectionSpacing = 24
QamSectionHeaderSpacing = 4
~~~

Use them correctly:

~~~text
ordinary row ↔ ordinary row
→ QamRowSpacing = 0

section/card ↔ next section/card
→ QamSectionSpacing = 24

structural section heading ↔ its row stack
→ QamSectionHeaderSpacing = 4
~~~

Do not globally set every spacing resource to zero.

The target is compact rows, not removal of visual hierarchy.

---

# 27. Width / rail / font are frozen

Keep the PR661 geometry:

~~~text
total Overlay width = 432 DIP
left rail           = 52 DIP
right content       = 380 DIP
floating gap        = 4 DIP
~~~

Do not change window geometry in this PR.

Keep:

~~~text
QamFontFamily = Segoe UI Variable
~~~

The product decision is to use Segoe UI Variable and stop pursuing Steam Motiva font integration.

Do not add font logging, DirectWrite resolution, font extraction, or private Steam font loading.

---

# 28. Expected production files

Likely:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml

src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayValueRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayNumericSliderRow.cs

src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shortcuts.cs
~~~

OverlayTabOrderRow.cs may change only if needed for the new width/height resource names.

No Runtime / Contracts / FrontendTransport production change should be required.

---

# 29. Tests — compact ordinary controls

Update/add tests proving:

~~~text
QamRowPadding remains 16,10,16,10
QamRowMinHeight remains 42
QamRowSpacing remains 0

QamToggleStyle MinHeight = 22
OverlayToggleRow locally supplies
  ToggleSwitchPreContentMargin = 1
  ToggleSwitchPostContentMargin = 1

QamValueButtonWidth = 40
QamValueButtonHeight = 22
OverlayValueRow uses width and height separately

QamSliderHeight = 22
track remains 4
thumb remains 12 x 12
~~~

Do not test actual WinUI rendered pixels with golden images.

---

# 30. Tests — fixed page title

Update OverlayQamVisualResourcesTests so it proves:

~~~text
exactly one PageTitle exists in OverlayWindow.xaml
PageTitle uses QamPageTitleTextStyle
PageTitle is NOT a descendant of BodyScroll
TabBody IS a descendant of BodyScroll

right content column has:
  fixed title row
  scrolling body row

BodyScroll remains the only ScrollViewer
vertical scrollbar remains hidden
horizontal scrollbar remains disabled

BodyScroll still owns an internal left/right 16-DIP padded host
selected-row negative margins remain inside the scroll viewport
~~~

Update Shell source tests:

~~~text
BuildPage returns page-specific body directly
CreateQamPage no longer exists
ApplySelectedTabVisualState sets PageTitle.Text from LabelFor(selected)
~~~

---

# 31. Tests — minimal-text policy

Add focused source assertions that supplemental presentation strings no longer exist in Overlay production sources.

At minimum cover:

~~~text
Waiting for ClawHUD state.
Enabled · Ready
VRR:
Already using the native VRR range

Xbox 360 mode only.
Steam Game / Big Picture keeps M1 as R4 and M2 as L4.
Updating M1 / M2 mapping...
M1 / M2 mapping is unavailable.

Use Left and Right to move the selected tab.

Loading games...
No games found.

No shortcuts configured.
Shortcut settings are unavailable.
Shortcut could not be executed.
~~~

Do not assert that these words disappear from shared Contracts or Runtime.

The test scope is Overlay visual production sources.

---

# 32. Tests — Quick Settings message removal

Update renderer tests proving:

~~~text
QuickSettingsSurface has no FailureText
QuickSettingsSurface has no UnavailableText
BuildQuickSettingsPage does not create a failure banner
BuildQuickSettingsSection does not render section.Message
unavailable page clears controls without creating an unavailable TextBlock
~~~

Add a pure shape regression:

~~~text
same section/rows/label
only Message differs
→ OverlayQuickSettingsSectionRendering.HasSameShape returns true
~~~

Keep all binding tests for LastLocalFailureMessage.

The state still exists; only its visual presentation is removed.

---

# 33. Tests — ClawHUD / Controller / Shortcut / Profile behavior remains

Preserve all behavioral tests for:

~~~text
ClawHUD mutation-in-flight gating
ClawHUD authoritative readback
VRR setting value
M1/M2 mutation and selection preservation
Tab Order movement
Profile catalog navigation
Profile selected/active detail transitions
Shortcut selection/execution
Shortcut execution-in-flight gating
~~~

Update source-shape tests that previously required visible captions/status fields.

Do not weaken behavior tests.

---

# 34. Manual hardware acceptance

Reference:

~~~text
MSI Claw
1920 × 1200
150%
~~~

Validate Setting first.

Expected:

~~~text
Setting title stays visible at top while navigating to the bottom

ClawHUD header
→ one-line title + chevron only

expanded ClawHUD
→ no Ready/status/helper/VRR-result lines

ordinary Toggle/value rows
→ roughly 42 DIP
→ roughly 63 physical px at 150%
→ selected fill occupies the row
→ no large empty vertical bands between rows

Display Mode
HUD Size
Font
Alignment
Background
Opacity
VRR
→ form a compact continuous list

Tab Order
→ no current-order summary
→ no usage instruction
~~~

Controller:

~~~text
Controller fixed title
M1 / M2 heading
compact M1
compact M2
no explanatory caption
~~~

Device/Profile:

~~~text
fixed page title
section labels remain
section.Message does not render
ordinary rows compact
numeric sliders remain taller than ordinary rows but visibly reduced
~~~

Shortcut/Profile catalog:

~~~text
no loading/status/subtitle prose
only actionable tile/game titles
~~~

---

# 35. Interaction acceptance

Verify unchanged:

~~~text
LB/RB switches tabs
Up/Down changes row selection
Left/Right adjusts selected values
A toggles/activates
B collapses Setting card / backs out / dismisses as before

pointer Toggle works
pointer Slider works
pointer arrow buttons work
Profile card pointer works
Shortcut pointer works

BringSelectedRowIntoView scrolls only body
PageTitle remains fixed
~~~

---

# 36. Explicit non-goals

Not in this PR:

- changing 432-DIP width;
- changing 52-DIP rail;
- changing rail item height;
- changing font family;
- Motiva integration;
- new message severity system;
- selective error captions;
- toast/notification UI;
- protocol changes;
- removing status/error fields from shared contracts;
- Runtime projection changes;
- mutation changes;
- controller navigation changes;
- custom Toggle control;
- custom Slider control;
- Profile column-count changes;
- Shortcut column-count changes;
- generic page/view-model architecture.

---

# 37. Overengineering guardrail

Desired implementation shape:

~~~text
existing XAML shell
→ fixed one-line PageTitle
→ existing BodyScroll below it

existing QAM resources
→ smaller intrinsic control heights

existing concrete row controls
→ no new row framework

existing page builders
→ stop creating supplemental caption/status TextBlocks
~~~

Do not add:

- FixedHeaderManager;
- CompactRowManager;
- MessageVisibilityPolicy service;
- caption registry;
- new page host class;
- new view model layer;
- custom control framework.

The UI policy is simple enough to express directly in the existing files.

---

# 38. Definition of done

The PR is complete when:

~~~text
ordinary Toggle/value rows are ~42 DIP instead of ~60 DIP
numeric Slider rows are reduced without becoming one-line controls
row-to-row spacing is 0
section hierarchy spacing remains intentional

PageTitle is fixed outside BodyScroll
PageTitle never scrolls away
BodyScroll remains the only scrolling body
PR661 internal horizontal inset / negative-row-margin fix remains intact

Overlay shows no supplemental status/help/explanation prose by default
only page/section/card titles, option labels, values, and actionable tile titles remain

all feature state, error data, contracts, transport, mutation behavior, and lifecycle remain unchanged

432 DIP total width remains
52 DIP rail remains
Segoe UI Variable remains

all relevant UI tests pass
normal Build and Test passes
~~~

After this PR, any explanatory/status message should be reintroduced only as a deliberate, separately justified Overlay UX decision.
