# Work Order — Full1902 End-to-End Independent 250 Hz Controller Cadence

> **Date:** 2026-09-27  
> **Reviewed baseline:** `main` at `b230f4a93260217f808d29e28a5f3c9f75c84197`  
> **Scope:** Full PID1902 standalone controller runtime  
> **Change type:** One focused production cadence PR  
> **Target:** Physical PID1902 input + Xbox360 publisher  
> **SteamDeck:** Already 250 Hz; no functional cadence change

---

## 1. Goal

Align the complete Full1902 controller data path with the measured MSI Claw PID1902 DirectInput-visible state cadence:

```text
PID1902 physical input        ≈250 Hz / 4 ms
Xbox360 virtual publisher    ≈250 Hz / 4 ms
SteamDeck virtual publisher  ≈250 Hz / 4 ms
```

All three timing loops must remain **independent**.

Do not phase-lock them.

Do not introduce one shared scheduler, shared timer, barrier, epoch, wake-on-`StateChanged`, or cross-thread timing handshake.

The intended final architecture is:

```text
MsiClawInputSource
dedicated physical worker
+ existing WindowsHighResolutionOneShotTimer
+ existing CanonicalPublisherDeadlineMath
+ independent 4 ms absolute deadline
        │
        ▼
    LatestState
        │
        ├──────────────────────┐
        ▼                      ▼
CanonicalXbox360          CanonicalSteamDeck
InputPublisher            InputPublisher
independent 4 ms          independent 4 ms
absolute deadline         absolute deadline
        │                      │
        ▼                      ▼
      X360                  SteamDeck
```

The publishers continue to consume the latest available snapshot. They do not wait for a specific physical-input generation.

---

## 2. Real-hardware evidence

PR #617 initially attempted a 1 ms diagnostic oversampling cadence through `Task.Delay`.

That first run was scheduler-limited:

```text
Reads:            ~64.6 Hz
Distinct states:  ~64.5 Hz
interval:         ~15–16 ms
```

PR #619 replaced the diagnostic wait with the existing high-resolution waitable timer.

Two independent real MSI Claw runs then produced:

### Run 1

```text
Reads:            8,554  (855.4 Hz)
Distinct states:  2,496  (249.5 Hz)
Duplicates:       70.8%

Distinct interval:
Min       1.00 ms
Mean      3.54 ms
Median    4.00 ms
P95       4.00 ms
Max      12.00 ms
```

### Run 2

```text
Reads:            8,425  (842.4 Hz)
Distinct states:  2,472  (247.1 Hz)
Duplicates:       70.7%

Distinct interval:
Min       1.00 ms
Mean      3.53 ms
Median    3.00 ms
P95       4.00 ms
Max      28.00 ms
```

The reader itself was comfortably faster than 250 Hz in both runs, while distinct raw DirectInput state changes converged near 250 Hz.

This is sufficient product evidence to treat the supported PID1902 path as approximately:

```text
4 ms / 250 Hz DirectInput-visible fresh-state cadence
```

for the current Full1902 implementation.

The diagnostic still measures DirectInput-visible distinct raw state, not USB interrupt packets. That distinction does not block this production change: the Addon can demonstrably consume fresh DirectInput state at approximately 250 Hz.

---

## 3. Why this supersedes the recent Xbox360 125 Hz decision

PR #616 changed the Xbox360 publisher from 250 Hz to 125 Hz.

That decision was based on the then-current assumption:

```text
physical PID1902 source ≈125 Hz
therefore
X360 at 250 Hz mostly republishes duplicate physical snapshots
```

The corrected hardware diagnostic disproves that assumption for the supported MSI Claw path.

The new measured relationship is:

```text
physical source capability ≈250 Hz
```

Therefore an X360 125 Hz presentation would intentionally discard approximately every other fresh physical state before virtual publication.

The new current product policy is:

```text
Physical PID1902 = 250 Hz
Xbox360          = 250 Hz
SteamDeck        = 250 Hz
```

`FULL1902_XBOX360_125HZ_PUBLISHER_CADENCE_WORK_ORDER.md` remains a historical record of PR #616 and must not be rewritten to pretend that change never happened.

This work order supersedes its cadence recommendation.

---

## 4. Why production physical polling must NOT use Task.Delay(4)

Do not implement the physical 250 Hz change as:

```csharp
await Task.Delay(TimeSpan.FromMilliseconds(4), cancellationToken);
```

There are two concrete reasons.

### 4.1 The target machine already demonstrated coarse Task.Delay behavior

The original cadence diagnostic requested 1 ms through `Task.Delay` but observed approximately 15–16 ms / 65 Hz.

Therefore simply requesting 4 ms from the same managed-delay mechanism is not evidence that the production loop will actually run at 4 ms.

The product now has a working high-resolution primitive. Use it.

### 4.2 Relative delay-after-work drifts even if the delay itself were accurate

This pattern:

```text
ReadState
→ map/log/publish LatestState
→ delay 4 ms
→ ReadState
```

has a real period of:

```text
work duration + 4 ms
```

For example:

```text
0.5 ms work + 4 ms delay = 4.5 ms ≈222 Hz
1.0 ms work + 4 ms delay = 5.0 ms =200 Hz
```

The existing publisher scheduler instead keeps a monotonic logical grid:

```text
4 ms
8 ms
12 ms
16 ms
20 ms
...
```

If a wake or work item is late, the next wait becomes shorter rather than redefining the schedule from "now".

That is the behavior required for production physical 250 Hz polling too.

---

## 5. Reuse existing timing primitives — do not build a new scheduler framework

The repository already contains the two timing primitives required for this work.

### Existing native timer

`src/SteamInputAddonforClaw/VirtualOutput/Viiper/WindowsHighResolutionOneShotTimer.cs`

provides the existing wrapper around:

```text
CreateWaitableTimerExW
CREATE_WAITABLE_TIMER_HIGH_RESOLUTION
SetWaitableTimerEx
CancelWaitableTimer
```

PR #619 already reuses this exact type for the PID1902 developer cadence diagnostic.

### Existing absolute-deadline math

`src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalPublisherDeadlineMath.cs`

already provides:

- `StopwatchTicksFromTimeSpan(...)`;
- `ConvertToRelativeDueTime100ns(...)`;
- `AdvanceDeadline(...)`.

The X360 and SteamDeck production publishers already use these primitives.

The physical production loop should now reuse both directly.

Do **not** create:

```text
PhysicalInputScheduler
ControllerCadenceManager
Shared250HzClock
PollingRateService
InputOutputTimingCoordinator
DeadlineScheduler<T>
```

Do not duplicate the Win32 P/Invoke.

Do not rename/move the existing timer/math types solely because the physical input source will also use them.

The current names are acceptable for this focused change.

---

## 6. Final cadence policy

Required end state:

| Stage | Period | Nominal rate | Scheduler |
|---|---:|---:|---|
| PID1902 physical input | 4 ms | ~250 Hz | independent high-resolution absolute deadline |
| Xbox360 publisher | 4 ms | ~250 Hz | existing independent high-resolution absolute deadline |
| SteamDeck publisher | 4 ms | ~250 Hz | existing independent high-resolution absolute deadline |

This does **not** mean all three ticks occur at the same instant.

Example:

```text
Physical deadlines:
0.7, 4.7, 8.7, 12.7 ...

X360 deadlines:
2.1, 6.1, 10.1, 14.1 ...

SteamDeck deadlines:
3.0, 7.0, 11.0, 15.0 ...
```

That is acceptable.

Each publisher simply reads `LatestState` at its own deadline.

Do not attempt to align those phases.

---

## 7. MsiClawInputSource production worker

### Current production path

Current `MsiClawInputSource` remains an async polling loop with:

```csharp
private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(8);
```

and normal polling eventually reaches:

```csharp
Task.Delay(PollInterval, session.Cancellation.Token)
```

The diagnostic path added by PR #619 temporarily uses a separate high-resolution 1 ms wait.

### Required production path

Change the normal production physical cadence to:

```csharp
private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(4);
```

but do **not** use that value with `Task.Delay`.

Convert the production physical polling path to a dedicated background worker thread driven by:

```text
WindowsHighResolutionOneShotTimer
+
CanonicalPublisherDeadlineMath
+
Stopwatch.GetTimestamp()
```

The physical worker should use:

```text
IsBackground = true
Priority = ThreadPriority.AboveNormal
Name = a clear PID1902/physical-input worker name
```

Do not reuse `PublisherThreadQoS` merely to share its name-mismatched logging/helper surface. That is outside this cadence change.

Do not introduce a new generalized QoS wrapper.

---

## 8. Preserve the existing single DirectInput read owner

There must still be exactly one live sequence of:

```text
IDirectInputDevice.ReadState()
```

for the owned PID1902 session.

Do not create:

- a second physical polling thread;
- a separate high-rate DirectInput reader;
- a producer/consumer read queue;
- a second DirectInput device instance;
- a second Acquire;
- a raw HID side channel.

The existing `MsiClawInputSource` remains the sole DirectInput owner.

The production scheduler changes how that one owner waits between reads; it does not change ownership.

---

## 9. Physical absolute-deadline behavior

Use the same logical scheduling rule already proven by the publishers.

Conceptually:

```text
period = 4 ms
origin = Stopwatch.GetTimestamp()
nextDeadline = origin + period
```

The physical session may continue performing its first DirectInput read immediately after acquisition, preserving current first-valid-state behavior.

Subsequent production reads follow the fixed deadline grid.

Conceptually:

```text
initial immediate read
        ↓
wait until logical deadline 4 ms
        ↓
ReadState / map / LatestState / StateChanged
        ↓
advance logical deadline from previous deadline
        ↓
arm only for remaining time to next future deadline
        ↓
wait
```

If work or scheduling runs past one or more deadlines:

```text
CanonicalPublisherDeadlineMath.AdvanceDeadline(...)
```

must skip expired deadlines and return the next future deadline.

Do not burst multiple artificial reads to "catch up" every missed deadline.

Do not redefine the schedule as:

```text
now + 4 ms
```

after every normal iteration, because that reintroduces drift.

---

## 10. Dedicated worker lifetime

The physical session must retain the current externally-visible lifecycle contract:

```text
StartPrepared()
→ physical session becomes live
→ WaitForFirstValidStateAsync()
→ normal input
→ StopAsync()/failure
→ cleanup
→ TestCompleted
```

Implementation may replace the current `PollingTask` with the smallest dedicated-thread equivalent.

A suitable session-local shape is:

```text
InputSession
    PollingThread
    PollingCompletion TaskCompletionSource
    ProductionTimer
    existing CancellationTokenSource
    existing DirectInput device/enumerator
```

This state is justified by the real worker lifetime.

Do not add another owner/manager around it.

### StopAsync

Keep `StopAsync` asynchronous.

Conceptually:

```text
session.Cancellation.Cancel()
→ worker wait wakes
→ worker exits its read loop
→ existing neutral + DirectInput cleanup runs
→ completion is signaled
→ StopAsync returns
```

The completion signal must only be completed after the existing cleanup/finally behavior has executed.

This preserves the invariant that a successful `StopAsync` means no further physical reads can occur from that session.

A separate elaborate epoch/barrier protocol is not required.

---

## 11. Physical timer startup failure

A production physical scheduler is required infrastructure, unlike the optional developer diagnostic.

### Timer creation / initial arm / worker-start failure before a live session is established

Fail closed.

Required result:

```text
timer/thread startup fails
→ clean up the acquired DirectInput session/resources
→ do not leave _currentSession live
→ StartPrepared returns an existing appropriate initialization/start failure
→ outer Full1902 ownership rollback/reconcile remains authoritative
```

Do not silently fall back to `Task.Delay(4)`.

Do not leave the controller running at a coarse unmanaged cadence while claiming 250 Hz.

Do not add a new public start-status value unless the existing `InitializationFailed` contract genuinely cannot represent this failure.

Prefer the existing status surface.

---

## 12. Physical timer runtime failure

If `SetWaitableTimerEx` / re-arm fails after the physical session is already live, this is a real owned-input operation failure.

Do not continue with a defective timing fallback.

Add one explicit stop reason if needed, for example:

```text
MsiClawInputStopReason.PollSchedulerFailed
```

Then let the existing physical-input fault policy treat it like the other non-`Stopped` owned-session failures:

```text
live physical scheduler failure
→ stop physical session
→ neutral LatestState
→ existing cleanup
→ TestCompleted(non-Stopped reason)
→ existing AddonProcessHost physical recovery/reconcile
```

Do not create a scheduler-specific recovery manager.

The existing owner/reconcile path is the correct authority.

---

## 13. Preserve PR #619 cadence diagnostic

The Developer `PID1902 Input Cadence` diagnostic must remain functional.

It still needs to oversample above the 250 Hz production cadence, so its current nominal 1 ms high-resolution wait remains useful.

Required behavior:

```text
normal production
→ physical worker uses its own 4 ms absolute-deadline timer

diagnostic active
→ same physical worker / same DirectInput session
→ temporary 1 ms diagnostic wait
→ no second reader
→ collect raw cadence statistics

diagnostic ends
→ resume production 4 ms absolute-deadline schedule
```

### Rebase after diagnostic completion

Do not attempt to catch up ten seconds of production deadlines after the diagnostic.

When leaving the temporary diagnostic timing mode:

```text
now = Stopwatch.GetTimestamp()
next production deadline = now + 4 ms
```

then continue the normal absolute-deadline grid from that new origin.

This is a bounded diagnostic-mode transition, not a second production timing authority.

No persistent timing state from the diagnostic may survive after it ends.

---

## 14. Do not share timer handles across the three loops

Reuse the **timer implementation**, not one timer instance.

Required:

```text
physical session
→ its own WindowsHighResolutionOneShotTimer handle

active X360 publisher
→ its own WindowsHighResolutionOneShotTimer handle

active SteamDeck publisher
→ its own WindowsHighResolutionOneShotTimer handle
```

Only one virtual publisher is active at a time under the existing presentation policy, but its timer is still independent from physical input.

Do not introduce:

- shared wait handle ownership;
- one master 250 Hz timer broadcasting ticks;
- physical-to-publisher event signaling;
- common phase state.

---

## 15. Xbox360 publisher — restore 250 Hz

Current:

`src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalXbox360InputPublisher.cs`

contains:

```csharp
private static readonly TimeSpan ProductionPeriod = TimeSpan.FromMilliseconds(8);
```

Change it back to:

```csharp
private static readonly TimeSpan ProductionPeriod = TimeSpan.FromMilliseconds(4);
```

Update current XML/documentation from:

```text
~125 Hz / 8 ms
```

to:

```text
~250 Hz / 4 ms
```

Do not otherwise redesign the X360 publisher.

Preserve:

- its existing dedicated worker;
- `WindowsHighResolutionOneShotTimer`;
- `CanonicalPublisherDeadlineMath`;
- absolute logical deadlines;
- `ThreadPriority.AboveNormal`;
- current sink failure behavior;
- stop/join fail-close behavior;
- restart-after-clean-stop semantics;
- mapping/back-button behavior;
- rumble behavior.

This part of the PR is a fixed period restoration, not a publisher rewrite.

---

## 16. SteamDeck publisher — no functional change

Current SteamDeck publisher already uses:

```csharp
private static readonly TimeSpan ProductionPeriod = TimeSpan.FromMilliseconds(4);
```

and already has the required high-resolution absolute-deadline worker.

Do not change its functional timing behavior.

Do not refactor it solely to make the three implementations visually identical.

Its existing timing diagnostics/heartbeat and SteamDeck-specific behavior remain intact.

Final relationship:

```text
Physical  = 4 ms
X360      = 4 ms
SteamDeck = 4 ms
```

with three independent clocks.

---

## 17. LatestState remains the handoff boundary

Do not replace the current snapshot architecture.

Required:

```text
physical worker
→ Volatile.Write LatestState

publisher worker
→ read LatestState at its own deadline
→ map
→ VIIPER SetState
```

Do not introduce a state queue between physical and virtual output.

Do not require every 250 Hz physical state to produce exactly one corresponding virtual report.

Independent clocks can naturally produce:

- an occasional repeated snapshot;
- a fresh physical snapshot that is superseded before one publisher deadline;
- a fresh snapshot that waits until the next publisher tick.

These are normal consequences of independent sampling clocks and are not defects requiring synchronization machinery.

---

## 18. StateChanged semantics

Preserve the existing semantic rule:

```text
StateChanged fires when mapped ControllerState changes
```

Do not turn it into a mandatory 250 Hz tick event.

The virtual publishers must continue using `LatestState`, not `StateChanged`, as their production data source.

This avoids coupling publisher cadence to event delivery.

---

## 19. Logging and 250 Hz behavior

Do not add per-poll INFO logs.

Existing change-based logging should remain bounded by its current semantics.

Review the physical loop after conversion to ensure the new 250 Hz cadence does not accidentally turn any previously low-frequency unconditional diagnostic into a 250 Hz log source.

Do not solve logging concerns by lowering the polling cadence.

If an existing diagnostic is unnecessarily executed on every unchanged sample, gate/throttle that diagnostic locally rather than changing the controller timing architecture.

---

## 20. Tests — MsiClawInputSource production scheduler

Update/add focused deterministic tests under:

```text
tests/SteamInputAddonforClaw.Tests/MsiClawInputSourceTests.cs
tests/SteamInputAddonforClaw.Tests/MsiClawInputCadenceDiagnosticTests.cs
```

At minimum prove:

1. production physical period is exactly 4 ms;
2. production uses `WindowsHighResolutionOneShotTimer`, not `Task.Delay(4)`;
3. initial production absolute deadline is one 4 ms period from the chosen origin;
4. production worker is a background thread;
5. production worker priority is `AboveNormal`;
6. one source session creates/acquires exactly one DirectInput device;
7. normal stop wakes the worker and completes cleanup;
8. no physical read occurs after successful `StopAsync`;
9. timer creation failure before successful start cleans up and does not leave `IsRunning=true`;
10. timer initial-arm/worker-start failure cleans up the session;
11. runtime timer re-arm failure stops the live session with a non-`Stopped` failure reason;
12. that failure remains compatible with existing physical recovery classification;
13. read failure behavior remains `ReadStateFailed`;
14. invalid initial state / invalid layout behavior remains unchanged;
15. first valid state still completes the existing `FirstValidState` contract.

Do not write wall-clock CI tests that require exactly 250 reads per second.

Use the existing timer/native test seams and deterministic timestamp/deadline math.

---

## 21. Tests — diagnostic coexistence with 250 Hz production

Preserve and extend PR #619 coverage to prove:

```text
production physical timing
→ 4 ms high-res absolute deadline

start diagnostic
→ same device/session
→ temporary 1 ms diagnostic high-res wait

finish/cancel diagnostic
→ no reacquire
→ production deadline is rebased
→ 4 ms high-res production continues
```

Verify:

- no second device creation;
- no second `Acquire`;
- diagnostic timer and production timer are separate handles/instances;
- cancellation leaves the physical source running;
- source loss still fails the diagnostic with the current session;
- later recovered sessions do not inherit diagnostic state.

Do not add a generalized "timing mode manager".

A worker-local production-deadline-valid/rebase flag is sufficient if needed.

---

## 22. Tests — Xbox360 250 Hz restoration

Update:

`tests/SteamInputAddonforClaw.Tests/CanonicalXbox360InputPublisherTests.cs`

The current test:

```text
Production_worker_is_background_AboveNormal_and_starts_from_an_8ms_absolute_deadline
```

must become the 4 ms equivalent.

Update deterministic expected period/deadline calculations from 8 ms to 4 ms.

Preserve all existing lifecycle/fault/cleanup assertions.

Do not add flaky report-count-per-second tests.

---

## 23. SteamDeck tests

Existing SteamDeck 4 ms tests should continue to pass unchanged.

Do not rewrite them simply because the final cadence table now says all three are 250 Hz.

If an architecture/static test is useful to assert the final three 4 ms contracts, keep it small and deterministic.

---

## 24. Full1902 lifecycle requirements

This timing change must preserve the current real product lifecycle.

### Suspend / Hibernate

Existing owner behavior remains:

```text
suspend
→ neutral / stop or quiesce owned process resources as already implemented
→ do not release Addon authority to MSI merely because of sleep
```

The physical high-resolution worker must stop cleanly when the existing source/session cancellation occurs.

### Resume

Existing reconciliation remains authoritative:

```text
resume
→ reconcile PID1902
→ create/acquire fresh DirectInput session as needed
→ new physical 250 Hz worker
→ current desired presentation
```

Do not assume the old timer/device handle survives resume.

### Physical device loss / PnP re-enumeration

Existing `ReadState`/owned-session failure and physical recovery paths remain authoritative.

Do not hide a real device loss behind timer retry logic.

### Restart / shutdown / Center M authority transition

Existing controller authority/teardown ordering remains unchanged.

The dedicated worker must stop before its DirectInput device/enumerator are disposed.

No new controller authority is introduced.

---

## 25. Timer failure and fail-close summary

Use different policy for developer-diagnostic timing and production timing because their responsibilities differ.

### Developer diagnostic timer fails

Existing PR #619 policy remains:

```text
diagnostic fails locally
physical production continues
```

### Production physical timer fails

Required:

```text
physical production session cannot maintain required scheduler
→ physical session fails
→ existing Full1902 physical recovery/reconcile path
```

Do not silently degrade production to `Task.Delay(4)`.

This is a real operation failure, not a theoretical race.

---

## 26. Documentation updates

Update current/living documentation that describes the present cadence as:

```text
physical = nominal 125 Hz
X360 = 125 Hz
```

to the new measured/current policy:

```text
physical = 250 Hz
X360 = 250 Hz
SteamDeck = 250 Hz
```

At minimum inspect:

- current XML comments in `MsiClawInputSource.cs`;
- current XML comments in `CanonicalXbox360InputPublisher.cs`;
- `docs/VIIPER_MIGRATION_TODO.md`;
- any living Full1902 document that explicitly claims the current physical/X360 cadence.

Do not rewrite completed historical work orders such as:

```text
FULL1902_XBOX360_125HZ_PUBLISHER_CADENCE_WORK_ORDER.md
FULL1902_PID1902_CADENCE_DIAGNOSTIC_HIGH_RES_TIMER_FIX_WORK_ORDER.md
```

Those should remain evidence of the development/measurement sequence.

This new work order is the current cadence authority.

---

## 27. Expected implementation surface

Primary expected files:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputSource.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputContracts.cs
src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalXbox360InputPublisher.cs

tests/SteamInputAddonforClaw.Tests/MsiClawInputSourceTests.cs
tests/SteamInputAddonforClaw.Tests/MsiClawInputCadenceDiagnosticTests.cs
tests/SteamInputAddonforClaw.Tests/CanonicalXbox360InputPublisherTests.cs
```

Reuse directly:

```text
src/SteamInputAddonforClaw/VirtualOutput/Viiper/WindowsHighResolutionOneShotTimer.cs
src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalPublisherDeadlineMath.cs
```

These two timing primitives should require no functional redesign.

Only add/adjust narrow test seams if needed.

SteamDeck production implementation should not require functional changes.

---

## 28. Explicit non-goals

Do not include:

- shared 250 Hz master clock;
- phase locking;
- physical/publisher barrier;
- sequence epochs;
- state queue;
- publisher wake-on-`StateChanged`;
- second DirectInput reader;
- extra HID/RawInput path;
- USB ETW/USBPcap;
- `Task.Delay(4)` production fallback;
- `timeBeginPeriod`;
- global Windows timer-resolution mutation;
- busy spin;
- spin-wait scheduler;
- new generic scheduler abstraction;
- new controller timing manager;
- user-selectable polling rate;
- persisted cadence setting;
- dynamic 125/250 mode;
- changes to PID1901/PID1902 ownership policy;
- HidHide policy changes;
- VIIPER attach/detach policy changes;
- rumble behavior changes;
- gyro integration;
- SteamDeck cadence changes.

---

## 29. Hardware validation

After implementation, validate on the same supported MSI Claw.

### 29.1 Physical production cadence

Do not rely only on the developer oversampling result.

Add or use bounded production timing summary diagnostics sufficient to verify that the new normal physical worker is actually operating around:

```text
~250 wakes/reads per second
~4 ms nominal deadline
```

Do not permanently log every poll.

A bounded summary/debug diagnostic is sufficient.

### 29.2 Xbox360 presentation

With Steam/BPM inactive:

```text
physical = 250 Hz
X360 = 250 Hz
```

Verify:

- sticks;
- triggers;
- face buttons;
- D-pad;
- M1/M2 mappings;
- no stuck state;
- no obvious input regression;
- rumble still behaves normally.

### 29.3 SteamDeck presentation

With Steam/BPM active:

```text
physical = 250 Hz
SteamDeck = 250 Hz
```

Verify normal Steam Input behavior and no regression.

### 29.4 Presentation transitions

Exercise repeatedly:

```text
X360
→ SteamDeck
→ X360
```

Verify:

- physical PID1902 session stays owned;
- no physical reacquire solely because presentation changed;
- one virtual presentation remains active;
- X360 and Deck each resume their own 4 ms schedule.

### 29.5 Lifecycle

At minimum smoke-test:

- Sleep → Resume;
- Hibernate → Resume if available in the normal test cycle;
- controlled Runtime restart;
- normal app shutdown/restart;
- physical device re-enumeration/recovery where practical.

Timing changes must not alter authority or teardown semantics.

---

## 30. Performance validation

Moving physical polling from the current managed-delay behavior to a real ~250 Hz worker will increase the number of:

- DirectInput reads;
- mappings;
- `LatestState` writes;
- change comparisons.

That increase is intentional because hardware testing shows the source actually supplies fresh data at this cadence.

Still verify on hardware:

- no sustained abnormal CPU usage;
- no UI starvation;
- no publisher starvation;
- no excessive logging;
- no change in thermal/power behavior large enough to be user-visible.

Do not preemptively add batching, coalescing, queues, or adaptive rates without a measured problem.

If a concrete per-poll operation becomes expensive, optimize that operation rather than lowering the entire physical cadence by assumption.

---

## 31. Acceptance criteria

The PR is complete only when all are true:

1. production `MsiClawInputSource` nominal period is 4 ms;
2. production physical polling no longer uses `Task.Delay` as its cadence scheduler;
3. physical production uses the existing `WindowsHighResolutionOneShotTimer`;
4. physical production uses the existing `CanonicalPublisherDeadlineMath` absolute-deadline logic;
5. physical polling runs on one dedicated background worker;
6. the physical worker is `AboveNormal` priority;
7. exactly one DirectInput device/session remains owned;
8. first-valid-state, mapping, `LatestState`, `StateChanged`, and cleanup semantics remain intact;
9. diagnostic oversampling still uses the same physical session and works above the production cadence;
10. leaving diagnostic mode rebases cleanly to a new 4 ms production deadline;
11. production timer startup failure fails closed without leaving a live partial session;
12. production timer runtime failure enters the existing physical recovery path rather than falling back to coarse polling;
13. Xbox360 `ProductionPeriod` is 4 ms;
14. Xbox360 remains on its existing independent high-resolution absolute-deadline worker;
15. SteamDeck remains 4 ms with no functional timing change;
16. physical, X360, and SteamDeck clocks are not phase-locked or shared;
17. no new timing manager/scheduler abstraction is introduced;
18. no HidHide/VIIPER/PID authority policy changes are introduced;
19. deterministic unit tests pass;
20. full CI passes;
21. real hardware confirms normal production physical polling is approximately 250 Hz under ordinary conditions;
22. X360 and SteamDeck hardware smoke tests show no user-visible regression;
23. Sleep/Resume and normal recovery/teardown remain safe.

---

## 32. Review guidance

Treat the following as blockers:

- production physical polling still uses `Task.Delay(4)`;
- production physical timing silently falls back to a coarse timer after high-resolution timer failure;
- a second DirectInput reader/session is introduced;
- physical worker can continue reading after `StopAsync` completes;
- physical timer failure is ignored while the source remains advertised as healthy;
- Xbox360 remains at 8 ms;
- SteamDeck is accidentally changed away from 4 ms;
- one shared timer/clock becomes a new authority for physical + virtual output;
- diagnostic and production paths can concurrently call `ReadState`;
- Suspend/Resume, PnP recovery, shutdown, or controller teardown can dispose DirectInput while the physical worker is still live;
- the change creates a new timing manager/state-machine architecture without a real lifecycle requirement.

Do **not** block for:

- different phase offsets between physical/X360/SteamDeck 4 ms clocks;
- occasional repeated `LatestState` publication;
- a physical state being superseded before a publisher tick;
- narrow instruction-level interleavings with no realistic lifecycle consequence;
- exact 250.000 Hz under scheduler load;
- not sharing a single master timer;
- not building a generalized scheduler abstraction.

The intended implementation remains deliberately simple:

```text
one physical owner
+ one physical 4 ms high-resolution absolute-deadline worker

one active virtual publisher
+ its own existing 4 ms high-resolution absolute-deadline worker

LatestState between them
no synchronization coupling
```
