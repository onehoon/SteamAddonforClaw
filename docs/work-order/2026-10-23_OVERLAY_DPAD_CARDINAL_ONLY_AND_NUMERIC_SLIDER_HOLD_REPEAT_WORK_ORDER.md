# Work Order — Overlay D-Pad Cardinal-Only Navigation and Numeric Slider Hold-Repeat

**Date:** 2026-10-23  
**Status:** Ready for local Codex implementation  
**Repository:** `onehoon/SteamAddonforClaw`  
**Target:** `main`  
**Reviewed main baseline:** `1eaf5092ab5add9c67ae9504d2ab133f30391a01`  
**Implementation scope:** **One focused PR**, preferably two logically separated commits within that PR  
**Product:** Standalone Full PID1902 Steam Addon for Claw; **no ClawTweaks/CTW integration**

## 0. User-observed problem and frozen decisions

On the Overlay, D-pad adjustment of the **Intel FPS Limit** numeric slider sometimes moves the logical selection to a different row after one or two steps. Holding D-pad Left/Right currently advances only one step rather than repeating. The physical reproduction is user-reported; the relevant problematic and missing behavior is independently confirmed in code.

This work order implements two deliberately coordinated, narrow fixes:

1. **D-pad cardinal-only** semantics for **every Overlay tab and page**: Up, Down, Left, Right only; diagonals must generate **no navigation**.
2. **D-pad Left/Right press-and-hold repeat exclusively for an actually selected, enabled native numeric slider row** (`OverlayNumericSliderRow`).

All other rows, shortcut tiles, tab-order choices, toggles, numeric steppers (`OverlayValueRow`), and non-slider controls remain **single-step per press**. No global navigation auto-repeat, no repeat on Up/Down, no stick hold-repeat.

Expected UX:

```text
D-pad Right on an enabled numeric slider
  -> immediately +1 configured semantic step
  -> still held for 300 ms -> first repeat
  -> continue every approximately 75 ms while the same cardinal input is held
  -> stop on release, diagonal transition, overlay retirement, or source loss

D-pad Right on a non-slider row
  -> existing single step/action
  -> no hold-repeat even after 300 ms

D-pad Right -> Up+Right diagonal without full release
  -> initial Right action remains valid
  -> diagonal produces NO Up/Right action and STOPS any Right hold-repeat
  -> even if returning to cardinal Right/Up, suppress new D-pad actions until FULL neutral
  -> after complete D-pad release, a new cardinal press is admitted

Neutral -> diagonal
  -> zero action
  -> remain disarmed until full neutral
```

The timing targets deliberately match the existing pointer `RepeatButton` used for numeric `-/+` controls: `Delay = 300`, `Interval = 75` milliseconds. No acceleration is requested.

## 1. Required source and authority reading

Review the **latest** target-branch sources before editing; this document's baseline is a review snapshot, not permission to overwrite later changes.

**Full1902 mandatory authority:**

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`

**Overlay context:**

- `docs/overlayui/README.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/overlayui/OQ5_UI_03_DUAL_STICK_DIRECTIONAL_NAVIGATION_WORK_ORDER.md`
- `docs/overlayui/OQ5_UI_04_LOGICAL_ROW_SELECTION_SCROLLING_WORK_ORDER.md`
- `docs/overlayui/OQ5_UI_06_SLIDER_ROW_PRIMITIVE_WORK_ORDER.md`
- `docs/overlayui/OQ5_UI_07_SHARED_DELAYED_SLIDER_COMMIT_WORK_ORDER.md`
- `docs/work-order/OVERLAY_COMPACT_WIDTH_SECTION_HIERARCHY_AND_HOLD_REPEAT_POLISH_WORK_ORDER_2026-09-24.md`

**Current implementation and tests:**

- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawControllerStateMapper.cs`
- `src/SteamInputAddonforClaw/Lifecycle/OverlayControllerInputRouter.cs`
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs` (capture wiring only; no redesign)
- `src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeOverlayServer.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeOverlayClient.cs`
- `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayRowSelection.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayNumericSliderRow.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayValueRow.cs`
- `tests/SteamInputAddonforClaw.Tests/OverlayControllerInputRouterTests.cs`
- `tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/OverlayRowSelectionTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/OverlayNumericSliderRowTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs`

## 2. Verified code facts and root cause

### 2.1 The diagonal bug is at the Overlay semantic-input boundary

`MsiClawControllerStateMapper.TryMap` maps physical POV diagonals to **two** normal gamepad direction flags:

```csharp
var up = pov is 0 or 4500 or 31500;
var right = pov is 4500 or 9000 or 13500;
var down = pov is 13500 or 18000 or 22500;
var left = pov is 22500 or 27000 or 31500;
```

This mapping is valid for the controller's **game-facing state** and must NOT be altered.

However, `OverlayControllerInputRouter.OnStateChanged` currently iterates each D-pad direction independently:

```csharp
if (held(buttons) && !held(_previousState.Buttons))
    _actions.Writer.TryWrite(action);
```

Therefore Right (POV 9000) -> Up+Right (POV 4500) can produce a new `NavigateUp` edge, causing real Overlay logical-row movement. Starting in a diagonal can produce **two** actions. Both violate the requested Overlay-only UX.

### 2.2 No controller hold-repeat currently exists

The input source emits `StateChanged` on actual changes, not continuously simply because a button stays down. The router generates button actions on rising edges only. Existing stick arming also emits one dominant-axis action per deflection. Therefore a held D-pad direction does not currently produce repeated actions.

### 2.3 The frontend alone knows slider eligibility

The Runtime router knows `ControllerState`, not selected UI rows. The transport currently carries **semantic** `OverlayNavigationAction` values only.

On the frontend:

- `OverlayWindow.AdjustSelectedRow` handles `NavigateLeft/Right` across multiple row kinds.
- `OverlayNumericSliderRow` is the **real native numeric slider** used for e.g. Device/Profile numeric sliders with a trailing debounce, including Intel FPS Limit.
- `OverlayValueRow` may be a numeric **stepper** or discrete choice and has a separate pointer `RepeatButton` implementation for numeric `-/+`; it must **not** gain D-pad repeat through this change.
- `OverlayRowCapabilities` is the existing minimal row-eligibility seam.
- `OverlayQuickSettingsPageBinding` owns pending value preview/commit; it must retain its current trailing debounce, pending draft and settlement rules.
- `OverlayWindow` uses its own logical row selection; WinUI keyboard focus, `Focus()`, activation and key synthesis are not the answer.

### 2.4 Transport baseline

`OverlayTransportProtocol.CurrentVersion == 20`. `OverlayWireMessageKind.Navigation` transports `OverlayNavigationAction` with no raw button states. Existing `NamedPipeOverlayServer.SendNavigationAsync` sends only to a Ready + Visible overlay and uses the existing write gate. The Overlay app's `HandleNavigationAsync` dispatches to the existing UI queue.

### 2.5 Existing hold-repeat is unrelated

The 2026-09-24 polish work order added a native WinUI `RepeatButton` to **pointer/touch numeric steppers** (`OverlayValueRow`) with 300/75 ms. It explicitly excluded controller auto-repeat. Preserve that behavior.

## 3. Design decision: minimal semantic repeat signal; frontend enforces eligibility

**Preferred implementation:** extend the existing `OverlayNavigationAction` enum with two explicit **repeat-only** actions:

```csharp
// Append; keep existing numeric enum values stable.
RepeatNavigateLeft,
RepeatNavigateRight
```

The exact naming can follow established conventions, but the two new actions must be distinguishable from an ordinary fresh `NavigateLeft/Right` **without sending raw button/POV state**.

- **Runtime router:** owns D-pad classification and physical hold timing; emits one normal initial directional action and then repeat-only Left/Right actions while valid.
- **Overlay frontend:** accepts a repeat-only action **only** if its **original initial press** targeted an enabled `OverlayNumericSliderRow` and the **same row remains selected in the same tab**. Otherwise silently ignore it.
- **No other frontend type** may treat repeat-only actions as general `NavigateLeft/Right`.
- No UI query/ack or Runtime knowledge of selected rows.
- No raw `ControllerState`, per-button mask, POV angle, timestamps, row IDs or arbitrary payloads on the pipe.
- No new pipe message kind or navigation DTO; append enum values, bump `.Overlay` protocol **20 -> 21** and update its version notes/tests. Do not change the separate Main UI frontend protocol.
- Preserve existing `NamedPipeOverlayServer/Client` transport, visibility and command serialization. Modify their validation only as needed to admit the new known semantic values, never relax unrelated payload validation.

A controller repeat tick being generated when a non-slider has focus is harmless **only because the frontend strictly rejects it**. Do not introduce a reverse IPC eligibility request or subscription to optimize this small bounded input rate.

## 4. Runtime: one cardinal-only D-pad gesture at a time

In `OverlayControllerInputRouter`, separate **the D-pad direction group** from independently edge-triggered A/B/LB/RB buttons.

### 4.1 Cardinal classifier

A D-pad group has exactly five relevant states:

```text
Neutral: none of Up/Down/Left/Right flags
Cardinal: exactly one direction flag
Diagonal/invalid: two or more direction flags
```

Both physical diagonals and inconsistent multi-direction state are invalid for Overlay navigation.

Suggested tiny helper logic:

```csharp
// Conceptual only; align with current GamepadButtons implementation.
var count = (buttons.DPadUp ? 1 : 0)
          + (buttons.DPadDown ? 1 : 0)
          + (buttons.DPadLeft ? 1 : 0)
          + (buttons.DPadRight ? 1 : 0);

if (count == 0)
    ReArmDpadAfterNeutral();
else if (count != 1)
    CancelDpadRepeatAndKeepDisarmed();
else if (_dpadArmed)
    AcceptOneFreshCardinalPressAndDisarm();
```

`_dpadArmed` is only an Overlay input gesture fact, not a new controller authority or lifecycle state machine.

Rules:

- Start capture with D-pad already held: do **not** synthesize a press; require full D-pad neutral before any new action.
- Fresh neutral -> cardinal: exactly one ordinary action; disarm until full neutral.
- Fresh neutral -> diagonal: **zero** actions; disarm until full neutral.
- Cardinal -> diagonal: cancel pending repeat immediately; do not emit the newly asserted second direction.
- Cardinal -> different cardinal without observing neutral: no new action; cancel repeat rather than silently switching direction.
- Diagonal -> cardinal without observing neutral: still blocked.
- Only full D-pad neutral permits another accepted direction.
- Up and Down remain single-shot; only accepted Left/Right can schedule a repeat.
- A/B/LB/RB remain independent rising-edge controls. LT/RT/X/Y remain unmapped.
- The existing **both-stick dominant-axis/hysteresis** navigation is unchanged, including the fact that an analog diagonal resolves to one dominant axis; analog is not a D-pad and receives no hold-repeat.
- Keep `AnyConsumedHeld` / `AnyConsumedInputActive` semantics: **any** held D-pad direction, even a suppressed diagonal, still counts as consumed while waiting for the Overlay publisher release gate.

Do **not** modify `MsiClawControllerStateMapper` or alter physical/game-facing D-pad mapping.

### 4.2 Repeat schedule and cancellation

Production timing (fixed, code-local):

```text
accepted initial D-pad Left/Right press -> t = 0: NavigateLeft/Right
still cardinal at 300 ms           -> RepeatNavigateLeft/Right
continuing while held              -> every ~75 ms
```

Use a single narrow cancellable asynchronous hold loop inside the **existing router**, not a new shared service or generic input engine. The source's `StateChanged` is edge/change-driven, so waiting for another state callback cannot implement this feature; a small delayed task is actually necessary.

Requirements:

- Never `await Task.Delay`, IPC, or UI work inside the physical DirectInput `StateChanged` callback or while holding `_sync`.
- Every emitted repeat must still be admitted by the existing `_accepting` / source-available / current cardinal held-direction facts; do not enqueue if no longer valid.
- Stop promptly on full release, diagonal, changed cardinal, `StopAcceptingNavigation`, `NotifySourceUnavailable`, `Dispose` and any existing capture retirement path.
- Reopening Overlay is a fresh router lifetime; no old timer should generate actions in the new capture.
- Queue repeat actions via the existing low-rate delivery channel; no raw input IPC and no new delivery worker.
- Do not repeat A/B/LB/RB/Up/Down or stick navigation.
- Keep repeat cadence bounded and do not introduce acceleration.
- Avoid pathological timing defenses: do not add an epoch/barrier/input manager/extra lock exclusively for contrived scheduler interleavings. A single local cancellation mechanism and guarded state check are sufficient.
- Existing capture dismissal, consumed-control release and publisher neutral/resume ordering remain authoritative; timer cleanup must not hold the release gate hostage.
- If the target reaches min/max, the existing slider model clamps and suppresses duplicate value requests; no special Runtime range knowledge is needed.

## 5. Overlay: strict slider-only repeat admission

### 5.1 Use the existing row capability seam

Add one optional/false-by-default property to `OverlayRowCapabilities`, e.g.:

```csharp
internal sealed record OverlayRowCapabilities(
    Func<bool> IsSelectable,
    Action? Activate = null,
    Action<int>? Adjust = null,
    bool AllowsDpadHoldRepeat = false);
```

Set `AllowsDpadHoldRepeat: true` **only in `OverlayNumericSliderRow`**. Require the row to be currently selectable/enabled and adjustable when admitting repeat.

Leave all of the following at the default `false`:

- `OverlayValueRow` **even with** `OverlayValueButtonKind.NumericStepper` (Controller LED and Vibration Strength included);
- `OverlayToggleRow`;
- tab-order rows;
- discrete choice rows;
- Shortcut tiles;
- other Controller/Setting/UI rows.

The shared `OverlayRowSelection` must retain its existing rules for bounds, unavailable rows, normalizing stale selection and no accidental edit on normalization. Do not use `Adjust is not null` as the repeat eligibility test: many non-slider rows also expose `Adjust`.

### 5.2 Arm for the originally selected slider, not a later target

A fresh ordinary `NavigateLeft/Right` is still dispatched through the existing `AdjustSelectedRow` path once. Only if it actually targets an enabled numeric slider should the UI remember one **transient** eligible target: the active tab, the actual row identity/reference, and the direction. This is an Overlay window interaction fact only.

A following `RepeatNavigateLeft/Right` must:

1. match that original direction;
2. find that **same currently selected row** in that **same tab** (object reference or the smallest equivalent existing row-identity seam);
3. confirm `AllowsDpadHoldRepeat`, `IsSelectable()`, and an `Adjust` callback are still valid;
4. adjust once by exactly one semantic step using the existing row model and draft/commit path.

Otherwise **ignore the repeat**, with **no fallback target adjustment, navigation or selection normalization**.

Clear/invalidate the transient eligibility when it is naturally known to be stale: next fresh horizontal press, logical selection movement/pointer reselection, tab switch, row replacement/rebuild, unavailable selected row, Hide/Show or equivalent existing UI reset. The same-row check should already cover many of those cases; do not create duplicate event subscriptions purely to mirror facts. A stable value-only refresh that **reuses the same row** may continue repeating; a shape rebuild replacing the row must not let a held press edit a different row.

Especially prevent:

```text
Start Right hold on a non-slider
-> move tab/selection to numeric slider while held
-> repeat must NOT begin editing the new slider

Start Right hold on slider A
-> selection moves or page changes before next repeat
-> no repeat may edit slider B / a Shortcut tile / a numeric stepper
```

Keep the existing selected-row visual and scroll behavior; do not introduce actual WinUI keyboard focus, `Focus()`, window activation, `SendInput`, or a second focus manager.

### 5.3 Dispatch

Extend `App.HandleNavigationAsync`:

```csharp
case OverlayNavigationAction.RepeatNavigateLeft:
    _window?.RepeatAdjustSelectedNumericSlider(-1);
    break;
case OverlayNavigationAction.RepeatNavigateRight:
    _window?.RepeatAdjustSelectedNumericSlider(+1);
    break;
```

The method names are suggestions. The repeat-only handler must **not** call the general-purpose `AdjustSelectedRow` unconditionally.

Retain existing normal `NavigateLeft/Right` behavior for all non-slider rows and the Shortcut grid.

### 5.4 Delayed mutation remains unchanged

For Intel FPS Limit and other native numeric sliders:

- each repeat advances the immediate local preview using `OverlayValueModel.RequestAdjust`;
- `OverlayQuickSettingsPageBinding.ScheduleSlider` still applies the current feature-provided trailing debounce (Intel FPS Limit currently 300 ms);
- every new preview value replaces the previous pending desired value;
- releasing the D-pad leaves the **last value** to settle through the existing debounce/flush policy;
- do not issue one hardware/runtime mutation per repeat tick;
- preserve pending draft and authoritative readback behavior across refresh;
- respect each feature's min/max/step, availability, and commit group;
- preserve current user-dismiss flush vs forced Hide/cancel policy.

Do not change the 300 ms **input hold delay** based on the independent 300 ms **slider commit debounce**. Their equal numeric values do not mean they are the same timer.

## 6. Transport compatibility

Append `RepeatNavigateLeft/Right` to `OverlayNavigationAction`, preserving all existing values. Bump `OverlayTransportProtocol.CurrentVersion` from 20 to **21**, document the two new semantic actions, and update pinned-version tests.

Retain `OverlayWireMessageKind.Navigation` and the existing message shape, write gate and Ready/Visible checks. Unknown or malformed frames must not accidentally become valid; do not weaken other wire validators. The Overlay Runtime and Overlay frontend are shipped together; no backward-compatibility shim is requested.

No changes to `FrontendTransportProtocol`, Steam QAM, Steam Deck virtual navigation, physical DirectInput mapping, device detection, or settings persistence.

## 7. Tests and acceptance criteria

Add focused automated tests. **Do not require physical hardware testing for completion, PR approval or merge.** Hardware confirmation of thumb/POV behavior is the user's post-merge role.

### 7.1 Runtime router unit tests

Extend `tests/SteamInputAddonforClaw.Tests/OverlayControllerInputRouterTests.cs`:

1. Each fresh neutral -> Up/Down/Left/Right emits exactly one original semantic action.
2. Neutral -> Up+Right / Down+Left emits **zero** actions.
3. Right -> Up+Right emits **one initial Right only**, no Up; diagonal stops ongoing repeat.
4. Left -> Down+Left emits **one initial Left only**, no Down; diagonal stops ongoing repeat.
5. Diagonal -> cardinal without neutral still emits nothing; full neutral -> new cardinal works again.
6. Capturing while held/diagonal never fabricates a press before an observed neutral.
7. Ordinary D-pad Up/Down remains single-shot even when held.
8. Held D-pad Left/Right produces an immediate ordinary action, then repeat-only actions after the intended delay; release stops repeats. Test with tolerances / signal-based waits, **not** brittle exact scheduler tick counts.
9. `StopAcceptingNavigation`, `NotifySourceUnavailable` and `Dispose` promptly stop further repeats. Existing release-wait outcome remains correct for suppressed diagonals.
10. A/B/LB/RB edge actions and existing stick dominant-axis/hysteresis behavior remain unchanged. No stick repeat.

### 7.2 Frontend eligibility / logical-selection tests

Extend `tests/SteamInputAddonforClaw.UiTests/OverlayRowSelectionTests.cs` and/or a small focused test for the **pure repeat-eligibility seam**. Avoid constructing an actual WinUI window in headless CI unless the project's existing test harness already supports it. Add minimal source/compiled wiring coverage in `OverlayNumericSliderRowTests` / `OverlayDeviceRendererWiringTests` as appropriate.

Verify:

1. An initial horizontal action on the enabled native numeric slider enables only that original target for repeat; each admitted repeat adjusts one step.
2. Initial horizontal action on toggle, discrete choice, `OverlayValueRow` numeric stepper, tab-order or Shortcut never enables repeat.
3. If selected row, tab or direction changes, repeat is rejected without mutating another row.
4. A shape rebuild or target unavailability blocks further repeat, while value-only refresh preserving the same row can remain valid.
5. Releasing/re-pressing the D-pad permits a new eligible press/hold; stale eligibility is not carried across Hide/Show.
6. Normal single-step `NavigateLeft/Right` on non-slider rows and Shortcut continues working.
7. Existing slider clamp/step, availability, no-duplicate-request and delayed commit tests still pass.

### 7.3 Transport tests

Extend `tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs`:

- current Overlay protocol version is 21;
- both new repeat actions round-trip as Navigation messages;
- hidden/unready peers do not receive repeats (as with other navigation);
- malformed or unknown navigation payloads are not silently accepted;
- no change to the separate frontend protocol;
- the existing all-actions / shared-write-gate test still passes with the appended enum members.

### 7.4 Build and review

Run applicable Release build, existing .NET unit/UI-test assemblies and targeted regression tests. Report any environment-only inability to run a command rather than claiming a pass. Keep the diff minimal and review real normal-use failure paths, especially capture close/release and source loss. No requirement to run a handheld, suspend/resume test, or hardware bench before PR merge.

## 8. Non-goals / anti-overengineering

**Do not**:

- add repeat for Up/Down, analog sticks, LB/RB or A/B;
- add repeat for numeric stepper controller actions merely because their **pointer** buttons use RepeatButton;
- change shared game-facing D-pad/POV mapping;
- invent a second physical input reader or native focus/keyboard event injection;
- introduce a generic RepeatManager, controller input service, input epoch, barrier, extra authority, scene graph or new global focus system;
- add UI->Runtime focus/eligibility IPC, raw state transport, acknowledgements, or row-type knowledge to the Runtime;
- rework profile, mutation, renderer, lifecycle, ClawHUD or QAM architectures;
- alter VIIPER/HidHide/PID1901-PID1902 ownership, publisher stop/neutral/release-to-resume, WING policy, startup or recovery;
- add CTW integration.

Implement the smallest testable state needed to enforce **one D-pad gesture until neutral**, **bounded held Left/Right repeat**, and **frontend-only native slider eligibility**.

## 9. Suggested commits, one PR

**Commit 1 — Overlay D-pad cardinal-only filtering**
- One D-pad gesture until full neutral.
- Diagonal suppression globally.
- Existing bumper/action/stick/release gate regressions.

**Commit 2 — Native numeric-slider-only D-pad hold-repeat**
- Router repeat production and cancellation.
- Two semantic repeat-only navigation enum cases; Overlay protocol v21.
- Frontend current-row eligibility and original-target guard.
- Targeted unit, UI wiring and transport tests.

This is **one focused PR**, not two independent PRs; the behavior should land together.

## 10. Definition of done

- [ ] D-pad diagonal never causes Overlay movement or slider adjustment.
- [ ] Cardinal -> diagonal immediately stops a held slider repeat; cardinal cannot re-arm without full release.
- [ ] Any tab/page uses the same cardinal-only D-pad routing policy.
- [ ] Native numeric sliders only: initial ±1 step, then ~300 ms delay and ~75 ms held-repeat.
- [ ] Non-slider D-pad controls and analog navigation remain single-shot.
- [ ] Held repeat cannot start editing a different selected row after tab/selection/row-shape change.
- [ ] Numeric steppers retain their existing pointer RepeatButton and have no D-pad repeat.
- [ ] Existing preview/debounce/last-desired-value behavior remains correct.
- [ ] Overlay close, source loss, teardown and consumed-input release gate retain original lifecycle safety.
- [ ] No physical/game-facing D-pad mapping, virtual presentation or Full1902 ownership change.
- [ ] Applicable automated tests and Release compilation pass, or failures are accurately reported.
- [ ] Hardware/real-device verification is **not** a Codex/PR completion gate; the user handles that after merge.
