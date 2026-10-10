# Work Order — Full1902 Adaptive 125 Hz Idle / 250 Hz Active Controller Cadence

> Date: 2026-10-10
> Target: `onehoon/SteamAddonforClaw`, current `main`
> Scope: Standalone Full1902, one Windows interactive user/session
> PR size: One focused implementation PR
> Delivery: Local Codex implements and runs code/automated tests. Hardware smoke validation belongs to the user after merge and is **not** a PR blocker.

## 1. Decision and goal

Reduce idle Runtime CPU/timer wakeups without sacrificing the established 250 Hz gaming path.

Both *physical PID1902 DirectInput polling* and the *active virtual publisher* must use **125 Hz / 8 ms** when no game recognized by the existing Steam/XBOX observers is running and Steam Big Picture Mode (BPM) is inactive. They must return to **250 Hz / 4 ms** when any existing recognized Steam game, XBOX game, or BPM session is active.

The SteamDeck publisher **itself stays unconditionally 250 Hz**. As the existing presentation policy already selects SteamDeck for a Steam RunningAppID or BPM, this naturally implements the active policy without dynamically modifying the SteamDeck publisher.

There must be **no new game detector or process monitoring**. Reuse the existing Runtime-owned Steam and XBOX facts and callbacks only.

| Existing observations | Physical input | Active virtual presentation | Virtual publication |
| --- | --- | --- | --- |
| Steam RunningAppID = 0; BPM inactive; no recognized XBOX game; existing detection healthy | 8 ms / 125 Hz | Xbox360 | 8 ms / 125 Hz |
| Recognized XBOX game active; Steam/BPM inactive | 4 ms / 250 Hz | Xbox360 | 4 ms / 250 Hz |
| Steam RunningAppID != 0 | 4 ms / 250 Hz | SteamDeck | **fixed** 4 ms / 250 Hz |
| BPM active, even without a game | 4 ms / 250 Hz | SteamDeck | **fixed** 4 ms / 250 Hz |
| Last recognized game exits; Steam/BPM inactive | 8 ms / 125 Hz | Xbox360 | 8 ms / 125 Hz |
| Observation cannot establish a trustworthy idle condition | 4 ms / 250 Hz, conservatively | Existing Steam/BPM policy | 4 ms / 250 Hz |

BPM alone **must never** select 125 Hz. This explicitly supersedes the suggestion of running SteamDeck at 125 Hz with BPM open.

The intent is CPU/power efficiency, **not** a claim that the ~100 MB Runtime working set will fall.

## 2. Source baseline and binding contracts reviewed

Read the current versions again at implementation time, particularly if main moved after this order.

Authority, lifecycle and historical work:

- `docs/Full 1902 Implementation/README.md` — current document precedence.
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md` — standalone PID1902 ownership, one attached VIIPER device, fail-close, recovery.
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md` — reboot-bound authority, PnP, suspend/resume.
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md` — current Runtime privilege/lifecycle.
- `docs/work-order/FULL1902_END_TO_END_250HZ_CONTROLLER_CADENCE_WORK_ORDER.md` — validated physical ~250 Hz and independent high-resolution deadline scheduling.
- `docs/work-order/FULL1902_XBOX360_125HZ_PUBLISHER_CADENCE_WORK_ORDER.md` — **historical** fixed-125-Hz Xbox change; superseded by the full-250-Hz work and by this adaptive proposal. Do not mistake its old physical-125-Hz assumption for current implementation.
- `docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md` — XBOX detection is event-driven and must not control virtual-presentation identity.

Actual code inspected:

| File | Current fact / required change |
| --- | --- |
| `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs` | Already subscribes to Steam RunningAppID and XBOX ActiveGame changes, receives BPM callback, creates the physical source and presentation; add a **single host-owned current fast-cadence decision**, update it on existing events, pass read-only access to the two relevant workers |
| `src/SteamInputAddonforClaw/Steam/SteamSessionRuntime.cs` | `SteamPresentationSnapshot(RunningAppId, BigPictureActive)`; `WantsSteamDeck` is true if either is active; **preserve** presentation policy |
| `src/SteamInputAddonforClaw/Runtime/AddonRuntimeHost.cs` and `.../AddonRuntimeComposition.cs` | Existing Steam event forwarding and BPM callback wiring; preserve normal startup, refresh and event ownership |
| `src/SteamInputAddonforClaw/Xbox/Session/XboxGameSessionRuntime.cs` | `ActiveGame`, `ActiveGameChanged`, event-driven WinEvent observation and startup/resume reconciliation; no new game scanning |
| `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputSource.cs` | `PollInterval = 4 ms`; single physical worker with existing high-res one-shot timer and absolute-deadline math; session's `ProductionPeriodTicks` is currently fixed at start; add worker-local live-period selection |
| `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalXbox360InputPublisher.cs` | `ProductionPeriod = 4 ms`; dedicated publisher worker, high-res one-shot timer, `_periodTicks` and `_nextDeadlineTicks`; add worker-local live-period selection |
| `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs` | Constructs Xbox360 and SteamDeck publishers using existing factories; pass the **same read-only cadence signal** into the Xbox360 publisher, preserving test fakes |
| `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckInputPublisher.cs` | Keep at **fixed** 4 ms; no functional changes |
| `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputContracts.cs` | Preserve existing physical ownership/snapshot/StateChanged contracts; do not add a global controller cadence manager |

Note: the X360 publisher still has old Game Bar / temporary-routing XML remarks in some areas; the implementation is already the Full1902 always-selected Xbox360 presentation when Steam/BPM is inactive. Correct any *current cadence description* touched by this work; unrelated documentation cleanups are not required.

## 3. Exact cadence decision: reuse facts, not another detector

The existing authority must remain:

```text
AddonProcessHost receives existing observations:
  Steam actual RunningAppID changed
  Steam BPM state changed
  XBOX ActiveGameChanged
  startup/resume observation reconciliation

Effective high-rate requirement:
  Steam RunningAppID != 0
  OR BPM active
  OR XBOX ActiveGame != null
  OR existing observations not yet sufficiently ready to affirm "idle"
     -> 250 Hz

Otherwise
     -> 125 Hz
```

Keep **one** host-owned in-memory decision, initialized **fast (250 Hz)** until startup observations are ready. Example concept (names are illustrative):

```csharp
private int _require250Hz = 1;

private bool Requires250Hz() => Volatile.Read(ref _require250Hz) != 0;
```

Update once on the relevant *existing* callbacks and after startup/resume reconciliation. Only publish the new decision when it actually changes, with `Volatile.Write` / `Interlocked.Exchange` as appropriate. The high-frequency worker threads read that cached value; they must **not** call `CapturePresentationSnapshot()`, registry APIs, process enumerators, WinEvent APIs or profile resolution every tick.

- Steam RunningAppID: `AddonProcessHost.OnActualRunningAppIdChanged`, currently also requests presentation reconcile. Update the cadence decision promptly, without waiting for CPU Boost/TDP/profile work.
- BPM: `AddonProcessHost.OnBigPictureStateChanged` already receives the current event. Update the cadence decision even if no game is running; keep existing presentation reconcile.
- XBOX: `AddonProcessHost.OnActiveXboxGameChanged` already sees process-identity activation and exit. Update the cadence decision before unrelated profile mutation work; DO NOT tie it to foreground focus. A minimized/background running recognized XBOX game still requires 250 Hz.
- Startup: after the existing XBOX observer's initial reconciliation is complete and current Steam/BPM facts are available, compute the real initial rate. Do not permanently remain 250 Hz merely because no change event occurs after startup.
- Resume: use the **existing** Steam refresh and `XboxGameSessionRuntime.ReconcileAfterResumeAsync` path. Retain/use 250 Hz until the relevant post-resume facts are refreshed, then select the correct rate. Do not add a resume polling loop.

### 3.1 XBOX observation health is a real, narrow fallback

Current `XboxGameSessionRuntime.StartAsync` can log and swallow an observer startup failure; `ActiveGame == null` therefore does **not** prove idle when the observer failed to start. Its existing `_acceptWindowEvents` also becomes false on WinEvent source failure and while resume re-arms the watcher.

If the implementation needs an observer-ready fact, expose **only a small read-only readiness indication / notification from the existing XBOX observer**, based on its **existing** start, bounded initial/resume reconcile and source-failure lifecycle. Do not create a new monitoring process, timer, thread, health manager, periodic scan or game identity authority.

For example, a read-only `CanReliablyReportNoActiveGame` plus notification when that fact changes is acceptable **only if necessary** to prevent incorrectly selecting idle after ordinary observer failure. Mark startup and resume reconciliation failure as not ready; remain at 250 Hz until the existing observer successfully establishes an idle observation. Existing active-game results may still force 250 Hz regardless of readiness.

Do not conflate a recognized XBOX process losing focus with game exit. The observer already retains positive identity until its process generation exits.

The Steam RunningAppID reader currently maps a registry read exception to zero. This order does **not** authorize a redesign of Steam detection, nor does it guarantee discovery of games the existing detector cannot recognize. Reuse the Steam observer's current contract; preserve its warning logs.

### 3.2 Deliberate detection boundary

A non-Steam, non-XBOX desktop game not recognized by either existing detector **will remain at 125 Hz**, by the user's explicit no-new-monitoring policy. Do not introduce a generalized process, fullscreen, foreground, GPU or window-based game monitor just to cover it.

There is also no need for a UI preference, profile-persisted 125/250 option, or separate rate ownership per game.

## 4. Wiring: minimal changes to existing owners

Pass an inexpensive read-only provider from the existing host to:

1. the single production `MsiClawInputSource` created by `AddonProcessHost.CreatePhysicalOwnership`;
2. the X360 publisher created by `MsiClawAddonPresentation`.

A constructor argument such as `Func<bool> requires250Hz` is sufficient. Preserve existing source and publisher constructors and tests through an optional/default provider if appropriate; absent provider defaults to the current **250 Hz** contract.

Avoid expanding `IMsiClawPreparedInputSource` / `IAddonPresentationPublisher` merely to distribute cadence, introducing second state ownership, or asking the host to stop/restart a publisher to change timing.

Keep the existing injected `_xbox360PublisherFactory` fake seam intact; the presentation owner can capture the cadence provider once and pass it to its production default factory. Do not modify Deck publisher factory signatures for this feature.

No change to any physical controller mode, HidHide owner, VIIPER logical-device attachment, M1/M2 mapping, gyro owner, rumble, input normalization or OEM1/WING policy.

## 5. Production physical source: adaptive 4/8 ms on the existing worker

Target: `MsiClawInputSource`.

Keep `CadenceDiagnosticPollInterval = 1 ms` and all existing diagnostic observation/return contracts.

The current production worker:

- initializes and arms a one-shot timer before starting the dedicated polling thread;
- reads `ReadState()` and maps it to `LatestState`;
- waits through `WaitForNextPoll` on an absolute deadline;
- tracks `session.ProductionPeriodTicks` initialized to 4 ms.

Change only the **production** period decision. The initial arm should use the current desired period (4 or 8 ms). The worker should read the cached provider at a bounded, cheap point in the existing scheduling path. When the requested period differs from the worker-local last successfully applied period:

1. obtain the new 4/8 ms interval, avoiding any per-tick allocations/logs;
2. compute a sensible **new absolute deadline based on now + new interval** for this one rate change; do not try to phase-lock another thread;
3. arm the **same** `WindowsHighResolutionOneShotTimer`; only after successful arm commit the worker's applied period and emit a one-time Debug success line;
4. resume normal `CanonicalPublisherDeadlineMath.AdvanceDeadline` behavior on unchanged-period iterations.

Do not mutate session period concurrently from an event thread. Event code changes only the host cached decision; the physical worker owns its timer and its applied-period/deadline variables.

Preserve normal first-valid-state, invalid-state limit, `StateChanged` semantics, full physical cleanup, `TestCompleted`, `ResetLatestStateToNeutral`, StopAsync, PnP recovery, and existing `PollSchedulerFailed` classification.

### Diagnostic override

The existing explicit 1 ms cadence diagnostic uses its independent diagnostic collector/timer. While it is active, retain the existing diagnostic scheduling behavior; do not falsely log an 8ms/4ms production arm as applied while the 1ms diagnostic is controlling waits. On return to production, read the **latest** desired period, re-establish that absolute deadline, and log actual successful production application if changed. Update diagnostic metadata that currently assumes production period is always 4ms so it describes the applied/current period correctly.

Do not implement the production cadence with `Task.Delay`, `Thread.Sleep`, periodic managed timers, a spin loop or a new timer.

## 6. Xbox360 publication: adaptive 4/8 ms on its existing worker

Target: `CanonicalXbox360InputPublisher`.

- Initial production timer arm uses the current desired 4ms or 8ms period.
- On each timer wake, after publishing, read the cached provider and, only if the desired period differs, rebase **that publisher's** next deadline and arm its existing timer at the new interval.
- If unchanged, use existing `CanonicalPublisherDeadlineMath.AdvanceDeadline` exactly as before.
- A successful change must not stop the publishing thread, create a second timer/device, emit synthetic neutral frames, or lose a current button/stick state.
- Preserve `SetState` every active publisher tick, native rejection -> `ReportFault` -> existing presentation fail-close, stop/join invariants and zero output after proven stop.
- Preserve all manual-tick tests and the test-driven `IInputReportTickSource` path: they represent one state write per supplied tick, not a real wall-clock scheduler.

It is normal that physical and virtual 8ms clocks are *independent and not phase-locked*. No barriers, shared timer or wake-on-physical-state-change redesign.

## 7. SteamDeck publication and BPM are unchanged

Do not modify `CanonicalSteamDeckInputPublisher` functional code or period.

Existing presentation decision:

```csharp
SteamPresentationSnapshot.WantsSteamDeck =>
    RunningAppId != 0 || BigPictureActive;
```

Consequences:

- BPM only -> Deck at **250 Hz**, physical input at **250 Hz**.
- Steam game -> Deck at **250 Hz**, physical input at **250 Hz**.
- Xbox game -> X360 at **250 Hz**, physical input at **250 Hz**.
- Idle -> X360 at **125 Hz**, physical input at **125 Hz**.

During actual X360 <-> Deck switch, the existing presentation gate still owns stop/join, neutral, detach, attach and resume. This feature only changes cadence within live/eligible workers.

## 8. Required diagnostic logging: request, actual apply, failure

User requirement: make the results diagnosable in the **existing application Debug log**. Do not depend on external SDK/profiling tools for determining whether a transition was *accepted by the scheduler*.

Use existing `AppLog.Debug`/`Warn`/`Error` with compact structured properties. Pick stable, searchable event tokens such as these (exact words may be adjusted consistently):

### 8.1 Host decision changed (one line, no repeated identical decisions)

```text
[DEBUG] [ControllerCadence] CadenceDecisionChanged
  Trigger=XboxGameStarted
  PreviousHz=125 DesiredHz=250
  SteamRunningAppId=0 BPM=false XboxActive=true
```

Also log the first confirmed startup decision, including idle-at-start. Do not log for unchanged duplicate notifications.

### 8.2 Worker period actually armed (one line for initial arm and each distinct successful change)

```text
[DEBUG] [DirectInput] CadenceApplied
  Stage=Physical PreviousHz=125 AppliedHz=250 PeriodMs=4
  Result=Success

[DEBUG] [SteamOutput] CadenceApplied
  Stage=Xbox360Publisher PreviousHz=125 AppliedHz=250 PeriodMs=4
  Result=Success
```

**Success means that this worker successfully armed its next production timer deadline with the selected interval.** It does **not** assert measured USB/DirectInput report frequency or that both independent workers executed at the same instant.

If no Xbox360 publisher is attached (because SteamDeck is active), do **not** report a fictitious X360 publisher application; only physical application and the existing Deck 250Hz fact are relevant.

### 8.3 Timer/application failure (existing fail-close remains authoritative)

```text
[WARN] [DirectInput] CadenceApplyFailed
  Stage=Physical PreviousHz=125 DesiredHz=250
  Result=Failed Reason=TimerArmFailed

[ERROR] [SteamOutput] CadenceApplyFailed
  Stage=Xbox360Publisher PreviousHz=125 DesiredHz=250
  Result=Failed Reason=TimerArmFailed
```

Use the appropriate existing error levels and include exception details on the real failure path. **Never log a success before the arm succeeds.** Do not silently claim fallback-to-250 success after an arm failure. Invoke the **existing** physical-input scheduler fault/cleanup or publisher fault/fail-close as applicable; do not invent a retry/recovery state machine for rate changes.

Initial timer creation/arm/thread-start failure must also retain existing log/error semantics, and any cadence-specific result should reflect **failure**, not success.

### 8.4 Logging limits

- Debug logs: initial chosen cadence, actual initial arm, real 125->250 / 250->125 decisions and successful applications.
- Warn/Error: actual rejection, timer arm/scheduler failure, and a materially unavailable existing observer when it prevents safe idle classification.
- Do **not** log every 4ms/8ms tick, every `ReadState`, every `SetState`, repeated unchanged decisions or routine timer wakeup.
- Logging must not allocate format payloads every tick when Debug is disabled.
- Do not add a periodic timer just to emit cadence telemetry.
- Existing short cadence diagnostic remains usable to verify actual observed rates when necessary.

## 9. Startup, suspend/resume, recovery and teardown

Respect the existing Full1902 owner/gate/fail-close contracts.

### Startup

- Initially use 250 Hz until existing observations are ready.
- After initial Steam/BPM and XBOX observation reconciliation, switch to 125 Hz if truly idle.
- A live physical session and presentation started before observation readiness must converge to the chosen period **without restarting** either worker.
- When observations show an already-running game or BPM during boot, remain 250 Hz without an unnecessary idle round-trip.

### Sleep / Hibernate / Resume

- Keep the current suspend quiesce barrier and publisher stop/join/neutral behavior.
- On wake, the existing resumption and observer reconciliation update the host's desired cadence. Until ready, prefer 250 Hz.
- A successfully reused publisher or re-created publisher reads the latest decision. Never bypass the existing `ResetLatestStateToNeutral` step.
- No new epoch/barrier/lock solely to align the exact instant an event arrives relative to a timer tick.

### Physical loss / PnP re-enumeration / native operation failure

- Lost physical session still resets neutral, stops its worker, and goes through existing owned-session recovery.
- Recovered source starts at the current desired 4/8ms rate and current presentation remains selected solely by Steam/BPM.
- This feature must not change PID1901/PID1902 transitions, persistent HidHide isolation, VIIPER lifecycle or rumble endpoint teardown.
- If setting a rate fails in the timer scheduler, existing failure path remains fail-close. Never carry on with a falsely reported successful cadence.

### Restart / shutdown and presentation switching

- Preserve existing stop/join, resource disposal, feedback drain, VIIPER teardown and Center M Enable-and-Restart stock restoration.
- The host's cadence decision is process-memory-only. No new persisted authority/state/configuration file.
- No functional changes to overlay capture/pause/resume or M1/M2/physical front button mappings. Idle input edges are sampled at 8ms and remain available to the existing overlay `StateChanged` router.

## 10. Unit/integration test instructions (local Codex)

Focus on deterministic deadline and state tests, not wall-clock 125Hz/250Hz assertions.

### `MsiClawInputSourceTests.cs`

- Existing default/no-provider 4ms production test remains valid, or is explicitly updated only if a new default is intentionally provided by production composition.
- New initial idle 8ms initial-arm test; active 4ms initial-arm test.
- Fake mutable cached fast/idle provider, fake high-res timer and controlled monotonic clock: 8->4 and 4->8 produce the correct next deadline **without another DirectInput acquisition or worker**.
- Repeated identical input facts do not change applied period or produce repeated success logs.
- Re-arm failure while changing cadence yields the **existing** `PollSchedulerFailed` physical-session cleanup/fault policy and no false success.
- Existing 1ms diagnostic test still passes and returns to the correct latest 4/8ms production period.
- Existing first valid read, neutral/reset, StateChanged, fault/cleanup and StopAsync tests remain intact.

### `CanonicalXbox360InputPublisherTests.cs`

- Current 4ms default/deadline and AboveNormal/background worker tests retain coverage.
- New 8ms idle initial arm, live 8->4 and 4->8, unchanged-rate behavior.
- One active X360 publisher, one timer, no detach/recreate or unintended neutral frame from cadence change.
- A failed re-arm still invokes existing `ReportFault` and publisher fail-close; no false `CadenceApplied Result=Success`.
- Existing sink rejection, restart-after-stop, join timeout, test-tick and state mapping tests remain intact.

### Host / game observation wiring tests

- Existing Steam AppID event, existing BPM event, existing XBOX `ActiveGameChanged` cause the correct cached rate decisions, including **BPM only = 250Hz**.
- An XBOX process remains 250Hz after losing focus; drops to 125Hz only when its recognized active process exits (assuming no Steam/BPM).
- Startup with no game eventually enters 125Hz; startup with game/BPM remains 250Hz.
- Known XBOX observer startup/reconcile/source failure does not erroneously mark an unknown game state as confirmed idle. Recovery via existing start/resume reconcile may safely lower to 125Hz.
- Suspend/resume, physical loss/recovery and existing X360<->Deck presentation-selection behavior remain unchanged.
- Verify Debug log outcomes using existing logging/test seams where practical; no additional production logging sink or framework merely to test log text.

Run relevant tests plus normal project test/build/CI. Local Codex must **not** require handheld testing to close the coding task. The user validates the merged build on MSI Claw hardware.

## 11. Explicit non-goals / overengineering guard

Do **not** add any of the following:

- new Steam, XBOX, non-Steam or fullscreen game monitor; WMI polling or process enumeration loop;
- rate manager/service/provider framework or duplicate rate authority;
- second game/profile/session state machine;
- persisted period preference, configuration, UI or RPC contract;
- physical input -> publisher phase synchronization, cross-thread barrier, sequence epoch or new mutex;
- publisher/device recreation just to change frequency;
- new high-frequency diagnostics/heartbeat logging;
- edits to the SteamDeck 4ms scheduling path, gyro processing or native Steam output mapping;
- controller ownership or Center M/stock restoration changes;
- work to cover unsupported multi-session, Fast User Switching or RDP scenarios.

Prefer the smallest change built on the existing host and both worker-local timer/deadline implementations. Judge regressions against **realistic** supported hardware lifecycle paths, not hypothetical instruction-level interleavings.

## 12. Acceptance criteria

1. Verified idle with no recognized Steam/XBOX game and BPM off -> physical **8 ms** and X360 publisher **8 ms**.
2. Recognized XBOX game active -> physical **4 ms**, X360 publisher **4 ms**, regardless of window focus.
3. Steam game active or **BPM only** -> physical **4 ms**, SteamDeck publisher **unchanged 4 ms**.
4. End of game + BPM off -> both live eligible workers converge to **8 ms**, without worker/device recreation.
5. No new game polling/monitoring or controller rate-manager abstraction.
6. Initial, changed, and failed cadence operations are diagnosable from **bounded Debug + Warn/Error logs**; successful applied events occur only after actual timer arm succeeds.
7. Failed timer-arm path uses existing fail-close/cleanup; no silent nominal 250Hz fallback and no false success.
8. Existing sleep/resume, PnP loss/recovery, routing fail-close, HidHide/VIIPER, overlay, rumble and PID1901/1902 restoration contracts remain intact.
9. Code builds and deterministic automated tests/CI pass.
10. Handheld hardware validation is **post-merge user responsibility**, not an implementation or review blocker.

## 13. Practical user hardware validation (post-merge, informative only)

The user may later capture Debug logs for: idle startup; XBOX game start/exit; BPM-only start/exit; Steam game start/exit; sleep/resume; overlay use while idle. Inspect request/applied/failure events, confirm 125/250 decisions, and compare idle CPU. This section is not a prerequisite for the local Codex PR to merge.
