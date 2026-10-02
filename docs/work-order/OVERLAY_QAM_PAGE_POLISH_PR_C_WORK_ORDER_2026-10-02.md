# Work Order — Overlay QAM Page Polish PR C

**Date:** 2026-10-02  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**Target branch:** main  
**Reviewed main baseline:** 11d19fe8df87d7ff5946fca76ee28cf2d26f51d9  
**Scope:** standalone WinUI 3 Overlay page hierarchy and remaining native Button chrome  
**Implementation shape:** one focused PR

---

# 0. Goal

PR A established the QAM palette, typography, row/section chrome, and rail structure.

PR B established QAM-like Toggle, numeric Slider, and discrete value-control visuals.

The remaining code-visible mismatches are now page-level:

~~~text
1. Steam QAM page-title typography was measured and added to resources,
   but no current Overlay page renders a page title.

2. Several larger product-owned Button surfaces still use the default WinUI Button template:
   - rail buttons
   - Profile catalog cards
   - Setting expandable headers

3. Page roots do not yet share one title/content rhythm.
~~~

PR C must finish those code-side visual gaps without changing behavior.

Target:

~~~text
[canonical page title]

existing page content
~~~

for:

~~~text
Device
Profile
Controller
Shortcut
Setting
~~~

Do not add a generic Quick Settings title.

---

# 1. Mandatory project review

Read current versions of:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/work-order/OVERLAY_QAM_SHELL_FOUNDATION_PR1_WORK_ORDER_2026-10-02.md
docs/work-order/OVERLAY_CONTROLLER_M1_M2_MAPPING_PR1_WORK_ORDER_2026-10-02.md
docs/work-order/OVERLAY_QAM_VISUAL_FOUNDATION_PR_A_WORK_ORDER_2026-10-02.md
docs/work-order/OVERLAY_QAM_CONTROLS_PR_B_WORK_ORDER_2026-10-02.md

docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
~~~

This PR is presentation-only.

Do not modify:

- Full1902 ownership or lifecycle;
- PID1901 / PID1902;
- HidHide;
- VIIPER;
- DirectInput;
- Xbox360 / SteamDeck presentation;
- Overlay capture/release;
- transport/protocols;
- Quick Settings contracts;
- M1/M2 contracts;
- ClawHUD contracts;
- Shortcut execution;
- tab-order persistence;
- show/hide/no-activate behavior.

---

# 2. Mandatory source review

Inspect current main:

~~~text
src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsShellContracts.cs

src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml
src/SteamInputAddonforClaw.Overlay/OverlayQamResources.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shortcuts.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs
src/SteamInputAddonforClaw.Overlay/OverlayRowChrome.cs
src/SteamInputAddonforClaw.Overlay/OverlayTabOrderRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayValueRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayNumericSliderRow.cs
~~~

Inspect relevant UI tests before changing source-shaped contracts.

---

# 3. Current facts

## 3.1 Measured QAM page title is currently unused

The current QAM visual reference measured the Steam Performance page title as:

~~~text
22px
weight 700
line-height 28px
white
Motiva Sans family with platform fallback
~~~

PR A created:

~~~text
QamSectionHeaderTextStyle
~~~

with the measured 22 / Bold / 28 / white values.

Current source uses no page title and does not consume that measured style.

This PR must make the measured title hierarchy visible.

## 3.2 Canonical product labels already exist

Use:

~~~text
AddonQuickSettingsShellContract.LabelFor(id)
~~~

Current labels are:

~~~text
Device
Profile
Controller
Shortcut
Setting
~~~

Do not duplicate these strings in another table.

## 3.3 Remaining default Button chrome

PR B already gives explicit QAM chrome to value and Tab Order buttons.

Remaining larger Buttons are:

~~~text
rail tab Button
Profile catalog card Button
Setting expandable-header Button
~~~

Their colors are partly QAM-owned, but the native WinUI Button template can still supply Windows hover/pressed/focus chrome.

Remove that remaining Windows visual dependency.

---

# 4. QAM evidence boundary

Measured facts available to this PR:

~~~text
surface #0E141B
page title 22 / 700 / 28 / white
focused ordinary row rgba(255,255,255,0.15)
row radius 2
rail selected fill #23262E
rail icon #8B929A
content top padding 16
section bottom spacing 24
~~~

Still provisional:

~~~text
page-title-to-content gap
Profile tile visual
Shortcut tile visual
hover/pressed colors
exact live Toggle/Slider geometry
~~~

Do not invent new measured values.

---

# 5. Page title style

Keep the measured style and add a semantic alias:

~~~xml
<Style x:Key="QamPageTitleTextStyle"
       TargetType="TextBlock"
       BasedOn="{StaticResource QamSectionHeaderTextStyle}" />
~~~

Do not repeat font-size, weight, line-height, or color setters.

Add one centralized metric:

~~~text
QamPageContentSpacing
~~~

The exact title-to-content gap is not measured, so mark this value provisional in the reference document.

Do not add page-specific margins.

---

# 6. Shared page wrapper

Add one tiny helper on the existing OverlayWindow partial, preferably OverlayWindow.Shell.cs:

~~~text
CreateQamPage(tabId, content)
~~~

Conceptual output:

~~~text
StackPanel
    Spacing = QamPageContentSpacing
    page-title TextBlock
        Text = LabelFor(tabId)
        Style = QamPageTitleTextStyle
    existing page content
~~~

Do not create:

- PageViewModel;
- OverlayPageBase;
- QamPageControl;
- page registry;
- another UserControl layer.

One small presentation helper is sufficient.

---

# 7. Wrap all five pages exactly once

BuildPage must continue choosing the same current builders, then wrap the chosen content once.

Required result:

~~~text
Device
  title: Device
  existing generic Device Quick Settings

Profile
  title: Profile
  existing Catalog / SelectedDetail / ActiveDetail content

Controller
  title: Controller
  existing M1 / M2 section

Shortcut
  title: Shortcut
  existing shortcut grid

Setting
  title: Setting
  existing ClawHUD / Tab Order expanders
~~~

Do not put duplicate titles inside individual page builders.

This is especially important for Profile, whose catalog/detail state changes inside one page.

---

# 8. Scroll architecture remains unchanged

The page title must remain inside the existing BodyScroll content.

Do not add:

- fixed/sticky title;
- second ScrollViewer;
- nested ScrollViewer;
- separate page-header host.

Current one-ScrollViewer architecture remains authoritative.

---

# 9. Add one QAM flat Button style

Add an explicitly keyed:

~~~text
QamFlatButtonStyle
~~~

to QamOverlayResources.xaml.

Purpose:

> Preserve native Button click/accessibility semantics while removing default Windows Button chrome from product-owned large surfaces.

Use a small explicit ControlTemplate with:

~~~text
Normal
PointerOver
Pressed
Disabled
~~~

and a Border + ContentPresenter.

Use only existing QAM resources:

~~~text
QamSectionBrush
QamHoverFillBrush
QamPressedFillBrush
QamDisabledTextBrush
QamFocusBorderBrush
~~~

Do not add new hard-coded hover/pressed colors.

Set:

~~~text
IsTabStop = false
UseSystemFocusVisuals = false
~~~

because controller selection remains owned by OverlayRowSelection / page-specific selection models.

Do not add animation.

---

# 10. Keep QamValueButtonStyle separate

PR B already owns QamValueButtonStyle for:

~~~text
OverlayValueRow previous/next
ClawHUD numeric stepper buttons
Tab Order move buttons
~~~

Do not replace it.

QamFlatButtonStyle is for larger surfaces:

~~~text
rail
Profile catalog cards
Setting expandable headers
~~~

No mode-switching mega-template.

---

# 11. Rail Button

Make QamRailButtonStyle use the flat QAM Button template, preferably by BasedOn QamFlatButtonStyle.

Keep rail-specific:

~~~text
52 DIP structural width
64 DIP item height
24 DIP icon
QamRailIconBrush
QamRailIconSelectedBrush
QamRailSelectedFillBrush
~~~

Selected state remains owned by:

~~~text
ApplySelectedHeaderVisual()
~~~

Do not add a second selected-tab state machine.

LB/RB remains the controller tab-navigation authority.

The exact Steam rail hover state is not measured; keep hover/pressed within the existing neutral QAM palette and never show Windows accent blue.

---

# 12. Profile catalog card Button

Apply QAM flat/tile Button chrome to Profile catalog cards.

A tiny:

~~~text
QamTileButtonStyle
~~~

based on QamFlatButtonStyle is acceptable if it only centralizes:

~~~text
QamTilePadding
QamTileMinHeight
QamTileCornerRadius
QamTileBrush
left content alignment
~~~

Keep unchanged:

~~~text
3 columns
Favorite-first order
two-line title wrapping
CharacterEllipsis
controller 2D navigation
BringIntoView
A activation
pointer click
~~~

Do not create a ProfileTile control.

Selection remains owned only by OverlayProfileCatalogSelection.SelectedIndex.

The Button style must respect the Background assigned by ApplyProfileCatalogSelectionVisual().

---

# 13. Setting expandable header Button

Apply QamFlatButtonStyle to the existing Setting card header Button.

Keep:

~~~text
title
summary
chevron
full-width pointer target
Click -> ToggleSettingCard
AutomationProperties.Name
outer OverlayRowChrome selection
~~~

The inner Button is not the controller selection owner.

Do not change:

~~~text
_expandedSettingCard
BuildSettingRows
detail row availability
B-to-collapse behavior
ClawHUD state
Tab Order state
~~~

This is chrome only.

---

# 14. Shortcut boundary

Shortcut tiles are Borders with Tapped handling, not native Buttons.

Do not redesign them in PR C.

Keep:

~~~text
max 2 columns
dynamic tile count
2D controller selection
A execution
pointer/touch execution
disabled opacity
QamTileBrush / QamTileSelectedBrush
~~~

Only the shared Shortcut page title is new.

---

# 15. Controller boundary

Keep current hierarchy:

~~~text
Controller          page title
M1 / M2             section heading
caption
M1
M2
~~~

Do not remove the M1 / M2 section heading.

Do not change mutation, selection preservation, or authoritative settlement.

---

# 16. Device / Profile / Setting boundaries

Device:

~~~text
Device page title
existing generic sections
~~~

Profile:

~~~text
Profile page title
existing catalog or detail
~~~

Do not duplicate game name outside the existing Profile product section.

Setting:

~~~text
Setting page title
existing ClawHUD and Tab Order rows
~~~

Do not rename Setting to Settings in this PR; use the canonical contract label.

---

# 17. Geometry stays frozen

Do not change:

~~~text
MaxSurfaceWidthDip = 416
OpaquePanel MaxWidth = 416
rail width = 52
outer floating gap
work-area/taskbar logic
DPI logic
~~~

Potential 400 / 416 / 432 comparison remains a hardware-informed future adjustment.

---

# 18. Profile and Shortcut density stays frozen

Do not change:

~~~text
Profile catalog = 3 columns
Shortcut = maximum 2 columns
~~~

At 416 DIP these may later need hardware tuning, but changing the column model now would alter controller navigation without actual device evidence.

Do not modify OverlayProfileCatalogSelection for this PR.

---

# 19. PR B control values stay frozen

Do not retune:

~~~text
Toggle geometry
Slider geometry
Slider handle
Slider colors
discrete field width
stepper button timing
~~~

PR B documented that live current-QAM controls were unavailable for exact computed-style measurement.

Do not replace one provisional guess with another in PR C.

---

# 20. Update visual reference

Update:

~~~text
docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md
~~~

Add a PR C note:

~~~text
Measured page-title typography is now consumed by all five Addon pages.
Page-title-to-content spacing remains provisional.
QamFlatButtonStyle removes native WinUI chrome from rail/Profile/Setting large Buttons.
Profile and Shortcut tile layouts remain Overlay-specific and are not claimed to be Steam-native equivalents.
Profile 3-column and Shortcut 2-column density remains deferred to hardware acceptance.
~~~

Do not invent new measurements.

---

# 21. Expected files

Primary:

~~~text
docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md

src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs

tests/SteamInputAddonforClaw.UiTests/OverlayQamVisualResourcesTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayControllerRendererTests.cs
~~~

No Runtime, Contracts, or FrontendTransport production file should need modification.

---

# 22. Tests — page title

Prove:

~~~text
QamPageTitleTextStyle exists
it is based on QamSectionHeaderTextStyle
QamPageContentSpacing exists
CreateQamPage exists
CreateQamPage uses LabelFor(id)
CreateQamPage applies QamPageTitleTextStyle
BuildPage wraps the chosen page exactly once
~~~

Also prove no generic Quick Settings title is reintroduced.

Do not hard-code five duplicate page-title strings in Overlay code.

---

# 23. Tests — flat Button chrome

Prove:

~~~text
QamFlatButtonStyle exists
it owns an explicit ControlTemplate
it does not consume Windows accent resources
UseSystemFocusVisuals is false
IsTabStop is false

QamRailButtonStyle uses the flat template
Profile catalog Button uses QAM flat/tile style
Setting header Button uses QamFlatButtonStyle

QamValueButtonStyle remains separate and still used by ValueRow/TabOrder
~~~

---

# 24. Behavior regression

All existing behavior tests remain green:

~~~text
LB/RB tab switching
row selection
Device Quick Settings
Profile Quick Settings
Profile catalog selection
Profile mode transitions
M1/M2 selection preservation
Shortcut selection/execution
Setting expand/collapse
Tab Order reorder
ClawHUD
numeric delayed commit
linked TDP constraints
Overlay lifecycle
~~~

Do not weaken behavioral assertions because a visual wrapper was added.

---

# 25. Frozen layout regression

Preserve/assert:

~~~text
MaxSurfaceWidthDip = 416
OpaquePanel MaxWidth = 416
rail Width = 52
Profile catalog has 3 star columns
OverlayProfileCatalogSelection uses 3 columns
Shortcut maximum columns = 2
one BodyScroll
vertical scrollbar hidden
no bottom footer
~~~

No screenshot/golden-image test is required.

---

# 26. Manual acceptance later

When MSI Claw is available at 1920x1200 / 150%:

~~~text
each tab has exactly one page title
title visually resembles Steam QAM 22px title
no generic Quick Settings heading
title spacing is not excessive

rail pointer states no longer look like Windows
Profile card pointer states no longer look like Windows
Setting header pointer states no longer look like Windows

selected logical row remains visually dominant
Profile 3-column game names remain readable
Shortcut 2-column tiles remain readable
single-scroll behavior remains natural
~~~

Any panel-width or Profile-column adjustment becomes a later hardware-informed PR.

---

# 27. Explicit non-goals

Not in PR C:

- panel width change;
- Profile 3-to-2-column conversion;
- Shortcut layout redesign;
- Toggle/Slider retuning;
- Runtime changes;
- transport/protocol changes;
- M1/M2 product changes;
- ClawHUD behavior changes;
- tab-order behavior changes;
- generic Quick Settings contract changes;
- font extraction;
- CSS injection;
- Decky dependency;
- WebView/CEF;
- sticky header;
- nested scroll;
- page/view-model architecture;
- animation system;
- localization overhaul.

---

# 28. Overengineering guardrails

Desired shape:

~~~text
QamOverlayResources.xaml
    + QamPageTitleTextStyle
    + QamPageContentSpacing
    + QamFlatButtonStyle
    + optional tiny QamTileButtonStyle

OverlayWindow.Shell
    + CreateQamPage(...)

Profile / Setting
    + explicit QAM Button style
~~~

Do not add:

- QamPageManager;
- OverlayPageBase;
- PageViewModel;
- QamButtonControl;
- StyleService;
- navigation wrapper.

The existing ResourceDictionary remains the visual authority.

---

# 29. Definition of done

Complete when:

~~~text
all five current tabs
→ canonical QAM-style page title

rail / Profile catalog / Setting header large Buttons
→ no default Windows Button chrome

Device/Profile/Controller/Shortcut/Setting behavior
→ unchanged

416 DIP / 52 DIP / Profile 3-column / Shortcut 2-column
→ unchanged
~~~

After PR C, further visual work must be driven by actual MSI Claw screenshots or live QAM control measurements rather than another speculative architecture pass.
