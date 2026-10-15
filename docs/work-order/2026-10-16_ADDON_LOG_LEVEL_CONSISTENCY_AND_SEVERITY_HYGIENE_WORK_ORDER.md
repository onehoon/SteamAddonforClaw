# Work Order — Addon-Owned Log Level Consistency and Production Severity Hygiene

> **Repository:** `onehoon/SteamAddonforClaw`  
> **Target branch:** `main`  
> **Reviewed baseline:** `a9b6962946fa9c613f08dd4dd8c909feb8eb479c` (2026-10-15 UTC)  
> **Date:** 2026-10-16  
> **Implementation owner:** Local Codex  
> **Scope:** Addon-owned C# Runtime, Main UI, WinUI Overlay, and the short-lived Addon FSE Home diagnostic sink only  
> **Excluded:** The independent VIIPER native Go file logger, `libVIIPER.log`; CTW; unrelated ClawHUD/Steam logs

## 1. Goal and confirmed problems

Make the existing Settings > Logging `Off / Info / Debug` choice effective throughout **Addon-owned** output, including process startup and changes while Main UI / warm hidden Overlay are running. Reclassify specific oververbose INFO diagnostics to DEBUG **without concealing real failure, recovery, controller ownership, routing, or teardown events**.

Confirmed against current source:

- Runtime `Diagnostics/AppLog.cs` already implements the correct threshold, with `Off` above `Fatal`, and `Program.cs` reads the persisted level before emitting startup entries. `StartupSettingsCoordinator.ChangeLogLevel` persists and applies it live. **Preserve this owner/contract.**
- Main UI `Diagnostics/UiLog.cs` and Overlay `Diagnostics/OverlayLog.cs` always append every level, including DEBUG, regardless of the preference.
- `FrontendProcessLauncher` and `OverlayProcessController` launch children with `--log-directory` only, not with a logging preference.
- Main UI `SettingsPage.LogLevelComboBox_SelectionChanged` receives the authoritative result from Runtime, but does not update its own `UiLog`.
- `InProcessAddonFrontendControl.SetLogLevelAsync` already raises `StateInvalidated` after changing the Runtime setting. `AddonProcessHost.OnFrontendStateInvalidatedForOverlay` currently returns when the Overlay is not visible/captured. `OverlayProcessController.RefreshFrontendSettingsAsync` and `NamedPipeOverlayServer.SendFrontendSettingsStateAsync` also gate settings delivery on visible state. Therefore passing a level only at Overlay creation does **not** fix subsequent OFF changes while the Overlay remains warm and hidden.
- `SteamInputAddonforClaw.FseHome/FseHomeDiagnostics` is another **Addon-owned**, short-lived file sink: it unconditionally truncates/appends `fse-home-last.log` during FSE handoff. It can run before the regular Runtime, so do not assume it can receive live Runtime IPC or launch arguments.

Evidence from Google Drive `Addon/Log/AV2M/1015-1`: Runtime P16052 explicitly recorded `Settings loaded. LogLevel=Debug`; that session's Runtime DEBUG lines **do not prove** Runtime threshold failure. UI and Overlay were still independent unfiltered sinks. Separate `Addon/Log/AV2M/1015` Runtime P9716 contained 187 repeated INFO `CenterM.Startup/Startup state captured.` entries out of 566 INFO lines.

**Do not claim that an old log file remaining on disk violates OFF.** The contract is no **new entry** after OFF is effective for a component. Log retention and previously created files continue to exist.

## 2. Governing architecture and relevant sources

Read these documents before implementation; prefer the latest policy where historical work orders conflict:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
- `docs/work-order/FRESH_INSTALL_SETTINGS_LOGGING_AND_CONTROLLER_PREREQUISITE_REPAIR_WORK_ORDER_2026-10-03.md`
- `docs/work-order/FULL1902_LIFECYCLE_LOG_HYGIENE_WORK_ORDER_2026-10-05.md`

Process contract: one Windows user, one interactive session. Runtime is the elevated settings and controller owner; Main UI and Overlay are Runtime-launched separate WinUI processes. All log preference mutations still go through `StartupSettingsCoordinator` / `IAddonFrontendControl`. This task must not add a second settings authority.

## 3. Locked behavior

| User choice | Addon Runtime | Main UI | Overlay | FSE Home |
|---|---|---|---|---|
| Off | no new log entries (including Fatal) | none | none | no new entries; do not truncate/create a file |
| Info | INFO, WARN, ERROR, FATAL | INFO, WARN, ERROR | INFO, WARN, ERROR | INFO and actual warnings/errors |
| Debug | all levels | DEBUG and above | DEBUG and above | debug diagnostics and higher |

- **First install:** missing `settings.json` resolves to INFO, consistent with `LogLevelBootstrap.Read` and existing first-install repair.
- **Existing malformed/unreadable/unknown preference:** fail closed to OFF, also consistent with the Runtime bootstrap. Do not silently turn logging on.
- **Changing preference in Main UI:** persist through Runtime first. On confirmed success, update UiLog to the returned authoritative `FrontendSettingsSnapshot.LogLevel`; on failed mutation, keep the previous effective level and reconcile from an authoritative bootstrap if available.
- **Warm hidden Overlay:** a confirmed level change must propagate over the **existing** Runtime–Overlay pipe without requiring the Overlay to become visible. Next Show/Hide must use the updated level. Do not start a new process, introduce polling, or piggyback on a user gesture.
- **Startup:** apply the launch preference before the first UI/Overlay log write. Never log a startup INFO or DEBUG line first and only later discover OFF.
- **No requirement for path/file deletion on OFF.** Existing logs remain available for diagnosis and obey current retention.
- **Log failures stay best effort;** never block application functionality because a log cannot be opened.
- No new hard dependency from Runtime startup to UI/Overlay readiness, and no changes to physical/virtual controller authority.

## 4. Runtime-to-child bootstrap: existing arguments, no duplicate settings store

### 4.1 Shared launch-argument contract

Extend `src/SteamInputAddonforClaw.FrontendTransport/FrontendLaunchArguments.cs` with a small public `--log-level` option and a safe parser for `Off/Info/Debug`, using the already referenced `SteamInputAddonforClaw.Contracts.Frontend.FrontendLogLevel`.

Example shape (adapt naming to project style):

```csharp
public const string LogLevelOption = "--log-level";

public static FrontendLogLevel ResolveLogLevel(string[] args)
{
    for (var i = 0; i + 1 < args.Length; i++)
    {
        if (!string.Equals(args[i], LogLevelOption, StringComparison.OrdinalIgnoreCase))
            continue;
        return args[i + 1].ToLowerInvariant() switch
        {
            "debug" => FrontendLogLevel.Debug,
            "info" => FrontendLogLevel.Info,
            _ => FrontendLogLevel.Off
        };
    }
    return FrontendLogLevel.Off; // unmanaged direct UI/Overlay launch must not log unexpectedly
}
```

Do not disturb `ResolveLogDirectory`. The Runtime must supply the **current effective** level on every child launch, including when the preference was changed since its own startup. Avoid caching a constructor-time snapshot in either launcher.

### 4.2 Child launchers

Update:

- `src/SteamInputAddonforClaw/Lifecycle/FrontendProcessLauncher.cs`: retain `ProcessStartInfo.ArgumentList` and existing directory path, append `--log-level <effective-level>`. Read the effective Runtime level at actual `LaunchLocked`, not at constructor time.
- `src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs`: use `ProcessStartInfo.ArgumentList` (instead of concatenated `Arguments` string) for both the existing `--log-directory` and the new `--log-level`, with proper handling of paths containing spaces. Read the current effective Runtime level at the launch moment.

Use Runtime `AppLog.MinimumLevelOverride` / its authoritative setting mapped to `FrontendLogLevel` without adding a second persisted preference. A single tiny local mapping is sufficient. A test-injectable provider is acceptable only if needed for launcher tests; no generalized argument builder, logging manager, daemon, or event bus.

### 4.3 UI / Overlay log sinks

Update `UiLog`, `OverlayLog`, and their `Program.Main` entrypoints to establish the threshold before the first log call. Reuse the shared parsed enum; keep each process's existing formatting, file name, best-effort exception containment, and log retention logic.

An acceptable low-complexity filter for these three user-facing choices is:

```csharp
private static int _level = (int)FrontendLogLevel.Off;
internal static void SetMinimumLevel(FrontendLogLevel level) =>
    Volatile.Write(ref _level, (int)level);

private static bool ShouldWrite(string severity)
{
    var selected = (FrontendLogLevel)Volatile.Read(ref _level);
    return selected == FrontendLogLevel.Debug ||
        (selected == FrontendLogLevel.Info && severity != "DEBUG");
}
// Call BEFORE formatting, directory creation, or File.AppendAllText.
// The exact implementation may instead use a small shared helper in FrontendTransport.
```

Keep `Warn`/`Error` visible at INFO and absent at OFF. Do not confuse enum ordinal order (`FrontendLogLevel.Off=0`) with Runtime `AppLogLevel` threshold order (`Off` is highest).

## 5. Live setting changes: reuse existing control/pipe

### 5.1 Main UI

In `src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs`:

- After `await _frontend.SetLogLevelAsync(level)`, call `UiLog.SetMinimumLevel(result.LogLevel)` and update UI selection to `result.LogLevel`; do not apply a speculative request before Runtime acknowledges it.
- In `RefreshLoggingStateAsync`, also reconcile UiLog from `bootstrap.Settings.LogLevel` after a successful authoritative refresh.
- Reconcile from `bootstrap.Settings.LogLevel` at Main UI bootstrap/initialization (a lightweight safeguard after the launch argument; it is **not** a new authority).
- Preserve current behavior if frontend transport fails; no new retries/transport state machine.

### 5.2 Warm hidden Overlay — important lifecycle regression trap

Use the **existing** typed `FrontendSettingsState` on the Overlay pipe. No new wire message, pipe, protocol version, or settings watcher.

Existing relevant links:

```text
InProcessAddonFrontendControl.SetLogLevelAsync
  -> StateInvalidated
  -> AddonProcessHost.OnFrontendStateInvalidatedForOverlay
  -> OverlayProcessController.RefreshFrontendSettingsAsync
  -> NamedPipeOverlayServer.SendFrontendSettingsStateAsync
  -> NamedPipeOverlayClient.FrontendSettingsStateReceived
  -> Overlay.App.OnFrontendSettingsStateReceived
```

The current visibility checks prevent this path from working while the Overlay is warm/hidden. Make the **minimal narrowly scoped exception**:

1. In `AddonProcessHost.OnFrontendStateInvalidatedForOverlay`, schedule **only** `RefreshFrontendSettingsAsync` before the `_overlayCaptureActive/IsVisible` checks (after the existing shutdown guard). Keep all other Shortcut, M1/M2, Quick Settings, ClawHUD, and controller vibration state publishes visible/captured as they are.
2. In `OverlayProcessController.RefreshFrontendSettingsAsync`, allow the read-only `FrontendSettingsState` publish for a connected Ready Overlay even while Hidden. Do not relax mutation, Show/Hide, dismissal, navigation, device/profile snapshot, or capture admission guards.
3. In `NamedPipeOverlayServer.SendFrontendSettingsStateAsync`, allow this **one** typed state message while connected and Ready, regardless of Visible/Hidden, retaining the same `_writeGate`, protocol validation and best-effort error handling. Every other state publication's gating remains unchanged.
4. In `Overlay.App.OnFrontendSettingsStateReceived`, update `OverlayLog.SetMinimumLevel(settings.LogLevel)` immediately on a valid/available settings snapshot **before** posting UI work to DispatcherQueue. Preserve current window state update via DispatcherQueue. An unavailable/fallback snapshot must not become a new logging authority.
5. The initial `--log-level` covers logging before the first pipe snapshot, so a warm child never emits OFF-forbidden startup records. Later authoritative snapshots reconcile it without additional polling.

**Why this is necessary:** The Overlay starts warm and hidden in the supported normal lifecycle. A user can change Debug -> Off in Main UI and then open the Overlay; leaving the existing Visible gate would make it append stale logs during hidden time and at the subsequent Show, a straightforward real-world bug.

The current `StateInvalidated` event is already raised by the Runtime's `SetLogLevelAsync`. No new ownership/subscription mechanism is required.

## 6. Addon FSE Home one-shot sink

`src/SteamInputAddonforClaw.FseHome/Program.cs` defines `FseHomeDiagnostics`, which always truncates `fse-home-last.log` and then appends records. This sink is owned by the Addon repository, **not** the excluded VIIPER native library.

FSE Home may start before Runtime during Windows FSE boot handoff. Keep the FSE launch mechanism unchanged. At the beginning of the short-lived FSE Home process, perform one read-only, best-effort parse of the **same canonical** `%LocalAppData%/SteamInputAddonforClaw-Data/settings.json` preference (no new file, IPC dependency, or settings owner). Use exactly the Runtime bootstrap fallback policy: missing settings -> Info; malformed/unreadable/unknown level -> Off. Do not introduce a watcher or a live-update requirement for this one-shot process.

- OFF: do not create, truncate, or append `fse-home-last.log`.
- INFO: retain meaningful start/handoff completion/failure logs; downgrade the intermediate HWND, foreground, probe and elapsed-time details to DEBUG.
- DEBUG: include the existing intermediate diagnostics.
- If making FSE Home's plain-text record format severity-tagged, preserve the existing `ResolveLogPath`, file name, truncation-on-enabled-start semantics, and diagnostic best-effort behavior.
- Do **not** change BPM launch, 60-second readiness timeout, window handoff, foreground takeover, or return codes.

This is deliberately bounded. Do not move FSE Home into Runtime just to share its logger.

## 7. INFO -> DEBUG severity adjustments (concrete code sites)

Reclassify **diagnostics only**. Keep actual controller lifecycle and user-facing mutation outcomes at INFO/WARN/ERROR.

| File / current message | Current | Required | Rationale |
|---|---|---|---|
| `CenterMStartup/CenterMStartupControl.cs` — `Startup state captured.` in `Capture()` | INFO | DEBUG | Repeated read-only snapshot; observed 187 times in one session |
| `VirtualOutput/Viiper/CanonicalSteamDeckInputPublisher.cs` — `Canonical Steam Deck publisher heartbeat` | INFO | DEBUG | One-second cadence and timing measurements |
| `Diagnostics/DiagnosticSession.cs` — `Physical D-pad state changed` | INFO | DEBUG | Legacy per-input diagnostic, not a production state transition |
| `Power/PowerTransitionCoordinator.cs` — raw `Power notification observed.` | INFO | DEBUG | Notification reception details; **keep completed suspend/resume and failure outcomes INFO/WARN/ERROR** |
| `SteamInputAddonforClaw.Overlay/OverlayWindow.Presentation.cs` — normal `Show/Hide animation started/completed`, `Overlay window configured`, `Overlay surface bounds snapshot` | INFO | DEBUG | UI geometry and animation timing |
| `SteamInputAddonforClaw.Overlay/App.xaml.cs` — `Authoritative tab-order state received.`, routine hidden window preparation | INFO | DEBUG | Routine UI projection/setup diagnostics |
| `SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs` — `Authoritative Overlay tab order applied.` | INFO | DEBUG | Routine snapshot projection, repeated on Show |
| `SteamInputAddonforClaw.UI/MainWindow.xaml.cs` — detailed deferred page warm-up start/step/completion with durations | INFO | DEBUG | Startup performance diagnostics (keep genuine failures) |

Do **not** mechanically downgrade every INFO from these files. Maintain substantive events such as Overlay process start, Runtime session and controller ownership, actual capture/Show/Hide lifecycle outcomes, FSE/BPM handoff result, user-driven settings changes, explicit startup result, and failure consequences.

**Steam Deck publisher performance hot path: mandatory companion change.**

In `CanonicalSteamDeckInputPublisher.WorkerLoop` and `PublishCurrentStateOnce`, the existing `diagnosticsEnabled = AppLog.IsEnabled(AppLogLevel.Info)` gates the heartbeat's timing counters, `RecordWaitBlocked/TimerWake/WakeLateness/PublishWork`, and `EmitHeartbeatIfDue`. Change **both** gates to `AppLogLevel.Debug` at the same time as changing `AppLog.Info("SteamOutput", "Canonical Steam Deck publisher heartbeat", ...)` to Debug. Keep the independent `pulseDiagnosticsEnabled`, fault detection, `_sink.SetState`, publisher pacing/deadline advancement, and `ReportFault` unchanged.

Sample:

```csharp
var diagnosticsEnabled = AppLog.IsEnabled(AppLogLevel.Debug);
// ... preserve existing functional publish/stop/fail-close logic ...
if (diagnosticsEnabled) EmitHeartbeatIfDue();

// In EmitHeartbeatIfDue:
AppLog.Debug("SteamOutput", "Canonical Steam Deck publisher heartbeat", ...);
```

**D-pad companion change:** In `MsiClawInputSource.cs`, change both `AppLog.IsEnabled(AppLogLevel.Info)` guards around `ControllerStateDiagnostics.LogDPadTransitionIfChanged` to `AppLogLevel.Debug`. Merely changing the final write level leaves unnecessary per-poll diagnostic work at INFO.

**Optional snapshot warning:** `OverlayWindow.Presentation.LogSurfaceBounds` explicitly describes missing XAML bounds as non-fatal diagnostic-only behavior. If it is confirmed to be an expected absence (e.g. early hidden preparation), downgrade that specific `WARN` to DEBUG. Do not downgrade animation fallback or errors where visible UI behavior changed.

**Re-review remaining touched INFO lines for equivalent read-only, per-poll, or timing-only diagnostics**, but require a concrete reason to lower severity. Preserve existing lifecycle log hygiene semantics from the October 5 work order.

## 8. Deliberate exclusions / non-goals

- **Do not modify `onehoon/VIIPER`, `libVIIPER.dll`, or `libVIIPER.log`.** It is a native Go-owned file sink fixed to DEBUG in its own fork. Its logs may continue when Addon Logging = Off; clearly document that limitation until the separate VIIPER PR is implemented. Do not attempt to suppress them by dropping the VIIPER callback or abandoning VIIPER initialization.
- Do not modify the separate ClawHUD logger, Windows Event Logs, Steam logs, or third-party libraries.
- Do not change CTW-related behavior or integrate CTW.
- Do not alter routing/presentation, PID1901/PID1902 restoration, HidHide policy, VIIPER native ownership/teardown, Sleep/Hibernate/Resume, power gating, WING, process elevation, FSE Home behavior, or UI activation.
- No new settings file, persistent state, service, file watcher, logger abstraction hierarchy, broad Serilog/NLog migration, timer, reconcile loop, lock/epoch/state machine, or cross-process all-or-nothing acknowledgement.
- No blocking real-device validation requirement. Local Codex owns code, builds, and automated tests; post-merge real-device verification belongs to the user.
- Do not count entries already in flight before an acknowledged remote preference publish as a production race requiring synchronization. Once preference propagation has settled, the logger must honor it.
- Do not silence genuine WARN/ERROR paths to achieve an artificially small log.

## 9. Automated code/test acceptance (no hardware prerequisite)

Add/adjust focused tests:

1. `UiLogTests` and `OverlayLogTests`: Off suppresses DEBUG, INFO, WARN, ERROR and does **not create** a log file; Info emits INFO/WARN/ERROR but no DEBUG; Debug emits all. Reset static test preferences and temporary directories; prevent test-order contamination.
2. `FrontendLaunchArguments` parser: exact legal values, case normalization, missing value, unsupported value, malformed/incomplete arg, log directory containing spaces, no unintended paths.
3. `FrontendProcessLauncherTests`: update existing `Passes_canonical_log_directory_as_one_argument` expectation to include `--log-level`, verify launch uses the **current** effective level (not stale level from launcher construction), and retains argument quoting via `ArgumentList`.
4. Overlay launcher/process tests: capture `ProcessStartInfo`; assert arguments preserve both directory and level, including spaces.
5. Overlay pipe/host tests: `FrontendSettingsState` may reach a connected Ready **hidden** client; other state/mutation/navigation/Show gates remain unchanged. Confirm `StateInvalidated` causes that narrow state update while hidden and does not publish QuickSettings/Shortcuts/etc.
6. Main UI setting tests: successful `SetLogLevelAsync` applies the authoritative returned preference to UiLog; failed mutation retains previous preference, successful bootstrap refresh corrects it.
7. Runtime `AppLogTests` / `LogLevelBootstrapTests`: retain all existing Off, Info, Debug, malformed settings, and fresh-install semantics unchanged.
8. Publisher diagnostics tests: assert heartbeat is gated to DEBUG, and normal INFO publish path no longer performs timing diagnostics; real fault/reporting and publication stay intact.
9. D-pad diagnostics tests: transitions are DEBUG only; no changes to actual input mapping/publishing.
10. FSE Home diagnostics tests: Off does not truncate/create/write; missing settings defaults Info; malformed preference Off; Debug includes detailed records. Preserve `FseHomeHandoffTests`' path/handoff contract.

Use the repository's existing .NET test projects, including `tests/SteamInputAddonforClaw.Tests` and `tests/SteamInputAddonforClaw.UiTests`. Provide build + automated test results in the PR. Tests must be deterministic and independent of MSI hardware, Steam BPM, USB/IP, or VIIPER native runtime.

## 10. Final checklist / acceptance

- [ ] A fresh-install INFO preference is applied from the first line in Runtime and both Runtime-launched WinUI children.
- [ ] Explicit OFF is silent in Runtime, Main UI, Overlay and FSE Home, without creating/truncating their log files; historical files need not be removed.
- [ ] With INFO, UI and Overlay DEBUG events no longer appear.
- [ ] With DEBUG, all pre-existing relevant diagnostic events remain available.
- [ ] Changing INFO/DEBUG/OFF through Main UI updates the running Main UI and **warm hidden Overlay** without a restart; the Overlay can subsequently Show/Hide without stale logging.
- [ ] All ordinary Overlay feature/capture admission checks remain unchanged; only the existing read-only frontend settings state is deliverable while hidden.
- [ ] The high-frequency Steam Deck publisher INFO diagnostic heartbeat and INFO-only expensive diagnostic sampling are removed.
- [ ] Repetitive Center M snapshot and D-pad transition records move to DEBUG.
- [ ] Startup, actual controller ownership changes, physical failures, unsafe routing/recovery, native teardown, and normal lifecycle completion remain accurately classed.
- [ ] No new logging authority, duplicated preference persistence, unrelated native Go changes or speculative race machinery.
- [ ] Compile and automated tests pass. Real hardware testing is **not** a local Codex acceptance/merge blocker.

## 11. Suggested PR scope

Implement this as **one bounded Addon-only PR** with small coherent commits if helpful:

1. Shared launch preference / C# sink filters and launcher tests;
2. Live UI/hidden Overlay settings propagation using existing state pipe;
3. Production severity cleanup, hot-path gating, FSE Home one-shot sink, and focused tests.

If the code diff becomes unexpectedly large, split by those commits/concerns, but do not ship a partial fix that leaves warm hidden Overlay unfiltered after an OFF change.

The separate VIIPER fork's `libVIIPER.log` level control remains a later, independent change.
