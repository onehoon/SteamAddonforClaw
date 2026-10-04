# Work Order — Overlay Profile Two-Column Catalog + Resolution Detail Alignment

**Date:** 2026-10-04  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**PR base:** main  
**Target branch after merge:** main  
**Reviewed main:** ca8ca56da0539b16f185767a9e341cf739f6962f  
**Feature area:** Standalone WinUI 3 Overlay / Profile tab  
**Implementation shape:** one focused presentation-only PR

---

# 0. Goal

Polish the Overlay Profile tab for the current 432 DIP shell by making three narrow, user-visible presentation changes:

~~~text
1. Profile game catalog
   3 columns -> 2 columns

2. Profile catalog game title
   16 DIP shared tile title -> 15 DIP only for Profile catalog titles

3. Selected Profile detail / Resolution section
   keep the section heading at the normal left edge
   indent the Resolution value row with the same existing detail indent
   used by other nested Profile controls
~~~

Target catalog shape:

~~~text
Profile

[ Cyberpunk 2077 ]   [ Baldur's Gate 3 ]
[ Ghost of Tsushima ] [ The Witcher 3 ]
~~~

Target Resolution hierarchy:

~~~text
Resolution
    Resolution    < 1920 × 1200 >
~~~

The second `Resolution` line is the actionable row. It should align with the nested/detail rows used by other Profile feature sections.

This PR is presentation/navigation geometry only.

Do not include the separate ClawHUD focus-preservation work in this PR.

---

# 1. Mandatory project review before coding

Read the current versions before implementation.

## 1.1 Full1902 authority

At minimum:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
~~~

This PR must not change any Full1902 controller or device authority.

Keep unchanged:

~~~text
PID1901 <-> PID1902 ownership
HidHide ownership / recovery
VIIPER ownership / teardown
physical DirectInput ownership
Xbox360 <-> SteamDeck presentation switching
Overlay capture / release
Sleep / Hibernate / Resume
Restart / Crash / Shutdown recovery
PnP re-enumeration
routing rollback / fail-close
~~~

The Overlay remains a presentation surface.

## 1.2 Relevant Overlay/Profile documents

Read:

~~~text
docs/work-order/OVERLAY_PROFILE_CATALOG_AND_SHARED_PROFILE_PARITY_WORK_ORDER_2026-09-24.md
docs/work-order/OVERLAY_QAM_PAGE_POLISH_PR_C_WORK_ORDER_2026-10-02.md
docs/work-order/OVERLAY_QAM_COMPACT_DENSITY_FIXED_HEADER_MINIMAL_TEXT_WORK_ORDER_2026-10-04.md
docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
~~~

This work order intentionally supersedes the old Profile three-column freeze.

The earlier three-column choice was explicitly provisional / deferred to hardware acceptance. The current product decision is now:

~~~text
Profile catalog = 2 columns
Shortcut grid = unchanged
~~~

Do not infer any broader grid redesign.

---

# 2. Current implementation facts

## 2.1 Current shell width

Current Overlay shell:

~~~text
total width = 432 DIP
rail width = 52 DIP
body left/right padding = 16 DIP
~~~

The Profile catalog currently still creates three equal star columns.

In:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs
~~~

current construction is effectively:

~~~csharp
for (var i = 0; i < 3; i++)
    _profileCatalogGrid.ColumnDefinitions.Add(
        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
~~~

and card placement is also hard-coded to three columns:

~~~csharp
var rowCount = (_profileCatalog.Count + 2) / 3;

Grid.SetRow(card, index / 3);
Grid.SetColumn(card, index % 3);
~~~

This produces unnecessarily narrow game-title cards in the current compact Overlay.

## 2.2 Controller navigation is also hard-coded to three columns

In:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayProfileCatalogSelection.cs
~~~

current state is:

~~~csharp
private const int Columns = 3;
~~~

The visual grid and logical controller navigation must change together.

Do not change one without the other.

## 2.3 Profile title currently inherits the shared 16 DIP tile title

Current Profile catalog title:

~~~csharp
OverlayQamResources.ApplyTextStyle(title, "QamTileTitleTextStyle");
~~~

`QamTileTitleTextStyle` inherits the shared 16 DIP body typography.

That shared resource is also part of the common QAM visual authority.

The requested reduction is Profile-catalog-specific.

Do not globally reduce `QamTileTitleTextStyle`.

## 2.4 Resolution uses a different generic-renderer shape

The shared Profile projection currently publishes Resolution as:

~~~text
section label = "Resolution"
first/only visible row = discrete Slider row "Resolution"
~~~

Unlike TDP/FPS/etc., Resolution does not start with a feature-header Toggle.

Therefore in:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
~~~

`BuildQuickSettingsSection(...)` resolves:

~~~text
usesFeatureHeader = false
~~~

and places the Resolution row directly into the normal `rowStack`.

Feature-header sections instead place their child controls inside:

~~~csharp
Margin = OverlayQamResources.Get(
    "QamDetailIndent",
    new Thickness(16, 0, 0, 0))
~~~

That is why the actionable Resolution row visually starts farther left than the other nested Profile controls.

This is a renderer-layout mismatch, not a Profile data-contract defect.

Do not change `QuickSettingsPresentation.BuildProfileResolutionSection(...)` merely to fix this visual alignment.

---

# 3. Scope

Production files expected to change:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs
src/SteamInputAddonforClaw.Overlay/OverlayProfileCatalogSelection.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
~~~

Tests expected to change:

~~~text
tests/SteamInputAddonforClaw.UiTests/OverlayProfileCatalogSelectionTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
~~~

Documentation expected to change:

~~~text
docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md
~~~

No Runtime, Contracts, FrontendTransport, profile persistence, or Full1902 lifecycle production file should need modification.

---

# 4. Change A — Profile catalog becomes two columns

In `BuildProfilePage()`, create exactly two equal star columns.

Required conceptual result:

~~~csharp
for (var i = 0; i < 2; i++)
{
    _profileCatalogGrid.ColumnDefinitions.Add(
        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
}
~~~

In `RebuildProfileCatalogCards()`, update the row calculation and card coordinates consistently.

Required shape:

~~~csharp
var rowCount = (_profileCatalog.Count + 1) / 2;

Grid.SetRow(card, index / 2);
Grid.SetColumn(card, index % 2);
~~~

Do not introduce a generic adaptive-grid helper, ItemsRepeater migration, GridView migration, or responsive-column manager.

The product requirement is a fixed two-column Overlay Profile catalog.

Keep unchanged:

~~~text
Favorite-first ordering
then Name
then AppId
two-line title wrapping
CharacterEllipsis
A activation
pointer click
BringIntoView
catalog/detail transitions
active-game override behavior
~~~

---

# 5. Change B — logical Profile catalog navigation becomes two columns

Update:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayProfileCatalogSelection.cs
~~~

from three-column ownership to two-column ownership.

At minimum:

~~~csharp
private const int Columns = 2;
~~~

Also update the class summary/comment so it no longer claims to be a three-column selection model.

Do not add another navigation class.

Do not move Profile catalog navigation into the generic `OverlayRowSelection`.

`OverlayProfileCatalogSelection` remains the one local authority for Profile catalog 2D selection.

The existing incomplete-last-row behavior remains valid:

~~~text
full rows use two columns
last row may contain one card
vertical navigation may clamp to the last available item
navigation must never produce an out-of-range index
~~~

---

# 6. Change C — Profile catalog game title is 15 DIP only

Keep using the existing shared style for family, weight, line height baseline, foreground, and other typography.

Then override only `FontSize` on the Profile catalog title.

Preferred minimal implementation:

~~~csharp
OverlayQamResources.ApplyTextStyle(title, "QamTileTitleTextStyle");
title.FontSize = 15;
~~~

Equivalent object-initializer ordering is acceptable if the final value is unambiguously 15 DIP after the shared style is applied.

Do not change:

~~~text
QamBodyTextStyle
QamBodyStrongTextStyle
QamTileTitleTextStyle
Shortcut tile typography
Device typography
Controller typography
Setting typography
~~~

Keep:

~~~csharp
TextWrapping = TextWrapping.Wrap;
TextTrimming = TextTrimming.CharacterEllipsis;
MaxLines = 2;
~~~

Do not add a new typography service or Profile tile control.

A new global/shared font-size token is not required for this one local adjustment.

---

# 7. Change D — indent only the Profile Resolution actionable row

Use the existing `QamDetailIndent` resource.

Do not hard-code a second 16-DIP value when the existing detail-indent resource already owns this spacing.

The section heading itself stays at the normal section left edge.

Only the row stack under the Profile Resolution heading receives the detail indent.

A minimal implementation in `BuildQuickSettingsSection(...)` is preferred.

Conceptual example:

~~~csharp
var rowStack = new StackPanel
{
    Spacing = OverlayQamResources.Get("QamRowSpacing", 0.0),
};

...

if (!usesFeatureHeader &&
    surface.PageId == QuickSettingsPageId.Profile &&
    section.SectionId == QuickSettingsSectionId.ProfileResolution)
{
    rowStack.Margin = OverlayQamResources.Get(
        "QamDetailIndent",
        new Thickness(16, 0, 0, 0));
}
~~~

The exact placement inside the method may vary, but preserve these rules:

~~~text
Profile + ProfileResolution only
section heading remains unindented
actionable Resolution row receives existing QamDetailIndent
Device sections are unchanged
other Profile sections are unchanged
~~~

Do not alter the shared frontend shape solely for layout.

Specifically, do not:

~~~text
add a fake Resolution Toggle
add a renderer-only field to QuickSettingsSection
add a new indentation property to frontend contracts
duplicate the Resolution section
rename the Resolution row
create a Profile-only Quick Settings renderer
~~~

This is one narrow presentation exception in the existing generic renderer.

---

# 8. Do not change Profile product semantics

Keep the existing shared Profile product exactly as-is:

~~~text
Profile Enabled
TDP
Intel FPS Limit
CPU Boost
Windows Power Mode
Resolution
~~~

Keep current Resolution options and order:

~~~text
Do not change
1920 × 1200
1920 × 1080
1680 × 1050
1440 × 900
~~~

Do not change:

~~~text
mutation intent
commit policy
profile persistence
selected offline profile behavior
active running-game behavior
QuickSettingsPageSnapshot contracts
QuickSettingsSectionId
QuickSettingsRowId
~~~

---

# 9. Tests — two-column selection

Update:

~~~text
tests/SteamInputAddonforClaw.UiTests/OverlayProfileCatalogSelectionTests.cs
~~~

Rename the existing three-column test to describe the two-column model.

Cover at least:

~~~text
Reset(5) starts at index 0

row 0:
    0 1

row 1:
    2 3

row 2:
    4

0 -> Right -> 1
1 -> Right -> blocked
1 -> Down -> 3
3 -> Down -> 4
4 -> Down -> blocked
4 -> Up -> 2
2 -> Left/right remain bounded by the two-column row rules

empty catalog remains -1 and non-navigable
~~~

Exact traversal assertions may differ if they preserve the current incomplete-row clamp semantics, but they must prove two-column geometry and bounds.

Do not weaken bounds checking.

---

# 10. Tests — Profile catalog presentation

Extend the existing source/composition regression coverage in:

~~~text
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
~~~

Prove the Profile catalog now uses two columns.

Prefer assertions over current source structure rather than introducing UI-hosted screenshot tests.

Also prove the title remains:

~~~text
entry.Name
Wrap
CharacterEllipsis
MaxLines = 2
FontSize = 15
QamTileTitleTextStyle still applied
~~~

The 15 DIP override must be local to `OverlayWindow.Profile.cs`.

Do not modify the shared QAM typography test to globally expect 15 DIP.

---

# 11. Tests — Resolution indent

Add a focused renderer-wiring regression proving the current generic renderer applies `QamDetailIndent` to:

~~~text
QuickSettingsPageId.Profile
+
QuickSettingsSectionId.ProfileResolution
~~~

and does not replace the shared renderer.

The test should also protect the existing feature-header detail path.

At minimum verify that:

~~~text
QamDetailIndent is still used for feature-header child rows
ProfileResolution receives the same resource
the condition is scoped to ProfileResolution
~~~

Do not add XAML-hosted focus tests for this PR.

---

# 12. Visual reference update

Update:

~~~text
docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md
~~~

The current document still says Profile three-column density is deferred to hardware acceptance.

Replace/update that stale statement with the current product decision:

~~~text
- The Overlay Profile catalog now uses two columns in the 432 DIP shell.
- This is an Addon-specific hardware/UI decision, not a claim that Steam QAM has an equivalent game-card layout.
- Profile catalog titles use a local 15 DIP font-size override while retaining the shared QAM tile-title family/weight/color.
- Shortcut remains maximum two columns and is unchanged.
~~~

Do not rewrite historical measured Steam evidence.

Keep measured vs inferred/product-specific choices clearly separated.

---

# 13. Explicit non-goals

Not in this PR:

~~~text
ClawHUD focus preservation
ClawHUD native WinUI focus behavior
QamToggleStyle changes
QamValueButtonStyle focus changes

Profile cover art
game icons
Favorite editing
search
manual refresh
source labels
new card metadata

Shortcut layout changes
Shortcut typography changes

Overlay width changes
rail width changes
outer gap changes
DPI logic changes
scroll architecture changes

Runtime changes
FrontendTransport changes
Quick Settings contract changes
Profile persistence changes
Full1902 controller/lifecycle changes
~~~

Do not opportunistically combine any of those items.

---

# 14. Overengineering guardrails

Desired implementation shape:

~~~text
OverlayWindow.Profile.cs
    3 -> 2 visual columns
    3-column arithmetic -> 2-column arithmetic
    local title FontSize = 15

OverlayProfileCatalogSelection.cs
    Columns = 2

OverlayWindow.QuickSettings.cs
    one narrow ProfileResolution detail-indent condition

existing tests
    update frozen geometry assertions
~~~

Do not add:

~~~text
AdaptiveProfileGrid
ProfileLayoutManager
responsive breakpoint state
generic grid-navigation framework
ProfileTile control
Profile-specific Quick Settings renderer
new frontend layout metadata
new view model
new state authority
~~~

The goal is not to redesign the Profile architecture.

The goal is to correct the current compact Overlay presentation with the smallest coherent change.

---

# 15. Build / test validation

Run at minimum:

~~~text
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Debug --no-restore

dotnet test -c Debug --no-restore

dotnet build -c Debug --no-restore

git diff --check
~~~

Use the repository's current required restore/build sequence if a clean environment requires restore first.

Do not remove or weaken unrelated tests to make this PR pass.

---

# 16. Manual acceptance

Validate on the supported MSI Claw Overlay target, especially 1920 × 1200 at 150% scaling.

## Catalog

~~~text
Profile tab opens catalog with no active game
exactly two game cards per complete row
cards are visibly wider than the old three-column layout
long names are easier to read
game title is slightly smaller than before, not dramatically reduced
title still wraps to at most two lines
ellipsis still works
no horizontal clipping
~~~

Controller navigation:

~~~text
Left/Right follows two-column rows
Up/Down follows two-column rows
incomplete final row remains bounded
selected card remains brought into view
A opens the selected Profile
B returns according to existing Profile behavior
~~~

## Resolution

Open a game Profile detail.

Confirm:

~~~text
Resolution section heading remains aligned with other section headings
the actionable Resolution row is inset to match other nested Profile controls
the row is not double-indented
value selector remains usable
controller selection/highlight remains correct
pointer operation remains correct
~~~

Also confirm Device Quick Settings did not move.

---

# 17. Definition of done

Complete when:

~~~text
Profile catalog
    2 columns

Profile controller selection
    same 2-column geometry
    bounded incomplete last row

Profile catalog title
    QamTileTitleTextStyle retained
    FontSize = 15 only in Profile catalog
    Wrap + CharacterEllipsis + MaxLines=2 retained

Profile Resolution
    section heading unchanged
    actionable row uses QamDetailIndent
    no shared contract change

Shortcut
    unchanged

ClawHUD focus work
    not included

Full1902 lifecycle / controller ownership
    unchanged

UI tests + repository tests + build
    pass
~~~
