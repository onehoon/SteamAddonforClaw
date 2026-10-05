# Work Order — Overlay Fluent Rail Icons and LB/RB Bumper Hints

**Date:** 2026-10-05  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**Target branch:** main  
**Reviewed main baseline:** d5ed3a09fadc341233ae9e31cb9df904b5260bda  
**Feature area:** Standalone WinUI 3 Overlay / left vertical tab rail presentation  
**Implementation shape:** one focused visual-shell PR

---

# 0. Goal

Polish the Overlay's left vertical tab rail with the user-selected **Segoe Fluent Icons** glyphs and add small static LB/RB bumper hints at the top and bottom of the rail.

This PR is presentation-only.

Replace the current WinUI SymbolIcon tab mapping:

~~~text
Device     -> Symbol.CellPhone
Profile    -> Symbol.Contact
Controller -> Symbol.XboxOneConsole
Shortcut   -> Symbol.ViewAll
Setting    -> Symbol.Setting
~~~

with the following explicit Segoe Fluent Icons glyph mapping:

~~~text
Device     -> E945  LightningBolt
Profile    -> E71D  AllApps
Controller -> E7FC  Game
Shortcut   -> E75F  Dialpad
Setting    -> E713  Settings
~~~

Also add fixed, non-interactive rail hints:

~~~text
top of rail    -> F10C  BumperLeft
bottom of rail -> F10D  BumperRight
~~~

The visual meaning is:

~~~text
F10C BumperLeft
      ↓
[ Device     ]
[ Profile    ]
[ Controller ]
[ Shortcut   ]
[ Setting    ]
      ↓
F10D BumperRight

LB -> previous tab
RB -> next tab
~~~

The bumper icons are only visual hints. Existing controller semantics remain authoritative.

---

# 1. Mandatory project review

Before implementation, read the current versions of:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/work-order/OVERLAY_QAM_SHELL_FOUNDATION_PR1_WORK_ORDER_2026-10-02.md
docs/work-order/OVERLAY_QAM_VISUAL_FOUNDATION_PR_A_WORK_ORDER_2026-10-02.md
docs/work-order/OVERLAY_QAM_HARDWARE_POLISH_ROW_INSET_TOGGLE_BLUE_WIDTH_WORK_ORDER_2026-10-04.md

docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md
docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md
~~~

This PR must not change Full1902 controller authority or lifecycle behavior.

---

# 2. Mandatory source review

Inspect current main:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml
src/SteamInputAddonforClaw.Overlay/OverlayQamResources.cs

tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayQamVisualResourcesTests.cs
~~~

Current relevant facts:

~~~text
rail width         = 52 DIP
rail item height   = 64 DIP
rail icon size     = 24 DIP
TabStrip alignment = VerticalAlignment.Center
tab order          = Runtime authoritative / user reorderable
visible tab label  = icon only
accessible label   = LabelFor(id)
LB                 = previous tab
RB                 = next tab
~~~

Keep those contracts.

---

# 3. Scope boundaries

## In scope

1. Replace the five rail SymbolIcon values with exact FontIcon glyphs.
2. Use the Windows 11 Segoe Fluent Icons font explicitly.
3. Add a small static BumperLeft hint at the top of the rail.
4. Add a small static BumperRight hint at the bottom of the rail.
5. Preserve existing tab button selection/background/foreground behavior.
6. Preserve tooltip and accessibility names.
7. Add focused regression tests.
8. Update the active Overlay visual reference with the selected glyph mapping.

## Out of scope

Do not change:

- tab identities;
- tab order persistence;
- Runtime tab-order authority;
- LB/RB input handling;
- tab selection logic;
- row navigation;
- Overlay capture;
- controller routing;
- PID1901/PID1902;
- HidHide;
- VIIPER;
- DirectInput;
- X360/SteamDeck presentation;
- Overlay window geometry;
- 432-DIP maximum Overlay width;
- 52-DIP rail width;
- 64-DIP tab item height;
- 24-DIP tab icon size;
- page title or body layout;
- selected-tab fill behavior;
- hover/pressed behavior;
- any feature mutation;
- any protocol or transport contract.

Do not add:

- an icon package;
- SVG/PNG assets;
- an icon service;
- an icon registry;
- a new navigation abstraction;
- a controller-hint manager;
- a new input path.

---

# 4. Change A — use FontIcon + Segoe Fluent Icons for all five tabs

## 4.1 Replace SymbolIcon

Current BuildShell() uses:

~~~csharp
Content = new SymbolIcon
{
    Symbol = SymbolFor(id),
    Width = OverlayQamResources.Get("QamRailIconSize", 24.0),
    Height = OverlayQamResources.Get("QamRailIconSize", 24.0),
},
~~~

Replace this with FontIcon.

Use the font explicitly:

~~~text
Segoe Fluent Icons
~~~

Do not rely on an implicit default font.

A narrow implementation is sufficient:

~~~csharp
Content = new FontIcon
{
    FontFamily = new FontFamily("Segoe Fluent Icons"),
    Glyph = GlyphFor(id),
    FontSize = OverlayQamResources.Get("QamRailIconSize", 24.0),
    Width = OverlayQamResources.Get("QamRailIconSize", 24.0),
    Height = OverlayQamResources.Get("QamRailIconSize", 24.0),
},
~~~

Add only the required Microsoft.UI.Xaml.Media import for FontFamily.

Do not introduce a custom control.

## 4.2 Exact tab glyph mapping

Replace SymbolFor(...) with one local string mapping:

~~~csharp
private static string GlyphFor(AddonQuickSettingsTabId id) => id switch
{
    AddonQuickSettingsTabId.Device     => "\uE945", // LightningBolt
    AddonQuickSettingsTabId.Profile    => "\uE71D", // AllApps
    AddonQuickSettingsTabId.Controller => "\uE7FC", // Game
    AddonQuickSettingsTabId.Shortcut   => "\uE75F", // Dialpad
    AddonQuickSettingsTabId.Setting    => "\uE713", // Settings
    _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown Overlay tab identity."),
};
~~~

These glyphs are an explicit product selection for this rail.

Do not substitute similar symbols during implementation.

## 4.3 Preserve existing visual state ownership

The current button owns rail foreground/background state:

~~~text
normal Foreground   -> QamRailIconBrush
selected Foreground -> QamRailIconSelectedBrush
normal Background   -> QamSectionBrush
selected Background -> QamRailSelectedFillBrush
~~~

Keep that behavior.

The child FontIcon should not hard-code its own Foreground, because it must continue to inherit the button's visual state.

Do not create per-tab color resources.

## 4.4 Preserve accessibility

Keep the existing:

~~~csharp
AutomationProperties.SetName(button, label);
ToolTipService.SetToolTip(button, label);
~~~

The accessible name remains:

~~~text
Device
Profile
Controller
Shortcut
Setting
~~~

Do not use LightningBolt, AllApps, Game, Dialpad, or raw glyph codes as the user-facing accessible tab name.

---

# 5. Change B — add BumperLeft / BumperRight rail hints

## 5.1 Visual contract

Add two static FontIcon elements inside TabRail:

~~~text
top    -> F10C BumperLeft
bottom -> F10D BumperRight
~~~

They communicate:

~~~text
LB -> previous tab
RB -> next tab
~~~

They do not perform the action themselves.

## 5.2 Keep TabStrip centered and independent

Current structure:

~~~xml
<Border x:Name="TabRail">
    <Grid
        x:Name="TabStrip"
        VerticalAlignment="Center" />
</Border>
~~~

Use the smallest structure that allows:

~~~text
top bumper hint
centered TabStrip
bottom bumper hint
~~~

Recommended shape:

~~~xml
<Border x:Name="TabRail" ...>
    <Grid>
        <FontIcon
            x:Name="PreviousTabBumperHint"
            Glyph="&#xF10C;"
            FontFamily="Segoe Fluent Icons"
            FontSize="{StaticResource QamRailHintIconSize}"
            Foreground="{StaticResource QamDisabledTextBrush}"
            HorizontalAlignment="Center"
            VerticalAlignment="Top"
            Margin="{StaticResource QamRailHintMargin}"
            IsHitTestVisible="False"
            AutomationProperties.Name="LB, previous tab" />

        <Grid
            x:Name="TabStrip"
            VerticalAlignment="Center"
            RowSpacing="{StaticResource QamRailSpacing}" />

        <FontIcon
            x:Name="NextTabBumperHint"
            Glyph="&#xF10D;"
            FontFamily="Segoe Fluent Icons"
            FontSize="{StaticResource QamRailHintIconSize}"
            Foreground="{StaticResource QamDisabledTextBrush}"
            HorizontalAlignment="Center"
            VerticalAlignment="Bottom"
            Margin="{StaticResource QamRailHintMargin}"
            IsHitTestVisible="False"
            AutomationProperties.Name="RB, next tab" />
    </Grid>
</Border>
~~~

Exact element names may differ, but keep the structure this simple.

Do not move the hints into TabStrip.

The hints are fixed rail chrome and must not move when the user reorders tabs.

## 5.3 Hint size and visual priority

Add only the minimum dedicated visual resources:

~~~xml
<x:Double x:Key="QamRailHintIconSize">16</x:Double>
<Thickness x:Key="QamRailHintMargin">0,12,0,12</Thickness>
~~~

Use:

~~~text
QamRailHintIconSize = 16 DIP
Foreground          = QamDisabledTextBrush
~~~

Reason:

- tab icons remain the primary rail content at 24 DIP;
- bumper hints should read as guidance, not selectable tabs;
- the existing disabled/muted text brush already has the right lower visual priority.

Do not add a new hint brush unless hardware evidence later proves the existing muted brush unsuitable.

## 5.4 Non-interactive contract

The bumper hints must be:

~~~text
not buttons
not clickable
not selectable
not focusable
not tab-order items
not part of _tabButtons
not part of _tabHosts
not part of _pageRows
not part of tab reorder
not part of controller navigation state
~~~

No Click, Tapped, PointerPressed, or selection callback is needed.

IsHitTestVisible="False" should make this explicit.

The existing semantic controller path remains:

~~~text
LB -> SelectPreviousTab()
RB -> SelectNextTab()
~~~

Do not duplicate that logic in the UI hints.

---

# 6. Keep the rail geometry unchanged

This PR must not resize the tab rail.

Keep:

~~~text
QamRailButtonSize = 52
QamRailItemHeight = 64
QamRailIconSize   = 24
QamRailSpacing    = 0
rail column width = 52
~~~

The bumper hints live within the existing 52-DIP rail.

Do not widen the Overlay or content area for these icons.

Do not change PR672 full-monitor placement.

---

# 7. Do not reintroduce the old footer/hint block

Older Overlay iterations had larger textual controller-help areas that consumed vertical space.

This PR must not add:

~~~text
LB / RB text labels
a bottom footer
a help card
a controller instruction panel
a separate legend
~~~

The two small Fluent bumper glyphs are the entire navigation hint.

The earlier "no footer/hint bar" shell policy remains valid: these are passive rail chrome, not a footer bar.

---

# 8. Tests

## 8.1 Update current shell source-contract test

Update the existing rail assertions in:

~~~text
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
~~~

Remove assertions that require:

~~~text
Symbol.CellPhone
Symbol.Contact
Symbol.XboxOneConsole
Symbol.ViewAll
Symbol.Setting
SymbolFor(
~~~

Replace them with assertions proving the exact new mapping:

~~~text
Content = new FontIcon
FontFamily = new FontFamily("Segoe Fluent Icons")

Device     -> \uE945
Profile    -> \uE71D
Controller -> \uE7FC
Shortcut   -> \uE75F
Setting    -> \uE713
~~~

Also prove:

- AutomationProperties.SetName(button, label) remains;
- ToolTipService.SetToolTip(button, label) remains;
- QamRailButtonStyle remains;
- QamRailIconSize remains;
- selected visual state still mutates the button Foreground/Background rather than the child icon directly.

Do not add reflection infrastructure just for glyphs.

A source-contract assertion is enough.

## 8.2 Add bumper-hint XAML regression

In the most appropriate existing Overlay visual test, verify:

~~~text
F10C exists once in TabRail
F10D exists once in TabRail
FontFamily = Segoe Fluent Icons
QamRailHintIconSize is used
QamDisabledTextBrush is used
top icon VerticalAlignment = Top
bottom icon VerticalAlignment = Bottom
both IsHitTestVisible = False
TabStrip remains VerticalAlignment = Center
~~~

Also verify the two hints are siblings of TabStrip, not children of it.

This prevents user tab reorder from moving the hints.

## 8.3 Resource regression

Extend OverlayQamVisualResourcesTests to require:

~~~text
QamRailHintIconSize = 16
QamRailHintMargin   = 0,12,0,12
~~~

Do not create additional resources without a visible need.

---

# 9. Active visual documentation

Update:

~~~text
docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md
~~~

with the current rail icon selection:

~~~text
Device     E945 LightningBolt
Profile    E71D AllApps
Controller E7FC Game
Shortcut   E75F Dialpad
Setting    E713 Settings

LB hint    F10C BumperLeft
RB hint    F10D BumperRight
~~~

Document that:

- tab glyphs use Segoe Fluent Icons;
- bumper hints are passive rail chrome;
- actual LB/RB behavior remains existing controller navigation;
- no image assets are introduced.

Do not rewrite historical work orders.

---

# 10. Manual hardware validation

Primary target:

~~~text
MSI Claw
1920 x 1200
150% / 144 DPI
~~~

Validate:

1. All five tab glyphs render correctly.
2. No tofu/missing-glyph boxes appear.
3. The 24-DIP tab icons remain visually centered in the 52-DIP rail.
4. Selected-tab fill and Foreground behavior remain unchanged.
5. Tooltips still show Device / Profile / Controller / Shortcut / Setting.
6. BumperLeft appears clearly at the rail top.
7. BumperRight appears clearly at the rail bottom.
8. Bumper hints are visually subordinate to the five tabs.
9. Bumper hints do not intercept touch/mouse input.
10. LB still moves to the previous tab.
11. RB still moves to the next tab.
12. Reordering tabs in Setting changes only the five tab positions; the bumper hints stay fixed.
13. Repeated Overlay show/hide preserves the same rail layout.
14. PR672 full-monitor geometry remains unchanged.

---

# 11. Expected implementation scope

Expected production files:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml
~~~

Expected test files:

~~~text
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayQamVisualResourcesTests.cs
~~~

Expected documentation:

~~~text
docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md
~~~

No other production area should normally need changes.

If implementation starts touching controller input dispatch, tab persistence, transport, Runtime, or Full1902 lifecycle code, stop and reduce scope.

---

# 12. Acceptance criteria

The PR is complete when all of the following are true:

1. The five visible tab icons use FontIcon, not SymbolIcon.
2. The tab icon font is explicitly Segoe Fluent Icons.
3. Device uses E945 LightningBolt.
4. Profile uses E71D AllApps.
5. Controller uses E7FC Game.
6. Shortcut uses E75F Dialpad.
7. Setting uses E713 Settings.
8. BumperLeft F10C is shown at the top of the rail.
9. BumperRight F10D is shown at the bottom of the rail.
10. Bumper hints are passive and non-hit-testable.
11. TabStrip stays vertically centered.
12. User tab reorder does not move the bumper hints.
13. Existing LB/RB navigation logic is unchanged.
14. Existing tab accessibility names/tooltips are unchanged.
15. Existing selected/unselected button Foreground/Background ownership is unchanged.
16. Rail width remains 52 DIP.
17. Tab item height remains 64 DIP.
18. Tab icon size remains 24 DIP.
19. Bumper hint size is 16 DIP.
20. No image assets or icon package are added.
21. No icon service/registry/manager abstraction is added.
22. No protocol, Runtime, controller authority, lifecycle, or geometry changes are included.
23. Focused UI tests and the full test suite pass.
24. MSI Claw hardware validation confirms all seven glyphs render correctly.

---

# 13. Implementation principle

Keep one local rail icon mapping and two static rail hints.

~~~text
tab identity
-> GlyphFor(id)
-> FontIcon / Segoe Fluent Icons
-> existing Button visual state

LB/RB behavior
-> existing controller navigation authority

F10C/F10D
-> passive visual guidance only
~~~

Do not turn a five-icon visual change into a generalized icon system.
