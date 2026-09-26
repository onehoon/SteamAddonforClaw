# Work Order — Full1902 Xbox360 Terminal-STOP Loop Diagnostic

## Status

Temporary developer diagnostic PR.

Repository:

```text
onehoon/SteamAddonforClaw
```

Implementation baseline reviewed for this work order:

```text
branch: main
commit: 8ac5ded108c544fce4ddf868325549cec7b8034d
latest merged PR at review time: #608 — Harden physical rumble STOP and suspend safety
frontend protocol: v42
```

Read before implementation:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/XBOX360_RUMBLE_PROBE_CONSOLE_WORK_ORDER_2026-09-24.md`
- `docs/work-order/FULL1902_XBOX360_RUMBLE_LATCH_SAFETY_STOP_HOTFIX_WORK_ORDER.md`
- `docs/work-order/FULL1902_PHYSICAL_RUMBLE_STOP_RETRY_HARDENING_WORK_ORDER_2026-09-25.md`
- `src/SteamInputAddonforClaw/Feedback/PresentationRumbleFeedback.cs`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs`
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`
- `src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml`
- `src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml.cs`
- `tools/RumbleProbe/Program.cs`
- `tests/SteamInputAddonforClaw.Tests/RumbleProbeTests.cs`
- `tests/SteamInputAddonforClaw.Tests/Xbox360RumbleFeedbackBridgeTests.cs`
- `tests/SteamInputAddonforClaw.Tests/VibrationContractRemovalTests.cs`

This PR is a diagnostic extension of the current Full1902 Xbox360 presentation. It must not restore the deleted pre-Full1902 vibration-test architecture.

---

# 1. Problem being measured

Real hardware has reproduced an intermittent Xbox360 rumble latch where a normal rumble envelope ends with a non-zero value and the expected terminal `0 / 0` callback never arrives.

Observed real-game pattern:

```text
normal case:
255
247
229
210
0
-> physical motor stops normally

failure case:
255
248
230
206
[terminal 0 never observed]
-> physical motor remains at the last state
-> existing 5 s safety stop eventually stops it
```

The same game action can succeed several times and then fail, so manual game reproduction is noisy.

A deterministic Addon-owned test is required so the exact expected terminal STOP time is known.

Target measurement path:

```text
Addon Runtime diagnostic
-> XInputSetState(...)
-> Windows XInput/XUSB
-> usbip-win2
-> VIIPER USB/IP
-> VIIPER Xbox360.HandleTransfer
-> SetXbox360RumbleCallback
-> Xbox360RumbleFeedbackBridge
-> physical MSI Claw rumble sink
```

When the diagnostic deliberately sends `0 / 0`, it must know whether the corresponding Addon Xbox360 callback arrived.

This complements the temporary VIIPER PR #48 diagnostic trace, including the planned pre-device-dispatch `X360RumbleUSBIPSubmitOut` event.

---

# 2. Important safety decision — keep the existing 5-second dead-man enabled

Do **not** globally disable or delete:

```text
Xbox360RumbleFeedbackBridge.DefaultSafetyStop = 5 seconds
```

The original investigation considered temporarily disabling it, but this is unnecessary and removes a useful last-resort safety layer.

The new diagnostic must detect a missing terminal `0 / 0` within:

```text
1000 ms
```

and stop the test before the 5-second production dead-man can become the deciding signal.

Therefore the expected failure sequence is:

```text
last non-zero callback
-> diagnostic sends XInputSetState(0,0)
-> XInputSetState returns success
-> wait up to 1000 ms for exact 0/0 callback
-> callback missing
-> diagnostic declares failure and stops
-> direct physical diagnostic cleanup STOP
```

The normal 5-second dead-man may still fire later from the last production callback. That is acceptable and should remain visible as the existing `ProductionXbox360RumbleSafetyStop` event.

Do not modify production dead-man semantics merely to make diagnostic logs prettier.

---

# 3. Goal

Re-enable the existing Developer `Vibration Test` page only as a **new Full1902 diagnostic surface** and add one focused test:

```text
Xbox360 Terminal STOP Loop
```

The test repeatedly:

1. selects the one active XInput slot;
2. generates a deterministic pseudo-random descending rumble envelope;
3. sends each value through real `XInputSetState`;
4. uses a fixed one-second cadence;
5. always ends each cycle with exact `0 / 0`;
6. waits for the production Xbox360 callback to observe that exact `0 / 0`;
7. starts the next cycle only after the terminal STOP was observed;
8. immediately stops and records failure when a successful `XInputSetState(0,0)` is not followed by the expected callback.

The test must continue until:

- the terminal STOP is missed;
- the operator presses Stop;
- the page/runtime/lifecycle cancels the test;
- XInput reports an operation failure;
- the active presentation is no longer a healthy Xbox360 presentation.

Do not add an artificial product retry/recovery loop.

---

# 4. Do not restore the legacy Developer Vibration Test

Full1902 Cleanup J intentionally removed:

```text
RunVibrationTest
OpenVibrationTestSession
CloseVibrationTestSession
FrontendVibrationTestCommand
FrontendVibrationTestResult
FeedbackAuthority
legacy routing-owned vibration session
legacy SteamDeck synthetic vibration path
```

Keep those deleted.

The new diagnostic must use the **current Full1902 production Xbox360 presentation and production feedback bridge**.

Do not reintroduce any type or RPC with the old removed names.

`VibrationContractRemovalTests` must continue proving that the old contract stays gone.

---

# 5. Reuse the existing RumbleProbe behavior, not its architecture

Use `tools/RumbleProbe/Program.cs` as the behavioral reference for:

- `xinput1_4.dll`;
- `XInputGetState`;
- `XInputSetState`;
- slot scanning 0..3;
- exact 8-bit-to-16-bit expansion:
  ```csharp
  ushort Expand(byte value) => (ushort)(value * 257);
  ```
- deterministic timing using `Stopwatch`;
- explicit logging of requested 8-bit and sent 16-bit values;
- cleanup behavior.

Do **not** refactor RumbleProbe into a general shared library merely for this feature.

Do **not** launch the console tool and scrape its logs from the UI.

The integrated diagnostic needs direct access to the current production callback observation, so it belongs in the Runtime process.

Small copied primitives are preferable to a new generic XInput abstraction hierarchy.

One narrow local interface for unit-test substitution is acceptable if needed, for example:

```csharp
internal interface IXbox360RumbleLoopXInput
{
    uint GetState(uint slot);
    uint SetState(uint slot, ushort leftMotor, ushort rightMotor);
}
```

Do not turn it into a general controller service.

---

# 6. Preconditions

Start must fail closed unless all required conditions are true.

Required:

```text
Center M Disabled / Addon controller authority exists
MsiClawAddonPresentation exists
ActivePresentation == Xbox360
Xbox360 publisher is healthy/running
Xbox360 production rumble callback is armed
physical rumble sink exists
presentation is not Overlay-paused
presentation is not Suspend-paused
Debug logging is enabled
exactly one XInput slot is connected, unless an explicit developer-only slot selector is added
```

Prefer automatic slot selection exactly like RumbleProbe:

```text
scan slots 0..3
0 connected -> Unavailable / fail start
1 connected -> use it
2+ connected -> fail start and report ambiguity
```

Do not invent VID/PID-to-XInput-slot correlation logic.

The operator should close games before starting this diagnostic.

If a current Steam AppId is available through the existing Runtime fact and is non-zero, Start should reject the test with a clear status such as:

```text
Close the active game before running the Xbox360 terminal STOP diagnostic.
```

Do not build a new process/game detector solely for this test.

---

# 7. Deterministic pseudo-random envelope

Do not use one fixed envelope such as:

```text
128 -> 112 -> 96 -> 80 -> 64 -> 48 -> 32 -> 16 -> 8 -> 4 -> 0
```

Use a deterministic pseudo-random **strictly descending** envelope per cycle.

Requirements:

```text
start value:          128..220
non-zero step count:  6..10
final non-zero value: 4..16
left motor:           same value
right motor:          same value
step interval:        fixed 1000 ms
terminal state:       always exact 0/0
terminal wait:        fixed 1000 ms
```

Only amplitude/step count vary.

Timing must **not** be randomized.

Left/right must **not** be randomized independently in this PR.

The diagnostic is trying to vary rumble shape while keeping cadence and terminal STOP semantics controlled.

## 7.1 Reproducible seed

Use a fixed base seed and derive each cycle deterministically.

Example:

```csharp
const int BaseSeed = unchecked((int)0x434C4157); // "CLAW"
var random = new Random(BaseSeed ^ cycleNumber);
```

Any equivalent deterministic formula is acceptable.

Log:

```text
BaseSeed
Cycle
CycleSeed
full generated 8-bit sequence
```

A failed cycle must be reproducible later from the log alone.

Do not use `Random.Shared`, current time, cryptographic randomness, or another non-repeatable seed.

## 7.2 Simple generation rule

Keep generation simple.

One acceptable algorithm:

1. choose `nonZeroStepCount` in 6..10;
2. choose `start` in 128..220;
3. choose `finalNonZero` in 4..16;
4. sample enough unique interior integer values from:
   ```text
   finalNonZero + 1 .. start - 1
   ```
5. sort those interior values descending;
6. construct:
   ```text
   [start] + [descending random interior values] + [finalNonZero] + [0]
   ```

The resulting sequence must always be strictly descending until terminal zero.

Example output:

```text
Cycle 1: 174,151,137,104,79,53,31,12,0
Cycle 2: 201,182,143,119,91,62,37,9,0
Cycle 3: 156,139,122,88,73,41,18,6,0
```

The exact examples are illustrative only.

---

# 8. Fixed one-second cadence

Every state is scheduled on a fixed 1000 ms cadence.

Use `Stopwatch`-based elapsed timing like the existing RumbleProbe rather than accumulating `Task.Delay(1000)` drift indefinitely.

Conceptually:

```text
T+0s    send cycle value #1
T+1s    send cycle value #2
T+2s    send cycle value #3
...
T+Ns    send 0/0
T+(N+1)s:
    if callback 0/0 was observed -> next cycle
    otherwise -> fail test
```

If the terminal callback arrives quickly, still preserve the remainder of the one-second cadence before starting the next cycle.

Do not randomize timing.

---

# 9. Terminal STOP observation contract

This is the core of the diagnostic.

Before calling:

```csharp
XInputSetState(slot, 0, 0)
```

arm one in-memory expectation for:

```text
expected callback = Left8=0, Right8=0
current diagnostic RunId
current Cycle
terminal Step
```

The expectation must be armed **before** the native XInput call.

Reason:

A native/USB/IP callback may arrive very quickly, including before `XInputSetState` returns. Missing that legitimate callback would create a false failure.

Keep this simple:

- one small lock/gate;
- one current expectation;
- one `TaskCompletionSource` with `RunContinuationsAsynchronously`;
- no epoch framework;
- no queue;
- no generalized event bus.

After arming:

```text
call XInputSetState(0,0)
log return code
```

If XInput returns non-success:

- cancel the expectation;
- classify as XInput operation failure;
- stop the diagnostic;
- do not classify it as a missing terminal callback.

If XInput returns success:

- wait up to 1000 ms for the production callback observer to see exact `0/0`.

If exact `0/0` arrives:

- mark cycle terminal STOP matched;
- log callback timing;
- preserve one-second cadence;
- start the next cycle.

If exact `0/0` does not arrive:

- freeze the failure result;
- stop generating XInput states;
- do **not** send another XInput `0/0`;
- perform one separate physical diagnostic cleanup STOP;
- leave the failed state visible until the operator starts a new run or leaves the page.

---

# 10. Production callback observation seam

Do not create a second VIIPER callback registration.

There must still be exactly one production Xbox360 native callback:

```text
VIIPER
-> Xbox360RumbleFeedbackBridge.OnRumble
```

Add only a tiny diagnostic observer seam to the existing bridge/presentation.

Preferred shape:

```text
MsiClawAddonPresentation
    owns current diagnostic runner, if any

Xbox360RumbleFeedbackBridge
    receives normal production callback
    logs it
    notifies one optional in-process diagnostic observer
    writes the normal physical rumble
    preserves normal 5s safety behavior
```

For example, `TryArm` may accept one optional internal callback:

```csharp
Action<byte, byte, long?>? diagnosticObserver
```

or an equivalent tiny internal record/callback.

The observer must:

- never replace the existing VIIPER callback;
- never write physical rumble;
- never schedule presentation changes;
- never throw across the native callback boundary;
- be a no-op when no diagnostic is running.

Prefer a stable presentation-owned observer callback that forwards to the current optional runner, so starting/stopping the diagnostic does not require unregistering/re-registering the VIIPER callback.

---

# 11. Diagnostic owner

Do not introduce:

```text
RumbleDiagnosticManager
RumbleDiagnosticService
FeedbackAuthority
DiagnosticSessionAuthority
RumbleWatchdog
generic Scheduler
generic TestFramework
```

Use one small diagnostic object, for example:

```text
Diagnostics/Xbox360RumbleLoopDiagnostic.cs
```

and let the existing `MsiClawAddonPresentation` own at most one active instance.

The presentation owner is already the authority for:

- active Xbox360 vs SteamDeck presentation;
- publisher lifetime;
- feedback callback lifetime;
- physical rumble sink lifetime.

That is the correct place to admit/cancel this diagnostic.

Add narrow operations to the existing presentation contract only if required, conceptually:

```csharp
Xbox360RumbleLoopSnapshot CaptureXbox360RumbleLoopDiagnostic();
Task<Xbox360RumbleLoopSnapshot> StartXbox360RumbleLoopDiagnosticAsync(CancellationToken token);
Task<Xbox360RumbleLoopSnapshot> StopXbox360RumbleLoopDiagnosticAsync(CancellationToken token);
```

Exact internal type names are implementation choice.

Do not expose raw VIIPER handles or the physical rumble sink through frontend contracts.

---

# 12. Physical cleanup after a detected missing terminal callback

When the target failure is detected:

```text
XInputSetState(0,0) returned SUCCESS
but exact 0/0 production callback was not observed within 1000 ms
```

do **not** call `XInputSetState(0,0)` again.

A second host STOP could succeed and destroy the clean evidence that the first terminal command did not arrive.

Instead request one direct physical STOP through the **existing** presentation-owned:

```text
IPhysicalRumbleSink
```

Log it as a clearly separate diagnostic safety action, for example:

```text
Event=X360LoopProbePhysicalCleanupStop
RunId=...
Cycle=...
Status=Succeeded|Failed
Reason=...
```

This physical STOP must not be confused with:

- a host-generated XInput terminal STOP;
- a VIIPER callback;
- the existing 5-second production safety stop.

Do not add a second HID writer.

Reuse the existing shared sink.

---

# 13. Manual Stop / ordinary cancellation

Manual operator Stop is not the failure-under-test.

For a normal operator cancellation:

1. cancel the loop;
2. issue one normal `XInputSetState(0,0)` cleanup attempt if the selected slot is still known;
3. log the cleanup result;
4. request the existing physical sink STOP as final safety if appropriate;
5. transition to `Stopped`.

This cleanup `0/0` must be explicitly labeled as:

```text
DiagnosticCleanupStop
```

not as a cycle terminal STOP.

Do not use a retry loop.

---

# 14. Real lifecycle handling

This is developer-only code, but it must not weaken real Full1902 lifecycle safety.

Cancel the diagnostic when any of these occur:

- presentation begins switching away from Xbox360;
- presentation is retired;
- Overlay pause begins;
- Suspend pause begins;
- Center M authority release begins;
- physical/publisher fail-close retires the presentation;
- process shutdown / presentation dispose occurs.

Do not try to survive or resume the test across those transitions.

After cancellation, normal existing lifecycle STOP/neutral/teardown remains authoritative.

Do not create an epoch/barrier/state machine solely to cover theoretical callback interleavings.

A single cancellation token + one current expectation is enough.

---

# 15. Frontend contract

The UI process is not the Runtime owner, so expose the diagnostic through the existing frontend pipe.

Add new typed Full1902 diagnostic contracts with **new names**, for example:

```text
FrontendXbox360RumbleLoopState
FrontendXbox360RumbleLoopSnapshot

IAddonFrontendControl.CaptureXbox360RumbleLoopDiagnosticAsync(...)
IAddonFrontendControl.StartXbox360RumbleLoopDiagnosticAsync(...)
IAddonFrontendControl.StopXbox360RumbleLoopDiagnosticAsync(...)
```

Suggested states:

```text
Unavailable
Ready
Running
Stopped
Failed
```

Snapshot should stay small and UI-focused.

Suggested fields:

```text
Available
State
Status
RunId
Slot
Cycle
Step
CurrentValue8
LastObservedLeft8
LastObservedRight8
LastObservedCallbackSequence
FailureReason
```

The full generated sequence belongs in logs, not necessarily in every frontend snapshot.

Use safe default interface implementations returning Unavailable where that avoids unrelated test-double churn.

---

# 16. Named-pipe transport

Add:

```text
CaptureXbox360RumbleLoopDiagnostic
StartXbox360RumbleLoopDiagnostic
StopXbox360RumbleLoopDiagnostic
```

to `FrontendRpcMethod`.

Bump:

```text
FrontendTransportProtocol.CurrentVersion
42 -> 43
```

Add the normal v43 comment explaining that the Developer Vibration page now has a new Full1902 Xbox360 loop diagnostic contract.

Wire through:

```text
IAddonFrontendControl
InProcessAddonFrontendControl
NamedPipeAddonFrontendServer
NamedPipeAddonFrontendClient
FrontendWire
```

Do not reuse the deleted v22 vibration-test request/response types.

Do not add a second named pipe.

---

# 17. Late-bind to the existing presentation owner

`InProcessAddonFrontendControl` is constructed before deferred Disabled-mode controller presentation startup.

Do not create a second presentation reference merely for this diagnostic.

Prefer narrow late-bound delegates from `AddonProcessHost`, using its existing:

```text
_presentationOwnership
_runtimeHost
```

facts.

Conceptually:

```text
frontend Start
-> host-local delegate
-> current _presentationOwnership
-> current active Xbox360 diagnostic owner
```

If no presentation exists, return Unavailable.

Do not move controller ownership into `AddonRuntimeHost`.

Do not make frontend lifetime controller authority.

---

# 18. Developer Vibration Test UI

Reuse the existing page:

```text
src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml
src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml.cs
```

Do not restore the old disabled:

```text
Rumble (0xEB)
Haptic (0xEA)
```

legacy controls.

Replace/rework the current unavailable placeholder into one focused developer diagnostic.

Suggested presentation:

```text
Vibration Test

Xbox360 Terminal STOP Loop

Repeatedly sends deterministic pseudo-random descending XInput rumble
sequences ending in exact 0/0. The test stops automatically if a
successful terminal 0/0 command is not observed by the Addon callback.

State: Ready / Running / Failed / Stopped
Slot: 0
Cycle: 14
Current: 37 / 37
Last callback: 37 / 37

[Start]
[Stop]
```

While Running:

- Start disabled;
- Stop enabled.

When Failed:

- Start may start a fresh run;
- Stop disabled;
- failure message visible;
- keep failed cycle number visible.

The page is developer-only. Do not expose this on Device/Controller/Overlay ordinary UI.

---

# 19. UI activation/deactivation

On page Activate:

- capture current diagnostic status;
- do not automatically start.

While page is visible and a test is Running, a simple developer-only capture refresh approximately once per second is acceptable.

Do not use global `StateInvalidated` every cycle because that would unnecessarily refresh unrelated Main UI surfaces.

Stop UI-local polling when the page is hidden.

When the user leaves the page normally:

- request diagnostic Stop;
- do not leave a deliberate vibration generator running because its frontend page disappeared.

On UI shutdown, `DeactivateAsync` must await the Stop request before the frontend client is disposed when possible.

Do not change the product rule that the controller Runtime itself survives frontend closure; only this explicitly developer-started diagnostic should stop.

---

# 20. Diagnostic logging

Require Debug logging before Start.

Use existing `AppLog`.

Do not create another Addon log file.

Lifecycle/failure events may be Info; per-step detail should be Debug.

Required correlation fields:

```text
RunId
BaseSeed
Cycle
CycleSeed
Step
StepCount
ExpectedLeft8
ExpectedRight8
XInputSlot
XInputResult
ElapsedMs
```

## 20.1 Session start

Example:

```text
Event=X360LoopProbeStart
RunId=3
Slot=0
BaseSeed=0x434C4157
StepIntervalMs=1000
TerminalWaitMs=1000
ProductionDeadmanMs=5000
```

## 20.2 Cycle sequence

Before first state in each cycle:

```text
Event=X360LoopProbeCycleStart
RunId=3
Cycle=27
CycleSeed=...
Sequence=189,161,143,112,85,59,33,14,0
```

## 20.3 State send

```text
Event=X360LoopProbeSend
RunId=3
Cycle=27
Step=9
ExpectedLeft8=0
ExpectedRight8=0
Left16=0
Right16=0
XInputResult=0
```

## 20.4 Callback observation

The existing production log remains authoritative:

```text
Event=ProductionXbox360RumbleRx
RumbleSeq=...
Left8=...
Right8=...
IsStop=...
```

Add diagnostic correlation when the active expectation matches, for example:

```text
Event=X360LoopProbeCallbackMatched
RunId=3
Cycle=27
Step=9
ObservedLeft8=0
ObservedRight8=0
ObservedRumbleSeq=...
ElapsedFromSetMs=...
```

## 20.5 Target failure

```text
Event=X360LoopProbeTerminalStopMissing
RunId=3
Cycle=27
Step=9
ExpectedLeft8=0
ExpectedRight8=0
XInputResult=0
LastObservedLeft8=14
LastObservedRight8=14
LastObservedRumbleSeq=...
TimeoutMs=1000
```

After this event, no more XInput state generation is allowed.

## 20.6 Cleanup

```text
Event=X360LoopProbePhysicalCleanupStop
RunId=3
Cycle=27
Status=Succeeded
```

---

# 21. Correlation with VIIPER PR #48

This Addon diagnostic must work independently of a special VIIPER build.

Do not add a new VIIPER API dependency in this PR.

However, the intended field build should later run with the diagnostic VIIPER PR #48 artifact so logs can correlate:

```text
Addon X360LoopProbeSend 0/0
        ↓
VIIPER X360RumbleUSBIPSubmitOut 0/0
        ↓
VIIPER X360RumbleRaw 0/0
        ↓
Addon ProductionXbox360RumbleRx 0/0
```

Do not hard-code an unmerged PR #48 commit/hash into this work order.

Artifact pinning/provenance should be handled only after the final PR #48 diagnostic DLL exists.

---

# 22. Failure interpretation

## Case A — Addon sends terminal STOP, VIIPER USB/IP boundary has no 0/0

```text
X360LoopProbeSend Expected=0/0 XInputResult=0
(no X360RumbleUSBIPSubmitOut 0/0)
(no X360RumbleRaw 0/0)
(no ProductionXbox360RumbleRx 0/0)
```

Conclusion:

```text
successful XInputSetState(0,0) did not become a terminal CMD_SUBMIT OUT
at VIIPER's TCP USB/IP receive boundary
```

Investigate Windows XInput/XUSB/usbip-win2 upstream of VIIPER receive.

Do not claim usbip-win2 is proven solely from this.

## Case B — USB/IP boundary has 0/0, X360 raw does not

```text
X360RumbleUSBIPSubmitOut 0/0 exists
X360RumbleRaw 0/0 absent
```

Investigate VIIPER transport-to-device dispatch.

## Case C — X360 raw has 0/0, Addon callback does not

Investigate VIIPER callback / C ABI / managed callback boundary.

## Case D — Addon callback has 0/0

The terminal STOP reached the Addon.

If physical behavior is still wrong, inspect the physical sink/write evidence separately.

---

# 23. Tests

Add focused tests only.

Do not add scheduler torture tests or pathological race tests.

## 23.1 Sequence generation

Prove for many cycle numbers:

- deterministic output for the same cycle;
- 6..10 non-zero values;
- start 128..220;
- final non-zero 4..16;
- strictly descending non-zero sequence;
- terminal value exactly 0;
- left/right identical;
- different representative cycles produce different sequences.

## 23.2 Full-range expansion

Reuse/prove:

```text
0   -> 0
1   -> 257
127 -> 32639
255 -> 65535
```

Do not invent another amplitude conversion.

## 23.3 Expectation armed before XInput call

Use a fake XInput implementation that invokes the diagnostic observer synchronously from inside `SetState(0,0)`.

Prove the terminal callback is observed and the test does not falsely fail.

This protects a realistic native callback ordering without introducing extra lifecycle machinery.

## 23.4 Successful cycle

For one fast-timeout test sequence:

```text
non-zero sends succeed
terminal XInputSetState succeeds
0/0 callback is observed
-> next cycle begins
```

## 23.5 Missing terminal callback

Prove:

```text
terminal XInputSetState(0,0) == SUCCESS
no 0/0 callback within injected short test timeout
-> state Failed
-> exact failure event/state recorded
-> no next cycle
-> no second XInput 0/0
-> one direct physical diagnostic cleanup STOP
```

## 23.6 XInput failure is distinct

If terminal `XInputSetState` returns an error:

- state Failed;
- reason is XInput failure;
- do not label it `TerminalStopMissing`.

## 23.7 Manual stop

Prove explicit Stop cancels the runner and performs one diagnostic cleanup path without retry loops.

## 23.8 Presentation lifecycle cancellation

At minimum prove a real owner transition that retires/disarms Xbox360 cancels the diagnostic and no later loop step is generated.

Use existing presentation test seams.

Do not invent timing-only race tests.

## 23.9 Dead-man unchanged

Existing `Xbox360RumbleFeedbackBridgeTests` must continue proving the normal 5-second production policy.

Add a regression assertion if needed that starting the diagnostic does not set the bridge safety delay to zero or disable it.

## 23.10 Legacy vibration contract remains removed

Update `VibrationContractRemovalTests` only enough to recognize the new diagnostic contract.

It must still prove the old names/types remain absent:

```text
RunVibrationTest
OpenVibrationTestSession
CloseVibrationTestSession
FrontendVibrationTestCommand
FrontendVibrationTestResult
FeedbackAuthority
```

## 23.11 Frontend wire

Prove:

- protocol == 43;
- Capture/Start/Stop round-trip;
- invalid/unexpected payload rejected;
- passive/default implementation reports Unavailable;
- no old vibration RPC is restored.

## 23.12 UI architecture

Replace the old assertion that the page is always unavailable.

Prove the page exposes the new Terminal STOP Loop controls and does not contain the deleted legacy RPC/type names.

---

# 24. Expected files

Likely files, subject to actual implementation:

```text
src/SteamInputAddonforClaw/Diagnostics/Xbox360RumbleLoopDiagnostic.cs
src/SteamInputAddonforClaw/Feedback/PresentationRumbleFeedback.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml
src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml.cs

tests/SteamInputAddonforClaw.Tests/...
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
```

Do not create a new project merely for the integrated diagnostic.

Keep `tools/RumbleProbe` as the existing standalone reference utility.

---

# 25. Explicit non-goals

Do not include:

```text
production dead-man removal
dead-man timeout retuning
VIIPER parser changes
usbip-win2 changes
low-latency / zero-copy A/B switch
VIIPER artifact pin before PR48 artifact is finalized
PID1901/PID1902 policy changes
HidHide changes
physical input changes
presentation switching redesign
second VIIPER callback
second physical HID writer
legacy vibration test restoration
SteamDeck haptic test restoration
user-facing vibration-strength setting
generic test scripting language
random timing
independent left/right randomization
automatic restart after failure
retrying the missing terminal STOP
```

---

# 26. Validation

Run at minimum:

```text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet test SteamInputAddonforClaw.slnx -c Debug --no-build

dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release --no-build

git diff --check
```

Also run the focused RumbleProbe tests and the new loop-diagnostic tests directly if useful.

Verify no release/package inclusion change is made to `tools/RumbleProbe`.

---

# 27. First hardware run

Use:

```text
Center M Disabled
physical PID1902 owned normally
Steam/BPM inactive
Xbox360 presentation active
all games closed
Addon logging = Debug
diagnostic VIIPER PR48 build installed when available
```

Open:

```text
Settings
-> Developer Menu
-> Vibration Test
-> Xbox360 Terminal STOP Loop
-> Start
```

Let it run until:

- `Failed: terminal STOP callback missing`, or
- enough cycles have completed to justify a manual Stop.

Export:

```text
Addon logs
libVIIPER.log
```

Do not trigger another XInput STOP after automatic missing-terminal failure before logs are collected, except the diagnostic's direct physical cleanup STOP.

---

# 28. Acceptance criteria

The PR is complete when:

1. The existing Developer Vibration Test page exposes exactly one new Full1902 Xbox360 terminal-STOP loop diagnostic.
2. Old Cleanup-J vibration RPC/session architecture remains deleted.
3. Production Xbox360 5-second dead-man remains enabled and unchanged.
4. Each cycle uses deterministic pseudo-random strictly descending equal-motor values.
5. Non-zero step count is 6..10.
6. Start value is 128..220.
7. Final non-zero is 4..16.
8. Timing is fixed at 1000 ms per state.
9. Every cycle ends with exact `0 / 0`.
10. A terminal callback expectation is armed before `XInputSetState(0,0)`.
11. A successful terminal XInput call without callback within 1000 ms stops the test.
12. Missing-terminal failure does not send a second XInput STOP.
13. Failure cleanup uses the existing physical rumble sink exactly once as a separately logged diagnostic safety action.
14. The existing production callback remains the one VIIPER callback authority.
15. Logs carry RunId/Cycle/Seed/Step/expected values/XInput result/callback evidence.
16. The test is cancelled by real presentation/suspend/shutdown lifecycle transitions.
17. Frontend protocol is bumped to v43 and Capture/Start/Stop are typed.
18. The UI stops the developer diagnostic when leaving the page.
19. Unit/integration/UI tests pass.
20. The implementation adds no new controller authority, manager hierarchy, retry state machine, or speculative race-defense machinery.
