# Work Order — OQ5-UI-06: Slider Row Primitive

## Status

Sixth implementation PR from:

- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`

Track: Addon Quick Settings Overlay UI

Label/name: `OQ5-UI-06`

Baseline: `main` after PR #464 / commit `80e84333bd4c01129c0bea0327cabb4c2d5e3b93`.

This is **not** part of the numbered Full PID1902 implementation sequence.

---

## 1. Goal

Add the second real reusable Quick Settings row primitive: a standard WinUI 3 **Slider row** that plugs directly into the OQ5-UI-04 logical row-selection contract.

Required controller behavior:

```text
Up / Down
→ existing logical row selection

Slider row selected
+ Left
→ one semantic step lower

Slider row selected
+ Right
→ one semantic step higher

Slider row selected
+ A / Accept
→ no-op

B
→ existing global Overlay close
```

Required pointer/touch behavior:

```text
user drags/taps the WinUI Slider
→ visible preview changes immediately
→ emit the same narrow desired-value callback used by controller adjustment
```

The row must display its current preview value and must clamp/snap user edits to its configured range/step.

This PR establishes **only the Slider UI/value-edit primitive**.

It must **not** implement the OQ5-UI-07 delayed/trailing Runtime commit policy yet.

---

## 2. Required reading before implementation

Read current `main` after PR #464.

Required documents:

- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/overlayui/OQ5_UI_04_LOGICAL_ROW_SELECTION_SCROLLING_WORK_ORDER.md`
- `docs/overlayui/OQ5_UI_05_TOGGLE_ROW_PRIMITIVE_WORK_ORDER.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/OQ4_CONTROLLER_CAPTURE_NEUTRAL_PUBLICATION_WORK_ORDER.md`

Required current source:

- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayRowSelection.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs`
- `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
- current Overlay tests under `tests/SteamInputAddonforClaw.Tests/`

Do not copy a desktop UI control hierarchy into Overlay merely because similar sliders exist elsewhere.

---

## 3. Current code facts that define the implementation seam

### 3.1 OQ5-UI-04 already owns controller row adjustment

Current `OverlayRowCapabilities` is:

```csharp
internal sealed record OverlayRowCapabilities(
    Func<bool> IsSelectable,
    Action? Activate = null,
    Action<int>? Adjust = null);
```

Current navigation is already:

```text
NavigateLeft
→ OverlayWindow.AdjustSelectedRow(-1)

NavigateRight
→ OverlayWindow.AdjustSelectedRow(+1)
```

DPad, left stick, and right stick already arrive through the same semantic `NavigateLeft` / `NavigateRight` path.

Therefore a Slider row only needs to register:

```text
IsSelectable → current availability/editability
Activate     → null
Adjust       → apply -1 / +1 semantic step
```

Do not add another controller action.

Do not add an edit mode.

### 3.2 `.Overlay` protocol remains v4

This PR is frontend-only.

No new transport action is required.

Do not modify:

- `OverlayNavigationAction`;
- `.Overlay` protocol version;
- `OverlayControllerInputRouter`;
- OQ4 capture/release behavior.

### 3.3 OQ5-UI-05 established the primitive pattern

The current Toggle primitive already separates:

```text
frontend control state
from
future Runtime feature authority
```

The Slider should follow the same architectural rule:

```text
future authoritative state
→ ApplyState(...)
→ Slider row renders it

user controller/pointer edit
→ row computes/updates local preview immediately
→ row emits desired value
```

Do not make the Slider itself a persistence or hardware authority.

### 3.4 OQ5-UI-07 owns delayed commit semantics

The roadmap deliberately puts QAM-style delayed mutation in the **next** PR.

OQ5-UI-06 must not add:

- 2-second timers;
- pending-mutation dictionaries;
- mutation keys;
- latest-value-wins scheduling;
- Runtime feature calls;
- stale invalidation protection;
- close/hide flush/cancel policy.

This PR's desired-value callback is an **edit/draft seam**, not a requirement to perform an immediate hardware write.

OQ5-UI-07 will consume that seam and add the delayed commit owner.

---

## 4. Frozen Slider interaction contract

### 4.1 Controller UX

The selected Slider row is directly adjustable.

```text
Left  → current preview - one step
Right → current preview + one step
```

There is no:

```text
A → enter slider edit mode
A → exit slider edit mode
```

For Slider rows:

```text
Activate = null
```

Therefore `A / Accept` is a no-op while a Slider row is selected.

Do not change global `A` behavior for other row types.

### 4.2 One semantic action means one step

Current Runtime input routing emits one directional semantic action per armed deflection/button edge.

This PR maps one received `Adjust(-1/+1)` to exactly one configured Slider step.

Do not add held-direction repeat in this PR.

If later hardware testing shows held repeat is required, handle it as a separate narrow input-policy change rather than embedding timers inside the Slider row.

### 4.3 Pointer/touch remains native WinUI interaction

Use a standard WinUI 3 `Slider`.

Pointer/touch changes must:

1. update the visible preview immediately;
2. normalize the value to the row's range/step;
3. emit the same desired-value callback used by controller adjustment;
4. avoid duplicate callbacks for a value that did not actually change after normalization.

Do not synthesize controller actions from pointer events.

---

## 5. Narrow Slider state model

A small pure/testable Slider value model is appropriate, analogous to `OverlayToggleModel`.

Recommended responsibilities only:

```text
availability
minimum
maximum
step
current preview value
clamp/snap
controller ±1 step edit
pointer/touch desired-value edit
apply authoritative state without emitting an edit
```

Conceptually:

```csharp
internal sealed class OverlaySliderModel
{
    bool IsAvailable { get; }
    double Minimum { get; }
    double Maximum { get; }
    double Step { get; }
    double PreviewValue { get; }

    void ApplyState(
        bool isAvailable,
        double minimum,
        double maximum,
        double step,
        double value);

    // user edits only
    void RequestAdjust(int delta);
    void RequestSet(double desired);
}
```

Exact naming/signature may differ if a simpler implementation is clearer.

Do **not** introduce:

- a generic numeric framework;
- generic-math interfaces;
- a shared settings value hierarchy;
- `INotifyPropertyChanged` infrastructure solely for this row;
- a general-purpose `OverlayRowBase` class;
- a Slider manager/service.

---

## 6. Authoritative state application

The row must expose one narrow state-application seam for future Runtime-backed features.

The future caller needs to be able to supply at least:

```text
available/editable
minimum
maximum
semantic step
current authoritative value
```

Reason: limits/step may be board- or feature-dependent and should not become hard-coded inside the generic Slider primitive.

### 6.1 ApplyState behavior

Applying state must update:

```text
row IsSelectable fact
Slider.IsEnabled
Slider.Minimum
Slider.Maximum
Slider step configuration
Slider.Value
visible value text
internal preview value
```

Programmatic application must **not** emit a user desired-value callback.

Use one local event-suppression fact around programmatic `Slider.Value` / range updates if WinUI raises `ValueChanged`.

Do not create a shared/global event suppression service.

### 6.2 Invalid constraint input fails closed locally

The primitive must not attempt adjustment when the supplied numeric contract is invalid.

At minimum, treat the row as unavailable/non-selectable when:

```text
minimum > maximum
step <= 0
minimum/maximum/step/value is NaN or Infinity
```

A supplied value outside a valid range should be clamped into that valid range for rendering.

Do not crash the Overlay merely because a later feature binding supplied malformed numeric metadata.

Keep this local; do not build a validation framework.

---

## 7. Step / clamp / snap rule

Controller and pointer/touch must converge on one normalized value rule.

For a valid row:

```text
candidate
→ clamp to [minimum, maximum]
→ snap to the configured semantic step relative to minimum
→ clamp once more if needed
→ compare with current preview
```

Only a real normalized value change emits the desired-value callback.

At a boundary:

```text
value = minimum + Left
→ stays minimum
→ no duplicate desired-value callback

value = maximum + Right
→ stays maximum
→ no duplicate desired-value callback
```

The implementation should avoid obvious floating-point drift in the displayed/returned stepped value.

A small local normalization helper is sufficient.

Do not build arbitrary decimal precision configuration or a general units library.

---

## 8. Immediate preview is part of this PR

This PR must already feel responsive even though Runtime commit timing is deferred to OQ5-UI-07.

Required ordering on user edit:

```text
controller Left/Right OR pointer/touch
→ compute normalized desired value
→ update local PreviewValue
→ update Slider.Value under event suppression as needed
→ update visible value label
→ emit desired-value callback
```

The visual preview therefore changes immediately.

### Important boundary with OQ5-UI-07

For OQ5-UI-06 alone:

```text
ApplyState(authoritative)
→ authoritative value replaces local preview immediately
```

Do **not** implement pending-draft protection against later stale invalidations yet.

That behavior belongs to OQ5-UI-07 together with delayed commit ownership.

---

## 9. Slider row visual structure

Use the existing row-oriented design.

Conceptual layout:

```text
┌──────────────────────────────────────┐
│ Label                         50      │
│ ━━━━━━━━━━━━━●━━━━━━━━━━━━━━━━━━━━━ │
└──────────────────────────────────────┘
```

Requirements:

- standard WinUI 3 `Slider` sizing;
- label visible;
- current preview value always visible;
- Slider occupies useful horizontal width below the label/value line;
- outer row `Border` remains the OQ5 logical-selection visual;
- no custom Slider template;
- no global compact-control styling;
- keep the current 400-DIP Overlay width and existing body inset.

A simple two-row `Grid` / `StackPanel` inside the existing row Border is sufficient.

### 9.1 Value formatter

The primitive needs a narrow way to display feature-appropriate text later.

A constructor-supplied formatter such as:

```csharp
Func<double, string> formatValue
```

is acceptable and preferred over hard-coding units inside the primitive.

A similarly small equivalent is fine.

The primitive must not know about:

- watts;
- FPS;
- percentages;
- named Device features.

Do not add a units/formatting service.

---

## 10. Keep selection visuals owned by OQ5-UI-04

The WinUI Slider's pointer/focus visuals are not the controller-selection authority.

Required layering remains:

```text
outer Border
→ logical selected-row highlight

inner Slider
→ current numeric value / pointer interaction only
```

Do not call:

- `Focus()`;
- `Activate()`;
- `SetForegroundWindow`;
- `SendInput`.

The Overlay remains no-activate.

---

## 11. Disabled / unavailable Slider behavior

When unavailable:

```text
Slider.IsEnabled = false
OverlayRowCapabilities.IsSelectable() = false
Up/Down skips it
Left/Right cannot mutate it
pointer/touch cannot mutate it
A remains no-op
```

Keep the row visible rather than hiding it.

Later feature PRs may add explanatory unavailable text; do not invent product-specific messages here.

---

## 12. Device-page preview fixture

Extend the current temporary Device preview surface only enough to exercise the Slider primitive.

Keep the existing Toggle previews from OQ5-UI-05.

Add at least:

1. one available `Slider Preview`;
2. one unavailable `Unavailable Slider Preview`.

Recommended neutral preview numbers:

```text
minimum = 0
maximum = 100
step = 5
initial value = 50
```

These are UI-fixture values only.

They are **not** TDP, FPS, power, fan, or any other production feature.

The available preview may use a local no-op/echo desired-value callback because the Slider model itself owns the temporary visible preview for this PR.

Retain enough `Navigation Preview NN` rows to continue exercising:

- Up/Down selection;
- skip-unavailable behavior;
- selected-row highlight;
- bring-into-view;
- scrolling.

Do not create another page or preview app.

---

## 13. No Runtime / transport change

This PR must not add:

- Runtime Device DTOs;
- named-pipe feature snapshots;
- named-pipe feature mutation commands;
- SettingsStore entries;
- hardware readers/writers;
- TDP/CPU Boost/Power/FPS bindings;
- another `.Overlay` protocol version.

The Slider callback exists so later work can bind it cleanly.

It is not authority by itself.

---

## 14. OQ4 / controller lifecycle invariants

This frontend work must not weaken the existing capture contract.

While Overlay is visible:

```text
physical PID1902 state
→ Overlay semantic navigation

current virtual presentation
→ remains attached and neutral
```

Slider interaction must not cause:

- PID switch;
- DirectInput reacquire;
- HidHide mutation;
- VIIPER attach/detach;
- X360 ↔ SteamDeck presentation change;
- Steam QAM close/restart;
- a second controller reader.

B remains Runtime-owned global close.

Steam QAM may remain visible behind the Addon Overlay.

---

## 15. Files expected to change

Expected minimal shape:

### New

```text
src/SteamInputAddonforClaw.Overlay/OverlaySliderRow.cs
```

A pure Slider model may live in the same file, matching the intentionally simple OQ5-UI-05 Toggle primitive pattern.

### Modify

```text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
```

for the temporary Slider preview fixture only.

### Tests

Add focused pure-model tests, for example:

```text
tests/SteamInputAddonforClaw.Tests/OverlaySliderRowTests.cs
```

Do not edit Runtime/lifecycle/transport projects unless current code proves an unavoidable compile dependency. If that happens, keep the change mechanical and do not broaden product behavior.

---

## 16. Required automated tests

Test the pure Slider value model without requiring a XAML host.

Minimum cases:

### Availability / authoritative state

- valid available state becomes selectable;
- unavailable state rejects controller/pointer edits;
- `ApplyState` emits no desired-value callback;
- authoritative value outside range is clamped for preview;
- malformed numeric constraints fail closed/non-selectable.

### Controller step behavior

- `-1` lowers exactly one configured step;
- `+1` raises exactly one configured step;
- repeated separate calls continue from the current preview;
- lower boundary clamps/no extra callback;
- upper boundary clamps/no extra callback;
- unavailable row emits nothing.

### Pointer/touch value behavior

- direct desired value is clamped;
- direct desired value is snapped to semantic step;
- unchanged normalized value emits no duplicate callback;
- pointer and controller edits produce the same normalized values for equivalent targets.

### Authoritative reset

- after local preview edits, `ApplyState` replaces the preview with the supplied authoritative value;
- the authoritative reset itself does not emit another desired-value callback.

### Capability contract

Where practical without a XAML host, verify the model contract used by the row:

```text
Slider → Adjust exists
Slider → Activate is null
```

If testing the WinUI wrapper requires a XAML host unavailable to the normal test project, cover the pure model in CI and leave wrapper rendering/interaction to hardware validation. Do not add a heavyweight UI-test framework for this PR.

---

## 17. Hardware validation on MSI Claw

After implementation, validate on supported hardware.

### A. Controller selection and one-step adjustment

1. Open Overlay.
2. Navigate to `Slider Preview` using DPad.
3. Press Left once.
4. Confirm preview decreases by exactly one step.
5. Press Right once.
6. Confirm preview increases by exactly one step.
7. Repeat using left stick and right stick.

Expected:

```text
all directional sources
→ same semantic one-step behavior
```

### B. A does not enter edit mode

With `Slider Preview` selected:

1. press A repeatedly;
2. confirm value does not change;
3. confirm no alternate slider mode/focus state is entered;
4. confirm Up/Down immediately continues row navigation.

### C. Boundaries

Drive preview to minimum and maximum.

Confirm further Left/Right input at the boundary:

- does not move outside range;
- does not visually jitter;
- does not produce another effective value change.

### D. Pointer/touch

Use touch/pointer on the Slider.

Confirm:

- native WinUI Slider interaction works;
- preview value text updates immediately;
- resulting value respects configured step/range;
- controller navigation still works immediately afterward without activating the Overlay HWND.

### E. Unavailable row

Confirm `Unavailable Slider Preview`:

- is visibly disabled;
- is skipped by controller Up/Down selection;
- cannot be changed by touch/pointer;
- cannot be adjusted through Left/Right.

### F. Scroll / bring-into-view regression

Navigate through Toggle, Slider, and Navigation Preview rows.

Confirm:

- one logical selected row only;
- highlight follows selection;
- off-screen selection is brought into `BodyScroll` view;
- tab changes reset to the first selectable row.

### G. OQ4 regression

While Overlay is visible and Slider is being adjusted:

- game / Steam QAM behind receives no controller navigation;
- current X360 or SteamDeck presentation stays attached and neutral;
- B closes through the existing OQ4 retirement/release path;
- no PID / DI / HidHide / VIIPER churn occurs.

---

## 18. Acceptance criteria

This PR is complete when all of the following are true:

- a standard WinUI 3 Slider row primitive exists;
- it reuses `OverlayRowCapabilities` rather than introducing a second navigation model;
- Up/Down remains row navigation;
- Left/Right changes exactly one semantic step;
- A/Accept is no-op for Slider rows;
- pointer/touch uses the same desired-value seam;
- preview value updates immediately;
- current preview value is always visible;
- range/step clamp/snap is deterministic;
- unavailable Slider is disabled and non-selectable;
- authoritative `ApplyState` does not emit user edits;
- no delayed/debounced Runtime commit policy exists yet;
- `.Overlay` remains v4;
- Runtime/OQ4/controller authority code is unchanged;
- Release build passes;
- full test suite passes;
- `git diff --check` is clean;
- hardware validation is recorded when physical MSI Claw access is available.

---

## 19. Explicit non-goals

Do not include any of the following in OQ5-UI-06:

- OQ5-UI-07 2-second delayed commit;
- mutation-key scheduler;
- pending-draft invalidation protection;
- Runtime feature snapshot bridge;
- Runtime feature mutation commands;
- TDP control;
- CPU Boost control;
- Windows Power Mode control;
- Intel FPS control;
- Choice row;
- shortcut tile behavior;
- tab-order persistence/editor;
- hold-repeat input policy;
- A/edit-mode Slider UX;
- custom Slider templates;
- generalized row base hierarchy;
- generic numeric/settings framework;
- raw controller IPC;
- additional DirectInput/XInput/GameInput reader;
- PID / HidHide / VIIPER / presentation changes;
- Steam QAM visibility management;
- Game Bar/OEM button policy.

---

## 20. Implementation principle

Keep this PR simple:

```text
existing semantic Left/Right
→ existing selected-row Adjust(-1/+1)
→ narrow Slider model normalizes one step
→ local preview updates immediately
→ narrow desired-value callback
```

Do not solve OQ5-UI-07, feature transport, or hardware authority early.

One Slider primitive, one existing selection authority, one future binding seam.
