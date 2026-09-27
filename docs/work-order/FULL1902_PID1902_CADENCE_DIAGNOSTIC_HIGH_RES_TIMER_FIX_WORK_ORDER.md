# Work Order — PR618 Full1902 PID1902 Cadence Diagnostic High-Resolution Timer Fix

> **Date:** 2026-09-27  
> **Scope:** Developer-only PID1902 cadence diagnostic follow-up  
> **Change type:** Small diagnostic timing correction  
> **Production physical polling:** No change  
> **Frontend protocol:** No change

---

## 1. Goal

Fix the Developer Menu `PID1902 Input Cadence` diagnostic added by PR #617 so that its temporary oversampling loop actually runs fast enough to distinguish approximately 125 Hz from approximately 250 Hz DirectInput state cadence.

The current hardware result is:

```text
Reads:             646  (64.6 Hz)
Distinct states:   646  (64.5 Hz)
Duplicates:        0.0%

Distinct interval:
Min       15.00 ms
Mean      15.06 ms
Median    15.00 ms
P95       16.00 ms
Max       16.00 ms
```

This result is **not evidence that PID1902 itself is approximately 65 Hz**.

The diagnostic currently requests a 1 ms cadence through `Task.Delay(1 ms)`, but the observed 15–16 ms wake interval shows that the diagnostic loop itself is the limiting factor.

The corrected diagnostic must use the existing Windows high-resolution waitable-timer primitive so that:

```text
diagnostic requested cadence = 1 ms nominal
actual ReadState rate        = comfortably above 250 Hz on supported hardware
```

Only after the diagnostic reader itself exceeds 250 Hz can `DistinctStateHz` / distinct-state intervals be used to compare a possible ~125 Hz versus ~250 Hz physical state cadence.

---

## 2. Important interpretation of the current hardware result

Current PR #617 hardware evidence:

```text
ReadState cadence      ≈ 64.6 Hz
distinct-state cadence ≈ 64.5 Hz
duplicate rate         = 0%
median interval        = 15 ms
```

The reader never outran the source.

Therefore:

```text
64.5 Hz distinct state
!=
proven PID1902 hardware/report cadence
```

The current run is **inconclusive about PID1902 cadence**.

It only proves that the current diagnostic's managed-delay scheduler is too coarse for this measurement.

Do not change the production controller cadence based on this result.

---

## 3. Current architecture to preserve

Full1902 ownership remains:

```text
Center M Disabled
→ one Addon-owned PID1902 DirectInput session
→ MsiClawInputSource
→ LatestState
→ exactly one active virtual presentation
```

PR #617 correctly kept the cadence diagnostic inside the existing live `MsiClawInputSource`.

Preserve that.

The diagnostic must still:

- use the same already-acquired `IDirectInputDevice`;
- create no second DirectInput session;
- perform no PID transition;
- perform no HidHide mutation;
- perform no VIIPER attach/detach;
- perform no controller reacquire;
- keep `LatestState` and normal controller behavior live during the test.

---

## 4. Root cause to correct

Current diagnostic behavior in:

`src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputSource.cs`

temporarily resolves:

```text
diagnostic active → TimeSpan.FromMilliseconds(1)
```

but still waits through the existing async polling path:

```csharp
await Task.Delay(...);
```

The real hardware run shows this does not produce 1 ms sampling on the target Windows environment.

Do not attempt to fix this by changing:

```text
1 ms → 0 ms
Task.Yield()
busy spin
Thread.SpinWait
timeBeginPeriod()
global timer-resolution mutation
```

The repository already contains the appropriate native timing primitive.

---

## 5. Reuse the existing high-resolution timer

Existing file:

`src/SteamInputAddonforClaw/VirtualOutput/Viiper/WindowsHighResolutionOneShotTimer.cs`

already provides:

```text
CreateWaitableTimerExW
CREATE_WAITABLE_TIMER_HIGH_RESOLUTION
SetWaitableTimerEx
one-shot relative waits
WaitHandle integration
fail-closed timer creation/arming
```

It is already used by the canonical X360 / SteamDeck publisher timing paths.

Use this existing primitive for the **temporary diagnostic oversampling wait only**.

Do not create:

- another P/Invoke implementation;
- another timer wrapper;
- a new generic scheduler;
- a timer service/manager;
- a global timing authority.

A direct internal dependency from the developer-only diagnostic path to the existing timer primitive is acceptable for this focused PR.

Do not move or rename the timer type merely for architectural aesthetics.

---

## 6. Required diagnostic scheduling behavior

Normal production behavior must remain exactly as it is today:

```text
diagnostic inactive
→ existing production MsiClawInputSource polling path
→ existing nominal 8 ms Task.Delay path
```

This PR does **not** decide whether that production path later needs a high-resolution scheduler.

While the cadence diagnostic is active:

```text
successful raw DirectInput read
→ observe raw state
→ arm existing high-resolution one-shot timer for nominal 1 ms
→ wait for the timer
→ next ReadState
```

When the diagnostic finishes, is cancelled, or fails:

```text
dispose/retire diagnostic timer
→ immediately return to the existing normal production polling path
```

No persistent timing change may survive the test.

---

## 7. Keep the timing fix local to the diagnostic

The smallest reasonable implementation is preferred.

A practical shape is:

```text
MsiClawInputCadenceCollector
or
the current InputSession cadence-diagnostic state

owns:
    one WindowsHighResolutionOneShotTimer
    only for the lifetime of the 10-second diagnostic
```

The poll loop can then use a narrow helper conceptually similar to:

```csharp
if (cadenceDiagnostic is { IsActive: true })
{
    cadenceDiagnostic.WaitForNextSample(session.Cancellation.Token);
}
else
{
    await Task.Delay(PollInterval, session.Cancellation.Token)
        .ConfigureAwait(false);
}
```

Exact naming/placement may follow the current implementation.

Do not create an independent diagnostic polling thread that reads the same device concurrently.

There must still be exactly one sequence of `ReadState()` calls against the live DirectInput device.

---

## 8. Cancellation / lifecycle behavior

The diagnostic timer must not interfere with the existing lifecycle authority.

### Frontend cancellation

When the frontend request is cancelled:

```text
collector completes Cancelled
→ diagnostic timer is disposed
→ next normal polling wait uses existing 8 ms production path
```

Do not stop the physical DirectInput session merely because the developer diagnostic was cancelled.

### Input-session stop / source loss

Existing source-loss handling remains authoritative.

If the physical session stops:

```text
existing MsiClawInputSource failure/teardown path
→ diagnostic completes Failed/Cancelled as appropriate
→ timer disposed
→ no stale diagnostic carries into a recovered/new InputSession
```

### Suspend / shutdown / Center M authority release

Do not add special lifecycle state.

The existing session cancellation/teardown must be able to end the high-resolution wait promptly enough for normal supported lifecycle operations.

A bounded sub-millisecond / ~1 ms timer wake is acceptable; do not add an elaborate cancellation state machine solely to eliminate an extremely narrow wait window.

---

## 9. High-resolution timer failure policy

This is a developer diagnostic.

Failure to create or arm the diagnostic high-resolution timer must **not** kill the production physical-input session.

Required behavior:

```text
high-resolution timer create/arm failure
→ diagnostic result = Failed
→ log one bounded diagnostic failure
→ clear diagnostic state
→ production DirectInput loop continues at its existing normal cadence
```

Do not:

- terminate PID1902 ownership;
- schedule physical recovery;
- detach VIIPER;
- fall back silently to `Task.Delay(1)` and report misleading data.

The diagnostic must fail honestly if its measurement scheduler cannot be established.

---

## 10. Do not change the result contract

PR #617 already added:

`FrontendPid1902InputCadenceResult`

with the required fields:

- `RequestedDurationMs`
- `ActualDurationMs`
- `SuccessfulReadCount`
- `ObservedReadHz`
- `DistinctStateCount`
- `DuplicateReadCount`
- `DuplicatePercent`
- `DistinctStateHz`
- distinct interval min/mean/median/P95/max

Keep this contract.

No new frontend RPC is required.

No payload change is required.

No result DTO change is required.

Therefore:

```text
FrontendTransportProtocol.CurrentVersion = 44
```

must remain unchanged.

Do not bump to v45 for an internal scheduler correction.

---

## 11. Do not change the Developer UI

Keep the current Developer card and button:

```text
PID1902 Input Cadence
[ Run 10s Test ]
```

Keep the current result rendering and the existing warning:

```text
Sampling rate was too low for a reliable 125 Hz vs 250 Hz comparison.
```

That warning remains useful if the target machine still cannot exceed 250 Hz after the timing fix.

No new page, option, slider, or polling-rate selector is required.

---

## 12. Measurement semantics remain unchanged

Continue measuring raw `DirectInputState` before normalized `ControllerState` mapping.

Continue comparing:

- buttons;
- X/Y/Z;
- RotationX/Y/Z;
- POV values.

Continue counting only changed raw states as distinct.

Do not reinterpret the diagnostic as a USB interrupt-report capture.

The result still means:

> effective distinct raw DirectInput state cadence observable by the Addon.

It does not prove that every USB interrupt report was observed, because identical consecutive reports remain indistinguishable through current-state comparison.

Raw HID / USB ETW / USBPcap remains outside this PR.

---

## 13. Logging

Keep logging bounded.

Start/completion summary logs are sufficient.

Do not log every 1 ms read.

Do not log every duplicate.

Do not dump raw states.

If the high-resolution timer cannot be created or armed, add one clear diagnostic failure log including the exception / Win32 error already surfaced by the existing timer primitive.

Normal production failure logging remains unchanged.

---

## 14. Tests

Update/add focused deterministic tests.

### 14.1 Production path is unchanged

Prove that when no cadence diagnostic is active:

```text
MsiClawInputSource normal poll interval = existing 8 ms
```

Do not change the expected production interval in this PR.

### 14.2 Diagnostic no longer depends on Task.Delay(1)

Add structural or seam-based coverage proving that the active diagnostic path uses the high-resolution waitable timer and does not route its 1 ms cadence through `Task.Delay`.

Avoid a wall-clock CI assertion such as:

```text
must achieve >500 Hz on CI
```

CI scheduler behavior is not a reliable hardware timing oracle.

### 14.3 Timer creation failure is diagnostic-local

Simulate high-resolution timer creation failure.

Verify:

```text
diagnostic → Failed
physical input source → remains running
normal polling → remains available
no second device is created/acquired
```

### 14.4 Timer arm failure is diagnostic-local

Simulate a `SetWaitableTimerEx` / arm failure.

Verify the same fail-local behavior.

### 14.5 Cancellation

Start a diagnostic, cancel it, and verify:

- result is Cancelled;
- diagnostic timer is disposed;
- source remains running;
- a later diagnostic can start normally;
- production polling selection returns to normal.

### 14.6 Source loss

Preserve the PR #617 source-loss test:

- diagnostic fails with the current session;
- no diagnostic state is reused by a later recovered/new session.

### 14.7 Existing statistics

PR #617 raw equality and percentile/statistics tests should remain unchanged and pass.

---

## 15. Hardware validation

Run the same physical test again after this PR.

Environment:

```text
Center M Disabled
Addon owns PID1902
normal virtual presentation active
Developer → PID1902 Input Cadence
```

During all 10 seconds:

- continuously rotate both sticks;
- continuously vary LT/RT;
- do not hold controls at endpoints.

### First validation gate — reader speed

Before interpreting the device cadence, require:

```text
ObservedReadHz > 250 Hz
```

Prefer a comfortable margin above 250 Hz.

If the result is still <=250 Hz:

```text
measurement remains inconclusive
→ do not infer PID1902 cadence
```

### If reader speed is sufficiently high

Interpret the distribution:

#### Likely ~125 Hz observable state cadence

```text
Reads: comfortably >250 Hz
DistinctStateHz: around 125 Hz
Median distinct interval: around 8 ms
substantial duplicate percentage
```

#### Likely ~250 Hz observable state cadence

```text
Reads: comfortably >250 Hz
DistinctStateHz: around 250 Hz
Median distinct interval: around 4 ms
substantial duplicate percentage relative to ~1 ms reads
```

#### Possible ~60–65 Hz observable state cadence

Only if:

```text
Reads: comfortably >250 Hz
DistinctStateHz: around 60–65 Hz
Median distinct interval: around 15–16 ms
many duplicate reads between state changes
```

would the ~65 Hz result become meaningful evidence about the DirectInput-visible source cadence rather than the diagnostic scheduler.

---

## 16. Production cadence decision is explicitly deferred

Do **not** change the production physical input scheduler in this PR.

Even though the current hardware result raises a legitimate question about the existing production:

```csharp
Task.Delay(TimeSpan.FromMilliseconds(8))
```

that is a separate product change.

This PR must answer only:

> Can the diagnostic oversample the live PID1902 source fast enough to measure its observable distinct-state cadence?

After corrected hardware results exist, make a separate decision about whether production `MsiClawInputSource` should remain on its current managed 8 ms delay or move to a high-resolution deadline worker.

Do not combine measurement correction and production timing behavior into one PR.

---

## 17. Expected files

Primary expected changes:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputSource.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputCadenceDiagnostic.cs
tests/SteamInputAddonforClaw.Tests/MsiClawInputCadenceDiagnosticTests.cs
```

Reuse without functional changes where possible:

```text
src/SteamInputAddonforClaw/VirtualOutput/Viiper/WindowsHighResolutionOneShotTimer.cs
```

If a tiny test seam is necessary in the existing timer type, keep it focused and do not alter its production publisher semantics.

Do not modify frontend transport/UI files unless the current implementation unexpectedly requires a compile-only adjustment.

---

## 18. Explicit non-goals

Do not include:

- production PID1902 polling cadence change;
- production 8 ms `Task.Delay` replacement;
- Xbox360 publisher cadence change;
- SteamDeck publisher cadence change;
- synchronization between physical input and virtual publishers;
- second DirectInput session;
- raw HID reader;
- Raw Input reader;
- USB ETW;
- USBPcap;
- `timeBeginPeriod`;
- busy-spin polling;
- global timer-resolution changes;
- new polling manager/service;
- persisted cadence settings;
- protocol v45;
- Developer UI redesign;
- PID/HidHide/VIIPER lifecycle changes.

---

## 19. Acceptance criteria

The PR is complete only when all are true:

1. The 1 ms diagnostic oversampling path no longer uses `Task.Delay(1)`.
2. It reuses the existing `WindowsHighResolutionOneShotTimer` implementation.
3. No second DirectInput session or reader is created.
4. The high-resolution timer exists only for the active bounded diagnostic.
5. Diagnostic completion/cancellation/failure returns polling to the existing normal production path.
6. Timer creation/arm failure fails only the diagnostic and does not kill physical ownership.
7. Frontend result contract and UI remain unchanged.
8. `FrontendTransportProtocol.CurrentVersion` remains 44.
9. Production `MsiClawInputSource` behavior outside the active diagnostic remains unchanged.
10. X360 / SteamDeck publisher timing remains unchanged.
11. Existing Full1902 ownership, HidHide, VIIPER, recovery, suspend/resume, and teardown semantics remain unchanged.
12. Deterministic tests cover diagnostic timer success/failure/cancellation without wall-clock frequency assumptions.
13. Build and test CI pass.
14. Real-hardware rerun reaches an `ObservedReadHz` comfortably above 250 Hz before any 125-vs-250 conclusion is accepted.

---

## 20. Review guidance

Treat as blockers:

- the diagnostic still uses `Task.Delay(1)`;
- a second DirectInput reader/session is introduced;
- high-resolution timer failure kills the production physical-input session;
- a diagnostic timer/resource survives after completion/cancellation;
- frontend protocol is bumped unnecessarily;
- production polling cadence is changed in the same PR;
- X360/SteamDeck publisher timing changes;
- supported lifecycle teardown can leave the diagnostic wait active.

Do not block for:

- an instruction-level race requiring an extra epoch/barrier with no realistic lifecycle consequence;
- exact 1000 Hz diagnostic read rate;
- minor 1 ms wake jitter;
- lack of a generic timing abstraction.

The purpose is not to build a new scheduler framework.

The purpose is simply:

```text
current diagnostic:
Task.Delay(1)
→ ~15–16 ms actual
→ ~65 Hz
→ unusable measurement

PR618:
existing high-resolution one-shot timer
→ nominal 1 ms diagnostic wait
→ actual reader comfortably >250 Hz
→ meaningful PID1902 cadence measurement
```
