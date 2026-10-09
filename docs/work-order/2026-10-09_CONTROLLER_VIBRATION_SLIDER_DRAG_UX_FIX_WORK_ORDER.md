# Work Order — Fix Controller Vibration Slider Drag Interruption and Stale Async Commit UI

**Date:** 2026-10-09  
**Repository:** `onehoon/SteamAddonforClaw`  
**Implementer:** Local Codex  
**Implementation scope:** One small, independent UI-only PR; no native controller, Runtime routing, or firmware protocol modifications  
**Physical hardware testing:** Performed by the user **after merge**; not a local-Codex implementation requirement, CI prerequisite, or PR review blocker.

## 1. Observed problem and goal

On the **Controller → Vibration Strength** section, a user drags either motor's slider, **holds the pointer still while still pressed**, then attempts to resume dragging without releasing it. The thumb stops following the pointer; releasing and pressing again is required. Test controls are visually disabled while the edit is pending. The user asks to compare this behavior with the **Device → TDP** controls and remove unnecessarily aggressive UI locking/debounce.

**Goal:** Continuous, uninterrupted vibration-slider pointer gestures; commit only an intentional settled final **pair**; preserve the latest displayed user edit despite in-flight responses. Keep physical motor **Test** semantically safe without creating a new state machine or additional controller authority.

This is a **real user-visible drag regression**, not a theoretical timing race.

## 2. Read existing architectural contracts first

Read and comply with the current code and documents, in this order:

1. `docs/Full 1902 Implementation/README.md` — precedence.
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`.
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`.
4. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`.
5. `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`.
6. `docs/work-order/FULL1902_CONTROLLER_VIBRATION_PRODUCTION_PERSISTENCE_LIFECYCLE_WORK_ORDER_2026-10-04.md`.
7. The latest `src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml[.cs]`, `ControllerVibrationStrengthDebounce.cs`, `DevicePage.xaml.cs`, and their existing tests.

Full1902 is a standalone application; CTW integration is **out of scope**. The persistent elevated Runtime owns PID1902, HidHide, VIIPER and device writes; Main UI only edits desired values through existing frontend RPCs. Supported product environment: one Windows administrator user, one interactive session.

## 3. Root cause — concrete code paths (verified against current main)

### 3.1 Vibration UI

`src/SteamInputAddonforClaw.UI/Views/ControllerVibrationStrengthDebounce.cs`:

```csharp
internal static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(500);
```

The debounce is driven solely by `Slider.ValueChanged`; it has **no pointer-held / drag-in-progress awareness**.

`src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs`:

```csharp
private void VibrationStrengthSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs args)
{
    // ... source guards include _vibrationMutationInProgress ...
    _vibrationDebounce?.Schedule(left, right);
    UpdateVibrationControls();
}

private async Task CommitVibrationStrengthAsync(int leftPercent, int rightPercent)
{
    _vibrationMutationInProgress = true;
    UpdateVibrationControls();
    // ... frontend RPC ...
    ApplyVibrationStrengthSnapshot(result.Snapshot, preserveDraft: false);
    // ...
}
```

`UpdateVibrationControls()` sets **both sliders' `IsEnabled=false`** while `_vibrationMutationInProgress` or `_vibrationTestInProgress` is true. Holding a thumb still for 500 ms allows the debounce to fire **mid-gesture**, the RPC disables the slider, and WinUI loses continuous thumb interaction. The RPC completion then applies the returned older snapshot with `preserveDraft:false`, possibly snapping the thumb to a stale value.

`VibrationStrengthSlider_ValueChanged` also ignores edits while `_vibrationMutationInProgress`. Merely changing 500 ms to 300 ms **makes premature mid-gesture commits more likely**, and does not solve the design defect.

### 3.2 TDP comparison

`src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs`:

- Uses a 300 ms debounced edit path for TDP.
- Maintains local dirty draft + generation, and uses `ShouldPreserveDirtyDraft` when a pending RPC returns.
- `RunTdpMutationAsync` does **not** set the mutation busy state that disables all TDP sliders; `SetTdpMutationBusy` is used for the separate enable/disable toggle operation.

TDP is a **comparison for preserving user editing**, not a request to port the TDP implementation or change its behavior. Its generation machinery should not be transplanted wholesale.

### 3.3 Test button and toggle distinction

In `ControllerPage.xaml`, vibration has two **Test buttons**, not an independent vibration On/Off toggle. The nearby **`ControllerLedEnabledToggle`** belongs to *Joystick LED*. `UpdateVibrationControls` updates only the vibration sliders and vibration Test buttons: **it does not disable the LED toggle**. Do not bind LED controls to vibration editing or change the LED surface as part of this fix.

A Test button must not dispatch a motor pulse for a pair that is still uncommitted. It is acceptable to remain disabled during an unsettled draft, pointer gesture, pending apply, or physical Test; once those have completed successfully and the hardware is available, re-enable promptly. The disabled appearance is **not** the root cause of slider discontinuity.

## 4. Target behavior

1. **Pointer/touch/pen thumb gesture:** Drag freely, hold still for any duration (including **>500 ms**), continue moving **without releasing**. No firmware settings commit begins while that gesture is active.
2. **Gesture completion:** On a genuine pointer release / slider drag completion, submit the **latest complete left/right pair** once. Handle pointer capture loss/cancellation so no draft is permanently stuck; avoid double commits if several completion notifications arrive.
3. **Keyboard/controller/UI Automation edits:** Remain supported even without a pointer gesture. Use a modest **300 ms idle debounce**, matching the TDP settle interval; no busy-wait, per-keystroke firmware write, or requirement for pointer input.
4. **During the save/apply RPC:** Sliders remain interactive and can show new draft values immediately. An older server response must not overwrite a newer local edit or cause a thumb jump.
5. **Ordered persistence:** Never let an older async save completion win over the user's newer final desired pair. At most one vibration-setting RPC should be active per Controller page; coalesce subsequent edits into the **latest pair** and commit it once the active RPC completes **and the current gesture/idle settle has ended**. Reuse the existing debounce + one in-flight flag; do not introduce a separate scheduler/manager.
6. **Test buttons:** Keep feature gating and physical Test exclusivity. A Test cannot start while a pointer gesture, unsettled draft, vibration-settings apply, or Test is active; it becomes available again after applying the settled pair and meeting `TestAvailable`. Never physically test values only visible in an unsaved draft.
7. **Failure/unavailable:** Keep the user's newest draft visible on a stale/error response when appropriate; show existing error status rather than claiming the latest settings applied. On a genuine Runtime disconnect, loss of snapshot availability/ownership, or page teardown, use the existing unavailable/fail-close behavior and cancel any pending delayed writes. No stale retry loop.
8. **Feature behavior:** Left/right 0–100% paired settings, persisted defaults, test output/STOP behavior, existing A2VM 8 + CG3EM model support, and all Runtime hardware lifecycle writes remain unchanged.

## 5. Minimal implementation guidance

### 5.1 Keep user input separate from transport state

In `ControllerPage.xaml.cs`, track only the transient UI facts needed to protect real gestures and in-flight edits:

- Whether a vibration slider pointer drag is in progress.
- Whether the local pair is dirty/pending application.
- Whether a vibration setting RPC is already running.

Prefer the existing slider/debounce and `_vibrationMutationInProgress` fields; add the minimum additional state for pointer gesture + latest pair. A `long` edit sequence is justified **only if** necessary to distinguish a genuinely newer user edit from an older response; for these two sliders, comparing a submitted pair to the current dirty pair is also acceptable if it safely preserves user intent. Do not create another abstraction just to mirror TDP.

**Illustrative gating (not a literal drop-in diff):**

```csharp
// User-owned draft is always updated first; firmware/persistence is separate.
var pair = (Left: ToPercent(LeftVibrationStrengthSlider.Value),
            Right: ToPercent(RightVibrationStrengthSlider.Value));
ShowDraftPercentages(pair);
RememberLatestDraft(pair);

if (IsVibrationPointerGestureActive)
{
    _vibrationDebounce?.CancelPending();
    // Do not commit or disable the sliders while the pointer is down.
}
else
{
    _vibrationDebounce?.Schedule(pair.Left, pair.Right);
}
```

On pointer completion, if the draft differs from the currently confirmed pair, flush/schedule **one** commit for the final pair. Avoid commit while the same thumb is still captured.

### 5.2 Wire the actual WinUI Slider gesture end correctly

`ControllerPage.xaml` currently registers only `ValueChanged`, not pointer/drag completion events.

Use the smallest **reliable WinUI 3** event wiring, taking account of WinUI `Slider`'s inner `Thumb` handling. Where appropriate, use handled-events-too `AddHandler` or the actual thumb drag events; do not assume a normally registered `PointerPressed` on the outer Slider will always fire if the Thumb consumes it. Also support slider **track clicking** and `PointerReleased`, `PointerCanceled`, `PointerCaptureLost`, not just thumb drags. The completion routine must be idempotent and must not leave a held flag stuck after capture loss.

Do **not** call `CapturePointer` on the page or take pointer capture from the WinUI Slider: preserve the native thumb's interaction.

A genuine page departure or Runtime/unavailable transition must not leave a deferred mutation running after the UI has been disposed. Prefer the page's existing activation/lifetime conventions; avoid adding a global input service.

### 5.3 Do not disable sliders solely for saving

The existing code effectively does:

```csharp
var operationInProgress = _vibrationMutationInProgress || _vibrationTestInProgress;
LeftVibrationStrengthSlider.IsEnabled =
    _vibrationSnapshot.Available && _vibrationSnapshot.Writable && !operationInProgress;
RightVibrationStrengthSlider.IsEnabled =
    _vibrationSnapshot.Available && _vibrationSnapshot.Writable && !operationInProgress;
```

Replace this gating so **`_vibrationMutationInProgress` alone does not disable sliders**, and remove that flag from `ValueChanged`'s user-edit rejection. Keep unavailable/read-only states and genuinely exclusive physical-Test operations guarded. **Do not** enable writes to an unavailable Runtime.

### 5.4 Preserve newest user intent when RPC returns

The current unconditional call:

```csharp
ApplyVibrationStrengthSnapshot(result.Snapshot, preserveDraft: false);
```

must no longer overwrite an edit made during the in-flight RPC.

Suggested small-path rules:

- When the returned snapshot corresponds to the latest settled draft and no newer edit is pending, it may update both sliders.
- If newer input exists or a pointer gesture is active, update the available/writable authority information **without replacing displayed slider values**. Reconcile/submit the latest draft **after** the previous RPC finishes and the gesture settles.
- On the same thread, serialize normal UI-requested vibration strength RPCs (do not issue overlapping mutations for old/new pairs).
- If a save fails and no newer edit exists, surface the existing error and refresh authoritative state as currently designed. If a newer draft exists, **do not silently discard it**, do not declare it applied, and do not repeatedly retry hardware writes.
- A late `RefreshVibrationStrengthAsync` must use the same draft-preservation rules while the user is interacting.

There is no need to replace `ControllerVibrationStrengthDebounce` with a generic scheduler, port TDP's generation manager, or introduce a second persistence authority.

### 5.5 Test button and LED controls

Keep `CanRunVibrationMotorTest` and `TryStartVibrationMotorTest` as the single guard for physical test execution. Add a held-gesture / dirty-draft condition **if needed**, not a parallel authoritative test state.

```csharp
// Illustrative only: the pending-draft check must account for a
// pointer-held draft even if the debounce timer has been cancelled.
var canTest = snapshot.TestAvailable
              && !pointerEditing
              && !hasUncommittedDraft
              && !mutationInProgress
              && !testInProgress;
```

Keep the real `StopConfirmed` safety and Test exclusivity untouched.

**Do not change `ControllerLedEnabledToggle`, brightness slider, LED color picker, or their frontend save chain.** The LED toggle's perceived disabled state should only be investigated separately if a reproducible LED-specific bug is later provided.

## 6. Tests — software/CI only

Update existing `tests/SteamInputAddonforClaw.UiTests/ControllerVibrationStrengthUiTests.cs` and, only if necessary, add focused UI-logic tests using existing test patterns. **Delete/update stale source-shape assertions** that hard-require `ApplyVibrationStrengthSnapshot(..., preserveDraft:false)`, 500 ms, or `!operationInProgress` for slider availability. Assert **behavior**, not an old implementation shape.

Required deterministic scenarios:

1. **Hold while dragging:** Begin drag, change value, advance a controlled timer by more than 500 ms **without release**. Zero persistence RPCs; both sliders stay enabled; the thumb and percent text continue to follow changes.
2. **Release:** One final left/right pair is committed after a genuine gesture release; duplicate release/capture-lost notifications do not double-save.
3. **Keyboard:** Non-pointer value changes settle once after 300 ms; intermediate edits coalesce into the final pair.
4. **In-flight edit:** Commit pair A, immediately edit to pair B while A's RPC is still running. Sliders stay editable; A's completion does not overwrite B; B is eventually persisted after its gesture/settle.
5. **No overlapping RPCs:** Hold A's RPC, generate several new drafts, release/settle; only one final newer pair is submitted when A completes.
6. **Test guard:** While held/dirty/applying/testing, Test dispatch is denied; once the confirmed pair is active, Test dispatch occurs exactly once; a Test failure still follows the current safe STOP/recovery behavior.
7. **Lifecycle unavailable:** When a snapshot reports genuinely unavailable/read-only, controls reflect it and zero further hardware-setting RPCs are sent. After recovery, normal frontend refresh governs availability.
8. **Regression:** Existing A2VM 8/CG3EM vibration strength values and IPC contracts remain unchanged. TDP drag/debounce and LED brightness/color/toggle continue to behave exactly as before.

Run normal solution build + relevant UI tests and automated frontend tests. **No actual Claw, virtual device, HID motor test, or real pointer automation is required for Codex/CI**. A test being possible does not justify speculative lock/epoch/state-machine additions.

## 7. Non-goals and file scope

Preferred change scope:

```text
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs
src/SteamInputAddonforClaw.UI/Views/ControllerVibrationStrengthDebounce.cs
tests/SteamInputAddonforClaw.UiTests/ControllerVibrationStrengthUiTests.cs
```

Only edit neighboring UI tests if their old shape assertions now conflict with the intended behavior.

**Do not modify**:

- `MsiClawVibrationStrengthClient`, vibration profile packets/policy, `SyncToROM`, motor Test/STOP HID path.
- Runtime-owned PID1902 physical device identity/ownership, HidHide, VIIPER, Xbox360↔SteamDeck routing, suspension/PnP recovery or teardown.
- Existing frontend RPC schema, persist/settings structure, controller-feature capability policy.
- TDP/LED production behavior or the Overlay UI/slider implementation.
- UX beyond this existing Controller page; no new tab, feature flag, generic slider framework, or drag manager.

## 8. User acceptance after merge (not a PR blocker)

On a real A2VM 8 or CG3EM:

1. Drag Left from 50→70, hold the pointer for >1 s without releasing, then continue to 20: **thumb continues moving**, percentage updates, no mid-drag lock/jump. Release: final left/right pair is applied.
2. While a save is completing, rapidly drag to another value: it remains visible and becomes the last persisted pair; no flick-back.
3. Repeat with Right, track clicks, keyboard arrows, and touch if available.
4. Confirm Test uses the latest **applied** motor settings and STOP completes.
5. Confirm normal Runtime restart restores the last saved pair; LED toggle remains independent.
6. Check that existing Full1902 controller input and virtual presentation are unaffected.

The user owns these practical tests after merge. **Do not hold merge for the user's physical test availability**.

## 9. Completion criteria

- The specific **press → move → hold → move → release** scenario remains continuously editable without a thumb lock or intermediate commit.
- A gesture's final pair is saved once; keyboard edits remain debounced and coalesced.
- No stale RPC response replaces newer displayed draft values; no overlapping pair mutation RPCs.
- Sliders are no longer disabled by an ordinary asynchronous save.
- Test remains safe, and LED toggle/TDP are left unchanged.
- All targeted build/automated tests pass; changes fit in one small PR without extra lifecycle machinery.
