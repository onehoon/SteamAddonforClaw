
# Work Order — Shortcut Foundation PR-B: Runtime-Owned shortcuts.json Persistence

**Date:** 2026-09-24  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**Target branch:** main  
**Reviewed main baseline:** 786008738673745ebbe605e834af5b7970e5d852  
**Depends on:** PR #593 / Shortcut Foundation PR-A  
**Scope:** persistence only

---

## 0. Goal

Add one safe, dedicated persistence domain for the dynamic Shortcut foundation introduced by PR #593.

Target durable file:

~~~text
SteamInputAddonforClaw-Data/shortcuts.json
~~~

PR-B adds only:

~~~text
ShortcutDocument
ShortcutStore
ShortcutLoadStatus
ShortcutLoadResult
AddonDataPaths.ShortcutsPath
load/save validation
atomic replacement
tests
~~~

Do not add execution, action registry, frontend RPC, Main App editor, drag/drop, dynamic Overlay tiles, or protocol changes.

---

## 1. Mandatory review before implementation

Read the latest versions of:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/work-order/SHORTCUT_FOUNDATION_PR_A_DYNAMIC_ACTION_TILE_CONTRACTS_WORK_ORDER_2026-09-24.md

src/SteamInputAddonforClaw.Contracts/Shortcuts/ShortcutContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutFrontendContracts.cs

src/SteamInputAddonforClaw/Profiles/ProfileDocument.cs
src/SteamInputAddonforClaw/Profiles/ProfileStore.cs
src/SteamInputAddonforClaw/Install/AddonDataPaths.cs

tests/SteamInputAddonforClaw.Tests/ProfileStoreTests.cs
tests/SteamInputAddonforClaw.Tests/AddonDataPathsTests.cs
~~~

Shortcut persistence must remain completely independent of Full1902 controller ownership.

Do not touch PID1901/PID1902, HidHide, VIIPER, DirectInput, suspend/resume, shutdown/restart, or routing recovery.

---

## 2. Existing PR-A contract is authoritative

PR #593 established:

~~~text
ShortcutDashboardDefinition
└─ ordered ShortcutTileDefinition[]
    ├─ Guid TileId
    ├─ Title
    └─ ShortcutActionSpec
        ├─ string TypeId
        ├─ int SchemaVersion
        └─ JsonElement Parameters
~~~

Keep these rules:

- TileId is stable identity.
- Collection order is layout order.
- No Slot1..SlotN identity.
- No row/column/X/Y/order field on each tile.
- TypeId is case-sensitive extensible string.
- Unknown TypeId is structurally valid.
- Parameters is an opaque JSON object.
- Action-specific validation is not the store's job.

Do not change the PR-A domain model unless compilation reveals a real issue.

---

## 3. Separate persistence domain

Do not place Shortcut data in:

~~~text
AppSettings
SettingsStore
settings.json
profiles.json
~~~

Shortcut data can later contain arbitrary-length collections, executable paths, arguments, multiline PowerShell, and action payloads unknown to older builds.

It therefore gets its own file and failure boundary.

Use ProfileStore only as the persistence-safety precedent.

---

## 4. ShortcutDocument

Create:

~~~text
src/SteamInputAddonforClaw/Shortcuts/ShortcutDocument.cs
~~~

Namespace:

~~~text
SteamInputAddonforClaw.Shortcuts
~~~

Recommended shape:

~~~csharp
public sealed record ShortcutDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public ShortcutDashboardDefinition Dashboard { get; init; } =
        ShortcutDashboardDefinition.Empty;
}
~~~

Persisted v1 shape:

~~~json
{
  "schemaVersion": 1,
  "dashboard": {
    "tiles": [
      {
        "tileId": "11111111-2222-3333-4444-555555555555",
        "title": "Example",
        "action": {
          "typeId": "future.example",
          "schemaVersion": 1,
          "parameters": {
            "value": 30
          }
        }
      }
    ]
  }
}
~~~

Use camelCase JSON names.

Do not persist frontend status/state.

Do not persist four sample slots.

---

## 5. Two schema levels are intentional

Keep these distinct:

~~~text
ShortcutDocument.SchemaVersion
    base shortcuts.json structure

ShortcutActionSpec.SchemaVersion
    parameter schema for one action TypeId
~~~

Example:

~~~text
document schemaVersion = 1
action TypeId = addon.tdp-preset
action SchemaVersion = 3
~~~

is structurally legal at the persistence layer.

Whether action schema version 3 is executable belongs to the future action registry.

Do not add migration machinery for nonexistent root versions.

---

## 6. Canonical data path

Update:

~~~text
src/SteamInputAddonforClaw/Install/AddonDataPaths.cs
~~~

Add equivalent members:

~~~csharp
internal static string ShortcutsPath =>
    ResolveShortcutsPath(VelopackAppPaths.RootAppDirectory);

internal static string ResolveShortcutsPath(string rootAppDirectory) =>
    Path.Combine(ResolveDataRoot(rootAppDirectory), "shortcuts.json");
~~~

Required location:

~~~text
SteamInputAddonforClaw-Data/shortcuts.json
~~~

It must be inside the existing persistent data root and outside the versioned install directory.

Do not create another root resolver.

The existing full-reset deletion of the whole data root should naturally remove it.

---

## 7. ShortcutStore

Create:

~~~text
src/SteamInputAddonforClaw/Shortcuts/ShortcutStore.cs
~~~

Recommended constructor:

~~~csharp
public ShortcutStore(string shortcutsPath)
~~~

The store must:

- live in the Runtime project;
- have no WinUI dependency;
- have no Overlay dependency;
- have no frontend transport dependency;
- receive its path from the caller;
- not discover Velopack paths internally.

A null path must fail immediately.

Production callers will later use AddonDataPaths.ShortcutsPath.

Tests use a temporary explicit path.

---

## 8. Load result

Create:

~~~csharp
public enum ShortcutLoadStatus
{
    Loaded,
    NotFound,
    Malformed,
    UnsupportedSchemaVersion,
    ReadFailure
}
~~~

and:

~~~csharp
public sealed record ShortcutLoadResult(
    ShortcutDocument Document,
    ShortcutLoadStatus Status)
{
    public bool CanSafelyReplace =>
        Status is ShortcutLoadStatus.Loaded
            or ShortcutLoadStatus.NotFound;
}
~~~

Semantics:

### Loaded

The file was readable, schema version supported, deserialized successfully, Dashboard was non-null, and ShortcutDefinitionValidation accepted the Dashboard.

### NotFound

The file does not exist.

Return a fresh current ShortcutDocument.

This is normal first run.

Load must not create shortcuts.json.

CanSafelyReplace is true.

### Malformed

Use for:

- invalid JSON;
- missing schemaVersion;
- non-integer schemaVersion;
- schemaVersion less than 1;
- deserialize failure;
- null required structures;
- ShortcutDefinitionValidation failure.

Return a fresh empty current document for caller safety, but CanSafelyReplace is false.

Never rewrite the original file during Load.

### UnsupportedSchemaVersion

Use when persisted document schemaVersion is greater than CurrentSchemaVersion.

Do not reinterpret it as v1.

Return a fresh non-authoritative current document.

CanSafelyReplace is false.

### ReadFailure

Use when an existing file cannot be read due to I/O/access/security failure.

Do not treat it as first run.

CanSafelyReplace is false.

---

## 9. Load algorithm

Use the simple ProfileStore pattern:

~~~text
file absent
    -> NotFound

read text
    failure
        -> ReadFailure

parse minimal JSON root
    failure
        -> Malformed

read schemaVersion
    missing / wrong type / < 1
        -> Malformed

schemaVersion > CurrentSchemaVersion
    -> UnsupportedSchemaVersion

deserialize ShortcutDocument from the original text

validate:
    document != null
    document.Dashboard != null
    ShortcutDefinitionValidation.Validate(document.Dashboard) == null

failure
    -> Malformed

success
    -> Loaded
~~~

Do not partially salvage individual malformed tiles.

If the base document cannot be trusted, preserve the original and fail closed.

---

## 10. JsonElement lifetime

PR-A uses JsonElement for opaque action parameters.

Avoid retaining JsonElement values from the temporary JsonDocument used only for root-schema inspection.

Preferred implementation:

1. inspect schemaVersion with a short-lived JsonDocument;
2. dispose it;
3. deserialize ShortcutDocument separately from the original JSON string with JsonSerializer.

Add a test proving that after ShortcutStore.Load returns, nested Parameters can still be read without ObjectDisposedException.

Do not add a recursive clone framework unless the normal serializer path actually requires one.

---

## 11. Serializer options

ShortcutStore should own small local JsonSerializerOptions:

~~~text
WriteIndented = true
PropertyNamingPolicy = JsonNamingPolicy.CamelCase
~~~

No enum converter is needed in v1.

Do not:

- normalize TypeId case;
- trim TypeId;
- deserialize Parameters into Dictionary<string, object>;
- add action-specific converters.

ShortcutDefinitionValidation remains the base structural validator.

---

## 12. Unknown action preservation

This is mandatory.

The store must load and save a tile such as:

~~~text
TypeId = future.vendor.unknown-action
Action SchemaVersion = 99
Parameters = arbitrary JSON object
~~~

as long as the base tile/action structure is valid.

Do not:

- consult an action registry;
- reject unknown TypeId;
- remove unsupported tiles;
- rewrite TypeId;
- downgrade action SchemaVersion;
- parse action-specific Parameters.

The future Runtime registry may project such a tile as Unsupported.

Persistence must preserve it.

---

## 13. Save contract

Provide:

~~~csharp
public void Save(ShortcutDocument document)
~~~

Before touching the canonical file:

1. document must be non-null;
2. document.SchemaVersion must equal CurrentSchemaVersion;
3. Dashboard must be non-null;
4. ShortcutDefinitionValidation must accept Dashboard.

If validation fails:

- throw a clear ArgumentException or InvalidDataException;
- do not create shortcuts.json;
- do not replace an existing valid shortcuts.json.

Do not silently repair invalid in-memory data.

---

## 14. Atomic replacement

Use the same simple pattern as ProfileStore:

~~~text
create parent directory
serialize complete document
write shortcuts.json.tmp
move temp -> shortcuts.json with overwrite
~~~

A failure before final replacement must leave the last valid canonical file unchanged.

After successful Save, the .tmp file must not remain.

Do not add:

- journal;
- backup rotation;
- transaction manager;
- retry manager;
- global file lock service.

The supported product scope is one Windows user / one interactive session.

---

## 15. Logging and payload privacy

Use AppLog category Shortcuts.

Useful log metadata:

~~~text
Path
Status
TileCount
DocumentSchemaVersion
SupportedSchemaVersion
validation reason
~~~

Never log:

- full Parameters JSON;
- PowerShell source;
- executable arguments;
- arbitrary user payload.

No redaction framework is needed in PR-B.

Just do not log the payload.

---

## 16. Do not wire AddonProcessHost yet

Do not add an unused field such as:

~~~text
private readonly ShortcutStore _shortcutStore;
~~~

to AddonProcessHost.

There is currently:

- no Shortcut Runtime coordinator;
- no executor;
- no editor mutation;
- no frontend capture.

Runtime-owned in PR-B means the store lives in the Runtime project and frontends never access the file.

The later live Shortcut Runtime PR should instantiate it when there is an actual owner that uses it.

---

## 17. No frontend or Overlay changes

Do not add:

~~~text
CaptureShortcutDashboardAsync
CreateShortcutAsync
UpdateShortcutAsync
DeleteShortcutAsync
MoveShortcutAsync
ExecuteShortcutAsync
~~~

Do not modify:

~~~text
IAddonFrontendControl
FrontendWire
OverlayWire
Overlay Shortcut POC
~~~

Do not bump any protocol version.

The existing four Overlay sample tiles remain local temporary UI data and are not persisted.

---

## 18. ShortcutStoreTests

Create:

~~~text
tests/SteamInputAddonforClaw.Tests/ShortcutStoreTests.cs
~~~

Use one unique temporary directory per test fixture, matching ProfileStoreTests style.

Required tests:

### First run

File absent:

~~~text
Status = NotFound
CanSafelyReplace = true
CurrentSchemaVersion
empty Dashboard
shortcuts.json still absent
~~~

### Empty round trip

Save and load an empty document.

Assert Loaded, current schema, empty Dashboard, no temp file.

### Dynamic ordered round trip

Persist more than four tiles.

Verify:

- all TileIds;
- titles;
- exact collection order;
- no fixed count.

### Representative action round trips

Persist and reload at least:

~~~text
addon.tdp-preset
system.executable
system.powershell
future.vendor.unknown-action
~~~

Verify exact TypeId casing, action SchemaVersion, and semantic Parameters preservation.

### JsonElement lifetime

After Load returns, access nested Parameters members successfully.

### Malformed JSON

Invalid JSON returns Malformed, CanSafelyReplace false, original file unchanged.

### Root schema

Cover:

- missing schemaVersion;
- string schemaVersion;
- zero;
- negative;
- newer-than-current.

Newer version must be UnsupportedSchemaVersion, not Malformed.

### Explicit null structure

Cover at least:

~~~text
dashboard: null
dashboard.tiles: null
tile entry: null
tile.action: null
~~~

Each is Malformed and preserves the original.

### PR-A structural validation through persistence

Persist examples with:

- Guid.Empty;
- duplicate TileId;
- blank title;
- blank TypeId;
- TypeId leading/trailing whitespace;
- action SchemaVersion less than 1;
- Parameters null/string/array/number.

Each must return Malformed and CanSafelyReplace false.

Unknown TypeId by itself must remain valid.

### Read failure

Use the same realistic exclusive-file pattern already used by ProfileStoreTests when practical.

Existing unreadable file:

~~~text
-> ReadFailure
-> CanSafelyReplace = false
~~~

### Invalid Save cannot destroy valid file

1. Save one valid document.
2. Capture its canonical file contents.
3. Attempt Save with an invalid document.
4. Assert Save throws.
5. Assert canonical shortcuts.json is unchanged.

### Atomic temp behavior

Prove:

- successful Save leaves no .tmp;
- an abandoned temp file alone does not alter the canonical file.

Do not add artificial crash/fault-injection infrastructure.

---

## 19. AddonDataPaths tests

Update:

~~~text
tests/SteamInputAddonforClaw.Tests/AddonDataPathsTests.cs
~~~

Verify:

~~~text
C:\Users\Test\AppData\Local\SteamInputAddonforClaw-Data\shortcuts.json
~~~

and prove it is:

- inside the canonical data root;
- outside the install root.

Do not change existing settings.json or profiles.json locations.

---

## 20. Expected files

Expected production changes:

~~~text
src/SteamInputAddonforClaw/Shortcuts/ShortcutDocument.cs
src/SteamInputAddonforClaw/Shortcuts/ShortcutStore.cs
src/SteamInputAddonforClaw/Install/AddonDataPaths.cs
~~~

Expected tests:

~~~text
tests/SteamInputAddonforClaw.Tests/ShortcutStoreTests.cs
tests/SteamInputAddonforClaw.Tests/AddonDataPathsTests.cs
~~~

Avoid changing unless compilation genuinely requires it:

~~~text
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Settings/AppSettings.cs
src/SteamInputAddonforClaw/Settings/SettingsStore.cs
src/SteamInputAddonforClaw/Profiles/*
src/SteamInputAddonforClaw.FrontendTransport/*
src/SteamInputAddonforClaw.Overlay/*
~~~

---

## 21. Explicit non-goals

Do NOT implement:

- action registry;
- action handler interfaces;
- Shortcut executor;
- TDP preset execution;
- FPS preset execution;
- ClawHUD Shortcut execution;
- Process.Start;
- PowerShell execution;
- URL execution;
- media action;
- keyboard action;
- frontend projection wiring;
- frontend mutation;
- wire changes;
- Main App Shortcut editor;
- drag/drop;
- dynamic Overlay grid;
- status polling;
- controller chords;
- macros;
- action chains;
- conditions;
- schedules;
- folders/pages;
- sync;
- encryption;
- migration registry;
- generic JSON repository.

---

## 22. Overengineering guard

PR-B should stay approximately:

~~~text
1 document record
1 load-status enum
1 load-result record
1 store
1 path
focused tests
~~~

Do not add repository interfaces, generic persistence abstractions, factories, managers, state machines, or file watchers.

ProfileStore already proves the direct-file-store pattern is adequate.

---

## 23. Acceptance checklist

- [ ] ShortcutDocument.CurrentSchemaVersion is 1.
- [ ] ShortcutDocument owns one non-null Dashboard.
- [ ] shortcuts.json is under the canonical -Data root.
- [ ] Missing file is NotFound and does not create a file.
- [ ] Valid v1 is Loaded.
- [ ] Malformed base data is Malformed.
- [ ] Newer root schema is UnsupportedSchemaVersion.
- [ ] ReadFailure is distinct from NotFound.
- [ ] Only Loaded and NotFound are CanSafelyReplace.
- [ ] ShortcutDefinitionValidation is enforced on load and save.
- [ ] Unknown TypeId is preserved.
- [ ] Unknown action SchemaVersion is preserved by the base store.
- [ ] More than four tiles round-trip.
- [ ] Tile order and GUID identity round-trip.
- [ ] TypeId case is preserved.
- [ ] Parameters remain semantically intact.
- [ ] Parameters remain readable after Load returns.
- [ ] Invalid Save cannot replace the last valid file.
- [ ] Save uses same-directory .tmp then overwrite move.
- [ ] Successful Save leaves no .tmp.
- [ ] No action payload is logged.
- [ ] No AppSettings member is added.
- [ ] No AddonProcessHost dormant store field is added.
- [ ] No frontend RPC or wire version changes.
- [ ] No Overlay change.
- [ ] No Full1902 lifecycle change.

---

## 24. Automated validation

Run at minimum:

~~~text
dotnet build SteamInputAddonforClaw.slnx -c Debug --no-restore
dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore

dotnet test SteamInputAddonforClaw.slnx -c Debug --no-restore
dotnet test SteamInputAddonforClaw.slnx -c Release --no-restore

dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Debug --no-restore

git diff --check
~~~

Run normal repository CI before merge.

No physical-device validation is required for PR-B.

---

## 25. Follow-up

After PR-B, the next phase should add the first live Runtime Shortcut owner and the smallest execution/registry seam needed by real actions.

Expected later flow:

~~~text
shortcuts.json
    ↓
ShortcutStore
    ↓
Runtime Shortcut owner
    ├─ resolve supported action
    ├─ project live state
    └─ execute by TileId
    ↓
Frontend / Overlay
~~~

Execution must eventually use:

~~~text
ExecuteShortcut(TileId)
~~~

Runtime re-resolves the current authoritative stored definition.

Overlay must never send executable paths, PowerShell source, or other action payload as execution authority.

---

## 26. Final principle

PR-A defined the extensible Shortcut model.

PR-B gives it one safe durable home.

Keep this PR limited to:

~~~text
safe Runtime persistence
+ data preservation
+ explicit failure semantics
~~~

Execution and UI come after the persistence foundation is trustworthy.
