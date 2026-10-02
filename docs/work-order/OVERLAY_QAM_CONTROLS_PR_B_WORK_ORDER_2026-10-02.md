# Work Order — Overlay QAM Controls PR B

**Date:** 2026-10-02  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**Target branch:** main  
**Reviewed main baseline:** 54b28391e60f2c9b57f699d9e1ad5ad0789261b1  
**Feature area:** Standalone WinUI 3 Overlay control visuals  
**Implementation shape:** one focused PR  
**Primary goal:** make the Overlay interactive controls look as close as practical to the current Steam Quick Access Menu while preserving the existing controller-first semantics, Runtime authority, mutation timing, and Full1902 lifecycle.

---

# 0. Goal

PR A established:

~~~text
dark QAM surface
left icon rail
measured QAM row chrome
QAM-like typography
QAM-like selected-row fill
flat QAM section hierarchy
shared QamOverlayResources.xaml
~~~

The strongest remaining Windows visual cues are:

~~~text
default WinUI ToggleSwitch
numeric previous/value/next stepper
discrete previous/value/next buttons
default WinUI Button visual states
~~~

PR B target:

~~~text
Boolean
→ QAM-like toggle

Device/Profile numeric range
→ QAM-like slider

Discrete / enum choice
→ QAM-like compact value field

Controller input semantics
→ unchanged
~~~

The objective remains maximum practical visual parity with the current Steam QAM, not merely a generic dark handheld UI.

---

# 1. Mandatory authority review

Read the latest versions before coding:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/work-order/OVERLAY_QAM_SHELL_FOUNDATION_PR1_WORK_ORDER_2026-10-02.md
docs/work-order/OVERLAY_CONTROLLER_M1_M2_MAPPING_PR1_WORK_ORDER_2026-10-02.md
docs/work-order/OVERLAY_QAM_VISUAL_FOUNDATION_PR_A_WORK_ORDER_2026-10-02.md

docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
~~~

This PR is presentation/control rendering only.

Do not modify:

- PID1901 / PID1902 ownership;
- HidHide;
- VIIPER;
- DirectInput;
- Xbox360 / SteamDeck presentation policy;
- Overlay capture/release-to-resume;
- named-pipe protocol;
- Quick Settings shared contracts;
- M1/M2 mapping transport;
- ClawHUD transport;
- Shortcut transport;
- tab-order transport;
- profile transport;
- show/hide/no-activate behavior.

---

# 2. Mandatory source review

Inspect current main before editing:

~~~text
src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml
src/SteamInputAddonforClaw.Overlay/OverlayQamResources.cs

src/SteamInputAddonforClaw.Overlay/OverlayRowChrome.cs
src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayValueRow.cs

src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
src/SteamInputAddonforClaw.Overlay/OverlayQuickSettingsPageBinding.cs
src/SteamInputAddonforClaw.Overlay/OverlayDelayedSliderCommit.cs

src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs
src/SteamInputAddonforClaw.Overlay/OverlayTabOrderRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs

src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs

tests/SteamInputAddonforClaw.UiTests/OverlayValueRowTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayToggleRowTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayDelayedSliderCommitTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayQuickSettingsPageBindingTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayControllerRendererTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayQamVisualResourcesTests.cs
~~~

---

# 3. Current direction supersedes the old all-ValueRow numeric visual decision

Historical work order:

~~~text
docs/work-order/OVERLAY_UI_FOUNDATION_PR_B_CONTROLLER_FIRST_VALUE_ROW_WORK_ORDER.md
~~~

replaced an earlier draggable Slider with one controller-first ValueRow for both numeric and discrete values.

That was correct for the September 2026 product direction.

The October 2026 product direction is explicitly:

> Make the standalone Overlay look as close as practical to Steam QAM.

Therefore PR B intentionally supersedes only the **numeric visual presentation** portion of that historical work order.

Frozen controller semantics remain:

~~~text
Up / Down
→ move logical row selection

Left / Right
→ adjust selected value one semantic step

A
→ no edit mode for numeric/discrete rows

B
→ normal Overlay back/dismiss behavior
~~~

Do not restore WinUI focus engagement and do not require A before Left/Right adjustment.

---

# 4. External reference findings

## 4.1 Decky

Current Decky frontend library exposes Steam-facing:

~~~text
ToggleField
SliderField
PanelSection
PanelSectionRow
~~~

and resolves ToggleField / SliderField from Steam CommonUIModule rather than maintaining an independent visual implementation.

Reviewed:

~~~text
https://github.com/SteamDeckHomebrew/decky-frontend-lib/blob/main/src/components/ToggleField.ts
https://github.com/SteamDeckHomebrew/decky-frontend-lib/blob/main/src/components/SliderField.ts
https://github.com/SteamDeckHomebrew/decky-frontend-lib/blob/main/src/components/Panel.ts
https://github.com/SteamDeckHomebrew/decky-frontend-lib/blob/main/src/utils/static-classes.ts
~~~

The current static class map includes:

~~~text
Toggle
ToggleRail
ToggleSwitch

SliderTrack
SliderHandle
SliderHandleContainer
SliderNotch
SliderNotchLabel
SliderControl
~~~

This is evidence that the Steam controls have specific visual structure worth measuring.

Do not add Decky as a dependency.

## 4.2 WinUI

Use standard WinUI Style / ControlTemplate mechanisms.

Reviewed Microsoft references:

~~~text
https://github.com/microsoft/microsoft-ui-xaml/blob/main/controls/dev/CommonStyles/ToggleSwitch_themeresources.xaml
https://github.com/microsoft/microsoft-ui-xaml/blob/main/controls/dev/CommonStyles/Slider_themeresources.xaml
https://learn.microsoft.com/windows/apps/develop/ui/controls/toggles
~~~

These are implementation references only.

Do not copy the full generic template blindly.

Use the smallest template that preserves the required WinUI control contract.

---

# 5. Actual Steam QAM remains the visual source of truth

Existing reference already measured:

~~~text
Steam build 1790721607
CEF target QuickAccess_uid17
surface #0E141B
focused row rgba(255,255,255,0.15)
ordinary row 42 CSS px
row padding 10px vertical / 16px horizontal
row radius 2px
~~~

PR A intentionally deferred control-specific measurements.

Before freezing PR B control constants, extend:

~~~text
docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md
~~~

with actual current ToggleField, SliderField, and discrete-field measurements.

Do not implement final control constants from screenshots when the current QuickAccess DOM is available.

---

# 6. Measurement gate

Use read-only DevTools/CDP inspection of the visible current QuickAccess target.

## Toggle

Capture OFF and ON where possible:

~~~text
control bounding box
rail width/height/radius
OFF fill
ON fill
border/stroke
thumb width/height/radius
thumb OFF fill
thumb ON fill
thumb OFF position
thumb ON position
disabled state if observable
pointer-over/pressed if safely observable
transition duration if directly observable
~~~

Record stable semantic roles where identifiable:

~~~text
Toggle
ToggleRail
ToggleSwitch
On
Disabled
~~~

Generated hashed class names are evidence only.

No runtime dependency on them.

## Numeric SliderField

Capture:

~~~text
row height
label/value placement
slider control width/height
track height/radius
inactive track color
active/value track color
handle width/height/shape/radius/color
selected-row interaction
value text position
suffix position
notch/tick geometry if present
disabled state if observable
~~~

Inspect at least one real Performance-page numeric slider.

## Discrete / ordered field

Find a current QAM finite ordered choice if available and capture:

~~~text
value alignment
previous/next controls if present
button/field height
button width
glyph size
background
focus/hover/pressed
disabled
~~~

If no directly comparable QAM control is available, mark this chrome provisional and reuse measured QAM row/value colors.

Do not claim exact parity without evidence.

---

# 7. Extend the existing QAM resource authority

Keep one visual authority:

~~~text
src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml
~~~

Do not add a ThemeManager.

Expected resource families:

## Toggle

~~~text
QamToggleStyle
QamToggleWidth
QamToggleHeight
QamToggleRailCornerRadius
QamToggleOffBrush
QamToggleOnBrush
QamToggleDisabledBrush
QamToggleThumbBrush
QamToggleThumbDisabledBrush
QamToggleThumbWidth
QamToggleThumbHeight
QamToggleThumbCornerRadius
QamToggleThumbOffOffset
QamToggleThumbOnOffset
~~~

## Slider

~~~text
QamSliderStyle
QamSliderTrackHeight
QamSliderTrackBrush
QamSliderValueTrackBrush
QamSliderDisabledTrackBrush
QamSliderThumbBrush
QamSliderThumbWidth
QamSliderThumbHeight
QamSliderThumbCornerRadius
QamSliderHeight
~~~

## Value buttons

~~~text
QamValueButtonStyle
QamValueButtonSize
QamValueButtonCornerRadius
QamValueButtonForeground
QamValueButtonDisabledForeground
QamValueButtonHoverFill
QamValueButtonPressedFill
QamDiscreteValueMinWidth
~~~

Exact names may vary slightly.

Do not override framework control resources globally.

---

# 8. Toggle implementation

Keep native:

~~~text
Microsoft.UI.Xaml.Controls.ToggleSwitch
~~~

and existing behavior:

~~~text
IsOn
IsEnabled
Toggled
authoritative readback suppression
OverlayToggleModel
A → RequestToggle()
pointer/touch → native toggle
~~~

Apply one explicit keyed:

~~~text
QamToggleStyle
~~~

Use a focused ControlTemplate only as required to reproduce measured Steam track/thumb geometry.

Do not replace ToggleSwitch with custom Border/Ellipse click state.

No custom boolean state machine.

---

# 9. Toggle template boundary

Implement only product-used states:

~~~text
Off
On
Disabled Off
Disabled On
PointerOver / Pressed only if measured and straightforward
~~~

Do not port every WinUI generic.xaml state.

Do not add a storyboard framework.

A small thumb transition is acceptable only when straightforward and evidence-backed.

---

# 10. Device/Profile numeric Quick Settings become QAM-like sliders

Current:

~~~text
QuickSettingsSliderKind.Numeric
→ OverlayValueRow
→ NumericStepper
~~~

Target:

~~~text
QuickSettingsSliderKind.Numeric
→ OverlayNumericSliderRow
→ QAM-like Slider visual
~~~

Add:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayNumericSliderRow.cs
~~~

A dedicated concrete row is justified because Device/Profile already provide real production consumers.

Do not create:

~~~text
IOverlayControl
OverlayControlBase
control registry
generic control factory
~~~

Three concrete row primitives are enough:

~~~text
OverlayToggleRow
OverlayNumericSliderRow
OverlayValueRow
~~~

---

# 11. Numeric Slider scope is Device/Profile first

Use OverlayNumericSliderRow only for generic Quick Settings rows with:

~~~text
QuickSettingsSliderKind.Numeric
+
QuickSettingsCommitMode.TrailingDebounce
~~~

These already support repeated preview changes safely:

~~~text
preview
→ ScheduleSlider(...)
→ existing trailing debounce
→ Runtime mutation/readback
~~~

Do not convert ClawHUD HUD Size or Opacity to draggable sliders in this PR.

Those currently use immediate mutation/in-flight gating. Converting them would require a separate commit-on-release policy or risk repeated immediate RPCs, which is outside a visual-only PR.

Keep ClawHUD numeric settings as the current stepper semantics, but style their buttons with QAM value-button resources.

---

# 12. OverlayNumericSliderRow structure

The row owns only presentation and local value intent.

Conceptually:

~~~text
Border / existing OverlayRowChrome
└─ vertical content
   ├─ top line
   │  ├─ label
   │  └─ formatted current value
   └─ native Slider
~~~

Exact geometry follows measured QAM SliderField.

Use existing QAM text styles.

Do not add page-specific headers.

---

# 13. Reuse one numeric normalization authority

Do not duplicate clamp/snap logic.

Preferred:

extend the existing OverlayValueModel with:

~~~text
ApplyState(...)
RequestAdjust(-1/+1)
RequestSet(desired)
Normalize(...)
CanDecrease
CanIncrease
~~~

RequestSet exists only for pointer/touch Slider changes.

Acceptable alternative:

extract one tiny pure normalization helper shared by OverlayValueRow and OverlayNumericSliderRow.

Do not create a view-model framework.

Normalization remains:

~~~text
clamp
→ snap to Step relative to Minimum
→ clamp
→ bounded floating cleanup
~~~

---

# 14. Numeric slider controller semantics

When the row is logically selected:

~~~text
Left
→ one Step lower

Right
→ one Step higher

A
→ no-op
~~~

Use existing OverlayRowCapabilities.

Do not give native Slider focus authority.

No FocusEngagement.

No slider edit mode.

The Slider visual reflects PreviewValue immediately.

---

# 15. Numeric slider pointer/touch semantics

Pointer/touch may use native Slider drag/tap behavior.

On user ValueChanged:

~~~text
desired raw value
→ normalize to product Step
→ local preview
→ existing request callback
→ ScheduleQuickSettingsSlider(...)
~~~

The existing binding already performs trailing debounce.

Do not add another timer/debounce to OverlayNumericSliderRow.

Programmatic ApplyState must not emit another request.

Use one narrow suppression flag around programmatic Slider.Value updates.

---

# 16. Preview/readback authority remains unchanged

~~~text
drag / Left / Right
→ local preview

fresh Runtime page during pending draft
→ existing pending draft remains effective

trailing commit settles
→ Runtime page authoritative

typed failure
→ returned authoritative page wins

operation failure
→ existing binder failure behavior
~~~

Native Slider.Value is never the settings authority.

---

# 17. Linked TDP constraints stay in the binding

Keep:

~~~text
OverlayQuickSettingsPageBinding.ApplyLinkedConstraints(...)
~~~

exactly as the linked TDP authority.

A user may drag one TDP slider and the companion slider may move because the effective pending draft is corrected.

Renderer must ApplyState into both existing row instances.

Do not move linked logic into OverlayNumericSliderRow.

---

# 18. Generic Quick Settings renderer split

Current surface state uses ValueRows for both numeric and discrete.

Target:

~~~text
Toggle
→ OverlayToggleRow

Slider + Numeric
→ OverlayNumericSliderRow

Slider + Discrete
→ OverlayValueRow
~~~

Maintain narrow dictionaries, for example:

~~~text
NumericSliderRows
ValueRows
ToggleRows
~~~

Do not add a generic row registry.

Shared contract remains:

~~~text
QuickSettingsControlKind.Slider
QuickSettingsSliderKind.Numeric
QuickSettingsSliderKind.Discrete
QuickSettingsSliderSpec
QuickSettingsCommitPolicy
~~~

No protocol version bump.

---

# 19. Structural reconciliation

Keep current same-shape reconciliation.

RowShape already includes SliderKind, so it already distinguishes Numeric from Discrete.

Stable numeric updates must:

~~~text
reuse existing OverlayNumericSliderRow
→ ApplyState(...)
~~~

Do not rebuild the row for value-only changes.

Preserve selection, scroll, pointer stability, pending preview, and low visual-tree churn.

---

# 20. Discrete value row remains the enum primitive

OverlayValueRow remains for:

~~~text
Quick Settings discrete options
Controller M1
Controller M2
ClawHUD Display Mode
ClawHUD Font
ClawHUD Alignment
ClawHUD Background
ClawHUD numeric stepper controls
~~~

No ComboBox.

No dropdown.

No flyout.

Controller:

~~~text
Left/Right
→ previous/next

A
→ no-op
~~~

Pointer:

~~~text
previous/next buttons
→ same one-step seam
~~~

---

# 21. QAM value-button chrome

Apply explicit:

~~~text
QamValueButtonStyle
~~~

to current Button/RepeatButton controls.

Keep:

~~~text
Button for discrete
RepeatButton for existing NumericStepper
~~~

Do not implement another repeat timer.

Do not change current RepeatButton Delay/Interval unless separate real UX evidence proves them wrong.

---

# 22. M1/M2 behavior is frozen

Keep:

~~~text
BackButtonMappingSettings
whole-record mutation
one mutation in flight
selection preservation
authoritative settlement
duplicate targets allowed
SteamDeck R4/L4 meaning
~~~

Only visual changes are allowed.

Do not change available targets.

---

# 23. ClawHUD boundary

Allowed:

~~~text
QAM Toggle style
QAM discrete value-button style
QAM numeric stepper button style
QAM text/value style
~~~

Not allowed:

~~~text
convert HUD Size to draggable Slider
convert Opacity to draggable Slider
new debounce policy
change mutation-in-flight behavior
change ClawHUD contract
~~~

---

# 24. Tab Order boundary

Apply QAM value-button styling to the existing move earlier/later buttons where appropriate.

Keep:

~~~text
authoritative order
one-position move intent
Left/Right controller movement
pointer buttons
~~~

No drag reorder.

No new glyph package.

---

# 25. Do not reintroduce Windows accent

Do not use final visible control colors from:

~~~text
AccentFillColorDefaultBrush
SliderTrackValueFill
ToggleSwitchFillOn
SystemAccentColor
~~~

Use measured Qam* resources.

That is the reason for explicit keyed styles/templates.

---

# 26. Do not globally override WinUI resources

Bad:

~~~text
Application-level replacement of framework ToggleSwitchFillOn
Application-level replacement of framework SliderTrackValueFill
~~~

Preferred:

~~~text
QamToggleStyle
QamSliderStyle
QamValueButtonStyle
~~~

assigned explicitly by concrete Overlay controls.

---

# 27. Accessibility

Preserve:

~~~text
AutomationProperties.Name
native IsEnabled
native ToggleSwitch semantics
native Slider range semantics
visible current value
~~~

For numeric slider:

- automation name comes from row label;
- native Slider remains the range control;
- do not add a duplicate hidden range control.

For discrete buttons:

- keep Previous value / Next value automation names.

Pixel parity must not remove accessibility semantics.

---

# 28. No Steam runtime dependency

The actual QAM is design evidence only.

Production Overlay must work when:

~~~text
Steam is stopped
Steam is updating
QuickAccess target is absent
generated CSS class names change
~~~

Production code must never:

- query Steam CEF;
- read DOM;
- inject CSS;
- parse Steam CSS;
- depend on Decky;
- depend on generated class names.

Only checked-in QamOverlayResources are used at runtime.

---

# 29. Expected files

Primary:

~~~text
docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md

src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml

src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayValueRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayNumericSliderRow.cs

src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs
src/SteamInputAddonforClaw.Overlay/OverlayTabOrderRow.cs

tests/SteamInputAddonforClaw.UiTests/...
~~~

No Contracts, Runtime, or FrontendTransport edit should be required.

---

# 30. Toggle tests

Prove:

~~~text
QamToggleStyle exists exactly once
OverlayToggleRow explicitly applies it
QamToggleStyle does not use Windows accent resources
authoritative ApplyState still suppresses Toggled feedback
Activate still calls RequestToggle
unavailable Toggle remains disabled
~~~

Keep existing pure Toggle model tests.

---

# 31. Numeric slider tests

Add a focused pure-model/control-contract test file.

Prove:

1. valid ApplyState;
2. malformed constraints fail closed;
3. RequestAdjust moves exactly one Step;
4. RequestSet snaps pointer values to Step;
5. boundaries emit no duplicate request;
6. unavailable emits no request;
7. authoritative ApplyState replaces local preview without emitting;
8. Step snapping is relative to Minimum.

Source/composition tests prove:

~~~text
QuickSettingsSliderKind.Numeric
→ OverlayNumericSliderRow

QuickSettingsSliderKind.Discrete
→ OverlayValueRow

numeric callback
→ ScheduleQuickSettingsSlider(...)

stable shape
→ existing numeric row updated in place
~~~

Do not add timing-race tests.

---

# 32. Delayed commit / linked constraint tests

Existing tests must remain green:

~~~text
OverlayDelayedSliderCommitTests
OverlayQuickSettingsPageBindingTests
linked TDP constraint tests
Profile context/stale-settlement tests
hide-time draft cancellation
~~~

No second debounce.

Repeated pointer drag updates simply reschedule the existing pending draft.

---

# 33. Discrete / M1/M2 / ClawHUD tests

Keep all current behavior tests passing.

Update old source assertions that require:

~~~text
QuickSettingsSliderKind.Numeric
→ OverlayValueButtonKind.NumericStepper
~~~

because that visual contract is intentionally superseded.

New generic renderer contract:

~~~text
Numeric
→ OverlayNumericSliderRow

Discrete
→ OverlayValueRow + DiscreteChoice
~~~

ClawHUD NumericStepper remains valid.

---

# 34. Visual acceptance

Reference hardware when available:

~~~text
MSI Claw
1920 × 1200
150%
~~~

Compare directly with current Steam QAM.

## Toggle

Check:

~~~text
track geometry
thumb geometry
ON/OFF positions
colors
disabled readability
vertical alignment
selected-row interaction
~~~

## Numeric slider

Check:

~~~text
track height/color
active track
thumb size/shape
label/value placement
pointer/touch drag
controller Left/Right one-step
selected-row fill
disabled state
~~~

## Discrete

Check:

~~~text
value alignment
previous/next density
glyph size
hover/pressed
disabled
long M1/M2 labels
~~~

Do not change 416 DIP panel geometry in this PR to compensate for a control-padding mistake.

---

# 35. Practical acceptance

Device/Profile numeric:

~~~text
Left/Right
→ one step
→ preview immediately
→ existing trailing commit
→ authoritative readback
~~~

Pointer drag:

~~~text
drag
→ snapped preview
→ existing debounce restarts
→ latest Runtime mutation after idle
~~~

Toggle:

~~~text
A / pointer
→ same mutation path
~~~

Discrete:

~~~text
Left/Right / button click
→ same mutation path
~~~

M1/M2:

~~~text
selected M2 must remain selected across mutation settlement
~~~

Overlay lifecycle:

~~~text
unchanged
~~~

---

# 36. Explicit non-goals

Not in PR B:

- Runtime changes;
- transport changes;
- protocol bump;
- shared Quick Settings rename;
- M1/M2 product changes;
- ClawHUD slider commit redesign;
- Profile catalog redesign;
- Shortcut redesign;
- panel width change;
- font extraction;
- CSS runtime;
- Decky dependency;
- WebView/CEF;
- dropdown/flyout;
- generic control framework;
- custom gesture manager;
- new navigation state;
- new debounce manager;
- A-to-enter-slider mode.

---

# 37. Overengineering guardrails

Target:

~~~text
QamOverlayResources.xaml
  ├─ QamToggleStyle
  ├─ QamSliderStyle
  └─ QamValueButtonStyle

OverlayToggleRow
OverlayNumericSliderRow
OverlayValueRow
  ↓
existing OverlayRowCapabilities
existing OverlayRowSelection
existing QuickSettings binding/delayed commit
~~~

Do not add:

- QamControlFactory;
- IOverlayControl;
- QamThemeManager;
- SliderCommitManager;
- pointer gesture service;
- control registry;
- generic form renderer;
- CSS parser;
- Steam UI adapter.

Three concrete row primitives are enough because all three have current production consumers.

---

# 38. Definition of done

Complete when:

~~~text
Boolean
→ QAM-like ToggleSwitch

Device/Profile numeric Quick Settings
→ QAM-like native Slider
→ pointer/touch drag
→ controller Left/Right one-step
→ existing trailing debounce/readback

Discrete / enum
→ QAM-like compact previous/value/next field

M1/M2
→ same QAM-like discrete field

ClawHUD immediate numeric
→ existing stepper semantics
→ QAM-styled buttons
~~~

with:

~~~text
no Runtime/transport changes
no second mutation authority
no new navigation model
no global Windows-accent styling
no Steam runtime dependency
measured QAM control values recorded in the visual reference
~~~

After PR B, the next work should be a small PR C page-level polish/hardware acceptance pass rather than another control-architecture rewrite.
