# Work Order — OQ5-UI-05: Toggle Row Primitive

## Status

Fifth implementation PR from:

- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`

Track: Addon Quick Settings Overlay UI

Label/name: `OQ5-UI-05`

Baseline: `main` after PR #463 / commit `3af3b3f5a4e80faa1799b92fa661fd52f660974f`.

This is **not** part of the numbered Full PID1902 implementation sequence.

---

## 1. Goal

Add the first real reusable Quick Settings row primitive: a standard WinUI 3 **boolean Toggle row** that plugs directly into the logical row-selection contract completed by OQ5-UI-04.

Required controller behavior:

```text
Up / Down
→ existing logical row selection

Toggle row selected
+ A / Accept
→ request the opposite boolean state

Left / Right
→ no-op for Toggle row

B
→ existing global Overlay close
```

Required pointer/touch behavior:

```text
user clicks/taps the WinUI ToggleSwitch
→ request that desired boolean state through the SAME row request seam
```

The primitive must be ready for later Runtime-backed features, but this PR must **not** add a feature snapshot or hardware mutation transport merely to make the Toggle row exist.

The control is a frontend primitive, not a new feature authority.

---

## 2. Required reading before implementation

Read current `main` after PR #463.

Required documents:

- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/overlayui/OQ5_UI_04_LOGICAL_ROW_SELECTION_SCROLLING_WORK_ORDER.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/OQ4_CONTROLLER_CAPTURE_NEUTRAL_PUBLICATION_WORK_ORDER.md`

Required current source:

- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayRowSelection.cs`
- `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
- current Overlay tests under `tests/SteamInputAddonforClaw.Tests/`

For the existing authoritative-toggle pattern, also inspect current main UI examples such as:

- `src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs`

The existing main UI already uses event suppression while applying an authoritative state back into a `ToggleSwitch`; reuse that **behavioral idea**, not a new shared abstraction between the desktop UI and Overlay.

---

## 3. Current code facts that define the implementation seam

### 3.1 OQ5-UI-04 already owns logical selection

Current `OverlayRowCapabilities` is intentionally narrow:

```csharp
internal sealed record OverlayRowCapabilities(
    Func<bool> IsSelectable,
    Action? Activate = null,
    Action<int>? Adjust = null);
```

Current `OverlayWindow` already owns:

```text
per-tab row registrations
one OverlayRowSelection
selected-row highlight
BodyScroll bring-into-view
```

Therefore a Toggle row does **not** need another selection model or focus manager.

For a Toggle row:

```text
IsSelectable → current availability/editability
Activate     → request boolean inversion
Adjust       → null
```

Do not change the global navigation model for this PR.

### 3.2 `Accept` already reaches the selected row

Current `.Overlay` protocol is v4.

`Accept` already arrives at:

```text
Runtime semantic input
→ .Overlay Navigation
→ App.HandleNavigationAsync
→ OverlayWindow.ActivateSelectedRow()
→ OverlayRowSelection.ActivateSelected()
```

Do **not** bump the protocol.

Do **not** add another controller action for toggles.

### 3.3 The Device page currently contains temporary navigation-preview rows

PR #463 intentionally added temporary `Navigation Preview NN` rows so row navigation and scrolling could be exercised before real control primitives existed.

OQ5-UI-05 should start replacing that temporary-only surface with a Toggle-row preview fixture sufficient to hardware-test:

- selectable toggle activation;
- disabled/unavailable toggle behavior;
- pointer/touch behavior;
- selected-row highlight around a real WinUI control;
- continued scrolling/navigation with enough rows to exercise `BodyScroll`.

Do **not** invent a fake production feature such as fake TDP, fake CPU Boost, or fake Power Mode just to test the primitive.

Use clearly temporary labels such as `Toggle Preview` / `Unavailable Toggle Preview`.

### 3.4 No Runtime Device snapshot exists on `.Overlay` yet

The implementation roadmap intentionally schedules the real Device snapshot bridge later under `OQ5-FEAT-01`.

Therefore this PR must not pull forward:

- TDP snapshot transport;
- CPU Boost snapshot transport;
- Power Mode snapshot transport;
- FPS snapshot transport;
- a generic feature-state protocol;
- a generic mutation protocol.

Instead, the Toggle primitive must expose a narrow way for a future caller to **apply authoritative state** and a narrow way to **request a desired state**.

---

## 4. Frozen Toggle row contract

### 4.1 One row, one boolean

Conceptual layout:

```text
┌──────────────────────────────────────┐
│ Label                      [ Toggle ] │
└──────────────────────────────────────┘
```

The entire row remains one logical controller navigation target.

Use:

- a standard WinUI 3 `ToggleSwitch`;
- standard WinUI sizing;
- the existing OQ5-UI-04 outer row `Border` for selected state;
- simple label + ToggleSwitch layout.

Do not introduce custom ToggleSwitch templates or globally compact WinUI controls in this PR.

### 4.2 Authoritative state is applied from outside the control

The Toggle row must not become the authoritative feature owner.

Provide one narrow state-application seam, conceptually:

```csharp
ApplyState(
    bool isAvailable,
    bool isOn);
```

Exact naming is flexible.

Applying state must update at least:

```text
row IsSelectable fact
ToggleSwitch.IsEnabled
ToggleSwitch.IsOn
```

When future Runtime-backed features arrive, the path should be:

```text
Runtime authoritative snapshot/result
→ Overlay feature binding
→ Toggle row ApplyState(...)
```

Do not persist toggle state inside `Overlay.exe`.

Do not use the ToggleSwitch's current visual state as the long-term feature truth.

### 4.3 Change request is separate from authoritative state application

Provide one narrow desired-state request seam, conceptually:

```csharp
Action<bool> requestChange
```

or an equally small equivalent.

Controller and pointer/touch must both converge on this same request:

```text
A on selected row
→ requestChange(!authoritativeIsOn)

pointer/touch toggles switch
→ requestChange(userDesiredState)
```

The primitive itself must not know:

- TDP;
- CPU Boost;
- Windows power mode;
- FPS;
- named-pipe feature DTOs;
- hardware APIs;
- settings persistence.

### 4.4 Applying authoritative state must suppress synthetic Toggled feedback

Programmatic state application can raise `ToggleSwitch.Toggled`.

Use one small local suppression fact around authoritative rendering, equivalent in spirit to the existing main UI pattern:

```text
suppress toggle event
→ set IsOn / IsEnabled
→ clear suppression
```

This is required so:

```text
Runtime result/readback
→ ApplyState
```

does **not** generate another mutation request back to Runtime.

Do not create a shared/global event-suppression manager.

### 4.5 Disabled/unavailable state

When `isAvailable == false`:

```text
ToggleSwitch.IsEnabled = false
OverlayRowCapabilities.IsSelectable() = false
A cannot mutate it
pointer/touch cannot mutate it
Up/Down skips it through the existing OQ5-UI-04 selection logic
```

Do not hide an unavailable row merely because it cannot mutate. A visible disabled row is useful when a later feature needs to explain that a setting exists but is unavailable.

Exact unavailable-description text belongs to later feature bindings, not this primitive PR.

### 4.6 Left / Right stays no-op

Toggle rows do not register `Adjust`.

Therefore:

```text
selected Toggle row
+ Left / Right
→ no-op
```

Do not duplicate A-toggle behavior onto Left/Right in this PR.

---

## 5. Keep selection visuals owned by OQ5-UI-04

The ToggleSwitch's internal focus/checked visual is **not** the controller-selection indicator.

Required layering:

```text
outer row Border
→ OQ5 logical selected-row highlight

inner ToggleSwitch
→ On/Off state only
```

Do not call:

- `Focus()`;
- HWND activation;
- `SendInput`;
- synthetic Space/Enter;
- keyboard navigation APIs.

The Overlay remains `WS_EX_NOACTIVATE` / no-activate.

Do not create a second selection brush or per-control controller-focus implementation.

---

## 6. Recommended narrow implementation shape

A small Overlay-local class is appropriate, for example:

```text
OverlayToggleRow
├─ Border Container
├─ ToggleSwitch Toggle
├─ OverlayRowCapabilities Capabilities
├─ current authoritative isAvailable
├─ current authoritative isOn
├─ requestChange(bool desired)
└─ ApplyState(isAvailable, isOn)
```

Exact class name and file split may differ if a simpler implementation is clearer.

Important constraints:

- Overlay-only type;
- no interface hierarchy;
- no base `OverlayControlRow` class yet;
- no view-model framework;
- no service registration;
- no generalized command system;
- no shared desktop/Overlay control abstraction.

OQ5-UI-06 can later add a separate Slider row primitive beside this type. Do not pre-design a class hierarchy merely because both are rows.

---

## 7. Pointer / touch behavior

Pointer/touch must remain naturally usable.

Required:

```text
user directly toggles the enabled ToggleSwitch
→ desired state request fires once
```

Programmatic `ApplyState` must not fire that request.

For this PR it is **not required** that pointer/touch interaction also changes the logical controller-selected row.

Do not broaden OQ5-UI-04 with a pointer-selection manager solely for the Toggle primitive.

Existing outside-click dismissal remains unchanged:

```text
pointer inside Overlay
→ interact normally

pointer outside Overlay
→ existing Runtime-owned dismiss path
```

---

## 8. Temporary Device-page validation fixture

The repository is pre-release and the actual feature transport intentionally comes later, so use a clearly non-production fixture to prove this primitive.

Recommended Device-page fixture:

```text
Toggle Preview                    [ Off ]
Unavailable Toggle Preview        [ Off, disabled ]
Navigation Preview 01
Navigation Preview 02
...
```

Keep enough additional selectable preview rows to continue validating scroll/bring-into-view from OQ5-UI-04.

For the enabled Toggle Preview only:

```text
requestChange(desired)
→ local fixture immediately applies desired as its authoritative preview state
```

This local callback exists **only** to hardware-test the primitive before real Runtime feature binding.

It must be clearly isolated from product feature authority.

The unavailable preview must remain disabled/unselectable.

Do not name preview rows after actual product settings.

Do not write any local settings file.

Do not persist preview state across Overlay process restart.

It is acceptable for every warm Overlay session to retain the preview boolean until the process exits; this is diagnostic UI state only, not a product contract. Do not add reset/persistence machinery for it.

---

## 9. Existing lifecycle and capture contracts remain unchanged

OQ5-UI-05 is a frontend primitive PR.

It must not modify:

- PID1902 / PID1901 behavior;
- DirectInput ownership;
- HidHide ownership;
- VIIPER ownership;
- X360 / SteamDeck presentation selection;
- OQ4 pause / neutral / release-to-resume;
- stick deadzone/release gate;
- LB/RB tab release gate;
- physical-device recovery;
- Overlay process ownership;
- Main UI ↔ Overlay retirement ordering;
- Steam QAM coexistence policy;
- outside-click Runtime retirement.

B remains global Overlay close.

No feature mutation is important enough in this PR to justify touching controller lifecycle code.

---

## 10. `.Overlay` transport remains v4

Do not change:

```text
OverlayTransportProtocol.CurrentVersion = 4
```

No new transport message is required because this PR has no Runtime-backed setting yet.

Do not send:

- toggle state;
- availability;
- fake preview state;
- raw controller state

over the pipe.

The actual Runtime feature snapshot/mutation contract is later work.

---

## 11. Tests

Keep tests focused on durable behavior that future real toggles depend on.

### 11.1 Required logical-row regression coverage

Existing OQ5-UI-04 tests must remain green, especially:

- unavailable rows are skipped;
- activation only dispatches for an activatable selected row;
- stale/unavailable selection normalizes before mutation;
- the same input that only normalizes selection does not mutate the fallback row.

### 11.2 Toggle-specific testable behavior

If the Toggle primitive implementation separates a small pure state/request object from WinUI rendering, cover at least:

- available Off + controller activation requests `true`;
- available On + controller activation requests `false`;
- unavailable state emits no controller request;
- authoritative `ApplyState` changes the current state without producing another request;
- repeated authoritative state application does not create a feedback loop.

If those behaviors remain directly inside a WinUI `ToggleSwitch` wrapper and are not practical to instantiate in the current unit-test environment, do **not** build a larger abstraction solely for unit testing. Instead keep the wrapper small, preserve existing pure row-selection coverage, and validate pointer/controller behavior on hardware.

### 11.3 Regression suite

Required before PR completion:

```text
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test
git diff --check
```

No warnings/errors introduced by this work.

---

## 12. Hardware validation

On the supported MSI Claw, with Center M Disabled / Addon-owned PID1902 path active:

### A. Controller Toggle

1. Open Addon Overlay.
2. Navigate to `Toggle Preview` with DPad or either stick.
3. Press A.
4. Verify the ToggleSwitch changes once.
5. Press A again.
6. Verify it returns to the opposite state once.

Expected:

```text
one A press
→ one request
→ one visible authoritative preview update
```

No double-toggle.

### B. Left / Right no-op

1. Select `Toggle Preview`.
2. Press DPad Left/Right or use either stick Left/Right.

Expected:

```text
Toggle state unchanged
```

### C. Unavailable row

1. Navigate through the Device page.

Expected:

- unavailable Toggle preview is skipped by controller selection;
- its ToggleSwitch is visibly disabled;
- it cannot be toggled with A;
- direct pointer/touch cannot change it.

### D. Pointer / touch

1. Directly click/tap the enabled ToggleSwitch.

Expected:

- exactly one change request;
- visual state follows the fixture's applied authoritative preview state;
- no duplicate request caused by programmatic state application.

### E. Selection highlight

Expected:

- selected-row accent Border remains visible around Toggle rows;
- the ToggleSwitch does not need keyboard focus;
- Overlay does not activate/steal foreground focus.

### F. Scroll regression

Use remaining navigation-preview rows to move beyond the initial viewport.

Expected:

- selected row still brings into view;
- Toggle rows participate in the same row ordering;
- no nested/per-control scrolling appears.

### G. OQ4 safety regression

Open/close the Overlay using normal controller flow.

Expected:

- game / Steam QAM behind receives no controller input while Overlay capture is active;
- B closes through the existing Runtime-owned retirement path;
- no PID/DirectInput/HidHide/VIIPER churn occurs.

---

## 13. Failure behavior

This PR has no real hardware mutation, so failure handling stays simple.

The future feature contract must support:

```text
request desired bool
→ Runtime operation
→ authoritative result/readback
→ ApplyState(...)
```

If Runtime later rejects or fails a mutation, the authoritative readback must be able to restore the ToggleSwitch through the same `ApplyState` seam.

Do not implement speculative retry/toast/error-state infrastructure in this primitive PR.

Do not make the Toggle row remember a failed desired state as truth.

---

## 14. Explicit non-goals

Do **not** implement any of the following in OQ5-UI-05:

- TDP control;
- CPU Boost control;
- Windows Power Mode control;
- FPS limit control;
- Runtime Device snapshot bridge;
- generic `.Overlay` feature snapshot/mutation protocol;
- slider row;
- delayed/debounced mutation helper;
- Choice row/dropdown;
- Shortcut slots;
- tab-order persistence/editor;
- per-page row persistence;
- pointer-driven logical-selection manager;
- custom ToggleSwitch templates;
- compact control scaling;
- generic `OverlayRowBase` / control hierarchy;
- generic MVVM/navigation framework;
- another controller reader;
- another capture/presentation authority;
- Steam QAM visibility management;
- Game Bar policy;
- OQ6 physical-button mapping.

---

## 15. Acceptance criteria

OQ5-UI-05 is complete when all of the following are true:

1. A standard WinUI 3 Toggle row primitive exists inside `SteamInputAddonforClaw.Overlay`.
2. The primitive plugs into the existing `OverlayRowCapabilities` model rather than introducing another controller-selection system.
3. `A / Accept` on a selected available Toggle row requests the opposite boolean state exactly once.
4. `Left / Right` on a Toggle row is a no-op.
5. Pointer/touch on the ToggleSwitch requests the user-desired state through the same request seam.
6. Programmatic authoritative state application does not recursively produce another mutation request.
7. Unavailable Toggle rows expose `IsSelectable == false` and disable the WinUI ToggleSwitch.
8. OQ5 selected-row Border highlighting remains the controller-selection visual; no HWND/keyboard focus is required.
9. Device page contains clearly labeled temporary Toggle preview state sufficient for hardware validation, without pretending to be a real product feature.
10. Enough existing preview rows remain to validate scrolling and bring-into-view.
11. No Runtime feature snapshot or mutation transport is added.
12. `.Overlay` protocol remains v4.
13. No PID / DirectInput / HidHide / VIIPER / OQ4 lifecycle behavior changes.
14. Existing Overlay navigation, LB/RB tabs, both-stick navigation, outside-click dismissal, B close, show/hide animation, and capture/release tests remain green.
15. Release build, full test suite, and `git diff --check` pass.
16. Hardware validation confirms controller toggle, pointer/touch toggle, unavailable-row skip, no double-toggle, and no focus-steal regression.

---

## 16. Review focus

Review this PR primarily for:

- whether the Toggle row reuses the OQ5-UI-04 row-selection authority;
- whether A and pointer/touch converge on one desired-state request seam;
- whether authoritative `ApplyState` is protected from Toggled feedback;
- whether unavailable rows are both disabled and non-selectable;
- whether the primitive is genuinely feature-agnostic;
- whether preview state is clearly temporary and non-persistent;
- whether the implementation avoided creating a generalized control/view-model abstraction prematurely;
- whether controller capture/lifecycle code remained untouched.

Do not request new managers, interfaces, persistence, transport DTOs, or retry machinery solely for hypothetical future features.
