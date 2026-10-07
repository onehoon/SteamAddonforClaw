# Work Order — Shortcut Overlay 3-Column Square Grid + Built-in Steam/Xbox Actions

**Date:** 2026-10-07  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**Target branch:** main  
**Reviewed main baseline:** 24f98cd973ed42719f6f5784d836bec13765a281  
**Implementation rule:** re-read the latest `main` before coding; the baseline above records this work-order review point, not a branch pin.  
**Feature area:** Main App Shortcut editor + WinUI3 Overlay Shortcut page + Runtime Shortcut built-ins  
**Implementation shape:** one focused PR  

---

# 0. Goal

Polish the current Shortcut experience in one focused change.

The PR has three related product goals:

1. Change the WinUI3 Overlay Shortcut page from the current dynamic one/two-column tile layout to a **stable three-column square-tile layout** sized for the current 452-DIP Overlay surface.
2. Change the Main App Shortcut editor from a one-dimensional vertical list into a **stable three-column layout editor** so users can arrange tiles while seeing the same row-major placement used by the Overlay. One or two Main App cards must keep one-third-row width and must not stretch across the unused columns.
3. Add first-class built-in Shortcut actions for:
   - Steam Big Picture;
   - normal Steam client;
   - Xbox app;
   - and fix the existing Screenshot editor experience so built-in actions receive a useful default title automatically when created.

The intended user experience is:

```text
Main App
  Shortcut
    Add Shortcut
      Application (.exe)
      PowerShell
      Website (URL)
      Steam Big Picture
      Steam
      Xbox
      Screenshot

Selecting a built-in action while creating a Shortcut
  -> automatically supplies its normal title
  -> user may still edit the title
  -> no path / script / URL configuration is required for that built-in
```

Overlay target:

```text
Shortcut

[ Tile ][ Tile ][ Tile ]
   8      8

[ Tile ][ Tile ][ Tile ]
```

The visual grid remains three columns even when fewer than three tiles exist:

```text
1 tile
[ Tile ][      ][      ]

2 tiles
[ Tile ][ Tile ][      ]

3 tiles
[ Tile ][ Tile ][ Tile ]
```

Do not auto-create any of these Shortcut tiles for the user.

This PR only makes them selectable built-in actions.

---

# 1. Mandatory source review before coding

Re-read current `main` before implementation.

## 1.1 Full1902 authority

Read at minimum:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md
```

Product baseline remains:

```text
Standalone Full1902
one Windows user
one interactive session
no Fast User Switching
no RDP / multi-session support
```

CTW integration is not part of the current product.

This Shortcut work must remain independent from:

```text
PID1901 / PID1902 ownership
HidHide
VIIPER
SteamDeck / Xbox360 presentation switching
controller recovery
routing rollback
suspend/resume authority
WING suppression authority
```

Do not add a controller lifecycle participant, owner, gate, epoch, watchdog, manager, or recovery path for Shortcut work.

## 1.2 Shortcut foundation

Read:

```text
docs/work-order/SHORTCUT_FOUNDATION_PR_D_MAIN_APP_SHORTCUT_EDITOR_WORK_ORDER_2026-09-24.md
docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR4_SHORTCUT_PARITY_WORK_ORDER.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
```

Inspect at minimum:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutFrontendContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutEditorFrontendContracts.cs

src/SteamInputAddonforClaw/Shortcuts/ShortcutActionTypeIds.cs
src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs

src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs

src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shortcuts.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
src/SteamInputAddonforClaw.Overlay/OverlayShortcutSelection.cs
src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
```

Also inspect the existing product launch paths before implementing built-ins:

```text
src/SteamInputAddonforClaw/CenterM/Oem1BigPictureLauncher.cs
src/SteamInputAddonforClaw/CenterM/FrontButtonXboxAppLauncher.cs
src/SteamInputAddonforClaw/WindowsGaming/SteamFseConfiguration.cs
```

The PR must reuse the already-established Steam/Xbox product identities and launch semantics rather than inventing another discovery stack.

---

# 2. Reviewed current state

## 2.1 Current Overlay geometry

The current merged Overlay geometry is:

```text
OpaquePanel MaxWidth = 452 DIP
Tab rail            = 52 DIP
right content area  = 400 DIP

QamBodyContentPadding
  left  = 16 DIP
  right = 16 DIP

usable Shortcut width
  = 452 - 52 - 16 - 16
  = 368 DIP
```

Current tile spacing is:

```text
QamTileSpacing = 8 DIP
```

A three-column row therefore has:

```text
368 DIP usable width
- 8 DIP gap
- 8 DIP gap
= 352 DIP tile width total

352 / 3
= 117.333333333... DIP per tile
```

At the reference MSI Claw 1920 x 1200 display at 150% scaling:

```text
117.333333 DIP * 1.5 = 176 physical px
8 DIP * 1.5          = 12 physical px
16 DIP * 1.5         = 24 physical px
```

So the row lands exactly as:

```text
24 px
[176] 12 [176] 12 [176]
24 px
```

This is the target geometry.

## 2.2 Current Shortcut renderer

Current `OverlayWindow.Shortcuts.cs` uses:

```csharp
var columnCount = Math.Min(2, _shortcutSnapshot.Tiles.Count);
```

and creates only as many star columns as are needed.

That causes:

- one tile to consume the full width;
- two tiles to consume half each;
- three or more tiles to use a two-column grid.

This PR replaces that behavior with a stable three-column visual grid.

## 2.3 Current Shortcut tile projection already has status data

The renderer currently displays only:

```text
FrontendShortcutTile.Title
```

but the existing contract already provides:

```text
Title
StatusText
State
Enabled
```

Current Runtime resolutions include normal no-status tiles plus failure/status cases such as:

```text
Unavailable
Not found
Unsupported
Invalid configuration
```

Do not add another status model.

Use the existing projection.

## 2.4 Current editor action types

The current Main App editor supports:

```text
Executable
PowerShell
URL
ScreenshotFullscreen
```

The general URL action intentionally accepts only absolute HTTP/HTTPS URLs.

It rejects arbitrary custom schemes such as Steam protocol URIs.

Keep that security/validation boundary intact.

Do not broaden the generic Website action to arbitrary URI schemes merely to support Steam.

## 2.5 Existing product launch seams

Current product code already has:

```text
Steam Big Picture
  steam://open/bigpicture

Xbox app identity
  PackageIdentityName = Microsoft.GamingApp
  PackageFamilyName   = Microsoft.GamingApp_8wekyb3d8bbwe
  ApplicationId       = Microsoft.Xbox.App
  AUMID               = Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App
```

The existing Xbox front-button action delegates packaged-app activation through the interactive shell:

```text
explorer.exe
  shell:AppsFolder\{XboxGamingHomeAppIdentity.Aumid}
```

Do not duplicate Xbox identity constants.

---

# 3. Overlay Shortcut layout

## 3.1 Fixed three-column visual grid

When at least one Shortcut tile exists, the visual Shortcut grid must always reserve **three columns**.

Do not use:

```csharp
Math.Min(2, tileCount)
```

for visual column construction.

Required visual arrangement:

```text
tile 0 -> row 0 column 0
tile 1 -> row 0 column 1
tile 2 -> row 0 column 2

tile 3 -> row 1 column 0
tile 4 -> row 1 column 1
tile 5 -> row 1 column 2
...
```

One or two tiles must **not** stretch to consume the missing columns.

The visible tile width must remain the same width it would have in a full three-tile row.

## 3.2 Square size

Use a Shortcut-specific tile-size resource equivalent to:

```text
QamShortcutTileSize = 117.33333333333333 DIP
```

The tile must be square:

```text
Width  = QamShortcutTileSize
Height = QamShortcutTileSize
```

The three columns plus two existing 8-DIP gaps exactly fill the current 368-DIP body width.

Keep:

```text
ColumnSpacing = 8 DIP
RowSpacing    = 8 DIP
```

Do not introduce:

- a `SizeChanged`-driven Shortcut layout calculator;
- per-DPI manual physical-pixel sizing;
- a responsive grid manager;
- another layout abstraction;
- special-case widths for one/two tiles.

The current 452-DIP maximum surface and existing body padding are the product geometry used for this polish.

DPI scaling remains WinUI/DIP responsibility.

## 3.3 Top spacing

Do not add a Shortcut-only top-offset calculation.

Current resources already provide:

```text
QamPageTitleMargin = 16,20,16,16
QamBodyContentPadding = 16,0,16,12
```

Therefore the page title's existing 16-DIP bottom margin already supplies the visual top spacing before the Shortcut grid.

Keep the Shortcut grid itself at zero additional top margin.

Do not accidentally create:

```text
PageTitle bottom 16
+ Shortcut top 16
= 32 DIP
```

The desired effective spacing is the existing **16 DIP**, not 32 DIP.

## 3.4 Tile internal content

Replace the current title-only child with a centered content container.

Required behavior:

### Title only

If `StatusText` is null/empty:

```text
┌─────────────────┐
│                 │
│                 │
│      Title      │
│                 │
│                 │
└─────────────────┘
```

The title is horizontally and vertically centered in the square.

Use:

```text
QamTileTitleTextStyle
TextAlignment = Center
HorizontalAlignment = Center
```

### Title + status

If `StatusText` is present:

```text
┌─────────────────┐
│                 │
│      Title      │
│     4 DIP       │
│   StatusText    │
│                 │
└─────────────────┘
```

Use a centered vertical stack:

```text
Title      -> QamTileTitleTextStyle
StatusText -> QamCaptionTextStyle
Spacing    -> 4 DIP
```

The complete title/status group is vertically centered as one unit.

Do not create 16-DIP or 32-DIP internal line spacing.

Keep existing tile padding unless the implementation proves it conflicts with centered layout:

```text
QamTilePadding = 12 DIP
```

Keep existing wrapping so longer user-defined titles remain usable.

## 3.5 Selection / disabled visuals

Preserve the current visual behavior:

```text
normal
  QamTileBrush

selected
  QamTileSelectedBrush

disabled
  existing QamDisabledOpacity
```

Do not invent new Active/Inactive colors in this PR.

The contract has `FrontendShortcutTileState.Active` and `Inactive`, but the current Runtime does not require a new state color system for the requested work.

The existing `StatusText` plus `Enabled` behavior is sufficient.

---

# 4. Overlay controller navigation

The renderer has three visual columns, but `OverlayShortcutSelection.Configure` currently requires:

```text
columnCount <= tileCount
```

Do not weaken that invariant solely so a one-tile grid can claim three logical columns.

Use:

```text
selectionColumnCount = Math.Min(3, tileCount)
```

for controller-navigation selection state.

This gives:

```text
1 tile -> logical selection columns = 1
2 tiles -> logical selection columns = 2
3+     -> logical selection columns = 3
```

For one/two tiles there is only one populated row, so this produces the same useful D-pad behavior while the visual grid still reserves three columns.

For three or more tiles, D-pad geometry must match the new three-column layout.

Preserve:

- TileId-based selection retention across snapshot refresh;
- A-button execution;
- pointer selection/execution;
- disabled tile non-execution;
- `StartBringIntoView` behavior;
- existing execution in-flight guard.

Do not redesign `OverlayShortcutSelection` into a generic grid-navigation framework.

---

# 5. New built-in Shortcut actions

Add three first-class built-in action kinds.

## 5.1 Editor action kinds

Extend:

```csharp
FrontendShortcutEditorActionKind
```

with equivalent values:

```csharp
SteamBigPicture,
SteamClient,
XboxApp,
```

Keep:

```csharp
Executable,
PowerShell,
Url,
ScreenshotFullscreen,
Unsupported
```

Do not replace the persisted extensible string TypeId contract with the enum.

## 5.2 Persisted TypeIds

Add canonical TypeIds equivalent to:

```text
system.steam-big-picture
system.steam-client
system.xbox-app
```

to `ShortcutActionTypeIds`.

All three built-ins use:

```text
SchemaVersion = 1
Parameters = {}
```

They are parameterless product-owned actions.

Do not store:

- Steam install paths;
- Steam protocol strings;
- Xbox AUMIDs;
- shell command strings

inside each tile's `Parameters`.

Those implementation details belong to code, not user Shortcut data.

## 5.3 Validation / repair semantics

For each new built-in:

```text
schemaVersion == 1
+ Parameters == empty JSON object
=> valid
```

Any parameter property under schema 1 is invalid configuration.

Follow the existing Screenshot repair pattern:

```text
known TypeId
+ schema 1
+ malformed/non-empty Parameters
=> project as the known editor kind
=> Editable = true
=> ConfigurationValid = false
=> user can open Edit and save
=> save canonicalizes Parameters to {}
```

Unknown TypeIds and unsupported future schema versions remain preserved and non-editable according to the existing Shortcut foundation contract.

Do not silently rewrite persisted invalid data during load/capture.

---

# 6. Built-in execution semantics

## 6.1 Steam Big Picture

Use the already-established product protocol:

```text
steam://open/bigpicture
```

This is already used by the existing front-button Big Picture action.

The Shortcut built-in should produce the same user-visible result.

Do not route this through the generic HTTP/HTTPS Website action.

## 6.2 Steam client

Use the Steam protocol to request the ordinary Steam client UI:

```text
steam://open/main
```

Treat this as a dedicated built-in protocol action.

Do not require Steam install-path entry from the user.

Do not persist a resolved `steam.exe` path in `shortcuts.json`.

Do not broaden generic URL validation to accept `steam://`.

## 6.3 Xbox app

Use the current canonical Xbox app identity:

```text
XboxGamingHomeAppIdentity.Aumid
```

and the same interactive-shell activation semantics already used by `FrontButtonXboxAppLauncher`:

```text
explorer.exe shell:AppsFolder\{AUMID}
```

Do not introduce another Xbox package/AUMID constant.

Do not add package-discovery polling or a new app activation manager merely for this Shortcut.

## 6.4 Sharing existing launch code

Avoid broad refactoring.

Acceptable implementation shapes are limited to the smallest practical option:

- reuse an existing narrow static launcher where dependency direction remains clean; or
- rename/extract a **narrow product-specific launcher** so the front-button and Shortcut paths can share the same Steam Big Picture or Xbox activation primitive.

Do **not** create:

```text
ExternalAppLauncherService
ShortcutLaunchManager
ShellActivationManager
IApplicationLauncher
generic URI broker
generic privilege broker
```

for these three actions.

A tiny product-specific static launcher is acceptable if sharing is necessary.

## 6.5 Elevated Runtime boundary

The Full1902 Runtime is currently High integrity.

The active Full1902 architecture explicitly leaves the final user-launched external-process privilege policy as separate follow-up work.

Therefore this PR must **not** attempt to solve the general de-elevation problem for:

- arbitrary Shortcut EXE actions;
- PowerShell;
- URL actions;
- front-button LaunchApplication.

Do not add:

- token copying;
- explorer-token duplication;
- unelevated broker processes;
- COM shell brokers;
- services;
- new IPC;
- privilege managers.

For the new built-ins, preserve/reuse the existing product shell/protocol launch semantics.

This PR is a Shortcut product-feature change, not the external-process privilege-boundary redesign.

---

# 7. Main App editor UX

## 7.1 Fixed three-column layout editor

The Main App Shortcut collection must visually represent the same ordered three-column placement used by the Overlay.

Target layout:

```text
[ tile 0 ][ tile 1 ][ tile 2 ]
[ tile 3 ][ tile 4 ][ tile 5 ]
[ tile 6 ][ tile 7 ][ tile 8 ]
```

The Main App cards do **not** need to use the Overlay's 117.333333-DIP square dimensions. The Main App has a different surface and needs room for editor information such as Title, target/action summary, Edit, and Delete.

However, its horizontal layout contract is fixed:

```text
three equal-width columns
row-major order
one/two tiles leave the remaining column slots empty
```

Required behavior:

```text
1 tile
[ Tile ][      ][      ]

2 tiles
[ Tile ][ Tile ][      ]

3 tiles
[ Tile ][ Tile ][ Tile ]

4 tiles
[ Tile ][ Tile ][ Tile ]
[ Tile ][      ][      ]
```

A single card must **not** stretch to the full Shortcut content width.

Two cards must **not** expand to 50% each.

Each card must occupy the same one-third-row slot width it would occupy when three cards are present.

The purpose is WYSIWYG ordering, not pixel-identical rendering:

```text
Main App position 0/1/2
-> Overlay row 0 column 0/1/2

Main App position 3/4/5
-> Overlay row 1 column 0/1/2
```

Preserve the existing ordered collection as the only layout authority.

Do not add Row/Column fields to persisted Shortcut data.

Drag/drop reorder must continue to mutate only list order.

Prefer adapting the existing `ListView` / `ListViewBase` reorder surface to a three-column wrap/grid presentation if the current WinUI control supports the required stable reorder behavior.

Do not replace the editor with a new ItemsRepeater/custom drag framework merely for this layout.

Do not add responsive one/two-column breakpoints for the Shortcut editor in this PR. The editor intentionally remains three columns so it continues to preview Overlay placement.

## 7.2 Add action choices

Add the three built-ins to the current Add/Edit Shortcut action picker.

The picker must expose all current supported actions:

```text
Application (.exe)
PowerShell
Website (URL)
Steam Big Picture
Steam
Xbox
Screenshot
```

Exact ordering may follow the existing editor layout, but keep all built-ins directly selectable without entering raw parameters.

For the new built-ins, the editor body should show only a concise description if useful.

Do not show:

- executable path;
- arguments;
- PowerShell script;
- URL

for a parameterless built-in.

## 7.3 Default titles

When creating a new Shortcut, selecting these built-in actions must provide these defaults:

```text
SteamBigPicture     -> "Steam Big Picture"
SteamClient         -> "Steam"
XboxApp             -> "Xbox"
ScreenshotFullscreen -> "Screenshot"
```

This fixes the current Screenshot behavior as well.

The title remains user-editable.

Examples:

```text
Steam Big Picture -> user may rename to "BPM"
Xbox              -> user may rename to "Game Pass"
Screenshot        -> user may rename to "Capture"
```

Do not make built-in titles immutable.

## 7.4 Creation-only auto-title behavior

Automatic title assignment is a **new-Shortcut creation convenience**.

When editing an existing tile:

- preserve the persisted Title;
- opening the editor must not overwrite it simply because its action picker is initialized;
- no migration should rename old Screenshot tiles or any other existing tile.

During a new Create dialog, selecting a built-in should populate its default title.

Do not change the Runtime mutation contract to make Title optional just for this UX.

`ShortcutRuntime` should continue receiving and persisting the explicit Title supplied by the editor.

Keep the default-title logic narrow and local to the Main App editor.

## 7.5 Built-ins remain editable

Supported built-in schema-1 actions must have:

```text
Editable = true
```

so the user can:

- rename the tile;
- change the action type;
- repair an invalid parameterless built-in definition.

Delete and reorder behavior remains unchanged.

Multiple tiles using the same built-in action are allowed.

Do not add uniqueness enforcement.

---

# 8. Runtime editor projection

Extend the existing projection helpers instead of introducing another editor layer.

Update the existing paths equivalent to:

```text
GetEditorActionKind(...)
ProjectEditorAction(...)
GetEditorTargetSummary(...)
TryBuildAction(...)
Resolve(...)
ExecuteAsync(...)
```

Suggested target summaries:

```text
SteamBigPicture -> "Steam Big Picture"
SteamClient     -> "Steam client"
XboxApp         -> "Xbox app"
Screenshot      -> existing "Fullscreen screenshot"
```

Normal valid built-ins project as:

```text
State      = Neutral
Enabled    = true
StatusText = null
```

unless the current implementation can synchronously prove a real unsupported/invalid configuration using an already-existing narrow check.

Do not add async package probing, polling, caching, or another availability authority merely to decorate Shortcut tiles.

Execution failure continues through the existing `ShortcutExecutionResult` path and existing logging policy.

Do not expose raw exception text, shell command strings, private paths, or internal details in frontend failure messages.

---

# 9. Overlay status rendering

The Overlay must begin consuming the already-existing `FrontendShortcutTile.StatusText`.

This is display-only.

Examples:

```text
Title only:
  Screenshot

Invalid/missing target:
  My Tool
  Not found

Unsupported:
  Legacy Action
  Unsupported
```

Do not make the Overlay inspect:

- `Action.TypeId`;
- action parameters;
- executable path;
- Steam/Xbox action kind.

The Overlay remains a sanitized Runtime projection plus TileId-only execution surface.

No new execution transport is required for built-ins.

---

# 10. Persistence / compatibility

No shortcut-document schema migration is required.

Existing tiles must remain byte-semantically preserved unless the user edits them through the existing mutation path.

Existing supported action TypeIds remain unchanged:

```text
system.executable
system.powershell
system.url
system.screenshot-fullscreen
```

The new TypeIds are additive.

Do not:

- convert existing Steam-like user EXE/URL tiles into the new built-ins;
- rename existing Screenshot tiles;
- auto-add default built-ins to an existing or empty dashboard;
- rewrite the document merely because the app starts with the new version.

Unknown future TypeIds must continue to survive load/save according to the established Shortcut persistence contract.

---

# 11. Tests

Update/add focused tests.

## 11.1 Runtime built-ins

Cover at minimum:

### Steam Big Picture

```text
valid schema-1 {}
-> projected enabled / Neutral / no StatusText
-> execution requests steam://open/bigpicture
```

### Steam client

```text
valid schema-1 {}
-> projected enabled / Neutral / no StatusText
-> execution requests steam://open/main
```

### Xbox

```text
valid schema-1 {}
-> projected enabled / Neutral / no StatusText
-> execution uses the canonical Xbox AUMID / established shell activation seam
```

Use injectable/narrow test seams consistent with the current `ShortcutRuntime` test style.

Do not require a real Steam or Xbox installation in unit tests.

### Invalid built-in parameters

For each built-in, prove:

```text
schema 1 + non-empty/malformed Parameters
-> dashboard disabled
-> StatusText = "Invalid configuration"
-> editor Kind remains the known built-in
-> Editable = true
-> ConfigurationValid = false
-> execution does not launch
```

### Mutation canonicalization

Prove creating/updating each built-in writes:

```text
correct TypeId
SchemaVersion = 1
Parameters = {}
```

and preserves the explicit Title.

## 11.2 Main App editor

Add/update UI architecture tests proving:

- the Shortcut collection uses a stable three-column Main App layout;
- one card occupies only the first one-third-row slot and does not stretch to full width;
- two cards occupy the first two one-third-row slots and do not expand to half width;
- 3/4/5/6 cards preserve the same row-major order used by the Overlay;
- drag/drop reorder still updates the single ordered Shortcut collection rather than introducing row/column persistence;
- the action picker exposes Steam Big Picture, Steam, Xbox, and Screenshot;
- Screenshot now has the creation default title `Screenshot`;
- the three new built-ins have their required creation default titles;
- existing Edit initialization does not overwrite a persisted custom title;
- built-ins do not expose EXE/script/URL configuration controls when selected.

Avoid brittle pixel screenshot tests.

## 11.3 Overlay geometry

Prove the Shortcut renderer contract now uses:

```text
3 visual columns
QamShortcutTileSize = 117.33333333333333 DIP
QamTileSpacing = 8 DIP
square Width/Height
```

and no longer derives visual columns from `Math.Min(2, tileCount)`.

Cover rendering expectations for:

```text
1 tile
2 tiles
3 tiles
4 tiles
6 tiles
```

At minimum prove one/two tiles retain the same tile width as a full three-tile row.

## 11.4 Overlay selection

Update `OverlayShortcutSelectionTests` for three-column navigation.

Cover at minimum:

```text
1 tile
2 tiles
3 tiles
4 tiles
5 tiles
6 tiles
```

Verify:

- left/right remain within the populated row;
- up/down follow three-column geometry once there are 3+ tiles;
- incomplete final rows retain the current bounded/clamped behavior;
- refresh selection is preserved by TileId where possible.

## 11.5 Status text

Add a renderer/wiring test proving:

```text
StatusText == null
-> centered title-only presentation

StatusText != null
-> centered title + status presentation
-> status uses QamCaptionTextStyle
```

Do not add another status DTO solely for testing.

---

# 12. Documentation updates

Update current active documentation that still describes the Shortcut Overlay as an "up to two columns" dynamic grid.

At minimum update:

```text
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
```

The active statement should become equivalent to:

```text
Shortcut Overlay
-> ordered Runtime-projected tiles
-> fixed three-column visual grid
-> square 117.333333-DIP tiles on the current 452-DIP surface
-> 8-DIP horizontal/vertical tile gaps
-> Title centered
-> existing StatusText shown below Title when present
-> TileId-only execution
```

Also document the additive built-in actions and their parameterless schema-1 contract in the appropriate Shortcut section.

Historical work orders remain historical.

Do not rewrite old completed work orders to pretend they originally specified the new behavior.

---

# 13. Explicit non-goals

Do not include any of the following in this PR:

- automatic creation of default Shortcut tiles;
- default dashboard migration;
- icon support for Shortcut tiles;
- Steam artwork/icon extraction;
- Xbox artwork/icon extraction;
- per-tile custom colors;
- new Active/Inactive visual design;
- generic custom URI support;
- widening the Website action beyond HTTP/HTTPS;
- Steam process polling;
- BPM HWND polling;
- Steam install discovery for the new built-in;
- Xbox package polling/caching;
- a generic shell/application launcher service;
- a generic de-elevation broker;
- controller lifecycle changes;
- PID1901/PID1902 changes;
- HidHide changes;
- VIIPER changes;
- WING suppression changes;
- SteamDeck/Xbox360 presentation-policy changes;
- sleep/resume state changes;
- new frontend RPCs for execution.

The existing Overlay execution transport remains:

```text
TileId
-> Runtime ShortcutRuntime
-> resolve current action
-> execute
```

---

# 14. Acceptance criteria

The PR is complete when all of the following are true.

## Overlay

1. Shortcut tiles always use a three-column visual grid.
2. One or two tiles no longer stretch wider than a tile in a full three-tile row.
3. On the current 452-DIP Overlay surface, each tile is 117.333333... DIP square.
4. Horizontal and vertical tile gaps remain 8 DIP.
5. The effective title-to-grid top spacing remains the existing 16 DIP; no duplicated 32-DIP gap is introduced.
6. A title-only tile centers its title both horizontally and vertically.
7. A tile with StatusText centers the title/status group, using 4 DIP between the two text lines.
8. Existing selection fill and disabled opacity remain unchanged.
9. Controller D-pad navigation matches three-column geometry for 3+ tiles.
10. TileId selection preservation and execution behavior remain unchanged.

## Main App layout

11. Main App Shortcut editing uses a stable three-column visual layout.
12. One Shortcut card occupies only one-third of the row and does not stretch across empty columns.
13. Two Shortcut cards occupy two one-third slots and do not expand to half-width cards.
14. Main App row-major card order matches Overlay row-major tile order.
15. Existing reorder semantics continue to persist only collection order.

## Built-in actions

16. Main App Add Shortcut exposes Steam Big Picture.
17. Main App Add Shortcut exposes Steam.
18. Main App Add Shortcut exposes Xbox.
19. Screenshot remains available.
20. Creating each built-in auto-populates:
    - Steam Big Picture -> `Steam Big Picture`
    - Steam -> `Steam`
    - Xbox -> `Xbox`
    - Screenshot -> `Screenshot`
21. The user can edit those titles.
22. Editing an existing Shortcut does not silently replace its persisted title.
23. New built-ins persist as schema-1 parameterless `{}` actions.
24. Steam Big Picture launches through `steam://open/bigpicture`.
25. Steam launches through `steam://open/main`.
26. Xbox activation reuses the canonical `XboxGamingHomeAppIdentity.Aumid` semantics.
27. Generic Website remains HTTP/HTTPS-only.
28. No Shortcut document migration or automatic default-tile creation occurs.
29. No new generic launcher/broker/manager abstraction is introduced.
30. Existing Full1902 controller and power lifecycle behavior is untouched.

---

# 15. Expected implementation footprint

The exact file list may vary after re-reading latest `main`, but the expected footprint is narrow.

Likely production files:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutEditorFrontendContracts.cs

src/SteamInputAddonforClaw/Shortcuts/ShortcutActionTypeIds.cs
src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs

src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs

src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shortcuts.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml
```

Possibly one/two existing narrow launcher files if sharing the current Steam/Xbox activation seams is cleaner than duplication.

Likely tests:

```text
tests/SteamInputAddonforClaw.Tests/ShortcutRuntimeTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayShortcutSelectionTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayQamVisualResourcesTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
```

Documentation:

```text
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
```

Do not expand the file footprint merely to establish new layering.

The intended design remains:

```text
Main UI
  fixed 3-column layout editor
  editor UX + default title convenience
       |
       v
existing typed mutation
       |
       v
ShortcutRuntime
  one persistence/execution authority
       |
       +-- user-configured EXE / PowerShell / HTTP(S)
       +-- Screenshot built-in
       +-- Steam Big Picture built-in
       +-- Steam client built-in
       +-- Xbox app built-in

Overlay
  Runtime snapshot only
  fixed 3-column square visual grid
  TileId-only execution
```

Keep one clear Shortcut authority and one execution path.
