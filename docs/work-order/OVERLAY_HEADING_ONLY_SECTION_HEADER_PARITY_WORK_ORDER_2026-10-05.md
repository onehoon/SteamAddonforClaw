# Work Order — Overlay Heading-Only Section Header Parity

**Date:** 2026-10-05  
**Status:** Ready for implementation  
**Target repository:** `onehoon/SteamAddonforClaw`  
**Target branch:** `main`  
**Reviewed main baseline:** `f10c7863ffcb69ccd4f97e4cc3c6335678917a9d`  
**Scope:** standalone WinUI 3 Overlay presentation only  
**Implementation shape:** one small focused PR

---

# 0. Goal

Finish the Overlay section-hierarchy cleanup after PR670.

PR670 correctly unified:

~~~text
top-level section separators
detail indentation
generic Device/Profile detail stacks
Controller M1/M2, LED, and vibration detail stacks
Profile order
TDP Control naming
~~~

but one visual inconsistency remains:

> **Top-level sections with a Toggle use the shared row chrome, while top-level sections without a Toggle still render a naked TextBlock.**

That difference is visible on hardware.

Current examples:

~~~text
TDP Control / CPU Boost / Windows Power Mode / Intel FPS Limit
-> OverlayToggleRow
-> QamBodyStrongTextStyle
-> OverlayRowChrome
-> QamRowPadding
-> correct separator-to-title spacing

Resolution / M1-M2 / Vibration Strength
-> direct TextBlock
-> no OverlayRowChrome
-> no QamRowPadding
-> title sits too close to the section separator
~~~

A second related inconsistency exists in Controller:

~~~text
Joystick LED
-> OverlayToggleRow(... strongLabel: false)
-> QamBodyTextStyle
-> normal weight + secondary text color
~~~

while every other top-level toggle feature uses:

~~~text
strongLabel: true
-> SemiBold + QamPrimaryTextBrush
~~~

This PR must finish that presentation parity without changing any feature behavior.

---

# 1. Mandatory project review

Before editing, read the current versions of:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md
docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md

docs/work-order/OVERLAY_SHARED_SECTION_HIERARCHY_AND_PROFILE_UI_ALIGNMENT_WORK_ORDER_2026-10-05.md
~~~

PR670 is the current presentation baseline.

Do not regress:

- section-level separator ownership;
- detail-row indentation;
- Profile feature order;
- `TDP Control` naming;
- Intel FPS Main App visibility;
- fill-only row selection;
- page title spacing.

---

# 2. Mandatory source review

Inspect current main:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayRowChrome.cs
src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs
src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml

tests/SteamInputAddonforClaw.UiTests/OverlayControllerRendererTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayQamVisualResourcesTests.cs
~~~

If main has moved from the reviewed SHA, re-read these files before implementation.

---

# 3. Current code facts

## 3.1 Toggle-backed top-level headers already use the correct shared chrome

Generic Device/Profile feature sections detect a first visible Toggle through:

~~~text
OverlayQuickSettingsSectionRendering.TryGetFeatureHeaderToggle(...)
~~~

and render it through:

~~~csharp
TryCreateQuickSettingsRow(
    surface,
    featureHeaderToggle,
    out var headerRow,
    section.Label,
    strongLabel: true)
~~~

That path ultimately creates:

~~~text
OverlayToggleRow
-> QamBodyStrongTextStyle
-> OverlayRowChrome.Create(...)
-> QamRowPadding = 16,10,16,10
~~~

This is why TDP Control, CPU Boost, Windows Power Mode, and Intel FPS Limit have the correct:

- top padding below a section separator;
- bottom padding before their detail rows;
- SemiBold title;
- primary white title color.

Do not change this path.

## 3.2 Generic no-toggle sections bypass row chrome

Current `BuildQuickSettingsSection(...)` does this when a section does not use a feature-header Toggle:

~~~csharp
if (!usesFeatureHeader && !string.IsNullOrEmpty(section.Label))
{
    var label = new TextBlock
    {
        Text = section.Label,
        TextWrapping = TextWrapping.Wrap,
    };
    OverlayQamResources.ApplyTextStyle(label, "QamBodyStrongTextStyle");
    sectionPanel.Children.Add(label);
}
~~~

That TextBlock is not inside `OverlayRowChrome`.

Current Profile Resolution therefore renders approximately:

~~~text
section separator
Resolution          <- no QamRowPadding above
    Resolution row
~~~

This is the source of the separator appearing visually attached to `Resolution`.

## 3.3 Controller heading-only sections duplicate the same bypass

Current:

~~~csharp
private static StackPanel CreateControllerSection(string title)
{
    var section = new StackPanel
    {
        Spacing = OverlayQamResources.Get("QamSectionHeaderSpacing", 4.0),
    };

    var heading = new TextBlock { Text = title };
    OverlayQamResources.ApplyTextStyle(
        heading,
        "QamBodyStrongTextStyle");

    section.Children.Add(heading);
    return section;
}
~~~

This is used for:

~~~text
M1 / M2
Vibration Strength
~~~

So these top-level headings also do not receive `QamRowPadding`.

`Vibration Strength` makes the issue especially visible because it has a separator above it.

`M1 / M2` is the first Controller section and has no separator, but it is the same visual type and must use the same path.

## 3.4 Joystick LED uses the wrong top-level label strength

Current:

~~~csharp
_controllerLedEnabledRow =
    new OverlayToggleRow(
        "Joystick LED",
        RequestControllerLedEnabled);
~~~

The constructor default is:

~~~csharp
bool strongLabel = false
~~~

so it uses:

~~~text
QamBodyTextStyle
FontWeight = Normal
Foreground = QamSecondaryTextBrush (#FFDCDEDF)
~~~

Top-level feature headers should use:

~~~text
QamBodyStrongTextStyle
FontWeight = SemiBold
Foreground = QamPrimaryTextBrush (#FFFFFFFF)
~~~

This explains the current hardware screenshot where `Joystick LED` appears thinner and slightly grayer than the other top-level feature labels.

---

# 4. Product rule

The final visual rule is:

~~~text
Top-level option with Toggle
-> OverlayToggleRow(... strongLabel: true)
-> OverlayRowChrome

Top-level option without Toggle
-> shared heading-row helper
-> QamBodyStrongTextStyle
-> OverlayRowChrome

Detail row
-> existing CreateOverlayDetailStack()
-> existing row primitives
~~~

The difference between "toggle" and "no toggle" must affect interaction only.

It must **not** create different:

- title font weight;
- title color;
- separator-to-title spacing;
- title-row vertical padding;
- title-row horizontal geometry.

---

# 5. Change A — add one shared heading-only top-level row helper

Add one very small helper on the existing `OverlayWindow` partial.

Recommended shape:

~~~csharp
private static Border CreateOverlaySectionHeadingRow(string title)
{
    var heading = new TextBlock
    {
        Text = title,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };

    OverlayQamResources.ApplyTextStyle(
        heading,
        "QamBodyStrongTextStyle");

    return OverlayRowChrome.Create(heading);
}
~~~

Exact naming may follow current conventions.

Requirements:

- reuse `QamBodyStrongTextStyle`;
- reuse `OverlayRowChrome`;
- therefore reuse the existing `QamRowPadding`, `QamRowMargin`, `QamRowMinHeight`, and row corner radius;
- do not add a new padding resource;
- do not add a new font style;
- do not add a new reusable control class;
- do not register this heading row with controller selection;
- do not add it to `_pageRows`;
- do not make it clickable/focusable.

This helper is a presentation primitive only.

---

# 6. Change B — generic no-toggle sections use the same row hierarchy

Update `BuildQuickSettingsSection(...)`.

Do not keep the current naked label directly in `sectionPanel`.

The no-toggle path should structurally mirror the toggle path.

Target conceptual structure:

~~~text
rowStack
├─ heading row        <- CreateOverlaySectionHeadingRow(section.Label)
└─ detailStack
   └─ actual actionable row(s)
~~~

For a generic section with no feature toggle:

~~~csharp
var rowStack = new StackPanel
{
    Spacing = OverlayQamResources.Get(
        "QamRowSpacing",
        0.0),
};

if (!usesFeatureHeader && !string.IsNullOrEmpty(section.Label))
    rowStack.Children.Add(
        CreateOverlaySectionHeadingRow(section.Label));

var detailStack = CreateOverlayDetailStack();
...
rowStack.Children.Add(detailStack);
~~~

The exact implementation may be slightly different, but the final hierarchy must produce the same vertical relationship as a toggle-backed section.

Important:

- do **not** add `QamSectionHeaderSpacing` between the heading row and its detail stack;
- the heading row already carries the same row padding as toggle-backed headers;
- use the same zero row spacing used by the existing toggle path.

This avoids creating a new +4 DIP discrepancy.

## 6.1 Resolution

Profile Resolution must become:

~~~text
separator
[ shared heading row: Resolution ]
    [ existing Resolution value row ]
~~~

Keep the existing detail indent.

Do not change:

- Resolution options;
- `Do not change`;
- mutation behavior;
- availability rules;
- Profile contracts.

---

# 7. Change C — Controller heading-only sections use the shared helper

Update `CreateControllerSection(...)` so its title is built through the same:

~~~text
CreateOverlaySectionHeadingRow(...)
~~~

instead of a naked TextBlock.

This automatically aligns:

~~~text
M1 / M2
Vibration Strength
~~~

with generic no-toggle sections such as Resolution.

Required conceptual result:

~~~csharp
private static StackPanel CreateControllerSection(string title)
{
    var section = new StackPanel
    {
        Spacing = OverlayQamResources.Get(
            "QamRowSpacing",
            0.0),
    };

    section.Children.Add(
        CreateOverlaySectionHeadingRow(title));

    return section;
}
~~~

Then existing detail stacks remain unchanged.

Do not:

- add the heading to logical row selection;
- change M1/M2 navigation order;
- change vibration commit behavior;
- change section separator ownership.

---

# 8. Change D — Joystick LED is a strong top-level toggle header

Change only its presentation flag.

Current:

~~~csharp
new OverlayToggleRow(
    "Joystick LED",
    RequestControllerLedEnabled)
~~~

Required:

~~~csharp
new OverlayToggleRow(
    "Joystick LED",
    RequestControllerLedEnabled,
    strongLabel: true)
~~~

This must make Joystick LED use:

~~~text
QamBodyStrongTextStyle
SemiBold
QamPrimaryTextBrush
~~~

matching:

- TDP Control;
- CPU Boost;
- Windows Power Mode;
- Intel FPS Limit.

Do not create a separate `Joystick LED` heading TextBlock.

The Toggle row itself remains the section header.

---

# 9. Explicit exclusions

Do not apply the new heading-only helper to UI that is not the same semantic type.

Keep separate:

## Setting expandable cards

~~~text
ClawHUD
Quick Settings
Tab Order
~~~

These headings are actionable expand/collapse buttons.

They already use:

~~~text
QamBodyStrongTextStyle
OverlayRowChrome
~~~

through `CreateSettingCard(...)`.

Do not convert them into passive section heading rows.

## Profile catalog

Game cards remain custom tile UI.

## Shortcut

Shortcut tiles remain custom tile UI.

## Page title

The fixed PageTitle remains unchanged.

---

# 10. Expected visual result

## Profile

Before:

~~~text
────────────────────────
Resolution
    Resolution   < Do not change >
~~~

After:

~~~text
────────────────────────

Resolution
    Resolution   < Do not change >
~~~

The actual spacing must come from the same row chrome used by toggle-backed top-level headers, not an arbitrary new margin.

## Controller

Before:

~~~text
────────────────────────
Vibration Strength
    Left Motor
    Right Motor
~~~

After:

~~~text
────────────────────────

Vibration Strength
    Left Motor
    Right Motor
~~~

Again, the spacing comes from `OverlayRowChrome`.

## Joystick LED

Before:

~~~text
Joystick LED       [toggle]
normal / secondary
~~~

After:

~~~text
Joystick LED       [toggle]
semibold / primary
~~~

matching other top-level toggle features.

---

# 11. Tests

Update existing source-contract tests only.

## 11.1 `OverlayDeviceRendererWiringTests`

Replace the current implicit no-toggle heading expectations with explicit shared-heading coverage.

Assert:

~~~text
CreateOverlaySectionHeadingRow
QamBodyStrongTextStyle
OverlayRowChrome.Create(...)
~~~

are used for no-toggle generic section labels.

Verify `BuildQuickSettingsSection(...)` no longer directly creates the section label through:

~~~text
new TextBlock { Text = section.Label ... }
~~~

outside the shared helper.

Keep the existing Resolution detail-indent test.

Add an assertion that the no-toggle heading and detail stack are both placed in the same zero-spacing row stack or otherwise prove no `QamSectionHeaderSpacing` is inserted between them.

## 11.2 `OverlayControllerRendererTests`

Update Controller heading tests.

Required:

~~~text
CreateControllerSection("M1 / M2")
CreateControllerSection("Vibration Strength")
CreateControllerSection(...)
-> CreateOverlaySectionHeadingRow(title)
~~~

Assert the old direct heading creation is gone from `CreateControllerSection(...)`:

~~~text
new TextBlock { Text = title }
~~~

must not remain there.

For LED, assert exact construction includes:

~~~text
new OverlayToggleRow(
    "Joystick LED",
    RequestControllerLedEnabled,
    strongLabel: true)
~~~

or equivalent.

Do not weaken existing tests for:

- M1/M2 mapping row order;
- LED child controls;
- vibration delayed commit;
- runtime authority;
- selection/navigation.

## 11.3 `OverlayQamVisualResourcesTests`

No new visual resource should be required.

Keep proving:

~~~text
QamRowPadding = 16,10,16,10
QamBodyStrongTextStyle
QamPrimaryTextBrush
QamSecondaryTextBrush
~~~

remain current authorities.

Add source-contract coverage that `CreateOverlaySectionHeadingRow` uses `OverlayRowChrome.Create`.

Do not introduce:

~~~text
QamHeadingPadding
QamHeadingMargin
QamHeadingSeparatorGap
~~~

or equivalent duplicate resources.

---

# 12. Manual hardware validation

Validate on:

~~~text
MSI Claw
1920 x 1200
150% Windows scaling
~~~

Required visual checks:

### Profile

- Resolution title no longer touches the separator.
- Resolution title top/bottom spacing visually matches other top-level section headers.
- Resolution remains indented correctly at the detail row.

### Controller

- Vibration Strength no longer touches the separator.
- M1 / M2 uses the same heading-row vertical geometry even though it has no separator above it.
- M1/M2 detail indentation remains correct.
- Vibration Left/Right detail indentation remains correct.
- Joystick LED is visually the same weight/color as other top-level toggle features.

### Regression

Check:

~~~text
TDP Control
CPU Boost
Windows Power Mode
Intel FPS Limit
Battery Charge Limit
ClawHUD
Quick Settings
Tab Order
~~~

for unintended layout changes.

---

# 13. Runtime / lifecycle non-goals

This PR must not change:

- Full1902 authority;
- PID1901/PID1902 behavior;
- HidHide;
- VIIPER;
- DirectInput;
- routing rollback/fail-close;
- Sleep/Hibernate/Resume;
- Restart/Crash/Shutdown handling;
- physical device loss/PnP re-enumeration;
- controller presentation switching;
- Overlay show/hide/capture behavior;
- profile persistence;
- transport schemas;
- mutation commands;
- delayed-commit policies;
- LED authority;
- vibration authority;
- M1/M2 mapping semantics.

---

# 14. Expected production-file scope

Expected production changes should stay within:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs
~~~

Possibly no other production file is needed.

Expected tests:

~~~text
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayControllerRendererTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayQamVisualResourcesTests.cs
~~~

Do not modify `OverlayToggleRow` merely to change its default `strongLabel`.

The default remains useful for ordinary non-header toggle rows.

---

# 15. Acceptance criteria

The PR is complete when:

1. Toggle-backed top-level feature headers remain unchanged.
2. Heading-only top-level sections use one shared heading-row helper.
3. That helper uses `QamBodyStrongTextStyle`.
4. That helper uses `OverlayRowChrome`.
5. No new heading-specific padding/margin resource is introduced.
6. Generic Resolution uses the shared heading row.
7. Controller M1 / M2 uses the shared heading row.
8. Controller Vibration Strength uses the shared heading row.
9. Heading-only rows are not selectable and are not added to logical navigation rows.
10. Existing detail indentation remains unchanged.
11. Existing section-level separator ownership remains unchanged.
12. Joystick LED uses `strongLabel: true`.
13. Joystick LED therefore uses SemiBold + primary text color like other top-level toggle features.
14. Setting expandable-card headers remain on their current actionable Button path.
15. No Runtime/transport/lifecycle change is introduced.
16. Existing and updated tests pass.
17. Hardware screenshots confirm Resolution and Vibration Strength no longer visually touch their separator.

---

# 16. Implementation principle

Do not solve this with per-feature margins.

The bug is not:

~~~text
Resolution needs +10 DIP
Vibration needs +10 DIP
~~~

The bug is:

~~~text
same semantic top-level heading
-> different presentation construction
~~~

The fix is therefore:

~~~text
toggle top-level header
    -> shared row chrome

non-toggle top-level header
    -> shared row chrome

detail
    -> shared detail stack
~~~

Keep one visual authority for each semantic level and no more.
