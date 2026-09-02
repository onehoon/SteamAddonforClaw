# Work Order — OQ5-UI-02: LB/RB Tab Navigation

## Status

Second implementation PR from:

- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`

Track: Addon Quick Settings Overlay UI

Label/name: `OQ5-UI-02`

Baseline: `main` after PR #460 / commit `306c1ea58cd9c1d24cb2f71cb48f4f5abf131707`.

This is **not** part of the numbered Full PID1902 PR sequence.

---

## 1. Goal

Add controller-driven top-level tab switching to the five-tab Overlay shell implemented by OQ5-UI-01.

Frozen controller meaning for this PR:

```text
LB → previous tab
RB → next tab
LT → no action
RT → no action
B  → close Overlay through the existing OQ4 Runtime-owned dismiss path
```

The implementation must remain semantic and low-rate:

```text
physical PID1902 state
→ existing OverlayControllerInputRouter
→ PreviousTab / NextTab semantic action
→ existing .Overlay pipe
→ Overlay DispatcherQueue
→ existing OverlayTabState
→ selected tab/page visual update
```

Do not introduce raw controller state IPC or another input owner.

---

## 2. Required reading before implementation

Read current `main`, not the pre-OQ5 shell POC.

Required documents:

- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/overlayui/OQ5_UI_01_FIVE_TAB_OVERLAY_SHELL_WORK_ORDER.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/OQ4_CONTROLLER_CAPTURE_NEUTRAL_PUBLICATION_WORK_ORDER.md`

Required current source:

- `src/SteamInputAddonforClaw/Lifecycle/OverlayControllerInputRouter.cs`
- `src/SteamInputAddonforClaw/Input/ControllerState.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs`
- `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayTabState.cs`
- relevant existing Overlay transport/router/tab tests under `tests/SteamInputAddonforClaw.Tests/`

---

## 3. Current code facts that must guide the change

### 3.1 OQ5-UI-01 shell is already merged

Current `OverlayTabState` owns only:

```text
Order
SelectedTab
Select(tab)
ResetForShow()
```

Current default order is:

```text
Device
Profile
Controller
Shortcut
Setting
```

Every Show still resets to `Order[0]` before visual reveal.

Pointer/touch tab selection is already implemented in `OverlayWindow`.

Do not replace this state with another tab/navigation manager.

### 3.2 Current OQ4 semantic navigation protocol

Current `.Overlay` protocol is version 3.

Current navigation enum:

```text
NavigateUp
NavigateDown
NavigateLeft
NavigateRight
Accept
Back
```

LB/RB do not currently have a semantic action.

### 3.3 Current physical button model already contains bumpers

`GamepadButtons` already exposes:

```text
LeftBumper
RightBumper
```

Do not add a second physical-controller representation.

### 3.4 Current Overlay router already has the correct ownership seam

`OverlayControllerInputRouter` currently:

- subscribes to the existing prepared PID1902 input source;
- snapshots the initial button state at capture start;
- emits only false → true button edges;
- never blocks the DirectInput callback on pipe I/O;
- serializes semantic delivery through its existing channel;
- uses the same `Bindings` set to determine which controls are consumed for release-to-resume.

Current consumed controls are DPad + A + B.

This PR must extend that existing seam rather than adding another bumper-specific handler.

### 3.5 Current Overlay UI navigation handler

`App.HandleNavigationAsync()` already marshals semantic actions onto the existing `DispatcherQueue`.

`Back` remains special because it requests Runtime-owned dismissal.

Previous/Next tab actions should use this same UI dispatch path.

---

## 4. Frozen behavior

### 4.1 LB / RB mapping

Add two explicit semantic actions:

```text
PreviousTab
NextTab
```

Map:

```text
LeftBumper  rising edge → PreviousTab
RightBumper rising edge → NextTab
```

Do not overload existing `NavigateLeft` / `NavigateRight` for top-level tab switching.

Reason:

- Left/Right is reserved for row adjustment once sliders/choice rows exist;
- tab navigation is a distinct global action;
- later page controls must not need to infer whether Left/Right means tab or row mutation.

### 4.2 Rising-edge only

Keep the current OQ4 edge semantics.

```text
released → pressed → one action
held → no additional actions
pressed → released → no action
```

A bumper already held when Overlay capture begins must not switch tabs.

The current router already seeds `_previous` from `LatestState` at `Start()`; preserve that behavior.

Do not add hold-repeat in this PR.

### 4.3 Boundary behavior — no wrap

Initial behavior is bounded/no-wrap:

```text
Device + LB  → Device
Setting + RB → Setting
```

For an arbitrary future persisted order:

```text
Order[0] + PreviousTab       → no-op
Order[Order.Count - 1] + NextTab → no-op
```

Do not wrap first ↔ last.

Do not add a user preference for wrap behavior.

### 4.4 B remains global close

Do not change:

```text
B / Back
→ SendDismissRequestedAsync
→ Runtime unified Overlay retirement
→ OQ4 release gate
→ same presentation resumes when safe
```

The tab/page layer must not reinterpret B as a local Back action.

### 4.5 LT / RT remain empty

No action for:

```text
LeftTriggerFull
RightTriggerFull
Triggers.Left
Triggers.Right
```

Do not consume LT/RT merely because the controller model exposes them.

### 4.6 X / Y remain empty

No new X/Y behavior in this PR.

---

## 5. Transport change

### 5.1 Bump `.Overlay` protocol version

Adding new semantic navigation enum values changes the wire contract.

Bump:

```text
OverlayTransportProtocol.CurrentVersion
3 → 4
```

Update the version comment accordingly.

Do not attempt compatibility fallback with v3.

The existing handshake mismatch behavior is sufficient.

### 5.2 Add navigation actions only

Extend `OverlayNavigationAction` with:

```csharp
PreviousTab,
NextTab,
```

Do not add:

- LB/RB raw button fields;
- ControllerState payload;
- physical-button bitmasks;
- repeat metadata;
- timestamps;
- sequence numbers;
- ACKs for navigation.

The existing fire-and-forget `SendNavigationAsync()` contract remains unchanged.

### 5.3 Strict message validation remains unchanged

The current client/server validation for `Navigation` frames remains the authority.

Do not weaken protocol validation to make new enum values pass.

---

## 6. Runtime router change

Modify only the existing `OverlayControllerInputRouter` mapping.

Conceptually the binding set becomes:

```csharp
private static readonly (Func<GamepadButtons, bool> Held, OverlayNavigationAction Action)[] Bindings =
[
    (b => b.DPadUp, OverlayNavigationAction.NavigateUp),
    (b => b.DPadDown, OverlayNavigationAction.NavigateDown),
    (b => b.DPadLeft, OverlayNavigationAction.NavigateLeft),
    (b => b.DPadRight, OverlayNavigationAction.NavigateRight),
    (b => b.A, OverlayNavigationAction.Accept),
    (b => b.B, OverlayNavigationAction.Back),
    (b => b.LeftBumper, OverlayNavigationAction.PreviousTab),
    (b => b.RightBumper, OverlayNavigationAction.NextTab),
];
```

Exact source ordering may differ if tests/readability benefit, but do not create a second bumper collection.

Because `AnyConsumedHeld(...)` already evaluates the same binding set, adding bumpers here must also make LB/RB consumed by the OQ4 release gate.

That is intentional and required.

---

## 7. Release-to-resume safety

This is a lifecycle requirement, not optional polish.

Before this PR:

```text
consumed release gate = DPad + A + B
```

After this PR:

```text
consumed release gate = DPad + A + B + LB + RB
```

Example real user path:

```text
Overlay visible
→ user holds RB
→ Setting/next tab selected
→ user closes Overlay through outside click or another close path while RB is still held
→ Overlay hides
→ current virtual presentation remains neutral
→ wait for RB release
→ then resume publisher
```

Without this, the held bumper can leak into the resumed game/Steam UI.

Do not solve this with:

- timers;
- sleeps;
- release epochs;
- another release manager.

Reuse the existing OQ4 event-driven release waiter.

### 7.1 Source loss still wins

Existing behavior remains:

```text
physical DirectInput source lost while waiting
→ SourceUnavailable
→ do not resume against dead input
→ existing physical recovery owns recovery
```

Do not special-case bumpers around source loss.

---

## 8. Overlay tab-state change

Add the narrowest tab traversal operations to the existing `OverlayTabState`.

Recommended shape:

```csharp
internal bool SelectPrevious()
internal bool SelectNext()
```

Behavior:

```text
returns true  → selection changed
returns false → already at boundary; selection unchanged
```

Implementation should derive the current position from `Order` and `SelectedTab`.

Do not hard-code:

```text
Device → Profile → Controller → Shortcut → Setting
```

inside traversal logic.

The order is intentionally abstracted already because a later PR will persist a user-defined order.

Example:

```text
order = Controller, Device, Profile, Shortcut, Setting
selected = Device
PreviousTab → Controller
NextTab     → Profile
```

Do not implement persisted order in this PR.

---

## 9. Overlay UI dispatch

### 9.1 Keep one DispatcherQueue path

Extend the existing `App.HandleNavigationAsync()` switch/dispatch behavior.

Conceptually:

```text
PreviousTab → OverlayWindow.SelectPreviousTab()
NextTab     → OverlayWindow.SelectNextTab()
Back        → existing SendBackDismissAsync()
other actions → unchanged for now
```

Do not create a second navigation queue or tab IPC client.

### 9.2 OverlayWindow owns visual application

Add narrow methods on `OverlayWindow`, conceptually:

```csharp
internal void SelectPreviousTab();
internal void SelectNextTab();
```

Each method should:

1. ask `OverlayTabState` to change selection;
2. only call `ApplySelectedTabVisualState()` when selection actually changed;
3. do nothing at a boundary.

This keeps the existing visual dictionaries/page visibility logic in one place.

Do not expose `_tabButtons`, `_tabPages`, or `OverlayTabState` to `App`.

### 9.3 Footer hint may now advertise implemented behavior

Because LB/RB becomes real after this PR, the footer may change from:

```text
B  Close
```

to a compact truthful hint such as:

```text
LB/RB  Tabs    B  Close
```

Do not add LT/RT/X/Y hints.

Do not spend this PR building controller glyph assets or a generalized hint renderer.

---

## 10. Tests

Use existing behavior tests and add narrow tests around the changed contracts.

### 10.1 OverlayTabState traversal

Add tests proving:

```text
Device + Next → Profile
Profile + Previous → Device
Setting + Next → no change
Device + Previous → no change
```

Also prove traversal follows injected/current order rather than enum declaration order:

```text
order = Controller, Device, Profile, Shortcut, Setting
Device + Previous → Controller
Device + Next     → Profile
```

Do not test implementation details such as dictionary enumeration order.

### 10.2 Router bumper edges

Add/extend `OverlayControllerInputRouter` tests proving:

- LB released → pressed emits exactly `PreviousTab` once;
- RB released → pressed emits exactly `NextTab` once;
- holding LB/RB does not repeatedly emit;
- release does not emit;
- bumper already held at router start does not emit until released then pressed again;
- LT/RT still emit nothing;
- X/Y still emit nothing.

### 10.3 Release gate

Add tests proving:

```text
LB held when WaitForConsumedControlsReleaseAsync starts
→ waiter remains incomplete
→ LB release StateChanged
→ ReleasedAfterWait
```

and same for RB.

Also preserve existing DPad/A/B release tests.

### 10.4 Transport protocol

Update existing protocol-version expectations from v3 to v4.

Add/adjust round-trip coverage proving `PreviousTab` and `NextTab` serialize/deserialize as valid semantic Navigation frames.

Do not weaken malformed-frame/version-mismatch tests.

### 10.5 Existing regressions

Existing tests for these areas must remain green:

- Overlay Show/Hide transport;
- DismissRequested;
- OQ4 source loss;
- Overlay capture retirement;
- presentation pause/resume;
- OQ5-UI-01 tab reset/default order.

---

## 11. Manual / hardware validation

Use the existing Runtime → Overlay path on a supported MSI Claw.

Validate:

1. Open Overlay → Device selected.
2. Tap RB once → Profile.
3. Tap RB repeatedly → Controller → Shortcut → Setting.
4. Tap RB on Setting → remains Setting; no wrap.
5. Tap LB repeatedly → moves backward to Device.
6. Tap LB on Device → remains Device; no wrap.
7. Hold RB → only one tab change; no repeat.
8. Release and press RB again → one additional tab change.
9. Enter Overlay while RB is already physically held → no tab change until release and a fresh press.
10. LT/RT do nothing.
11. B still closes Overlay.
12. Outside click/touch still closes Overlay.
13. Close Overlay while LB or RB is held → game/Steam QAM behind does not receive that held bumper before physical release.
14. Steam QAM may remain visible behind Addon Overlay; this PR must not close/restart QamHost.
15. Show again after navigating away → first tab is selected again, preserving OQ5-UI-01 behavior.

No hardware-only item should be marked validated unless actually tested on the device.

---

## 12. Expected files

Primary expected changes:

```text
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
src/SteamInputAddonforClaw/Lifecycle/OverlayControllerInputRouter.cs
src/SteamInputAddonforClaw.Overlay/App.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayTabState.cs
```

Possible tiny XAML change only for truthful footer copy:

```text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
```

Tests under:

```text
tests/SteamInputAddonforClaw.Tests/
```

No Runtime controller-owner/presentation-owner architecture change is expected.

---

## 13. Build / verification

Required before PR completion:

```text
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test
git diff --check
```

If the full suite exposes a known unrelated flaky failure, reproduce it on unchanged current `main` before treating it as unrelated.

---

## 14. Acceptance criteria

The PR is complete when all of the following are true:

- [ ] `.Overlay` protocol is bumped to v4;
- [ ] `PreviousTab` and `NextTab` are explicit semantic actions;
- [ ] LB rising edge emits `PreviousTab` exactly once;
- [ ] RB rising edge emits `NextTab` exactly once;
- [ ] a bumper already held at capture start emits nothing;
- [ ] held bumpers do not repeat;
- [ ] LT/RT remain no-op;
- [ ] X/Y remain no-op;
- [ ] `OverlayTabState` traverses the current order, not a hard-coded tab sequence;
- [ ] first/last boundaries are no-op; no wrap;
- [ ] Overlay UI applies tab changes through the existing DispatcherQueue;
- [ ] pointer/touch tab selection from OQ5-UI-01 still works;
- [ ] every Show still resets to current `Order[0]` before reveal;
- [ ] B still closes through the Runtime-owned OQ4 dismiss path;
- [ ] LB/RB are included in the OQ4 consumed-controls release gate;
- [ ] close while a bumper is held stays neutral until bumper release;
- [ ] source loss while waiting still returns `SourceUnavailable` and does not resume against dead input;
- [ ] no raw controller state crosses `.Overlay`;
- [ ] no PID/DirectInput/HidHide/VIIPER ownership behavior changes;
- [ ] Steam QAM is not closed or inspected merely because Addon Overlay is visible;
- [ ] no persisted tab order is added yet;
- [ ] no stick navigation is added yet;
- [ ] Release build/tests/diff check are clean.

---

## 15. Explicit non-goals

Do **not** include any of the following in OQ5-UI-02:

- left-stick navigation;
- right-stick navigation;
- stick deadzone/threshold policy;
- hold-repeat for DPad or bumpers;
- tab-order persistence;
- Setting-page reorder UI;
- row selection/focus model;
- slider/toggle controls;
- feature mutation or snapshots;
- Shortcut actions;
- LT/RT actions;
- X/Y actions;
- generalized navigation manager;
- generalized input timing manager;
- navigation epochs/barriers;
- Steam-QAM visibility/XOR manager;
- physical WING/OEM1 button policy;
- window geometry/DPI changes.

---

## 16. Overengineering guard

This PR addresses a normal, user-visible controller path:

```text
Overlay visible
→ user presses LB/RB
→ tab changes
→ user may close while bumper is still held
→ held bumper must not leak through when game-facing publication resumes
```

Protect that real lifecycle path.

Do not add synchronization or state solely for pathological instruction-level crossings.

The intended authority structure after this PR remains:

```text
one physical input owner
→ one existing OverlayControllerInputRouter
→ one semantic .Overlay transport
→ one OverlayTabState
→ one existing visual shell
→ one existing OQ4 release path
```

No additional manager/authority is required.