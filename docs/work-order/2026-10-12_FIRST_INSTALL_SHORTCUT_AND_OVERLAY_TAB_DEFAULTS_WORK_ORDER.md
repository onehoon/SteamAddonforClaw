# Work Order — First-Install Shortcut Tiles, Editable Fluent Glyph Icons, and Default Overlay Tab Order

**Date:** 2026-10-12  
**Status:** Ready for local Codex implementation  
**Repository:** `onehoon/SteamAddonforClaw`  
**Target:** latest `main` (re-read before coding)  
**Implementation:** one focused PR  
**Product:** Standalone Full1902; one Windows user / one interactive session  

---

## 0. Objective

Make a **new installation with no persisted user data** start with four ready-to-use Shortcut tiles in the exact order:

```text
Screenshot | Steam | Steam Big Picture
Xbox       |       |
```

Collection order is authoritative:

1. `Screenshot`
2. `Steam`
3. `Steam Big Picture`
4. `Xbox`

Also change the initial five-tab Overlay order to:

```text
Shortcut -> Device -> Controller -> Setting -> Profile
```

The first Overlay tab on every Show is already the first item in the saved tab order, so a fresh install should open on **Shortcut**.

**Additional feature in this same PR:** Give every supported Shortcut action a default Segoe Fluent Icons glyph, allow users to choose a different glyph in the existing Add/Edit Shortcut dialog, and render the glyph **to the left of its title** in the Overlay. Built-in defaults must be derived from the action TypeId, not the title, tile GUID, or initial seed. Deleting and re-adding a built-in therefore restores its automatic icon.

This is a **default-data change**, not a migration or a new installation lifecycle framework. The owner explicitly ruled out existing-user migration/backfill considerations. Do not introduce an install marker, migration version, registry key, bootstrap coordinator, new settings authority, or backfill pass.

## 1. Mandatory reference review

Read the current code, not only this work order.

Architecture boundaries:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md
docs/work-order/SHORTCUT_FOUNDATION_PR_B_RUNTIME_PERSISTENCE_WORK_ORDER_2026-09-24.md
docs/work-order/1007_SHORTCUT_3_COLUMN_BUILTIN_ACTIONS_WORK_ORDER.md
docs/work-order/1007_SHORTCUT_CLOSE_OVERLAY_AFTER_LAUNCH_WORK_ORDER.md
docs/work-order/1008_SHORTCUT_SCREENSHOT_ACTION_OWNED_FOLDER_WORK_ORDER.md
```

Source-of-truth paths:

```text
src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs
src/SteamInputAddonforClaw/Shortcuts/ShortcutStore.cs
src/SteamInputAddonforClaw/Shortcuts/ShortcutDocument.cs
src/SteamInputAddonforClaw/Shortcuts/ShortcutActionTypeIds.cs
src/SteamInputAddonforClaw.Contracts/Shortcuts/ShortcutContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutFrontendContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutEditorFrontendContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsShellContracts.cs
src/SteamInputAddonforClaw/Settings/AppSettings.cs
src/SteamInputAddonforClaw/Settings/SettingsStore.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw.Overlay/OverlayTabState.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shortcuts.cs
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs
tests/SteamInputAddonforClaw.Tests/ShortcutStoreTests.cs
tests/SteamInputAddonforClaw.Tests/ShortcutRuntimeTests.cs
tests/SteamInputAddonforClaw.Tests/ShortcutEditorRuntimeTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayTabOrderContractTests.cs
tests/SteamInputAddonforClaw.Tests/SettingsStoreTests.cs
tests/SteamInputAddonforClaw.Tests/ShortcutFoundationContractTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayShortcutTransportTests.cs
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
```

Existing Full1902 physical-input, HidHide, VIIPER, SteamDeck/Xbox360 presentation, WING suppression, sleep/resume, shutdown, and recovery logic are **out of scope**. No CTW integration.

## 2. Verified current implementation

As reviewed on 2026-10-12:

- `ShortcutStore.Load()` returns `ShortcutLoadStatus.NotFound` and a new empty `ShortcutDocument` when `shortcuts.json` does not exist; it deliberately does **not** write a file during Load.
- `ShortcutRuntime` currently accepts `Loaded` and `NotFound` as usable, but on `NotFound` retains that empty document in memory. This is the narrow initialization seam for the new four-tile default.
- `ShortcutStore.Save()` already validates a `ShortcutDocument` and writes via `shortcuts.json.tmp` followed by replacement. Reuse it; no new persistence or JSON schema.
- `ShortcutDocument.CurrentSchemaVersion` is **2**. `ShortcutActionSpec.SchemaVersion` for the supported built-ins is **1**. These are intentionally different layers.
- The Shortcut tile collection has ordered entries, each with a fresh `TileId`, `Title`, `CloseOverlayAfterLaunch`, and `Action`. No per-tile row/column/slot fields.
- Existing built-in TypeIds are in `ShortcutActionTypeIds` and already have execution implementations.
- Currently `ShortcutTileDefinition` and both Shortcut frontend projections carry **no icon**; `OverlayWindow.Shortcuts.cs` renders only Title and optional StatusText in a three-column, 117.33-DIP square grid. This PR must add one small optional persisted glyph override and additive frontend projection/mutation fields.
- `AddonQuickSettingsTabOrderContract.DefaultOrder` in `AddonQuickSettingsShellContracts.cs` currently orders `Device, Profile, Controller, Shortcut, Setting`.
- `AppSettings.AddonQuickSettingsTabOrder` uses that shared default; `SettingsStore.Load()` already saves it when `settings.json` is absent.
- `OverlayTabState.ResetForShow()` already selects `order[0]`.

Therefore: **Shortcut seeding needs a small NotFound-only write; tab ordering needs only its canonical default-array adjustment; icon editing needs an optional persisted override, a shared default/catalog definition, additive frontend fields, and narrow Main App/Overlay UI changes.**

## 3. First-install Shortcut seed

### 3.1 Trigger and persistence

Seed exactly once in the existing Runtime-owned Shortcut initialization path **only when** `ShortcutStore.Load().Status == ShortcutLoadStatus.NotFound`.

For that path:

1. Build one `ShortcutDocument` with schema version 2 and the four ordered tiles below.
2. Generate a non-empty unique `Guid` for each `TileId`.
3. Persist using the existing `ShortcutStore.Save()` before exposing the seeded document as normal available Runtime state.
4. Hold the successfully saved document as the `ShortcutRuntime` authoritative in-memory document.
5. On next launch, `Load()` returns `Loaded`, and **must not** seed again or overwrite user changes.

**Do not change `ShortcutStore.Load()`'s meaning**; its `NotFound` result can stay empty/read-only so the store remains a general persistence primitive. Put seeding at the existing Runtime composition/initialization seam; prefer a small private helper over a new service, manager, or document migration layer.

When `Load()` returns `Loaded`, use the saved document unchanged, including a deliberately empty `dashboard.tiles` collection. Likewise do not rewrite malformed, unsupported-schema, or unreadable files. The existing unavailable/preserve-original behavior remains intact.

### 3.2 Exact default tiles

| Index | Display title | Persisted TypeId | Action schema | Parameters | CloseOverlayAfterLaunch |
| ---: | --- | --- | ---: | --- | --- |
| 0 | `Screenshot` | `system.screenshot-fullscreen` | 1 | `{}` | `false` |
| 1 | `Steam` | `system.steam-client` | 1 | `{}` | `true` |
| 2 | `Steam Big Picture` | `system.steam-big-picture` | 1 | `{}` | `true` |
| 3 | `Xbox` | `system.xbox-app` | 1 | `{}` | `true` |

Use the existing `ShortcutActionTypeIds` constants in code, not duplicated string literals. **Do not persist a glyph override on any of these four seeded tiles.** The default is calculated from the action kind every time; see Section 4A. This is why a deleted and re-added Steam/Big Picture/Xbox/Screenshot Shortcut automatically regains the intended icon.

The Screenshot action's empty parameters mean its existing default save location (**Pictures/Screenshots**) is used. Do not add a global Screenshot-folder preference, hard-code a folder per install, alter the screenshot capture pipeline, or change its existing overlay-retirement behavior.

The other three tiles remain existing parameterless built-ins. Do not persist Steam paths, Steam URIs, Xbox AUMIDs, resolved process paths, or shell commands in their action parameters. They continue to use the established launchers.

### 3.3 Illustrative implementation shape

Adapt to the latest production code and naming. The example is a design sketch, **not** a required extra abstraction:

```csharp
private static ShortcutDocument CreateInitialShortcutDocument()
{
    var emptyParameters = JsonSerializer.SerializeToElement(new { });
    ShortcutTileDefinition Tile(string title, string typeId, bool closeOverlay) =>
        new(
            Guid.NewGuid(),
            title,
            closeOverlay,
            new ShortcutActionSpec(typeId, 1, emptyParameters));

    return new ShortcutDocument
    {
        Dashboard = new ShortcutDashboardDefinition(
        [
            Tile("Screenshot", ShortcutActionTypeIds.ScreenshotFullscreen, false),
            Tile("Steam", ShortcutActionTypeIds.SteamClient, true),
            Tile("Steam Big Picture", ShortcutActionTypeIds.SteamBigPicture, true),
            Tile("Xbox", ShortcutActionTypeIds.XboxApp, true),
        ])
    };
}
```

Integrate conceptually after the existing `store.Load()`:

```csharp
if (load.Status == ShortcutLoadStatus.NotFound)
{
    var defaults = CreateInitialShortcutDocument();
    _store.Save(defaults); // Existing validation and atomic writer
    _document = defaults;
}
```

The production implementation must handle normal save failure locally. A failed initial write must be logged and **must not crash the overall Full1902 Runtime or claim a persisted seed succeeded**. Retain the existing Shortcut-only unavailable/failure boundary if initialization cannot be saved, rather than introducing a retry loop or presenting unsaved defaults as durable user configuration. Use normal filesystem exception handling already established by `ShortcutStore`; do not add generalized crash-recovery machinery.

No new file marker. The successfully persisted `shortcuts.json` itself is the initialization record.

## 4. Canonical Overlay tab default

Change **one** canonical default array:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsShellContracts.cs
AddonQuickSettingsTabOrderContract.Default
```

Target code:

```csharp
private static readonly AddonQuickSettingsTabId[] Default =
[
    AddonQuickSettingsTabId.Shortcut,
    AddonQuickSettingsTabId.Device,
    AddonQuickSettingsTabId.Controller,
    AddonQuickSettingsTabId.Setting,
    AddonQuickSettingsTabId.Profile,
];
```

Do not reorder the `AddonQuickSettingsTabId` enum values: tab identities and transport contracts stay unchanged. Do not hardcode a second default in the Overlay process, its shell, Settings page, or Main App. The existing `AppSettings` default, `SettingsStore` initial save, runtime authoritative transport, tab-order editor, and Overlay Show selection must continue to derive from the canonical order.

Do not change `TryNormalize`, `NormalizeOrDefault`, the move/reorder APIs, or settings serialization semantics.

The saved `settings.json` is the durable authority after initial creation. No forced reorder during normal startup.


## 4A. Editable Segoe Fluent Icons (included in this PR)

### 4A.1 Product behavior and glyph authority

Use the built-in Windows 11 `Segoe Fluent Icons` font via WinUI 3 `FontIcon`. **This feature is font-glyph-only**: no PNG/SVG/ICO/JPEG upload, EXE icon extraction, image cache, shell icon lookup, dynamic downloadable icon fonts, or external icon packages.

For all seven currently supported action kinds, supply a useful default glyph. **These four built-in mappings are fixed for this PR** and come from Microsoft's documented Segoe Fluent Icons codepoints:

| Action kind | Default label | Segoe Fluent glyph | Codepoint |
| --- | --- | --- | --- |
| ScreenshotFullscreen | Screenshot | Camera | `U+E722` |
| SteamClient | Steam | Game | `U+E7FC` |
| SteamBigPicture | Steam Big Picture | TVMonitor | `U+E7F4` |
| XboxApp | Xbox | XboxOneConsole | `U+E990` |

Also define sensible initial mappings for the other supported action kinds:

| Action kind | Glyph | Codepoint |
| --- | --- | --- |
| Executable | AllApps | `U+E71D` |
| PowerShell | CommandPrompt | `U+E756` |
| Url | Globe | `U+E774` |

Microsoft's reference: https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-fluent-icons-font

These are **generic Fluent glyphs, not official Steam/Xbox brand artwork**. Use a single authoritative catalog/default map rather than hand-maintaining different glyphs in Runtime, Main App, and Overlay. A narrow static shared-contract catalog is sufficient; do not introduce an icon manager, provider interface, renderer service, asset registry, or independent icon persistence authority.

The effective display glyph is:

~~~csharp
string effectiveGlyph = tile.IconGlyphOverride
    ?? ShortcutIconCatalog.GetDefaultGlyph(actionKind);
~~~

Resolve `actionKind` using the Runtime's **existing** TypeId-to-editor-kind mapping, or equivalently resolve directly from the known TypeId. Do **not** use `Title` or `TileId`. The same default catalog must be used when the user switches Action Type in the Add/Edit dialog, without an extra Runtime RPC.

Unknown/unsupported future action TypeIds can display the generic AllApps fallback; keep their existing unsupported execution/editability rules.

### 4A.2 Persistence contract

Append an **optional, nullable, backward-compatible** member to the existing tile record, rather than putting presentation preferences into `Action.Parameters`:

~~~csharp
public sealed record ShortcutTileDefinition(
    Guid TileId,
    string Title,
    [property: JsonRequired] bool CloseOverlayAfterLaunch,
    ShortcutActionSpec Action,
    string? IconGlyphOverride = null);
~~~

Use the positional form only if it fits the current source; preserve existing call sites and JSON naming conventions (`iconGlyphOverride`). `null` means **automatic Action-Type default**. Any explicitly selected value is one supported Segoe Fluent Icons glyph encoded as a **single-character string** (e.g. `"\uE722"`). Keep glyph choice separate from action execution and target payloads.

- No override on a first-install seed; all four initial defaults are computed from TypeId.
- On any new Shortcut, initially `IconGlyphOverride = null`.
- Deleting one Shortcut deletes its saved override with the tile.
- Re-adding the same built-in creates a new tile with null override and shows its default icon again.
- Rename/reorder/persist/reload must preserve explicit overrides.
- If the Action Type changes and an override exists, keep the explicit override until the user chooses **Use default**; if no override exists, recompute the new action's default immediately.
- An existing schema-2 document with no `iconGlyphOverride` must deserialize normally and display automatic defaults; **do not backfill or rewrite it just to add icon fields**.
- Do **not** bump `ShortcutDocument.CurrentSchemaVersion = 2` or action schema 1 for this optional additive member. Do not add a new JSON file.
- Existing unknown action parameters remain opaque. An unfamiliar/invalid persisted override must **not** make the whole Shortcut document unreadable: project a known generic/default glyph and preserve the stored value unless an explicit edit replaces it. Distinguish invalid editor input (reject) from reading forward-compatible persisted data (preserve).

The Runtime remains the sole Shortcut document authority; `ShortcutStore` retains its existing validation and atomic-write path.

### 4A.3 Runtime, frontend, and mutation flow

Extend the **existing** frontend data contracts using optional trailing members, as needed to minimize incidental call-site changes:

- `FrontendShortcutTile`: one `IconGlyph` (effective display glyph). This is a sanitized rendering value, **not** the action TypeId or action payload.
- `FrontendShortcutEditorTile`: stored `IconGlyphOverride` plus effective display glyph (or equivalent data sufficient to show preview and default status).
- `FrontendShortcutMutationIntent`: nullable `IconGlyphOverride` accepted for Create and Update, with `null` meaning **Use default**. No new mutation kind.
- Use the current `ShortcutRuntime.Capture()` / `ProjectEditorSnapshot()` projection, `TryBuildMutation` Create/Update, and current save-then-publish behavior.
- Delete and Move semantics are unchanged. Any added icon field must not create an alternate way to change an icon on Delete/Move.
- Validate newly selected glyphs against the application's finite named picker catalog in the Runtime mutation boundary; do not trust an arbitrary string from frontend IPC.
- A stored override must be exactly a recognized catalog glyph to become effective. For unexpected stored content, fall back visually as above without discarding it or affecting execution.
- The Frontend transport is already typed/serialized. Extend existing DTOs only. **Do not add new pipe endpoints, transport messages, RPC methods, or dedicated icon-loader operations.**

Optional-field serialization is sufficient; do not introduce separate document migrations, hidden edit caches, or redundant icon state owners.

### 4A.4 Main App icon editor

In the existing `ShortcutPage.xaml.cs` Add/Edit Shortcut `ContentDialog`, add an **Icon** row between Title and Action Type (or directly alongside Title), showing:

- a live `FontIcon` preview and the chosen glyph's friendly name;
- a **Change icon** command which opens a simple WinUI 3 selection dialog or dialog-owned flyout;
- a grid of **named, selectable glyphs** from the same shared catalog, with at least a useful curated set of **24–40 common choices** covering screenshot, apps, game, monitor/TV, Xbox, globe, terminal, folders, settings, media, power, etc.;
- a **Use default** choice that clears the override to `null` and displays the action-specific default immediately;
- readable icon labels/tooltips/automation names, so users are not selecting unexplained PUA numbers.

The user does **not** need a raw Unicode textbox or a generated browser for the thousands of PUA entries. A curated catalog is intentional for this release: one set of vetted glyph codepoints, no missing-glyph placeholders, and no dependency on a dynamically parsed Microsoft web page. Expand the static catalog later without changing the saved format. Do not expose a numeric glyph range text input or permit arbitrary codepoints through the mutation API.

Selecting a new glyph only stages the value inside the existing editor dialog. Persist on the existing **Save** action; **Cancel** must not write anything. The existing editor's Create/Update path, current action validations, Screenshot single-instance rule, and execution/overlay-close behavior remain intact.

**Update the Main App Shortcut card preview** to display the effective glyph next to the existing title/summary without redesigning the three-column layout or pointer reorder interactions. The existing drag handle remains a separate control and must not be mistaken for the selected Shortcut icon.

### 4A.5 Overlay rendering/layout

In `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shortcuts.cs`, replace the title-only inner visual with a horizontally centered row:

~~~text
[ FontIcon ] [ Shortcut Title ]
       [ optional status ]
~~~

Illustrative implementation shape:

~~~csharp
var header = new StackPanel
{
    Orientation = Orientation.Horizontal,
    HorizontalAlignment = HorizontalAlignment.Center,
    VerticalAlignment = VerticalAlignment.Center,
    Spacing = 6
};
header.Children.Add(new FontIcon
{
    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
    Glyph = tile.IconGlyph,
    FontSize = 16
});
header.Children.Add(title);
content.Children.Add(header);
~~~

Adapt to existing layout and namespaces. Preserve the **existing three-column square grid**, 117.33-DIP tile resource, TileId selection, controller navigation, tap/execute, focus/disabled styling, and optional StatusText. Long titles must still wrap or trim within the fixed tile without making a row wider; fit Icon + Title together, do not silently change tile size or break selection visuals.

**No image files, image access over IPC, runtime image lookups, or additional startup work** are required.

### 4A.6 Icon acceptance tests

Include focused, **automated** tests:

1. Each of the four named built-in action TypeIds projects the **exact** default codepoints `E722`, `E7FC`, `E7F4`, `E990`. Also test the three other supported action defaults.
2. First-install seeds contain **no explicit icon overrides** and their projected defaults are correct.
3. A built-in removed and re-created through the existing editor mutation path regains its action-kind default, even with a different title and TileId.
4. A selected override survives Save, Load, Runtime restart, projection, renaming, and reorder.
5. `Use default` writes a null override and immediately resolves the correct action-specific default; Action Type switching with no override changes the preview/default.
6. Old valid schema-2 Shortcut JSON without the optional property still loads, does not trigger migration, and is not rewritten on load.
7. Malformed mutation glyph input is rejected without saving; a future/unknown persisted glyph does not corrupt storage or disable action execution.
8. The Main App picker previews the selected glyph, shows labeled choices, obeys Save/Cancel semantics, and leaves existing drag/reorder interactions intact.
9. Overlay projection/rendering contains the icon + title row with its existing three-column, enabled/disabled, status, and selection behavior unchanged.

Use the established existing test projects and seams, including the UI architecture tests. Do not add a device hardware test requirement or a separate integration test harness.

## 5. Scope and non-goals

Implement in **one focused PR** containing these three related outcomes:

- first-missing-file Shortcut default document creation and persistence;
- canonical five-tab default order;
- action-based Segoe Fluent Icons defaults **and** user glyph selection/editing, with one nullable saved override per Shortcut;
- necessary automated test updates.

Do **not**:

- implement first-install detection through installer, Velopack, registry, startup tasks, a database, or a dedicated installation flag;
- migrate, populate, or normalize other users' existing data;
- seed defaults on every Runtime start or whenever a tile list is empty;
- insert duplicate Screenshot actions;
- bump Shortcut action/document **schema versions**, introduce new RPC/pipe endpoints, or add a separate settings domain (the optional field and additive existing frontend DTO members are explicitly in scope);
- redesign renderer geometry, tile drag/drop, screenshot folder UI, app launching, or external process privilege (the icon picker and icon+title presentation changes in Section 4A are explicitly in scope);
- use icons extracted from EXEs, custom image files, external icon packs, or an arbitrary PUA-range browser;
- add additional locks, owners, epochs, services, synchronization primitives, or lifecycle participants;
- require device-in-hand testing by local Codex.

Historical work order `1007_SHORTCUT_3_COLUMN_BUILTIN_ACTIONS_WORK_ORDER.md` explicitly prohibited automatic tile creation **and icon support** for that earlier PR. This **new** work order supersedes those two feature exclusions only; all other action/layout and safety contracts remain unchanged.

## 6. Automated acceptance tests

Update/extend tests as necessary. Existing tests that deliberately expected an empty Runtime dashboard on missing `shortcuts.json` must be changed to expect the four seeded defaults. Keep the lower-level `ShortcutStore.Load()` absent-file test: it should still return `NotFound` without modifying the filesystem.

### Shortcut seeding

Verify:

1. Absent `shortcuts.json` -> Runtime initialization creates it using the existing Store.
2. The file reloads as schema 2 and has exactly four tiles in the requested order, with correct titles, TypeIds, action schema 1, empty-object parameters, and close-after-launch flags.
3. Every seeded `TileId` is unique and nonempty.
4. Runtime capture and editor projection show the same four tiles and order.
5. Re-initializing Runtime with the persisted file preserves IDs and order; no duplicate tiles are appended.
6. An existing valid document, including `dashboard.tiles = []`, is not seeded or overwritten.
7. Malformed, unsupported-schema, and unreadable documents retain existing safe-unavailable behavior and original bytes; no auto-repair.
8. A filesystem write failure during first initialization does not terminate the surrounding Runtime and does not advertise a successfully persisted default.
9. The existing one-Screenshot limit, default Screenshot folder, and built-in execution flags remain valid.

Review other tests that construct `ShortcutRuntime` with an absent file; if their intended fixture is an *empty pre-existing dashboard*, explicitly save `new ShortcutDocument()` before creating the Runtime. Do not weaken production initialization to preserve obsolete test expectations.

### Overlay order

Verify:

1. The canonical default is exactly `Shortcut, Device, Controller, Setting, Profile`, five unique known identities.
2. Caller mutation of the returned default array cannot mutate the canonical default.
3. Fresh `SettingsStore.Load()` creates `settings.json` with the new order.
4. Reloading the newly saved settings retains the order.
5. Existing saved custom tab order is still honored (no forced reset).
6. Overlay initial/reset-for-Show selection resolves to the first tab, now `Shortcut`.
7. `AddonQuickSettingsTabOrderContract.TryNormalize`, shell projection, and user reorder remain otherwise unchanged.

Run the relevant Shortcut, glyph catalog/mutation, frontend transport, Settings, Overlay-tab-order, and UI architecture automated tests, then the project's normal build/test workflow. **Automated code verification is Codex's role; hands-on device validation is the user's post-merge role and is not a PR merge blocker.**

## 7. Completion criteria

- One focused PR containing the implementation and corrected automated tests.
- Clean compile and relevant automated tests.
- Backward-compatible **optional** `iconGlyphOverride` on existing schema-2 Shortcut tiles; no schema-version bump, installation marker, migration, framework, or new RPC endpoint.
- No changes to Full1902 controller lifecycle or overlay/input ownership.
- A clean installation opens with the four Shortcut tiles and the new default five-tab order, with Shortcut as the initially selected Overlay tab.
- The initial four Shortcut icons resolve from action types: Camera, Game, TVMonitor, XboxOneConsole. Deleting and re-adding each built-in restores its default glyph.
- A user can select another curated Segoe Fluent Icons glyph in Add/Edit, preview it, save it across restarts, and reset it to the action default.
- Main App cards and Overlay tiles show the effective icon **before** the title without changing the established three-column layout or input behavior.
- Existing valid saved Shortcut and tab-order documents continue to be authoritative without modification or load-time backfill.
