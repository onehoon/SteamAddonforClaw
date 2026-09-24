
# Work Order — Shortcut Foundation PR-A: Dynamic Action / Tile Contracts

**Date:** 2026-09-24  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**Target branch:** main  
**Reviewed main baseline:** 28810b700161ab9f050963f97534b38218abf09d  
**Feature area:** Shortcut foundation only  
**Implementation shape:** one focused PR  
**Expected size:** small/medium contract refactor; no Runtime executor, persistence, editor, or final Overlay UI

---

# 0. Goal

Replace the current fixed four-slot Shortcut POC contract with a foundation that can grow without redesigning storage, Runtime execution ownership, or frontend projection every time a new Shortcut action type is added.

The future Shortcut product is intentionally broader than controller-button remapping.

Expected future action families include, but are not limited to:

~~~text
Addon-owned actions
    TDP preset
    FPS preset
    ClawHUD toggle / mode
    Battery limit preset
    Power Mode
    future Device controls

External/system actions
    launch EXE
    PowerShell script
    URL / website
    media/system actions
    keyboard actions
    future user-created actions
~~~

The final UI is NOT part of this PR.

The current 2×2 Overlay Shortcut page is only a visual/navigation POC and must not define the product model.

This PR establishes only the durable domain boundaries that later PRs will build on:

~~~text
Shortcut definition
    stable tile identity
    ordered layout
    extensible action identity
    versioned action parameters
    structural validation

Runtime projection boundary
    title
    current status/value
    availability
    visual state
    ordered tiles

No execution yet
No persistence yet
No editor yet
No final dynamic grid yet
~~~

---

# 1. Mandatory source review before coding

Read the latest implementation-branch versions before editing.

## 1.1 Full1902 authority

At minimum:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
~~~

Shortcut is a user-invocation surface. It must not create a second controller/device authority.

Future Addon-native Shortcut actions must call the existing typed Runtime feature path.

Examples:

~~~text
TDP preset Shortcut
    -> existing Runtime-owned TDP mutation/application path

ClawHUD Shortcut
    -> existing Runtime-owned ClawHUD mutation path

Battery limit Shortcut
    -> existing Runtime-owned battery-limit path
~~~

Never:

~~~text
Shortcut action
    -> directly writes EC/WMI/IGCL/HidHide/VIIPER from Overlay or Main UI
~~~

PR-A itself must not touch any Full1902 controller lifecycle path.

## 1.2 Existing Shortcut POC

Read:

~~~text
src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsShortcutContracts.cs
src/SteamInputAddonforClaw.Overlay/OverlayShortcutSelection.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs

docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR4_SHORTCUT_PARITY_WORK_ORDER.md
docs/overlayui/OQ5_UI_11_SHORTCUT_2X2_SLOT_SHELL_WORK_ORDER.md
~~~

## 1.3 Existing shared contract / persistence patterns

Read:

~~~text
src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsShellContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsTabOrderContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs

src/SteamInputAddonforClaw.Contracts/FrontButtons/FrontButtonMapping.cs
src/SteamInputAddonforClaw.Contracts/BackButtons/BackButtonMapping.cs

src/SteamInputAddonforClaw/Settings/AppSettings.cs
src/SteamInputAddonforClaw/Settings/SettingsStore.cs

src/SteamInputAddonforClaw/Profiles/ProfileDocument.cs
src/SteamInputAddonforClaw/Profiles/ProfileStore.cs

src/SteamInputAddonforClaw/Install/AddonDataPaths.cs
~~~

---

# 2. Reviewed current state

## 2.1 Current Shortcut contract is intentionally POC-only

The current shared contract is fixed to:

~~~csharp
public enum AddonQuickSettingsShortcutSlotId
{
    Slot1,
    Slot2,
    Slot3,
    Slot4,
}
~~~

and AddonQuickSettingsShortcutContract.Create() always returns four static Unassigned entries.

That was correct for the original Shortcut-shell parity POC.

It is NOT suitable as the product foundation because future Shortcut requirements are:

- arbitrary tile count;
- drag/drop reorder;
- near-square compact tiles;
- title + current status/value;
- built-in Addon feature actions;
- EXE;
- PowerShell;
- URL/system/media actions;
- unknown future action types.

Do not extend Slot1..Slot4 into Slot5..SlotN or add more fixed enum members.

## 2.2 Current Overlay navigation is also fixed 2×2

OverlayShortcutSelection encodes:

~~~text
2 rows
2 columns
Slot1..Slot4
~~~

directly.

This remains a temporary POC implementation until the final dynamic Overlay grid PR.

PR-A must remove the shared product dependency on the fixed slot enum, but it does NOT need to implement final dynamic grid navigation.

Minimal compile-preserving cleanup is allowed; visual redesign is not.

## 2.3 Current settings.json is not the right future storage domain

AppSettings / SettingsStore currently own compact operational preferences such as:

- log preference;
- developer menu state;
- ClawHUD enablement;
- current-power-source-only preference;
- front-button mapping;
- M1/M2 mapping;
- five-tab Overlay order.

A future Shortcut document may contain:

- variable-length tile collections;
- user-entered executable paths/arguments;
- multiline PowerShell source;
- per-action schema evolution;
- action types unknown to older builds.

Do NOT put the future Shortcut collection into AppSettings.

## 2.4 profiles.json provides the correct persistence precedent

ProfileStore already demonstrates the desired pattern for a richer independent domain:

~~~text
separate file
explicit schema version
Runtime-owned
UI-independent
safe load result
unsupported newer schema is preserved
malformed/unreadable document is not overwritten
same canonical -Data root
atomic temp-file replacement
~~~

The later persistence PR should follow this pattern for:

~~~text
SteamInputAddonforClaw-Data/shortcuts.json
~~~

PR-A does NOT implement that store yet.

---

# 3. Foundation decisions locked by PR-A

## 3.1 Shortcut is a product domain, not an Overlay-specific domain

Core definition types belong under a new namespace/folder:

~~~text
src/SteamInputAddonforClaw.Contracts/Shortcuts/
namespace SteamInputAddonforClaw.Contracts.Shortcuts;
~~~

Do not make durable domain type names start with Overlay, AddonQuickSettings, or Qam.

Overlay is only one future consumer.

Main App editor and Runtime will share the same domain.

## 3.2 Stable tile identity is a GUID

Each user-visible tile has one stable:

~~~csharp
Guid TileId
~~~

The GUID survives:

- reorder;
- label edit;
- action configuration edit;
- Runtime restart;
- app restart.

Do not use:

- array index as identity;
- row/column as identity;
- Slot1..SlotN enum;
- title as identity.

A tile may move without becoming a new tile.

## 3.3 Ordered collection is the layout authority

The tile collection order is the one logical layout order.

Conceptually:

~~~text
Tiles[0]
Tiles[1]
Tiles[2]
...
~~~

Later, an editor/renderer may map it to a grid:

~~~text
columnCount = N

row    = index / N
column = index % N
~~~

PR-A must NOT persist or contractually expose duplicate layout authorities such as:

~~~text
Order
Index
Row
Column
X
Y
GridArea
PositionId
~~~

on every tile.

The list order itself is authoritative.

This supports drag/drop reorder without binding the domain to today's screen width or future column count.

## 3.4 Action identity is an extensible stable string, not a closed enum

Do NOT define a ShortcutAction enum whose members expand for each future action.

Action identity must be a stable, case-sensitive string.

Examples for future implementations:

~~~text
addon.tdp-preset
addon.fps-preset
addon.clawhud-toggle
system.executable
system.powershell
system.url
system.media
~~~

PR-A must not register or execute these examples.

They are architecture exemplars only.

The base structural validator must accept an unknown well-formed TypeId.

Unknown action type is:

~~~text
valid persisted data
+ potentially unsupported by this Runtime
~~~

not corrupt document data.

This is necessary so future/older builds do not destroy user-defined actions merely because they do not understand them.

## 3.5 Action parameters are opaque to the base foundation

The foundation needs one transport/persistence-safe parameter envelope without building a generic property system.

Use:

~~~csharp
JsonElement Parameters
~~~

with the invariant:

~~~text
Parameters.ValueKind == JsonValueKind.Object
~~~

The base foundation knows only:

~~~text
TypeId
ActionSchemaVersion
JSON object parameters
~~~

A future registered action implementation will deserialize/validate its own typed configuration.

Examples:

~~~json
{
  "watts": 30
}
~~~

~~~json
{
  "path": "C:\\Tools\\Tool.exe",
  "arguments": "--foo"
}
~~~

~~~json
{
  "script": "Write-Output 'hello'"
}
~~~

Do not add:

- Dictionary<string, object>;
- reflection-based property editors;
- dynamic runtime invocation;
- a generic parameter-type DSL;
- a plugin loader.

JsonElement is only the opaque action-specific payload envelope.

## 3.6 Each action payload has its own schema version

The action definition carries:

~~~csharp
int SchemaVersion
~~~

with SchemaVersion >= 1.

This is the schema version for that Action Type's parameter object, not the future shortcuts.json root document version.

Example:

~~~text
TypeId = addon.tdp-preset
SchemaVersion = 1
Parameters = { watts: 30 }
~~~

If addon.tdp-preset later needs a breaking parameter change, its implementation can explicitly support/migrate version 1 vs version 2 without changing every other action family.

Do not invent migration code for versions that do not exist yet.

## 3.7 Definition and frontend presentation are different contracts

A persisted/editable tile definition contains action configuration.

A normal read-only frontend tile projection does NOT.

This matters especially for:

- inline PowerShell source;
- executable arguments;
- future sensitive or bulky configuration.

The normal Overlay projection should receive only what it needs to render and invoke by identity.

Conceptually:

~~~text
Definition
    TileId
    Title
    Action
        TypeId
        SchemaVersion
        Parameters

Runtime
    validates / resolves / projects

Frontend presentation
    TileId
    Title
    StatusText
    State
    Enabled
~~~

Do not send PowerShell source or EXE configuration to Overlay merely so the tile can be rendered.

Future execution from Overlay must be:

~~~text
ExecuteShortcut(TileId)
~~~

not:

~~~text
ExecuteShortcut(ActionSpec copied from Overlay)
~~~

Runtime must re-resolve the current authoritative action by TileId.

---

# 4. Required core domain contracts

Create a focused file such as:

~~~text
src/SteamInputAddonforClaw.Contracts/Shortcuts/ShortcutContracts.cs
~~~

Use names equivalent to the following unless the existing codebase strongly justifies a clearer name.

## 4.1 ShortcutActionSpec

Recommended shape:

~~~csharp
public sealed record ShortcutActionSpec(
    string TypeId,
    int SchemaVersion,
    JsonElement Parameters);
~~~

Contract:

- TypeId is stable and case-sensitive;
- non-empty / non-whitespace;
- no trimming/normalizing during load;
- SchemaVersion >= 1;
- Parameters must be a JSON object;
- base foundation does NOT decide whether the TypeId is currently executable;
- base foundation does NOT parse action-specific members.

Do not make TypeId an enum.

## 4.2 ShortcutTileDefinition

Recommended shape:

~~~csharp
public sealed record ShortcutTileDefinition(
    Guid TileId,
    string Title,
    ShortcutActionSpec Action);
~~~

Contract:

- TileId != Guid.Empty;
- title is required user-visible tile text;
- title may contain Unicode;
- title must not be null/empty/whitespace;
- action is required;
- current status/value does NOT belong here.

Do not add row/column/order fields.

Do not add current TDP/FPS/etc state here.

## 4.3 ShortcutDashboardDefinition

Recommended shape:

~~~csharp
public sealed record ShortcutDashboardDefinition(
    IReadOnlyList<ShortcutTileDefinition> Tiles)
{
    public static ShortcutDashboardDefinition Empty { get; } = new([]);
}
~~~

The list order is authoritative.

An empty dashboard is valid.

Do not hard-code a tile count.

Do not insert four placeholder tiles into this domain type.

---

# 5. Structural validation

Create one small pure validator in the same Shortcut contract area.

Suggested name:

~~~text
ShortcutDefinitionValidation
~~~

Provide a Validate method returning null on success and a short failure reason otherwise, following the existing FrontButtonMappingValidation / BackButtonMappingValidation style.

Validation must reject at minimum:

- null dashboard;
- null tile collection;
- null tile;
- Guid.Empty;
- duplicate TileId;
- null/empty/whitespace title;
- null action;
- null/empty/whitespace TypeId;
- leading/trailing whitespace in TypeId;
- SchemaVersion < 1;
- Parameters.ValueKind other than Object.

Validation must NOT reject merely because:

- TypeId is unknown;
- the future Runtime does not currently have an executor for it;
- action-specific members are unknown to the base validator.

Action-specific validation belongs to the future action registration/execution layer.

Do not add a general validation framework/interface hierarchy.

One small static validator is enough.

---

# 6. Frontend presentation contract

Replace the current fixed-slot shared Shortcut projection with a dynamic sanitized projection.

The durable frontend contract belongs under:

~~~text
src/SteamInputAddonforClaw.Contracts/Frontend/
~~~

A suggested file:

~~~text
ShortcutFrontendContracts.cs
~~~

## 6.1 Presentation state

Use a small presentation-only state enum equivalent to:

~~~csharp
public enum FrontendShortcutTileState
{
    Neutral,
    Active,
    Inactive,
    Unavailable,
}
~~~

Semantics:

~~~text
Neutral
    one-shot/stateless action, or no meaningful active/inactive state

Active
    action's target/state is currently active
    e.g. future TDP preset matches current authoritative TDP

Inactive
    action is available but its target/state is not currently active

Unavailable
    action cannot currently be used/projected
~~~

This is visual/presentation metadata only.

It is NOT action execution state and NOT a state machine.

Do not add Busy/Queued/Running lifecycle machinery in PR-A.

## 6.2 One projected tile

Recommended shape:

~~~csharp
public sealed record FrontendShortcutTile(
    Guid TileId,
    string Title,
    string? StatusText,
    FrontendShortcutTileState State,
    bool Enabled);
~~~

Examples of future projections:

~~~text
Title:  TDP
Status: 30 W
State:  Active

Title:  ClawHUD
Status: On
State:  Active

Title:  HDR Script
Status: null
State:  Neutral

Title:  Old Future Action
Status: Unsupported
State:  Unavailable
Enabled: false
~~~

Do not put TypeId, JsonElement Parameters, PowerShell source, executable path, or arguments into this normal render projection.

## 6.3 Dashboard snapshot

Recommended shape:

~~~csharp
public sealed record FrontendShortcutDashboardSnapshot(
    bool Available,
    IReadOnlyList<FrontendShortcutTile> Tiles,
    string? FailureMessage = null)
{
    public static FrontendShortcutDashboardSnapshot Unavailable(string? message = null) =>
        new(false, [], message);
}
~~~

Important:

~~~text
Available + zero tiles
    = valid empty user dashboard

Unavailable
    = Runtime cannot currently produce a trustworthy Shortcut projection
~~~

Do not use an empty tile list itself as an availability signal.

The list order is the render order.

---

# 7. Remove the fixed four-slot shared product contract

The following shared product concepts must no longer be foundation authority:

~~~text
AddonQuickSettingsShortcutSlotId
Slot1
Slot2
Slot3
Slot4
AddonQuickSettingsShortcutSlot
static four-slot AddonQuickSettingsShortcutContract.Create()
~~~

Because the repository is unreleased, do not preserve obsolete public contract types solely for compatibility.

Delete or replace:

~~~text
src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsShortcutContracts.cs
~~~

as appropriate.

Do not add aliases/deprecated wrappers for the old fixed slot model.

---

# 8. Minimal Overlay adaptation only

PR-A is NOT the dynamic Shortcut UI PR.

However removing the old shared fixed-slot contract must not leave Overlay depending on obsolete product types.

Make the smallest compile-preserving change.

## 8.1 Keep current visual POC unchanged

The current Shortcut tab may continue to display exactly four temporary:

~~~text
Slot 1 / Unassigned
Slot 2 / Unassigned
Slot 3 / Unassigned
Slot 4 / Unassigned
~~~

tiles.

Those four placeholders must become Overlay-local POC data, not a shared domain contract.

Clearly comment them as temporary UI shell data.

Do not publish them as Runtime/frontend authoritative Shortcut state.

## 8.2 Remove Slot enum dependency from OverlayShortcutSelection

The current selection helper should no longer depend on AddonQuickSettingsShortcutSlotId.

For this temporary 2×2 POC, prefer a tiny index-based implementation:

~~~text
SelectedIndex = 0..3
~~~

with the same bounded/no-wrap behavior.

Keep Up/Down/Left/Right/Reset/Select semantics.

Do NOT generalize it into the final variable-size navigation model in PR-A.

The later Overlay dynamic-grid PR will replace this fixed POC selection geometry.

This is deliberate:

> remove obsolete shared product identity now, but do not prematurely implement final UI navigation.

## 8.3 Keep current UI behavior

No visual change required.

No new Runtime request.

No A-button execution.

No dynamic tile rendering.

No scrolling/paging redesign.

---

# 9. Persistence direction — design locked, implementation deferred

PR-A must remain compatible with a later dedicated:

~~~text
shortcuts.json
~~~

store.

Do NOT add Shortcut collection to AppSettings, SettingsStore, or settings.json.

The future store belongs under the same canonical SteamInputAddonforClaw-Data root as profiles.json.

The future persistence PR should follow ProfileStore principles:

- root schemaVersion;
- first-run empty document;
- malformed file preserved;
- unsupported newer schema preserved;
- unreadable file not overwritten;
- explicit CanSafelyReplace-style result;
- atomic same-directory temp-file replace;
- Runtime-owned, UI-independent;
- no automatic downgrade/destructive normalization of unknown actions.

PR-A does NOT need to add AddonDataPaths.ShortcutsPath, ShortcutStore, or ShortcutDocument.

The preferred PR-A scope is contracts/tests plus the minimal Overlay POC compile adaptation.

---

# 10. Action extensibility acceptance examples

PR-A must prove that the foundation can structurally represent very different future actions without adding action-specific members to the base contract.

Tests should construct at least these examples.

## 10.1 Future TDP preset

~~~csharp
new ShortcutActionSpec(
    "addon.tdp-preset",
    1,
    JsonSerializer.SerializeToElement(new { watts = 30 }))
~~~

This does NOT implement TDP execution.

It proves that a parameterized Addon-native action fits the contract.

## 10.2 Future executable

~~~csharp
new ShortcutActionSpec(
    "system.executable",
    1,
    JsonSerializer.SerializeToElement(new
    {
        path = @"C:\Tools\Tool.exe",
        arguments = "--example"
    }))
~~~

No Process.Start in PR-A.

## 10.3 Future PowerShell

~~~csharp
new ShortcutActionSpec(
    "system.powershell",
    1,
    JsonSerializer.SerializeToElement(new
    {
        script = "Write-Output 'hello'"
    }))
~~~

No PowerShell process/runspace in PR-A.

## 10.4 Unknown future action

~~~csharp
new ShortcutActionSpec(
    "future.vendor.new-action",
    7,
    JsonSerializer.SerializeToElement(new { anything = "value" }))
~~~

The base structural validator must accept this shape.

Whether it is supported/executable is a future Runtime registry concern.

---

# 11. Definition vs presentation acceptance example

Prove the two layers are intentionally different.

Definition:

~~~text
TileId
Title
Action
    TypeId
    SchemaVersion
    Parameters
~~~

Presentation:

~~~text
TileId
Title
StatusText
State
Enabled
~~~

A test/reflection assertion should make it difficult to accidentally leak Action payload into the render contract.

For example assert FrontendShortcutTile has no property named:

~~~text
Action
Parameters
ParametersJson
Script
ExecutablePath
Arguments
~~~

Do not build a security framework around this test.

The point is only to lock the clean frontend boundary.

---

# 12. No Action Registry in PR-A

Do not introduce:

~~~text
IShortcutAction
IShortcutActionHandler
ShortcutActionRegistry
ShortcutActionManager
ShortcutExecutor
ShortcutStateProvider
ShortcutPlugin
MEF
reflection discovery
DI registration tables
~~~

Those require real Runtime behavior and belong to the next phase.

PR-A defines the data seam they will consume.

This avoids creating a framework based only on hypothetical future actions.

---

# 13. No action-specific config classes in PR-A

Do not add production classes such as:

~~~text
TdpPresetShortcutConfig
ExecutableShortcutConfig
PowerShellShortcutConfig
UrlShortcutConfig
~~~

yet.

The test examples may serialize anonymous objects into JsonElement.

Typed config classes should be added only when the corresponding real action implementation exists.

This keeps PR-A extensible without pre-building unused abstractions.

---

# 14. No execution contract yet

Do not add a frontend method such as ExecuteShortcutAsync in PR-A.

Do not modify:

~~~text
IFrontendService
FrontendWire
OverlayWire
protocol versions
Runtime command dispatch
~~~

Execution will be introduced only when Runtime owns:

- persisted definitions;
- supported-action resolution;
- action-specific validation;
- execution dispatch.

The final rule is already decided:

~~~text
frontend sends TileId
Runtime re-resolves authoritative definition
Runtime executes
~~~

but PR-A does not need a dead RPC method before an executor exists.

---

# 15. No editor mutation contract yet

Do not add production RPCs/intents for create/delete/update/reorder until the Runtime persistence owner exists.

The domain ordering rule is established now:

~~~text
collection order == layout order
~~~

The later persistence/editor PR can add narrow typed mutations based on stable TileId.

Do not build an unused generic CRUD API in PR-A.

---

# 16. No fixed column-count contract

The future Main App editor will provide a dedicated Shortcut editing sheet and drag/drop layout.

The future Overlay will use compact near-square tiles.

PR-A must NOT decide:

~~~text
2 columns
3 columns
4 columns
tile pixel width
tile aspect ratio
page count
scroll direction
~~~

Those are presentation choices.

The domain remains an ordered list.

This prevents another data migration if the 720-DIP Overlay later settles on a different number of columns.

---

# 17. Current-state projection principles for later actions

PR-A only defines the presentation shape, but the following ownership rule is part of the foundation.

Future state projection must use existing authoritative feature state.

Examples:

~~~text
TDP preset tile
    current status/value comes from Runtime TDP authority

ClawHUD tile
    status comes from Runtime ClawHUD state

Battery Limit tile
    status comes from Runtime battery-limit state
~~~

Do not create Shortcut-owned duplicate state like:

~~~text
ShortcutTdpCurrentValue
ShortcutHudEnabled
ShortcutBatteryState
~~~

Shortcut projects existing truth.

For stateless external actions such as EXE, PowerShell, or URL, State = Neutral and optional/null StatusText are valid.

---

# 18. Failure / unsupported behavior for future Runtime

Although execution is deferred, lock these product rules now so the contract does not force destructive behavior later.

## Unknown action type

~~~text
definition remains preserved
frontend projection may show:
    Enabled = false
    State = Unavailable
    StatusText = "Unsupported"
~~~

Do not delete the tile.

## Known type with unsupported action schema version

Same principle:

~~~text
preserve definition
fail execution/projection closed
do not reinterpret parameters
~~~

## Malformed base definition

The future persistence layer must treat malformed persisted structure as an unsafe document and preserve the original file, following the ProfileStore precedent.

Do not silently discard only the malformed tile and rewrite the rest unless a future explicit recovery policy is designed.

---

# 19. Logging / privacy direction

PR-A does not execute or persist actions, but future code must not casually log full action parameters.

Especially do not log:

- PowerShell script contents;
- full command arguments;
- arbitrary user-entered action payload JSON.

Foundation types should remain ordinary records, but later production diagnostics should prefer:

~~~text
TileId
TypeId
SchemaVersion
outcome/reason
~~~

rather than full Parameters.

Do not add custom redaction infrastructure in PR-A.

---

# 20. Tests

Add a focused contract test file such as:

~~~text
tests/SteamInputAddonforClaw.Tests/ShortcutFoundationContractTests.cs
~~~

## 20.1 Dynamic cardinality

Prove valid dashboards can contain:

~~~text
0 tiles
1 tile
4 tiles
more than 4 tiles
~~~

No fixed four-slot product rule may remain in the shared foundation.

Do not introduce an arbitrary max tile count in PR-A merely for a hypothetical future UI.

Transport/persistence size bounds belong at their actual boundary.

## 20.2 Stable identity

Prove:

- non-empty GUID accepted;
- Guid.Empty rejected;
- duplicate TileId rejected;
- reorder does not require changing TileId.

No need to add a reorder helper if persistence mutation does not exist yet.

A test can simply construct the same definitions in a different list order and assert their IDs remain unchanged.

## 20.3 Order is the only layout contract

Use reflection/source tests as appropriate to prove ShortcutTileDefinition does not add Row, Column, X, Y, Order, or Index.

Do not over-test compiler implementation details.

The goal is to lock the absence of duplicate layout authority.

## 20.4 Action extensibility

Prove the four examples in section 10 are structurally valid:

- TDP preset;
- executable;
- PowerShell;
- unknown future action.

No executor required.

## 20.5 Structural validation

Reject:

- null dashboard;
- null collection if constructible/deserialized;
- null tile;
- empty GUID;
- duplicate GUID;
- blank title;
- null action;
- blank TypeId;
- TypeId with leading/trailing whitespace;
- schema version 0/negative;
- undefined/default JsonElement;
- array/string/number/null parameters.

Accept any object-shaped parameters for a structurally valid unknown TypeId.

## 20.6 Frontend projection

Prove:

- empty available snapshot is distinct from unavailable snapshot;
- order is retained;
- title/status/state/enabled round-trip;
- projected tile carries TileId;
- projection does not carry action payload/config.

## 20.7 Legacy fixed-slot removal

Update/remove existing tests that assert Slot1, Slot2, Slot3, Slot4, or AddonQuickSettingsShortcutSlotId.

Add a source/contract regression proving the shared Contracts project no longer declares the fixed slot enum.

Do not require the temporary Overlay-local POC labels to disappear in this PR.

## 20.8 Overlay POC regression

Keep tests proving current behavior remains:

- four visible local sample tiles;
- entering Shortcut selects the first tile;
- 2×2 bounded/no-wrap temporary navigation;
- pointer selection still works;
- A still does nothing;
- no Runtime/wire call added.

Update tests from shared Slot enum identity to local selected-index semantics.

---

# 21. Files expected to change

Likely:

~~~text
src/SteamInputAddonforClaw.Contracts/Shortcuts/ShortcutContracts.cs                    [new]
src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutFrontendContracts.cs             [new]

src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsShortcutContracts.cs   [delete/replace]

src/SteamInputAddonforClaw.Overlay/OverlayShortcutSelection.cs                          [minimal]
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs                               [minimal]

tests/SteamInputAddonforClaw.Tests/ShortcutFoundationContractTests.cs                   [new]
tests/SteamInputAddonforClaw.Tests/AddonQuickSettingsSurfaceParityTests.cs              [update]
tests/SteamInputAddonforClaw.UiTests/*Shortcut*                                         [update only as required]
~~~

Do not change files outside this area unless compilation/tests prove they genuinely reference the old fixed-slot contract.

In particular, avoid changing:

~~~text
SettingsStore
AppSettings
ProfileStore
AddonDataPaths
FrontendWire
OverlayWire
Runtime controller owner
TDP implementation
ClawHUD implementation
~~~

in PR-A.

---

# 22. Explicit non-goals

Do NOT implement:

- shortcuts.json;
- ShortcutStore;
- Runtime action registry;
- Runtime action executor;
- EXE execution;
- PowerShell execution;
- URL execution;
- TDP preset execution;
- FPS preset execution;
- current-state polling;
- Main App Shortcut editor;
- drag/drop;
- add/edit/delete UI;
- final dynamic Overlay grid;
- final column count;
- paging;
- search;
- icons;
- custom colors;
- controller chord assignment;
- macros;
- action sequencing;
- nested/folder tiles;
- conditional actions;
- scheduled actions;
- plugin discovery;
- generic scripting host;
- protocol/RPC additions;
- protocol version bumps.

---

# 23. Overengineering guard

The purpose of PR-A is to make future action additions cheap without building the future system today.

Required small foundation:

~~~text
3 domain records
1 small structural validator
1 frontend tile record
1 frontend dashboard snapshot
1 small presentation enum
minimal removal of fixed Slot1..4 shared identity
tests
~~~

Do not turn this into:

~~~text
Command Bus
Mediator
Action Context
Action Result hierarchy
Action factory
Action provider interfaces
Action registry
plugin framework
state machine
event bus
generic persistence repository
~~~

The future Runtime PR will introduce the smallest executor/registry only after real action implementations are selected.

---

# 24. Architecture acceptance criteria

## Domain

- [ ] Core Shortcut types live outside the Overlay-specific namespace.
- [ ] Guid TileId is the stable tile identity.
- [ ] Tile collection order is the only layout authority.
- [ ] No fixed slot count exists.
- [ ] No row/column/XY/order field exists on the tile.
- [ ] Action TypeId is a string, not an enum.
- [ ] Action TypeId is case-sensitive and not silently normalized.
- [ ] Action SchemaVersion is explicit and >= 1.
- [ ] Action Parameters are one JSON object.
- [ ] Base validation does not reject unknown TypeId solely because it is unknown.
- [ ] Duplicate TileId fails validation.
- [ ] Empty dashboard is valid.

## Frontend projection

- [ ] Projection contains TileId/title/status/state/enabled.
- [ ] Projection does not carry action parameters.
- [ ] Available empty dashboard is representable.
- [ ] Unavailable dashboard is distinct.
- [ ] List order is preserved.

## Legacy POC

- [ ] Shared AddonQuickSettingsShortcutSlotId is removed.
- [ ] Shared Slot1..Slot4 product is removed.
- [ ] Current four-tile Overlay POC may remain visually unchanged using local data.
- [ ] Temporary Overlay navigation no longer depends on the deleted slot enum.
- [ ] No final dynamic-grid implementation is introduced.

## Ownership / scope

- [ ] No persistence added.
- [ ] No executor/registry added.
- [ ] No frontend RPC added.
- [ ] No wire version changed.
- [ ] No AppSettings member added.
- [ ] No Full1902 lifecycle path changed.
- [ ] No duplicate TDP/HUD/etc state authority introduced.

---

# 25. Automated validation

Run at minimum:

~~~text
dotnet build SteamInputAddonforClaw.slnx -c Debug --no-restore
dotnet test SteamInputAddonforClaw.slnx -c Debug --no-restore
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Debug --no-restore
git diff --check
~~~

Also run the normal repository CI.

No physical-device test is required for PR-A because there is:

- no execution;
- no persistence;
- no Runtime mutation;
- no controller lifecycle change;
- no intended visual behavior change.

---

# 26. Follow-up roadmap after PR-A

This roadmap explains why PR-A stops where it does. Do not implement these phases in PR-A.

## PR-B — Runtime-owned Shortcut document persistence

Add:

~~~text
shortcuts.json
ShortcutDocument
ShortcutStore
AddonDataPaths.ShortcutsPath
safe load/save result
Runtime owner
~~~

Follow ProfileStore safety semantics.

## PR-C — Action registry/execution seam + first real actions

Add the smallest Runtime registry needed by actual supported actions.

Likely first external actions:

~~~text
system.executable
system.powershell
system.url
~~~

and a small number of useful Addon-native actions.

Every Addon-native action must reuse existing typed feature mutation/application paths.

Unknown/unsupported action stays preserved and projects unavailable.

## PR-D — Main App Shortcut editor

Dedicated Shortcut editing sheet/page:

- add;
- edit;
- delete;
- drag/drop reorder;
- action-specific editor surfaces;
- live layout preview.

Main App edits Runtime-owned definitions.

It is not persistence authority.

## PR-E — Overlay dynamic grid

Replace temporary 2×2 POC with:

- dynamic tile collection;
- compact near-square tiles;
- title;
- current status/value;
- controller navigation based on actual rendered column count;
- A/Click -> execute by TileId;
- no editing in Overlay.

---

# 27. TDP preset as the future architecture proof

TDP preset is an important future validation case because it proves Shortcut is not merely an external launcher.

The desired later path is:

~~~text
Tile
    Title = "TDP 30 W"
    Action.TypeId = "addon.tdp-preset"
    Parameters = { watts: 30 }

Overlay
    A
      ↓
ExecuteShortcut(TileId)
      ↓
Runtime resolves current definition
      ↓
registered addon.tdp-preset implementation
      ↓
existing Runtime TDP authority/mutation path
      ↓
authoritative TDP state changes
      ↓
Shortcut presentation refresh
    Status = "30 W"
    State = Active
~~~

Never:

~~~text
Shortcut executor
    -> direct EC/WMI TDP write
~~~

The same principle applies to every future Addon-native action.

---

# 28. Final foundation principle

The Shortcut system must be extensible in data, not prebuilt in machinery.

PR-A target:

~~~text
stable tile identity
        +
ordered collection
        +
extensible action envelope
        +
sanitized live frontend projection
~~~

Later persistence, execution, editor, and Overlay work can build on that without changing the core model.

The current fixed 2×2 Slot1..Slot4 shell is a temporary UI POC.

It must not become the product architecture.
