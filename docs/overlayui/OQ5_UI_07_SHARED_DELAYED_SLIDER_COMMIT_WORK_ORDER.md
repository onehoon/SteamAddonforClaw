# Work Order — OQ5-UI-07: Shared Delayed Slider Commit Behavior

## Status

Seventh implementation PR from:

- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`

Track: Addon Quick Settings Overlay UI

Label/name: `OQ5-UI-07`

Baseline: `main` after PR #465 / commit `db9a50c47c03651f8632e3764d3246a474364b3a`.

This is **not** part of the numbered Full PID1902 implementation sequence.

---

## 1. Goal

Add the narrow delayed-commit behavior that future Runtime-backed Overlay sliders will share, while preserving the immediate controller/touch preview completed by OQ5-UI-06.

Required product behavior:

```text
slider edit
→ OQ5-UI-06 updates local preview immediately
→ schedule latest desired value

another edit before 2 seconds
→ replace the previous unsubmitted value
→ restart the 2-second window
→ keep newest preview visible

2 seconds after the last edit
→ submit only the latest desired value
→ receive authoritative settlement/readback
→ reconcile the visible slider state
```

The production delay must match the current Steam QAM policy:

```text
2000 ms
```

This PR is still **frontend infrastructure only**.

It must not add TDP/CPU/FPS/Power mutation transport merely to exercise the helper.

---

## 2. Required reading before implementation

Read current `main` after PR #465.

Required documents:

- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/overlayui/OQ5_UI_06_SLIDER_ROW_PRIMITIVE_WORK_ORDER.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/OQ4_CONTROLLER_CAPTURE_NEUTRAL_PUBLICATION_WORK_ORDER.md`

Required current source:

- `src/SteamInputAddonforClaw.Overlay/OverlaySliderRow.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs`
- `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
- current Overlay tests under `tests/SteamInputAddonforClaw.Tests/`

Required QAM reference behavior:

- `src/SteamInputAddonforClaw.QamHost/Frontend/qam.js`
- `tests/SteamInputAddonforClaw.Tests/QamFrontendContractTests.cs`

Do not port QAM JavaScript architecture into C#.

Reuse the behavior only.

---

## 3. Current code facts that define the seam

### 3.1 OQ5-UI-06 already owns immediate preview

Current `OverlaySliderModel` already does:

```text
controller Left/Right or pointer/touch edit
→ normalize / clamp / snap
→ update PreviewValue immediately
→ invoke Action<double> requestChange only on a real value change
```

Current `OverlaySliderRow` already renders that preview immediately.

Therefore this PR must **not** move the 2-second delay inside:

- `OverlaySliderModel.RequestAdjust()`;
- `OverlaySliderModel.RequestSet()`;
- WinUI `Slider.ValueChanged` handling.

The user-visible preview must remain immediate.

The new delayed-commit helper belongs **after** the existing desired-value callback.

Conceptually:

```text
OverlaySliderRow
    immediate preview
        |
        v
requestChange(desiredValue)
        |
        v
Overlay delayed-slider commit helper
        |
      2 sec
        |
        v
future feature binding / Runtime mutation
```

### 3.2 Current `.Overlay` protocol remains v4

This PR does not add production feature transport.

Therefore:

- do not bump `.Overlay` protocol;
- do not add generic mutation messages;
- do not expose whole Runtime feature state to Overlay;
- do not modify OQ4 navigation/capture transport.

### 3.3 QAM already defines the product pacing

Current QAM uses:

```text
QAM_SLIDER_COMMIT_DELAY_MS = 2000
```

For the same logical mutation key, a new edit:

```text
cancels the previous pending timer
→ stores the newest pending value/configuration
→ starts a new timer
```

QAM also gives each scheduled entry a newer token. An older async completion checks that its token is still current before deleting the entry or applying settlement.

Preserve that observable behavior.

Do not reproduce QAM's JavaScript global `Map` merely because it exists there.

---

## 4. Keep the C# design simpler than the QAM implementation

### 4.1 One helper instance represents one logical slider mutation key

Preferred Overlay model:

```text
one production slider binding
→ one delayed-commit helper instance
→ at most one current pending draft for that logical setting
```

Examples later may be:

```text
TDP slider binding       → helper A
FPS limit slider binding → helper B
```

This naturally satisfies:

> one pending mutation per logical setting key

without creating a global string-key scheduler or a generic mutation manager.

Do **not** add merely for this PR:

- global `Dictionary<string, object>` mutation infrastructure;
- generic key/value mutation APIs;
- a cross-feature debounce service;
- a scheduler service registered in DI;
- a page-wide mutation manager;
- a QAM/Overlay shared abstraction.

A small reusable slider-specific helper class is sufficient.

Suggested conceptual name:

```text
OverlayDelayedSliderCommit
```

Exact naming may differ if the implementation is clearer.

### 4.2 Narrow value domain

The OQ5-UI-06 primitive emits a normalized `double` preview value.

Keep this helper slider-specific and numeric.

Do not generalize it to arbitrary payload objects just because future non-slider controls may exist.

Toggles remain immediate and do not use this helper.

---

## 5. Frozen delayed-commit state

The helper needs only the state necessary for real delayed slider behavior.

Conceptually:

```text
latest desired value
current schedule/completion generation
scheduled delay cancellation
whether an unsubmitted/in-flight draft is current
optional current failure/settlement fact
```

Expose the smallest facts a later feature binding needs, for example:

```text
HasPendingDraft
TryGetPendingValue(out value)
```

The exact API shape is flexible.

Do not expose internal timer/token objects to the UI.

Do not persist this state.

Do not make it a Runtime authority.

---

## 6. Schedule semantics

### 6.1 Production delay

Use exactly:

```text
2000 ms after the most recent edit
```

A change at `t=0` then another at `t=1.5s` means:

```text
first scheduled commit is replaced
new commit target = latest value
new due time ≈ t=3.5s
```

Do not submit every intermediate value.

### 6.2 Latest value wins before submission

Example:

```text
50 → 55 → 60 → 65
```

all within the debounce window must produce:

```text
visible preview: 50 → 55 → 60 → 65 immediately
submitted authoritative request: 65 only
```

The helper must not queue:

```text
55
then 60
then 65
```

for later sequential submission.

This is a trailing debounce, not a work queue.

### 6.3 Boundary/no-op edits remain callback-free

OQ5-UI-06 already suppresses requests when the normalized preview did not actually change.

Do not schedule or restart the 2-second timer when `OverlaySliderModel` emitted no desired-value callback.

---

## 7. In-flight commit and stale settlement protection

This is a real normal-I/O scenario, not a theoretical instruction-level race:

```text
value A waits 2 sec
→ commit A begins
→ Runtime/I/O is still completing
→ user changes slider to value B
→ B becomes the new current draft
→ commit A completes late
```

Required outcome:

```text
late A completion must NOT:
- clear B's pending state;
- overwrite B's visible preview;
- apply A's stale authoritative settlement as if it were current.
```

Use one small monotonically newer generation/token for scheduled commits, equivalent in purpose to the existing QAM token check.

Conceptually:

```text
schedule A → generation 10
schedule B → generation 11
A settles  → generation 10 != current 11 → ignore UI settlement
B settles  → generation 11 == current 11 → accept settlement
```

Do not build epochs/barriers or a general concurrency state machine.

One local generation fact is sufficient for this normal async mutation path.

If the underlying already-submitted Runtime operation cannot be canceled, that is acceptable. The important contract here is that its **stale completion cannot become current UI authority**.

The latest submitted/current binding result will eventually reconcile final state.

---

## 8. Authoritative settlement contract

The delayed helper does not become feature authority.

The future feature-specific binding remains responsible for turning a Runtime result/readback into the authoritative value shown by the row.

A narrow slider settlement shape is acceptable, conceptually:

```csharp
internal sealed record OverlaySliderCommitSettlement(
    bool Succeeded,
    double? AuthoritativeValue,
    string? FailureMessage);
```

Exact naming/shape may differ.

Requirements:

- successful current settlement can provide the authoritative rendered value;
- failure can provide a failure fact;
- feature binding may perform a targeted authoritative reload before returning failure settlement;
- stale settlement from an older generation is ignored;
- applying authoritative state continues through the existing `OverlaySliderRow.ApplyState(...)` path so programmatic `ValueChanged` feedback stays suppressed.

Do not teach the helper about:

- TDP DTOs;
- FPS DTOs;
- profile configuration objects;
- named-pipe message types;
- hardware APIs.

---

## 9. Pending-draft protection across authoritative refresh/invalidation

Current QAM intentionally preserves pending slider drafts across state invalidation.

The Overlay helper must expose enough state for the later binding to do the same.

Required binding rule:

```text
Runtime refresh says authoritative value = 50
local current pending draft = 65

while 65 is still pending/current:
visible value remains 65
```

Do not visibly jump the slider back to 50 just because an invalidation/refresh arrived before the current delayed commit settled.

Conceptually, a future binding can render:

```csharp
var visibleValue = delayedCommit.TryGetPendingValue(out var draft)
    ? draft
    : snapshot.Value;

row.ApplyState(..., visibleValue);
```

The exact implementation may differ.

This PR does **not** add Runtime state invalidation transport to Overlay if it does not already exist.

Instead, unit-test the helper fact that the latest pending value remains queryable until current settlement/cancellation.

---

## 10. Failure behavior

A current commit failure must not leave the UI permanently pretending the draft succeeded.

Required product direction for future bindings:

```text
current commit fails
→ obtain/retain authoritative fallback state
→ clear current pending draft
→ render authoritative fallback through ApplyState(...)
→ expose a local failure fact
```

This PR must establish the settlement/failure seam and test it with a fake commit path.

Because no production Device mutation transport exists on `.Overlay` yet, this PR does **not** need to invent final user-facing error copy or a generalized InfoBar system.

Acceptable now:

- a failure result/property/callback the future binding can consume;
- diagnostic logging for the preview fixture;
- deterministic unit tests proving failure clears only the current draft and returns the supplied authoritative fallback.

Do not add fake TDP/CPU/FPS failures as product UI.

---

## 11. Close / hide / lifecycle policy

The UI design deliberately says a 2-second debounce must never become a controller-capture authority.

Required invariant:

```text
Overlay close
→ must NOT wait 2 seconds for an unsubmitted slider timer
→ OQ4 close/release lifecycle continues normally
```

### 11.1 Stop accepting new edits when normal close begins

Existing OQ4/Overlay retirement remains the owner of close.

Do not add another surface lifecycle gate.

The delayed helper must not hold Overlay visibility or controller capture open.

### 11.2 Distinguish unsubmitted delay from already-submitted operation

The current UI design intentionally defers the **production feature** choice of flush-vs-cancel for an unsubmitted draft until the real feature mutation transport is bound.

Do not prematurely freeze a single product-wide flush policy in this infrastructure PR.

Instead, the helper must expose a narrow lifecycle operation sufficient for later bindings, e.g.:

```text
cancel unsubmitted scheduled draft
```

For the temporary preview fixture in this PR, cancel the unsubmitted fake commit when the Overlay begins hiding so a hidden preview cannot fire an obsolete fake mutation later.

If a commit has already been submitted before close:

```text
Overlay capture may close immediately
already-submitted operation may settle normally
```

Do not keep capture alive waiting for it.

A late settlement must still obey the current-generation rule.

### 11.3 Runtime/process teardown

On actual Overlay/Runtime teardown:

- prevent new schedule calls;
- cancel unsubmitted delay work;
- suppress obsolete UI settlement callbacks after helper disposal;
- do not invent controller cleanup here.

If a hardware mutation was already submitted, Runtime/feature transport teardown owns its actual operation lifetime.

---

## 12. Do not conflate mutation debounce with controller repeat

OQ5-UI-03 intentionally did not add hold-repeat.

This PR must not change controller navigation timing.

```text
2-second slider commit delay
!=
DPad/stick repeat delay
```

Do not add:

- held-direction repeat;
- acceleration;
- gesture timing;
- stick timing changes;
- `OverlayControllerInputRouter` changes.

---

## 13. Temporary Device-page validation fixture

Reuse the existing neutral `Slider Preview` from OQ5-UI-06.

Do not create a fake production feature.

Change only that preview binding so its desired-value callback goes through the new delayed helper.

Suggested behavior:

```text
Slider Preview starts at 50
Left/Right quickly changes visible value immediately
fake authoritative commit is not invoked yet
2 seconds after last change
fake commit receives only latest value
fake settlement applies that value back through Slider ApplyState(...)
```

The fake commit may simply echo the desired value as its authoritative value.

Keep the existing `Unavailable Slider Preview` behavior.

Optional diagnostic logging is acceptable for:

```text
preview commit scheduled
preview commit submitted
preview commit settled/canceled
```

Do not turn these into permanent user-facing rows.

At hide start, cancel any **unsubmitted preview** timer.

This is test-fixture lifecycle behavior, not the final production feature close policy.

---

## 14. Expected implementation scope

Likely files:

```text
src/SteamInputAddonforClaw.Overlay/
    OverlayDelayedSliderCommit.cs       new, or equivalent narrow helper
    OverlayWindow.xaml.cs               preview-fixture wiring/lifecycle only

possibly:
    OverlaySliderRow.cs                 only if a tiny seam is genuinely required

tests/SteamInputAddonforClaw.Tests/
    OverlayDelayedSliderCommitTests.cs  new
```

Avoid unrelated changes.

No expected changes to:

```text
src/SteamInputAddonforClaw/Lifecycle/OverlayControllerInputRouter.cs
src/SteamInputAddonforClaw/VirtualOutput/
src/SteamInputAddonforClaw/Input/
HidHide code
VIIPER code
PID1901/PID1902 code
Frontend/QAM named-pipe protocol
Runtime feature authorities
```

---

## 15. Deterministic tests

Do not make CI literally sleep for 2 seconds per test.

Use the smallest deterministic timing seam practical for this helper.

The repository already uses small local timing/test seams in other lifecycle code. It is acceptable to provide an internal test delay/time source or another equally narrow mechanism.

Do not add a heavyweight scheduler/test package solely for this helper.

Required tests:

### A. Immediate desired draft ownership

```text
Schedule(55)
→ pending draft immediately reports 55
→ commit has not run before delay expires
```

### B. Latest-value-wins before delay

```text
Schedule(55)
Schedule(60)
Schedule(65)
advance delay
→ one commit only
→ committed value = 65
```

### C. New edit restarts trailing window

Prove the timer is measured from the **last** schedule, not the first.

### D. Independent helper instances do not cancel each other

Two helper instances represent two logical setting keys.

```text
helper A schedules 55
helper B schedules 30
helper A reschedules 60
→ B remains independently scheduled
```

Do not add a global coordinator just to make this test pass.

### E. Pending value remains visible/queryable

While timer or current commit is pending:

```text
TryGetPendingValue → latest draft
```

This is the seam future invalidation handling will use.

### F. Current success settlement

```text
latest commit settles successfully with authoritative value
→ current pending fact clears
→ current settlement callback/result is applied once
```

### G. Current failure settlement

```text
latest commit fails / returns failure settlement
→ current pending fact clears
→ failure settlement is exposed once
→ supplied authoritative fallback can replace preview
```

### H. Stale in-flight completion cannot overwrite newer draft

Deterministically model:

```text
A delay expires
A commit starts and is held
Schedule B
A completes
```

Assert:

```text
A settlement is ignored as stale
B remains pending/current
B preview remains current
```

Then release/settle B and prove B becomes authoritative.

This is the most important latest-value-wins async test.

### I. Cancel unsubmitted work

```text
Schedule(55)
CancelUnsubmitted()
advance delay
→ commit never called
→ no stale settlement callback
```

### J. Disposal/teardown suppresses obsolete callbacks

After disposal/teardown:

- new scheduling is rejected/no-op according to the chosen narrow contract;
- a canceled unsubmitted callback cannot fire later;
- stale settlement cannot update disposed preview ownership.

### K. Production delay contract

A focused contract test should prove the default production delay remains exactly:

```text
2000 ms
```

Do not accidentally drift from QAM policy.

---

## 16. Hardware validation on MSI Claw

After CI passes, validate the preview behavior on physical hardware.

### A. Controller rapid adjustment

1. Open Overlay.
2. Navigate to `Slider Preview`.
3. Press Left/Right several times quickly.

Verify:

- preview changes immediately on every accepted step;
- no A/edit mode is needed;
- UI does not pause for 2 seconds between visible steps.

### B. Trailing commit

Make several quick changes and stop.

Verify through diagnostics that:

- no intermediate fake commits are submitted;
- one commit occurs about 2 seconds after the last change;
- it contains the latest preview value.

### C. Close before debounce expires

1. Change `Slider Preview`.
2. Close Overlay immediately with B or outside dismissal.

Verify:

- Overlay capture closes through normal OQ4 ordering without a 2-second delay;
- unsubmitted preview commit is canceled;
- controller returns to the underlying game/QAM only after the existing OQ4 consumed-input release gate;
- no later fake preview mutation fires while Overlay is hidden.

### D. Reopen

Reopen Overlay and verify the warm process remains usable and no stale delayed callback from the prior visible session changes the current preview unexpectedly.

---

## 17. Preserve OQ4 / Full PID1902 safety

Slider debounce must never cause:

- PID change;
- DirectInput reacquire/release;
- HidHide mutation;
- VIIPER attach/detach;
- X360 ↔ SteamDeck switch;
- another publisher pause gate;
- another controller capture owner;
- Overlay capture extension solely to wait for a timer.

Steam QAM may remain visible behind Addon Overlay as already designed.

This PR changes only UI-side slider mutation pacing.

---

## 18. Explicit non-goals

Do not implement in OQ5-UI-07:

- real TDP mutation transport;
- real CPU Boost mutation transport;
- real Power Mode mutation transport;
- real Intel FPS mutation transport;
- Device snapshot bridge;
- Runtime state polling;
- generic Overlay feature mutation protocol;
- `.Overlay` protocol v5;
- toggle debounce;
- hold-repeat;
- A/edit-mode slider UX;
- tab order persistence/editor;
- Shortcut behavior;
- final failure InfoBar design;
- a cross-feature/global scheduler service;
- a generic mutation-key dictionary unless a concrete current consumer requires it;
- QAM JavaScript refactoring;
- OQ4 lifecycle changes.

---

## 19. Acceptance criteria

OQ5-UI-07 is complete when all of the following are true:

### Behavior

- OQ5-UI-06 slider preview remains immediate.
- Production delayed commit uses exactly 2000 ms.
- Multiple rapid edits collapse to the latest value.
- The trailing window restarts on every new emitted desired value.
- One helper instance owns at most one current draft.
- Multiple helper instances are independent.
- Current pending value is queryable for future invalidation protection.
- Current authoritative settlement replaces the pending draft only after settlement.
- Current failure can restore a supplied authoritative fallback and expose failure.
- Older in-flight completion cannot clear/overwrite a newer current draft.

### Lifecycle

- Hiding Overlay never waits for the 2-second timer.
- Temporary preview unsubmitted work is canceled on hide.
- Already-submitted work does not become an Overlay-capture authority.
- Teardown/disposal prevents obsolete delayed UI callbacks.

### Architecture

- `.Overlay` stays v4.
- No Runtime feature transport is added.
- No new controller/capture/presentation authority is added.
- No global/general mutation scheduler is introduced.
- QAM code is unchanged; only its established behavior is reused.
- Toggle behavior stays immediate.

### Quality

- deterministic unit coverage includes latest-value-wins, stale in-flight settlement, cancellation, failure, and independent settings;
- full Release build passes;
- full test suite passes;
- `git diff --check` is clean;
- hardware validation is reported separately where available.

---

## 20. Expected result

After this PR the Overlay UI foundation has:

```text
five-tab shell
+ controller tab navigation
+ DPad / both-stick navigation
+ logical selected rows / scrolling
+ Toggle row primitive
+ Slider row primitive
+ QAM-equivalent delayed slider mutation pacing
```

but still no duplicated feature authority.

The next planned step remains:

```text
OQ5-UI-08
→ Runtime-owned Overlay tab-order setting
```

Production Device feature transport/binding remains in the later `OQ5-FEAT-*` sequence.