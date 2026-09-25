# Work Order — Full1902 Physical Rumble STOP Retry Hardening for Xbox360 + SteamDeck

## Status

Single focused production safety PR.

Repository: **onehoon/SteamAddonforClaw**  
Target branch: **main**

Code-review baseline used for this work order:

~~~text
commit: c5c5834c63324e971ace7ce465921b02a5dccf30
latest merged PR at review time: #607
~~~

Read and preserve the current Full1902 authority documents before implementation:

- docs/Full 1902 Implementation/README.md
- docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
- docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
- docs/work-order/FULL1902_PRODUCTION_RUMBLE_FEEDBACK_WORK_ORDER.md
- docs/work-order/FULL1902_XBOX360_RUMBLE_LATCH_SAFETY_STOP_HOTFIX_WORK_ORDER.md
- docs/work-order/FULL1902_SUSPEND_RESUME_NEUTRAL_PRESENTATION_WORK_ORDER.md
- docs/work-order/FULL1902_SUSPEND_RESUME_NEUTRAL_PRESENTATION_REVIEW_ADDENDUM.md

This PR hardens the already-existing production rumble path only.

Do **not** redesign controller authority, Full1902 physical ownership, HidHide, VIIPER ownership, presentation selection, power coordination, PID policy, or native feedback callback ownership.

---

# 1. Goal

Close the remaining physical-rumble safety gap where a logical STOP is requested correctly, but the **single physical STOP HID write fails**.

Current production behavior already protects against missing host STOP packets:

~~~text
Xbox360 non-zero callback
-> 5 s inactivity safety STOP

SteamDeck rumble/haptic
-> explicit/timed safety STOP

presentation retire / switch / Overlay pause / Suspend
-> lifecycle physical STOP
~~~

However, every logical STOP ultimately depends on one call to:

~~~text
IPhysicalRumbleSink.SetRumble(TwoMotorRumble.Stopped)
~~~

If that physical write returns Failed, the current call site generally logs/contains the failure and proceeds.

The existing lower layers are already designed so the same STOP can safely be attempted again:

~~~text
failed/partial physical write
-> STOP is NOT committed as last-written state
-> transport closes the failed HID handle
-> next write can reopen/retry
~~~

Therefore add exactly **one immediate bounded retry for physical STOP failures**, centrally in the shared MSI Claw physical rumble sink.

Required policy:

~~~text
normal non-zero physical rumble
-> existing single attempt only

STOP
-> attempt #1
   -> Succeeded: done
   -> Failed: attempt #2 immediately
   -> Unavailable / Disposed: do not retry

STOP attempt #2
-> Succeeded: STOP confirmed
-> Failed / Unavailable / Disposed: return final result and log observable failure
~~~

The policy must apply equally to:

~~~text
Xbox360 explicit 0/0
Xbox360 inactivity safety STOP
SteamDeck explicit STOP / haptic command-0
SteamDeck timed haptic STOP
SteamDeck rumble dead-man STOP
Overlay lifecycle STOP
presentation switch/retire STOP
Suspend/Hibernate STOP
Center M authority-release presentation STOP
shutdown/fail-close presentation retirement STOP
~~~

Do not implement separate retry logic in each feedback bridge.

---

# 2. Why this is a real safety hardening

The physical MSI Claw rumble command can remain at the last non-zero value if a required STOP never reaches the device.

The project already reproduced a real Xbox360 latch where terminal host STOP feedback did not arrive. The current Xbox360 inactivity safety STOP closes that upstream omission, but the safety STOP itself still depends on one physical I/O attempt.

A physical write can fail during realistic handheld lifecycle through:

- transient HID handle invalidation;
- PnP disturbance/re-enumeration;
- WriteFile failure;
- partial/short write;
- synchronous native write cancellation by the existing 250 ms watchdog.

This PR closes the concrete path:

~~~text
physical motor is non-zero
-> logical STOP is correctly requested
-> first physical HID STOP write fails
-> no second physical STOP attempt
-> motor can remain latched
~~~

Do not add machinery for theoretical instruction-level races.

---

# 3. Current production convergence point

Production feedback converges as follows:

~~~text
Xbox360RumbleFeedbackBridge
                         \
                          -> IPhysicalRumbleSink
                         /      |
SteamDeckRumbleFeedbackAdapter  |
                                v
                         MsiClawRumbleSink
                                |
                                v
                    WindowsMsiClawRumbleTransport
                                |
                                v
                       PID1902 gamepad HID
~~~

Lifecycle STOP also uses the same sink:

~~~text
MsiClawAddonPresentation
-> DisarmFeedbackAndStopLocked(...)
-> _rumbleSink.SetRumble(TwoMotorRumble.Stopped)
~~~

Therefore the one physical retry owner must be:

~~~text
MsiClawRumbleSink
~~~

Do not put retry ownership in Xbox360RumbleFeedbackBridge, SteamDeckRumbleFeedbackAdapter, or MsiClawAddonPresentation.

---

# 4. Existing lower-layer invariants to preserve

## 4.1 Successful-write-only dedupe

MsiClawRumbleSink records its last-written generation and rumble only after the transport reports success.

Preserve this invariant:

~~~text
STOP attempt fails
-> do not commit STOP as last-written
-> second STOP attempt must not be deduped
~~~

## 4.2 Failed transport handle is discarded

WindowsMsiClawRumbleTransport already closes the native HID handle after:

~~~text
WriteFailed
PartialWrite
~~~

The retry should naturally reopen through existing transport behavior.

Do not add another HID handle owner.

## 4.3 Native write is already bounded

The production transport already uses a default 250 ms physical-write timeout with synchronous-I/O cancellation.

Do not add a second watchdog.

With exactly two attempts, a pathological blocked STOP remains bounded to roughly:

~~~text
2 × 250 ms
~~~

plus small open/write overhead.

This is intentionally narrower than 3+ retries or background delayed retry.

---

# 5. Required Change A — STOP-only retry in MsiClawRumbleSink

Primary production file:

~~~text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawRumbleSink.cs
~~~

Implement one shared retry around the existing one-attempt core.

Do not recursively call SetRumble.

Preferred minimal structure:

~~~csharp
public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble)
{
    var first = SetRumbleContained(rumble);

    if (!rumble.Equals(TwoMotorRumble.Stopped)
        || first.Status != PhysicalRumbleWriteStatus.Failed)
    {
        return first;
    }

    var second = SetRumbleContained(rumble);
    return second;
}
~~~

SetRumbleContained should be only the smallest factoring needed to preserve the existing top-level exception containment around SetRumbleCore.

Conceptual shape:

~~~csharp
private PhysicalRumbleWriteResult SetRumbleContained(TwoMotorRumble rumble)
{
    try
    {
        return SetRumbleCore(rumble);
    }
    catch (Exception exception)
    {
        try
        {
            AppLog.Debug(
                "Rumble",
                "MSI rumble sink failure was contained.",
                ("Reason", exception.GetType().Name));
        }
        catch { }

        return new(PhysicalRumbleWriteStatus.Failed, "SinkException");
    }
}
~~~

Exact naming may follow local style.

Do not create a retry class, policy object, scheduler, or service.

---

# 6. Retry eligibility must remain narrow

Retry only when:

~~~text
rumble == TwoMotorRumble.Stopped
AND first.Status == PhysicalRumbleWriteStatus.Failed
~~~

Do not retry:

~~~text
non-zero rumble
Succeeded
Unavailable
Disposed
~~~

Reasoning:

**Non-zero**: gameplay vibration is not a safety action and should not introduce extra I/O or feedback latency.

**Unavailable**: missing identity, unavailable endpoint, stale physical session, or similar conditions are not repaired by immediate repetition and must not become a new recovery loop.

**Disposed**: lifetime has ended; never revive it from feedback.

**Failed**: this is the case where existing transport semantics make one immediate second attempt useful, including OpenFailed, WriteFailed, PartialWrite, transport exception, endpoint-resolution exception, or contained sink exception.

---

# 7. No delay/backoff

Do not add:

~~~text
Task.Delay
Timer
PeriodicTimer
background retry task
retry worker
retry queue
exponential backoff
~~~

The purpose is not general HID recovery.

It is one bounded second chance while the same STOP is still authoritative.

---

# 8. Final result semantics

SetRumble(Stopped) must return the final attempt result.

~~~text
attempt #1 Failed
attempt #2 Succeeded
-> return Succeeded

attempt #1 Failed
attempt #2 Failed
-> return second Failed

attempt #1 Failed
attempt #2 Unavailable
-> return Unavailable

attempt #1 Unavailable
-> no retry
-> return Unavailable
~~~

Do not report Succeeded merely because a retry happened.

---

# 9. Generation and identity safety remain authoritative

Every retry attempt must pass through the existing SetRumbleCore checks:

~~~text
CurrentSessionGeneration
CurrentIdentity
SameIdentity(...)
endpoint generation
stale-session rejection
admission policy
~~~

Do not cache an eligibility decision outside SetRumbleCore and reuse it blindly for attempt #2.

If the physical session changes between attempts, the second attempt must observe the current generation/identity and reject stale writes as today.

No new generation, epoch, or authority state is required.

---

# 10. Endpoint behavior remains unchanged

Preserve the current resolver and cached endpoint rules.

For a transport failure:

~~~text
transport closes native handle
-> second attempt uses existing verified endpoint metadata
-> transport reopens native handle
~~~

For a resolver exception that returns Failed:

~~~text
attempt #2 may run the existing resolver path again
~~~

For structural Unavailable:

~~~text
do not retry
~~~

Do not create another endpoint resolver.

---

# 11. Logging for the bounded retry

Keep the current failure-warning throttling.

Add low-volume structured diagnostics for the extra STOP attempt.

Recommended event meanings:

~~~text
PhysicalRumbleStopRetry
PhysicalRumbleStopRetrySucceeded
PhysicalRumbleStopRetryFailed
~~~

or equivalent existing naming.

Useful fields:

~~~text
Attempt=2
FirstStatus
FirstReason
FinalStatus
FinalReason
PhysicalGeneration when naturally available
~~~

Do not add per-frame rumble logging.

---

# 12. Xbox360 must automatically receive the hardening

Relevant production file:

~~~text
src/SteamInputAddonforClaw/Feedback/PresentationRumbleFeedback.cs
~~~

Current Xbox360 paths already target IPhysicalRumbleSink.

## Explicit host STOP

~~~text
Xbox360 callback 0/0
-> TwoMotorRumble.Stopped
-> shared sink
-> failed first physical STOP gets one second attempt
~~~

## Inactivity safety STOP

~~~text
5-second callback-silence deadline
-> TwoMotorRumble.Stopped
-> shared sink
-> failed first physical STOP gets one second attempt
~~~

Do not add Xbox360-specific retry state.

Do not change the 5-second inactivity timeout in this PR.

Do not change callback drain or native callback lifetime.

---

# 13. SteamDeck must automatically receive the hardening

The SteamDeck adapter in PresentationRumbleFeedback.cs also targets the same sink.

The shared retry therefore applies to:

~~~text
Haptic CommandType == 0
timed haptic expiry
untimed haptic safety timeout
EB rumble safety timeout
other decoded TwoMotorRumble.Stopped paths
~~~

Do not add SteamDeck-specific retry state.

Do not change:

~~~text
DefaultUntimedHapticSafetyStop = 1 s
DefaultRumbleSafetyStop = 5 s
declared haptic duration behavior
SteamDeck decoding
~~~

---

# 14. Required Change B — Suspend must not certify Safe after an unconfirmed physical STOP

Relevant file:

~~~text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs
~~~

Current suspend sequence includes:

~~~text
publisher stopped/joined
-> feedback callback disarmed/drained
-> physical rumble STOP requested
-> SAME virtual device neutral written
-> if virtual neutral succeeds:
     Paused
     Safe = true
~~~

DisarmFeedbackAndStopLocked currently returns void.

Therefore the physical STOP can be unconfirmed while the later virtual neutral succeeds, yet Suspend still reports Safe=true.

That conflicts with SuspendPauseResult.Safe, which claims game-facing output is proven safe before sleep.

---

# 15. Return STOP confirmation from DisarmFeedbackAndStopLocked

Change only what is needed.

Preferred signature:

~~~csharp
private bool DisarmFeedbackAndStopLocked(string reason)
~~~

Semantics:

~~~text
_rumbleSink is null
-> true
   no production physical-rumble sink is configured for this presentation

sink final result == Succeeded
-> true

sink final result != Succeeded
-> false

exception
-> false
~~~

Ordering must remain:

~~~text
clear/dispose feedback callback
-> drain in-progress feedback write
-> issue logical physical STOP
~~~

The logical STOP call may internally perform at most two physical attempts through MsiClawRumbleSink.

Do not add presentation-level retry or the logical STOP could multiply into more than two physical attempts.

---

# 16. Non-Suspend lifecycle callers keep existing behavior

DisarmFeedbackAndStopLocked is also used by Overlay/presentation retirement/switch/fail-close/release paths.

Those paths should benefit from the sink retry but should not be redesigned in this PR.

A final physical STOP failure must not by itself prevent required virtual detach or ownership cleanup unless an existing contract already says so.

The special new correctness requirement is only:

> Suspend must not explicitly certify output safety when the final physical motor STOP is unconfirmed.

---

# 17. Add a narrow Suspend outcome

Add one specific outcome to SuspendPauseOutcome, for example:

~~~text
RumbleStopUnconfirmed
~~~

It must not be included in SuspendPauseResult.Safe.

Required suspend flow:

~~~text
1. record suspend barrier
2. stop/join publisher
3. clear synthetic system-button pulse
4. disarm/drain feedback
5. request logical physical STOP
   -> sink may perform attempt #1 + one attempt #2
6. still write SAME attached virtual device neutral
7. classify final safety
~~~

If virtual neutral fails, preserve the existing NeutralRejected retirement/fail-close behavior.

If virtual neutral succeeds but physical STOP remains unconfirmed:

~~~text
Outcome = RumbleStopUnconfirmed
Safe = false
suspend pause remains recorded
no new PID/VIIPER/HidHide recovery
~~~

If both physical STOP and virtual neutral are confirmed:

~~~text
Outcome = Paused
Safe = true
~~~

---

# 18. Never skip virtual neutral because physical STOP failed

Incorrect:

~~~text
physical STOP fails
-> return immediately
-> virtual output remains non-neutral
~~~

Required:

~~~text
record physical STOP confirmation result
-> continue SAME-device virtual neutral
-> preserve existing neutral fail-close handling
-> then classify Suspend result
~~~

Physical motor state and virtual input state are separate safety facts.

---

# 19. No controller lifecycle reaction to STOP failure

Even after both physical STOP attempts fail, do not add:

~~~text
PID1902 -> PID1901
PID re-enumeration
physical ownership restart
DirectInput restart
HidHide mutation
VIIPER teardown/recreation
presentation swap
Center M launch
PnP recovery
runtime restart
~~~

The existing Full1902 owners remain authoritative.

This PR reports STOP as unconfirmed; it does not create a new recovery authority.

---

# 20. Physical-session retirement behavior must stay intact

MsiClawRumbleSink currently permits STOP after BeginPhysicalSessionRetirement while rejecting non-zero feedback.

Preserve this.

The second STOP attempt must remain eligible during retirement under the same existing rules.

---

# 21. Sink tests

Primary test file:

~~~text
tests/SteamInputAddonforClaw.Tests/MsiClawRumbleTests.cs
~~~

Add focused regression coverage.

## A. STOP first failure then success

~~~text
attempt #1 -> Failed
attempt #2 -> Succeeded
~~~

Assert:

~~~text
final status == Succeeded
physical write count == 2
~~~

## B. STOP fails twice

~~~text
attempt #1 -> Failed
attempt #2 -> Failed
~~~

Assert:

~~~text
final status == Failed
physical write count == 2
no third attempt
~~~

## C. Non-zero failure is not retried

Assert one transport write only.

## D. STOP Unavailable is not retried

Use existing fake identity/resolver seams.

Assert no retry loop.

## E. STOP Disposed is not retried

Assert Disposed and zero native I/O.

## F. First failed STOP is not deduped

Prove the second STOP reaches physical transport.

## G. PartialWrite can recover on second attempt

~~~text
attempt #1 -> PartialWrite
attempt #2 -> success
~~~

Assert final success and fresh native open/write behavior as appropriate.

## H. Generation safety

Only if existing fake seams express this realistic lifecycle cleanly:

~~~text
attempt #1 fails
physical generation changes
attempt #2 observes current session and does not perform stale write
~~~

Do not create elaborate synchronization solely for a theoretical interleaving.

---

# 22. Xbox360 test coverage

Existing file:

~~~text
tests/SteamInputAddonforClaw.Tests/Xbox360RumbleFeedbackBridgeTests.cs
~~~

Existing coverage already proves:

~~~text
non-zero -> inactivity STOP
newer non-zero refreshes deadline
explicit 0/0 stops immediately
Dispose cancels pending stop
~~~

Do not duplicate retry logic in bridge tests.

The test suite only needs to preserve the transitive contract:

~~~text
Xbox360 bridge -> one logical IPhysicalRumbleSink STOP
MsiClawRumbleSink -> up to two physical STOP attempts
~~~

If a tiny assertion improves clarity, add it; otherwise existing bridge tests plus new sink tests are sufficient.

---

# 23. SteamDeck test coverage

Use the existing SteamDeck feedback adapter tests.

Preserve coverage that these paths issue TwoMotorRumble.Stopped:

~~~text
Haptic CommandType == 0
timed haptic expiry
untimed haptic safety expiry
rumble safety expiry
~~~

The transitive contract is:

~~~text
SteamDeck adapter -> one logical IPhysicalRumbleSink STOP
MsiClawRumbleSink -> up to two physical STOP attempts
~~~

Do not add independent SteamDeck retry state.

---

# 24. Suspend tests

Primary test file:

~~~text
tests/SteamInputAddonforClaw.Tests/MsiClawAddonPresentationTests.cs
~~~

Add focused regressions.

## I. Normal successful Suspend

~~~text
physical STOP confirmed
virtual neutral confirmed
-> Paused
-> Safe == true
~~~

## J. First STOP failure then retry success

~~~text
physical STOP attempt #1 Failed
attempt #2 Succeeded
virtual neutral succeeds
~~~

Assert:

~~~text
Outcome == Paused
Safe == true
~~~

## K. STOP remains unconfirmed

~~~text
physical STOP attempt #1 Failed
physical STOP attempt #2 Failed
virtual neutral succeeds
~~~

Assert:

~~~text
Outcome == RumbleStopUnconfirmed
Safe == false
virtual neutral was still attempted
suspend pause remains active
no PID/HidHide/VIIPER mutation was introduced
~~~

## L. Existing neutral failure remains authoritative

If physical STOP is unconfirmed and virtual neutral also fails, preserve the current NeutralRejected retirement/fail-close result.

Do not mask the stronger existing virtual-output safety failure with RumbleStopUnconfirmed.

---

# 25. One retry owner only

After this PR, the only physical STOP retry owner must be:

~~~text
MsiClawRumbleSink
~~~

Forbidden implementation:

~~~csharp
// bridge/presentation-level retry
if (sink.SetRumble(TwoMotorRumble.Stopped).Status == Failed)
    sink.SetRumble(TwoMotorRumble.Stopped);
~~~

if MsiClawRumbleSink already performs the second attempt.

Nested retry could turn one logical STOP into four or more physical writes.

---

# 26. Final failure policy

After attempt #2 does not confirm STOP:

~~~text
return final result
log STOP unconfirmed
do not schedule perpetual retry
do not create worker
do not restart controller
do not mutate ownership
~~~

The correct terminology is:

~~~text
bounded STOP retry
best-effort physical STOP
STOP confirmed
STOP unconfirmed
~~~

Do not claim software can guarantee physical motor stop under arbitrary hardware/driver failure.

---

# 27. Expected production files

Primary:

~~~text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawRumbleSink.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs
~~~

Primary tests:

~~~text
tests/SteamInputAddonforClaw.Tests/MsiClawRumbleTests.cs
tests/SteamInputAddonforClaw.Tests/MsiClawAddonPresentationTests.cs
~~~

Potential test-only changes:

~~~text
tests/SteamInputAddonforClaw.Tests/Xbox360RumbleFeedbackBridgeTests.cs
existing SteamDeck rumble feedback tests
~~~

Production changes to PresentationRumbleFeedback.cs should normally be unnecessary because both Xbox360 and SteamDeck already converge on the common sink.

If implementation changes production feedback bridge code, explain why in the PR.

---

# 28. Explicit non-goals

Do not include:

~~~text
Xbox360 safety-timeout changes
SteamDeck timeout/duration changes
new retry framework
new timer service
new feedback authority
new rumble manager
new transport abstraction
new HID handle owner
PID1901/PID1902 lifecycle changes
HidHide changes
VIIPER lifecycle changes
physical ownership redesign
PnP recovery redesign
power coordinator redesign
new suspend state machine
Center M changes
frontend/UI changes
vibration-intensity UX changes
~~~

---

# 29. Implementation sequence

~~~text
1. Factor current sink exception containment into one-attempt contained helper.
2. Add STOP-only Failed -> one immediate second attempt.
3. Add focused sink regression tests.
4. Make DisarmFeedbackAndStopLocked report STOP confirmation.
5. Add RumbleStopUnconfirmed Suspend outcome with Safe=false.
6. Ensure virtual neutral still executes after STOP failure.
7. Add Suspend regression tests.
8. Run Xbox360 feedback tests.
9. Run SteamDeck feedback tests.
10. Run full Release suite.
11. Hardware smoke Xbox360.
12. Hardware smoke SteamDeck.
13. Hardware smoke Suspend/Hibernate.
~~~

---

# 30. Hardware validation

## Xbox360 presentation

Confirm:

~~~text
normal gameplay rumble unchanged
explicit 0/0 STOP works
5-second safety STOP still works
retire/switch does not leave vibration latched
~~~

If a diagnostic/fake seam can force first physical STOP failure without shipping test hooks, confirm the second succeeds.

## SteamDeck presentation

Confirm:

~~~text
normal SteamDeck rumble unchanged
explicit/haptic STOP works
timed haptic completion works
dead-man STOP works
retire/switch does not leave vibration latched
~~~

## Suspend/Hibernate

While vibration is active:

~~~text
Suspend/Hibernate
-> publisher quiesces
-> physical STOP requested
-> virtual device neutralized
~~~

Normal confirmed path remains Safe=true.

No controller authority transition occurs because of this PR.

---

# 31. Regression gates

Must remain unchanged:

~~~text
Full1902 Disabled-mode authority
persistent PID1902 desired state
DirectInput ownership
HidHide normalization
canonical VIIPER ownership
Xbox360 <-> SteamDeck policy
Steam/BPM observation
publisher lifecycle
Overlay pause/resume
physical input recovery
Suspend/Resume presentation ownership
Center M Enable-and-Restart release
shutdown/restart policy
front buttons
rear buttons
gyro
controller input publication
~~~

---

# 32. Acceptance criteria

~~~text
[ ] MsiClawRumbleSink is the only physical STOP retry owner
[ ] non-zero rumble remains single-attempt
[ ] STOP Failed gets exactly one immediate second attempt
[ ] STOP Unavailable is not retried
[ ] STOP Disposed is not retried
[ ] no third sink attempt occurs
[ ] failed/partial first STOP is not committed as successful STOP
[ ] successful second STOP returns Succeeded
[ ] failed second STOP returns final unconfirmed result
[ ] Xbox360 explicit STOP benefits from shared retry
[ ] Xbox360 inactivity STOP benefits from shared retry
[ ] SteamDeck explicit/haptic STOP benefits from shared retry
[ ] SteamDeck timed/dead-man STOP benefits from shared retry
[ ] lifecycle retire/Overlay STOP benefits from shared retry
[ ] Suspend STOP benefits from shared retry
[ ] Suspend cannot return Safe=true when final physical STOP is unconfirmed
[ ] virtual neutral is still attempted after physical STOP failure
[ ] existing neutral-rejection fail-close behavior remains intact
[ ] no PID/HidHide/VIIPER/authority recovery is added
[ ] focused tests pass
[ ] full Release suite passes
[ ] Xbox360 hardware rumble smoke passes
[ ] SteamDeck hardware rumble smoke passes
~~~

---

# 33. Review policy

Treat as blocking:

~~~text
Xbox360 STOP does not receive the common retry
SteamDeck STOP does not receive the common retry
non-zero rumble is retried
retry is unbounded
retry bypasses current generation/identity validation
first failed STOP is deduped
nested retry exists in bridge + sink
Suspend still certifies Safe=true after final physical STOP failure
Suspend skips virtual neutral after rumble STOP failure
new PID/HidHide/VIIPER lifecycle behavior is introduced
~~~

Do not block for:

~~~text
theoretical instruction-level races without realistic lifecycle impact
requests for a generalized retry framework
exponential backoff
3+ attempts without hardware evidence
unsupported multi-user/multi-session scenarios
~~~

---

# 34. Final design principle

Keep the implementation small:

~~~text
one shared physical rumble sink
+ one extra STOP attempt on real physical-write failure
+ one accurate Suspend safety result
~~~

Xbox360 and SteamDeck already converge on the same physical sink.

Use that convergence point instead of creating presentation-specific retry systems.

> **Every production physical STOP in both Xbox360 and SteamDeck gets one bounded second chance after a real physical write failure, while Full1902 controller ownership and lifecycle remain unchanged.**
