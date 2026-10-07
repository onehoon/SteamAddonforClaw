# Work Order — Screenshot Shortcut Owns Its Save Folder

**Date:** 2026-10-08  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**Target branch:** main  
**Reviewed main baseline:** 5f2bf97af77c36fa4b66f42c71c27152d0c61a2a  
**Implementation rule:** re-read the latest main before coding; this SHA records only the review point.  
**Feature area:** Main App Shortcut editor + Runtime Shortcut persistence/execution + frontend transport cleanup  
**Implementation shape:** one focused PR

---

# 0. Goal

Retire the current global Screenshot save-folder preference.

The Screenshot folder must belong to the Screenshot Shortcut action itself and be persisted in `shortcuts.json`.

Target product model:

```text
Screenshot Shortcut
  Title
  Action = Screenshot
  Save folder
    Default -> Windows Pictures\Screenshots
    Custom  -> one fully-qualified folder
```

The normal editor supports at most one Screenshot Shortcut.

Remove the large standalone Screenshot folder card from the bottom of the Main App Shortcut page.

This is a data-ownership correction, not only a visual relocation.

---

# 1. Mandatory review before coding

Read current `main` and at minimum:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md

docs/work-order/SHORTCUT_FOUNDATION_PR_C2_NIRCMD_FULLSCREEN_SCREENSHOT_ACTION_WORK_ORDER_2026-09-24.md
docs/work-order/SHORTCUT_FOUNDATION_PR_D_MAIN_APP_SHORTCUT_EDITOR_WORK_ORDER_2026-09-24.md
docs/work-order/SHORTCUT_FOUNDATION_PR_E_DYNAMIC_OVERLAY_SHORTCUT_GRID_WORK_ORDER_2026-09-24.md
docs/work-order/1007_SHORTCUT_3_COLUMN_BUILTIN_ACTIONS_WORK_ORDER.md
docs/work-order/1007_SHORTCUT_CLOSE_OVERLAY_AFTER_LAUNCH_WORK_ORDER.md
docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
```

Inspect the current Shortcut contracts, `ShortcutRuntime`, `ShortcutStore`, `NirCmdScreenshotCapture`, frontend transport, `InProcessAddonFrontendControl`, `AddonProcessHost`, settings classes, and `ShortcutPage` XAML/code-behind.

Full1902 remains standalone and this work must not change PID1901/PID1902 ownership, HidHide, VIIPER, controller recovery, suspend/resume, routing rollback, WING suppression, or presentation ownership.

Do not add a new authority, manager, epoch, gate, watchdog, or state machine for this work.

---

# 2. Supersession

For Screenshot folder ownership only, this work order supersedes the conflicting parts of historical PR-C2, PR-D, and PR-E.

Retired rules:

```text
Screenshot folder = global AppSettings preference
StartupSettingsCoordinator = active Screenshot folder owner
ShortcutPage = separate global Screenshot folder card
Screenshot schema 1 requires Parameters = {}
SetScreenshotSaveFolderAsync = separate frontend mutation
```

New authority:

```text
ShortcutRuntime / ShortcutStore
  -> Screenshot action Parameters
  -> shortcuts.json
```

Historical work orders remain historical records; add a supersession note rather than rewriting their bodies.

---

# 3. Locked product rules

## 3.1 One Screenshot Shortcut

Supported normal configuration:

```text
0 or 1 Screenshot Shortcut
```

When another Screenshot already exists:

- Add Shortcut must not offer Screenshot as another action.
- Editing a non-Screenshot tile must not offer Screenshot.
- Editing the existing Screenshot tile keeps Screenshot available.
- Runtime mutation must independently reject a second Screenshot action.

Do not add a generic unique-action framework.

The product is still pre-release, so no compatibility or migration behavior is required for prior local Shortcut/settings data. Existing developer/test data may be reset manually if needed.

## 3.2 Folder ownership

Folder controls live only inside the Screenshot action panel of the existing Add/Edit dialog.

Required controls:

```text
Save folder
<effective path>

[Browse...] [Use default] [Open folder]
```

Do not label the folder `Global`, `Shared`, or `Applies to all shortcuts`.

## 3.3 Dialog semantics

Folder selection is staged with the rest of the Shortcut edit:

```text
Browse       -> change dialog-local staged folder
Use default  -> staged configured folder = null
Cancel       -> discard staged folder change
Save         -> MutateShortcutAsync persists complete action
```

Do not perform an immediate settings mutation from Browse or Use default.

## 3.4 Existing Screenshot close behavior

Keep `CloseOverlayAfterLaunch` forced false for Screenshot.

Screenshot already retires Overlay through its dedicated safe capture path.

Do not change that lifecycle.

---

# 4. Screenshot action schema

Keep:

```text
TypeId        system.screenshot-fullscreen
SchemaVersion 1
```

Do not bump the shared action schema version.

Canonical schema-1 `Parameters`:

Default:

```json
{}
```

Custom:

```json
{
  "folder": "D:\\Screenshots"
}
```

Rules:

- null/blank editor input canonicalizes to `{}`;
- custom folder must be a nonblank fully-qualified path;
- custom folder must fit the existing frontend field-size budget;
- custom form contains exactly one property named `folder`;
- unknown extra properties are invalid under schema 1;
- do not persist `folder: null` as the normal default form.

Existing Screenshot actions with `{}` remain valid and mean Windows `Pictures\Screenshots`.

Use one narrow Screenshot parser inside `ShortcutRuntime`, equivalent to:

```csharp
private static bool TryReadScreenshotParameters(
    JsonElement parameters,
    out string? folder)
```

Do not move Screenshot action validation into `SettingsStore` and do not introduce a generic action schema framework.

---

# 5. Frontend editor contract

Extend `FrontendShortcutActionInput` with a Screenshot-specific configured folder field, equivalent to:

```csharp
string? ScreenshotFolder = null
```

For Screenshot, EXE/PowerShell/URL fields must be null.

For every other action, `ScreenshotFolder` must be null.

Extend `FrontendShortcutEditorAction` with the configured Screenshot folder so the edit dialog can populate the saved value.

Do not send raw `JsonElement` or raw `ShortcutActionSpec` to the Main UI.

The effective default path can be displayed from the current Windows user environment when the configured value is null.

Remove the global editor folder types and field:

```text
FrontendScreenshotFolderSnapshot
FrontendScreenshotFolderMutationResult
FrontendShortcutEditorSnapshot.ScreenshotFolder
```

---

# 6. ShortcutRuntime changes

`ShortcutRuntime` remains the one edit and persistence owner.

Update `TryBuildAction` so Screenshot input canonicalizes to `{}` or `{"folder":"..."}`.

Update `ProjectEditorAction`, `Resolve`, and `ExecuteScreenshotAsync` to use the same Screenshot parameter parser instead of `HasEmptyObjectParameters`.

Runtime mutation must enforce one Screenshot:

```text
Create Screenshot
+ any existing Screenshot
-> reject

Update tile to Screenshot
+ another Screenshot with a different TileId
-> reject
```

A simple message is enough:

```text
Only one Screenshot Shortcut can be added.
```

Keep generic `ShortcutDefinitionValidation` generic. Do not teach it internal action TypeIds solely for this uniqueness rule.

Existing save-then-publish semantics remain mandatory:

```text
build candidate
validate
ShortcutStore.Save(candidate)
success -> _document = candidate
failure -> current _document unchanged
```

---

# 7. Main App UI changes

Remove the standalone Screenshot `Border`/card and its unused layout row from `ShortcutPage.xaml`.

Delete page-level Screenshot path/mode controls and the handlers whose only purpose was global folder mutation.

Move Browse, Use default, and Open folder into the Screenshot action panel created by `ShowEditorAsync`.

Keep the current Windows App SDK `FolderPicker` pattern:

```csharp
new Microsoft.Windows.Storage.Pickers.FolderPicker(windowId)
```

Do not add a picker wrapper/service.

Open folder uses the staged effective folder.

Build the dialog action choices from the current authoritative editor snapshot:

- no Screenshot exists -> offer Screenshot;
- editing the existing Screenshot -> offer Screenshot;
- another Screenshot exists -> omit Screenshot for other tiles/new tile.

Backend rejection remains authoritative.

---

# 8. Retire the global frontend RPC

Remove:

```text
IAddonFrontendControl.SetScreenshotSaveFolderAsync
FrontendRpcMethod.SetScreenshotSaveFolder
SetScreenshotSaveFolderRequest
NamedPipe client/server routing for SetScreenshotSaveFolder
InProcessAddonFrontendControl.SetScreenshotSaveFolderAsync
CaptureScreenshotFolderSnapshot
```

Shortcut editing uses only:

```text
CaptureShortcutEditorAsync
MutateShortcutAsync
```

The current frontend protocol baseline is 67.

Bump to 68 because serialized Shortcut editor contracts change and one RPC is removed.

Protocol comment:

```text
Version 68: Screenshot save-folder ownership moves into Screenshot Shortcut
action parameters. The global Screenshot-folder snapshot/mutation RPC is
retired and the editor action/input contracts carry the configured folder.
A v67 peer must fail the handshake before using the changed contract.
```

No compatibility shim is required.

---

# 9. Screenshot execution

Overlay remains TileId-only.

Do not send Screenshot folder or raw Parameters through Overlay transport.

Execution remains:

```text
Overlay -> TileId
ShortcutRuntime -> current tile -> parse Screenshot folder
ShortcutRuntime -> Host screenshot callback(folder, token)
```

Change the screenshot callback seam from:

```csharp
Func<CancellationToken, Task<ShortcutExecutionResult>>
```

to the smallest equivalent carrying the already-resolved folder:

```csharp
Func<string?, CancellationToken, Task<ShortcutExecutionResult>>
```

Change `AddonProcessHost` screenshot orchestration equivalently:

```csharp
ExecuteFullscreenScreenshotShortcutAsync(
    string? saveFolder,
    CancellationToken cancellationToken)
```

Preserve all current shutdown, `_visibleSurfaceTransition`, Overlay retirement, consumed-button release, and capture ordering.

Only replace `_runtimeStartupSettings.ScreenshotSaveFolder` with the action-owned folder supplied by `ShortcutRuntime`.

`NirCmdScreenshotCapture.ResolveFolder(null)` remains the default-path authority.

---

# 10. Remove ScreenshotSaveFolder from AppSettings without migration

The product is pre-release and no deployed user data compatibility is required.

Remove the old Screenshot folder authority directly:

```text
AppSettings.ScreenshotSaveFolder
StartupSettingsCoordinator.ScreenshotSaveFolder
StartupSettingsCoordinator.ChangeScreenshotSaveFolder
SettingsStore ScreenshotSaveFolder read/validation helpers that become unused
```

New `settings.json` writes must not contain `ScreenshotSaveFolder`.

Do not implement:

```text
legacy setting import
one-time migration
compatibility fallback
old-settings retention
old-to-new copy logic
settings/shortcuts cross-file transaction
```

If stale developer/test `settings.json` still contains `ScreenshotSaveFolder`, the normal settings loader may simply ignore the unknown property after the active field/parser is removed.

If stale developer/test `shortcuts.json` contains the old Screenshot `{}` action, it remains valid because `{}` is still the canonical default-folder representation.

Do not disturb unrelated settings.

---

# 11. Failure policy

Invalid custom path:

```text
mutation rejected
Succeeded=false
Changed=false
current Runtime document unchanged
```

Do not silently fall back to default.

`shortcuts.json` save failure keeps the current Runtime document unchanged.

Screenshot capture directory/write failure keeps the existing bounded `NirCmdScreenshotCapture` failure behavior and must not silently save elsewhere.

Screenshot feature failures must not affect Full1902 controller authority.

---

# 12. Tests

Update existing tests; do not create a new test framework.

Required coverage:

### Action parsing

```text
{} -> valid default
{"folder":"D:\\Screenshots"} -> valid custom
relative folder -> invalid
blank folder -> invalid persisted config
extra schema-1 property -> invalid
```

### Mutation

```text
create default Screenshot -> {}
create custom Screenshot -> folder persisted
default -> custom update
custom -> default update
invalid custom path -> rejected and document unchanged
```

### Uniqueness

```text
first Screenshot create succeeds
second Screenshot create rejected
non-Screenshot -> Screenshot update rejected when another exists
existing Screenshot remains editable
```

### Execution

```text
custom action folder -> callback receives custom folder
{} -> callback receives null
malformed Screenshot parameters -> callback not invoked
```

### Frontend/UI

Verify:

- standalone Screenshot card is gone;
- `FolderPicker` remains in editor flow;
- `SetScreenshotSaveFolderAsync` is gone;
- Screenshot folder is carried by `MutateShortcutAsync`;
- Screenshot Close Overlay stays forced off;
- a second Screenshot is not offered by normal UI;
- protocol is 68.

Do not add stress tests for theoretical timing-only interleavings.

---

# 13. Documentation updates in the same PR

Update:

```text
README.md
docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
```

They must state that Screenshot folder configuration lives inside the Screenshot Shortcut editor and is action-owned.

Overlay documentation must continue to state that Overlay receives only sanitized tile state and executes by TileId.

Add short supersession notes to historical PR-C2, PR-D, and PR-E instead of rewriting their historical bodies.

---

# 14. Non-goals

Do not add image-format selection, JPEG quality UI, monitor selection, filename templates, per-game Screenshot settings, Screenshot history/gallery, new Shortcut persistence files, a generic action schema registry, a generic uniqueness framework, a Screenshot manager/service, new Overlay screenshot state, controller lifecycle changes, HidHide/VIIPER changes, WING changes, or external-process privilege redesign.

---

# 15. Acceptance criteria

- [ ] Page-level Screenshot folder card removed.
- [ ] Screenshot Add/Edit owns Save folder controls.
- [ ] Cancel discards staged folder changes.
- [ ] Save persists folder through `MutateShortcutAsync`.
- [ ] `shortcuts.json` is canonical Screenshot folder persistence.
- [ ] Screenshot schema 1 supports `{}` default and one custom folder property.
- [ ] Existing `{}` Screenshot actions stay valid.
- [ ] Normal editor supports at most one Screenshot Shortcut.
- [ ] Runtime independently rejects a second Screenshot.
- [ ] Overlay still sends only TileId.
- [ ] `ShortcutRuntime` passes parsed folder to Host callback.
- [ ] Host no longer reads active Screenshot folder from startup settings.
- [ ] Existing Overlay retirement/capture ordering is unchanged.
- [ ] Separate Screenshot-folder frontend RPC/contracts removed.
- [ ] Frontend protocol bumped 67 -> 68.
- [ ] `ScreenshotSaveFolder` is no longer active AppSettings authority.
- [ ] No legacy Screenshot folder migration/import/fallback code is added.
- [ ] README and active UI/Overlay architecture docs match the new model.
- [ ] Historical conflicting work orders contain supersession notes.
- [ ] Relevant tests pass.

---

# 16. Final architecture

```text
settings.json
  -> no active Screenshot folder preference

shortcuts.json
  -> Screenshot tile
       Action
         TypeId = system.screenshot-fullscreen
         SchemaVersion = 1
         Parameters
           {}                     -> default
           {"folder":"D:\\Shots"} -> custom

Main UI
  -> CaptureShortcutEditorAsync
  -> staged Screenshot folder in Add/Edit
  -> MutateShortcutAsync

ShortcutRuntime
  -> one ShortcutDocument authority
  -> one-Screenshot product rule
  -> Screenshot parameter validation
  -> shortcuts.json persistence
  -> TileId execution

Overlay
  -> sanitized tile projection
  -> TileId only

AddonProcessHost
  -> existing safe Overlay retirement
  -> action-owned folder
  -> NirCmdScreenshotCapture
```

The goal is one clear owner: Screenshot-specific configuration belongs to the Screenshot Shortcut.