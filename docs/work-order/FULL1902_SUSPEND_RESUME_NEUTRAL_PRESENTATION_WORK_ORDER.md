# Work Order — Full1902 Suspend/Resume Neutral Presentation Safety

> **Date:** 2026-09-04  
> **Status:** Implementation work order  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed production baseline:** `2e7885fc7bb85876d5ed3344c105a1a02eafe255`  
> **Baseline head:** `Full1902 production rumble feedback (#488)`

---

## 0. Purpose

Fix one real Full1902 Sleep/Hibernate/Resume lifecycle gap found by hardware validation on the latest production architecture.

The physical Full1902 ownership path itself is healthy:

```text
Center M Disabled
→ PID1902 remains owned
→ same DirectInput session can survive Sleep/Resume
→ HidHide target remains stable
→ canonical VIIPER remains attached
→ X360 / SteamDeck presentation still works after Resume
```

The defect is narrower:

> The current virtual presentation publisher is not a power-suspend participant. It can remain logically live until the machine actually sleeps and can begin publishing again when Windows execution resumes, before the Addon receives the later Resume notification.

Because each publisher continuously maps `IMsiClawPreparedInputSource.LatestState`, the publisher can potentially replay the last pre-suspend non-neutral snapshot during the wake-to-Resume-notification gap.

This work order must make the game-facing virtual output explicitly neutral across Suspend and keep it neutral until the existing Full1902 owner is allowed to resume publication.

Do this without changing the Full1902 authority model, without adding a new routing runtime, and without adding a new generalized power/controller state machine.

---

# 1. Hardware evidence that makes this a production issue

The `C:\GoogleDrive\Addon\Log\0904-resume` hardware run exercised two real Sleep/Resume cycles on the current Full1902 build.

## 1.1 Physical ownership survived correctly

Both cycles retained the same live PID1902 DirectInput session.

Observed behavior:

```text
before Sleep  → TestSession=1
Resume #1     → TestSession=1 continues
Resume #2     → TestSession=1 continues
```

There was no:

- PID1901 drift;
- PID1902 mode rewrite;
- DirectInput reacquire;
- HidHide target migration;
- physical PnP loss/re-enumeration;
- VIIPER recreation.

Therefore this PR must **not** introduce unconditional PID1902 switching, DirectInput restart, HidHide rewrite, or VIIPER teardown on every Resume.

## 1.2 SteamDeck publisher remained live through Suspend

During the second cycle, BPM/SteamDeck presentation was active.

Hardware log sequence:

```text
20:56:05.258  Suspend observed
20:56:05.259  Suspend quiesce completed, GateState=Closed
20:56:05.706  SteamDeck publisher heartbeat ~250 Hz
20:56:06.706  SteamDeck publisher heartbeat ~250 Hz
20:56:07.706  SteamDeck publisher heartbeat still present
```

So current:

```text
Suspend quiesce completed
```

does **not** mean:

```text
Full1902 virtual presentation quiesced
```

## 1.3 Publisher resumed before Resume notification

On wake:

```text
21:00:31.500  SteamDeck publisher already publishing again
21:00:35.496  ResumeSuspend notification observed
```

The publisher resumed roughly four seconds before the Addon received the Resume notification.

This is not a theoretical instruction-level race. It is the normal Windows handheld lifecycle observed on real hardware.

## 1.4 Last physical snapshot before Sleep was non-neutral

Immediately before the second Sleep, the physical snapshot included non-neutral stick state.

Therefore the current sequence can be:

```text
pre-suspend non-neutral LatestState
→ system sleeps
→ process execution resumes
→ publisher wakes before Resume notification
→ publisher reads the same LatestState
→ stale pre-suspend input may be exposed again
```

The logs do not prove a game character actually moved during the gap, but the complete production path that can replay stale input is present and was exercised on real hardware.

This is sufficient to treat the issue as a real lifecycle safety defect.

---

# 2. Current source root cause

## 2.1 `AddonRuntimeHost` has no suspend participant

Current `AddonRuntimeHost` constructs the power coordinator with:

```csharp
Array.Empty<IPowerSuspendParticipant>()
```

So the generic power coordinator can close its mutation gate and report `Suspended`, but nothing in the current Full1902 controller/presentation owner is asked to stop live publication or write neutral.

Do not reintroduce any of the removed legacy routing power participants.

The correct fix is one narrow Full1902 participant using the existing `IPowerSuspendParticipant` contract.

## 2.2 `MsiClawAddonPresentation` already contains the right safe primitive shape

The current Overlay capture path already proves the safe ordering required here:

```text
publisher StopAsync / join
→ clear feedback callback
→ drain in-progress physical rumble callback
→ physical rumble STOP
→ write SAME attached virtual device neutral
→ keep typed device attached
→ later restart SAME publisher when safe
```

This behavior exists in:

```text
MsiClawAddonPresentation.PauseForOverlayAsync(...)
MsiClawAddonPresentation.ResumeAfterOverlayAsync(...)
DisarmFeedbackAndStopLocked(...)
```

Suspend should reuse the same owner and the same lifecycle principles.

Do **not** create:

- `PowerPresentationManager`;
- `SuspendControllerManager`;
- a second virtual-device owner;
- a second VIIPER runtime;
- a second feedback authority;
- a new routing state machine.

## 2.3 Current Resume observer does not resume Full1902 presentation

`AddonProcessHost.OnPowerResumeObserved()` currently schedules only the delayed CPU Boost / Power Mode reconcile.

It does not currently perform a Full1902 presentation Resume operation.

The existing `PowerTransitionCoordinator` intentionally emits `PowerResumeObserved` before its Disabled-authority generic `RemainPassive` fail-close.

Preserve that design.

Do **not** fix this by setting generic `recoveryEnabled=true` for Full1902.

That would incorrectly route Center M Disabled / Addon authority through the stock PID1901 recovery baseline.

---

# 3. Product invariants to preserve

This PR must preserve all current Full1902 authority decisions.

## 3.1 Physical controller authority

```text
Center M Disabled
→ Addon owns PID1902
→ DirectInput is process-owned
→ HidHide baseline is persistent Addon state
```

Sleep/Resume does not release that authority.

## 3.2 Virtual presentation

```text
Steam/BPM inactive → Xbox360
Steam/BPM active   → SteamDeck
```

Sleep/Resume must not invent a third presentation kind.

## 3.3 VIIPER lifetime

A normal Sleep must not destroy and recreate the canonical VIIPER runtime or typed devices.

Preferred healthy path:

```text
attached X360/Deck
→ stop publisher
→ neutral same attached device
→ Sleep
→ Resume
→ restart same publisher if still desired and structurally healthy
```

## 3.4 Physical DirectInput lifetime

A healthy DirectInput session that survived Resume must stay alive.

Do not perform unconditional:

```text
StopAsync
Unacquire
CreateDevice
Acquire
PID1902 rewrite
HidHide rewrite
```

on every Resume.

If the physical session actually dies, reuse the existing PR8/PR10 recovery path.

## 3.5 Generic power gate

The existing Full1902 behavior where generic stock recovery remains `RemainPassive` is intentional.

The new controller Resume path is hosted by the existing Full1902 owner outside the legacy stock baseline.

---

# 4. Required architecture

Use exactly the current owners:

```text
PowerTransitionCoordinator
        |
        | existing IPowerSuspendParticipant
        v
AddonProcessHost
        |
        v
MsiClawAddonPresentation
        |
        +-- existing publisher
        +-- existing VIIPER typed device/session
        +-- existing rumble callback lifetime

Resume notification
        |
        v
AddonProcessHost.OnPowerResumeObserved
        |
        v
existing RequestControllerPresentationReconcile(...)
        |
        v
existing Full1902 presentation owner + PR7 policy
```

Do not add a new long-lived controller/power authority object.

---

# 5. Reuse `IPowerSuspendParticipant`

`IPowerSuspendParticipant` already exists and is exactly the required coordinator contract.

Current production simply passes an empty participant array.

## 5.1 Preferred minimal wiring

Prefer making `AddonProcessHost` itself the one Full1902 suspend participant, because it already owns:

```text
_physicalOwnership
_presentationOwnership
_overlay capture coordination
Full1902 authority expectation
process shutdown state
```

Conceptually:

```csharp
internal sealed class AddonProcessHost : IAsyncDisposable, IPowerSuspendParticipant
{
    public string Name => "Full1902ControllerPresentation";

    public Task<bool> QuiesceForSuspendAsync(
        DateTimeOffset deadline,
        long cycle,
        long epoch,
        CancellationToken cancellationToken)
    {
        ...
    }
}
```

If direct interface implementation creates an actual compile/layering problem, one **small host-local adapter** is acceptable.

Do not create a reusable participant framework.

## 5.2 Runtime composition wiring

Extend the current production composition only enough to pass one optional participant into `AddonRuntimeHost`.

Conceptually:

```csharp
AddonRuntimeCompositionFactory.Create(
    ...,
    IPowerSuspendParticipant? full1902SuspendParticipant = null)
```

and:

```csharp
new AddonRuntimeHost(
    ...,
    suspendParticipant: full1902SuspendParticipant)
```

`AddonRuntimeHost` should then construct:

```csharp
full1902SuspendParticipant is null
    ? Array.Empty<IPowerSuspendParticipant>()
    : [full1902SuspendParticipant]
```

Do not make participant registration mutable after startup.

Do not add a dynamic participant registry.

---

# 6. Add one suspend-pause fact to `MsiClawAddonPresentation`

The presentation owner needs one process-memory-only fact, for example:

```csharp
private bool _suspendPaused;
```

This is **not** controller authority and is never persisted.

It only means:

> Live game-facing publication is intentionally blocked because the current power cycle entered Suspend and has not yet been safely released.

Expose only the smallest read seam needed by the host/tests, for example:

```csharp
bool IsSuspendPaused { get; }
```

Do not add:

- an epoch;
- a suspend token object;
- a pause stack;
- a generalized pause-reason manager;
- a new state-machine class.

The existing `_gate` remains the serialization authority.

---

# 7. Add `PauseForSuspendAsync`

Add a narrow operation to `IMsiClawAddonPresentation`, conceptually:

```csharp
Task<bool> PauseForSuspendAsync(CancellationToken cancellationToken);
```

A small result enum/record is acceptable if tests/logs materially benefit from distinguishing failure reasons, but do not build a broad result hierarchy.

## 7.1 Required ordering

Under the existing presentation `_gate`:

```text
1. mark suspend pause active
2. stop + JOIN the current publisher if one is running
3. clear pending Steam/QuickAccess synthetic pulses
4. disarm current rumble feedback callback
5. drain any in-progress rumble physical write through existing bridge Dispose()
6. request physical rumble STOP through existing DisarmFeedbackAndStopLocked(...)
7. write SAME currently-attached virtual device neutral
8. keep the typed device attached when neutral succeeds
```

Do not detach/recreate a healthy typed device merely because Windows is going to sleep.

## 7.2 No active presentation

If Full1902 authority exists but no presentation is currently attached:

```text
_suspendPaused = true
→ no native attach/detach
→ return success
```

This still blocks a Steam/BPM event from attaching a new live presentation after the suspend barrier.

## 7.3 Publisher stop failure

If `StopAsync()` cannot prove the publisher stopped/joined:

```text
_suspendPaused remains true
→ do not write neutral underneath a possibly-live publisher
→ return false
```

Let the existing `PowerTransitionCoordinator` classify suspend quiesce as failed/unsafe.

Do not attempt an unsafe detach underneath a publisher that may still call `SetState`.

## 7.4 Neutral rejection

If the publisher is proven stopped but the same attached device rejects neutral:

reuse the existing presentation fail-close behavior:

```text
retire current presentation through the existing owner
→ no alternate-presentation fallback
```

Do not write a sleep-specific rollback path.

## 7.5 Synthetic Steam/QAM pulses

Call:

```csharp
_systemButtonOverlay.Clear();
```

once the publisher is stopped.

A pre-suspend Steam or QuickAccess pulse must never survive Sleep and assert after Resume.

---

# 8. Block live presentation mutations while suspend-paused

While `_suspendPaused == true`:

## 8.1 Runtime X360/SteamDeck reconcile

`ReconcileDesiredPresentationAsync(...)` must return a blocked/no-forward-mutation result.

Do not attach, detach, or restart a publisher until the suspend pause is explicitly released.

Use a reason such as:

```text
SuspendPaused
```

## 8.2 Synthetic Steam/QuickAccess pulse

`TryRequestSteamPulse()` and `TryRequestQuickAccessPulse()` must return `false` while suspend-paused.

## 8.3 Overlay capture

Do not create overlapping pause authority.

Required interaction:

```text
Overlay already paused
→ Suspend may add _suspendPaused
→ presentation stays stopped + neutral

Suspend already paused
→ new Overlay pause request must not restart/mutate game-facing publication
```

`PauseForOverlayAsync(...)` should refuse a new capture-side publication transition while suspend-paused, unless the implementation can prove the operation is a no-op over an already-neutral presentation.

Prefer the simpler explicit block.

---

# 9. Neutralize the physical snapshot at the Resume release boundary

Stopping the publisher during Sleep removes the early-wake output problem, but the publisher still consumes:

```text
IMsiClawPreparedInputSource.LatestState
```

Do not rely on a timing delay such as:

```text
wait 100 ms and assume DirectInput refreshed
```

Do not add a persistent polling loop or a new read-generation/epoch system for this PR.

The simpler safe solution is to explicitly neutralize the published snapshot immediately before live publication is allowed to restart.

## 9.1 Add one narrow source seam

Extend `IMsiClawPreparedInputSource` with a small operation such as:

```csharp
void ResetLatestStateToNeutral();
```

Production implementation in `MsiClawInputSource` should do only the existing snapshot operation:

```csharp
Volatile.Write(ref _latestState, new StateBox(NeutralState()));
```

Do **not**:

- stop the DirectInput session;
- reset `TestSession`;
- increment physical `CurrentSessionGeneration`;
- reacquire the device;
- emit fake hardware `StateChanged` transitions;
- mutate M1/M2 diagnostics;
- clear physical identity.

The existing poll loop already writes `_latestState` on **every successful DirectInput read**, even when the mapped state equals the previous state and no `StateChanged` event is raised.

Therefore the safe steady behavior becomes:

```text
Resume release
→ LatestState explicitly neutral
→ publisher starts
→ output is neutral until the next successful physical read
→ next normal 8 ms DirectInput poll writes fresh current state
→ live output naturally resumes
```

This avoids replaying a pre-suspend snapshot without requiring an unconditional DirectInput restart.

---

# 10. Add `ResumeAfterSuspendAsync`

Add one matching narrow presentation operation.

Conceptually:

```csharp
Task<...> ResumeAfterSuspendAsync(
    IMsiClawPreparedInputSource source,
    Func<SteamPresentationSnapshot> captureSnapshot,
    CancellationToken cancellationToken);
```

The exact result type is implementation choice; keep it small.

The method must run under the existing presentation `_gate`.

## 10.1 Source unavailable

If the current physical source is null/not running:

```text
keep _suspendPaused = true
keep virtual output stopped/neutral
return without forward presentation mutation
```

Do not create a new recovery loop.

The existing PR8/PR10 physical recovery remains authoritative.

## 10.2 Fresh Steam/BPM policy

Capture the current presentation policy at the actual resume mutation boundary, using the same current `SteamPresentationSnapshot` authority already used by PR7.

Do not cache the pre-sleep desired presentation.

## 10.3 Same presentation still desired

If:

```text
active kind == fresh desired kind
AND source is running
AND canonical VIIPER is Ready
AND current typed device is still proven attached
AND Overlay is not holding its own pause
```

then:

```text
clear suspend pause
→ restart SAME publisher object
→ re-arm rumble feedback for SAME presentation
→ no detach
→ no attach
→ no VIIPER recreation
```

This is the preferred healthy hardware path.

## 10.4 Desired presentation changed during Sleep

Example:

```text
Sleep in SteamDeck/BPM
→ BPM exits while sleeping / state refresh resolves X360
→ Resume
```

Do not briefly restart the old SteamDeck publisher merely to switch it immediately afterward.

Required behavior:

```text
fresh desired != active kind
→ clear suspend pause
→ leave current old presentation publisher stopped + neutral
→ return "reconcile required"
→ existing PR7 ReconcileDesiredPresentationAsync performs normal retirement/switch
```

The PR7 path must remain the only X360/SteamDeck selection policy.

## 10.5 Overlay already owns neutral pause

If Overlay capture was already active before Sleep:

```text
_suspendPaused may be cleared after Resume safety is established
_overlayPaused remains authoritative for the visible Overlay session
publisher stays stopped
```

Do not restart game-facing publication underneath an active Overlay capture.

Later normal `ResumeAfterOverlayAsync(...)` remains responsible for ending the Overlay pause.

## 10.6 Structural attachment is not proven

If the typed device is no longer proven attached on Resume:

```text
never restart publisher against it
→ leave output neutral/stopped
→ return a result that lets the existing presentation reconcile/fail-close policy run
```

Do not add a new sleep-only VIIPER attachment repair algorithm in this PR.

---

# 11. Resume orchestration must reuse existing PR7 / PR8 / PR10 flow

`AddonProcessHost.OnPowerResumeObserved()` currently starts only delayed profile reconciliation.

Change it so Full1902 controller presentation reconcile is requested immediately, before the unrelated 2.5 s CPU Boost / Power Mode delay.

Conceptually:

```csharp
private void OnPowerResumeObserved()
{
    if (shuttingDown) return;

    RequestControllerPresentationReconcile("PowerResume");

    _ = ReconcilePerformanceAfterResumeAsync(...); // existing delayed profile path
}
```

Do not make controller Resume wait 2.5 seconds for profile work.

## 11.1 Extend the existing reconcile entrypoint, do not add another owner

Inside the existing:

```text
ReconcileControllerPresentationAsync(...)
```

add the smallest suspend-release pre-step.

Conceptually:

```text
source = physical.LiveInputSource

if presentation.IsSuspendPaused:
    if source is not running:
        leave suspend pause active
        return

    source.ResetLatestStateToNeutral()

    resume = presentation.ResumeAfterSuspendAsync(
        source,
        fresh presentation snapshot,
        token)

    if resume says still blocked/paused:
        return

continue existing Win+G guard
continue existing ReconcileDesiredPresentationAsync(...)
```

This is important because the same method is already called after successful physical recovery:

```text
RequestControllerPresentationReconcile("PhysicalInputRecovered")
```

Therefore:

```text
Resume notification while physical source is healthy
→ PowerResume reconcile releases suspend pause immediately

Resume notification while physical source is dead
→ suspend pause stays neutral
→ existing PR8/PR10 recovery repairs physical source
→ existing PhysicalInputRecovered reconcile reaches the same suspend-release pre-step
→ then normal presentation reconcile continues
```

No separate resume recovery manager is needed.

## 11.2 Preserve prior cleanup safety

A Resume-triggered path must not bypass:

```text
_ownedControllerRecoveryBlockedByCleanup
```

Do not invent a direct physical reacquire from `OnPowerResumeObserved()`.

If recovery is necessary, the already-existing PR8/PR10 event/recovery path remains authoritative.

---

# 12. Rumble requirements from current main (#488)

Current `main` includes Full1902 production rumble feedback and callback-drain safety.

Suspend must integrate with that current architecture.

Required Suspend ordering after publisher stop/join:

```text
feedback callback unregister
→ drain callback already inside physical write
→ final physical STOP
→ virtual neutral
```

Reuse:

```csharp
DisarmFeedbackAndStopLocked("Suspend")
```

or the smallest equivalent reuse of the existing helper.

Do not duplicate callback-drain logic.

On successful same-presentation Resume:

```text
publisher restarted
→ feedback callback re-armed through existing ArmFeedbackForActivePresentationLocked()
```

A callback registration failure remains rumble-feature-local and must not tear down controller input.

---

# 13. Failure policy

## 13.1 Publisher cannot stop

```text
Suspend pause remains active
→ do not neutral-write underneath live publisher
→ participant reports failure
→ existing PowerTransitionCoordinator marks unsafe
```

No unsafe detach.

## 13.2 Neutral write rejected

```text
publisher proven stopped
→ neutral rejected
→ use existing presentation fail-close retirement
```

No alternate device fallback.

## 13.3 Physical source is gone at Resume

```text
keep presentation stopped/neutral
keep suspend pause active
→ existing physical recovery owns repair
```

## 13.4 Resume publisher restart fails

```text
leave output neutral/stopped
→ clear/retain suspend pause according to the minimal result contract needed to allow existing PR7 reconcile
→ existing PR7 owner performs any normal retirement/reattach attempt
```

Do not create retry loops.

## 13.5 Generic power `RemainPassive`

The log:

```text
Resume recovery is disabled because this process did not establish a safe startup boundary.
Action=RemainPassive
```

is not itself the defect in Full1902.

Do not remove it or set generic recovery safe merely to hide the warning.

Full1902 controller resume is intentionally handled through `PowerResumeObserved` and the Addon-owned presentation path.

---

# 14. Logging

Keep logging transition-based only.

No per-poll or per-frame Resume logging.

Recommended events:

```text
ControllerPresentation
  PresentationSuspendPauseStarted
  PresentationSuspendPausedNeutral
  PresentationSuspendPauseFailed
  PresentationResumeRequested
  PresentationResumeDeferredSourceUnavailable
  PresentationResumeSamePublisher
  PresentationResumeReconcileRequired
  PresentationResumeLeftNeutral
```

Include useful fields where available:

```text
Presentation
OverlayPaused
PublisherWasRunning
Reason
Trigger
```

The existing publisher heartbeat remains sufficient to validate that publication stopped during Sleep and resumed afterward.

Do not add a second periodic heartbeat.

---

# 15. Expected production files

Likely production touch set:

```text
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Runtime/AddonRuntimeHost.cs
src/SteamInputAddonforClaw/Runtime/AddonRuntimeComposition.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputContracts.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputSource.cs
```

Tests will likely touch:

```text
tests/SteamInputAddonforClaw.Tests/MsiClawAddonPresentationTests.cs
tests/SteamInputAddonforClaw.Tests/PowerTransitionTests.cs
tests/SteamInputAddonforClaw.Tests/AddonProcessHostResumeTests.cs
relevant MsiClawInputSource tests
```

Do not broaden the PR because nearby power/legacy code looks old.

---

# 16. Required tests

## 16.1 Presentation — SteamDeck suspend

Given a live SteamDeck presentation:

```text
PauseForSuspendAsync
→ publisher StopAsync called and proven stopped
→ synthetic system-button state cleared
→ feedback callback disposed/drained
→ physical rumble STOP requested
→ SteamDeck SetNeutral succeeds
→ device remains attached
→ active kind remains SteamDeck
→ suspend pause=true
```

## 16.2 Presentation — Xbox360 suspend

Same contract for Xbox360:

```text
publisher stopped
→ feedback disarmed + STOP
→ SetXbox360State(default)
→ X360 remains attached
→ suspend pause=true
```

## 16.3 No live mutation while suspended

While `IsSuspendPaused`:

```text
ReconcileDesiredPresentationAsync → Blocked: SuspendPaused
TryRequestSteamPulse             → false
TryRequestQuickAccessPulse       → false
```

No native attach/detach.

## 16.4 Same-presentation Resume

Given:

```text
suspend-paused SteamDeck
fresh desired = SteamDeck
source running
attachment still Attached
Overlay not paused
```

Resume must:

```text
restart SAME publisher
re-arm feedback
clear suspend pause
0 detach
0 attach
```

Repeat for X360.

## 16.5 Desired kind changed while sleeping

Given:

```text
active SteamDeck was suspend-paused
fresh desired = Xbox360
```

Resume must **not** restart SteamDeck.

It must leave the old publisher stopped/neutral, release the suspend block for normal policy reconciliation, and let the existing PR7 switch perform the actual Deck→X360 transition.

## 16.6 Overlay + Suspend interaction

Cover both orders:

```text
Overlay paused → Suspend → Resume → Overlay still owns neutral pause
```

and:

```text
Suspend paused → Overlay pause request
```

No path may restart the publisher while either safety pause still requires neutral output.

## 16.7 Input snapshot reset

`ResetLatestStateToNeutral()` must:

```text
make LatestState neutral
keep IsRunning unchanged
not complete/stop the session
not change physical session generation
```

Then prove the next successful normal DirectInput read writes the current mapped state back into `LatestState` even if no `StateChanged` event is required.

## 16.8 Resume host ordering

`OnPowerResumeObserved()` must request controller presentation reconcile immediately.

The existing 2.5 s CPU Boost / Power Mode delay must remain profile-only.

## 16.9 Physical source unavailable on Resume

Given suspend pause + unavailable physical source:

```text
PowerResume reconcile
→ publisher remains stopped
→ output remains neutral
→ suspend pause remains active
→ no direct Acquire/PID/HidHide mutation
```

After existing physical recovery provides a live source and calls:

```text
RequestControllerPresentationReconcile("PhysicalInputRecovered")
```

the same reconcile path must release the suspend pause and restore presentation safely.

## 16.10 Rumble callback in progress during Suspend

Reuse the current #488 blocked-callback test technique.

Prove:

```text
callback already inside physical write
→ Suspend pause clears native callback
→ waits/drains callback
→ final physical STOP is the last write
→ no non-zero rumble lands after STOP
```

Do not add another feedback lock.

## 16.11 Power coordinator

Prove the supplied Full1902 participant is invoked on Suspend and its result contributes to the existing `Suspend quiesce completed` outcome.

Preserve the existing test that a Full1902 authoritative Resume still emits `PowerResumeObserved` even when generic `recoveryEnabled=false`.

---

# 17. Hardware validation after implementation

Repeat on a real MSI Claw with Center M Disabled / Full1902 ownership.

## 17.1 X360 idle/normal mode

```text
X360 active
→ Sleep
→ Resume
```

Expected:

```text
same PID1902 physical identity
same DirectInput TestSession when hardware keeps it alive
no mandatory reacquire
X360 typed device may remain attached
publisher stops before Sleep
publisher resumes only through Addon Resume reconcile
```

## 17.2 SteamDeck/BPM mode

```text
BPM active
→ SteamDeck live
→ move stick to a non-neutral state
→ release/leave a clearly known last state
→ Sleep
→ Resume
```

Expected log ordering:

```text
Suspend observed
→ PresentationSuspendPausedNeutral
→ Suspend quiesce completed
→ [sleep interval: no publisher heartbeat / no SetState publication]
→ Resume observed
→ PowerResume presentation reconcile
→ source snapshot reset neutral
→ same publisher restarted if SteamDeck still desired
→ publisher heartbeat returns
```

The key regression test is:

> There must be no period after OS execution resumes where the pre-suspend publisher is already streaming again before the Addon Resume release path runs.

## 17.3 Physical session survival

If the device again preserves the existing DirectInput session:

```text
TestSession remains the same
```

This is the preferred result.

Do not treat lack of reacquire as missing recovery.

## 17.4 Actual device-loss variant

Separately test one real device-loss/re-enumeration path if convenient.

Expected:

```text
presentation stays neutral
→ existing PR8/PR10 recovery repairs physical source
→ PhysicalInputRecovered reconcile releases suspend pause / reconciles presentation
```

No new sleep-specific recovery path should appear in logs.

## 17.5 Rumble

If rumble is active immediately before Sleep:

```text
Suspend
→ physical STOP
→ no latched rumble during/after Resume
→ feedback re-arms only after live presentation resumes
```

---

# 18. Explicit anti-overengineering constraints

This PR is fixing one observed handheld lifecycle defect.

Do **not** add:

```text
PowerControllerManager
PresentationPowerStateMachine
SuspendEpoch
ResumeEpoch
publication generation barriers
new watchdog
new periodic polling
new retry service
new process
new Windows service
new VIIPER wrapper layer
new feedback authority
new physical controller authority
new HidHide owner
```

Do not defend against arbitrary instruction-level interleavings that were not demonstrated in the supported product lifecycle.

Required safety comes from the existing owner gates and the observed lifecycle:

```text
existing PowerTransitionCoordinator serialization
+ existing MsiClawAddonPresentation _gate
+ publisher stop/join
+ neutral write
+ existing rumble callback drain/STOP
+ one in-memory suspend pause bool
+ source snapshot neutral reset at Resume
+ existing PR7/PR8/PR10 reconciliation
```

That is sufficient for the real issue found in hardware logs.

---

# 19. Non-goals

Do not include unrelated work:

- publisher 250 Hz optimization;
- prior 0904 CPU-starvation investigation;
- TDP Resume policy changes;
- QamHost reinjection changes;
- Center M Enable flow changes;
- HidHide policy changes;
- PID1901/PID1902 transition changes;
- VIIPER ABI changes;
- rumble protocol redesign;
- Overlay UI behavior changes;
- new controller settings UI;
- startup-task changes.

---

# 20. Acceptance criteria

The PR is complete only when all of the following are true.

### Suspend safety

- [ ] Full1902 presentation participates in the existing power suspend quiesce.
- [ ] Publisher is stopped/joined before Suspend quiesce reports success.
- [ ] Current virtual device is neutral before Suspend quiesce reports success.
- [ ] Healthy typed device remains attached; no routine Sleep detach/recreate.
- [ ] Synthetic Steam/QuickAccess pulses are cleared.
- [ ] Production rumble callback is disarmed/drained and physical STOP requested.
- [ ] Steam/BPM presentation reconcile cannot restart/attach while suspend-paused.

### Resume safety

- [ ] `OnPowerResumeObserved()` requests controller presentation reconcile immediately.
- [ ] The 2.5 s performance-profile delay does not delay controller Resume.
- [ ] `LatestState` is reset to neutral immediately before suspend publication is released.
- [ ] Healthy surviving DirectInput sessions are reused without unconditional reacquire.
- [ ] Same desired presentation restarts the same publisher without detach/attach.
- [ ] Changed desired presentation is handled by existing PR7 switching only.
- [ ] Unavailable physical source leaves presentation neutral and relies on existing PR8/PR10 recovery.
- [ ] `PhysicalInputRecovered` uses the same reconcile path to finish Resume safety.

### Ownership / architecture

- [ ] Generic Full1902 `RemainPassive` stock-recovery behavior is preserved.
- [ ] No new controller authority/state manager is introduced.
- [ ] PID1902/HidHide ownership contracts are unchanged.
- [ ] Canonical VIIPER runtime ownership is unchanged.
- [ ] Current #488 rumble callback-drain semantics are reused, not duplicated.

### Verification

- [ ] Focused new/updated tests pass.
- [ ] Full test suite passes.
- [ ] Debug build passes.
- [ ] Release build passes.
- [ ] Real hardware Sleep/Resume validation confirms no publisher output between suspend-neutral commit and explicit Resume release.

---

# 21. Expected final lifecycle

Healthy Full1902 SteamDeck example:

```text
SteamDeck publisher live
        |
        v
Windows Suspend notification
        |
        v
Power barrier applied
        |
        v
Full1902 suspend participant
        |
        +-- presentation _suspendPaused = true
        +-- stop/join publisher
        +-- clear Steam/QAM pulse
        +-- disarm/drain rumble callback
        +-- physical rumble STOP
        +-- SAME SteamDeck device neutral
        |
        v
Suspend quiesce succeeds
        |
        v
Sleep
        |
        v
Windows execution wakes
        |
        | publisher is STILL stopped
        | virtual device remains neutral
        v
Resume notification
        |
        v
PowerResumeObserved
        |
        v
RequestControllerPresentationReconcile("PowerResume")
        |
        v
existing physical source still running?
        |
        +-- NO → stay neutral; PR8/PR10 owns recovery
        |
        +-- YES
             |
             +-- ResetLatestStateToNeutral()
             +-- capture fresh Steam/BPM desired kind
             |
             +-- same kind + attached
             |      → restart SAME publisher
             |      → re-arm rumble
             |
             +-- different kind
                    → keep old publisher stopped neutral
                    → existing PR7 switch
```

This is the target. Keep the implementation as close to this graph as the current source structure allows.
