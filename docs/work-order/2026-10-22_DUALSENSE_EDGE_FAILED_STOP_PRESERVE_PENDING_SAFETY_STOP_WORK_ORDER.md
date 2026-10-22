# Work Order — DualSense Edge Failed Explicit STOP: Preserve the Pending Safety STOP

Date: 2026-10-22
Repository: onehoon/SteamAddonforClaw
Implementation baseline: main @ 8c67d4a5edd6c027fb61ff7aa3a843946742bef3
Type: One small focused Addon code PR
Scope: DualSense Edge physical-rumble STOP failure handling ONLY
Hardware validation: User-owned after merge; NOT a PR, CI, or Codex blocker

## 1. Goal

Fix one real, source-confirmed safety gap: when a DualSense Edge explicit zero-rumble callback reaches the physical MSI Claw sink and its final return status is Failed (after the sink's already-implemented bounded immediate STOP retry), the DualSense Edge adapter currently cancels the pending 5-second safety STOP anyway.

If the last nonzero physical rumble write succeeded and the immediate zero writes then fail, the physical motors can remain latched until some other independent STOP event occurs. Preserve the **existing, already scheduled safety STOP** after a failed explicit STOP instead of discarding that remaining opportunity.

This is NOT a change to normal rumble semantics and NOT a new retry system. When the pending safety timer expires, it should execute its existing single SetRumble(Stopped) call; that shared sink retains responsibility for its existing maximum one immediate retry on Failed.

## 2. Required current product authority

Read and follow the current document precedence in:

- docs/Full 1902 Implementation/README.md
- docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
- docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
- docs/work-order/FULL1902_PRODUCTION_RUMBLE_FEEDBACK_WORK_ORDER.md
- docs/work-order/FULL1902_PHYSICAL_RUMBLE_STOP_RETRY_HARDENING_WORK_ORDER_2026-09-25.md
- docs/work-order/DUALSENSE_EDGE_PR_A_TYPED_BACKEND_WORK_ORDER_2026-10-11.md

The product is the standalone Full PID1902 application. CTW integration is obsolete. Respect the one-Windows-user/one-interactive-session product scope, one physical rumble writer, the existing presentation owner, and normal Sleep/Hibernate/Resume, teardown, device loss/PnP, and fail-close paths.

## 3. Confirmed current code path (read before editing)

### 3.1 The edge adapter cancels the deadman without inspecting physical STOP success

File: src/SteamInputAddonforClaw/Feedback/PresentationRumbleFeedback.cs
Class: DualSenseEdgeRumbleFeedbackAdapter
Methods: OnOutput, ScheduleSafetyStop, StopAfterDelayAsync, Dispose

Current callback critical section:

    try { _sink.SetRumble(rumble); }
    finally { ScheduleSafetyStop(rumble); }

When rumble is Stopped, ScheduleSafetyStop cancels/disposes _safetyStop, increments _feedbackSequence, and returns without a new timer.

The timer was armed by the most recent accepted nonzero Edge callback. A Failed zero callback therefore destroys that still-pending chance to stop the motors, even though the physical STOP was not confirmed.

### 3.2 The shared physical sink ALREADY retries failed STOP writes

File: src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawRumbleSink.cs
Method: SetRumble

- First call to SetRumbleContained(Stopped).
- If first Status != Failed, return that result with no retry.
- Otherwise perform ONE additional SetRumbleContained(Stopped).
- Return the second/final result.
- Successful-write-only dedupe prevents a failed STOP from being incorrectly cached as delivered.
- The retry revalidates the current physical generation/identity and endpoint before writing.
- Unavailable and Disposed do not trigger the retry.

Existing tests:
- tests/SteamInputAddonforClaw.Tests/MsiClawRumbleTests.cs
  - Sink_retries_a_failed_stop_once_and_returns_the_successful_retry
  - Sink_stops_retrying_after_two_failed_stop_attempts
  - Stop_retry_rechecks_physical_identity_before_the_second_write
  - Sink_does_not_retry_an_unavailable_stop
  - Sink_does_not_retry_a_disposed_stop

DO NOT duplicate, remove, or redesign this sink-level retry.

### 3.3 Owner-level lifecycle STOP is independent

File: src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs
Methods: TryDisarmFeedbackLocked, RequestPhysicalRumbleStopLocked, DisarmFeedbackAndStopLocked

Publisher retirement/disarm still drains the callback and requests physical STOP through the same shared sink. Retain that ordering. This small change must not make the adapter own teardown or modify presentation/PID1902/HidHide/VIIPER authority.

## 4. Precise required behavior

| Edge callback / sink final result | Pending safety STOP behavior |
| --- | --- |
| Nonzero rumble, any normal result | Preserve existing behavior: cancel/rearm the 5-second timer from this nonzero callback, including the existing exception-containment behavior. |
| Explicit Stopped, Succeeded (including a successful sink retry) | Preserve existing behavior: cancel the pending timer; STOP was confirmed. |
| Explicit Stopped, Failed | NEW: DO NOT call ScheduleSafetyStop(Stopped); preserve the existing pending timer and its original deadline/sequence. |
| Explicit Stopped, Unavailable or Disposed | Do not broaden this work order: preserve current no-retry/no-new-deadman policy and rely on existing physical-session/owner fail-close. |
| Explicit Stopped, sink unexpectedly throws | Do not cancel an already-pending safety STOP solely because the exceptional STOP was unconfirmed; still contain the exception at the native callback boundary. Do not add a throw-specific retry mechanism. |
| Existing safety timer fires | Preserve the existing single timer callback, which calls the shared sink with Stopped. No additional timer is scheduled by the timer itself. |
| New nonzero callback after failed STOP | Preserve existing nonzero callback scheduling: cancel the old pending timer and rearm from the new callback. |
| Dispose / presentation retirement | Preserve cancellation, native callback clear/drain, and the owner's independent final STOP. No delayed write from a retired adapter. |

Important: this is an existing pending deadline, NOT a new full 5-second period measured from the failed STOP. It is also not a guarantee of another write if the prior safety timer has already fired or was never armed; do not introduce a scheduler/state framework to handle such unsupported cases.

For a normal pending timer, the maximum physical attempts are:
- Explicit STOP via MsiClawRumbleSink: up to 2 synchronous transport writes if the first fails.
- At the already scheduled safety deadline: one further SetRumble(Stopped) call; the same sink may internally perform up to 2 transport writes if its first attempt fails.
- No unbounded retries, background polling, or timer rescheduling.

## 5. Recommended minimal implementation shape

Edit only the Edge adapter's OnOutput callback branch. Inspect the latest source before applying the example.

Replace the unconditional zero-result timer update with a decision based on the existing PhysicalRumbleWriteResult.Status.

Illustrative C# (adapt local naming/style as needed):

    var isStop = rumble.Equals(TwoMotorRumble.Stopped);
    var shouldUpdateSafetyStop = !isStop;
    try
    {
        var result = _sink.SetRumble(rumble);
        if (isStop)
        {
            // Final Failed already includes the sink's immediate STOP retry.
            // Keep the previous nonzero callback's pending deadman.
            shouldUpdateSafetyStop = result.Status != PhysicalRumbleWriteStatus.Failed;
        }
    }
    finally
    {
        if (shouldUpdateSafetyStop)
            ScheduleSafetyStop(rumble);
    }

Place this within the EXISTING _callbackWriteGate lock and outer native callback exception containment.

Why this shape:
- Nonzero callback: the preexisting finally schedule remains, even if the sink unexpectedly throws.
- Stopped + Succeeded/Unavailable/Disposed: the timer is canceled just as before.
- Stopped + final Failed: no ScheduleSafetyStop invocation, therefore the old _safetyStop token, _feedbackSequence, and deadline remain intact.
- Stopped + unexpected throw: the pending timer likewise is not canceled because the STOP was unconfirmed.

A logically equivalent, equally small implementation is acceptable. Do not add a flag/state member or a new owner just to express this local decision.

Do not change the timeout from 5 seconds, motor mapping/Expand, native callback signature, callback registration, VIIPER Output Report validity, or the sink's current write/dedupe behavior.

## 6. Required focused automated tests

Extend tests/SteamInputAddonforClaw.Tests/DualSenseEdgeRumbleFeedbackAdapterTests.cs.

Reuse the current tests' injectable short safetyStop TimeSpan, RecordingSink pattern, and TaskCompletionSource/WaitAsync convention; use a small scripted result fake for this class if necessary. At this adapter boundary, one SetRumble call represents the sink's FINAL result, not each internal transport attempt. The sink's two-transport-write behavior is already tested separately in MsiClawRumbleTests.

Required scenarios:

1. Failed explicit STOP preserves pending fallback:
   - Create/arm Edge callback with a short test safety deadline.
   - Nonzero callback -> physical sink responds Succeeded.
   - Before deadline, zero callback -> scripted final Failed (representing both internal physical STOP attempts failing).
   - Verify the zero callback tried STOP immediately.
   - Verify the previously scheduled safety callback later calls SetRumble(Stopped) again WITHOUT a new START.
   - Confirm the fallback is bounded (the adapter does not arm an additional timer after it fires).

2. Successful explicit STOP cancels pending fallback:
   - Nonzero callback -> Succeeded.
   - Zero callback -> Succeeded.
   - No delayed duplicate STOP should be emitted by the prior timer.
   - If time-based checking is needed, use modest test-only deadlines/timeouts similar to existing tests, not a new injected scheduler abstraction.

3. Newly received nonzero START supersedes the preserved pending fallback:
   - Nonzero -> final Failed explicit STOP -> later new nonzero.
   - The new nonzero must retain the existing cancel/rearm behavior, not let the old failed-STOP timer stop the newly active motors.
   - Validate with the simplest robust timing arrangement available in the existing test suite.

4. Retirement/dispose before the retained deadline:
   - Retire/dispose the adapter after a failed explicit STOP.
   - No stale timer should write afterward; current owner-level final STOP behavior remains the owner's responsibility.

5. Result-status boundary:
   - A sink whose first internal physical STOP fails but second succeeds would return Succeeded to the adapter: verify this cancels the timer (scripted Succeeded is sufficient).
   - Unavailable/Disposed retain existing behavior; no new retry/resume machinery.

6. Exception containment:
   - An unexpected throw from a zero-rumble sink call must not escape the native callback and must not discard a still-pending timer. Preserve nonzero-throw scheduling semantics.

Retain existing tests:
- Callback_expands_and_maps_large_and_small_motors_while_ignoring_led_fields
- Nonzero_callback_gets_bounded_deadman_stop
- Dispose_clears_callback_and_drains_inflight_writer_before_final_stop
- Native_callback_exceptions_are_contained

Avoid testing theoretical instruction-level timing interleavings solely because they can be fabricated. Normal cancellation, new input, and real failure paths are enough.

## 7. Regression and lifecycle constraints

- Do not modify Xbox360RumbleFeedbackBridge or SteamDeckRumbleFeedbackAdapter for this Edge-only task.
- Do not modify MsiClawRumbleSink, WindowsMsiClawRumbleTransport, endpoint resolution, or physical session identity logic.
- Preserve the ordering of current owner disarm/clear/drain/physical STOP, including overlay capture, publisher retirement, suspend/hibernate, physical device loss, PnP re-enumeration, and runtime shutdown.
- Do not change successful-zero behavior, nonzero 5-second safety policy, or timer ownership.
- No new lock, manager, queue, epoch, retry worker, alarm service, or VIIPER protocol change.
- Do not use device discovery or create new authority outside the existing owner.
- Do not tie this to the separate Audio Return or selector-only-zero research. Ghost of Tsushima's earlier neutral STOP fix is already a different VIIPER change and is out of scope.
- Keep the shared sink's failure handling truthful: a Failed result must not be logged or cached as a confirmed physical STOP.

Optional: one narrow Debug entry when a Failed explicit STOP leaves the pending timer intact is acceptable if it uses existing AppLog and is not per-report spam. Do not implement a new diagnostic system.

## 8. Test commands and PR acceptance

Use the existing .NET test project:

    dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj --filter FullyQualifiedName~DualSenseEdgeRumbleFeedbackAdapterTests

Also run the existing sink and presentation lifecycle tests or the full existing test suite as feasible. Correct build/test regressions introduced by the PR.

Source-level acceptance:
- A final Failed explicit Edge STOP no longer cancels an already pending nonzero deadman.
- Existing timed STOP fires once if still owned and pending, using the shared physical sink.
- Successful explicit STOP cancels pending safety work immediately.
- A subsequent nonzero callback replaces the pending deadline.
- Disposal/lifecycle retirement cancels the timer and keeps the owner's final physical STOP path intact.
- Existing VIIPER, Xbox360, SteamDeck, and Full1902 controller-authority contracts remain unchanged.
- Only the focused Addon adapter code and its related automated tests need substantive change.

Do NOT require an MSI Claw, DualSense Edge controller, Steam/BPM, connected USB hardware, or real game reproduction as a Codex implementation/test/PR blocker. After a code-correct PR passes CI it may be merged; any physical validation belongs to the user post-merge.

## 9. Expected PR size / delivery

One small implementation PR. Expected changed files:

1. src/SteamInputAddonforClaw/Feedback/PresentationRumbleFeedback.cs
2. tests/SteamInputAddonforClaw.Tests/DualSenseEdgeRumbleFeedbackAdapterTests.cs

No Go/VIIPER PR, ABI change, dependency lock/hash update, UI change, new settings, release workflow change, or cleanup refactor.

In the PR description briefly report:
- the previous double-failed explicit STOP timeline,
- exactly how the existing pending safety timer is now preserved,
- which final write statuses cancel vs retain the timer,
- automated test outcomes,
- any genuine remaining lifecycle limitation (without treating a theoretical corner case as a release blocker).

**Product principle:** Reuse the established presentation-scoped deadman and physical sink. A failed physical STOP should not destroy the only already-scheduled fallback, but normal ownership and teardown remain with the existing Full1902 controller owner.
