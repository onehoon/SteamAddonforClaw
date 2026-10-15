# Work Order — Overlay Cold-Start Ready Timeout and Main UI Startup Diagnostics

> Date: 2026-10-15  
> Repository: `onehoon/SteamAddonforClaw`  
> Target: latest `main` (re-read HEAD immediately before implementation)  
> Product: standalone Full1902, one Windows user / one interactive session  
> Implementation: local Codex, **one small PR containing two narrowly scoped changes**  
> Hardware verification: performed by the user **after merge**; never a local-Codex implementation requirement, CI gate, or PR-review blocker

## 1. Locked scope / intended outcome

This work order intentionally contains **one behavioral fix** and **one diagnostics-only change**.

1. **Overlay behavior fix:** increase the `OverlayProcessController` initial `Ready` wait timeout from **5 seconds to 12 seconds**, so a legitimate cold WinUI 3 startup can finish its existing pipe handshake instead of being killed just after it has created the hidden window.
2. **Main UI diagnostic logging:** instrument the **pre-IPC cold-start interval** and the existing UI startup milestones with low-volume timestamped/elapsed-time logs. Do **not** optimize or otherwise change startup behavior in this PR.

These two changes belong in one PR. Do not introduce a generic startup manager, a retry framework, adaptive timeout policy, new worker/thread, launch broker, or UI warm-host architecture.

## 2. Required architecture and source baseline

Read and follow:

- `docs/Full 1902 Implementation/README.md`, including its document-precedence rules.
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`.
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`.
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md` — existing separate warm hidden Overlay and Main UI/Overlay surface coordination.
- `docs/work-order/2026-10-13_MAIN_UI_DEVICE_FIRST_DEFERRED_PAGE_WARMUP_AND_ON_DEMAND_CATALOG_WORK_ORDER.md` — already-implemented Main UI optimizations, **not** work to redo.

Source files already inspected on `main`:

- `src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeOverlayServer.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeOverlayClient.cs`
- `src/SteamInputAddonforClaw.Overlay/Program.cs`
- `src/SteamInputAddonforClaw.UI/Program.cs`
- `src/SteamInputAddonforClaw.UI/App.xaml.cs`
- `src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs`
- `src/SteamInputAddonforClaw.UI/Diagnostics/UiLog.cs`
- `src/SteamInputAddonforClaw/Lifecycle/FrontendProcessLauncher.cs`
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`

The Runtime remains the elevated Full1902/controller authority. Main UI and Overlay are disposable child surfaces inheriting its token. Preserve the existing per-user named pipes and all existing exclusive-visible-surface/capture-retirement policy. There is **no CTW integration**.

## 3. Evidence: logs from 2026-10-15

Source: Google Drive `Addon/Log/AV2M/1015`. Do not claim benchmarks outside the observed sessions.

### 3.1 Cold Overlay repeatedly misses the current 5-second Ready window

On `0.1.386.0`:

| Event | Local timestamp |
| --- | --- |
| Runtime launches/tracks Overlay process | 22:14:59.032 (approximately) |
| Overlay first process-start log | 22:15:02.294 |
| Overlay `OnLaunched` | 22:15:03.664 |
| OverlayWindow constructed | 22:15:03.986 |
| Hidden preparation finished and Overlay command loop started | 22:15:04.171 |
| Runtime initiates timed-out Overlay process termination | 22:15:07.054 |

The Overlay does not reach its IPC loop until **after** the approximately 5-second Ready deadline measured from the Runtime's process start. The Runtime then disposes the server and, if the child has not exited after the existing bounded shutdown wait, terminates it.

Earlier `0.1.385.0` runs at 20:59 and 21:19 show the same cold-start timeout behavior. A later `0.1.386.0` explicit Overlay launch at 22:16:59 reached `Ready` in approximately **1.27 seconds** and handled Show/Hide successfully.

The evidence supports a too-short cold-start handshake deadline. It does **not** prove that the Windows App SDK itself is the only expensive substep, nor does it justify transport rewrites.

### 3.2 First Main UI open is slow before the already-instrumented window creation

On `0.1.386.0`:

| Phase | Timestamp / elapsed |
| --- | --- |
| Runtime `Frontend open requested` (Tray) | 22:14:50.730 |
| Runtime `Frontend process launch succeeded` | 22:14:52.825 |
| UI `Frontend connected` | 22:14:58.463 |
| UI `Bootstrap acquired` | 22:14:58.669 |
| UI `MainWindow activated` | 22:14:59.229 |
| Total request-to-activation | ~8.50 s |

Decomposition:

- Open request to `Process.Start` return: ~2.10 s (this includes pre-launch coordination and the `Process.Start` call; existing logs do not distinguish them).
- `Process.Start` return to `Frontend connected`: ~5.64 s (**not** yet broken down by existing UI logs).
- Frontend connection to window activation: ~0.77 s.

By contrast, the separate 22:23:03 Tray request reached the UI activation at ~22:23:04.696 (**~1.04 s**). Do **not** pair it with the different UI execution at 22:23:09; those are separate launches.

Existing `MainWindow` XAML/constructor/activation/warm-up measurements are healthy (the 386 activation measured ~0.55 s after bootstrap). The purpose here is to locate **which pre-connection stages** are slow when the UI starts cold; no further MainWindow/page warm-up optimization belongs in this PR.

## 4. Change A — Overlay cold Ready timeout (behavioral)

File: `src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs`

Current:

~~~csharp
private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(5);
private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);
~~~

**Required:**

~~~csharp
// Allow the first unpackaged WinUI 3 process startup to finish before declaring IPC Ready failure.
private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(12);
private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);
~~~

Keep the existing `StartCoreAsync` sequence and logic:

~~~csharp
if (!await server.WaitForReadyAsync(ReadyTimeout, cancellationToken).ConfigureAwait(false))
{
    await StopCurrentAsync().ConfigureAwait(false);
    return false;
}
~~~

Mandatory invariants:

- Exactly the existing Runtime-owned Overlay process/server pair.
- Existing `Ready` state proof and transport handshake; no early/optimistic success.
- Existing cancellation, cleanup, bounded process retirement, and ability to retry on a subsequent explicit Overlay request.
- Existing `ShutdownTimeout` (**3 s**), pipe client `ConnectAsync(5000)` (**5 s**), server command acknowledgement timeout (**5 s**), and non-overlay timeouts remain **unchanged**.
- The warm-up stays optional after initial controller startup; do not delay VIIPER, physical PID1902 admission, first virtual controller presentation, or any fail-close path.
- No extra retry, no new lock/gate, no launch concurrency/state machine, no hidden polling, and no speculative fix for theoretical races.

**Known trade-off:** an explicit Overlay request that arrives while the existing cold warm-up is still pending can wait longer behind the existing transition gate if the child genuinely hangs. This is acceptable for this targeted 12-second change; do not mask it with new coordination logic.

## 5. Change B — Main UI cold-start diagnostic logs (NO behavioral change)

Goal: provide enough **existing-file, structured logs** to distinguish:

~~~text
Runtime UI open request
  -> optional Runtime surface/prerequisite coordination
  -> Process.Start entered/returned
  -> UI managed Program.Main entered
  -> log retention / single-instance gate / COM setup
  -> WinUI Application.Start / App construction / OnLaunched
  -> UI frontend client connection and handshake
  -> bootstrap request/response
  -> MainWindow initialization and activation
~~~

### 5.1 UI process entry and WinUI initialization milestones

File: `src/SteamInputAddonforClaw.UI/Program.cs`

Use a single local `Stopwatch` or `Stopwatch.GetTimestamp()` captured at the **very beginning of `Main`**, before `UiLog.ConfigureDirectory`. Log only after a usable UI log directory is available. Keep the existing single-instance and WinUI entry behavior identical.

Add a small number of existing-`UiLog` startup milestone entries:

1. `UI managed entry` immediately after `UiLog.ConfigureDirectory(args)`; include `PID` and process-entry elapsed time.
2. `Log retention completed`; include elapsed time for `AddonLogRetention.PruneDirectory(...)`.
3. `Single-instance gate checked`; include `IsPrimaryInstance` and elapsed time. Preserve the existing secondary-instance activation/return path without additional work.
4. `COM wrapper initialization completed` and/or `Application.Start beginning`; include elapsed since managed entry.
5. Within the existing `Application.Start` callback (before constructing `App`), `Application.Start callback entered`; include elapsed since managed entry.

A **minimal illustrative pattern**; adapt to exact existing naming/locals:

~~~csharp
var managedEntry = Stopwatch.GetTimestamp();
UiLog.ConfigureDirectory(args);
UiLog.Info("Startup", "UI managed entry.",
    ("PID", Environment.ProcessId),
    ("ElapsedMs", Stopwatch.GetElapsedTime(managedEntry).TotalMilliseconds));

var retentionStart = Stopwatch.GetTimestamp();
AddonLogRetention.PruneDirectory(UiLog.DirectoryPath);
UiLog.Info("Startup", "UI log retention completed.",
    ("DurationMs", Stopwatch.GetElapsedTime(retentionStart).TotalMilliseconds));

// Keep the actual gate and Application.Start behavior exactly as before.
// Log primary/secondary outcome, WinRT initialization, and App callback timing
// around (never instead of) the existing operations.
~~~

The first managed log cannot measure OS scheduling/.NET startup **before** `Program.Main`. Correlate its wall-clock timestamp and PID with Runtime `Process.Start` logs to bound that earlier interval; never label it as a measured WinUI-only duration.

Avoid log-heavy per-page tracing or frame/render instrumentation; `MainWindow.xaml.cs` already records XAML, constructor, activation, and deferred warm-up measurements.

### 5.2 App/IPC/bootstrap timings

File: `src/SteamInputAddonforClaw.UI/App.xaml.cs`

Add an initial `OnLaunched entered` milestone and simple elapsed times around the **existing** `StartFrontendAsync` chain. Use the same existing UI logging mechanism (this UI already aliases `AppLog` to its own `UiLog`).

Required independent durations or equivalent event timestamps:

- `StartFrontendAsync entered` / client creation complete.
- `_frontendClient.ConnectAsync()`: **frontend IPC/handshake duration**.
- `_frontendClient.GetBootstrapAsync()`: **bootstrap round-trip duration**.
- `new MainWindow(...)`: retain existing detailed `MainWindow` constructor/XAML logs, optionally attach total duration to the existing `MainWindow initialized` log.
- `ActivateOrDeferOnUiThread()` / `Frontend activated`: startup elapsed and existing activation outcome.

Prefer enriching the current `Frontend connected.`, `Bootstrap acquired.`, `MainWindow initialized.`, `Frontend activated.` events with numeric `DurationMs` / `ElapsedMs` fields instead of duplicating each event at another severity.

The existing `stage`-based exception handling must remain intact, and any added diagnostic code must not make UI startup fail when logging is unavailable. Use ordinary local stopwatch measurements; do **not** add a telemetry subsystem, global timing state, injected clock service, new API, or cross-process clock sync mechanism. Make sure timestamps can be correlated using the existing ISO-offset log times and process IDs.

### 5.3 Small Runtime launcher log refinement (diagnostics only)

File: `src/SteamInputAddonforClaw/Lifecycle/FrontendProcessLauncher.cs`

Retain the current `Frontend open requested`, `Frontend process launch attempted`, and `Frontend process launch succeeded/failed` events.

At the narrow existing `_startProcess(startInfo)` call, capture **Process.Start call duration** and (on success) child `PID`. Include `Reason` in the success event as already done. Preserve null-return, exception, retry-on-next-user-request, and lack-of-process-ownership behavior. Do **not** conflate the time spent awaiting Runtime prerequisites/surface coordination with the measured `Process.Start` call duration.

An implementation outline:

~~~csharp
var launchStart = Stopwatch.GetTimestamp();
var process = _startProcess(startInfo);
// Existing null-return handling stays intact.
AppLog.Info("Frontend", "Frontend process launch succeeded.",
    ("Reason", reason),
    ("PID", process.Id),
    ("StartCallElapsedMs", Stopwatch.GetElapsedTime(launchStart).TotalMilliseconds));
~~~

Keep the actual existing exception/null handling intact; the snippet shows only the measurement placement. No prelaunch `WaitForInputIdle`, no synchronous `WaitForExit`, no automatic retry and no change in requested elevation/token or launch flags.

There is **no need to change** `NamedPipeAddonFrontendClient` or `NamedPipeAddonFrontendServer` protocols just to add the timings.

### 5.4 Logging quality

- Prefer a handful of **INFO** startup milestones, ensuring they are present in normal captured `ui-<PID>.log` files even without an explicitly enabled Runtime debug level.
- Use numeric milliseconds consistently (prefer `ElapsedMs` for a single starting point; `DurationMs` or `StartCallElapsedMs` for a discrete step).
- Preserve existing log directory selection, file name, retention policy, and exception-swallowing logging behavior.
- Log no user SID, personal paths beyond already-present diagnostics, secrets, controller IDs, or large serialized snapshots.
- Keep existing activation/close behavior, dispatcher affinity, initial Device page, lazy/deferred pages, and installed-game catalog scanning unchanged.
- Do not incorrectly infer that `Process.Start` returning means the window is visible.

## 6. Out of scope / regression boundaries

Absolutely do not modify:

- Full1902 PID1901/PID1902 transition/restore, physical DirectInput, HidHide, VIIPER attach/detach, publisher, rumble, or recovery authority.
- Suspend/Hibernate/Resume, system shutdown or restart gates, fail-close policies.
- Win+G suppression, Center M, MSI quick-settings suppression, FSE/Steam routing.
- Main UI page materialization, XAML layout, eager/deferred tabs, profile/catalog refresh, bootstrap RPC contents, or startup policy.
- Overlay IPC messages/schema/version, initial tab-order handshake semantics, gesture/capture neutrality, visible-surface mutual exclusion, or Overlay settings.
- Background warm host, adaptive timeout, retry/watchdog, and new abstractions.

## 7. Local Codex validation — CI/automated only

1. Inspect current implementation/tests before modifying them. Use the smallest compatible code change; avoid a long-running 12-second real delay in unit tests.
2. Run relevant existing tests where supported, in particular:
   - `tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs` and Overlay related lifecycle tests;
   - `tests/SteamInputAddonforClaw.Tests/FrontendProcessLauncherTests.cs`;
   - `tests/SteamInputAddonforClaw.UiTests/UiLogTests.cs` and related UI architecture tests.
3. Build the Runtime and WinUI UI projects using the repository's supported build/test commands. Report actual PASS/FAIL and any environment-specific limitation truthfully.
4. Keep existing tests for Ready handshake, failure cleanup, subsequent explicit retry, secondary UI activation, and Runtime survivability. Add only narrowly scoped automated assertions if there is an actual regression gap; do not add timer-based integration tests that sleep 12 seconds just to verify a literal constant.
5. Ensure diagnostic logs compile and use the real `UiLog`/existing aliases. No unhandled logging exceptions, UI-thread blocking waits, or changes to failure behavior.
6. Summarize changed files, test results, and log milestone names in the PR body.

**Physical handheld tests are exclusively the user's post-merge role.** Do not require the local Codex to boot/reboot the MSI Claw, open the Overlay physically, collect fresh device logs, test Sleep/Resume, or provide on-device proof before PR creation/approval/merge.

## 8. Acceptance criteria

### Overlay fix
- The only deliberate behavioral change is `OverlayProcessController.ReadyTimeout = 12 s` (previously 5 s).
- Overlay remains hidden until the existing pipe Ready handshake; normal warm-start confirmation still derives from the real protocol.
- Genuine failure/timeout retains existing bounded cleanup and does not become a false success.
- All other IPC command and shutdown timeout constants are unchanged.

### Main UI instrumentation
- First UI managed entry is visible **before** the older `Frontend connected` log in a fresh `ui-<PID>.log`.
- Logs let an investigator distinguish managed-entry/log-prune/single-instance/WinUI callback timing from frontend IPC handshake, bootstrap, MainWindow initialization and activation.
- Runtime Process.Start call duration and child PID are measurable/correlatable.
- Existing warm Main UI path remains functionally unchanged; no startup optimization is attempted in this PR.
- Existing Main UI/Overlay cooperation and standalone Full1902 lifecycle behavior remain unchanged.

## 9. Post-merge user-owned diagnostic follow-up (informational only)

After merging, the user may optionally capture a new `Addon/Log/... ` run with:

- first desktop/Tray UI open after Windows logon;
- a second open after closing the UI (Runtime remains alive);
- one cold Overlay prewarm and a later user-triggered Show/Hide.

Compare Runtime `Frontend open requested` / `Frontend process launch succeeded` with the new UI `UI managed entry`, `Application.Start callback entered`, `OnLaunched`, `Frontend connected`, `Bootstrap acquired`, and `MainWindow activated` milestones. That evidence determines any **separate follow-up UI performance PR**. Do not assert a startup-time improvement from instrumentation alone.

---
**Implementation policy:** one PR, one literal Overlay Ready timeout adjustment, and bounded startup diagnostic logs only. Prefer a simple change that answers a real observed failure over speculative concurrency defenses.
