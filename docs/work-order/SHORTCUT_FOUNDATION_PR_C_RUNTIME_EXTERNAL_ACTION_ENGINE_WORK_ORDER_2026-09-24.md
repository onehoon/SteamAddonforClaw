
# Work Order — Shortcut Foundation PR-C: Runtime Owner + First External Action Engine

**Date:** 2026-09-24  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**Target branch:** main  
**Reviewed main baseline:** 969afc4f804104a7163d5c065af363b0b96daf28  
**Depends on:** PR #593 and PR #594  
**Scope:** live Runtime owner + first real actions; no editor/UI/transport yet

---

## 0. Goal

Turn the persisted Shortcut foundation into a real Runtime-owned execution domain.

PR-C introduces:

~~~text
ShortcutStore
    ↓
ShortcutRuntime
    ├─ loads one authoritative ShortcutDocument
    ├─ projects FrontendShortcutDashboardSnapshot
    ├─ resolves TileId against the authoritative document
    ├─ resolves supported Action TypeId
    └─ executes first real external actions
         ├─ system.executable
         ├─ system.powershell
         └─ system.url
~~~

Do not add the Main App editor, drag/drop, CRUD RPC, frontend pipe methods, Overlay dynamic tiles, Overlay execution, TDP/FPS/ClawHUD Shortcut actions, controller chords, macros, chains, or plugin discovery.

---

## 1. Mandatory source review

Read the latest versions before coding:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/work-order/SHORTCUT_FOUNDATION_PR_A_DYNAMIC_ACTION_TILE_CONTRACTS_WORK_ORDER_2026-09-24.md
docs/work-order/SHORTCUT_FOUNDATION_PR_B_RUNTIME_PERSISTENCE_WORK_ORDER_2026-09-24.md

src/SteamInputAddonforClaw.Contracts/Shortcuts/ShortcutContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutFrontendContracts.cs

src/SteamInputAddonforClaw/Shortcuts/ShortcutDocument.cs
src/SteamInputAddonforClaw/Shortcuts/ShortcutStore.cs

src/SteamInputAddonforClaw/CenterM/Oem1ApplicationLauncher.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
~~~

Shortcut remains independent from Full1902 controller ownership.

Do not touch PID1901/PID1902, HidHide, VIIPER, DirectInput, controller presentation switching, suspend/resume, shutdown/restart, or controller recovery.

---

## 2. Product direction

Shortcut is broader than controller remapping.

Future action families may include:

~~~text
external
    EXE
    PowerShell
    URL
    media
    keyboard

Addon-native
    TDP preset
    FPS preset
    Battery Limit
    ClawHUD
    Power Mode
    future Device controls
~~~

PR-C intentionally starts with only:

~~~text
system.executable
system.powershell
system.url
~~~

Do not implement addon.tdp-preset yet.

TDP Shortcut semantics are not finalized, including Device vs Profile ownership, current-power-source behavior, PL1/PL2 shape, and whether execution enables TDP ownership.

Do not guess these semantics.

PR-C is correct only if a future addon.tdp-preset can be added without changing ShortcutDocument, ShortcutTileDefinition, ShortcutActionSpec, ShortcutStore, or TileId execution semantics.

---

## 3. Benchmark direction

Previous HHC/CTW review supports these capabilities:

~~~text
HHC
    executable actions
    inline PowerShell
    live Shortcut projection

CTW
    executable
    PowerShell
    website/URL
    broad action tiles
~~~

Do not copy HHC's large command inheritance hierarchy.

Implement only the smallest dispatch seam required by the three real actions in this PR.

---

## 4. Runtime is execution authority

Required execution path:

~~~text
caller
    ↓
Execute(TileId)
    ↓
ShortcutRuntime resolves TileId from its current authoritative ShortcutDocument
    ↓
resolve Action.TypeId
    ↓
validate action-specific schema/config
    ↓
execute
~~~

Never accept executable path, PowerShell source, URL, or raw ShortcutActionSpec as the normal execution authority from a frontend.

Future Overlay/Main UI execution sends TileId only.

---

## 5. ShortcutRuntime

Create:

~~~text
src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs
~~~

Responsibilities:

~~~text
one ShortcutStore
one loaded authoritative ShortcutDocument
one persistence-availability fact
action resolution
read-only frontend projection
execution by TileId
~~~

Suggested construction:

~~~csharp
internal ShortcutRuntime(
    ShortcutStore store,
    Func<ProcessStartInfo, Process?>? startProcess = null,
    Func<string, bool>? fileExists = null)
~~~

Defaults:

~~~text
startProcess -> Process.Start
fileExists   -> File.Exists
~~~

These delegates are allowed for realistic tests without launching child processes.

Do not introduce IProcessLauncher solely for tests.

---

## 6. Startup/load behavior

Load ShortcutStore once when ShortcutRuntime is constructed or initialized.

Interpret ShortcutLoadResult:

~~~text
Loaded
    usable authoritative document

NotFound
    usable authoritative empty document

Malformed
UnsupportedSchemaVersion
ReadFailure
    Shortcut Runtime unavailable for this process lifetime
~~~

For unsafe load statuses:

- preserve the original file;
- Capture returns an unavailable dashboard;
- Execute refuses all actions;
- do not replace the document;
- do NOT throw out of ShortcutRuntime construction solely because ShortcutStore returned Malformed, UnsupportedSchemaVersion, or ReadFailure;
- do NOT fail AddonProcessHost startup because Shortcut persistence is unavailable.

Shortcut is an optional sibling Runtime capability. A bad shortcuts.json must disable only Shortcut functionality while the rest of the Addon Runtime, including Full1902 controller ownership, continues normally.

Only failures that represent a real programming/composition error outside the documented Shortcut load-status model may fail Host construction.

No watcher, retry loop, or periodic reload is required.

A later editor can add explicit reload/recovery only if actually needed.

---

## 7. In-memory authority

ShortcutRuntime owns the in-memory ShortcutDocument used for both projection and execution.

PR-C has no mutation UI, so the document is read-only after startup.

Do not reload shortcuts.json on every execution.

Do not let Main UI or Overlay read ShortcutStore directly.

No new lock is required solely for theoretical concurrency because PR-C has no document mutation path.

---

## 8. Action Type IDs

Add a narrow internal constants holder, for example:

~~~csharp
internal static class ShortcutActionTypeIds
{
    internal const string Executable = "system.executable";
    internal const string PowerShell = "system.powershell";
    internal const string Url = "system.url";
}
~~~

Do not replace the persisted TypeId string with an enum.

Unknown TypeId remains valid persisted data.

---

## 9. Minimal action dispatch seam

Centralize:

~~~text
TypeId
    -> supported action schema version
    -> action-specific validation
    -> projection
    -> execution
~~~

A small private handler dictionary is acceptable.

A single centralized switch is also acceptable if it is simpler.

Do not duplicate unrelated TypeId switches across multiple owners.

Do not add MEF, reflection discovery, plugins, a public handler hierarchy, command bus, mediator, factory framework, or DI registration graph.

---

## 10. system.executable schema 1

Parameters:

~~~json
{
  "path": "C:\\Tools\\Tool.exe",
  "arguments": "--example"
}
~~~

Validation:

- path is required;
- path is non-empty;
- path is absolute;
- extension is .exe, case-insensitive;
- arguments are optional and default to empty;
- action SchemaVersion must equal 1.

Do not accept .bat, .cmd, .ps1, .lnk, documents, or arbitrary shell-open targets through this action.

Execution:

~~~csharp
ProcessStartInfo
{
    FileName = path,
    Arguments = arguments,
    UseShellExecute = false,
    WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty
}
~~~

Start and forget.

Do not wait, track, kill, restart, or infer running/toggle state.

Re-check File.Exists immediately before launch.

Missing file is an ordinary unavailable action, not a Runtime failure.

The safety semantics should match the existing Oem1ApplicationLauncher principle: an executable action is a literal executable action, not a general shell-open action.

Do not reuse the OEM1 binding DTO as Shortcut persistence.

---

## 11. system.powershell schema 1

Parameters:

~~~json
{
  "script": "Write-Output 'hello'"
}
~~~

Validation:

- script required;
- non-empty/non-whitespace;
- SchemaVersion must equal 1;
- inline script content only, not a .ps1 path.

Execution should use Windows PowerShell without temporary script files.

Use the deterministic Windows PowerShell 5.1 executable path:

~~~text
%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe
~~~

Resolve it from Environment.GetFolderPath(Environment.SpecialFolder.Windows) or the equivalent existing repository convention, then append:

~~~text
System32\WindowsPowerShell\v1.0\powershell.exe
~~~

Do not rely on PATH lookup for powershell.exe in PR-C.

Recommended arguments:

~~~text
-NoLogo
-NoProfile
-NonInteractive
-ExecutionPolicy Bypass
-EncodedCommand <UTF-16LE Base64 script>
~~~

ProcessStartInfo requirements:

~~~text
UseShellExecute = false
CreateNoWindow = true
~~~

Prefer ArgumentList for fixed arguments.

The Runtime does not wait for script completion.

Do not capture stdout/stderr.

Do not host a PowerShell runspace.

Do not write a temporary .ps1.

The script runs with the same Windows token/integrity level as the Addon Runtime.

Do not add a separate run-as-administrator setting in schema 1.

A later schema version may add window/elevation controls if real product need appears.

---

## 12. system.url schema 1

Parameters:

~~~json
{
  "url": "https://example.com"
}
~~~

Validation:

- URL required;
- Uri.TryCreate(value, UriKind.Absolute, out uri) must succeed;
- uri.Host must be non-empty;
- scheme comparison is case-insensitive;
- only http and https schemes are accepted;
- both http and https are valid;
- SchemaVersion must equal 1.

Execution:

~~~csharp
Process.Start(new ProcessStartInfo
{
    FileName = url,
    UseShellExecute = true
});
~~~

This action intentionally uses shell resolution to open the registered browser.

Reject:

~~~text
file://
javascript:
data:
steam://
relative URLs
local executable/document paths
~~~

If custom URI support is needed later, add a separately reviewed TypeId rather than silently widening system.url.

---

## 13. Persistence validity vs executability

The base ShortcutStore preserves unknown TypeId and unknown action SchemaVersion.

ShortcutRuntime must keep that distinction.

Example:

~~~text
TypeId = system.executable
SchemaVersion = 7
~~~

is valid persisted data but unsupported by this implementation.

Projection:

~~~text
Enabled = false
State = Unavailable
StatusText = "Unsupported"
~~~

Execution returns Unsupported.

Do not rewrite or downgrade the stored action.

---

## 14. Unknown TypeId

For an unknown TypeId:

~~~text
future.vendor.some-action
~~~

keep the tile in projection:

~~~text
Title = persisted Title
StatusText = "Unsupported"
State = Unavailable
Enabled = false
~~~

Execution returns Unsupported.

Do not delete the tile.

Do not hide it from the dashboard.

---

## 15. Invalid known parameters

A document can pass base structural validation while a known action has invalid action-specific fields.

Example:

~~~json
{
  "typeId": "system.executable",
  "schemaVersion": 1,
  "parameters": {
    "path": ""
  }
}
~~~

Only that tile becomes unavailable:

~~~text
Enabled = false
State = Unavailable
StatusText = "Invalid configuration"
~~~

Execution returns InvalidConfiguration.

The whole dashboard remains Available.

Do not rewrite the persisted definition during capture.

---

## 16. Frontend projection

Use the PR-A contracts already present:

~~~text
FrontendShortcutDashboardSnapshot
FrontendShortcutTile
FrontendShortcutTileState
~~~

ShortcutRuntime should expose:

~~~csharp
internal FrontendShortcutDashboardSnapshot Capture()
~~~

Healthy Loaded/NotFound document:

~~~text
Available = true
Tiles = persisted order
~~~

Unsafe persistence load:

~~~text
Available = false
Tiles = []
FailureMessage = short generic reason
~~~

Valid external action:

~~~text
Title = persisted Title
Enabled = true
State = Neutral
StatusText = null
~~~

Do not claim that a launched EXE or script is currently active merely because it was started.

Missing executable:

~~~text
Enabled = false
State = Unavailable
StatusText = "Not found"
~~~

Unsupported action/type schema:

~~~text
Enabled = false
State = Unavailable
StatusText = "Unsupported"
~~~

Invalid known config:

~~~text
Enabled = false
State = Unavailable
StatusText = "Invalid configuration"
~~~

Never put Parameters into FrontendShortcutTile.

---

## 17. Execution result

Add a small Runtime-local result:

~~~csharp
internal enum ShortcutExecutionOutcome
{
    Succeeded,
    NotFound,
    Unsupported,
    InvalidConfiguration,
    Unavailable,
    Failed
}

internal sealed record ShortcutExecutionResult(
    ShortcutExecutionOutcome Outcome,
    string? FailureMessage = null);
~~~

Do not create subclasses.

Semantics are fixed as follows:

- Succeeded: launch request succeeded.
- NotFound: TileId is absent from authoritative document.
- Unsupported: unknown TypeId or unsupported action SchemaVersion.
- InvalidConfiguration: known action with invalid action-specific parameters.
- Unavailable:
  - ShortcutRuntime itself is unavailable because persistence load was unsafe;
  - executable target is missing immediately before launch;
  - Process.Start throws FileNotFoundException;
  - Process.Start throws DirectoryNotFoundException.
- Failed:
  - Process.Start returns null;
  - Process.Start throws UnauthorizedAccessException;
  - Process.Start throws another Win32Exception;
  - Process.Start throws InvalidOperationException;
  - another ordinary launch exception not explicitly classified above.

Do not leave these mappings to caller/implementation discretion.

If Process.Start returns a Process handle, dispose the local handle after a successful fire-and-forget start where appropriate.

Do not wait for child exit.

---

## 18. Cancellation

Execution accepts CancellationToken.

If already cancelled before process launch:

- do not launch;
- follow existing project cancellation convention.

Do not terminate a child process after it has successfully launched.

ShortcutRuntime does not own child process lifetime.

---

## 19. Launch failure handling

Handle realistic launch failures without crashing the Runtime, using the fixed outcome mapping from section 17.

Logging and returned failure text must be payload-safe.

Allowed log metadata:

~~~text
TileId
TypeId
Outcome
exception type
fixed generic error text
~~~

Do NOT log or return raw exception.Message because Windows/process exceptions may contain:

- executable paths;
- working directories;
- command-line content;
- environment-specific filesystem details.

Never log or return:

- full Parameters JSON;
- PowerShell source;
- executable arguments;
- raw URL;
- raw exception message.

ShortcutExecutionResult.FailureMessage, when non-null, must be a short fixed/general user-safe message such as:

~~~text
"Shortcut target is unavailable."
"Shortcut could not be launched."
"Shortcut configuration is invalid."
~~~

Do not derive FailureMessage directly from exception.Message.

---

## 20. AddonProcessHost composition

PR-B intentionally did not add an unused ShortcutStore owner.

PR-C now has real live behavior, so wire it.

The Host composition rule is strict:

> Shortcut load failure must not block AddonProcessHost construction or Runtime startup.

Malformed, unsupported-newer, or unreadable shortcuts.json must produce an unavailable ShortcutRuntime capability, not a failed application startup.

Add fields equivalent to:

~~~text
ShortcutStore _shortcutStore
ShortcutRuntime _shortcutRuntime
~~~

Path:

~~~text
production
    AddonDataPaths.ShortcutsPath

testOnlyDataRoot
    Path.Combine(testOnlyDataRoot, "shortcuts.json")
~~~

Construct ShortcutRuntime as a sibling Runtime capability.

Do not put it inside controller routing composition.

No shutdown teardown is required because these actions are fire-and-forget and no child process is owned.

---

## 21. No frontend transport in PR-C

PR-C stops before pipe/RPC exposure.

Do not modify:

~~~text
IAddonFrontendControl
NamedPipeAddonFrontendClient
NamedPipeAddonFrontendServer
FrontendRpcMethod
OverlayWire
~~~

Do not bump a wire version.

PR-C proves Runtime authority and execution first.

PR-D will add editor/capture transport together with the Main App editor contract.

PR-E will reuse the same Runtime Capture/Execute path for Overlay.

---

## 22. No UI changes

Do not change:

~~~text
SteamInputAddonforClaw.UI
SteamInputAddonforClaw.Overlay
~~~

The current four sample Overlay Shortcut tiles remain temporary local POC UI.

No debug UI is needed to invoke PR-C.

Tests are sufficient.

---

## 23. No Addon-native action yet

Do not implement in this PR:

~~~text
addon.tdp-preset
addon.fps-preset
addon.clawhud-toggle
addon.battery-limit
addon.power-mode
~~~

But do not create an external-only architecture.

A future native handler must be able to call existing typed Runtime feature authority instead of Process.Start.

Future rule:

~~~text
Shortcut is an invocation surface,
not a second implementation of Addon feature logic.
~~~

Example future path:

~~~text
ShortcutRuntime
    ↓
addon.tdp-preset
    ↓
existing TDP/Profile mutation authority
    ↓
existing apply/reconcile path
~~~

Never direct EC writes from Shortcut code.

---

## 24. ShortcutRuntime tests

Create:

~~~text
tests/SteamInputAddonforClaw.Tests/ShortcutRuntimeTests.cs
~~~

Use a temporary ShortcutStore and injected process-start/file-existence delegates.

Never launch real EXEs, PowerShell, or browsers in tests.

Required tests:

### Empty first run

NotFound store:

~~~text
Capture
-> Available = true
-> zero tiles
~~~

### Unsafe persistence

Malformed / unsupported newer root schema:

~~~text
Capture
-> Available = false
-> zero tiles

Execute
-> Unavailable
~~~

### Persisted order

More than four tiles project in exact persisted order with stable TileIds.

### Unknown TypeId

~~~text
tile retained
Enabled false
State Unavailable
Status Unsupported

Execute -> Unsupported
~~~

### Unsupported known schema

system.executable with SchemaVersion 2:

~~~text
tile retained
Enabled false
Unsupported
no launch
~~~

### Invalid known config

Cover:

~~~text
executable blank path
executable relative path
executable non-.exe path
executable path value with non-string JSON type
executable arguments value with non-string JSON type
PowerShell blank script
PowerShell script value with non-string JSON type
URL relative
URL non-http/non-https
URL value with non-string JSON type
~~~

Each remains visible but disabled with InvalidConfiguration.

No launch delegate call.

### Executable launch

Given:

~~~json
{
  "path": "C:\\Tools\\Tool.exe",
  "arguments": "--foo bar"
}
~~~

with fileExists true, assert ProcessStartInfo:

~~~text
FileName exact
Arguments exact
UseShellExecute false
WorkingDirectory executable directory
~~~

Exactly one launch.

### Executable disappears

Re-check file existence at execution.

If fileExists becomes false immediately before launch:

~~~text
Execute -> Unavailable
no Process.Start
~~~

This is ordinary filesystem drift and does not require epoch/state machinery.

Also inject FileNotFoundException and DirectoryNotFoundException from the process-start delegate and assert Unavailable.

### PowerShell encoding

Use a script containing spaces, quotes, and Unicode.

Assert:

~~~text
FileName = deterministic %SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe path
UseShellExecute = false
CreateNoWindow = true
ArgumentList contains:
    -NoLogo
    -NoProfile
    -NonInteractive
    -ExecutionPolicy
    Bypass
    -EncodedCommand
~~~

Decode the Base64 as UTF-16LE and assert exact original script content.

Do not spawn PowerShell.

### URL launch

Test both:

~~~text
http://example.com
https://example.com
~~~

For each valid URL:

~~~text
UseShellExecute true
FileName exact URL
~~~

Also prove scheme matching is case-insensitive and host must be non-empty.

### URL rejection

Reject:

~~~text
file://
javascript:
data:
steam://
relative URL
~~~

### Launch failure and fixed outcome mapping

Injected process starter must cover:

~~~text
returns null                  -> Failed
throws FileNotFoundException -> Unavailable
throws DirectoryNotFoundException -> Unavailable
throws UnauthorizedAccessException -> Failed
throws Win32Exception        -> Failed
throws InvalidOperationException -> Failed
~~~

ShortcutRuntime remains usable after every failure.

Assert FailureMessage is generic and does not contain raw exception.Message, executable path, arguments, script content, or URL.

### Cancellation before launch

Pass an already-cancelled CancellationToken.

Assert:

~~~text
no file/process launch delegate call
operation follows the repository cancellation convention
~~~

Do not add child-process cancellation ownership.

### TileId resolution

Unknown TileId:

~~~text
NotFound
no launch
~~~

Normal Runtime execution entry must take TileId, not raw ActionSpec.

---

## 25. AddonProcessHost composition tests

Add focused coverage proving:

- production composition uses AddonDataPaths.ShortcutsPath;
- testOnlyDataRoot gets isolated shortcuts.json;
- ShortcutRuntime is constructed independently from controller routing composition;
- malformed shortcuts.json does NOT prevent AddonProcessHost construction/startup;
- unsupported-newer shortcuts.json does NOT prevent Host startup;
- Shortcut capability becomes unavailable while unrelated Runtime capabilities remain constructible.

Use existing repository test/source-shape patterns.

Do not expose a new public test API solely for this.

---

## 26. Lifecycle expectations

External action lifecycle is intentionally:

~~~text
execute Shortcut
-> create external process
-> forget child process
~~~

No child-process ownership persists across:

- sleep;
- resume;
- Runtime restart;
- Addon shutdown;
- controller routing changes.

Do not add cleanup/kill behavior.

The real safety requirements are:

- malformed shortcuts.json must not affect the rest of the Addon Runtime;
- failed child launch must not affect controller state;
- ShortcutRuntime must remain outside Full1902 controller teardown.

---

## 27. Expected files

Likely production changes:

~~~text
src/SteamInputAddonforClaw/Shortcuts/ShortcutActionTypeIds.cs
src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
~~~

Likely tests:

~~~text
tests/SteamInputAddonforClaw.Tests/ShortcutRuntimeTests.cs
plus focused host architecture/composition coverage
~~~

A small private/internal parameter model may live in ShortcutRuntime.cs or one focused companion file.

Do not create one handler class/file per tiny action unless the code is genuinely clearer.

---

## 28. Explicit non-goals

Do NOT implement:

- Shortcut CRUD;
- ShortcutRuntime Save mutation;
- editor mutation;
- frontend pipe capture;
- frontend pipe execute;
- Overlay execute;
- dynamic tile grid;
- action catalog UI metadata;
- icons;
- Addon-native action;
- media action;
- keyboard action;
- generic custom URI action;
- arbitrary file/document shell open;
- .bat/.cmd/.ps1 through system.executable;
- run-as option;
- window-style selector;
- child process tracking;
- child process termination;
- stdout/stderr capture;
- PowerShell runspace;
- plugin architecture;
- macros/chains;
- conditions;
- schedules.

---

## 29. Overengineering guard

The target is one Runtime owner and three real action types.

Prefer:

~~~text
ShortcutRuntime
    small resolver
    small per-type validation
    ProcessStartInfo construction
~~~

Do not add:

~~~text
IShortcutActionPlugin
ActionContext
ActionPipeline
Middleware
CommandBus
generic scheduler
process supervisor
plugin scanner
~~~

A small private handler table is enough if it reduces duplicate switches.

If one centralized switch is simpler, use it.

Future TDP support does not require a plugin framework today.

---

## 30. Acceptance checklist

### Runtime owner

- [ ] ShortcutRuntime owns one loaded ShortcutDocument.
- [ ] Loaded and NotFound are usable.
- [ ] Malformed/Unsupported/ReadFailure disable Shortcut Runtime only.
- [ ] Shortcut persistence failure never blocks AddonProcessHost construction/startup.
- [ ] ShortcutRuntime is wired into AddonProcessHost.
- [ ] testOnlyDataRoot gets isolated shortcuts.json.
- [ ] no controller owner is changed.

### Resolution

- [ ] Execute takes TileId.
- [ ] TileId is re-resolved from authoritative Runtime document.
- [ ] unknown TileId returns NotFound.
- [ ] unknown TypeId returns Unsupported.
- [ ] unsupported action schema returns Unsupported.
- [ ] invalid action config affects only that tile.

### Executable

- [ ] absolute .exe only.
- [ ] UseShellExecute false.
- [ ] arguments preserved.
- [ ] executable directory used as working directory.
- [ ] file existence checked at execution.
- [ ] fire-and-forget.
- [ ] no shell-open fallback.

### PowerShell

- [ ] inline script.
- [ ] blank/non-string script rejected.
- [ ] deterministic Windows PowerShell 5.1 path under %SystemRoot%\System32\WindowsPowerShell\v1.0.
- [ ] no PATH lookup dependency.
- [ ] UTF-16LE EncodedCommand.
- [ ] NoLogo/NoProfile/NonInteractive.
- [ ] ExecutionPolicy Bypass.
- [ ] UseShellExecute false.
- [ ] CreateNoWindow true.
- [ ] no temp file.
- [ ] no output capture.
- [ ] no extra UAC path.

### URL

- [ ] Uri.TryCreate absolute validation.
- [ ] host must be non-empty.
- [ ] scheme comparison is case-insensitive.
- [ ] both http and https accepted.
- [ ] UseShellExecute true.
- [ ] custom/local schemes rejected.
- [ ] non-string URL rejected as InvalidConfiguration.

### Outcome and privacy

- [ ] missing executable and FileNotFoundException/DirectoryNotFoundException map to Unavailable.
- [ ] UnauthorizedAccessException/Win32Exception/InvalidOperationException map to Failed.
- [ ] Process.Start null maps to Failed.
- [ ] raw exception.Message is never logged or returned.
- [ ] payload/path/script/arguments/URL are not leaked through failure logging.

### Projection

- [ ] persisted order retained.
- [ ] valid external actions are Neutral + Enabled.
- [ ] missing executable is unavailable.
- [ ] unknown/unsupported action is preserved and unavailable.
- [ ] invalid config is preserved and unavailable.
- [ ] no Parameters payload enters FrontendShortcutTile.

### Scope

- [ ] no frontend RPC.
- [ ] no wire version bump.
- [ ] no Main UI change.
- [ ] no Overlay change.
- [ ] no CRUD.
- [ ] no Addon-native Shortcut action.
- [ ] no Full1902 lifecycle change.

---

## 31. Automated validation

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

No physical-device test is required.

Do not launch real arbitrary EXE/PowerShell/browser processes during automated tests.

---

## 32. Follow-up roadmap

After PR-C:

### PR-D — Runtime edit contract + Main App Shortcut editor

Add:

- full editable definition capture;
- supported action catalog for editor;
- create/update/delete;
- drag/drop reorder;
- persistence through ShortcutRuntime;
- EXE/PowerShell/URL action editors.

Main App remains a client of Runtime authority.

### PR-E — Overlay dynamic grid

Add:

- dynamic compact near-square tile layout;
- title + StatusText;
- controller navigation based on actual grid;
- execute by TileId;
- no editing in Overlay.

### Later Addon-native action PRs

Add only after semantics are decided:

~~~text
addon.tdp-preset
addon.fps-preset
addon.clawhud-toggle
...
~~~

Each must reuse existing Runtime feature authority.

No storage redesign should be necessary.

---

## 33. Final principle

PR-A made Shortcut definitions extensible.

PR-B made them durable.

PR-C makes them executable.

Keep the first executor intentionally small:

~~~text
authoritative persisted TileId
    ↓
one Runtime owner
    ↓
one centralized action resolver
    ↓
EXE / PowerShell / HTTPS URL
~~~

The architecture is successful if future TDP and other Addon-native actions can be added without changing the foundation.
