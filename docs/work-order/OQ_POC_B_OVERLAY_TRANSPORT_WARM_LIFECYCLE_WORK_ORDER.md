# Work Order — OQ-POC-B: Dedicated Overlay Transport and Warm Lifecycle

## Status

Implementation work order for the second Addon Quick Settings Overlay proof-of-concept PR.

This Overlay track is intentionally independent from the numbered Full PID1902 controller-authority PR sequence.

Use the label:

```text
OQ-POC-B
```

Do not rename this work as `PR4`, `PR3.5`, or another Full PID1902 sequence number.

This work order assumes the OQ-POC-A implementation from PR #435 is the starting point:

```text
OQ-POC-A
→ standalone SteamInputAddonforClaw.Overlay.exe
→ WinUI 3 / PerMonitorV2
→ opaque left-side WorkArea panel
→ 400 DIP POC width
→ topmost / no-activate
→ direct-launch viability POC
```

If OQ-POC-A is not yet merged, implement OQ-POC-B only after rebasing onto the final OQ-POC-A result. Do not recreate the Overlay project independently.

Current design authorities:

- `docs/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/work-order/OQ_POC_A_OVERLAY_WINDOW_VIABILITY_WORK_ORDER.md`
- `docs/work-order/PR2_5_MANDATORY_CONTROLLER_RUNTIME_LIFETIME_WORK_ORDER.md`
- `docs/work-order/PR3_REBOOT_BOUND_CONTROLLER_AUTHORITY_TRANSITION_WORK_ORDER.md`

Current source seams that must be inspected before implementation:

- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`
- `src/SteamInputAddonforClaw/Hosting/RuntimeProcessApplication.cs`
- `src/SteamInputAddonforClaw/Lifecycle/QamHostProcessController.cs`
- `src/SteamInputAddonforClaw/Lifecycle/FrontendProcessLauncher.cs`
- `src/SteamInputAddonforClaw/Lifecycle/SystemTrayIcon.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs`
- OQ-POC-A `src/SteamInputAddonforClaw.Overlay/*`
- `scripts/publish-layout.ps1`
- `scripts/verify-publish-assets.ps1`
- `scripts/report-publish-size.ps1`
- corresponding tests under `tests/SteamInputAddonforClaw.Tests/`

The project is pre-release. Do not add compatibility layers for an Overlay transport/lifecycle that does not yet exist.

---

## 1. Goal

Move the OQ-POC-A window from a direct-launch disposable test executable to the intended **Runtime-owned warm hidden frontend lifecycle** without adding controller capture or production Quick Settings feature controls yet.

After OQ-POC-B, the desired process shape is:

```text
SteamInputAddonforClaw.exe
    Runtime
    ├─ existing .Frontend endpoint
    ├─ existing .Qam endpoint
    ├─ new .Overlay endpoint
    └─ one narrow Overlay process/lifecycle owner
                │
                ▼
SteamInputAddonforClaw.Overlay.exe
    WinUI initialized once
    window created once
    initially hidden
    connected to .Overlay
    warm / idle
```

The normal POC interaction becomes:

```text
Runtime ready
→ start .Overlay server
→ launch Overlay.exe once
→ Overlay connects
→ create window hidden
→ Overlay reports Ready/Hidden

POC Show request
→ Runtime sends Show
→ Overlay resolves current monitor / WorkArea / DPI
→ show no-activate
→ Overlay reports Visible

POC Hide request
→ Runtime sends Hide
→ Overlay hides without exiting
→ Overlay reports Hidden

next Show
→ reuse same process / XAML app / window
```

This PR answers:

> **Can the Addon Runtime keep the native WinUI Overlay warm and hidden, control it through an independent current-user pipe, and repeatedly Show/Hide it with low latency and failure isolation?**

Do not solve controller navigation, input neutralization, physical button policy, or Steam-QAM visual handoff in this PR.

---

## 2. Non-negotiable endpoint split

The architecture has three distinct frontend endpoints:

```text
Runtime
├─ .Frontend → SteamInputAddonforClaw.UI.exe
├─ .Qam      → SteamInputAddonforClaw.QamHost.exe
└─ .Overlay  → SteamInputAddonforClaw.Overlay.exe
```

Add to `FrontendPipeEndpoint` a narrow factory conceptually equivalent to:

```csharp
public static string CreateOverlayForCurrentUser()
{
    var desktop = CreateForCurrentUser();
    return $"{desktop}.Overlay";
}
```

The exact implementation may share the existing current-user SID/hash prefix logic.

Requirements:

- `.Frontend` name remains unchanged;
- `.Qam` name remains unchanged;
- `.Overlay` is distinct from both;
- all endpoints remain local/current-user only;
- supported scope remains one Windows user / one interactive session.

Do not make `.Frontend` multi-client.

Do not make `.Qam` multi-client.

Do not disconnect or restart QamHost to make room for Overlay.

The existing QamHost may remain alive and connected to `.Qam` while Overlay is alive and connected to `.Overlay`.

---

## 3. Do not reuse the full frontend RPC server as the Overlay lifecycle protocol

The current `NamedPipeAddonFrontendServer` exposes the full `IAddonFrontendControl` RPC surface and is designed around:

```text
frontend client → Runtime request
Runtime → frontend response
Runtime → StateInvalidated notification
```

OQ-POC-B needs a different immediate direction:

```text
Runtime → Overlay Show/Hide/Shutdown
Overlay → Runtime Ready/Visible/Hidden acknowledgement
```

Do not attach a third `NamedPipeAddonFrontendServer` using the entire `_frontendControl` merely because it already exists.

That would expose developer/setup/profile/device methods to a POC that does not need them and would make the Overlay boundary wider before its actual feature surface is known.

Likewise, do not rewrite the existing desktop/QAM transport to become a generalized multi-peer message bus.

### Preferred POC-B shape

Add one small Overlay-specific duplex transport in the existing `SteamInputAddonforClaw.FrontendTransport` project, for example:

```text
OverlayTransportProtocol
OverlayWireMessage / OverlayWireCodec
NamedPipeOverlayServer
NamedPipeOverlayClient
```

Exact names may follow repository conventions.

This is not a new authority layer. It is only the wire between Runtime and a disposable UI process.

The implementation should remain small enough that future OQ work can extend the same `.Overlay` endpoint with the actual narrow Quick Settings snapshot/mutation operations without creating another process or pipe.

Do not pre-build those feature operations in OQ-POC-B.

---

## 4. Overlay transport protocol scope

Use an independent Overlay protocol version, conceptually:

```text
OverlayTransportProtocol.CurrentVersion = 1
```

Do **not** bump `FrontendTransportProtocol.CurrentVersion` merely because `.Overlay` is being introduced.

The existing desktop and QAM wire contract is unchanged by this POC.

### 4.1 Minimum Runtime → Overlay commands

OQ-POC-B needs only:

```text
Show
Hide
Shutdown
```

No controller-navigation events yet.

No TDP/CPU/FPS/Profile RPC yet.

### 4.2 Minimum Overlay → Runtime states/acknowledgements

OQ-POC-B needs only the states required to prove lifecycle correctness:

```text
Ready
Visible
Hidden
```

A compact failure response/message is acceptable if needed to distinguish a command failure from a dropped connection.

Do not introduce a generalized event taxonomy.

### 4.3 Ready means the actual warm frontend is usable

Do not treat `Process.Start()` as Overlay readiness.

Do not treat `NamedPipeClientStream.ConnectAsync()` alone as full readiness.

For this POC, `Ready` should mean at least:

```text
WinUI Application initialized
OverlayWindow constructed
window HWND available
window currently hidden
.Overlay transport connected
command loop ready
```

Only after this state may Runtime call the Overlay warm/ready.

### 4.4 Visible/Hidden acknowledgement

`Show` and `Hide` must have bounded acknowledgements so Runtime can distinguish:

```text
command written
```

from:

```text
window actually reached the requested visible state path
```

This is not controller-safety synchronization yet. OQ-POC-B has no input neutralization.

A simple request/ack command sequence is enough. Do not add generations, epochs, barriers, or transaction IDs beyond the minimum correlation needed by the chosen wire shape.

---

## 5. Security and framing

Preserve the existing frontend security baseline:

```text
NamedPipeServerStream
PipeOptions.Asynchronous
PipeOptions.CurrentUserOnly
one Overlay client
```

Use bounded messages.

The existing frontend uses a bounded length-prefixed JSON wire. OQ-POC-B may follow the same simple framing principle.

Do not generalize `FrontendWireCodec` into a large shared serialization framework merely to save a few lines.

A small Overlay-specific codec is acceptable if that keeps the existing production frontend protocol untouched.

Do not use unbounded `ReadLine()` payloads from arbitrary local processes as the final pipe protocol.

Do not expose a global/public pipe.

---

## 6. Runtime lifecycle owner

Add one narrow Runtime-side lifecycle owner, suggested name:

```text
OverlayProcessController
```

Exact naming may follow repository conventions.

It should own the real Overlay process/transport lifecycle rather than spreading process state across `AddonProcessHost`, `SystemTrayIcon`, and the transport server.

Conceptual responsibility:

```text
OverlayProcessController
    ├─ .Overlay server lifetime
    ├─ Overlay.exe process lifetime
    ├─ warm readiness
    ├─ Show/Hide command delivery
    ├─ connection/process-loss observation
    └─ graceful Runtime shutdown of disposable Overlay
```

`AddonProcessHost` remains the composition/lifecycle caller.

Do not create:

- `OverlayAuthorityManager`;
- generic `ChildProcessSupervisor`;
- generic `FrontendProcessManager` hierarchy;
- heartbeat service;
- watchdog thread;
- restart epoch/state-machine framework.

Do not refactor `QamHostProcessController` and `FrontendProcessLauncher` into a common abstraction solely because another child process now exists.

Their lifecycle contracts are different:

```text
Main UI
→ launch on request and own its own single-instance behavior

QamHost
→ Steam/BPM session-scoped CDP integration

Overlay
→ Runtime-started warm hidden UI process
```

Keep the new owner direct and narrow.

---

## 7. Runtime startup sequence

The Overlay is optional UI. It must never block controller Runtime availability.

Preferred ordering after the normal Runtime frontend infrastructure is available:

```text
Runtime core initialized
→ existing .Frontend server starts
→ existing .Qam server starts / remains feature-local
→ start .Overlay server
→ launch Overlay.exe
→ wait bounded for Overlay Ready
→ continue Runtime regardless of Overlay success/failure
```

The exact placement in `AddonProcessHost.InitializeRuntimeAsync()` may follow existing startup ordering, but preserve these rules:

1. Overlay startup must not run before the Runtime has reached the point where a disposable frontend is allowed to connect.
2. Overlay startup failure must not fail `InitializeRuntimeAsync()`.
3. Overlay startup must not delay initial controller routing/reconcile or device profile safety paths by an unbounded wait.
4. Do not start Overlay from Startup prerequisite/hardware detection code.

### Bounded startup

Use a short bounded wait for initial Ready.

Do not wait forever for XAML or pipe connection.

If initial launch/connect/readiness fails:

```text
log warning
mark Overlay unavailable for now
continue Runtime
```

No controller state changes occur.

No QamHost state changes occur.

No Main UI state changes occur.

---

## 8. Warm hidden Overlay process behavior

Change the OQ-POC-A Overlay app lifecycle from:

```text
launch
→ ShowForPoc immediately
→ close window
→ process exits
```

into:

```text
launch by Runtime
→ initialize XAML
→ create OverlayWindow
→ keep window hidden
→ connect / complete .Overlay readiness
→ remain idle
```

The warm process should not render or poll continuously while hidden.

Hidden-state requirements:

- no animation loop;
- no controller polling;
- no telemetry polling;
- no high-rate timer;
- no repeated monitor/DPI polling;
- no busy reconnect loop.

The normal process remains alive because WinUI/pipe command lifetime remains alive, not because a synthetic timer keeps it alive.

---

## 9. Show behavior

When Runtime sends `Show`:

```text
Overlay receives Show
→ resolve current foreground target monitor fresh
→ read current WorkArea fresh
→ resolve current target-monitor DPI using the OQ-POC-A PerMonitorV2/two-pass rule
→ apply 400-DIP POC width
→ apply full WorkArea height
→ apply borderless/topmost/no-activate placement
→ show window without intentional activation
→ report Visible
```

Do not cache monitor, WorkArea, or DPI from process startup.

This matters because a warm process can remain alive while Windows scale, taskbar layout, display topology, or foreground application changes.

OQ-POC-A geometry remains authoritative:

```text
primary reference = 1920 × 1200 @ 150% / 144 DPI
POC width          = 400 DIP
150% physical width = 600 px
vertical geometry   = current monitor rcWork
```

The implementation must continue to support 96/120/144/168/192 DPI calculations without hard-coded scaling cases.

---

## 10. Hide behavior

`Hide` must hide the existing window without closing it and without terminating the process.

Desired:

```text
Visible
→ Runtime Hide
→ Window hidden
→ Hidden acknowledgement
→ same Overlay.exe remains connected and warm
```

Do not call `Window.Close()` for normal Hide.

Do not recreate the WinUI Application or XAML window on every Show.

Do not dispose and reconnect the `.Overlay` pipe on every Hide.

A repeated sequence such as:

```text
Show
Hide
Show
Hide
Show
```

should normally use:

```text
one process
one XAML application
one window
one connected .Overlay session
```

unless the Overlay actually crashes or is intentionally shut down.

---

## 11. Process/pipe loss policy

### 11.1 Hidden process dies

This is feature-local.

```text
Overlay hidden
→ Overlay.exe exits/crashes or pipe disconnects
→ Runtime/controller remain unaffected
→ QamHost remains unaffected
→ Main UI remains unaffected
```

Do not auto-restart it in a tight loop.

The next explicit POC Show request may make **one normal fresh launch attempt**.

If that attempt fails, log and return unavailable.

No watchdog is required.

### 11.2 Visible process dies in OQ-POC-B

OQ-POC-B still has **no controller capture and no neutral virtual output**.

Therefore:

```text
Overlay visible
→ process/pipe dies
→ game/controller remains live because Runtime never diverted input
→ mark Overlay unavailable
→ log feature-local failure
```

Do not implement the future OQ4 capture recovery path in this PR.

### 11.3 Pipe disconnect and process exit are enough

Do not add a periodic heartbeat merely to prove that a local process with an active pipe still exists.

Observe normal process/pipe termination.

---

## 12. Runtime shutdown

On controlled Addon Runtime shutdown/restart:

```text
stop accepting Overlay POC Show/Hide requests
→ if Overlay connection is ready, send Shutdown
→ Overlay closes its window/application and exits
→ wait bounded for graceful process exit
→ if disposable Overlay remains hung, terminate it
→ dispose .Overlay server
→ continue normal Runtime teardown
```

The Overlay process is disposable UI.

Its failure to respond must not block controller teardown indefinitely.

Do not let Overlay shutdown become the owner of:

- routing cleanup;
- HidHide cleanup;
- PID restoration;
- VIIPER teardown;
- device/profile shutdown.

`BeginProcessShutdown()` should stop new Overlay work early, consistent with the existing frontend/QamHost shutdown direction.

---

## 13. POC manual actuation

OQ-POC-B needs a way to exercise Runtime → Overlay Show/Hide before the physical WING/OEM1 policy exists.

Use the **smallest existing headless Runtime interaction surface** rather than adding a global keyboard hook/hotkey or physical-button mapping.

Preferred POC harness:

```text
Tray context menu
→ "Overlay POC: Toggle"
```

This is explicitly a temporary development/POC command, not the final product trigger.

It should call one narrow Runtime method, conceptually:

```text
ToggleOverlayForPoc()
```

which sends `Show` when hidden and `Hide` when visible.

Requirements:

- label clearly contains `POC`;
- no WING/OEM1 integration;
- no Win+G suppression change;
- no Steam Button/Quick Access change;
- no controller input capture;
- no Main UI mutation authority;
- no separate developer window.

Do not add a second tray icon for Overlay.

Do not make Overlay.exe own a notification-area icon.

### Important limitation of the tray trigger

The tray context menu is only an actuation harness for repeated warm Show/Hide and latency/process testing.

It is **not** the final proof of game-foreground semantics because opening a Windows tray context menu itself participates in foreground activation.

Continue to use the OQ-POC-A direct-launch test and later physical-button POC for strict foreground validation.

On the primary single-display MSI Claw hardware this harness is sufficient to validate warm lifecycle and geometry refresh.

---

## 14. Visible-surface coexistence is still out of scope

Do **not** implement OQ3 in this PR.

OQ-POC-B does not yet need to detect whether the injected Steam QAM panel is actually visible.

Do not:

- terminate QamHost when Addon Overlay shows;
- disconnect `.Qam`;
- add Steam-QAM visibility polling;
- inject new Steam QAM close/open commands;
- add a `QuickSurfaceManager`;
- change Main UI close behavior.

For manual POC testing, the tester may close Main UI / Steam QAM before exercising Addon Overlay.

The future product invariant remains:

```text
Steam QAM panel visible XOR Addon Overlay visible
Main UI visible       XOR Addon Overlay visible
```

but enforcing that belongs to the next visible-surface coexistence work.

---

## 15. Controller scope boundary

OQ-POC-B must cause **zero controller publication behavior change**.

Strictly out of scope:

- `ControllerState` consumption;
- DirectInput changes;
- XInput/GameInput reads;
- X360 publisher changes;
- SteamDeck publisher changes;
- X360 ↔ SteamDeck switching;
- OverlayCapture;
- neutral output;
- pause/resume presentation;
- release-to-resume gate;
- physical WING/OEM1 button mapping;
- Game Bar suppression changes;
- PID1901/PID1902 mutation;
- HidHide mutation;
- VIIPER server/bus mutation;
- Steam/BPM routing policy changes.

Showing or hiding the POC window must be a UI-only event.

This PR must remain safe to merge independently of the ongoing Full PID1902 PR3 controller-authority implementation.

---

## 16. No Quick Settings feature controls yet

Do not add actual TDP/CPU/FPS/Power/Profile cards in OQ-POC-B.

Do not add feature snapshot/mutation RPCs to `.Overlay` yet.

Do not create:

- `OverlayTdpManager`;
- `OverlayProfileStore`;
- Overlay EC/helper access;
- Overlay settings persistence;
- duplicate feature state.

Future controls will call the existing Runtime-owned feature authorities through a narrow Overlay transport extension.

POC-B only proves that the process/channel is suitable for that future work.

---

## 17. Packaging is now in scope

Unlike OQ-POC-A, OQ-POC-B requires Runtime to launch the Overlay from the actual staged product layout.

Add an internal publish directory, preferably:

```text
overlay/
    SteamInputAddonforClaw.Overlay.exe
    required runtime/XAML assets
```

Update `scripts/publish-layout.ps1` so the publish result is conceptually:

```text
artifacts/publish/
    SteamInputAddonforClaw.exe
    ... Runtime assets
    ui/
    qam/
    overlay/
```

The Runtime-side process owner should resolve:

```text
<AppContext.BaseDirectory>/overlay/SteamInputAddonforClaw.Overlay.exe
```

Do not install a second application.

Do not create another installer entry or Start-menu shortcut for Overlay.

Do not create separate Velopack update ownership.

### 17.1 Preserve POC-A WinUI deployment baseline first

For this POC, start with the OQ-POC-A project deployment settings that already build/run correctly.

Do **not** redesign the Windows App SDK deployment strategy in the same PR merely to optimize size.

However, explicitly measure and report the incremental staged size of `overlay/`.

If the self-contained WinUI payload produces a large duplicate footprint, record it as a follow-up packaging optimization backed by actual numbers rather than expanding OQ-POC-B into a deployment refactor.

Do not silently merge UI/Overlay publish directories or rely on overwrite behavior without a dedicated validation plan.

---

## 18. Publish verification

Update existing publish verification narrowly so CI proves the Runtime-launchable Overlay payload exists.

At minimum verify:

```text
overlay/SteamInputAddonforClaw.Overlay.exe exists
```

and any WinUI application PRI/XBF assets required by the POC-A project are preserved in the staged Overlay output.

Follow the existing UI publish-asset preservation pattern only where the new project actually requires it.

Do not copy unrelated main-UI assets or SettingsControls dependencies into Overlay.

Update publish-layout/report tests only as required by the new `overlay/` component.

Do not broaden release artifact policy beyond the new internal component.

---

## 19. Logging

Add low-rate lifecycle logging sufficient for hardware POC analysis.

Useful Runtime events:

```text
Overlay pipe server starting / ready / stopped
Overlay process launch attempted / started / exited
Overlay Ready received
Show requested
Visible acknowledged
Hide requested
Hidden acknowledged
Overlay connection lost
Overlay graceful shutdown requested
Overlay forced termination if required
```

Useful Overlay-side events may be written to a small Overlay log in the existing Addon log directory when Runtime passes the log-directory argument.

Do not log continuously while hidden.

Do not log every Win32 window message.

Do not add telemetry loops.

### Latency timestamps

For POC measurement, log enough timestamps/durations to estimate:

```text
Runtime Show request → Overlay Visible acknowledgement
Runtime Hide request → Overlay Hidden acknowledgement
```

Do not build a metrics framework.

A direct elapsed-ms field on lifecycle log entries is sufficient.

---

## 20. Suggested implementation shape

A reasonable minimal file-level shape is:

```text
SteamInputAddonforClaw.FrontendTransport/
    FrontendWire.cs                         # add CreateOverlayForCurrentUser only
    OverlayWire.cs                          # protocol v1 + small codec/messages
    NamedPipeOverlayServer.cs
    NamedPipeOverlayClient.cs

SteamInputAddonforClaw/
    Lifecycle/OverlayProcessController.cs
    Hosting/AddonProcessHost.cs             # compose/start/stop owner
    Lifecycle/SystemTrayIcon.cs             # temporary POC Toggle command

SteamInputAddonforClaw.Overlay/
    Program.cs / App.xaml.cs                 # managed warm lifecycle
    OverlayWindow.xaml(.cs)                  # hidden at startup, Show/Hide methods
    WindowInterop.cs                         # reuse POC-A geometry/show logic
    OverlayLog.cs                            # only if needed; keep small

scripts/
    publish-layout.ps1
    verify-publish-assets.ps1
    report-publish-size.ps1                 # only if component accounting requires change

tests/
    Overlay transport tests
    Overlay process-controller tests
    publish-layout / asset-verifier tests as required
```

Exact filenames are not mandated.

Keep the diff focused. If one helper class can remain private/internal to the owner instead of becoming another abstraction, prefer that.

---

## 21. Connection/start ordering

Avoid a launch race by using a simple deterministic order:

```text
1. Runtime creates/listens on .Overlay server
2. Runtime launches Overlay.exe
3. Overlay connects to .Overlay
4. protocol handshake succeeds
5. Overlay creates/owns hidden usable window
6. Overlay reports Ready/Hidden
7. Runtime marks warm-ready
```

If the Overlay process starts before the server becomes ready due to implementation details, a short **bounded startup acquisition retry** is acceptable.

Do not add a permanent reconnect loop.

The preferred implementation is server-first so ordinary startup needs no retry complexity.

### After an actual crash

If Overlay dies and a later POC Show requests it again:

```text
ensure server can accept a new client
→ launch one new Overlay process
→ bounded wait for Ready
→ send Show
```

Do not restart continuously while no user requests the Overlay.

---

## 22. Command serialization

Show/Hide/Shutdown are one UI lifecycle stream.

Use one narrow serialization gate in the Overlay process controller/server if needed so two actual user requests cannot mutate the same window concurrently.

Do not add:

- command epochs;
- sequence reconciliation state machine;
- desired/actual surface authority graph;
- barrier tokens.

For realistic use:

```text
Toggle
→ complete Show/Hide command
→ accept next Toggle
```

is sufficient for this POC.

If repeated clicks occur while one command is in flight, either coalesce to the latest desired visible bool or reject/ignore the duplicate using the smallest implementation already natural to the controller.

Do not build theoretical instruction-level race protection.

---

## 23. State ownership

The only new POC state that the Runtime needs is the UI-process fact necessary to command it, conceptually:

```text
Process = absent | running
Transport = disconnected | ready
Visibility = hidden | visible
Stopping = false | true
```

These are process-lifecycle facts, not product controller authority.

Do not persist any of them to disk.

Do not add a generalized state-store.

Overlay visibility after Runtime restart is not restored.

Fresh Runtime startup always targets:

```text
Overlay warm + hidden
```

---

## 24. Sleep / hibernate / resume in POC-B

Do not build special Overlay power state machinery yet.

OQ-POC-B has no controller capture, so the safe initial behavior is:

```text
normal Windows suspend occurs
→ process/window may be suspended by Windows
→ no controller authority changes

resume
→ Overlay remains hidden if it was hidden
```

If the POC window happened to be visible during a manual test, it is acceptable for OQ-POC-B to hide it on the existing Runtime power-resume observation **only if this can be done with a very small call through the existing power event**.

Otherwise record visible-across-resume behavior for OQ7 rather than adding a new power watcher here.

Do not create an Overlay-specific WMI/power watcher.

The Full PID1902 controller lifecycle remains authoritative and unchanged.

---

## 25. Automated tests

Add deterministic tests for the new behavior without requiring a real interactive desktop wherever possible.

### 25.1 Endpoint tests

Verify:

```text
CreateForCurrentUser() != CreateQamForCurrentUser()
CreateForCurrentUser() != CreateOverlayForCurrentUser()
CreateQamForCurrentUser() != CreateOverlayForCurrentUser()
Overlay endpoint has stable current-user-derived prefix/suffix behavior
```

Do not assert a literal SID/hash value.

### 25.2 Overlay wire handshake

Test:

- matching Overlay protocol version succeeds;
- mismatched version fails before commands are accepted;
- invalid/oversized frame is rejected;
- one disconnected client allows a later new client to connect.

### 25.3 Command/ack tests

Test at least:

```text
Show → Visible
Hide → Hidden
Shutdown → client exits/connection retires
```

The transport test may use fake callbacks instead of constructing real WinUI.

### 25.4 Process controller tests

Test practical lifecycle behavior:

- initial warm start launches once;
- already-warm Show does not launch a second process;
- Hide keeps process alive;
- hidden process loss is contained;
- later explicit Show can attempt one relaunch;
- startup/connection failure does not throw through Runtime initialization path;
- BeginShutdown rejects new Show/Hide;
- Dispose retires transport/process.

Use existing repository testing patterns before inventing a process abstraction solely for tests.

If a tiny injected `ProcessStartInfo → Process?` delegate is sufficient, prefer that over a generic child-process interface.

### 25.5 Existing regression suite

Full tests must remain green.

There must be no regression to:

- Main UI frontend connection;
- QamHost `.Qam` connection;
- QamHost session lifecycle;
- Runtime single-instance activation;
- tray Restart/Exit policy;
- controller routing/VIIPER/HidHide tests;
- publish-layout verification.

---

## 26. CI / build requirements

Before completion, run at minimum:

```text
dotnet restore SteamInputAddonforClaw.slnx
dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Release --no-build --no-restore
git diff --check
```

Also execute the normal publish layout used by CI and verify the new Overlay staged payload.

Do not report OQ-POC-B complete if the solution builds but the Runtime's configured `overlay/SteamInputAddonforClaw.Overlay.exe` does not exist in the staged publish tree.

---

## 27. Manual hardware validation

Primary reference device:

```text
MSI Claw display
1920 × 1200
Windows scaling 150%
144 DPI reference
```

Also retain the POC-A DPI behavior for other scaling values.

### Required POC-B hardware checks

#### Startup / hidden

- launch Addon Runtime normally;
- confirm `SteamInputAddonforClaw.Overlay.exe` starts once;
- confirm no Overlay window is visible initially;
- confirm `.Overlay` connects / Ready is logged;
- record hidden Working Set / Private Bytes;
- record hidden CPU after settling;
- confirm existing QamHost can still run/connect independently.

#### Warm Show / Hide

Using the temporary tray `Overlay POC: Toggle` command:

- first Show appears correctly;
- Hide makes the window disappear without Overlay.exe exiting;
- second/third Show reuse the same PID;
- geometry is recomputed on each Show;
- taskbar remains uncovered;
- 150% width is approximately 600 physical px for the 400-DIP POC width;
- repeated Show/Hide does not accumulate windows or processes.

#### Performance

Record:

- hidden idle CPU;
- hidden memory;
- Runtime Show request → Visible ack ms;
- Runtime Hide request → Hidden ack ms;
- repeated Show/Hide latency;
- obvious game frametime/VRR regression if tested over a representative borderless game.

No hard numeric product threshold is frozen by this work order; record real results first.

#### Failure

While hidden:

```text
kill Overlay.exe
→ Runtime remains alive
→ controller remains usable
→ QamHost remains unaffected
```

Then request Show:

```text
Runtime performs one fresh Overlay launch attempt
→ new Overlay PID connects
→ panel can show
```

While visible:

```text
kill Overlay.exe
→ Runtime remains alive
→ controller behavior is unchanged because OQ-POC-B has no capture
```

#### Runtime shutdown

- Exit/Restart according to existing allowed policy;
- Overlay receives graceful shutdown where possible;
- no orphan Overlay.exe remains after Runtime completes shutdown;
- a hung Overlay must not hang controller/runtime teardown indefinitely.

---

## 28. Acceptance criteria

OQ-POC-B is complete only when all of the following are true.

### Architecture

- [ ] `.Overlay` endpoint exists and is distinct from `.Frontend` and `.Qam`.
- [ ] `.Frontend` transport remains single-client and otherwise unchanged.
- [ ] `.Qam` remains dedicated to the existing Steam QamHost.
- [ ] QamHost does not need to stop/disconnect for Overlay to run.
- [ ] Overlay lifecycle transport is narrow and does not expose the full frontend control surface.
- [ ] no new controller/settings authority is introduced.

### Process lifecycle

- [ ] Runtime starts Overlay warm/hidden after its server is ready.
- [ ] Overlay reports Ready only after XAML/window/pipe are usable.
- [ ] Show/Hide reuse the same normal process/window/session.
- [ ] startup failure is feature-local.
- [ ] hidden process crash is feature-local.
- [ ] next explicit Show can make one fresh launch attempt.
- [ ] Runtime shutdown retires Overlay without unbounded wait.
- [ ] no watchdog/heartbeat/service is added.

### Window behavior

- [ ] each Show re-resolves target WorkArea/DPI.
- [ ] OQ-POC-A PerMonitorV2 geometry contract remains intact.
- [ ] hidden means actually not visible, not an off-screen visible window.
- [ ] Hide does not close the process.
- [ ] Show still uses no-activate/topmost behavior.

### Packaging

- [ ] staged product contains `overlay/SteamInputAddonforClaw.Overlay.exe`.
- [ ] required Overlay XAML/WinUI assets are present.
- [ ] no second installer/app registration is created.
- [ ] incremental Overlay staged size is recorded.
- [ ] Windows App SDK deployment is not unnecessarily redesigned in this POC.

### Scope

- [ ] no WING/OEM1 mapping change.
- [ ] no Game Bar suppression change.
- [ ] no Steam-QAM visible-surface handoff implementation.
- [ ] no Main UI/Overlay mutual-exclusion implementation.
- [ ] no controller capture.
- [ ] no neutral output.
- [ ] no presentation switching.
- [ ] no PID/HidHide/VIIPER mutation.
- [ ] no TDP/CPU/FPS/Profile Quick Settings cards.

### Verification

- [ ] focused Overlay endpoint/transport tests pass.
- [ ] focused Overlay process lifecycle tests pass.
- [ ] full test suite passes.
- [ ] release build passes.
- [ ] staged publish verification passes.
- [ ] manual Claw warm Show/Hide behavior is documented in the PR description or follow-up test result.

---

## 29. Explicitly deferred to later Overlay work

Do not pull these into OQ-POC-B:

### OQ3 / visible-surface coexistence

```text
Steam QAM visible ↔ Addon Overlay visible handoff
Main UI ↔ Addon Overlay visible handoff
real Steam-QAM visibility/close seam
```

### OQ4 / controller capture

```text
OverlayCapture
current presentation neutralization
semantic controller navigation
release-to-resume
crash while input is neutral
```

### OQ5 / actual controls

```text
TDP
CPU Boost
Power Mode
FPS
active-game/profile cards
```

### OQ6 / physical button policy

```text
WING/OEM1 final mapping
Game Bar suppression for selected QAM button
Steam Button / Quick Access final mapping
```

Do not solve later phases early simply because the new `.Overlay` pipe exists.

---

## 30. Anti-overengineering constraints

The supported product remains:

```text
1 Windows user
1 interactive session
MSI Claw supported hardware
```

Protect realistic failures:

- Overlay executable missing from staged product;
- process launch failure;
- pipe connection/handshake failure;
- Overlay crash;
- command failure;
- hung disposable Overlay during Runtime shutdown;
- DPI/WorkArea changing between warm process startup and later Show.

Do not add machinery for theoretical instruction-level races.

Specifically avoid:

- service/supervisor process;
- heartbeat protocol;
- auto-restart loop;
- epoch/generation/barrier system;
- multi-user/session arbitration;
- generalized child-process framework;
- generalized UI-surface authority manager;
- generalized IPC/event bus;
- multi-client rewrite of existing frontend servers;
- duplicate settings/device authorities.

The intended result is deliberately small:

> **one dedicated Overlay endpoint, one disposable warm Overlay process owner, one hidden WinUI window, and one simple Show/Hide lifecycle.**

---

## 31. Expected final POC-B architecture

```text
                         SteamInputAddonforClaw.exe
                                Runtime
                                  │
             ┌────────────────────┼─────────────────────┐
             │                    │                     │
        .Frontend              .Qam                .Overlay
             │                    │                     │
             ▼                    ▼                     ▼
      Main UI.exe           QamHost.exe            Overlay.exe
      disposable UI         Steam CEF/QAM          WinUI 3
                                                warm + hidden
                                                     │
                                             Show / Hide only
                                                     │
                                             NO controller capture
```

Process coexistence is valid:

```text
Main UI process may exist
QamHost process may exist
Overlay process may exist
```

OQ-POC-B does **not** yet enforce simultaneous visible-surface policy.

Controller architecture is unchanged:

```text
Controller Runtime / routing / PID / HidHide / VIIPER
→ completely unaffected by Overlay Show/Hide
```

---

## 32. Completion statement

A successful OQ-POC-B should be describable as:

> The Addon now stages and warm-starts a dedicated WinUI Overlay process through a current-user-only `.Overlay` pipe. The process remains hidden and idle until Runtime issues a POC Show command, can be hidden and shown repeatedly without process/XAML recreation, recomputes WorkArea/DPI on every Show, and fails independently from the controller Runtime and existing Steam QamHost. No controller capture, presentation mutation, or Quick Settings feature control is implemented yet.
