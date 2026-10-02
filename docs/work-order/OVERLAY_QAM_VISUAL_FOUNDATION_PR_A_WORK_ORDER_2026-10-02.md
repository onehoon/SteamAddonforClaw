# Work Order — Overlay QAM Visual Foundation PR A

**Date:** 2026-10-02  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**Target branch:** main  
**Reviewed main baseline:** d379308ae751a59229937b57a966ba155cb3e980  
**Feature area:** Standalone WinUI 3 Overlay visual system  
**Implementation shape:** one focused PR  
**Primary goal:** make every visual element in scope look as close as practical to the current Steam Quick Access Menu while preserving the existing standalone Full1902 architecture and all input/runtime behavior.

---

# 0. Product goal

The Addon Overlay is now structurally QAM-like:

~~~text
right-side standalone Overlay
+
left vertical tab rail
+
right content pane
~~~

PR A establishes the shared visual foundation so the Overlay stops looking like a light Windows Settings surface and starts looking like the current Steam QAM.

The target is not:

~~~text
"Steam inspired"
"dark mode"
"roughly handheld-like"
~~~

The target is:

> Reproduce the current Steam QAM visual language as faithfully as WinUI 3 reasonably allows, using measured current-QAM values wherever those values can be observed.

PR A covers the common visual foundation only:

~~~text
surface / rail palette
content palette
typography
spacing tokens
section chrome
ordinary row chrome
tab chrome
selection / focus chrome
secondary / disabled text
Profile / Shortcut base surfaces
shared resource ownership
~~~

PR B will handle the controls whose default WinUI visual tree is materially different from Steam:

~~~text
ToggleSwitch template
numeric slider visual
discrete value control visual
arrow/value button chrome
other control-specific interaction states
~~~

Do not move PR B control-template work into PR A unless a tiny rail-only style is required to remove an obvious Windows button artifact.

---

# 1. Mandatory project authority review

Read the latest versions before coding:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/work-order/OVERLAY_QAM_SHELL_FOUNDATION_PR1_WORK_ORDER_2026-10-02.md
docs/work-order/OVERLAY_CONTROLLER_M1_M2_MAPPING_PR1_WORK_ORDER_2026-10-02.md

docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
~~~

This PR is presentation-only.

Do not modify:

- Full1902 authority;
- PID1901 / PID1902 handling;
- DirectInput ownership;
- HidHide;
- VIIPER;
- Xbox360 / SteamDeck presentation policy;
- Overlay capture / neutral publication;
- release-to-resume;
- named-pipe protocol;
- M1/M2 mapping transport;
- Device/Profile mutation contracts;
- Shortcut execution;
- ClawHUD mutation;
- tab-order authority;
- controller navigation semantics;
- show/hide lifetime;
- no-activate/topmost policy.

---

# 2. Mandatory source review

Read the latest versions of at least:

~~~text
src/SteamInputAddonforClaw.Overlay/App.xaml
src/SteamInputAddonforClaw.Overlay/SteamInputAddonforClaw.Overlay.csproj

src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs

src/SteamInputAddonforClaw.Overlay/OverlayRowChrome.cs
src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayValueRow.cs

src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shortcuts.cs

tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayControllerRendererTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayRowSelectionTests.cs
~~~

Do not perform unrelated page refactors.

---

# 3. External reference hierarchy

Use the following reference order.

## 3.1 Primary reference — actual current Steam QAM

The source of truth for visual values is the current local Steam client Quick Access target:

~~~text
QuickAccess_uid<number>
~~~

The user has already verified:

~~~text
Steam QAM is actually open
QuickAccess_uid17 is the QAM target in that observed session
QAM internal structure = left vertical rail + right content
observed total width ≈ 490 physical px
observed structure ≈ 48 CSS px rail + 300 CSS px content
Motiva Sans is preferred by Steam GamepadUI CSS
Steam CEF receives Motiva through clientui.uifont/custom_fonts
~~~

The exact QuickAccess computed styles, state colors, and rendered Korean fallback still require direct measurement.

Do not treat screenshots or old Steam Deck screenshots as more authoritative than the current local QuickAccess target.

## 3.2 Decky reference — native Steam UI reuse model

Current Decky Loader uses Steam-facing primitives such as:

~~~text
PanelSection
PanelSectionRow
QuickAccessTab
quickAccessMenuClasses
~~~

Reference:

~~~text
https://github.com/SteamDeckHomebrew/decky-loader/blob/main/frontend/src/plugin-loader.tsx
https://github.com/SteamDeckHomebrew/decky-frontend-lib
~~~

This is evidence that Decky gets native QAM appearance primarily by reusing Steam React/UI primitives.

Do not add Decky as a dependency.

## 3.3 CSS Loader reference — measurement and selector model

CSS Loader targets Steam CEF views such as:

~~~text
QuickAccess_uid<number>
QuickAccess
bigpictureoverlay
~~~

References:

~~~text
https://github.com/DeckThemes/DeckThemes-Docs/blob/mkdocs/docs/CSSLoader/theming_step_by_step.md
https://github.com/DeckThemes/SDH-CssLoader/blob/main/css_inject.py
~~~

CSS Loader is useful as a reference for locating and measuring the actual Steam view.

Do not add CSS injection to the Addon.

## 3.4 WinUI implementation reference

Use ordinary WinUI resource/style/template mechanisms.

References:

~~~text
https://learn.microsoft.com/windows/apps/winui/winui3/xaml-templated-controls-winui-3
https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.control.template
~~~

PR A should prefer resource dictionaries and keyed styles.

Do not create a second rendering technology.

---

# 4. Technology decision — frozen for PR A

Use:

~~~text
WinUI 3 XAML
+
ResourceDictionary
+
keyed Styles
+
existing programmatic row/page builders
~~~

Do NOT use:

- CSS at runtime;
- CSS Loader;
- Decky Loader;
- React;
- WebView2;
- CEF;
- JS bridge;
- Steam DOM injection;
- GamepadUI patching;
- Steam class-name dependency;
- npm;
- HTML;
- a custom renderer.

Reason:

~~~text
Steam/Decky:
Steam CEF/React already exists
→ reuse native React primitives / inject CSS

Addon:
standalone WinUI process already exists
→ reproduce measured visual rules in WinUI
~~~

Adding a browser/React layer only for appearance would add an unnecessary renderer, bridge, focus layer, and Steam-update dependency.

---

# 5. Reference-capture gate

Maximum visual parity requires measured current-QAM values.

Before freezing PR A visual constants, capture the following from the current QuickAccess target with CEF DevTools.

Record both the selector/context and the final computed value.

## 5.1 Shell

Capture:

~~~text
QAM root/background color
rail background color
content background color
rail/content separator if present
content left/right padding
content top/bottom padding
rail width
rail item height
rail icon size
rail item spacing
rail selected background
rail selected icon color
rail unselected icon color
rail disabled icon color if observable
rail selected indicator shape, width, radius, and placement
~~~

## 5.2 Typography

For each role capture:

~~~text
font-family
actual rendered font when DevTools exposes it
font-size
font-weight
line-height
letter-spacing
color
opacity
~~~

Roles:

~~~text
section heading
ordinary row label
ordinary row value
secondary/caption text
disabled text
status/error text
shortcut/tile title if QAM exposes an equivalent
~~~

For Korean, inspect an actual rendered Korean QAM label if possible.

Do not infer the Korean fallback merely from the CSS family list.

## 5.3 Row and section chrome

Capture:

~~~text
ordinary row min/actual height
row horizontal padding
row vertical padding
row radius
row selected/focused fill
row selected/focused outline
row selected/focused accent
row hover fill if present
row disabled opacity
section spacing
section header spacing
section background
section radius
section divider color/thickness if present
distance between section header and first row
distance between rows
~~~

## 5.4 State colors

Capture:

~~~text
primary text
secondary text
disabled text
accent/focus
selected fill
hover fill
pressed fill if observable
separator
error/warning if observable
tile/card surface if QAM uses one
~~~

## 5.5 Evidence document

Add or update:

~~~text
docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md
~~~

The document must include:

~~~text
Steam build identifier
capture date
CEF target name
viewport
devicePixelRatio
display resolution/scaling if known
measurement method
computed values
rendered-font observation
which values are exact
which values remain inferred
~~~

This document becomes the visual evidence source for PR A and later PR B.

Do not write an invented value as measured.

---

# 6. If current QAM cannot be inspected

Do not silently guess exact Steam values from old screenshots.

Allowed fallback:

1. build the resource/style structure;
2. use only values already supported by the current observed QAM evidence;
3. mark unresolved values explicitly in the reference document;
4. use a restrained provisional value only where the UI cannot compile/render without one;
5. keep each provisional value centralized so it can be replaced without page rewrites.

Do not scatter screenshot-sampled hex values through C#.

The implementation should remain reviewable even if one or two final color/font measurements need hardware follow-up.

---

# 7. Add one QAM resource dictionary

Add:

~~~text
src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml
~~~

Merge it from App.xaml after XamlControlsResources.

Conceptually:

~~~xml
<ResourceDictionary.MergedDictionaries>
    <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
    <ResourceDictionary Source="Themes/QamOverlayResources.xaml" />
</ResourceDictionary.MergedDictionaries>
~~~

This file is the single visual source of truth for the standalone Overlay.

Do not add a ThemeManager class.

Do not add runtime theme switching.

The Overlay has one product theme: QAM-like dark UI.

---

# 8. Resource ownership

At minimum define named resources for the roles below.

Exact values come from the reference-capture gate.

## 8.1 Brushes

Use clear names such as:

~~~text
QamSurfaceBrush
QamRailBrush
QamContentBrush

QamPrimaryTextBrush
QamSecondaryTextBrush
QamDisabledTextBrush
QamErrorTextBrush

QamAccentBrush
QamSelectedFillBrush
QamHoverFillBrush
QamPressedFillBrush
QamFocusBorderBrush
QamSeparatorBrush

QamSectionBrush
QamTileBrush
QamTileSelectedBrush

QamRailIconBrush
QamRailIconSelectedBrush
QamRailSelectedFillBrush
~~~

Do not reuse Windows AccentFillColorDefaultBrush as the QAM accent.

Do not use CardBackgroundFillColorDefaultBrush as the QAM section/tile surface.

## 8.2 Metrics

Centralize the measured values required by multiple files, for example:

~~~text
QamContentPadding
QamSectionSpacing
QamSectionPadding
QamSectionCornerRadius

QamRowPadding
QamRowMinHeight
QamRowCornerRadius
QamRowSpacing
QamSelectionBorderThickness

QamRailButtonSize
QamRailItemHeight
QamRailIconSize
QamRailSpacing
QamRailCornerRadius

QamTilePadding
QamTileCornerRadius
QamTileMinHeight
~~~

Do not create a C# settings object for these.

XAML resources are sufficient.

## 8.3 Typography styles

Add explicit keyed styles, for example:

~~~text
QamBodyTextStyle
QamBodyStrongTextStyle
QamCaptionTextStyle
QamSectionHeaderTextStyle
QamValueTextStyle
QamTileTitleTextStyle
~~~

Each should define only visual properties appropriate to the role.

Do not override every TextBlock in the application through one implicit global style.

---

# 9. Theme direction

Change the Overlay visual root from:

~~~text
RequestedTheme="Light"
~~~

to:

~~~text
RequestedTheme="Dark"
~~~

This is not a user-selectable theme.

The QAM palette remains explicit through QamOverlayResources.xaml rather than relying on generic Windows dark-mode colors.

Remove the old light-only literals:

~~~text
#FFE7E7E7
#FFD6D6D6
Colors.DimGray
Windows AccentFillColorDefaultBrush selection
Windows SubtleFillColorSecondaryBrush selection
Windows CardBackgroundFillColorDefaultBrush section/tile fills
~~~

when they are part of the Overlay's product chrome.

---

# 10. Window surface

Current:

~~~text
SurfaceHost = #FFE7E7E7
TabRail     = #FFD6D6D6
content     = Windows light defaults
~~~

PR A:

~~~text
SurfaceHost.Background = QamSurfaceBrush
TabRail.Background      = QamRailBrush
content background      = QamContentBrush when the measured QAM uses a distinct content color
~~~

Preserve:

~~~text
OpaquePanel
no acrylic
no Mica
no blur
no transparent desktop composition
~~~

Do not add shadows unless the actual current QAM evidence shows a visible shadow that materially affects the panel.

The goal is QAM parity, not a generic Windows 11 floating panel.

---

# 11. Geometry boundary

Do not reopen PR638's geometry policy.

Keep:

~~~text
right-side panel
MaxSurfaceWidthDip = 416
left rail structural width = 52 DIP
small outer floating gap
per-edge work-area safety
~~~

The observed Steam QAM rail was approximately 48 CSS px, while this Addon intentionally remains somewhat wider than real QAM.

PR A may tune only internal visual spacing if measured QAM evidence supports it:

~~~text
content inner padding
rail button size
rail vertical spacing
section spacing
row spacing
~~~

Do not change the window width merely to make a text string fit.

If the later hardware review concludes 400/416/432 DIP should change, do that in a separate tiny geometry follow-up.

---

# 12. Rail visual parity

Keep the existing tab identities and order logic.

Do not change:

~~~text
Device
Profile
Controller
Shortcut
Setting
~~~

Do not change LB/RB navigation.

Replace Windows/default visual values with QAM resources.

The rail must use:

~~~text
QamRailBrush
QamRailIconBrush
QamRailIconSelectedBrush
QamRailSelectedFillBrush
QamAccentBrush / measured indicator style
~~~

If the actual QAM selected tab uses a pill/block without the current 3 px right indicator, remove the indicator.

If the actual QAM uses an indicator, match its measured location, thickness, and radius.

Do not preserve the current 3 px indicator merely because it already exists.

Do not invent an additional tab focus state.

Existing selected tab authority remains _tabState.SelectedTab.

A small keyed Button style for rail buttons is allowed if it is the smallest way to suppress obvious default Windows hover/pressed chrome.

Do not build a custom RailButton control.

---

# 13. Ordinary row chrome

Current OverlayRowChrome:

~~~text
Padding      = 14,7,14,7
MinHeight    = 54
CornerRadius = 8
left border  = 3 px selection accent
~~~

Replace these constants with QAM resource values.

The current left-accent selection model is not sacred.

The selected/focused visual must follow the measured QAM row style.

Possible measured result may be:

~~~text
filled focused row
outline
accent edge
or a combination
~~~

Implement the actual measured shape.

Do not add animation to row selection unless the real QAM uses a clearly observable transition and the change is trivial.

No new selection manager.

---

# 14. Selection/focus visual authority

Keep one logical selection authority:

~~~text
OverlayRowSelection
~~~

Only change visual application.

Current:

~~~text
selected
→ _rowSelectedFillBrush
→ _rowSelectedBrush
~~~

PR A:

~~~text
selected
→ QamSelectedFillBrush
→ QamFocusBorderBrush / QamAccentBrush according to measured QAM
~~~

Update OverlayWindow.xaml.cs so:

~~~text
_rowSelectedBrush
_rowSelectedFillBrush
~~~

are sourced from QAM resources, not WinUI accent/subtle-fill resources.

Keep the existing field seams if that results in the smallest patch.

Do not introduce a visual-state authority object.

---

# 15. Section chrome

Current generic Quick Settings sections are Windows-card-like:

~~~text
CardBackgroundFillColorDefaultBrush
Padding = 8
CornerRadius = 8
~~~

That is one of the strongest remaining Windows Settings cues.

Change CreateOverlaySectionCard(...) to use measured QAM section behavior.

If the actual QAM section is visually flat:

~~~text
Background = Transparent / QamContentBrush
small or zero radius
measured section padding
measured section spacing
optional measured divider
~~~

If the actual QAM uses a distinct section surface, use QamSectionBrush.

Do not keep a Windows card because it is convenient.

Do not delete the helper or rebuild page rendering just to remove card chrome.

The helper can remain the one construction seam while its visual resources change.

---

# 16. Typography

Replace generic Windows text styles used by the Overlay with QAM-specific keyed styles.

Examples currently using Windows styles:

~~~text
BodyTextBlockStyle
BodyStrongTextBlockStyle
CaptionTextBlockStyle
manual FontSize = 17
manual Semibold
manual Opacity = 0.7 / 0.72 / 0.75
~~~

Move those visual decisions into QAM resources/styles.

Update at least:

~~~text
OverlayToggleRow labels
OverlayValueRow labels
OverlayValueRow values
Quick Settings section labels/messages
Controller M1/M2 header/caption
ClawHUD captions/status
Setting card title/summary
Profile status/title
Shortcut status/title
placeholder text if any remains
~~~

Do not change strings/content semantics.

Do not change localization scope in this PR.

---

# 17. Font policy

Visual fidelity goal:

~~~text
match Steam font family when legally and technically practical
match exact font size/weight/line-height regardless
~~~

Known current evidence:

~~~text
Steam GamepadUI CSS prefers Motiva Sans
Steam CEF receives a Motiva resource through
D:/Program Files (x86)/Steam/clientui/fonts/clientui.uifont
via Steam custom_fonts
~~~

Important boundary:

Do NOT:

- copy clientui.uifont into this repository;
- extract Motiva font files from it;
- ship Valve font resources;
- install Steam fonts into Windows;
- write a clientui.uifont parser;
- depend on Steam's private font container format.

If normal WinUI font resolution can use Motiva Sans without extracting/bundling anything, document and use that only after verifying it.

Otherwise use the closest normal Windows font fallback, currently expected to be Segoe UI Variable / Segoe UI, while preserving the measured Steam:

~~~text
font-size
font-weight
line-height
letter-spacing
text color
~~~

For Korean, use the actual rendered-font evidence when available.

Do not claim the fallback is Steam's actual Korean font unless DevTools proves it.

---

# 18. Quick Settings Device/Profile pages

Do not change the generic renderer/binding architecture.

Keep:

~~~text
QuickSettingsSurface
OverlayQuickSettingsPageBinding
stable section reconciliation
row identity preservation
Runtime-authoritative mutation/readback
~~~

PR A changes only visual construction:

~~~text
section header style
caption style
section chrome
row chrome
text/value style
selection style
spacing
~~~

Do not add QuickSettings-specific style state in the Runtime contract.

Do not add visual metadata to QuickSettingsPageSnapshot.

Visual policy belongs to the Overlay.

---

# 19. Controller page

Keep PR639 behavior unchanged.

The M1/M2 page should inherit:

~~~text
QAM section chrome
QAM body text
QAM secondary caption
QAM value text
QAM row focus
~~~

Do not change:

- BackButtonMappingSettings;
- labels/options;
- mutation behavior;
- availability;
- selection-preservation fix;
- SteamDeck R4/L4 meaning.

The actual discrete-choice control chrome remains PR B.

---

# 20. Setting page

Keep the existing expandable-card semantics.

PR A should make its collapsed/expanded shell look like QAM settings rows instead of Windows cards.

Style:

~~~text
header title
summary
chevron
expanded detail spacing
detail indentation
selected header row
detail row
~~~

from QAM resources.

Do not redesign the expander state machine.

Do not create a WinUI Expander dependency if the current simple implementation remains sufficient.

---

# 21. Shortcut page

The Shortcut page is a product-specific quick-action grid, so it does not need to become a literal Steam system setting list.

However, it must stop looking like a Windows card dashboard.

Change its base surface to QAM resources:

~~~text
title typography
secondary status typography
tile fill
tile radius
tile padding
tile selected/focus border/fill
disabled opacity
grid spacing
~~~

Keep:

~~~text
2-column maximum
TileId authority
execution behavior
selection model
pointer behavior
~~~

Do not redesign Shortcut data or execution.

PR C may later refine exact tile density after hardware review.

---

# 22. Profile catalog

The Profile catalog has no exact native QAM semantic equivalent.

Do not force it into an unrelated Steam control merely for visual mimicry.

Keep the existing catalog behavior and 3-column layout for PR A.

Only replace Windows visual primitives:

~~~text
CardBackgroundFillColorDefaultBrush
generic BodyTextBlockStyle
generic selected fill/accent
generic radius/padding where shared QAM tile values are appropriate
~~~

Use:

~~~text
QamTileBrush
QamTileSelectedBrush
QamFocusBorderBrush
QamTileTitleTextStyle
QamTilePadding
QamTileCornerRadius
~~~

PR C can revisit the catalog layout if actual hardware shows it is too dense at 416 DIP.

---

# 23. Toggle and value controls — PR A boundary

Do not fully restyle the ToggleSwitch or value-stepper visual trees in this PR.

Allowed:

- QAM text foreground around them;
- dark theme inheritance;
- surrounding row chrome;
- removal of obvious inherited light theme;
- a tiny resource setter needed to prevent an unreadable state.

Deferred to PR B:

~~~text
QAM ToggleSwitch track/thumb template
QAM numeric slider
QAM discrete value selector
QAM arrow/stepper button states
QAM pressed/hover/focus states inside those controls
~~~

This boundary keeps PR A reviewable and makes visual regressions easy to isolate.

---

# 24. Do not override Windows resources globally

Avoid replacing framework keys such as:

~~~text
AccentFillColorDefaultBrush
CardBackgroundFillColorDefaultBrush
TextFillColorPrimaryBrush
~~~

at Application scope just to coerce the UI.

Use explicit Qam* resources for product-owned visuals.

Reason:

- framework controls may consume those keys in unexpected internal places;
- PR B needs deliberate control templates rather than accidental global theme mutation;
- explicit keys make Steam-reference changes easy to audit.

---

# 25. Resource use from programmatic UI

Most Overlay controls are created in C#.

Follow the repository's existing small resource-lookup pattern.

For example, assign a keyed style/brush from Application.Current.Resources when constructing a programmatic element.

It is acceptable to add one very small stateless typed-resource helper only if repeated lookup code becomes clearly noisy.

Do not add:

- ThemeManager;
- StyleService;
- VisualStateCoordinator;
- runtime theme cache;
- DI registration;
- another owner object.

One ResourceDictionary remains the authority.

---

# 26. Files expected to change

Primary:

~~~text
src/SteamInputAddonforClaw.Overlay/App.xaml
src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml   [new]

src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
src/SteamInputAddonforClaw.Overlay/OverlayRowChrome.cs
src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayValueRow.cs

src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shortcuts.cs

docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md               [new]
~~~

Test files as required.

No Runtime/controller project should need a functional edit.

---

# 27. Tests

## 27.1 Resource dictionary contract

Add a focused source/XML contract test proving:

~~~text
QamOverlayResources.xaml exists
App.xaml merges it
all required Qam* brush/style keys exist exactly once
Overlay root requests Dark theme
old light surface literals are gone
~~~

Do not instantiate a full WinUI window solely for this.

## 27.2 Old Windows chrome removal

Add source-contract assertions that product-owned Overlay code no longer depends on:

~~~text
#FFE7E7E7
#FFD6D6D6
Colors.DimGray
AccentFillColorDefaultBrush for row/tab selection
SubtleFillColorSecondaryBrush for selection fill
CardBackgroundFillColorDefaultBrush for section/tile surfaces
~~~

Do not forbid those strings repository-wide; scope tests to the Overlay visual files where they were replaced.

## 27.3 Shared visual ownership

Prove:

~~~text
OverlayRowChrome consumes QAM metrics
OverlayWindow selection brushes consume QAM resources
rail consumes QAM resources
Quick Settings section card consumes QAM resources
Shortcut tile consumes QAM resources
Profile card consumes QAM resources
~~~

## 27.4 Typography

Prove the programmatic Overlay surfaces use QAM keyed styles rather than generic Windows Body/Caption styles where PR A owns the text role.

Do not write brittle tests for every whitespace position in XAML.

## 27.5 Behavior regression

Keep all current tests passing for:

~~~text
tab navigation
row selection
M1/M2 selection preservation
Quick Settings section reconciliation
Profile selection
Shortcut selection/execution
Setting expansion
Overlay lifecycle
transport
Full1902 Runtime
~~~

No behavior test should need to change merely because a color changed.

---

# 28. Hardware/visual acceptance

Reference device:

~~~text
MSI Claw
1920 × 1200
150% display scaling
~~~

Compare the Addon Overlay directly against the current Steam QAM on the same display.

## Required PR A visible result

1. The Overlay reads immediately as the same dark UI family as Steam QAM.
2. The rail and content pane no longer look like Windows light-mode surfaces.
3. Primary/secondary text hierarchy matches QAM closely.
4. Rail selected/unselected state matches QAM closely.
5. Controller-selected row/focus state matches QAM closely.
6. Section separation/hierarchy matches QAM closely.
7. Windows Settings card chrome is absent from Device/Profile/Controller/Setting shared sections.
8. Profile/Shortcut custom surfaces use the same QAM palette and focus language.
9. No visible light-gray #E7E7E7 / #D6D6D6 shell remains.
10. No default Windows accent blue is used merely because it is the OS accent.
11. Text is readable at 150%.
12. Existing controller navigation still works exactly as before.
13. Existing pointer/touch behavior still works.
14. Hidden scrollbar policy remains unchanged.
15. The Overlay remains opaque and no-activate.

## Explicitly acceptable PR A differences

Until PR B:

~~~text
ToggleSwitch shape may still look partly WinUI
numeric/discrete stepper controls may still look partly WinUI
slider parity is not yet complete
~~~

Those are not reasons to expand PR A.

---

# 29. Visual comparison method

Prefer side-by-side comparison:

~~~text
Steam QAM open
capture current QAM

Addon Overlay open
capture same 1920x1200 / 150% environment

compare:
surface
rail
inner margins
text baseline
section spacing
row height
selected state
icon scale
content density
~~~

Do not judge parity from a desktop screenshot captured at another DPI.

If a difference comes from the deferred Toggle/Slider templates, record it for PR B rather than broadening PR A.

---

# 30. Reference documentation rules

The new QAM visual reference document must distinguish:

~~~text
Measured
Observed
Inferred
Deferred
~~~

Example:

~~~text
Primary text color       Measured   rgb(...)
Section gap              Measured   12 CSS px
Korean rendered font     Deferred   not yet exposed
Motiva WinUI availability Inferred/Failed PoC
~~~

Do not turn a screenshot estimate into an exact computed value.

The goal is to make future Steam-QAM visual updates cheap:

~~~text
Steam QAM appearance changes
→ remeasure current QuickAccess
→ update QAM visual reference
→ update QamOverlayResources.xaml
→ pages inherit the change
~~~

---

# 31. Explicit non-goals

Not in PR A:

- CSS injection;
- Decky integration;
- Steam React reuse;
- WebView2/CEF;
- Motiva extraction/bundling;
- final ToggleSwitch template;
- final Slider template;
- final discrete-value template;
- new numeric slider behavior;
- Device/Profile product logic changes;
- Controller product logic changes;
- Shortcut behavior changes;
- Profile catalog layout redesign;
- tab order changes;
- geometry width redesign;
- overlay animation redesign;
- Runtime/transport change;
- localization project;
- user-selectable themes;
- Windows accent integration;
- Mica/Acrylic/blur.

---

# 32. Overengineering guardrails

Desired shape:

~~~text
one QamOverlayResources.xaml
+
existing OverlayWindow owner
+
existing row/page primitives
+
measured QAM values
~~~

Do not add:

- QamThemeManager;
- SteamCssParser;
- SteamStyleImporter;
- MotivaFontExtractor;
- VisualTokenService;
- SkinEngine;
- ThemeProvider;
- theme JSON;
- CSS runtime;
- DOM/CEF inspector runtime;
- WebView;
- page-specific duplicate color constants.

The resource dictionary is the visual authority.

---

# 33. Definition of done

PR A is complete when the current:

~~~text
light WinUI shell
+
Windows accent selection
+
Windows card surfaces
+
generic Windows typography
~~~

has become:

~~~text
measured Steam-QAM-like dark surface
+
QAM-like left rail
+
QAM-like text hierarchy
+
QAM-like section hierarchy
+
QAM-like row/focus selection
+
shared QAM visual resources across every current Overlay page
~~~

without changing any feature/runtime/navigation authority.

The next focused work order, PR B, should then implement:

~~~text
QAM ToggleSwitch
QAM numeric slider
QAM discrete value selector
QAM control hover/pressed/focus templates
~~~

on top of the PR A resources rather than introducing another style system.
