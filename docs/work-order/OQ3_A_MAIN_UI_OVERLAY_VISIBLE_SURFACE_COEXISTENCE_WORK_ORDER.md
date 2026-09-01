# Work Order — OQ3-A: Main UI ↔ Addon Overlay Visible-Surface Coexistence

## Status

Focused Overlay lifecycle PR.

Label: `OQ3-A`

This is part of the Addon Quick Settings Overlay track, not the numbered Full PID1902 implementation sequence.

Read together with:

- `docs/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- current `docs/work-order/OQ_*` Overlay work orders

## 1. Goal

Enforce the first visible-surface coexistence rule:

```text
Main UI visible
        XOR
Addon Overlay visible
```

The Runtime remains the orchestration owner.

Two normal product paths must work:

```text
Main UI open
→ Addon Overlay requested
→ request the Main UI's normal close path
→ wait for the .Frontend client to disconnect
→ only then Show Overlay
```

and:

```text
Addon Overlay visible
→ Main UI requested
→ Hide/retire Overlay first
→ only then launch/activate Main UI normally
```

Do not terminate either process merely to switch visible surfaces during the normal successful path.

## 2. Current implementation facts to preserve

Current Main UI lifecycle is already clean and disposable:

```text
MainWindow closes
→ App.OnMainWindowClosed
→ existing UI-local cleanup
→ UiShutdownCoordinator
→ .Frontend client Dispose
→ Application.Exit
→ UI process exits
```

Do not replace this path.

Current Runtime seams are:

- `AddonProcessHost.RequestFrontendOpen(...)`
- `FrontendProcessLauncher.RequestOpen(...)`
- `AddonProcessHost.ToggleOverlayForPoc()`
- `OverlayProcessController.ToggleForPocAsync()`
- one `.Frontend` `NamedPipeAddonFrontendServer`
- one independent `.Overlay` transport owned by `OverlayProcessController`

Current Overlay lifecycle already has one `_transition` gate and one `_visible` fact. Reuse them.

## 3. Frozen ownership rules

This PR is UI/process coordination only.

It must not change:

- Center M authority;
- PID1901/PID1902;
- DirectInput ownership;
- HidHide state;
- VIIPER ownership;
- X360/SteamDeck presentation selection;
- Steam/BPM state;
- Game Bar policy;
- QamHost lifetime;
- `.Qam` transport.

OQ4 controller capture/neutral publication is not implemented here.

Therefore, in OQ3-A, Overlay Hide means only the current Overlay visibility lifecycle. A later OQ4 change will extend that same retirement path with capture release rather than creating another coexistence owner.

## 4. Use the existing `.Frontend` pipe for a normal UI close request

Do not add another IPC endpoint, process scanner, HWND scanner, WMI watcher, or UI-process owner.

Extend the existing frontend notification contract with one semantic notification:

```text
CloseRequested
```

Conceptually:

```csharp
internal enum FrontendNotificationKind
{
    StateInvalidated,
    CloseRequested
}
```

Because this changes the wire contract, bump:

```text
FrontendTransportProtocol.CurrentVersion
18 → 19
```

Add the corresponding protocol-version comment in `FrontendWire.cs`.

The Addon is pre-release. Do not add old-protocol forwarding or compatibility fallback.

## 5. Frontend client handling

`NamedPipeAddonFrontendClient` should expose a narrow event:

```csharp
public event EventHandler? CloseRequested;
```

Its existing read loop should recognize the new notification exactly as it already recognizes `StateInvalidated`:

```text
Notification / CloseRequested
→ raise CloseRequested
→ continue read loop
```

Do not turn frontend notifications into a generalized command bus.

## 6. Main UI handling — use the existing normal Close path

In `SteamInputAddonforClaw.UI/App.xaml.cs`:

- subscribe to `_frontendClient.CloseRequested`;
- marshal the request onto the existing UI `DispatcherQueue`;
- call the existing `MainWindow.Close()` path;
- unsubscribe during frontend disposal.

Normal visible case:

```text
Runtime CloseRequested
→ UI dispatcher
→ MainWindow.Close()
→ existing OnMainWindowClosed cleanup
→ existing ShutdownAndExitAsync("WindowClosed")
→ frontend Dispose
→ process exit
```

Do not bypass `OnMainWindowClosed` by directly killing the UI process.

### Close request during UI startup

Keep this narrow.

If the frontend is connected but `_mainWindow` has not been created yet, retain one boolean pending-close fact in `App`.

After `MainWindow` construction, honor the pending close before normal activation:

```text
CloseRequested while starting
→ pendingClose = true
→ MainWindow constructed
→ Close()
→ do not activate/show it first
```

No epoch, request ID, queue, or UI surface state machine is needed.

If dispatching the close request fails, leave the UI alive. The Runtime-side timeout below must then prevent Overlay Show.

## 7. Runtime must positively observe Main UI retirement before Overlay Show

Add one narrow Runtime-side operation on the existing `.Frontend` server, conceptually:

```csharp
Task<bool> RequestClientCloseAsync(TimeSpan timeout, CancellationToken token)
```

Contract:

```text
no connected .Frontend client
→ return true

connected client
→ send CloseRequested
→ wait for THIS current frontend connection to disconnect
→ return true

send fails and the connection is already gone
→ treat as successfully retired

send fails while the client is still connected
→ return false

timeout while client remains connected
→ return false
```

Do not poll `IsConnected` in a timer loop.

Use one connection-completion signal associated with the currently served frontend connection and complete it from the existing `ServeAsync` teardown/finally boundary.

The server already permits only one connected frontend. Do not add session IDs, generations, epochs, or a multi-client model.

### Write serialization

A Runtime-originated `CloseRequested` notification must not interleave bytes with existing responses or `StateInvalidated` notifications.

Reuse one server write gate for all frames on the current server instead of creating an independent unsynchronized write path.

Do not change the one-client transport architecture.

### Timeout

Use one bounded close wait slightly above the existing UI shutdown coordinator's 5-second bound, e.g.:

```text
MainUiCloseTimeout = 6 seconds
```

One attempt only.

No retries and no forced Main UI process kill in OQ3-A.

## 8. Add one narrow cross-surface transition gate in `AddonProcessHost`

Main UI and Overlay already have their own internal lifetime synchronization, but OQ3-A introduces a real cross-surface ordering requirement.

Use one narrow `SemaphoreSlim` (or equivalent existing primitive) owned directly by `AddonProcessHost` for only these two operations:

```text
Request Main UI
Toggle/Show Addon Overlay
```

Do not create:

- `UiSurfaceManager`;
- `OverlayAuthorityManager`;
- surface enums/state machines;
- epochs/barriers;
- a generalized frontend coordinator service.

The gate exists only so normal user requests cannot intentionally execute the two opposite visibility transitions at the same time.

## 9. Overlay-request path

Replace the direct host call:

```text
ToggleOverlayForPoc()
→ OverlayProcessController.ToggleForPocAsync()
```

with host-level coordination.

### Overlay already visible

A normal toggle-off remains simple:

```text
Overlay visible
→ Hide Overlay
```

No Main UI close request is needed.

### Overlay hidden and Show is requested

Required order:

```text
acquire AddonProcessHost visible-surface transition gate
        ↓
request current Main UI close through .Frontend
        ↓
if no Main UI is connected, continue immediately
        ↓
if connected, wait for .Frontend disconnect
        ↓
close success
        ↓
Overlay Show
        ↓
release gate
```

Failure:

```text
Main UI close send/timeout failure
→ DO NOT Show Overlay
→ leave controller/presentation behavior unchanged
→ log feature-local failure
```

Do not kill Main UI as a fallback.

## 10. Main-UI-request path

Route normal Runtime-owned Main UI requests through the same host-level transition gate:

- tray Open;
- Runtime secondary-instance activation;
- other existing `RequestFrontendOpen(...)` paths.

Required order when Overlay is visible:

```text
acquire visible-surface transition gate
        ↓
ensure Overlay hidden/retired
        ↓
only after Overlay is no longer visibly active
        ↓
FrontendProcessLauncher.RequestOpen(reason)
        ↓
release gate
```

If Overlay Hide is not acknowledged, reuse `OverlayProcessController`'s existing bounded session-retirement behavior. Do not invent a second termination path in `AddonProcessHost`.

Only launch/activate the Main UI once the Overlay controller can positively report that the visible Overlay session is gone.

When Overlay is already hidden, preserve the existing frontend launch/activation behavior.

## 11. Make Overlay operations directional enough for coordination

`OverlayProcessController.ToggleForPocAsync()` currently owns both directions internally.

OQ3-A may add narrow explicit operations such as:

```csharp
internal bool IsVisible { get; }
internal Task<bool> ShowAsync(...)
internal Task<bool> EnsureHiddenAsync(...)
```

or an equivalently small shape.

Requirements:

- reuse the existing `_transition` gate;
- reuse `_visible` as the single Overlay visibility fact;
- reuse existing Show/Hide command ACK handling;
- reuse existing failed-command session retirement;
- do not add another Overlay visibility state enum/state machine.

`ToggleForPocAsync()` may remain as a thin compatibility/test wrapper if useful, but `AddonProcessHost` must be able to enforce the correct direction explicitly.

## 12. Logging

Add concise order-verification logs, for example:

```text
[UiSurface] Main UI close requested before Overlay Show
[UiSurface] Main UI retired before Overlay Show
[UiSurface] Overlay Show blocked because Main UI did not retire
[UiSurface] Overlay Hide requested before Main UI open
[UiSurface] Overlay retired before Main UI open
```

Do not add continuous visibility logging or polling diagnostics.

The important hardware evidence is ordering, not high-rate state reporting.

## 13. Tests

Extend existing tests rather than creating a new test framework.

### Frontend transport

In/near `FrontendNamedPipeTransportTests` verify:

1. `CloseRequested` notification reaches `NamedPipeAddonFrontendClient.CloseRequested`.
2. `RequestClientCloseAsync` does not complete successfully merely because the notification was sent; it succeeds after the client disconnects.
3. no connected frontend returns success immediately.
4. a connected client that does not close produces one bounded timeout/failure.
5. normal RPC responses and `StateInvalidated` remain valid with the shared write gate.

### Overlay controller

Extend `OverlayTransportTests` as needed to prove:

1. explicit Hide when visible sends one Hide and leaves `IsVisible == false`;
2. explicit Hide when already hidden is idempotent;
3. failed Hide reuses the existing bounded session retirement;
4. explicit Show preserves current warm-process behavior.

### Main UI

Do not introduce a mock WinUI application architecture solely to test `Window.Close()`.

If the existing UI lifecycle seams allow a small focused test, use them. Otherwise rely on transport tests plus the required real UI validation below.

## 14. Required manual validation

### A. Main UI → Overlay

```text
1. Start Runtime.
2. Open Main UI normally.
3. Request Addon Overlay.
```

Expected:

```text
Main UI closes normally
→ UI frontend disconnects
→ Overlay Show is requested afterward
→ Overlay becomes visible
```

There must be no interval in the normal completed transition where both finished visible surfaces remain on screen.

Verify the Main UI process exits through its existing shutdown path rather than being killed.

### B. Overlay → Main UI

```text
1. Show Addon Overlay.
2. Use tray/application activation to request Main UI.
```

Expected:

```text
Overlay Hide completes
→ Overlay process remains warm/hidden
→ Main UI launch/activation occurs
→ Main UI visible
```

Do not terminate the warm Overlay process.

### C. Main UI close failure

Use a transport test or controlled diagnostic condition where the frontend does not retire.

Expected:

```text
CloseRequested
→ bounded timeout/failure
→ Overlay is NOT shown
```

No Runtime shutdown, controller mutation, or forced UI kill.

### D. Repeated normal switching

Repeat several times:

```text
Main UI → Overlay → Main UI → Overlay
```

Expected:

- one Main UI process at a time;
- one warm Overlay process reused;
- no broken pipe;
- no stuck visible state;
- no process accumulation.

## 15. Explicit non-goals

Do not include OQ3-B or later work:

- Steam QAM visible-state detection;
- Steam QAM close/open commands;
- QamHost termination/restart;
- controller Overlay capture;
- neutral virtual publication;
- release-to-resume controller gating;
- physical WING/OEM1 button assignment;
- Game Bar suppression changes;
- Quick Settings controls;
- TDP/FPS/CPU Boost UI;
- new frontend multi-client support;
- process enumeration as visibility authority;
- HWND enumeration as visibility authority.

In particular:

```text
QamHost alive != Steam QAM visible
```

remains an OQ3-B problem and must not be solved in this PR.

## 16. Acceptance criteria

- [ ] Main UI and Addon Overlay use mutually exclusive normal visible-surface transitions.
- [ ] Main UI is retired through its existing `MainWindow.Close()` / shutdown path.
- [ ] Runtime waits for `.Frontend` disconnect before Overlay Show.
- [ ] Overlay Show is blocked if Main UI cannot be positively retired within the bound.
- [ ] Main UI open requests Hide/retire Overlay first.
- [ ] Overlay process remains warm after Hide.
- [ ] `.Frontend`, `.Qam`, and `.Overlay` endpoint ownership remains unchanged.
- [ ] frontend protocol version is bumped for `CloseRequested`.
- [ ] existing frontend RPC/StateInvalidated behavior remains green.
- [ ] existing Overlay Show/Hide/outside-click/no-activate behavior remains green.
- [ ] no controller/PID/HidHide/VIIPER changes are included.
- [ ] no generalized UI surface manager/state machine is introduced.

## 17. Verification

Run the repository's current required validation, including at minimum:

```powershell
dotnet restore SteamInputAddonforClaw.slnx

dotnet build SteamInputAddonforClaw.slnx `
    -c Release `
    --no-restore

dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj `
    -c Release `
    --no-build `
    --no-restore

git diff --check
```

Keep the PR focused on Main UI ↔ Addon Overlay coexistence.

Final design rule:

> Use the existing frontend connection as the Main UI lifetime seam, the existing Overlay controller as the Overlay lifetime owner, and one narrow Runtime ordering gate. Do not create a third UI-surface authority.
