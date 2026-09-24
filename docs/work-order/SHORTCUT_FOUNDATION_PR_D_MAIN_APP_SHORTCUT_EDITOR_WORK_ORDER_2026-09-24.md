# Work Order — Shortcut Foundation PR-D: Runtime CRUD + Main App Shortcut Editor

**Date:** 2026-09-24  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**Target branch:** main  
**Reviewed main baseline:** d705eb768e91b8c9cce5b86177abec0d653fc25f  
**Depends on:** PR #593, PR #594, PR #595, PR #596  
**Feature area:** Shortcut editor / Runtime mutation authority  
**Implementation shape:** one focused PR  
**Next phase:** PR-E dynamic Overlay Shortcut grid

---

# 0. Goal

Implement the Main App Shortcut editor on top of the completed Shortcut foundation.

PR-D must make the current Runtime-owned Shortcut document editable without moving persistence or execution authority into WinUI.

The Main App must get a **dedicated top-level Shortcut navigation tab**.

This is intentionally separate from the existing Main App `Overlay` page.

Target Main App ownership:

```text
Device
Controller
Profile
Overlay
Shortcut
How to Use

----------------
Settings
```

`Overlay` remains responsible for the performance HUD / Overlay product.

`Shortcut` becomes responsible for user-created Shortcut definitions and Screenshot Shortcut storage preferences.

Required user-facing capability:

```text
Main App
  Shortcut
    Shortcut list
      add
      edit
      delete
      drag/drop reorder

      supported action types:
        EXE
        PowerShell
        URL
        Screenshot

    Screenshot
      Save folder
      Browse
      Use default
      Open folder
```

Required ownership:

```text
Main UI ShortcutPage
  -> displays Runtime-owned editor snapshot
  -> sends typed Shortcut edit intents
  -> never reads/writes shortcuts.json
  -> never reads/writes settings.json
  -> never executes an ActionSpec directly

Runtime ShortcutRuntime
  -> owns current ShortcutDocument
  -> resolves TileId
  -> creates new TileId values
  -> validates action configuration
  -> persists shortcuts.json
  -> publishes the new document only after save succeeds
  -> remains the later execution authority

StartupSettingsCoordinator
  -> remains the owner of ScreenshotSaveFolder
```

This PR is the editor phase only.

Do **not** implement the final Overlay dynamic Shortcut grid in PR-D.

---

# 1. Mandatory source review before coding

Read current `main` before editing.

## 1.1 Full1902 authority

At minimum:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
```

Product baseline:

```text
Standalone Full1902
one Windows user
one interactive session
no Fast User Switching
no RDP / multi-session support
```

CTW integration is no longer part of the product.

Ignore old CTW-integration design material.

Shortcut editing must remain completely independent from:

```text
PID1901 / PID1902 ownership
HidHide
VIIPER
SteamDeck / Xbox360 presentation
routing rollback
controller PnP recovery
suspend/resume
```

Do not add a Shortcut lifecycle participant, controller gate, epoch, authority, recovery manager, or state machine.

## 1.2 Completed Shortcut foundation

Read:

```text
docs/work-order/SHORTCUT_FOUNDATION_PR_A_DYNAMIC_ACTION_TILE_CONTRACTS_WORK_ORDER_2026-09-24.md
docs/work-order/SHORTCUT_FOUNDATION_PR_B_RUNTIME_PERSISTENCE_WORK_ORDER_2026-09-24.md
docs/work-order/SHORTCUT_FOUNDATION_PR_C_RUNTIME_EXTERNAL_ACTION_ENGINE_WORK_ORDER_2026-09-24.md
docs/work-order/SHORTCUT_FOUNDATION_PR_C2_NIRCMD_FULLSCREEN_SCREENSHOT_ACTION_WORK_ORDER_2026-09-24.md
```

At minimum inspect:

```text
src/SteamInputAddonforClaw.Contracts/Shortcuts/ShortcutContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutFrontendContracts.cs

src/SteamInputAddonforClaw/Shortcuts/ShortcutDocument.cs
src/SteamInputAddonforClaw/Shortcuts/ShortcutStore.cs
src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs
src/SteamInputAddonforClaw/Shortcuts/ShortcutActionTypeIds.cs
src/SteamInputAddonforClaw/Shortcuts/NirCmdScreenshotCapture.cs

src/SteamInputAddonforClaw/Settings/AppSettings.cs
src/SteamInputAddonforClaw/Settings/SettingsStore.cs
src/SteamInputAddonforClaw/Settings/StartupSettingsCoordinator.cs

src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
```

## 1.3 Current Main UI / transport

Read:

```text
src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
src/SteamInputAddonforClaw.UI/MainNavigationState.cs
src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml
src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml.cs

src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs

src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
```

For picker patterns, inspect:

```text
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs
```

The current UI already uses the Windows App SDK picker pattern:

```text
Microsoft.Windows.Storage.Pickers
WindowId
FileOpenPicker(windowId)
```

Use the same family for the new EXE and folder pickers.

Do not create another picker wrapper/service.

---

# 2. Reviewed current state

## 2.1 Shortcut domain is already durable

Current durable identity is:

```text
ShortcutDashboardDefinition
  ordered Tiles[]

ShortcutTileDefinition
  TileId
  Title
  Action

ShortcutActionSpec
  TypeId
  SchemaVersion
  Parameters
```

Existing rules remain authoritative:

- `TileId` is stable identity.
- collection order is layout order.
- there is no Row/Column/Order field.
- unknown action TypeId is structurally valid persisted data.
- the normal render projection intentionally does not expose action parameters.

Do not replace these contracts.

## 2.2 Persistence is already Runtime-owned

`ShortcutStore` already owns:

```text
shortcuts.json
root schemaVersion
structural validation
atomic same-directory temp replacement
malformed/newer/read-failure preservation
CanSafelyReplace semantics
```

Do not move Shortcut data into `settings.json`.

Do not let `ShortcutPage` call `ShortcutStore`.

## 2.3 Execution is already TileId-owned

`ShortcutRuntime` already resolves and executes:

```text
system.executable
system.powershell
system.url
system.screenshot-fullscreen
```

PR #596 made the execution entry point:

```text
ExecuteAsync(TileId)
```

That remains the execution contract.

Editor transport may carry editable configuration because editing requires it.

It must never become execution authority.

Never add:

```text
ExecuteShortcut(ActionSpec)
ExecuteExecutable(path)
ExecutePowerShell(script)
ExecuteUrl(url)
```

to the frontend contract.

## 2.4 Screenshot lifecycle is already complete

PR #596 already owns:

```text
Shortcut Screenshot invocation
  -> existing visible-surface transition
  -> existing Overlay retirement
  -> Hidden ACK
  -> consumed controller release
  -> presentation resume/reconcile
  -> NirCmd capture
```

PR-D must not modify this ordering.

The Main App editor only creates/edits the Screenshot tile definition and its global save-folder preference.

## 2.5 ScreenshotSaveFolder already exists

Current ownership:

```text
AppSettings.ScreenshotSaveFolder
StartupSettingsCoordinator.ScreenshotSaveFolder
StartupSettingsCoordinator.ChangeScreenshotSaveFolder(...)
NirCmdScreenshotCapture.ResolveFolder(...)
```

Current policy:

```text
null
  -> Pictures\Screenshots

absolute custom path
  -> exact custom path

relative/malformed
  -> rejected / fallback according to existing settings policy
```

Do not put the folder in Shortcut tile Parameters.

---

# 3. Main App information architecture

## 3.1 Add a dedicated Shortcut top-level tab

Modify Main App navigation to:

```text
Device
Controller
Profile
Overlay
Shortcut
How to Use
```

with `Settings` continuing to use the standard NavigationView Settings destination.

Recommended XAML shape:

```xml
<NavigationViewItem Content="Shortcut" Tag="Shortcut">
    <NavigationViewItem.Icon>
        <SymbolIcon Symbol="Link" />
    </NavigationViewItem.Icon>
</NavigationViewItem>
```

If `Link` is not available/appropriate in the current WinUI Symbol set, select one existing stock Symbol that clearly reads as a user action/shortcut.

Do not add a custom icon package for this PR.

## 3.2 Shortcut is not part of Overlay

Create:

```text
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs
```

and add:

```text
MainNavigationPage.Shortcut
"Shortcut" => MainNavigationPage.Shortcut
views:ShortcutPage x:Name="ShortcutContent"
```

`ShowPage(...)` must give Shortcut its own Visibility and Activate/Deactivate handling.

Do **not** place Shortcut editor controls in:

```text
OverlayPage.xaml
OverlayPage.xaml.cs
```

The Main App `Overlay` page remains dedicated to ClawHUD / performance-overlay configuration.

No Shortcut list, Screenshot folder picker, Shortcut Add button, or Shortcut editor dialog belongs on the Overlay page.

## 3.3 No new child navigation state

The Shortcut editor can use `ContentDialog` for Add/Edit.

Do not create:

```text
ShortcutDetailPage
ShortcutEditNavigationState
ShortcutNavigationManager
```

unless a concrete implementation blocker proves a dialog cannot satisfy the editor.

A dedicated top-level `ShortcutPage` + local Add/Edit dialog is sufficient.

---

# 4. Frontend editor contracts

The existing `FrontendShortcutDashboardSnapshot` remains the sanitized read-only renderer contract.

Do not expand it with script/path/url fields.

Add a **separate editor-only contract** under:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/
```

Suggested file:

```text
ShortcutEditorFrontendContracts.cs
```

The editor contract is allowed to expose action configuration to the trusted Main App because editing requires that data.

It must not be reused by Overlay rendering.

## 4.1 Action kind

Use a closed editor enum only for action families this build can actually edit:

```csharp
public enum FrontendShortcutEditorActionKind
{
    Executable,
    PowerShell,
    Url,
    ScreenshotFullscreen,
    Unsupported,
}
```

This enum is an editor capability list.

It does **not** replace the extensible persisted `TypeId` string.

The persisted action model remains extensible.

## 4.2 Editor action projection

Use a small explicit shape equivalent to:

```csharp
public sealed record FrontendShortcutEditorAction(
    FrontendShortcutEditorActionKind Kind,
    string TypeId,
    int SchemaVersion,
    bool Editable,
    string? ExecutablePath = null,
    string? ExecutableArguments = null,
    string? PowerShellScript = null,
    string? Url = null);
```

Rules:

- known schema-1 EXE/PowerShell/URL/Screenshot actions map to their known Kind;
- known action TypeId with unsupported schema version maps to `Unsupported`, `Editable=false`;
- unknown TypeId maps to `Unsupported`, `Editable=false`;
- preserve and display TypeId for unsupported tiles;
- never log `PowerShellScript`, executable arguments, or full editor payloads.

For a known schema-1 action whose parameters are currently invalid, prefer projecting the known editor kind with whatever safe field values can be extracted so the user can repair it.

Do not force-delete an invalid known action.

## 4.3 Editor tile

Equivalent shape:

```csharp
public sealed record FrontendShortcutEditorTile(
    Guid TileId,
    string Title,
    FrontendShortcutEditorAction Action);
```

No layout Row/Column/Order field.

List order remains authoritative.

## 4.4 Screenshot folder projection

Equivalent shape:

```csharp
public sealed record FrontendScreenshotFolderSnapshot(
    bool UsingDefault,
    string EffectiveFolder,
    string? ConfiguredFolder);
```

`EffectiveFolder` is the folder the current Runtime would actually use.

When default is selected:

```text
ConfiguredFolder = null
UsingDefault = true
EffectiveFolder = Pictures\Screenshots
```

Do not duplicate folder resolution rules in WinUI.

The Runtime/frontend owner should derive the effective value from the existing screenshot folder policy.

## 4.5 Editor snapshot

Equivalent shape:

```csharp
public sealed record FrontendShortcutEditorSnapshot(
    bool Available,
    IReadOnlyList<FrontendShortcutEditorTile> Tiles,
    FrontendScreenshotFolderSnapshot ScreenshotFolder,
    string? FailureMessage = null);
```

Semantics:

```text
Available + zero tiles
  = valid empty Shortcut collection

Available=false
  = Shortcut document is not safely editable in this Runtime
```

Unsafe-load cases from PR-B remain fail-closed.

---

# 5. Mutation intent

Prefer **one Shortcut-specific mutation RPC**, not four generic CRUD endpoints.

Use a narrow discriminated intent.

## 5.1 Mutation kind

Equivalent:

```csharp
public enum FrontendShortcutMutationKind
{
    Create,
    Update,
    Delete,
    Move,
}
```

## 5.2 Action input

The Main App should not invent persisted TypeId strings or action schema versions.

Use an editor input shape equivalent to:

```csharp
public sealed record FrontendShortcutActionInput(
    FrontendShortcutEditorActionKind Kind,
    string? ExecutablePath = null,
    string? ExecutableArguments = null,
    string? PowerShellScript = null,
    string? Url = null);
```

Rules:

- `Unsupported` is never accepted as an input kind.
- Runtime maps the supported Kind to the canonical `ShortcutActionTypeIds` string.
- Runtime writes action SchemaVersion 1 for the current supported action.
- Screenshot maps to an empty JSON object.
- EXE maps to `{ path, arguments }`.
- PowerShell maps to `{ script }`.
- URL maps to `{ url }`.

No generic key/value editor.

No raw JSON text editor.

## 5.3 Mutation intent

Equivalent:

```csharp
public sealed record FrontendShortcutMutationIntent(
    FrontendShortcutMutationKind Kind,
    Guid? TileId = null,
    string? Title = null,
    FrontendShortcutActionInput? Action = null,
    int? TargetIndex = null);
```

Required shapes:

```text
Create
  TileId = null
  Title required
  Action required
  TargetIndex = null

Update
  TileId required
  Title required
  Action required
  TargetIndex = null

Delete
  TileId required
  Title/Action/TargetIndex absent

Move
  TileId required
  TargetIndex required
  Title/Action absent
```

Reject malformed mixed shapes.

Do not silently infer missing members.

## 5.4 Mutation result

Equivalent:

```csharp
public sealed record FrontendShortcutMutationResult(
    bool Succeeded,
    bool Changed,
    string? FailureMessage,
    FrontendShortcutEditorSnapshot Snapshot);
```

Always return the authoritative post-operation snapshot.

On failure:

- `Succeeded=false`;
- `Changed=false`;
- snapshot is the still-current Runtime state;
- Main UI re-renders that snapshot.

This gives UI rollback without a separate recovery mechanism.

---

# 6. ShortcutRuntime becomes the edit owner

PR-C/C2 currently load one Runtime-owned document.

PR-D should extend that same owner.

Do **not** create:

```text
ShortcutEditorManager
ShortcutCrudService
ShortcutRepository
ShortcutMutationCoordinator
ShortcutDocumentAuthority
```

The existing `ShortcutRuntime` is already the correct owner.

## 6.1 Keep the store reference

`ShortcutRuntime` currently consumes `ShortcutStore` during construction.

Retain that store as the persistence target for mutations.

The loaded `_document` must become replaceable after a successful mutation.

Conceptually:

```text
ShortcutRuntime
  _store
  _document
  _available
```

Do not create a second in-memory document copy.

## 6.2 Save-then-publish

Every successful mutation must follow:

```text
current document
  -> construct complete candidate document
  -> structural/action validation
  -> ShortcutStore.Save(candidate)
  -> ONLY after Save succeeds:
       _document = candidate
  -> return authoritative snapshot
```

If save fails:

```text
current _document unchanged
canonical current Runtime state unchanged
failure result returned
```

Do not optimistically replace `_document` before persistence.

## 6.3 Unsafe-load fail-close

If initial `ShortcutStore.Load()` was:

```text
Malformed
UnsupportedSchemaVersion
ReadFailure
```

then Runtime remains unavailable for editing.

Do not overwrite the original file from the editor.

This is one of PR-B's central safety guarantees.

## 6.4 Create

Create rules:

- Runtime generates `Guid.NewGuid()`.
- UI never supplies TileId.
- validate nonblank title.
- validate action input.
- append the new tile to the end of the ordered collection.
- save-then-publish.
- return the new authoritative snapshot.

Do not add an arbitrary maximum tile count in this PR.

## 6.5 Update

Update resolves by TileId.

Rules:

- TileId must already exist.
- preserve TileId.
- preserve current collection position.
- title may change.
- supported action kind/configuration may change.
- Runtime rebuilds the canonical `ShortcutActionSpec`.
- save-then-publish.

If the existing tile is `Unsupported` / non-editable, Main UI should not offer Edit.

Runtime must still reject an attempted Update that cannot be mapped safely.

Delete and Move remain allowed for unsupported tiles because they do not reinterpret the action payload.

## 6.6 Delete

Delete resolves by TileId and removes exactly that tile.

Unknown TileId fails cleanly.

Deleting the final tile produces a valid empty dashboard.

No placeholder tiles are inserted.

## 6.7 Move

Move intent is:

```text
TileId + TargetIndex
```

Rules:

- resolve the current tile by TileId;
- target index must be within the current collection bounds;
- remove then insert at TargetIndex using the current authoritative collection;
- TileId never changes;
- no row/column/order metadata is added;
- same-position move is a successful no-op.

The product supports one Windows user / one interactive editor.

Do not add version vectors, edit epochs, optimistic concurrency tokens, or multi-editor merge logic.

## 6.8 Action validation

The editor must create definitions that the existing executor understands.

Match the current Runtime rules.

### EXE

Valid configuration:

- nonblank path;
- fully qualified path;
- extension is `.exe` case-insensitively;
- arguments optional.

Do **not** require the file to exist at edit/save time.

A removable/external target may legitimately be unavailable later.

Execution availability remains a Runtime projection concern.

### PowerShell

- nonblank script;
- multiline supported;
- preserve user text;
- no editor-side execution/test command.

### URL

- absolute URI;
- scheme must be http or https;
- valid host required.

### Screenshot

- schema 1;
- empty Parameters object only;
- no per-tile folder;
- no PNG selector;
- no JPEG quality selector;
- no primary/all-monitor selector.

## 6.9 Do not add a new Runtime lock for theoretical races

Current Main UI frontend requests are already serialized through the existing named-pipe request operation gate.

PR-E has not yet exposed concurrent Overlay Shortcut execution.

Do not add a new lock/semaphore/epoch/state machine solely for hypothetical future interleavings.

If PR-E later introduces a concrete concurrently reachable path, review the actual path then.

---

# 7. Frontend seam

Extend `IAddonFrontendControl` with exactly the editor operations needed now.

Recommended surface:

```csharp
Task<FrontendShortcutEditorSnapshot> CaptureShortcutEditorAsync(
    CancellationToken cancellationToken = default);

Task<FrontendShortcutMutationResult> MutateShortcutAsync(
    FrontendShortcutMutationIntent intent,
    CancellationToken cancellationToken = default);

Task<FrontendScreenshotFolderMutationResult> SetScreenshotSaveFolderAsync(
    string? folder,
    CancellationToken cancellationToken = default);
```

Define a small folder mutation result:

```csharp
public sealed record FrontendScreenshotFolderMutationResult(
    bool Succeeded,
    string? FailureMessage,
    FrontendScreenshotFolderSnapshot Snapshot);
```

No `ExecuteShortcutAsync` RPC yet.

That belongs to PR-E when Overlay needs TileId execution transport.

No editor method accepts raw `ShortcutActionSpec`.

---

# 8. InProcessAddonFrontendControl wiring

Inject the existing `ShortcutRuntime` into `InProcessAddonFrontendControl` as one optional/runtime-owned dependency, following the class's existing concrete Runtime dependency pattern.

Production `AddonProcessHost` must pass its existing:

```text
_shortcutRuntime
```

Do not construct a second ShortcutRuntime.

## 8.1 Capture

`CaptureShortcutEditorAsync` composes:

```text
ShortcutRuntime editor snapshot
+
StartupSettingsCoordinator ScreenshotSaveFolder
+
existing NirCmdScreenshotCapture.ResolveFolder(...) policy
```

No direct file read.

## 8.2 Mutate

`MutateShortcutAsync` delegates to `ShortcutRuntime`.

On an actually changed successful mutation:

```text
StateInvalidated?.Invoke(...)
```

Do not emit invalidation before persistence succeeds.

A successful no-op does not need invalidation.

## 8.3 Screenshot folder mutation

`SetScreenshotSaveFolderAsync` delegates to:

```text
StartupSettingsCoordinator.ChangeScreenshotSaveFolder(...)
```

Rules:

- absolute path accepted;
- null/blank means reset to default;
- invalid relative path rejected;
- unrelated settings unchanged;
- save-before-publish remains owned by StartupSettingsCoordinator.

Return the effective folder snapshot after the operation.

Raise `StateInvalidated` only when the persisted setting actually changes.

---

# 9. Main frontend pipe transport

PR-D adds new Main App RPC surface, so the named-pipe protocol must move:

```text
FrontendTransportProtocol.CurrentVersion
41 -> 42
```

Pre-release product: no compatibility shim.

A v41 UI/runtime pair must fail handshake instead of partially understanding the new editor surface.

Add:

```text
FrontendRpcMethod.CaptureShortcutEditor
FrontendRpcMethod.MutateShortcut
FrontendRpcMethod.SetScreenshotSaveFolder
```

Add only the request DTO required for the folder mutation if needed.

`CaptureShortcutEditor` takes no payload.

`MutateShortcut` takes `FrontendShortcutMutationIntent`.

`SetScreenshotSaveFolder` takes a narrow request containing nullable folder.

Wire through:

```text
NamedPipeAddonFrontendClient
NamedPipeAddonFrontendServer
FrontendWireCodec
```

Do not change:

```text
OverlayWire
OverlayTransportProtocol
```

PR-D is Main App editor transport only.

---

# 10. ShortcutPage UI

Create one dedicated Main App page.

Suggested structure:

```text
Shortcut

Create and organize actions shown in the Shortcut overlay page.

[ + Add Shortcut ]

[ drag ] Steam
         Application
         Edit   Delete

[ drag ] HDR Script
         PowerShell
         Edit   Delete

[ drag ] Website
         URL
         Edit   Delete

[ drag ] Screenshot
         Screenshot
         Edit   Delete

Screenshot
[ Save folder      C:\Users\...\Pictures\Screenshots ]
                   Default

[ Browse ] [ Use default ] [ Open folder ]
```

Keep normal Main App styling consistent with the current WinUI pages.

Do not imitate the final near-square Overlay tiles here.

The Main App editor is a configuration list.

## 10.1 Empty state

A valid empty dashboard must show:

```text
No shortcuts yet.
Add Shortcut
```

Do not create four placeholder rows.

## 10.2 List row

Each row should show at minimum:

- drag/reorder affordance;
- Title;
- action label;
- concise target summary where safe;
- Edit button when editable;
- Delete button.

Safe target summaries:

```text
EXE
  filename only is sufficient

PowerShell
  "PowerShell"
  do not render full script in the list

URL
  host or concise URL is acceptable

Screenshot
  "Fullscreen screenshot"

Unsupported
  TypeId
  "Unsupported in this version"
```

Do not expose full PowerShell body in the list.

## 10.3 Add button

Add opens one `ContentDialog`.

Fields:

```text
Title
Action type
```

Action options:

```text
Application (.exe)
PowerShell
Website (URL)
Screenshot
```

No TDP/FPS/ClawHUD actions in PR-D.

## 10.4 Edit dialog

The same dialog may be reused for supported existing actions.

Do not create a generic metadata-driven form engine.

Switch four explicit panels based on the selected action kind.

### Application panel

```text
Executable path
Browse...
Arguments
```

Use the existing Windows App SDK `FileOpenPicker(WindowId)` pattern.

Filter to `.exe`.

The text field remains editable.

### PowerShell panel

```text
Script
[multiline TextBox]
```

Requirements:

- `AcceptsReturn=true`;
- practical editor height;
- horizontal/vertical behavior that does not make long scripts unusable;
- no Run/Test button.

### URL panel

```text
URL
```

Expected:

```text
https://...
```

### Screenshot panel

No action-specific parameters.

Show concise text:

```text
Captures the primary display as JPEG.
The save folder is configured on this Shortcut page.
```

Do not duplicate folder selection inside every Screenshot tile dialog.

## 10.5 Unsupported tile

For an unsupported/unknown action:

- keep the tile visible;
- show TypeId;
- disable Edit;
- allow Delete;
- allow reorder.

Do not rewrite the action merely because this version cannot edit it.

---

# 11. Drag/drop ordering

Use the normal WinUI list reorder facility.

A suitable implementation is a `ListView` backed by an ObservableCollection with:

```text
CanDragItems
CanReorderItems
AllowDrop
```

or the current Windows App SDK equivalent supported by the project.

The UI collection reorder is only an optimistic presentation proposal.

After drop:

```text
dragged TileId
+
new index
  -> MutateShortcutAsync(Move)
  -> Runtime persists
  -> authoritative result snapshot
  -> re-render from result
```

If Runtime rejects or persistence fails, the returned authoritative snapshot restores the actual order.

Do not persist from the collection changed event itself.

Do not add an editor-local order file.

Do not add per-tile Order properties.

---

# 12. Simple UI operation serialization

One page-local in-flight mutation guard is sufficient.

While Add/Edit/Delete/Move/folder mutation is in progress:

- disable controls that would launch another conflicting edit;
- await the one RPC;
- render the returned authoritative result;
- re-enable controls.

No edit queue, command bus, mutation scheduler, or version-token system is required.

The current supported product is one Main UI / one user / one session.

## 12.1 StateInvalidated handling

`ShortcutPage` should support:

```text
Activate()
Deactivate()
RequestRefresh()
```

MainWindow may forward the existing frontend `StateInvalidated` event to `ShortcutContent.RequestRefresh()`.

Rules:

- refresh only when the page is active;
- do not let a notification overwrite an in-flight mutation dialog/result;
- after a mutation, the mutation result snapshot is already authoritative;
- a skipped refresh during the short in-flight operation does not require a queue/state machine.

A simple dirty bit or post-operation refresh is acceptable only if the implementation actually needs it.

Do not build generalized frontend synchronization machinery.

---

# 13. Screenshot folder UI

Place a dedicated `Screenshot` section on `ShortcutPage`.

Show:

- effective folder path;
- whether the default is being used;
- Browse;
- Use default;
- Open folder.

## 13.1 Browse

Use:

```text
Microsoft.Windows.Storage.Pickers.FolderPicker
WindowId
```

following the same current-project picker ownership pattern used by `ControllerPage`.

The page can receive the existing MainWindow HWND provider and derive WindowId exactly as ControllerPage already does.

Do not add a picker service abstraction.

The picked path is sent through:

```text
SetScreenshotSaveFolderAsync(path)
```

The page itself does not write settings.json.

## 13.2 Use default

Send:

```text
SetScreenshotSaveFolderAsync(null)
```

Default remains:

```text
Pictures\Screenshots
```

No separate reset flag is needed.

## 13.3 Open folder

This is a Main UI convenience action, not a Runtime Shortcut execution command.

Use the authoritative `EffectiveFolder` from the latest editor snapshot.

It is acceptable for the WinUI process to:

1. create the effective directory if it does not yet exist;
2. open it with the normal Windows shell / Explorer path.

Follow the existing UI pattern that uses `Process.Start(... UseShellExecute=true)` for opening an Explorer target.

Do not add a Runtime RPC just to launch Explorer.

Failure should show a local nonfatal InfoBar/message.

No Full1902 state is involved.

---

# 14. MainWindow wiring

Update:

```text
src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
src/SteamInputAddonforClaw.UI/MainNavigationState.cs
```

Required behavior:

```text
NavigationView Shortcut item
  -> MainNavigationPage.Shortcut
  -> ShortcutContent visible
  -> ShortcutContent.Activate()

leaving Shortcut
  -> ShortcutContent.Deactivate()
```

Initialize with:

- `IAddonFrontendControl`;
- MainWindow handle/window provider needed for Windows App SDK pickers.

Do not make Shortcut a child of Overlay.

Mouse-back does not need a special destination for a top-level Shortcut page.

---

# 15. Overlay must remain untouched functionally

PR-D must not change the existing Overlay Shortcut POC behavior.

Specifically do not modify its current product behavior:

```text
temporary 2x2 Shortcut shell
OverlayShortcutSelection
A does nothing
no Runtime Shortcut transport
```

PR-E replaces that shell.

PR-D must not add:

- dynamic Overlay Shortcut tiles;
- Overlay Shortcut capture;
- Overlay Shortcut execution;
- Screenshot-specific Overlay button handling;
- Overlay Screenshot folder editor;
- OverlayWire Shortcut RPC.

Main App `OverlayPage` also remains independent and should not gain Shortcut editor controls.

---

# 16. Logging and privacy

Do not log:

- PowerShell script contents;
- executable arguments;
- full URL query strings unnecessarily;
- full editor mutation payload;
- raw action JSON.

Log only safe metadata such as:

```text
TileId
TypeId / action kind
mutation kind
outcome
tile count
failure category
```

Existing executable paths may be user-private.

Prefer not to log full paths unless an existing diagnostic convention explicitly requires it.

No new redaction framework is needed.

---

# 17. Failure policy

## 17.1 Store save failure

If `ShortcutStore.Save` throws:

- mutation fails;
- current Runtime document stays unchanged;
- UI receives current snapshot;
- Full1902 lifecycle stays unaffected.

Do not crash the Runtime.

## 17.2 Unknown TileId

Update/Delete/Move fails cleanly.

Do not mutate a nearby index as fallback.

## 17.3 Invalid edit configuration

Runtime rejects it.

UI remains usable and can show the failure reason.

Do not save a partially valid tile.

## 17.4 Malformed/newer shortcuts.json

Editor shows unavailable state.

Do not overwrite.

Do not offer "repair" or destructive reset in this PR.

## 17.5 Frontend disconnect

No separate recovery is needed.

Each committed mutation is already durable before the Runtime publishes it.

A disconnected Main UI can reopen and recapture the current document.

## 17.6 Screenshot folder failure

Invalid folder setting is rejected without changing unrelated settings.

Opening the folder from Main UI is best-effort UI behavior and cannot affect Runtime lifecycle.

---

# 18. Tests — Runtime CRUD

Extend `ShortcutRuntimeTests` or add one focused editor test file.

At minimum prove:

## 18.1 Capture

1. empty document -> available empty editor snapshot;
2. known EXE maps to editable EXE fields;
3. PowerShell maps script only in editor contract;
4. URL maps correctly;
5. Screenshot maps to Screenshot kind with no per-tile folder;
6. unknown TypeId is visible but non-editable;
7. unsupported known action schema is visible but non-editable.

## 18.2 Create

1. Runtime creates non-empty TileId;
2. caller does not provide TileId;
3. created tile is appended;
4. EXE parameters serialize to canonical schema-1 object;
5. PowerShell parameters serialize correctly;
6. URL parameters serialize correctly;
7. Screenshot parameters are exactly an empty object;
8. persisted document reloads with the same TileId/order.

## 18.3 Update

1. preserves TileId;
2. preserves position;
3. changes title;
4. can change between supported action families;
5. rejects invalid input without changing disk/current document.

## 18.4 Delete

1. removes exactly the matching TileId;
2. unknown TileId fails;
3. deleting final tile leaves a valid empty dashboard.

## 18.5 Move

1. move first -> last;
2. move last -> first;
3. middle move;
4. same-position no-op;
5. out-of-range index rejected;
6. TileIds unchanged.

## 18.6 Safe persistence

1. save failure leaves Runtime document unchanged;
2. malformed load cannot be mutated;
3. unsupported newer root schema cannot be mutated;
4. read-failure load cannot be mutated;
5. the original unsafe file is preserved.

Use the smallest test seam needed to force a save failure.

Do not introduce a production persistence interface hierarchy solely for tests.

---

# 19. Tests — Screenshot folder frontend

Extend current settings/frontend tests.

Prove:

1. null setting projects default `Pictures\Screenshots`;
2. custom absolute folder projects exact effective path;
3. valid folder mutation persists before publish;
4. null/blank resets to default;
5. invalid relative folder is rejected;
6. unrelated AppSettings remain unchanged;
7. StateInvalidated fires only for an actual committed change.

---

# 20. Tests — named-pipe transport

Update frontend transport tests for protocol v42.

Prove:

1. `CaptureShortcutEditorAsync` round-trips;
2. `MutateShortcutAsync` round-trips all intent fields;
3. `SetScreenshotSaveFolderAsync` round-trips nullable folder;
4. capture rejects unexpected payload;
5. malformed mutation payload returns a protocol/error result rather than mutating;
6. v41 peer is rejected at handshake;
7. CurrentVersion assertions are updated to 42.

Do not bump Overlay protocol.

---

# 21. Tests — Main UI navigation / layout

Add/extend UI source/architecture tests.

Prove:

1. Main NavigationView contains one top-level `Shortcut` item;
2. order is:

```text
Device
Controller
Profile
Overlay
Shortcut
How to Use
```

3. `MainNavigationPage.Shortcut` exists;
4. `"Shortcut"` maps to it;
5. `ShortcutContent` exists in MainWindow;
6. `ShowPage` owns Shortcut visibility;
7. Shortcut page activates/deactivates independently;
8. Main App `OverlayPage.xaml` does not contain Shortcut editor controls;
9. `ShortcutPage` contains Add, list/reorder, Screenshot folder controls;
10. no fixed four-slot Main App editor model exists.

Do not write brittle pixel-perfect tests.

---

# 22. Manual validation

On a normal Windows 11 MSI Claw environment, validate:

## 22.1 Navigation

- Shortcut appears as its own Main App tab.
- Overlay remains its own independent tab.
- switching between them does not share controls/state.

## 22.2 EXE

- Add EXE tile with picker.
- edit title/arguments.
- reorder.
- close/reopen Main App.
- data remains persisted.

Actual execution will be validated again in PR-E through Overlay TileId execution.

## 22.3 PowerShell

- multiline script can be entered and edited.
- script is not shown in full in the Shortcut list.
- close/reopen preserves it.

## 22.4 URL

- valid https URL accepted.
- invalid/non-http(s) URL rejected.
- persisted edit survives UI restart.

## 22.5 Screenshot

- add Screenshot tile.
- tile has no folder setting in its edit dialog.
- global Screenshot folder Browse works.
- Use default returns to Pictures\Screenshots.
- Open folder opens the effective folder.
- setting survives UI restart.

## 22.6 Reorder

- drag first item to last.
- drag last item to first.
- restart UI.
- order remains exactly as saved.

## 22.7 Failure isolation

If feasible, simulate one Shortcut storage failure.

Confirm:

- editor shows failure;
- previous data remains authoritative;
- controller remains usable;
- Full1902 presentation is unaffected;
- Runtime does not restart or lose controller authority.

---

# 23. Explicit non-goals

Do not implement in PR-D:

```text
dynamic Overlay Shortcut grid
Overlay Shortcut execution RPC
ExecuteShortcut(TileId) frontend transport
TDP Shortcut
FPS Shortcut
ClawHUD Shortcut
Battery Shortcut
Power Mode Shortcut
media actions
keyboard actions
macros
multi-action chains
conditions
delays
hotkeys
controller chords
Shortcut import/export
Shortcut cloud sync
plugin discovery
generic form engine
generic action registry UI
per-action icon selection
custom tile colors
Screenshot PNG
Screenshot quality selector
multi-monitor Screenshot selector
screenshot history/gallery
```

No polling.

No CTW integration.

No new controller authority.

---

# 24. Expected files

Likely production changes:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutEditorFrontendContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs

src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs

src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs

src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
src/SteamInputAddonforClaw.UI/MainNavigationState.cs
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs
```

Expected tests include:

```text
tests/SteamInputAddonforClaw.Tests/ShortcutRuntimeTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
tests/SteamInputAddonforClaw.Tests/MainNavigationStateTests.cs
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
```

Add a focused Shortcut editor UI/contract test file if that is cleaner than expanding unrelated suites.

Do not edit `OverlayWire.cs` for this PR.

---

# 25. Documentation update

Update the current UI architecture documentation so it no longer implies that Shortcut belongs to Overlay.

At minimum revise:

```text
docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md
```

Current Main App product ownership should become:

```text
Device
  handheld-level controls

Controller
  controller/button behavior

Profile
  per-game overrides

Overlay
  ClawHUD / performance overlay settings

Shortcut
  user-created Shortcut definitions
  Screenshot Shortcut save-folder preference

How to Use
  help

Settings
  required components / app settings / developer entry
```

The final Overlay Shortcut tiles remain a separate runtime consumption surface implemented in PR-E.

Also update Shortcut architecture/work-order cross references where necessary to record:

```text
Main App Shortcut editor
  != Main App Overlay page
  != WinUI3 Addon Overlay Shortcut grid
```

---

# 26. Automated validation

Run at minimum:

```text
dotnet restore SteamInputAddonforClaw.slnx

dotnet build SteamInputAddonforClaw.slnx -c Debug --no-restore
dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore

dotnet test SteamInputAddonforClaw.slnx -c Debug --no-restore
dotnet test SteamInputAddonforClaw.slnx -c Release --no-restore

git diff --check
```

Run normal GitHub CI on the final head SHA before merge.

No new native dependency is introduced in PR-D.

NirCmd packaging verification remains unchanged from PR #596.

---

# 27. Acceptance checklist

## Architecture

- [ ] Standalone Full1902 assumptions preserved.
- [ ] CTW integration ignored.
- [ ] no new controller/device authority.
- [ ] ShortcutRuntime remains the one Shortcut document/execution owner.
- [ ] Main UI never accesses shortcuts.json directly.
- [ ] Main UI never executes raw action payloads.
- [ ] Screenshot capture lifecycle from PR #596 is unchanged.

## Navigation

- [ ] Shortcut is a dedicated top-level Main App tab.
- [ ] Shortcut is separate from Main App Overlay.
- [ ] navigation order is Device / Controller / Profile / Overlay / Shortcut / How to Use.
- [ ] ShortcutPage owns its own activation/refresh.

## Runtime CRUD

- [ ] Create generates TileId in Runtime.
- [ ] Update preserves TileId and list position.
- [ ] Delete resolves by TileId.
- [ ] Move uses TileId + target index.
- [ ] list order remains the only layout authority.
- [ ] save succeeds before Runtime publishes the new document.
- [ ] unsafe loaded files cannot be overwritten.
- [ ] no multi-editor/version-vector machinery added.

## Actions

- [ ] EXE editor.
- [ ] PowerShell editor.
- [ ] URL editor.
- [ ] Screenshot editor.
- [ ] unknown/newer actions preserved and visible as unsupported.
- [ ] unsupported tiles are not rewritten.
- [ ] PowerShell script is not logged.
- [ ] no raw JSON editor.

## Screenshot settings

- [ ] global Screenshot folder shown on ShortcutPage.
- [ ] Browse uses Windows App SDK FolderPicker.
- [ ] Use default writes null.
- [ ] Open folder uses authoritative EffectiveFolder.
- [ ] no per-tile Screenshot folder.
- [ ] no PNG/quality/multi-monitor additions.

## Transport

- [ ] Frontend protocol 41 -> 42.
- [ ] CaptureShortcutEditor RPC.
- [ ] MutateShortcut RPC.
- [ ] SetScreenshotSaveFolder RPC.
- [ ] Overlay protocol unchanged.
- [ ] ExecuteShortcut RPC remains deferred to PR-E.

## UI

- [ ] empty-state UX.
- [ ] Add/Edit ContentDialog.
- [ ] drag/drop reorder.
- [ ] authoritative rollback on failed move/save.
- [ ] unsupported action Edit disabled.
- [ ] Delete available for unsupported tile.
- [ ] OverlayPage contains no Shortcut editor UI.

## Regression

- [ ] existing EXE/PowerShell/URL execution unchanged.
- [ ] existing Screenshot capture unchanged.
- [ ] Full1902 lifecycle tests pass.
- [ ] frontend transport tests pass.
- [ ] Main UI navigation tests pass.
- [ ] Overlay current 2x2 Shortcut POC remains unchanged.

---

# 28. PR-E handoff boundary

After PR-D, the product should have:

```text
Main App Shortcut tab
  -> create/edit/delete/reorder persisted Shortcut definitions
  -> configure Screenshot save folder

Runtime
  -> authoritative ShortcutDocument
  -> ExecuteAsync(TileId)
  -> four real supported action families

Overlay
  -> still temporary 2x2 Shortcut POC
```

Then PR-E can do only the consumption surface:

```text
Runtime Shortcut projection
  -> Overlay Shortcut page
  -> dynamic compact near-square tiles
  -> title/status
  -> actual rendered-column controller navigation
  -> A / click
  -> ExecuteShortcut(TileId)
```

For Screenshot:

```text
Overlay A on Screenshot tile
  -> TileId RPC
  -> Runtime ShortcutRuntime.ExecuteAsync(TileId)
  -> existing AddonProcessHost screenshot callback
  -> existing visible-surface transition
  -> existing Overlay retirement
  -> consumed A release
  -> NirCmd capture
```

Overlay must never receive Screenshot path/settings/action authority.

---

# 29. Final principle

PR-D is not a new Shortcut subsystem.

It is the editor surface for the subsystem that already exists.

Keep the authority chain simple:

```text
Main App ShortcutPage
  -> typed edit intent

ShortcutRuntime
  -> validate
  -> persist
  -> publish authoritative document

ShortcutStore
  -> shortcuts.json

Later Overlay
  -> render snapshot
  -> execute by TileId
```

And keep Main App ownership explicit:

```text
Overlay tab != Shortcut tab
```

They are separate products/surfaces and must remain separate in the UI architecture.
