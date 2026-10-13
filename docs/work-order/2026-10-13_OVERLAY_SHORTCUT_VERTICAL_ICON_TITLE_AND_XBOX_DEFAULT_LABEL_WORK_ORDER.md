# Work Order — Overlay Shortcut Tile Vertical Icon/Title Layout + XBOX Default Label

**Date:** 2026-10-13  
**Status:** Ready for local Codex implementation  
**Repository:** `onehoon/SteamAddonforClaw`  
**Target:** re-read latest `main` before implementation; reviewed baseline `b342a1fccfa4df02717085f809c8759cee13b9d4` (PR #775 squash merge)  
**Implementation:** one small, focused PR  
**Product scope:** standalone Full1902, Windows 11, one interactive Windows session; no CTW integration  

---

## 0. Goals and explicit user decisions

This is a narrowly scoped visual/label correction to the already-shipped Shortcut experience.

1. In the **standalone WinUI 3 Overlay > Shortcut** three-column square-tile grid, put each existing Segoe Fluent icon **above** its title, not inline to the title's left. Center the icon/title stack **both horizontally and vertically within the entire tile**.
2. Increase the Overlay Shortcut tile icon from **16 DIP to 24 DIP**. Keep the current **16-DIP title**, centered, with an **approximately 6-DIP icon/title gap**, a maximum of **two title lines**, and ellipsis if needed.
3. **No status-line reservation for an absent status.** Normal tiles display only icon and title, centered as one group. When an existing nonempty `StatusText` is present (e.g. an unavailable action), continue rendering it below the title with the existing caption style; do not remove the product's existing status/error information or introduce a new always-present placeholder. Let the *actually rendered content* remain centered, with no empty gap/row for a missing status.
4. In the Shortcut feature's **newly generated UI/default titles**, spell **`XBOX`** in all capitals: fresh-install built-in tile name, the Add Shortcut action-kind picker item, and the default title offered when creating a new Xbox app Shortcut. Use `XBOX app` in the Shortcut editor's informational label/summary text where applicable.
5. **Critical user constraint: do not change an existing user's saved Shortcut title `Xbox`.** Titles are user-controlled persisted data. Do not perform migration, backfill, display-time case conversion, forced rename, case-insensitive normalization, or implicit save/rewrite. Existing `Xbox` remains exactly `Xbox` until that user explicitly edits it. This applies equally to any custom capitalization or name.

A referenced screenshot shows a centered standalone large icon on a separate row above a title. Match the *composition*, not its four-column layout, dimensions, external icon/branding assets, or visual theme.

### Expected appearance

```text
117.33 × 117.33 DIP fixed tile

+-------------------+
|                   |
|       [icon]      |   Segoe Fluent Icons / 24 DIP
|                   |
|        XBOX       |   current title style, centered
|                   |
+-------------------+

If a real StatusText exists:
       [icon]
       title
       existing status
(all currently visible text/icon remains centered as a group)
```

This work supersedes only the **horizontal icon/title rendering instruction** and the **`Xbox` default labels** in the historical `2026-10-12_FIRST_INSTALL_SHORTCUT_AND_OVERLAY_TAB_DEFAULTS_WORK_ORDER.md`. All of its Shortcut authority, icons, first-install order, user-editability, and no-migration constraints remain active.

---

## 1. Mandatory source and architecture review

Read the current code and these reference documents before making changes:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md
docs/work-order/1007_SHORTCUT_3_COLUMN_BUILTIN_ACTIONS_WORK_ORDER.md
docs/work-order/2026-10-12_FIRST_INSTALL_SHORTCUT_AND_OVERLAY_TAB_DEFAULTS_WORK_ORDER.md
```

Primary source:

```text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shortcuts.cs
src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs
src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs
src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutFrontendContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutIconCatalog.cs
```

Test owners:

```text
tests/SteamInputAddonforClaw.UiTests/OverlayShortcutRendererTests.cs
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
tests/SteamInputAddonforClaw.Tests/ShortcutRuntimeTests.cs
```

### Reviewed source facts

- `OverlayWindow.Shortcuts.cs` currently creates a horizontal `header` `StackPanel`, then appends the `FontIcon` at 16 DIP and `TextBlock` title side-by-side. The title width is artificially limited to `contentWidth - 22` to account for the inline icon.
- The parent `content` `StackPanel` is already horizontally/vertically centered inside the tile, and adds `StatusText` **only if nonempty**. Preserve that optional-status behavior.
- The Overlay uses `ShortcutVisualColumnCount = 3`, `QamShortcutTileSize = 117.33333333333333`, `QamTilePadding = 12`, and `QamTileSpacing = 8`. Thus the nominal available square content area is about 93.33 DIP wide/high.
- The title receives `QamTileTitleTextStyle` (current inherited 16-DIP body strong text); status receives `QamCaptionTextStyle` (14 DIP).
- `ShortcutRuntime.CreateInitialShortcutDocument()` seeds a fresh `shortcuts.json` with `Screenshot`, `Steam`, `Steam Big Picture`, `Xbox` in that order. It **does not seed an already-existing file**.
- The Main App editor initializes `titleBox.Text = existing?.Title ?? string.Empty`, and `ShouldApplyDefaultTitle(existing is null, ...)` only allows automatic default titles on creation. Keep this existing behavior so saved `Xbox` stays untouched.
- `ShortcutRuntime.GetEditorTargetSummary` currently displays `Xbox app`, and `ShortcutPage.xaml.cs` exposes an `Xbox` picker label and `GetDefaultTitle(XboxApp) = "Xbox"`.
- The Overlay receives `FrontendShortcutDashboardSnapshot`, including already-sanitized `Title`, `StatusText`, and `IconGlyph`. It does **not** own action configuration or title casing.

---

## 2. Overlay tile presentation — exact change

**File:** `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shortcuts.cs`

Change only the visual construction inside `RenderShortcutSnapshot()` for each tile.

### 2.1 Composition

Replace the horizontal `header` with an equivalent simple **vertical icon/title group**; keep the existing outer tile `content` and `Border` owners.

Illustrative shape (adapt directly to current code, not a request for new abstractions):

```csharp
var title = new TextBlock
{
    Text = tile.Title, // exact user-controlled title; no case conversion
    TextWrapping = TextWrapping.Wrap,
    TextTrimming = TextTrimming.CharacterEllipsis,
    MaxLines = 2,
    TextAlignment = TextAlignment.Center,
    HorizontalAlignment = HorizontalAlignment.Center,
    MaxWidth = contentWidth, // no longer subtract inline icon width
};
OverlayQamResources.ApplyTextStyle(title, "QamTileTitleTextStyle");

var header = new StackPanel
{
    Orientation = Orientation.Vertical,
    HorizontalAlignment = HorizontalAlignment.Center,
    VerticalAlignment = VerticalAlignment.Center,
    MaxWidth = contentWidth,
    Spacing = 6,
};
header.Children.Add(new FontIcon
{
    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
    Glyph = tile.IconGlyph, // preserve default and user-selected glyph
    FontSize = 24,
    HorizontalAlignment = HorizontalAlignment.Center,
});
header.Children.Add(title);

var content = new StackPanel
{
    HorizontalAlignment = HorizontalAlignment.Center,
    VerticalAlignment = VerticalAlignment.Center,
    Spacing = string.IsNullOrWhiteSpace(tile.StatusText) ? 0 : 4,
};
content.Children.Add(header);
// Keep the existing conditional StatusText construction and caption style.
```

This keeps one vertically centered group for ordinary tiles, without any reserved empty status region or per-tile title-offset calculations. If `StatusText` exists, it remains a real child below the icon/title group and participates in the same centering. Avoid special layouts, blank placeholder `TextBlock`, or independent top/bottom alignment of the icon and title.

Do not clamp the whole title to one line: `Steam Big Picture` and user-defined two-line titles must be usable. Preserve actual string content; use the existing caption/normal styling and only add a two-line visual cap/ellipsis where needed to prevent overflow from arbitrarily long titles. Keep the actual status text data intact; use ordinary wrapping/bounds behavior and don't introduce a status-specific state machine. If the optional status is long, prioritize avoiding visual overflow within the fixed tile without redesigning tile geometry.

### 2.2 Freeze tile/grid behavior

**Do not change**:

- The 3-column tile layout, index-to-row/column mapping, 117.3333-DIP square width/height, 12-DIP tile padding, 8-DIP gaps, card background/corner radius, disabled opacity, and selected-row styling.
- `TileId`-based pointer execution, `ShortcutExecutionRequested`, `_shortcutExecutionInFlight`, and the controller keyboard/selection navigation path.
- Runtime-owned snapshot, icon catalog and overrides, action kind, persisted title, loading, availability, error/status semantics, Shortcut close-after-launch behavior, or Shortcut screenshot behavior.
- Main App Shortcut **editor grid/card layout**; only its labels are changed in this PR.

Do not introduce a shared tile presenter class, icon service, resource bundle, special glyph mapping, alternate frontend state, protocol update, or any new dependencies.

---

## 3. New default XBOX presentation — no edits to existing users

### 3.1 Fresh-install seed

**File:** `src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs`

Make this one-literal change in `CreateInitialShortcutDocument()`:

```csharp
Tile("XBOX", ShortcutActionTypeIds.XboxApp, true),
```

Keep all other seed titles, action kinds, initial order, `CloseOverlayAfterLaunch`, schema, `IconGlyphOverride = null`, and the existing *missing-file-only* seeding policy unchanged.

**No write, migration, or normalization for an existing `shortcuts.json`.**

### 3.2 Main App Add Shortcut editor

**File:** `src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs`

Use capital `XBOX` in these **user-visible** shortcut labels:

```csharp
AddActionChoice(actionPicker, "XBOX", FrontendShortcutEditorActionKind.XboxApp);

// In the existing GetDefaultTitle(...) switch:
FrontendShortcutEditorActionKind.XboxApp => "XBOX",
```

The existing action description can also read `"Opens the XBOX app."` instead of `"Opens the Xbox app."` for consistent Shortcut UI wording. No action TypeId or behavioral change.

**File:** `src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs`

The current Shortcut editor's *informational target summary* may use:

```csharp
FrontendShortcutEditorActionKind.XboxApp => "XBOX app",
```

This summary is **not** the editable title and must never replace it.

### 3.3 Existing user's `Xbox` title must remain unchanged

Explicit invariants:

- `FrontendShortcutTile.Title` and `FrontendShortcutEditorTile.Title` must always reflect the persisted title verbatim.
- If an existing tile has `Title = "Xbox"`, it continues to show **Xbox** in Overlay and Main App editor; its bytes in `shortcuts.json` are unchanged by upgrade/startup/display.
- Opening **Edit Shortcut** must still prefill **Xbox**; merely choosing the already-selected `XboxApp` action kind must not rewrite that field. Only an explicit user edit followed by Save can rename the tile.
- Any other user-chosen spelling (e.g. `XbOx`, `My Xbox`) must also remain untouched.
- No `ToUpperInvariant()`, `ToUpper()`, `String.Replace("Xbox", "XBOX")`, case-insensitive repair, automatic `Title` recomputation, read-time normalization, or data migration.
- Other uses of `Xbox` in internal identifier/type names, logs, device/launcher plumbing, historical documentation, unrelated features, or test fixtures are **not** part of this terminology change. Do not globally replace strings.

The only effect on an existing user should be the visual orientation/size of the Shortcut icons and, if they open the Add dialog, the new creation defaults/labels.

---

## 4. Automated tests and review expectations

### 4.1 `OverlayShortcutRendererTests.cs`

Update the existing source-level renderer contract tests (do not create a UI-host/screenshot infrastructure):

- `header.Orientation == Vertical` by inspecting the actual renderer source shape.
- `FontIcon` consumes `tile.IconGlyph`, uses `Segoe Fluent Icons` and **24 DIP**, and is added before the title.
- `MaxWidth = contentWidth` on title (not `contentWidth - 22`), centered text, two-line cap and trimming.
- The icon/title content is centered vertically and horizontally; no fixed empty status placeholder.
- Existing optional status is still rendered with `QamCaptionTextStyle`, only on nonempty `StatusText`.
- Keep current assertions for three columns, tile dimensions, pointer/Accept execution, selection, unavailable/disabled, execution guard, and no editor-action payload.

Remove or revise only assertions explicitly coupled to the old horizontal composition.

### 4.2 `UiArchitectureTests.cs`

Update the existing Shortcut editor assertions:

- `AddActionChoice(..., "XBOX", XboxApp)`.
- `GetDefaultTitle(XboxApp) == "XBOX"`.
- Existing `ShouldApplyDefaultTitle(...)` rules still prevent automatic renames when editing.

### 4.3 `ShortcutRuntimeTests.cs`

Update the exact **fresh-install seed** expectation from `Xbox` to `XBOX`.

Add or adapt one focused **existing-user preservation** test using a stored XboxApp tile titled `Xbox`:

1. Create/store an existing Shortcut document with `Title = "Xbox"`, TypeId `XboxApp` and a stable `TileId`.
2. Construct/restart the Runtime using that file.
3. Confirm `Capture().Tiles` and `CaptureEditor().Tiles` both still report `"Xbox"`, unchanged `TileId`, and that the original document bytes remain unchanged (no seed/migration).
4. Leave any unrelated test fixture using `Xbox` for user-supplied labels untouched; only the seed expectation changes.

No actual MSI Claw hardware/manual UI verification is a Codex or PR merge blocker. User performs real-device validation separately after merge. Build, focused UI tests, and relevant runtime tests are the required implementation checks.

### 4.4 Commands (Windows development environment)

Use the repository's existing restore/build/test guidance, for example:

```powershell
dotnet build .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\tests\SteamInputAddonforClaw.UiTests\SteamInputAddonforClaw.UiTests.csproj -c Release --no-restore
dotnet test .\tests\SteamInputAddonforClaw.Tests\SteamInputAddonforClaw.Tests.csproj -c Release --no-restore
git diff --check
```

Report the actual commands/results in the PR; do not claim hardware validation.

---

## 5. Narrow documentation consistency

Update only the **active Shortcut summary/override** in:

```text
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
```

State that Shortcut tiles still use the existing 3-column, 117.33-DIP square grid, but now show a **24-DIP icon centered above a centered title**, with optional status below and the whole visible group centered. Include that new generated XBOX defaults are uppercase while existing persisted titles are never rewritten.

Do **not** rewrite the historical 2026-10-12 work order or unrelated sections of the general architecture.

---

## 6. Proposed PR boundaries and final checklist

**One PR, no follow-on infrastructure PR required.** Expected changed files:

```text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shortcuts.cs
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs
src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs
tests/SteamInputAddonforClaw.UiTests/OverlayShortcutRendererTests.cs
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
tests/SteamInputAddonforClaw.Tests/ShortcutRuntimeTests.cs
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
```

Acceptance:

- [ ] Icon is on its own row **above** the title on every Overlay Shortcut tile.
- [ ] Icon is 24 DIP; title retains its current 16-DIP style; icon/title vertical gap is approximately 6 DIP.
- [ ] The icon and title are one vertically and horizontally centered group.
- [ ] No empty/status line is reserved when the snapshot has no status; an actual status remains visible when present.
- [ ] Titles wrap up to two lines with trimming; use full tile content width.
- [ ] Three fixed columns, square 117.3333-DIP tile, 8-DIP gaps, padding, selection, pointer/controller activation, disabled styling, and Runtime ownership remain unchanged.
- [ ] Missing-file first-install seed creates `XBOX` (not `Xbox`) and keeps all other defaults/positions.
- [ ] Main App Shortcut Add dialog action label/default new title uses `XBOX` and its informational app wording is consistent.
- [ ] Existing stored `Xbox` (and any custom title spelling) remains **byte-for-byte unchanged** unless explicitly edited by the user.
- [ ] No title migration/backfill, display-time uppercase conversion, schema/protocol change, extra state/manager/abstraction, or CTW integration.
- [ ] Appropriate source-level UI and Runtime tests pass; physical validation belongs to the user **after merge**, never a PR blocker.

**Definition of done:** a narrow visual tile composition change plus new-only XBOX default labeling, with existing user titles and runtime/controller behavior fully preserved.
