# Work Order — OQ5-UI-03: Dual-Stick Directional Navigation

## Status

Third implementation PR from:

- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`

Track: Addon Quick Settings Overlay UI

Label/name: `OQ5-UI-03`

Baseline: `main` after PR #461 / commit `623fdaf59485d5b5a9410984d4e94be2b166b873`.

This is **not** part of the numbered Full PID1902 PR sequence.

---

## 1. Goal

Extend the existing OQ4/OQ5 semantic controller-input router so **both physical analog sticks** can act as directional Overlay navigation sources.

Frozen product behavior for this PR:

```text
DPad        → existing NavigateUp/Down/Left/Right
Left Stick  → NavigateUp/Down/Left/Right
Right Stick → NavigateUp/Down/Left/Right
```

The implementation must remain controller-authority-safe:

```text
physical MSI Claw PID1902
→ existing MsiClawInputSource
→ normalized ControllerState
→ existing OverlayControllerInputRouter
→ semantic Navigate* action
→ existing .Overlay transport
→ Overlay frontend
```

Do **not** send raw stick axes over `.Overlay`.

Do **not** open another DirectInput/XInput/GameInput reader in `Overlay.exe`.

Do **not** add hold-repeat, acceleration, gestures, velocity, or a generalized analog-navigation framework in this PR.

The visible shell does not yet have the row-selection model from OQ5-UI-04, so this PR primarily establishes the correct Runtime semantic-input and release-gate foundation. Existing `NavigateUp/Down/Left/Right` messages continue to cross the current transport; OQ5-UI-04 will make them move real logical row selection.

---

## 2. Required reading before implementation

Read current `main` after #461.

Required documents:

- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/overlayui/OQ5_UI_02_LB_RB_TAB_NAVIGATION_WORK_ORDER.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/OQ4_CONTROLLER_CAPTURE_NEUTRAL_PUBLICATION_WORK_ORDER.md`

Required current source:

- `src/SteamInputAddonforClaw/Lifecycle/OverlayControllerInputRouter.cs`
- `src/SteamInputAddonforClaw/Input/ControllerState.cs`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawControllerStateMapper.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs`
- `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
- `tests/SteamInputAddonforClaw.Tests/OverlayControllerInputRouterTests.cs`

---

## 3. Current code facts that must guide the change

### 3.1 There is already one canonical physical input source

OQ4 already uses the existing prepared PID1902 source:

```text
IMsiClawPreparedInputSource.LatestState
IMsiClawPreparedInputSource.StateChanged
```

`OverlayControllerInputRouter` subscribes to that source and is the narrow semantic-input seam.

Do not add another controller-input owner.

### 3.2 Stick axes are already normalized

Current `ControllerState` contains:

```csharp
StickState LeftStick;
StickState RightStick;
```

with signed `short` axes centered around zero.

Current MSI mapping already normalizes physical DirectInput axes as follows:

```text
Left Stick X  : negative = left,  positive = right
Left Stick Y  : positive = up,    negative = down
Right Stick X : negative = left,  positive = right
Right Stick Y : positive = up,    negative = down
```

Y inversion is already handled by `MsiClawControllerStateMapper`.

Do not add another axis normalization layer in the Overlay router.

### 3.3 Current semantic wire actions are already sufficient

After OQ5-UI-02, `.Overlay` protocol is version 4 and already exposes:

```text
NavigateUp
NavigateDown
NavigateLeft
NavigateRight
Accept
Back
PreviousTab
NextTab
```

This PR maps sticks to the **existing** `Navigate*` actions.

Therefore:

```text
OverlayTransportProtocol.CurrentVersion remains 4
```

Do **not** bump the protocol only because a new physical input source now produces existing semantic actions.

### 3.4 Current router stores only previous buttons

Current OQ4/OQ5 router stores:

```csharp
private GamepadButtons _previous;
```

That is sufficient for button rising edges but insufficient for:

- stick neutral/active edge detection;
- stick state at capture start;
- stick neutrality in the release-to-resume gate.

This PR should extend the existing router to track the latest full `ControllerState` rather than creating a second parallel stick-state authority.

### 3.5 Current release gate covers button inputs only

After #461, current consumed button set is:

```text
DPad + A + B + LB + RB
```

Once sticks become Overlay navigation inputs, close safety must also include both sticks.

---

## 4. Frozen stick-navigation behavior

### 4.1 Both sticks are equivalent navigation sources

Frozen mapping:

```text
Left Stick up    → NavigateUp
Left Stick down  → NavigateDown
Left Stick left  → NavigateLeft
Left Stick right → NavigateRight

Right Stick up    → NavigateUp
Right Stick down  → NavigateDown
Right Stick left  → NavigateLeft
Right Stick right → NavigateRight
```

Do not assign different meanings to left and right stick in this PR.

Do not use right stick for scrolling/velocity separately.

### 4.2 One deflection produces one action

A stick starts **armed** only when it is neutral.

Conceptual behavior:

```text
neutral
→ deflect beyond activation threshold
→ emit exactly one Navigate* action
→ disarm that stick
→ continuing to hold / moving farther in same direction emits nothing
→ return to neutral/deadzone
→ re-arm
→ next deflection may emit one action
```

This is intentionally analogous to the current button rising-edge model.

Do not add held-direction repeat yet.

### 4.3 A stick already deflected when Overlay capture starts must not emit

At `Start()` inspect `source.LatestState`.

If either stick is already outside the neutral region:

```text
capture starts
→ no navigation action for that already-held deflection
→ stick must first return to neutral
→ later fresh deflection may emit
```

This matches the existing OQ4 rule that a button already held when capture starts does not create a synthetic press.

### 4.4 Dominant-axis classification

A physical stick can be moved diagonally. The router must emit **at most one direction for one stick activation**.

Do not emit both vertical and horizontal actions from one diagonal deflection.

Use a simple dominant-axis rule:

```text
abs(Y) >= abs(X) → vertical direction
abs(X) >  abs(Y) → horizontal direction
```

Direction mapping:

```text
vertical:
Y > 0 → NavigateUp
Y < 0 → NavigateDown

horizontal:
X > 0 → NavigateRight
X < 0 → NavigateLeft
```

The exact equality tie rule above intentionally prefers vertical. The important invariant is deterministic single-action classification, not the specific preference.

Reason: once OQ5-UI-04 adds real row selection, a diagonal stick input must not accidentally produce two sequential moves such as Up + Right.

Do not build angle sectors, vector normalization, radial acceleration, or gesture recognition.

---

## 5. Threshold / deadzone policy

Use one small implementation-local policy inside the existing router.

Recommended initial constants:

```csharp
private const int StickActivationThreshold = 16_000;
private const int StickNeutralThreshold = 8_000;
```

The source range is the normalized signed-short range.

These are starting values for the supported Claw hardware and should be hardware-validated later.

### 5.1 Activation

A stick is directionally active when its dominant axis magnitude is at least the activation threshold.

Conceptually:

```text
max(abs(X), abs(Y)) >= StickActivationThreshold
```

### 5.2 Neutral / re-arm

A stick is neutral only when **both axes** are within the smaller neutral threshold:

```text
abs(X) <= StickNeutralThreshold
AND
abs(Y) <= StickNeutralThreshold
```

The smaller neutral threshold creates simple hysteresis so ordinary analog jitter near the activation threshold does not repeatedly re-arm and retrigger navigation.

This is not a generalized debounce system.

### 5.3 Safe absolute-value handling

Because an axis can be `short.MinValue`, avoid `Math.Abs(short)` overflow behavior.

Convert to `int` before absolute value, conceptually:

```csharp
var x = (int)stick.X;
var y = (int)stick.Y;
var absX = Math.Abs(x);
var absY = Math.Abs(y);
```

### 5.4 No user-facing deadzone setting

Do not add:

- settings.json keys;
- UI controls;
- profile values;
- device-specific deadzone preferences;

for Overlay navigation thresholds in this PR.

If hardware validation proves the initial thresholds poor, adjust the constants in a focused follow-up or during this PR before merge.

---

## 6. Router state model

Keep the change inside `OverlayControllerInputRouter`.

A simple implementation shape is preferred, for example:

```csharp
private ControllerState _previousState;
private bool _leftStickArmed;
private bool _rightStickArmed;
```

or an equivalently small representation.

Do not introduce:

- `AnalogNavigationManager`;
- `StickInputService`;
- `InputGestureStateMachine`;
- epochs/generations;
- another event queue;
- another lock.

The existing router `_sync` lock and existing semantic delivery channel remain sufficient.

### 6.1 Start behavior

Inside existing `Start()`:

```text
_previousState = _source.LatestState
_leftStickArmed  = IsStickNeutral(_previousState.LeftStick)
_rightStickArmed = IsStickNeutral(_previousState.RightStick)
```

Existing button held-at-start behavior must remain unchanged.

### 6.2 StateChanged behavior

Inside the existing `OnStateChanged` callback:

1. keep current button rising-edge processing;
2. process left-stick semantic edge;
3. process right-stick semantic edge;
4. update the latest full controller state;
5. evaluate the existing release waiter against the full consumed-input state.

Do not await pipe delivery from the DirectInput callback.

Continue using the existing channel for semantic delivery.

### 6.3 Stick helper shape

A tiny private helper is acceptable, conceptually:

```csharp
private static OverlayNavigationAction? ClassifyStickDirection(StickState stick)
```

or:

```csharp
private bool TryQueueStickNavigation(StickState stick, ref bool armed)
```

Keep it private/narrow to this router.

Do not create a public reusable stick-navigation framework before there is another real consumer.

---

## 7. Release-to-resume safety

This is a required OQ4 lifecycle extension.

After this PR, Overlay-consumed input means:

```text
buttons:
DPad + A + B + LB + RB

analog:
Left Stick
Right Stick
```

The virtual publisher must not resume while either stick is still outside the neutral region.

### 7.1 Required close behavior

Example:

```text
Overlay visible
→ user pushes Left Stick up
→ NavigateUp emitted
→ user closes Overlay with outside click/touch while stick is still held up
→ Overlay hides
→ current X360/SteamDeck presentation remains neutral
→ existing release waiter waits
→ user returns Left Stick to neutral/deadzone
→ release waiter completes
→ only then current publisher may resume
```

This prevents the same held stick from immediately moving/controlling the game or Steam QAM behind the Overlay after close.

### 7.2 Full-state consumed check

Replace the button-only conceptual check:

```text
AnyConsumedHeld(buttons)
```

with a narrow full-state check equivalent to:

```text
AnyConsumedInputActive(controllerState)
```

which returns true when either:

- one of the existing consumed buttons is held; or
- left stick is not neutral; or
- right stick is not neutral.

Keep button binding/action mapping separate enough that LT/RT/X/Y do not accidentally become consumed.

### 7.3 Source unavailable still wins

Existing OQ4 behavior remains:

```text
physical input source becomes unavailable while waiting
→ release waiter completes SourceUnavailable
→ publisher is not resumed against dead input
→ existing physical recovery owns recovery
```

Do not add stick-specific recovery logic.

### 7.4 Shutdown behavior unchanged

Process shutdown still does not wait indefinitely for user release.

Existing shutdown/teardown behavior remains authoritative.

Do not add a shutdown stick-neutral loop.

---

## 8. Transport and Overlay frontend scope

### 8.1 No protocol bump

This PR adds no new wire semantic.

Keep:

```text
OverlayTransportProtocol.CurrentVersion = 4
```

Do not edit the protocol only to document physical stick support unless a normal comment update is genuinely useful.

### 8.2 Existing Navigate* path remains the UI seam

Stick navigation must emit the same actions already used by DPad:

```text
NavigateUp
NavigateDown
NavigateLeft
NavigateRight
```

Do not add:

```text
LeftStickUp
RightStickUp
StickMoved
AxisValue
```

The visible Overlay should not care which physical directional source created the semantic action.

### 8.3 No row-selection UI yet

OQ5-UI-04 owns the logical row-selection model.

Therefore this PR must not add:

- selected row visuals;
- ScrollViewer bring-into-view logic;
- Left/Right slider adjustment;
- A activation dispatch;
- Shortcut 2×2 navigation;
- page-specific directional behavior.

It is acceptable that DPad/stick `Navigate*` actions are delivered/logged but do not yet visibly move placeholder content.

### 8.4 Footer remains truthful

Keep the current footer:

```text
LB/RB  Tabs   B  Close
```

Do not advertise stick navigation in the footer before OQ5-UI-04 gives directional input a visible selection target.

---

## 9. Interaction with existing button navigation

Existing inputs must remain unchanged:

```text
DPad → Navigate*
A    → Accept
B    → Back / Runtime-owned close
LB   → PreviousTab
RB   → NextTab
LT   → no action
RT   → no action
X/Y  → no action
```

If a user physically operates multiple consumed controls at once, the router may enqueue the independent semantic edges that genuinely occurred.

Do not add arbitration across unrelated controls solely to defend against pathological combinations.

The only stick-specific arbitration required is **within one stick activation**, where a diagonal deflection emits one dominant-axis action.

---

## 10. Tests

Extend the existing `OverlayControllerInputRouterTests` rather than creating a second stick-router test framework.

Update the fake source so tests can raise full `ControllerState` values including both sticks.

### 10.1 Left-stick directional mapping

Prove fresh neutral → deflection produces exactly:

```text
Y positive → NavigateUp
Y negative → NavigateDown
X negative → NavigateLeft
X positive → NavigateRight
```

### 10.2 Right-stick directional mapping

Prove the same four directions for `RightStick`.

### 10.3 No repeat while held

For each stick, at minimum prove:

```text
neutral
→ Right beyond activation threshold
→ one NavigateRight
→ repeated StateChanged while still deflected
→ no additional action
```

Then:

```text
return neutral
→ deflect Right again
→ one new NavigateRight
```

### 10.4 Held at capture start

Prove:

```text
LatestState has left stick already Up beyond activation
router.Start()
StateChanged still Up
→ no action
neutral
→ Up again
→ NavigateUp once
```

Repeat for at least one right-stick direction as coverage for independent per-stick arming.

### 10.5 Hysteresis / neutral re-arm

Prove that movement from active into the band between neutral and activation does **not** re-arm.

Conceptually with the recommended constants:

```text
activate at 20_000
→ move to 12_000
→ still disarmed
→ move back to 20_000
→ no second event
→ move to 0
→ re-arm
→ move to 20_000
→ second event
```

### 10.6 Diagonal dominant-axis behavior

Prove one physical stick activation emits only one action.

Examples:

```text
X = 18_000, Y = 24_000 → NavigateUp only
X = -24_000, Y = 18_000 → NavigateLeft only
```

Add at least one equal-magnitude tie case if the implementation explicitly uses the frozen vertical-on-tie rule.

### 10.7 Release gate — left stick

Prove:

```text
left stick outside neutral
→ WaitForConsumedControlsReleaseAsync
→ waiter incomplete
→ left stick enters neutral region
→ ReleasedAfterWait
```

### 10.8 Release gate — right stick

Prove the same for right stick.

### 10.9 Release gate — mixed inputs

Prove the waiter does not complete until **all** consumed input is neutral/released.

Example:

```text
RB held + right stick deflected
→ wait
RB released but stick remains deflected
→ still waiting
stick neutral
→ ReleasedAfterWait
```

This protects the existing OQ4 invariant as the consumed set grows.

### 10.10 Existing regressions

All existing tests must remain green, especially:

- button rising-edge navigation;
- bumper PreviousTab/NextTab;
- held-at-capture-start button behavior;
- DPad/A/B/LB/RB release wait;
- `NotifySourceUnavailable`;
- transport v4;
- Overlay capture retirement;
- presentation pause/resume.

Do not weaken existing button tests to accommodate stick support.

---

## 11. Manual / hardware validation

Use the normal Runtime → Overlay capture path on a supported MSI Claw.

Because OQ5-UI-04 has not yet added visible row selection, use existing logs/diagnostic evidence to validate semantic stick events in this PR.

Validate:

1. Open Overlay with both sticks neutral.
2. Move Left Stick Up → one `NavigateUp` semantic event.
3. Hold Left Stick Up → no repeated event.
4. Return Left Stick to center, then Up again → one new event.
5. Validate Down/Left/Right.
6. Validate the same four directions on Right Stick.
7. Start/open Overlay while a stick is already deflected → no synthetic navigation until center then fresh deflection.
8. Move a stick diagonally → exactly one dominant-axis navigation event.
9. Slight center drift does not produce navigation.
10. LB/RB tab navigation still works.
11. B still closes Overlay.
12. LT/RT/X/Y still do nothing.
13. Close Overlay while Left Stick is held outside neutral → game/Steam QAM behind receives no stick movement until physical stick returns to neutral.
14. Repeat close test with Right Stick.
15. Close while RB is held and a stick is deflected → publisher remains neutral until both are released/neutral.
16. Steam QAM may remain visible behind Addon Overlay; no QamHost visibility/process management is added.

Threshold values may be adjusted during implementation only if hardware evidence shows the recommended values are too sensitive or too insensitive. Keep the policy code-local and simple.

---

## 12. Expected files

Primary expected changes:

```text
src/SteamInputAddonforClaw/Lifecycle/OverlayControllerInputRouter.cs
tests/SteamInputAddonforClaw.Tests/OverlayControllerInputRouterTests.cs
```

Possible comment-only or test-support changes if necessary:

```text
src/SteamInputAddonforClaw/Input/ControllerState.cs
```

No functional change is expected in:

```text
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
src/SteamInputAddonforClaw.Overlay/App.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
```

Do not modify `MsiClawControllerStateMapper` unless a concrete existing normalization bug is demonstrated. Its current signed-axis mapping is already the desired source for this PR.

No settings/persistence changes are expected.

No Runtime controller-owner/presentation-owner architecture changes are expected.

---

## 13. Build / verification

Required before PR completion:

```text
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test
git diff --check
```

If the full suite exposes an unrelated flaky failure, reproduce it on unchanged current `main` before treating it as unrelated.

No hardware-only behavior should be marked validated unless it was actually tested on a supported MSI Claw.

---

## 14. Acceptance criteria

The PR is complete when all of the following are true:

- [ ] Left Stick produces semantic Up/Down/Left/Right navigation.
- [ ] Right Stick produces semantic Up/Down/Left/Right navigation.
- [ ] Both sticks use the existing normalized `ControllerState`; no raw DirectInput mapping is duplicated.
- [ ] `.Overlay` remains protocol v4.
- [ ] No raw stick/ControllerState payload crosses `.Overlay`.
- [ ] One stick deflection emits at most one dominant-axis action.
- [ ] A held stick does not repeat navigation.
- [ ] A stick must return to neutral/deadzone before it can emit again.
- [ ] A stick already deflected at capture start emits nothing until neutral then freshly deflected.
- [ ] A small local activation/neutral threshold policy exists and is not persisted/user-configurable.
- [ ] Existing DPad/A/B/LB/RB semantics remain unchanged.
- [ ] LT/RT/X/Y remain unconsumed/no-op.
- [ ] Release-to-resume waits for Left Stick neutrality.
- [ ] Release-to-resume waits for Right Stick neutrality.
- [ ] Release-to-resume waits for all consumed buttons and sticks together.
- [ ] `SourceUnavailable` still terminates the release wait without resuming against dead input.
- [ ] No hold-repeat/timer/gesture framework is introduced.
- [ ] No second controller-input owner/session is introduced.
- [ ] No row-selection/slider/Shortcut UI behavior is added yet.
- [ ] No PID/DirectInput/HidHide/VIIPER presentation behavior changes.
- [ ] Release build, full tests, and diff check pass.

---

## 15. Explicit non-goals

Do not include any of the following in OQ5-UI-03:

- held stick repeat;
- DPad held-repeat;
- input acceleration;
- gesture recognition;
- radial angle sectors;
- user-configurable Overlay deadzone;
- per-game stick-navigation settings;
- raw axis transport;
- `.Overlay` protocol bump;
- logical row selection/highlight;
- auto-scroll / bring-into-view;
- slider/toggle interaction;
- Shortcut tile navigation;
- tab-order persistence;
- feature bindings;
- LT/RT behavior;
- X/Y behavior;
- Steam-QAM visibility management;
- Game Bar policy changes;
- window geometry/animation changes;
- new controller/input manager abstractions.

---

## 16. Overengineering guard

Protect real product behavior:

- ordinary analog stick jitter;
- a stick already held when Overlay opens;
- diagonal stick use;
- closing Overlay while a stick remains physically deflected;
- physical input source loss while release is pending;
- existing button navigation/release behavior.

Do **not** add complexity for speculative instruction-level interleavings.

Target architecture after this PR remains:

```text
one physical input owner
→ one OverlayControllerInputRouter
→ one small button + stick semantic classification path
→ one existing semantic delivery channel
→ one existing OQ4 release waiter
```

No additional authority, manager, epoch, barrier, or state-machine layer is required.
