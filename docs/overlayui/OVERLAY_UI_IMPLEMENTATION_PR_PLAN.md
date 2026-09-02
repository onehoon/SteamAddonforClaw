# Addon Quick Settings Overlay — UI Implementation PR Plan

> **Status:** Implementation roadmap  
> **Date:** 2026-09-02  
> **Baseline:** `main` at `2e25819e3e292ebb0deb85e20f34e55452b242ab`  
> **Scope:** Implement the currently agreed Overlay UI shell, controller-first navigation, common control interaction, tab-order preference, Shortcut foundation, and the first Device controls in focused, independently reviewable PRs.  
> **Folder:** `docs/overalyui/` contains this roadmap and the per-PR work orders derived from it.

---

## 1. Authoritative documents and current code seams

Read this plan together with:

- `docs/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/OQ3_A_MAIN_UI_OVERLAY_VISIBLE_SURFACE_COEXISTENCE_WORK_ORDER.md`
- `docs/work-order/OQ4_CONTROLLER_CAPTURE_NEUTRAL_PUBLICATION_WORK_ORDER.md`

Current implementation seams verified on the baseline `main`:

- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml`
  - 400 DIP panel
  - `#FFF3F3F3` opaque surface
  - `Padding="28"`
  - current POC content is one `StackPanel`
  - `AnimationViewport`, `OpaquePanel`, and `AnimatedContent` are existing animation/diagnostic seams that must remain stable while the shell evolves.
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs`
  - `ShowForPocAsync()` / `HideForPocAsync()` own the current visual show/hide animation and outside-click arm/disarm lifecycle.
  - visible diagnostic text is still written by `ConfigureWindow()` / `ShowNavigationDiagnostic()`.
- `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
  - Runtime `OverlayCommand.Show` currently dispatches directly to `ShowForPocAsync()`.
  - semantic OQ4 navigation currently only displays a diagnostic; `Back` routes to the existing `DismissRequested` path.
- `src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs`
  - protocol v3
  - semantic navigation only
  - current actions: Up / Down / Left / Right / Accept / Back.
- `src/SteamInputAddonforClaw/Lifecycle/OverlayControllerInputRouter.cs`
  - existing OQ4 owner for translating the PR5 physical input source into semantic Overlay input.
  - current consumed controls/release gate are DPad + A + B.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawControllerStateMapper.cs`
  - both sticks are already normalized into signed `short` axes centered around zero.
  - Y is normalized with the existing inversion, so the Overlay router can reason in logical up/down direction without another raw DirectInput mapping layer.
- `src/SteamInputAddonforClaw/Settings/SettingsStore.cs`
  - Runtime already owns current-user `settings.json` persistence through the canonical `SettingsStore`.
  - Overlay UI preferences must extend this Runtime-owned persistence rather than making `Overlay.exe` write a second settings file.
- `src/SteamInputAddonforClaw.QamHost/Frontend/qam.js`
  - current slider mutation behavior uses a 2-second trailing commit, immediate local preview, latest-value-wins replacement for the same key, and authoritative refresh/readback after commit.

---

## 2. Frozen UI/product decisions

The implementation sequence below must preserve these decisions unless a later explicit product decision changes them.

### 2.1 Tabs

Exactly five top-level horizontal tabs:

```text
Device
Profile
Controller
Shortcut
Setting
```

All five pages exist from the first shell PR even when their bodies are placeholders.

The tab collection is reorderable by the user later in this sequence.

The first tab in the persisted order is the page selected on **every Overlay Show**.

There is no separate `DefaultTab` setting and no last-tab restore.

### 2.2 Fixed controller bindings

```text
LB          previous tab
RB          next tab
LT          reserved / no action
RT          reserved / no action
B           always close Overlay
A           select / activate
X           reserved / no action
Y           reserved / no action
DPad        directional navigation / adjustment
Left Stick  directional navigation / adjustment
Right Stick directional navigation / adjustment
```

`B` remains a root close action. Do not introduce a local Overlay back stack in this sequence.

### 2.3 Slider and toggle interaction

Slider:

```text
Up / Down   move row selection
Left / Right immediately change the selected slider value
A           not required to enter an edit mode
```

Toggle:

```text
Up / Down   move row selection
A           toggle the selected value
```

No separate slider edit mode.

### 2.4 Mutation pacing

Behaviorally reuse the existing QAM slider policy:

```text
controller adjustment
→ immediate local preview
→ replace pending same-key value
→ trailing commit after the existing relaxed delay
→ Runtime mutation
→ authoritative result/readback
```

Do not duplicate a second independent debounce authority in both Runtime and Overlay.

### 2.5 Visual baseline

For the initial UI implementation:

- keep the current 400 DIP panel width;
- keep the current opaque `#FFF3F3F3` surface;
- keep standard WinUI 3 control sizing and typography defaults unless a later polish PR deliberately changes them;
- preserve a real panel inset instead of edge-to-edge content;
- evolve the current single POC stack into a structured `Header / Tabs / Body / Footer` shell;
- keep the body scrollable as content grows.

### 2.6 Reordering scope

General Device/Profile/Controller/Setting row reordering is **not supported**.

Tab order is configurable.

Shortcut is the one planned exception: it uses a small fixed set of configurable shortcut slots, not a generalized reorder framework.

### 2.7 Shortcut direction

Initial Shortcut page foundation:

```text
4 fixed slots
2 × 2 visual layout
controller directional selection
slot assignment/order may be configurable later
```

The actual action catalog/execution policy is not frozen by the current UI design and must not be invented merely to fill the page.

---

## 3. Architecture rules for all PRs

### 3.1 Runtime remains authority

Overlay is a frontend.

Do not add:

- Overlay-owned TDP/CPU/FPS managers;
- another settings file owned by `Overlay.exe`;
- another DirectInput session;
- another presentation/capture authority;
- a generic input event bus;
- a generic UI state manager.

### 3.2 Preserve OQ4 capture safety

Normal Overlay UI work must not change:

- PID1901/PID1902;
- DirectInput ownership;
- HidHide;
- VIIPER server/bus ownership;
- X360/SteamDeck presentation attachment on normal open/close.

Any newly consumed physical controller input must be reflected in the OQ4 release-to-resume gate so a held control cannot leak into the game immediately after close.

This is especially important for:

- LB/RB once tab switching is added;
- both analog sticks once stick navigation is added.

LT/RT/X/Y remain unconsumed/reserved and therefore do not need release-gate ownership in this phase.

### 3.3 Preserve surface/process lifecycle

Do not regress:

- warm hidden Overlay process;
- no-activate/topmost behavior;
- WorkArea placement;
- outside-click dismissal;
- Main UI ↔ Overlay retirement ordering;
- Steam QAM allowed to remain visible behind Addon Overlay;
- unexpected Overlay loss → Runtime-owned capture retirement.

### 3.4 Prefer small explicit contracts

When a tab/control needs a new behavior, add the narrowest explicit contract that represents it.

Do not introduce reusable frameworks before more than one real consumer requires them.

---

# 4. PR sequence overview

## Phase A — shell and controller navigation

| PR | Title | Primary outcome |
|---|---|---|
| OQ5-UI-01 | Five-tab Overlay shell | Replace POC text stack with the final top-level page skeleton and reset to first tab on every Show. |
| OQ5-UI-02 | LB/RB tab navigation | Add explicit PreviousTab/NextTab semantics and include bumpers in the consumed-control release gate. |
| OQ5-UI-03 | Dual-stick directional navigation | Convert both normalized sticks into edge-driven directional navigation with a simple neutral/deadzone re-arm rule. |
| OQ5-UI-04 | Logical row selection and scrolling | Add one controller-first selected-row model for Up/Down, Left/Right dispatch, A activation, and bring-into-view. |

## Phase B — common Quick Settings controls

| PR | Title | Primary outcome |
|---|---|---|
| OQ5-UI-05 | Toggle row primitive | Add the first standard WinUI toggle row using A to activate and Runtime-provided availability state. |
| OQ5-UI-06 | Slider row primitive | Add direct Left/Right slider adjustment without an A/edit mode. |
| OQ5-UI-07 | Shared delayed slider commit behavior | Implement the existing QAM-style local-preview / trailing-commit / latest-value-wins behavior as one narrow Overlay mutation helper. |

## Phase C — tab order preference

| PR | Title | Primary outcome |
|---|---|---|
| OQ5-UI-08 | Runtime-owned Overlay tab-order setting | Extend canonical Runtime settings with only the ordered five known tab IDs and deterministic validation/defaulting. |
| OQ5-UI-09 | Overlay preference transport | Send current tab order to Overlay and accept a narrow tab-order mutation through `.Overlay`; Overlay never writes settings directly. |
| OQ5-UI-10 | Setting-page tab order editor | Add controller/pointer UI for reordering the five tabs; first item becomes the next Show's startup tab. |

## Phase D — Shortcut foundation

| PR | Title | Primary outcome |
|---|---|---|
| OQ5-UI-11 | Shortcut 2×2 slot shell | Add four fixed selectable shortcut tiles, initially unassigned where no action contract exists. |
| OQ5-UI-12 | Shortcut slot preference model | Add only the persistence/assignment shape needed by four slots; no generalized row/card reorder system. |

## Phase E — first Device controls through existing Runtime authorities

These PRs begin only after the common UI/input/persistence foundation above is stable.

| PR | Title | Primary outcome |
|---|---|---|
| OQ5-FEAT-01 | Overlay Device snapshot bridge | Add a narrow Device-page snapshot contract sourced from existing Runtime authorities. No duplicate hardware readers. |
| OQ5-FEAT-02 | Device TDP control | Bind TDP status/toggle/sliders to the existing Runtime TDP authority using the common row primitives and delayed slider policy. |
| OQ5-FEAT-03 | Device CPU Boost control | Bind CPU Boost enable/mode controls to the existing Runtime authority. |
| OQ5-FEAT-04 | Device Power Mode control | Bind Windows Power Mode to the existing Runtime authority. |
| OQ5-FEAT-05 | Device Intel FPS Limit control | Bind FPS limit only where the current Runtime reports the feature available/writable. |

Profile and Controller page **content** is intentionally not invented in this roadmap. Their pages and navigation exist from OQ5-UI-01, but production controls should be planned only after their exact product contents are frozen against the current Runtime feature contracts.

Likewise, the Shortcut action catalog/execution layer is a later focused plan. OQ5-UI-11/12 only establish the agreed four-slot surface and preference boundary.

---

# 5. Detailed PR boundaries

## OQ5-UI-01 — Five-tab Overlay shell

### Goal

Create the stable visual/page skeleton before feature controls are added.

### Changes

- preserve `AnimationViewport`, `OpaquePanel`, `AnimatedContent`;
- replace visible POC diagnostic layout with a structured shell;
- add five horizontal tabs in the frozen order;
- create five page containers/placeholders;
- add scrollable body region;
- add a minimal footer suitable for controller hints;
- add one small tab identity model;
- select the first tab in the current order before **every** `ShowForPocAsync()` visual reveal;
- pointer/touch tab click may select a tab immediately;
- keep `Back` close behavior unchanged in `App.xaml.cs`;
- stop rendering geometry/navigation diagnostics as normal user-facing content, while retaining existing diagnostic logging.

### Explicit non-goals

- LB/RB navigation;
- sticks;
- persisted reorder;
- row focus model;
- real settings;
- Shortcut actions;
- custom control sizing;
- geometry/window changes.

---

## OQ5-UI-02 — LB/RB tab navigation

### Goal

Make tab switching a first-class semantic controller operation.

### Changes

- extend `OverlayNavigationAction` with explicit previous/next tab actions;
- bump `.Overlay` protocol because enum semantics changed;
- map LB/RB rising edges in the existing OQ4 router;
- keep LT/RT ignored;
- add LB/RB to the consumed-control release gate;
- dispatch previous/next in Overlay UI;
- use bounded no-op behavior at the first/last tab initially rather than creating another navigation mode;
- keep B global close.

### Tests

- LB/RB rising edge emits exactly one semantic action;
- held bumper at capture start does not switch tab;
- release edge does not emit;
- LT/RT emit nothing;
- close waits for held LB/RB release before publisher resume.

---

## OQ5-UI-03 — Dual-stick directional navigation

### Goal

Make both physical sticks equivalent directional navigation sources without raw-state IPC or a repeat/gesture framework.

### Changes

- track the latest full `ControllerState` inside the existing Overlay input router;
- use the already normalized signed-short axes;
- define one small named directional threshold/deadzone policy;
- emit one direction when a stick crosses from neutral into that direction;
- require return to the neutral/deadzone region before that stick direction can emit again;
- map both left and right stick to the existing NavigateUp/Down/Left/Right semantics;
- include consumed stick neutrality in release-to-resume;
- do not add hold repeat yet.

### Hardware note

The threshold is implementation-local and should be hardware-validated on the supported Claw. Do not expose a user deadzone setting merely for Overlay navigation at this stage.

---

## OQ5-UI-04 — Logical row selection and scrolling

### Goal

Establish the one controller-first focus model that all later rows use.

### Changes

- one selected interactive row per active page;
- Up/Down move between enabled/selectable rows;
- selection is visually obvious without requiring HWND activation;
- selected row is brought into the ScrollViewer viewport;
- Left/Right dispatch to the selected row only when that row supports adjustment;
- A dispatches activation to the selected row only when supported;
- tab switch chooses the page's first selectable row; retaining a per-page selection within the same visible session is acceptable, but every Overlay Show still resets the top-level tab to the first configured tab;
- B remains handled above the page as global dismissal.

### Do not build

- keyboard focus framework replacement;
- navigation graph engine;
- arbitrary nested focus scopes;
- page back stack.

---

## OQ5-UI-05 — Toggle row primitive

### Goal

Add one reusable-but-narrow row for boolean Quick Settings.

### Contract

```text
row selected
A → request toggle
```

- use standard WinUI 3 ToggleSwitch sizing initially;
- selected-row highlight belongs to the row, not the switch's keyboard focus state;
- unavailable/disabled state cannot mutate;
- pointer/touch remains usable;
- mutation result later comes from Runtime authority.

No feature-specific hardware code lands here.

---

## OQ5-UI-06 — Slider row primitive

### Goal

Implement the agreed controller slider UX before binding real device features.

### Contract

```text
slider row selected
Left  → one step lower
Right → one step higher
A     → no edit-mode transition required
```

- standard WinUI 3 Slider sizing initially;
- visible label + current preview value;
- clamp at min/max;
- explicit step;
- pointer/touch slider remains usable;
- no feature-specific mutation yet.

---

## OQ5-UI-07 — Shared delayed slider commit behavior

### Goal

Reuse the proven QAM mutation pacing without copying the entire QAM frontend architecture.

### Required behavior

- immediate preview on each step;
- one pending mutation per logical setting key;
- subsequent changes replace the pending same-key value;
- commit after the same relaxed delay currently used by QAM;
- authoritative Runtime result/readback replaces preview after commit;
- mutation failure restores/reloads authoritative state and exposes a local failure state;
- disposing/hiding the page must not allow obsolete delayed callbacks to mutate after ownership is gone.

Keep this as one narrow helper owned by the Overlay feature client layer, not a general scheduler framework.

---

## OQ5-UI-08 — Runtime-owned Overlay tab-order setting

### Goal

Persist only the agreed tab order through the existing settings authority.

### Data model

Five known identities only:

```text
Device
Profile
Controller
Shortcut
Setting
```

Default order is exactly the list above.

Persist one ordered list. Do not persist:

- a second default-tab field;
- last visible tab;
- selected row;
- scroll offsets.

### Validation

Loaded order must resolve to all five known tabs exactly once.

Missing/corrupt/unknown/duplicate data must deterministically resolve to a valid complete order and must never prevent Runtime startup.

Use canonical `SettingsStore/settings.json`; do not create `overlay-settings.json`.

---

## OQ5-UI-09 — Overlay preference transport

### Goal

Keep persistence in Runtime while allowing the independent Overlay process to render and mutate the order.

### Contract

- Runtime sends a current tab-order snapshot through `.Overlay`;
- Overlay sends a narrow `SetTabOrder`-type mutation;
- Runtime validates and persists;
- Runtime returns/republishes authoritative order;
- protocol version changes explicitly;
- no generic key/value preference API;
- no whole `AppSettings` object is exposed to Overlay.

The transport remains current-user-only and single Overlay client.

---

## OQ5-UI-10 — Setting-page tab order editor

### Goal

Expose the tab-order preference without introducing general item reordering.

### Planned controller interaction

```text
Up / Down   choose one of the five tab-order rows
Left        move selected tab one position earlier
Right       move selected tab one position later
```

A separate reorder mode is not required.

The top tab strip updates from the authoritative returned order.

The first item in the resulting order becomes the page selected on the **next** Overlay Show.

Do not add drag/drop unless a later pointer UX request justifies it.

---

## OQ5-UI-11 — Shortcut 2×2 slot shell

### Goal

Create the agreed Game-Bar-like visual foundation without inventing shortcut actions.

### Layout

```text
[ Slot 1 ] [ Slot 2 ]
[ Slot 3 ] [ Slot 4 ]
```

- four fixed slot identities;
- directional selection by DPad/both sticks;
- clear selected tile state;
- A activates only when a slot has a valid assigned action;
- unassigned slot is visibly inert/unassigned;
- no dynamic slot count;
- no generalized card collection.

---

## OQ5-UI-12 — Shortcut slot preference model

### Goal

Provide only the preference shape needed by the four fixed slots.

Persist assignment/order by fixed slots through Runtime authority.

Do not implement a general reorder system shared with Device/Profile/Controller rows.

Actual action definitions such as Game Bar, keyboard, desktop, task manager, app launch, or other shortcuts require a later product-specific work order.

---

# 6. First Device feature population

The following PRs use the UI primitives above and existing Runtime feature authorities. They must not create frontend-owned hardware logic.

## OQ5-FEAT-01 — Device snapshot bridge

Aggregate only values needed by the Device page from existing Runtime authorities and send a fresh snapshot on Show/refresh. Use targeted invalidation/refresh, not telemetry polling.

## OQ5-FEAT-02 — TDP

- availability/writable state from Runtime;
- enable toggle if the current Runtime contract exposes it;
- AC/DC and PL1/PL2 rows according to the existing canonical TDP model;
- slider previews use OQ5-UI-07 behavior;
- Runtime result/readback wins.

## OQ5-FEAT-03 — CPU Boost

- enable state;
- supported mode/value rows from the current Runtime contract;
- no duplicated Windows power-policy writer.

## OQ5-FEAT-04 — Power Mode

- use existing Runtime power-mode authority;
- expose only supported current choices;
- disabled/unavailable state is explicit.

## OQ5-FEAT-05 — Intel FPS Limit

- expose only where Runtime reports supported/writable;
- use the common slider/discrete adjustment and delayed commit behavior;
- do not duplicate GPU registry/feature authority in Overlay.

---

# 7. Validation strategy across the sequence

Each PR should validate its own narrow contract plus the pre-existing Overlay lifecycle it touches.

### Always preserve

- Release build clean;
- relevant tests clean;
- `git diff --check` clean;
- warm Overlay process still starts/connects;
- Show/Hide acknowledgement remains correct;
- outside-click dismissal remains correct;
- B close remains Runtime-owned;
- OQ4 neutral capture remains active while the surface is visible;
- no input leaks on newly consumed controls;
- no focus activation of the game/Overlay window;
- no PID/DI/HidHide/VIIPER mutation caused by UI work.

### Hardware checkpoints

Do not defer every hardware observation until the final page is populated.

Recommended checkpoints:

1. after OQ5-UI-01: five-tab shell placement, text fit, 150% DPI, animation, scrolling shell;
2. after OQ5-UI-03: DPad + LB/RB + both-stick navigation and release leakage;
3. after OQ5-UI-06/07: slider feel and delayed mutation pacing;
4. after OQ5-UI-10: tab reorder/start-tab behavior;
5. after OQ5-UI-11: 2×2 Shortcut directional navigation;
6. after each Device feature binding: actual hardware mutation/readback.

---

# 8. Explicitly deferred

The following are intentionally outside this plan until the product contract is frozen or hardware evidence requires them:

- custom smaller WinUI ToggleSwitch/Slider templates;
- custom font scale system;
- analog hold-repeat/acceleration;
- LT/RT actions;
- X/Y actions;
- arbitrary row reorder in Device/Profile/Controller/Setting;
- dynamic Shortcut slot counts;
- concrete Shortcut action catalog/execution;
- Profile page final feature list;
- Controller page final feature list;
- generalized navigation/event/state framework;
- Steam QAM forced visibility exclusion;
- physical WING/OEM1 binding policy (OQ6 remains separate).

---

# 9. Immediate next PR

Start with:

`OQ5-UI-01 — Five-tab Overlay shell`

The corresponding implementation work order is:

`docs/overalyui/OQ5_UI_01_FIVE_TAB_OVERLAY_SHELL_WORK_ORDER.md`

This first PR deliberately changes only the visible shell/show-reset seam. It does not touch Runtime controller routing, semantic wire actions, feature settings, or persistence.