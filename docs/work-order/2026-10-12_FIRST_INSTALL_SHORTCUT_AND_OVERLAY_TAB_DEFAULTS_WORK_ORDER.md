# Work Order — First-Install Shortcut Tiles and Default Overlay Tab Order

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
src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsShellContracts.cs
src/SteamInputAddonforClaw/Settings/AppSettings.cs
src/SteamInputAddonforClaw/Settings/SettingsStore.cs
src/SteamInputAddonforClaw/Runtime/AddonRuntimeComposition.cs
src/SteamInputAddonforClaw.Overlay/OverlayTabState.cs
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs
tests/SteamInputAddonforClaw.Tests/ShortcutStoreTests.cs
tests/SteamInputAddonforClaw.Tests/ShortcutRuntimeTests.cs
tests/SteamInputAddonforClaw.Tests/ShortcutEditorRuntimeTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayTabOrderContractTests.cs
tests/SteamInputAddonforClaw.Tests/SettingsStoreTests.cs
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
- `AddonQuickSettingsTabOrderContract.DefaultOrder` in `AddonQuickSettingsShellContracts.cs` currently orders `Device, Profile, Controller, Shortcut, Setting`.
- `AppSettings.AddonQuickSettingsTabOrder` uses that shared default; `SettingsStore.Load()` already saves it when `settings.json` is absent.
- `OverlayTabState.ResetForShow()` already selects `order[0]`.

Therefore: **Shortcut seeding needs a small NotFound-only write. Tab ordering needs only the canonical default-array adjustment plus tests.**

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

Use the existing `ShortcutActionTypeIds` constants in code, not duplicated string literals.

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

## 5. Scope and non-goals

Implement in **one small PR**, limited to:

- first-missing-file Shortcut default document creation and persistence;
- canonical five-tab default order;
- necessary automated test updates.

Do **not**:

- implement first-install detection through installer, Velopack, registry, startup tasks, a database, or a dedicated installation flag;
- migrate, populate, or normalize other users' existing data;
- seed defaults on every Runtime start or whenever a tile list is empty;
- insert duplicate Screenshot actions;
- bump Shortcut action/document schema or frontend/Overlay protocol;
- touch renderer geometry, tile drag/drop, editor layouts, screenshot folder UI, app launching, or external process privilege;
- add additional locks, owners, epochs, services, synchronization primitives, or lifecycle participants;
- require device-in-hand testing by local Codex.

Historical work order `1007_SHORTCUT_3_COLUMN_BUILTIN_ACTIONS_WORK_ORDER.md` explicitly prohibited automatic tile creation for that earlier PR. This **new** work order supersedes only that prior no-auto-seeding instruction; all other action/layout contracts remain unchanged.

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

Run the relevant Shortcut, Settings, Overlay-tab-order and affected contract unit tests, then the project's normal build/test workflow. **Automated code verification is Codex's role; hands-on device validation is the user's post-merge role and is not a PR merge blocker.**

## 7. Completion criteria

- One focused PR containing the implementation and corrected automated tests.
- Clean compile and relevant automated tests.
- No new persisted schema, installation marker, migration, framework, or protocol change.
- No changes to Full1902 controller lifecycle or overlay/input ownership.
- A clean installation opens with the four Shortcut tiles and the new default five-tab order, with Shortcut as the initially selected Overlay tab.
- Existing valid saved Shortcut and tab-order documents continue to be authoritative without modification.
