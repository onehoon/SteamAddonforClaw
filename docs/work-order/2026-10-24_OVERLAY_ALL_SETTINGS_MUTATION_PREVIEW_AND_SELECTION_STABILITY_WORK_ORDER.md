# Work Order — Overlay-Wide Setting Mutation Preview and Selection Stability

**Date:** 2026-10-24  
**Status:** Ready for local Codex implementation  
**Repository:** `onehoon/SteamAddonforClaw`  
**Code baseline reviewed:** `main` at `98b61428daff79e1b8ba48e9f1dfa8d0449910f3`  
**Recommended delivery:** Two focused implementation PRs (Settings first, Controller second), with Device/Profile/Shortcut/Tab Order regression coverage.  
**Ownership:** Local Codex implements and runs code tests; the user performs physical-device/visual validation **after merge**. Hardware validation is **not** a Codex completion criterion, CI gate, or PR review blocker.

## 0. Purpose and user report

The user reports that the Overlay has visible instability not just on Intel FPS sliders, but also when changing **toggles, numeric values, and discrete choices** inside **Settings → ClawHUD**. The requested audit scope is **every editable Overlay setting**, including Device, Profile, Controller, and Settings child cards.

Fix realistic visual/value rebound and selection/scroll instability during normal UI mutations, while retaining Runtime-authoritative readback and existing feature safety. Do **not** treat every toggle/slider as already proved to flicker; separate confirmed source paths from hypotheses requiring visual validation.

This is a **standalone Full1902 WinUI 3 Overlay frontend** task. CTW integration is out of scope.

## 1. Required source and document review

Read the current implementation again before editing; line numbers below describe the reviewed commit, not an immutable API.

### Product/architecture authority

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md` (frontend/Runtime token and authority boundary)
- `docs/overlayui/OQ5_UI_04_LOGICAL_ROW_SELECTION_SCROLLING_WORK_ORDER.md`
- `docs/work-order/CH_A3_CLAWHUD_MAIN_UI_AND_OVERLAY_SETTINGS_WORK_ORDER_2026-09-22.md`
- `docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR3_SETTING_TAB_ORDER_PARITY_WORK_ORDER.md`
- `docs/work-order/2026-10-23_OVERLAY_DPAD_CARDINAL_ONLY_AND_NUMERIC_SLIDER_HOLD_REPEAT_WORK_ORDER.md` (verify actual filename if renamed)

### Current frontend implementation

- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayRowSelection.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayValueRow.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayNumericSliderRow.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayQuickSettingsPageBinding.cs`
- `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
- `src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs`
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`
- Relevant existing `tests/SteamInputAddonforClaw.UiTests/` and `tests/SteamInputAddonforClaw.Tests/`.

Reference the previous library audit `Overlay-UI-Refresh-Flicker-Audit-2026-10-23.md` for investigative background **only**; its baseline was `8c74a25...` and several findings have already been implemented since then.

## 2. Current code facts and confirmed defect paths

### F1 — Settings → ClawHUD: broad busy-disable and stale-value rebound (confirmed)

`OverlayWindow.ClawHud.cs`:

- `RequestClawHudEnabled` and `RequestClawHudSetting` set the **single** `_clawHudMutationInFlight` flag, then call `RenderClawHudControls()` **before** forwarding the mutation.
- `RenderClawHudControls` computes `available = snapshot != null && !_clawHudMutationInFlight` and applies **all eight** rows from the old authoritative snapshot.
- Hence every mutation temporarily disables Enable HUD, Display Mode, HUD Size, Font, Alignment, Background, Opacity, and Intel VRR Range Fix, even when only one field changed.
- A native ToggleSwitch has already moved to the user's requested `IsOn` before its `Toggled` callback. The immediate old-state `ApplyState` can visibly reverse it, then the mutation result applies the new value. `OverlayValueRow` likewise locally previews a new value before invoking its callback, then `RenderClawHudControls` restores the old snapshot value until completion.
- The controls themselves are retained (not destroyed/recreated); this is a **state/availability/repaint** problem, not ClawHUD full-page rebuilding.

**User-visible risk:** visible on/off or numeric-choice snap-back, disabled-style flashing, and a selected row temporarily becoming unselectable.

### F2 — Settings → ClawHUD: unrelated state refresh clears pending mutation (confirmed code path)

`ApplyClawHudSnapshot` unconditionally sets `_clawHudMutationInFlight = false`. It is called from **both**:

1. `App.HandleClawHudAsync` for independent Runtime-published `ClawHudState`; and
2. `App.ApplyClawHudMutationResult` for the correlated mutation result.

Meanwhile `AddonProcessHost.OnFrontendStateInvalidatedForOverlay` schedules `OverlayProcessController.RefreshClawHudAsync` on ordinary visible-Overlay invalidation, including while another feature refresh is in flight.

Thus an independent snapshot can clear the mutation admission guard **before** the original request settles. A further user edit can then be accepted against a stale or intermediate snapshot. This is a plausible normal I/O/invalidation path, **not** an exotic scheduler interleaving.

**Required distinction:** independent state publication may update the latest authoritative facts, but must **not** mark a local request complete. Only its own correlated result or failure releases that request's busy flag. Do not introduce a new Runtime owner or a generalized request-versioning system for this.

### F3 — Shared logical row selection normalizes transient-disabled targets (confirmed)

`OverlayRowSelection.SetRows` and `NormalizeSelection` use `IsSelectable()`. If the current selected row becomes unavailable during a pending mutation, a subsequent `MovePrevious/MoveNext/AdjustSelected/ActivateSelected` can select the first other available row or clear selection. `OverlayWindow.Navigation.RefreshRowSelectionAfterMove` then invokes `BringSelectedRowIntoView()`.

This yields a realistic path from **one setting change followed by another D-pad action** to highlight/scroll movement unrelated to user intent. It is particularly exposed by F1.

Do **not** globally treat genuinely unsupported, removed, or externally unavailable rows as selectable. Preserve the existing fail-safe fallback when availability truly changes or a row disappears. Any mutation-busy protection should remain narrow and frontend-local.

### F4 — Settings → Quick Settings: same old-value/busy pattern (confirmed)

`RequestQuickSettingsCurrentPowerSourceOnly` sets `_quickSettingsMutationInFlight`, then `RenderQuickSettingsCurrentPowerSourceRow` reapplies the previous `_quickSettingsCurrentPowerSourceOnly` and disables the switch. `ApplyQuickSettingsCurrentPowerSourceOnly` and the shared frontend-settings mutation result later paint authoritative state.

Include the **Show only current power source** toggle. Preserve its existing availability checks and mutation result/failure completion path.

### F5 — Controller: group busy-disable, pending-preview inconsistencies (confirmed)

`OverlayWindow.Controller.cs` implements **separate existing admission flags**, all of which must be preserved:

- Controller for Steam and Gyroscope: `_steamControllerMutationInFlight` / `_gyroscopeMutationInFlight`. Pending visual fields already exist (`_pendingSteamControllerPreference`, `_pendingGyroscopeEnabled`), but affected native rows are transiently disabled.
- M1/M2: `_backButtonMutationInFlight` disables **both** mapping rows and reapplies `_backButtonMapping` (pre-result authority). The sender's `OverlayValueModel` has already previewed the new choice.
- Joystick LED: `_controllerLedMutationInFlight` disables the LED toggle and brightness/R/G/B detail rows, reapplies old `_controllerLed`, and may change detail visibility when authority truly changes.
- Vibration: existing paired `_vibrationDraftLeft/Right` and trailing debounce correctly preserve a shared draft; once the pair is submitted, `_controllerVibrationMutationInFlight` disables both rows. Preserve atomic left/right mutation, delay/flush behavior, and failure rollback.

PR #866 already added `RestoreControllerRowSelection` following frontend settings refresh/failure. **Do not duplicate or remove it.** It does not by itself prevent the *initial* selected-row normalization while a field is transiently busy. Likewise do not replace existing `_pending...` facts with a second state store.

### F6 — Device/Profile: recent changes already implemented (confirmed)

After PRs #863/#864 (and subsequent main commits):

- Value-only Device/Profile updates use `QuickSettingsRowRendering.IsValueOnlyPageUpdate` and changed RowIds; the fast path skips row-list rebuilding, selection restoration, and scroll calls.
- `ScheduleQuickSettingsSlider` updates the edited row plus linked/group companions only.
- `OverlayNumericSliderRow` and `OverlayValueRow` compare native dependency-property values before assigning.
- Quick Settings structural updates retain stable section cards/header/compatible row controls.
- Immediate toggle results include `forceRowId` to restore a rejected native toggle even if the authoritative value did not change.

**These improvements are complete and must not be reimplemented.** Verify them against regressions; only extend them for a **concretely demonstrated** remaining selection/value defect. Maintain profile target change, linked TDP edits, debounce, typed failure/readback, and available/unavailable transitions.

### F7 — Shortcut and Settings → Tab Order

Shortcut tile identity reconciliation and visibility-aware bring-into-view have also landed since the prior audit. Tab Order retains existing row instances, remaps selection by tab identity, and legitimately changes row position when the user reorders it. Treat both as **regression checks**, not targets for speculative redesign.

## 3. Frozen UX and correctness contract for every editable Overlay control

1. **One accepted edit → one requested field/value.** Preserve the user's last accepted local preview while its request is genuinely outstanding. Do not paint an older snapshot over that preview simply to indicate busy.
2. **No fake success.** Runtime result/readback is authoritative. A rejection, error, or operation failure must restore the actual authoritative value, re-enable eligible input, and allow a fresh edit; a failed request must not leave the preview stuck.
3. **Correct admission.** While a field/group is actually in flight, reject conflicting duplicate edits using the existing feature-local gates. A rejected second click/step must not remain displayed as though it had been accepted. Do not silently queue an unbounded succession of requests.
4. **Selection stability.** A temporary local mutation-busy interval must not reselect the top/first row, clear a still-existing selected row, trigger an unexpected `StartBringIntoView`, or change the shared `BodyScroll` offset. User-directed Up/Down, real tab changes, real expand/collapse, or genuine row removal retain their existing navigation/scroll contracts.
5. **Availability is not busy.** A real ClawHUD Runtime not-Ready/disconnected state, disabled hardware capability, profile change, or collapsed setting section must still prevent unavailable interactions and fall back only when necessary. Never make an unsupported operation executable to keep highlight position.
6. **Pointer and gamepad parity.** Native ToggleSwitch/stepper buttons, D-pad Left/Right, A, pointer press, and held-input behavior must converge on the same accepted/rejected request and preview rules. D-pad hold repeat remains **native Device/Profile numeric sliders only**, never ClawHUD steppers, Controller value rows, choices, or toggles.
7. **No extra state authority.** Retain the existing single `OverlayRowSelection`, feature-local in-flight flags, mutation channels, and Runtime-owned snapshots. A **small ephemeral pending desired value/intent** is acceptable when strictly needed for accurate preview; no generic optimistic store, reconciler, queue, epoch, lock, new manager, or transport protocol.
8. **Lifecycle and dismissal.** Keep root-dismiss gate/flush-before-dismiss and the PR #862 repeat cancellation intact; nested B/Back still collapses an expanded Setting card. Frontend Hide/close, disconnected transport, and feature mutation failure must not alter Full1902 physical-controller ownership.
9. **Visual performance.** Retain stable XAML controls; avoid whole-page repaints, unnecessary native `IsOn`/`Value`/`IsEnabled` changes, and broad selection repaint when no effective state changed. No UI restyling or component rewrite.

## 4. Implementation PR A — Settings mutation stability

**Scope:** `OverlayWindow.ClawHud.cs`, corresponding narrow `App.xaml.cs` dispatch paths, existing selection/navigation code only if needed, and targeted tests. Do not touch Controller hardware/runtime logic.

### A.1 Separate independent ClawHUD state from correlated mutation completion

Today `ApplyClawHudSnapshot` performs two different jobs. Split the semantics using existing routes (names may differ):

```csharp
// Illustrative frontend-only contract; not a demand for a new DTO or manager.
internal void ApplyClawHudState(FrontendClawHudSnapshot state)
{
    _clawHudSnapshot = state;
    // Do NOT clear _clawHudMutationInFlight on an unrelated state publication.
    RenderClawHudControls();
}

internal void CompleteClawHudMutation(FrontendClawHudMutationResult result)
{
    _clawHudSnapshot = result.Snapshot;
    _clawHudMutationInFlight = false;
    // Clear the transient accepted-edit preview; render actual readback.
    RenderClawHudControls();
}
```

Route `HandleClawHudAsync` to the independent-state path and `ApplyClawHudMutationResult` to completion; transport exception/failure must also clear the existing busy fact and restore an authoritative display. Do not consume a pending ClawHUD edit merely because an unrelated `StateInvalidated` was published.

### A.2 Stop all-eight-row old-state repaint during one edit

For each of these controls:

- Enable HUD (toggle)
- Display Mode (discrete)
- HUD Size (numeric stepper)
- Font (discrete)
- Alignment (discrete)
- Background (discrete)
- Opacity (numeric stepper)
- Intel VRR Range Fix (toggle)

Retain the accepted row's desired preview through the pending interval; do not unconditionally write the old `_clawHudSnapshot` over it at request start. Keep authoritative updates to other fields. Only temporarily block changes that existing single-flight ClawHUD semantics actually cannot accept. A narrow `pending intent` or target value can reuse `FrontendClawHudMutationIntent`; prefer no additional helper if existing controls already have a safe preview seam. Ensure the top-level enable toggle and nested controls properly follow actual `Ready` status.

### A.3 Fix Settings → Quick Settings toggle

Apply the same user-visible contract for **Show only current power source** using its existing frontend settings response/failure and `_quickSettingsMutationInFlight`. Do not add a separate settings authority.

### A.4 Prevent selection/scroll normalization caused *only* by temporary busy

Preserve the logical Settings-row target through an accepted mutation. Make the smallest change compatible with `OverlayRowSelection`; if the simplest correct approach is to ignore repeat editing/navigation on the busy Setting row until settlement, ensure it **does not** suppress B/Back, tab changes, or actual availability transitions. Do not globally disable row normalization for real unavailability. Keep row identity across expanded ClawHUD/Quick Settings/Tab Order cards and preserve existing `SetExpandedSettingCard` behavior.

### A.5 Tests (automated and hardware-free)

Add coverage for:

- All eight ClawHUD controls and the Quick Settings toggle: accepted pointer and D-pad change, pending preview stays at desired value, no unrelated rows revert, correct authoritative success.
- Typed rejected mutation and transport/operation exception: restore actual authority; correct enable/disable; no stuck preview or stale busy.
- Independent ClawHUD state pushed **during** an outstanding request: busy remains true; second request is rejected; **only** correlated response or failure completes it.
- Second rejected pointer click/step during busy does not leave a false visual preview.
- Setting selection on an inner row, then mutation pending plus additional controller navigation: no unintended fallback to first row or scroll; after completion the same selected row remains where valid.
- Genuinely unavailable/removed row, ClawHUD not Ready, card collapse, Tab Order reorder, B/Back, tab switching: existing intended behavior still works.
- At least one **behavioral** regression test of the pure row/model or dispatch seam; source-string assertions alone are insufficient for the premature-busy-clear scenario.

## 5. Implementation PR B — Controller mutation stability and cross-page regression

**Scope:** `OverlayWindow.Controller.cs`, existing row selection/navigation seam only when actually required, and targeted tests. Build on PR A where it provides a genuinely shared narrow fix.

### B.1 Reconcile group-specific pending states without losing the accepted visual value

Inventory and validate the actual accepted/pending/readback contract for:

| Controller group | Controls | Existing gate / preview facts to keep |
| --- | --- | --- |
| Controller for Steam | Steam Deck vs DualSense Edge | `_steamControllerMutationInFlight`, `_pendingSteamControllerPreference` |
| Gyroscope | Enabled toggle | `_gyroscopeMutationInFlight`, `_pendingGyroscopeEnabled` |
| Back buttons | M1 and M2 mapping choices | `_backButtonMutationInFlight`; whole-record update must retain the unchanged partner value |
| Joystick LED | Enable toggle; brightness; R, G, B steppers | `_controllerLedMutationInFlight`; detail visibility only changes for actual enabled/availability transition |
| Vibration Strength | Left and Right steppers | `_controllerVibrationMutationInFlight`; current two-value draft, 300-ms trailing delay, explicit flush, atomic pair commit |

Avoid rewriting the previous authoritative value over an accepted choice/toggle/step while pending. Correctly revert on failure/readback. Preserve existing independent Controller and Gyroscope mutation flags and paired vibration behavior. Do **not** expand gamepad D-pad hold repeat to these stepper/choice controls.

### B.2 Keep Controller selection stable through pending + refresh

PR #866 already saves and restores the index **after** complete frontend settings application. Extend only what is missing in actual edit/pending paths. Avoid an intermediate normalization/scroll jump while the selected Controller row temporarily becomes nonselectable due solely to the in-flight flag. Real LED detail collapse, unsupported capability, controller presentation change, and removed/unavailable rows retain their genuine fallback rules.

Keep `RestoreControllerRowSelection` and its existing staged-render ordering. Do not introduce another controller selection owner.

### B.3 Verify Device/Profile and remaining Settings/Shortcut contracts rather than rebuilding them

Use current main's stable renderer:

- Device and Profile toggle (including feature ON/OFF revealing dependent rows), numeric slider (Intel FPS AC/DC), linked/grouped TDP, numeric stepper, and discrete choice.
- Pending value preview, 300/75 D-pad hold repeat **only** for native numeric sliders, rejected toggle restoration, authoritative settlement/failure, Profile context switch.
- Settings Tab Order retains identity when physically reordered, and when an unchanged state snapshot republishes; intentional user reordering may move scroll.
- Shortcut execution and snapshot refresh retain same-ID tile controls/selection and do not request scroll on a hidden page.
- Dismiss while holding a slider: no post-flush edits or regressions to PR #862's `_userDismissInProgress` admission gate.

Avoid changing the Runtime's broad `StateInvalidated` fan-out merely to suppress a visible glitch. First prove a production failure that cannot be solved in the existing frontend update path.

### B.4 Tests (automated and hardware-free)

- Table-driven checks for Controller controls: accepted preview, overlapping/rejected edit, success/readback, typed failure, and exception.
- Selection highlighted on each Controller field: in-flight, unrelated FrontendSettingsState, result, and failure preserve selection and avoid unexpected bring-into-view.
- Independent Controller/Gyro requests remain independently admitted; M1/M2 updates preserve partner values; LED availability/visibility transitions stay correct; vibration pair/delay/flush remain correct.
- Device/Profile value-only fast path, row identity, immediate toggle failure, linked constraints, Profile target change, and both root/nested dismissal retain current tests.
- Prefer behavior-level tests of existing state models and narrow WinUI wiring checks; only introduce a small seam if meaningful behavior cannot be tested otherwise.

## 6. Merge-readiness and acceptance checklist

- [ ] Every interactive Overlay setting in the inventory has been reviewed for **request → local preview → in-flight admission → independent snapshot → correlated result/failure → native render → logical selection/scroll**.
- [ ] No supported user change momentarily restores the old visible value **solely because the local request is in flight**.
- [ ] A rejected edit never leaves an uncommitted displayed value; an accepted edit is never silently dropped by premature busy clearance.
- [ ] Mutating one control does not needlessly disable/repaint unrelated independent controls beyond the existing genuine group/whole-record constraints.
- [ ] Temporary in-flight unselectability does not cause first-row selection jumps or unwanted scroll. Genuine availability and structural changes still fail closed.
- [ ] Existing PR #862, #863, #864, #865 (Shortcut), #866 improvements are preserved; no reimplementation of completed work.
- [ ] No unnecessary new authority, background loop, state machine, generic viewmodel framework, locks, wrapper interfaces, or unsupported multi-session design.
- [ ] Debug/Release builds and relevant Core/UI test suites pass; `git diff --check` passes.
- [ ] Regressions cover ordinary save success, rejection, transport failure, and independent state refresh during pending mutations.
- [ ] **No physical-device or subjective visual verification as a merge blocker**. The user handles post-merge MSI Claw validation.

### Explicitly out of scope

- CTW/HHC integration; multi-user/Fast User Switching/RDP support.
- Changing PID1901/PID1902, HidHide/VIIPER ownership, suspend/resume, shutdown/recovery policy, or Steam controller presentation routing.
- Replacing the existing WinUI 3 Overlay shell, page scroll authority, Settings/Controller layout, or the feature/transport protocols.
- A new mutation queue/coordinator/state framework or speculative handling of instruction-boundary races.

## 7. PR separation

**PR A — Settings:** ClawHUD correlated-vs-independent result handling, stable pending preview, current-power-source toggle, Settings logical selection/scroll behavior, focused tests.

**PR B — Controller:** Controller groups' accepted preview/busy/rollback/selection behavior plus final full-surface regression matrix, focused tests.

Both PRs must be reviewable and independently code-testable. The user performs real handheld validation after merge; do not delay a code-correct merge for device access.

---

**Evidence boundary:** All source defects described above are based on the reviewed main code. Visible flashing or scroll offset changes require on-device confirmation; the work order fixes concrete state and navigation paths, not unproven compositor behavior.
