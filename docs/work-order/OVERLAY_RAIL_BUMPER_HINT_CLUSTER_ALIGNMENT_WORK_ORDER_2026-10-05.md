# Work Order — Overlay Rail Bumper Hint Cluster Alignment

**Date:** 2026-10-05  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**Target branch:** main  
**Reviewed main baseline:** f5403d09ab3f854518f0f842d5f46f704f97a30b  
**Feature area:** Standalone WinUI 3 Overlay / left rail visual layout  
**Implementation shape:** one small visual-only PR

---

# 0. Goal

Move the existing passive LB/RB bumper hints from the physical top/bottom edges of the Overlay rail to the immediate top/bottom of the five-tab icon cluster.

Current PR674 result:

~~~text
LB                         <- near Overlay top edge



      Device
      Profile
      Controller
      Shortcut
      Setting



RB                         <- near Overlay bottom edge
~~~

Target:

~~~text
        LB
        ↓  small gap
      Device
      Profile
    Controller
     Shortcut
      Setting
        ↑  small gap
        RB
~~~

The complete LB + five tabs + RB visual group is vertically centered in the rail.

LB/RB remain passive guidance. They are not real tabs.

---

# 1. Mandatory review

Before implementation, read the current versions of:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/work-order/OVERLAY_QAM_SHELL_FOUNDATION_PR1_WORK_ORDER_2026-10-02.md
docs/work-order/OVERLAY_FLUENT_RAIL_ICONS_AND_BUMPER_HINTS_WORK_ORDER_2026-10-05.md
docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md
~~~

This PR must not alter Full1902 controller authority, lifecycle, routing, or presentation ownership.

---

# 2. Current source fact

Current OverlayWindow.xaml effectively does:

~~~xml
<Border x:Name="TabRail">
    <Grid>
        <FontIcon
            x:Name="PreviousTabBumperHint"
            VerticalAlignment="Top"
            Margin="{StaticResource QamRailHintMargin}" />

        <Grid
            x:Name="TabStrip"
            VerticalAlignment="Center" />

        <FontIcon
            x:Name="NextTabBumperHint"
            VerticalAlignment="Bottom"
            Margin="{StaticResource QamRailHintMargin}" />
    </Grid>
</Border>
~~~

Because the Grid fills the rail height:

~~~text
PreviousTabBumperHint -> full rail top
TabStrip              -> full rail center
NextTabBumperHint     -> full rail bottom
~~~

That is the only presentation behavior to remove.

Current relevant resources:

~~~text
QamRailButtonSize   = 52
QamRailItemHeight   = 64
QamRailIconSize     = 24
QamRailHintIconSize = 16
QamRailHintMargin   = 0,12,0,12
QamRailSpacing      = 0
~~~

---

# 3. Required layout

Use one centered visual cluster containing, in order:

~~~text
PreviousTabBumperHint
TabStrip
NextTabBumperHint
~~~

Preferred minimal XAML:

~~~xml
<Border
    x:Name="TabRail"
    Grid.Column="0"
    Background="{StaticResource QamRailBrush}"
    CornerRadius="{StaticResource QamRailCornerRadius}">

    <StackPanel
        x:Name="TabRailCluster"
        VerticalAlignment="Center"
        HorizontalAlignment="Stretch"
        Spacing="{StaticResource QamRailHintGap}">

        <FontIcon
            x:Name="PreviousTabBumperHint"
            Glyph="&#xF10C;"
            FontFamily="Segoe Fluent Icons"
            FontSize="{StaticResource QamRailHintIconSize}"
            Foreground="{StaticResource QamDisabledTextBrush}"
            HorizontalAlignment="Center"
            IsHitTestVisible="False"
            AutomationProperties.Name="LB, previous tab" />

        <Grid
            x:Name="TabStrip"
            RowSpacing="{StaticResource QamRailSpacing}" />

        <FontIcon
            x:Name="NextTabBumperHint"
            Glyph="&#xF10D;"
            FontFamily="Segoe Fluent Icons"
            FontSize="{StaticResource QamRailHintIconSize}"
            Foreground="{StaticResource QamDisabledTextBrush}"
            HorizontalAlignment="Center"
            IsHitTestVisible="False"
            AutomationProperties.Name="RB, next tab" />

    </StackPanel>
</Border>
~~~

Equivalent minimal XAML is acceptable.

Keep the ownership model:

~~~text
centered rail cluster
├─ passive LB hint
├─ reorderable five-tab TabStrip
└─ passive RB hint
~~~

Do not move bumper hints inside TabStrip.

---

# 4. Spacing policy

Replace the old edge-oriented resource:

~~~text
QamRailHintMargin = 0,12,0,12
~~~

with one cluster-spacing resource:

~~~text
QamRailHintGap = 6
~~~

Use the gap only between:

~~~text
LB hint <-> TabStrip
TabStrip <-> RB hint
~~~

Do not change spacing between the five actual tabs.

Delete QamRailHintMargin once it has no remaining owner. Do not retain two competing spacing models.

---

# 5. LB/RB must not become real tabs

Do not implement LB/RB as TabStrip rows or tab hosts.

They remain outside:

~~~text
_tabButtons
_tabHosts
_tabPages
_pageRows
OverlayTabState.Order
Runtime tab-order publication
Setting tab-order editor
selected-tab state
row-selection state
~~~

Do not add:

~~~text
position + 1
reserved tab indices
synthetic tab IDs
non-selectable AddonQuickSettingsTabId values
placeholder pages
special-case reorder offsets
~~~

Existing controller behavior remains unchanged:

~~~text
LB -> previous tab
RB -> next tab
~~~

The icons only explain that behavior.

---

# 6. Preserve PR674 icon contract

Do not change:

~~~text
Device     = E945  LightningBolt
Profile    = E71D  AllApps
Controller = E7FC  Game
Shortcut   = E75F  Dialpad
Setting    = E713  Settings

LB hint    = F10C  BumperLeft
RB hint    = F10D  BumperRight
~~~

Keep:

~~~text
tab FontFamily  = Segoe Fluent Icons
hint FontFamily = Segoe Fluent Icons
tab icon size   = 24 DIP
hint icon size  = 16 DIP
hint foreground = QamDisabledTextBrush
~~~

Keep current tab button foreground/background selection ownership unchanged.

---

# 7. Preserve geometry and behavior

Do not change:

~~~text
rail width        = 52 DIP
tab item height   = 64 DIP
tab icon size     = 24 DIP
tab row spacing   = 0
Overlay max width = 432 DIP
PR672 monitor geometry
~~~

Do not change:

- tab order;
- tab persistence;
- tab click behavior;
- LB/RB input dispatch;
- page selection;
- row selection;
- Overlay capture;
- transport;
- Runtime;
- controller routing;
- Overlay show/hide.

The only user-visible change is the location of the two passive bumper hints.

---

# 8. Tests

Update the existing shell test in:

~~~text
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
~~~

Remove assertions that require:

~~~text
PreviousTabBumperHint VerticalAlignment = Top
NextTabBumperHint VerticalAlignment = Bottom
~~~

Replace them with assertions proving:

1. TabRail contains one vertically centered cluster container.
2. The cluster uses QamRailHintGap.
3. The cluster contains PreviousTabBumperHint, TabStrip, NextTabBumperHint in that order.
4. Both hints remain siblings of TabStrip.
5. Neither hint is a child of TabStrip.
6. Both remain IsHitTestVisible=False.
7. F10C/F10D and Segoe Fluent Icons remain unchanged.
8. Grid.SetRow(tabHost, position) remains unchanged.
9. No position + 1 or reserved-row offset is introduced for real tabs.

Update:

~~~text
tests/SteamInputAddonforClaw.UiTests/OverlayQamVisualResourcesTests.cs
~~~

Replace:

~~~text
QamRailHintMargin = 0,12,0,12
~~~

with:

~~~text
QamRailHintGap = 6
~~~

Also assert QamRailHintMargin no longer exists if unused.

No new test framework is required.

---

# 9. Documentation

Update:

~~~text
docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md
~~~

Clarify the active presentation:

~~~text
centered rail cluster
-> BumperLeft
-> 6 DIP gap
-> five reorderable tab icons
-> 6 DIP gap
-> BumperRight
~~~

Keep the existing glyph table.

Do not rewrite the historical PR674 work order.

---

# 10. Manual hardware validation

Primary target:

~~~text
MSI Claw
1920 x 1200
150% / 144 DPI
~~~

Validate:

1. LB appears directly above the first tab with a small gap.
2. RB appears directly below the last tab with the same gap.
3. The whole LB + five tabs + RB cluster is vertically centered.
4. LB/RB are no longer near the physical Overlay top/bottom edges.
5. The five tab icons retain their existing 64-DIP item spacing.
6. Reordering tabs moves only the five actual tabs.
7. LB always stays above the tab group.
8. RB always stays below the tab group.
9. LB/RB remain muted and non-interactive.
10. Existing controller LB/RB navigation still works.
11. Selected-tab fill/foreground remains unchanged.
12. Repeated show/hide does not shift the cluster.
13. PR672 full-monitor geometry is unchanged.

---

# 11. Expected file scope

Expected production changes:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml
~~~

Expected tests:

~~~text
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayQamVisualResourcesTests.cs
~~~

Expected documentation:

~~~text
docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md
~~~

OverlayWindow.Shell.cs should normally require no production change.

If implementation starts modifying tab-order or controller-navigation logic, stop and reduce scope.

---

# 12. Acceptance criteria

The PR is complete when:

1. LB/RB no longer align to the full rail top/bottom.
2. LB, TabStrip, and RB form one centered visual cluster.
3. LB is immediately above TabStrip with a 6-DIP gap.
4. RB is immediately below TabStrip with a 6-DIP gap.
5. LB/RB remain outside dynamic TabStrip.
6. LB/RB remain passive and non-hit-testable.
7. No synthetic tab IDs or reserved tab positions are added.
8. Runtime-authoritative five-tab order is unchanged.
9. Grid.SetRow(tabHost, position) remains unchanged.
10. Five tab glyphs remain unchanged.
11. F10C/F10D remain unchanged.
12. Rail width remains 52 DIP.
13. Tab item height remains 64 DIP.
14. Tab icon size remains 24 DIP.
15. Hint icon size remains 16 DIP.
16. QamRailHintMargin is removed if unused.
17. QamRailHintGap = 6 is the single hint-to-tab spacing authority.
18. No controller input, Runtime, protocol, lifecycle, routing, or Overlay geometry change is included.
19. Focused UI tests and full tests pass.
20. MSI Claw hardware validation confirms the centered cluster is visually clear.

---

# 13. Implementation principle

This is a layout correction, not a navigation-model change.

~~~text
visual cluster
    -> XAML

real tab identity/order
    -> existing TabStrip + Runtime authority

LB/RB navigation
    -> existing controller input path

F10C/F10D
    -> passive visual guidance only
~~~

Do not make passive hints part of navigation state merely to make them look aligned.
