# Work Order — Full1902 Xbox360 Rumble Latch Safety-Stop Hotfix

## Status

Single focused production hotfix PR.

Code-review baseline used for this work order:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     856a72abd4383ae2ef58c15a7fc4ce4d02706f14
latest merged PR at review time: #512 — Add automated battery validation runner
```

Read these first and follow the Full1902 authority order:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/FULL1902_PRODUCTION_RUMBLE_FEEDBACK_WORK_ORDER.md`

This is a hotfix for the already-shipped Full1902 production rumble path. Do not redesign controller authority, presentation ownership, VIIPER ownership, physical input ownership, or HidHide behavior.

---

# 1. Goal

Prevent a real hardware-reproduced Xbox360 rumble latch where the physical MSI Claw continues vibrating indefinitely after VIIPER/XInput feedback stops arriving without a terminal `0 / 0` motor command.

Required behavior:

```text
Xbox360 non-zero rumble callback
-> physical MSI Claw rumble is written normally
-> later Xbox360 callbacks refresh the safety window

explicit 0 / 0 callback
-> stop immediately
-> cancel any pending safety stop

no further callback after non-zero rumble
-> bounded inactivity timeout expires
-> write TwoMotorRumble.Stopped once as a fail-safe
```

Use the existing presentation-scoped `Xbox360RumbleFeedbackBridge`. Do not create a new rumble authority, manager, session service, watchdog service, or generalized timer abstraction.

---

# 2. Production incident / evidence

A real MSI Claw Full1902 hardware session on 2026-09-12 reproduced an infinite-vibration condition while the active virtual presentation was Xbox360.

Observed physical rumble writes:

```text
16:53:14.160  Large8=76  Small8=0  Result=OK
16:53:14.226  Large8=51  Small8=0  Result=OK
16:53:14.282  Large8=16  Small8=0  Result=OK
```

No terminal physical `0 / 0` write followed during normal runtime.

The physical controller remained vibrating at the last non-zero state until the application was restarted approximately 85 seconds later. During teardown:

```text
Xbox360 production rumble callback disarmed
-> presentation lifecycle STOP
-> Large8=0 Small8=0 Result=OK
-> vibration stopped immediately
```

The same incident's VIIPER log showed a healthy USB/IP attachment across the latch interval. There was no attach failure, transport disconnect, or physical HID write failure at the moment the vibration became stuck.

This proves the user-visible failure is not theoretical. The product must fail safe when a non-zero Xbox360 rumble state is left without a terminal stop event.

---

# 3. Root cause in current main

Current production code in:

```text
src/SteamInputAddonforClaw/Feedback/PresentationRumbleFeedback.cs
```

contains this Xbox360 assumption:

```csharp
// XInput rumble is persistent host state (0,0 is the host's own stop), so no dead-man
// timer is needed here -- only the write drain.
```

`Xbox360RumbleFeedbackBridge.OnRumble(...)` currently:

```text
receives VIIPER callback
-> expands 8-bit motors to TwoMotorRumble
-> writes the physical sink
-> does nothing else
```

If the last callback is non-zero and no later `0 / 0` callback arrives, the MSI physical rumble command remains latched indefinitely.

The current SteamDeck adapter already protects the equivalent physical-output risk with a bounded local dead-man stop. Xbox360 is the missing path.

Do not change VIIPER solely for this hotfix. Current VIIPER Xbox360 handling already forwards valid host rumble packets, including zero motor values, through `SetXbox360RumbleCallback` without an intentional `0 / 0` filter.

---

# 4. Architectural constraints

Preserve the current Full1902 product architecture.

## 4.1 Controller authority remains unchanged

```text
Center M Enabled
-> MSI / stock authority
-> PID1901

Center M Disabled
-> Addon Runtime authority
-> PID1902
-> DirectInput + HidHide + VIIPER owned by the existing Full1902 owners
```

This hotfix must not touch PID switching, Center M transitions, startup authority, HidHide normalization, physical acquisition/recovery, or device identity.

## 4.2 Presentation ownership remains unchanged

`MsiClawAddonPresentation` remains the single owner of:

```text
Xbox360 XOR SteamDeck active presentation
publisher lifetime
feedback callback lifetime
presentation switching
Overlay pause/resume
presentation teardown / fail-close
```

The safety timer belongs inside the disposable Xbox360 feedback bridge. It must not become another presentation owner.

## 4.3 One physical rumble writer remains unchanged

Continue using only:

```text
IPhysicalRumbleSink
MsiClawRumbleSink
MsiClawRumblePacketBuilder
TwoMotorRumble
```

Do not add a second physical writer or write HID packets directly from the bridge.

---

# 5. Scope

Expected production file:

```text
src/SteamInputAddonforClaw/Feedback/PresentationRumbleFeedback.cs
```

Expected tests:

```text
tests/SteamInputAddonforClaw.Tests/RumbleV1Tests.cs
```

or, preferably if the behavioral tests become clearer as a separate focused file:

```text
tests/SteamInputAddonforClaw.Tests/ProductionRumbleFeedbackTests.cs
```

No VIIPER repository change is required for this hotfix unless implementation evidence discovered while coding contradicts the current verified callback behavior. Do not expand the PR speculatively.

---

# 6. Required Xbox360 safety policy

Use a bounded inactivity dead-man for non-zero Xbox360 physical rumble.

Production default:

```text
5 seconds after the most recent non-zero Xbox360 rumble callback
```

Rationale:

- the reproduced failure otherwise persists indefinitely;
- the current SteamDeck ordinary-rumble safety window is already 5 seconds;
- 5 seconds is intentionally conservative and avoids turning this hotfix into aggressive haptic shaping;
- the timer is only a safety fallback when no newer callback arrives;
- any newer callback refreshes the window;
- an explicit `0 / 0` still stops immediately and remains authoritative.

Do not use a very short timeout such as 100–500 ms in production. This is a safety stop, not an attempt to synthesize normal XInput rumble envelopes.

Suggested constant:

```csharp
internal static readonly TimeSpan DefaultSafetyStop = TimeSpan.FromSeconds(5);
```

A narrow optional `TimeSpan?` injection on `TryArm(...)` is acceptable for deterministic/fast unit tests. Do not introduce a clock service or timer interface solely for testing.

---

# 7. Required implementation shape

Extend `Xbox360RumbleFeedbackBridge` with the smallest local state required to cancel/replace one delayed stop.

Preferred shape, matching the existing SteamDeck adapter pattern:

```csharp
private readonly TimeSpan _safetyStopDelay;
private readonly object _safetyGate = new();
private CancellationTokenSource? _safetyStop;
private long _feedbackSequence;
```

Keep the existing:

```csharp
private readonly object _callbackWriteGate = new();
private int _disposed;
```

Do not create:

```text
RumbleWatchdog
XboxRumbleManager
FeedbackTimeoutService
TimerCoordinator
RumbleEpochManager
IRumbleClock
IRumbleScheduler
```

The existing disposable bridge is already the correct lifetime scope.

---

# 8. `TryArm(...)`

Allow tests to override the safety interval without changing normal callers.

Conceptual shape:

```csharp
internal static Xbox360RumbleFeedbackBridge? TryArm(
    IPhysicalRumbleSink sink,
    Func<Xbox360RumbleCallback?, bool> setNativeCallback,
    TimeSpan? safetyStop = null)
{
    var bridge = new Xbox360RumbleFeedbackBridge(
        sink,
        setNativeCallback,
        safetyStop ?? DefaultSafetyStop);

    // existing callback registration behavior unchanged
}
```

Do not require `MsiClawAddonPresentation` to pass a timeout. Production composition should continue calling `TryArm(...)` exactly as it does today unless the signature requires only the optional default parameter.

Callback registration failure policy is unchanged: input/presentation remains healthy and rumble is unavailable for that presentation.

---

# 9. Callback behavior

`OnRumble(...)` must preserve the existing motor mapping and physical sink behavior.

```csharp
var rumble = new TwoMotorRumble(
    Expand(leftMotor),
    Expand(rightMotor));
```

The existing mapping remains:

```text
leftMotor  -> LargeMotor
rightMotor -> SmallMotor
```

Then:

```text
write current rumble under _callbackWriteGate
-> if explicit stopped state, cancel pending dead-man and schedule nothing
-> if non-zero, cancel/replace the previous dead-man with a new 5-second window
```

The callback must still contain exceptions and never throw across the native callback boundary.

Do not add retries, endpoint re-resolution loops, controller reconciliation, or lifecycle transitions inside the native callback.

---

# 10. Safety-stop scheduling

Use the same simple cancellation + sequence style already used by `SteamDeckRumbleFeedbackAdapter`.

Conceptual implementation:

```csharp
private void ScheduleSafetyStop(TwoMotorRumble rumble)
{
    CancellationToken token;
    long sequence;

    lock (_safetyGate)
    {
        _safetyStop?.Cancel();
        _safetyStop?.Dispose();
        _safetyStop = null;
        sequence = ++_feedbackSequence;

        if (Volatile.Read(ref _disposed) != 0 || rumble.Equals(TwoMotorRumble.Stopped))
            return;

        _safetyStop = new CancellationTokenSource();
        token = _safetyStop.Token;
    }

    _ = StopAfterDelayAsync(sequence, _safetyStopDelay, token);
}
```

Then:

```csharp
private async Task StopAfterDelayAsync(long sequence, TimeSpan delay, CancellationToken token)
{
    try
    {
        await Task.Delay(delay, token).ConfigureAwait(false);

        lock (_safetyGate)
        {
            if (token.IsCancellationRequested ||
                sequence != _feedbackSequence ||
                Volatile.Read(ref _disposed) != 0)
                return;
        }

        lock (_callbackWriteGate)
        {
            if (Volatile.Read(ref _disposed) != 0) return;

            var result = _sink.SetRumble(TwoMotorRumble.Stopped);
            if (result.Status == PhysicalRumbleWriteStatus.Failed)
            {
                AppLog.Debug(
                    "Rumble",
                    "Production Xbox360 rumble safety stop was not confirmed.",
                    ("Event", "ProductionRumbleStopFailed"),
                    ("Presentation", "Xbox360"),
                    ("Reason", result.Reason));
            }
        }
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested)
    {
    }
    catch (Exception exception)
    {
        AppLog.Debug(
            "Rumble",
            "Production Xbox360 rumble safety stop was contained.",
            ("Event", "ProductionRumbleStopFailed"),
            ("Presentation", "Xbox360"),
            ("Reason", exception.GetType().Name));
    }
}
```

This is sample code, not a demand to copy line-for-line. Match current project style and reuse the same semantics as the existing SteamDeck local dead-man implementation.

Do not add extra epoch/barrier/state machinery for pathological instruction-level interleavings. The requirement is the realistic product failure: non-zero motor state followed by callback silence must converge to STOP.

---

# 11. Explicit `0 / 0` behavior

A host-delivered zero state remains the normal authoritative stop.

Required:

```text
callback left=0 right=0
-> physical STOP immediately through the existing sink
-> pending dead-man cancelled/disposed
-> no delayed duplicate stop required
```

Do not debounce or suppress the explicit stop.

Do not wait for the five-second timeout when the host already sent `0 / 0`.

---

# 12. Repeated non-zero callbacks

Every newer non-zero callback supersedes the previous safety deadline.

Example:

```text
T+0.0s   76 / 0   -> write 76 / 0, safety deadline T+5.0
T+0.1s   51 / 0   -> write 51 / 0, old deadline cancelled, new deadline T+5.1
T+0.2s   16 / 0   -> write 16 / 0, old deadline cancelled, new deadline T+5.2
no more callbacks
T+5.2s             -> write STOP
```

This directly addresses the reproduced incident.

A steady stream of legitimate rumble updates must keep refreshing the deadline and must not be interrupted by the safety stop.

---

# 13. Dispose / presentation teardown

Preserve current presentation teardown ordering.

`MsiClawAddonPresentation.DisarmFeedbackAndStopLocked(...)` already owns:

```text
armed feedback Dispose()
-> callback clear
-> callback write drain
-> explicit final physical TwoMotorRumble.Stopped
```

The Xbox360 bridge must cancel its delayed stop during `Dispose()` before the presentation owner performs its final lifecycle STOP.

Required conceptual addition:

```csharp
public void Dispose()
{
    if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

    lock (_safetyGate)
    {
        _safetyStop?.Cancel();
        _safetyStop?.Dispose();
        _safetyStop = null;
        _feedbackSequence++;
    }

    RumbleCallbackCleanup.ClearNativeCallback(_setNativeCallback, "Xbox360");
    lock (_callbackWriteGate) { }
}
```

Do **not** move the presentation owner's explicit lifecycle STOP into the bridge. There should still be one clear final teardown path owned by `MsiClawAddonPresentation`.

Do not change Overlay pause/resume, X360↔Deck switching, publisher shutdown, or Center M release ordering unless a directly failing existing test proves the hotfix requires it.

---

# 14. Logging

Do not add per-callback INFO logging. Rumble callbacks can be frequent.

Keep normal callback traffic silent.

Use DEBUG only for real safety-stop write failure/exception, consistent with the existing production rumble failure policy.

Optional: one DEBUG diagnostic when the dead-man actually fires is acceptable if useful during hardware validation, but it is not required. Do not log every timer arm/cancel.

If added, prefer one event such as:

```text
Event=ProductionRumbleSafetyStop
Presentation=Xbox360
Reason=CallbackSilence
```

Avoid noisy logs in normal gameplay.

---

# 15. Tests

Add focused behavior tests. Do not rely on the five-second production timeout in tests; pass a short test-only `TimeSpan` through the optional `TryArm(...)` argument.

Use a fake `IPhysicalRumbleSink` that records writes and a fake native callback registrar that captures the registered delegate.

Minimum required cases:

## 15.1 Non-zero then silence stops

```text
arm bridge with short test timeout
invoke callback with non-zero motors
verify non-zero physical write
wait beyond test timeout
verify final write == TwoMotorRumble.Stopped
```

This is the regression test for the hardware incident.

## 15.2 Newer non-zero callback refreshes deadline

```text
non-zero A
wait less than timeout
non-zero B
pass A's original deadline but not B's deadline
verify B is still the latest physical state / no premature STOP
then pass B's deadline
verify STOP
```

Do not make this a scheduler torture test. Use comfortable timing margins suitable for CI.

## 15.3 Explicit zero stops immediately and cancels delayed stop

```text
non-zero
-> explicit 0 / 0 before timeout
-> immediate STOP
-> wait beyond old deadline
-> no later stale timer write that changes state
```

It is fine if the fake sink observes an idempotent STOP only if the implementation naturally produces one, but the preferred behavior is cancellation without an unnecessary delayed duplicate.

## 15.4 Dispose cancels pending delayed stop

```text
non-zero
-> Dispose bridge
-> presentation/lifecycle test owns any final STOP separately
-> wait beyond timeout
-> disposed bridge must not produce a late write
```

This guards the existing owner contract: after bridge disposal, `MsiClawAddonPresentation`'s lifecycle STOP remains final.

## 15.5 Existing mapping tests remain green

Preserve existing exact 8-bit expansion behavior:

```text
0   -> 0
1   -> 257
127 -> 32639
255 -> 65535
```

Do not change physical scaling or motor channel mapping.

---

# 16. Test implementation guidance

Prefer deterministic state assertions over exact wall-clock equality.

Reasonable test-only intervals are acceptable, for example:

```text
safety stop: 100–200 ms
poll/assert margin: comfortably beyond that
```

Do not use 1–5 ms deadlines that create flaky CI scheduler tests.

Do not add a production clock abstraction solely to make these tests instant.

If the existing test utilities already provide a wait-until helper, reuse it. Otherwise a small bounded polling helper local to the test file is acceptable.

---

# 17. Manual hardware validation

After unit tests pass, validate on the MSI Claw in Center M Disabled / Full1902 authority mode.

Required checks:

### A. Reproduce the original class of failure

Use a game/application that generated the observed descending Xbox360 rumble sequence.

Expected after fix:

```text
non-zero rumble callbacks arrive
-> physical vibration follows them
-> if terminal 0 / 0 is absent
-> vibration stops automatically within approximately 5 seconds of the final non-zero callback
```

### B. Normal short rumble

Normal button/impact rumble must start and stop without noticeable regression.

### C. Repeated rumble updates

Repeated active rumble callbacks must refresh the window rather than being cut off by the first callback's timeout.

### D. Explicit stop

When the host sends a normal terminal stop, physical vibration must stop immediately rather than waiting five seconds.

### E. Presentation switch

```text
Xbox360 -> SteamDeck
SteamDeck -> Xbox360
```

must still stop old-presentation rumble during switch and must not leave a delayed Xbox360 timer that writes after the switch.

### F. Overlay pause / resume

Existing presentation pause/resume lifecycle must still stop rumble safely.

### G. Runtime restart / teardown

Controlled Runtime restart must still send the normal lifecycle STOP and leave motors stopped.

No PID1901/PID1902 behavior change is expected from this PR.

---

# 18. Explicit non-goals

Do not include any of the following in this PR:

```text
VIIPER protocol redesign
VIIPER USB/IP transport changes
Xbox360 descriptor changes
physical HID packet format changes
rumble strength user setting
per-game rumble strength
Controller-page vibration UI
Developer Vibration Test restoration
SteamDeck rumble redesign
new feedback authority
new generic dead-man framework
new scheduler/clock abstraction
new lifecycle state machine
epoch/barrier machinery
PID1901/PID1902 changes
HidHide changes
Center M changes
publisher changes
Steam/BPM detection changes
```

If unrelated issues are discovered, document them separately. Do not grow this hotfix.

---

# 19. Acceptance criteria

The PR is complete when all of the following are true:

1. `Xbox360RumbleFeedbackBridge` has a bounded non-zero callback inactivity safety stop.
2. Production timeout defaults to 5 seconds.
3. Every new non-zero callback refreshes the safety window.
4. Explicit `0 / 0` stops immediately and cancels the pending safety stop.
5. `Dispose()` cancels pending delayed work before callback teardown/drain.
6. No delayed Xbox360 safety write can intentionally survive bridge disposal.
7. `MsiClawAddonPresentation` remains the presentation/feedback lifetime owner.
8. Existing lifecycle final STOP remains in `DisarmFeedbackAndStopLocked(...)`.
9. No new authority/manager/service/state machine is introduced.
10. Existing motor mapping/scaling remains unchanged.
11. Regression tests cover non-zero→silence, refresh, explicit stop, and dispose.
12. Existing Full1902 rumble, presentation, and lifecycle tests remain green.
13. Full test suite passes.
14. MSI Claw hardware validation confirms the reproduced infinite-vibration failure converges to STOP without requiring an app restart.

---

# 20. PR review focus

Review this PR for realistic product behavior, especially:

```text
non-zero rumble + missing terminal stop
presentation switch
Overlay pause/resume
Runtime restart / teardown
physical device/session loss as already handled by the existing sink
real physical write failure containment
```

Do not block the PR for purely theoretical instruction-level timer/callback interleavings if the existing bridge gates, cancellation/sequence check, and presentation teardown path converge safely under normal supported lifecycle behavior.

The goal is one small, understandable safety correction to the existing owner—not another feedback architecture.
