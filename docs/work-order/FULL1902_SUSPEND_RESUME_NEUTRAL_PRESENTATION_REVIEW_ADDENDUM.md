# Review Addendum — Full1902 Suspend/Resume Neutral Presentation Safety

> **Date:** 2026-09-04  
> **Status:** Authoritative implementation addendum  
> **Applies to:** `FULL1902_SUSPEND_RESUME_NEUTRAL_PRESENTATION_WORK_ORDER.md`  
> **Reviewed production code baseline:** `2e7885fc7bb85876d5ed3344c105a1a02eafe255`  
> **Work-order commit:** `1263ec190a6ed645ea1cfbfb48ea1bcc38c51f4e`

This addendum records the two implementation-order clarifications found during review of the work order against the real `main` codebase.

If any wording in the original work order conflicts with this addendum, **this addendum is authoritative**.

The original problem statement, hardware evidence, Full1902 authority boundaries, PID1902/HidHide/VIIPER invariants, PR7/PR8/PR10 reuse requirements, rumble safety requirements, anti-overengineering constraints, tests, and acceptance criteria remain unchanged except where explicitly overridden below.

---

# A. Override original §5.1 — use a host-local suspend-participant adapter as the default implementation

The original §5.1 preferred making `AddonProcessHost` directly implement `IPowerSuspendParticipant`.

Do **not** use that as the default implementation.

Use one **small host-local adapter** instead.

Reason:

- the `PowerTransitionCoordinator` participant collection is fixed when `AddonRuntimeHost` is constructed;
- `_physicalOwnership` and `_presentationOwnership` are runtime-owned fields populated later by the Disabled-mode controller startup path;
- the participant therefore must tolerate the legitimate state where the host exists but Full1902 ownership/presentation has not committed or is unavailable;
- a tiny adapter keeps this lifecycle seam explicit without turning `AddonProcessHost` itself into a generic power participant or adding a mutable participant registry.

This is a wiring/lifecycle clarification only. It is **not** permission to add another power/controller authority.

## A.1 Required shape

Keep the adapter local to the existing host/runtime composition boundary. A private nested type in `AddonProcessHost` or an equivalently narrow internal type is acceptable.

Conceptual example:

```csharp
private sealed class Full1902SuspendParticipant : IPowerSuspendParticipant
{
    private readonly Func<CancellationToken, Task<bool>> _quiesce;

    internal Full1902SuspendParticipant(Func<CancellationToken, Task<bool>> quiesce)
        => _quiesce = quiesce;

    public string Name => "Full1902ControllerPresentation";

    public Task<bool> QuiesceForSuspendAsync(
        DateTimeOffset deadline,
        long cycle,
        long epoch,
        CancellationToken cancellationToken)
        => _quiesce(cancellationToken);
}
```

The exact syntax/type location is implementation choice. Keep it this small.

Do **not** add:

- a participant registry;
- dynamic registration/unregistration;
- a new coordinator;
- a new authority enum;
- a reusable power-participant framework;
- ownership snapshots or epochs beyond the existing power coordinator contract.

## A.2 Host-side quiesce callback must null-guard current ownership

The adapter calls one narrow host method/callback that reads the current process-owned fields at execution time.

Conceptually:

```csharp
private Task<bool> QuiesceFull1902PresentationForSuspendAsync(
    CancellationToken cancellationToken)
{
    if (Volatile.Read(ref _processShutdownStarted) != 0)
        return Task.FromResult(true);

    var presentation = _presentationOwnership;

    if (presentation is null)
        return Task.FromResult(true);

    return presentation.PauseForSuspendAsync(cancellationToken);
}
```

The real implementation may also use the existing startup/authority fact where needed, but it must remain conservative and simple.

Required semantics:

```text
stock / non-Full1902 runtime
→ no Full1902 presentation mutation
→ participant is effectively a no-op success

Center M Disabled but controller startup has not committed a presentation
→ no attach/detach/recovery attempt from suspend callback
→ no-op success

Full1902 presentation exists
→ call existing presentation owner PauseForSuspendAsync
```

A missing presentation is not itself a suspend failure. It can be a legitimate fail-closed/startup-failed state.

## A.3 Construction/wiring rule

Create the adapter from `AddonProcessHost` during runtime composition and pass that fixed instance through the minimal optional participant seam described by the original work order.

The adapter must read `_presentationOwnership` only when the suspend callback actually runs; do not capture the field's startup-time value.

Conceptually:

```csharp
var full1902SuspendParticipant =
    new Full1902SuspendParticipant(QuiesceFull1902PresentationForSuspendAsync);

AddonRuntimeCompositionFactory.Create(
    ...,
    full1902SuspendParticipant: full1902SuspendParticipant);
```

`AddonRuntimeHost` still receives a fixed participant collection. Do not make it mutable after construction.

Also preserve the current startup ordering: production power observation begins only through the existing `StartPowerObservation()` path. Do not start power observation earlier merely to support this feature.

---

# B. Override original §11.1 — Win+G fail-close guard must pass before releasing suspend pause

The original §11.1 pseudocode placed the suspend-release pre-step before the existing Win+G suppression guard.

Change that ordering.

This is required because the current Full1902 Policy B contract says a live virtual presentation must not be restored while native Win+G suppression is not proven armed.

If code clears `_suspendPaused` first and only afterward discovers `_winGSuppressionGuard.IsArmed == false`, the method can return with:

```text
suspend pause cleared
publisher still stopped
no presentation reconcile performed
no guaranteed future trigger
```

That is an avoidable inconsistent lifecycle state.

The correct rule is simpler:

> **Do not release the suspend pause until all existing preconditions that permit live Full1902 presentation mutation have already passed.**

## B.1 Required ordering in `ReconcileControllerPresentationAsync`

Preserve the current source check and current Full1902 Win+G suppression fail-close guard first.

Then perform the suspend-release pre-step.

Required shape:

```text
source = physical.LiveInputSource

if source is null / not running:
    leave suspend pause active
    return

if Win+G suppression is not armed:
    leave suspend pause active
    leave publisher stopped / neutral
    return

if presentation.IsSuspendPaused:
    source.ResetLatestStateToNeutral()

    resume = presentation.ResumeAfterSuspendAsync(
        source,
        fresh presentation snapshot,
        token)

    if resume remains blocked / paused / unsafe:
        return

continue existing ReconcileDesiredPresentationAsync(...)
```

Equivalent C# organization is fine, but this semantic ordering is mandatory.

## B.2 Why the guard comes first

Normal Disabled steady-state should already have `IsArmed == true`, but the guard is an explicit Full1902 fail-close boundary and must remain authoritative during abnormal lifecycle conditions too.

Therefore:

```text
Resume observed
→ physical source healthy
→ Win+G guard NOT armed
→ DO NOT clear suspend pause
→ DO NOT restart publisher
→ remain neutral / fail-closed
```

Do not add an automatic retry loop just to recover this abnormal state.

Current product policy already treats failed Win+G suppression as a fail-closed controller-presentation condition. A normal Runtime restart is the existing retry boundary.

## B.3 `ResetLatestStateToNeutral()` placement

Call `ResetLatestStateToNeutral()` only **after** the source-running and Win+G guards pass, and immediately before `ResumeAfterSuspendAsync(...)` is allowed to restart or release game-facing publication.

This preserves the intended invariant:

```text
all live-presentation safety guards proven
→ erase potentially stale pre-suspend LatestState
→ presentation owner decides same-presentation restart vs PR7 switch
→ first resumed publication cannot replay the old pre-suspend snapshot
```

Do not neutralize the snapshot and then wait behind unrelated 2.5 s performance-profile work.

Do not emit `StateChanged` from the reset seam.

---

# C. Interaction with PR8/PR10 physical recovery remains unchanged

The corrected guard ordering must also apply when the existing recovery flow re-enters the same reconcile method:

```text
Resume notification
→ physical source unavailable
→ suspend pause remains active / output neutral
→ existing PR8/PR10 recovery owns physical repair
→ RequestControllerPresentationReconcile("PhysicalInputRecovered")
→ source is now live
→ existing Win+G guard passes
→ ResetLatestStateToNeutral()
→ ResumeAfterSuspendAsync(...)
→ existing PR7 reconcile continues
```

Do not create a second Resume-specific physical recovery operation.

Do not bypass `_ownedControllerRecoveryBlockedByCleanup`.

Do not reacquire DirectInput directly from `OnPowerResumeObserved()`.

---

# D. Required tests added by this review

In addition to the original work-order tests, add focused regression tests for these two review findings.

## D.1 Participant adapter before Full1902 ownership commit

Prove:

```text
participant exists
_presentationOwnership == null
Suspend arrives
→ no exception
→ no attach/detach/native mutation
→ quiesce reports success/no-op
```

Also prove the adapter reads the **current** field at callback time rather than a captured startup value:

```text
adapter constructed while presentation == null
presentation assigned later
Suspend callback
→ assigned presentation receives PauseForSuspendAsync
```

No dynamic registration is required.

## D.2 Win+G guard blocks suspend release

Set up:

```text
presentation suspend-paused
source running
Win+G suppression not armed
```

Then run the existing controller presentation reconcile entrypoint.

Prove:

```text
ResetLatestStateToNeutral NOT called
ResumeAfterSuspendAsync NOT called
ReconcileDesiredPresentationAsync NOT called
suspend pause remains active
publisher remains stopped/neutral
```

This test is important even though the state is abnormal: it verifies the existing explicit Policy B fail-close guard cannot accidentally be bypassed by the new Resume path.

## D.3 Win+G guard passes, then release occurs

Set up:

```text
presentation suspend-paused
source running
Win+G suppression armed
```

Prove strict semantic order:

```text
source/guard validation
→ ResetLatestStateToNeutral
→ ResumeAfterSuspendAsync
→ normal PR7 reconcile when required
```

The test does not need instruction-level race machinery. Verify observable call order only.

---

# E. Updated implementation summary

After this addendum, the intended implementation is:

```text
POWER SUSPEND

existing PowerTransitionCoordinator
→ one fixed host-local Full1902SuspendParticipant
→ callback reads current _presentationOwnership with null guard
→ existing MsiClawAddonPresentation owner
→ stop/join publisher
→ clear synthetic system-button pulses
→ disarm + drain rumble callback
→ physical rumble STOP
→ write same attached virtual device neutral
→ keep VIIPER / typed attachment / PID1902 / DirectInput / HidHide ownership intact

POWER RESUME

PowerResumeObserved
→ RequestControllerPresentationReconcile("PowerResume") immediately
→ existing physical source check
→ existing Win+G suppression fail-close guard
→ ONLY NOW, if suspend-paused:
     ResetLatestStateToNeutral()
     ResumeAfterSuspendAsync(... fresh Steam/BPM snapshot ...)
→ existing PR7 presentation reconcile
→ existing delayed profile resume work remains independent
```

This remains intentionally small:

- one host-local adapter;
- one presentation-local `_suspendPaused` fact;
- one narrow physical snapshot-neutral reset seam;
- suspend/resume methods on the existing presentation owner;
- existing PR7/PR8/PR10 recovery and selection paths.

No new controller authority, routing runtime, power state machine, VIIPER owner, retry manager, epoch, or polling loop is allowed.
