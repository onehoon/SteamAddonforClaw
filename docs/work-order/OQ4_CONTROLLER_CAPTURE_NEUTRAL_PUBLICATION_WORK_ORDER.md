# Work Order — OQ4: Controller Capture + Neutral Virtual Publication

## Status

Focused Addon Quick Settings Overlay controller-input PR.

Label: `OQ4`

This is part of the Overlay track, not the numbered Full PID1902 implementation sequence.

Read together with:

- `docs/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/OQ3_A_MAIN_UI_OVERLAY_VISIBLE_SURFACE_COEXISTENCE_WORK_ORDER.md`
- `docs/work-order/PR6_FIRST_VIRTUAL_PRESENTATION_ATTACH_WORK_ORDER.md`
- `docs/work-order/PR7_RUNTIME_XBOX360_STEAMDECK_PRESENTATION_SWITCHING_WORK_ORDER.md`
- current `main` implementations of:
  - `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`
  - `src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs`
  - `src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs`
  - `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
  - `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs`
  - `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputContracts.cs`
  - `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputSource.cs`
  - `src/SteamInputAddonforClaw/Input/IControllerStateSnapshotSource.cs`
  - `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalXbox360InputPublisher.cs`
  - `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckInputPublisher.cs`
  - `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckOutputStage.cs`

---

## 1. Goal

Turn the already-working visual Overlay into a real controller-modal surface while Full PID1902 Addon authority is active.

Required behavior:

```text
current presentation = Xbox360 OR SteamDeck
        ↓
Addon Overlay Show succeeds
        ↓
SAME presentation remains attached
        ↓
its publisher stops
        ↓
that same attached virtual device is written neutral
        ↓
physical PID1902 ControllerState is consumed by Runtime for Overlay navigation
        ↓
NO controller input is published to the game / Steam QAM behind the Overlay
```

Close behavior:

```text
Overlay close requested
→ stop accepting new Overlay navigation
→ Hide/retire Overlay surface
→ keep current virtual presentation neutral
→ wait until Overlay-consumed controls are released
→ clear Overlay capture
→ restart the SAME presentation publisher
→ request one normal PR7 presentation reconcile from current Steam/BPM facts
```

Normal Overlay open/close must NOT:

- switch PID1902 ↔ PID1901;
- reacquire DirectInput;
- change HidHide;
- tear down VIIPER;
- detach the current Xbox360 / SteamDeck presentation;
- attach the other presentation;
- change Steam/BPM facts.

---

## 2. Frozen product correction — do NOT implement Steam-QAM visible-surface exclusion

The current product decision is now:

```text
Steam QAM may remain visible behind Addon Overlay.
```

Do NOT add an OQ3-B Steam-QAM close/visibility controller as part of this work.

Desired behavior:

```text
WING / Steam QAM path
→ Steam QAM may already be visible

OEM1 / future Addon Overlay path
→ Overlay appears above it
→ Overlay capture neutralizes game-facing virtual controller output
→ Steam QAM behind the Overlay receives no controller navigation
```

Pointer/touch behavior remains the current transient-surface behavior:

```text
pointer/touch inside Overlay
→ Overlay handles it

pointer/touch outside Overlay
→ existing DismissRequested path
→ Overlay retires
→ the underlying Steam QAM / game remains available afterward
```

Therefore:

- do not inspect Steam QAM visibility;
- do not close Steam QAM before Overlay Show;
- do not terminate/restart `QamHost.exe`;
- do not modify `.Qam` ownership;
- do not add a Steam UI polling loop;
- do not add a generalized visible-surface manager.

The Main UI ↔ Overlay mutual exclusion from OQ3-A remains unchanged.

---

## 3. Current code facts that define the implementation seam

### 3.1 One physical input source already exists

`IMsiClawPreparedInputSource` already extends `IControllerStateSnapshotSource` and exposes:

```csharp
ControllerState LatestState { get; }
event EventHandler<ControllerState>? StateChanged;
bool IsRunning { get; }
```

`MsiClawInputSource` is the existing PR5-owned PID1902 DirectInput source.

Important current behavior:

- DirectInput polling is currently 8 ms;
- `LatestState` is updated on every successful read;
- `StateChanged` fires only for the initial valid state and later actual state transitions;
- input-session teardown resets `LatestState` to neutral;
- no second DirectInput session is required or allowed for Overlay.

Use this SAME source.

### 3.2 One presentation owner already exists

`MsiClawAddonPresentation` is the Full PID1902 virtual-presentation owner.

It already owns:

```text
one CanonicalViiperRuntime
one active AddonPresentationKind
one matching publisher
one private _gate
```

Current publishers already support:

```text
Start()
StopAsync()   // stop + prove worker joined
IsRunning
```

Current attach paths already know how to write neutral:

```text
Xbox360   → CanonicalViiperRuntime.SetXbox360State(default)
SteamDeck → CanonicalSteamDeckSession.SetNeutral()
```

The older `CanonicalSteamDeckOutputStage.PausePresentationAsync()` already proves the desired pause ordering:

```text
stop publisher
→ write neutral
→ mark paused
```

OQ4 must move that concept into the ONE Full-1902 `MsiClawAddonPresentation` owner so it works for both Xbox360 and SteamDeck.

### 3.3 Existing OQ3-A ordering gate must be reused

`AddonProcessHost` already owns:

```csharp
_visibleSurfaceTransition
```

for Main UI ↔ Overlay ordering.

Reuse this same narrow gate for Overlay Show/capture/retire ordering.

Do NOT add:

- `OverlayCaptureManager`;
- `ControllerCaptureManager`;
- another cross-surface semaphore;
- epochs/generations/barriers;
- a generalized presentation state machine.

### 3.4 Current outside-click dismissal bypasses Runtime capture coordination

Today `OverlayProcessController` receives `NamedPipeOverlayServer.DismissRequested` and performs Hide internally.

That is acceptable before OQ4 but becomes incorrect once controller capture exists, because it would bypass:

```text
stop navigation
→ Hide
→ consumed-control release gate
→ presentation resume
```

OQ4 must route all Overlay-close reasons through the Runtime-owned capture retirement path.

---

## 4. Runtime capture authority

`AddonProcessHost` remains the Overlay-capture authority.

Use one simple in-memory capture fact, for example:

```csharp
private bool _overlayCaptureActive;
private OverlayControllerInputRouter? _overlayInputRouter;
```

This is not another controller authority.

The authority hierarchy remains:

```text
Center M Disabled
→ Addon Runtime owns physical controller

MsiClawAddonPresentation
→ owns selected virtual presentation

AddonProcessHost OverlayCapture
→ temporarily decides whether physical input feeds the game publisher or Overlay navigation
```

Do not persist Overlay capture.

Do not restore it across process restart.

---

## 5. Add pause/resume primitives to the existing Full-1902 presentation owner

Extend `IMsiClawAddonPresentation` / `MsiClawAddonPresentation` with a narrow Overlay pause/resume contract.

Exact result type is implementation choice, but it must communicate success/failure clearly.

Conceptually:

```csharp
Task<bool> PauseForOverlayAsync(CancellationToken token);
Task<bool> ResumeAfterOverlayAsync(IMsiClawPreparedInputSource source, CancellationToken token);
```

A small typed result with `Reason` is acceptable if it improves tests/logs.

Do not create a second presentation abstraction.

### 5.1 Pause — Xbox360

While holding the existing `_gate`:

```text
require active presentation == Xbox360
require publisher exists/running
        ↓
await publisher.StopAsync()
        ↓
prove publisher.IsRunning == false
        ↓
SetXbox360State(default)
        ↓
mark Overlay-paused
```

Normal pause must NOT call:

```text
DetachXbox360()
AttachXbox360()
```

and must not recreate VIIPER server/bus/device objects.

### 5.2 Pause — SteamDeck

While holding the same `_gate`:

```text
require active presentation == SteamDeck
require CanonicalSteamDeckSession Active
require publisher exists/running
        ↓
await publisher.StopAsync()
        ↓
prove publisher.IsRunning == false
        ↓
session.SetNeutral()
        ↓
mark Overlay-paused
```

Normal pause must NOT detach the SteamDeck device.

### 5.3 Pause failure policy

#### Publisher cannot be stopped/joined

```text
publisher StopAsync fails / publisher still running
→ DO NOT write neutral underneath a possibly-live publisher
→ report pause failure
→ leave normal presentation ownership intact
→ Runtime hides/does not commit Overlay capture
```

Do not detach a native device underneath a possibly-live publisher.

#### Publisher stopped but neutral write is rejected

This is a real fail-close boundary.

```text
publisher proven stopped
+ neutral write rejected
→ do not claim Overlay capture succeeded
→ fail closed the current active presentation through the existing presentation owner
→ no alternate presentation fallback inside the pause operation
```

It is acceptable to reuse the owner’s existing active-presentation retirement primitive here because the ordinary no-detach rule applies to successful Overlay pause, not to an actual output-safety failure.

After the failed Overlay open is retired, Runtime may request the existing PR7 reconcile once so current policy can attempt a clean presentation recovery.

No retry loop.

### 5.4 Reconcile while Overlay-paused

This is required.

A Steam game/BPM state change may happen while the Overlay is open.

`ReconcileDesiredPresentationAsync()` must not detach/switch the current presentation while Overlay capture is active/paused.

Required result:

```text
Overlay pause active
→ PresentationReconcileOutcome.Blocked (or equivalent no-mutation result)
→ Reason = OverlayCaptureActive
→ no attach
→ no detach
→ no publisher restart
```

Do not queue a deferred desired-presentation state inside the presentation owner.

The Runtime already has current Steam/BPM authority. After Overlay capture ends, request one normal existing reconcile using fresh facts.

### 5.5 Resume after Overlay

While holding the same `_gate`:

```text
Overlay pause active
→ clear the pause fact as part of ending capture
→ if physical source is healthy and current presentation is still structurally valid
     start the SAME publisher object
→ success = same presentation live again
```

Normal success must have:

```text
no AttachUSBDeviceEx
no DetachUSBDeviceEx
no VIIPER recreate
```

If the physical source is no longer running, or the same presentation can no longer safely resume:

```text
leave output neutral / publisher stopped
→ clear Overlay pause so normal controller recovery/reconcile is no longer blocked
→ return failure/blocked
```

Then existing physical recovery / PR7 reconcile remains the recovery authority.

If publisher restart throws:

```text
keep output neutral
→ do not pretend resume succeeded
→ allow the existing PR7 reconcile/fail-close path to repair or retire it
```

Do not invent a separate Overlay presentation-recovery manager.

### 5.6 Teardown/Center M release still wins

`ReleaseForCenterMEnableAsync()` and process teardown must remain able to retire a presentation even if it is currently Overlay-paused.

The current ordering remains:

```text
presentation retire/VIIPER teardown
→ physical DirectInput release
→ PID1901 stock restore
```

Overlay pause must never block explicit authority release or process shutdown.

---

## 6. Add one narrow semantic controller-input router

Add one small Runtime-side helper, e.g.:

```text
OverlayControllerInputRouter
```

This helper is NOT a DirectInput owner and NOT a generic input framework.

It receives the existing `IMsiClawPreparedInputSource` and listens to `StateChanged` only while capture is active.

Responsibilities only:

1. convert controller transitions to low-rate semantic Overlay actions;
2. stop accepting new actions during close;
3. detect release of the controls that were consumed by Overlay;
4. provide one source-unavailable signal for real DirectInput loss.

Do not move controller authority out of `MsiClawInputSource`.

### 6.1 Initial OQ4 semantic bindings

Keep the first mapping intentionally small:

```text
DPadUp    rising edge → NavigateUp
DPadDown  rising edge → NavigateDown
DPadLeft  rising edge → NavigateLeft
DPadRight rising edge → NavigateRight
A         rising edge → Accept
B         rising edge → Back
```

Do not implement in OQ4:

- analog-stick navigation;
- trigger navigation;
- shoulder section switching;
- hold-repeat timers;
- acceleration curves;
- debounce manager;
- gesture recognition.

OQ5 can extend UI behavior after the basic capture path is hardware-proven.

### 6.2 Edge behavior

When capture starts:

```text
previous = source.LatestState
```

Do not emit a navigation event merely because a button was already held when capture began.

Only a later false→true edge emits a semantic action.

Button release itself emits no navigation message.

### 6.3 Do not block the DirectInput poll thread

`StateChanged` is raised from the physical polling path.

The handler must only:

- compare state;
- update small in-memory facts;
- schedule low-rate semantic delivery.

Do not synchronously wait for named-pipe I/O from the DirectInput callback.

No raw 8 ms / 125 Hz state streaming over IPC.

---

## 7. Release-to-resume gate

The Overlay close input must not immediately leak into the game after publisher resume.

For OQ4, consumed controls are exactly the initial semantic controls:

```text
DPad Up/Down/Left/Right
A
B
```

The release gate does NOT need to wait for unrelated sticks/triggers/buttons that OQ4 does not consume.

Required close behavior:

```text
router.StopAcceptingNavigation()
        ↓
Hide/retire Overlay surface
        ↓
current presentation remains neutral
        ↓
if A/B/DPad are already all released
    continue immediately
else
    await StateChanged until all consumed controls are released
        ↓
dispose/unsubscribe router
        ↓
clear OverlayCapture
        ↓
resume same publisher
```

No polling timer.

No sleep loop.

No input epoch.

### 7.1 Real physical-source loss while waiting

`MsiClawInputSource` resets `LatestState` to neutral during teardown but does not emit a final `StateChanged` after that reset.

Therefore the release waiter must not depend only on a future state event when the DirectInput session is lost.

Reuse the existing real input-loss seam in `AddonProcessHost.OnOwnedControllerPhysicalInputCompleted(...)`:

```text
unexpected owned DirectInput completion
→ synchronously notify active OverlayControllerInputRouter that source is unavailable
→ any release waiter completes as SourceUnavailable
→ request Overlay retirement
→ DO NOT resume a publisher against the dead source
→ existing PR8/PR10 physical recovery continues
```

This is a real handheld lifecycle path and must be handled.

Do not add a polling source-health monitor.

---

## 8. Extend `.Overlay` transport with semantic navigation only

Current Overlay transport is protocol v2 and carries:

```text
Show / Hide / Shutdown
Ready / Visible / Hidden
DismissRequested
```

OQ4 adds Runtime → Overlay semantic navigation.

Bump:

```text
OverlayTransportProtocol.CurrentVersion
2 → 3
```

Conceptually add:

```csharp
internal enum OverlayWireMessageKind
{
    Handshake,
    HandshakeAccepted,
    Command,
    Navigation,
    State,
    DismissRequested,
    ProtocolError
}

internal enum OverlayNavigationAction
{
    NavigateUp,
    NavigateDown,
    NavigateLeft,
    NavigateRight,
    Accept,
    Back
}
```

and one optional navigation field on `OverlayWireMessage`.

Do NOT add `ControllerState`, buttons arrays, sticks, triggers, or raw reports to the wire contract.

### 8.1 Server delivery

Add one narrow operation such as:

```csharp
Task<bool> SendNavigationAsync(OverlayNavigationAction action, CancellationToken token = default)
```

Requirements:

- only send when the current Overlay connection is Ready/Visible;
- use the existing server write gate so command/navigation frames cannot interleave bytes;
- navigation has no acknowledgement round-trip;
- return false on disconnected/unavailable transport;
- no queue manager;
- no retry loop.

Low-rate edge events are enough.

### 8.2 Client handling

`NamedPipeOverlayClient.RunAsync(...)` should recognize both:

```text
Command
Navigation
```

with strict message-shape validation.

Commands retain current state acknowledgement behavior.

Navigation does not produce a `Visible/Hidden` acknowledgement.

---

## 9. Overlay process behavior for semantic actions

In `SteamInputAddonforClaw.Overlay/App.xaml.cs`:

- receive semantic navigation from the `.Overlay` client;
- marshal UI work through the existing `DispatcherQueue`;
- do not activate/focus the top-level HWND;
- do not synthesize keyboard input with `SendInput`;
- do not read XInput/GameInput/DirectInput locally.

OQ4 does not build the real Quick Settings control tree yet.

For this POC stage:

- `Navigate*` / `Accept` may be logged and optionally reflected in one simple diagnostic text field;
- `Back` at the current root POC surface should request the existing semantic Overlay dismissal (`DismissRequested`) so the complete capture → close → B-release → resume path can be hardware-tested.

Do not build OQ5 settings controls in this PR.

---

## 10. Move Overlay dismissal authority outward to Runtime

`OverlayProcessController` must remain the process/window/transport owner, but it must no longer independently finish a visible Hide on outside-click once OQ4 capture exists.

Refactor narrowly:

```text
NamedPipeOverlayServer.DismissRequested
→ OverlayProcessController validates current visible/current server
→ raise one narrow DismissRequested event/callback to AddonProcessHost
→ AddonProcessHost runs the unified Overlay retirement path
```

Do not keep the old internal path:

```text
DismissRequested
→ OverlayProcessController immediately Send Hide
```

because that would bypass release-to-resume.

### 10.1 Unexpected visible Overlay process loss

`OverlayProcessController` already observes process exit.

Expose one narrow signal when the current Overlay process/session dies while it was visible, e.g.:

```text
VisibleSessionLost
```

This exists only for the concrete crash/disconnect lifecycle:

```text
Overlay visible + capture active + Overlay process exits
→ Runtime stops navigation
→ no Hide required because surface is gone
→ wait consumed-control release if physical source still healthy
→ clear capture
→ resume same presentation
```

If physical input is also unhealthy, leave output neutral and let existing physical recovery own recovery.

Do not add a watchdog or Overlay heartbeat.

Intentional `StopCurrentAsync()` / normal shutdown must not be misclassified as an unexpected visible-session loss.

---

## 11. Unify every Overlay close reason in `AddonProcessHost`

Refactor OQ3-A coordination so all normal/real close reasons converge on one helper owned by `AddonProcessHost`.

Conceptually:

```text
RetireOverlayCaptureUnderTransitionAsync(reason, surfaceAlreadyGone)
```

The helper may assume `_visibleSurfaceTransition` is already held.

Close reasons include:

- tray/test toggle off;
- outside pointer/touch `DismissRequested`;
- controller `Back` → Overlay `DismissRequested`;
- Main UI open request;
- unexpected visible Overlay process loss;
- physical DirectInput loss.

Do NOT create one handler per reason with separate release logic.

### 11.1 Normal close ordering

While holding the existing `_visibleSurfaceTransition`:

```text
stop router accepting navigation
→ Hide/retire Overlay (unless surface already gone)
→ prove Overlay is no longer visible
→ keep presentation neutral
→ await consumed-control release
→ dispose router
→ clear _overlayCaptureActive
→ presentation.ResumeAfterOverlayAsync(current source)
→ request PR7 reconcile("OverlayCaptureReleased")
```

If the Overlay cannot be proven retired:

```text
DO NOT resume game-facing publisher
→ leave capture/neutral fail-safe
→ log feature-local failure
```

Do not prioritize UI convenience over input safety.

### 11.2 Main UI open path

Replace the OQ3-A direct `EnsureHiddenAsync()` step with the same unified retirement helper.

Required order:

```text
Overlay capture active
→ retire capture safely
→ only then launch/activate Main UI
```

When Overlay is not active, preserve current OQ3-A behavior.

---

## 12. Overlay open sequence

Update `CoordinateOverlayToggleAsync()` (or an equivalently narrow host method) to become capture-aware.

Required successful order:

```text
acquire existing _visibleSurfaceTransition
        ↓
if current Overlay capture/visible → run unified close and return
        ↓
retire Main UI through existing OQ3-A .Frontend CloseRequested path
        ↓
require current Full-1902 presentation owner
require current PR5 LiveInputSource and source.IsRunning
        ↓
OverlayProcessController.ShowAsync()
        ↓
require positive Visible acknowledgement
        ↓
MsiClawAddonPresentation.PauseForOverlayAsync()
        ↓
require publisher stopped + same device neutral
        ↓
construct/start OverlayControllerInputRouter on SAME physical source
        ↓
commit _overlayCaptureActive = true
        ↓
release gate
```

If Overlay Show fails:

```text
no presentation pause
no capture commit
current controller continues live
```

If presentation pause fails after the Overlay became visible:

```text
DO NOT commit capture
→ retire/hide Overlay through existing process controller
→ request one normal presentation reconcile if the failed pause changed presentation state
→ controller safety remains owned by current presentation/fail-close path
```

No retry loop.

No attempt to switch to the other presentation.

---

## 13. Steam/BPM change while Overlay is open

This is a required normal lifecycle case.

Example:

```text
Overlay opens while Xbox360 selected
→ user/game state changes so SteamDeck is now desired
```

While capture is active:

```text
existing PR7 reconcile event fires
→ presentation owner returns Blocked:OverlayCaptureActive
→ Xbox360 remains attached + neutral
```

When Overlay closes:

```text
release consumed controls
→ resume SAME Xbox360 publisher first
→ clear capture
→ request normal PR7 reconcile with fresh Steam/BPM state
→ PR7 may then legitimately switch Xbox360 → SteamDeck
```

Overlay itself never owns the switch decision.

---

## 14. Physical input loss / PnP recovery interaction

This is a real supported handheld lifecycle and must not regress.

If PID1902 DirectInput fails while Overlay capture is active:

```text
MsiClawInputSource stops
→ existing OnOwnedControllerPhysicalInputCompleted fires
→ Overlay input router gets SourceUnavailable signal
→ active Overlay surface is retired
→ Overlay capture clears without restarting publisher against dead input
→ virtual output remains neutral
→ existing PR8/PR10 recovery path runs
```

On successful physical recovery:

```text
existing RequestControllerPresentationReconcile("PhysicalInputRecovered")
→ fresh source is healthy
→ normal presentation owner repairs/reattaches/resumes according to current Steam/BPM policy
```

Do not duplicate PR8/PR10 PnP/identity/recovery logic inside Overlay code.

Do not mutate PID or HidHide from Overlay capture.

---

## 15. Process shutdown interaction

During Runtime shutdown, do not wait for the user to release Overlay buttons merely to exit Windows/process.

`BeginProcessShutdown()` should:

```text
stop accepting Overlay navigation
→ unsubscribe/dispose active Overlay input router
→ prevent new Overlay capture transitions
```

Then existing shutdown order remains authoritative:

```text
Overlay process teardown
→ presentation teardown / neutral detach
→ physical DirectInput teardown
```

Do not resume a publisher during process shutdown.

Do not add shutdown-specific presentation switching.

---

## 16. Logging

Add concise transition evidence only.

Recommended examples:

```text
[OverlayCapture] Capture requested
[OverlayCapture] Presentation paused neutral
[OverlayCapture] Capture committed
[OverlayCapture] Navigation action=NavigateDown
[OverlayCapture] Retirement requested reason=OutsideClick
[OverlayCapture] Waiting for consumed controls release
[OverlayCapture] Consumed controls released
[OverlayCapture] Same presentation resumed
[OverlayCapture] Presentation reconcile requested after release
[OverlayCapture] Source unavailable; leaving presentation neutral
[OverlayCapture] Overlay visible session lost
```

Do not log every raw `ControllerState` or every 8 ms poll.

Navigation edge logs may be Debug level.

---

## 17. Tests

Extend existing tests. Do not build a new test framework.

### 17.1 `MsiClawAddonPresentationTests`

Add focused coverage for both presentation kinds.

#### Xbox360 pause/resume

Verify:

```text
publisher StopAsync
→ SetXbox360DeviceState(default)
→ ActivePresentation remains Xbox360
→ NO DetachUSBDeviceEx
→ NO AttachUSBDeviceEx
→ resume Start on same publisher
```

#### SteamDeck pause/resume

Verify:

```text
publisher StopAsync
→ SetSteamDeckDeviceState neutral
→ ActivePresentation remains SteamDeck
→ NO detach
→ resume same publisher
```

#### Reconcile while paused

Verify fresh PR7 reconcile:

```text
returns Blocked / OverlayCaptureActive
→ no attach/detach
```

#### Publisher stop failure

Verify pause fails and never neutralizes/detaches underneath a possibly-live publisher.

#### Neutral-write failure

Verify pause does not succeed and presentation fails closed through the existing owner without alternate fallback.

#### Resume failure/source unavailable

Verify output is not falsely reported live and a later normal reconcile is allowed.

#### Center M release while paused

Verify official release still stops/neutral-detaches/tears VIIPER down correctly.

### 17.2 Overlay input router tests

Use a fake `IMsiClawPreparedInputSource`.

Verify:

1. initial held DPad/A/B does not emit an action on router start;
2. each false→true DPad edge emits one correct semantic action;
3. A rising edge → `Accept`;
4. B rising edge → `Back`;
5. release edges emit no navigation;
6. sticks/triggers/unmapped buttons emit nothing in OQ4;
7. `StopAcceptingNavigation()` prevents later actions;
8. release waiter completes immediately when DPad/A/B already neutral;
9. release waiter waits while a consumed control is held and completes on release;
10. `NotifySourceUnavailable()` releases the waiter as unavailable without polling.

### 17.3 Overlay transport tests

Extend `OverlayTransportTests` / nearby transport coverage:

1. protocol v2 peer is rejected after v3 bump;
2. server can deliver one semantic navigation frame to connected visible Overlay;
3. hidden/unready Overlay does not accept navigation delivery;
4. command + navigation frames remain intact through the shared write gate;
5. existing Show/Hide/DismissRequested behavior remains valid.

### 17.4 Host coordination guards

Use existing architecture/source guards or a small extracted test seam only where useful.

Prove the normal code path order conceptually:

```text
Show ACK
< presentation pause/neutral
< capture commit
```

and close:

```text
stop navigation
< Hide
< release gate
< presentation resume
```

Do not create a fake `AddonProcessHost` dependency graph merely to unit-test private orchestration.

---

## 18. Required hardware validation

Use Center M Disabled / Full PID1902 Addon ownership.

### Test A — Xbox360 presentation neutral capture

Precondition:

```text
Steam/BPM inactive
→ Xbox360 presentation active
```

Steps:

1. Open a controller-sensitive app/game or controller tester.
2. Show Addon Overlay through the current test/tray toggle.
3. Press DPad, A, B.

Expected:

- Overlay receives semantic actions;
- game/controller tester receives no controller movement while Overlay is active;
- Xbox360 remains attached throughout;
- no PID change;
- no DirectInput reacquire;
- no VIIPER detach/attach cycle.

### Test B — SteamDeck presentation neutral capture

Precondition:

```text
Steam game or BPM active
→ SteamDeck presentation active
```

Repeat Test A.

Expected:

- Overlay receives semantic navigation;
- game/Steam QAM behind Overlay does not move from controller input;
- SteamDeck remains attached;
- no Xbox360 presentation switch caused by Overlay.

### Test C — B close + release gate

1. Show Overlay.
2. Press and HOLD B.
3. B causes root Back/dismiss.
4. Keep B held briefly after the Overlay visually disappears.
5. Release B.

Expected:

```text
Overlay hides
→ virtual output remains neutral while B held
→ no B leaks into game/Steam QAM
→ publisher resumes only after B release
```

### Test D — outside touch/click over Steam QAM

1. Open Steam QAM.
2. Show Addon Overlay over it.
3. Navigate Overlay with controller.
4. Confirm Steam QAM behind it does not navigate.
5. Touch/click outside Overlay on the Steam QAM area.

Expected:

```text
outside interaction reaches existing DismissRequested path
→ Overlay retires
→ Steam QAM was never forcibly closed
→ after release gate, controller can control Steam QAM normally
```

This test is the acceptance evidence for NOT implementing OQ3-B.

### Test E — Steam/BPM policy changes while Overlay is active

1. Start with Xbox360 active.
2. Show Overlay.
3. Cause Steam/BPM desired state to become SteamDeck while Overlay remains visible.

Expected while Overlay active:

```text
Xbox360 remains attached + neutral
→ no detach/switch
```

Close Overlay.

Expected:

```text
same Xbox360 resumes after release gate
→ then normal PR7 reconcile may switch to SteamDeck
```

### Test F — unexpected Overlay process exit

1. Show Overlay with capture active.
2. Terminate `SteamInputAddonforClaw.Overlay.exe` externally.

Expected:

```text
Runtime survives
→ visible-session loss observed
→ capture retires
→ consumed controls release gate honored if source healthy
→ same presentation resumes
→ next Overlay request can warm-start a new Overlay process
```

No PID/HidHide/DirectInput churn.

### Test G — physical input loss / recovery while captured

Use the safest existing hardware test path that causes the owned DirectInput session to terminate/re-enumerate.

Expected:

```text
Overlay capture stops
→ presentation remains neutral
→ no stale controller state is published
→ existing PR8/PR10 recovery owns reacquisition
→ presentation returns only after physical input is healthy
```

Do not add a new destructive test mechanism solely for this scenario.

### Test H — Main UI coexistence regression

1. Show Overlay with capture active.
2. Request Main UI from tray/application activation.

Expected:

```text
Overlay capture safely retires
→ Overlay hidden
→ Main UI launches
```

No simultaneous finished Main UI + Overlay visible surfaces.

---

## 19. Explicit non-goals

Do NOT include:

- Steam QAM visibility detection/forced close;
- QamHost restart/termination;
- WING/OEM1 final physical-button assignment (OQ6);
- Game Bar suppression changes (OQ6);
- TDP/FPS/CPU Boost/Power UI controls (OQ5);
- raw ControllerState IPC;
- analog stick navigation;
- hold-repeat/debounce/gesture framework;
- XInput/GameInput reads in Overlay.exe;
- second DirectInput session;
- presentation detach/switch on normal Overlay open/close;
- PID1901/PID1902 changes;
- HidHide changes;
- VIIPER server/bus recreation;
- generalized UI surface manager;
- generalized input manager;
- watchdog/service/heartbeat;
- epoch/barrier/state-machine machinery for theoretical timing crossings.

---

## 20. Failure-policy summary

```text
Overlay Show fails
→ controller remains live
→ no capture

Presentation pause cannot stop publisher
→ hide Overlay
→ controller presentation remains owned/live
→ no unsafe detach

Publisher stopped but neutral rejected
→ fail close current presentation through existing owner
→ hide Overlay
→ normal reconcile may repair later

Overlay transport/navigation fails while captured
→ retire visible Overlay session
→ keep output neutral until release/capture retirement

Overlay process dies while captured
→ Runtime survives
→ retire capture
→ resume same presentation only when physical source healthy

Physical source dies while captured
→ retire Overlay capture
→ DO NOT resume against dead source
→ existing PR8/PR10 recovery owns recovery

Runtime shutdown
→ abandon Overlay navigation/capture without waiting for user release
→ existing presentation teardown remains authoritative
```

---

## 21. Acceptance criteria

- [ ] Overlay controller capture uses the existing PR5 `IMsiClawPreparedInputSource`; no second DirectInput session exists.
- [ ] Overlay navigation is semantic/edge-driven; no raw high-rate controller IPC exists.
- [ ] While capture is active, the currently selected virtual presentation remains attached but neutral.
- [ ] Normal Overlay Show/Hide performs no X360↔SteamDeck switch and no attach/detach.
- [ ] Xbox360 and SteamDeck both support stop-publisher → neutral → pause through `MsiClawAddonPresentation`.
- [ ] PR7 presentation reconcile is blocked from switching while Overlay capture is active.
- [ ] Close stops navigation, hides Overlay, waits DPad/A/B release, then resumes the SAME publisher.
- [ ] B-close does not leak B into the game/Steam QAM after Overlay disappearance.
- [ ] Outside click/touch uses the same Runtime capture retirement path.
- [ ] Main UI open uses the same safe capture retirement path before launch.
- [ ] Unexpected visible Overlay process loss cannot strand virtual output neutral indefinitely when the physical source is healthy.
- [ ] Physical DirectInput loss while captured leaves virtual output neutral and delegates recovery to existing PR8/PR10 ownership logic.
- [ ] Steam QAM may remain behind Overlay and receives no controller input while Overlay capture is active.
- [ ] No OQ3-B Steam-QAM close manager is implemented.
- [ ] Center M/PID/HidHide/VIIPER ownership contracts remain unchanged.
- [ ] Existing OQ3-A Main UI coexistence behavior remains intact.
- [ ] Existing Overlay warm-process, no-activate, outside-click, animation, DPI/WorkArea, and surface behavior remain intact.
- [ ] Build/test suite is green.
- [ ] Required real MSI Claw hardware tests A–H are recorded before calling OQ4 hardware-proven.

---

## 22. Overengineering guard

Judge failures against the supported real product lifecycle:

Must protect:

- actual Overlay process crash;
- actual Overlay transport failure;
- actual DirectInput loss;
- PnP re-enumeration/recovery;
- sleep/resume effects that invalidate physical input;
- controller publication failure;
- Main UI ↔ Overlay normal switching;
- held consumed controls during Overlay close.

Do not add complexity solely for:

- a navigation edge landing between two specific assignments;
- a Steam/BPM callback crossing one instruction during capture commit;
- an outside-click callback crossing one instruction during Hide;
- artificial task scheduling interleavings that still converge safely through the existing owner/gates.

The target is:

> one physical input owner, one presentation owner, one Runtime Overlay-capture fact, one Overlay retirement path.
