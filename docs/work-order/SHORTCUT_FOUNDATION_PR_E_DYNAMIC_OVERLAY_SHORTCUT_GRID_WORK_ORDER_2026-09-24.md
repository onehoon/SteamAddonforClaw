# Work Order — Shortcut Foundation PR-E: Dynamic Overlay Shortcut Grid + TileId Execution

**Date:** 2026-09-24  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**Target branch:** main  
**Reviewed main baseline:** f9008fff7c6d741432c32f5feed593ed9939b7e0  
**Implementation rule:** re-read the latest `main` before coding; the baseline above records this work-order review point, not a branch pin.  
**Depends on:** PR #593, PR #594, PR #595, PR #596, PR #598  
**Feature area:** WinUI3 Addon Overlay / Runtime-owned Shortcut consumption  
**Implementation shape:** one focused PR  
**Product:** standalone Full1902; CTW integration is out of scope

---

# 0. Goal

Replace the current temporary fixed `2x2` Shortcut POC in the WinUI3 Addon Overlay with the real Runtime-owned Shortcut product completed by PR-A through PR-D.

Target behavior:

```text
Main App / Shortcut
    -> create / edit / delete / reorder
    -> shortcuts.json

ShortcutRuntime
    -> one authoritative ShortcutDocument
    -> sanitized FrontendShortcutDashboardSnapshot
    -> ExecuteAsync(TileId)

WinUI3 Addon Overlay / Shortcut
    -> render the sanitized Runtime snapshot
    -> preserve persisted tile order
    -> controller navigation follows the actual rendered grid
    -> A / pointer click sends TileId only
    -> Runtime resolves and executes the action
```

Supported actions already exist and PR-E must consume them without reimplementing them:

```text
system.executable
system.powershell
system.url
system.screenshot-fullscreen
```

PR-E is the **consumption surface**.

It is not another Shortcut editor, persistence layer, action engine, or lifecycle owner.

---

# 1. Mandatory review before coding

Read current `main` before editing.

## 1.1 Full1902 authority

At minimum:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
```

Product assumptions remain:

```text
one Windows user
one interactive session
no Fast User Switching support
no RDP / multi-session support
standalone Full1902
```

Shortcut execution must not become a new controller lifecycle participant.

Do not add Shortcut-specific:

```text
PID1901/PID1902 authority
HidHide ownership
VIIPER ownership
PnP recovery
suspend/resume participant
routing rollback
controller recovery
lifecycle epoch
global state machine
```

The one exception is the already-existing Screenshot callback, which deliberately reuses the existing Overlay retirement path.

## 1.2 Completed Shortcut foundation

Read:

```text
docs/work-order/SHORTCUT_FOUNDATION_PR_A_DYNAMIC_ACTION_TILE_CONTRACTS_WORK_ORDER_2026-09-24.md
docs/work-order/SHORTCUT_FOUNDATION_PR_B_RUNTIME_PERSISTENCE_WORK_ORDER_2026-09-24.md
docs/work-order/SHORTCUT_FOUNDATION_PR_C_RUNTIME_EXTERNAL_ACTION_ENGINE_WORK_ORDER_2026-09-24.md
docs/work-order/SHORTCUT_FOUNDATION_PR_C2_NIRCMD_FULLSCREEN_SCREENSHOT_ACTION_WORK_ORDER_2026-09-24.md
docs/work-order/SHORTCUT_FOUNDATION_PR_D_MAIN_APP_SHORTCUT_EDITOR_WORK_ORDER_2026-09-24.md
```

Inspect at minimum:

```text
src/SteamInputAddonforClaw.Contracts/Shortcuts/ShortcutContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutFrontendContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutEditorFrontendContracts.cs

src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs
src/SteamInputAddonforClaw/Shortcuts/ShortcutStore.cs
src/SteamInputAddonforClaw/Shortcuts/ShortcutActionTypeIds.cs
src/SteamInputAddonforClaw/Shortcuts/NirCmdScreenshotCapture.cs

src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
```

## 1.3 Current Overlay

Inspect:

```text
src/SteamInputAddonforClaw.Overlay/OverlayShortcutSelection.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Presentation.cs
src/SteamInputAddonforClaw.Overlay/App.xaml.cs

src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs

tests/SteamInputAddonforClaw.UiTests/OverlayShortcutSelectionTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs
```

Also inspect the current Overlay shell / Quick Settings transport tests before changing message validation.

---

# 2. Historical Shortcut documents that must NOT drive PR-E

The older document:

```text
docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR4_SHORTCUT_PARITY_WORK_ORDER.md
```

describes a historical pre-Shortcut-foundation design:

```text
four fixed Slot identities
Slot 1..4
Unassigned
QAM + Overlay parity
no persistence
no execution
```

That design is obsolete for PR-E.

Do **not** implement or revive:

```text
AddonQuickSettingsShortcutSlotId
fixed four-slot product authority
QAM Shortcut parity
QAM transport
static Unassigned slots
```

Current authority is now:

```text
ShortcutDocument
FrontendShortcutDashboardSnapshot
ShortcutRuntime.ExecuteAsync(TileId)
```

Steam QAM is Steam-owned and is not part of this PR.

---

# 3. Reviewed current state at main@f9008fff

## 3.1 Shortcut Runtime is complete

Current `ShortcutRuntime` already owns:

```text
ShortcutStore
current in-memory ShortcutDocument
CRUD publication after durable save
sanitized renderer projection
execution by TileId
EXE / PowerShell / URL / Screenshot
```

Current renderer projection:

```csharp
FrontendShortcutDashboardSnapshot
{
    Available
    Tiles[]
    FailureMessage
}

FrontendShortcutTile
{
    TileId
    Title
    StatusText
    State
    Enabled
}
```

This is exactly the data the Overlay needs.

Do not create another Overlay-specific Shortcut product model containing path/script/url/action JSON.

## 3.2 Editor payload is intentionally separate

PR-D added:

```text
FrontendShortcutEditorSnapshot
FrontendShortcutEditorAction
FrontendShortcutMutationIntent
```

Those contracts contain editable action configuration.

They belong to the trusted Main App editor only.

**The Overlay must never consume the editor contract.**

In particular the Overlay must never receive:

```text
ExecutablePath
ExecutableArguments
PowerShellScript
Url
Configured Screenshot folder
raw ShortcutActionSpec
raw JSON Parameters
```

## 3.3 Overlay is still the temporary POC

Current Overlay Shortcut implementation is:

```text
TemporaryShortcutTiles
    Slot 1
    Slot 2
    Slot 3
    Slot 4

OverlayShortcutSelection
    TileCount = 4
    ColumnCount = 2

Accept
    -> no action
```

PR-E removes that temporary product behavior.

## 3.4 Current Overlay transport

Current production baseline:

```text
OverlayTransportProtocol.CurrentVersion = 11
OverlayTransportProtocol.MaxFrameBytes  = 512 KiB

FrontendTransportProtocol.CurrentVersion = 42
```

The Overlay pipe already carries:

```text
Show / Hide / Shutdown
semantic navigation
tab order
Quick Settings state + mutation
ClawHUD state + mutation
Profile catalog/detail
```

PR-E adds Shortcut state + execution only.

Main frontend editor transport does not need another change.

---

# 4. Locked authority model

Use this exact ownership direction:

```text
shortcuts.json
    ↓
ShortcutStore
    ↓
ShortcutRuntime
    ├─ Capture()
    └─ ExecuteAsync(TileId)
          ↓
     existing action implementation

OverlayProcessController
    ↓ typed capture/execute delegates

OverlayWire
    ↓ sanitized state / TileId request only

OverlayWindow
    ↓ render / selection / user intent
```

The Overlay is never an execution authority.

The Overlay is never a persistence authority.

The Overlay must not infer an action type from the title or status.

The Overlay must not cache an action payload.

The only durable execution identity crossing from Overlay to Runtime is:

```text
TileId
```

---

# 5. Do not route the Overlay through the Main App editor contract

PR-E should **not** call:

```text
CaptureShortcutEditorAsync
MutateShortcutAsync
SetScreenshotSaveFolderAsync
```

from the Overlay.

The editor contract contains sensitive configuration and is intentionally broader than a renderer needs.

Preferred Runtime binding from `AddonProcessHost`:

```text
capture
    -> _shortcutRuntime.Capture()

execute
    -> _shortcutRuntime.ExecuteAsync(tileId, token)
```

A narrow adapter may map the internal execution result to the transport response.

Do not create a second `ShortcutRuntime`.

Do not reload `shortcuts.json` in Overlay code.

---

# 6. Main frontend protocol stays unchanged

PR-E does not require another Main App named-pipe contract change.

Required:

```text
FrontendTransportProtocol.CurrentVersion
    stays 42
```

Do not add a Main App execute button merely because execution transport now exists for Overlay.

Do not expose Shortcut execution through `FrontendWire` unless a concrete implementation blocker proves it is necessary.

The product need in PR-E is the separate Overlay transport.

---

# 7. Overlay protocol v11 -> v12

PR-E changes the Overlay wire shape, therefore bump:

```text
OverlayTransportProtocol
11 -> 12
```

Add a version comment explaining that v12 adds:

```text
Runtime -> Overlay Shortcut dashboard state
Overlay -> Runtime TileId execution request
Runtime -> Overlay correlated execution result
```

A v11 peer must fail handshake.

No compatibility shim is required; this is the same bundled application.

Keep:

```text
OverlayTransportProtocol.MaxFrameBytes = 512 * 1024
```

Do not increase the frame limit.

---

# 8. Overlay wire additions

Use the existing `FrontendShortcutDashboardSnapshot` directly for renderer state.

Add message kinds equivalent to:

```text
ShortcutState
ShortcutExecuteRequest
ShortcutExecuteResult
```

Suggested transport-only request:

```csharp
internal sealed record OverlayShortcutExecuteRequest(
    long RequestId,
    Guid TileId);
```

Suggested transport-only response:

```csharp
internal sealed record OverlayShortcutExecuteResponse(
    long RequestId,
    Guid TileId,
    bool Succeeded,
    string? FailureMessage,
    FrontendShortcutDashboardSnapshot Snapshot);
```

Exact names may differ slightly.

Do not send:

```text
ActionSpec
TypeId as execution authority
path
arguments
script
URL
Screenshot folder
```

## 8.1 Why the result contains a fresh snapshot

Execution can discover a real current failure after the previously-rendered snapshot was captured.

Example:

```text
EXE existed when state was rendered
-> target removed
-> user presses A
-> ExecuteAsync returns Unavailable
-> fresh Capture now reports Not found / disabled
```

Returning a fresh sanitized snapshot lets the Overlay immediately converge to Runtime truth without inventing local action-state rules.

It does not make the Overlay authoritative.

---

# 9. Wire structural validation

Add narrow structural validation for Shortcut frames.

## Shortcut state

Valid state requires:

```text
snapshot != null
Tiles != null
every tile != null
TileId != Guid.Empty
Title not null
enum State defined
```

Do not re-run persisted Shortcut semantic validation in OverlayWire.

That remains ShortcutRuntime's job.

## Execute request

Valid:

```text
RequestId > 0
TileId != Guid.Empty
all unrelated message payload members are null
```

Unknown or malformed wire shape fails closed as a protocol error.

## Execute result

Valid:

```text
RequestId > 0
TileId != Guid.Empty
Snapshot structurally valid
Succeeded=true  -> FailureMessage may be null
Succeeded=false -> FailureMessage may be present
```

Do not add another public execution-outcome enum solely for Overlay UI if boolean success + failure text is sufficient.

The Runtime may keep its existing richer internal `ShortcutExecutionOutcome`.

---

# 10. Shortcut state must fail locally if it cannot fit the Overlay frame

The Overlay frame limit is 512 KiB.

Most real Shortcut renderer snapshots are very small because they contain only:

```text
TileId
Title
StatusText
State
Enabled
```

But PR-E must not assume an arbitrary number of manually persisted tiles can never exceed the wire envelope.

Do not:

- increase the 512 KiB frame limit;
- truncate titles;
- silently omit tiles;
- impose a fixed four-tile or arbitrary tile-count cap;
- disconnect the whole Overlay because one Shortcut snapshot is too large.

Before sending a Shortcut state/result, prove the **actual full OverlayWireMessage** fits.

If the available state would exceed the wire limit, replace only the Shortcut projection with a small unavailable state such as:

```text
Available = false
Tiles = []
FailureMessage = "Shortcut configuration is too large to display."
```

Then send that small state.

The persisted document remains untouched.

Add a transport-boundary test using the actual serialized Overlay envelope.

---

# 11. Critical: Shortcut execution must NOT block the Overlay receive loop

This is a real Screenshot lifecycle requirement, not a theoretical race.

Current Screenshot execution does:

```text
ShortcutRuntime.ExecuteAsync(TileId)
    ↓
AddonProcessHost.ExecuteFullscreenScreenshotShortcutAsync
    ↓
_visibleSurfaceTransition
    ↓
RetireOverlayCaptureUnderTransitionAsync
    ↓
OverlayProcessController.EnsureHiddenAsync
    ↓
Runtime sends OverlayCommand.Hide
    ↓
Overlay hides
    ↓
Overlay sends Hidden acknowledgement
    ↓
Runtime resumes controller presentation
    ↓
NirCmd capture
```

If `ShortcutExecuteRequest` is handled inline inside the one `NamedPipeOverlayServer.ServeAsync` read loop, that same read loop cannot consume the `Hidden` acknowledgement while `ExecuteAsync` is waiting for it.

That creates a real deadlock/timeout path for the supported Screenshot action.

Therefore follow the pattern already used by Quick Settings mutation:

```csharp
if (message.Kind == OverlayWireMessageKind.ShortcutExecuteRequest)
{
    validate(...);
    _ = HandleShortcutExecuteRequestAsync(...);
    continue; // resume read loop immediately
}
```

`HandleShortcutExecuteRequestAsync` must:

- be exception-contained;
- run outside the read loop;
- use the existing shared Overlay write gate for the result;
- allow the read loop to process Hide/Hidden while execution is pending;
- stop naturally when the connection/process lifetime token is cancelled.

Do not add a second pipe or screenshot-only transport.

---

# 12. Execution admission

There are two existing, real ownership facts.

## Transport admission

`NamedPipeOverlayServer` should admit execution only when:

```text
Ready
AND Visible
```

This is the same transport-level surface fact used by current mutation paths.

## Runtime capture admission

The Host execution adapter must also require:

```text
process shutdown has not started
AND _overlayCaptureActive == true
```

This prevents a mouse click during the short:

```text
window Visible
-> presentation not yet paused/captured
```

interval from invoking an action as if the Overlay were already the active captured surface.

Do not add an epoch/generation/state machine for this.

The current supported lifecycle has one Overlay process/session and one capture fact.

A narrow admission check is sufficient.

---

# 13. Bind Shortcut authority through OverlayProcessController

Extend the existing controller with one narrow binding equivalent to:

```csharp
BindShortcutAuthority(
    capture,
    execute)
```

Conceptually:

```text
capture
    -> FrontendShortcutDashboardSnapshot

execute
    TileId
    -> Runtime internal Shortcut execution
    -> small transport execution outcome
```

Do not create:

```text
OverlayShortcutManager
ShortcutTransportService
ShortcutExecutionCoordinator
ShortcutSurfaceAuthority
ShortcutSessionStateMachine
```

`OverlayProcessController` is already the process/transport owner.

`ShortcutRuntime` is already the Shortcut state/execution owner.

That is enough.

---

# 14. AddonProcessHost wiring

Bind the same `_shortcutRuntime` instance created at process construction.

Recommended direction:

```text
_overlayController.BindShortcutAuthority(
    capture: ... _shortcutRuntime.Capture(),
    execute: ... HandleOverlayShortcutExecutionAsync(tileId, token))
```

Use one small Host adapter for execution admission.

Conceptually:

```csharp
private async Task<...> HandleOverlayShortcutExecutionAsync(
    Guid tileId,
    CancellationToken token)
{
    if (process shutdown || !_overlayCaptureActive)
        return failure;

    var result = await _shortcutRuntime.ExecuteAsync(tileId, token);
    return map-to-small-overlay-result(result);
}
```

Do not inspect ActionSpec in the Host.

Do not switch on EXE/PowerShell/URL/Screenshot in the Host.

The Host sees only:

```text
TileId
ShortcutExecutionResult
```

---

# 15. State publication timing

Do not add polling or a shortcuts.json file watcher.

## 15.1 Every new Overlay Show

The current Overlay process is warm and hidden between shows.

A Shortcut snapshot left in that process can become stale while the Main App is open and editing Shortcuts.

Therefore every Show must avoid displaying the previous session's Shortcut definitions as current.

Required UI behavior on `ResetUiForShow()`:

```text
Shortcut page
    -> clear previous rendered tiles
    -> clear selection
    -> show a lightweight Loading state
```

After the normal OQ4 capture commits, alongside the existing:

```text
RefreshQuickSettingsAsync
RefreshTabOrderAsync
RefreshClawHudAsync
```

also start:

```text
RefreshShortcutAsync
```

This obtains a fresh in-memory Runtime projection and replaces Loading with current state.

The capture is cheap and does not touch disk.

Do not delay or block the visible-surface transition waiting for the Shortcut snapshot.

Keep the same fire-and-forget post-capture publication pattern as the existing feature state.

## 15.2 StateInvalidated while visible

When the Overlay is currently captured and visible, the existing `StateInvalidated` path may also request:

```text
RefreshShortcutAsync
```

This keeps one obvious event-driven convergence path.

No polling.

No timer.

If a refresh arrives with the same tiles, the renderer may simply reapply it while preserving selected TileId.

---

# 16. Refresh failure policy

`OverlayProcessController.RefreshShortcutAsync()` should be feature-local.

If capture throws:

```text
send FrontendShortcutDashboardSnapshot.Unavailable(
    "Shortcut settings are unavailable.")
```

If transport publication fails:

- log a concise transport failure;
- do not affect Quick Settings / ClawHUD / Profile;
- do not tear down Full1902 controller ownership solely because Shortcut state failed to publish.

Existing Overlay connection/session rules still apply for actual pipe failure.

---

# 17. Execution response and refresh

After execution completes:

1. capture the current sanitized Shortcut dashboard;
2. return the execution result with that authoritative snapshot;
3. Overlay applies the snapshot;
4. if execution failed and the Overlay is still visible, show a concise feature-local failure message.

For Screenshot:

```text
execution request
-> Overlay retires through existing Screenshot callback
-> response may arrive while Overlay is hidden
-> client may still update its hidden in-memory page
-> no new visual surface is shown
```

Do not reopen the Overlay to show Screenshot success/failure.

---

# 18. Do not add generic auto-dismiss for other Shortcut actions

PR-E does **not** introduce a new policy that all Shortcut actions close the Overlay.

Current locked behavior:

```text
EXE
PowerShell
URL
    -> execute
    -> Overlay remains visible

Screenshot
    -> existing screenshot callback retires Overlay before capture
```

Screenshot is special because hiding is part of correct capture semantics and controller-release safety.

Do not route EXE/PowerShell/URL through the Screenshot retirement path.

If generic auto-dismiss is wanted later, treat it as a separate UX/product decision.

---

# 19. Replace the temporary fixed Shortcut renderer

Remove current product placeholders:

```text
TemporaryShortcutTiles
"Slot 1"
"Slot 2"
"Slot 3"
"Slot 4"
"Unassigned"
fixed 2-row assumption
TileCount = 4
```

`BuildShortcutPage()` should create a reusable page host.

The actual cards are built by:

```text
ApplyShortcutSnapshot(...)
```

or an equivalent narrow renderer method.

Do not construct a second Shortcut ViewModel layer.

The shared `FrontendShortcutTile` is already the renderer ViewModel.

---

# 20. Overlay Shortcut page states

The page must support three explicit states.

## Loading

Used after every Show reset until fresh Runtime state arrives.

Example:

```text
Loading shortcuts…
```

No selected tile.

Accept is a no-op.

## Available + empty

Example:

```text
No shortcuts configured.
Add shortcuts in the Main App.
```

Do not provide an Add button in Overlay.

No selected tile.

## Unavailable

Show:

```text
snapshot.FailureMessage
```

with a concise fallback if null.

No selected tile.

Do not offer destructive reset/repair from Overlay.

---

# 21. Dynamic tile renderer

For every tile, render only:

```text
Title
StatusText when non-null
selection visual
enabled/unavailable visual
```

Suggested compact card:

```text
Border
  StackPanel
    Title
    StatusText
```

Use existing Overlay card/theme resources where possible.

Do not add an icon download/cache system in PR-E.

Do not derive an icon from EXE paths because the Overlay does not receive paths.

## 21.1 Persisted order is visual order

Render:

```text
snapshot.Tiles[0]
snapshot.Tiles[1]
...
```

exactly in Runtime order.

No sorting by:

- title;
- action type;
- enabled state;
- status.

Main App drag/drop already owns persistent order.

## 21.2 No four-tile cap

Render every tile supplied by the available snapshot.

The body ScrollViewer already owns vertical overflow.

Do not add a product tile-count limit merely because the old POC had four slots.

---

# 22. Grid geometry

Keep PR-E simple and compatible with the current Overlay visual footprint.

Use at most two columns:

```text
tileCount == 0 -> no columns / no selection
tileCount == 1 -> 1 rendered column
tileCount >= 2 -> 2 rendered columns
```

So:

```csharp
renderedColumnCount = Math.Min(2, tileCount);
```

Rows are created dynamically:

```text
row    = index / renderedColumnCount
column = index % renderedColumnCount
```

This preserves the current two-column Shortcut visual language while removing the fixed two-row/four-slot product constraint.

Use compact card padding/height and let the page scroll vertically for larger collections.

Do not introduce responsive breakpoint infrastructure solely for PR-E.

If a later UI-polish pass wants 3-column/width-adaptive cards, it can change the renderer and pass the new actual column count to the same selection model.

---

# 23. Dynamic OverlayShortcutSelection

Refactor the existing pure local selection model.

It must no longer contain:

```text
TileCount = 4
ColumnCount = 2
```

It should own only transient UI selection for the current rendered layout.

Equivalent state:

```text
TileCount
ColumnCount
SelectedIndex?   // null when zero tiles
```

Suggested API shape:

```csharp
Configure(int tileCount, int columnCount, int? preferredIndex = null)
Reset(...)
Select(index)
MoveUp()
MoveDown()
MoveLeft()
MoveRight()
```

Exact naming may differ.

No navigation graph framework.

No focus manager.

No spatial-search abstraction.

## 23.1 Boundary semantics

Left / Right:

- stay inside the current row;
- no wrapping.

Up / Down:

- move one row;
- preserve column when that tile exists;
- if the target row is shorter, clamp to its last existing tile;
- no wrapping beyond first/last row.

Example with five tiles / two columns:

```text
0 1
2 3
4

3 + Down -> 4
4 + Up   -> 2
```

This is deterministic and handheld-friendly.

---

# 24. Preserve selection by TileId across live state refresh

Numeric index is not durable identity.

Before rebuilding a currently-visible Shortcut page:

1. resolve the selected tile's current `TileId`;
2. apply the new snapshot;
3. if the same TileId still exists, select its new index;
4. otherwise select the first tile;
5. if zero tiles, selection becomes null.

Do not persist Overlay selection.

Do not write selection into `shortcuts.json`.

On a new Overlay Show, Loading resets selection and the first current tile becomes selected when state arrives.

---

# 25. Controller navigation

The existing semantic controller path remains:

```text
OverlayControllerInputRouter
    -> OverlayNavigationAction
    -> Overlay App
    -> OverlayWindow
```

Do not read controller HID/raw state from the Overlay process.

While Shortcut tab is active:

```text
Up     -> ShortcutSelection.MoveUp()
Down   -> ShortcutSelection.MoveDown()
Left   -> ShortcutSelection.MoveLeft()
Right  -> ShortcutSelection.MoveRight()
Accept -> execute selected enabled TileId
Back   -> existing Overlay Back/dismiss policy
LB/RB  -> existing tab navigation
```

After moving selection, bring the selected tile into view using the existing no-animation `StartBringIntoView` pattern used by normal Overlay rows.

---

# 26. Disabled / unavailable tiles

`FrontendShortcutTile.Enabled` is Runtime-owned execution availability.

Disabled tiles remain visible in persisted order.

Examples:

```text
EXE target missing
unsupported action
invalid configuration
Screenshot capability unavailable
```

Render:

- title;
- Runtime `StatusText`;
- subdued/disabled visual.

Controller selection may still land on the tile so the user can see its status.

But Accept must not send an execute request when:

```text
Enabled == false
```

Pointer click on a disabled tile may select it, but must not execute it.

Do not infer why it is disabled from Action TypeId; the Overlay does not have that data.

---

# 27. Pointer behavior

A pointer click/tap on an enabled tile means:

```text
select tile
-> request execution of its TileId
```

This replaces the POC behavior where pointer input only selected the slot.

Use the same `ShortcutExecutionRequested(TileId)` intent path as controller Accept.

Do not create separate mouse execution logic.

---

# 28. One local execution-in-flight guard

Rapid A presses / repeated pointer taps must not queue multiple launches while the first request is unresolved.

Use one small Overlay-local guard.

For example:

```text
bool _shortcutExecutionInFlight
```

or an equivalent one-request semaphore in the existing client request path.

While true:

- additional Shortcut Accept/click intents are ignored;
- normal Hide/Back/navigation transport must remain processable;
- the pipe receive loop remains active.

Clear it in `finally`.

Do not add:

```text
execution queue
command scheduler
per-tile epochs
request history
retry manager
```

One explicit user invocation at a time is enough.

---

# 29. Overlay client request correlation

Follow the existing narrow correlated-request pattern already used by:

```text
Quick Settings mutation
ClawHUD mutation
tab-order move
```

Client needs:

```text
monotonic request id
one pending Shortcut execution
one completion source
one small serialization gate if needed
```

Do not reuse Quick Settings request IDs/state.

Do not make a generic command bus.

A Shortcut-specific pending request is clearer and smaller.

---

# 30. App.xaml.cs binding

`OverlayWindow` should expose one narrow event:

```csharp
internal event Action<Guid>? ShortcutExecutionRequested;
```

or equivalent.

`App` subscribes once and calls:

```text
NamedPipeOverlayClient.SendShortcutExecuteAsync(TileId)
```

When the response arrives:

```text
apply response.Snapshot
if failure and window is visible:
    show concise Shortcut failure
```

Do not let `OverlayWindow` own the transport client.

Keep the existing split:

```text
Window = presentation / local selection
App    = transport adapter / dispatcher marshal
```

---

# 31. Failure feedback

Runtime `StatusText` is the normal durable/current tile status.

For an execution failure that does not change Runtime projection, such as an ordinary launch failure, provide one small page-local message.

Examples:

```text
Shortcut could not be launched.
Screenshot is unavailable.
```

No toast framework is required.

No persistent failure history.

Clear the transient failure when:

- a new execution starts;
- a new authoritative Shortcut snapshot is applied;
- the Overlay begins a new Show session.

Do not expose exception messages or process command lines to the user.

---

# 32. Screenshot path — preserve the existing implementation exactly

For a Screenshot tile:

```text
Overlay selected TileId
    ↓
ShortcutExecuteRequest(TileId)
    ↓
ShortcutRuntime.ExecuteAsync(TileId)
    ↓
existing screenshot callback
    ↓
AddonProcessHost.ExecuteFullscreenScreenshotShortcutAsync
    ↓
_visibleSurfaceTransition
    ↓
RetireOverlayCaptureUnderTransitionAsync("ShortcutScreenshot")
    ↓
StopAcceptingNavigation
    ↓
Overlay Hide
    ↓
Hidden acknowledgement
    ↓
wait consumed controller A release
    ↓
resume/reconcile existing presentation
    ↓
NirCmd capture
```

Do not add any Screenshot-special branch in Overlay UI other than rendering the ordinary tile state.

The Overlay does not know it is a Screenshot action.

Only `ShortcutRuntime` resolves the action.

Do not add arbitrary delay/DwmFlush/frame barrier.

The existing Hidden acknowledgement remains the supported semantic boundary unless real hardware proves otherwise.

---

# 33. Important Screenshot transport test

Add a realistic test proving the new request handler does not deadlock the same Overlay connection.

Test shape:

```text
Overlay visible + Ready

client sends ShortcutExecuteRequest

server execute delegate enters and waits on a test gate
    (models Screenshot waiting for Overlay retirement)

while execute is still pending:
    Runtime/server sends OverlayCommand.Hide

client handles Hide
client sends OverlayState.Hidden

server receive loop consumes Hidden acknowledgement

test releases execute delegate

server sends ShortcutExecuteResult

client request completes
```

The test must prove that Shortcut execution runs outside `ServeAsync`'s read loop.

This is a real supported product path and is blocking if broken.

Do not replace this with an artificial instruction-level race test.

---

# 34. Refresh / execution concurrency

Keep the existing product model simple.

Supported practical cases:

```text
normal state refresh while visible
one Shortcut execution in flight
Hide / Back / Screenshot retirement
process shutdown
transport disconnect
```

The authoritative Shortcut document is already owned by one Runtime object.

Do not add locks/epochs solely because a theoretical state refresh could land on either side of one line of execution.

Required convergence:

- execution resolves TileId against Runtime's current document;
- response returns a fresh Runtime snapshot;
- later refreshes also return Runtime truth.

That is sufficient.

---

# 35. Privacy / logging

Overlay and Runtime logs may contain:

```text
TileId
request id
Succeeded
failure category / safe failure message
tile count
snapshot Available
```

Do not log:

```text
PowerShell source
EXE arguments
full executable path unnecessarily
full URL query
raw ActionSpec
raw action Parameters
editor snapshot
Screenshot configured folder unless existing diagnostics explicitly require it
```

The Overlay should never receive most of those values at all.

---

# 36. Full1902 lifecycle isolation

PR-E must not change:

```text
PID1901 <-> PID1902 policy
Center M reboot-bound authority
HidHide
VIIPER initialization/teardown
DirectInput physical ownership
X360 / SteamDeck presentation selection
PnP recovery
suspend / resume
shutdown / restart
routing rollback
```

The only interaction is existing Overlay capture/retirement.

Shortcut execution failure must never trigger controller recovery.

If Screenshot retirement fails, reuse its existing failure policy.

---

# 37. Expected production files

Likely changes:

```text
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs

src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs

src/SteamInputAddonforClaw.Overlay/App.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
src/SteamInputAddonforClaw.Overlay/OverlayShortcutSelection.cs

docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
```

Potential small change:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutFrontendContracts.cs
```

only if a tiny shared renderer/execution-result helper is genuinely needed.

Do not add a new project.

Do not modify Main App `ShortcutPage` unless a concrete regression requires it.

---

# 38. Tests — Shortcut selection

Replace the fixed-2x2 assumptions in:

```text
tests/SteamInputAddonforClaw.UiTests/OverlayShortcutSelectionTests.cs
```

Prove at minimum:

1. zero tiles -> no selection;
2. one tile -> selected index 0, one column;
3. two tiles -> two columns;
4. more than four tiles are supported;
5. Left/Right do not wrap rows;
6. Up/Down preserve column where possible;
7. short final row clamps to its last tile;
8. first/last row edges are bounded;
9. invalid Select index is rejected;
10. reconfigure after tile deletion normalizes selection;
11. preferred index can be restored when valid.

No scheduler/race tests.

---

# 39. Tests — renderer / Overlay UI

Add focused architecture/UI tests proving:

1. `TemporaryShortcutTiles` is gone;
2. literal `Slot 1` / `Slot 2` / `Slot 3` / `Slot 4` product data is gone;
3. Shortcut page consumes `FrontendShortcutDashboardSnapshot`;
4. available empty snapshot shows empty state;
5. unavailable snapshot shows failure state;
6. five or more tiles render in exact snapshot order;
7. one tile renders one column;
8. two or more tiles use the PR-E two-column layout;
9. disabled tiles show status and do not emit execution;
10. controller Accept emits selected enabled TileId;
11. pointer click emits the same TileId execution intent;
12. live snapshot refresh preserves selected TileId when it still exists;
13. missing selected TileId falls back to the first tile;
14. reset-for-show clears stale previous-session tiles to Loading;
15. Shortcut renderer contains no editor action fields / ActionSpec access.

---

# 40. Tests — Overlay transport v12

Update transport tests.

Prove:

1. `OverlayTransportProtocol.CurrentVersion == 12`;
2. v11 handshake is rejected;
3. ShortcutState round-trips an ordered sanitized snapshot;
4. malformed ShortcutState is rejected;
5. execute request requires `RequestId > 0`;
6. execute request requires non-empty TileId;
7. execute request carries no action payload;
8. execute result correlates by request id;
9. client permits only one pending Shortcut execution through the narrow guard;
10. not-Visible request does not invoke Runtime execution;
11. visible request invokes exactly the requested TileId;
12. execution failure returns a typed/safe failure without disconnecting the Overlay;
13. response contains the fresh authoritative sanitized snapshot;
14. oversized Shortcut snapshot becomes small `Available=false` instead of violating the 512 KiB frame;
15. the largest accepted Shortcut state/result fits the actual serialized Overlay envelope;
16. Screenshot-style in-flight execution does not block Hide/Hidden acknowledgement processing.

Do not increase `MaxFrameBytes` to make tests pass.

---

# 41. Tests — OverlayProcessController

Add focused tests for:

1. `BindShortcutAuthority` uses the bound capture delegate;
2. `RefreshShortcutAsync` publishes only to a live applicable session;
3. capture exception becomes unavailable Shortcut state;
4. execution delegate is supplied to each new Overlay server connection;
5. Shortcut state publish failure is feature-local;
6. Quick Settings / ClawHUD/Profile behavior is unchanged.

Do not add a generic authority registry.

---

# 42. Tests — AddonProcessHost

Prove source/behavior contracts:

1. Host binds the existing `_shortcutRuntime`, not a second instance;
2. capture uses `_shortcutRuntime.Capture()`;
3. execution receives TileId only;
4. process-shutdown admission rejects execution;
5. non-captured Overlay admission rejects execution;
6. admitted execution calls `_shortcutRuntime.ExecuteAsync(TileId)`;
7. after Overlay capture commit, `RefreshShortcutAsync` is scheduled;
8. existing Screenshot callback remains `ExecuteFullscreenScreenshotShortcutAsync`;
9. Screenshot still calls `RetireOverlayCaptureUnderTransitionAsync`;
10. no new Screenshot manager/coordinator/gate exists.

Existing Full1902 lifecycle suites must remain green.

---

# 43. Manual validation

On a real MSI Claw / Windows 11 test build:

## 43.1 Empty

```text
delete all Shortcuts in Main App
open Overlay
Shortcut tab
-> empty message
-> no fake Slot 1..4
```

## 43.2 More than four

Create at least six Shortcuts.

Verify:

- all six render;
- persisted order is exact;
- vertical scroll works;
- controller selection follows visible two-column geometry;
- last short row behaves correctly.

## 43.3 Reorder

In Main App:

```text
reorder shortcuts
close Main App
open Overlay
```

Verify the next Overlay session shows the new order.

No Runtime restart should be required because the same Runtime document was updated by PR-D.

## 43.4 EXE

Create a valid EXE Shortcut.

Verify:

- tile enabled;
- A executes exactly once;
- mouse click executes exactly once;
- Overlay remains visible;
- B still closes normally.

Then temporarily remove the EXE target.

On the next state/execute convergence:

- tile reports Not found / disabled;
- action does not launch.

## 43.5 PowerShell

Use a harmless script.

Verify one execution.

Confirm no script source appears in Overlay logs.

## 43.6 URL

Use an HTTPS URL.

Verify one browser launch.

Confirm Overlay received no URL string in its Shortcut state.

## 43.7 Screenshot

Create Screenshot Shortcut.

Verify exact lifecycle:

```text
A
-> Overlay hides
-> consumed A release
-> controller presentation resumes
-> JPEG captured
```

Verify:

- Screenshot does not contain the Addon Overlay;
- no A press leaks into the game;
- configured Main App Screenshot folder is respected by the existing Runtime callback;
- Overlay does not receive the folder path as tile/action configuration.

## 43.8 Failure isolation

Use malformed/newer `shortcuts.json`.

Verify:

- Shortcut page shows unavailable;
- other Overlay tabs still function;
- Full1902 controller remains functional;
- file is preserved.

---

# 44. Non-goals

Do not implement in PR-E:

```text
Shortcut editing in Overlay
Add/Delete/Edit/Reorder in Overlay
Shortcut editor dialog in Overlay
raw action JSON
EXE path display
PowerShell script display
URL display
Screenshot folder editor
icons extracted from EXEs
custom tile colors
per-tile icon selection
drag/drop inside Overlay
search/filter
folders/categories
macros/chains
hotkeys
keyboard action
media action
addon.tdp-preset
addon.fps-preset
addon.clawhud-toggle
addon.battery-limit
addon.power-mode
generic auto-dismiss after every action
QAM integration
CTW integration
polling / file watcher
new Shortcut lifecycle manager
```

Addon-native Shortcut actions are a later phase.

When added, they must call existing typed feature authorities rather than duplicating hardware/control logic.

---

# 45. Documentation update

Update:

```text
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
```

Its current architecture override already says the Main App Shortcut editor is separate.

After PR-E, document the now-live direction:

```text
Main App Shortcut
    -> edit/persist

ShortcutRuntime
    -> projection/execution authority

WinUI3 Overlay Shortcut
    -> dynamic sanitized tile renderer
    -> execute by TileId
```

Do not revive QAM-specific historical material.

If nearby historical sections still describe the four-slot POC as current, clearly mark them historical/superseded rather than rewriting unrelated history.

---

# 46. Validation commands

Run the normal repository baseline.

At minimum:

```powershell
dotnet restore SteamInputAddonforClaw.slnx

dotnet build SteamInputAddonforClaw.slnx -c Debug --no-restore
dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore

dotnet test SteamInputAddonforClaw.slnx -c Debug --no-restore
dotnet test SteamInputAddonforClaw.slnx -c Release --no-restore

git diff --check
```

Also run focused:

```text
Overlay Shortcut selection/UI tests
Overlay transport tests
Shortcut Runtime tests
AddonProcessHost Screenshot contract tests
Full1902 lifecycle tests
```

Do not claim physical-device validation unless it was actually performed.

---

# 47. PR acceptance checklist

## Authority

- [ ] one existing ShortcutRuntime remains the state/execution authority.
- [ ] Overlay never reads shortcuts.json.
- [ ] Overlay never receives editor action payloads.
- [ ] execution request contains TileId only.
- [ ] no duplicate action dispatcher exists.

## Transport

- [ ] Overlay protocol 11 -> 12.
- [ ] Main frontend protocol remains 42.
- [ ] Overlay max frame remains 512 KiB.
- [ ] ShortcutState added.
- [ ] correlated ShortcutExecute request/result added.
- [ ] execute handler runs outside the server read loop.
- [ ] Screenshot Hide/Hidden can be processed while execution is pending.
- [ ] oversized Shortcut state fails feature-locally.

## Overlay UI

- [ ] fixed Slot 1..4 data removed.
- [ ] all Runtime tiles render in persisted order.
- [ ] zero/one/>four tile states work.
- [ ] dynamic rows with actual 1-or-2 column count.
- [ ] selected TileId preserved on live refresh.
- [ ] disabled tiles visible but not executable.
- [ ] A and pointer click use the same TileId intent.
- [ ] no editor controls added.

## Screenshot

- [ ] same Screenshot Runtime callback.
- [ ] same visible-surface transition.
- [ ] same RetireOverlayCaptureUnderTransitionAsync path.
- [ ] no arbitrary delay/barrier.
- [ ] Overlay does not receive Screenshot folder/path authority.

## Lifecycle

- [ ] no PID/HidHide/VIIPER policy change.
- [ ] no controller recovery change.
- [ ] no suspend/resume change.
- [ ] no shutdown/restart change.
- [ ] Shortcut failure remains feature-local.

## Regression

- [ ] Quick Settings unchanged.
- [ ] ClawHUD Overlay controls unchanged.
- [ ] Profile catalog/detail unchanged.
- [ ] tab-order behavior unchanged.
- [ ] Main App Shortcut editor unchanged.
- [ ] existing EXE/PowerShell/URL/Screenshot Runtime tests pass.
- [ ] Full1902 suites pass.

---

# 48. Final architecture after PR-E

After this PR the first complete Shortcut vertical slice is:

```text
                    Main App Shortcut page
                    create/edit/delete/reorder
                              │
                              ▼
                        ShortcutRuntime
                   one authoritative document
                   one persistence authority
                   one execution authority
                       │              │
              Capture()│              │ExecuteAsync(TileId)
                       │              │
                       ▼              ▼
              sanitized dashboard   EXE / PowerShell /
                       │             URL / Screenshot
                       │
                       ▼
                 OverlayWire v12
                       │
                       ▼
              WinUI3 Overlay Shortcut
              dynamic tile renderer
              controller / pointer intent
```

The key rule remains:

> The Overlay chooses **which TileId** the user invoked.  
> The Runtime decides **what that TileId means and whether/how it executes**.

Do not weaken that boundary.
