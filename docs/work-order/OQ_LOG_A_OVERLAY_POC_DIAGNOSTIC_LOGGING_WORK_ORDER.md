# Work Order — OQ-LOG-A: Overlay POC Diagnostic Logging

## Status

Implementation work order for a focused diagnostic follow-up after the merged Overlay warm-lifecycle POC.

Use the label:

```text
OQ-LOG-A
```

Do not number this as part of the Full PID1902 PR sequence.

Current `main` baseline when this work order was prepared:

```text
088cfcf796408a46882d55b94895251b0cb14acb
Add warm Overlay transport lifecycle (#436)
```

PR #436 is merged and establishes:

```text
Runtime
├─ .Frontend
├─ .Qam
└─ .Overlay

SteamInputAddonforClaw.Overlay.exe
→ Runtime-owned warm hidden WinUI process
→ dedicated .Overlay pipe
→ Show / Hide / Shutdown commands
→ Ready / Visible / Hidden acknowledgement
→ temporary tray "Overlay POC: Toggle"
```

Read before implementation:

- `docs/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/OQ_POC_A_OVERLAY_WINDOW_VIABILITY_WORK_ORDER.md`
- `docs/work-order/OQ_POC_B_OVERLAY_TRANSPORT_WARM_LIFECYCLE_WORK_ORDER.md`
- current `main` implementations of:
  - `src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs`
  - `src/SteamInputAddonforClaw.Overlay/Program.cs`
  - `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
  - `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs`
  - `src/SteamInputAddonforClaw.Overlay/WindowInterop.cs`
  - `src/SteamInputAddonforClaw.FrontendTransport/FrontendLaunchArguments.cs`
  - `src/SteamInputAddonforClaw.FrontendTransport/AddonLogRetention.cs`
  - `src/SteamInputAddonforClaw.UI/Diagnostics/UiLog.cs`
  - `src/SteamInputAddonforClaw/Diagnostics/AppLog.cs`

This PR is diagnostic instrumentation only.

It must not change Overlay behavior, controller behavior, transport semantics, presentation policy, or physical-button policy.

---

## 1. Goal

Make the first real MSI Claw Overlay hardware POC diagnosable from collected log files without attaching a debugger.

Current PR #436 logging is sufficient to answer broad questions such as:

```text
Did Runtime attempt to start Overlay?
Did a Show/Hide acknowledgement fail?
Did Overlay.exe exit?
```

but it is not sufficient to reliably answer:

```text
Did Overlay.exe itself initialize correctly?
Did its XAML window exist before the pipe became Ready?
Did the dispatcher receive the Show command?
Which foreground HWND/monitor was selected?
What rcMonitor / rcWork / DPI were used?
What physical width was calculated from 400 DIP?
Did Win32 placement succeed but Show fail?
How long did warm startup take?
How long did Show → Visible acknowledgement take?
How long did Hide → Hidden acknowledgement take?
Did graceful shutdown succeed or did Runtime have to kill Overlay.exe?
```

The intended result is that a normal user/tester can reproduce an Overlay problem, provide the Addon log directory, and the failure can be separated into:

```text
Runtime launch/process problem
vs
.Overlay readiness/transport problem
vs
WinUI dispatcher problem
vs
monitor/WorkArea/DPI geometry problem
vs
Show/Hide Win32 window operation problem
vs
shutdown problem
```

without adding a debugger-only workflow.

---

## 2. Current logging gap

### 2.1 Runtime side

`OverlayProcessController` currently logs several important outer failures, including:

- Overlay executable missing;
- startup failure;
- Show/Hide acknowledgement failure;
- successful POC shown/hidden state;
- process exit;
- cleanup failure.

However, it does not currently record enough correlation/timing information for the POC.

For example, a log entry saying:

```text
Overlay POC shown.
```

cannot tell us whether the visible acknowledgement took 15 ms or 4.9 seconds.

Likewise, process startup currently does not record the launched PID together with the elapsed time until `Ready`.

### 2.2 Overlay process side

The larger gap is inside `SteamInputAddonforClaw.Overlay.exe`.

Current `App.ConnectAndRunAsync()` handles failures with:

```csharp
System.Diagnostics.Debug.WriteLine($"Overlay transport failed: {exception}");
```

That is not a useful installed-build diagnostic path.

Runtime already launches Overlay with:

```text
--log-directory <existing Addon log directory>
```

but the Overlay process currently does not consume that argument or create its own persistent log file.

Therefore a XAML/dispatcher/window failure inside Overlay may leave only a generic Runtime timeout or process-exit symptom.

OQ-LOG-A fixes that observability gap.

---

## 3. Core design rule

Logging must observe the existing architecture, not create a new one.

Instrument the three real boundaries that already own the work:

```text
Runtime lifecycle
→ OverlayProcessController

Overlay application/dispatcher lifecycle
→ Overlay App / Program

Window geometry/show/hide
→ OverlayWindow / WindowInterop
```

Do not introduce:

- logging service DI infrastructure;
- transport tracing manager;
- heartbeat;
- pipe observer;
- process watchdog;
- monitor polling;
- dispatcher polling;
- generalized diagnostics event bus;
- Overlay state-machine redesign;
- protocol sequence numbers merely for logging;
- extra IPC messages merely for logging.

The transport is already bounded and acknowledged. Use its existing boundaries.

---

## 4. Add a small Overlay-side file logger

Add one narrow Overlay-local logger, suggested location/name:

```text
src/SteamInputAddonforClaw.Overlay/Diagnostics/OverlayLog.cs
```

or, if the project is intentionally kept flat:

```text
src/SteamInputAddonforClaw.Overlay/OverlayLog.cs
```

Do not reference the Runtime project just to reuse `AppLog`.

The Overlay already references `SteamInputAddonforClaw.FrontendTransport`, so reuse:

```csharp
FrontendLaunchArguments.ResolveLogDirectory(args)
AddonLogRetention.PruneDirectory(...)
```

as appropriate.

A small logger modeled after the current `UiLog` is sufficient.

### 4.1 Log directory

The Overlay log must use the same directory passed by Runtime through:

```text
--log-directory
```

Fallback behavior should be the existing `FrontendLaunchArguments.ResolveLogDirectory(args)` fallback.

Do not create a second product log root.

### 4.2 File name

Use a process-identifiable name such as:

```text
overlay-<PID>.log
```

or an equivalent timestamp/PID form.

The exact filename is not important as long as:

- it is clearly distinguishable from Runtime and UI logs;
- each Overlay process launch is attributable;
- it ends in `.log` so existing `AddonLogRetention.PruneDirectory()` can manage it.

Do not make the filename depend on a new persisted setting.

### 4.3 Logger behavior

The logger should support only what the POC needs, conceptually:

```csharp
Info(category, message, fields...)
Warn(category, message, exception, fields...)
Error(category, message, exception, fields...)
```

A `Debug` helper is acceptable, but do not build a second full Runtime logging subsystem.

This is a low-volume process. A simple synchronized append logger like current `UiLog` is acceptable.

Do not add a background queue solely for Overlay logging in this PR unless measurement proves synchronous writes are a real problem.

### 4.4 Failure policy

Logging must remain best-effort.

A log-directory permission or write failure must never prevent Overlay from starting/showing/hiding.

Match the current UI logging philosophy:

```text
try to log
→ swallow logging-only I/O failure
→ continue Overlay lifecycle
```

---

## 5. Configure logging at the earliest practical Overlay entry point

In `SteamInputAddonforClaw.Overlay/Program.cs`, configure the Overlay log from `args` before WinUI application startup work.

Conceptually:

```csharp
OverlayLog.ConfigureDirectory(args);
AddonLogRetention.PruneDirectory(OverlayLog.DirectoryPath);
OverlayLog.Info("App", "Overlay process starting", ...);

ComWrappersSupport.InitializeComWrappers();
Application.Start(...);
```

Record at least:

```text
PID
assembly/app version
ProcessPath
BaseDirectory
OS version
.NET runtime version
process architecture
```

Do not duplicate the very large Runtime startup header if the additional fields are not useful for Overlay diagnosis.

A compact process-start line is enough.

If WinUI bootstrap/application startup throws before `App` is constructed, attempt to persist the exception before process termination where practical.

Do not add global crash-recovery behavior in this PR.

---

## 6. Overlay application / dispatcher lifecycle logging

Instrument the existing `App` lifecycle directly.

Useful events:

```text
OnLaunched entered
DispatcherQueue acquired
OverlayWindow constructed
initial hidden preparation started
initial hidden preparation completed
.Overlay command loop starting
.Overlay command loop ended/disconnected
application exit requested
```

### 6.1 Command reception

For each low-rate lifecycle command, log one receipt/completion pair or one concise completion line:

```text
Show received
Show completed

Hide received
Hide completed

Shutdown received
Shutdown completed / window close requested
```

The command rate is human interaction scale, so this is safe.

### 6.2 Dispatcher failures

The following must produce persistent Overlay log entries:

```text
DispatcherQueue unavailable
TryEnqueue returned false
window unavailable
Show handler threw
Hide handler threw
Shutdown handler threw
transport loop failed
```

Include the exception text/stack for real failures.

Replace the current `Debug.WriteLine()`-only transport failure path with persistent logging.

`Debug.WriteLine()` may remain as an extra development aid, but it cannot be the only record.

### 6.3 Do not log every pipe frame

Do not log:

```text
4-byte length prefix read
JSON frame serialized
pipe write completed
pipe read completed
```

The existing Runtime acknowledgement/timing plus Overlay command receipt is enough to locate the failure boundary.

Do not modify `OverlayWireCodec` simply to add verbose frame tracing.

---

## 7. Window/geometry logging

The first hardware POC specifically needs to validate:

```text
1920 × 1200 physical display
150% scaling / 144 DPI reference
400 DIP temporary panel width
expected physical panel width ≈ 600 px
full current WorkArea height
foreground monitor selection
no taskbar overlap
```

`WindowInterop.Configure()` already owns all the authoritative facts needed for this diagnosis:

- Overlay HWND;
- foreground HWND;
- selected monitor handle;
- `rcMonitor`;
- `rcWork`;
- effective DPI from `GetDpiForWindow()`;
- calculated final `OverlayRect`.

Log these facts there rather than inventing a second monitor/DPI inspection path.

### 7.1 Required successful geometry record

After successful final geometry calculation/application, emit one concise structured line containing at least:

```text
OverlayHwnd
ForegroundHwnd
MonitorLeft
MonitorTop
MonitorRight
MonitorBottom
WorkLeft
WorkTop
WorkRight
WorkBottom
Dpi
Scale
PanelWidthDip
PanelWidthPx
PanelHeightPx
```

For the primary reference machine, a healthy example should make it obvious that the calculation is approximately:

```text
DPI=144
Scale=1.50
PanelWidthDip=400
PanelWidthPx=600
```

Do not hard-code those runtime results.

### 7.2 Show-time freshness

Because PR #436 keeps Overlay warm, every `Show` already reruns `ConfigureWindow()`.

The log must therefore let us prove that each Show used a fresh foreground/window/WorkArea/DPI observation.

Do not add a monitor/DPI polling loop.

Do not cache geometry for logging.

### 7.3 Win32 operation failures

The existing exceptions from:

- monitor selection;
- `GetMonitorInfo`;
- provisional `SetWindowPos`;
- `GetDpiForWindow`;
- final `SetWindowPos`;
- Show `SetWindowPos`;
- Hide `SetWindowPos`;

must reach a persistent Overlay error log with operation context.

Do not silently convert these failures into generic `Show failed` without the original exception.

---

## 8. Runtime lifecycle/timing logging

Enhance `OverlayProcessController` using the existing `AppLog` only.

Do not add a second Runtime logger.

### 8.1 Warm startup

Record:

```text
Overlay warm start requested
Overlay server ready / launch about to begin
Overlay process started
Overlay Ready confirmed
```

Useful structured fields:

```text
Path
PID
ElapsedMs
```

`ElapsedMs` for warm startup should measure a meaningful interval, preferably:

```text
before process launch/start sequence
→ Ready acknowledgement confirmed
```

Use `Stopwatch` / elapsed ticks locally.

Do not add a global metrics collector.

### 8.2 Show/Hide latency

For each explicit POC toggle, record the command and acknowledgement latency.

Desired shape:

```text
[Overlay] Overlay command requested Command=Show PID=1234
[Overlay] Overlay command acknowledged Command=Show PID=1234 ElapsedMs=22
```

and similarly for Hide.

If acknowledgement fails:

```text
Command=Show
PID=<tracked PID if available>
ElapsedMs=<time until failure/timeout>
Action=RetireSession
```

Keep the existing PR #436 behavior:

```text
failed Show/Hide acknowledgement
→ retire current Overlay session
→ next explicit toggle may make one fresh launch attempt
```

Logging must not change that behavior.

### 8.3 Process exit

Current log says the Overlay process exited but does not provide enough identity/result information.

When safely available, include:

```text
PID
ExitCode
WasVisible
Stopping
```

Do not add fragile process inspection after disposal merely to obtain these fields.

If ExitCode cannot be read safely, log PID/state only.

### 8.4 Shutdown

Record the real shutdown path:

```text
Overlay graceful shutdown requested
Shutdown command sent / unavailable
process exited gracefully ElapsedMs=...
```

or:

```text
Overlay graceful shutdown timed out
process tree termination requested
process termination completed
```

The existing 3-second bounded shutdown behavior must remain unchanged unless a concrete bug is found.

Do not increase shutdown time merely for logging.

---

## 9. Correlation policy

Do not build a new correlation-ID protocol.

The following are enough for this POC:

```text
Runtime log timestamp
Overlay log timestamp
Overlay PID
command name
```

Runtime already owns the child `Process` and therefore knows the PID.

Overlay log naturally knows its own PID.

That is sufficient to correlate:

```text
Runtime Show requested at 17:31:12.100 PID=1234
Overlay Show received at 17:31:12.102 PID=1234
Overlay geometry applied at 17:31:12.108 PID=1234
Overlay Show completed at 17:31:12.111 PID=1234
Runtime Visible ACK at 17:31:12.113 PID=1234 ElapsedMs=13
```

Do not add request IDs, epochs, or sequence numbers solely for manual POC log analysis.

---

## 10. Log levels / volume policy

The Runtime `AppLog` already supports Debug/Info/Warn/Error and may be disabled by user policy.

Use levels intentionally.

Recommended Runtime split:

### Info

Low-rate lifecycle milestones worth seeing in ordinary diagnostic logging:

```text
process started / PID
Ready confirmed
Show acknowledged
Hide acknowledged
process exited
shutdown result
```

### Debug

Extra POC detail that is useful when Debug logging is enabled:

```text
start request
command request before acknowledgement
server/path details already duplicated elsewhere
```

### Warn/Error

Actual abnormal operation:

```text
startup timeout
command timeout/failure
pipe/process loss
forced termination
cleanup failure
```

For the Overlay-local logger, do not add a new settings-sync protocol just to mirror Runtime's log-level preference.

Because the Overlay lifecycle log is extremely low volume, it may always persist its small lifecycle/geometry record while this POC is under validation.

Do not continuously log while hidden.

---

## 11. Performance constraints

This logging PR must preserve the POC performance goal.

No logging occurs at controller-publisher frequency.

No logging occurs at frame rate.

No logging occurs from a periodic hidden-state timer.

Expected volume is roughly:

```text
process startup: a few lines
Show:           a few lines
Hide:           a few lines
shutdown:       a few lines
failure:        exception details only when needed
```

A user rapidly toggling the POC should still produce human-scale logs, not thousands of lines per second.

Do not instrument XAML layout/render callbacks that can fire repeatedly.

---

## 12. Explicitly out of scope

Do not implement in OQ-LOG-A:

- controller input capture;
- ControllerState logging;
- raw DirectInput logging;
- semantic controller navigation;
- X360/SteamDeck neutralization;
- release-to-resume gate;
- WING/OEM1 mapping;
- Game Bar suppression changes;
- Steam QAM visibility detection;
- Main UI/Overlay mutual exclusion;
- TDP/CPU/FPS/Power feature cards;
- feature mutation RPCs;
- Overlay transport v2;
- `.Frontend` protocol changes;
- `.Qam` protocol changes;
- heartbeat/watchdog;
- process auto-restart loop;
- renderer changes;
- DPI/WorkArea policy changes;
- window-style changes unless a logging implementation exposes a concrete existing bug.

This PR must have zero controller-routing lifecycle behavior impact.

---

## 13. Suggested implementation shape

Keep the diff small.

Likely files:

```text
src/SteamInputAddonforClaw.Overlay/
    Program.cs
    App.xaml.cs
    OverlayWindow.xaml.cs          [only if needed for clear command-level logging]
    WindowInterop.cs
    Diagnostics/OverlayLog.cs      [new, or equivalent flat file]

src/SteamInputAddonforClaw/
    Lifecycle/OverlayProcessController.cs

tests/SteamInputAddonforClaw.Tests/
    OverlayLogTests.cs             [or focused additions to existing Overlay tests]
    OverlayTransportTests.cs       [only for controller/timing behavior if needed]
```

Do not change `OverlayWire.cs` merely to get frame-level logs.

Do not refactor `UiLog` and `OverlayLog` into a generalized cross-frontend logger in this PR.

A little duplication is preferable to introducing a new shared logging architecture for two tiny frontend loggers.

---

## 14. Testing requirements

### 14.1 Overlay log path

Add deterministic tests proving:

```text
--log-directory C:\some\absolute\path
→ OverlayLog writes into that directory
```

and the existing fallback path works through `FrontendLaunchArguments.ResolveLogDirectory()` where practical.

Do not re-test every `FrontendLaunchArguments` case already covered elsewhere.

### 14.2 Log file identity

Verify the Overlay log filename is distinct from UI/Runtime and contains enough process identity to find the correct child process log.

### 14.3 Geometry log formatting

If geometry formatting is factored into a pure helper, test the important fields.

Do not introduce a geometry abstraction solely to make logging testable.

It is acceptable to keep the actual Win32 logging integration as hardware/manual validation and test only the logger formatting/path behavior.

### 14.4 Runtime timing behavior

Do not make tests assert exact milliseconds.

Where practical, verify that successful/failing process-controller paths include an elapsed-time field/event through a narrow injectable log sink only if one already exists.

Do **not** add an `ILogger` interface to `OverlayProcessController` solely to unit-test log strings.

Behavior tests from PR #436 remain the primary correctness tests.

### 14.5 Full regression

Run:

```text
dotnet restore SteamInputAddonforClaw.slnx
dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Release --no-build --no-restore
git diff --check
```

The existing Overlay transport/lifecycle tests must remain green.

---

## 15. Manual hardware validation protocol

After implementation, perform the first real Claw test with Runtime Debug logging enabled where possible.

Primary reference:

```text
Display: 1920 × 1200
Scale:   150%
DPI:     expected 144
POC DIP width: 400
Expected physical panel width: approximately 600 px
```

### Test A — startup hidden

1. Start the installed Addon normally.
2. Do not open Overlay yet.
3. Confirm `SteamInputAddonforClaw.Overlay.exe` is running warm/hidden.
4. Confirm an `overlay-*.log` exists in the same Addon log directory.
5. Confirm the log shows window creation/hidden readiness without continuous repeated entries.

### Test B — first Show

1. Keep an ordinary desktop/game window foreground.
2. Tray → `Overlay POC: Toggle`.
3. Confirm Overlay appears.
4. Record whether foreground remained on the original app/game.
5. Verify logs contain:

```text
Runtime command requested
Overlay command received
foreground HWND
monitor/work-area geometry
DPI/scale
600-ish px panel width at 150%
Overlay Show completed
Runtime Visible acknowledgement + ElapsedMs
```

### Test C — Hide / Show reuse

1. Toggle Hide.
2. Toggle Show again.
3. Confirm the same Overlay PID is reused.
4. Confirm each Show contains a fresh geometry record.
5. Confirm Hide/Show acknowledgement timings are recorded.

### Test D — foreground monitor change

If a second display is available:

1. Show on one monitor.
2. Hide.
3. Move foreground app to the other monitor/scaling configuration.
4. Show again.
5. Confirm the new log records the new foreground HWND / monitor / WorkArea / DPI.

Do not add special multi-monitor product policy if this fails; first use the logs to identify the concrete problem.

### Test E — process failure

While Overlay is hidden or visible:

1. terminate `SteamInputAddonforClaw.Overlay.exe` manually;
2. confirm Runtime logs the exited PID/state;
3. confirm controller/Runtime/QamHost remain unaffected;
4. use the next explicit tray Toggle;
5. confirm one fresh Overlay PID is launched and its new log file can be correlated.

### Test F — Runtime shutdown

Exit/restart Runtime through a currently allowed product path.

Confirm logs prove either:

```text
Shutdown requested
→ Overlay exits gracefully within bound
```

or, if intentionally testing a hung Overlay:

```text
Shutdown timeout
→ forced termination
```

without changing controller teardown authority.

---

## 16. Diagnostic acceptance checklist

The PR is complete when a collected Runtime + Overlay log set can answer all of the following without attaching a debugger:

- [ ] Which Overlay PID was launched?
- [ ] How long did process launch → Ready take?
- [ ] Did the Overlay command loop start successfully?
- [ ] Did Overlay receive Show/Hide/Shutdown?
- [ ] Did the WinUI dispatcher successfully execute the command?
- [ ] Which foreground HWND was used for target-monitor selection?
- [ ] What `rcMonitor` was selected?
- [ ] What `rcWork` was selected?
- [ ] What DPI/scale was observed?
- [ ] What physical width resulted from 400 DIP?
- [ ] Did final window placement succeed?
- [ ] Did Show/Hide complete?
- [ ] How long did Runtime wait for Visible/Hidden acknowledgement?
- [ ] If command acknowledgement failed, was the current session retired as PR #436 requires?
- [ ] If Overlay exited unexpectedly, which PID/state exited?
- [ ] Did graceful Runtime shutdown close Overlay or require forced termination?
- [ ] Is there no periodic hidden-state log spam?
- [ ] Are `.Frontend`, `.Qam`, controller routing, HidHide, VIIPER, and presentation behavior unchanged?

---

## 17. Review policy for this PR

Review only realistic diagnostic/correctness issues.

Blocking examples:

- Overlay exceptions still only go to `Debug.WriteLine()` and are absent from files;
- log directory argument is ignored;
- logger failure can crash/prevent Overlay startup;
- geometry log records stale/cached DPI rather than the actual Show-time values;
- Show/Hide timing is measured across the wrong operation and is misleading;
- added logging accidentally changes Overlay acknowledgement/lifecycle behavior;
- hidden Overlay generates continuous log traffic;
- logging introduces controller/runtime coupling or a new transport dependency.

Non-blocking/theoretical examples:

- nanosecond ordering differences between Runtime and Overlay timestamps;
- hypothetical log write races that cannot materially lose normal low-rate POC diagnostics;
- adding a centralized logging abstraction merely to remove a few duplicated lines;
- adding correlation generations/epochs for impossible-to-confuse human-scale Show/Hide tests.

Keep the implementation simple and directly useful for real MSI Claw POC analysis.
